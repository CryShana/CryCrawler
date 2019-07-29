using System.Net;
using System.Linq;
using Newtonsoft.Json;
using CryCrawler.Worker;
using CryCrawler.Network;
using System.Collections.Generic;

namespace CryCrawler.Host
{
    public class HostProgram : IProgram
    {
        readonly WebGUI webgui;
        readonly Configuration config;
        readonly PluginManager plugins;
        readonly CacheDatabase database;
        readonly WorkManager workmanager;
        readonly WorkerManager workermanager;

        public HostProgram(Configuration config, PluginManager plugins)
        {
            this.config = config;
            this.plugins = plugins;

            database = new CacheDatabase(config.CacheFilename);

            workmanager = new WorkManager(config.WorkerConfig, database, plugins, () =>
            {
                return workermanager.Clients.Count(x => x.Online && string.IsNullOrEmpty(x.AssignedUrl))
                     < workermanager.Clients.Count(x => x.Online);
            });

            workermanager = new WorkerManager(workmanager, config, new FreeWorkerPicker(), plugins);

            webgui = new WebGUI(new IPEndPoint(IPAddress.Parse(config.WebGUI.IP), config.WebGUI.Port), new HostResponder(config, workmanager, workermanager));

            workmanager.HostMessageReceived += Workmanager_HostMessageReceived;
        }

        public void Start()
        {
            // start listening for connections
            workermanager.Start();

            webgui.Start();
        }

        public void Stop()
        {
            // cleanup
            workermanager.Stop();

            webgui.Stop();

            workmanager.Dispose();
        }

        void Workmanager_HostMessageReceived(NetworkMessage w, NetworkMessageHandler<NetworkMessage> msgHandler)
        {
            switch (w.MessageType)
            {
                case NetworkMessageType.ConfigUpdate:
                    Logger.Log("Host configuration received!");
                    var config = ((IDictionary<object, object>)w.Data).Deserialize<WorkerConfiguration>();

                    // override config in worker manager (this also disconnects it from local configuration)
                    workermanager.WorkerConfig = config;

                    break;
                case NetworkMessageType.StatusCheck:
                    var msg = JsonConvert.SerializeObject(new
                    {
                        IsHost = true,
                        IsActive = workermanager.IsListening,
                        IsBusy = workmanager.ConsecutiveInvalidWorks > 10
                    });

                    msgHandler.SendMessage(new NetworkMessage(NetworkMessageType.StatusCheck, msg));
                    break;
            }
        }
    }
}
