using CryCrawler.Security;
using CryCrawler.Worker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryCrawler.Network
{
    public class NetworkWorkManager
    {
        public IPAddress Address { get; private set; }
        public string Hostname { get; }
        public int Port { get; }
        public bool IsActive { get; private set; }
        public bool IsConnected { get; private set; }
        public string PasswordHash { get; }

        public delegate void WorkHandler(NetworkMessage w);
        public event WorkHandler WorkReceived;

        private CancellationTokenSource csrc;

        public NetworkWorkManager(string hostname, int port, string password = null)
        {
            Port = port;
            Hostname = hostname;
            PasswordHash = SecurityUtils.GetHash(password);
        }

        public void Start()
        {
            if (IsActive) throw new InvalidOperationException("NetworkWorkManager already running!");

            try
            {
                // validate hostname
                if (IPAddress.TryParse(Hostname, out IPAddress addr) == false)
                {
                    // attempt to resolve hostname
                    var hostEntry = Dns.GetHostEntry(Hostname);
                    if (hostEntry.AddressList.Length > 0)
                    {
                        Address = hostEntry.AddressList[0];
                    }
                    else throw new InvalidOperationException("Failed to resolve hostname!");
                }
                else Address = addr;

                // start connection loop
                csrc = new CancellationTokenSource();
                ConnectionLoop(csrc.Token);
            }
            finally
            {
                IsActive = true;
            }
        }

        public void Stop()
        {
            if (!IsActive) return;

            try
            {
                csrc.Cancel();
            }
            finally
            {
                IsActive = false;
                IsConnected = false;
            }
        }
        private void ConnectionLoop(CancellationToken token)
        {
            new Task(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = null;
                    ManualResetEvent reset = new ManualResetEvent(false);

                    try
                    {
                        client = new TcpClient();
                        client.Connect(new IPEndPoint(Address, Port));

                        // message handler here
                        var messageHandler = new NetworkMessageHandler<NetworkMessage>(
                            client.GetStream(),
                            w =>
                            {
                                if (!token.IsCancellationRequested && IsConnected)
                                {
                                    Logger.Log("Received msg type -> " + w.MessageType.ToString());
                                    switch (w.MessageType)
                                    {
                                        case NetworkMessageType.Work:
                                            WorkReceived?.Invoke(w);
                                            break;
                                    }                                  
                                }
                            });

                        // if message handler throws an exception, dispose it
                        messageHandler.ExceptionThrown += (a, b) =>
                        {
                            messageHandler.Dispose();
                            reset.Set();
                        };

                        // handshake
                        SecurityUtils.DoHandshake(messageHandler, PasswordHash, true);

                        IsConnected = true;

                        // wait here until exception is thrown on message handler
                        reset.WaitOne();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Host connection error. " + ex.Message, Logger.LogSeverity.Debug);
                    }
                    finally
                    {
                        Logger.Log("Disconnected");
                        client.ProperlyClose();
                        IsConnected = false;
                    }

                    Task.Delay(300).Wait();
                }

            }, token, TaskCreationOptions.LongRunning).Start();
        }
    }
}
