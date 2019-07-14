using System.Collections;
using System.Collections.Generic;
using System.Net;
using CryCrawler.Network;
using CryCrawler.Worker;

namespace CryCrawler.Host
{
    public class HostProgram : IProgram
    {
        readonly WebGUI webgui;
        readonly Configuration config;
        readonly CacheDatabase database;
        readonly WorkManager workmanager;
        readonly WorkerManager workermanager;

        public HostProgram(Configuration config)
        {
            this.config = config;

            database = new CacheDatabase(config.CacheFilename);

            workmanager = new WorkManager(config.WorkerConfig, database);

            workermanager = new WorkerManager(workmanager, config);

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
        }

        void Workmanager_HostMessageReceived(NetworkMessage w, NetworkMessageHandler<NetworkMessage> msgHandler)
        {
            switch (w.MessageType)
            {
                case NetworkMessageType.ConfigUpdate:
                    Logger.Log("Host configuration receieved!");
                    var config = ((IDictionary<object, object>)w.Data).Deserialize<WorkerConfiguration>();

                    // override config in worker manager (this also disconnects it from local configuration)
                    workermanager.WorkerConfig = config;

                    break;
            }
        }
    }
}
