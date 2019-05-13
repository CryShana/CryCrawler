using System.Linq;
using CryCrawler.Structures;
using System.Collections.Generic;
using System.Threading;
using System.Collections;
using System;
using static CryCrawler.CacheDatabase;

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

        readonly int MaxLoadLimit = 5000;
        const int MaxMemoryLimitMB = 300;
        public readonly int MemoryLimitCount = (MaxMemoryLimitMB * 1024 * 1024) / 200;

        #region Public Properties
        public ConcurrentQueueOrStack<Work> Backlog { get; }
        public long WorkCount => Backlog.Count + CachedWorkCount;
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
            if (database.Upsert(w, out bool wasIns, true))
            {
                if (wasIns) CachedCrawledWorkCount++;
            }
            else throw new DatabaseErrorException("Failed to upsert crawled work to database!");
        }

        SemaphoreSlim addingSemaphore = new SemaphoreSlim(1);
        public void AddToBacklog(string url)
        {
            var w = new Work(url);

            addingSemaphore.Wait();
            try
            {
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
        public void AddToBacklog(IEnumerable<string> urls)
        {
            var works = new List<Work>();
            foreach (var i in urls) works.Add(new Work(i));

            // for bulk insertion into cache
            const int minItems = 100;
            const int maxItems = 100000;

            addingSemaphore.Wait();
            try
            {
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

                    // Bulk insertion only supports from 100 to 100000 items
                    if (forCacheCount >= minItems && forCacheCount <= maxItems)
                    {
                        if (database.InsertBulk(forCache, forCacheCount)) CachedWorkCount += forCacheCount;
                        else throw new DatabaseErrorException("Failed to bulk insert!");
                    }
                    else if (forCacheCount > maxItems)
                    {
                        // divide it into parts and bulk save it
                        int offset = 0;
                        while (offset < works.Count)
                        {
                            var space = works.Count - offset;
                            var count = maxItems < space ? maxItems : space;
                            var split_works = works.GetRange(offset, count);

                            if (database.InsertBulk(split_works, count))
                            {
                                CachedWorkCount += count;
                                offset += count;
                            }
                            else throw new DatabaseErrorException("Failed to bulk insert!");
                        }
                    }
                    else
                    {
                        foreach (var i in forCache)
                            if (database.Insert(i))
                                CachedWorkCount++;
                            else throw new DatabaseErrorException("Failed to insert!");
                    }
                }
            }
            finally
            {
                addingSemaphore.Release();
            }
        }
        public bool GetWork(out string url)
        {
            Work w = null;

            addingSemaphore.Wait();
            try
            {
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

                // if there is space to load cache to memory, do it
                if (Backlog.Count < MemoryLimitCount && CachedWorkCount > 0) LoadCacheToMemory();
            }
            finally
            {
                addingSemaphore.Release();
            }

            url = w?.Url;
            return w != null;
        }

        private void LoadCacheToMemory()
        {
            long howMuch = MemoryLimitCount - (long)Backlog.Count;
            howMuch = howMuch > CachedWorkCount ? CachedWorkCount : howMuch;

            if (database.GetWorks(out List<Work> works, (int)howMuch, isFIFO, true))
            {
                Backlog.AddItems(works);
                CachedWorkCount -= works.Count;
            }
        }
        
        private void DumpMemoryToCache()
        {
            // TODO: dump all current work (and check for it on load)
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
    }
}
