using CryCrawler.Structures;
using LiteDB;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CryCrawler.Worker
{
    /// <summary>
    /// Manages work (URLs to crawl) either locally, via dashboard or via connected Host
    /// </summary>
    public class WorkManager
    {
        bool isFIFO = false;
        long cachedWork = 0, cachedCrawled = 0;
        const int MemoryLimitCount = 10000;

        readonly WorkerConfiguration config;
        readonly ConcurrentQueueOrStack<Work> backlog;

        public bool IsWorkAvailable => backlog.Count > 0 || cachedWork > 0;

        public WorkManager(WorkerConfiguration config)
        {
            this.config = config;

            // depth-search = Stack (LIFO), breadth-search = Queue (FIFO)
            isFIFO = !config.DepthSearch;
            backlog = new ConcurrentQueueOrStack<Work>(!isFIFO);

            if (config.HostEndpoint.UseHost)
            {
                // HOST MODE

                // use host
                Logger.Log($"Using Host as Url source ({config.HostEndpoint.Hostname}:{config.HostEndpoint.Port})");

                // delete existing cache and create new one
                CacheDatabase.Recreate();

                // TODO: establish connection (full proof retrying connection - make separate class for this) - keep at it even if it fails - use failproof class
            }
            else
            {
                // LOCAL MODE

                // use local Urls and Dashboard provided URLs
                Logger.Log($"Using local Url source");

                // load all local Urls (only if not yet crawled)
                foreach (var url in config.Urls)
                    if (CacheDatabase.GetWork(out Work w, url, true) == false)
                        AddToBacklog(url);

                // load cache stats
                cachedWork = CacheDatabase.GetWorkCount(false);
                cachedCrawled = CacheDatabase.GetWorkCount(true);
            }
        }

        public void AddToCrawled(Work w)
        {
            // TODO: check for existing work and update that

            if (CacheDatabase.Insert(w, true)) cachedCrawled++;
        }

        public void AddToBacklog(string url)
        {
            var w = new Work(url);

            // if above memory limit, save to database
            if (backlog.Count >= MemoryLimitCount)
            {
                // save to database     
                if (CacheDatabase.Insert(w)) cachedWork++;
            }
            // if below memory limit but cache is not empty, load cache to memory
            else if (cachedWork > 0)
            {
                // save to database
                if (CacheDatabase.Insert(w)) cachedWork++;

                // load cache to memory
                LoadCacheToMemory();                  
            }
            else
            {
                // normally add to memory
                backlog.AddItem(w);
            }
        }

        public bool GetWork(out string url)
        {
            Work w = null;

            if (isFIFO)
            {
                // take from memory if available
                if (backlog.Count > 0) backlog.TryGetItem(out w);

                // take from database if available and memory is empty
                if (w == null && cachedWork > 0 && CacheDatabase.GetWorks(out IEnumerable<Work> works, 1, true))
                {
                    w = works.FirstOrDefault();
                    cachedWork--;

                    LoadCacheToMemory();
                }
            }
            else
            {
                // take from database if available
                if (cachedWork > 0 && CacheDatabase.GetWorks(out IEnumerable<Work> works, 1, false))
                {
                    w = works.FirstOrDefault();
                    cachedWork--;

                    LoadCacheToMemory();
                }

                // take from memory if available and database is empty
                if (w == null && backlog.Count > 0) backlog.TryGetItem(out w);
            }         

            url = w?.Url;
            return w != null;
        }

        private void LoadCacheToMemory()
        {
            long howMuch = MemoryLimitCount - (long)backlog.Count;
            howMuch = howMuch > cachedWork ? cachedWork : howMuch;

            if (CacheDatabase.GetWorks(out IEnumerable<Work> works, (int)howMuch, isFIFO))
            {
                backlog.AddItems(works);
                cachedWork -= howMuch;
            }
        }

        public void Dispose()
        {
            // TODO: dump all work if working locally - if working via Host, delete cache
        }
    }
}
