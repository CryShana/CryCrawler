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

            workermanager = new WorkerManager(workmanager, config.HostConfig);

            webgui = new WebGUI(new IPEndPoint(IPAddress.Parse(config.WebGUI.IP), config.WebGUI.Port), new HostResponder(config, workmanager, workermanager));
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
    }
}
