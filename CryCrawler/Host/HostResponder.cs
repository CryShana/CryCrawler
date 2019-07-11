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
                x.Id,
                x.Online,
                LastConnected = x.LastConnected.ToString("dd.MM.yyyy HH:mm:ss"),
                RemoteEndpoint = x.RemoteEndpoint.ToString()
            })
        });

        string handleStateUpdate(StateUpdateRequest req)
        {
            // handle it
            if (req.IsActive == false)
            {
                // stop it if started
                if (workerManager.IsListening) workerManager.Stop();
            }
            else
            {
                // start it if stopped
                if (!workerManager.IsListening) workerManager.Start();
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
