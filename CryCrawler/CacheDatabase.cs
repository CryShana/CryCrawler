using System;
using System.IO;
using System.Linq;
using System.Threading;
using CryCrawler.Worker;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace CryCrawler
{
    public class DatabaseContext : DbContext
    {
        public const string DefaultFilename = "crycrawler_cache";
        public string DatabaseName { get; }

        public DbSet<Work> Crawled { get; set; }
        public DbSet<Work> CachedBacklog { get; set; }
        public DbSet<Work> Dumped { get; set; }

        public DatabaseContext(string dbname) => DatabaseName = dbname;
        
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) 
            => optionsBuilder.UseSqlite("Data Source=" + DatabaseName);
        
        public static bool EnsureCreated(string dbname)
        {
            using (var db = new DatabaseContext(dbname)) return db.Database.EnsureCreated();          
        }

        public static void EnsureNew(string dbname)
        {
            //if (File.Exists(dbname)) File.Delete(dbname);

            // test this
            Delete(dbname);
            new DatabaseContext(dbname).Database.EnsureCreated();
        }

        public static void Delete(string dbname) => new DatabaseContext(dbname).Database.EnsureDeleted();
    }
}
