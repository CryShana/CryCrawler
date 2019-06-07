using System.Threading;
using System.Collections;
using System.Collections.Generic;

namespace CryCrawler.Structures
{
    public class ConcurrentSlidingBuffer<T> : IEnumerable<T>
    {
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private readonly Queue<T> _queue;
        private readonly int _maxCount;

        public ConcurrentSlidingBuffer(int maxCount)
        {
            _maxCount = maxCount;
            _queue = new Queue<T>(maxCount);
        }

        public void Add(T item)
        {
            semaphore.Wait();

            try
            {
                if (_queue.Count == _maxCount) _queue.Dequeue();
                _queue.Enqueue(item);
            }
            finally
            {
                semaphore.Release();
            }
        }

        public IEnumerator<T> GetEnumerator() => _queue.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        
    }
}
