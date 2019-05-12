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
        const string ResultsName = "results";

        const string DatabaseFile = "crycrawler_cache";
        
        static readonly LiteDatabase database;

        static CacheDatabase()
        {
            // TODO: improve this logic

            // delete old database file if it exists
            if (File.Exists(DatabaseFile)) File.Delete(DatabaseFile);

            // create new database file
            database = new LiteDatabase(DatabaseFile);
        }

        public static void Insert(Work w)
        {
            try
            {
                var col = database.GetCollection<Work>(BacklogName);
                col.EnsureIndex(x => x.AddedTime);
                col.EnsureIndex(x => x.Url);
                col.Insert(w);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to insert item to database! " + ex.Message, Logger.LogSeverity.Error);
            }
        }

        public static void InsertBulk(IEnumerable<Work> ws, int count)
        {
            try
            {
                var col = database.GetCollection<Work>(BacklogName);
                col.EnsureIndex(x => x.AddedTime);
                col.EnsureIndex(x => x.Url);
                col.InsertBulk(ws, count);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to insert bulk items to database! " + ex.Message, Logger.LogSeverity.Error);
            }
        }

        public static bool GetWorks(out IEnumerable<Work> works, int count, bool oldestFirst, bool remove = true)
        {
            try
            {
                var col = database.GetCollection<Work>(BacklogName);
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

        public static void Dispose() => database.Dispose();
    }
}
