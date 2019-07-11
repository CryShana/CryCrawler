using CryCrawler.Network;
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
    }
}
