using System;
using System.Collections.Generic;

namespace HexTactica.AI {

    /// <summary>
    /// Binary min-heap with O(log n) decrease-key, keyed by reference identity.
    ///
    /// The existing Assets/scripts/PriorityQueue rebuilds its entire backing
    /// SortedList on every AlterPriority and does a linear scan for Contains,
    /// which makes Dijkstra O(n^2 log n). The transport planner runs several
    /// full-map searches per unit per turn, so that cost is no longer academic.
    /// </summary>
    public class AIHeap<T> where T : class {

        struct Node {
            public T item;
            public long key;
        }

        readonly List<Node> heap = new List<Node>();
        readonly Dictionary<T, int> index = new Dictionary<T, int>();

        public int Count { get { return heap.Count; } }

        public void Clear() {
            heap.Clear();
            index.Clear();
        }

        public bool Contains(T item) {
            return index.ContainsKey(item);
        }

        public bool TryGetKey(T item, out long key) {
            int i;
            if (index.TryGetValue(item, out i)) { key = heap[i].key; return true; }
            key = 0;
            return false;
        }

        public void Push(T item, long key) {
            int existing;
            if (index.TryGetValue(item, out existing)) {
                if (key < heap[existing].key) {
                    Node n = heap[existing];
                    n.key = key;
                    heap[existing] = n;
                    SiftUp(existing);
                }
                return;
            }
            heap.Add(new Node { item = item, key = key });
            index[item] = heap.Count - 1;
            SiftUp(heap.Count - 1);
        }

        public T Pop() {
            if (heap.Count == 0) throw new InvalidOperationException("AIHeap empty");
            T top = heap[0].item;
            index.Remove(top);
            int last = heap.Count - 1;
            if (last == 0) {
                heap.RemoveAt(0);
                return top;
            }
            heap[0] = heap[last];
            heap.RemoveAt(last);
            index[heap[0].item] = 0;
            SiftDown(0);
            return top;
        }

        public T Peek() {
            if (heap.Count == 0) throw new InvalidOperationException("AIHeap empty");
            return heap[0].item;
        }

        void SiftUp(int i) {
            Node n = heap[i];
            while (i > 0) {
                int parent = (i - 1) >> 1;
                if (heap[parent].key <= n.key) break;
                heap[i] = heap[parent];
                index[heap[i].item] = i;
                i = parent;
            }
            heap[i] = n;
            index[n.item] = i;
        }

        void SiftDown(int i) {
            Node n = heap[i];
            int count = heap.Count;
            while (true) {
                int left = (i << 1) + 1;
                if (left >= count) break;
                int right = left + 1;
                int child = (right < count && heap[right].key < heap[left].key) ? right : left;
                if (heap[child].key >= n.key) break;
                heap[i] = heap[child];
                index[heap[i].item] = i;
                i = child;
            }
            heap[i] = n;
            index[n.item] = i;
        }
    }
}
