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
            AddFunction("count", CountFunction);
            AddFunction("cycle", CycleFunction);
            AddFunction("repeat", RepeatFunction);
            AddFunction("chain", ChainFunction);
            AddFunction("combinations", CombinationsFunction);
            AddFunction("permutations", PermutationsFunction);
            AddFunction("product", ProductFunction);
            AddFunction("accumulate", AccumulateFunction);
            AddFunction("compress", CompressFunction);
            AddFunction("dropwhile", DropWhileFunction);
            AddFunction("takewhile", TakeWhileFunction);
            AddFunction("filterfalse", FilterFalseFunction);
            AddFunction("islice", ISliceFunction);
            AddFunction("groupby", GroupByFunction);
            AddFunction("zip_longest", ZipLongestFunction);
        }

        /// <summary>
        /// itertools.count(start=0, step=1) - 무한 카운터
        /// </summary>
        private PyObject CountFunction(PyObject[] args)
        {
            // CPython 3.12: Modules/itertoolsmodule.c:4510-4550 - itertools_count
            long start = 0;
            long step = 1;

            if (args.Length >= 1 && args[0] is PyInt startInt)
                start = (long)startInt.Value;
            if (args.Length >= 2 && args[1] is PyInt stepInt)
                step = (long)stepInt.Value;

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
            return new ChainIterator(args);
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
        private PyObject ProductFunction(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("product expected at least 1 argument");

            int repeat = 1;
            // Performance: Eliminated LINQ
            var iterables = new List<PyObject>();
            for (int i = 0; i < args.Length; i++)
            {
                iterables.Add(args[i]);
            }

            // repeat 키워드 인수는 단순화하여 생략
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

            if (args.Length >= 2)
                func = args[1];
            if (args.Length == 3)
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
            if (args.Length == 2)
                key = args[1];

            return new GroupByIterator(args[0], key);
        }

        /// <summary>
        /// itertools.zip_longest(*iterables, fillvalue=None) - 가장 긴 이터러블에 맞춰 zip
        /// </summary>
        private PyObject ZipLongestFunction(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("zip_longest expected at least 1 argument");

            // fillvalue는 단순화하여 None으로 고정
            return new ZipLongestIterator(args, PyNone.Instance);
        }
    }

    #region Iterator Implementations

    /// <summary>
    /// count 이터레이터 구현
    /// </summary>
    public class CountIterator : PyIterator
    {
        private long _current;
        private readonly long _step;

        public CountIterator(long start, long step)
        {
            _current = start;
            _step = step;
        }

        public override PyObject Next()
        {
            var result = new PyInt((int)_current);
            _current += _step;
            return result;
        }

        public override string ToString() => $"count({_current - _step}, {_step})";
    }

    /// <summary>
    /// cycle 이터레이터 구현
    /// </summary>
    public class CycleIterator : PyIterator
    {
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

    /// <summary>
    /// chain 이터레이터 구현
    /// </summary>
    public class ChainIterator : PyIterator
    {
        private readonly PyIterator[] _iterators;
        private int _currentIndex = 0;

        public ChainIterator(PyObject[] iterables)
        {
            _iterators = new PyIterator[iterables.Length];
            for (int i = 0; i < iterables.Length; i++)
            {
                var iter = iterables[i].GetIterator();
                if (iter is PyIterator pyIter)
                    _iterators[i] = pyIter;
                else
                    throw PyTypeError.Create($"'{iterables[i].GetTypeName()}' object is not iterable");
            }
        }

        public override PyObject Next()
        {
            while (_currentIndex < _iterators.Length)
            {
                try
                {
                    return _iterators[_currentIndex].Next();
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    _currentIndex++;
                }
            }

            throw PyStopIteration.Create();
        }

        public override string ToString() => $"chain({_iterators.Length} iterables)";
    }

    #endregion

    /// <summary>
    /// combinations(iterable, r) 이터레이터 구현
    /// </summary>
    public class CombinationsIterator : PyIterator
    {
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

    public class AccumulateIterator : PyIterator
    {
        public AccumulateIterator(PyObject iterable, PyObject func, PyObject initial) { }
        public override PyObject Next() => throw PyStopIteration.Create();
    }

    public class CompressIterator : PyIterator
    {
        public CompressIterator(PyObject data, PyObject selectors) { }
        public override PyObject Next() => throw PyStopIteration.Create();
    }

    public class DropWhileIterator : PyIterator
    {
        public DropWhileIterator(PyObject predicate, PyObject iterable) { }
        public override PyObject Next() => throw PyStopIteration.Create();
    }

    public class TakeWhileIterator : PyIterator
    {
        public TakeWhileIterator(PyObject predicate, PyObject iterable) { }
        public override PyObject Next() => throw PyStopIteration.Create();
    }

    public class FilterFalseIterator : PyIterator
    {
        public FilterFalseIterator(PyObject predicate, PyObject iterable) { }
        public override PyObject Next() => throw PyStopIteration.Create();
    }

    public class ISliceIterator : PyIterator
    {
        private readonly PyIterator _iterator;
        private readonly int _start;
        private readonly int? _stop;
        private readonly int _step;
        private int _index;

        public ISliceIterator(PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 4)
                throw PyTypeError.Create($"islice expected 2-4 arguments ({args.Length} given)");

            // Get iterable and convert to iterator
            var iteratorObj = args[0].GetIterator();
            if (iteratorObj == null)
                throw PyTypeError.Create("islice argument 1 must be iterable");
            _iterator = (PyIterator)iteratorObj;

            if (args.Length == 2)
            {
                // islice(iterable, stop)
                _start = 0;
                _stop = (int?)((PyInt)args[1]).Value;
                _step = 1;
            }
            else if (args.Length == 3)
            {
                // islice(iterable, start, stop)
                _start = (int)((PyInt)args[1]).Value;
                _stop = (int?)((PyInt)args[2]).Value;
                _step = 1;
            }
            else
            {
                // islice(iterable, start, stop, step)
                _start = (int)((PyInt)args[1]).Value;
                _stop = (int?)((PyInt)args[2]).Value;
                _step = (int)((PyInt)args[3]).Value;

                if (_step <= 0)
                    throw PyValueError.Create("Step for islice() must be a positive integer or None.");
            }

            if (_start < 0)
                throw PyValueError.Create("Indices for islice() must be None or an integer: 0 <= x <= maxint.");
            
            if (_stop.HasValue && _stop < 0)
                throw PyValueError.Create("Stop argument for islice() must be None or an integer: 0 <= x <= maxint.");

            _index = 0;
            
            // Skip elements before start
            while (_index < _start)
            {
                try
                {
                    _iterator.Next();
                    _index++;
                }
                catch (Exception ex) when (ex.Data.Contains("PyException") && ex.Data["PyException"] is PyStopIteration)
                {
                    break;
                }
            }
        }

        public override PyObject Next()
        {
            // Check if we've reached the stop position
            if (_stop.HasValue && _index >= _stop.Value)
                throw PyStopIteration.Create();

            // Skip elements according to step (after the first yielded element)
            if (_index > _start)
            {
                for (int i = 1; i < _step; i++)
                {
                    try
                    {
                        _iterator.Next();
                        _index++;
                        if (_stop.HasValue && _index >= _stop.Value)
                            throw PyStopIteration.Create();
                    }
                    catch (Exception ex) when (ex.Data.Contains("PyException") && ex.Data["PyException"] is PyStopIteration)
                    {
                        throw;
                    }
                }
            }

            try
            {
                var result = _iterator.Next();
                _index++;
                return result;
            }
            catch (Exception ex) when (ex.Data.Contains("PyException") && ex.Data["PyException"] is PyStopIteration)
            {
                throw;
            }
        }
    }

    public class GroupByIterator : PyIterator
    {
        public GroupByIterator(PyObject iterable, PyObject key) { }
        public override PyObject Next() => throw PyStopIteration.Create();
    }

    public class ZipLongestIterator : PyIterator
    {
        public ZipLongestIterator(PyObject[] iterables, PyObject fillvalue) { }
        public override PyObject Next() => throw PyStopIteration.Create();
    }
}