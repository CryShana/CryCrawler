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
            FindingValidWorks = crawler.Manager.ConsecutiveInvalidWorks > 10,

            // use local config for this
            UsingHost = config.WorkerConfig.HostEndpoint.UseHost,
            HostEndpoint = $"{config.WorkerConfig.HostEndpoint.Hostname}:{config.WorkerConfig.HostEndpoint.Port}",
            ClientId = config.WorkerConfig.HostEndpoint.ClientId,

            WorkCount = crawler.Manager.WorkCount,
            TaskCount = crawler.CurrentTasks.Count,
            CacheCount = crawler.Manager.CachedWorkCount,
            CacheCrawledCount = crawler.Manager.CachedCrawledWorkCount,
            UsageRAM = Process.GetCurrentProcess().PrivateMemorySize64,
            CurrentTasks = new Dictionary<int, string>(crawler.CurrentTasks),
            RecentDownloads = new List<DownloadedWork>(crawler.RecentDownloads),

            // read current crawler config (either local or host provided)
            Whitelist = crawler.Config.DomainWhitelist,
            Blacklist = crawler.Config.DomainBlacklist,
            AcceptedExtensions = crawler.Config.AcceptedExtensions,
            AccesptedMediaTypes = crawler.Config.AcceptedMediaTypes,
            ScanTargetMediaTypes = crawler.Config.ScanTargetsMediaTypes,
            SeedUrls = crawler.Config.Urls,
            AllFiles = crawler.Config.AcceptAllFiles,
            MaxSize = crawler.Config.MaximumAllowedFileSizekB,
            MinSize = crawler.Config.MinimumAllowedFileSizekB,
            DontSubfolders = crawler.Config.DontCreateSubfolders,
            FilenameCriteria = crawler.Config.FilenameMustContainEither,
            URLPatterns = crawler.Config.URLMustMatchPattern,
            BlacklistedURLPatterns = crawler.Config.BlacklistedURLPatterns,
            UserAgent = crawler.Config.UserAgent,
            RespectRobots = crawler.Config.RespectRobotsExclusionStandard
        });

        string handleStateUpdate(StateUpdateRequest req)
        {
            var error = "";

            try
            {
                // handle it
                if (req.IsActive == false)
                {
                    Logger.Log("Stopping crawler...");

                    // stop it if started
                    if (crawler.IsActive) crawler.Stop();
                }
                else
                {
                    Logger.Log("Starting crawler...");

                    // start it if stopped
                    if (!crawler.IsActive) crawler.Start();
                }

                if (req.ClearCache == true)
                {
                    if (config.WorkerConfig.HostEndpoint.UseHost)
                        throw new InvalidOperationException("Can not clear cache when using Host as Url source!");

                    Logger.Log("Clearing cache...");

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
            if (config.WorkerConfig.HostEndpoint.UseHost)
                throw new InvalidOperationException("Can not update configuration when using Host as Url source!");

            string error = "";
            try
            {
                // update configuration and save it
                config.WorkerConfig.Urls = req.SeedUrls;
                config.WorkerConfig.UserAgent = req.UserAgent;
                config.WorkerConfig.AcceptAllFiles = req.AllFiles;
                config.WorkerConfig.DomainWhitelist = req.Whitelist;
                config.WorkerConfig.DomainBlacklist = req.Blacklist;
                config.WorkerConfig.AcceptedExtensions = req.Extensions;
                config.WorkerConfig.AcceptedMediaTypes = req.MediaTypes;
                config.WorkerConfig.ScanTargetsMediaTypes = req.ScanTargets;
                config.WorkerConfig.MaximumAllowedFileSizekB = req.MaxSize;
                config.WorkerConfig.MinimumAllowedFileSizekB = req.MinSize;
                config.WorkerConfig.BlacklistedURLPatterns = req.BlacklistedURLPatterns;
                config.WorkerConfig.URLMustMatchPattern = req.URLPatterns;
                config.WorkerConfig.DontCreateSubfolders = req.DontCreateSubfolders;
                config.WorkerConfig.FilenameMustContainEither = req.FilenameCriteria;
                config.WorkerConfig.RespectRobotsExclusionStandard = req.RespectRobots;
                ConfigManager.SaveConfiguration(ConfigManager.LastLoaded);

                // reload seed urls
                crawler.Manager.ReloadUrlSource();
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
            public List<string> Whitelist { get; set; }
            public List<string> Blacklist { get; set; }
            public List<string> FilenameCriteria { get; set; }
            public List<string> URLPatterns { get; set; }
            public List<string> BlacklistedURLPatterns { get; set; }
            public double MaxSize { get; set; }
            public double MinSize { get; set; }
            public string UserAgent { get; set; }
            public bool RespectRobots { get; set; }
            public bool DontCreateSubfolders { get; set; }
        }
    }
}
