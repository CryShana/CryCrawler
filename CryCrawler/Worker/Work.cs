using MessagePack;
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

        // Information
        public bool Success { get; set; }
        public bool Transferred { get; set; }
        public bool IsDownloaded { get; set; }
        public string DownloadLocation { get; set; }
        public DateTime? RecrawlDate { get; set; } // this should be set if we want delayed recrawling

        public DateTime? LastCrawled { get; set; }  // use this to log time of crawling
        public DateTime AddedTime { get; set; }

        public Work(string url)
        {
            Url = url;
            Key = GetKeyFromUrl(url);
            AddedTime = DateTime.Now;

            Success = false;
            Transferred = false;
            IsDownloaded = false;
            DownloadLocation = null;
        }
        public Work() { }


        public bool IsEligibleForCrawl()
        {
            if (RecrawlDate != null && DateTime.Now.Subtract(RecrawlDate.Value).TotalMinutes < 0) return false;

            return true;
        }

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
        public string Size { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string DownloadedTime { get; set; }

        public DownloadedWork(string path, long? size)
        {
            FilePath = path;
            FileName = Path.GetFileName(path);
            Size = size == null ? "-" : ByteSizeToString(size.Value);
            DownloadedTime = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
        }

        /// <summary>
        /// Convert size in bytes to string
        /// </summary>
        /// <param name="sizeInBytes">Size in bytes</param>
        public static string ByteSizeToString(long sizeInBytes)
        {
            if (sizeInBytes < 1024) return $"{sizeInBytes} bytes";
            else if (sizeInBytes < 1024 * 1024) return $"{Math.Round(sizeInBytes / 1024.0, 2)} kB";
            else if (sizeInBytes < 1024 * 1024 * 1024) return $"{Math.Round((sizeInBytes / 1024.0) / 1024.0, 2)} MB";
            else return $"{Math.Round(((sizeInBytes / 1024.0) / 1024.0) / 1024.0, 2)} GB";
        }
    }

    [MessagePackObject]
    public class FileTransferInfo
    {
        [Key("Url")]
        public string Url { get; set; }

        [Key("Size")]
        public long Size { get; set; }

        [Key("Location")]
        public string Location { get; set; }
    }

    [MessagePackObject]
    public class FileChunk
    {
        [Key("Size")]
        public int Size { get; set; }

        [Key("Data")]
        public byte[] Data { get; set; }

        [Key("Location")]
        public string Location { get; set; }
    }
}
