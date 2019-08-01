using CryCrawler;
using CryCrawler.Worker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace CryCrawlerTests
{
    public class ExtensionsTester
    {
        [Fact]
        public void FileCopying()
        {
            string temp = "";

            string text1 = "Hello World - 1";
            string path1 = "file1.txt";

            string text2 = "Hello World - 2";
            string path2 = "file1 (1).txt";

            try
            {
                File.WriteAllText(path1, text1);
                File.WriteAllText(path2, text2);

                temp = Extensions.GetTempFile("temp");
                File.WriteAllText(temp, text2);

                var path = Extensions.CopyToAndGetPath(temp, path1);

                Assert.Equal(path2, path);
            }
            catch
            {

            }
            finally
            {
                File.Delete(temp);
                File.Delete(path1);
                File.Delete(path2);
            }
        }

        [Fact]
        public void GettingDomainName()
        {
            string url1 = "https://regexr.com/",
                url2 = "https://regexr.com",
                url3 = "https://twitter.com/robots.txt",
                url4 = "http://developercommunity.visualstudio.com/hello/something?test=1",
                url5 = "www.google.com",
                url6 = "google.com";

            var domain = Extensions.GetDomainName(url1, out string protocol, out string path);
            Assert.Equal("regexr.com", domain);
            Assert.Equal("https", protocol);
            Assert.Equal("/", path);

            domain = Extensions.GetDomainName(url2, out protocol, out path);
            Assert.Equal("regexr.com", domain);
            Assert.Equal("https", protocol);
            Assert.Equal("/", path);

            domain = Extensions.GetDomainName(url3, out protocol, out path);
            Assert.Equal("twitter.com", domain);
            Assert.Equal("https", protocol);
            Assert.Equal("/robots.txt", path);

            domain = Extensions.GetDomainName(url4, out protocol, out path);
            Assert.Equal("developercommunity.visualstudio.com", domain);
            Assert.Equal("http", protocol);
            Assert.Equal("/hello/something?test=1", path);

            domain = Extensions.GetDomainName(url5, out protocol, out path);
            Assert.Equal("www.google.com", domain);
            Assert.Equal("http", protocol);
            Assert.Equal("/", path);

            domain = Extensions.GetDomainName(url6, out protocol, out path);
            Assert.Equal("google.com", domain);
            Assert.Equal("http", protocol);
            Assert.Equal("/", path);
        }
    }
}
