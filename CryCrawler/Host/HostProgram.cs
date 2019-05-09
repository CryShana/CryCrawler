using CryCrawler.Network;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CryCrawler.Host
{
    public class HostProgram : IProgram
    {
        readonly Configuration configuration;
        readonly WorkerManager manager;
        readonly WebGUI webgui;

        public HostProgram(Configuration config)
        {
            configuration = config;

            manager = new WorkerManager(
                new IPEndPoint(IPAddress.Parse(config.HostConfig.ListenerConfiguration.IP), config.HostConfig.ListenerConfiguration.Port),
                config.HostConfig.ListenerConfiguration.Password);

            webgui = new WebGUI(new IPEndPoint(IPAddress.Parse(config.WebGUI.IP), config.WebGUI.Port), new HostResponder());
        }

        public void Start()
        {
            // start listening for connections
            manager.StartListening();
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
