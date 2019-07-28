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
    }
}
