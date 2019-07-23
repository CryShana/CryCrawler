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
                    var workerm = new WorkerManager(wm, new Configuration {                 
                        WorkerConfig = config
                    }, new FreeWorkerPicker());

                    var p = workerm.TranslateWorkerFilePathToHost($"hello.jpg");

                    Assert.True(p.Length > 0);
                    Assert.StartsWith(Directory.GetCurrentDirectory(), p);
                    Assert.Contains(config.DownloadsPath, p);
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
