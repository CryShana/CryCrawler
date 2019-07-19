using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using static CryCrawler.Host.WorkerManager;


namespace CryCrawler.Host
{
    public interface IWorkerPicker
    {
        /// <summary>
        /// Picks a worker from a list based on custom criteria
        /// </summary>
        public WorkerClient Pick(IEnumerable<WorkerClient> clients);
    }

    /// <summary>
    /// Picks worker based on work count.
    /// </summary>
    public class WorkWeightedPicker : IWorkerPicker
    {
        /// <summary>
        /// Picks an online worker with least amount of work
        /// </summary>
        public WorkerClient Pick(IEnumerable<WorkerClient> clients)
            => clients.Where(x => x.Online).Aggregate((a, b) => a.WorkCount < b.WorkCount ? a : b);
    }
}
