using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// Synchronized wrapper for function.__dict__ that maintains bidirectional sync
    /// with the underlying C# Dictionary&lt;string, PyObject&gt;
    ///
    /// CPython 3.12 requirement: func.__dict__ is func.__dict__ must be True
    /// This means we must return the same object every time, AND modifications
    /// to the PyDict must be reflected in the underlying Attributes dictionary.
    /// </summary>
    internal class PyFunctionAttrDict : PyDict
    {
        private readonly Dictionary<string, PyObject> _underlyingDict;

        /// <summary>
        /// Creates a synchronized wrapper around a function's Attributes dictionary
        /// </summary>
        /// <param name="underlyingDict">The C# Dictionary to wrap (NOT copy)</param>
        public PyFunctionAttrDict(Dictionary<string, PyObject> underlyingDict)
        {
            _underlyingDict = underlyingDict ?? throw new ArgumentNullException(nameof(underlyingDict));

            // Initialize _dict and _keys from the underlying dictionary
            // We maintain our own PyObject-keyed dict for Python operations,
            // but sync all changes back to the underlying string-keyed dict
            foreach (var kv in underlyingDict)
            {
                var pyKey = new PyStr(kv.Key);
                _dict[pyKey] = kv.Value;
                _keys.Add(pyKey);
            }
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
                throw PyTypeError.Create($"function.__dict__ keys must be strings, not '{key.GetTypeName()}'");
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
