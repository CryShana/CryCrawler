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

        public HostProgram(Configuration config)
        {
            configuration = config;
            manager = new WorkerManager(
                new IPEndPoint(IPAddress.Parse(config.HostConfig.ListenerConfiguration.IP), config.HostConfig.ListenerConfiguration.Port),
                config.HostConfig.ListenerConfiguration.Password);
        }

        public void Start()
        {
            // start listening for connections
            manager.StartListening();
        }

        public void Stop()
        {
            // cleanup
            manager.Stop();
        }
    }
}
