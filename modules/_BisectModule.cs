using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_bisectmodule.c. The search reads a[mid] through the sequence's sq_item and
    /// compares with PyObject_RichCompareBool; insort inserts into an exact list directly and
    /// calls a.insert(i, x) on anything else.
    /// </summary>
    internal static class _BisectModule
    {
        private static readonly string[] Keywords = { "a", "x", "lo", "hi", "key" };

        internal static PyModule Create()
        {
            var module = new PyModule("_bisect");
            var d = module.ModuleDict;
            if (PyDocs.Lookup("_bisect.__doc__") is { } doc)
                d["__doc__"] = new PyString(doc);
            d["bisect_right"] = Function("bisect_right", (a, x, lo, hi, key) => new PyInt(BisectRight(a, x, lo, hi, key)));
            d["insort_right"] = Function("insort_right", (a, x, lo, hi, key) =>
            {
                long index = BisectRight(a, key is PyNone ? x : key.Call(new[] { x }, null), lo, hi, key);
                Insert(a, index, x);
                return PyNone.Instance;
            });
            d["bisect_left"] = Function("bisect_left", (a, x, lo, hi, key) => new PyInt(BisectLeft(a, x, lo, hi, key)));
            d["insort_left"] = Function("insort_left", (a, x, lo, hi, key) =>
            {
                long index = BisectLeft(a, key is PyNone ? x : key.Call(new[] { x }, null), lo, hi, key);
                Insert(a, index, x);
                return PyNone.Instance;
            });
            return module;
        }

        private delegate PyObject Impl(PyObject a, PyObject x, long lo, long hi, PyObject key);

        // The clinic code: a, x, lo=0, hi=None, *, key=None.
        private static PyBuiltinFunction Function(string name, Impl impl) => new(name, (args, kwargs) =>
        {
            var parsed = PyArgs.Unpack(name, args, kwargs, Keywords, 0, 2, 4);
            long lo = parsed[2] == null ? 0 : PyArgs.Ssize(parsed[2]);
            long hi = -1;
            if (parsed[3] != null && parsed[3] is not PyNone)
            {
                // _Py_convert_optional_to_ssize_t
                if (PyObject.TryGetIndex(parsed[3]) == null)
                    throw PyTypeError.Create($"argument should be integer or None, not '{parsed[3].GetTypeName()}'");
                hi = PyArgs.IndexSsize(parsed[3]);
            }
            return impl(parsed[0], parsed[1], lo, hi, parsed[4] ?? PyNone.Instance);
        });

        // get_sq_item: the part of PySequence_GetItem done once.
        private static void CheckSqItem(PyObject s)
        {
            if (PyVM.IsSequence(s))
                return;
            throw PyTypeError.Create(s is PyDict || s.GetPyType().IsSubclassOf(PyType.DictType)
                ? $"{s.GetTypeName()} is not a sequence"
                : $"'{s.GetTypeName()}' object does not support indexing");
        }

        // The common head of internal_bisect_right and internal_bisect_left.
        private static long Begin(PyObject list, long lo, long hi)
        {
            if (lo < 0)
                throw PyValueError.Create("lo must be non-negative");
            if (hi == -1)
                hi = PyAbstract.SequenceSize(list);
            CheckSqItem(list);
            return hi;
        }

        // sq_item(list, mid) and the key function on it.
        private static PyObject Item(PyObject list, long mid, PyObject key)
        {
            var litem = PyAbstract.GetItem(list, new PyInt(mid));
            return key is PyNone ? litem : key.Call(new[] { litem }, null);
        }

        // internal_bisect_right: if item < key(list[mid]) then hi = mid, else lo = mid + 1.
        private static long BisectRight(PyObject list, PyObject item, long lo, long hi, PyObject key)
        {
            hi = Begin(list, lo, hi);
            PyCRecursion.Enter(" in _bisect.bisect_right");
            try
            {
                while (lo < hi)
                {
                    long mid = (long)(((ulong)lo + (ulong)hi) / 2);
                    var litem = Item(list, mid, key);
                    if (PyObject.CompareBool(item, litem, PyObject.CompareOp.LT))
                        hi = mid;
                    else
                        lo = mid + 1;
                }
            }
            finally
            {
                PyCRecursion.Leave();
            }
            return lo;
        }

        // internal_bisect_left: if key(list[mid]) < item then lo = mid + 1, else hi = mid.
        private static long BisectLeft(PyObject list, PyObject item, long lo, long hi, PyObject key)
        {
            hi = Begin(list, lo, hi);
            PyCRecursion.Enter(" in _bisect.bisect_left");
            try
            {
                while (lo < hi)
                {
                    long mid = (long)(((ulong)lo + (ulong)hi) / 2);
                    var litem = Item(list, mid, key);
                    if (PyObject.CompareBool(litem, item, PyObject.CompareOp.LT))
                        lo = mid + 1;
                    else
                        hi = mid;
                }
            }
            finally
            {
                PyCRecursion.Leave();
            }
            return lo;
        }

        // PyList_Insert for an exact list, a.insert(index, x) for anything else.
        private static void Insert(PyObject a, long index, PyObject x)
        {
            if (a is PyList list && a is not PyListSubclass)
                list.Insert((int)Math.Min(index, int.MaxValue), x);
            else
                a.GetAttribute("insert").Call(new PyObject[] { new PyInt(index), x }, null);
        }
    }
}
