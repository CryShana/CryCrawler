using System.Linq;
using Newtonsoft.Json;
using CryCrawler.Network;
using CryCrawler.Worker;
using System.Diagnostics;

namespace CryCrawler.Host
{
    class HostResponder : WebGUIResponder
    {
        protected override string GetPath() => "Host.GUI";
        protected override string ResponsePOST(string filename, out string contentType)
        {
            if (filename == "status")
            {
                contentType = ContentTypes["json"];
                return getStatus();
            }
            else return base.ResponsePOST(filename, out contentType);
        }

        Configuration config;
        WorkManager workManager;
        WorkerManager workerManager;

        public HostResponder(Configuration config, WorkManager workManager, WorkerManager workerManager)
        {
            this.config = config;
            this.workManager = workManager;
            this.workerManager = workerManager;
        }

        string getStatus() => JsonConvert.SerializeObject(new
        {
            IsListening = workerManager.IsListening,
            WorkAvailable = workManager.IsWorkAvailable,
            ConnectedToHost = workManager.ConnectedToHost,
            UsingHost = config.WorkerConfig.HostEndpoint.UseHost,
            HostEndpoint = $"{config.WorkerConfig.HostEndpoint.Hostname}:{config.WorkerConfig.HostEndpoint.Port}",
            ClientId = config.WorkerConfig.HostEndpoint.ClientId,
            WorkCount = workManager.WorkCount,
            CacheCount = workManager.CachedWorkCount,
            CacheCrawledCount = workManager.CachedCrawledWorkCount,
            UsageRAM = Process.GetCurrentProcess().PrivateMemorySize64,
            Clients = workerManager.Clients.Select(x => new
            {
                Id = x.Id,
                Online = x.Online,
                LastConnected = x.LastConnected,
                RemoteEndpoint = x.RemoteEndpoint.ToString()
            })
        });
    }
}
