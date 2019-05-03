using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;

namespace CryCrawler
{
    public static class ConfigManager
    {
        public const string FileName = "config.json";

        public static bool LoadConfiguration(out Configuration config)
        {
            config = new Configuration();

            if (!File.Exists(FileName)) return false;
            else
            {
                try
                {
                    //  attempt to load config file
                    var content = File.ReadAllText(FileName);
                    config = JsonConvert.DeserializeObject<Configuration>(content);

                    // validate some properties
                    if (IPAddress.TryParse(config.HostConfig.ListenerConfiguration.IP, out IPAddress addr) == false)
                        throw new Exception($"'{config.HostConfig.ListenerConfiguration.IP}' is not a valid IP address for listener!");
                    if (IPAddress.TryParse(config.WebGUI.IP, out IPAddress addr2) == false)
                        throw new Exception($"'{config.WebGUI.IP}' is not a valid IP address for listener!");

                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to load configuration. Corrupted or invalid file!", Logger.LogSeverity.Error);
                    Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);

                    config = null;
                    return false;
                }
            }
        }

        public static void SaveConfiguration(Configuration configuration)
        {
            try
            {
                var content = JsonConvert.SerializeObject(configuration, Formatting.Indented);
                File.WriteAllText(FileName, content);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to save configuration!", Logger.LogSeverity.Error);
                Logger.Log(ex.GetDetailedMessage(), Logger.LogSeverity.Debug);
            }
        }
    }
}
