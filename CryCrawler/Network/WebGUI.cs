﻿using System;
using System.Net;
using System.Text;
using System.Net.Sockets;

namespace CryCrawler.Network
{
    public class WebGUI
    {
        public bool IsListening { get; private set; }

        const string serverName = "CryCrawler WebGUI";
        const int bufferSize = 1024 * 10;
        readonly TcpListener listener;
        readonly WebGUIResponder responder;

        public WebGUI(IPEndPoint endpoint, WebGUIResponder resp)
        {
            listener = new TcpListener(endpoint);
            responder = resp;
        }

        public void Start()
        {
            try
            {
                listener.Start();
                listener.BeginAcceptTcpClient(ClientAccepted, null);
                IsListening = true;

                Logger.Log($"WebGUI listening on {listener.LocalEndpoint}");
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
            // Logger.Log($"Client connected to WebGUI from {client.Client.RemoteEndPoint}", Logger.LogSeverity.Debug);

            // Wait for request
            var state = new ClientState(client);
            state.Buffer = new byte[bufferSize];
            client.Client.BeginReceive(state.Buffer, 0, bufferSize, SocketFlags.None, ClientMessageReceived, state);
        }

        void ClientMessageReceived(IAsyncResult r)
        {
            var state = (ClientState)r.AsyncState;

            try
            {
                var read = state.Client.Client.EndReceive(r);
                if (read == 0) return;

                // Get request
                var request = Encoding.UTF8.GetString(state.Buffer, 0, read).Replace("\r","");  // TODO: find more elegant way to parse request

                // Parse request
                var (method, url, body) = HttpUtils.ParseRequest(request);

                // Create response
                var raw_response = responder.GetResponse(method, url, body, out string ctype);
                var status = raw_response == null ? "HTTP/1.1 404 Not Found" : "HTTP/1.1 200 OK";       

                // Inject headers (very simplified, might need to improve later)
                var headers = $"{status}\n" +
                    $"Server: {serverName}\n" +
                    $"Content-Type: {ctype}; charset=utf-8\n" +
                    $"Cache-Control: no-store\n";

                var response = headers + "\n\n" + (raw_response ?? "");

                // Send response back
                state.Client.Client.Send(Encoding.UTF8.GetBytes(response));

                // Wait for more requests...
                // state.Client.Client.BeginReceive(state.Buffer, 0, bufferSize, SocketFlags.None, ClientMessageReceived, state);

                // Disconnect
                state.Client.ProperlyClose();
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to process request from WebGUI connected client.", Logger.LogSeverity.Error);
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);
            }
        }

        public void Stop()
        {
            try
            {
                listener.Stop();
                IsListening = false;
                Logger.Log("WebGUI listener stopped.", Logger.LogSeverity.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);
            }
        }

        class ClientState
        {
            public TcpClient Client;
            public byte[] Buffer;

            public ClientState(TcpClient client) => Client = client;
        }
    }
}
