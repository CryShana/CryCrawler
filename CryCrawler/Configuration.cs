using MessagePack;
using System.Collections.Generic;
using System.Net;

namespace CryCrawler
{
    [MessagePackObject(keyAsPropertyName: true)]
    public class Configuration
    {
        public HostConfiguration HostConfig { get; set; } = new HostConfiguration();
        public WorkerConfiguration WorkerConfig { get; set; } = new WorkerConfiguration();
        public WebGUIEndPoint WebGUI { get; set; } = new WebGUIEndPoint();
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class HostConfiguration
    {
        public HostListeningEndPoint ListenerConfiguration { get; set; } = new HostListeningEndPoint();
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class WorkerConfiguration
    {
        public HostEndPoint HostEndpoint { get; set; } = new HostEndPoint();

        public bool DepthSearch { get; set; } = false;
        public List<string> Urls { get; set; } = new List<string>();
    }

    #region Other Classes
    [MessagePackObject(keyAsPropertyName: true)]
    public class WebGUIEndPoint
    {
        public string IP { get; set; } = IPAddress.Loopback.ToString();
        public int Port { get; set; } = 6001;
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class HostListeningEndPoint
    {
        public string IP { get; set; } = IPAddress.Any.ToString();
        public int Port { get; set; } = 6000;
        public string Password { get; set; } = "";
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class HostEndPoint
    {
        public string Hostname { get; set; } = "localhost";
        public int Port { get; set; } = 6000;
        public string Password { get; set; } = "";
        public bool UseHost { get; set; } = false;
    } 
    #endregion

}
