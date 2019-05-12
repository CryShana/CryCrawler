using System;
using System.Collections.Generic;

namespace CryCrawler.Worker
{
    public class Work
    {
        public long Id { get; set; }
        public string Url { get; set; }
        public List<string> FoundDocuments { get; set; }  // use this to log every document found on site
        public DateTime? LastCrawled { get; set; }  // use this to log time of crawling
        public DateTime AddedTime { get; set; }

        public Work(string url)
        {
            Url = url;
            AddedTime = DateTime.Now;
        }
        public Work() { }
    }
}
