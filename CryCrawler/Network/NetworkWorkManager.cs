using CryCrawler.Security;
using CryCrawler.Worker;
using LiteDB;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        public string ClientId { get; private set; }
        public string PasswordHash { get; }

        public delegate void ConnectedHandler(string clientid);
        public delegate void MessageHandler(NetworkMessage w, 
            NetworkMessageHandler<NetworkMessage> msgHandler);

        public event ConnectedHandler Connected;
        public event ConnectedHandler Disconnected;
        public event MessageHandler MessageReceived;

        private TcpClient client;
        private NetworkMessageHandler<NetworkMessage> messageHandler;

        private CancellationTokenSource csrc;

        public NetworkWorkManager(string hostname, int port, string password = null, string clientId = null)
        {
            Port = port;
            Hostname = hostname;
            ClientId = clientId;
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
                        Address = hostEntry.AddressList.Last();
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

        void ConnectionLoop(CancellationToken token)
        {
            new Task(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    ManualResetEvent reset = new ManualResetEvent(false);

                    try
                    {
                        client = new TcpClient();
                        client.Connect(new IPEndPoint(Address, Port));

                        // message handler here
                        messageHandler = new NetworkMessageHandler<NetworkMessage>(
                            client.GetStream(),
                            w =>
                            {
                                if (!token.IsCancellationRequested && IsConnected)
                                {
                                    // do not log status checks because they happen too frequently
                                    if (w.MessageType != NetworkMessageType.StatusCheck)
                                        Logger.Log("Received message -> " + w.MessageType.ToString(), Logger.LogSeverity.Debug);

                                    if (w.MessageType == NetworkMessageType.Disconnect) client.ProperlyClose();

                                    MessageReceived?.Invoke(w, messageHandler);
                                }
                            });

                        // if message handler throws an exception, dispose it
                        messageHandler.ExceptionThrown += (a, b) =>
                        {
                            messageHandler.Dispose();
                            reset.Set();
                        };

                        // handshake
                        ClientId = SecurityUtils.DoHandshake(messageHandler, PasswordHash, true, ClientId);

                        // wait a bit (to make sure message handler callbacks don't get early messages)
                        Task.Delay(100).Wait();

                        IsConnected = true;

                        Logger.Log("Connected to host");
                        Connected?.Invoke(ClientId);

                        // wait here until exception is thrown on message handler
                        reset.WaitOne();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Host connection error. " + ex.Message, Logger.LogSeverity.Debug);
                    }
                    finally
                    {
                        if (IsConnected)
                        {
                            Logger.Log("Disconnected from host");
                            Disconnected?.Invoke(ClientId);
                            IsConnected = false;
                        }

                        client.ProperlyClose();
                    }

                    Task.Delay(300).Wait();
                }

            }, token, TaskCreationOptions.LongRunning).Start();
        }
    }
}
