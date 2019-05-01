using System.Linq;
using CryCrawler.Host;
using CryCrawler.Worker;
using static System.Console;
using System.Threading.Tasks;

namespace CryCrawler
{
    class Program
    {  
        static void Main(string[] args)
        {
            // Parse arguments
            bool showHelp = args.Count(x => x.ToLower() == "-h") > 0;
            bool isHost = args.Count(x => x.ToLower() == "--host") > 0;
            Logger.DebugMode = args.Count(x => x.ToLower() == "--debug" || x.ToLower() == "-d") > 0;

            // Show help if necessary
            if (showHelp)
            {
                ShowHelp();
                return;
            }

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
            if (isHost) new HostProgram(config).Start();
            else new WorkerProgram(config).Start();

            // Wait for shutdown signal
            ConsoleHost.WaitForShutdown();
        }

        static void ShowHelp()
        {
            WriteLine("Following flags are supported:\n");

            ForegroundColor = System.ConsoleColor.Cyan;
            WriteLine("     -h              - Shows help");
            WriteLine("     -d, --debug     - Enables debug logs");
            WriteLine("     --host          - Starts program in Hosting mode");
            ResetColor();

            WriteLine($"\nFirst run will generate an empty configuration file '{ConfigManager.FileName}' that can be used for further configuration.");
        }
    }
}
