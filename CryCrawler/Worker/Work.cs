using System;
using System.Collections.Generic;
using System.Text;

namespace CryCrawler.Worker
{
    public class Work
    {
        public long Id { get; set; }

        // Key will be the Url, but limited to 512 bytes (LiteDB limitation)
        public string Key { get; set; }
        public string Url { get; set; }

        public List<string> FoundDocuments { get; set; }  // use this to log every document found on site
        public DateTime? LastCrawled { get; set; }  // use this to log time of crawling
        public DateTime AddedTime { get; set; }

        public Work(string url)
        {
            Url = url;
            Key = LimitText(url, CacheDatabase.MaxIndexLength);
            AddedTime = DateTime.Now;
        }
        public Work() { }

        public static string LimitText(string text, int maxLenBytes)
        {
            var txt = text.Length >= maxLenBytes ? text.Substring(0, maxLenBytes - 1) : text;

            // check size in bytes and adjust accordingly
            while (Encoding.UTF8.GetBytes(txt).Length >= maxLenBytes) txt = txt.Substring(0, txt.Length - 1);

            return txt;
        }
    }
}
