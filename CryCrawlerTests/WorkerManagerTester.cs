using Xunit;
using System;
using System.IO;
using CryCrawler;
using CryCrawler.Host;
using CryCrawler.Worker;

namespace CryCrawlerTests
{
    public class WorkerManagerTester
    {
        [Fact]
        public void PathTranslationTest()
            => MakeEnvironment("testing_wrm_path_test",
                (database, config, wm, memLimit) =>
                {
                    var workerm = new WorkerManager(wm, new Configuration
                    {
                        WorkerConfig = config
                    }, new FreeWorkerPicker());

                    var p = workerm.TranslateWorkerFilePathToHost($"hello.jpg");

                    Assert.True(p.Length > 0);
                    Assert.StartsWith(Directory.GetCurrentDirectory(), p);
                    Assert.Contains(config.DownloadsPath, p);
                });

        [Fact]
        public void PathDuplicateTest()
            => MakeEnvironment("testing_wrm_path_duplicate_test",
                (database, config, wm, memLimit) =>
                {
                    Directory.CreateDirectory(config.DownloadsPath);

                    var workerm = new WorkerManager(wm, new Configuration
                    {
                        WorkerConfig = config
                    }, new FreeWorkerPicker());

                    string p0 = null, p1 = null, p2 = null, p3 = null;
                    try
                    {
                        p0 = workerm.TranslateWorkerFilePathToHost($"hello.jpg");
                        Assert.Contains(config.DownloadsPath, p0);
                        Assert.EndsWith("hello.jpg", p0);
                        File.Create(p0).Close();

                        p1 = workerm.TranslateWorkerFilePathToHost($"hello.jpg");
                        Assert.EndsWith("hello (1).jpg", p1);
                        File.Create(p1).Close();

                        p2 = workerm.TranslateWorkerFilePathToHost($"hello.jpg");
                        Assert.EndsWith("hello (2).jpg", p2);
                        File.Create(p2).Close();

                        p3 = workerm.TranslateWorkerFilePathToHost($"helloo.jpg");
                        Assert.EndsWith("helloo.jpg", p3);
                    }
                    finally
                    {
                        if (!string.IsNullOrEmpty(p0)) File.Delete(p0);
                        if (!string.IsNullOrEmpty(p1)) File.Delete(p1);
                        if (!string.IsNullOrEmpty(p2)) File.Delete(p2);
                    }
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
