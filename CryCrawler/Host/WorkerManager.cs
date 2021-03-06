using System;
using System.IO;
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
using CryCrawler.Structures;
using System.Threading.Tasks;
using System.Collections.Generic;
using Timer = System.Timers.Timer;
using System.Security.Cryptography.X509Certificates;

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
        readonly PluginManager plugins;
        readonly HostConfiguration config;
        readonly X509Certificate2 certificate;
        CancellationTokenSource cancelSource;
        WorkerConfiguration toSendConfig = null;
        readonly SemaphoreSlim transferSemaphore = new SemaphoreSlim(1);
        public readonly List<WorkerClient> Clients = new List<WorkerClient>();

        public ConcurrentSlidingBuffer<DownloadedWork> RecentDownloads { get; }

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
        public WorkerManager(WorkManager manager, Configuration config, 
            IWorkerPicker workerPicker, PluginManager plugins = null)
        {
            // paramaters
            this.plugins = plugins;
            this.manager = manager;
            this.picker = workerPicker;
            this.config = config.HostConfig;
            this.WorkerConfig = config.WorkerConfig;

            var password = this.config.ListenerConfiguration.Password;
            var endpoint = new IPEndPoint(
                IPAddress.Parse(this.config.ListenerConfiguration.IP),
                this.config.ListenerConfiguration.Port);

            RecentDownloads = new ConcurrentSlidingBuffer<DownloadedWork>(config.WorkerConfig.MaxLoggedDownloads);

            UpdateWorkerConfigurations(WorkerConfig);

            // initialize everything
            listener = new TcpListener(endpoint);
            certificate = SecurityUtils.BuildSelfSignedCertificate("crycrawler");
            passwordHash = string.IsNullOrEmpty(password) ? null : SecurityUtils.GetHash(password);

            // prepare checking timer
            timer = new Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
            timer.Elapsed += OldClientCheck;

            // prepare worker timer
            workerTimer = new Timer(TimeSpan.FromSeconds(1).TotalMilliseconds);
            workerTimer.Elapsed += WorkerStatusCheck;

            // subscribe to events
            this.ClientRemoved += clientRemoved;
            this.ClientLeft += clientDisconnected;
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

        /// <summary>
        /// Client connected. Establish SSL, do handshake and validate client before accepting it.
        /// </summary>
        /// <param name="r"></param>
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

            if (plugins?.Invoke(p => p.OnClientConnecting(client), true) == false)
            {
                // reject client
                Logger.Log("Client rejected by plugin.", Logger.LogSeverity.Debug);
                client.ProperlyClose();
                return;
            }

            // Create client object
            var wc = new WorkerClient(client);

            // Start handshake
            try
            {
                var stream = client.GetStream();

                Logger.Log($"Establishing secure connection to {client.Client.RemoteEndPoint}...", Logger.LogSeverity.Debug);
                // setup SSL here
                var sslstream = SecurityUtils.ServerEstablishSSL(stream, certificate);

                wc.MesssageHandler = new NetworkMessageHandler<NetworkMessage>(sslstream, m => ClientMessageReceived(wc, m));
                wc.MesssageHandler.ExceptionThrown += (a, b) =>
                {
                    wc.MesssageHandler.Dispose();

                    if (wc.Online)
                    {
                        wc.Online = false;
                        ClientLeft?.Invoke(wc, null);

                        Logger.Log($"Client disconnected from {wc.RemoteEndpoint}! ({wc.Id})");

                        // ignore certain common errors
                        if (!IgnoreError(b)) Logger.Log(b.GetDetailedMessage(), Logger.LogSeverity.Debug);
                    }
                };

                Logger.Log($"Validating {client.Client.RemoteEndPoint}...", Logger.LogSeverity.Debug);

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

            plugins?.Invoke(p => p.OnClientConnect(wc.Client, wc.Id));

            // send configuration
            wc.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.ConfigUpdate, WorkerConfig));

            // send client limit
            wc.MesssageHandler.SendMessage(new NetworkMessage(
                NetworkMessageType.WorkLimitUpdate, config.ClientWorkLimit));

            ClientJoined?.Invoke(wc, ewc != null);
        }

        /// <summary>
        /// Handles messages received from clients
        /// </summary>
        void ClientMessageReceived(WorkerClient client, NetworkMessage message)
        {
            if (!client.HandshakeCompleted) return;

            // do not log status checks because they happen too frequently
            /*
            if (message.MessageType != NetworkMessageType.StatusCheck)
                Logger.Log($"Received message from '{client.Id}' -> {message.MessageType}", Logger.LogSeverity.Debug);
            */

            switch (message.MessageType)
            {
                case NetworkMessageType.StatusCheck:

                    #region Handle new Cient Status
                    var status = JsonConvert.DeserializeObject<JObject>((string)message.Data);

                    var isBusy = (bool?)status["IsBusy"];
                    var hostMode = (bool?)status["IsHost"];
                    var isActive = (bool?)status["IsActive"];
                    var workCount = (long?)status["WorkCount"];
                    var crawledCount = (long?)status["CrawledCount"];

                    // update client information
                    client.IsBusy = isBusy ?? client.IsBusy;
                    client.IsHost = hostMode ?? client.IsHost;
                    client.IsActive = isActive ?? client.IsActive;
                    client.WorkCount = workCount ?? client.WorkCount;
                    client.CrawledCount = crawledCount ?? client.CrawledCount;
                    #endregion

                    break;
                case NetworkMessageType.ResultsReady:
                    // worker has results ready. Send request to retrieve results.
                    client.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.SendResults));

                    break;

                case NetworkMessageType.Work:
                    // retrieve results from worker

                    #region Handle Received Works
                    // do not retrieve if work not assigned
                    if (client.AssignedUrl != null)
                    {
                        var works = (object[])message.Data;
                        Logger.Log($"({client.Id}) - Retrieved {works.Length} results...", Logger.LogSeverity.Debug);

                        var added = 0;

                        // only add to backlog if not yet crawled
                        foreach (var url in works)
                        {
                            var u = (string)url;

                            // do not add if already crawled
                            if (manager.IsUrlCrawled(u)) continue;

                            // do not add if already in backlog
                            if (manager.IsUrlInBacklog(u)) continue;

                            // add to backlog
                            manager.AddToBacklog(u);
                            added++;
                        }

                        Logger.Log($"({client.Id}) - Added {added} items to backlog", Logger.LogSeverity.Debug);

                        // unassign work
                        client.AssignedUrl = null;

                        // confirm that all results have been received
                        client.MesssageHandler.SendMessage(
                            new NetworkMessage(NetworkMessageType.ResultsReceived));
                    }
                    #endregion

                    break;

                case NetworkMessageType.CrawledWorks:
                    // retrieve crawled items from worker

                    #region Handle Receieved Crawled Works

                    // do not retrieve if work not assigned
                    if (client.AssignedUrl != null)
                    {
                        var works = (object[])message.Data;
                        Logger.Log($"({client.Id}) - Retrieved {works.Length} cached crawled items...", Logger.LogSeverity.Debug);

                        // only add to crawled if it doesn't exist yet
                        foreach (var url in works)
                        {
                            var u = (string)url;

                            // do not add if already crawled
                            // if (manager.IsUrlCrawled(u)) continue;
                            // Check is not necessary because database upserts it anyway (will update existing ones automatically)

                            manager.AddToCrawled(u);
                        }
                    }
                    #endregion

                    break;

                case NetworkMessageType.FileTransfer:
                    // client wants to initiate file transfer

                    #region Initiate File Transfer
                    // use semaphore for starting file transfer - we don't want multiple threads creating same file and accessing it
                    string destination_path = null;
                    string temp_path = null;

                    try
                    {
                        var transferInfo = ((Dictionary<object, object>)message.Data)
                            .Deserialize<FileTransferInfo>();

                        destination_path = TranslateWorkerFilePathToHost(transferInfo.Location, WorkerConfig.DontCreateSubfolders);

                        temp_path = Extensions.GetTempFile(ConfigManager.TemporaryFileTransferDirectory);

                        if (client.TransferringFile)
                        {
                            if (transferInfo.Url == client.TransferringUrl &&
                                transferInfo.Size == client.TransferringFileSize &&
                                transferInfo.Location == client.TransferringFileLocation)
                            {
                                // same file requested, ignore it
                                Logger.Log($"({client.Id}) - New file transfer same as existing one. Ignoring request. ({transferInfo.Location})", Logger.LogSeverity.Debug);
                                break;
                            }

                            // new file transfer is initiated, old one is canceled
                            Logger.Log($"({client.Id}) New file transfer ({transferInfo.Location}) canceled old one. " +
                                $"({client.TransferringFileLocation})", Logger.LogSeverity.Debug);

                            client.StopTransfer();
                        }

                        transferSemaphore.Wait();
                        // Logger.Log("Starting transfer...");

                        // start transferring file
                        client.TransferringFile = true;
                        client.TransferringFileSizeCompleted = 0;
                        client.TransferringUrl = transferInfo.Url;
                        client.TransferringFileSize = transferInfo.Size;
                        client.TransferringFileLocation = transferInfo.Location;
                        client.TransferringFileLocationHost = destination_path;

                        // create necessary directories and use proper location
                        Directory.CreateDirectory(Path.GetDirectoryName(destination_path));

                        // transfer file to temporary file first and later check MD5 hashes for duplicates
                        client.TransferringFileStream = new FileStream(temp_path,
                            FileMode.Create, FileAccess.ReadWrite);

                        // accept file transfer
                        client.MesssageHandler.SendMessage(
                            new NetworkMessage(NetworkMessageType.FileAccept));
                    }
                    catch (Exception ex)
                    {
                        client.StopTransfer();

                        // make sure file is deleted
                        if (temp_path != null && File.Exists(temp_path))
                        {
                            Logger.Log($"({client.Id}) Deleted canceled file due to file transfer exception.", Logger.LogSeverity.Debug);
                            File.Delete(temp_path);
                        }

                        Logger.Log($"({client.Id}) Failed to accept file! " + ex.GetDetailedMessage(), Logger.LogSeverity.Warning);
                    }
                    finally
                    {
                        transferSemaphore.Release();
                    }
                    #endregion

                    break;

                case NetworkMessageType.FileChunk:
                    // client sent a chunk of file

                    #region Accept File Chunk
                    if (client.TransferringFile == false)
                    {
                        // Logger.Log("Client is NOT transferring anything...");
                        // reject file transfer until previous file finishes transferring
                        client.MesssageHandler.SendMessage(
                            new NetworkMessage(NetworkMessageType.FileReject));
                    }
                    else
                    {
                        try
                        {
                            var chunk = ((Dictionary<object, object>)message.Data)
                                .Deserialize<FileChunk>();

                            // if location doesn't matches, reject it
                            if (client.TransferringFileLocation != chunk.Location)
                            {
                                client.MesssageHandler.SendMessage(
                                    new NetworkMessage(NetworkMessageType.FileReject));

                                throw new InvalidOperationException("Invalid file chunk received!");
                            }

                            // write to stream
                            client.TransferringFileStream.Write(chunk.Data, 0, chunk.Size);
                            client.TransferringFileSizeCompleted += chunk.Size;

                            // check if all chunks transferred
                            if (client.TransferringFileSize <= client.TransferringFileSizeCompleted)
                            {
                                try
                                {
                                    // close file stream as we don't need it anymore
                                    client.TransferringFileStream.Close();

                                    // attempt to copy file to destination while checking for duplicates
                                    var spath = Extensions.CopyToAndGetPath(
                                        client.TransferringFileStream.Name,
                                        client.TransferringFileLocationHost);

                                    // delete old temporary file
                                    File.Delete(client.TransferringFileStream.Name);

                                    // transfer completed
                                    Logger.Log($"({client.Id}) - File transferred ({Path.GetFileName(spath)}).",
                                        Logger.LogSeverity.Debug);

                                    // create work and upsert it to Crawled
                                    var w = new Work(client.TransferringUrl)
                                    {
                                        Transferred = false,
                                        IsDownloaded = true,
                                        DownloadLocation = Extensions.GetRelativeFilePath(spath, WorkerConfig)
                                    };

                                    manager.AddToCrawled(w);
                                    RecentDownloads.Add(new DownloadedWork(
                                        Path.Combine(WorkerConfig.DownloadsPath, w.DownloadLocation),
                                        client.TransferringFileSize));

                                    // mark as done
                                    client.TransferringFile = false;

                                    client.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.FileTransfer, new FileTransferInfo
                                    {
                                        Url = w.Url,
                                        Size = -1,
                                        Location = ""
                                    }));
                                }
                                catch
                                {
                                    // client.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.FileReject));
                                }
                                finally
                                {
                                    client.StopTransfer();
                                }
                            }

                            // host accepted file chunk
                            client.MesssageHandler.SendMessage(
                                 new NetworkMessage(NetworkMessageType.FileChunkAccept));
                        }
                        catch (Exception ex)
                        {
                            client.StopTransfer();

                            // make sure file is deleted if it exists!
                            if (client.TransferringFileLocationHost != null &&
                                File.Exists(client.TransferringFileLocationHost))
                            {
                                Logger.Log($"({client.Id}) - Deleted canceled file due to chunk transfer exception.", Logger.LogSeverity.Debug);
                                File.Delete(client.TransferringFileLocationHost);
                            }

                            if (!IgnoreError(ex))
                                Logger.Log($"({client.Id}) - Failed to transfer chunk! " + ex.GetDetailedMessage() + ex.StackTrace, Logger.LogSeverity.Debug);
                        }
                    }
                    #endregion

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
                if (!IgnoreError(ex)) Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);
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

                Urls = config.Urls,
                UserAgent = config.UserAgent, 
                AcceptAllFiles = config.AcceptAllFiles,
                DomainBlacklist = config.DomainBlacklist,
                DomainWhitelist = config.DomainWhitelist,
                CrawlDelaySeconds = config.CrawlDelaySeconds,
                AcceptedExtensions = config.AcceptedExtensions,
                AcceptedMediaTypes = config.AcceptedMediaTypes,
                URLMustMatchPattern = config.URLMustMatchPattern,
                DontCreateSubfolders = config.DontCreateSubfolders,
                ScanTargetsMediaTypes = config.ScanTargetsMediaTypes,
                BlacklistedURLPatterns = config.BlacklistedURLPatterns,
                MaximumAllowedFileSizekB = config.MaximumAllowedFileSizekB,
                MinimumAllowedFileSizekB = config.MinimumAllowedFileSizekB,
                FilenameMustContainEither = config.FilenameMustContainEither,
                RespectRobotsExclusionStandard = config.RespectRobotsExclusionStandard
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

        void clientRemoved(WorkerClient wc, object data) => unassignWorkFromClient(wc);
        void clientDisconnected(WorkerClient wc, object data)
        {
            plugins?.Invoke(x => x.OnClientDisconnect(wc.Client, wc.Id));

            unassignWorkFromClient(wc);
        }

        /// <summary>
        /// Unassigns work from client and stops any ongoing file transfers.
        /// Unassigned works are added back to the backlog.
        /// </summary>
        void unassignWorkFromClient(WorkerClient wc)
        {
            // take assigned work from client, remove it, add back to backlog
            var w = wc.AssignedUrl;
            wc.AssignedUrl = null;

            // stop any transfer that might be going on
            wc.StopTransfer();

            if (w != null) manager.AddToBacklog(w);
        }

        /// <summary>
        /// Translates provided remote file path (retrieved from worker) to absolute local file path
        /// </summary>
        /// <param name="workerPath">Worker provided relative path to file (does not include the downloads folder)</param>
        /// <param name="dontCreateSubfolders">If true, any subfolders in provided path will be removed</param>
        /// <returns></returns>
        public string TranslateWorkerFilePathToHost(string workerPath, bool dontCreateSubfolders = false)
        {
            // WorkerPath must be relative without the "Downloads" folder
            if (dontCreateSubfolders && string.IsNullOrEmpty(workerPath) == false)
            {
                // file path contains 1 or more subfolders - remove them if "DontCreateSubfolders" is true
                workerPath = Path.GetFileName(workerPath);
            }

            var path = Path.Combine(Directory.GetCurrentDirectory(), WorkerConfig.DownloadsPath, workerPath);

            return path;
        }

        /// <summary>
        /// Main WorkerManager function. Used to get work from WorkManager and assign it to workers.
        /// </summary>
        async void Work()
        {
            string failedUrl = null, url = null;
            while (!cancelSource.IsCancellationRequested)
            {
                // CHECK IF WORKERS ARE AVAILABLE
                if (Clients == null || picker.Pick(Clients) == null)
                {
                    // no workers to give work to
                    await Task.Delay(200);
                    continue;
                }

                #region Get valid work
                // VALIDATE URL
                // if there is a failed url, use it again
                if (string.IsNullOrEmpty(failedUrl))
                {
                    if (!manager.IsWorkAvailable || manager.GetWork(out Work w) == false)
                    {
                        // unable to get work, wait a bit and try again
                        await Task.Delay(20);
                        continue;
                    }

                    url = w.Url;
                }
                else url = failedUrl;

                // check if url is whitelisted
                if (Extensions.IsUrlWhitelisted(url, WorkerConfig) == false) continue;
                #endregion

                // PICK WORKER AND ASSIGN WORK
                try
                {
                    if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("Invalid Url!");

                    // pick client without work
                    var c = picker.Pick(Clients);
                    if (c == null) throw new NullReferenceException("No worker picked!");

                    // do something with work
                    Logger.Log($"({c.Id}) - Assigning work - {url}");

                    // send it work
                    c.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Work, url));
                    c.AssignedUrl = url;
                    failedUrl = null;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to assign work! " + ex.Message, Logger.LogSeverity.Warning);
                    failedUrl = url;
                }

                // wait a bit
                await Task.Delay(100);
            }
        }

        /// <summary>
        /// Used to send status and file checks to clients 
        /// </summary>
        void WorkerStatusCheck(object sender, ElapsedEventArgs e)
        {
            foreach (var c in Clients.Where(x => x.Online))
            {
                // send status check request
                Task.Run(() =>
                    c.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.StatusCheck)));

                // send file check request
                if (c.TransferringFile == false)
                    Task.Run(() => c.MesssageHandler.SendMessage(new NetworkMessage(NetworkMessageType.FileCheck)));
            }
        }

        /// <summary>
        /// Checks registered clients and removes old clients from list
        /// </summary>
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

        /// <summary>
        /// Returns true if the exception should be ignored. Used to hide common exceptions.
        /// </summary>
        bool IgnoreError(Exception ex)
        {
            if (
                (ex.Message.Contains("Cannot access a disposed object") && (ex.Message.Contains("NetworkStream") || ex.Message.Contains("Object disposed!"))) ||
                ex.Message.Contains("interrupted by a call to WSACancelBlockingCall") ||
                ex.Message.Contains("An existing connection was forcibly closed by the remote host"))
                return true;

            return false;
        }

        public class WorkerClient
        {
            // worker status and information
            public string Id;
            public bool IsHost;     // client is a host
            public bool Online;     // client is connected
            public bool IsBusy;     // client trying to find valid work
            public bool IsActive;   // client crawler is active
            public long WorkCount;
            public long CrawledCount;
            public string AssignedUrl;

            // file transfer variables
            public bool TransferringFile;
            public string TransferringUrl;
            public long TransferringFileSize;
            public string TransferringFileLocation;
            public FileStream TransferringFileStream;
            public long TransferringFileSizeCompleted;
            public string TransferringFileLocationHost;

            // client technical variables
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

            public void StopTransfer()
            {
                var trf = TransferringFile;
                var size = TransferringFileSize;
                var sizec = TransferringFileSizeCompleted;
                string path = TransferringFileStream?.Name;

                try
                {
                    TransferringUrl = null;
                    TransferringFile = false;
                    TransferringFileLocation = null;
                    TransferringFileSize = 0;
                    TransferringFileSizeCompleted = 0;

                    TransferringFileStream?.Close();
                    TransferringFileStream?.Dispose();

                    // check if file was interrupted and not deleted
                    if (size > 0 &&
                        sizec < size &&
                        File.Exists(path) &&
                        (new FileInfo(path).Length == 0 || trf))
                    {
                        // delete file
                        File.Delete(path);
                        Logger.Log("Interrupted file deleted.", Logger.LogSeverity.Debug);
                    }
                }
                catch
                {

                }
            }
        }
    }
}
