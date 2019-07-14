using CryCrawler.Network;
using MessagePack;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Net;

namespace CryCrawler.Worker
{
    public class WorkerProgram : IProgram
    {
        readonly WebGUI webgui;
        readonly Crawler crawler;
        readonly Configuration config;
        readonly CacheDatabase database;
        readonly WorkManager workmanager;

        public WorkerProgram(Configuration config)
        {
            this.config = config;

            database = new CacheDatabase(config.CacheFilename);

            workmanager = new WorkManager(config.WorkerConfig, database);

            crawler = new Crawler(workmanager, config.WorkerConfig);

            webgui = new WebGUI(new IPEndPoint(IPAddress.Parse(config.WebGUI.IP), config.WebGUI.Port), new WorkerResponder(config, crawler));

            // register events
            workmanager.HostMessageReceived += Workmanager_HostMessageReceived;
            crawler.StateChanged += Crawler_StateChanged;
        }

        public void Start()
        {
            webgui.Start();

            crawler.Start();
        }

        public void Stop()
        {
            // cleanup
            webgui.Stop();

            crawler.Stop();

            workmanager.Dispose();
        }

        void Crawler_StateChanged(object sender, bool e) => SendStatusMessage(workmanager?.NetworkManager?.MessageHandler);      

        void Workmanager_HostMessageReceived(NetworkMessage w, 
            NetworkMessageHandler<NetworkMessage> msgHandler)
        {
            switch (w.MessageType)
            {
                case NetworkMessageType.StatusCheck:
                    SendStatusMessage(msgHandler);
                    break;
                case NetworkMessageType.ConfigUpdate:
                    Logger.Log("Host configuration receieved!");
                    var config = ((IDictionary<object,object>)w.Data).Deserialize<WorkerConfiguration>();
                    // override config in crawler (this also disconnects it from local configuration)
                    crawler.Config = config;

                    break;
            }
        }

        void SendStatusMessage(NetworkMessageHandler<NetworkMessage> msgHandler)
        {
            if (workmanager.ConnectedToHost == false || msgHandler == null) return;

            var msg = JsonConvert.SerializeObject(new
            {
                IsActive = crawler.IsActive
            });

            msgHandler.SendMessage(new NetworkMessage(NetworkMessageType.StatusCheck, msg));
        }
    }
}
