using CryCrawler.Network;
using CryCrawler.Structures;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;

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

            workmanager = new WorkManager(config.WorkerConfig);

            crawler = new Crawler(workmanager, config.WorkerConfig);

            webgui = new WebGUI(new IPEndPoint(IPAddress.Parse(config.WebGUI.IP), config.WebGUI.Port), new WorkerResponder());
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
            crawler.Stop();

            webgui.Stop();

            workmanager.Dispose();
        }
    }
}
