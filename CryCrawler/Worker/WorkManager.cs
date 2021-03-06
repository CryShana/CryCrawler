using LiteDB;
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Threading;
using CryCrawler.Network;
using CryCrawler.Structures;
using System.Threading.Tasks;
using System.Collections.Generic;
using Timer = System.Timers.Timer;
using System.Collections.Concurrent;
using static CryCrawler.CacheDatabase;

namespace CryCrawler.Worker
{
    /// <summary>
    /// Manages work (URLs to crawl) either locally, via dashboard or via connected Host
    /// </summary>
    public class WorkManager
    {
        // HOST RELATED VARIABLES
        int wlimit = 0;
        string assignedurl = null;
        bool resultsReady = false;
        bool sendingResults = false;
        int? hostMaxFileChunkSize = null;
        CancellationTokenSource workCancelSource;

        // file transfer variables       
        Work transferWork = null;
        bool transferringFile = false;
        string transferringFilePath = null;
        long transferringFileSize = 0;
        long transferringFileSizeCompleted = 0;
        DateTime transferReqSent = DateTime.Now;
        FileStream transferringFileStream = null;

        // MAIN VARIABLES
        bool isFIFO = false;
        readonly Thread dumpThread;
        readonly Timer resultTimer;
        readonly Timer statusTimer;
        readonly PluginManager plugins;
        readonly CacheDatabase database;
        readonly WorkerConfiguration config;
        readonly Func<bool> areWorkersActive;
        readonly SemaphoreSlim addingSemaphore = new SemaphoreSlim(1);
        readonly SemaphoreSlim dumpingSemaphore = new SemaphoreSlim(1);
        readonly ConcurrentDictionary<string, DomainFailInfo> domainFails = new ConcurrentDictionary<string, DomainFailInfo>();

        public readonly int MemoryLimitCount = 500000;

        #region Public Properties
        public NetworkWorkManager NetworkManager { get; private set; }
        public ConcurrentQueueOrStack<Work, string> Backlog { get; }
        public long WorkCount => Backlog.Count + CachedWorkCount;
        public long CachedWorkCount { get; private set; } = 0;
        public long CachedCrawledWorkCount { get; private set; } = 0;
        public bool IsWorkAvailable => Backlog.Count > 0 || CachedWorkCount > 0;
        public long ConsecutiveInvalidWorks { get; private set; } = 0;

        public bool HostMode { get; }
        public bool ConnectedToHost { get; private set; }
        public event NetworkWorkManager.MessageReceivedHandler HostMessageReceived;
        #endregion

        public WorkManager(WorkerConfiguration config, CacheDatabase database, int newMemoryLimitCount,
            PluginManager plugins = null, Func<bool> areWorkersActive = null)
            : this(config, database, plugins, areWorkersActive) => MemoryLimitCount = newMemoryLimitCount;

        public WorkManager(WorkerConfiguration config, CacheDatabase database, PluginManager plugins = null,
            Func<bool> areWorkersActive = null)
        {
            this.config = config;
            this.plugins = plugins;
            this.database = database;
            this.areWorkersActive = areWorkersActive;

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
                NetworkManager.Disconnected += id =>
                {
                    // reset whatever we were doing
                    ResetWorkStatus();

                    // set status
                    ConnectedToHost = false;

                    plugins?.Invoke(p => p.OnDisconnect());
                };
                NetworkManager.Connected += id =>
                {
                    config.HostEndpoint.ClientId = id;
                    ConfigManager.SaveConfiguration(ConfigManager.LastLoaded);

                    // reset whatever we were doing
                    ResetWorkStatus();

                    // set status
                    ConnectedToHost = true;

                    plugins?.Invoke(p => p.OnConnect(id));
                };

                NetworkManager.Start();
            }
            else
            {
                // LOCAL MODE
                HostMode = false;
                ConnectedToHost = false;

                // autosaving/dumping only on localsources - in hostmode, host manages everything
                dumpThread = new Thread(TemporaryDump) { IsBackground = true };
                dumpThread.Start();

                // use local Urls and Dashboard provided URLs
                Logger.Log($"Using local Url source");

                #region Attempt to load backlog from 3 different dumps
                // load dumped 
                if (database.GetWorks(out List<Work> dumped, -1, false, false, Collection.DumpedBacklog) && dumped != null && dumped.Count > 0)
                {
                    // add to backlog
                    Backlog.AddItems(dumped);
                    database.DropCollection(Collection.DumpedBacklog);

                    Logger.Log($"Loaded {dumped.Count} backlog items from cache.");
                }
                else if (database.GetWorks(out dumped, -1, false, false, Collection.DumpedBacklogBackup) && dumped != null && dumped.Count > 0)
                {
                    // add to backlog
                    Backlog.AddItems(dumped);

                    // Don't drop backlog backup, it will be replaced by autodump anyway
                    // database.DropCollection(Collection.DumpedBacklogBackup);

                    Logger.Log($"Loaded {dumped.Count} backlog items from backup cache.");
                }
                else if (database.GetWorks(out dumped, -1, false, false, Collection.DumpedBacklogBackupTemp) && dumped != null && dumped.Count > 0)
                {
                    // add to backlog
                    Backlog.AddItems(dumped);

                    // do not drop it - instead transfer it
                    // database.DropCollection(Collection.DumpedBacklogBackupTemp);
                    database.TransferTemporaryDumpedFilesToBackup();

                    Logger.Log($"Loaded {dumped.Count} backlog items from temporary cache.");
                }
                #endregion

                // ONLY LOAD URLs IF DUMP HASN'T LOADED ANYTHING
                if (dumped.Count == 0)
                {
                    // load all local Urls (only if not yet crawled and dumped backlog items were loaded to continue work)
                    ReloadUrlSource();
                }
                else
                {
                    Logger.Log("Ignoring seed Urls because existing items were loaded from cache.", Logger.LogSeverity.Debug);
                }

                // load cache stats
                CachedWorkCount = database.GetWorkCount(Collection.CachedBacklog);
                CachedCrawledWorkCount = database.GetWorkCount(Collection.CachedCrawled);
            }
        }

        /// <summary>
        /// Resets anything that might be going on between Host and us
        /// </summary>
        void ResetWorkStatus()
        {
            if (HostMode == false) throw new InvalidOperationException("Not in host mode!");

            StopFileTransfer();
            workCancelSource?.Cancel();
            sendingResults = false;
            resultsReady = false;

            PrepareForNewWork();
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
        public void AddToCrawled(Work w, bool bypassSemaphore = false)
        {
            if (bypassSemaphore == false) addingSemaphore.Wait();
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
                if (bypassSemaphore == false) addingSemaphore.Release();
            }
        }
        public void AddToCrawled(string url) => AddToCrawled(new Work(url));

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
        public void AddToBacklog(List<Work> works, bool bypassSemaphore = false)
        {
            if (bypassSemaphore == false) addingSemaphore.Wait();
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
                if (bypassSemaphore == false) addingSemaphore.Release();
            }
        }
        public void AddToBacklog(Work w, bool bypassSemaphore = false)
        {
            if (bypassSemaphore == false) addingSemaphore.Wait();
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
                if (bypassSemaphore == false) addingSemaphore.Release();
            }
        }

        public bool IsUrlCrawled(string url) => database.IsUrlCrawled(url);
        public bool IsUrlInBacklog(string url)
        {
            // check if url in memory backlog
            var isInMemory = Backlog.Contains(x => x.Url == url);

            // if any cached backlog items, check cache too
            if (CachedWorkCount > 0) return isInMemory || database.GetWork(out _, url);
            else return isInMemory;
        }


        /// <summary>
        /// Attempts to get work from backlog and removes it from work list.
        /// </summary>
        /// <param name="crawlDelay">Crawl delay in seconds. Pass the configuration value here.</param>
        public bool GetWork(out Work w, bool checkForCrawled = true, double crawlDelay = 0)
        {
            w = null;
            string url = null;

            addingSemaphore.Wait();
            try
            {
                if (database.Disposing)
                {
                    Logger.Log("Database disposing... Can't get work.", Logger.LogSeverity.Debug);
                    crawlDelay = 0;

                    url = null;
                    return false;
                }

                // if using Host mode - if backlog count passes defined limit, results are ready to be sent
                if (HostMode && Backlog.Count >= wlimit)
                {
                    resultsReady = true;
                    crawlDelay = 0;

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

                url = w?.Url;

                if (url != null)
                {
                    // check if work was already crawled
                    // if CheckForCrawled is true, return false if work is already crawled - except in special case
                    if (checkForCrawled && IsUrlCrawled(url))
                    {
                        // SPECIAL CASE: if this URL is a seed URL and backlog is empty, delete from crawled and retry
                        if (config.Urls.Contains(url) && Backlog.Count <= config.Urls.Count && CachedCrawledWorkCount <= config.Urls.Count)
                        {
                            if (database.DeleteWork(url, out int del, Collection.CachedCrawled)) CachedCrawledWorkCount -= del;

                            Logger.Log($"Removed URL '{url}' from crawled list to crawl again.", Logger.LogSeverity.Debug);
                            return true;
                        }

                        Logger.Log($"Skipping URL '{url}' - crawled at {w.AddedTime.ToString("dd.MM.yyyy HH:mm:ss")}", Logger.LogSeverity.Debug);
                        crawlDelay = 0;
                        return false;
                    }

                    // VALIDATE WORK

                    #region Validate Work
                    // check recrawl date
                    if (w?.IsEligibleForCrawl() == false)
                    {
                        // re-add to backlog
                        IncrementConsecutiveFailCount();
                        AddToBacklog(w, true);
                        crawlDelay = 0;
                        return false;
                    }

                    // check domain recrawl dates
                    var domain = Extensions.GetDomainName(url, out _);
                    if (domainFails.TryGetValue(domain, out DomainFailInfo info))
                    {
                        if (info.Blocked)
                        {
                            // check if recrawl date has passed
                            if (info.RecrawlTime.Subtract(DateTime.Now).TotalMinutes <= 0)
                            {
                                // if yes, remove block
                                info.Blocked = false;
                                Logger.Log($"Domain is now no longer blocked - {domain}", Logger.LogSeverity.Debug);
                            }
                            else
                            {
                                // re-add to backlog
                                IncrementConsecutiveFailCount();
                                AddToBacklog(w, true);
                                crawlDelay = 0;
                                return false;
                            }
                        }
                    }

                    // reset consecutive invalid works counter on success
                    ConsecutiveInvalidWorks = 0;

                    // check plugins
                    if (plugins?.Invoke(p => p.OnWorkReceived(url), true) == false)
                    {
                        Logger.Log("Plugin rejected work - " + url, Logger.LogSeverity.Debug);
                        crawlDelay = 0;
                        url = null;

                        return false;
                    } 
                    #endregion
                }
            }
            finally
            {
                // check if global crawl delay was specified
                if (url != null && crawlDelay > 0)
                {
                    Task.Delay((int)TimeSpan.FromSeconds(crawlDelay).TotalMilliseconds).Wait();
                }

                addingSemaphore.Release();
            }

            return url != null;
        }

        /// <summary>
        /// Adds work item to crawled items, except if recrawl date is defined and not yet reached. 
        /// In that case it adds it to Backlog again. This method checks if results are ready to be sent to host when is hostmode.
        /// </summary>
        /// <param name="w"></param>
        public void ReportWorkResult(Work w)
        {
            if (w.RecrawlDate != null)
            {
                // check if recrawl date is valid
                if (w.RecrawlDate.Value.Subtract(DateTime.Now).TotalDays < MaxWorkRecrawlDays)
                {
                    // increment fail count for domain
                    IncrementDomainFailCount(w.Url);

                    // add back to backlog with a recrawl date
                    AddToBacklog(w);
                }
                else
                {
                    // add to crawled as a failed crawl
                    AddToCrawled(w);

                    CheckIfResultsReady();
                }
            }
            else
            {
                // ON SUCCESS, remove domain block if there is any
                var domain = Extensions.GetDomainName(w.Url, out _);
                if (w.Success && domainFails.TryRemove(domain, out _))
                {
                    Logger.Log($"Domain fail counter removed for {domain}", Logger.LogSeverity.Debug);
                }

                AddToCrawled(w);

                CheckIfResultsReady();
            }
        }

        // if work-specific recrawl date exceeds below limit, work is automatically classified as failed
        readonly uint MaxWorkRecrawlDays = 3;
        // if domain-specific recrawl date exceeds below limit, recrawl date will no longer keep doubling
        readonly uint MaxDomainRecrawlMinutes = (uint)TimeSpan.FromHours(12).TotalMinutes;

        /// <summary>
        /// Increments fail count number for domain name of given Url. 
        /// If fail count exceeds certain threshold, block domain for double the minutes of last block (Starting with 5min)
        /// </summary>
        void IncrementDomainFailCount(string url)
        {
            var domain = Extensions.GetDomainName(url, out _);

            if (domainFails.ContainsKey(domain) && domainFails.TryGetValue(domain, out DomainFailInfo info))
            {
                info.FailCount++;

                // if fail count exceeds threshold, activate block mode
                if (info.FailCount > 5) // THRESHOLD
                {
                    info.FailCount = 0;
                    info.Blocked = true;

                    // if waiting time exceeds threshold, do not increase it further
                    if (info.WaitMinutes < MaxDomainRecrawlMinutes) info.WaitMinutes = info.WaitMinutes * 2;

                    info.RecrawlTime = DateTime.Now.AddMinutes(info.WaitMinutes);

                    Logger.Log($"Domain '{domain}' is now being ignored for {info.WaitMinutes} minutes.");
                }
            }
            else domainFails.TryAdd(domain, new DomainFailInfo
            {
                Domain = domain,
                FailCount = 1,
                WaitMinutes = 1,
                RecrawlTime = DateTime.Now
            });

            // TODO: send to worker?
        }

        /// <summary>
        /// Increment consencutive fail count number. If counter exceeds backlog count, client will send results to host.
        /// </summary>
        void IncrementConsecutiveFailCount()
        {
            ConsecutiveInvalidWorks++;

            if (ConsecutiveInvalidWorks > Backlog.Count)
            {
                if (HostMode)
                {
                    resultsReady = true;
                }
                else
                {
                    // maybe warn user in GUI                 
                }
            }
        }

        /// <summary>
        /// In hostmode, check if all workers using this work manager are inactive and mark results as ready to be sent.
        /// </summary>
        void CheckIfResultsReady()
        {
            if (HostMode == false) return;

            if (areWorkersActive == null) return;

            if (!areWorkersActive() && Backlog.Count == 0)
            {
                Logger.Log("Crawlers inactive. Results ready.", Logger.LogSeverity.Debug);
                resultsReady = true;
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

        bool disposing = false;
        public void Dispose()
        {
            disposing = true;
            statusTimer?.Stop();
            resultTimer?.Stop();
            workCancelSource?.Cancel();

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

        /// <summary>
        /// Load cached backlog items to memory
        /// </summary>
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

        /// <summary>
        /// Dump memory items to backlog cache
        /// </summary>
        void DumpMemoryToCache(bool useTemporaryCollection = false)
        {
            dumpingSemaphore.Wait();

            try
            {
                var blist = Backlog.ToList();

                // set all Ids to 0
                foreach (var w in blist) w.Id = 0;

                if (useTemporaryCollection)
                {
                    if (database.InsertBulk(blist, blist.Count, out int inserted, Collection.DumpedBacklogBackupTemp))
                    {
                        Logger.Log($"Dumped {inserted} backlog items to temporary cache", Logger.LogSeverity.Debug);

                        // transfer to backup
                        database.TransferTemporaryDumpedFilesToBackup();
                        Logger.Log($"Copied over to backup cache.", Logger.LogSeverity.Debug);
                    }
                }
                else
                {
                    database.InsertBulk(blist, blist.Count, out int inserted, Collection.DumpedBacklog);
                    Logger.Log($"Dumped {inserted} backlog items to cache");
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                dumpingSemaphore.Release();

                plugins?.Invoke(p => p.OnDump());
            }
        }

        /// <summary>
        /// Handles messages received from host
        /// </summary>
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
                    // ignore it (handled outside this function and by status timer)
                    break;

                case NetworkMessageType.SendResults:

                    #region Send Results
                    if (sendingResults == false)
                    {
                        sendingResults = true;

                        try
                        {
                            SendResults();
                        }
                        catch
                        {
                            sendingResults = false;
                        }
                    }
                    #endregion

                    break;

                case NetworkMessageType.ResultsReceived:

                    #region Prepare for new Work

                    // only clean crawled if above certain threshold
                    if (CachedCrawledWorkCount > config.MaxCrawledWorksBeforeCleanHost) PrepareForNewWork(true);
                    else PrepareForNewWork();

                    sendingResults = false;
                    resultsReady = false;
                    #endregion

                    break;

                case NetworkMessageType.FileCheck:
                    // host checks if file is available for transfer

                    #region Check File Transfer 
                    if (FileAvailableForTransfer(out Work availableWork, out string path))
                    {
                        if (transferringFile)
                        {
                            Logger.Log("Canceling existing file transfer. Starting new one...");

                            // if new file is requested, stop existing file transfer
                            StopFileTransfer();
                        }

                        // check if request has already been sent for this work and is not older than 6 seconds
                        if (transferWork != null && availableWork.Url == transferWork.Url &&
                            DateTime.Now.Subtract(transferReqSent).TotalSeconds < 6)
                        {
                            // ignore
                            // Logger.Log("File check ignored because of existing file transfer request.", Logger.LogSeverity.Debug);
                            break;
                        }

                        try
                        {
                            // prepare file for transfer
                            transferWork = availableWork;
                            transferringFileSize = new FileInfo(path).Length;
                            transferringFilePath = path;
                            transferringFileSizeCompleted = 0;
                            transferringFileStream = null;
                            transferReqSent = DateTime.Now;

                            // send file transfer request for this file
                            msgHandler.SendMessage(new NetworkMessage(NetworkMessageType.FileTransfer,
                                new FileTransferInfo
                                {
                                    Url = transferWork.Url,
                                    Size = transferringFileSize,
                                    Location = availableWork.DownloadLocation
                                }));
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("Failed to send file transfer request! " + ex.GetDetailedMessage(),
                                Logger.LogSeverity.Debug);

                            StopFileTransfer();
                        }
                    }

                    #endregion

                    break;

                case NetworkMessageType.FileReject:
                    // host rejected file transfer

                    StopFileTransfer();

                    break;

                case NetworkMessageType.FileAccept:
                    // host accepted file transfer

                    #region Start File Transfer
                    // check if already transferring
                    if (transferringFile)
                    {
                        throw new InvalidOperationException("File is already being transferred!");
                    }

                    // start transferring
                    transferringFile = true;
                    transferringFileStream = new FileStream(transferringFilePath,
                        System.IO.FileMode.Open, FileAccess.Read, FileShare.Read);

                    // send chunk
                    SendNextFileChunk(msgHandler);
                    #endregion

                    break;
                case NetworkMessageType.FileChunkAccept:
                    // host accepted file chunk, send the next one if not finished

                    #region Send next Chunk
                    if (transferringFile && SendNextFileChunk(msgHandler))
                    {
                        // file should now be transferred, let's wait for the confirmarmation (FileTransfer package)
                    }
                    #endregion

                    break;
                case NetworkMessageType.FileTransfer:

                    #region File transfer confirmation
                    var transferInfo = ((Dictionary<object, object>)w.Data)
                            .Deserialize<FileTransferInfo>();

                    if (transferringFile && transferInfo.Url == transferWork?.Url)
                    {
                        // file transfer completed
                        Logger.Log($"File transferred ({Path.GetFileName(transferringFilePath)}).", Logger.LogSeverity.Debug);

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
                    else
                    {
                        // invalid url
                        Logger.Log("Invalid file transfer confirmation!", Logger.LogSeverity.Debug);
                    }
                    #endregion

                    break;
            }

            // pass it on (to handle it outside this class)
            HostMessageReceived?.Invoke(w, msgHandler);
        }

        /// <summary>
        /// Send crawled and backlog items to host
        /// </summary>
        void SendResults()
        {
            if (ConnectedToHost == false) return;

            // send crawled data (only transferred works and already downloaded works)
            database.FindWorks(out IEnumerable<Work> crawledWorks, Query.Or(
                Query.EQ("IsDownloaded", new BsonValue(false)),
                Query.EQ("Transferred", new BsonValue(true))));

            var crawled = crawledWorks.Select(x => x.Url).ToList();
            Logger.Log($"Sending {crawled.Count} crawled urls to host...", Logger.LogSeverity.Debug);

            NetworkManager.MessageHandler.SendMessage(
                new NetworkMessage(NetworkMessageType.CrawledWorks, crawled));

            // send work results

            var itemsToSend = Backlog.ToList().Select(x => x.Url).ToList(); // cached items??

            Logger.Log($"Sending {itemsToSend.Count} results to host...", Logger.LogSeverity.Debug);

            NetworkManager.MessageHandler.SendMessage(
                new NetworkMessage(NetworkMessageType.Work, itemsToSend));
        }

        /// <summary>
        /// Send worker status to host
        /// </summary>
        void StatusSend(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (ConnectedToHost == false) return;

            // status is currently being sent in 2 parts (here and in program class)
            // TODO: optimize this

            var msg = JsonConvert.SerializeObject(new
            {
                WorkCount = WorkCount,
                CrawledCount = CachedCrawledWorkCount
            });

            NetworkManager.MessageHandler.SendMessage(new NetworkMessage(NetworkMessageType.StatusCheck, msg));
        }

        /// <summary>
        /// Keep checking if results are ready to be sent
        /// </summary>
        void ResultsCheck(object sender, System.Timers.ElapsedEventArgs e)
        {
            // if no work assigned, ignore it
            if (assignedurl == null) return;

            // if we are connected and are not yet sending results but results are ready 
            // keep sending ResultsReady request on every timer tick until server is ready to receieve them
            if (ConnectedToHost && !sendingResults && resultsReady)
            {
                // send request to Host that work results are ready
                NetworkManager.MessageHandler.SendMessage(new NetworkMessage(NetworkMessageType.ResultsReady));
            }
        }

        /// <summary>
        /// Work received from host
        /// </summary>
        void WorkReceived(string url)
        {
            workCancelSource?.Cancel();
            workCancelSource = new CancellationTokenSource();

            Logger.Log("New work assigned - " + url);

            PrepareForNewWork();

            // add to backlog
            AddToBacklog(url);

            assignedurl = url;
            resultsReady = false;

            // start work checker
            var tsource = workCancelSource;
            Task.Run(() =>
            {
                while (!tsource.IsCancellationRequested)
                {
                    Task.Delay(2000).Wait();
                    if (tsource.IsCancellationRequested) break;

                    if (Backlog.Count == 0 && CachedCrawledWorkCount > 0 &&
                        areWorkersActive?.Invoke() == false && !resultsReady)
                    {
                        Logger.Log($"Checker setting results as ready! (Active: {areWorkersActive?.Invoke()}" +
                            $", Sending: {sendingResults})", Logger.LogSeverity.Debug);

                        resultsReady = true;
                    }
                }

            }, tsource.Token);
        }

        /// <summary>
        /// Clears backlog and cache and prepares worker to be assigned new work from Host.
        /// </summary>
        void PrepareForNewWork(bool cleanCrawled = false)
        {
            assignedurl = null;
            sendingResults = false;

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
                if (deleted > 0) Logger.Log($"Deleted {deleted} crawled works.", Logger.LogSeverity.Debug);
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


        /// <summary>
        /// Dump backlog to backup cache
        /// </summary>
        void TemporaryDump()
        {
            bool dumping = false;
            var waitTime = (int)TimeSpan.FromSeconds(config.AutoSaveIntervalSeconds).TotalMilliseconds;

            while (!disposing)
            {
                Thread.Sleep(waitTime);
                if (disposing) break;

                if (dumping) continue;
                dumping = true;

                try
                {
                    if (Backlog.Count == 0) continue;

                    DumpMemoryToCache(true);
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to dump to backup cache! " + ex.GetDetailedMessage(), Logger.LogSeverity.Warning);
                }
                finally
                {
                    dumping = false;
                }
            }
        }

        public class DomainFailInfo
        {
            public bool Blocked { get; set; }
            public string Domain { get; set; }
            public uint FailCount { get; set; }
            public uint WaitMinutes { get; set; } = 5;
            public DateTime RecrawlTime { get; set; }
        }
    }
}
