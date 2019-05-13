using CryCrawler;
using CryCrawler.Worker;
using System;
using Xunit;

namespace CryCrawlerTests
{
    public class WorkManagerTester
    {
        [Fact]
        public void CachingFIFO()
        {
            var database = new CacheDatabase("testing_fifo");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = false; 

            var wm = new WorkManager(config, database, 4);

            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(0, wm.Backlog.Count);

            wm.AddToBacklog("google1.com");
            wm.AddToBacklog("google2.com");
            wm.AddToBacklog("google3.com");
            wm.AddToBacklog("google4.com");

            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);

            wm.AddToBacklog("google5.com"); // this should be cached
            wm.AddToBacklog("google6.com"); // this should be cached

            Assert.Equal(2, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);

            wm.GetWork(out string url);

            Assert.Equal("google1.com", url);
            Assert.Equal(1, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);

            wm.GetWork(out url);

            Assert.Equal("google2.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);

            wm.GetWork(out url);

            Assert.Equal("google3.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(3, wm.Backlog.Count);

            wm.GetWork(out url);

            Assert.Equal("google4.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(2, wm.Backlog.Count);

            wm.GetWork(out url);

            Assert.Equal("google5.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(1, wm.Backlog.Count);

            database.Dispose();
        }

        [Fact]
        public void CachingLIFO()
        {
            var database = new CacheDatabase("testing_lifo");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = true;

            var wm = new WorkManager(config, database, 4);

            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(0, wm.Backlog.Count);

            wm.AddToBacklog("google1.com");
            wm.AddToBacklog("google2.com");
            wm.AddToBacklog("google3.com");
            wm.AddToBacklog("google4.com");

            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);

            wm.AddToBacklog("google5.com"); // this should be cached
            wm.AddToBacklog("google6.com"); // this should be cached

            Assert.Equal(2, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);

            wm.GetWork(out string url);

            Assert.Equal("google6.com", url);
            Assert.Equal(1, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);

            wm.GetWork(out url);

            Assert.Equal("google5.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);

            wm.GetWork(out url);

            Assert.Equal("google4.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(3, wm.Backlog.Count);

            wm.GetWork(out url);

            Assert.Equal("google3.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(2, wm.Backlog.Count);

            wm.GetWork(out url);

            Assert.Equal("google2.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(1, wm.Backlog.Count);

            database.Dispose();
        }

        [Fact]
        public void ParallelCachingFIFO()
        {
            int maxBackLogSize = 4;

            var database = new CacheDatabase("testing_parallel_fifo");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = false;

            var wm = new WorkManager(config, database, maxBackLogSize);

            var urls = new[] {
                "google1.com", "google2.com", "google3.com", "google4.com", "google5.com", "google6.com", "google7.com", "google8.com", "google9.com",
                "google10.com", "google11.com", "google12.com", "google13.com", "google14.com", "google15.com", "google16.com", "google17.com", "google18.com",
                "google19.com", "google20.com", "google21.com", "google22.com", "google23.com", "google24.com", "google25.com", "google26.com", "google27.com"
            };

            Assert.Equal(0, wm.WorkCount);
            Parallel.ForEach(urls, a => wm.AddToBacklog(a));

            Assert.Equal(urls.Length, wm.WorkCount);
            Assert.Equal(maxBackLogSize, wm.Backlog.Count);
            Assert.Equal(urls.Length - maxBackLogSize, wm.CachedWorkCount);

            database.Dispose();
        }
    }
}
