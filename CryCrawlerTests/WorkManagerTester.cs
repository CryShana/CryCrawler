using Xunit;
using System;
using CryCrawler;
using CryCrawler.Worker;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using LiteDB;
using System.Collections;
using System.Linq;

namespace CryCrawlerTests
{
    public class WorkManagerTester
    {
        [Fact]
        public void CachingFIFO()
            => MakeEnvironment("testing_wm_fifo",
                (database, config, wm, memLimit) =>
                {
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

                    wm.GetWork(out Work w);
                    var url = w.Url;

                    Assert.Equal("google1.com", url);
                    Assert.Equal(1, wm.CachedWorkCount);
                    Assert.Equal(4, wm.Backlog.Count);
                    Assert.Equal(5, wm.WorkCount);

                    wm.GetWork(out w);
                    url = w.Url;

                    Assert.Equal("google2.com", url);
                    Assert.Equal(0, wm.CachedWorkCount);
                    Assert.Equal(4, wm.Backlog.Count);
                    Assert.Equal(4, wm.WorkCount);

                    wm.GetWork(out w);
                    url = w.Url;

                    Assert.Equal("google3.com", url);
                    Assert.Equal(0, wm.CachedWorkCount);
                    Assert.Equal(3, wm.Backlog.Count);
                    Assert.Equal(3, wm.WorkCount);

                    wm.GetWork(out w);
                    url = w.Url;

                    Assert.Equal("google4.com", url);
                    Assert.Equal(0, wm.CachedWorkCount);
                    Assert.Equal(2, wm.Backlog.Count);
                    Assert.Equal(2, wm.WorkCount);

                    wm.GetWork(out w);
                    url = w.Url;

                    Assert.Equal("google5.com", url);
                    Assert.Equal(0, wm.CachedWorkCount);
                    Assert.Equal(1, wm.Backlog.Count);
                    Assert.Equal(1, wm.WorkCount);

                }, false, 4);

        [Fact]
        public void CachingLIFO()
            => MakeEnvironment("testing_wm_lifo",
                (database, config, wm, memLimit) =>
                {
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

                    wm.GetWork(out Work w);
                    var url = w.Url;

                    Assert.Equal("google6.com", url);
                    Assert.Equal(1, wm.CachedWorkCount);
                    Assert.Equal(4, wm.Backlog.Count);
                    Assert.Equal(5, wm.WorkCount);

                    wm.GetWork(out w);
                    url = w.Url;

                    Assert.Equal("google5.com", url);
                    Assert.Equal(0, wm.CachedWorkCount);
                    Assert.Equal(4, wm.Backlog.Count);
                    Assert.Equal(4, wm.WorkCount);

                    wm.GetWork(out w);
                    url = w.Url;

                    Assert.Equal("google4.com", url);
                    Assert.Equal(0, wm.CachedWorkCount);
                    Assert.Equal(3, wm.Backlog.Count);
                    Assert.Equal(3, wm.WorkCount);

                    wm.GetWork(out w);
                    url = w.Url;

                    Assert.Equal("google3.com", url);
                    Assert.Equal(0, wm.CachedWorkCount);
                    Assert.Equal(2, wm.Backlog.Count);
                    Assert.Equal(2, wm.WorkCount);

                    wm.GetWork(out w);
                    url = w.Url;

                    Assert.Equal("google2.com", url);
                    Assert.Equal(0, wm.CachedWorkCount);
                    Assert.Equal(1, wm.Backlog.Count);
                    Assert.Equal(1, wm.WorkCount);

                }, true, 4);

        [Fact]
        public void ParallelCachingFIFO()
            => MakeEnvironment("testing_wm_parallel_fifo",
                (database, config, wm, memLimit) =>
                {
                    var urls = new[] {
                        "google1.com", "google2.com", "google3.com", "google4.com", "google5.com", "google6.com", "google7.com", "google8.com", "google9.com",
                        "google10.com", "google11.com", "google12.com", "google13.com", "google14.com", "google15.com", "google16.com", "google17.com", "google18.com",
                        "google19.com", "google20.com", "google21.com", "google22.com", "google23.com", "google24.com", "google25.com", "google26.com", "google27.com"
                    };

                    Assert.Equal(0, wm.WorkCount);
                    Parallel.ForEach(urls, a => wm.AddToBacklog(a));

                    Assert.Equal(urls.Length, wm.WorkCount);
                    Assert.Equal(memLimit, wm.Backlog.Count);
                    Assert.Equal(urls.Length - memLimit, wm.CachedWorkCount);

                }, false, 4);

        [Fact]
        public void BulkCachingFIFO()
            => MakeEnvironment("testing_wm_performance_test_cache_bulk_fifo",
                (database, config, wm, memLimit) =>
                {
                    var generateCount = 1000;

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
                }, false, 4);

        [Fact]
        public void BulkCachingLIFO()
            => MakeEnvironment("testing_wm_performance_test_cache_bulk_lifo",
                (database, config, wm, memLimit) =>
                {
                    var generateCount = 1000;

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
                }, true, 4);

        [Fact]
        public void PerformanceTest()
            => MakeEnvironment("testing_wm_performance_test",
                (database, config, wm, memLimit) =>
            {
                // generate URLs
                var urls = new List<string>();
                for (int i = 0; i < memLimit; i++) urls.Add($"google-someurl-this-url{(i + 1)}-needs-to-be-relatively-long/page{i}/host{i}");

                var sw = Stopwatch.StartNew();
                Parallel.ForEach(urls, a => wm.AddToBacklog(a));
                sw.Stop();
                var time = sw.ElapsedMilliseconds;

                Assert.Equal(memLimit, wm.WorkCount);
                Assert.True(time < 400);
            });

        [Fact]
        public void CachePerformanceTest()
             => MakeEnvironment("testing_wm_performance_test_cache",
                 (database, config, wm, memLimit) =>
             {
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

             }, false, 100000);

        [Fact]
        public void CachePerformanceTestBulk()
            => MakeEnvironment("testing_wm_performance_test_cache_bulk",
                (database, config, wm, memLimit) =>
            {
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
            }, false, 100000);

        [Fact]
        public void UrlCrawledTest()
            => MakeEnvironment("testing_wm_urlcrawled",
                (database, config, wm, memLimit) =>
        {
            var url = "http://google.com/";
            wm.AddToCrawled(url);

            var count = database.GetWorkCount(CacheDatabase.Collection.CachedCrawled);
            Assert.Equal(1, count);

            wm.AddToCrawled(url);

            count = database.GetWorkCount(CacheDatabase.Collection.CachedCrawled);
            Assert.Equal(1, count);

            var isCrawled = wm.IsUrlCrawled(url);
            Assert.True(isCrawled);

            isCrawled = wm.IsUrlCrawled(url + "test");
            Assert.False(isCrawled);
        });

        [Fact]
        public void DatabaseTest()
            => MakeEnvironment("testing_wm_databasetest",
                (database, config, wm, memLimit) =>
                {
                    var w1 = new Work("http://google0.com/");
                    var w2 = new Work("http://google1.com/");
                    var w3 = new Work("http://google2.com/");
                    var w4 = new Work("http://google3.com/");

                    var count = database.GetWorkCount(CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(0, count);

                    // insert into backlog cache
                    database.Insert(w1, CacheDatabase.Collection.CachedBacklog);

                    count = database.GetWorkCount(CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(1, count);

                    count = database.GetWorkCount(CacheDatabase.Collection.CachedCrawled);
                    Assert.Equal(0, count);

                    // insert into crawled cache
                    database.Insert(w1, CacheDatabase.Collection.CachedCrawled);

                    count = database.GetWorkCount(CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(1, count);

                    count = database.GetWorkCount(CacheDatabase.Collection.CachedCrawled);
                    Assert.Equal(1, count);

                    // insert new one into backlog
                    database.Insert(w2, CacheDatabase.Collection.CachedBacklog);

                    count = database.GetWorkCount(CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(2, count);

                    count = database.GetWorkCount(CacheDatabase.Collection.CachedCrawled);
                    Assert.Equal(1, count);

                    // get all works from backlog
                    database.GetWorks(out List<Work> ws, -1, true, false, CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(2, ws.Count);
                    Assert.Equal(w1.Url, ws[0].Url);
                    Assert.Equal(w2.Url, ws[1].Url);
                    count = database.GetWorkCount(CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(2, count);

                    // get all works but autoremove them
                    database.GetWorks(out ws, -1, false, true, CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(2, ws.Count);
                    Assert.Equal(w2.Url, ws[0].Url);
                    Assert.Equal(w1.Url, ws[1].Url);
                    count = database.GetWorkCount(CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(0, count);

                    // insert both works in again - this time using Upsert
                    database.Upsert(w1, out bool wasIns1, CacheDatabase.Collection.CachedBacklog);
                    database.Upsert(w2, out bool wasIns2, CacheDatabase.Collection.CachedBacklog);
                    count = database.GetWorkCount(CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(2, count);
                    Assert.True(wasIns1);
                    Assert.True(wasIns2);

                    // get W1 from backlog
                    database.GetWork(out Work w11, w1.Url, CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(w1.Url, w11.Url);
                    Assert.False(w11.IsDownloaded);

                    // change "IsDownloaded" and Upsert it
                    w1.IsDownloaded = true;
                    database.Upsert(w1, out bool wasIns, CacheDatabase.Collection.CachedBacklog);
                    Assert.False(wasIns);

                    // get W1 from backlog again and check again
                    database.GetWork(out w11, w1.Url, CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(w1.Url, w11.Url);
                    Assert.True(w11.IsDownloaded);

                    // find works with IsDownloaded = false
                    database.FindWorks(out IEnumerable<Work> works, 
                        Query.EQ("IsDownloaded", new BsonValue(false)), CacheDatabase.Collection.CachedBacklog);
                    Assert.Single(works);

                    // find all works
                    database.FindWorks(out works, Query.All(), CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(2, works.Count());

                    // delete only works with IsDownloaded = true
                    database.DeleteWorks(out int dcount, Query.EQ("IsDownloaded", new BsonValue(true)), 
                        CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(1, dcount);

                    count = database.GetWorkCount(CacheDatabase.Collection.CachedBacklog);
                    Assert.Equal(1, count);

                    // try get W1 again
                    bool success = database.GetWork(out w11, w1.Url, CacheDatabase.Collection.CachedBacklog);
                    Assert.False(success);
                    Assert.True(ReferenceEquals(w11, null));

                    // find works with IsDownloaded = true
                    success = database.FindWorks(out works,
                        Query.EQ("IsDownloaded", new BsonValue(true)), CacheDatabase.Collection.CachedBacklog);
                    Assert.Empty(works);
                    Assert.False(success);

                    // DUPLICATE TEST
                    var works_bulk = new List<Work>();
                    for (int i = 0; i < 200; i++) works_bulk.Add(new Work("http://google.com"));

                    // insert 200 works with same Urls
                    database.InsertBulk(works_bulk, works_bulk.Count, out int inserted, CacheDatabase.Collection.DumpedBacklog);

                    count = database.GetWorkCount(CacheDatabase.Collection.DumpedBacklog);
                    Assert.Equal(works_bulk.Count, count);
                    Assert.Equal(count, inserted);

                    // drop collection
                    database.DropCollection(CacheDatabase.Collection.DumpedBacklog);

                    count = database.GetWorkCount(CacheDatabase.Collection.DumpedBacklog);
                    Assert.Equal(0, count);

                    // DUPLICATE TEST with IDs
                    works_bulk = new List<Work>();
                    for (int i = 0; i < 200; i++) works_bulk.Add(new Work("http://google.com") { Id = i + 1000 });

                    // insert 200 works with same Urls and different Ids
                    database.InsertBulk(works_bulk, works_bulk.Count, out inserted, CacheDatabase.Collection.DumpedBacklog);

                    count = database.GetWorkCount(CacheDatabase.Collection.DumpedBacklog);
                    Assert.Equal(works_bulk.Count, count);
                    Assert.Equal(count, inserted);

                    database.DropCollection(CacheDatabase.Collection.DumpedBacklog);

                    // DUPLICATE TEST with duplicate IDs
                    works_bulk = new List<Work>();
                    for (int i = 0; i < 200; i++) works_bulk.Add(new Work("http://google.com") { Id = ((i + 1) % 5) + 100 });

                    // insert 200 works with same Urls and duplicate Ids (should fail)
                    database.InsertBulk(works_bulk, works_bulk.Count, out inserted, CacheDatabase.Collection.DumpedBacklog);

                    count = database.GetWorkCount(CacheDatabase.Collection.DumpedBacklog);
                    Assert.Equal(0, count);
                    Assert.Equal(count, inserted);
                });
           

        void MakeEnvironment(string dbName,
            Action<CacheDatabase, WorkerConfiguration, WorkManager, int> action,
            bool depthSearch = false, int memLimit = 100000)
        {
            var database = new CacheDatabase(dbName);
            database.EnsureNew();

            var config = new WorkerConfiguration();
            config.DepthSearch = depthSearch;

            var wm = new WorkManager(config, database, memLimit);

            try
            {
                action(database, config, wm, memLimit);
            }
            finally
            {
                database.Dispose();
                database.Delete();
            }
        }
    }
}
