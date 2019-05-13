using System.Linq;
using CryCrawler.Structures;
using System.Collections.Generic;
using System.Threading;

namespace CryCrawler.Worker
{
    /// <summary>
    /// Manages work (URLs to crawl) either locally, via dashboard or via connected Host
    /// </summary>
    public class WorkManager
    {
        bool isFIFO = false;
        readonly CacheDatabase database;
        readonly WorkerConfiguration config;
        readonly int MemoryLimitCount = 10000;
        readonly int MaxLoadLimit = 5000;

        #region Public Properties
        public ConcurrentQueueOrStack<Work> Backlog { get; }
        public long CachedWorkCount { get; private set; } = 0;
        public long CachedCrawledWorkCount { get; private set; } = 0;
        public bool IsWorkAvailable => Backlog.Count > 0 || CachedWorkCount > 0;
        #endregion

        public WorkManager(WorkerConfiguration config, CacheDatabase database, int newMemoryLimitCount) : this(config, database) => MemoryLimitCount = newMemoryLimitCount;
        public WorkManager(WorkerConfiguration config, CacheDatabase database)
        {
            this.config = config;
            this.database = database;

            // depth-search = Stack (LIFO), breadth-search = Queue (FIFO)
            isFIFO = !config.DepthSearch;
            Backlog = new ConcurrentQueueOrStack<Work>(!isFIFO);

            if (config.HostEndpoint.UseHost)
            {
                // HOST MODE

                // use host
                Logger.Log($"Using Host as Url source ({config.HostEndpoint.Hostname}:{config.HostEndpoint.Port})");

                // delete existing cache and create new one
                database.EnsureNew();

                // TODO: establish connection (full proof retrying connection - make separate class for this) - keep at it even if it fails - use failproof class
            }
            else
            {
                // LOCAL MODE

                // use local Urls and Dashboard provided URLs
                Logger.Log($"Using local Url source");

                // load all local Urls (only if not yet crawled)
                foreach (var url in config.Urls)
                    if (database.GetWork(out Work w, url, true) == false)
                        AddToBacklog(url);

                // load cache stats
                CachedWorkCount = database.GetWorkCount(false);
                CachedCrawledWorkCount = database.GetWorkCount(true);
            }
        }

        public void AddToCrawled(Work w)
        {
            // TODO: check for existing work and update that

            if (database.Insert(w, true)) CachedCrawledWorkCount++;
        }

        public void AddToBacklog(string url)
        {
            // TODO: IMPROVE THIS

            var w = new Work(url);

            // if above memory limit, save to database
            if (Backlog.Count >= MemoryLimitCount)
            {
                // save to database     
                if (database.Insert(w)) CachedWorkCount++;
            }
            // if below memory limit but cache is not empty, load cache to memory
            else if (CachedWorkCount > 0)
            {
                // save to database
                if (database.Insert(w)) CachedWorkCount++;

                // load cache to memory
                LoadCacheToMemory();
            }
            else
            {
                // normally add to memory
                Backlog.AddItem(w);
            }
        }

        public bool GetWork(out string url)
        {
            Work w = null;

            // TODO: IMPROVE THIS
            if (isFIFO)
            {
                // take from memory if available
                if (Backlog.Count > 0) Backlog.TryGetItem(out w);

                // take from database if available and memory is empty
                if (w == null && CachedWorkCount > 0 && database.GetWorks(out List<Work> works, 1, true))
                {
                    w = works.FirstOrDefault();
                    CachedWorkCount--;
                }
            }
            else
            {
                // take from database if available
                if (CachedWorkCount > 0 && database.GetWorks(out List<Work> works, 1, false))
                {
                    w = works.FirstOrDefault();
                    CachedWorkCount--;
                }

                // take from memory if available and database is empty
                if (w == null && Backlog.Count > 0) Backlog.TryGetItem(out w);
            }

            // if there is space to load cache to memory, do it  (TODO: check for performance issues here)
            if (Backlog.Count < MemoryLimitCount && CachedWorkCount > 0) LoadCacheToMemory();

            url = w?.Url;
            return w != null;
        }


        private void LoadCacheToMemory()
        {
            // TODO: make this thread safe
            long howMuch = MemoryLimitCount - (long)Backlog.Count;
            howMuch = howMuch > CachedWorkCount ? CachedWorkCount : howMuch;

            if (database.GetWorks(out List<Work> works, (int)howMuch, isFIFO))
            {
                Backlog.AddItems(works);
                CachedWorkCount -= works.Count;
            }
        }

        public void Dispose()
        {
            // TODO: dump all work if working locally - if working via Host, delete cache
        }
    }
}
