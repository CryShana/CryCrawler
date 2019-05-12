using LiteDB;
using System;
using CryCrawler.Worker;
using System.Collections.Generic;
using System.IO;

namespace CryCrawler
{
    public static class CacheDatabase
    {
        const string BacklogName = "backlog";
        const string CrawledName = "crawled";

        const string DatabaseFile = "crycrawler_cache";
        
        static LiteDatabase database;

        static CacheDatabase() => database = new LiteDatabase(DatabaseFile);
        
        public static bool Insert(Work w, bool asCrawled = false)
        {
            try
            {
                var col = database.GetCollection<Work>(asCrawled ? CrawledName : BacklogName);
                col.EnsureIndex(x => x.LastCrawled);
                col.EnsureIndex(x => x.AddedTime);
                col.EnsureIndex(x => x.Url);
                col.Insert(w);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to insert item to database! " + ex.Message, Logger.LogSeverity.Error);
                return false;
            }
        }

        public static bool InsertBulk(IEnumerable<Work> ws, int count, bool asCrawled = false)
        {
            try
            {
                var col = database.GetCollection<Work>(asCrawled ? CrawledName : BacklogName);
                col.EnsureIndex(x => x.LastCrawled);
                col.EnsureIndex(x => x.AddedTime);
                col.EnsureIndex(x => x.Url);
                col.InsertBulk(ws, count);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to insert bulk items to database! " + ex.Message, Logger.LogSeverity.Error);
                return false;
            }
        }

        public static bool GetWorks(out IEnumerable<Work> works, int count, bool oldestFirst, bool remove = true, bool asCrawled = false)
        {
            if (count <= 0) return;

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

        public static long GetWorkCount(bool asCrawled = false)
        {
            try
            {
                var col = database.GetCollection<Work>(asCrawled ? CrawledName : BacklogName);
                return col.LongCount();
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to get work count from database! " + ex.Message, Logger.LogSeverity.Error);
                return 0;
            }
        }

        public static void Recreate()
        {
            database.Dispose();

            if (File.Exists(DatabaseFile)) File.Delete(DatabaseFile);

            database = new LiteDatabase(DatabaseFile);
        }

        public static void Dispose() => database.Dispose();
    }
}
