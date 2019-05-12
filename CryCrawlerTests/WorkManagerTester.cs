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
    }
}
