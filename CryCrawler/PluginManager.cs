using System;
using System.Collections.Generic;
using System.Text;

namespace CryCrawler
{
    public class PluginManager
    {
        public readonly string PluginDirectory;
        public PluginManager(string pluginDirectory)
        {
            PluginDirectory = pluginDirectory;
        }

        /// <summary>
        /// Attempts to load all plugins in the plugins' folder.
        /// </summary>
        /// <returns>Returns true if all plugins were loaded successfully</returns>
        public bool Load()
        {
            return true;
        }

        /// <summary>
        /// Calls dispose on all loaded plugins
        /// </summary>
        public void Dispose()
        {

        }

        // Plugins should have following TYPES of functions:
        // - Middleware functions (no limit on how many, will be called based on plugin load order)
        // - Override functions (only one will be loaded from plugins - this function overrides a whole function)

        // Plugins are classes that inherit from Plugin class

        // Plugins must have the following functions defined:
        // - Constructor
        // - void Dispose()

        // Supported functions:
        // - [Middleware] void OnDump()
        // - [Middleware] void OnClientConnect(TcpClient client)                -> When client connects to host
        // - [Middleware] bool OnClientConnecting(TcpClient client)             -> Used for accepting or denying clients on host
        // - [Middleware] void OnClientDisconnect(TcpClient client)             -> When client disconnects from host
        // - [Middleware] void OnDisconnect()                                   -> When we disconnect from host
        // - [Middleware] void OnConnect()                                      -> When we connect to host
        // - [Override]   IEnumerable<string> FindUrls(string content)          -> Used for returning next URLs to crawl based on content
        // - [Middleware] bool BeforeDownload(string url, string detination)    -> When file is about to be downloaded, this can accept or deny it
        // - [Middleware] void AfterDownload(string url, string desitnation)    -> After file is downloaded
        // - [Middleware] bool OnWorkReceived(string url)                       -> Crawler or Worker Manager get's next work to crawl/assign. This can accept or deny it.

        // Can add more later...
    }

    public class Plugin
    {

    }
}
