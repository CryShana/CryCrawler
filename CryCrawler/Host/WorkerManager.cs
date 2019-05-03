using CryCrawler.Network;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CryCrawler.Host
{
    public class WorkerManager
    {
        public int ClientCount => clients.Count;
        public bool IsListening { get; private set; }

        readonly TcpListener listener;
        readonly ConcurrentBag<WorkerClient> clients = new ConcurrentBag<WorkerClient>();


        public WorkerManager(IPEndPoint endpoint)
        {
            listener = new TcpListener(endpoint);
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
            Logger.Log($"Client connected from {client.Client.RemoteEndPoint}. Handshake started.");

            // Start handshake
            // TODO: implement hadnshake / client validation / password check

            // Accept valid client
            Logger.Log($"Client from {client.Client.RemoteEndPoint} accepted.");
            var wc = new WorkerClient() { Client = client };          
            wc.MesssageHandler = new NetworkMessageHandler<NetworkMessage>(client.GetStream(), m => ClientMessageReceived(wc, m));

            clients.Add(wc);        
        }

        void ClientMessageReceived(WorkerClient client, NetworkMessage message)
        {
            Logger.Log($"Received message from {client.Client.Client.RemoteEndPoint}");

            // TODO: add message handling
        }

        class WorkerClient
        {
            public TcpClient Client;
            public NetworkMessageHandler<NetworkMessage> MesssageHandler;
        }
    }
}
