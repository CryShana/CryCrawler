using CryCrawler.Network;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;

namespace CryCrawler.Worker
{
    class WorkerResponder : WebGUIResponder
    {
        protected override string GetPath() => "Worker.GUI";
        protected override string ResponsePOST(string filename, out string contentType)
        {
            if (filename == "status")
            {
                contentType = ContentTypes["json"];
                return getStatus();
            }
            else return base.ResponsePOST(filename, out contentType);
        }


        Crawler crawler;
        Configuration config;

        public WorkerResponder(Configuration config, Crawler crawler)
        {
            this.config = config;
            this.crawler = crawler;
        }

        // get worker information here
        string getStatus() => JsonConvert.SerializeObject(new StatusResponses
        {
            IsActive = crawler.IsActive,
            IsWorking = !crawler.WaitingForWork,
            UsingHost = config.WorkerConfig.HostEndpoint.UseHost,
            WorkCount = crawler.Manager.WorkCount,
            CacheCount = crawler.Manager.CachedWorkCount,
            CacheCrawledCount = crawler.Manager.CachedCrawledWorkCount,
            UsageRAM = Process.GetCurrentProcess().PrivateMemorySize64,
            CurrentTasks = new Dictionary<int, string>(crawler.CurrentTasks),
            ConfigurationWorker = crawler.Config,
            TaskCount = crawler.CurrentTasks.Count
        });

        

        class StatusResponses
        {
            public bool IsActive { get; set; }
            public bool IsWorking { get; set; }
            public bool UsingHost { get; set; }
            public long WorkCount { get; set; }
            public long CacheCount { get; set; }
            public long CacheCrawledCount { get; set; }
            public long UsageRAM { get; set; }
            public int TaskCount { get; set; }
            public Dictionary<int, string> CurrentTasks { get; set; }
            public WorkerConfiguration ConfigurationWorker { get; set; }
        }
    }
}
