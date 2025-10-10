using System;
using System.Collections.Generic;
using System.Linq;

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
            module.ModuleDict["deque"] = new PyDequeType();

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
                throw PyKeyError.Create(key.ToRepr());

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

        public override string ToRepr()
        {
            var factoryRepr = DefaultFactory?.ToRepr() ?? "None";

            if (_dict.Count == 0)
                return $"defaultdict({factoryRepr}, {{}})";

            var pairs = _dict.Select(kv => $"{kv.Key.ToRepr()}: {kv.Value.ToRepr()}");
            return $"defaultdict({factoryRepr}, {{{string.Join(", ", pairs)}}})";
        }
    }

    /// <summary>
    /// CPython 3.12: deque type - 양방향 큐
    /// Modules/_collectionsmodule.c: deque_type
    /// </summary>
    public class PyDequeType : PyObject
    {
        public PyDequeType() { }

        public override string GetTypeName() => "deque";

        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            // CPython: deque_new
            PyObject? iterable = null;
            int? maxlen = null;

            if (args.Length > 0)
                iterable = args[0];

            // Check for maxlen keyword argument
            if (kwargs != null && kwargs.InternalDict.TryGetValue(new PyString("maxlen"), out var maxlenObj))
            {
                if (maxlenObj is PyInt maxlenInt)
                    maxlen = (int)maxlenInt.Value;
                else if (!(maxlenObj is PyNone))
                    throw PyTypeError.Create("maxlen must be an integer or None");
            }

            var deque = new PyDeque(maxlen);

            // Initialize from iterable if provided
            if (iterable != null && !(iterable is PyNone))
            {
                var iterator = iterable.GetIterator();
                while (true)
                {
                    try
                    {
                        var item = iterator.Next();
                        deque.Append(item);
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        break;
                    }
                }
            }

            return deque;
        }
    }

    /// <summary>
    /// CPython 3.12: deque instance
    /// Double-ended queue with O(1) append/pop on both ends
    /// </summary>
    public class PyDeque : PyObject
    {
        private readonly LinkedList<PyObject> _items;
        private readonly int? _maxlen;

        public PyDeque(int? maxlen = null)
        {
            _items = new LinkedList<PyObject>();
            _maxlen = maxlen;
        }

        public override string GetTypeName() => "deque";

        // CPython: deque_append
        public void Append(PyObject item)
        {
            _items.AddLast(item);

            // Enforce maxlen
            if (_maxlen.HasValue && _items.Count > _maxlen.Value)
                _items.RemoveFirst();
        }

        // CPython: deque_appendleft
        public void AppendLeft(PyObject item)
        {
            _items.AddFirst(item);

            // Enforce maxlen
            if (_maxlen.HasValue && _items.Count > _maxlen.Value)
                _items.RemoveLast();
        }

        // CPython: deque_pop
        public PyObject Pop()
        {
            if (_items.Count == 0)
                throw PyIndexError.Create("pop from an empty deque");

            var item = _items.Last!.Value;
            _items.RemoveLast();
            return item;
        }

        // CPython: deque_popleft
        public PyObject PopLeft()
        {
            if (_items.Count == 0)
                throw PyIndexError.Create("pop from an empty deque");

            var item = _items.First!.Value;
            _items.RemoveFirst();
            return item;
        }

        // CPython: deque_len
        public int Length => _items.Count;

        // CPython: deque_rotate
        public void Rotate(int n = 1)
        {
            if (_items.Count == 0)
                return;

            n = n % _items.Count;
            if (n < 0)
                n += _items.Count;

            for (int i = 0; i < n; i++)
            {
                var item = _items.Last!.Value;
                _items.RemoveLast();
                _items.AddFirst(item);
            }
        }

        // CPython: deque_clear
        public void Clear()
        {
            _items.Clear();
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "append":
                    return new PyBuiltinFunction("append", (args, kwargs) => {
                        if (args.Length != 1)
                            throw PyTypeError.Create($"append() takes exactly one argument ({args.Length} given)");
                        Append(args[0]);
                        return PyNone.Instance;
                    });

                case "appendleft":
                    return new PyBuiltinFunction("appendleft", (args, kwargs) => {
                        if (args.Length != 1)
                            throw PyTypeError.Create($"appendleft() takes exactly one argument ({args.Length} given)");
                        AppendLeft(args[0]);
                        return PyNone.Instance;
                    });

                case "pop":
                    return new PyBuiltinFunction("pop", (args, kwargs) => {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"pop() takes no arguments ({args.Length} given)");
                        return Pop();
                    });

                case "popleft":
                    return new PyBuiltinFunction("popleft", (args, kwargs) => {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"popleft() takes no arguments ({args.Length} given)");
                        return PopLeft();
                    });

                case "rotate":
                    return new PyBuiltinFunction("rotate", (args, kwargs) => {
                        int n = 1;
                        if (args.Length > 0)
                        {
                            if (args[0] is PyInt nInt)
                                n = (int)nInt.Value;
                            else
                                throw PyTypeError.Create("rotate() argument must be an integer");
                        }
                        Rotate(n);
                        return PyNone.Instance;
                    });

                case "clear":
                    return new PyBuiltinFunction("clear", (args, kwargs) => {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"clear() takes no arguments ({args.Length} given)");
                        Clear();
                        return PyNone.Instance;
                    });

                case "maxlen":
                    return _maxlen.HasValue ? new PyInt(_maxlen.Value) : PyNone.Instance;

                case "__len__":
                    return new PyBuiltinFunction("__len__", (args, kwargs) => {
                        if (args.Length != 0)
                            throw PyTypeError.Create($"__len__() takes no arguments ({args.Length} given)");
                        return new PyInt(Length);
                    });

                default:
                    return base.GetAttribute(name);
            }
        }

        public override PyObject GetIterator()
        {
            return new PyDequeIterator(_items.ToList());
        }

        public override string ToRepr()
        {
            if (_items.Count == 0)
            {
                if (_maxlen.HasValue)
                    return $"deque([], maxlen={_maxlen.Value})";
                return "deque([])";
            }

            var items = string.Join(", ", _items.Select(x => x.ToRepr()));
            if (_maxlen.HasValue)
                return $"deque([{items}], maxlen={_maxlen.Value})";
            return $"deque([{items}])";
        }
    }

    /// <summary>
    /// Iterator for deque
    /// </summary>
    public class PyDequeIterator : PyObject
    {
        private readonly List<PyObject> _items;
        private int _index;

        public PyDequeIterator(List<PyObject> items)
        {
            _items = items;
            _index = 0;
        }

        public override string GetTypeName() => "deque_iterator";

        public override PyObject Next()
        {
            if (_index >= _items.Count)
                throw PyStopIteration.Create();

            return _items[_index++];
        }
    }
}
