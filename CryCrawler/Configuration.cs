using MessagePack;
using System.Net;

namespace CryCrawler
{
    [MessagePackObject(keyAsPropertyName: true)]
    public class Configuration
    {
        public HostConfiguration HostConfig { get; set; } = new HostConfiguration();
        public WorkerConfiguration WorkerConfig { get; set; } = new WorkerConfiguration();
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class HostConfiguration
    {
        public HostListeningEndPoint HostEndpoint { get; set; } = new HostListeningEndPoint();
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public class WorkerConfiguration
    {
        public HostEndPoint HostEndpoint { get; set; } = new HostEndPoint();
    }

    #region Other Classes
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
