using Newtonsoft.Json;
using CryCrawler.Network;
using System.Diagnostics;
using System.Collections.Generic;
using System;

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
            else if (filename == "config")
            {
                contentType = ContentTypes["json"];

                // parse content
                var config = JsonConvert.DeserializeObject<ConfigUpdateRequest>(body);

                return handleConfigUpdate(config);
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
            TaskCount = crawler.CurrentTasks.Count,
            RecentDownloads = new List<DownloadedWork>(crawler.RecentDownloads),
            // read local config, not Host provided
            AcceptedExtensions = config.WorkerConfig.AcceptedExtensions,
            AccesptedMediaTypes = config.WorkerConfig.AcceptedMediaTypes,
            ScanTargetMediaTypes = config.WorkerConfig.ScanTargetsMediaTypes,
            SeedUrls = config.WorkerConfig.Urls,
            AllFiles = config.WorkerConfig.AcceptAllFiles
        });

        string handleStateUpdate(StateUpdateRequest req)
        {
            var error = "";

            try
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

                if (req.ClearCache == true)
                {
                    if (crawler.IsActive)
                    {
                        crawler.Stop();
                        crawler.Manager.ClearCache();
                        crawler.Start();
                    }
                    else
                    {
                        crawler.Manager.ClearCache();
                    }
                }

            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            return JsonConvert.SerializeObject(new
            {
                Success = string.IsNullOrEmpty(error),
                Error = error
            });
        }

        string handleConfigUpdate(ConfigUpdateRequest req)
        {
            // handle it
            config.WorkerConfig.Urls = req.SeedUrls;
            config.WorkerConfig.AcceptAllFiles = req.AllFiles;
            config.WorkerConfig.AcceptedExtensions = req.Extensions;
            config.WorkerConfig.AcceptedMediaTypes = req.MediaTypes;
            config.WorkerConfig.ScanTargetsMediaTypes = req.ScanTargets;
            crawler.Manager.ReloadUrlSource();

            return JsonConvert.SerializeObject(new
            {
                Success = true,
                Error = ""
            });
        }


        class StateUpdateRequest
        {
            public bool IsActive { get; set; }
            public bool ClearCache { get; set; }
        }

        class ConfigUpdateRequest
        {
            public bool AllFiles { get; set; }
            public List<string> Extensions { get; set; }
            public List<string> MediaTypes { get; set; }
            public List<string> ScanTargets { get; set; }
            public List<string> SeedUrls { get; set; }
        }
    }
}
