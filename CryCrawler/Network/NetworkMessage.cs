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
        StatusCheck,    // for checking whether clients are alive and responsive
        ResultsReady,   // for reporting to Host whether results are ready to be delivered
        Disconnect,     // for reporting imminent client/Host disconnect
        Work,           // for giving new work to clients
        ConfigUpdate,   // for giving results to Host
        WorkLimitUpdate,// for updating client work limits
        SendResults,    // worker should send results
        ResultsReceived,// host received client results
        FileCheck,      // host checks if client has files to transfer
        FileTransfer,   // client wants to initiate file transfer
        FileAccept,     // host accepted file transfer
        FileChunk,      // client started sending file chunks
        FileReject,     // host rejects a file transfer
        FileChunkAccept,// host accepted file chunk
        CrawledWorks    // client sends it's crawled list to host

        // add more later
    }
}
