using System;
using System.IO;
using System.Text;

namespace CryCrawler.Worker
{
    public class Work
    {
        public long Id { get; set; }

        // Key will be the Url, but limited to 512 bytes (LiteDB limitation)
        public string Key { get; set; }
        public string Url { get; set; }

        public DateTime? LastCrawled { get; set; }  // use this to log time of crawling
        public DateTime AddedTime { get; set; }

        public Work(string url)
        {
            Url = url;
            Key = GetKeyFromUrl(url);
            AddedTime = DateTime.Now;
        }
        public Work() { }

        public static string LimitText(string text, int maxLenBytes)
        {
            var txt = text.Length >= maxLenBytes ? text.Substring(0, maxLenBytes - 1) : text;

            // check size in bytes and adjust accordingly
            while (Encoding.UTF8.GetBytes(txt).Length >= maxLenBytes) txt = txt[0..^1];

            return txt;
        }

        public static string GetKeyFromUrl(string url) => LimitText(url, CacheDatabase.MaxIndexLength);
    }

    public class DownloadedWork
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string DownloadedTime{ get; set; }
        public DownloadedWork(string path)
        {
            FilePath = path;
            FileName = Path.GetFileName(path);
            DownloadedTime = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
        }
    }
}
