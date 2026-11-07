using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// A type's dict (tp_dict). Attribute lookup reads it by name, so it is a table keyed by text,
    /// but as a dict it is CPython's: insertion order survives deletions, a key keeps the object it
    /// was inserted with (a str subclass stays one), and a key that is no str at all (type("C", (),
    /// {1: 2})) is an entry too, which no attribute lookup can reach.
    ///
    /// A CLR Dictionary enumerates in insertion order only until something is removed: the freed
    /// entry is reused by the next insertion. A removal here compacts the table, so a later key comes
    /// last (`del C.x; C.y = 1` lists y at the end). The entries without a str key, and the one order
    /// of both kinds, are kept only once such a key appears.
    /// </summary>
    public sealed class PyTypeDictionary : Dictionary<string, PyObject>
    {
        // The key object of a str key that is not an exact str, by its text.
        private Dictionary<string, PyObject> _keyObjects;
        // The entries whose key is no str, and from the first of them on, every key in order.
        private PyDict _others;
        private List<PyObject> _order;

        public PyTypeDictionary() { }

        public PyTypeDictionary(IDictionary<string, PyObject> source) : base(source)
        {
            if (source is not PyTypeDictionary from)
                return;
            if (from._keyObjects != null)
                _keyObjects = new Dictionary<string, PyObject>(from._keyObjects);
            if (from._others != null)
            {
                _others = from._others.Copy();
                _order = new List<PyObject>(from._order);
            }
        }

        public new PyObject this[string key]
        {
            get => base[key];
            set
            {
                bool added = !ContainsKey(key);
                base[key] = value;
                if (added)
                    _order?.Add(PyString.From(key));
            }
        }

        public new void Add(string key, PyObject value)
        {
            base.Add(key, value);
            _order?.Add(PyString.From(key));
        }

        public new bool TryAdd(string key, PyObject value)
        {
            if (!base.TryAdd(key, value))
                return false;
            _order?.Add(PyString.From(key));
            return true;
        }

        public new bool Remove(string key) => Remove(key, out _);

        public new bool Remove(string key, out PyObject value)
        {
            if (!base.Remove(key, out value))
                return false;
            _keyObjects?.Remove(key);
            _order?.RemoveAll(k => k is PyString s && s.Value == key);
            Compact();
            return true;
        }

        public new void Clear()
        {
            base.Clear();
            _keyObjects = null;
            _others = null;
            _order = null;
        }

        /// <summary>PyDict_SetItem with a key object: a str keeps the object it came with.</summary>
        internal void SetItem(PyObject key, PyObject value)
        {
            if (key is PyString text)
            {
                bool added = !ContainsKey(text.Value);
                this[text.Value] = value;
                // insertdict keeps the key a dict already has.
                if (added && !text.IsExact)
                    (_keyObjects ??= new Dictionary<string, PyObject>())[text.Value] = text;
                if (added && _order != null)
                    _order[^1] = text;
                return;
            }
            if (_others == null)
            {
                _others = new PyDict();
                _order = new List<PyObject>();
                foreach (var name in Keys)
                    _order.Add(KeyObject(name));
            }
            if (!_others.ContainsKey(key))
                _order.Add(key);
            _others.SetItem(key, value);
        }

        internal bool TryGetItem(PyObject key, out PyObject value)
        {
            if (key is PyString text)
                return TryGetValue(text.Value, out value);
            value = null;
            return _others != null && _others.TryGetStoredValue(key, out value);
        }

        internal int EntryCount => Count + (_others?.InternalDict.Count ?? 0);

        /// <summary>The dict's entries, key objects and values, in its one insertion order.</summary>
        internal IEnumerable<KeyValuePair<PyObject, PyObject>> Entries()
        {
            if (_order == null)
            {
                foreach (var entry in this)
                    yield return new KeyValuePair<PyObject, PyObject>(KeyObject(entry.Key), entry.Value);
                yield break;
            }
            var seen = new HashSet<string>();
            foreach (var key in _order.ToArray())
            {
                if (key is PyString text)
                {
                    if (TryGetValue(text.Value, out var value) && seen.Add(text.Value))
                        yield return new KeyValuePair<PyObject, PyObject>(text, value);
                }
                else if (_others.TryGetStoredValue(key, out var other))
                    yield return new KeyValuePair<PyObject, PyObject>(key, other);
            }
            // A key a caller added through the base table directly is not lost.
            foreach (var entry in this)
                if (seen.Add(entry.Key))
                    yield return new KeyValuePair<PyObject, PyObject>(KeyObject(entry.Key), entry.Value);
        }

        private PyObject KeyObject(string name) =>
            _keyObjects != null && _keyObjects.TryGetValue(name, out var key) ? key : PyString.From(name);

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
