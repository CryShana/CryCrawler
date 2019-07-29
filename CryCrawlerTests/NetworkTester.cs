using CryCrawler;
using CryCrawler.Network;
using CryCrawler.Security;
using CryCrawler.Worker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CryCrawlerTests
{
    public class NetworkTester
    {
        [Fact]
        public void SSLEstablishing()
        {
            string msgToSend = "Hello World!";
            string msgReceived = "";
            bool success = false;
            int port = 0;

            var t1 = Task.Run(() =>
            {
                var listener = new TcpListener(IPAddress.Any, 0);
                listener.Start();

                port = ((IPEndPoint)listener.LocalEndpoint).Port;

                try
                {
                    listener.Start();

                    var cert = SecurityUtils.BuildSelfSignedCertificate("crycrawler");

                    var cl = listener.AcceptTcpClient();

                    var str = cl.GetStream();

                    var ssl = SecurityUtils.ServerEstablishSSL(str, cert);

                    var bfr = new byte[1024];
                    var rd = ssl.Read(bfr, 0, bfr.Length);
                    var txt = Encoding.UTF8.GetString(bfr, 0, rd);

                    success = true;
                    msgReceived = txt;
                }
                finally
                {
                    listener.Stop();
                }
            });

            Task.Delay(1000).Wait();
            var t2 = Task.Run(() =>
            {
                var cl = new TcpClient();
                cl.Connect(IPAddress.Loopback, port);

                var str = cl.GetStream();

                var ssl = SecurityUtils.ClientEstablishSSL(str);

                var bfr = Encoding.UTF8.GetBytes(msgToSend);
                ssl.Write(bfr, 0, bfr.Length);
            });

            Task.WhenAll(t1, t2).Wait();

            Assert.True(success);
            Assert.Equal(msgToSend, msgReceived);
        }

        [Fact]
        public void SSLWithMessageHandler()
        {
            string clMessage = "Ping!";
            string srMessage = "Pong!";
            string clReceived = "";
            string srReceived = "";

            int port = 0;

            var t1 = Task.Run(() =>
            {
                var listener = new TcpListener(IPAddress.Any, 0);
                listener.Start();

                port = ((IPEndPoint)listener.LocalEndpoint).Port;

                try
                {
                    listener.Start();

                    var cert = SecurityUtils.BuildSelfSignedCertificate("crycrawler");

                    var cl = listener.AcceptTcpClient();

                    var str = cl.GetStream();

                    var ssl = SecurityUtils.ServerEstablishSSL(str, cert);

                    // create message handler
                    var msgHandler = new NetworkMessageHandler<NetworkMessage>(ssl);
                    msgHandler.ExceptionThrown += (a, b) =>
                    {

                    };

                    var response = msgHandler.WaitForResponse(5000).Result;
                    srReceived = response.Data as string;

                    Task.Delay(3000).Wait();

                    msgHandler.SendMessage(new NetworkMessage(NetworkMessageType.Join, srMessage));

                    Task.Delay(1000).Wait();
                    ssl.Close();
                    str.Close();
                }
                finally
                {
                    listener.Stop();
                }
            });

            Task.Delay(1000).Wait();
            var t2 = Task.Run(() =>
            {
                var cl = new TcpClient();
                cl.Connect(IPAddress.Loopback, port);

                var str = cl.GetStream();

                var ssl = SecurityUtils.ClientEstablishSSL(str);

                // create message handler
                var msgHandler = new NetworkMessageHandler<NetworkMessage>(ssl);
                msgHandler.ExceptionThrown += (a, b) =>
                {

                };

                msgHandler.SendMessage(new NetworkMessage(NetworkMessageType.Join, clMessage));

                var response = msgHandler.WaitForResponse(5000).Result;
                clReceived = response.Data as string;

                Task.Delay(1000).Wait();
                ssl.Close();
                str.Close();
            });

            Task.WhenAll(t1, t2).Wait();

            Assert.Equal(clMessage, srReceived);
            Assert.Equal(srMessage, clReceived);
        }
    }
}
