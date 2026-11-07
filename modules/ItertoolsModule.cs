using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/itertoolsmodule.c (CPython v3.12.15): one heap type per iterator, made from its spec
    /// with the module as its home, and the tee function.
    /// </summary>
    internal static class ItertoolsModule
    {
        internal static readonly PyType AccumulateType, BatchedType, ChainType, CombinationsType, CompressType,
            CountType, CwrType, CycleType, DropWhileType, FilterFalseType, GroupByType, GrouperType, ISliceType,
            PairwiseType, PermutationsType, ProductType, RepeatType, StarmapType, TakeWhileType, TeeType,
            TeeDataObjectType, ZipLongestType;

        private static readonly PyType[] AllTypes;

        internal static PyModule Create()
        {
            var module = new PyModule("itertools");
            var d = module.ModuleDict;
            if (PyDocs.Lookup("itertools.__doc__") is { } doc)
                d["__doc__"] = PyString.From(doc);
            // itertoolsmodule_exec: PyModule_AddType files each type under the name after the dot.
            foreach (var type in AllTypes)
                d[type.Name] = type;
            // ITERTOOLS_TEE_METHODDEF: METH_FASTCALL, "n" defaulting to 2.
            d["tee"] = new PyBuiltinFunction("tee", (args, kwargs) =>
            {
                PyArgs.NoKeywords("itertools.tee", kwargs);
                PyArgs.Positional("tee", args, null, 1, 2);
                long n = args.Length < 2 ? 2 : PyArgs.Ssize(args[1]);
                return TeeObject.Tee(args[0], n);
            });
            return module;
        }

        // ITERTOOL_PICKLE_DEPRECATION
        internal static void PickleDeprecation() =>
            PyWarnings.Warn(PyType.DeprecationWarningType,
                "Pickle, copy, and deepcopy support will be removed from itertools in Python 3.14.", 1);

        /// <summary>
        /// PyType_FromModuleAndSpec of one spec. Py_tp_iter is PyObject_SelfIter and Py_tp_getattro,
        /// where the spec names it, PyObject_GenericGetAttr; add_operators gives both their wrapper.
        /// </summary>
        private static PyType Spec(string name, Func<PyType, PyObject[], PyDict, PyObject> tpNew,
            bool iterator = true, bool genericGetAttr = true)
        {
            var type = new PyType(name, new[] { PyType.ObjectType }, "itertools");
            type.TypeDict["__module__"] = PyString.From("itertools");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => tpNew((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            if (genericGetAttr)
                TypeTables.GenericAttributes(type);
            if (iterator)
            {
                type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) => self);
                type.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", type, (self, args, kwargs) => self.IterNext());
            }
            if (PyDocs.Lookup("itertools." + name) is { } doc)
                type.TypeDict["__doc__"] = PyDocs.DocFromInternal(name, doc);
            return type;
        }

        private static void Method<T>(PyType type, string name, PyCall kind, Func<T, PyObject[], PyObject> body) where T : PyObject =>
            PyArgs.Define(type, name, kind, (self, args) => body((T)self, args));

        // The __reduce__ and __setstate__ every pickling type has, behind the deprecation warning.
        private static void Pickled<T>(PyType type, Func<T, PyObject> reduce, Action<T, PyObject> setState = null) where T : PyObject
        {
            Method<T>(type, "__reduce__", PyCall.NoArgs, (self, args) =>
            {
                PickleDeprecation();
                return reduce(self);
            });
            if (setState != null)
                Method<T>(type, "__setstate__", PyCall.One, (self, args) =>
                {
                    PickleDeprecation();
                    setState(self, args[0]);
                    return PyNone.Instance;
                });
        }

        // The clinic's "(type == base_tp || type->tp_init == base_tp->tp_init) && !_PyArg_NoKeywords(name, kwargs)".
        internal static void NoKeywords(PyType type, PyType baseType, string name, PyDict kwargs)
        {
            if ((type == baseType || PyType.TypeInitIsObjectInit(type)) && kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{name}() takes no keyword arguments");
        }

        // _PyArg_BadArgument
        internal static Exception BadArgument(string fname, string displayname, string expected, PyObject arg) =>
            PyTypeError.Create($"{fname}() {displayname} must be {expected}, not {arg.GetPyType().Name}");

        // PyIter_Next: the next item, or null once the iterator is done.
        internal static PyObject IterNext(PyObject it)
        {
            try
            {
                return it.Next();
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                return null;
            }
        }

        internal static Exception Stop() => PyStopIteration.Create();

        // PySequence_Tuple
        internal static PyObject[] SequenceTuple(PyObject value) =>
            value is PyTuple tuple ? tuple.Items : value.AsTuple().Items;

        // PyLong_Check and the value.
        internal static bool LongValue(PyObject value, out BigInteger number)
        {
            var integer = PyInt.LongValue(value);
            number = integer?.Value ?? 0;
            return integer != null;
        }

        // PyLong_AsSsize_t: an int and nothing else.
        internal static long AsSsize(PyObject value)
        {
            if (!LongValue(value, out var number))
                throw PyTypeError.Create("an integer is required");
            if (number < long.MinValue || number > long.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
            return (long)number;
        }

        // PyNumber_AsSsize_t(value, PyExc_OverflowError) whose error the caller clears.
        internal static bool TryNumberSsize(PyObject value, out long result)
        {
            try
            {
                result = PyArgs.IndexSsize(value);
                return true;
            }
            catch (PythonException)
            {
                result = -1;
                return false;
            }
        }

        // PyMem_New of n Py_ssize_t: NULL past what can be allocated, and PyErr_NoMemory.
        internal static long[] Indices(long n)
        {
            if (n > Array.MaxLength)
                throw PyMemoryError.Create();
            return new long[n];
        }

        // "state is not a tuple", then PyArg_ParseTuple without a function name.
        internal static PyObject[] StateTuple(PyObject state, int min, int max)
        {
            if (state is not PyTuple tuple)
                throw PyTypeError.Create("state is not a tuple");
            var items = tuple.Items;
            if (items.Length < min || items.Length > max)
            {
                int count = items.Length < min ? min : max;
                string how = min == max ? "exactly" : items.Length < min ? "at least" : "at most";
                throw PyTypeError.Create($"function takes {how} {count} argument{(count == 1 ? "" : "s")} ({items.Length} given)");
            }
            return items;
        }

        // The "O!" converter of PyArg_ParseTuple.
        internal static T StateItem<T>(PyObject[] items, int index, string expected) where T : PyObject =>
            items[index] as T ?? throw PyTypeError.Create($"argument {index + 1} must be {expected}, not {items[index].GetPyType().Name}");

        internal static PyTuple Pack(params PyObject[] items) => PyTuple.From(items);

        static ItertoolsModule()
        {
            BatchedType = Spec("batched", BatchedObject.New);
            PairwiseType = Spec("pairwise", PairwiseObject.New);
            GroupByType = Spec("groupby", GroupByObject.New);
            Pickled<GroupByObject>(GroupByType, g => g.Reduce(), (g, s) => g.SetState(s));
            GrouperType = Spec("_grouper", GrouperObject.New);
            Pickled<GrouperObject>(GrouperType, g => g.Reduce());
            TeeDataObjectType = Spec("_tee_dataobject", TeeDataObject.New, iterator: false);
            Pickled<TeeDataObject>(TeeDataObjectType, t => t.Reduce());
            TeeType = Spec("_tee", TeeObject.New, genericGetAttr: false);
            Method<TeeObject>(TeeType, "__copy__", PyCall.NoArgs, (t, args) => t.Copy());
            Pickled<TeeObject>(TeeType, t => t.Reduce(), (t, s) => t.SetState(s));
            CycleType = Spec("cycle", CycleObject.New);
            Pickled<CycleObject>(CycleType, c => c.Reduce(), (c, s) => c.SetState(s));
            DropWhileType = Spec("dropwhile", DropWhileObject.New);
            Pickled<DropWhileObject>(DropWhileType, o => o.Reduce(), (o, s) => o.SetState(s));
            TakeWhileType = Spec("takewhile", TakeWhileObject.New);
            Pickled<TakeWhileObject>(TakeWhileType, o => o.Reduce(), (o, s) => o.SetState(s));
            ISliceType = Spec("islice", ISliceObject.New);
            Pickled<ISliceObject>(ISliceType, o => o.Reduce(), (o, s) => o.SetState(s));
            StarmapType = Spec("starmap", StarmapObject.New);
            Pickled<StarmapObject>(StarmapType, o => o.Reduce());
            ChainType = Spec("chain", ChainObject.New);
            // ITERTOOLS_CHAIN_FROM_ITERABLE_METHODDEF: METH_O|METH_CLASS.
            ChainType.TypeDict["from_iterable"] = new PyClassMethodDescriptor("from_iterable", ChainType,
                new PyBuiltinFunction("from_iterable", (args, kwargs) =>
                {
                    var rest = args[1..];
                    PyArgs.One("chain.from_iterable", rest, kwargs);
                    return new ChainObject((PyType)args[0], rest[0].GetIterator());
                }));
            Pickled<ChainObject>(ChainType, o => o.Reduce(), (o, s) => o.SetState(s));
            ChainType.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(ChainType);
            ProductType = Spec("product", ProductObject.New);
            Pickled<ProductObject>(ProductType, o => o.Reduce(), (o, s) => o.SetState(s));
            Method<ProductObject>(ProductType, "__sizeof__", PyCall.NoArgs, (o, args) => o.SizeOf());
            CombinationsType = Spec("combinations", CombinationsObject.New);
            Pickled<CombinationsObject>(CombinationsType, o => o.Reduce(), (o, s) => o.SetState(s));
            Method<CombinationsObject>(CombinationsType, "__sizeof__", PyCall.NoArgs, (o, args) => o.SizeOf());
            CwrType = Spec("combinations_with_replacement", CwrObject.New);
            Pickled<CwrObject>(CwrType, o => o.Reduce(), (o, s) => o.SetState(s));
            Method<CwrObject>(CwrType, "__sizeof__", PyCall.NoArgs, (o, args) => o.SizeOf());
            PermutationsType = Spec("permutations", PermutationsObject.New);
            Pickled<PermutationsObject>(PermutationsType, o => o.Reduce(), (o, s) => o.SetState(s));
            Method<PermutationsObject>(PermutationsType, "__sizeof__", PyCall.NoArgs, (o, args) => o.SizeOf());
            AccumulateType = Spec("accumulate", AccumulateObject.New);
            Pickled<AccumulateObject>(AccumulateType, o => o.Reduce(), (o, s) => o.SetState(s));
            CompressType = Spec("compress", CompressObject.New);
            Pickled<CompressObject>(CompressType, o => o.Reduce());
            FilterFalseType = Spec("filterfalse", FilterFalseObject.New);
            Pickled<FilterFalseObject>(FilterFalseType, o => o.Reduce());
            CountType = Spec("count", CountObject.New);
            TypeTables.Repr(CountType);
            Pickled<CountObject>(CountType, o => o.Reduce());
            RepeatType = Spec("repeat", RepeatObject.New);
            TypeTables.Repr(RepeatType);
            Method<RepeatObject>(RepeatType, "__length_hint__", PyCall.NoArgs, (o, args) => o.RemainingHint());
            Pickled<RepeatObject>(RepeatType, o => o.Reduce());
            ZipLongestType = Spec("zip_longest", ZipLongestObject.New);
            Pickled<ZipLongestObject>(ZipLongestType, o => o.Reduce(), (o, s) => o.SetState(s));

            // The order of itertoolsmodule_exec.
            AllTypes = new[]
            {
                AccumulateType, BatchedType, ChainType, CombinationsType, CompressType, CountType, CwrType, CycleType,
                DropWhileType, FilterFalseType, GroupByType, GrouperType, ISliceType, PairwiseType, PermutationsType,
                ProductType, RepeatType, StarmapType, TakeWhileType, TeeType, TeeDataObjectType, ZipLongestType,
            };
        }
    }

    /// <summary>
    /// What every itertools object shares: tp_alloc of the type it was made for, which a Python
    /// subclass may be, and tp_name, which is the spec's dotted name.
    /// </summary>
    internal abstract class ItertoolsObject : PyIterator
    {
        private readonly PyType _base;

        protected ItertoolsObject(PyType type, PyType baseType)
        {
            _base = baseType;
            if (type != null && type != baseType)
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? _base;
        public override string GetTypeName() => _customType is PyClass heap ? heap.Name : _base.FullName;
        protected override string IteratorName => GetTypeName();

        // _PyType_Name(Py_TYPE(self)): tp_name after its last dot.
        protected string ShortTypeName => GetPyType().Name;

        // _PyObject_SIZE(Py_TYPE(self)): a Python subclass keeps its __dict__ and __weakref__ managed,
        // outside the basicsize.
        protected static long ObjectSize(long basicsize) => basicsize;
    }

    /// <summary>batchedobject</summary>
    internal sealed class BatchedObject : ItertoolsObject
    {
        private PyObject _it;
        private readonly long _batchSize;

        private BatchedObject(PyType type, PyObject it, long n) : base(type, ItertoolsModule.BatchedType)
        {
            _it = it;
            _batchSize = n;
        }

        // batched_new: "iterable", "n" by position or name.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            var fast = PyArgs.Unpack("batched", args, kwargs, new[] { "iterable", "n" }, 0, 2, 2);
            long n = PyArgs.Ssize(fast[1]);
            if (n < 1)
                throw PyValueError.Create("n must be at least one");
            return new BatchedObject(type, fast[0].GetIterator(), n);
        }

        // batched_next
        protected internal override PyObject IterNext()
        {
            var it = _it;
            if (it == null)
                throw ItertoolsModule.Stop();
            var items = new List<PyObject>();
            for (long i = 0; i < _batchSize; i++)
            {
                PyObject item;
                try
                {
                    item = it.Next();
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    break;
                }
                catch (PythonException)
                {
                    // Input raised an exception other than StopIteration.
                    _it = null;
                    throw;
                }
                items.Add(item);
            }
            if (items.Count == 0)
            {
                _it = null;
                throw ItertoolsModule.Stop();
            }
            return PyTuple.From(items.ToArray());
        }
    }

    /// <summary>pairwiseobject</summary>
    internal sealed class PairwiseObject : ItertoolsObject
    {
        private PyObject _it;
        private PyObject _old;

        private PairwiseObject(PyType type, PyObject it) : base(type, ItertoolsModule.PairwiseType) => _it = it;

        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.PairwiseType, "pairwise", kwargs);
            PyArgs.Positional("pairwise", args, null, 1, 1);
            return new PairwiseObject(type, args[0].GetIterator());
        }

        // pairwise_next: the fields are read again after each call, which may have re-entered.
        protected internal override PyObject IterNext()
        {
            var it = _it;
            var old = _old;
            if (it == null)
                throw ItertoolsModule.Stop();
            if (old == null)
            {
                try
                {
                    old = it.Next();
                }
                catch (PythonException)
                {
                    _old = null;
                    _it = null;
                    throw;
                }
                _old = old;
                it = _it;
                if (it == null)
                {
                    _old = null;
                    throw ItertoolsModule.Stop();
                }
            }
            PyObject @new;
            try
            {
                @new = it.Next();
            }
            catch (PythonException)
            {
                _it = null;
                _old = null;
                throw;
            }
            var result = ItertoolsModule.Pack(old, @new);
            _old = @new;
            return result;
        }
    }

    /// <summary>groupbyobject</summary>
    internal sealed class GroupByObject : ItertoolsObject
    {
        internal readonly PyObject It;
        private readonly PyObject _keyfunc;
        private PyObject _tgtkey;
        internal PyObject CurrKey;
        internal PyObject CurrValue;
        internal GrouperObject CurrGrouper;

        private GroupByObject(PyType type, PyObject it, PyObject keyfunc) : base(type, ItertoolsModule.GroupByType)
        {
            It = it;
            _keyfunc = keyfunc;
        }

        // itertools_groupby: "iterable", "key"=None.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            var fast = PyArgs.Unpack("groupby", args, kwargs, new[] { "iterable", "key" }, 0, 1, 2);
            var keyfunc = fast[1] ?? PyNone.Instance;
            return new GroupByObject(type, fast[0].GetIterator(), keyfunc);
        }

        // groupby_step: false when the input is done.
        internal bool Step()
        {
            var newvalue = ItertoolsModule.IterNext(It);
            if (newvalue == null)
                return false;
            var newkey = _keyfunc is PyNone ? newvalue : _keyfunc.Call(new[] { newvalue }, null);
            CurrValue = newvalue;
            CurrKey = newkey;
            return true;
        }

        // groupby_next
        protected internal override PyObject IterNext()
        {
            CurrGrouper = null;
            // skip to next iteration group
            while (true)
            {
                if (CurrKey == null)
                {
                }
                else if (_tgtkey == null)
                    break;
                else if (!PyObject.CompareBool(_tgtkey, CurrKey, PyObject.CompareOp.EQ))
                    break;
                if (!Step())
                    throw ItertoolsModule.Stop();
            }
            _tgtkey = CurrKey;
            var grouper = new GrouperObject(this, _tgtkey);
            return ItertoolsModule.Pack(CurrKey, grouper);
        }

        // groupby_reduce: a 'new' call, with a 'setstate' once the groupby has started.
        internal PyObject Reduce()
        {
            var args = ItertoolsModule.Pack(It, _keyfunc);
            if (_tgtkey != null && CurrKey != null && CurrValue != null)
                return ItertoolsModule.Pack(GetPyType(), args, ItertoolsModule.Pack(CurrKey, CurrValue, _tgtkey));
            return ItertoolsModule.Pack(GetPyType(), args);
        }

        // groupby_setstate: "OOO"
        internal void SetState(PyObject state)
        {
            var items = ItertoolsModule.StateTuple(state, 3, 3);
            CurrKey = items[0];
            CurrValue = items[1];
            _tgtkey = items[2];
        }
    }

    /// <summary>_grouperobject</summary>
    internal sealed class GrouperObject : ItertoolsObject
    {
        private readonly GroupByObject _parent;
        private readonly PyObject _tgtkey;

        // _grouper_create: always the module's _grouper type; the parent's grouper is now this one.
        internal GrouperObject(GroupByObject parent, PyObject tgtkey) : base(null, ItertoolsModule.GrouperType)
        {
            _parent = parent;
            _tgtkey = tgtkey;
            parent.CurrGrouper = this;
        }

        // itertools__grouper: "parent" a groupby, "tgtkey", both by position.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.GrouperType, "_grouper", kwargs);
            PyArgs.Positional("_grouper", args, null, 2, 2);
            if (args[0] is not GroupByObject parent)
                throw ItertoolsModule.BadArgument("_grouper", "argument 1", ItertoolsModule.GroupByType.FullName, args[0]);
            return new GrouperObject(parent, args[1]);
        }

        // _grouper_next
        protected internal override PyObject IterNext()
        {
            var gbo = _parent;
            if (gbo.CurrGrouper != this)
                throw ItertoolsModule.Stop();
            if (gbo.CurrValue == null && !gbo.Step())
                throw ItertoolsModule.Stop();
            // got any error or current group is end
            if (!PyObject.CompareBool(_tgtkey, gbo.CurrKey, PyObject.CompareOp.EQ))
                throw ItertoolsModule.Stop();
            var r = gbo.CurrValue;
            gbo.CurrValue = null;
            gbo.CurrKey = null;
            return r;
        }

        // _grouper_reduce
        internal PyObject Reduce()
        {
            if (_parent.CurrGrouper != this)
                return ItertoolsModule.Pack(Builtin("iter"), ItertoolsModule.Pack(PyTuple.Empty));
            return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_parent, _tgtkey));
        }
    }

    /// <summary>
    /// teedataobject: LINKCELLS values read from the shared iterator, and the link to the next
    /// block once this one is full.
    /// </summary>
    internal sealed class TeeDataObject : PyObject
    {
        internal const int LINKCELLS = 57;

        internal readonly PyObject It;
        internal int NumRead;
        private bool _running;
        internal TeeDataObject NextLink;
        internal readonly PyObject[] Values = new PyObject[LINKCELLS];

        // teedataobject_newinternal
        internal TeeDataObject(PyObject it) => It = it;

        public override PyType GetPyType() => ItertoolsModule.TeeDataObjectType;
        public override string GetTypeName() => ItertoolsModule.TeeDataObjectType.FullName;

        // teedataobject_jumplink
        internal TeeDataObject JumpLink() => NextLink ??= new TeeDataObject(It);

        // teedataobject_getitem: null once the iterator is done.
        internal PyObject GetItem(int i)
        {
            if (i < NumRead)
                return Values[i];
            // this is the lead iterator, so fetch more data
            if (_running)
                throw PyRuntimeError.Create("cannot re-enter the tee iterator");
            _running = true;
            PyObject value;
            try
            {
                value = ItertoolsModule.IterNext(It);
            }
            finally
            {
                _running = false;
            }
            if (value == null)
                return null;
            NumRead++;
            Values[i] = value;
            return value;
        }

        // teedataobject_reduce
        internal PyObject Reduce()
        {
            var values = new PyList();
            for (int i = 0; i < NumRead; i++)
                values.Append(Values[i]);
            return ItertoolsModule.Pack(GetPyType(),
                ItertoolsModule.Pack(It, values, (PyObject)NextLink ?? PyNone.Instance));
        }

        // itertools_teedataobject: "iterable", "values" a list, "next", by position.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.TeeDataObjectType, "teedataobject", kwargs);
            PyArgs.Positional("teedataobject", args, null, 3, 3);
            if (args[1] is not PyList values)
                throw ItertoolsModule.BadArgument("teedataobject", "argument 2", "list", args[1]);
            var next = args[2];
            var tdo = new TeeDataObject(args[0]);
            int len = values.RawCount();
            if (len > LINKCELLS)
                throw PyValueError.Create("Invalid arguments");
            for (int i = 0; i < len; i++)
                tdo.Values[i] = values.RawGet(i);
            tdo.NumRead = len;
            if (len == LINKCELLS)
            {
                if (next is not PyNone)
                {
                    if (next is not TeeDataObject link)
                        throw PyValueError.Create("Invalid arguments");
                    tdo.NextLink = link;
                }
            }
            else if (next is not PyNone)
                // shouldn't have a next if we are not full
                throw PyValueError.Create("Invalid arguments");
            return tdo;
        }
    }

    /// <summary>teeobject</summary>
    internal sealed class TeeObject : ItertoolsObject
    {
        private TeeDataObject _dataobj;
        private int _index;

        private TeeObject(TeeDataObject dataobj, int index) : base(null, ItertoolsModule.TeeType)
        {
            _dataobj = dataobj;
            _index = index;
        }

        // tee_next
        protected internal override PyObject IterNext()
        {
            if (_index >= TeeDataObject.LINKCELLS)
            {
                _dataobj = _dataobj.JumpLink();
                _index = 0;
            }
            var value = _dataobj.GetItem(_index) ?? throw ItertoolsModule.Stop();
            _index++;
            return value;
        }

        // tee_copy
        internal TeeObject Copy() => new TeeObject(_dataobj, _index);

        // tee_fromiterable
        internal static TeeObject FromIterable(PyObject iterable)
        {
            var it = iterable.GetIterator();
            if (it is TeeObject tee)
                return tee.Copy();
            return new TeeObject(new TeeDataObject(it), 0);
        }

        // itertools__tee: "iterable" by position.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.TeeType, "_tee", kwargs);
            PyArgs.Positional("_tee", args, null, 1, 1);
            return FromIterable(args[0]);
        }

        // tee_reduce
        internal PyObject Reduce() => ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(PyTuple.Empty),
            ItertoolsModule.Pack(_dataobj, PyInt.From(_index)));

        // tee_setstate: "O!i" with the teedataobject type.
        internal void SetState(PyObject state)
        {
            var items = ItertoolsModule.StateTuple(state, 2, 2);
            var tdo = ItertoolsModule.StateItem<TeeDataObject>(items, 0, ItertoolsModule.TeeDataObjectType.FullName);
            int index = PyArgs.ParseInt(items[1]);
            if (index < 0 || index > TeeDataObject.LINKCELLS)
                throw PyValueError.Create("Index out of range");
            _dataobj = tdo;
            _index = index;
        }

        // itertools_tee_impl
        internal static PyObject Tee(PyObject iterable, long n)
        {
            if (n < 0)
                throw PyValueError.Create("n must be >= 0");
            if (n > Array.MaxLength)
                throw PyMemoryError.Create();
            var result = new PyObject[n];
            if (n == 0)
                return PyTuple.From(result);
            var it = iterable.GetIterator();
            var to = FromIterable(it);
            result[0] = to;
            for (long i = 1; i < n; i++)
            {
                to = to.Copy();
                result[i] = to;
            }
            return PyTuple.From(result);
        }
    }

    /// <summary>cycleobject</summary>
    internal sealed class CycleObject : ItertoolsObject
    {
        private PyObject _it;
        private PyList _saved;
        private long _index;
        private bool _firstpass;

        private CycleObject(PyType type, PyObject it) : base(type, ItertoolsModule.CycleType)
        {
            _it = it;
            _saved = new PyList();
        }

        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.CycleType, "cycle", kwargs);
            PyArgs.Positional("cycle", args, null, 1, 1);
            return new CycleObject(type, args[0].GetIterator());
        }

        // cycle_next
        protected internal override PyObject IterNext()
        {
            if (_it != null)
            {
                var item = ItertoolsModule.IterNext(_it);
                if (item != null)
                {
                    if (!_firstpass)
                        _saved.Append(item);
                    return item;
                }
                _it = null;
            }
            int size = _saved.RawCount();
            if (size == 0)
                throw ItertoolsModule.Stop();
            var value = _saved.RawGet((int)_index);
            _index++;
            if (_index >= _saved.RawCount())
                _index = 0;
            return value;
        }

        // cycle_reduce: a new cycle over the iterator, then the saved state.
        internal PyObject Reduce()
        {
            if (_it == null)
            {
                var it = _saved.GetIterator();
                if (_index != 0)
                    it.GetAttribute("__setstate__").Call(new PyObject[] { PyInt.From(_index) }, null);
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(it), ItertoolsModule.Pack(_saved, PyBool.True));
            }
            return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_it),
                ItertoolsModule.Pack(_saved, PyBool.FromBool(_firstpass)));
        }

        // cycle_setstate: "O!i"; the second item is 1/0 in old pickles and True/False in new ones.
        internal void SetState(PyObject state)
        {
            var items = ItertoolsModule.StateTuple(state, 2, 2);
            var saved = ItertoolsModule.StateItem<PyList>(items, 0, "list");
            int firstpass = PyArgs.ParseInt(items[1]);
            _saved = saved;
            _firstpass = firstpass != 0;
            _index = 0;
        }
    }

    /// <summary>dropwhileobject</summary>
    internal sealed class DropWhileObject : ItertoolsObject
    {
        private readonly PyObject _func;
        private readonly PyObject _it;
        private long _start;

        private DropWhileObject(PyType type, PyObject func, PyObject it) : base(type, ItertoolsModule.DropWhileType)
        {
            _func = func;
            _it = it;
        }

        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.DropWhileType, "dropwhile", kwargs);
            PyArgs.Positional("dropwhile", args, null, 2, 2);
            var it = args[1].GetIterator();
            return new DropWhileObject(type, args[0], it);
        }

        // dropwhile_next
        protected internal override PyObject IterNext()
        {
            while (true)
            {
                var item = _it.Next();
                if (_start == 1)
                    return item;
                var good = _func.Call(new[] { item }, null);
                if (!good.IsTrue())
                {
                    _start = 1;
                    return item;
                }
            }
        }

        internal PyObject Reduce() => ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_func, _it), PyInt.From(_start));

        internal void SetState(PyObject state) => _start = state.IsTrue() ? 1 : 0;
    }

    /// <summary>takewhileobject</summary>
    internal sealed class TakeWhileObject : ItertoolsObject
    {
        private readonly PyObject _func;
        private readonly PyObject _it;
        private long _stop;

        private TakeWhileObject(PyType type, PyObject func, PyObject it) : base(type, ItertoolsModule.TakeWhileType)
        {
            _func = func;
            _it = it;
        }

        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.TakeWhileType, "takewhile", kwargs);
            PyArgs.Positional("takewhile", args, null, 2, 2);
            var it = args[1].GetIterator();
            return new TakeWhileObject(type, args[0], it);
        }

        // takewhile_next
        protected internal override PyObject IterNext()
        {
            if (_stop == 1)
                throw ItertoolsModule.Stop();
            var item = _it.Next();
            var good = _func.Call(new[] { item }, null);
            if (good.IsTrue())
                return item;
            _stop = 1;
            throw ItertoolsModule.Stop();
        }

        internal PyObject Reduce() => ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_func, _it), PyInt.From(_stop));

        internal void SetState(PyObject state) => _stop = state.IsTrue() ? 1 : 0;
    }

    /// <summary>isliceobject</summary>
    internal sealed class ISliceObject : ItertoolsObject
    {
        private PyObject _it;
        private long _next;
        private readonly long _stop;
        private readonly long _step;
        private long _cnt;

        private ISliceObject(PyType type, PyObject it, long start, long stop, long step) : base(type, ItertoolsModule.ISliceType)
        {
            _it = it;
            _next = start;
            _stop = stop;
            _step = step;
        }

        private const string StopMessage = "Stop argument for islice() must be None or an integer: 0 <= x <= sys.maxsize.";

        // islice_new
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            long start = 0, stop = -1, step = 1;
            ItertoolsModule.NoKeywords(type, ItertoolsModule.ISliceType, "islice", kwargs);
            PyArgs.Positional("islice", args, null, 2, 4);
            var seq = args[0];
            var a1 = args[1];
            var a2 = args.Length > 2 ? args[2] : null;
            var a3 = args.Length > 3 ? args[3] : null;
            if (args.Length == 2)
            {
                if (a1 is not PyNone && (!ItertoolsModule.TryNumberSsize(a1, out stop) || stop == -1))
                    throw PyValueError.Create(StopMessage);
            }
            else
            {
                if (a1 is not PyNone)
                    ItertoolsModule.TryNumberSsize(a1, out start);
                if (a2 is not PyNone && (!ItertoolsModule.TryNumberSsize(a2, out stop) || stop == -1))
                    throw PyValueError.Create(StopMessage);
            }
            if (start < 0 || stop < -1)
                throw PyValueError.Create("Indices for islice() must be None or an integer: 0 <= x <= sys.maxsize.");
            if (a3 != null && a3 is not PyNone)
                ItertoolsModule.TryNumberSsize(a3, out step);
            if (step < 1)
                throw PyValueError.Create("Step for islice() must be a positive integer or None.");
            var it = seq.GetIterator();
            return new ISliceObject(type, it, start, stop, step);
        }

        // islice_next: whatever ends the input, an error or its end, lets it go.
        protected internal override PyObject IterNext()
        {
            var it = _it;
            long stop = _stop;
            if (it == null)
                throw ItertoolsModule.Stop();
            try
            {
                while (_cnt < _next)
                {
                    it.Next();
                    _cnt++;
                }
                if (stop != -1 && _cnt >= stop)
                {
                    _it = null;
                    throw ItertoolsModule.Stop();
                }
                var item = it.Next();
                _cnt++;
                long oldnext = _next;
                _next = unchecked(_next + _step);
                if (_next < oldnext || (stop != -1 && _next > stop))
                    _next = stop;
                return item;
            }
            catch (PythonException)
            {
                _it = null;
                throw;
            }
        }

        // islice_reduce: a new object with the same bounds, then 'setstate' with the count.
        internal PyObject Reduce()
        {
            if (_it == null)
            {
                var emptyIt = new PyList().GetIterator();
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(emptyIt, PyInt.From(0)), PyInt.From(0));
            }
            PyObject stop = _stop == -1 ? PyNone.Instance : PyInt.From(_stop);
            return ItertoolsModule.Pack(GetPyType(),
                ItertoolsModule.Pack(_it, PyInt.From(_next), stop, PyInt.From(_step)), PyInt.From(_cnt));
        }

        internal void SetState(PyObject state) => _cnt = ItertoolsModule.AsSsize(state);
    }

    /// <summary>starmapobject</summary>
    internal sealed class StarmapObject : ItertoolsObject
    {
        private readonly PyObject _func;
        private readonly PyObject _it;

        private StarmapObject(PyType type, PyObject func, PyObject it) : base(type, ItertoolsModule.StarmapType)
        {
            _func = func;
            _it = it;
        }

        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.StarmapType, "starmap", kwargs);
            PyArgs.Positional("starmap", args, null, 2, 2);
            var it = args[1].GetIterator();
            return new StarmapObject(type, args[0], it);
        }

        // starmap_next
        protected internal override PyObject IterNext()
        {
            var args = _it.Next();
            var items = args is PyTuple tuple && tuple.GetType() == typeof(PyTuple) && !tuple.HasCustomType
                ? tuple.Items
                : ItertoolsModule.SequenceTuple(args);
            return _func.Call(items, null);
        }

        internal PyObject Reduce() => ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_func, _it));
    }

    /// <summary>chainobject: an iterator over the input iterables, and the one running now.</summary>
    internal sealed class ChainObject : ItertoolsObject
    {
        private PyObject _source;
        private PyObject _active;

        // chain_new_internal
        internal ChainObject(PyType type, PyObject source) : base(type, ItertoolsModule.ChainType) => _source = source;

        // chain_new
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.ChainType, "chain", kwargs);
            var source = PyTuple.From(args).GetIterator();
            return new ChainObject(type, source);
        }

        // chain_next
        protected internal override PyObject IterNext()
        {
            while (_source != null)
            {
                if (_active == null)
                {
                    PyObject iterable;
                    try
                    {
                        iterable = ItertoolsModule.IterNext(_source);
                    }
                    catch (PythonException)
                    {
                        _source = null;
                        throw;
                    }
                    if (iterable == null)
                    {
                        _source = null;
                        throw ItertoolsModule.Stop();       // no more input sources
                    }
                    try
                    {
                        _active = iterable.GetIterator();
                    }
                    catch (PythonException)
                    {
                        _source = null;
                        throw;                              // input not iterable
                    }
                }
                try
                {
                    return _active.Next();
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                }
                // the active iterator is consumed, try with the next iterable
                _active = null;
            }
            // Everything had been consumed already.
            throw ItertoolsModule.Stop();
        }

        // chain_reduce
        internal PyObject Reduce()
        {
            if (_source != null)
                return _active != null
                    ? ItertoolsModule.Pack(GetPyType(), PyTuple.Empty, ItertoolsModule.Pack(_source, _active))
                    : ItertoolsModule.Pack(GetPyType(), PyTuple.Empty, ItertoolsModule.Pack(_source));
            return ItertoolsModule.Pack(GetPyType(), PyTuple.Empty);   // exhausted
        }

        // chain_setstate: "O|O", both iterators.
        internal void SetState(PyObject state)
        {
            var items = ItertoolsModule.StateTuple(state, 1, 2);
            var source = items[0];
            var active = items.Length > 1 ? items[1] : null;
            if (!source.IsIterator || (active != null && !active.IsIterator))
                throw PyTypeError.Create("Arguments must be iterators.");
            _source = source;
            _active = active;
        }
    }

    /// <summary>productobject: a tuple of pool tuples and one index per pool.</summary>
    internal sealed class ProductObject : ItertoolsObject
    {
        private const long BasicSize = 48;

        private readonly PyObject[][] _pools;
        private readonly PyObject _poolsTuple;
        private readonly long[] _indices;
        private PyObject[] _result;
        private bool _stopped;

        private ProductObject(PyType type, PyObject[][] pools, PyObject poolsTuple, long[] indices) : base(type, ItertoolsModule.ProductType)
        {
            _pools = pools;
            _poolsTuple = poolsTuple;
            _indices = indices;
        }

        // product_new
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            long repeat = 1;
            if (kwargs != null)
            {
                // PyArg_ParseTupleAndKeywords((), kwds, "|n:product", {"repeat"})
                var fast = PyArgs.Unpack("product", Array.Empty<PyObject>(), kwargs, new[] { "repeat" }, 0, 0, 1);
                if (fast[0] != null)
                    repeat = PyArgs.Ssize(fast[0]);
                if (repeat < 0)
                    throw PyValueError.Create("repeat argument cannot be negative");
            }
            long nargs;
            if (repeat == 0)
                nargs = 0;
            else
            {
                nargs = args.Length;
                if ((ulong)nargs > (ulong)long.MaxValue / sizeof(long) / (ulong)repeat)
                    throw PyOverflowError.Create("repeat argument too large");
            }
            long npools = nargs * repeat;
            var indices = ItertoolsModule.Indices(npools);
            var pools = new PyObject[npools][];
            var poolObjects = new PyObject[npools];
            long i;
            for (i = 0; i < nargs; i++)
            {
                var pool = args[i] is PyTuple exact && exact.GetType() == typeof(PyTuple) && !exact.HasCustomType
                    ? exact
                    : PyTuple.From(ItertoolsModule.SequenceTuple(args[i]));
                poolObjects[i] = pool;
                pools[i] = pool.Items;
            }
            for (; i < npools; i++)
            {
                poolObjects[i] = poolObjects[i - nargs];
                pools[i] = pools[i - nargs];
            }
            return new ProductObject(type, pools, PyTuple.From(poolObjects), indices);
        }

        // product_sizeof
        internal PyObject SizeOf() => PyInt.From(ObjectSize(BasicSize) + (long)_pools.Length * sizeof(long));

        // product_next: the result is a fresh tuple each time, the one CPython reuses when no one
        // else holds it is never visible as the same object here.
        protected internal override PyObject IterNext()
        {
            var pools = _pools;
            int npools = pools.Length;
            if (_stopped)
                throw ItertoolsModule.Stop();
            if (_result == null)
            {
                // On the first pass, return an initial tuple filled with the first element from each pool.
                var result = new PyObject[npools];
                _result = result;
                for (int i = 0; i < npools; i++)
                {
                    if (pools[i].Length == 0)
                        return Empty();
                    result[i] = pools[i][0];
                }
            }
            else
            {
                var result = (PyObject[])_result.Clone();
                _result = result;
                var indices = _indices;
                // Update the pool indices right-to-left.  Only advance to the next pool when the previous one rolls-over.
                int i;
                for (i = npools - 1; i >= 0; i--)
                {
                    var pool = pools[i];
                    indices[i]++;
                    if (indices[i] == pool.Length)
                    {
                        // Roll-over and advance to next pool
                        indices[i] = 0;
                        result[i] = pool[0];
                    }
                    else
                    {
                        // No rollover. Just increment and stop here.
                        result[i] = pool[indices[i]];
                        break;
                    }
                }
                // If i is negative, then the indices have all rolled-over and we're done.
                if (i < 0)
                    return Empty();
            }
            return PyTuple.From((PyObject[])_result.Clone());
        }

        private PyObject Empty()
        {
            _stopped = true;
            throw ItertoolsModule.Stop();
        }

        // product_reduce
        internal PyObject Reduce()
        {
            if (_stopped)
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(PyTuple.Empty));
            if (_result == null)
                return ItertoolsModule.Pack(GetPyType(), _poolsTuple);
            // the indices for setstate, which also say that the iterator has started
            var indices = new PyObject[_pools.Length];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = PyInt.From(_indices[i]);
            return ItertoolsModule.Pack(GetPyType(), _poolsTuple, PyTuple.From(indices));
        }

        // product_setstate
        internal void SetState(PyObject state)
        {
            int n = _pools.Length;
            if (state is not PyTuple tuple || tuple.Items.Length != n)
                throw PyValueError.Create("invalid arguments");
            for (int i = 0; i < n; i++)
            {
                long index = ItertoolsModule.AsSsize(tuple.Items[i]);
                int poolsize = _pools[i].Length;
                if (poolsize == 0)
                {
                    _stopped = true;
                    return;
                }
                // clamp the index
                if (index < 0)
                    index = 0;
                else if (index > poolsize - 1)
                    index = poolsize - 1;
                _indices[i] = index;
            }
            var result = new PyObject[n];
            for (int i = 0; i < n; i++)
                result[i] = _pools[i][_indices[i]];
            _result = result;
        }
    }

    /// <summary>combinationsobject</summary>
    internal sealed class CombinationsObject : ItertoolsObject
    {
        private const long BasicSize = 56;

        private readonly PyTuple _pool;
        private readonly long[] _indices;
        private PyObject[] _result;
        private readonly long _r;
        private bool _stopped;

        private CombinationsObject(PyType type, PyTuple pool, long[] indices, long r) : base(type, ItertoolsModule.CombinationsType)
        {
            _pool = pool;
            _indices = indices;
            _r = r;
            _stopped = r > pool.Items.Length;
        }

        // itertools_combinations: "iterable", "r" by position or name.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            var fast = PyArgs.Unpack("combinations", args, kwargs, new[] { "iterable", "r" }, 0, 2, 2);
            long r = PyArgs.Ssize(fast[1]);
            var pool = PyTuple.From(ItertoolsModule.SequenceTuple(fast[0]));
            if (r < 0)
                throw PyValueError.Create("r must be non-negative");
            var indices = ItertoolsModule.Indices(r);
            for (long i = 0; i < r; i++)
                indices[i] = i;
            return new CombinationsObject(type, pool, indices, r);
        }

        internal PyObject SizeOf() => PyInt.From(ObjectSize(BasicSize) + _r * sizeof(long));

        // combinations_next
        protected internal override PyObject IterNext()
        {
            var pool = _pool.Items;
            var indices = _indices;
            long n = pool.Length;
            long r = _r;
            if (_stopped)
                throw ItertoolsModule.Stop();
            if (_result == null)
            {
                // On the first pass, initialize result tuple using the indices
                var result = new PyObject[r];
                for (long i = 0; i < r; i++)
                    result[i] = pool[indices[i]];
                _result = result;
            }
            else
            {
                var result = (PyObject[])_result.Clone();
                _result = result;
                // Scan indices right-to-left until finding one that is not at its maximum (i + n - r).
                long i;
                for (i = r - 1; i >= 0 && indices[i] == i + n - r; i--)
                {
                }
                // If i is negative, then the indices are all at their maximum value and we're done.
                if (i < 0)
                {
                    _stopped = true;
                    throw ItertoolsModule.Stop();
                }
                // Increment the current index which we know is not at its maximum. Then move back to
                // the right setting each index to its lowest possible value.
                indices[i]++;
                for (long j = i + 1; j < r; j++)
                    indices[j] = indices[j - 1] + 1;
                // Update the result tuple for the new indices starting with i, the leftmost index that changed
                for (; i < r; i++)
                    result[i] = pool[indices[i]];
            }
            return PyTuple.From((PyObject[])_result.Clone());
        }

        // combinations_reduce
        internal PyObject Reduce()
        {
            if (_result == null)
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_pool, PyInt.From(_r)));
            if (_stopped)
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(PyTuple.Empty, PyInt.From(_r)));
            var indices = new PyObject[_r];
            for (long i = 0; i < _r; i++)
                indices[i] = PyInt.From(_indices[i]);
            return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_pool, PyInt.From(_r)), PyTuple.From(indices));
        }

        // combinations_setstate
        internal void SetState(PyObject state)
        {
            long n = _pool.Items.Length;
            if (state is not PyTuple tuple || tuple.Items.Length != _r)
                throw PyValueError.Create("invalid arguments");
            for (long i = 0; i < _r; i++)
            {
                long index = ItertoolsModule.AsSsize(tuple.Items[i]);
                long max = i + n - _r;
                // clamp the index (beware of negative max)
                if (index > max)
                    index = max;
                if (index < 0)
                    index = 0;
                _indices[i] = index;
            }
            var result = new PyObject[_r];
            for (long i = 0; i < _r; i++)
                result[i] = _pool.Items[_indices[i]];
            _result = result;
        }
    }

    /// <summary>cwrobject</summary>
    internal sealed class CwrObject : ItertoolsObject
    {
        private const long BasicSize = 56;

        private readonly PyTuple _pool;
        private readonly long[] _indices;
        private PyObject[] _result;
        private readonly long _r;
        private bool _stopped;

        private CwrObject(PyType type, PyTuple pool, long[] indices, long r) : base(type, ItertoolsModule.CwrType)
        {
            _pool = pool;
            _indices = indices;
            _r = r;
            _stopped = pool.Items.Length == 0 && r != 0;
        }

        // itertools_combinations_with_replacement: "iterable", "r" by position or name.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            var fast = PyArgs.Unpack("combinations_with_replacement", args, kwargs, new[] { "iterable", "r" }, 0, 2, 2);
            long r = PyArgs.Ssize(fast[1]);
            var pool = PyTuple.From(ItertoolsModule.SequenceTuple(fast[0]));
            if (r < 0)
                throw PyValueError.Create("r must be non-negative");
            var indices = ItertoolsModule.Indices(r);
            return new CwrObject(type, pool, indices, r);
        }

        internal PyObject SizeOf() => PyInt.From(ObjectSize(BasicSize) + _r * sizeof(long));

        // cwr_next
        protected internal override PyObject IterNext()
        {
            var pool = _pool.Items;
            var indices = _indices;
            long n = pool.Length;
            long r = _r;
            if (_stopped)
                throw ItertoolsModule.Stop();
            if (_result == null)
            {
                // On the first pass, initialize result tuple with pool[0]
                var result = new PyObject[r];
                if (n > 0)
                    for (long i = 0; i < r; i++)
                        result[i] = pool[0];
                _result = result;
            }
            else
            {
                var result = (PyObject[])_result.Clone();
                _result = result;
                // Scan indices right-to-left until finding one that is not at its maximum (n-1).
                long i;
                for (i = r - 1; i >= 0 && indices[i] == n - 1; i--)
                {
                }
                // If i is negative, then the indices are all at their maximum value and we're done.
                if (i < 0)
                {
                    _stopped = true;
                    throw ItertoolsModule.Stop();
                }
                // Increment the current index which we know is not at its maximum. Then set all to
                // the right to the same value.
                long index = indices[i] + 1;
                var elem = pool[index];
                for (; i < r; i++)
                {
                    indices[i] = index;
                    result[i] = elem;
                }
            }
            return PyTuple.From((PyObject[])_result.Clone());
        }

        // cwr_reduce
        internal PyObject Reduce()
        {
            if (_result == null)
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_pool, PyInt.From(_r)));
            if (_stopped)
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(PyTuple.Empty, PyInt.From(_r)));
            var indices = new PyObject[_r];
            for (long i = 0; i < _r; i++)
                indices[i] = PyInt.From(_indices[i]);
            return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_pool, PyInt.From(_r)), PyTuple.From(indices));
        }

        // cwr_setstate
        internal void SetState(PyObject state)
        {
            if (state is not PyTuple tuple || tuple.Items.Length != _r)
                throw PyValueError.Create("invalid arguments");
            long n = _pool.Items.Length;
            for (long i = 0; i < _r; i++)
            {
                long index = ItertoolsModule.AsSsize(tuple.Items[i]);
                // clamp the index
                if (index < 0)
                    index = 0;
                else if (index > n - 1)
                    index = n - 1;
                _indices[i] = index;
            }
            var result = new PyObject[_r];
            for (long i = 0; i < _r; i++)
                result[i] = _pool.Items[_indices[i]];
            _result = result;
        }
    }

    /// <summary>permutationsobject</summary>
    internal sealed class PermutationsObject : ItertoolsObject
    {
        private const long BasicSize = 64;

        private readonly PyTuple _pool;
        private readonly long[] _indices;
        private readonly long[] _cycles;
        private PyObject[] _result;
        private readonly long _r;
        private bool _stopped;

        private PermutationsObject(PyType type, PyTuple pool, long[] indices, long[] cycles, long r) : base(type, ItertoolsModule.PermutationsType)
        {
            _pool = pool;
            _indices = indices;
            _cycles = cycles;
            _r = r;
            _stopped = r > pool.Items.Length;
        }

        // itertools_permutations: "iterable", "r"=None by position or name.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            var fast = PyArgs.Unpack("permutations", args, kwargs, new[] { "iterable", "r" }, 0, 1, 2);
            var robj = fast[1] ?? PyNone.Instance;
            var pool = PyTuple.From(ItertoolsModule.SequenceTuple(fast[0]));
            long n = pool.Items.Length;
            long r = n;
            if (robj is not PyNone)
            {
                if (!ItertoolsModule.LongValue(robj, out _))
                    throw PyTypeError.Create("Expected int as r");
                r = ItertoolsModule.AsSsize(robj);
            }
            if (r < 0)
                throw PyValueError.Create("r must be non-negative");
            var indices = ItertoolsModule.Indices(n);
            var cycles = ItertoolsModule.Indices(r);
            for (long i = 0; i < n; i++)
                indices[i] = i;
            for (long i = 0; i < r; i++)
                cycles[i] = n - i;
            return new PermutationsObject(type, pool, indices, cycles, r);
        }

        internal PyObject SizeOf() => PyInt.From(ObjectSize(BasicSize) + (long)_pool.Items.Length * sizeof(long) + _r * sizeof(long));

        // permutations_next
        protected internal override PyObject IterNext()
        {
            var pool = _pool.Items;
            var indices = _indices;
            var cycles = _cycles;
            long n = pool.Length;
            long r = _r;
            if (_stopped)
                throw ItertoolsModule.Stop();
            if (_result == null)
            {
                // On the first pass, initialize result tuple using the indices
                var result = new PyObject[r];
                for (long i = 0; i < r; i++)
                    result[i] = pool[indices[i]];
                _result = result;
            }
            else
            {
                if (n == 0)
                {
                    _stopped = true;
                    throw ItertoolsModule.Stop();
                }
                var result = (PyObject[])_result.Clone();
                _result = result;
                // Decrement rightmost cycle, moving leftward upon zero rollover
                long i;
                for (i = r - 1; i >= 0; i--)
                {
                    cycles[i] -= 1;
                    if (cycles[i] == 0)
                    {
                        // rotatation: indices[i:] = indices[i+1:] + indices[i:i+1]
                        long index = indices[i];
                        for (long j = i; j < n - 1; j++)
                            indices[j] = indices[j + 1];
                        indices[n - 1] = index;
                        cycles[i] = n - i;
                    }
                    else
                    {
                        long j = cycles[i];
                        long index = indices[i];
                        indices[i] = indices[n - j];
                        indices[n - j] = index;
                        // start with i, the leftmost element that changed
                        for (long k = i; k < r; k++)
                            result[k] = pool[indices[k]];
                        break;
                    }
                }
                // If i is negative, then the cycles have all rolled-over and we're done.
                if (i < 0)
                {
                    _stopped = true;
                    throw ItertoolsModule.Stop();
                }
            }
            return PyTuple.From((PyObject[])_result.Clone());
        }

        // permutations_reduce
        internal PyObject Reduce()
        {
            if (_result == null)
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_pool, PyInt.From(_r)));
            if (_stopped)
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(PyTuple.Empty, PyInt.From(_r)));
            // the indices and cycles for setstate
            long n = _pool.Items.Length;
            var indices = new PyObject[n];
            for (long i = 0; i < n; i++)
                indices[i] = PyInt.From(_indices[i]);
            var cycles = new PyObject[_r];
            for (long i = 0; i < _r; i++)
                cycles[i] = PyInt.From(_cycles[i]);
            return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_pool, PyInt.From(_r)),
                ItertoolsModule.Pack(PyTuple.From(indices), PyTuple.From(cycles)));
        }

        // permutations_setstate: "O!O!", two tuples.
        internal void SetState(PyObject state)
        {
            var items = ItertoolsModule.StateTuple(state, 2, 2);
            var indices = ItertoolsModule.StateItem<PyTuple>(items, 0, "tuple");
            var cycles = ItertoolsModule.StateItem<PyTuple>(items, 1, "tuple");
            long n = _pool.Items.Length;
            if (indices.Items.Length != n || cycles.Items.Length != _r)
                throw PyValueError.Create("invalid arguments");
            for (long i = 0; i < n; i++)
            {
                long index = ItertoolsModule.AsSsize(indices.Items[i]);
                // clamp the index
                if (index < 0)
                    index = 0;
                else if (index > n - 1)
                    index = n - 1;
                _indices[i] = index;
            }
            for (long i = 0; i < _r; i++)
            {
                long index = ItertoolsModule.AsSsize(cycles.Items[i]);
                if (index < 1)
                    index = 1;
                else if (index > n - i)
                    index = n - i;
                _cycles[i] = index;
            }
            var result = new PyObject[_r];
            for (long i = 0; i < _r; i++)
                result[i] = _pool.Items[_indices[i]];
            _result = result;
        }
    }

    /// <summary>accumulateobject</summary>
    internal sealed class AccumulateObject : ItertoolsObject
    {
        private PyObject _total;
        private readonly PyObject _it;
        private readonly PyObject _binop;
        private PyObject _initial;

        private AccumulateObject(PyType type, PyObject it, PyObject binop, PyObject initial) : base(type, ItertoolsModule.AccumulateType)
        {
            _it = it;
            _binop = binop;
            _initial = initial;
        }

        // itertools_accumulate: "iterable", "func"=None, and the keyword-only "initial"=None.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            var fast = PyArgs.Unpack("accumulate", args, kwargs, new[] { "iterable", "func", "initial" }, 0, 1, 2);
            var binop = fast[1] ?? PyNone.Instance;
            var initial = fast[2] ?? PyNone.Instance;
            var it = fast[0].GetIterator();
            return new AccumulateObject(type, it, binop is PyNone ? null : binop, initial);
        }

        // accumulate_next
        protected internal override PyObject IterNext()
        {
            if (_initial is not PyNone)
            {
                _total = _initial;
                _initial = PyNone.Instance;
                return _total;
            }
            var val = _it.Next();
            if (_total == null)
            {
                _total = val;
                return _total;
            }
            var newtotal = _binop == null
                ? PyVM.Instance.ExecuteBinaryOpType(_total, val, BinaryOpType.ADD)
                : _binop.Call(new[] { _total, val }, null);
            _total = newtotal;
            return newtotal;
        }

        // accumulate_reduce
        internal PyObject Reduce()
        {
            PyObject binop = _binop ?? PyNone.Instance;
            if (_initial is not PyNone)
            {
                var it = ItertoolsModule.ChainType.Call(new PyObject[] { ItertoolsModule.Pack(_initial), _it }, null);
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(it, binop), PyNone.Instance);
            }
            if (_total is PyNone)
            {
                var it = ItertoolsModule.ChainType.Call(new PyObject[] { ItertoolsModule.Pack(_total), _it }, null);
                it = GetPyType().Call(new[] { it, binop }, null);
                return ItertoolsModule.Pack(ItertoolsModule.ISliceType, ItertoolsModule.Pack(it, PyInt.From(1), PyNone.Instance));
            }
            return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_it, binop), _total ?? PyNone.Instance);
        }

        internal void SetState(PyObject state) => _total = state;
    }

    /// <summary>compressobject</summary>
    internal sealed class CompressObject : ItertoolsObject
    {
        private readonly PyObject _data;
        private readonly PyObject _selectors;

        private CompressObject(PyType type, PyObject data, PyObject selectors) : base(type, ItertoolsModule.CompressType)
        {
            _data = data;
            _selectors = selectors;
        }

        // itertools_compress: "data", "selectors" by position or name.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            var fast = PyArgs.Unpack("compress", args, kwargs, new[] { "data", "selectors" }, 0, 2, 2);
            var data = fast[0].GetIterator();
            var selectors = fast[1].GetIterator();
            return new CompressObject(type, data, selectors);
        }

        // compress_next: datum, then selector, then the selector's truth, as the pure Python version.
        protected internal override PyObject IterNext()
        {
            while (true)
            {
                var datum = _data.Next();
                var selector = _selectors.Next();
                if (selector.IsTrue())
                    return datum;
            }
        }

        internal PyObject Reduce() => ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_data, _selectors));
    }

    /// <summary>filterfalseobject</summary>
    internal sealed class FilterFalseObject : ItertoolsObject
    {
        private readonly PyObject _func;
        private readonly PyObject _it;

        private FilterFalseObject(PyType type, PyObject func, PyObject it) : base(type, ItertoolsModule.FilterFalseType)
        {
            _func = func;
            _it = it;
        }

        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            ItertoolsModule.NoKeywords(type, ItertoolsModule.FilterFalseType, "filterfalse", kwargs);
            PyArgs.Positional("filterfalse", args, null, 2, 2);
            var it = args[1].GetIterator();
            return new FilterFalseObject(type, args[0], it);
        }

        // filterfalse_next
        protected internal override PyObject IterNext()
        {
            while (true)
            {
                var item = _it.Next();
                bool ok = _func is PyNone || ReferenceEquals(_func, PyType.BoolType)
                    ? item.IsTrue()
                    : _func.Call(new[] { item }, null).IsTrue();
                if (!ok)
                    return item;
            }
        }

        internal PyObject Reduce() => ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_func, _it));
    }

    /// <summary>
    /// countobject. fast_mode: cnt counts by one while it is below PY_SSIZE_T_MAX and no other
    /// step was given. slow_mode: cnt is PY_SSIZE_T_MAX and long_cnt advances by long_step.
    /// </summary>
    internal sealed class CountObject : ItertoolsObject
    {
        private long _cnt;
        private PyObject _longCnt;
        private readonly PyObject _longStep;

        private CountObject(PyType type, long cnt, PyObject longCnt, PyObject longStep) : base(type, ItertoolsModule.CountType)
        {
            _cnt = cnt;
            _longCnt = longCnt;
            _longStep = longStep;
        }

        // itertools_count: "start"=0, "step"=1 by position or name.
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            var fast = PyArgs.Unpack("count", args, kwargs, new[] { "start", "step" }, 0, 0, 2);
            var longCnt = fast[0];
            var longStep = fast[1];
            if ((longCnt != null && !PyObject.NumberCheck(longCnt)) || (longStep != null && !PyObject.NumberCheck(longStep)))
                throw PyTypeError.Create("a number is required");
            bool fastMode = (longCnt == null || ItertoolsModule.LongValue(longCnt, out _)) &&
                            (longStep == null || ItertoolsModule.LongValue(longStep, out _));
            long cnt = 0;
            // If not specified, start defaults to 0
            if (longCnt != null)
            {
                if (fastMode)
                {
                    ItertoolsModule.LongValue(longCnt, out var start);
                    if (start < long.MinValue || start > long.MaxValue)
                        fastMode = false;
                    else
                        cnt = (long)start;
                }
            }
            else
                longCnt = PyInt.Zero;
            // If not specified, step defaults to 1
            longStep ??= PyInt.One;
            // Fast mode only works when the step is 1
            if (fastMode)
            {
                ItertoolsModule.LongValue(longStep, out var step);
                if (step != 1)
                    fastMode = false;
            }
            if (fastMode)
                longCnt = null;
            else
                cnt = long.MaxValue;
            return new CountObject(type, cnt, longCnt, longStep);
        }

        // count_next and count_nextlong
        protected internal override PyObject IterNext()
        {
            if (_cnt == long.MaxValue)
            {
                // Switch to slow_mode
                var longCnt = _longCnt ?? PyInt.From(long.MaxValue);
                var steppedUp = PyVM.Instance.ExecuteBinaryOpType(longCnt, _longStep, BinaryOpType.ADD);
                _longCnt = steppedUp;
                return longCnt;
            }
            return PyInt.From(_cnt++);
        }

        // count_repr
        protected internal override PyString Repr() => CountRepr();

        private PyString CountRepr()
        {
            if (_longCnt == null)
                return PyString.From($"{ShortTypeName}({_cnt})");
            // Don't display step when it is an integer equal to 1
            if (ItertoolsModule.LongValue(_longStep, out var step) && step == 1)
                return PyString.From($"{ShortTypeName}({_longCnt.ToRepr().Value})");
            return PyString.From($"{ShortTypeName}({_longCnt.ToRepr().Value}, {_longStep.ToRepr().Value})");
        }

        // count_reduce
        internal PyObject Reduce()
        {
            if (_cnt == long.MaxValue)
                return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_longCnt, _longStep));
            return ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(PyInt.From(_cnt)));
        }
    }

    /// <summary>repeatobject: cnt is -1 for ever.</summary>
    internal sealed class RepeatObject : ItertoolsObject
    {
        private readonly PyObject _element;
        private long _cnt;

        private RepeatObject(PyType type, PyObject element, long cnt) : base(type, ItertoolsModule.RepeatType)
        {
            _element = element;
            _cnt = cnt;
        }

        // repeat_new: "O|n:repeat" over "object", "times".
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            int nArgs = args.Length + (kwargs?.Length() ?? 0);
            var fast = PyArgs.Unpack("repeat", args, kwargs, new[] { "object", "times" }, 0, 1, 2);
            long cnt = fast[1] == null ? -1 : PyArgs.Ssize(fast[1]);
            // Does user supply times argument?
            if (nArgs == 2 && cnt < 0)
                cnt = 0;
            return new RepeatObject(type, fast[0], cnt);
        }

        // repeat_next
        protected internal override PyObject IterNext()
        {
            if (_cnt == 0)
                throw ItertoolsModule.Stop();
            if (_cnt > 0)
                _cnt--;
            return _element;
        }

        // repeat_repr
        protected internal override PyString Repr() => RepeatRepr();

        private PyString RepeatRepr() => _cnt == -1
            ? PyString.From($"{ShortTypeName}({_element.ToRepr().Value})")
            : PyString.From($"{ShortTypeName}({_element.ToRepr().Value}, {_cnt})");

        // repeat_len
        internal override PyObject RemainingHint() => _cnt == -1 ? throw PyTypeError.Create("len() of unsized object") : PyInt.From(_cnt);

        // repeat_reduce: a new repeat with the object and what is left of the count.
        internal PyObject Reduce() => _cnt >= 0
            ? ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_element, PyInt.From(_cnt)))
            : ItertoolsModule.Pack(GetPyType(), ItertoolsModule.Pack(_element));
    }

    /// <summary>ziplongestobject</summary>
    internal sealed class ZipLongestObject : ItertoolsObject
    {
        private readonly PyObject[] _ittuple;
        private long _numactive;
        private PyObject _fillvalue;

        private ZipLongestObject(PyType type, PyObject[] ittuple, PyObject fillvalue) : base(type, ItertoolsModule.ZipLongestType)
        {
            _ittuple = ittuple;
            _numactive = ittuple.Length;
            _fillvalue = fillvalue;
        }

        // zip_longest_new
        internal static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
        {
            PyObject fillvalue = PyNone.Instance;
            if (kwargs != null && kwargs.Length() > 0)
            {
                fillvalue = null;
                var key = PyString.From("fillvalue");
                if (kwargs.Length() == 1 && kwargs.ContainsKey(key))
                    fillvalue = kwargs.GetItem(key);
                if (fillvalue == null)
                    throw PyTypeError.Create("zip_longest() got an unexpected keyword argument");
            }
            var ittuple = new PyObject[args.Length];
            for (int i = 0; i < args.Length; i++)
                ittuple[i] = args[i].GetIterator();
            return new ZipLongestObject(type, ittuple, fillvalue);
        }

        // zip_longest_next: the branch that builds a new tuple, which is the only one seen here.
        protected internal override PyObject IterNext()
        {
            int tuplesize = _ittuple.Length;
            if (tuplesize == 0)
                throw ItertoolsModule.Stop();
            if (_numactive == 0)
                throw ItertoolsModule.Stop();
            var result = new PyObject[tuplesize];
            for (int i = 0; i < tuplesize; i++)
            {
                var it = _ittuple[i];
                PyObject item;
                if (it == null)
                    item = _fillvalue;
                else
                {
                    try
                    {
                        item = ItertoolsModule.IterNext(it);
                    }
                    catch (PythonException)
                    {
                        _numactive = 0;
                        throw;
                    }
                    if (item == null)
                    {
                        _numactive -= 1;
                        if (_numactive == 0)
                            throw ItertoolsModule.Stop();
                        item = _fillvalue;
                        _ittuple[i] = null;
                    }
                }
                result[i] = item;
            }
            return PyTuple.From(result);
        }

        // zip_longest_reduce: empty tuples for the inputs that are done, then setstate with the fillvalue.
        internal PyObject Reduce()
        {
            var args = new PyObject[_ittuple.Length];
            for (int i = 0; i < args.Length; i++)
                args[i] = _ittuple[i] ?? PyTuple.Empty;
            return ItertoolsModule.Pack(GetPyType(), PyTuple.From(args), _fillvalue);
        }

        internal void SetState(PyObject state) => _fillvalue = state;
    }
}
