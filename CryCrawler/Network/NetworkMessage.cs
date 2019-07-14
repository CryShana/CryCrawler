using MessagePack;

namespace CryCrawler.Network
{
    [MessagePackObject]
    public class NetworkMessage
    {
        [Key(0)]
        public NetworkMessageType MessageType { get; }

        [Key(1)]
        public object Data { get; }       

        public NetworkMessage(NetworkMessageType msgType, object data = null)
        {
            MessageType = msgType;
            Data = data;
        }
    }

    public enum NetworkMessageType
    {
        OK,             // to confirm responses
        Join,           // request to join Host worker chain
        Reject,         // response to rejected Join request
        Accept,         // response to accepted Join request
        StatusCheck,     // for checking whether clients are alive and responsive
        ResultsReady,   // for reporting to Host whether results are ready to be delivered
        Disconnect,     // for reporting imminent client/Host disconnect
        Work,           // for giving new work to clients
        ConfigUpdate         // for giving results to Host

        // add more later
    }
}
