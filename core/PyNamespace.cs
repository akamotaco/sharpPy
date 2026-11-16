using System.Collections;

namespace SharpPy;

/// <summary>
/// The host's view of a Python namespace: string keys over the one dict that is the namespace
/// (a module's md_dict, a frame's f_globals, the dict exec was given). The dict is the only
/// store; reads and writes go to it as PyDict_GetItem, PyDict_SetItem and PyDict_DelItem of the
/// name as a str, and the view enumerates the str keys in the dict's order.
/// </summary>
public sealed class PyNamespace : IDictionary<string, PyObject>
{
    private ICollection<string> keys;
    private ICollection<PyObject> values;

    /// <summary>A new, empty namespace.</summary>
    public PyNamespace() : this(new PyDict()) { }

    /// <summary>The namespace that is this dict.</summary>
    public PyNamespace(PyDict dict) => Dict = dict ?? throw new ArgumentNullException(nameof(dict));

    /// <summary>The dict itself: what globals(), module.__dict__ and f_globals are.</summary>
    internal PyDict Dict { get; }
    public PyDict PythonDictionary => Dict;

    public PyObject this[string key]
    {
        get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            // PyDict_SetItemString: the key is interned.
            Dict.DictAssSub(PyString.InternFrom(key), value);
        }
    }

    public int Count
    {
        get
        {
            int count = 0;
            foreach (var entry in Dict.InternalDict)
                if (entry.Key is PyString) count++;
            return count;
        }
    }

    public bool IsReadOnly => false;
    public ICollection<string> Keys => keys ??= new Projection<string>(this, pair => pair.Key);
    public ICollection<PyObject> Values => values ??= new Projection<PyObject>(this, pair => pair.Value);
    public bool ContainsKey(string key) => TryGetValue(key, out _);
    public bool TryGetValue(string key, out PyObject value) => Dict.TryGetStoredValue(PyString.From(key), out value);

    public void Add(string key, PyObject value)
    {
        if (ContainsKey(key)) throw new ArgumentException("An item with the same key has already been added.", nameof(key));
        this[key] = value;
    }

    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var name = PyString.From(key);
        if (!Dict.ContainsKey(name)) return false;
        Dict.DelItem(name);
        return true;
    }

    /// <summary>Removes the str keys; a key of another type belongs to Python code and stays.</summary>
    public void Clear()
    {
        var names = new List<PyObject>();
        foreach (var entry in Dict.InternalDict)
            if (entry.Key is PyString) names.Add(entry.Key);
        foreach (var name in names)
            Dict.DelItem(name);
    }

    public void Add(KeyValuePair<string, PyObject> item) => Add(item.Key, item.Value);
    public bool Contains(KeyValuePair<string, PyObject> item) =>
        TryGetValue(item.Key, out var value) && EqualityComparer<PyObject>.Default.Equals(value, item.Value);
    public bool Remove(KeyValuePair<string, PyObject> item) => Contains(item) && Remove(item.Key);

    public void CopyTo(KeyValuePair<string, PyObject>[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (array.Length - index < Count) throw new ArgumentException("Destination array is too small.");
        foreach (var pair in this) array[index++] = pair;
    }

    public IEnumerator<KeyValuePair<string, PyObject>> GetEnumerator()
    {
        // A snapshot: the host may change the namespace while it walks it.
        var entries = new List<KeyValuePair<PyObject, PyObject>>(Dict.InternalDict);
        foreach (var entry in entries)
            if (entry.Key is PyString key)
                yield return new KeyValuePair<string, PyObject>(key.Value, entry.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Projection<T>(PyNamespace owner, Func<KeyValuePair<string, PyObject>, T> select) : ICollection<T>
    {
        public int Count => owner.Count;
        public bool IsReadOnly => true;
        public bool Contains(T value)
        {
            foreach (var item in this) if (EqualityComparer<T>.Default.Equals(item, value)) return true;
            return false;
        }
        public void CopyTo(T[] array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < Count) throw new ArgumentException("Destination array is too small.");
            foreach (var item in this) array[index++] = item;
        }
        public IEnumerator<T> GetEnumerator() { foreach (var pair in owner) yield return select(pair); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void Add(T item) => throw new NotSupportedException();
        public bool Remove(T item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }
}
