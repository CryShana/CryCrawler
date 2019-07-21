using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace CryCrawler.Structures
{
    public class ConcurrentQueueOrStack<T, Key>
    {
        public int Count => queue == null ? stack.Count : queue.Count;

        // using hash set to instantly check if item is contained in backlog (more memory usage though)
        HashSet<Key> addedItems;
        Func<T, Key> keySelector;

        // one of these will be used to contain backlog items
        ConcurrentQueue<T> queue;
        ConcurrentStack<T> stack;

        public ConcurrentQueueOrStack(bool isStack, Func<T, Key> keySelector = null)
        {
            this.keySelector = keySelector;
            this.addedItems = new HashSet<Key>();

            if (!isStack) queue = new ConcurrentQueue<T>();
            else stack = new ConcurrentStack<T>();
        }

        public bool TryGetItem(out T item)
        {
            bool success;

            // attempt to get item
            if (queue == null) success = stack.TryPop(out item);
            else success = queue.TryDequeue(out item);

            // if successful, remove from hash set
            if (success) addedItems.Remove(keySelector(item));

            return success;
        }
        public bool TryPeekItem(out T item) => queue == null ? stack.TryPeek(out item) : queue.TryPeek(out item);
        public void AddItem(T item)
        {
            if (queue == null) stack.Push(item);
            else queue.Enqueue(item);

            // add to hashset for quick checking
            if (keySelector != null) addedItems.Add(keySelector(item));
        }
        public void AddItems(IEnumerable<T> items)
        {
            if (queue == null) stack.PushRange(items.ToArray());
            else foreach (var item in items) queue.Enqueue(item);

            // add to hashset for quick checking
            if (keySelector != null)
                foreach (var item in items)
                    addedItems.Add(keySelector(item));
        }
        public bool ContainsKey(Key key) => addedItems.Contains(key);
        public T FindItem(Predicate<T> predicate)
        {
            IEnumerable<T> col = (IEnumerable<T>)queue ?? stack;
            foreach (var i in col) if (predicate(i)) return i;
            return default(T);
        }
        public void ChangeType(bool isStack)
        {
            if (!isStack)
            {
                if (queue != null) return;
                else queue = new ConcurrentQueue<T>(stack);
            }
            else
            {
                if (stack != null) return;
                else stack = new ConcurrentStack<T>(queue);
            }
        }

        /// <summary>
        /// Clear collection. This does not clear the cached items!
        /// </summary>
        public void Clear()
        {
            addedItems.Clear();

            if (queue == null) stack.Clear();
            else queue.Clear();
        }

        public T[] ToArray() => queue == null ? stack.ToArray() : queue.ToArray();
        public List<T> ToList() => queue == null ? stack.ToList() : queue.ToList();
    }
}
