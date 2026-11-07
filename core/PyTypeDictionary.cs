using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// A type's dict (tp_dict) in CPython's order. A CLR Dictionary enumerates in insertion order
    /// only until something is removed: the freed entry is reused by the next insertion, which then
    /// shows up where the removed key was. CPython's dict keeps insertion order across deletions, so a
    /// removal here compacts the table and whatever is added later comes last, as in CPython
    /// (`del C.x; C.y = 1` lists y at the end; a class body's __qualname__, taken out by
    /// type_new_set_ht_name, does not lend its place to __dict__).
    /// </summary>
    public sealed class PyTypeDictionary : Dictionary<string, PyObject>
    {
        public PyTypeDictionary() { }

        public PyTypeDictionary(IDictionary<string, PyObject> source) : base(source) { }

        public new bool Remove(string key)
        {
            if (!base.Remove(key))
                return false;
            Compact();
            return true;
        }

        public new bool Remove(string key, out PyObject value)
        {
            if (!base.Remove(key, out value))
                return false;
            Compact();
            return true;
        }

        // The remaining entries in their order, into a table without free entries.
        private void Compact()
        {
            var kept = new List<KeyValuePair<string, PyObject>>(this);
            base.Clear();
            foreach (var entry in kept)
                base.Add(entry.Key, entry.Value);
        }
    }
}
