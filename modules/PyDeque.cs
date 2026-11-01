using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Modules
{
    internal static class PyDequeModule
    {
        internal static readonly PyType DequeType;

        static PyDequeModule()
        {
            var type = new PyType("deque", new[] { PyType.ObjectType }, "collections") { ConstructedByNewSlot = true };

            type.TypeDict["__new__"] = new PyMethodDescriptor("__new__", type,
                (cls, args, kwargs) => new PyDeque((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);

            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                var d = (PyDeque)self;
                d.Clear();
                PyObject iterable = null;
                PyObject maxlenObj = null;
                if (args.Length >= 1) iterable = args[0];
                if (args.Length >= 2) maxlenObj = args[1];
                if (kwargs != null)
                {
                    if (kwargs.TryGetStoredValue(new PyString("iterable"), out var it)) iterable = it;
                    if (kwargs.TryGetStoredValue(new PyString("maxlen"), out var ml)) maxlenObj = ml;
                }
                if (maxlenObj != null && !ReferenceEquals(maxlenObj, PyNone.Instance))
                {
                    int ml;
                    try { ml = maxlenObj.ToInt(); }
                    catch { throw PyValueError.Create("maxlen must be non-negative"); }
                    if (ml < 0) throw PyValueError.Create("maxlen must be non-negative");
                    d._maxlen = ml;
                }
                if (iterable != null && !ReferenceEquals(iterable, PyNone.Instance))
                    d.Extend(iterable);
                return PyNone.Instance;
            });

            type.TypeDict["append"] = new PyMethodDescriptor("append", type,
                (self, args, kwargs) => { ((PyDeque)self).Append(args[0]); return PyNone.Instance; }, 1, 1);

            type.TypeDict["appendleft"] = new PyMethodDescriptor("appendleft", type,
                (self, args, kwargs) => { ((PyDeque)self).AppendLeft(args[0]); return PyNone.Instance; }, 1, 1);

            type.TypeDict["pop"] = new PyMethodDescriptor("pop", type,
                (self, args, kwargs) => ((PyDeque)self).Pop(), 0, 0);

            type.TypeDict["popleft"] = new PyMethodDescriptor("popleft", type,
                (self, args, kwargs) => ((PyDeque)self).PopLeft(), 0, 0);

            type.TypeDict["extend"] = new PyMethodDescriptor("extend", type,
                (self, args, kwargs) => { ((PyDeque)self).Extend(args[0]); return PyNone.Instance; }, 1, 1);

            type.TypeDict["extendleft"] = new PyMethodDescriptor("extendleft", type,
                (self, args, kwargs) => { ((PyDeque)self).ExtendLeft(args[0]); return PyNone.Instance; }, 1, 1);

            type.TypeDict["clear"] = new PyMethodDescriptor("clear", type,
                (self, args, kwargs) => { ((PyDeque)self).Clear(); return PyNone.Instance; }, 0, 0);

            type.TypeDict["copy"] = new PyMethodDescriptor("copy", type,
                (self, args, kwargs) => ((PyDeque)self).Copy(), 0, 0);

            type.TypeDict["count"] = new PyMethodDescriptor("count", type,
                (self, args, kwargs) => new PyInt(((PyDeque)self).Count(args[0])), 1, 1);

            type.TypeDict["index"] = new PyMethodDescriptor("index", type,
                (self, args, kwargs) =>
                {
                    var d = (PyDeque)self;
                    int start = 0, stop = d._count;
                    if (args.Length >= 2) start = args[1].ToInt();
                    if (args.Length >= 3) stop = args[2].ToInt();
                    if (kwargs != null)
                    {
                        if (kwargs.TryGetStoredValue(new PyString("start"), out var s)) start = s.ToInt();
                        if (kwargs.TryGetStoredValue(new PyString("stop"), out var st)) stop = st.ToInt();
                    }
                    return new PyInt(d.Index(args[0], start, stop));
                }, 1, 3);

            type.TypeDict["insert"] = new PyMethodDescriptor("insert", type,
                (self, args, kwargs) => { ((PyDeque)self).Insert(args[0].ToInt(), args[1]); return PyNone.Instance; }, 2, 2);

            type.TypeDict["remove"] = new PyMethodDescriptor("remove", type,
                (self, args, kwargs) => { ((PyDeque)self).Remove(args[0]); return PyNone.Instance; }, 1, 1);

            type.TypeDict["reverse"] = new PyMethodDescriptor("reverse", type,
                (self, args, kwargs) => { ((PyDeque)self).Reverse(); return PyNone.Instance; }, 0, 0);

            type.TypeDict["rotate"] = new PyMethodDescriptor("rotate", type,
                (self, args, kwargs) =>
                {
                    int n = 1;
                    if (args.Length >= 1) n = args[0].ToInt();
                    if (kwargs != null && kwargs.TryGetStoredValue(new PyString("n"), out var nv)) n = nv.ToInt();
                    ((PyDeque)self).Rotate(n);
                    return PyNone.Instance;
                }, 0, 1);

            type.TypeDict["maxlen"] = new PyGetSetDescriptor("maxlen", type,
                self => ((PyDeque)self)._maxlen.HasValue ? (PyObject)new PyInt(((PyDeque)self)._maxlen.Value) : PyNone.Instance);

            type.TypeDict["__hash__"] = PyNone.Instance;

            type.TypeDict["__iadd__"] = new PyMethodDescriptor("__iadd__", type,
                (self, args, kwargs) => { ((PyDeque)self).Extend(args[0]); return self; }, 1, 1);

            type.TypeDict["__imul__"] = new PyMethodDescriptor("__imul__", type,
                (self, args, kwargs) => { ((PyDeque)self).InPlaceMultiply(args[0].ToInt()); return self; }, 1, 1);

            type.TypeDict["__copy__"] = new PyMethodDescriptor("__copy__", type,
                (self, args, kwargs) => ((PyDeque)self).Copy(), 0, 0);

            type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type,
                (self, args, kwargs) => ((PyDeque)self).Reduce(), 0, 0);

            // The sq_ slots, visible as the wrappers CPython's type dict has.
            type.TypeDict["__len__"] = new PyWrapperDescriptor("__len__", type, (self, args, kwargs) => new PyInt(self.Length()));
            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) => self.GetIterator());
            type.TypeDict["__contains__"] = new PyWrapperDescriptor("__contains__", type, (self, args, kwargs) => self.Contains(args[0]));
            type.TypeDict["__getitem__"] = new PyWrapperDescriptor("__getitem__", type, (self, args, kwargs) => self.GetItem(args[0]));
            type.TypeDict["__setitem__"] = new PyWrapperDescriptor("__setitem__", type, (self, args, kwargs) =>
            {
                self.SetItem(args[0], args[1]);
                return PyNone.Instance;
            });
            type.TypeDict["__delitem__"] = new PyWrapperDescriptor("__delitem__", type, (self, args, kwargs) =>
            {
                self.DelItem(args[0]);
                return PyNone.Instance;
            });
            type.TypeDict["__reversed__"] = new PyMethodDescriptor("__reversed__", type, (self, args, kwargs) =>
            {
                var deque = (PyDeque)self;
                var items = new PyList();
                for (int i = deque._count - 1; i >= 0; i--) items.Append(deque.GetItem(new PyInt(i)));
                return items.GetIterator();
            }, 0, 0);

            DequeType = type;
        }
    }

    public sealed class PyDeque : PyObject, IDictBackedInstance
    {
        public Dictionary<string, PyObject> InstanceDict { get; } = new Dictionary<string, PyObject>();
        private readonly PyType _type;
        internal PyDeque(PyType type) { _type = type; }
        public override PyType GetPyType() => _type;
        public override string GetTypeName() => _type.Name;

        // A Python subclass keeps its attributes in InstanceDict, like the other value subclasses.
        public override PyObject GetAttribute(string name) => _type is PyClass heap
            ? SubclassInstanceSupport.GetAttribute(this, heap, InstanceDict, name, base.GetAttribute)
            : base.GetAttribute(name);

        public override void SetAttribute(string name, PyObject value)
        {
            if (_type is PyClass heap) SubclassInstanceSupport.SetAttribute(this, heap, InstanceDict, name, value);
            else base.SetAttribute(name, value);
        }

        private PyObject[] _items = new PyObject[8];
        private int _head;
        internal int _count;
        private int _capacity = 8;
        internal int? _maxlen;
        internal int _state;

        private int Mask => _capacity - 1;
        private int Phys(int i) => (_head + i) & Mask;

        private void EnsureCapacity(int min)
        {
            if (min <= _capacity) return;
            int newCap = _capacity;
            while (newCap < min) newCap <<= 1;
            var newItems = new PyObject[newCap];
            for (int i = 0; i < _count; i++) newItems[i] = _items[Phys(i)];
            _items = newItems;
            _head = 0;
            _capacity = newCap;
        }

        internal void Clear()
        {
            Array.Clear(_items, 0, _capacity);
            _head = 0;
            _count = 0;
            _state++;
        }

        internal void Append(PyObject item)
        {
            if (_maxlen == 0) return;
            if (_count == _maxlen)
            {
                // Full: the left end drops, and the logical tail moves by one with the head.
                _items[_head] = null;
                _head = (_head + 1) & Mask;
                _items[Phys(_count - 1)] = item;
            }
            else
            {
                EnsureCapacity(_count + 1);
                _items[Phys(_count)] = item;
                _count++;
            }
            _state++;
        }

        internal void AppendLeft(PyObject item)
        {
            if (_maxlen == 0) return;
            if (_count == _maxlen)
            {
                _items[Phys(_count - 1)] = null;
                _head = (_head - 1) & Mask;
                _items[_head] = item;
            }
            else
            {
                EnsureCapacity(_count + 1);
                _head = (_head - 1) & Mask;
                _items[_head] = item;
                _count++;
            }
            _state++;
        }

        internal PyObject Pop()
        {
            if (_count == 0) throw PyIndexError.Create("pop from an empty deque");
            _count--;
            int idx = Phys(_count);
            var item = _items[idx];
            _items[idx] = null;
            _state++;
            return item;
        }

        internal PyObject PopLeft()
        {
            if (_count == 0) throw PyIndexError.Create("pop from an empty deque");
            var item = _items[_head];
            _items[_head] = null;
            _head = (_head + 1) & Mask;
            _count--;
            _state++;
            return item;
        }

        internal void Extend(PyObject iterable)
        {
            if (ReferenceEquals(iterable, this))
            {
                var copy = new PyObject[_count];
                for (int i = 0; i < _count; i++) copy[i] = _items[Phys(i)];
                foreach (var item in copy) Append(item);
                return;
            }
            var it = iterable.GetIterator();
            while (true)
            {
                PyObject item;
                try { item = it.Next(); }
                catch (PythonException e) when (e.PyException is PyStopIteration) { break; }
                Append(item);
            }
        }

        internal void ExtendLeft(PyObject iterable)
        {
            if (ReferenceEquals(iterable, this))
            {
                var copy = new PyObject[_count];
                for (int i = 0; i < _count; i++) copy[i] = _items[Phys(i)];
                for (int i = copy.Length - 1; i >= 0; i--) AppendLeft(copy[i]);
                return;
            }
            var it = iterable.GetIterator();
            while (true)
            {
                PyObject item;
                try { item = it.Next(); }
                catch (PythonException e) when (e.PyException is PyStopIteration) { break; }
                AppendLeft(item);
            }
        }

        internal void Rotate(int n)
        {
            if (_count <= 1) return;
            n %= _count;
            if (n < 0) n += _count;
            if (n == 0) return;
            if (n > _count / 2)
            {
                int m = _count - n;
                for (int i = 0; i < m; i++)
                {
                    var item = _items[_head];
                    _head = (_head + 1) & Mask;
                    _items[Phys(_count - 1)] = item;
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    var item = _items[Phys(_count - 1)];
                    _head = (_head - 1) & Mask;
                    _items[_head] = item;
                }
            }
            _state++;
        }

        internal void Reverse()
        {
            int lo = 0, hi = _count - 1;
            while (lo < hi)
            {
                var tmp = _items[Phys(lo)];
                _items[Phys(lo)] = _items[Phys(hi)];
                _items[Phys(hi)] = tmp;
                lo++; hi--;
            }
            _state++;
        }

        internal int Count(PyObject x)
        {
            int c = 0;
            for (int i = 0; i < _count; i++)
                if (ReferenceEquals(_items[Phys(i)], x) || _items[Phys(i)].RichCompare(x, PyObject.CompareOp.EQ).IsTrue())
                    c++;
            return c;
        }

        internal int Index(PyObject x, int start, int stop)
        {
            if (start < 0) start = Math.Max(0, _count + start);
            if (stop < 0) stop = Math.Max(0, _count + stop);
            if (stop > _count) stop = _count;
            for (int i = start; i < stop; i++)
                if (ReferenceEquals(_items[Phys(i)], x) || _items[Phys(i)].RichCompare(x, PyObject.CompareOp.EQ).IsTrue())
                    return i;
            throw PyValueError.Create(x.ToRepr().Value + " is not in deque");
        }

        internal void Insert(int i, PyObject x)
        {
            if (_maxlen.HasValue && _count == _maxlen.Value)
                throw PyIndexError.Create("deque already at its maximum size");
            if (i < 0) i = Math.Max(0, _count + i);
            if (i > _count) i = _count;
            EnsureCapacity(_count + 1);
            for (int j = _count; j > i; j--)
                _items[Phys(j)] = _items[Phys(j - 1)];
            _items[Phys(i)] = x;
            _count++;
            _state++;
        }

        internal void Remove(PyObject x)
        {
            for (int i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_items[Phys(i)], x) || _items[Phys(i)].RichCompare(x, PyObject.CompareOp.EQ).IsTrue())
                {
                    for (int j = i; j < _count - 1; j++)
                        _items[Phys(j)] = _items[Phys(j + 1)];
                    _items[Phys(_count - 1)] = null;
                    _count--;
                    _state++;
                    return;
                }
            }
            throw PyValueError.Create(x.ToRepr().Value + " is not in deque");
        }

        internal PyDeque Copy()
        {
            var d = new PyDeque(_type);
            d._maxlen = _maxlen;
            for (int i = 0; i < _count; i++) d.Append(_items[Phys(i)]);
            foreach (var kv in InstanceDict) d.InstanceDict[kv.Key] = kv.Value;
            return d;
        }

        internal void InPlaceMultiply(int n)
        {
            if (n <= 0) { Clear(); return; }
            if (n == 1) return;
            var copy = new PyObject[_count];
            for (int i = 0; i < _count; i++) copy[i] = _items[Phys(i)];
            Clear();
            for (int r = 0; r < n; r++)
                for (int i = 0; i < copy.Length; i++)
                    Append(copy[i]);
        }

        internal PyObject Reduce()
        {
            var args = new PyTuple(new PyObject[]
            {
                new PyTuple(Array.Empty<PyObject>()),
                _maxlen.HasValue ? (PyObject)new PyInt(_maxlen.Value) : PyNone.Instance
            });
            PyObject state = InstanceDict.Count == 0 ? PyNone.Instance : BuildStateDict();
            return new PyTuple(new PyObject[] { _type, args, state, GetIterator() });
        }

        private PyDict BuildStateDict()
        {
            var d = new PyDict();
            foreach (var kv in InstanceDict) d.SetItem(new PyString(kv.Key), kv.Value);
            return d;
        }

        public override int Length() => _count;
        public override bool IsTrue() => _count > 0;
        public override PyObject GetIterator() => new DequeIterator(this);

        public override PyBool Contains(PyObject item)
        {
            for (int i = 0; i < _count; i++)
                if (ReferenceEquals(_items[Phys(i)], item) || _items[Phys(i)].RichCompare(item, PyObject.CompareOp.EQ).IsTrue())
                    return PyBool.True;
            return PyBool.FromBool(false);
        }

        public override PyObject GetItem(PyObject index)
        {
            int i = ToIndex(index);
            if (i < 0 || i >= _count) throw PyIndexError.Create("deque index out of range");
            return _items[Phys(i)];
        }

        public override void SetItem(PyObject index, PyObject value)
        {
            int i = ToIndex(index);
            if (i < 0 || i >= _count) throw PyIndexError.Create("deque index out of range");
            _items[Phys(i)] = value;
        }

        public override void DelItem(PyObject index)
        {
            int i = ToIndex(index);
            if (i < 0 || i >= _count) throw PyIndexError.Create("deque index out of range");
            for (int j = i; j < _count - 1; j++)
                _items[Phys(j)] = _items[Phys(j + 1)];
            _items[Phys(_count - 1)] = null;
            _count--;
            _state++;
        }

        private int ToIndex(PyObject index)
        {
            int i;
            try { i = index.ToInt(); }
            catch { throw PyTypeError.Create("sequence index must be integer, not '" + index.GetTypeName() + "'"); }
            return i < 0 ? i + _count : i;
        }

        [ThreadStatic] private static HashSet<PyDeque> _reprGuard;

        public override PyString ToRepr()
        {
            _reprGuard ??= new HashSet<PyDeque>();
            if (!_reprGuard.Add(this)) return new PyString("[...]");
            try
            {
                var sb = new StringBuilder();
                sb.Append(_type.Name).Append("([");
                for (int i = 0; i < _count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(_items[Phys(i)].ToRepr().Value);
                }
                sb.Append(']');
                if (_maxlen.HasValue) sb.Append(", maxlen=").Append(_maxlen.Value);
                sb.Append(')');
                return new PyString(sb.ToString());
            }
            finally { _reprGuard.Remove(this); }
        }

        public override PyString AsString() => ToRepr();

        public override PyObject Add(PyObject other)
        {
            if (other is not PyDeque d)
                throw PyTypeError.Create("can only concatenate deque (not \"" + other.GetTypeName() + "\") to deque");
            var result = new PyDeque(_type);
            result._maxlen = _maxlen;
            for (int i = 0; i < _count; i++) result.Append(_items[Phys(i)]);
            for (int i = 0; i < d._count; i++) result.Append(d._items[d.Phys(i)]);
            return result;
        }

        public override PyObject Multiply(PyObject other)
        {
            int n;
            try { n = other.ToInt(); }
            catch { return PyNotImplemented.Instance; }
            var result = new PyDeque(_type);
            result._maxlen = _maxlen;
            for (int r = 0; r < n; r++)
                for (int i = 0; i < _count; i++)
                    result.Append(_items[Phys(i)]);
            return result;
        }

        public override PyObject RichCompare(PyObject other, PyObject.CompareOp op)
        {
            if (other is not PyDeque d) return PyNotImplemented.Instance;
            int minLen = Math.Min(_count, d._count);
            for (int i = 0; i < minLen; i++)
            {
                var a = _items[Phys(i)];
                var b = d._items[d.Phys(i)];
                bool eq = ReferenceEquals(a, b) || a.RichCompare(b, PyObject.CompareOp.EQ).IsTrue();
                if (!eq)
                {
                    if (op == PyObject.CompareOp.EQ) return PyBool.FromBool(false);
                    if (op == PyObject.CompareOp.NE) return PyBool.True;
                    bool lt = a.RichCompare(b, PyObject.CompareOp.LT).IsTrue();
                    if (lt)
                        return (op == PyObject.CompareOp.LT || op == PyObject.CompareOp.LE) ? PyBool.True : PyBool.FromBool(false);
                    return (op == PyObject.CompareOp.GT || op == PyObject.CompareOp.GE) ? PyBool.True : PyBool.FromBool(false);
                }
            }
            return op switch
            {
                PyObject.CompareOp.EQ => PyBool.FromBool(_count == d._count),
                PyObject.CompareOp.NE => PyBool.FromBool(_count != d._count),
                PyObject.CompareOp.LT => PyBool.FromBool(_count < d._count),
                PyObject.CompareOp.LE => PyBool.FromBool(_count <= d._count),
                PyObject.CompareOp.GT => PyBool.FromBool(_count > d._count),
                PyObject.CompareOp.GE => PyBool.FromBool(_count >= d._count),
                _ => PyNotImplemented.Instance
            };
        }

        private sealed class DequeIterator : PyIterator
        {
            private readonly PyDeque _deque;
            private int _remaining;
            private int _index;
            private readonly int _state;

            internal DequeIterator(PyDeque deque)
            {
                _deque = deque;
                _remaining = deque._count;
                _index = 0;
                _state = deque._state;
            }

            protected override string IteratorName => "_deque_iterator";

            public override PyObject Next()
            {
                if (_remaining == 0) throw PyStopIteration.Create();
                if (_deque._state != _state) throw PyRuntimeError.Create("deque mutated during iteration");
                var item = _deque._items[_deque.Phys(_index)];
                _index++;
                _remaining--;
                return item;
            }
        }
    }
}