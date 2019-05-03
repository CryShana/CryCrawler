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
    public class HostProgram
    {
        readonly Configuration configuration;
        readonly WorkerManager manager;

        public HostProgram(Configuration config)
        {
            configuration = config;
            manager = new WorkerManager(
                new IPEndPoint(IPAddress.Parse(config.HostConfig.HostEndpoint.IP), config.HostConfig.HostEndpoint.Port),
                config.HostConfig.HostEndpoint.Password);
        }

        public void Start()
        {
            // start listening for connections
            manager.StartListening();
        }
    }
}
