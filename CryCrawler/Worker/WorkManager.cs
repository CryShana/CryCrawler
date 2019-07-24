using CryCrawler.Network;
using CryCrawler.Structures;
using LiteDB;
using MessagePack.Formatters;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        // HOST RELATED VARIABLES
        int wlimit = 0;
        string assignedw = null;
        bool resultsReady = false;
        bool sendingResults = false;
        int? hostMaxFileChunkSize = null;

        // file transfer variables
        Work transferWork = null;
        bool transferringFile = false;
        string transferringFilePath = null;
        long transferringFileSize = 0;
        long transferringFileSizeCompleted = 0;
        FileStream transferringFileStream = null;

        // MAIN VARIABLES
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
        public void AddToCrawled(string url) => AddToCrawled(new Work(url));


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
        public bool IsUrlCrawled(string url) => database.GetWork(out _, url, Collection.CachedCrawled);    

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
            resultsReady = true;
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
                    var cfg = ((IDictionary<object, object>)w.Data).Deserialize<WorkerConfiguration>();
                    hostMaxFileChunkSize = cfg.MaxFileChunkSizekB;

                    // the rest needs to be handled outside this class
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
                    PrepareForNewWork(true);

                    sendingResults = false;
                    resultsReady = false;
                    break;
                case NetworkMessageType.FileCheck:
                    // host checks if file is available for transfer

                    if (FileAvailableForTransfer(out Work availableWork, out string path))
                    {
                        if (transferringFile)
                        {
                            Logger.Log("Cancelling existing file transfer. Starting new one...");

                            // if new file is requested, stop existing file transfer
                            StopFileTransfer();
                        }

                        // prepare file for transfer
                        transferWork = availableWork;
                        transferringFileSize = new FileInfo(path).Length;
                        transferringFilePath = path;
                        transferringFileSizeCompleted = 0;
                        transferringFileStream = null;

                        // send file transfer request for this file
                        msgHandler.SendMessage(new NetworkMessage(NetworkMessageType.FileTransfer,
                            new FileTransferInfo
                            {
                                Url = transferWork.Url,
                                Size = transferringFileSize,
                                Location = availableWork.DownloadLocation
                            }));
                    }
                    break;
                case NetworkMessageType.FileReject:
                    // host rejected file transfer
                    StopFileTransfer();
                    break;
                case NetworkMessageType.FileAccept:
                    // host accepted file transfer

                    // check if already transferring
                    if (transferringFile)
                    {
                        throw new InvalidOperationException("File is already being transferred!");
                    }

                    // start transferring
                    transferringFile = true;
                    transferringFileStream = new FileStream(transferringFilePath, System.IO.FileMode.Open, FileAccess.ReadWrite);

                    // send chunk
                    SendNextFileChunk(msgHandler);
                    break;
                case NetworkMessageType.FileChunkAccept:
                    // host accepted file chunk, send the next one if not finished

                    if (transferringFile && SendNextFileChunk(msgHandler))
                    {
                        Logger.Log($"File transferred ({Path.GetFileName(transferringFilePath)}).");

                        // file transferred, mark as completed
                        var fpath = transferringFilePath;

                        transferWork.Transferred = true;
                       
                        // database.Upsert(transferWork, out bool ins, Collection.CachedCrawled);
                        database.DeleteWorks(out int dcount, Query.EQ("Url", new BsonValue(transferWork.Url)));
                        CachedCrawledWorkCount -= dcount;

                        StopFileTransfer();

                        // delete file
                        File.Delete(fpath);
                    }
                    break;
            }

            // pass it on
            HostMessageReceived?.Invoke(w, msgHandler);
        }

        void SendResults()
        {
            if (ConnectedToHost == false) return;

            // send crawled data (only transferred works and already downloaded works)
            database.FindWorks(out IEnumerable<Work> crawledWorks, Query.Or(
                Query.EQ("IsDownloaded", new BsonValue(false)),
                Query.EQ("Transferred", new BsonValue(true))));

            var crawled = crawledWorks.Select(x => x.Url).ToList();
            Logger.Log($"Sending {crawled.Count} crawled urls to host...");
            NetworkManager.MessageHandler.SendMessage(
                new NetworkMessage(NetworkMessageType.CrawledWorks, crawled));

            // send work results

            var itemsToSend = Backlog.ToList().Select(x => x.Url).ToList(); // cached items??

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
        void PrepareForNewWork(bool cleanCrawled = false)
        {
            assignedw = null;

            // clear backlog
            Backlog.Clear();

            // also clear cached work items
            if (CachedWorkCount > 0) database.DropCollection(Collection.CachedBacklog);

            if (cleanCrawled)
            {
                // clean crawled items
                CleanCrawledFiles();
            }
        }

        #region File Transferring
        /// <summary>
        /// Stops transferring current file
        /// </summary>
        void StopFileTransfer()
        {
            try
            {
                transferWork = null;
                transferringFile = false;
                transferringFilePath = null;
                transferringFileSize = 0;
                transferringFileSizeCompleted = 0;

                transferringFileStream?.Close();
                transferringFileStream?.Dispose();
            }
            catch
            {

            }
        }

        /// <summary>
        /// Checks if any file is ready to be transfered
        /// </summary>
        /// <param name="work"></param>
        /// <returns></returns>
        bool FileAvailableForTransfer(out Work work, out string path)
        {
            path = null;

            // always check if file exists, if not - remove it
            while (true)
            {
                // get work that has untransferred files
                if (database.FindWork(out work, Query.And(
                    Query.EQ("Transferred", new BsonValue(false)),
                    Query.EQ("IsDownloaded", new BsonValue(true)))) == false) return false;

                path = Path.Combine(config.DownloadsPath, work.DownloadLocation);
                if (File.Exists(path) == false)
                {
                    Logger.Log("Work has missing file. Updating work...", Logger.LogSeverity.Debug);

                    // update this item
                    work.IsDownloaded = false;
                    if (database.Upsert(work, out _, Collection.CachedCrawled) == false)
                        Logger.Log("Updating work failed.", Logger.LogSeverity.Warning);

                    continue;
                }

                return true;
            }
        }

        /// <summary>
        /// Delete crawled works that already got files transferred or don't have any files downloaded.
        /// </summary>
        void CleanCrawledFiles()
        {
            // delete works that don't have files or have already transferred files
            if (database.DeleteWorks(out int deleted, Query.Or(
                Query.EQ("IsDownloaded", new BsonValue(false)),
                Query.EQ("Transferred", new BsonValue(true)))))
            {
                CachedCrawledWorkCount -= deleted;
                Logger.Log($"Deleted {deleted} crawled works. (Already transferred files or not downloaded)", Logger.LogSeverity.Debug);
            }
        }

        /// <summary>
        /// Send next file chunk, returns true if chunk was last.
        /// </summary>
        bool SendNextFileChunk(NetworkMessageHandler<NetworkMessage> msghandler)
        {
            // completed check
            if (transferringFileSizeCompleted >= transferringFileSize) return true;

            // determine chunk size
            long toTransfer = transferringFileSize - transferringFileSizeCompleted;
            int chunkSizeLimit = (hostMaxFileChunkSize ?? config.MaxFileChunkSizekB) * 1024;
            int chunkSize = toTransfer > chunkSizeLimit ? chunkSizeLimit : (int)toTransfer;

            // read data from stream
            byte[] chunkData = new byte[chunkSize];
            transferringFileStream.Read(chunkData, 0, chunkSize);

            var chunk = new FileChunk
            {
                Size = chunkSize,
                Location = transferWork.DownloadLocation,
                Data = chunkData
            };

            // send chunk
            msghandler.SendMessage(new NetworkMessage(NetworkMessageType.FileChunk, chunk));
            transferringFileSizeCompleted += chunkSize;

            // completed check
            if (transferringFileSizeCompleted >= transferringFileSize) return true;

            return false;
        }
        #endregion
    }
}
