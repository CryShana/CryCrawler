using CryCrawler.Network;
using System.Net;

namespace CryCrawler.Worker
{
    public class WorkerProgram : IProgram
    {
        readonly WebGUI webgui;
        readonly Crawler crawler;
        readonly Configuration config;
        readonly WorkManager workmanager;

        public WorkerProgram(Configuration config)
        {
            this.config = config;

            workmanager = new WorkManager(config);

            crawler = new Crawler(workmanager, config);

            webgui = new WebGUI(new IPEndPoint(IPAddress.Parse(config.WebGUI.IP), config.WebGUI.Port),
                new WorkerResponder(config, crawler));
        }

        public void Start()
        {
            // Start UI server
            webgui.Start();

            // Start crawler
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
