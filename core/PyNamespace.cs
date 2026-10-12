using System.Collections;

namespace SharpPy;

/// <summary>
/// Host string-key access to a Python namespace. All writes use the same ordered
/// mapping as globals()/module.__dict__; the raw lookup cache is not public.
/// </summary>
public sealed class PyNamespace : IDictionary<string, PyObject>
{
    private readonly Dictionary<string, PyObject> lookup = new();
    internal PyGlobalsDict Mapping { get; }
    public PyDict PythonDictionary => Mapping;
    internal bool GenericKeys { get; private set; }
    private int bulkMutations;
    private ICollection<string> keys;
    private ICollection<PyObject> values;

    public PyNamespace() => Mapping = new PyGlobalsDict(this);
    public PyObject this[string key]
    {
        get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
        set { ArgumentNullException.ThrowIfNull(key); Mapping.SetItem(StringCache.GetOrCreate(key), value); }
    }
    public int Count => GenericKeys ? Mapping.StringKeyCount() : lookup.Count;
    public bool IsReadOnly => false;
    public ICollection<string> Keys => keys ??= new Projection<string>(this, pair => pair.Key);
    public ICollection<PyObject> Values => values ??= new Projection<PyObject>(this, pair => pair.Value);
    public bool ContainsKey(string key) => TryGetValue(key, out _);
    public bool TryGetValue(string key, out PyObject value) => GenericKeys
        ? Mapping.TryGetStoredValue(StringCache.GetOrCreate(key), out value) : lookup.TryGetValue(key, out value);
    internal bool TryGetExactString(string key, out PyObject value) => lookup.TryGetValue(key, out value);
    public void Add(string key, PyObject value)
    {
        if (ContainsKey(key)) throw new ArgumentException("An item with the same key has already been added.", nameof(key));
        this[key] = value;
    }
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Mapping.RemoveName(key);
    }
    public void Clear()
    {
        // This host interface exposes string keys only; Python keys remain owned
        // by the Python mapping, as with the former string Dictionary adapter.
        Mapping.ClearStringKeys();
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
        var cursor = Mapping.GetStorageKeyEnumerator();
        while (cursor.MoveNext())
            if (cursor.Current is PyString key)
                yield return new KeyValuePair<string, PyObject>(key.Value, Mapping.ReadStoredKey(key));
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Called only after the corresponding Python storage mutation succeeds.
    internal void StoreFromMapping(string key, PyObject value) => lookup[key] = value;
    internal bool RemoveFromMapping(string key) => lookup.Remove(key);
    internal void ClearFromMapping() => lookup.Clear();
    internal void PrepareKey(PyObject key)
    {
        if (!GenericKeys && (key.GetType() != typeof(PyString) || key.HasCustomType)) InvalidateLookup();
    }
    internal void InvalidateLookup() { GenericKeys = true; lookup.Clear(); }
    internal void BeginBulkMutation() { bulkMutations++; InvalidateLookup(); }
    internal void EndBulkMutation() { bulkMutations--; RebuildLookup(); }
    internal void RebuildLookup()
    {
        lookup.Clear();
        if (bulkMutations != 0) { GenericKeys = true; return; }
        foreach (var pair in Mapping.InternalDict)
        {
            if (pair.Key.GetType() != typeof(PyString) || pair.Key.HasCustomType)
            {
                GenericKeys = true; lookup.Clear(); return;
            }
            lookup[((PyString)pair.Key).Value] = pair.Value;
        }
        GenericKeys = false;
    }

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
