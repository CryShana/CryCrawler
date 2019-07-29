using MessagePack;
using System;
using System.Collections.Generic;
using System.Net;

namespace CryCrawler
{
    [MessagePackObject(keyAsPropertyName: true)]
    public class Configuration
    {
        public HostConfiguration HostConfig { get; set; } = new HostConfiguration();
        public WorkerConfiguration WorkerConfig { get; set; } = new WorkerConfiguration();
        public WebGUIEndPoint WebGUI { get; set; } = new WebGUIEndPoint();
        public string CacheFilename { get; set; } = CacheDatabase.DefaultFilename;
        public string PluginsDirectory { get; set; } = ConfigManager.PluginsDirectory;
        public Dictionary<string, string> CompiledPlugins { get; set; } = new Dictionary<string, string>();
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class HostConfiguration
    {
        public HostListeningEndPoint ListenerConfiguration { get; set; } = new HostListeningEndPoint();
        public long MaxClientAgeMinutes { get; set; } = (long)TimeSpan.FromDays(1).TotalMinutes;
        public int ClientWorkLimit { get; set; } = 2000;
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class WorkerConfiguration
    {
        public HostEndPoint HostEndpoint { get; set; } = new HostEndPoint();

        public string DownloadsPath { get; set; } = "downloads";
        public bool DontCreateSubfolders { get; set; } = false;
        public bool LogEveryCrawl { get; set; } = true;
        public int MaxConcurrency { get; set; } = 3;
        public bool DepthSearch { get; set; } = false;
        public int MaxLoggedDownloads { get; set; } = 30;
        public int MaxFileChunkSizekB { get; set; } = 200;
        public int MaxCrawledWorksBeforeCleanHost { get; set; } = 2000;
        public int AutoSaveIntervalSeconds { get; set; } = 40;

        /// <summary>
        /// When deciding if file is acceptable to be downloaded, extension is checked first
        /// </summary>
        public List<string> AcceptedExtensions { get; set; } = new List<string>()
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".webm",
            ".mp4",
            ".txt"
        };

        /// <summary>
        /// When deciding if file is acceptable to be downloaded, media type is checked second
        /// </summary>
        public List<string> AcceptedMediaTypes { get; set; } = new List<string>()
        {
            "image/png",
            "image/jpeg",
            "audio/mpeg",
            "audio/vorbis",
            "video/mp4",
            "application/pdf"
        };

        /// <summary>
        /// Decides which files should be scanned for new URLs to crawl
        /// </summary>
        public List<string> ScanTargetsMediaTypes { get; set; } = new List<string>()
        {
            "text/html",
            "text/css",
            "application/javascript"
        };

        /// <summary>
        /// If not empty, only URLs that contain whitelisted strings will be added to backlog
        /// </summary>
        public List<string> DomainWhitelist { get; set; } = new List<string>();

        /// <summary>
        /// URLs that contain blacklisted strings will not be added to backlog
        /// </summary>
        public List<string> DomainBlacklist { get; set; } = new List<string>();

        public double MaximumAllowedFileSizekB { get; set; } = -1;
        public double MinimumAllowedFileSizekB { get; set; } = -1;

        public bool AcceptAllFiles { get; set; } = false;

        public List<string> Urls { get; set; } = new List<string>();
    }

    #region Other Classes
    [MessagePackObject(keyAsPropertyName: true)]
    public class WebGUIEndPoint
    {
        public string IP { get; set; } = IPAddress.Loopback.ToString();
        public int Port { get; set; } = 6001;
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class HostListeningEndPoint
    {
        public string IP { get; set; } = IPAddress.Any.ToString();
        public int Port { get; set; } = 6000;
        public string Password { get; set; } = "";
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class HostEndPoint
    {
        public string Hostname { get; set; } = "localhost";
        public int Port { get; set; } = 6000;
        public string Password { get; set; } = "";
        public bool UseHost { get; set; } = false;
        public string ClientId { get; set; }
    }
    #endregion

}
