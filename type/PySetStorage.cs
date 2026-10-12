using System.Collections;

namespace SharpPy
{
    // CPython 3.12 set probing; entries retain Python's native-width hash.
    // No wrapper object per key, and resize/copy never invoke Python callbacks.
    internal sealed class PySetStorage : IReadOnlyCollection<PyObject>
    {
        internal struct Entry { internal PyObject Key; internal long Hash; }
        private Entry[] table;
        private int fill, finger;
        private bool shared;
        private Entry[] copyTemplate;
        private int layoutVersion;

        // Copies share only a canonical copy layout. A write detaches before
        // changing any entry; callbacks still see the same Python key objects.
        private void Writable()
        {
            copyTemplate = null;
            if (shared) { table = (Entry[])table.Clone(); shared = false; }
        }
        public int Count { get; private set; }
        internal PySetStorage() { table = new Entry[8]; }
        internal PySetStorage(IEnumerable<PyObject> source)
        {
            if (source is PySetStorage stored)
            {
                int size = 8;
                if (stored.Count * 5 >= 7 * 3)
                    while (size <= stored.Count * 2) size = checked(size * 2);
                if (stored.copyTemplate != null)
                {
                    table = stored.copyTemplate; shared = true;
                    Count = fill = stored.Count;
                    return;
                }
                if (size == stored.table.Length && stored.fill == stored.Count)
                {
                    table = stored.table; stored.shared = shared = true;
                    stored.copyTemplate = table; Count = fill = stored.Count;
                    return;
                }
                table = new Entry[size];
                if (stored.Count == 0) return;
                if (CanCopyPositions(stored.table, size))
                    Array.Copy(stored.table, table, stored.table.Length);
                else for (int i = 0; i < stored.table.Length; i++)
                {
                    var entry = stored.table[i];
                    if (entry.Key != null)
                    {
                        int slot = (int)(Unsigned(entry.Hash) & (uint)(size - 1));
                        if (table[slot].Key == null) table[slot] = entry;
                        else InsertClean(entry);
                    }
                }
                Count = fill = stored.Count;
                stored.copyTemplate = table; shared = true;
            }
            else { table = new Entry[8]; foreach (var key in source) Add(key); }
        }
        private static ulong Unsigned(long hash) => IntPtr.Size == 8 ? unchecked((ulong)hash) : unchecked((uint)hash);
        private static bool CanCopyPositions(Entry[] source, int size)
        {
            if (size < source.Length) return false;
            for (int i = 0; i < source.Length; i++)
            {
                var entry = source[i];
                if (entry.Key == null) { if (entry.Hash != 0) return false; }
                else if ((Unsigned(entry.Hash) & (uint)(size - 1)) != (uint)i) return false;
            }
            return true;
        }
        private int Find(PyObject key, long hash, bool inserting, out bool found)
        {
        restart:
            var observed = table;
            int version = layoutVersion;
            int mask = observed.Length - 1, free = -1;
            ulong perturb = Unsigned(hash);
            int start = (int)(perturb & (uint)mask);
            while (true)
            {
                int end = start + (start + 9 <= mask ? 9 : 0);
                for (int slot = start; slot <= end; slot++)
                {
                    var entry = observed[slot];
                    if (entry.Key == null)
                    {
                        if (entry.Hash == 0) { found = false; return inserting && free >= 0 ? free : slot; }
                        if (inserting) free = slot;
                    }
                    else if (entry.Hash == hash)
                    {
                        if (ReferenceEquals(entry.Key, key)) { found = true; return slot; }
                        bool equal = PyObject.CompareBool(entry.Key, key, PyObject.CompareOp.EQ);
                        // set_add_entry accepts equality before its restart check;
                        // set_lookkey checks mutation first, even when equal.
                        if (inserting && equal) { found = true; return slot; }
                        // Detaching a shared array does not change Python's probe
                        // layout and must not repeat an equality callback.
                        if (version != layoutVersion || !ReferenceEquals(table[slot].Key, entry.Key))
                            goto restart;
                        observed = table;
                        if (equal) { found = true; return slot; }
                    }
                }
                perturb >>= 5;
                start = (int)((unchecked((ulong)start * 5 + 1 + perturb)) & (uint)mask);
            }
        }
        internal bool Add(PyObject key) => AddKnown(key, key.GetPythonHash());
        internal bool AddKnown(PyObject key, long hash)
        {
            int slot = Find(key, hash, true, out bool found);
            if (found) return false;
            Writable();
            bool unused = table[slot].Hash == 0;
            table[slot] = new Entry { Key = key, Hash = hash };
            Count++;
            if (unused && ++fill * 5 >= (table.Length - 1) * 3)
                Resize(Count > 50000 ? Count * 2 : Count * 4);
            return true;
        }
        private void InsertClean(Entry entry)
        {
            int mask = table.Length - 1;
            ulong perturb = Unsigned(entry.Hash);
            int start = (int)(perturb & (uint)mask);
            while (true)
            {
                int end = start + (start + 9 <= mask ? 9 : 0);
                for (int slot = start; slot <= end; slot++)
                    if (table[slot].Key == null) { table[slot] = entry; return; }
                perturb >>= 5;
                start = (int)((unchecked((ulong)start * 5 + 1 + perturb)) & (uint)mask);
            }
        }
        private void Resize(int minimum)
        {
            int size = 8;
            while (size <= minimum) size = checked(size * 2);
            var old = table;
            table = new Entry[size]; shared = false; copyTemplate = null; layoutVersion++;
            for (int i = 0; i < old.Length; i++)
            {
                var entry = old[i];
                if (entry.Key != null) InsertClean(entry);
            }
            fill = Count;
        }
        internal bool Contains(PyObject key) => ContainsKnown(key, key.GetPythonHash());
        internal bool ContainsKnown(PyObject key, long hash) { Find(key, hash, false, out bool found); return found; }
        internal bool Remove(PyObject key) => RemoveKnown(key, key.GetPythonHash());
        internal bool RemoveKnown(PyObject key, long hash)
        {
            int slot = Find(key, hash, false, out bool found);
            if (!found) return false;
            Writable();
            table[slot] = new Entry { Hash = -1 };
            Count--;
            return true;
        }
        internal PyObject Pop()
        {
            if (Count == 0) throw PyKeyError.Create("pop from an empty set");
            int slot = finger & (table.Length - 1);
            while (table[slot].Key == null) slot = (slot + 1) & (table.Length - 1);
            var key = table[slot].Key;
            Writable();
            table[slot] = new Entry { Hash = -1 }; Count--; finger = slot + 1;
            return key;
        }
        internal void Clear() { table = new Entry[8]; Count = fill = finger = 0; shared = false; copyTemplate = null; layoutVersion++; }
        internal void Merge(PySetStorage other)
        {
            if (ReferenceEquals(this, other) || other.Count == 0) return;
            Writable();
            if ((fill + other.Count) * 5 >= (table.Length - 1) * 3)
                Resize((Count + other.Count) * 2);
            if (fill == 0)
            {
                if (table.Length == other.table.Length && other.fill == other.Count)
                    Array.Copy(other.table, table, table.Length);
                else foreach (var entry in other.Entries()) InsertClean(entry);
                Count = fill = other.Count;
                return;
            }
            foreach (var entry in other.Entries()) AddKnown(entry.Key, entry.Hash);
        }
        internal IEnumerable<Entry> Entries()
        {
            for (int i = 0; i < table.Length; i++) if (table[i].Key != null) yield return table[i];
        }
        public IEnumerator<PyObject> GetEnumerator()
        {
            foreach (var entry in Entries()) yield return entry.Key;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private static PySetStorage Storage(IEnumerable<PyObject> source) => source as PySetStorage ?? new PySetStorage(source);
        internal bool IsSubsetOf(IEnumerable<PyObject> source)
        {
            var other = Storage(source);
            if (Count > other.Count) return false;
            foreach (var entry in Entries()) if (!other.ContainsKnown(entry.Key, entry.Hash)) return false;
            return true;
        }
        internal bool SetEquals(IEnumerable<PyObject> source) { var other = Storage(source); return Count == other.Count && IsSubsetOf(other); }
        internal bool IsProperSubsetOf(IEnumerable<PyObject> source) { var other = Storage(source); return Count < other.Count && IsSubsetOf(other); }
        internal bool IsSupersetOf(IEnumerable<PyObject> source) => Storage(source).IsSubsetOf(this);
        internal bool IsProperSupersetOf(IEnumerable<PyObject> source) => Storage(source).IsProperSubsetOf(this);
        internal bool Overlaps(IEnumerable<PyObject> source)
        {
            var other = Storage(source);
            var scanned = other.Count > Count ? this : other;
            var searched = other.Count > Count ? other : this;
            foreach (var entry in scanned.Entries()) if (searched.ContainsKnown(entry.Key, entry.Hash)) return true;
            return false;
        }
        internal void ReplaceWith(PySetStorage result)
        {
            copyTemplate = null; shared = result.shared = true; layoutVersion++;
            table = result.table; Count = result.Count; fill = result.fill;
        }
        internal void ExceptWith(PySetStorage other)
        {
            if (ReferenceEquals(this, other)) { Clear(); return; }
            foreach (var entry in other.Entries()) RemoveKnown(entry.Key, entry.Hash);
        }
        internal void SymmetricExceptWith(PySetStorage other)
        {
            if (ReferenceEquals(this, other)) { Clear(); return; }
            foreach (var entry in other.Entries()) if (!RemoveKnown(entry.Key, entry.Hash)) AddKnown(entry.Key, entry.Hash);
        }
        internal long FrozenHash()
        {
            ulong hash = 0;
            foreach (var entry in Entries())
            {
                ulong value = Unsigned(entry.Hash);
                hash ^= unchecked(((value ^ 89869747UL) ^ (value << 16)) * 3644798167UL);
            }
            hash ^= unchecked(((ulong)Count + 1) * 1927868237UL);
            if (IntPtr.Size == 4) hash = (uint)hash;
            hash ^= (hash >> 11) ^ (hash >> 25);
            hash = unchecked(hash * 69069UL + 907133923UL);
            long result = IntPtr.Size == 8 ? unchecked((long)hash) : unchecked((int)hash);
            return result == -1 ? 590923713 : result;
        }
    }
}
