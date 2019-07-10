using CryCrawler.Network;
using CryCrawler.Security;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CryCrawler.Host
{
    public class WorkerManager
    {
        public int ClientCount => clients.Count;
        public bool IsListening { get; private set; }

        readonly string passwordHash;
        readonly TcpListener listener;
        readonly List<WorkerClient> clients = new List<WorkerClient>();

        public event ClientHandler ClientJoined;
        public event ClientHandler ClientLeft;
        public delegate void ClientHandler(WorkerClient wc, object data);

        public WorkerManager(IPEndPoint endpoint, string password = null)
        {
            listener = new TcpListener(endpoint);
            passwordHash = string.IsNullOrEmpty(password) ? null : SecurityUtils.GetHash(password);
        }

        public void StartListening()
        {
            try
            {
                listener.Start();
                IsListening = true;

                listener.BeginAcceptTcpClient(ClientAccepted, null);

                Logger.Log($"Listening on {listener.LocalEndpoint}");
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to start listening!", Logger.LogSeverity.Error);
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);
            }
        }

        void ClientAccepted(IAsyncResult r)
        {
            // Continue listening
            try
            {
                listener.BeginAcceptTcpClient(ClientAccepted, null);
            }
            catch (Exception ex)
            {
                if (IsListening) Logger.Log($"Failed to continue listening... " + ex.GetDetailedMessage(), Logger.LogSeverity.Debug);
                return;
            }

            // Start accepting client
            var client = listener.EndAcceptTcpClient(r);
            Logger.Log($"Client connecting from {client.Client.RemoteEndPoint}...", Logger.LogSeverity.Debug);

            // Create client object
            var wc = new WorkerClient(client);

            // Start handshake
            try
            {
                wc.MesssageHandler = new NetworkMessageHandler<NetworkMessage>(client.GetStream(), m => ClientMessageReceived(wc, m));
                wc.MesssageHandler.ExceptionThrown += (a, b) =>
                {
                    wc.MesssageHandler.Dispose();

                    if (wc.Online)
                    {
                        wc.Online = false;
                        ClientLeft?.Invoke(wc, null);

                        Logger.Log($"Client disconnected from {wc.RemoteEndpoint}");
                    }
                };

                wc.Id = SecurityUtils.DoHandshake(wc.MesssageHandler, passwordHash, false, null,
                    id => clients.Count(x => x.Id == id) > 0,
                    id =>
                    {
                        var c = clients.Where(x => x.Id == id).FirstOrDefault();
                        if (c == null) return false; // generate new Id if client doesn't exist
                        if (c.Online == true) return false; // worker already online with this Id, generate new Id

                        // worker with this Id is offline, assume this is that worker
                        return true;
                    });

                wc.HandshakeCompleted = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Rejected client from {client.Client.RemoteEndPoint}. " + ex.Message, Logger.LogSeverity.Warning);
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);

                try
                {
                    wc.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Reject));
                }
                catch { }

                client.ProperlyClose();
                return;
            }

            wc.Online = true;
            wc.LastConnected = DateTime.Now;

            // try get existing client
            var ewc = clients.Where(x => x.Id == wc.Id).FirstOrDefault();

            // Accept valid client
            Logger.Log($"Accepted {(ewc == null ? "new" : "existing")} client from {client.Client.RemoteEndPoint}");
            lock (clients)
            {
                // if client doesn't exist yet, add it - otherwise replace existing client
                if (ewc == null) clients.Add(wc);        
                else
                {
                    var index = clients.IndexOf(ewc);

                    clients[index] = wc;
                }

                // also do a sweep of old clients
                var now = DateTime.Now;
                for (int i = 0; i < clients.Count; i++)
                {
                    var c = clients[i];
                    if (c.Online) continue;

                    // remove inactive clients older than 1 day
                    if (now.Subtract(c.LastConnected).TotalDays > 1)
                    {
                        clients.RemoveAt(i);
                        i--;
                    }
                }
            }

            ClientJoined?.Invoke(wc, ewc != null);
        }

        void ClientMessageReceived(WorkerClient client, NetworkMessage message)
        {
            if (!client.HandshakeCompleted) return;

            Logger.Log($"Received message from {client.Client.Client.RemoteEndPoint} -> {message.MessageType}", Logger.LogSeverity.Debug);

            // TODO: add message handling
        }

        public void Stop()
        {
            // cleanup
            try
            {
                listener.Stop();
                IsListening = false;
                Logger.Log("Host listener stopped.", Logger.LogSeverity.Debug);

                // notify all connected clients of disconnect
                foreach (var cl in clients)
                {
                    try
                    {
                        cl.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Disconnect));
                    }
                    finally
                    {
                        cl.Client.ProperlyClose();
                    }
                }

                if (clients.Count > 0) Logger.Log("Sent disconnect to all connected clients", Logger.LogSeverity.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);
            }
        }

        public class WorkerClient
        {
            public string Id;
            public bool Online;
            public TcpClient Client;
            public DateTime LastConnected;
            public EndPoint RemoteEndpoint;
            public bool HandshakeCompleted;
            public NetworkMessageHandler<NetworkMessage> MesssageHandler;

            public WorkerClient(TcpClient client)
            {
                Client = client;
                RemoteEndpoint = client.Client.RemoteEndPoint;
            }
        }
    }
}
