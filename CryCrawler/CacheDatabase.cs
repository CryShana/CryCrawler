using LiteDB;
using System;
using System.IO;
using CryCrawler.Worker;
using System.Collections.Generic;

namespace CryCrawler
{
    public static class CacheDatabase
    {
        const string BacklogName = "backlog";
        const string CrawledName = "crawled";

        const string DatabaseFile = "crycrawler_cache";
        
        static readonly LiteDatabase database;

        static CacheDatabase()
        {
            // TODO: improve this logic

            // 1 - run and read config defined URLs (if local) - only load if they haven't been crawled yet (keep Recrawl setting in mind)
            // 2 - Host given work takes priority, even if Url already crawled, do it again if given from Host
            // 3 - cache only remains if working locally - if working via Host, cache needs to be deleted on start (check this)

            // create new database file
            database = new LiteDatabase(DatabaseFile);
        }

        public static void Insert(Work w, bool asCrawled = false)
        {
            try
            {
                var col = database.GetCollection<Work>(asCrawled ? CrawledName : BacklogName);
                col.EnsureIndex(x => x.LastCrawled);
                col.EnsureIndex(x => x.AddedTime);
                col.EnsureIndex(x => x.Url);
                col.Insert(w);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to insert item to database! " + ex.Message, Logger.LogSeverity.Error);
            }
        }

        public static void InsertBulk(IEnumerable<Work> ws, int count, bool asCrawled = false)
        {
            try
            {
                var col = database.GetCollection<Work>(asCrawled ? CrawledName : BacklogName);
                col.EnsureIndex(x => x.LastCrawled);
                col.EnsureIndex(x => x.AddedTime);
                col.EnsureIndex(x => x.Url);
                col.InsertBulk(ws, count);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to insert bulk items to database! " + ex.Message, Logger.LogSeverity.Error);
            }
        }

        public static bool GetWorks(out IEnumerable<Work> works, int count, bool oldestFirst, bool remove = true, bool asCrawled = false)
        {
            try
            {
                var col = database.GetCollection<Work>(asCrawled ? CrawledName : BacklogName);
                col.EnsureIndex(x => x.AddedTime);

                // construct query
                var query = Query.All("AddedTime", oldestFirst ? Query.Ascending : Query.Descending);

                // get works
                works = col.Find(query, 0, count);

                // delete works
                if (remove) col.Delete(query);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to get works from database! " + ex.Message, Logger.LogSeverity.Error);
                works = null;
                return false;
            }
        }      

        public static bool GetWork(out Work work, string url, bool asCrawled = false)
        {
            try
            {
                var col = database.GetCollection<Work>(asCrawled ? CrawledName : BacklogName);
                col.EnsureIndex(x => x.Url);

                // get work
                work = col.FindOne(Query.Where("Url", x => x.AsString == url));

                return work != null;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to get work from database! " + ex.Message, Logger.LogSeverity.Error);
                work = null;
                return false;
            }
        }

        public static void Dispose() => database.Dispose();
    }
}
