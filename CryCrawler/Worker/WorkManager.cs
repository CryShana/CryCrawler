using CryCrawler.Structures;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryCrawler.Worker
{
    /// <summary>
    /// Manages work (URLs to crawl) either locally, via dashboard or via connected Host
    /// </summary>
    public class WorkManager
    {
        readonly WorkerConfiguration config;
        readonly ConcurrentQueueOrStack<string> urlSource;

        public bool IsWorkAvailable => urlSource.Count > 0;

        public WorkManager(WorkerConfiguration config)
        {
            this.config = config;

            // depth-search = Stack (LIFO), breadth-search = Queue (FIFO)
            urlSource = new ConcurrentQueueOrStack<string>(config.DepthSearch);

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
                foreach (var url in config.Urls)
                    urlSource.AddItem(url);
            }
        }

        public bool GetWork(out string url) => urlSource.TryGetItem(out url);
    }
}
