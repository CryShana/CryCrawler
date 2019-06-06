using System;
using CommandLine;
using System.Linq;
using CryCrawler.Host;
using CryCrawler.Worker;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;

namespace CryCrawler
{
    class Program
    {
        static void Main(string[] args)
        {
            bool isHost = false, newSession = false;

            // Parse arguments
            Parser.Default.ParseArguments<CommandLineOptions>(args).WithParsed(o =>
            {
                isHost = o.HostMode;
                newSession = o.NewSession;
                Logger.DebugMode = o.DebugMode;
            });

            // Exit program if only help or version was shown
            if (args.Contains("--help") || args.Contains("--version")) return;

            // Start program with parsed arguments
            Start(isHost, newSession);
        }

        static void Start(bool isHost, bool newSession)
        {        
            // Load configuration
            if (ConfigManager.LoadConfiguration(out Configuration config) == false)
            {
                // if configuration failed to be loaded, warn user and exit (they might want to fix it)
                if (config == null)
                {
                    Logger.Log($"Please fix or delete '{ConfigManager.FileName}' before continuing!", Logger.LogSeverity.Error);
                    Task.Delay(300).Wait();
                    return;
                }

                // if configuration wasn't loaded because file was missing, create new file from empty configuration
                ConfigManager.SaveConfiguration(config);
                Logger.Log($"Created empty '{ConfigManager.FileName}' configuration file.");
            }

            // Delete old cache file if new session flag is present
            if (newSession && File.Exists(config.CacheFilename)) File.Delete(config.CacheFilename);

            // Start program
            var program = isHost ? new HostProgram(config) : (IProgram)new WorkerProgram(config);
            program.Start();

            // Wait for shutdown signal
            ConsoleHost.WaitForShutdown();

            // Cleanup
            Logger.Log("Shutting down...");
            program.Stop();

            // Save configuration
            ConfigManager.SaveConfiguration(config);

            // Wait a bit for logger
            Task.Delay(300).Wait();
        }
    }

    public class CommandLineOptions
    {
        [Option('d', "debug", Required = false, HelpText = "Enables debug mode (shows more detailed logs)")]
        public bool DebugMode { get; set; }

        [Option('h', "host", Required = false, HelpText = "Start program in Hosting mode")]
        public bool HostMode { get; set; }

        [Option('n', "new", Required = false, HelpText = "Delete old cache file and start a new crawler session")]
        public bool NewSession { get; set; }
    }
}
