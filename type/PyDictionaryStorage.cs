using System;
using System.Collections;
using System.Collections.Generic;

namespace SharpPy
{
    // Full-width Python hashes are retained with entries. CPython 3.12's
    // perturb probing and restart checks preserve observable equality calls.
    public sealed class PyDictionaryStorage : IEnumerable<KeyValuePair<PyObject, PyObject>>
    {
        internal struct Entry
        {
            internal PyObject Key, Value;
            internal long Hash;
        }
        private sealed class IndexTable
        {
            internal static readonly IndexTable Empty = new IndexTable(0);
            private readonly ushort[] small;
            private readonly int[] large;
            internal int Length => small != null ? small.Length : large.Length;
            internal IndexTable(int size)
            {
                if (size <= 65536) small = new ushort[size];
                else large = new int[size];
            }
            private IndexTable(IndexTable source)
            {
                if (source.small != null) small = (ushort[])source.small.Clone();
                else large = (int[])source.large.Clone();
            }
            internal IndexTable Clone() => Length == 0 ? Empty : new IndexTable(this);
            internal int this[int index]
            {
                get { if (small == null) return large[index]; int value = small[index]; return value == ushort.MaxValue ? -1 : value; }
                set { if (small == null) large[index] = value; else small[index] = unchecked((ushort)value); }
            }
        }
        private IndexTable indices = IndexTable.Empty; // 0 empty, -1 deleted, entry + 1
        private Entry[] entries = Array.Empty<Entry>();
        private int used, usable;
        private static ulong UnsignedHash(long hash) => IntPtr.Size == 8 ? unchecked((ulong)hash) : unchecked((uint)hash);
        public int Count { get; private set; }
        internal bool CanCloneForMerge => used == Count && (indices.Length <= 8 || indices.Length / 3 < Count);
        internal bool CanCloneForCopy => Count >= used * 2 / 3;
        internal int EntriesUsed => used;
        internal bool NextEntry(ref int position, out Entry entry)
        {
            while (position < used)
            {
                entry = entries[position++];
                if (entry.Key != null) return true;
            }
            entry = default;
            return false;
        }
        internal void PrepareMerge(int incoming)
        {
            if (indices.Length * 2 / 3 < incoming) Resize(checked((Count + incoming) * 3 / 2 + 1));
        }
        // Only keys whose hashing can execute Python need an identity lookup
        // for internal iteration. Ordinary numeric/string dictionaries pay none.
        private Dictionary<PyObject, long> callbackHashes;
        private static bool NeedsCachedHash(PyObject key) =>
            key.HasCustomType || (key is not PyInt && key is not PyString &&
            key is not PyFloat && key is not PyBool && key is not PyNone);

        public PyDictionaryStorage() { }
        public PyDictionaryStorage(IEnumerable<KeyValuePair<PyObject, PyObject>> source)
        {
            foreach (var pair in source) Set(pair.Key, pair.Value);
        }
        internal PyDictionaryStorage Clone()
        {
            var copy = new PyDictionaryStorage {
                indices = indices.Clone(), entries = new Entry[used],
                used = used, usable = usable, Count = Count
            };
            Array.Copy(entries, copy.entries, used);
            if (callbackHashes != null)
                copy.callbackHashes = new Dictionary<PyObject, long>(callbackHashes, ReferenceEqualityComparer.Instance);
            return copy;
        }
        internal void CopyFrom(PyDictionaryStorage source)
        {
            indices = source.indices.Clone(); entries = new Entry[source.used];
            Array.Copy(source.entries, entries, source.used);
            used = source.used; usable = source.usable; Count = source.Count;
            callbackHashes = source.callbackHashes == null ? null :
                new Dictionary<PyObject, long>(source.callbackHashes, ReferenceEqualityComparer.Instance);
        }

        private int Find(PyObject key, long hash, out int slot)
        {
            while (true)
            {
                var table = indices;
                if (table.Length == 0) { slot = 0; return -1; }
                int mask = table.Length - 1;
                ulong perturb = UnsignedHash(hash);
                int position = (int)(perturb & (uint)mask);
                int empty = -1;
                while (true)
                {
                    int index = table[position];
                    if (index == 0) { slot = empty >= 0 ? empty : position; return -1; }
                    if (index < 0) { if (empty < 0) empty = position; }
                    else
                    {
                        index--;
                        var entry = entries[index];
                        if (ReferenceEquals(entry.Key, key)) { slot = position; return index; }
                        if (entry.Hash == hash)
                        {
                            bool equal = PyObject.CompareBool(entry.Key, key, PyObject.CompareOp.EQ);
                            // A callback may clear, resize or remove the candidate.
                            if (!ReferenceEquals(table, indices) || !ReferenceEquals(entries[index].Key, entry.Key)) break;
                            if (equal) { slot = position; return index; }
                        }
                    }
                    perturb >>= 5;
                    position = (int)(((uint)position * 5 + perturb + 1) & (uint)mask);
                }
            }
        }

        private void Resize(int minimum = 0)
        {
            int size = 8;
            while (size < (minimum == 0 ? Count * 3L : minimum)) size = checked(size * 2);
            var table = new IndexTable(size);
            var data = new Entry[size * 2 / 3];
            int next = 0;
            for (int i = 0; i < used; i++)
            {
                if (entries[i].Key == null) continue;
                var entry = entries[i]; data[next] = entry;
                ulong perturb = UnsignedHash(entry.Hash);
                int position = (int)(perturb & (uint)(size - 1));
                while (table[position] != 0)
                {
                    perturb >>= 5;
                    position = (int)(((uint)position * 5 + perturb + 1) & (uint)(size - 1));
                }
                table[position] = ++next;
            }
            indices = table; entries = data; used = next; usable = data.Length - next;
        }

        internal bool Set(PyObject key, PyObject value) => SetKnownHash(key, value, key.GetPythonHash());
        internal bool SetKnownHash(PyObject key, PyObject value, long hash)
        {
            int index = Find(key, hash, out int slot);
            if (index >= 0) { entries[index].Value = value; return false; }
            Insert(key, value, hash, slot);
            return true;
        }
        internal PyObject SetDefault(PyObject key, PyObject value, out bool added)
        {
            long hash = key.GetPythonHash();
            int index = Find(key, hash, out int slot);
            added = index < 0;
            if (!added) return entries[index].Value;
            Insert(key, value, hash, slot);
            return value;
        }
        private void Insert(PyObject key, PyObject value, long hash, int slot)
        {
            if (usable == 0)
            {
                Resize();
                FindEmpty(hash, out slot);
            }
            if (used == entries.Length)
                Array.Resize(ref entries, Math.Min(indices.Length * 2 / 3, Math.Max(used + 1, used * 2)));
            entries[used] = new Entry { Key = key, Value = value, Hash = hash };
            indices[slot] = ++used; usable--; Count++;
            if (NeedsCachedHash(key))
            {
                callbackHashes ??= new Dictionary<PyObject, long>(ReferenceEqualityComparer.Instance);
                callbackHashes[key] = hash;
            }
        }
        private void FindEmpty(long hash, out int slot)
        {
            int mask = indices.Length - 1;
            ulong perturb = UnsignedHash(hash);
            slot = (int)(perturb & (uint)mask);
            while (indices[slot] > 0)
            {
                perturb >>= 5;
                slot = (int)(((uint)slot * 5 + perturb + 1) & (uint)mask);
            }
        }
        public PyObject this[PyObject key]
        {
            get => TryGetValue(key, out var value) ? value : throw PyKeyError.Create(key);
            set => Set(key, value);
        }
        public bool TryGetValue(PyObject key, out PyObject value) => TryGetKnownHash(key, key.GetPythonHash(), out value);
        internal bool TryGetKnownHash(PyObject key, long hash, out PyObject value)
        {
            int index = Find(key, hash, out _);
            value = index < 0 ? null : entries[index].Value;
            return index >= 0;
        }
        internal long StoredHash(PyObject key) => callbackHashes != null && callbackHashes.TryGetValue(key, out long hash)
            ? hash : key.GetPythonHash();
        internal bool TryGetStoredValue(PyObject key, out PyObject value)
        {
            value = null;
            int index = FindStored(key, out _);
            if (index < 0) return false;
            value = entries[index].Value;
            return true;
        }
        private int FindStored(PyObject key, out int position)
        {
            position = 0;
            if (indices.Length == 0) return -1;
            ulong perturb = UnsignedHash(StoredHash(key));
            int mask = indices.Length - 1;
            position = (int)(perturb & (uint)mask);
            while (indices[position] != 0)
            {
                int index = indices[position] - 1;
                if (index >= 0 && ReferenceEquals(entries[index].Key, key))
                    return index;
                perturb >>= 5;
                position = (int)(((uint)position * 5 + perturb + 1) & (uint)mask);
            }
            return -1;
        }
        internal PyObject StoredValue(PyObject key) => TryGetStoredValue(key, out var value) ? value : throw PyKeyError.Create(key);
        public bool ContainsKey(PyObject key) => TryGetValue(key, out _);
        public bool Remove(PyObject key) => Remove(key, out _, out _);
        public bool Remove(PyObject key, out PyObject value) => Remove(key, out value, out _);
        internal bool Remove(PyObject key, out PyObject value, out PyObject storedKey, bool knownHash = false)
        {
            int slot;
            int index = knownHash ? FindStored(key, out slot) : Find(key, key.GetPythonHash(), out slot);
            if (index < 0) { value = storedKey = null; return false; }
            value = entries[index].Value; storedKey = entries[index].Key;
            indices[slot] = -1; entries[index] = default; Count--;
            if (knownHash)
            {
                while (used > 0 && entries[used - 1].Key == null) used--;
            }
            callbackHashes?.Remove(storedKey);
            return true;
        }
        public void Clear()
        {
            indices = IndexTable.Empty; entries = Array.Empty<Entry>();
            used = usable = Count = 0; callbackHashes?.Clear();
        }
        public IEnumerable<PyObject> Keys { get { foreach (var pair in this) yield return pair.Key; } }
        public IEnumerable<PyObject> Values { get { foreach (var pair in this) yield return pair.Value; } }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<KeyValuePair<PyObject, PyObject>> IEnumerable<KeyValuePair<PyObject, PyObject>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public struct Enumerator : IEnumerator<KeyValuePair<PyObject, PyObject>>
        {
            private readonly PyDictionaryStorage owner;
            private int position;
            public KeyValuePair<PyObject, PyObject> Current { get; private set; }
            object IEnumerator.Current => Current;
            internal Enumerator(PyDictionaryStorage owner) { this.owner = owner; position = 0; Current = default; }
            public bool MoveNext()
            {
                while (position < owner.used)
                {
                    var entry = owner.entries[position++];
                    if (entry.Key == null) continue;
                    Current = new KeyValuePair<PyObject, PyObject>(entry.Key, entry.Value); return true;
                }
                return false;
            }
            public void Reset() => position = 0;
            public void Dispose() { }
        }
    }
}
