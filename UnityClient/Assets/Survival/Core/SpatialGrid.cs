using System;
using System.Collections.Generic;
using System.Numerics;

namespace Survival.Core
{
    /// <summary>
    /// Uniform grid on the XZ plane. Used for network interest management, AI perception and
    /// proximity queries on the server. Items move between cells in O(1).
    /// </summary>
    public sealed class SpatialGrid<T> where T : class
    {
        private readonly float _cellSize;
        private readonly float _invCell;
        private readonly Dictionary<long, List<T>> _cells = new Dictionary<long, List<T>>(1024);
        private readonly Dictionary<T, long> _itemCell = new Dictionary<T, long>(1024);
        private readonly Stack<List<T>> _listPool = new Stack<List<T>>();

        public SpatialGrid(float cellSize)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            _cellSize = cellSize;
            _invCell = 1f / cellSize;
        }

        public float CellSize => _cellSize;
        public int Count => _itemCell.Count;

        private static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;

        private long KeyFor(float x, float z) => Key(SMath.FloorToInt(x * _invCell), SMath.FloorToInt(z * _invCell));

        public void Insert(T item, Vector3 pos)
        {
            long key = KeyFor(pos.X, pos.Z);
            if (_itemCell.TryGetValue(item, out long old))
            {
                if (old == key) return;
                RemoveFromCell(item, old);
            }
            AddToCell(item, key);
            _itemCell[item] = key;
        }

        /// <summary>Updates the cell of an item; cheap when the item stays in its cell.</summary>
        public void Move(T item, Vector3 pos) => Insert(item, pos);

        public bool Remove(T item)
        {
            if (!_itemCell.TryGetValue(item, out long key)) return false;
            RemoveFromCell(item, key);
            _itemCell.Remove(item);
            return true;
        }

        public bool Contains(T item) => _itemCell.ContainsKey(item);

        /// <summary>Appends items from all cells overlapping the circle (coarse; callers filter by exact distance).</summary>
        public void QueryCells(Vector3 center, float radius, List<T> results)
        {
            int minX = SMath.FloorToInt((center.X - radius) * _invCell);
            int maxX = SMath.FloorToInt((center.X + radius) * _invCell);
            int minZ = SMath.FloorToInt((center.Z - radius) * _invCell);
            int maxZ = SMath.FloorToInt((center.Z + radius) * _invCell);
            for (int cx = minX; cx <= maxX; cx++)
            {
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    if (_cells.TryGetValue(Key(cx, cz), out var list))
                    {
                        results.AddRange(list);
                    }
                }
            }
        }

        private void AddToCell(T item, long key)
        {
            if (!_cells.TryGetValue(key, out var list))
            {
                list = _listPool.Count > 0 ? _listPool.Pop() : new List<T>(8);
                _cells[key] = list;
            }
            list.Add(item);
        }

        private void RemoveFromCell(T item, long key)
        {
            if (!_cells.TryGetValue(key, out var list)) return;
            int idx = list.IndexOf(item);
            if (idx >= 0)
            {
                int last = list.Count - 1;
                list[idx] = list[last];
                list.RemoveAt(last);
            }
            if (list.Count == 0)
            {
                _cells.Remove(key);
                _listPool.Push(list);
            }
        }
    }
}
