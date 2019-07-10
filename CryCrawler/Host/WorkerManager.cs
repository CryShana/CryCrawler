﻿using System;
using System.Net;
using System.Linq;
using System.Timers;
using System.Threading;
using CryCrawler.Worker;
using System.Net.Sockets;
using CryCrawler.Network;
using CryCrawler.Security;
using System.Threading.Tasks;
using System.Collections.Generic;
using Timer = System.Timers.Timer;
using System.Collections.Concurrent;


namespace CryCrawler.Host
{
    public class WorkerManager
    {
        public int ClientCount => Clients.Count;
        public bool IsListening { get; private set; }

        readonly Timer timer;
        readonly WorkManager manager;
        readonly string passwordHash;
        readonly TcpListener listener;
        readonly HostConfiguration config;
        CancellationTokenSource cancelSource;
        public readonly List<WorkerClient> Clients = new List<WorkerClient>();
        public readonly ConcurrentDictionary<string, string> ClientWork = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Called when client gets disconnected.
        /// </summary>
        public event ClientHandler ClientLeft;
        /// <summary>
        /// Called when new or existing client joins. If second parameter is true, existing client reconnected.
        /// </summary>
        public event ClientHandler ClientJoined;
        /// <summary>
        /// Called when an existing inactive client exceeds max age threshold and gets removed.
        /// </summary>
        public event ClientHandler ClientRemoved;
        public delegate void ClientHandler(WorkerClient wc, object data);

        /// <summary>
        /// Starts a TCP listener for clients. Uses WorkManager to get URLs to distribute among clients.
        /// </summary>
        public WorkerManager(WorkManager manager, HostConfiguration config)
        {
            // paramaters
            this.config = config;
            this.manager = manager;
            var password = config.ListenerConfiguration.Password;
            var endpoint = new IPEndPoint(IPAddress.Parse(config.ListenerConfiguration.IP), config.ListenerConfiguration.Port);

            // initialize everything
            listener = new TcpListener(endpoint);

            passwordHash = string.IsNullOrEmpty(password) ? null : SecurityUtils.GetHash(password);

            // prepare checking timer
            timer = new Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
            timer.Elapsed += OldClientCheck;

            // subscribe to events
            this.ClientRemoved += clientRemoved;
        }

        private void OldClientCheck(object sender, ElapsedEventArgs e)
        {
            if (Clients == null) return;

            lock (Clients)
            {
                // do a sweep of old clients
                var now = DateTime.Now;
                for (int i = 0; i < Clients.Count; i++)
                {
                    var c = Clients[i];
                    if (c.Online) continue;

                    // remove inactive clients older than 1 day
                    if (now.Subtract(c.LastConnected).TotalMinutes > config.MaxClientAgeMinutes)
                    {
                        Clients.RemoveAt(i);
                        ClientRemoved?.Invoke(c, null);

                        i--;
                    }
                }
            }
        }

        public void Start()
        {
            try
            {
                // start timer for age checking
                timer.Start();

                // initialize new cancellation source
                cancelSource = new CancellationTokenSource();

                // start new task for work
                new Task(Work, cancelSource.Token, TaskCreationOptions.LongRunning).Start();

                // start listener
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
                    id => Clients.Count(x => x.Id == id) > 0,
                    id =>
                    {
                        var c = Clients.Where(x => x.Id == id).FirstOrDefault();
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
            var ewc = Clients.Where(x => x.Id == wc.Id).FirstOrDefault();

            // Accept valid client
            Logger.Log($"Accepted {(ewc == null ? "new" : "existing")} client from {client.Client.RemoteEndPoint}");
            lock (Clients)
            {
                // if client doesn't exist yet, add it - otherwise replace existing client
                if (ewc == null) Clients.Add(wc);        
                else
                {
                    var index = Clients.IndexOf(ewc);

                    Clients[index] = wc;
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
                timer.Stop();

                cancelSource.Cancel();

                listener.Stop();
                IsListening = false;
                Logger.Log("Host listener stopped.", Logger.LogSeverity.Debug);

                // notify all connected clients of disconnect
                foreach (var cl in Clients)
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

                if (Clients.Count > 0) Logger.Log("Sent disconnect to all connected clients", Logger.LogSeverity.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);
            }
        }

        void clientRemoved(WorkerClient wc, object data)
        {
            // TODO: take work from thata client id, remove it, add back to backlog
        }

        async void Work()
        {
            string failedUrl = null, url = null;
            while (!cancelSource.IsCancellationRequested)
            {
                if (Clients == null || Clients.Count(x => x.Online) == 0)
                {
                    // no clients to give work to
                    await Task.Delay(1000);
                    continue;
                }

                // if there is a failed url, use it again
                if (string.IsNullOrEmpty(failedUrl))
                {
                    if (!manager.IsWorkAvailable || manager.GetWork(out url) == false)
                    {
                        // unable to get work, wait a bit and try again
                        await Task.Delay(100);
                        continue;
                    }
                }
                else url = failedUrl;

                try
                {
                    // do something with work
                    Logger.Log("Sending work to client... " + url);

                    // for now pick first client (TODO: picking algorithm -- probably by how busy they are - status reporting)
                    var c = Clients.Where(x => x.Online).FirstOrDefault();
                    c?.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Work, url));

                    failedUrl = null;
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to distribute work! " + ex.Message + "\n" + ex.StackTrace, Logger.LogSeverity.Warning);
                    failedUrl = url;                   
                }

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
