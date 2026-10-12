using System.Collections;

namespace SharpPy
{
    // Immutable entries retain the hash computed at insertion. Hashing a
    // frozenset must never call an element's __hash__ again.
    internal sealed class PyFrozenSetStorage : IReadOnlyCollection<PyObject>
    {
        private readonly record struct Entry(PyObject Key, long Hash);
        private sealed class Comparer : IEqualityComparer<Entry>
        {
            internal static readonly Comparer Instance = new Comparer();
            public int GetHashCode(Entry entry) => entry.Hash.GetHashCode();
            public bool Equals(Entry left, Entry right) => left.Hash == right.Hash &&
                (ReferenceEquals(left.Key, right.Key) || PyObject.CompareBool(left.Key, right.Key, PyObject.CompareOp.EQ));
        }
        private readonly HashSet<Entry> entries;
        public int Count => entries.Count;
        internal PyFrozenSetStorage(IEnumerable<PyObject> source)
        {
            if (source is PyFrozenSetStorage stored)
                entries = new HashSet<Entry>(stored.entries, Comparer.Instance);
            else
            {
                entries = new HashSet<Entry>(Comparer.Instance);
                foreach (var key in source) entries.Add(new Entry(key, key.GetPythonHash()));
            }
        }
        internal bool Contains(PyObject key) => entries.Contains(new Entry(key, key.GetPythonHash()));
        internal bool SetEquals(PyFrozenSetStorage other) => entries.SetEquals(other.entries);
        internal long FrozenHash()
        {
            // Objects/setobject.c: frozenset_hash, over active entries only.
            ulong hash = 0;
            foreach (var entry in entries)
            {
                ulong value = IntPtr.Size == 8 ? unchecked((ulong)entry.Hash) : unchecked((uint)entry.Hash);
                hash ^= unchecked(((value ^ 89869747UL) ^ (value << 16)) * 3644798167UL);
            }
            hash ^= unchecked(((ulong)Count + 1) * 1927868237UL);
            if (IntPtr.Size == 4) hash = (uint)hash;
            hash ^= (hash >> 11) ^ (hash >> 25);
            hash = unchecked(hash * 69069UL + 907133923UL);
            long result = IntPtr.Size == 8 ? unchecked((long)hash) : unchecked((int)hash);
            return result == -1 ? 590923713 : result;
        }
        public IEnumerator<PyObject> GetEnumerator()
        {
            foreach (var entry in entries) yield return entry.Key;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
