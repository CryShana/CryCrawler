using MessagePack;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryCrawler.Network
{
    [MessagePackObject]
    public class NetworkMessage
    {
        public object Data { get; set; }
    }
}
