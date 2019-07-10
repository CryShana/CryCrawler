﻿using CryCrawler.Network;
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
        string getStatus() => JsonConvert.SerializeObject(new
        {
            IsActive = crawler.IsActive,
            IsWorking = !crawler.WaitingForWork,
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
    }
}
