using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Mime;
using System.Reflection;
using System.Text;

namespace CryCrawler.Network
{
    public abstract class WebGUIResponder
    {
        protected Dictionary<string, string> ContentTypes = new Dictionary<string, string> {
            { "html", "text/html" },
            { "css", "text/css" },
            { "js", "application/javascript" },
            { "json", "application/json" },
            { "xml", "application/xml" },
            { "gif", "image/gif" },
            { "jpg", "image/jpeg" },
            { "png", "image/png" },
            { "ico", "image/x-icon" },
            { "txt", "text/plain" },
            { "webm", "video/webm" },
            { "mp4", "video/mp4" }
            // can add more later...
        };

        /// <summary>
        /// Returns path to website root directory. Use dots instead of slashes.
        /// </summary>
        /// <returns>Path to website root directory</returns>
        protected abstract string GetPath();


        ConcurrentDictionary<string, string> CachedPages = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Loads content of specified page from website root.
        /// </summary>
        /// <param name="filename">Relative path to file from website root</param>
        /// <returns>Text content</returns>
        public virtual string GetPage(string filename, out string contentType)
        {
            // Get extension and decide on content type
            var extension = Path.GetExtension(filename).ToLower().Replace(".", "");
            contentType = ContentTypes.ContainsKey(extension) ? ContentTypes[extension] : ContentTypes["txt"];

            // load from cache if possible
            if (CachedPages.ContainsKey(filename)) return CachedPages[filename];

            // check if filename contains illegal characters
            if (filename.Contains("..")) return null;

            // load from file otherwise
            var exec = Assembly.GetExecutingAssembly();
            var path = $"{exec.GetName().Name}.{GetPath()}.{filename.Replace('\\', '.')}";
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

        /// <summary>
        /// Constructs a proper HTTP response based on the request
        /// </summary>
        /// <param name="method">Request method</param>
        /// <param name="url">Requested resource</param>
        /// <param name="body">Request body</param>
        /// <param name="contentType">Type of content returned</param>
        /// <returns>Raw response without the headers</returns>
        public virtual string GetResponse(string method, string url, string body, out string contentType)
        {
            // TODO: improve later

            // Attempt to get filename, if URL is just "/", take "Home.html" as default starting page
            var filename = Path.GetFileName(url);
            if (url == "/") filename = "Home.html";

            // Handle methods
            if (method == "GET") return ResponseGET(filename, out contentType);
            else if (method == "POST") return ResponsePOST(filename, body, out contentType);
            else return GetJSON("Invalid HTTP method!", out contentType);            
        }

        protected virtual string ResponseGET(string filename, out string contentType) => GetPage(filename, out contentType);       
        protected virtual string ResponsePOST(string filename, string body, out string contentType) => GetJSON("Invalid HTTP method!", out contentType);

        protected virtual string GetJSON(string message, out string contentType)
        {
            contentType = ContentTypes["json"];
            return $"{{ \"message\": \"{message}\" }}";
        }
    }
}
