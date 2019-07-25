using LiteDB;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using CryCrawler.Worker;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CryCrawler
{
    public class CacheDatabase
    {
        public const string DefaultFilename = "crycrawler_cache";
        public const int MaxIndexLength = 512;

        const string DumpedName = "dumped";
        const string BacklogName = "backlog";
        const string CrawledName = "crawled";
        const string DumpedBackupName = "dumpedbackup";
        const string DumpedBackupTempName = "dumpedbackuptemp";

        readonly string filename = DefaultFilename;

        // semaphores per collection
        readonly Dictionary<Collection, SemaphoreSlim> semaphores =
            new Dictionary<Collection, SemaphoreSlim>()
            {
                { Collection.CachedCrawled, new SemaphoreSlim(1) },
                { Collection.CachedBacklog, new SemaphoreSlim(1) },
                { Collection.DumpedBacklog, new SemaphoreSlim(1) },
                { Collection.DumpedBacklogBackup, new SemaphoreSlim(1) },
                { Collection.DumpedBacklogBackupTemp, new SemaphoreSlim(1) }
            };

        readonly Dictionary<string, Work> cachedWorks = new Dictionary<string, Work>();

        public bool Disposing { get; private set; }

        LiteDatabase database;

        LiteCollection<Work> backlogCollection;
        LiteCollection<Work> crawledCollection;
        LiteCollection<Work> dumpedCollection;
        LiteCollection<Work> dumpedBackupCollection;
        LiteCollection<Work> dumpedBackupTempCollection;

        public CacheDatabase(string filename)
        {
            // here is the problem
            this.filename = filename;
            database = new LiteDatabase($"Filename={filename};Mode=Exclusive;");

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
            semaphores[collection].Wait();
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
                semaphores[collection].Release();
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
                    var split_works = works.GetRange(offset, cnt); // FIX: stack overflow error?

                    if (InsertBulk(split_works, cnt, out int ins, collection))
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
                semaphores[collection].Wait();
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
                    semaphores[collection].Release();
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
            semaphores[collection].Wait();
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
                semaphores[collection].Release();
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
                count = int.MaxValue;
            }

            if (count <= 0)
            {
                works = null;
                return false;
            }

            semaphores[collection].Wait();
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
                semaphores[collection].Release();
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

            semaphores[collection].Wait();
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
                semaphores[collection].Release();
            }
        }

        /// <summary>
        /// Get number of cached works
        /// </summary>
        /// <param name="collection">Collection to search</param>
        /// <returns>Number of cached works</returns>
        public long GetWorkCount(Collection collection = Collection.CachedBacklog)
        {
            semaphores[collection].Wait();
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
                semaphores[collection].Release();
            }
        }

        /// <summary>
        /// Find all works that satisfy the given predicate
        /// </summary>
        /// <param name="work">Found work</param>
        /// <param name="predicate">Predicate that must be matched</param>
        /// <param name="collection">Collection to search</param>
        /// <returns>Whether work was found or not</returns>
        public bool FindWorks(out IEnumerable<Work> works, Query query, Collection collection = Collection.CachedCrawled)
        {
            semaphores[collection].Wait();
            try
            {
                works = GetCollection(collection).Find(query);

                return works != null && works.Count() > 0;
            }
            catch (Exception ex)
            {
                works = null;

                if (Disposing) return false;
                Logger.Log("Failed to find work in database! " + ex.Message, Logger.LogSeverity.Error);
                return false;
            }
            finally
            {
                semaphores[collection].Release();
            }
        }

        /// <summary>
        /// Find first work that satisfies the given predicate
        /// </summary>
        /// <param name="work">Found work</param>
        /// <param name="predicate">Predicate that must be matched</param>
        /// <param name="collection">Collection to search</param>
        /// <returns>Whether work was found or not</returns>
        public bool FindWork(out Work work, Query query, Collection collection = Collection.CachedCrawled)
        {
            semaphores[collection].Wait();
            try
            {
                work = GetCollection(collection).FindOne(query);

                return work != null;
            }
            catch (Exception ex)
            {
                work = null;

                if (Disposing) return false;
                Logger.Log("Failed to find work in database! " + ex.Message, Logger.LogSeverity.Error);
                return false;
            }
            finally
            {
                semaphores[collection].Release();
            }
        }

        /// <summary>
        /// Delete all works that match the given predicate
        /// </summary>
        /// <param name="deletedCount">Number of items that were deleted</param>
        /// <param name="predicate">Predicate that must be matched</param>
        /// <param name="collection">Collection to delete works from</param>
        /// <returns>Whether operation was successful or not</returns>
        public bool DeleteWorks(out int deletedCount, Query query, Collection collection = Collection.CachedCrawled)
        {
            semaphores[collection].Wait();
            try
            {
                deletedCount = GetCollection(collection).Delete(query);

                if (collection == Collection.CachedCrawled) cachedWorks.Clear();

                return true;
            }
            catch (Exception ex)
            {
                deletedCount = 0;

                if (Disposing) return false;
                Logger.Log("Failed to delete works from database! " + ex.Message, Logger.LogSeverity.Error);
                return false;
            }
            finally
            {
                semaphores[collection].Release();
            }
        }

        /// <summary>
        /// Deletes old database and creates new one
        /// </summary>
        public void EnsureNew()
        {
            if (!File.Exists(filename)) return;

            foreach (var s in semaphores) s.Value.Wait();
            try
            {
                Delete();

                database = new LiteDatabase(filename);

                PrepareCollections();
            }
            finally
            {
                foreach (var s in semaphores) s.Value.Release();
            }
        }

        /// <summary>
        /// Releases database resources. This does not delete the database.
        /// </summary>
        public void Dispose()
        {
            cachedWorks.Clear();

            Disposing = true;
            database.Dispose();
        }

        /// <summary>
        /// Delete the database.
        /// </summary>
        public void Delete()
        {
            if (!File.Exists(filename)) return;

            cachedWorks.Clear();

            database.Dispose();

            File.Delete(filename);
        }

        /// <summary>
        /// Clear all items from collection
        /// </summary>
        public void DropCollection(Collection collection)
        {
            semaphores[collection].Wait();
            try
            {
                if (collection == Collection.CachedCrawled) cachedWorks.Clear();

                database.DropCollection(GetCollection(collection).Name);
            }
            finally
            {
                semaphores[collection].Release();
            }
        }

        private Work getWork(string url, Collection collection)
        {
            if (string.IsNullOrEmpty(url) || Disposing) return null;

            // try the cache first
            if (collection == Collection.CachedCrawled)
                if (cachedWorks.TryGetValue(url, out Work wrk)) return wrk;

            // need to search by key that is limited with 512 bytes
            var k = Work.GetKeyFromUrl(url);

            var works = GetCollection(collection).Find(Query.Where("Key", x => x.AsString == k));
            var w = works?.Where(x => x.Url == url)?.FirstOrDefault();

            // cache it
            if (collection == Collection.CachedCrawled && w != null)
                cachedWorks.TryAdd(url, w);

            return w;
        }

        private void PrepareCollections()
        {
            // WARNING: Calling EnsureIndex() on a large collection causes a huge memory usage spike
            dumpedCollection = database.GetCollection<Work>(DumpedName);
            crawledCollection = database.GetCollection<Work>(CrawledName);
            backlogCollection = database.GetCollection<Work>(BacklogName);
            dumpedBackupCollection = database.GetCollection<Work>(DumpedBackupName);
            dumpedBackupTempCollection = database.GetCollection<Work>(DumpedBackupTempName);

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
            if (!database.CollectionExists(DumpedBackupName))
            {
                dumpedBackupCollection.EnsureIndex(x => x.AddedTime);
                dumpedBackupCollection.EnsureIndex(x => x.Key);
            }
            if (!database.CollectionExists(DumpedBackupTempName))
            {
                dumpedBackupTempCollection.EnsureIndex(x => x.AddedTime);
                dumpedBackupTempCollection.EnsureIndex(x => x.Key);
            }
        }

        /// <summary>
        /// Transfers temporary backup dumped files to backup dumped files. 
        /// This drops the backup collection and renames the temporary collection.
        /// </summary>
        public bool TransferTemporaryDumpedFilesToBackup()
        {
            // we must wait if any other function is using the backup dump
            semaphores[Collection.DumpedBacklogBackup].Wait();

            // we must wait if any other function is using the backup temp dump
            semaphores[Collection.DumpedBacklogBackupTemp].Wait();

            try
            {
                var bcol = GetCollection(Collection.DumpedBacklogBackup);
                var tcol = GetCollection(Collection.DumpedBacklogBackupTemp);

                // rename backup collection and drop it later 
                // (because if we drop it now, there will be a time delay)
                // (LiteDB does something in the background either when dropping it or preparing collection)
                const string tempname = "dropping";
                database.RenameCollection(bcol.Name, tempname);

                // rename temporary collection to backup collection
                if (database.RenameCollection(tcol.Name, bcol.Name) == false)
                    throw new InvalidOperationException("Failed to rename collection!");

                // set the new collection
                dumpedBackupCollection = database.GetCollection<Work>(DumpedBackupName);

                // drop the backup collection now
                // (this will most likely cause a time delay, luckily for us - temp backup has already been transferred)
                database.DropCollection(tempname);

                // ensure all collections exist (should re-create temporary collection)
                PrepareCollections();

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to transfer temporary dumped files to backup dump! " + ex.Message,
                    Logger.LogSeverity.Warning);

                return false;
            }
            finally
            {
                semaphores[Collection.DumpedBacklogBackupTemp].Release();
                semaphores[Collection.DumpedBacklogBackup].Release();
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
                case Collection.DumpedBacklogBackup:
                    return dumpedBackupCollection;
                case Collection.DumpedBacklogBackupTemp:
                    return dumpedBackupTempCollection;
                default:
                    return null;
            }
        }

        public enum Collection
        {
            DumpedBacklog,
            CachedBacklog,
            CachedCrawled,
            DumpedBacklogBackup,
            DumpedBacklogBackupTemp,
            DroppingCollection
        }

        public class DatabaseErrorException : Exception
        {
            public DatabaseErrorException(string msg) : base(msg) { }
        }
    }
}
