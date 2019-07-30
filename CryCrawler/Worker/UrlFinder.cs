using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Web;
using System.Linq;

namespace CryCrawler.Worker
{
    public static class UrlFinder
    {
        /// <summary>
        /// Finds all URLs in given text content from given URL based on the given configuration and automatically adds to given work manager.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="content"></param>
        /// <param name="config"></param>
        /// <param name="plugins"></param>
        /// <param name="manager"></param>
        /// <param name="cancelSource"></param>
        public static void ScanContentAndAddToManager(string url, string content,
            WorkerConfiguration config, PluginManager plugins, WorkManager manager,
            CancellationTokenSource cancelSource)
        {
            // check plugins for FindUrls implementation
            PluginInfo foundplugin = null;
            if (plugins != null)
            {
                foreach (var p in plugins.Plugins)
                {
                    if (p.FindUrlsImplemented)
                    {
                        foundplugin = p;
                        break;
                    }
                }
            }

            // find URLs (use PLUGIN that overrides it, if it exists)
            if (foundplugin == null)
            {
                foreach (var u in FindUrls(url, content, config))
                {
                    if (cancelSource.IsCancellationRequested) break;
                    validateFoundUrl(u);
                }
            }
            else
            {
                foreach (var u in foundplugin.FindUrls(url, content))
                {
                    if (cancelSource.IsCancellationRequested) break;
                    validateFoundUrl(u);
                }
            }

            // LOCAL FUNCTION FOR VALIDATING FOUND URLS
            void validateFoundUrl(string u)
            {
                // check if URL is eligible for crawling
                if (manager.IsUrlEligibleForCrawl(u) == false) return;

                if (manager.IsUrlCrawled(u))
                {
                    // ignore already-crawled urls
                }
                else manager.AddToBacklog(u);
            }
        }

        /// <summary>
        /// Finds any URLs contained withing the given text content
        /// </summary>
        /// <param name="url">Url from which the content was received</param>
        /// <param name="content">Text content that might contain URLs</param>
        /// <param name="config">Configuration to use when validating URLs</param>
        /// <returns>A collection of found URLs</returns>
        public static IEnumerable<string> FindUrls(string currentUrl, string content, WorkerConfiguration config)
        {
            var domain = Extensions.GetDomainName(currentUrl, out string protocol, true);

            var foundUrls = new HashSet<string>();

            // Check for URLs beginning with HTTP
            var endings = new[] { '<', '>', '(', ')', '\'', '"', '\n', '\r', '\t', ' ', '[', ']' };

            int cindex = 0;
            while (cindex < content.Length && cindex != -1)
            {
                cindex = content.IndexOf("http", cindex, StringComparison.OrdinalIgnoreCase);
                if (cindex >= 0)
                {
                    // read until a non-URL character is found
                    var nextindex = content.IndexOfAny(endings, cindex + 1);
                    var url = content.Substring(cindex, nextindex - cindex);
                    cindex = nextindex;

                    // url decode it
                    url = HttpUtility.UrlDecode(url);

                    // quick check if valid url

                    // if URL contains too many ; and ?, it is most likely invalid
                    if (url.Count(x => x == ';') > 3) continue;
                    if (url.Count(x => x == '?') > 3) continue;

                    // absolute url must contain ':' - (http: or https:) and must be more than 8 in length
                    if (url.Length <= 8 || (!url.Contains("http:/") && !url.Contains("https:/"))) continue;

                    // url can't be longer than 2000 characters
                    if (url.Length > 2000) continue;

                    //var valid = Regex.IsMatch(url, @"^(ht|f)tp(s?)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?$");

                    if (Extensions.IsUrlWhitelisted(url, config) == false) continue;

                    if (foundUrls.Contains(url)) continue;
                    foundUrls.Add(url);
                    yield return url;
                }
            }

            // Check for relative URLs
            var startings = new[] { '"', '\'', '`', '(', ' ' };
            endings = new[] { '"', '\'', '`', ')', ' ', '\n', '\r', '\t', '>', '<' };

            cindex = 0;
            while (cindex < content.Length && cindex != -1)
            {
                cindex = content.IndexOf('/', cindex);
                if (cindex > 0)
                {
                    var charBefore = content[cindex - 1];
                    if (startings.Contains(charBefore) == false) { cindex++; continue; }

                    var nextindex = content.IndexOfAny(endings, cindex + 1);
                    var url = content.Substring(cindex, nextindex - cindex);
                    cindex = nextindex;

                    if (url.Length <= 2) continue;

                    url = HttpUtility.UrlDecode(url);

                    if (url.StartsWith("//"))
                    {
                        url = protocol + ":" + url;
                    }
                    else
                    {
                        url = domain + url;
                    }

                    // if URL contains too many ; and ?, it is most likely invalid
                    if (url.Count(x => x == ';') > 3) continue;
                    if (url.Count(x => x == '?') > 3) continue;

                    // url can't be longer than 2000 characters
                    if (url.Length > 2000) continue;

                    if (Extensions.IsUrlWhitelisted(url, config) == false) continue;

                    if (foundUrls.Contains(url)) continue;
                    foundUrls.Add(url);
                    yield return url;
                }
                else if (cindex == -1) break;
                else cindex++;
            }
        }
    }
}
