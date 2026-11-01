using System.Collections.Generic;

namespace SharpPy
{
    // Lazy built-in iterators matching CPython's map/zip/filter/callable_iterator/reversed.
    // CPython references: Objects/mapobject.c, Modules/itertoolsmodule.c (strict zip),
    // Objects/enumobject.c (reversed_new), Python/builtins.c (iter sentinel form).

    /// <summary>
    /// CPython map object: lazy; with multiple iterables it stops when the
    /// shortest iterable is exhausted (Objects/mapobject.c map_next; the
    /// None-padding behavior does NOT exist for func != None in Python 3).
    /// </summary>
    public class PyMapIterator : PyIterator
    {
        private readonly PyObject _func;
        private readonly PyObject[] _iterators;

        public PyMapIterator(PyObject func, PyObject[] iterators)
        {
            _func = func;
            _iterators = iterators;
        }

        public override PyObject Next()
        {
            // map_next: the shortest iterable ends this round, an exhausted map asks again.
            var args = new PyObject[_iterators.Length];
            for (int i = 0; i < _iterators.Length; i++)
                args[i] = _iterators[i].Next();
            return _func.Call(args, null);
        }

        public override string GetTypeName() => "map";
        public override PyType GetPyType() => PyType.MapType;
        public override PyString ToRepr() => new PyString($"<map object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x}>");
        public override PyString AsString() => ToRepr();
    }

    /// <summary>
    /// CPython zip object: lazy; strict=True raises ValueError on length mismatch with
    /// CPython's exact wording (Objects/itertoolsmodule.c zip_next strict checks).
    /// </summary>
    public class PyZipIterator : PyIterator
    {
        private readonly PyObject[] _iterators;
        private readonly bool _strict;
        public PyZipIterator(PyObject[] iterators, bool strict)
        {
            _iterators = iterators;
            _strict = strict;
        }

        public override PyObject Next()
        {
            // zip_next: an exhausted zip asks its iterators again, they decide.
            if (_iterators.Length == 0)
                throw PyStopIteration.Create();

            var items = new PyObject[_iterators.Length];
            for (int i = 0; i < _iterators.Length; i++)
            {
                try
                {
                    items[i] = _iterators[i].Next();
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    if (_strict)
                        CheckStrict(i);
                    throw PyStopIteration.Create();
                }
            }
            return new PyTuple(items);
        }

        // zip_next "check:": iterator i ran out first; the ones before it gave a value this round.
        private void CheckStrict(int exhausted)
        {
            if (exhausted > 0)
                throw PyValueError.Create($"zip() argument {exhausted + 1} is shorter than {Earlier(exhausted)}");
            for (int i = 1; i < _iterators.Length; i++)
            {
                try
                {
                    _iterators[i].Next();
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    continue;
                }
                throw PyValueError.Create($"zip() argument {i + 1} is longer than {Earlier(i)}");
            }
        }

        private static string Earlier(int count) => count == 1 ? "argument 1" : $"arguments 1-{count}";

        public override string GetTypeName() => "zip";
        public override PyType GetPyType() => PyType.ZipType;
        public override PyString ToRepr() => new PyString($"<zip object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x}>");
        public override PyString AsString() => ToRepr();
    }

    /// <summary>CPython filter object: lazy; func None means truthiness test.</summary>
    public class PyFilterIterator : PyIterator
    {
        private readonly PyObject _func;
        private readonly PyObject _iterator;

        public PyFilterIterator(PyObject func, PyObject iterator)
        {
            _func = func;
            _iterator = iterator;
        }

        public override PyObject Next()
        {
            while (true)
            {
                var item = _iterator.Next();
                bool keep;
                if (_func is PyNone)
                    keep = item.PyBoolValue();
                else
                    keep = _func.Call(new PyObject[] { item }, null).PyBoolValue();
                if (keep)
                    return item;
            }
        }

        public override string GetTypeName() => "filter";
        public override PyType GetPyType() => PyType.FilterType;
        public override PyString ToRepr() => new PyString($"<filter object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x}>");
        public override PyString AsString() => ToRepr();
    }

    /// <summary>
    /// CPython callable_iterator from iter(callable, sentinel): call until the result
    /// equals the sentinel. Errors from the callable propagate (no IndexError capture).
    /// </summary>
    public class PyCallableIterator : PyIterator
    {
        private readonly PyObject _callable;
        private readonly PyObject _sentinel;
        private bool _done;

        public PyCallableIterator(PyObject callable, PyObject sentinel)
        {
            _callable = callable;
            _sentinel = sentinel;
        }

        public override PyObject Next()
        {
            if (_done)
                throw PyStopIteration.Create();
            var value = _callable.Call(new PyObject[0], null);
            if (CompareBool(value, _sentinel, CompareOp.EQ))
            {
                _done = true;
                throw PyStopIteration.Create();
            }
            return value;
        }

        public override string GetTypeName() => "callable_iterator";
        public override PyType GetPyType() => PyType.CallableIteratorType;
        public override PyString ToRepr() => new PyString($"<callable_iterator object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x}>");
        public override PyString AsString() => ToRepr();
    }

    /// <summary>
    /// CPython generic reversed iterator (enumobject.c reversed_new): indexes a
    /// sequence from len-1 down through __getitem__.
    /// </summary>
    public class PyReversedIterator : PyIterator
    {
        private readonly PyObject _seq;
        private long _index;
        private readonly string _typeName;

        // CPython: reversed(list) -> list_reverseiterator, reversed(range) ->
        // range_iterator, reversed(dict) -> dict_keyiterator, others -> reversed.
        // The caller passes the appropriate name; generic sequences use "reversed".
        public PyReversedIterator(PyObject seq, long length, string typeName = "reversed")
        {
            _seq = seq;
            _index = length - 1;
            _typeName = typeName;
        }

        public override PyObject Next()
        {
            // reversed_next, listreviter_next: an index the sequence no longer has ends the iteration.
            if (_index >= 0)
            {
                try
                {
                    var item = _seq.GetItem(new PyInt(_index));
                    _index--;
                    return item;
                }
                catch (PythonException ex) when (ex.PyException is PyIndexError or PyStopIteration)
                {
                }
            }
            _index = -1;
            throw PyStopIteration.Create();
        }

        protected override string IteratorName => _typeName;
    }
}
