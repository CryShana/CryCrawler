using System;
using System.IO;
using CommandLine;
using System.Linq;
using CryCrawler.Host;
using CryCrawler.Worker;
using System.Threading.Tasks;

namespace CryCrawler
{
    class Program
    {
        static void Main(string[] args)
        {
            bool isHost = false;

            // Parse arguments
            Parser.Default.ParseArguments<CommandLineOptions>(args).WithParsed(o =>
            {
                isHost = o.HostMode;
                Logger.DebugMode = o.DebugMode;
            });

            // Exit program if only help or version was shown
            if (args.Contains("--help") || args.Contains("--version")) return;

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

            // Start program
            var program = isHost ? new HostProgram(config) : (IProgram)new WorkerProgram(config);
            program.Start();

            // Wait for shutdown signal
            ConsoleHost.WaitForShutdown();

            // Cleanup
            Logger.Log("Shutting down...");

            program.Stop();
            CacheDatabase.Dispose();

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
    }
}
