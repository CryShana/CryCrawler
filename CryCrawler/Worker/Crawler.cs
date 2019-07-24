using System;
using System.IO;
using System.Net;
using System.Web;
using System.Linq;
using System.Net.Http;
using System.Threading;
using CryCrawler.Structures;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace CryCrawler.Worker
{
    /// <summary>
    /// Crawls through URLs supplied by WorkManager based on configuration
    /// </summary>
    public class Crawler
    {
        #region Public Properties
        public bool IsActive { get; private set; }
        public WorkManager Manager { get; }
        public WorkerConfiguration Config { get; set; }

        public bool WaitingForWork => CurrentTasks.All(x => x.Value == null);
        public ConcurrentDictionary<int, string> CurrentTasks { get; } = new ConcurrentDictionary<int, string>();
        public ConcurrentSlidingBuffer<DownloadedWork> RecentDownloads { get; }

        public event EventHandler<bool> StateChanged;
        #endregion

        private HttpClient httpClient;
        private int currentTaskNumber = 1;
        private CancellationTokenSource cancelSource;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

        /// <summary>
        /// Crawler uses WorkManager to get URLs to crawl through.
        /// </summary>
        public Crawler(WorkManager manager, WorkerConfiguration config)
        {
            Config = config;
            Manager = manager;
            RecentDownloads = new ConcurrentSlidingBuffer<DownloadedWork>(config.MaxLoggedDownloads);
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

            StateChanged?.Invoke(this, true);
        }

        public void Stop()
        {
            if (!IsActive) return;

            IsActive = false;
            cancelSource.Cancel();
            CurrentTasks.Clear();
            currentTaskNumber = 1;

            StateChanged?.Invoke(this, false);
        }

        private async void Work()
        {
            var taskNumber = 0;

            semaphore.Wait();
            taskNumber = currentTaskNumber++;
            CurrentTasks.TryAdd(taskNumber, null);
            semaphore.Release();

            while (!cancelSource.IsCancellationRequested)
            {
                #region Get valid work
                if (!Manager.IsWorkAvailable || Manager.GetWork(out Work w) == false)
                {
                    // unable to get work, wait a bit and try again
                    CurrentTasks[taskNumber] = null;
                    await Task.Delay(20);
                    continue;
                }

                var url = w.Url;

                // check if url is whitelisted
                if (Extensions.IsUrlWhitelisted(url, Config) == false) continue;
                #endregion

                DateTime? recrawlDate = null;
                var lastCrawl = w.LastCrawled;
                w.LastCrawled = DateTime.Now;

                CurrentTasks[taskNumber] = url;
                HttpStatusCode? statusCode = null;
                try
                {
                    // Get response headers - DO NOT READ CONTENT yet (performance reasons)
                    var response = await httpClient
                        .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancelSource.Token);

                    statusCode = response.StatusCode;
                    if (!response.IsSuccessStatusCode || cancelSource.IsCancellationRequested)
                    {
                        #region Failed to crawl
                        // TODO: treat differently based on status code (for ex. if page doesn't exist at all, or if 500, 404,...)
                        switch (response.StatusCode)
                        {
                            case HttpStatusCode.Redirect:
                                // Add the redirected location to backlog
                                var newurl = response.Headers.Location.AbsoluteUri;
                                if (string.IsNullOrEmpty(newurl) == false)
                                {
                                    // check if URL is eligible for crawling
                                    if (Manager.IsUrlEligibleForCrawl(newurl) == false) continue;
                                    if (Manager.IsUrlCrawled(newurl))
                                    {
                                        // ignore already-crawled urls
                                    }
                                    else Manager.AddToBacklog(newurl);
                                }
                                break;
                            case HttpStatusCode.MethodNotAllowed:
                            case HttpStatusCode.Gone:
                            case HttpStatusCode.BadRequest:
                            case HttpStatusCode.NoContent:
                            case HttpStatusCode.Unauthorized:
                            case HttpStatusCode.NotFound:
                            case HttpStatusCode.Forbidden:
                                // ignore it - mark as failed
                                break;
                            case HttpStatusCode.BadGateway:
                            case HttpStatusCode.TooManyRequests:
                            case HttpStatusCode.InternalServerError:
                                // if no recrawl date set yet, set it into the future
                                if (w.RecrawlDate == null && w.LastCrawled != null) recrawlDate = DateTime.Now.AddMinutes(5);                  
                                // if recrawl was already set, double it since last time
                                else
                                {
                                    var duration = w.RecrawlDate.Value.Subtract(lastCrawl.Value);
                                    recrawlDate = DateTime.Now.Add(duration.Multiply(2));
                                }
                                break;

                            default:
                                break;
                        }

                        // if recrawl date was set
                        if (recrawlDate != null) w.RecrawlDate = recrawlDate;                       

                        w.Success = false;

                        continue;
                        #endregion
                    }

                    var mediaType = response.Content?.Headers?.ContentType?.MediaType;
                    if (mediaType == null) continue;                 

                    // Check if media type is set as a scanning target, if yes, scan it for new URLs
                    if (Config.ScanTargetsMediaTypes.Count(x => x == mediaType) > 0)
                    {
                        #region Scan for new Urls
                        // scan the content for more urls
                        var content = await response.Content.ReadAsStringAsync();

                        // find URLs
                        int cnt = 0;
                        foreach (var u in FindUrls(url, content))
                        {
                            if (cancelSource.IsCancellationRequested) break;

                            // check if URL is eligible for crawling
                            if (Manager.IsUrlEligibleForCrawl(u) == false) continue;                         

                            cnt++;
                            if (Manager.IsUrlCrawled(url))
                            {
                                // ignore already-crawled urls
                            }
                            else Manager.AddToBacklog(u);
                        }

                        // Logger.Log($"Found {cnt} new URLs from '{url}'"); 
                        #endregion
                    }

                    // Check if media type is set as an accepted file to download

                    #region Download resource if valid
                    // attempt to get filename
                    var filename = GetFilename(url, mediaType);

                    // don't download file if not acceptable
                    if (IsAcceptable(filename, mediaType) == false ||
                        cancelSource.IsCancellationRequested) continue;                    

                    // check file size limits
                    var size = response.Content.Headers.ContentLength;

                    // if content length is not provided, ignore file
                    if (size == null) continue;

                    var sizekB = size / 1024;
                    if (Config.MinimumAllowedFileSizekB != -1 && sizekB < Config.MinimumAllowedFileSizekB) continue;
                    if (Config.MaximumAllowedFileSizekB != -1 && sizekB > Config.MaximumAllowedFileSizekB) continue;

                    // construct path
                    var directory = GetDirectoryPath(url, true);
                    var path = Path.Combine(directory, filename);

                    // check if file exists
                    if (File.Exists(path))
                    {
                        // if files have same size, they are most likely the same file - overwrite it
                        var currentSize = new FileInfo(path).Length;

                        if (currentSize != size)
                        {
                            // otherwise rename it
                            var count = 1;
                            var fn = "";
                            do
                            {
                                fn = $"{Path.GetFileNameWithoutExtension(filename)} ({count}){Path.GetExtension(filename)}";
                                count++;

                            } while (File.Exists(Path.Combine(directory, fn)) && count < int.MaxValue);

                            path = Path.Combine(directory, fn);
                        }
                    }

                    if (cancelSource.IsCancellationRequested) break;

                    // download content to file
                    using (var fstream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        await response.Content.CopyToAsync(fstream);

                    // log the download
                    RecentDownloads.Add(new DownloadedWork(path, response.Content.Headers.ContentLength.Value));

                    // Logger.Log($"Downloaded '{url}' to '{path}'");
                    w.DownloadLocation = Extensions.GetRelativeFilePath(path, Config);
                    w.IsDownloaded = true;
                    w.Success = true;

                    Logger.Log($"Downloaded ({response.StatusCode}) {url}");
                    #endregion
                }
                catch (OperationCanceledException) { }
                catch (NullReferenceException nex)
                {
                    Logger.Log($"NullReferenceException while crawling - {url} - {nex.Message} -- {nex.StackTrace}", 
                        Logger.LogSeverity.Error);
                }
                catch (IOException iex)
                {
                    // usually happens when trying to download file with same name
                    Logger.Log($"IOException while crawling - {iex.Message}", 
                        Logger.LogSeverity.Debug);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Exception while crawling - {url} - ({ex.GetType().Name}) {ex.Message}", 
                        Logger.LogSeverity.Debug);
                }
                finally
                {
                    // also log crawled that weren't downloaded and had successful response
                    if (!w.Success && Config.LogEveryCrawl)
                    {
                        if (statusCode == null)
                        {
                            Logger.Log($"Canceled {url}");

                            // TODO: re-add it to backlog maybe?
                        }
                        else Logger.Log($"Crawled ({statusCode}) {url}");
                    }        

                    CurrentTasks[taskNumber] = null;
                    Manager.ReportWorkResult(w);
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

            // first check extension
            if (Config.AcceptedExtensions.Count(x => x.ToLower().Trim() == ext) > 0) return true;

            // then check media type
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

            // create subfolders based on URL if allowed
            if (Config.DontCreateSubfolders == false)
            {
                // if any query parameters present, remove them
                var index = url.IndexOf('?');
                if (index >= 0) url = url.Substring(0, index).Replace(':', '-');

                // start splitting the url
                var urlParts = url.Split('/');
                for (int i = 1; i < urlParts.Length; i++)
                {
                    if (i > 2 && i == urlParts.Length - 1) continue;

                    var fixedPath = Extensions.FixPath(urlParts[i]);
                    path = Path.Combine(path, fixedPath);
                }
            }

            if (createDirectory) Directory.CreateDirectory(path);

            return path;
        }

        /// <summary>
        /// Finds any URLs contained withing the given text content
        /// </summary>
        /// <param name="content">Text content that might contain URLs</param>
        /// <returns>A collection of found URLs</returns>
        public IEnumerable<string> FindUrls(string currentUrl, string content)
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

                    if (Extensions.IsUrlWhitelisted(url, Config) == false) continue;

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

                    if (Extensions.IsUrlWhitelisted(url, Config) == false) continue;

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
