using CryCrawler.Network;
using CryCrawler.Security;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        readonly ConcurrentBag<WorkerClient> clients = new ConcurrentBag<WorkerClient>();


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
            Logger.Log($"Client connecting from {client.Client.RemoteEndPoint}...");

            // Create client object
            var wc = new WorkerClient() { Client = client };

            // Start handshake
            try
            {
                wc.MesssageHandler = new NetworkMessageHandler<NetworkMessage>(client.GetStream(), m => ClientMessageReceived(wc, m));

                SecurityUtils.DoHandshake(wc.MesssageHandler, passwordHash);

                wc.HandshakeCompleted = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Rejected client from {client.Client.RemoteEndPoint}", Logger.LogSeverity.Warning);
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);

                wc.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Reject));
                client.ProperlyClose();
                return;
            }

            // Accept valid client
            Logger.Log($"Accepted client from {client.Client.RemoteEndPoint}");
            clients.Add(wc);
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

                if (clients.Count > 0) Logger.Log("All connected clients disconnected.", Logger.LogSeverity.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);
            }
        }

        class WorkerClient
        {
            public TcpClient Client;
            public bool HandshakeCompleted;
            public NetworkMessageHandler<NetworkMessage> MesssageHandler;
        }
    }
}
