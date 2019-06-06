using LiteDB;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using CryCrawler.Worker;
using System.Collections.Generic;

namespace CryCrawler
{
    public class CacheDatabase
    {
        public const string DefaultFilename = "crycrawler_cache";
        public const int MaxIndexLength = 512;

        const string DumpedName = "dumped";
        const string BacklogName = "backlog";
        const string CrawledName = "crawled";
        readonly string filename = DefaultFilename;
        readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        public bool Disposing { get; private set; }

        LiteDatabase database;

        LiteCollection<Work> backlogCollection;
        LiteCollection<Work> crawledCollection;
        LiteCollection<Work> dumpedCollection;

        public CacheDatabase(string filename)
        {
            // here is the problem
            this.filename = filename;
            database = new LiteDatabase(filename);

            PrepareCollections();
        }

        /// <summary>
        /// Insert specified work to database
        /// </summary>
        /// <param name="w">Work to insert</param>
        /// <param name="collection">Collection to insert into</param>
        /// <returns>Whether the operation was successful or not</returns>
        public bool Insert(Work w, Collection collection = Collection.CachedBacklog)
        {
            semaphore.Wait();
            try
            {
                GetCollection(collection).Insert(w);
                return true;
            }
            catch (Exception ex)
            {
                if (Disposing) return false;
                Logger.Log("Failed to insert item to database! " + ex.Message, Logger.LogSeverity.Error);
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        }


        /// <summary>
        /// Insert multiple works to database. Optimized for inserting 100 - 100000 works. Automatically splits larger collections.
        /// </summary>
        /// <param name="ws">Works to insert</param>
        /// <param name="count">Number of works to insert</param>
        /// <param name="inserted">Number of works successfully inserted</param>
        /// <param name="collection">Collection to insert into</param>
        /// <returns>Whether the operation was successful or not</returns>
        public bool InsertBulk(IEnumerable<Work> ws, int count, out int inserted, Collection collection = Collection.CachedBacklog)
        {
            // Handle if count is out of range 
            inserted = 0;
            const int minItems = 100;
            const int maxItems = 100000;

            if (count < minItems)
            {
                // Insert every item separately
                foreach (var w in ws)
                    if (Insert(w, collection))
                        inserted++;

                return true;
            }
            else if (count > maxItems)
            {
                // Split items into multiple bulks
                List<Work> works;
                if (ws is List<Work>) works = ws as List<Work>;
                else works = ws.ToList();

                int offset = 0;
                while (offset < works.Count)
                {
                    var space = works.Count - offset;
                    var cnt = maxItems < space ? maxItems : space;
                    var split_works = works.GetRange(offset, cnt);

                    if (InsertBulk(split_works, count, out int ins))
                    {
                        inserted += ins;
                        offset += ins;
                    }
                    else throw new DatabaseErrorException("Failed to bulk insert!");
                }
                return true;
            }
            else
            {
                // Do normal bulk insert
                semaphore.Wait();
                try
                {
                    GetCollection(collection).InsertBulk(ws, count);
                    inserted += count;
                    return true;
                }
                catch (Exception ex)
                {
                    if (Disposing) return false;
                    Logger.Log("Failed to insert bulk items to database! " + ex.Message, Logger.LogSeverity.Error);
                    return false;
                }
                finally
                {
                    semaphore.Release();
                }
            }        
        }

        /// <summary>
        /// Insert or update existing work based on URL (not Id)
        /// </summary>
        /// <param name="w">Work to insert or update</param>
        /// <param name="collection">Collection to use</param>
        /// <returns>Whether the operation was successful or not</returns>
        public bool Upsert(Work w, out bool wasInserted, Collection collection = Collection.CachedBacklog)
        {
            semaphore.Wait();
            try
            {
                var col = GetCollection(collection);
                var work = getWork(w.Url, collection);
                if (work == null)
                {
                    // insert work
                    wasInserted = true;
                    col.Insert(w);
                    return true;
                }
                else
                {
                    // update existing
                    w.Id = work.Id;
                    wasInserted = false;
                    return col.Update(w);
                }
            }
            catch (Exception ex)
            {
                wasInserted = false;

                if (Disposing) return false;
                Logger.Log("Failed to upsert item to database! " + ex.Message, Logger.LogSeverity.Error);
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Get multiple works in specified order and auto-remove them if necessary
        /// </summary>
        /// <param name="works">Retrieved works</param>
        /// <param name="count">Number of works to retrieve. Set -1 for all works.</param>
        /// <param name="oldestFirst">Order (true for Ascending)</param>
        /// <param name="autoRemove">Automatically remove retrieved works from database</param>
        /// <param name="collection">Collection to search</param>
        /// <returns>Whether works were retrieved successfully or not</returns>
        public bool GetWorks(out List<Work> works, int count, bool oldestFirst, bool autoRemove = false, Collection collection = Collection.CachedBacklog)
        {
            if (count == -1)
            {
                works = GetCollection(collection).FindAll().ToList();
                return true;
            }

            if (count <= 0 )
            {
                works = null;
                return false;
            }

            semaphore.Wait();
            try
            {
                var col = GetCollection(collection);

                // construct query
                var query = Query.All("AddedTime", oldestFirst ? Query.Ascending : Query.Descending);

                // get works
                works = col.Find(query, 0, count).ToList();
                if (works.Count == 0) return false;

                // delete works
                if (autoRemove) foreach (var i in works) col.Delete(i.Id);  // TODO: LiteDB v5 will support batch deletion apparently - use that for performance reasons!

                return true;
            }
            catch (Exception ex)
            {
                works = null;

                if (Disposing) return false;
                Logger.Log("Failed to get works from database! " + ex.Message, Logger.LogSeverity.Error);
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Get work based on URL
        /// </summary>
        /// <param name="work">Found work</param>
        /// <param name="url">URL to find (case-sensitive)</param>
        /// <param name="collection">Collection to search</param>
        /// <returns>Whether work was successfully found or not</returns>
        public bool GetWork(out Work work, string url, Collection collection = Collection.CachedBacklog)
        {
            if (string.IsNullOrEmpty(url))
            {
                work = null;
                return false;
            }

            semaphore.Wait();
            try
            {
                work = getWork(url, collection);

                return work != null;
            }
            catch (Exception ex)
            {
                work = null;

                if (Disposing) return false;
                Logger.Log("Failed to get work from database! " + ex.Message, Logger.LogSeverity.Error);
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Get number of cached works
        /// </summary>
        /// <param name="collection">Collection to search</param>
        /// <returns>Number of cached works</returns>
        public long GetWorkCount(Collection collection = Collection.CachedBacklog)
        {
            semaphore.Wait();
            try
            {
                return GetCollection(collection).LongCount();
            }
            catch (Exception ex)
            {
                if (Disposing) return 0;
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
                Delete();

                database = new LiteDatabase(filename);

                PrepareCollections();
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Releases database resources. This does not delete the database.
        /// </summary>
        public void Dispose()
        {
            Disposing = true;
            database.Dispose();
        }

        /// <summary>
        /// Delete the database.
        /// </summary>
        public void Delete()
        {
            if (!File.Exists(filename)) return;

            database.Dispose();

            File.Delete(filename);
        }

        /// <summary>
        /// Clear all items from collection
        /// </summary>
        public void DropCollection(Collection collection) => database.DropCollection(GetCollection(collection).Name);          
        
        private Work getWork(string url, Collection collection)
        {
            if (string.IsNullOrEmpty(url) || Disposing) return null;

            // need to search by key that is limited with 512 bytes
            var k = Work.GetKeyFromUrl(url);

            var works = GetCollection(collection).Find(Query.Where("Key", x => x.AsString == k));
            return works?.Where(x => x.Url == url)?.FirstOrDefault();
        }

        private void PrepareCollections()
        {
            // WARNING: Calling EnsureIndex() on a large collection causes a huge memory usage spike

            crawledCollection = database.GetCollection<Work>(CrawledName);
            backlogCollection = database.GetCollection<Work>(BacklogName);
            dumpedCollection = database.GetCollection<Work>(DumpedName);

            // Indexing only necessary on first database initialization
            if (!database.CollectionExists(CrawledName))
            {
                crawledCollection.EnsureIndex(x => x.AddedTime);
                crawledCollection.EnsureIndex(x => x.Key);
            }
            if (!database.CollectionExists(BacklogName))
            {
                backlogCollection.EnsureIndex(x => x.AddedTime);
                backlogCollection.EnsureIndex(x => x.Key);
            }
            if (!database.CollectionExists(DumpedName))
            {
                dumpedCollection.EnsureIndex(x => x.AddedTime);
                dumpedCollection.EnsureIndex(x => x.Key);
            }
        }

        private LiteCollection<Work> GetCollection(Collection collection)
        {
            switch (collection)
            {
                case Collection.CachedBacklog:
                    return backlogCollection;
                case Collection.CachedCrawled:
                    return crawledCollection;
                case Collection.DumpedBacklog:
                    return dumpedCollection;
                default:
                    return null;
            }
        }

        public enum Collection
        {
            DumpedBacklog,
            CachedBacklog,
            CachedCrawled
        }

        public class DatabaseErrorException : Exception
        {
            public DatabaseErrorException(string msg) : base(msg) { }
        }
    }
}
