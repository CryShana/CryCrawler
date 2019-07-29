using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace CryCrawler
{
    public class PluginManager
    {
        public List<PluginInfo> Plugins { get; } = new List<PluginInfo>();

        readonly Configuration config;

        public PluginManager(Configuration config)
        {
            this.config = config;
        }

        /// <summary>
        /// Attempts to load all plugins in the plugins' folder.
        /// </summary>
        public bool Load()
        {
            // check if directory even exists
            if (Directory.Exists(config.PluginsDirectory) == false)
            {
                CreatePluginTemplate();

                return false;
            }

            bool compiled = false;

            // get all files with correct extension
            var files = Directory.GetFiles(config.PluginsDirectory, "*.cs");

            // attempt to load each
            foreach (var f in files)
            {
                // for now use filename as plugin name
                var pluginName = Path.GetFileNameWithoutExtension(f);

                try
                {
                    var wasCompiled = CompilePluginIfNecessary(pluginName);
                    if (wasCompiled && !compiled) compiled = wasCompiled;

                    var plugin = LoadPlugin(pluginName);
                    if (plugin == null) throw new NullReferenceException("Unknown error while trying to load plugin.");

                    Plugins.Add(new PluginInfo(plugin));

                    var pname = plugin.GetType().Name;

                    // log differently if plugin names are different
                    if (pname != pluginName) Logger.Log($"Loaded plugin '{plugin.GetType().Name}' ('{pluginName}')");
                    else Logger.Log($"Loaded plugin '{plugin.GetType().Name}'");

                    // run Info()
                    plugin.Info();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to load plugin '{pluginName}' - {ex.Message}",
                        Logger.LogSeverity.Warning);
                }
            }

            return compiled;
        }

        /// <summary>
        /// If compiled DLL does not exist or source code is different, compile the source code. 
        /// Returns true if code was compiled, and false if compilation was skipped.
        /// </summary>
        /// <param name="filename">Filename without directory names and without extension</param>
        bool CompilePluginIfNecessary(string filename)
        {
            var codePath = Path.Combine(config.PluginsDirectory, filename + ".cs");
            var compPath = Path.Combine(config.PluginsDirectory, filename + ".dll");

            var codeHash = Extensions.GetHash(codePath);

            // check if compiled assembly for this source code already exists
            if (File.Exists(compPath))
            {
                // check if current source code hash is the same to compiled source code hash, then we can skip compilation
                if (config.CompiledPlugins.TryGetValue(filename, out string hash) && codeHash == hash)
                {
                    // skip compilation
                    return false;
                }
            }

            CompilePlugin(filename);

            if (config.CompiledPlugins.ContainsKey(filename)) config.CompiledPlugins[filename] = codeHash;
            else config.CompiledPlugins.Add(filename, codeHash);

            return true;
        }

        /// <summary>
        /// Compiles plugin and creates a DLL file with same filename
        /// </summary>
        /// <param name="filename">Filename without directory names and without extension</param>
        void CompilePlugin(string filename)
        {
            var codePath = Path.Combine(config.PluginsDirectory, filename + ".cs");

            var code = File.ReadAllText(codePath);

            var options = ScriptOptions.Default
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithReferences(typeof(Plugin).Assembly)
                .WithImports("CryCrawler");

            var script = CSharpScript.Create(code, options);
            var compiled = script.Compile();

            if (compiled.Length > 0)
            {
                var err = "";
                foreach (var c in compiled) err += $"\n{c.ToString()}";

                throw new Exception("Failed to compile plugin!" + err);
            }

            var comp = script.GetCompilation();

            // export to dll assembly
            using (var fstream = new FileStream(Path.Combine(config.PluginsDirectory, filename + ".dll"),
                FileMode.Create, FileAccess.Write, FileShare.None))
            {
                comp.Emit(fstream);
            }

            Logger.Log($"Compiled plugin {filename}", Logger.LogSeverity.Debug);
        }

        /// <summary>
        /// Attempts to load compiled assembly (DLL) as a Plugin
        /// </summary>
        /// <param name="filename">Filename without directory names and without extension</param>
        Plugin LoadPlugin(string filename)
        {
            var compPath = Path.Combine(config.PluginsDirectory, filename + ".dll");

            var assembly = Assembly.LoadFrom(compPath);
            foreach (Type t in assembly.ExportedTypes)
            {
                // skip if it doesn't inherit from plugin
                if (t.BaseType.FullName != "CryCrawler.Plugin") continue;

                var instance = Activator.CreateInstance(t, config);
                return (Plugin)instance;
            }

            return null;
        }

        /// <summary>
        /// Calls dispose on all loaded plugins
        /// </summary>
        public void Dispose()
        {
            foreach (var p in Plugins) p.LoadedPlugin.Dispose();
        }

        /// <summary>
        /// Ensures the plugin directory exists and creates a template plugin file
        /// </summary>
        void CreatePluginTemplate()
        {
            Directory.CreateDirectory(config.PluginsDirectory);

            var file = Path.Combine(config.PluginsDirectory, "PluginTemplate.cs-template");

            // create
            var exec = Assembly.GetExecutingAssembly();
            var path = $"{exec.GetName().Name}.Assets.TestPlugin.cs";
            using (var str = exec.GetManifestResourceStream(path))
            using (var fstr = new FileStream(file, FileMode.Create, FileAccess.Write)) str.CopyTo(fstr);     
        }

        /// <summary>
        /// Invoke function on all plugins in order
        /// </summary>
        /// <param name="invokeAction">Action to invoke on each plugin</param>
        public void Invoke(Action<Plugin> invokeAction)
        {
            foreach (var p in Plugins) invokeAction(p.LoadedPlugin);
        }

        // Plugins should have following TYPES of functions:
        // - Middleware functions (no limit on how many, will be called based on plugin load order)
        // - Override functions (only one will be loaded from plugins - this function overrides a whole function)

        // Plugins are classes that inherit from Plugin class

        // Plugins must have the following functions defined:
        // - Constructor(Configuration config)
        // - void Dispose()

        // Supported functions:
        // - [Middleware] void Info()                                               -> Called on start, can be used to display plugin info
        // - [Middleware] void OnDump()
        // - [Middleware] void OnClientConnect(TcpClient client)                    -> When client connects to host
        // - [Middleware] bool OnClientConnecting(TcpClient client)                 -> Used for accepting or denying clients on host
        // - [Middleware] void OnClientDisconnect(TcpClient client)                 -> When client disconnects from host
        // - [Middleware] void OnDisconnect()                                       -> When we disconnect from host
        // - [Middleware] void OnConnect()                                          -> When we connect to host
        // - [Override]   IEnumerable<string> FindUrls(string url, string content)  -> Used for returning next URLs to crawl based on content
        // - [Middleware] bool BeforeDownload(string url, string detination)        -> When file is about to be downloaded, this can accept or deny it
        // - [Middleware] void AfterDownload(string url, string desitnation)        -> After file is downloaded
        // - [Middleware] bool OnWorkReceived(string url)                           -> Crawler or Worker Manager get's next work to crawl/assign. This can accept or deny it.

        // Can add more later...
    }

    public class PluginInfo
    {
        public Plugin LoadedPlugin { get; }
        public bool FindUrlsImplemented { get; }

        readonly MethodInfo findUrlsMethod;

        public PluginInfo(Plugin p)
        {
            LoadedPlugin = p;
            
            // check for override functions
            var methods = p.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var m in methods)
            {
                var name = m.Name;

                if (name == "FindUrls")
                {
                    if (FindUrlsImplemented) throw new InvalidOperationException("Duplicate 'FindUrls' function!");
                    FindUrlsImplemented = true;
                    findUrlsMethod = m;
                }
            }
        }

        // Manually implement override functions below:
        public IEnumerable<string> FindUrls(string url, string content)
        {
            if (FindUrlsImplemented == false) throw new NotImplementedException();

            return (IEnumerable<string>)findUrlsMethod.Invoke(LoadedPlugin, new object[] { url, content });
        }
    }

    public abstract class Plugin
    {
        public virtual void Info() { }
        public virtual void OnDump() { }
        public virtual void OnClientConnect(TcpClient client) { }
        public virtual bool OnClientConnecting(TcpClient client) => true;
        public virtual void OnClientDisconnect(TcpClient client) { }
        public virtual void OnDisconnect() { }
        public virtual void OnConnect() { }
        public virtual bool BeforeDownload(string url, string detination) => true;
        public virtual void AfterDownload(string url, string desitnation) { }
        public virtual bool OnWorkReceived(string url) => true;
        public abstract void Dispose();
    }
}
