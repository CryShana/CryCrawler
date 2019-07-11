using Newtonsoft.Json;
using CryCrawler.Network;
using System.Diagnostics;
using System.Collections.Generic;

namespace CryCrawler.Worker
{
    class WorkerResponder : WebGUIResponder
    {
        protected override string GetPath() => "Worker.GUI";
        protected override string ResponsePOST(string filename, string body, out string contentType)
        {
            if (filename == "status")
            {
                contentType = ContentTypes["json"];
                return getStatus();
            }
            else if (filename == "state")
            {
                contentType = ContentTypes["json"];

                // parse content
                var state = JsonConvert.DeserializeObject<StateUpdateRequest>(body);

                return handleStateUpdate(state);
            }
            else return base.ResponsePOST(filename, body, out contentType);
        }

        Crawler crawler;
        Configuration config;

        public WorkerResponder(Configuration config, Crawler crawler)
        {
            this.config = config;
            this.crawler = crawler;
        }

        // get worker information here
        string getStatus() => JsonConvert.SerializeObject(new
        {
            IsActive = crawler.IsActive,
            IsWorking = !crawler.WaitingForWork,
            ConnectedToHost = crawler.Manager.ConnectedToHost,
            UsingHost = config.WorkerConfig.HostEndpoint.UseHost,
            HostEndpoint = $"{crawler.Config.HostEndpoint.Hostname}:{crawler.Config.HostEndpoint.Port}",
            ClientId = crawler.Config.HostEndpoint.ClientId,
            WorkCount = crawler.Manager.WorkCount,
            CacheCount = crawler.Manager.CachedWorkCount,
            CacheCrawledCount = crawler.Manager.CachedCrawledWorkCount,
            UsageRAM = Process.GetCurrentProcess().PrivateMemorySize64,
            CurrentTasks = new Dictionary<int, string>(crawler.CurrentTasks),
            ConfigurationWorker = crawler.Config,
            TaskCount = crawler.CurrentTasks.Count,
            RecentDownloads = new List<DownloadedWork>(crawler.RecentDownloads)
        });

        string handleStateUpdate(StateUpdateRequest req)
        {
            // handle it
            if (req.IsActive == false)
            {
                // stop it if started
                if (crawler.IsActive) crawler.Stop();                
            }
            else
            {
                // start it if stopped
                if (!crawler.IsActive) crawler.Start();
            }

            return JsonConvert.SerializeObject(new
            {
                Success = true,
                Error = ""
            });
        }

        class StateUpdateRequest
        {
            public bool IsActive { get; set; }
        }
    }
}
