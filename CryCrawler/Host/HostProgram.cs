using CryCrawler.Network;
using System.Net;

namespace CryCrawler.Host
{
    public class HostProgram : IProgram
    {
        readonly WorkerManager manager;
        readonly WebGUI webgui;

        readonly Configuration config;

        public HostProgram(Configuration config)
        {
            this.config = config;

            manager = new WorkerManager(
                config.HostConfig,
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
