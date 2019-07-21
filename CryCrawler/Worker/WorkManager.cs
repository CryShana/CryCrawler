using CryCrawler.Network;
using CryCrawler.Structures;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static CryCrawler.CacheDatabase;
using Timer = System.Timers.Timer;

namespace CryCrawler.Worker
{
    /// <summary>
    /// Manages work (URLs to crawl) either locally, via dashboard or via connected Host
    /// </summary>
    public class WorkManager
    {
        int wlimit = 0;
        string assignedw = null;
        bool resultsReady = false;
        bool sendingResults = false;

        bool isFIFO = false;
        readonly Timer resultTimer;
        readonly Timer statusTimer;
        readonly CacheDatabase database;
        readonly WorkerConfiguration config;

        public readonly int MemoryLimitCount = 500000;

        #region Public Properties
        public NetworkWorkManager NetworkManager { get; private set; }
        public ConcurrentQueueOrStack<Work, string> Backlog { get; }
        public long WorkCount => Backlog.Count + CachedWorkCount;
        public long CachedWorkCount { get; private set; } = 0;
        public long CachedCrawledWorkCount { get; private set; } = 0;
        public bool IsWorkAvailable => Backlog.Count > 0 || CachedWorkCount > 0;

        public bool HostMode { get; }
        public bool ConnectedToHost { get; private set; }
        public event NetworkWorkManager.MessageReceivedHandler HostMessageReceived;
        #endregion

        public WorkManager(WorkerConfiguration config, CacheDatabase database, int newMemoryLimitCount) : this(config, database) => MemoryLimitCount = newMemoryLimitCount;

        public WorkManager(WorkerConfiguration config, CacheDatabase database)
        {
            this.config = config;
            this.database = database;

            // depth-search = Stack (LIFO), breadth-search = Queue (FIFO)
            isFIFO = !config.DepthSearch;
            Backlog = new ConcurrentQueueOrStack<Work, string>(!isFIFO, t => t.Key);

            if (config.HostEndpoint.UseHost)
            {
                // HOST MODE
                HostMode = true;
                ConnectedToHost = false;

                statusTimer = new Timer(TimeSpan.FromSeconds(1).TotalMilliseconds);
                statusTimer.Elapsed += StatusSend;
                statusTimer.Start();

                resultTimer = new Timer(TimeSpan.FromSeconds(1).TotalMilliseconds);
                resultTimer.Elapsed += ResultsCheck;
                resultTimer.Start();

                // use host
                Logger.Log($"Using Host as Url source ({config.HostEndpoint.Hostname}:{config.HostEndpoint.Port})");

                // delete existing cache and create new one
                database.EnsureNew();

                NetworkManager = new NetworkWorkManager(
                    config.HostEndpoint.Hostname,
                    config.HostEndpoint.Port,
                    config.HostEndpoint.Password,
                    config.HostEndpoint.ClientId);

                NetworkManager.MessageReceived += NetworkManager_MessageReceived;
                NetworkManager.Disconnected += id => ConnectedToHost = false;
                NetworkManager.Connected += id =>
                {
                    config.HostEndpoint.ClientId = id;
                    ConfigManager.SaveConfiguration(ConfigManager.LastLoaded);

                    ConnectedToHost = true;
                };

                NetworkManager.Start();
            }
            else
            {
                // LOCAL MODE
                HostMode = false;
                ConnectedToHost = false;

                // use local Urls and Dashboard provided URLs
                Logger.Log($"Using local Url source");

                // load dumped 
                if (database.GetWorks(out List<Work> dumped, -1, false, false, Collection.DumpedBacklog) &&
                    dumped != null && dumped.Count > 0)
                {
                    // add to backlog
                    Backlog.AddItems(dumped);
                    database.DropCollection(Collection.DumpedBacklog);

                    Logger.Log($"Loaded {dumped.Count} backlog items from cache.");
                }

                // load all local Urls (only if not yet crawled and dumped backlog items were loaded to continue work)
                ReloadUrlSource();

                // load cache stats
                CachedWorkCount = database.GetWorkCount(Collection.CachedBacklog);
                CachedCrawledWorkCount = database.GetWorkCount(Collection.CachedCrawled);
            }
        }


        /// <summary>
        /// Checks the Url source for changes and adds new items to backlog
        /// </summary>
        public void ReloadUrlSource()
        {
            // process Urls 
            foreach (var url in config.Urls)
            {
                if (database.GetWork(out Work w, url, Collection.CachedCrawled) == false) AddToBacklog(url);
                else
                {
                    if (Backlog.Count == 0)
                    {
                        // no backlog loaded, so load this anyway
                        AddToBacklog(url);
                    }
                    else Logger.Log($"Skipping specified URL '{url}' - crawled at {w.AddedTime.ToString("dd.MM.yyyy HH:mm:ss")}", Logger.LogSeverity.Debug);
                }
            }
        }

        /// <summary>
        /// Clears download cache and starts downloading from seed Urls again
        /// </summary>
        public void ClearCache()
        {
            database.EnsureNew();

            Backlog.Clear();

            CachedWorkCount = 0;
            CachedCrawledWorkCount = 0;

            ReloadUrlSource();
        }
        public void AddToCrawled(Work w)
        {
            addingSemaphore.Wait();
            try
            {
                if (database.Disposing) return;

                if (database.Upsert(w, out bool wasIns, Collection.CachedCrawled))
                {
                    if (wasIns) CachedCrawledWorkCount++;
                }
                // TODO: better handle this
                else if (!database.Disposing) throw new DatabaseErrorException("Failed to upsert crawled work to database!");
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                addingSemaphore.Release();
            }
        }

        SemaphoreSlim addingSemaphore = new SemaphoreSlim(1);
        public void AddToBacklog(string url)
        {
            var w = new Work(url);
            AddToBacklog(w);
        }
        public void AddToBacklog(IEnumerable<string> urls)
        {
            var works = new List<Work>();
            foreach (var i in urls) works.Add(new Work(i));

            AddToBacklog(works);
        }
        public void AddToBacklog(List<Work> works)
        {
            addingSemaphore.Wait();
            try
            {
                if (database.Disposing) return;

                // if cache not empty, load it to memory first
                if (CachedWorkCount > 0) LoadCacheToMemory();

                var forBacklogCount = MemoryLimitCount - Backlog.Count;
                var dontCache = works.Count <= forBacklogCount;
                if (forBacklogCount > 0)
                {
                    var forBacklog = works.GetRange(0, dontCache ? works.Count : forBacklogCount);

                    // add to backlog
                    Backlog.AddItems(forBacklog);
                }

                // cache the rest
                if (!dontCache)
                {
                    var forCacheCount = works.Count - forBacklogCount;
                    var forCache = works.GetRange(forBacklogCount, forCacheCount);

                    if (database.InsertBulk(forCache, forCacheCount, out int inserted)) CachedWorkCount += inserted;
                    else throw new DatabaseErrorException("Failed to bulk insert!");
                }
            }
            finally
            {
                addingSemaphore.Release();
            }
        }
        public void AddToBacklog(Work w)
        {
            addingSemaphore.Wait();
            try
            {
                if (database.Disposing) return;

                // if above memory limit, save to database
                if (Backlog.Count >= MemoryLimitCount)
                {
                    // save to database     
                    if (database.Insert(w)) CachedWorkCount++;
                    else throw new DatabaseErrorException("Failed to insert!");
                }
                // if below memory limit but cache is not empty, load cache to memory
                else if (CachedWorkCount > 0)
                {
                    // save to database
                    if (database.Insert(w)) CachedWorkCount++;
                    else throw new DatabaseErrorException("Failed to insert!");

                    // load cache to memory
                    LoadCacheToMemory();
                }
                else
                {
                    // normally add to memory
                    Backlog.AddItem(w);
                }
            }
            finally
            {
                addingSemaphore.Release();
            }
        }
        public bool IsUrlCrawled(string url)
        {
            return database.GetWork(out _, url, Collection.CachedCrawled);
        }

        /// <summary>
        /// Attempts to get work from backlog and removes it from work list.
        /// </summary>
        public bool GetWork(out Work w, bool checkForCrawled = true)
        {
            w = null;
            string url = null;

            addingSemaphore.Wait();
            try
            {
                if (database.Disposing)
                {
                    url = null;
                    return false;
                }

                // if using Host mode - if backlog count passes defined limit, results are ready to be sent
                if (HostMode && Backlog.Count >= wlimit)
                {
                    resultsReady = true;

                    // don't give crawler any more work until work is sent to the host and backlog is cleared
                    return false;
                }

                #region Get Work
                if (isFIFO)
                {
                    // take from memory if available
                    if (Backlog.Count > 0) Backlog.TryGetItem(out w);

                    // take from database if available and memory is empty
                    if (w == null && CachedWorkCount > 0 && database.GetWorks(out List<Work> works, 1, true, true))
                    {
                        w = works.FirstOrDefault();
                        CachedWorkCount--;
                    }
                }
                else
                {
                    // take from database if available
                    if (CachedWorkCount > 0 && database.GetWorks(out List<Work> works, 1, false, true))
                    {
                        w = works.FirstOrDefault();
                        CachedWorkCount--;
                    }

                    // take from memory if available and database is empty
                    if (w == null && Backlog.Count > 0) Backlog.TryGetItem(out w);
                }
                #endregion

                // if there is space to load cache to memory, do it
                if (Backlog.Count < MemoryLimitCount && CachedWorkCount > 0) LoadCacheToMemory();
            }
            finally
            {
                addingSemaphore.Release();
            }

            url = w?.Url;

            // if CheckForCrawled is true, return false if work is already crawled
            if (checkForCrawled && url != null && IsUrlCrawled(url)) return false;

            return w != null;
        }

        /// <summary>
        /// Adds work item to crawled items, except if recrawl date is defined and not yet reached. In that case it adds it to Backlog again.
        /// </summary>
        /// <param name="w"></param>
        public void ReportWorkResult(Work w)
        {
            if (w.RecrawlDate != null)
            {
                // check if recrawl date is valid
                if (w.RecrawlDate.Value.Subtract(DateTime.Now).TotalDays < 14)
                {
                    // add back to backlog with a recrawl date
                    AddToBacklog(w);
                }
                else
                {
                    // add to crawled as a failed crawl
                    AddToCrawled(w);
                }
            }
            else
            {
                AddToCrawled(w);
            }
        }

        /// <summary>
        /// Check if URL is eligible for crawling. Usually if it was already crawled or not or is present in the backlog already.
        /// </summary>
        /// <param name="url">URL to check for</param>
        /// <returns>True if URL is eligible</returns>
        public bool IsUrlEligibleForCrawl(string url)
        {
            // TODO: consider recrawling

            // check if url already in backlog
            var backlogContains = false;
            if (Backlog.ContainsKey(Work.GetKeyFromUrl(url)))
            {
                backlogContains = true;

                // if URL is above max index length, the key was cut off, find actual work to confirm for sure
                if (url.Length >= MaxIndexLength - 1)
                {
                    // find work with identical url to really confirm it
                    var work = Backlog.FindItem(x => x.Url == url);
                    backlogContains = work != null;
                }
            }

            // skip further checking if backlog already contains it
            if (backlogContains) return false;

            // check if url already crawled
            if (database.GetWork(out _, url, Collection.CachedCrawled)) return false;

            return true;
        }

        /// <summary>
        /// Signals work manager that all services using it are finished processing work
        /// </summary>
        public void WorkDone()
        {
            if (WorkCount == 0 && CachedCrawledWorkCount > 0)
            {
                resultsReady = true;
            }
        }

        public void Dispose()
        {
            // dump all work if working locally - if working via Host, delete cache
            if (!config.HostEndpoint.UseHost)
            {
                DumpMemoryToCache();
                database.Dispose();
            }
            else
            {
                // delete cache
                database.Dispose();
                database.Delete();
            }
        }

        void LoadCacheToMemory()
        {
            long howMuch = MemoryLimitCount - (long)Backlog.Count;
            howMuch = howMuch > CachedWorkCount ? CachedWorkCount : howMuch;

            if (database.GetWorks(out List<Work> works, (int)howMuch, isFIFO, true))
            {
                Backlog.AddItems(works);
                CachedWorkCount -= works.Count;
            }
        }
        void DumpMemoryToCache()
        {
            database.InsertBulk(Backlog.ToList(), Backlog.Count, out int inserted, Collection.DumpedBacklog);
            Logger.Log($"Dumped {inserted} backlog items to cache");
        }

        void NetworkManager_MessageReceived(NetworkMessage w, NetworkMessageHandler<NetworkMessage> msgHandler)
        {
            // handle message inside work manager
            switch (w.MessageType)
            {
                case NetworkMessageType.Work:
                    WorkReceived((string)w.Data);
                    break;
                case NetworkMessageType.ConfigUpdate:
                    // this needs to be handled outside this class
                    break;
                case NetworkMessageType.WorkLimitUpdate:
                    wlimit = w.Data.AsInteger();
                    break;
                case NetworkMessageType.Disconnect:
                    break;
                case NetworkMessageType.StatusCheck:
                    // ignore it (status timer will send feedback handle it)
                    break;
                case NetworkMessageType.SendResults:
                    if (sendingResults == false)
                    {
                        sendingResults = true;

                        SendResults();
                    }
                    break;
                case NetworkMessageType.ResultsReceived:
                    PrepareForNewWork();

                    sendingResults = false;
                    resultsReady = false;
                    break;
            }

            // pass it on
            HostMessageReceived?.Invoke(w, msgHandler);

        }

        void SendResults()
        {
            if (ConnectedToHost == false) return;

            var itemsToSend = Backlog.ToList().Select(x => x.Url).ToList();

            // check cached items?

            Logger.Log($"Sending {itemsToSend.Count} results to host...");

            NetworkManager.MessageHandler.SendMessage(
                new NetworkMessage(NetworkMessageType.Work, itemsToSend));
        }

        void StatusSend(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (ConnectedToHost == false) return;

            // TODO: refactor this - optimize the whole process
            var msg = JsonConvert.SerializeObject(new
            {
                WorkCount = WorkCount,
                CrawledCount = CachedCrawledWorkCount
            });

            NetworkManager.MessageHandler.SendMessage(new NetworkMessage(NetworkMessageType.StatusCheck, msg));
        }

        void ResultsCheck(object sender, System.Timers.ElapsedEventArgs e)
        {
            // if no work assigned, ignore it
            if (assignedw == null) return;

            // if we are connected and are not yet sending results but results are ready 
            // keep sending ResultsReady request on every timer tick until server is ready to receieve them
            if (ConnectedToHost && !sendingResults && resultsReady)
            {
                // send request to Host that work results are ready
                NetworkManager.MessageHandler.SendMessage(new NetworkMessage(NetworkMessageType.ResultsReady));
            }
        }

        void WorkReceived(string work)
        {
            Logger.Log("New work assigned - " + work);

            PrepareForNewWork();

            // add to backlog
            AddToBacklog(work);

            assignedw = work;
            resultsReady = false;
        }

        /// <summary>
        /// Clears backlog and cache and prepares worker to be assigned new work from Host.
        /// </summary>
        void PrepareForNewWork()
        {
            assignedw = null;

            // clear backlog
            Backlog.Clear();

            // also clear cached work items
            if (CachedWorkCount > 0) database.DropCollection(Collection.CachedBacklog);
        }
    }
}
