using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// The deque, _deque_iterator and _deque_reverse_iterator types of Modules/_collectionsmodule.c.
    /// </summary>
    internal static class PyDequeModule
    {
        internal static readonly PyType DequeType;
        internal static readonly PyType DequeIterType;
        internal static readonly PyType DequeRevIterType;

        // deque_spec, with its basicsize: sizeof(dequeobject), and sizeof(block).
        internal const long BasicSize = 216;
        internal const long BlockSize = 528;

        private static PyDeque Self(PyObject self) => (PyDeque)self;

        private static void Method(PyType type, string name, PyCall kind, Func<PyDeque, PyObject[], PyObject> body) =>
            PyArgs.Define(type, name, kind, (self, args) => body(Self(self), args));

        // METH_FASTCALL without keywords: the descriptor refuses keywords under its qualname.
        private static void Fast(PyType type, string name, Func<PyDeque, PyObject[], PyObject> body) =>
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"deque.{name}() takes no keyword arguments");
                return body(Self(self), args);
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);

        static PyDequeModule()
        {
            var type = new PyType("deque", new[] { PyType.ObjectType }, "collections") { ConstructedByNewSlot = true };
            DequeType = type;
            type.TypeDict["__module__"] = new PyString("collections");

            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => new PyDeque((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                Self(self).Init(args, kwargs);
                return PyNone.Instance;
            });

            Method(type, "append", PyCall.One, (d, args) => { d.AppendInternal(args[0], d.MaxLen); return PyNone.Instance; });
            Method(type, "appendleft", PyCall.One, (d, args) => { d.AppendLeftInternal(args[0]); return PyNone.Instance; });
            Method(type, "clear", PyCall.NoArgs, (d, args) => { d.Clear(); return PyNone.Instance; });
            Method(type, "__copy__", PyCall.NoArgs, (d, args) => d.Copy());
            Method(type, "copy", PyCall.NoArgs, (d, args) => d.Copy());
            Method(type, "count", PyCall.One, (d, args) => new PyInt(d.Count(args[0])));
            Method(type, "extend", PyCall.One, (d, args) => { d.Extend(args[0]); return PyNone.Instance; });
            Method(type, "extendleft", PyCall.One, (d, args) => { d.ExtendLeft(args[0]); return PyNone.Instance; });
            Fast(type, "index", (d, args) => new PyInt(d.Index(args)));
            Fast(type, "insert", (d, args) => { d.Insert(args); return PyNone.Instance; });
            Method(type, "pop", PyCall.NoArgs, (d, args) => d.Pop());
            Method(type, "popleft", PyCall.NoArgs, (d, args) => d.PopLeft());
            Method(type, "__reduce__", PyCall.NoArgs, (d, args) => d.Reduce());
            Method(type, "remove", PyCall.One, (d, args) => { d.Remove(args[0]); return PyNone.Instance; });
            Method(type, "__reversed__", PyCall.NoArgs, (d, args) => new PyDeque.DequeIterator(d, reverse: true));
            Method(type, "reverse", PyCall.NoArgs, (d, args) => { d.Reverse(); return PyNone.Instance; });
            Fast(type, "rotate", (d, args) =>
            {
                PyArgs.Positional("deque.rotate", args, null, 0, 1);
                long n = args.Length == 0 ? 1 : PyArgs.Ssize(args[0]);
                d.Rotate(n);
                return PyNone.Instance;
            });
            Method(type, "__sizeof__", PyCall.NoArgs, (d, args) => new PyInt(d.SizeOf()));
            type.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__", (cls, arg) => new SharpPy.PyGenericAlias((PyType)cls, arg));
            type.TypeDict["maxlen"] = new PyGetSetDescriptor("maxlen", type,
                self => Self(self).MaxLen < 0 ? PyNone.Instance : new PyInt(Self(self).MaxLen));
            type.TypeDict["__hash__"] = PyNone.Instance;
            TypeTables.Repr(type);
            TypeTables.Compared(type);

            // The sequence slots and the wrappers add_operators makes of them.
            type.TypeDict["__len__"] = new PyWrapperDescriptor("__len__", type, (self, args, kwargs) => new PyInt(Self(self).Size));
            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) => new PyDeque.DequeIterator(Self(self)));
            type.TypeDict["__contains__"] = new PyWrapperDescriptor("__contains__", type, (self, args, kwargs) => PyBool.FromBool(Self(self).DequeContains(args[0])));
            type.TypeDict["__getitem__"] = new PyWrapperDescriptor("__getitem__", type, (self, args, kwargs) => Self(self).MpSubscript(args[0]));
            type.TypeDict["__setitem__"] = new PyWrapperDescriptor("__setitem__", type, (self, args, kwargs) =>
            {
                Self(self).MpAssSubscript(args[0], args[1]);
                return PyNone.Instance;
            });
            type.TypeDict["__delitem__"] = new PyWrapperDescriptor("__delitem__", type, (self, args, kwargs) =>
            {
                Self(self).MpDelSubscript(args[0]);
                return PyNone.Instance;
            });
            type.TypeDict["__add__"] = new PyWrapperDescriptor("__add__", type, (self, args, kwargs) => Self(self).Concat(args[0]));
            type.TypeDict["__iadd__"] = new PyWrapperDescriptor("__iadd__", type, (self, args, kwargs) =>
            {
                Self(self).Extend(args[0]);
                return self;
            });
            // wrap_indexargfunc: the count is PyNumber_AsSsize_t(arg, PyExc_OverflowError).
            type.TypeDict["__mul__"] = new PyWrapperDescriptor("__mul__", type, (self, args, kwargs) => Self(self).Repeat(PyArgs.IndexSsize(args[0])));
            type.TypeDict["__rmul__"] = new PyWrapperDescriptor("__rmul__", type, (self, args, kwargs) => Self(self).Repeat(PyArgs.IndexSsize(args[0])));
            type.TypeDict["__imul__"] = new PyWrapperDescriptor("__imul__", type, (self, args, kwargs) => Self(self).InplaceRepeat(PyArgs.IndexSsize(args[0])));
            if (PyDocs.Lookup("collections.deque") is { } doc)
                type.TypeDict["__doc__"] = PyDocs.DocFromInternal("deque", doc);
            type.CollectionFlags = PyType.TPFLAGS_SEQUENCE;

            DequeIterType = IteratorType("_deque_iterator", reverse: false);
            DequeRevIterType = IteratorType("_deque_reverse_iterator", reverse: true);
        }

        // dequeiter_spec and dequereviter_spec
        private static PyType IteratorType(string name, bool reverse)
        {
            var type = new PyType(name, new[] { PyType.ObjectType }, "collections") { ConstructedByNewSlot = true };
            type.TypeDict["__module__"] = new PyString("collections");
            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) => self);
            type.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", type, (self, args, kwargs) => self.IterNext());
            PyArgs.Define(type, "__length_hint__", PyCall.NoArgs, (self, args) => new PyInt(((PyDeque.DequeIterator)self).Counter));
            PyArgs.Define(type, "__reduce__", PyCall.NoArgs, (self, args) => ((PyDeque.DequeIterator)self).Reduce());
            // dequeiter_new: "O!|n", then as many items consumed as the index says.
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{name}() takes no keyword arguments");
                PyArgs.ParseTuple(name, args, 1, 2);
                if (args[0] is not PyDeque deque)
                    throw PyTypeError.Create($"argument 1 must be collections.deque, not {args[0].GetTypeName()}");
                long index = args.Length > 1 ? PyArgs.Ssize(args[1]) : 0;
                var it = new PyDeque.DequeIterator(deque, reverse);
                for (long i = 0; i < index; i++)
                    if (!it.TryNext(out _))
                        break;
                return it;
            }, 0, int.MaxValue, acceptsKwargs: true);
            return type;
        }
    }

    /// <summary>
    /// dequeobject: a doubly linked list of blocks of BLOCKLEN slots. The first item is at
    /// leftblock[leftindex] and the last at rightblock[rightindex]; state counts every move of the
    /// indices, which is how iterators and searches see a mutation.
    /// </summary>
    public sealed class PyDeque : PyObject, IDictBackedInstance
    {
        private const int BLOCKLEN = 64;
        private const int CENTER = (BLOCKLEN - 1) / 2;

        internal sealed class Block
        {
            internal Block LeftLink;
            internal Block RightLink;
            internal readonly PyObject[] Data = new PyObject[BLOCKLEN];
        }

        public Dictionary<string, PyObject> InstanceDict { get; } = new Dictionary<string, PyObject>();
        private readonly PyType _type;

        private Block _leftblock;
        private Block _rightblock;
        private int _leftindex;
        private int _rightindex;
        internal long Size;
        internal ulong State;
        internal long MaxLen = -1;

        // deque_new
        internal PyDeque(PyType type)
        {
            _type = type;
            var b = new Block();
            _leftblock = b;
            _rightblock = b;
            _leftindex = CENTER + 1;
            _rightindex = CENTER;
        }

        public override PyType GetPyType() => _type;
        public override string GetTypeName() => _type.FullName;

        // A Python subclass keeps its attributes in InstanceDict, like the other value subclasses.
        public override PyObject GetAttribute(string name) => _type is PyClass heap
            ? SubclassInstanceSupport.GetAttribute(this, heap, name)
            : base.GetAttribute(name);

        internal override PyObject GenericGetAttr(string name) => _type is PyClass heap
            ? SubclassInstanceSupport.GenericGetAttribute(this, heap, InstanceDict, name, base.GetAttribute)
            : base.GetAttribute(name);

        public override void SetAttribute(string name, PyObject value)
        {
            if (_type is PyClass heap) SubclassInstanceSupport.SetAttribute(this, heap, InstanceDict, name, value);
            else base.SetAttribute(name, value);
        }

        private static Exception Mutated() => PyRuntimeError.Create("deque mutated during iteration");

        // deque_pop
        internal PyObject Pop()
        {
            if (Size == 0)
                throw PyIndexError.Create("pop from an empty deque");
            var item = _rightblock.Data[_rightindex];
            _rightblock.Data[_rightindex] = null;
            _rightindex--;
            Size--;
            State++;
            if (_rightindex < 0)
            {
                if (Size != 0)
                {
                    var prevblock = _rightblock.LeftLink;
                    prevblock.RightLink = null;
                    _rightblock = prevblock;
                    _rightindex = BLOCKLEN - 1;
                }
                else
                {
                    _leftindex = CENTER + 1;
                    _rightindex = CENTER;
                }
            }
            return item;
        }

        // deque_popleft
        internal PyObject PopLeft()
        {
            if (Size == 0)
                throw PyIndexError.Create("pop from an empty deque");
            var item = _leftblock.Data[_leftindex];
            _leftblock.Data[_leftindex] = null;
            _leftindex++;
            Size--;
            State++;
            if (_leftindex == BLOCKLEN)
            {
                if (Size != 0)
                {
                    var prevblock = _leftblock.RightLink;
                    prevblock.LeftLink = null;
                    _leftblock = prevblock;
                    _leftindex = 0;
                }
                else
                {
                    _leftindex = CENTER + 1;
                    _rightindex = CENTER;
                }
            }
            return item;
        }

        // NEEDS_TRIM: 0 <= maxlen < size, in one unsigned test.
        private bool NeedsTrim(long maxlen) => (ulong)maxlen < (ulong)Size;

        // deque_append_internal
        internal void AppendInternal(PyObject item, long maxlen)
        {
            if (_rightindex == BLOCKLEN - 1)
            {
                var b = new Block { LeftLink = _rightblock };
                _rightblock.RightLink = b;
                _rightblock = b;
                _rightindex = -1;
            }
            Size++;
            _rightindex++;
            _rightblock.Data[_rightindex] = item;
            if (NeedsTrim(maxlen))
                PopLeft();
            else
                State++;
        }

        // deque_appendleft_internal: it trims to the deque's own maxlen.
        internal void AppendLeftInternal(PyObject item)
        {
            if (_leftindex == 0)
            {
                var b = new Block { RightLink = _leftblock };
                _leftblock.LeftLink = b;
                _leftblock = b;
                _leftindex = BLOCKLEN;
            }
            Size++;
            _leftindex--;
            _leftblock.Data[_leftindex] = item;
            if (NeedsTrim(MaxLen))
                Pop();
            else
                State++;
        }

        // The items in order, for PySequence_List(deque).
        private PyList AsList()
        {
            var list = new PyList();
            var it = new DequeIterator(this);
            while (it.TryNext(out var item))
                list.Append(item);
            return list;
        }

        // finalize_iterator around a loop over iternext
        private static IEnumerable<PyObject> Drain(PyObject iterator)
        {
            while (true)
            {
                PyObject item;
                try
                {
                    item = iterator.Next();
                }
                catch (PythonException e) when (e.PyException is PyStopIteration)
                {
                    yield break;
                }
                yield return item;
            }
        }

        // deque_extend
        internal void Extend(PyObject iterable)
        {
            long maxlen = MaxLen;
            if (ReferenceEquals(iterable, this))
            {
                Extend(AsList());
                return;
            }
            var it = iterable.GetIterator();
            if (maxlen == 0)
            {
                foreach (var _ in Drain(it)) { }
                return;
            }
            // Space saving heuristic: start filling from the left.
            if (Size == 0)
            {
                _leftindex = 1;
                _rightindex = 0;
            }
            foreach (var item in Drain(it))
                AppendInternal(item, maxlen);
        }

        // deque_extendleft
        internal void ExtendLeft(PyObject iterable)
        {
            if (ReferenceEquals(iterable, this))
            {
                ExtendLeft(AsList());
                return;
            }
            var it = iterable.GetIterator();
            if (MaxLen == 0)
            {
                foreach (var _ in Drain(it)) { }
                return;
            }
            // Space saving heuristic: start filling from the right.
            if (Size == 0)
            {
                _leftindex = BLOCKLEN - 1;
                _rightindex = BLOCKLEN - 2;
            }
            foreach (var item in Drain(it))
                AppendLeftInternal(item);
        }

        // deque_copy: an exact deque is built here, a subclass by calling its type.
        internal PyObject Copy()
        {
            if (ReferenceEquals(_type, PyDequeModule.DequeType))
            {
                var newDeque = new PyDeque(PyDequeModule.DequeType) { MaxLen = MaxLen };
                if (Size == 1)
                    newDeque.AppendInternal(_leftblock.Data[_leftindex], newDeque.MaxLen);
                else
                    newDeque.Extend(this);
                return newDeque;
            }
            var result = MaxLen < 0
                ? _type.Call(new PyObject[] { this }, null)
                : _type.Call(new PyObject[] { this, new PyInt(MaxLen) }, null);
            if (result is not PyDeque)
                throw PyTypeError.Create($"{_type.FullName}() must return a deque, not {result.GetTypeName()}");
            return result;
        }

        // deque_concat
        internal PyObject Concat(PyObject other)
        {
            if (!BuiltinsModule.IsInstance(new PyObject[] { other, PyDequeModule.DequeType }).IsTrue())
                throw PyTypeError.Create($"can only concatenate deque (not \"{other.GetTypeName()}\") to deque");
            var newDeque = (PyDeque)Copy();
            newDeque.Extend(other);
            return newDeque;
        }

        // deque_clear: the deque is emptied first, then the old blocks are let go.
        internal void Clear()
        {
            if (Size == 0)
                return;
            var b = new Block();
            Size = 0;
            _leftblock = b;
            _rightblock = b;
            _leftindex = CENTER + 1;
            _rightindex = CENTER;
            State++;
        }

        // deque_inplace_repeat
        internal PyObject InplaceRepeat(long n)
        {
            long size = Size;
            if (size == 0 || n == 1)
                return this;
            if (n <= 0)
            {
                Clear();
                return this;
            }
            if (size == 1)
            {
                var item = _leftblock.Data[_leftindex];
                if (MaxLen >= 0 && n > MaxLen)
                    n = MaxLen;
                State++;
                for (long i = 0; i < n - 1; i++)
                {
                    if (_rightindex == BLOCKLEN - 1)
                    {
                        var b = new Block { LeftLink = _rightblock };
                        _rightblock.RightLink = b;
                        _rightblock = b;
                        _rightindex = -1;
                    }
                    _rightindex++;
                    _rightblock.Data[_rightindex] = item;
                    Size++;
                }
                return this;
            }
            if ((ulong)size > (ulong)long.MaxValue / (ulong)n)
                throw PyMemoryError.Create();
            var seq = AsList();
            // Reduce the number of repetitions when maxlen would be exceeded.
            if (MaxLen >= 0 && n * size > MaxLen)
                n = (MaxLen + size - 1) / size;
            for (long i = 0; i < n - 1; i++)
                Extend(seq);
            return this;
        }

        // deque_repeat
        internal PyObject Repeat(long n) => ((PyDeque)Copy()).InplaceRepeat(n);

        // _deque_rotate: pointers move between the ends a block at a time.
        internal void Rotate(long n)
        {
            var leftblock = _leftblock;
            var rightblock = _rightblock;
            int leftindex = _leftindex;
            int rightindex = _rightindex;
            long len = Size, halflen = len >> 1;
            if (len <= 1)
                return;
            if (n > halflen || n < -halflen)
            {
                n %= len;
                if (n > halflen)
                    n -= len;
                else if (n < -halflen)
                    n += len;
            }
            State++;
            Block b = null;
            while (n > 0)
            {
                if (leftindex == 0)
                {
                    b ??= new Block();
                    b.RightLink = leftblock;
                    b.LeftLink = null;
                    leftblock.LeftLink = b;
                    leftblock = b;
                    leftindex = BLOCKLEN;
                    b = null;
                }
                long m = n;
                if (m > rightindex + 1)
                    m = rightindex + 1;
                if (m > leftindex)
                    m = leftindex;
                rightindex -= (int)m;
                leftindex -= (int)m;
                int src = rightindex + 1, dest = leftindex;
                n -= m;
                for (; m > 0; m--)
                {
                    leftblock.Data[dest++] = rightblock.Data[src];
                    rightblock.Data[src++] = null;
                }
                if (rightindex < 0)
                {
                    b = rightblock;
                    rightblock = rightblock.LeftLink;
                    rightblock.RightLink = null;
                    rightindex = BLOCKLEN - 1;
                }
            }
            while (n < 0)
            {
                if (rightindex == BLOCKLEN - 1)
                {
                    b ??= new Block();
                    b.LeftLink = rightblock;
                    b.RightLink = null;
                    rightblock.RightLink = b;
                    rightblock = b;
                    rightindex = -1;
                    b = null;
                }
                long m = -n;
                if (m > BLOCKLEN - leftindex)
                    m = BLOCKLEN - leftindex;
                if (m > BLOCKLEN - 1 - rightindex)
                    m = BLOCKLEN - 1 - rightindex;
                int src = leftindex, dest = rightindex + 1;
                leftindex += (int)m;
                rightindex += (int)m;
                n += m;
                for (; m > 0; m--)
                {
                    rightblock.Data[dest++] = leftblock.Data[src];
                    leftblock.Data[src++] = null;
                }
                if (leftindex == BLOCKLEN)
                {
                    b = leftblock;
                    leftblock = leftblock.RightLink;
                    leftblock.LeftLink = null;
                    leftindex = 0;
                }
            }
            _leftblock = leftblock;
            _rightblock = rightblock;
            _leftindex = leftindex;
            _rightindex = rightindex;
        }

        // deque_reverse
        internal void Reverse()
        {
            var leftblock = _leftblock;
            var rightblock = _rightblock;
            int leftindex = _leftindex;
            int rightindex = _rightindex;
            long n = Size >> 1;
            while (--n >= 0)
            {
                var tmp = leftblock.Data[leftindex];
                leftblock.Data[leftindex] = rightblock.Data[rightindex];
                rightblock.Data[rightindex] = tmp;
                leftindex++;
                if (leftindex == BLOCKLEN)
                {
                    leftblock = leftblock.RightLink;
                    leftindex = 0;
                }
                rightindex--;
                if (rightindex < 0)
                {
                    rightblock = rightblock.LeftLink;
                    rightindex = BLOCKLEN - 1;
                }
            }
        }

        private static bool Equal(PyObject item, PyObject v) => PyObject.CompareBool(item, v, PyObject.CompareOp.EQ);

        // deque_count
        internal long Count(PyObject v)
        {
            var b = _leftblock;
            int index = _leftindex;
            long n = Size;
            long count = 0;
            ulong startState = State;
            while (--n >= 0)
            {
                if (Equal(b.Data[index], v))
                    count++;
                if (startState != State)
                    throw Mutated();
                index++;
                if (index == BLOCKLEN)
                {
                    b = b.RightLink;
                    index = 0;
                }
            }
            return count;
        }

        // deque_contains
        internal bool DequeContains(PyObject v)
        {
            var b = _leftblock;
            int index = _leftindex;
            long n = Size;
            ulong startState = State;
            while (--n >= 0)
            {
                if (Equal(b.Data[index], v))
                    return true;
                if (startState != State)
                    throw Mutated();
                index++;
                if (index == BLOCKLEN)
                {
                    b = b.RightLink;
                    index = 0;
                }
            }
            return false;
        }

        protected internal override PyBool ContainsItem(PyObject item) => PyBool.FromBool(DequeContains(item));

        // _PyEval_SliceIndexNotNone
        private static long SliceIndexNotNone(PyObject value)
        {
            var index = PyObject.TryGetIndex(value)
                ?? throw PyTypeError.Create("slice indices must be integers or have an __index__ method");
            return index.Value > long.MaxValue ? long.MaxValue : index.Value < long.MinValue ? long.MinValue : (long)index.Value;
        }

        // deque_index: "O|O&O&:index"
        internal long Index(PyObject[] args)
        {
            PyArgs.ParseTuple("index", args, 1, 3);
            var v = args[0];
            long start = 0, stop = Size;
            if (args.Length > 1)
                start = SliceIndexNotNone(args[1]);
            if (args.Length > 2)
                stop = SliceIndexNotNone(args[2]);
            var b = _leftblock;
            int index = _leftindex;
            ulong startState = State;
            if (start < 0)
            {
                start += Size;
                if (start < 0)
                    start = 0;
            }
            if (stop < 0)
            {
                stop += Size;
                if (stop < 0)
                    stop = 0;
            }
            if (stop > Size)
                stop = Size;
            if (start > stop)
                start = stop;
            long i;
            for (i = 0; i < start - BLOCKLEN; i += BLOCKLEN)
                b = b.RightLink;
            for (; i < start; i++)
            {
                index++;
                if (index == BLOCKLEN)
                {
                    b = b.RightLink;
                    index = 0;
                }
            }
            long n = stop - i;
            while (--n >= 0)
            {
                if (Equal(b.Data[index], v))
                    return stop - n - 1;
                if (startState != State)
                    throw Mutated();
                index++;
                if (index == BLOCKLEN)
                {
                    b = b.RightLink;
                    index = 0;
                }
            }
            throw PyValueError.Create($"{v.ToRepr().Value} is not in deque");
        }

        // deque_insert: "nO:insert", done with rotations.
        internal void Insert(PyObject[] args)
        {
            PyArgs.ParseTuple("insert", args, 2, 2);
            long index = PyArgs.Ssize(args[0]);
            var value = args[1];
            long n = Size;
            if (MaxLen == Size)
                throw PyIndexError.Create("deque already at its maximum size");
            if (index >= n)
            {
                AppendInternal(value, MaxLen);
                return;
            }
            if (index <= -n || index == 0)
            {
                AppendLeftInternal(value);
                return;
            }
            Rotate(-index);
            if (index < 0)
                AppendInternal(value, MaxLen);
            else
                AppendLeftInternal(value);
            Rotate(index);
        }

        private static bool ValidIndex(long i, long limit) => (ulong)i < (ulong)limit;

        // The block and slot of item i.
        private (Block Block, int Slot) Locate(long index, bool fromLeft)
        {
            long i = index + _leftindex;
            long n = i / BLOCKLEN;
            int slot = (int)(i % BLOCKLEN);
            Block b;
            if (fromLeft)
            {
                b = _leftblock;
                while (--n >= 0)
                    b = b.RightLink;
            }
            else
            {
                n = (_leftindex + Size - 1) / BLOCKLEN - n;
                b = _rightblock;
                while (--n >= 0)
                    b = b.LeftLink;
            }
            return (b, slot);
        }

        // deque_item
        internal PyObject Item(long i)
        {
            if (!ValidIndex(i, Size))
                throw PyIndexError.Create("deque index out of range");
            if (i == 0)
                return _leftblock.Data[_leftindex];
            if (i == Size - 1)
                return _rightblock.Data[_rightindex];
            var (b, slot) = Locate(i, i < (Size >> 1));
            return b.Data[slot];
        }

        // deque_del_item
        private void DelItemAt(long i)
        {
            Rotate(-i);
            PopLeft();
            Rotate(i);
        }

        // deque_remove: a mutation during the search is an IndexError here.
        internal void Remove(PyObject value)
        {
            var b = _leftblock;
            long i, n = Size;
            int index = _leftindex;
            ulong startState = State;
            for (i = 0; i < n; i++)
            {
                bool cmp = Equal(b.Data[index], value);
                if (startState != State)
                    throw PyIndexError.Create("deque mutated during iteration");
                if (cmp)
                    break;
                index++;
                if (index == BLOCKLEN)
                {
                    b = b.RightLink;
                    index = 0;
                }
            }
            if (i == n)
                throw PyValueError.Create($"{value.ToRepr().Value} is not in deque");
            DelItemAt(i);
        }

        // deque_ass_item
        internal void AssItem(long i, PyObject v)
        {
            long len = Size, halflen = (len + 1) >> 1;
            if (!ValidIndex(i, len))
                throw PyIndexError.Create("deque index out of range");
            if (v == null)
            {
                DelItemAt(i);
                return;
            }
            var (b, slot) = Locate(i, i <= halflen);
            b.Data[slot] = v;
        }

        // PyObject_GetItem and friends reach sq_item through PyNumber_AsSsize_t(key, IndexError),
        // with a negative index counted from the end.
        private long SequenceIndex(PyObject key)
        {
            var index = PyObject.TryGetIndex(key)
                ?? throw PyTypeError.Create($"sequence index must be integer, not '{key.GetTypeName()}'");
            if (index.Value < long.MinValue || index.Value > long.MaxValue)
                throw PyIndexError.Create($"cannot fit '{key.GetTypeName()}' into an index-sized integer");
            long i = (long)index.Value;
            return i < 0 ? i + Size : i;
        }

        protected internal override PyObject MpSubscript(PyObject index) => Item(SequenceIndex(index));
        protected internal override void MpAssSubscript(PyObject index, PyObject value) => AssItem(SequenceIndex(index), value);
        protected internal override void MpDelSubscript(PyObject index) => AssItem(SequenceIndex(index), null);

        // deque_reduce: (type, () or ((), maxlen), state, iter(deque))
        internal PyObject Reduce()
        {
            var state = GetAttribute("__getstate__").Call(Array.Empty<PyObject>(), null);
            var it = GetIterator();
            var args = MaxLen < 0
                ? PyTuple.Empty
                : new PyTuple(new PyObject[] { PyTuple.Empty, new PyInt(MaxLen) });
            return new PyTuple(new PyObject[] { _type, args, state, it });
        }

        // deque_init: "|OO:deque" with iterable and maxlen; PyLong_AsSsize_t takes only an int.
        internal void Init(PyObject[] args, PyDict kwargs)
        {
            PyObject iterable = null, maxlenobj = null;
            if ((kwargs == null || kwargs.Length() == 0) && args.Length <= 2)
            {
                if (args.Length > 0) iterable = args[0];
                if (args.Length > 1) maxlenobj = args[1];
            }
            else
            {
                var parsed = PyArgs.Unpack("deque", args, kwargs, new[] { "iterable", "maxlen" }, 0, 0, 2);
                iterable = parsed[0];
                maxlenobj = parsed[1];
            }
            long maxlen = -1;
            if (maxlenobj != null && maxlenobj is not PyNone)
            {
                if (maxlenobj is not (PyInt or PyBool or PyIntSubclass))
                    throw PyTypeError.Create("an integer is required");
                var value = PyObject.TryGetIndex(maxlenobj).Value;
                if (value < long.MinValue || value > long.MaxValue)
                    throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
                maxlen = (long)value;
                if (maxlen < 0)
                    throw PyValueError.Create("maxlen must be non-negative");
            }
            MaxLen = maxlen;
            if (Size > 0)
                Clear();
            if (iterable != null)
                Extend(iterable);
        }

        // deque_sizeof
        internal long SizeOf()
        {
            long blocks = (_leftindex + Size + BLOCKLEN - 1) / BLOCKLEN;
            return PyDequeModule.BasicSize + blocks * PyDequeModule.BlockSize;
        }

        protected internal override long Len() => (int)Size;
        protected internal override bool Bool() => Size > 0;

        // PyObject_GetIter: a subclass's own __iter__, otherwise deque_iter.
        protected internal override PyObject Iter()
        {
            return new DequeIterator(this);
        }

        // deque_repr
        protected internal override PyString Repr()
        {
            if (!PyReprGuard.Enter(this))
                return new PyString("[...]");
            try
            {
                var aslist = AsList();
                string name = _type.Name;
                return new PyString(MaxLen >= 0
                    ? $"{name}({aslist.ToRepr().Value}, maxlen={MaxLen})"
                    : $"{name}({aslist.ToRepr().Value})");
            }
            finally
            {
                PyReprGuard.Leave(this);
            }
        }

        protected internal override PyString Str() => ToRepr();

        public override PyObject Add(PyObject other) => Concat(other);

        // sq_repeat, reached through PyNumber_Multiply's sequence_repeat.
        public override PyObject Multiply(PyObject other)
        {
            if (PyObject.TryGetIndex(other) == null)
                return PyNotImplemented.Instance;
            return Repeat(PyArgs.IndexSsize(other));
        }

        // PyIter_Next: an item, or false at the end.
        private static bool IterNext(PyObject iterator, out PyObject item)
        {
            try
            {
                item = iterator.Next();
                return true;
            }
            catch (PythonException e) when (e.PyException is PyStopIteration)
            {
                item = null;
                return false;
            }
        }

        // deque_richcompare: the first pair that differs decides; else the lengths do.
        protected internal override PyObject TpRichCompare(PyObject other, PyObject.CompareOp op)
        {
            if (other is not PyDeque w)
                return PyNotImplemented.Instance;
            long vs = Size, ws = w.Size;
            if (op == PyObject.CompareOp.EQ)
            {
                if (ReferenceEquals(this, w)) return PyBool.True;
                if (vs != ws) return PyBool.False;
            }
            if (op == PyObject.CompareOp.NE)
            {
                if (ReferenceEquals(this, w)) return PyBool.False;
                if (vs != ws) return PyBool.True;
            }
            var it1 = GetIterator();
            var it2 = w.GetIterator();
            PyObject x, y;
            while (true)
            {
                bool hasX = IterNext(it1, out x);
                bool hasY = IterNext(it2, out y);
                if (!hasX || !hasY)
                    break;
                if (!PyObject.CompareBool(x, y, PyObject.CompareOp.EQ))
                    return PyBool.FromBool(PyObject.CompareBool(x, y, op));
            }
            bool cmp = op switch
            {
                PyObject.CompareOp.LT => y != null,
                PyObject.CompareOp.LE => x == null,
                PyObject.CompareOp.EQ => x == null && y == null,
                PyObject.CompareOp.NE => !(x == null && y == null),
                PyObject.CompareOp.GT => x != null,
                _ => y == null,
            };
            return PyBool.FromBool(cmp);
        }

        /// <summary>dequeiterobject: the block and slot reached, the state it was made at and the count left.</summary>
        internal sealed class DequeIterator : PyIterator
        {
            private readonly PyDeque _deque;
            private readonly bool _reverse;
            private Block _b;
            private int _index;
            private readonly ulong _state;
            internal long Counter;

            internal DequeIterator(PyDeque deque, bool reverse = false)
            {
                _deque = deque;
                _reverse = reverse;
                _b = reverse ? deque._rightblock : deque._leftblock;
                _index = reverse ? deque._rightindex : deque._leftindex;
                _state = deque.State;
                Counter = deque.Size;
            }

            protected override string IteratorName => _reverse ? "_deque_reverse_iterator" : "_deque_iterator";
            public override PyType GetPyType() => _reverse ? PyDequeModule.DequeRevIterType : PyDequeModule.DequeIterType;
            public override string GetTypeName() => _reverse ? "collections._deque_reverse_iterator" : "collections._deque_iterator";

            // dequeiter_next checks the state first; dequereviter_next the count first.
            internal bool TryNext(out PyObject item)
            {
                item = null;
                if (_reverse && Counter == 0)
                    return false;
                if (_deque.State != _state)
                {
                    Counter = 0;
                    throw PyRuntimeError.Create("deque mutated during iteration");
                }
                if (Counter == 0)
                    return false;
                item = _b.Data[_index];
                Counter--;
                if (_reverse)
                {
                    _index--;
                    if (_index < 0 && Counter > 0)
                    {
                        _b = _b.LeftLink;
                        _index = BLOCKLEN - 1;
                    }
                }
                else
                {
                    _index++;
                    if (_index == BLOCKLEN && Counter > 0)
                    {
                        _b = _b.RightLink;
                        _index = 0;
                    }
                }
                return true;
            }

            protected internal override PyObject IterNext() => TryNext(out var item) ? item : throw PyStopIteration.Create();

            internal override PyObject RemainingHint() => new PyInt(Counter);

            // dequeiter_reduce
            internal override PyObject Reduce() => new PyTuple(new PyObject[]
            {
                GetPyType(), new PyTuple(new PyObject[] { _deque, new PyInt(_deque.Size - Counter) }),
            });
        }
    }
}
