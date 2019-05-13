using Xunit;
using CryCrawler;
using CryCrawler.Worker;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CryCrawlerTests
{
    public class WorkManagerTester
    {
        [Fact]
        public void CachingFIFO()
        {
            var database = new CacheDatabase("testing_wm_fifo");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = false; 

            var wm = new WorkManager(config, database, 4);

            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(0, wm.Backlog.Count);
            Assert.Equal(0, wm.WorkCount);

            wm.AddToBacklog("google1.com");
            wm.AddToBacklog("google2.com");
            wm.AddToBacklog("google3.com");
            wm.AddToBacklog("google4.com");

            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);
            Assert.Equal(4, wm.WorkCount);

            wm.AddToBacklog("google5.com"); // this should be cached
            wm.AddToBacklog("google6.com"); // this should be cached

            Assert.Equal(2, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);
            Assert.Equal(6, wm.WorkCount);

            wm.GetWork(out string url);

            Assert.Equal("google1.com", url);
            Assert.Equal(1, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);
            Assert.Equal(5, wm.WorkCount);

            wm.GetWork(out url);

            Assert.Equal("google2.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);
            Assert.Equal(4, wm.WorkCount);

            wm.GetWork(out url);

            Assert.Equal("google3.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(3, wm.Backlog.Count);
            Assert.Equal(3, wm.WorkCount);

            wm.GetWork(out url);

            Assert.Equal("google4.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(2, wm.Backlog.Count);
            Assert.Equal(2, wm.WorkCount);

            wm.GetWork(out url);

            Assert.Equal("google5.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(1, wm.Backlog.Count);
            Assert.Equal(1, wm.WorkCount);

            database.Dispose();
            database.Delete();
        }

        [Fact]
        public void CachingLIFO()
        {
            var database = new CacheDatabase("testing_wm_lifo");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = true;

            var wm = new WorkManager(config, database, 4);

            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(0, wm.Backlog.Count);
            Assert.Equal(0, wm.WorkCount);

            wm.AddToBacklog("google1.com");
            wm.AddToBacklog("google2.com");
            wm.AddToBacklog("google3.com");
            wm.AddToBacklog("google4.com");

            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);
            Assert.Equal(4, wm.WorkCount);

            wm.AddToBacklog("google5.com"); // this should be cached
            wm.AddToBacklog("google6.com"); // this should be cached

            Assert.Equal(2, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);
            Assert.Equal(6, wm.WorkCount);

            wm.GetWork(out string url);

            Assert.Equal("google6.com", url);
            Assert.Equal(1, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);
            Assert.Equal(5, wm.WorkCount);

            wm.GetWork(out url);

            Assert.Equal("google5.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(4, wm.Backlog.Count);
            Assert.Equal(4, wm.WorkCount);

            wm.GetWork(out url);

            Assert.Equal("google4.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(3, wm.Backlog.Count);
            Assert.Equal(3, wm.WorkCount);

            wm.GetWork(out url);

            Assert.Equal("google3.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(2, wm.Backlog.Count);
            Assert.Equal(2, wm.WorkCount);

            wm.GetWork(out url);

            Assert.Equal("google2.com", url);
            Assert.Equal(0, wm.CachedWorkCount);
            Assert.Equal(1, wm.Backlog.Count);
            Assert.Equal(1, wm.WorkCount);

            database.Dispose();
            database.Delete();
        }

        [Fact]
        public void ParallelCachingFIFO()
        {
            int maxBackLogSize = 4;

            var database = new CacheDatabase("testing_wm_parallel_fifo");
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
            database.Delete();
        }

        [Fact]
        public void BulkCachingFIFO()
        {
            var database = new CacheDatabase("testing_wm_performance_test_cache_bulk_fifo");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = false;

            var memLimit = 4;
            var generateCount = 1000;
            var wm = new WorkManager(config, database, memLimit);

            // generate URLs
            var urls = new List<string>(generateCount);
            for (int i = 0; i < generateCount; i++) urls.Add($"google-someurl-this-url{(i + 1)}-needs-to-be-relatively-long/page{i}/host{i}");

            Assert.Equal(0, wm.WorkCount);
            Assert.Equal(0, wm.Backlog.Count);
            Assert.Equal(0, wm.CachedWorkCount);
            var sw = Stopwatch.StartNew();
            wm.AddToBacklog(urls);
            sw.Stop();
            var time = sw.ElapsedMilliseconds;

            Assert.Equal(generateCount, wm.WorkCount);
            Assert.Equal(memLimit, wm.Backlog.Count);
            Assert.Equal(generateCount - memLimit, wm.CachedWorkCount);
            Assert.True(time < 300);

            sw = Stopwatch.StartNew();
            database.GetWorks(out List<Work> wss, generateCount, true);
            for (int i = memLimit; i < generateCount; i++) Assert.True(wss[i - memLimit].Url == urls[i]);      
            sw.Stop();
            time = sw.ElapsedMilliseconds;

            Assert.True(time < 300);
        }

        [Fact]
        public void BulkCachingLIFO()
        {
            var database = new CacheDatabase("testing_wm_performance_test_cache_bulk_lifo");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = false;

            var memLimit = 4;
            var generateCount = 1000;
            var wm = new WorkManager(config, database, memLimit);

            // generate URLs
            var urls = new List<string>(generateCount);
            for (int i = 0; i < generateCount; i++) urls.Add($"google-someurl-this-url{(i + 1)}-needs-to-be-relatively-long/page{i}/host{i}");

            Assert.Equal(0, wm.WorkCount);
            Assert.Equal(0, wm.Backlog.Count);
            Assert.Equal(0, wm.CachedWorkCount);
            var sw = Stopwatch.StartNew();
            wm.AddToBacklog(urls);
            sw.Stop();
            var time = sw.ElapsedMilliseconds;

            Assert.Equal(generateCount, wm.WorkCount);
            Assert.Equal(memLimit, wm.Backlog.Count);
            Assert.Equal(generateCount - memLimit, wm.CachedWorkCount);
            Assert.True(time < 300);

            sw = Stopwatch.StartNew();
            database.GetWorks(out List<Work> wss, generateCount, false);
            for (int i = generateCount - 1 - memLimit; i >= 0; i--) Assert.True(wss[i].Url == urls[generateCount - 1 - i]);
            sw.Stop();
            time = sw.ElapsedMilliseconds;

            Assert.True(time < 300);
        }

        [Fact]
        public void PerformanceTest()
        {           
            var database = new CacheDatabase("testing_wm_performance_test");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = false;

            var memLimit = 100000;
            var wm = new WorkManager(config, database, memLimit);

            // generate URLs
            var urls = new List<string>();
            for (int i = 0; i < memLimit; i++) urls.Add($"google-someurl-this-url{(i + 1)}-needs-to-be-relatively-long/page{i}/host{i}");

            var sw = Stopwatch.StartNew();
            Parallel.ForEach(urls, a => wm.AddToBacklog(a));
            sw.Stop();
            var time = sw.ElapsedMilliseconds;

            Assert.Equal(memLimit, wm.WorkCount);
            Assert.True(time < 400);

            database.Dispose();
            database.Delete();
        }

        [Fact]
        public void CachePerformanceTest()
        {
            var database = new CacheDatabase("testing_wm_performance_test_cache");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = false;

            int memLimit = 100000;
            var wm = new WorkManager(config, database, memLimit);

            // generate URLs over memory limit
            int count = memLimit + 50;
            var urls = new List<string>();
            for (int i = 0; i < count; i++) urls.Add($"google-someurl-this-url{(i + 1)}-needs-to-be-relatively-long/page{i}/host{i}");

            var sw = Stopwatch.StartNew();
            Parallel.ForEach(urls, a => wm.AddToBacklog(a));
            sw.Stop();
            var time = sw.ElapsedMilliseconds;

            Assert.Equal(count, wm.WorkCount);
            Assert.True(time < 3000);

            database.Dispose();
            database.Delete();
        }

        [Fact]
        public void CachePerformanceTestBulk()
        {
            var database = new CacheDatabase("testing_wm_performance_test_cache_bulk");
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = false;

            int memLimit = 100000;
            var wm = new WorkManager(config, database, memLimit);

            // generate URLs over memory limit
            int count = memLimit + 10000;
            var urls = new List<string>();
            for (int i = 0; i < count; i++) urls.Add($"google-someurl-this-url{(i + 1)}-needs-to-be-relatively-long/page{i}/host{i}");

            var sw = Stopwatch.StartNew();
            wm.AddToBacklog(urls);
            sw.Stop();
            var time = sw.ElapsedMilliseconds;

            Assert.Equal(count, wm.WorkCount);
            Assert.True(time < 1500);

            database.Dispose();
            database.Delete();
        }
    }
}
