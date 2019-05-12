using CryCrawler.Structures;
using LiteDB;
using System;
using System.Collections.Generic;
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
        ulong cachedWork = 0;
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
                // use host
                Logger.Log($"Using Host as Url source ({config.HostEndpoint.Hostname}:{config.HostEndpoint.Port})");

                // TODO: establish connection (full proof retrying connection - make separate class for this) - keep at it even if it fails - use failproof class
            }
            else
            {
                // use local Urls and Dashboard provided URLs
                Logger.Log($"Using local Url source");

                // load all local Urls
                foreach (var url in config.Urls) AddToBacklog(url);
            }
        }

        public void AddToBacklog(string url)
        {
            var w = new Work(url);

            // if above memory limit, save to database
            if (backlog.Count >= MemoryLimitCount)
            {
                // save to database     
                CacheDatabase.Insert(w);
                cachedWork++;
            }
            // if below memory limit but cache is not empty, load cache to memory
            else if (cachedWork > 0)
            {
                // save to database
                CacheDatabase.Insert(w);
                cachedWork++;

                // load cache to memory
                ulong howMuch = MemoryLimitCount - (ulong)backlog.Count;
                howMuch = howMuch > cachedWork ? cachedWork : howMuch;

                if (CacheDatabase.GetWorks(out IEnumerable<Work> works, (int)howMuch, isFIFO))
                {
                    backlog.AddItems(works);
                    cachedWork -= howMuch;
                }                       
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
                }
            }
            else
            {
                // take from database if available
                if (cachedWork > 0 && CacheDatabase.GetWorks(out IEnumerable<Work> works, 1, false))
                {
                    w = works.FirstOrDefault();
                    cachedWork--;
                }

                // take from memory if available and database is empty
                if (w == null && backlog.Count > 0) backlog.TryGetItem(out w);
            }         

            url = w?.Url;
            return w != null;
        }

        public void Dispose()
        {
            // TODO: should dump all work?
        }
    }
}
