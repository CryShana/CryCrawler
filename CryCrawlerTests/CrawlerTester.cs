using CryCrawler;
using CryCrawler.Worker;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace CryCrawlerTests
{
    public class CrawlerTester
    {
        [Fact]
        public void FilenamesTest()
        {
            var config = new Configuration();
            var database = new CacheDatabase("filenames_test");
            database.EnsureNew();

            var manager = new WorkManager(config.WorkerConfig, database);
            var crawler = new Crawler(manager, config.WorkerConfig);

            // CHECK FILES
            var f1 = crawler.GetFilename("https://www.youtube.com/playlist?list=PLJcysJ8SvKlA4NQGW_Qh-UF7sdpC0Mo", "text/html");
            Assert.Equal("playlist.htm", f1);

            var f2 = crawler.GetFilename("https://www.facebook.com/", "text/html");
            Assert.EndsWith(".htm", f2);

            var f3 = crawler.GetFilename("https://www.reddit.com/", "application/zip");
            Assert.EndsWith(".zip", f3);

            var f4 = crawler.GetFilename("https://stackoverflow.com/questions/2435594/net-path", "application/javascript");
            Assert.Equal("net-path.js", f4);

            var f5 = crawler.GetFilename("https://outlook.live.com/mail/inbox", "text/css");
            Assert.Equal("inbox.css", f5);

            var f6 = crawler.GetFilename("https://pbs.twimg.com/media/D8NsdsdsAIA8vP.png:large", "image/png");
            Assert.Equal("D8NsdsdsAIA8vP.png", f6);

            var f7 = crawler.GetFilename("https://pbs.twimg.com/D8NsdsdsAIA8vP.png", "image/png");
            Assert.Equal("D8NsdsdsAIA8vP.png", f7);

            var f8 = crawler.GetFilename("https://pbs.twimg.com/D8NsdsdsAIA8vP", "image/png");
            Assert.Equal("D8NsdsdsAIA8vP.png", f8);

            var f9 = crawler.GetFilename("https://cryshana.me/viewer/bsrhroae1pd.jpg?d=true", "image/jpeg");
            Assert.Equal("bsrhroae1pd.jpg", f9);

            // CHECK DIRECTORIES
            var d1 = crawler.GetDirectoryPath("https://www.youtube.com/playlist?list=PLJcysJ8SvKlA4NQGW_Qh-UF7sdpC0Mo");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\www.youtube.com", d1);

            var d2 = crawler.GetDirectoryPath("https://www.facebook.com/");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\www.facebook.com", d2);

            var d3 = crawler.GetDirectoryPath("https://www.reddit.com/");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\www.reddit.com", d3);

            var d4 = crawler.GetDirectoryPath("https://stackoverflow.com/questions/2435594/net-path");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\stackoverflow.com\\questions\\2435594", d4);

            var d5 = crawler.GetDirectoryPath("https://outlook.live.com/mail/inbox");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\outlook.live.com\\mail", d5);

            var d6 = crawler.GetDirectoryPath("https://pbs.twimg.com/media/D8NsdsdsAIA8vP.png:large");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\pbs.twimg.com\\media", d6);

            var d7 = crawler.GetDirectoryPath("https://pbs.twimg.com/D8Nwd7lUcAIA8vP.png");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\pbs.twimg.com", d7);

            var d8 = crawler.GetDirectoryPath("https://pbs.twimg.com/D8Nwd7lUcAIA8vP");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\pbs.twimg.com", d8);

            var d9 = crawler.GetDirectoryPath("https://cryshana.me/viewer/bsrhroae1pd.jpg?d=true");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\cryshana.me\\viewer", d9);

            var d10 = crawler.GetDirectoryPath("https://cryshana.me");
            Assert.Equal($"{config.WorkerConfig.DownloadsPath}\\cryshana.me", d10);

            database.Dispose();
            database.Delete();
        }
    }
}
