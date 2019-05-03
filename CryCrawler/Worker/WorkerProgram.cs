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
        readonly Configuration configuration;
        readonly ConcurrentQueueOrStack<string> urlSource;

        public WorkerProgram(Configuration config)
        {
            configuration = config;

            // depth-search = Stack (LIFO), breadth-search = Queue (FIFO)
            urlSource = new ConcurrentQueueOrStack<string>(config.WorkerConfig.DepthSearch);

            webgui = new WebGUI(new IPEndPoint(IPAddress.Parse(config.WebGUI.IP), config.WebGUI.Port), new WorkerResponder());
        }

        public void Start()
        {
            // Decide on Url source
            if (configuration.WorkerConfig.HostEndpoint.UseHost)
            {
                // host mode
                Logger.Log($"Using Host as Url source ({configuration.WorkerConfig.HostEndpoint.Hostname}:{configuration.WorkerConfig.HostEndpoint.Port})");

                // TODO: connect to Host
            }
            else
            {
                // local mode
                Logger.Log($"Using local Url source");

                // Add configured URLs to collection
                foreach (var url in configuration.WorkerConfig.Urls)
                    urlSource.AddItem(url);
            }

            // Start UI server
            webgui.Start();
        }

        public void Stop()
        {
            // cleanup
            webgui.Stop();
        }
    }
}
