using System;
using System.Collections;
using System.Collections.Generic;

namespace SpeechChem.UI
{
    /// <summary>
    /// An append-only list in fixed 16K-element chunks that can also drop its oldest elements:
    /// growing never copies (no doubling into ever-larger arrays) and, for 4-byte elements, no
    /// chunk reaches the large-object heap — what a run log's per-cycle index needs in a 32-bit
    /// process, where one contiguous 70 MB regrowth can fail outright. BCL-pure.
    /// </summary>
    internal sealed class ChunkedList<T> : IReadOnlyList<T>
    {
        private const int Bits = 14;
        private const int Size = 1 << Bits;
        private const int Mask = Size - 1;

        private readonly List<T[]> _chunks = new List<T[]>();
        private int _head;  // dead slots before the first element, in the first chunk (< Size)
        private int _count;

        public int Count => _count;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                int p = _head + index;
                return _chunks[p >> Bits][p & Mask];
            }
            set
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                int p = _head + index;
                _chunks[p >> Bits][p & Mask] = value;
            }
        }

        public void Add(T item)
        {
            int p = _head + _count;
            if ((p >> Bits) == _chunks.Count) _chunks.Add(new T[Size]);
            _chunks[p >> Bits][p & Mask] = item;
            _count++;
        }

        /// <summary>Drop the first <paramref name="n"/> elements (whole chunks are released).</summary>
        public void RemoveFirst(int n)
        {
            if (n <= 0) return;
            if (n >= _count) { Clear(); return; }
            for (int i = 0; i < n; i++) this[i] = default(T); // release references the kept chunk still holds
            _head += n;
            _count -= n;
            int dead = _head >> Bits;
            if (dead > 0)
            {
                _chunks.RemoveRange(0, dead);
                _head &= Mask;
            }
        }

        public void Clear()
        {
            _chunks.Clear();
            _head = 0;
            _count = 0;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++) yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
