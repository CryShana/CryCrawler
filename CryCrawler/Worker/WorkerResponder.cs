using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using static CryCrawler.Network.WebGUI;

namespace CryCrawler.Worker
{
    class WorkerResponder : WebGUIResponder
    {
        ConcurrentDictionary<string, string> CachedPages = new ConcurrentDictionary<string, string>();
        string GetPage(string filename)
        {
            // load from cache if possible
            if (CachedPages.ContainsKey(filename)) return CachedPages[filename];

            // check if filename contains illegal characters
            if (filename.Contains("..")) return null;

            // load from file otherwise
            var exec = Assembly.GetExecutingAssembly();
            var path = $"{exec.GetName().Name}.Worker.GUI.{filename.Replace('\\', '.')}";
            using (var str = exec.GetManifestResourceStream(path))
            {
                if (str == null) return null;
                using (var reader = new StreamReader(str))
                {
                    var page = reader.ReadToEnd();

                    // attempt to cache page
                    CachedPages.TryAdd(filename, page);

                    // return page
                    return page;
                }
            }      
        }

        public string GetResponse(string method, string url, string body, out string contentType)
        {
            // TODO: improve later

            if (method == "GET")
            {
                contentType = "text/html";
                if (url == "/") return GetPage("Home.html");
                else if (url.Length > 1)
                {
                    if (url.ToLower().EndsWith(".css")) contentType = "text/css";
                    else if (url.ToLower().EndsWith(".js")) contentType = "application/javascript";

                    return GetPage(url.Substring(1));
                }
            }
            else
            {
                contentType = "application/json";
                return @"{ ""message"": ""Invalid HTTP method!"" }";
            }
                      
            contentType = "application/json";
            return @"{ ""message"": ""Invalid location!"" }";
        }
    }
}
