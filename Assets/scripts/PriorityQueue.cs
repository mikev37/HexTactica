using System;
using System.Collections.Generic;

class PriorityQueue<T> {
    SortedList<Pair<float>, T> _list;
    float count;

    public PriorityQueue() {
        _list = new SortedList<Pair<float>, T>(new PairComparer<float>());
    }

    public void AlterPriority(T item, float priority) {
        SortedList<Pair<float>, T> _slist = new SortedList<Pair<float>, T>(new PairComparer<float>());
        for (int i = 0; i < _list.Count; i++) {
            if (_list[_list.Keys[i]].Equals(item)) {
                _slist.Add(new Pair<float>(priority, count), item);
                count++;
            } else {
                _slist.Add(_list.Keys[i], _list[_list.Keys[i]]);
            }
        }
        _list = _slist;
    }

    public void Enqueue(T item, float priority) {
        _list.Add(new Pair<float>(priority, count), item);
        count++;
    }

    public T Dequeue() {
        T item = _list[_list.Keys[0]];
        _list.RemoveAt(0);
        return item;
    }

    internal int Count() {
        return _list.Count;
    }

    internal bool Contains(T t) {
        return _list.ContainsValue(t);
    }
}

class Pair<T> {
    public T First { get; private set; }
    public T Second { get; private set; }

    public Pair(T first, T second) {
        First = first;
        Second = second;
    }

    public override int GetHashCode() {
        return First.GetHashCode() ^ Second.GetHashCode();
    }

    public override bool Equals(object other) {
        Pair<T> pair = other as Pair<T>;
        if (pair == null) {
            return false;
        }
        return (this.First.Equals(pair.First) && this.Second.Equals(pair.Second));
    }
}

class PairComparer<T> : IComparer<Pair<T>> where T : IComparable {
    public int Compare(Pair<T> x, Pair<T> y) {
        if (x.First.CompareTo(y.First) < 0) {
            return -1;
        } else if (x.First.CompareTo(y.First) > 0) {
            return 1;
        } else {
            return x.Second.CompareTo(y.Second);
        }
    }
}