using System;
using System.IO;
using System.Web;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CryCrawler.Worker
{
    /// <summary>
    /// Crawls through URLs supplied by WorkManager based on configuration
    /// </summary>
    public class Crawler
    {
        public bool IsActive { get; private set; }
        public WorkManager Manager { get; }
        public WorkerConfiguration Config { get; }

        private HttpClient httpClient;
        private CancellationTokenSource cancelSource;

        public Crawler(WorkManager manager, WorkerConfiguration config)
        {
            Config = config;
            Manager = manager;
        }

        public void Start()
        {
            if (IsActive) throw new InvalidOperationException("Crawler already active!");

            IsActive = true;

            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "CryCrawler");

            cancelSource = new CancellationTokenSource();

            for (int i = 0; i < Config.MaxConcurrency; i++)
                new Task(Work, cancelSource.Token, TaskCreationOptions.LongRunning).Start();
        }

        public void Stop()
        {
            if (!IsActive) return;

            IsActive = false;
            cancelSource.Cancel();
        }

        private async void Work()
        {
            while (!cancelSource.IsCancellationRequested)
            {
                if (Manager.GetWork(out string url) == false)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    continue;
                }

                bool success = false;
                try
                {
                    // Get response headers - DO NOT READ CONTENT yet (performance reasons)
                    var response = await httpClient
                        .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancelSource.Token)
                        .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        // TODO: treat differently based on status code (for ex. if page doesn't exist at all, or if 500, 404,...)
                        // Logger.Log($"Failed to crawl '{url}' ({response.StatusCode})", Logger.LogSeverity.Information);
                        continue;
                    }

                    var mediaType = response.Content.Headers.ContentType.MediaType;

                    // Check if media type is set as a scanning target, if yes, scan it for new URLs
                    if (Config.ScanTargetsMediaTypes.Count(x => x == mediaType) > 0)
                    {
                        // scan the content for more urls
                        var content = await response.Content.ReadAsStringAsync();

                        // find URLs
                        int cnt = 0;
                        foreach (var u in FindUrls(content))
                        {
                            // check if URL is eligible for crawling
                            if (Manager.IsUrlEligibleForCrawl(u) == false) continue;

                            cnt++;
                            Manager.AddToBacklog(u);
                        }

                        // Logger.Log($"Found {cnt} new URLs from '{url}'");
                        success = true;
                    }

                    // Check if media type is set as an accepted file to download

                    // attempt to get filename
                    var filename = GetFilename(url, mediaType);

                    // don't download file if not acceptable
                    if (IsAcceptable(filename, mediaType) == false) continue;
                    
                    // construct path
                    var directory = GetDirectoryPath(url, true);
                    var path = Path.Combine(directory, filename);

                    // download content to file
                    using (var fstream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                        await response.Content.CopyToAsync(fstream).ConfigureAwait(false);

                    // Logger.Log($"Downloaded '{url}' to '{path}'");
                    success = true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to crawl '{url}' - ({ex.GetType().Name}) {ex.Message}", Logger.LogSeverity.Error);
                }
                finally
                {
                    Manager.ReportWorkResult(url, success);
                }
            }
        }

        /// <summary>
        /// Based on the configuration, is the file acceptable based on extension and media type.
        /// </summary>
        /// <param name="filename">Filename with extension</param>
        /// <param name="mediaType">Media type</param>
        /// <returns>True if file is acceptable</returns>
        public bool IsAcceptable(string filename, string mediaType)
        {
            if (Config.AcceptAllFiles) return true;

            var ext = Path.GetExtension(filename).ToLower().Trim();
            var mty = mediaType.ToLower().Trim();

            // check extension
            if (Config.AcceptedExtensions.Count(x => x.ToLower().Trim() == ext) > 0) return true;

            // check media type
            if (Config.AcceptedMediaTypes.Count(x => x.ToLower().Trim() == mty) > 0) return true;

            // if both are not accepted, we are not interested
            return false;
        }

        /// <summary>
        /// Attempts to get filename from given URL and response media type.
        /// </summary>
        /// <returns>Parsed filename from URL</returns>
        public string GetFilename(string url, string mediaType)
        {
            // get extension based on media type
            var extension = MimeTypeMap.List.MimeTypeMap.GetExtension(mediaType).FirstOrDefault();
            if (extension == null) extension = "";

            // if any query parameters present, remove them
            var index = url.IndexOf('?');
            if (index >= 0) url = url.Substring(0, index);

            // parse URL and attempt to get filename and extension
            var parsedFilename = Path.GetFileNameWithoutExtension(url);
            var parsedExtension = Path.GetExtension(url);

            // parsed extension has priority - if it's not empty, set it as the extension
            if (string.IsNullOrEmpty(parsedExtension) == false) extension = parsedExtension;

            // if extension contains a part starting with :, remove it
            index = extension.IndexOf(':');
            if (index >= 1) extension = extension.Substring(0, index);

            // if filename couldn't be parsed, generate random filename
            if (string.IsNullOrEmpty(parsedFilename)) parsedFilename = Path.GetRandomFileName();

            return $"{parsedFilename}{extension}";
        }

        /// <summary>
        /// Get's the directory path based on URL
        /// </summary>
        /// <param name="createDirectory">Automatically ensure the directory is created</param>
        /// <returns>Path based on URL</returns>
        public string GetDirectoryPath(string url, bool createDirectory = false)
        {
            string path = Config.DownloadsPath;

            // if any query parameters present, remove them
            var index = url.IndexOf('?');
            if (index >= 0) url = url.Substring(0, index);

            // start splitting the url
            var urlParts = url.Split('/');
            for (int i = 1; i < urlParts.Length; i++)
            {
                if (i > 2 && i == urlParts.Length - 1) continue;

                var fixedPath = FixPath(urlParts[i]);
                path = Path.Combine(path, fixedPath);
            }

            if (createDirectory) Directory.CreateDirectory(path);

            return path;
        }

        /// <summary>
        /// Removes any invalid path characters from given path
        /// </summary>
        /// <param name="path">Original path</param>
        /// <returns>Modified paths without any invalid path characters</returns>
        public string FixPath(string path)
        {
            if (path == null) return "";

            var chars = Path.GetInvalidPathChars();
            int index = path.IndexOfAny(chars);
            while (index >= 0)
            {
                path = path.Remove(index, 1);
                index = path.IndexOfAny(chars);
            }

            return path;
        }

        /// <summary>
        /// Finds any URLs contained withing the given text content
        /// </summary>
        /// <param name="content">Text content that might contain URLs</param>
        /// <returns>A collection of found URLs</returns>
        public IEnumerable<string> FindUrls(string content)
        {
            // TODO: check for relative URLs

            // Check for URLs beginning with HTTP
            int cindex = 0;
            while (cindex < content.Length && cindex != -1)
            {   
                cindex = content.IndexOf("http", cindex, StringComparison.OrdinalIgnoreCase);
                if (cindex >= 0)
                {
                    // read until a non-URL character is found
                    var nextindex = content.IndexOfAny(new[] { '<', '>', '(', ')', '\'', '"', '\n', '\r', '\t', ' ', '[', ']' }, cindex + 1);
                    var url = content.Substring(cindex, nextindex - cindex);
                    cindex++;

                    // url decode it
                    url = HttpUtility.UrlDecode(url);

                    // check if valid url
                    //var valid = Regex.IsMatch(url, @"^(ht|f)tp(s?)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?$");

                    yield return url;
                }
            }

            // Check for relative URLs
        }
    }
}
