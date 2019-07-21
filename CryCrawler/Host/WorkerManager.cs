﻿using System;
using System.Net;
using System.Linq;
using System.Timers;
using Newtonsoft.Json;
using System.Threading;
using CryCrawler.Worker;
using System.Net.Sockets;
using CryCrawler.Network;
using CryCrawler.Security;
using Newtonsoft.Json.Linq;
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
        public WorkerConfiguration WorkerConfig { get; set; }

        readonly Timer timer;
        readonly Timer workerTimer;
        readonly WorkManager manager;
        readonly string passwordHash;
        readonly IWorkerPicker picker;
        readonly TcpListener listener;
        readonly HostConfiguration config;
        CancellationTokenSource cancelSource;
        WorkerConfiguration toSendConfig = null;
        public readonly List<WorkerClient> Clients = new List<WorkerClient>();

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
        public WorkerManager(WorkManager manager, Configuration config, IWorkerPicker workerPicker)
        {
            // paramaters
            this.manager = manager;
            this.picker = workerPicker;
            this.config = config.HostConfig;
            this.WorkerConfig = config.WorkerConfig;
            var password = this.config.ListenerConfiguration.Password;
            var endpoint = new IPEndPoint(
                IPAddress.Parse(this.config.ListenerConfiguration.IP),
                this.config.ListenerConfiguration.Port);

            UpdateWorkerConfigurations(WorkerConfig);

            // initialize everything
            listener = new TcpListener(endpoint);

            passwordHash = string.IsNullOrEmpty(password) ? null : SecurityUtils.GetHash(password);

            // prepare checking timer
            timer = new Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
            timer.Elapsed += OldClientCheck;

            // prepare worker timer
            workerTimer = new Timer(TimeSpan.FromSeconds(2).TotalMilliseconds);
            workerTimer.Elapsed += WorkerStatusCheck;

            // subscribe to events
            this.ClientRemoved += clientRemoved;
        }

        public void Start()
        {
            try
            {
                // start timer for age checking
                timer.Start();

                // start timer for checking workers
                workerTimer.Start();

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

                        Logger.Log($"Client disconnected from {wc.RemoteEndpoint}! ");
                        Logger.Log(b.GetDetailedMessage(), Logger.LogSeverity.Debug);
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
                Logger.Log($"Rejected client from {wc.RemoteEndpoint}. " + ex.Message, Logger.LogSeverity.Warning);
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
            Logger.Log($"Accepted {(ewc == null ? "new" : "existing")} client from {client.Client.RemoteEndPoint}. ({wc.Id})");
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

            // send configuration
            wc.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.ConfigUpdate, WorkerConfig));

            // send client limit
            wc.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.WorkLimitUpdate, config.ClientWorkLimit));

            ClientJoined?.Invoke(wc, ewc != null);
        }

        void ClientMessageReceived(WorkerClient client, NetworkMessage message)
        {
            if (!client.HandshakeCompleted) return;

            // do not log status checks because they happen too frequently
            if (message.MessageType != NetworkMessageType.StatusCheck)
                Logger.Log($"Received message from '{client.Id}' -> {message.MessageType}", Logger.LogSeverity.Debug);

            // TODO: improve message handling

            switch (message.MessageType)
            {
                case NetworkMessageType.StatusCheck:

                    var status = JsonConvert.DeserializeObject<JObject>((string)message.Data);

                    var hostMode = (bool?)status["IsHost"];
                    var isActive = (bool?)status["IsActive"];
                    var workCount = (long?)status["WorkCount"];
                    var crawledCount = (long?)status["CrawledCount"];

                    // update client information
                    client.IsHost = hostMode ?? client.IsHost;
                    client.IsActive = isActive ?? client.IsActive;
                    client.WorkCount = workCount ?? client.WorkCount;
                    client.CrawledCount = crawledCount ?? client.CrawledCount;

                    break;
                case NetworkMessageType.ResultsReady:
                    // worker has results ready. Send request to retrieve results.
                    client.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.SendResults));
                    break;
                case NetworkMessageType.Work:
                    // retrieve results from worker
                    var work = (List<Work>)message.Data;
                    Logger.Log($"Retrieved {work.Count} results from client '{client.Id}'");
                    
                    // only add to backlog if not yet crawled
                    foreach (var w in work)
                        if (manager.IsUrlCrawled(w.Url) == false)
                            manager.AddToBacklog(w);

                    // confirm that all results have been received
                    client.MesssageHandler.SendMessage(
                        new NetworkMessage(NetworkMessageType.ResultsReceived));
                    break;
            }
        }

        public void Stop()
        {
            // cleanup
            try
            {
                timer.Stop();
                workerTimer.Stop();
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

        /// <summary>
        /// Creates copy of our worker configuration without sensitive data and sends it to all online clients.
        /// </summary>
        public void UpdateWorkerConfigurations(WorkerConfiguration config)
        {
            // make new config object and remove sensitive information
            toSendConfig = new WorkerConfiguration()
            {
                HostEndpoint = null,
                AcceptAllFiles = config.AcceptAllFiles,
                AcceptedExtensions = config.AcceptedExtensions,
                AcceptedMediaTypes = config.AcceptedMediaTypes,
                MaximumAllowedFileSizekB = config.MaximumAllowedFileSizekB,
                MinimumAllowedFileSizekB = config.MinimumAllowedFileSizekB,
                DomainBlacklist = config.DomainBlacklist,
                DomainWhitelist = config.DomainWhitelist,
                ScanTargetsMediaTypes = config.ScanTargetsMediaTypes,
                Urls = config.Urls
            };

            foreach (var c in Clients.Where(x => x.Online))
            {
                try
                {

                    c.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.ConfigUpdate, toSendConfig));
                }
                catch
                {

                }
            }
        }

        void clientRemoved(WorkerClient wc, object data)
        {
            // take assigned work from client, remove it, add back to backlog
            var w = wc.AssignedUrl;
            wc.AssignedUrl = null;

            manager.AddToBacklog(w);
        }

        async void Work()
        {
            string failedUrl = null, url = null;
            while (!cancelSource.IsCancellationRequested)
            {
                // CHECK IF WORKERS ARE AVAILABLE
                if (Clients == null || picker.Pick(Clients) == null)
                {
                    // no workers to give work to
                    await Task.Delay(1000);
                    continue;
                }

                // VALIDATE URL
                // if there is a failed url, use it again
                if (string.IsNullOrEmpty(failedUrl))
                {
                    if (!manager.IsWorkAvailable || manager.GetWork(out Work w) == false)
                    {
                        // unable to get work, wait a bit and try again
                        await Task.Delay(100);
                        continue;
                    }

                    if (w.IsEligibleForCrawl()) url = w.Url;
                    else manager.AddToBacklog(w);
                }
                else url = failedUrl;

                // PICK WORKER AND ASSIGN WORK
                try
                {
                    if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("Invalid Url!");

                    // do something with work
                    Logger.Log($"Sending work to client... '{url}'");

                    // pick client without work
                    var c = picker.Pick(Clients);
                    if (c == null) throw new NullReferenceException("No worker picked!");

                    // send it work
                    c.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Work, url));

                    c.AssignedUrl = url;

                    failedUrl = null;
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to assign work! " + ex.Message, Logger.LogSeverity.Warning);
                    failedUrl = url;
                }

                // wait a bit
                await Task.Delay(100);
            }
        }

        void WorkerStatusCheck(object sender, ElapsedEventArgs e)
        {
            foreach (var c in Clients.Where(x => x.Online))
                Task.Run(() => c.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.StatusCheck)));
        }

        void OldClientCheck(object sender, ElapsedEventArgs e)
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

        public class WorkerClient
        {
            public string Id;
            public bool IsHost;
            public bool Online;
            public bool IsActive;
            public long WorkCount;
            public long CrawledCount;
            public string AssignedUrl;

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
