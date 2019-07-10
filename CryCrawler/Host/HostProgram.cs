using System.Net;
using CryCrawler.Network;
using CryCrawler.Worker;

namespace CryCrawler.Host
{
    public class HostProgram : IProgram
    {
        readonly WebGUI webgui;
        readonly Configuration config;
        readonly WorkerManager manager;
        readonly CacheDatabase database;
        readonly WorkManager workmanager;

        public HostProgram(Configuration config)
        {
            this.config = config;

            database = new CacheDatabase(config.CacheFilename);

            workmanager = new WorkManager(config.WorkerConfig, database);

            manager = new WorkerManager(workmanager, config.HostConfig);

            webgui = new WebGUI(new IPEndPoint(IPAddress.Parse(config.WebGUI.IP), config.WebGUI.Port), new HostResponder());
        }

        public void Start()
        {
            // start listening for connections
            manager.Start();
            webgui.Start();
        }

        public void Stop()
        {
            // cleanup
            manager.Stop();
            webgui.Stop();
        }
    }
}
