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
                listener.BeginAcceptTcpClient(ClientAccepted, null);
                IsListening = true;

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
            listener.BeginAcceptTcpClient(ClientAccepted, null);

            // Start accepting client
            var client = listener.EndAcceptTcpClient(r);
            Logger.Log($"Client connecting from {client.Client.RemoteEndPoint}...");

            // Create client object
            var wc = new WorkerClient() { Client = client };
            
            // Start handshake
            try
            {
                wc.MesssageHandler = new NetworkMessageHandler<NetworkMessage>(client.GetStream(), m => ClientMessageReceived(wc, m));

                // wait for JOIN request
                var response = wc.MesssageHandler.WaitForResponse().Result;
                if (response.MessageType != NetworkMessageType.Join) throw new InvalidOperationException($"Invalid client response! Got '{response.MessageType}', expected '{NetworkMessageType.Join}'");

                // check password
                var password = response.Data as string;
                if (passwordHash != SecurityUtils.GetHash(password)) throw new InvalidOperationException("Invalid password!");

                // send ACCEPT 
                wc.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Accept));
           
                // wait for OK
                response = wc.MesssageHandler.WaitForResponse().Result;
                if (response.MessageType != NetworkMessageType.OK) throw new InvalidOperationException($"Invalid client response! Got '{response.MessageType}', expected '{NetworkMessageType.OK}'");

                wc.HandshakeCompleted = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Client from {client.Client.RemoteEndPoint} rejected.", Logger.LogSeverity.Warning);
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);

                wc.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Reject));
                client.Dispose();
                return;
            }

            // Accept valid client
            Logger.Log($"Client from {client.Client.RemoteEndPoint} accepted.");
            clients.Add(wc);        
        }

        void ClientMessageReceived(WorkerClient client, NetworkMessage message)
        {
            if (!client.HandshakeCompleted) return;

            Logger.Log($"Received message from {client.Client.Client.RemoteEndPoint} -> {message.MessageType}", Logger.LogSeverity.Debug);

            // TODO: add message handling
        }

        class WorkerClient
        {
            public TcpClient Client;
            public bool HandshakeCompleted;
            public NetworkMessageHandler<NetworkMessage> MesssageHandler;
        }
    }
}
