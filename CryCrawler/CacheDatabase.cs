using LiteDB;
using System;
using CryCrawler.Worker;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CryCrawler
{
    public class CacheDatabase
    {
        public const string DefaultFilename = "crycrawler_cache";

        const string BacklogName = "backlog";
        const string CrawledName = "crawled";
        readonly string filename = DefaultFilename;
        readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

        LiteDatabase database;

        public CacheDatabase(string filename)
        {
            this.filename = filename;
            database = new LiteDatabase(filename);
        }

        public bool Insert(Work w, bool asCrawled = false)
        {
            semaphore.Wait();
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
            finally
            {
                semaphore.Release();
            }
        }

        public bool InsertBulk(IEnumerable<Work> ws, int count, bool asCrawled = false)
        {
            semaphore.Wait();
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
            finally
            {
                semaphore.Release();
            }
        }

        public bool GetWorks(out List<Work> works, int count, bool oldestFirst, bool remove = true, bool asCrawled = false)
        {
            if (count <= 0)
            {
                works = null;
                return false;
            }

            semaphore.Wait();
            try
            {
                var col = database.GetCollection<Work>(asCrawled ? CrawledName : BacklogName);
                col.EnsureIndex(x => x.AddedTime);

                // construct query
                var query = Query.All("AddedTime", oldestFirst ? Query.Ascending : Query.Descending);

                // get works
                works = col.Find(query, 0, count).ToList();
                if (works.Count == 0) return false;

                // delete works
                if (remove) foreach (var i in works) col.Delete(i.Id);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to get works from database! " + ex.Message, Logger.LogSeverity.Error);
                works = null;
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public bool GetWork(out Work work, string url, bool asCrawled = false)
        {
            semaphore.Wait();
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
            finally
            {
                semaphore.Release();
            }
        }

        public long GetWorkCount(bool asCrawled = false)
        {
            semaphore.Wait();
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
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Deletes old database and creates new one
        /// </summary>
        public void EnsureNew()
        {
            if (!File.Exists(filename)) return;

            semaphore.Wait();
            try
            {
                database.Dispose();

                File.Delete(filename);

                database = new LiteDatabase(filename);
            }
            finally
            {
                semaphore.Release();
            }
        }

        public void Dispose() => database.Dispose();
    }
}
