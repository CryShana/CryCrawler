using System;
using System.Linq;
using CryCrawler.Structures;
using System.Collections.Generic;

namespace CryCrawler.Worker
{
    /// <summary>
    /// Manages work (URLs to crawl) either locally, via dashboard or via connected Host
    /// </summary>
    public class WorkManager
    {
        bool isFIFO = false;
        readonly Configuration config;

        const int MaxMemoryLimitMB = 300;
        public readonly int MemoryLimitCount = (MaxMemoryLimitMB * 1024 * 1024) / 200;

        #region Public Properties
        public ConcurrentQueueOrStack<Work, string> Backlog { get; }
        public long WorkCount => Backlog.Count + CachedWorkCount;
        public long CachedWorkCount { get; private set; } = 0;
        public long CachedCrawledWorkCount { get; private set; } = 0;
        public bool IsWorkAvailable => Backlog.Count > 0 || CachedWorkCount > 0;
        #endregion

        public WorkManager(Configuration config, int newMemoryLimitCount) : this(config) => MemoryLimitCount = newMemoryLimitCount;
        public WorkManager(Configuration config)
        {
            this.config = config;

            // depth-search = Stack (LIFO), breadth-search = Queue (FIFO)
            isFIFO = !config.WorkerConfig.DepthSearch;
            Backlog = new ConcurrentQueueOrStack<Work, string>(!isFIFO, t => t.Key);

            if (config.WorkerConfig.HostEndpoint.UseHost)
            {
                // HOST MODE

                // use host
                Logger.Log($"Using Host as Url source " +
                    $"({config.WorkerConfig.HostEndpoint.Hostname}:{config.WorkerConfig.HostEndpoint.Port})");

                // delete existing cache and create new one
                DatabaseContext.EnsureNew(config.CacheFilename);

                // TODO: establish connection (full proof retrying connection - make separate class for this) - keep at it even if it fails - use failproof class
            }
            else
            {
                // LOCAL MODE

                // use local Urls and Dashboard provided URLs
                Logger.Log($"Using local Url source");

                using (var db = new DatabaseContext(config.CacheFilename))
                {
                    // load dumped 
                    var dumped = db.Dumped.ToList();
                    if (dumped != null && dumped.Count > 0)
                    {
                        // add to backlog
                        Backlog.AddItems(dumped);

                        // clear dumped
                        db.Dumped.RemoveRange(dumped);

                        Logger.Log($"Loaded {dumped.Count} backlog items from cache.");
                    }

                    // load all local Urls (only if not yet crawled)
                    foreach (var url in config.WorkerConfig.Urls)
                    {
                        var w = db.Crawled.Where(x => x.Url == url).FirstOrDefault();
                        if (w == null) AddToBacklog(url);              
                        else Logger.Log($"Skipping  specified URL '{url}' - crawled at {w.AddedTime.ToString("dd.MM.yyyy HH:mm:ss")}", Logger.LogSeverity.Debug);                     
                    }


                    // load cache stats
                    CachedWorkCount = db.CachedBacklog.Count();
                    CachedCrawledWorkCount = db.Crawled.Count();
                }
            }
        }

        public void AddToCrawled(Work w)
        {
            using (var db = new DatabaseContext(config.CacheFilename))
            {
                db.Crawled.Add(w);
                CachedCrawledWorkCount += db.SaveChanges();
            }
        }

        public void AddToBacklog(string url)
        {
            using (var db = new DatabaseContext(config.CacheFilename))
            {
                var w = new Work(url);

                // if above memory limit, save to database
                if (Backlog.Count >= MemoryLimitCount)
                {
                    // save to database     
                    db.CachedBacklog.Add(w);
                    CachedWorkCount += db.SaveChanges();
                }
                // if below memory limit but cache is not empty, load cache to memory
                else if (CachedWorkCount > 0)
                {
                    // save to database     
                    db.CachedBacklog.Add(w);
                    CachedWorkCount += db.SaveChanges();

                    // load cache to memory
                    LoadCacheToMemory();
                }
                else
                {
                    // normally add to memory
                    Backlog.AddItem(w);
                }
            }
        }
        public void AddToBacklog(IEnumerable<string> urls)
        {
            var works = new List<Work>();
            foreach (var i in urls) works.Add(new Work(i));

            using (var db = new DatabaseContext(config.CacheFilename))
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

                    db.CachedBacklog.AddRange(forCache);
                    CachedWorkCount += db.SaveChanges();
                }
            }
        }
        public bool GetWork(out string url)
        {
            Work w = null;
            url = null;

            if (isFIFO)
            {
                // take from memory if available
                if (Backlog.Count > 0) Backlog.TryGetItem(out w);

                using (var db = new DatabaseContext(config.CacheFilename))
                {
                    // take from database if available and memory is empty
                    if (w == null && CachedWorkCount > 0)
                    {
                        w = db.CachedBacklog.FirstOrDefault();

                        db.CachedBacklog.Remove(w);
                        CachedWorkCount -= db.SaveChanges();
                    }
                }
            }
            else
            {
                using (var db = new DatabaseContext(config.CacheFilename))
                {
                    // take from database if available
                    if (CachedWorkCount > 0)
                    {
                        w = db.CachedBacklog.LastOrDefault();

                        db.CachedBacklog.Remove(w);
                        CachedWorkCount -= db.SaveChanges();
                    }
                }

                // take from memory if available and database is empty
                if (w == null && Backlog.Count > 0) Backlog.TryGetItem(out w);
            }

            url = w?.Url;

            // if there is space to load cache to memory, do it
            if (Backlog.Count < MemoryLimitCount && CachedWorkCount > 0) LoadCacheToMemory();

            return w != null;
        }

        public void ReportWorkResult(string url, bool success)
        {
            // TODO: improve this whole thing

            // report result

            // if recrawl is enabled, re-add it here, otherwise dump the url

            if (success) AddToCrawled(new Work(url)
            {
                LastCrawled = DateTime.Now
            });
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
                if (url.Length >= Work.MaxIndexLength - 1)
                {
                    // find work with identical url to really confirm it
                    var work = Backlog.FindItem(x => x.Url == url);
                    backlogContains = work != null;
                }
            }

            // skip further checking if backlog already contains it
            if (backlogContains) return false;

            // check if url already crawled
            using (var db = new DatabaseContext(config.CacheFilename))
                if (db.Crawled.Where(x => x.Url == url).FirstOrDefault() != null) return false;

            return true;
        }


        public void Dispose()
        {
            // dump all work if working locally - if working via Host, delete cache
            if (!config.WorkerConfig.HostEndpoint.UseHost)
            {
                DumpMemoryToCache();
            }
            else
            {
                // delete cache
                DatabaseContext.Delete(config.CacheFilename);
            }
        }


        private void LoadCacheToMemory()
        {
            long howMuch = MemoryLimitCount - (long)Backlog.Count;
            howMuch = howMuch > CachedWorkCount ? CachedWorkCount : howMuch;

            using (var db = new DatabaseContext(config.CacheFilename))
            {
                List<Work> ws;

                if (isFIFO) ws = db.CachedBacklog.Take((int)howMuch).ToList(); 
                else ws = db.CachedBacklog.TakeLast((int)howMuch).Reverse().ToList();

                db.CachedBacklog.RemoveRange(ws);

                CachedWorkCount -= db.SaveChanges();
            }

        }
        private void DumpMemoryToCache()
        {
            using (var db = new DatabaseContext(config.CacheFilename))
            {
                db.Dumped.AddRange(Backlog.ToList());
                var cnt = db.SaveChanges();

                Logger.Log($"Dumped {cnt} backlog items to cache");
            }           
        }
    }
}
