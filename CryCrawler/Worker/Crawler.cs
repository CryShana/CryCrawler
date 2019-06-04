using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Linq;

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
                    await Task.Delay(100);
                    continue;
                }

                bool success = false;
                try
                {
                    // get response headers - do not read content yet
                    var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancelSource.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Log($"Failed to crawl '{url}' ({response.StatusCode})", Logger.LogSeverity.Information);
                        continue;
                    }

                    var mediaType = response.Content.Headers.ContentType.MediaType;
                    if (mediaType.ToLower().StartsWith("text"))
                    {
                        // scan the content for more urls
                        var content = await response.Content.ReadAsStringAsync();

                        // TODO: find URLs

                        Logger.Log($"Found 0 new URLs from '{url}'");
                        success = true;
                    }
                    else
                    {
                        // check if we are interested in the media type or extension

                        // attempt to get filename
                        var filename = GetFilename(url, mediaType);
                        var directory = GetDirectoryPath(url, true);

                        // construct path
                        var path = Path.Combine(directory, filename);

                        // download content to file
                        using (var fstream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                            await response.Content.CopyToAsync(fstream);

                        Logger.Log($"Downloaded '{url}' to '{path}'");
                        success = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to crawl '{url}' - {ex.Message}", Logger.LogSeverity.Error);

                }
                finally
                {
                    Manager.ReportWorkResult(url, success);
                }
            }
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
    }
}
