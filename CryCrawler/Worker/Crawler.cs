using System;
using System.Text;
using System.Collections.Generic;

namespace CryCrawler.Worker
{
    /// <summary>
    /// Crawls through URLs supplied by WorkManager based on configuration
    /// </summary>
    public class Crawler
    {
        public bool IsActive { get; private set; }
        public WorkManager Manager { get; }

        public Crawler(WorkManager manager, WorkerConfiguration config)
        {
            Manager = manager;
        }

        public void Start()
        {
            IsActive = true;
        }

        public void Stop()
        {
            IsActive = false;
        }
    }
}
