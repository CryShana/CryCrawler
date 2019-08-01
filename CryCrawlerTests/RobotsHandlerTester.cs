using CryCrawler;
using CryCrawler.Worker;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace CryCrawlerTests
{
    public class RobotsHandlerTester
    {
        [Fact]
        public void RegexMatchingTest()
        {
            // test if regex matching works as expected
            string p1 = "Test.com",
                p2 = "*",
                p3 = "Cry*Crawler",
                p4 = "Cry*Craw*ler";

            var r1 = RobotsHandler.GetRegexPattern(p1);
            var r2 = RobotsHandler.GetRegexPattern(p2);
            var r3 = RobotsHandler.GetRegexPattern(p3);
            var r4 = RobotsHandler.GetRegexPattern(p4);

            var m = Regex.IsMatch("test.com", r1, RegexOptions.IgnoreCase);
            Assert.True(m);
            m = Regex.IsMatch("test.com", r1);
            Assert.False(m);
            m = Regex.IsMatch("Test.com", r1);
            Assert.True(m);

            m = Regex.IsMatch("test.com", r2, RegexOptions.IgnoreCase);
            Assert.True(m);
            m = Regex.IsMatch("test.com", r2);
            Assert.True(m);
            m = Regex.IsMatch("", r2);
            Assert.True(m);

            m = Regex.IsMatch("test.com", r3);
            Assert.False(m);
            m = Regex.IsMatch("CryCrawler", r3);
            Assert.True(m);
            m = Regex.IsMatch("ACryCrawler", r3);
            Assert.False(m);
            m = Regex.IsMatch("CryACrawler", r3);
            Assert.True(m);
            m = Regex.IsMatch("cryCrawler", r3);
            Assert.False(m);
            m = Regex.IsMatch("cryCrawler", r3, RegexOptions.IgnoreCase);
            Assert.True(m);
            m = Regex.IsMatch("CryCrawADler", r3);
            Assert.False(m);

            m = Regex.IsMatch("CryCrawADler", r4);
            Assert.True(m);
            m = Regex.IsMatch("CryCrawler", r4);
            Assert.True(m);
        }

        [Fact]
        public void PriorityMatchingTest()
        {
            // test if more detailed rules have priority when matching patterns
        }

        [Fact]
        public void UserAgentMatchingTest()
        {
            // test if user agents get matched correctly
        }

        [Fact]
        public void GeneralTest1()
        {
            // general test
            string robotsTxt = @"
User-agent: *
Disallow: /admin
Disallow: /advertisements
Disallow: /artists
Disallow: /artist_commentaries
Disallow: /artist_commentary_versions
Disallow: /artist_versions
Disallow: /bans
Disallow: /comment_votes
Disallow: /comments
Disallow: /counts
Disallow: /delayed_jobs
Disallow: /dmails
Disallow: /favorite
Disallow: /iqdb_queries
Disallow: /ip_bans
Disallow: /janitor_trials";

            var config = new WorkerConfiguration() { UserAgent = "CryCrawler", RespectRobotsExclusionStandard = true };
            var robots = new RobotsHandler(config, new System.Net.Http.HttpClient());
            var data = robots.RegisterRobotsTxt("test.com", robotsTxt).Result;
            Assert.Empty(data.AllowedList);
            Assert.Equal(16, data.DisallowedList.Count);
            
            Assert.Contains(RobotsHandler.GetRegexPattern("/admin"), data.DisallowedList);
            Assert.Contains(RobotsHandler.GetRegexPattern("/bans"), data.DisallowedList);
            Assert.Contains(RobotsHandler.GetRegexPattern("/artist_commentary_versions"), data.DisallowedList);
            Assert.Contains(RobotsHandler.GetRegexPattern("/favorite"), data.DisallowedList);
            Assert.Contains(RobotsHandler.GetRegexPattern("/janitor_trials"), data.DisallowedList);

            var e = robots.IsUrlExcluded("http://test2.com/admin", null, false).Result;
            Assert.False(e);
            e = robots.IsUrlExcluded("http://test.com/admin").Result;
            Assert.True(e);
            e = robots.IsUrlExcluded("http://test.com/admin?url=test").Result;
            Assert.True(e);
            e = robots.IsUrlExcluded("http://test.com/admin/test/admin?url=test").Result;
            Assert.True(e);

            // special cases
            e = robots.IsUrlExcluded("http://test.com/test/admin").Result;
            Assert.False(e);
            e = robots.IsUrlExcluded("http://test.com/admin2").Result;
            Assert.False(e); 
        }

        [Fact]
        public void AdvancedTest()
        {
            // test multiple folders
            // test multiple wildards /*/...
            // test multiple user-agents
            // test if user-agents break after leaving
            // test prioritization
            // check if disallow: /   works

            // general test
            string robotsTxt = @"
User-agent: Googlebot
Allow: /testdomain2
Allow: /advertisements
Disallow: /artists
Disallow: /artist_commentaries
Disallow: /artist_commentary_versions
Disallow: /artist_versions
Disallow: /bans
Disallow: /comment_votes
Disallow: /comments
Disallow: /counts
Disallow: /delayed_jobs
Disallow: /dmails
Disallow: /favorite
Disallow: /iqdb_queries
Disallow: /ip_bans
Disallow: /janitor_trials

User-agent: CryCrawler
Disallow: /admin
Allow: /*?lang=
Allow: /hashtag/*?src=
Allow: /search?q=%23
Disallow: /search/realtime
Disallow: /search/users
Disallow: /search/*/grid
Disallow: /hashtag

User-agent: *
Disallow: /testdomain
Disallow: /advertisements
Disallow: /artists
";

            var config = new WorkerConfiguration() { UserAgent = "CryCrawler", RespectRobotsExclusionStandard = true };
            var robots = new RobotsHandler(config, new System.Net.Http.HttpClient());
            var data = robots.RegisterRobotsTxt("test.com", robotsTxt).Result;
            Assert.Empty(data.AllowedList);
            Assert.Equal(16, data.DisallowedList.Count);

            Assert.Contains(RobotsHandler.GetRegexPattern("/admin"), data.DisallowedList);
            Assert.Contains(RobotsHandler.GetRegexPattern("/bans"), data.DisallowedList);
            Assert.Contains(RobotsHandler.GetRegexPattern("/artist_commentary_versions"), data.DisallowedList);
            Assert.Contains(RobotsHandler.GetRegexPattern("/favorite"), data.DisallowedList);
            Assert.Contains(RobotsHandler.GetRegexPattern("/janitor_trials"), data.DisallowedList);

            var e = robots.IsUrlExcluded("http://test2.com/admin", null, false).Result;
            Assert.False(e);
            e = robots.IsUrlExcluded("http://test.com/admin").Result;
            Assert.True(e);
            e = robots.IsUrlExcluded("http://test.com/admin?url=test").Result;
            Assert.True(e);
            e = robots.IsUrlExcluded("http://test.com/admin/test/admin?url=test").Result;
            Assert.True(e);

            // special cases
            e = robots.IsUrlExcluded("http://test.com/test/admin").Result;
            Assert.False(e);
            e = robots.IsUrlExcluded("http://test.com/admin2").Result;
            Assert.False(e); // TODO: this should be false
        }

        [Fact]
        public void TimerRemovalTest()
        {
            // test if timer correctly removes old entries
        }
    }
}
