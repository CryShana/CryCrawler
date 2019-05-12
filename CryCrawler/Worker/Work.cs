using System;

namespace CryCrawler.Worker
{
    public class Work
    {
        public long Id { get; set; }
        public string Url { get; set; }
        public DateTime? LastCrawled { get; set; }
        public DateTime AddedTime { get; set; }

        public Work(string url)
        {
            Url = url;
            AddedTime = DateTime.Now;
        }
        public Work() { }
    }
}
