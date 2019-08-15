using System;
using System.IO;
using System.Text;
using System.Timers;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;


namespace CryCrawler.Worker
{
    public class RobotsHandler
    {
        readonly TimeSpan maxAgeDefault = TimeSpan.FromHours(24);
        readonly TimeSpan maxAge = default;

        readonly Timer timer;
        readonly HttpClient http;
        readonly WorkerConfiguration config;

        public RobotsHandler(WorkerConfiguration config, HttpClient client, TimeSpan overrideMaxEntryAge = default)
        {
            this.config = config;
            this.maxAge = overrideMaxEntryAge == default ? maxAgeDefault : overrideMaxEntryAge;

            http = client;
            timer = new Timer(TimeSpan.FromMinutes(5).TotalMinutes);
            timer.Elapsed += Timer_Elapsed;
            timer.Start();
        }

        /// <summary>
        /// Contains URLs that should be excluded from crawling
        /// </summary>
        public readonly ConcurrentDictionary<string, RobotsData> UrlExclusions
            = new ConcurrentDictionary<string, RobotsData>();

        /// <summary>
        /// Checks if URL is excluded for specified domain. Specify new config to override default one.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsUrlExcluded(string url, WorkerConfiguration config = null, bool getRobotsIfMissing = false)
        {
            config = config ?? this.config;

            // check if URL matches any blacklisted patterns
            if (Extensions.IsURLBlacklisted(url, config)) return true;
            
            // if robot standard is not being respected, return false
            if (config.RespectRobotsExclusionStandard == false) return false;

            // otherwise check the exclusion list

            var domain = Extensions.GetDomainName(url, out string protocol, out string path, false);

            if (UrlExclusions.TryGetValue(domain, out RobotsData data)) return isUrlExcluded(data, path);
            else if (getRobotsIfMissing)
            {
                // send request for robot.txt
                data = null;
                try
                {
                    // send request
                    var response = await http.GetAsync($"{protocol}://{domain}/robots.txt");
                    response.EnsureSuccessStatusCode();

                    var robotsStream = await response.Content.ReadAsStreamAsync();

                    // process content
                    data = await processRobotsContent(robotsStream);

                    Logger.Log($"Got 'robots.txt' for '{domain}' (" +
                        $"Allowed: {data.AllowedList.Count}, " +
                        $"Disallowed: {data.DisallowedList.Count}, " +
                        $"Wait: {data.WaitTime})", Logger.LogSeverity.Debug);
                }
                catch (Exception ex)
                {
                    // leave data empty if request fails
                    Logger.Log($"Failed to get 'robots.txt' for '{domain}': {ex.Message}", Logger.LogSeverity.Debug);
                }
                finally
                {
                    if (data == null) data = new RobotsData();

                    // add to url exclusions
                    UrlExclusions.TryAdd(domain, data);
                }

                return isUrlExcluded(data, path);
            }

            return false;
        }

        /// <summary>
        /// Sites can provide wait times for crawlers, 
        /// this method will return the provided wait time (in seconds) for specific URL if it has any. 
        /// Returns 0 if no wait time is provided. Specify new config to override default one.
        /// </summary>
        public double GetWaitTime(string url, WorkerConfiguration config = null)
        {
            config = config ?? this.config;

            // Crawl-delay: 1  (seconds)

            // if robot standard is not being respected, return false
            if (config.RespectRobotsExclusionStandard == false) return 0;

            var domain = Extensions.GetDomainName(url, out _);

            if (UrlExclusions.TryGetValue(domain, out RobotsData data)) return data.WaitTime;
            else return 0;
        }

        /// <summary>
        /// Associate specified domain with specified robot.txt content.
        /// </summary>
        public async Task<RobotsData> RegisterRobotsTxt(string domain, string content,
            bool overrideExistingDomainData = true)
        {
            if (string.IsNullOrEmpty(content)) return null;

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                var data = await processRobotsContent(stream);

                // add or override this data
                if (overrideExistingDomainData && UrlExclusions.ContainsKey(domain)) UrlExclusions[domain] = data;
                else UrlExclusions.TryAdd(domain, data);

                return data;

            }
        }

        /// <summary>
        /// Processes "robots.txt" content and returns the data. Specify new config to override default one.
        /// </summary>
        /// <param name="stream">Stream containing "robots.txt" content</param>
        async Task<RobotsData> processRobotsContent(Stream stream, WorkerConfiguration config = null)
        {
            config = config ?? this.config;

            var data = new RobotsData();

            using (var reader = new StreamReader(stream))
            {
                bool userAgentMatched = false;
                const string userAgentKey = "User-agent: ";

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();

                    // ignore comments
                    if (line.StartsWith('#')) continue;

                    // check if we match the user-agent
                    if (line.StartsWith(userAgentKey, StringComparison.OrdinalIgnoreCase))
                    {
                        if (userAgentMatched)
                        {
                            // our user-agent was already matched, this means we got OUT of our user-agent sector
                            // based on specification: Only first matched User-Agent section is respected - others are ignored
                            break;
                        }

                        var useragent = line.Substring(userAgentKey.Length);

                        // if useragent is identical to our useragent, it's a match
                        if (useragent.ToLower() == config.UserAgent.ToLower()) userAgentMatched = true;
                        else
                        {
                            // otherwise if useragent contains '*', treat it as regex pattern
                            var pattern = GetRegexPattern(useragent);
                            if (Regex.IsMatch(config.UserAgent, pattern, RegexOptions.IgnoreCase)) userAgentMatched = true;
                        }

                        continue;
                    }

                    // do not continue until user agent is matched
                    if (userAgentMatched == false) continue;

                    const string allowKey = "Allow: ";
                    const string disallowKey = "Disallow: ";
                    const string delayKey = "Crawl-delay: ";

                    if (line.StartsWith(disallowKey, StringComparison.OrdinalIgnoreCase))
                    {
                        var disallowedPath = line.Substring(disallowKey.Length);
                        var pattern = GetRegexPattern(disallowedPath);

                        // '/' is a special symbol to match everything
                        if (disallowedPath == "/") pattern = ".*";

                        data.DisallowedList.Add(pattern);
                    }
                    else if (line.StartsWith(allowKey, StringComparison.OrdinalIgnoreCase))
                    {
                        var allowedPath = line.Substring(allowKey.Length);
                        var pattern = GetRegexPattern(allowedPath);
                        data.AllowedList.Add(pattern);
                    }
                    else if (line.StartsWith(delayKey, StringComparison.OrdinalIgnoreCase))
                    {
                        var delay = line.Substring(delayKey.Length);
                        if (double.TryParse(delay, out double r)) data.WaitTime = r;
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// Using provided RobotsData, determines if specified path is disallowed or allowed.
        /// </summary>
        /// <param name="data">RobotsData containing necessary lists to check if URL is excluded</param>
        /// <param name="path">URL request path used for matching excluded patterns</param>
        /// <returns></returns>
        bool isUrlExcluded(RobotsData data, string path)
        {
            // update last access
            data.LastAccess = DateTime.Now;

            // check if disallow list is empty, automatically accept url
            if (data.DisallowedList == null || data.DisallowedList.Count == 0) return false;

            // THE MOST SPECIFIC RULE IS PRIORITIZED

            string matchedDisallowPattern = null;

            // check if URL is disallowed
            foreach (var pattern in data.DisallowedList)
            {
                if (Regex.IsMatch(path, pattern))
                {
                    matchedDisallowPattern = pattern;

                    // ONCE FIRST PATTERN IS MATCHED, DO NOT SEARCH FURTHER
                    break;
                }
            }

            // if no disallow was matched, automatically accept url
            if (matchedDisallowPattern == null) return false;

            // if URL is disallowed, check if a more specific allowed rule exists
            foreach (var pattern in data.AllowedList)
            {
                if (Regex.IsMatch(path, pattern))
                {
                    // if matched pattern is more detailed than disallowed pattern, URL is allowed
                    if (pattern.Length > matchedDisallowPattern.Length) return false;

                }
            }

            // URL is excluded because it was matched by a disallow pattern
            return true;
        }

        /// <summary>
        /// Transforms regular patterns with * symbols to regex pattern
        /// </summary>
        public static string GetRegexPattern(string pattern)
            // REGEX EXPLANATION:
            // - Beginning of path needs to be marked with ^
            // - Ending also needs to be marked in a special way to not include different paths (for example: "/admin2" is not matched by "/admin", but "/admin/test" is)
            => "^" + Regex.Escape(pattern).Replace("\\*", ".*") + @"([ \\\?\/].*?)?$";


        /// <summary>
        /// Checks and removes old entries to free up memory.
        /// </summary>
        void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var now = DateTime.Now;

            List<string> removalList = new List<string>();

            // check ages
            foreach (var i in UrlExclusions)
            {
                var lastAccess = i.Value.LastAccess;
                if (now.Subtract(lastAccess).TotalMinutes > maxAge.TotalMinutes)
                {
                    // needs to be removed
                    removalList.Add(i.Key);
                }
            }

            // remove them
            foreach (var r in removalList)
            {
                if (UrlExclusions.TryRemove(r, out _))
                {
                    Logger.Log($"Removed old robots.txt entry for '{r}'", Logger.LogSeverity.Debug);
                }
            }
        }


        ~RobotsHandler()
        {
            timer.Stop();
        }

        public class RobotsData
        {
            // These Lists should contain REGEX PATTERNS!
            public List<string> AllowedList { get; set; } = new List<string>();
            public List<string> DisallowedList { get; set; } = new List<string>();

            public double WaitTime { get; set; } = 0;
            public DateTime LastAccess { get; set; } = DateTime.Now;
        }
    }
}
