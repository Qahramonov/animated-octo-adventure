using System;
using System.Collections.Generic;

namespace Survival.Core
{
    /// <summary>Fixed-capacity ring buffer (newest overwrites oldest). Not thread-safe.</summary>
    public sealed class RingBuffer<T>
    {
        private readonly T[] _items;
        private int _start;
        private int _count;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;

        public void Add(T item)
        {
            int idx = (_start + _count) % _items.Length;
            _items[idx] = item;
            if (_count < _items.Length) _count++;
            else _start = (_start + 1) % _items.Length;
        }

        /// <summary>Index 0 = oldest.</summary>
        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                return _items[(_start + index) % _items.Length];
            }
            set
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                _items[(_start + index) % _items.Length] = value;
            }
        }

        public T Newest => this[_count - 1];
        public T Oldest => this[0];

        public void Clear()
        {
            _start = 0;
            _count = 0;
            Array.Clear(_items, 0, _items.Length);
        }

        /// <summary>Removes the oldest n items.</summary>
        public void DropOldest(int n)
        {
            n = Math.Min(n, _count);
            _start = (_start + n) % _items.Length;
            _count -= n;
        }
    }

    /// <summary>Simple object pool to avoid GC churn on hot paths (mobile + server tick).</summary>
    public sealed class Pool<T> where T : class
    {
        private readonly Stack<T> _free;
        private readonly Func<T> _factory;
        private readonly Action<T> _onRelease;

        public Pool(Func<T> factory, Action<T> onRelease = null, int initial = 0)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onRelease = onRelease;
            _free = new Stack<T>(Math.Max(4, initial));
            for (int i = 0; i < initial; i++) _free.Push(factory());
        }

        public int FreeCount => _free.Count;

        public T Get() => _free.Count > 0 ? _free.Pop() : _factory();

        public void Release(T item)
        {
            if (item == null) return;
            _onRelease?.Invoke(item);
            _free.Push(item);
        }
    }
}
