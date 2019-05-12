using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace CryCrawler.Structures
{
    public class ConcurrentQueueOrStack<T>
    {
        public int Count => queue == null ? stack.Count : queue.Count;

        ConcurrentQueue<T> queue;
        ConcurrentStack<T> stack;

        public ConcurrentQueueOrStack(bool isStack)
        {
            if (!isStack) queue = new ConcurrentQueue<T>();
            else stack = new ConcurrentStack<T>();
        }

        public bool TryGetItem(out T item) => queue == null ? stack.TryPop(out item) : queue.TryDequeue(out item);     
        public bool TryPeekItem(out T item) => queue == null ? stack.TryPeek(out item) : queue.TryPeek(out item);
        public void AddItem(T item)
        {
            if (queue == null) stack.Push(item);
            else queue.Enqueue(item);
        }
        public void AddItems(IEnumerable<T> items)
        {
            if (queue == null) stack.PushRange(items.ToArray());
            else foreach (var item in items) queue.Enqueue(item);
        }

        public void Clear()
        {
            if (queue == null) stack.Clear();
            else queue.Clear();
        }

        public T[] ToArray() => queue == null ? stack.ToArray() : queue.ToArray();
        public List<T> ToList() => queue == null ? stack.ToList() : queue.ToList();
    }
}
