using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython _collections C 확장 모듈
    /// CPython 3.12 호환 구현
    /// </summary>
    public static class _CollectionsModule
    {
        public static PyModule CreateCollectionsModule()
        {
            var module = new PyModule("_collections", "High performance container datatypes");

            // CPython 3.12: Core C extension types
            module.ModuleDict["defaultdict"] = new PyDefaultDictType();
            module.ModuleDict["deque"] = PyDequeModule.DequeType;

            return module;
        }
    }

    /// <summary>
    /// CPython 3.12: defaultdict type
    /// Modules/_collectionsmodule.c: defdict_type
    /// </summary>
    public class PyDefaultDictType : PyObject
    {
        public PyDefaultDictType() { }

        public override string GetTypeName() => "defaultdict";

        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            // CPython: defdict_new
            PyObject? defaultFactory = null;

            if (args.Length > 0)
            {
                defaultFactory = args[0];
                if (defaultFactory is PyNone)
                    defaultFactory = null;
            }

            var dd = new PyDefaultDict(defaultFactory);

            // If there are more args, treat as dict initialization
            if (args.Length > 1)
            {
                // Second argument should be a dict or iterable of pairs
                var initDict = args[1];
                if (initDict is PyDict dict)
                {
                    foreach (var kv in dict.InternalDict)
                        dd.SetItem(kv.Key, kv.Value);
                }
            }

            // Handle keyword arguments
            if (kwargs != null)
            {
                foreach (var kv in kwargs.InternalDict)
                    dd.SetItem(kv.Key, kv.Value);
            }

            return dd;
        }
    }

    /// <summary>
    /// CPython 3.12: defaultdict instance
    /// dict subclass with default_factory
    /// </summary>
    public class PyDefaultDict : PyDict
    {
        public PyObject? DefaultFactory { get; set; }

        public PyDefaultDict(PyObject? defaultFactory = null)
        {
            DefaultFactory = defaultFactory;
        }

        public override string GetTypeName() => "defaultdict";

        public override PyObject GetItem(PyObject key)
        {
            // Try to get existing value
            if (_dict.TryGetValue(key, out PyObject? value))
                return value;

            // CPython: defdict_missing - call default_factory if available
            if (DefaultFactory == null)
                throw PyKeyError.Create(key);

            // Call default_factory() to create default value
            var defaultValue = DefaultFactory.Call(new PyObject[0], null);

            // Store and return
            _dict[key] = defaultValue;
            return defaultValue;
        }

        public override PyObject GetAttribute(string name)
        {
            if (name == "default_factory")
                return DefaultFactory ?? PyNone.Instance;

            return base.GetAttribute(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            if (name == "default_factory")
            {
                DefaultFactory = value is PyNone ? null : value;
                return;
            }

            base.SetAttribute(name, value);
        }

        public override PyString ToRepr()
        {
            var factoryRepr = DefaultFactory?.ToRepr().Value ?? "None";

            if (_dict.Count == 0)
                return new PyString($"defaultdict({factoryRepr}, {{}})");

            // Performance: Eliminated LINQ
            var pairs = new List<string>();
            foreach (var kv in _dict)
            {
                pairs.Add($"{kv.Key.ToRepr().Value}: {kv.Value.ToRepr().Value}");
            }
            return new PyString($"defaultdict({factoryRepr}, {{{string.Join(", ", pairs)}}})");
        }
    }
}
