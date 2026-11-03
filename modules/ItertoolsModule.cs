using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// itertools 모듈 - 고급 이터레이터 도구들
    /// </summary>
    public class ItertoolsModule : PyModule
    {
        public static ItertoolsModule Instance { get; } = new ItertoolsModule();

        private ItertoolsModule() : base("itertools")
        {
            // 주요 itertools 함수들 등록
            AddKeywordFunction("count", CountFunction, "start", "step");
            AddFunction("cycle", CycleFunction);
            AddFunction("repeat", RepeatFunction);
            AddFunction("chain", ChainFunction);
            AddKeywordFunction("combinations", CombinationsFunction, "iterable", "r");
            AddKeywordFunction("permutations", PermutationsFunction, "iterable", "r");
            ModuleDict["product"] = new PyBuiltinFunction("product", ProductFunction);
            AddKeywordFunction("accumulate", AccumulateFunction, "iterable", "func", "initial");
            AddFunction("compress", CompressFunction);
            AddFunction("dropwhile", DropWhileFunction);
            AddFunction("takewhile", TakeWhileFunction);
            AddFunction("filterfalse", FilterFalseFunction);
            AddFunction("islice", ISliceFunction);
            AddKeywordFunction("groupby", GroupByFunction, "iterable", "key");
            ModuleDict["zip_longest"] = new PyBuiltinFunction("zip_longest", ZipLongestFunction);
            AddFunction("starmap", StarmapFunction);
            AddFunction("batched", BatchedFunction);
            AddFunction("combinations_with_replacement", CombinationsWithReplacementFunction);
            AddFunction("pairwise", PairwiseFunction);
            AddFunction("tee", TeeFunction);
            foreach (var name in new[]
            {
                "count", "cycle", "repeat", "chain", "combinations", "permutations", "product", "accumulate", "compress",
                "dropwhile", "takewhile", "filterfalse", "islice", "groupby", "zip_longest", "starmap", "batched",
                "combinations_with_replacement", "pairwise",
            })
                ModuleDict[name] = ClassOf(name, ModuleDict[name]);
            var chain = (PyType)ModuleDict["chain"];
            // chain.from_iterable is a classmethod: the outer iterable is taken one inner iterable at a time.
            chain.TypeDict["from_iterable"] = new PyBuiltinClassMethod("from_iterable", (cls, iterable) => new ChainFromIterable(iterable.GetIterator()));
            chain.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__", (cls, arg) => new PyGenericAlias((PyType)cls, arg));
        }

        // Each of these is a class in C: type(), isinstance() and the repr of what it makes all read that.
        private static PyType ClassOf(string name, PyObject constructor)
        {
            var type = PyType.GetIteratorType("itertools." + name);
            type.ConstructedByNewSlot = true;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => constructor.Call(args, kwargs),
                acceptsKwargs: true);
            return type;
        }

        /// <summary>The positional-or-keyword parameters of a C implementation (PyArg_ParseTupleAndKeywords):
        /// each keyword takes its place in the argument list, and a gap reads as None.</summary>
        private void AddKeywordFunction(string name, Func<PyObject[], PyObject> implementation, params string[] parameters)
        {
            ModuleDict[name] = new PyBuiltinFunction(name, (Func<PyObject[], PyDict, PyObject>)((args, kwargs) =>
            {
                if (kwargs == null || kwargs.Length() == 0) return implementation(args);
                var merged = new List<PyObject>(args);
                foreach (var item in kwargs.InternalDict)
                {
                    int slot = Array.IndexOf(parameters, ((PyString)item.Key).Value);
                    if (slot < 0)
                        throw PyTypeError.Create($"{name}() got an unexpected keyword argument '{((PyString)item.Key).Value}'");
                    while (merged.Count <= slot) merged.Add(PyNone.Instance);
                    merged[slot] = item.Value;
                }
                return implementation(merged.ToArray());
            }));
        }

        /// <summary>
        /// itertools.starmap(function, iterable) - 각 인수를 언팩하여 함수 호출
        /// CPython 3.12: Modules/itertoolsmodule.c:1144-1180
        /// </summary>
        private PyObject StarmapFunction(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"starmap expected 2 arguments ({args.Length} given)");

            return new StarmapIterator(args[0], args[1]);
        }

        /// <summary>
        /// itertools.batched(iterable, n) - CPython 3.12: Modules/itertoolsmodule.c batched_new
        /// </summary>
        private PyObject BatchedFunction(PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"batched expected 2 arguments ({args.Length} given)");

            int n;
            if (args[1] is PyInt nInt)
                n = (int)nInt.Value;
            else
                throw PyTypeError.Create($"'batched' object cannot be interpreted as an integer");

            if (n < 1)
                throw PyValueError.Create("n must be at least one");

            return new BatchedIterator(args[0], n);
        }

        /// <summary>
        /// itertools.combinations_with_replacement(iterable, r)
        /// CPython 3.12: Modules/itertoolsmodule.c combinations_with_replacement_new
        /// </summary>
        private PyObject CombinationsWithReplacementFunction(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"combinations_with_replacement expected 2 arguments ({args.Length} given)");

            int r;
            if (args[1] is PyInt rInt)
                r = (int)rInt.Value;
            else
                throw PyTypeError.Create($"'combinations_with_replacement' object cannot be interpreted as an integer");

            return new CombinationsWithReplacementIterator(args[0], r);
        }

        /// <summary>
        /// itertools.pairwise(iterable) - CPython 3.10+
        /// </summary>
        private PyObject PairwiseFunction(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"pairwise expected 1 argument ({args.Length} given)");

            return new PairwiseIterator(args[0]);
        }

        /// <summary>
        /// itertools.tee(iterable, n=2) - CPython 3.12: Modules/itertoolsmodule.c tee_new
        /// </summary>
        private PyObject TeeFunction(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"tee expected 1 or 2 arguments ({args.Length} given)");

            int n = 2;
            if (args.Length >= 2)
            {
                if (args[1] is PyInt nInt)
                    n = (int)nInt.Value;
                else
                    throw PyTypeError.Create($"'tee' object cannot be interpreted as an integer");
            }

            var shared = new TeeShared(args[0]);
            var results = new PyObject[n];
            for (int i = 0; i < n; i++)
                results[i] = new TeeIterator(shared);
            return new PyTuple(results);
        }

        /// <summary>
        /// itertools.count(start=0, step=1) - 무한 카운터
        /// </summary>
        private PyObject CountFunction(PyObject[] args)
        {
            // Modules/itertoolsmodule.c count: any numbers, advanced by +.
            PyObject start = args.Length >= 1 ? args[0] : new PyInt(0);
            PyObject step = args.Length >= 2 ? args[1] : new PyInt(1);
            foreach (var value in new[] { start, step })
                if (value.LookupSpecialMethodUncached("__add__") == null)
                    throw PyTypeError.Create("a number is required");
            return new CountIterator(start, step);
        }

        /// <summary>
        /// itertools.cycle(iterable) - 이터러블을 무한 반복
        /// </summary>
        private PyObject CycleFunction(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"cycle expected 1 argument ({args.Length} given)");

            return new CycleIterator(args[0]);
        }

        /// <summary>
        /// itertools.repeat(object, times=None) - 객체를 반복
        /// </summary>
        private PyObject RepeatFunction(PyObject[] args)
        {
            if (args.Length == 0 || args.Length > 2)
                throw PyTypeError.Create($"repeat expected 1 or 2 arguments ({args.Length} given)");

            PyObject obj = args[0];
            int? times = null;

            if (args.Length == 2 && args[1] is PyInt timesInt)
                times = (int)timesInt.Value;

            return new RepeatIterator(obj, times);
        }

        /// <summary>
        /// itertools.chain(*iterables) - 여러 이터러블을 연결
        /// </summary>
        private PyObject ChainFunction(PyObject[] args)
        {
            return new ChainFromIterable(new PyTuple(args).GetIterator());
        }

        /// <summary>
        /// itertools.combinations(iterable, r) - 조합
        /// </summary>
        private PyObject CombinationsFunction(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"combinations expected 2 arguments ({args.Length} given)");

            if (!(args[1] is PyInt rInt))
                throw PyTypeError.Create("combinations() r must be an integer");

            return new CombinationsIterator(args[0], (int)rInt.Value);
        }

        /// <summary>
        /// itertools.permutations(iterable, r=None) - 순열
        /// </summary>
        private PyObject PermutationsFunction(PyObject[] args)
        {
            if (args.Length == 0 || args.Length > 2)
                throw PyTypeError.Create($"permutations expected 1 or 2 arguments ({args.Length} given)");

            int? r = null;
            if (args.Length == 2 && args[1] is PyInt rInt)
                r = (int)rInt.Value;

            return new PermutationsIterator(args[0], r);
        }

        /// <summary>
        /// itertools.product(*iterables, repeat=1) - 곱집합
        /// </summary>
        private PyObject ProductFunction(PyObject[] args, PyDict kwargs)
        {
            int repeat = 1;
            if (KwValue(kwargs, "repeat") is PyObject repeatValue)
            {
                repeat = repeatValue.ToInt();
                if (repeat < 0) throw PyValueError.Create("repeat argument cannot be negative");
            }
            // Performance: Eliminated LINQ
            var iterables = new List<PyObject>();
            for (int i = 0; i < args.Length; i++)
            {
                iterables.Add(args[i]);
            }

            var iterablesArray = new PyObject[iterables.Count];
            for (int i = 0; i < iterables.Count; i++)
            {
                iterablesArray[i] = iterables[i];
            }
            return new ProductIterator(iterablesArray, repeat);
        }

        /// <summary>
        /// itertools.accumulate(iterable, func=None, initial=None) - 누적 계산
        /// </summary>
        private PyObject AccumulateFunction(PyObject[] args)
        {
            if (args.Length == 0 || args.Length > 3)
                throw PyTypeError.Create($"accumulate expected 1 to 3 arguments ({args.Length} given)");

            PyObject func = null;
            PyObject initial = null;

            if (args.Length >= 2 && !(args[1] is PyNone))
                func = args[1];
            if (args.Length == 3 && !(args[2] is PyNone))
                initial = args[2];

            return new AccumulateIterator(args[0], func, initial);
        }

        /// <summary>
        /// itertools.compress(data, selectors) - 선택적 필터링
        /// </summary>
        private PyObject CompressFunction(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"compress expected 2 arguments ({args.Length} given)");

            return new CompressIterator(args[0], args[1]);
        }

        /// <summary>
        /// itertools.dropwhile(predicate, iterable) - 조건이 false가 될 때까지 건너뛰기
        /// </summary>
        private PyObject DropWhileFunction(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"dropwhile expected 2 arguments ({args.Length} given)");

            return new DropWhileIterator(args[0], args[1]);
        }

        /// <summary>
        /// itertools.takewhile(predicate, iterable) - 조건이 true인 동안만 가져오기
        /// </summary>
        private PyObject TakeWhileFunction(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"takewhile expected 2 arguments ({args.Length} given)");

            return new TakeWhileIterator(args[0], args[1]);
        }

        /// <summary>
        /// itertools.filterfalse(predicate, iterable) - 조건이 false인 것만 필터링
        /// </summary>
        private PyObject FilterFalseFunction(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"filterfalse expected 2 arguments ({args.Length} given)");

            return new FilterFalseIterator(args[0], args[1]);
        }

        /// <summary>
        /// itertools.islice(iterable, start, stop, step) - 슬라이싱 이터레이터
        /// </summary>
        private PyObject ISliceFunction(PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 4)
                throw PyTypeError.Create($"islice expected 2 to 4 arguments ({args.Length} given)");

            return new ISliceIterator(args);
        }

        /// <summary>
        /// itertools.groupby(iterable, key=None) - 그룹화
        /// </summary>
        private PyObject GroupByFunction(PyObject[] args)
        {
            if (args.Length == 0 || args.Length > 2)
                throw PyTypeError.Create($"groupby expected 1 or 2 arguments ({args.Length} given)");

            PyObject key = null;
            if (args.Length == 2 && !(args[1] is PyNone))
                key = args[1];

            return new GroupByIterator(args[0], key);
        }

        /// <summary>
        /// itertools.zip_longest(*iterables, fillvalue=None) - 가장 긴 이터러블에 맞춰 zip
        /// </summary>
        private PyObject ZipLongestFunction(PyObject[] args, PyDict kwargs)
        {
            return new ZipLongestIterator(args, KwValue(kwargs, "fillvalue") ?? PyNone.Instance);
        }
    }

    internal sealed class ChainFromIterable : PyIterator
    {
        protected override string IteratorName => "itertools.chain";

        private readonly PyObject _outer;
        private PyObject _inner;

        internal ChainFromIterable(PyObject outer) { _outer = outer; }

        public override PyObject Next()
        {
            while (true)
            {
                if (_inner == null) _inner = _outer.Next().GetIterator();
                try { return _inner.Next(); }
                catch (PythonException error) when (error.PyException is PyStopIteration) { _inner = null; }
            }
        }
    }

    #region Iterator Implementations

    /// <summary>
    /// batched(iterable, n) iterator
    /// </summary>
    public class BatchedIterator : PyIterator
    {
        protected override string IteratorName => "itertools.batched";
        private readonly PyObject _iterator;
        private readonly int _n;

        public BatchedIterator(PyObject iterable, int n)
        {
            _iterator = iterable.GetIterator();
            _n = n;
        }

        public override PyObject Next()
        {
            var items = new List<PyObject>();
            for (int i = 0; i < _n; i++)
            {
                PyObject item;
                try { item = _iterator.Next(); }
                catch (PythonException ex) when (ex.PyException is PyStopIteration) { break; }
                items.Add(item);
            }
            if (items.Count == 0)
                throw PyStopIteration.Create();
            return new PyTuple(items.ToArray());
        }
    }

    /// <summary>
    /// combinations_with_replacement(iterable, r) iterator
    /// CPython 3.12: Modules/itertoolsmodule.c
    /// </summary>
    public class CombinationsWithReplacementIterator : PyIterator
    {
        protected override string IteratorName => "itertools.combinations_with_replacement";
        private readonly List<PyObject> _pool;
        private readonly int _r;
        private int[] _indices;
        private bool _started = false;

        public CombinationsWithReplacementIterator(PyObject iterable, int r)
        {
            _pool = new List<PyObject>();
            var iterator = iterable.GetIterator();
            try
            {
                while (true)
                    _pool.Add(iterator.Next());
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration) { }

            _r = r;
            if (r < 0 || _pool.Count == 0)
            {
                _indices = null;
            }
            else
            {
                _indices = new int[r];
                // All start at 0
            }
        }

        public override PyObject Next()
        {
            if (_indices == null)
                throw PyStopIteration.Create();

            if (!_started)
            {
                _started = true;
                var result = new PyObject[_r];
                for (int i = 0; i < _r; i++)
                    result[i] = _pool[_indices[i]];
                return new PyTuple(result);
            }

            // Find rightmost index that can be incremented
            int i2 = _r - 1;
            while (i2 >= 0 && _indices[i2] == _pool.Count - 1)
                i2--;

            if (i2 < 0)
                throw PyStopIteration.Create();

            int val = _indices[i2] + 1;
            for (int j = i2; j < _r; j++)
                _indices[j] = val;

            var resultArray = new PyObject[_r];
            for (int idx = 0; idx < _r; idx++)
                resultArray[idx] = _pool[_indices[idx]];
            return new PyTuple(resultArray);
        }
    }

    /// <summary>
    /// pairwise(iterable) iterator
    /// </summary>
    public class PairwiseIterator : PyIterator
    {
        protected override string IteratorName => "itertools.pairwise";
        private readonly PyObject _iterator;
        private PyObject _prev;
        private bool _started = false;

        public PairwiseIterator(PyObject iterable)
        {
            _iterator = iterable.GetIterator();
        }

        public override PyObject Next()
        {
            if (!_started)
            {
                _started = true;
                try { _prev = _iterator.Next(); }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    throw PyStopIteration.Create();
                }
            }

            PyObject current;
            try { current = _iterator.Next(); }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                throw PyStopIteration.Create();
            }

            var result = new PyTuple(new PyObject[] { _prev, current });
            _prev = current;
            return result;
        }
    }

    /// <summary>
    /// tee shared state - one source iterator with per-branch buffers
    /// CPython 3.12: Modules/itertoolsmodule.c teeobject
    /// </summary>
    public class TeeShared
    {
        private readonly PyObject _iterator;
        private readonly List<PyObject> _buffer = new List<PyObject>();
        private bool _exhausted = false;

        public TeeShared(PyObject iterable)
        {
            _iterator = iterable.GetIterator();
        }

        /// <summary>
        /// Get item at index, pulling from source if needed.
        /// Returns null if index is past the end.
        /// </summary>
        public PyObject GetItem(int index)
        {
            while (_buffer.Count <= index && !_exhausted)
            {
                try
                {
                    _buffer.Add(_iterator.Next());
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    _exhausted = true;
                    break;
                }
            }
            if (index < _buffer.Count)
                return _buffer[index];
            return null;
        }
    }

    /// <summary>
    /// tee branch iterator
    /// </summary>
    public class TeeIterator : PyIterator
    {
        private readonly TeeShared _shared;
        private int _index = 0;

        public TeeIterator(TeeShared shared)
        {
            _shared = shared;
        }

        public override PyObject Next()
        {
            PyObject item = _shared.GetItem(_index);
            if (item == null)
                throw PyStopIteration.Create();
            _index++;
            return item;
        }
    }

    /// <summary>
    /// count 이터레이터 구현
    /// </summary>
    public class CountIterator : PyIterator
    {
        protected override string IteratorName => "itertools.count";
        private PyObject _current;
        private readonly PyObject _step;

        public CountIterator(PyObject start, PyObject step)
        {
            _current = start;
            _step = step;
        }

        public override PyObject Next()
        {
            var result = _current;
            _current = PyVM.Instance.ExecuteBinaryOpType(_current, _step, BinaryOpType.ADD);
            return result;
        }

        public override string ToString() => $"count({_current.ToRepr().Value}, {_step.ToRepr().Value})";
    }

    /// <summary>
    /// cycle 이터레이터 구현
    /// </summary>
    public class CycleIterator : PyIterator
    {
        protected override string IteratorName => "itertools.cycle";
        private readonly List<PyObject> _items;
        private int _index = 0;

        public CycleIterator(PyObject iterable)
        {
            _items = new List<PyObject>();
            var iterator = iterable.GetIterator();
            
            try
            {
                while (true)
                {
                    _items.Add(iterator.Next());
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 모든 아이템을 수집 완료
            }

            if (_items.Count == 0)
                throw PyStopIteration.Create();
        }

        public override PyObject Next()
        {
            if (_items.Count == 0)
                throw PyStopIteration.Create();

            var result = _items[_index];
            _index = (_index + 1) % _items.Count;
            return result;
        }

        public override string ToString() => $"cycle({_items.Count} items)";
    }

    /// <summary>
    /// repeat 이터레이터 구현
    /// </summary>
    public class RepeatIterator : PyIterator
    {
        protected override string IteratorName => "itertools.repeat";
        private readonly PyObject _object;
        private int? _remaining;

        public RepeatIterator(PyObject obj, int? times)
        {
            _object = obj;
            _remaining = times;
        }

        public override PyObject Next()
        {
            if (_remaining.HasValue)
            {
                if (_remaining <= 0)
                    throw PyStopIteration.Create();
                _remaining--;
            }

            return _object;
        }

        public override string ToString() => 
            _remaining.HasValue ? $"repeat({_object}, {_remaining})" : $"repeat({_object})";
    }

    #endregion

    /// <summary>
    /// combinations(iterable, r) 이터레이터 구현
    /// </summary>
    public class CombinationsIterator : PyIterator
    {
        protected override string IteratorName => "itertools.combinations";
        private readonly List<PyObject> _pool;
        private readonly int _r;
        private int[] _indices;
        private bool _started = false;

        public CombinationsIterator(PyObject iterable, int r)
        {
            _pool = new List<PyObject>();
            var iterator = iterable.GetIterator();
            try
            {
                while (true)
                {
                    _pool.Add(iterator.Next());
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration) { }

            _r = r;
            if (r < 0 || r > _pool.Count)
            {
                _indices = null; // Empty iterator
            }
            else
            {
                // Performance: Eliminated LINQ
                _indices = new int[r];
                for (int i = 0; i < r; i++)
                {
                    _indices[i] = i;
                }
            }
        }

        public override PyObject Next()
        {
            if (_indices == null)
                throw PyStopIteration.Create();

            if (!_started)
            {
                _started = true;
                // Performance: Eliminated LINQ
                var result = new PyObject[_indices.Length];
                for (int idx = 0; idx < _indices.Length; idx++)
                {
                    result[idx] = _pool[_indices[idx]];
                }
                return new PyTuple(result);
            }

            // Generate next combination
            int i = _r - 1;
            while (i >= 0 && _indices[i] == _pool.Count - _r + i)
                i--;

            if (i < 0)
                throw PyStopIteration.Create();

            _indices[i]++;
            for (int j = i + 1; j < _r; j++)
                _indices[j] = _indices[j - 1] + 1;

            // Performance: Eliminated LINQ
            var resultArray = new PyObject[_indices.Length];
            for (int idx = 0; idx < _indices.Length; idx++)
            {
                resultArray[idx] = _pool[_indices[idx]];
            }
            return new PyTuple(resultArray);
        }
    }

    /// <summary>
    /// permutations(iterable, r) 이터레이터 구현
    /// </summary>
    public class PermutationsIterator : PyIterator
    {
        protected override string IteratorName => "itertools.permutations";
        private readonly List<PyObject> _pool;
        private readonly int _r;
        private int[] _indices;
        private int[] _cycles;
        private bool _started = false;

        public PermutationsIterator(PyObject iterable, int? r)
        {
            _pool = new List<PyObject>();
            var iterator = iterable.GetIterator();
            try
            {
                while (true)
                {
                    _pool.Add(iterator.Next());
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration) { }

            _r = r ?? _pool.Count;

            if (_r > _pool.Count)
            {
                _indices = null; // Empty iterator
            }
            else
            {
                // Performance: Eliminated LINQ
                _indices = new int[_pool.Count];
                for (int i = 0; i < _pool.Count; i++)
                {
                    _indices[i] = i;
                }

                _cycles = new int[_r];
                for (int i = 0; i < _r; i++)
                {
                    _cycles[i] = _pool.Count - i;
                }
            }
        }

        public override PyObject Next()
        {
            if (_indices == null)
                throw PyStopIteration.Create();

            if (!_started)
            {
                _started = true;
                // Performance: Eliminated LINQ
                var result = new PyObject[_r];
                for (int idx = 0; idx < _r; idx++)
                {
                    result[idx] = _pool[_indices[idx]];
                }
                return new PyTuple(result);
            }

            for (int i = _r - 1; i >= 0; i--)
            {
                _cycles[i]--;
                if (_cycles[i] == 0)
                {
                    // Rotate indices
                    var first = _indices[i];
                    Array.Copy(_indices, i + 1, _indices, i, _pool.Count - i - 1);
                    _indices[_pool.Count - 1] = first;
                    _cycles[i] = _pool.Count - i;
                }
                else
                {
                    // Swap
                    int j = _pool.Count - _cycles[i];
                    (_indices[i], _indices[j]) = (_indices[j], _indices[i]);
                    // Performance: Eliminated LINQ
                    var resultArray = new PyObject[_r];
                    for (int idx = 0; idx < _r; idx++)
                    {
                        resultArray[idx] = _pool[_indices[idx]];
                    }
                    return new PyTuple(resultArray);
                }
            }

            throw PyStopIteration.Create();
        }
    }

    /// <summary>
    /// product(*iterables, repeat=1) 이터레이터 구현
    /// </summary>
    public class ProductIterator : PyIterator
    {
        protected override string IteratorName => "itertools.product";
        private readonly List<PyObject>[] _pools;
        private readonly int[] _indices;
        private bool _started = false;

        public ProductIterator(PyObject[] iterables, int repeat)
        {
            var expandedIterables = new List<PyObject>();
            for (int r = 0; r < repeat; r++)
            {
                expandedIterables.AddRange(iterables);
            }
            
            _pools = new List<PyObject>[expandedIterables.Count];
            
            for (int i = 0; i < expandedIterables.Count; i++)
            {
                _pools[i] = new List<PyObject>();
                var iterator = expandedIterables[i].GetIterator();
                try
                {
                    while (true)
                    {
                        _pools[i].Add(iterator.Next());
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration) { }
                
                if (_pools[i].Count == 0)
                {
                    _indices = null; // Empty iterator if any pool is empty
                    return;
                }
            }
            
            _indices = new int[_pools.Length];
        }

        public override PyObject Next()
        {
            if (_indices == null)
                throw PyStopIteration.Create();

            if (!_started)
            {
                _started = true;
                // Performance: Eliminated LINQ
                var result = new PyObject[_indices.Length];
                for (int i = 0; i < _indices.Length; i++)
                {
                    result[i] = _pools[i][_indices[i]];
                }
                return new PyTuple(result);
            }

            // Increment indices (like odometer)
            for (int i = _pools.Length - 1; i >= 0; i--)
            {
                _indices[i]++;
                if (_indices[i] < _pools[i].Count)
                {
                    // Performance: Eliminated LINQ
                    var resultArray = new PyObject[_indices.Length];
                    for (int poolIdx = 0; poolIdx < _indices.Length; poolIdx++)
                    {
                        resultArray[poolIdx] = _pools[poolIdx][_indices[poolIdx]];
                    }
                    return new PyTuple(resultArray);
                }
                _indices[i] = 0;
            }

            throw PyStopIteration.Create();
        }
    }

    /// <summary>
    /// itertools.accumulate iterator
    /// CPython 3.12: Modules/itertoolsmodule.c:3587-3616 (accumulate_next)
    /// </summary>
    public class AccumulateIterator : PyIterator
    {
        protected override string IteratorName => "itertools.accumulate";
        private readonly PyIterator _iterator;
        private readonly PyObject _binop;  // CPython: lz->binop
        private PyObject _total;           // CPython: lz->total
        private PyObject _initial;         // CPython: lz->initial

        public AccumulateIterator(PyObject iterable, PyObject func, PyObject initial)
        {
            var iterObj = iterable.GetIterator();
            if (iterObj == null)
                throw PyTypeError.Create("accumulate argument must be iterable");
            _iterator = PyObjectIteratorShim.Of(iterObj);
            _binop = func;
            _total = null;
            _initial = initial;
        }

        public override PyObject Next()
        {
            // CPython 3.12: Modules/itertoolsmodule.c:3591-3594
            // if (lz->initial != Py_None) { lz->total = lz->initial; ... return lz->total; }
            if (_initial != null)
            {
                _total = _initial;
                _initial = null;
                return _total;
            }

            // CPython 3.12: Modules/itertoolsmodule.c:3596-3598
            var val = _iterator.Next();

            // CPython 3.12: Modules/itertoolsmodule.c:3600-3603
            // if (lz->total == NULL) { lz->total = val; return lz->total; }
            if (_total == null)
            {
                _total = val;
                return _total;
            }

            // CPython 3.12: Modules/itertoolsmodule.c:3605-3608
            // if (lz->binop == NULL) newtotal = PyNumber_Add(lz->total, val);
            // else newtotal = PyObject_CallFunctionObjArgs(lz->binop, lz->total, val, NULL);
            PyObject newtotal;
            if (_binop == null)
            {
                newtotal = PyVM.Instance.ExecuteBinaryOpType(_total, val, BinaryOpType.ADD);
            }
            else
            {
                newtotal = _binop.Call(new[] { _total, val }, null);
            }

            _total = newtotal;
            return newtotal;
        }
    }

    /// <summary>
    /// itertools.compress iterator
    /// CPython 3.12: Modules/itertoolsmodule.c:2890-2922 (compress_next)
    /// </summary>
    public class CompressIterator : PyIterator
    {
        protected override string IteratorName => "itertools.compress";
        private readonly PyIterator _dataIterator;     // CPython: lz->data
        private readonly PyIterator _selectorIterator; // CPython: lz->selectors

        public CompressIterator(PyObject data, PyObject selectors)
        {
            var dataObj = data.GetIterator();
            if (dataObj == null)
                throw PyTypeError.Create("compress data must be iterable");
            _dataIterator = PyObjectIteratorShim.Of(dataObj);

            var selectorsObj = selectors.GetIterator();
            if (selectorsObj == null)
                throw PyTypeError.Create("compress selectors must be iterable");
            _selectorIterator = PyObjectIteratorShim.Of(selectorsObj);
        }

        public override PyObject Next()
        {
            // CPython 3.12: Modules/itertoolsmodule.c:2896-2921
            while (true)
            {
                // datum = (*Py_TYPE(lz->data)->tp_iternext)(lz->data);
                // selector = (*Py_TYPE(lz->selectors)->tp_iternext)(lz->selectors);
                var datum = _dataIterator.Next();
                var selector = _selectorIterator.Next();

                // ok = PyObject_IsTrue(selector);
                bool ok = selector.ToBool();

                // if (ok > 0) return datum;
                if (ok)
                    return datum;
                // Continue (skip non-selected items)
            }
        }
    }

    /// <summary>
    /// itertools.dropwhile iterator
    /// CPython 3.12: Modules/itertoolsmodule.c:1422-1452 (dropwhile_next)
    /// </summary>
    public class DropWhileIterator : PyIterator
    {
        protected override string IteratorName => "itertools.dropwhile";
        private readonly PyObject _func;      // CPython: lz->func
        private readonly PyIterator _iterator; // CPython: lz->it
        private int _start;                    // CPython: lz->start (0=dropping, 1=started)

        public DropWhileIterator(PyObject predicate, PyObject iterable)
        {
            _func = predicate;
            var iterObj = iterable.GetIterator();
            if (iterObj == null)
                throw PyTypeError.Create("dropwhile argument 2 must be iterable");
            _iterator = PyObjectIteratorShim.Of(iterObj);
            _start = 0;
        }

        public override PyObject Next()
        {
            // CPython 3.12: Modules/itertoolsmodule.c:1430-1451
            while (true)
            {
                // item = iternext(it);
                var item = _iterator.Next();

                // CPython 3.12: Modules/itertoolsmodule.c:1434-1435
                // if (lz->start == 1) return item;
                if (_start == 1)
                    return item;

                // CPython 3.12: Modules/itertoolsmodule.c:1437-1443
                // good = PyObject_CallOneArg(lz->func, item);
                // ok = PyObject_IsTrue(good);
                // Note: Using ToBool() instead of IsTrue() because PyObject_IsTrue
                // uses nb_bool slot directly (CPython Objects/object.c:1684-1686)
                var good = _func.Call(new[] { item }, null);
                bool ok = good.ToBool();

                // CPython 3.12: Modules/itertoolsmodule.c:1444-1447
                // if (ok == 0) { lz->start = 1; return item; }
                if (!ok)
                {
                    _start = 1;
                    return item;
                }
                // Continue dropping (Py_DECREF(item) in CPython)
            }
        }
    }

    /// <summary>
    /// itertools.takewhile iterator
    /// CPython 3.12: Modules/itertoolsmodule.c:1566-1592 (takewhile_next)
    /// </summary>
    public class TakeWhileIterator : PyIterator
    {
        protected override string IteratorName => "itertools.takewhile";
        private readonly PyObject _func;       // CPython: lz->func
        private readonly PyIterator _iterator;  // CPython: lz->it
        private int _stop;                      // CPython: lz->stop

        public TakeWhileIterator(PyObject predicate, PyObject iterable)
        {
            _func = predicate;
            var iterObj = iterable.GetIterator();
            if (iterObj == null)
                throw PyTypeError.Create("takewhile argument 2 must be iterable");
            _iterator = PyObjectIteratorShim.Of(iterObj);
            _stop = 0;
        }

        public override PyObject Next()
        {
            // CPython 3.12: Modules/itertoolsmodule.c:1572-1573
            // if (lz->stop == 1) return NULL;
            if (_stop == 1)
                throw PyStopIteration.Create();

            // CPython 3.12: Modules/itertoolsmodule.c:1575-1577
            // item = (*Py_TYPE(it)->tp_iternext)(it);
            var item = _iterator.Next();

            // CPython 3.12: Modules/itertoolsmodule.c:1579-1585
            // good = PyObject_CallOneArg(lz->func, item);
            // ok = PyObject_IsTrue(good);
            // Note: Using ToBool() instead of IsTrue() because PyObject_IsTrue
            // uses nb_bool slot directly (CPython Objects/object.c:1684-1686)
            var good = _func.Call(new[] { item }, null);
            bool ok = good.ToBool();

            // CPython 3.12: Modules/itertoolsmodule.c:1586-1591
            // if (ok > 0) return item;
            // if (ok == 0) lz->stop = 1;
            // return NULL;
            if (ok)
            {
                return item;
            }
            else
            {
                _stop = 1;
                throw PyStopIteration.Create();
            }
        }
    }

    /// <summary>
    /// itertools.filterfalse iterator
    /// CPython 3.12: Modules/itertoolsmodule.c:3903-3934 (filterfalse_next)
    /// </summary>
    public class FilterFalseIterator : PyIterator
    {
        protected override string IteratorName => "itertools.filterfalse";
        private readonly PyObject _func;       // CPython: lz->func
        private readonly PyIterator _iterator;  // CPython: lz->it

        public FilterFalseIterator(PyObject predicate, PyObject iterable)
        {
            _func = predicate;
            var iterObj = iterable.GetIterator();
            if (iterObj == null)
                throw PyTypeError.Create("filterfalse argument 2 must be iterable");
            _iterator = PyObjectIteratorShim.Of(iterObj);
        }

        public override PyObject Next()
        {
            // CPython 3.12: Modules/itertoolsmodule.c:3911-3933
            while (true)
            {
                // item = iternext(it);
                var item = _iterator.Next();

                bool ok;
                // CPython 3.12: Modules/itertoolsmodule.c:3916-3927
                // if (lz->func == Py_None || lz->func == (PyObject *)&PyBool_Type)
                //     ok = PyObject_IsTrue(item);
                // else { good = PyObject_CallOneArg(lz->func, item); ok = PyObject_IsTrue(good); }
                // Note: Using ToBool() instead of IsTrue() because PyObject_IsTrue
                // uses nb_bool slot directly (CPython Objects/object.c:1684-1686)
                if (_func == null || _func is PyNone)
                {
                    ok = item.ToBool();
                }
                else
                {
                    var good = _func.Call(new[] { item }, null);
                    ok = good.ToBool();
                }

                // CPython 3.12: Modules/itertoolsmodule.c:3928-3929
                // if (ok == 0) return item;
                if (!ok)
                {
                    return item;
                }
                // Continue filtering (Py_DECREF(item) in CPython)
            }
        }
    }

    public class ISliceIterator : PyIterator
    {
        protected override string IteratorName => "itertools.islice";

        private PyObject _iterator;
        private long _next, _stop, _step, _count;

        // PyNumber_AsSsize_t, and any failure is the same complaint.
        private static long Index(PyObject value, long missing, string complaint)
        {
            if (value is PyNone)
                return missing;
            try
            {
                long number = BytesMethods.AsSsize(value);
                if (number >= 0)
                    return number;
            }
            catch (PythonException)
            {
            }
            throw PyValueError.Create(complaint);
        }

        private const string StopComplaint = "Stop argument for islice() must be None or an integer: 0 <= x <= sys.maxsize.";
        private const string IndexComplaint = "Indices for islice() must be None or an integer: 0 <= x <= sys.maxsize.";

        // islice_new
        public ISliceIterator(PyObject[] args)
        {
            if (args.Length is < 2 or > 4)
                throw PyTypeError.Create(args.Length < 2 ? $"islice expected at least 2 arguments, got {args.Length}" : $"islice expected at most 4 arguments, got {args.Length}");
            _step = 1;
            if (args.Length == 2)
                _stop = Index(args[1], -1, StopComplaint);
            else
            {
                _next = Index(args[1], 0, IndexComplaint);
                _stop = Index(args[2], -1, StopComplaint);
                if (args.Length == 4)
                    _step = Index(args[3], 1, "Step for islice() must be a positive integer or None.");
                if (_step < 1)
                    throw PyValueError.Create("Step for islice() must be a positive integer or None.");
            }
            _iterator = args[0].GetIterator();
        }

        // islice_next
        public override PyObject Next()
        {
            if (_iterator == null)
                throw PyStopIteration.Create();
            long stop = _stop == -1 ? _next : Math.Min(_next, _stop);
            try
            {
                while (_count < stop)
                {
                    _iterator.Next();
                    _count++;
                }
                if (stop != -1 && _count >= stop && _stop != -1 && _count >= _stop)
                    throw PyStopIteration.Create();
                var item = _iterator.Next();
                _count++;
                long previous = _next;
                _next = unchecked(_next + _step);
                if (_next < previous || (_stop != -1 && _next > _stop))
                    _next = _stop;
                return item;
            }
            catch (PythonException error) when (error.PyException is PyStopIteration)
            {
                _iterator = null;
                throw;
            }
        }
    }

    /// <summary>
    /// groupby_next and _grouper_next (Modules/itertoolsmodule.c). The outer iterator and the current
    /// group share one source, so moving the outer iterator on ends the group it handed out.
    /// </summary>
    public class GroupByIterator : PyIterator
    {
        protected override string IteratorName => "itertools.groupby";
        private readonly PyObject _source;
        private readonly PyObject _key;
        private PyObject _current, _currentKey, _targetKey;
        private object _activeGroup;

        public GroupByIterator(PyObject iterable, PyObject key)
        {
            _source = iterable.GetIterator();
            _key = key;
        }

        private bool Advance()
        {
            try { _current = _source.Next(); }
            catch (PythonException error) when (error.PyException is PyStopIteration)
            {
                _current = null;
                return false;
            }
            _currentKey = _key == null ? _current : _key.Call(new[] { _current }, null);
            return true;
        }

        private static bool SameKey(PyObject a, PyObject b) =>
            ReferenceEquals(a, b) || PyObject.Compare(a, b, PyObject.CompareOp.EQ).IsTrue();

        public override PyObject Next()
        {
            _activeGroup = null;
            while (true)
            {
                if (_currentKey == null)
                {
                    if (!Advance()) throw PyStopIteration.Create();
                }
                else if (_targetKey == null) break;
                else if (!SameKey(_targetKey, _currentKey)) break;
                else if (!Advance()) throw PyStopIteration.Create();
                if (_targetKey == null) break;
            }
            _targetKey = _currentKey;
            var group = new Grouper(this, _targetKey);
            _activeGroup = group;
            return new PyTuple(new PyObject[] { _currentKey, group });
        }

        private sealed class Grouper : PyIterator
        {
            private readonly GroupByIterator _parent;
            private readonly PyObject _groupKey;

            internal Grouper(GroupByIterator parent, PyObject groupKey)
            {
                _parent = parent;
                _groupKey = groupKey;
            }

            public override PyObject Next()
            {
                if (!ReferenceEquals(_parent._activeGroup, this) || _parent._current == null)
                    throw PyStopIteration.Create();
                if (!SameKey(_groupKey, _parent._currentKey)) throw PyStopIteration.Create();
                var value = _parent._current;
                if (!_parent.Advance()) _parent._currentKey = null;
                return value;
            }
        }
    }

    /// <summary>
    /// itertools.zip_longest iterator
    /// CPython 3.12: Modules/itertoolsmodule.c:4320-4380 (zip_longest_next)
    /// </summary>
    public class ZipLongestIterator : PyIterator
    {
        protected override string IteratorName => "itertools.zip_longest";
        private readonly PyIterator[] _iterators;   // CPython: lz->ittuple
        private readonly PyObject _fillvalue;       // CPython: lz->fillvalue
        private readonly bool[] _active;            // Track active iterators

        public ZipLongestIterator(PyObject[] iterables, PyObject fillvalue)
        {
            _fillvalue = fillvalue;
            _iterators = new PyIterator[iterables.Length];
            _active = new bool[iterables.Length];

            for (int i = 0; i < iterables.Length; i++)
            {
                var iterObj = iterables[i].GetIterator();
                if (iterObj == null)
                    throw PyTypeError.Create("zip_longest argument must be iterable");
                _iterators[i] = PyObjectIteratorShim.Of(iterObj);
                _active[i] = true;
            }
        }

        public override PyObject Next()
        {
            // CPython 3.12: Modules/itertoolsmodule.c:4328-4375
            var result = new PyObject[_iterators.Length];
            bool anyActive = false;

            for (int i = 0; i < _iterators.Length; i++)
            {
                if (_active[i])
                {
                    try
                    {
                        result[i] = _iterators[i].Next();
                        anyActive = true;
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        _active[i] = false;
                        result[i] = _fillvalue;
                    }
                }
                else
                {
                    result[i] = _fillvalue;
                }
            }

            // If all iterators are exhausted, stop
            if (!anyActive)
                throw PyStopIteration.Create();

            return new PyTuple(result);
        }
    }

    /// <summary>
    /// itertools.starmap iterator
    /// CPython 3.12: Modules/itertoolsmodule.c:1166-1190 (starmap_next)
    /// </summary>
    public class StarmapIterator : PyIterator
    {
        protected override string IteratorName => "itertools.starmap";
        private readonly PyObject _func;       // CPython: lz->func
        private readonly PyIterator _iterator; // CPython: lz->it

        public StarmapIterator(PyObject func, PyObject iterable)
        {
            _func = func;
            var iterObj = iterable.GetIterator();
            if (iterObj == null)
                throw PyTypeError.Create("starmap argument 2 must be iterable");
            _iterator = PyObjectIteratorShim.Of(iterObj);
        }

        public override PyObject Next()
        {
            // CPython 3.12: Modules/itertoolsmodule.c:1172-1188
            // args = iternext(lz->it);
            var args = _iterator.Next();

            // result = PyObject_Call(lz->func, args, NULL);
            // Unpack args and call function
            PyObject[] argsArray;
            if (args is PyTuple tuple)
            {
                argsArray = tuple.Items;
            }
            else if (args is PyList list)
            {
                argsArray = list.Items.ToArray();
            }
            else
            {
                // Try to iterate
                var tempList = new System.Collections.Generic.List<PyObject>();
                var iter = args.GetIterator();
                try
                {
                    while (true)
                    {
                        tempList.Add(iter.Next());
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration) { }
                argsArray = tempList.ToArray();
            }

            return _func.Call(argsArray, null);
        }
    }
}