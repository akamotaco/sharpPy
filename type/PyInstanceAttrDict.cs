using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// Synchronized wrapper for instance.__dict__ that maintains bidirectional sync
    /// with the underlying C# Dictionary&lt;string, PyObject&gt;
    ///
    /// CPython 3.12 requirement: obj.__dict__ is obj.__dict__ must be True
    /// This means we must return the same object every time, AND modifications
    /// to the PyDict must be reflected in the underlying InstanceDict dictionary.
    /// </summary>
    internal class PyInstanceAttrDict : PyDict
    {
        private readonly Dictionary<string, PyObject> _underlyingDict;

        /// <summary>
        /// Creates a synchronized wrapper around an instance's InstanceDict dictionary
        /// </summary>
        /// <param name="underlyingDict">The C# Dictionary to wrap (NOT copy)</param>
        public PyInstanceAttrDict(Dictionary<string, PyObject> underlyingDict)
        {
            _underlyingDict = underlyingDict ?? throw new ArgumentNullException(nameof(underlyingDict));

            // Initialize _dict and _keys from the underlying dictionary
            // We maintain our own PyObject-keyed dict for Python operations,
            // but sync all changes back to the underlying string-keyed dict
            SyncFromUnderlying();
        }

        /// <summary>
        /// Sync _dict and _keys from underlying dictionary
        /// Called when underlying dict is modified externally (e.g., via SetAttribute)
        /// Public so PyClassInstance.SetAttribute can call it
        /// </summary>
        public void SyncFromUnderlying()
        {
            // Add new keys that are in underlying but not in _dict
            foreach (var kv in _underlyingDict)
            {
                var pyKey = new PyStr(kv.Key);
                if (!_dict.ContainsKey(pyKey))
                {
                    _dict[pyKey] = kv.Value;
                    _keys.Add(pyKey);
                }
                else
                {
                    // Update value if it changed
                    _dict[pyKey] = kv.Value;
                }
            }

            // Remove keys that are in _dict but not in underlying
            var keysToRemove = new List<PyObject>();
            foreach (var pyKey in _keys)
            {
                if (pyKey is PyStr strKey && !_underlyingDict.ContainsKey(strKey.Value))
                {
                    keysToRemove.Add(pyKey);
                }
            }
            foreach (var key in keysToRemove)
            {
                _dict.Remove(key);
                _keys.Remove(key);
            }
        }

        /// <summary>
        /// Override GetItem to sync before reading
        /// </summary>
        public override PyObject GetItem(PyObject key)
        {
            SyncFromUnderlying();
            return base.GetItem(key);
        }

        /// <summary>
        /// Override Contains to sync before checking
        /// </summary>
        public override PyBool Contains(PyObject item)
        {
            SyncFromUnderlying();
            return base.Contains(item);
        }

        /// <summary>
        /// Override GetIterator to sync before returning iterator
        /// </summary>
        public override PyObject GetIterator()
        {
            SyncFromUnderlying();
            return base.GetIterator();
        }

        /// <summary>
        /// Override Length to sync before returning count
        /// </summary>
        public override int Length()
        {
            SyncFromUnderlying();
            return base.Length();
        }

        /// <summary>
        /// Override SetItem to sync changes to underlying dictionary
        /// </summary>
        public override void SetItem(PyObject key, PyObject value)
        {
            // Call base to update _dict and _keys
            base.SetItem(key, value);

            // Sync to underlying dictionary
            if (key is PyStr strKey)
            {
                _underlyingDict[strKey.Value] = value;
            }
            else
            {
                throw PyTypeError.Create($"attribute name must be string, not '{key.GetTypeName()}'");
            }
        }

        /// <summary>
        /// Override DelItem to sync deletions to underlying dictionary
        /// </summary>
        public override PyObject DelItem(PyObject key)
        {
            // Call base to remove from _dict and _keys
            var result = base.DelItem(key);

            // Sync to underlying dictionary
            if (key is PyStr strKey)
            {
                _underlyingDict.Remove(strKey.Value);
            }

            return result;
        }

        /// <summary>
        /// Override Update to sync bulk updates to underlying dictionary
        /// </summary>
        public override PyNone Update(PyObject other)
        {
            if (other is PyDict otherDict)
            {
                // Update our dict and sync each item
                // Use the public InternalDict property to access other dict's contents
                foreach (var kv in otherDict.InternalDict)
                {
                    SetItem(kv.Key, kv.Value);  // SetItem handles sync
                }
            }
            else if (other is PyMappingProxy mappingProxy)
            {
                // PyMappingProxy uses string keys
                foreach (var key in mappingProxy.Keys)
                {
                    var pyKey = new PyStr(key);
                    var value = mappingProxy.GetItem(pyKey);
                    SetItem(pyKey, value);  // SetItem handles sync
                }
            }
            else
            {
                throw PyTypeError.Create($"update() argument must be dict or mapping, not '{other.GetTypeName()}'");
            }
            return PyNone.Instance;
        }

        /// <summary>
        /// Override Clear to sync clearing to underlying dictionary
        /// </summary>
        public override PyNone Clear()
        {
            // Call base to clear _dict and _keys
            _dict.Clear();
            _keys.Clear();

            // Sync to underlying dictionary
            _underlyingDict.Clear();

            return PyNone.Instance;
        }

        /// <summary>
        /// Override Pop to sync removal to underlying dictionary
        /// </summary>
        public override PyObject Pop(PyObject key, PyObject defaultValue = null)
        {
            // Try to remove from base dict
            if (_dict.TryGetValue(key, out PyObject value))
            {
                _dict.Remove(key);
                _keys.Remove(key);

                // Sync to underlying dictionary
                if (key is PyStr strKey)
                {
                    _underlyingDict.Remove(strKey.Value);
                }

                return value;
            }

            if (defaultValue != null)
                return defaultValue;

            throw PyKeyError.Create(key.ToRepr());
        }

        /// <summary>
        /// Override PopItem to sync removal to underlying dictionary
        /// </summary>
        public override PyTuple PopItem()
        {
            if (_dict.Count == 0)
                throw PyKeyError.Create("popitem(): dictionary is empty");

            // Python 3.7+: LIFO - remove last inserted key
            var lastKey = _keys[_keys.Count - 1];
            var value = _dict[lastKey];
            _dict.Remove(lastKey);
            _keys.RemoveAt(_keys.Count - 1);

            // Sync to underlying dictionary
            if (lastKey is PyStr strKey)
            {
                _underlyingDict.Remove(strKey.Value);
            }

            return TupleCache.CreatePair(lastKey, value);
        }
    }
}
