using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_heapqmodule.c: the heap functions work on the list's own slots, and a comparison
    /// that changes the size of the list stops them with a RuntimeError.
    /// </summary>
    internal static class _HeapqModule
    {
        internal static PyModule Create()
        {
            var module = new PyModule("_heapq");
            var d = module.ModuleDict;
            if (PyDocs.Lookup("_heapq.__doc__") is { } doc)
                d["__doc__"] = PyString.From(doc);
            d["heappush"] = Fast("heappush", 2, args =>
            {
                var heap = ListArg("heappush", "argument 1", args[0]);
                heap.Append(args[1]);
                SiftDown(heap, 0, heap.RawCount() - 1);
                return PyNone.Instance;
            });
            d["heappushpop"] = Fast("heappushpop", 2, args => PushPop(ListArg("heappushpop", "argument 1", args[0]), args[1]));
            d["heappop"] = One("heappop", heap => PopInternal(heap, SiftUp));
            d["heapreplace"] = Fast("heapreplace", 2, args => ReplaceInternal(ListArg("heapreplace", "argument 1", args[0]), args[1], SiftUp));
            d["heapify"] = One("heapify", heap => HeapifyInternal(heap, SiftUp));
            d["_heappop_max"] = One("_heappop_max", heap => PopInternal(heap, SiftUpMax));
            d["_heapify_max"] = One("_heapify_max", heap => HeapifyInternal(heap, SiftUpMax));
            d["_heapreplace_max"] = Fast("_heapreplace_max", 2, args => ReplaceInternal(ListArg("_heapreplace_max", "argument 1", args[0]), args[1], SiftUpMax));
            return module;
        }

        // METH_FASTCALL positional only: _PyArg_CheckPositional.
        private static PyBuiltinFunction Fast(string name, int count, Func<PyObject[], PyObject> body) =>
            PyArgs.FastCall("_heapq", name, count, count, body);

        // METH_O with the subclass_of='&PyList_Type' converter.
        private static PyBuiltinFunction One(string name, Func<PyList, PyObject> body) =>
            PyArgs.MethO("_heapq", name, args => body(ListArg(name, "argument", args[0])));

        // _PyArg_BadArgument
        // _PyArg_BadArgument: "argument" for METH_O, "argument 1" for a positional of METH_FASTCALL.
        private static PyList ListArg(string name, string displayname, PyObject value) =>
            value as PyList ?? throw PyArgs.BadArgument(name, displayname, "list", value);

        private static Exception ChangedSize() => PyRuntimeError.Create("list changed size during iteration");

        private static bool Less(PyObject a, PyObject b) => PyObject.CompareBool(a, b, PyObject.CompareOp.LT);

        // siftdown
        private static void SiftDown(PyList heap, int startpos, int pos)
        {
            int size = heap.RawCount();
            if (pos >= size)
                throw PyIndexError.Create("index out of range");
            var newitem = heap.RawGet(pos);
            while (pos > startpos)
            {
                int parentpos = (pos - 1) >> 1;
                var parent = heap.RawGet(parentpos);
                bool cmp = Less(newitem, parent);
                if (size != heap.RawCount())
                    throw ChangedSize();
                if (!cmp)
                    break;
                parent = heap.RawGet(parentpos);
                newitem = heap.RawGet(pos);
                heap.RawSet(parentpos, newitem);
                heap.RawSet(pos, parent);
                pos = parentpos;
            }
        }

        // siftup
        private static void SiftUp(PyList heap, int pos)
        {
            int endpos = heap.RawCount();
            int startpos = pos;
            if (pos >= endpos)
                throw PyIndexError.Create("index out of range");
            int limit = endpos >> 1;
            while (pos < limit)
            {
                int childpos = 2 * pos + 1;
                if (childpos + 1 < endpos)
                {
                    bool cmp = Less(heap.RawGet(childpos), heap.RawGet(childpos + 1));
                    if (!cmp)
                        childpos++;
                    if (endpos != heap.RawCount())
                        throw ChangedSize();
                }
                var tmp1 = heap.RawGet(childpos);
                var tmp2 = heap.RawGet(pos);
                heap.RawSet(childpos, tmp2);
                heap.RawSet(pos, tmp1);
                pos = childpos;
            }
            SiftDown(heap, startpos, pos);
        }

        // siftdown_max
        private static void SiftDownMax(PyList heap, int startpos, int pos)
        {
            int size = heap.RawCount();
            if (pos >= size)
                throw PyIndexError.Create("index out of range");
            var newitem = heap.RawGet(pos);
            while (pos > startpos)
            {
                int parentpos = (pos - 1) >> 1;
                var parent = heap.RawGet(parentpos);
                bool cmp = Less(parent, newitem);
                if (size != heap.RawCount())
                    throw ChangedSize();
                if (!cmp)
                    break;
                parent = heap.RawGet(parentpos);
                newitem = heap.RawGet(pos);
                heap.RawSet(parentpos, newitem);
                heap.RawSet(pos, parent);
                pos = parentpos;
            }
        }

        // siftup_max
        private static void SiftUpMax(PyList heap, int pos)
        {
            int endpos = heap.RawCount();
            int startpos = pos;
            if (pos >= endpos)
                throw PyIndexError.Create("index out of range");
            int limit = endpos >> 1;
            while (pos < limit)
            {
                int childpos = 2 * pos + 1;
                if (childpos + 1 < endpos)
                {
                    bool cmp = Less(heap.RawGet(childpos + 1), heap.RawGet(childpos));
                    if (!cmp)
                        childpos++;
                    if (endpos != heap.RawCount())
                        throw ChangedSize();
                }
                var tmp1 = heap.RawGet(childpos);
                var tmp2 = heap.RawGet(pos);
                heap.RawSet(childpos, tmp2);
                heap.RawSet(pos, tmp1);
                pos = childpos;
            }
            SiftDownMax(heap, startpos, pos);
        }

        // heappop_internal
        private static PyObject PopInternal(PyList heap, Action<PyList, int> siftup)
        {
            int n = heap.RawCount();
            if (n == 0)
                throw PyIndexError.Create("index out of range");
            var lastelt = heap.RawGet(n - 1);
            heap.RawRemoveLast();
            n--;
            if (n == 0)
                return lastelt;
            var returnitem = heap.RawGet(0);
            heap.RawSet(0, lastelt);
            siftup(heap, 0);
            return returnitem;
        }

        // heapreplace_internal
        private static PyObject ReplaceInternal(PyList heap, PyObject item, Action<PyList, int> siftup)
        {
            if (heap.RawCount() == 0)
                throw PyIndexError.Create("index out of range");
            var returnitem = heap.RawGet(0);
            heap.RawSet(0, item);
            siftup(heap, 0);
            return returnitem;
        }

        // _heapq_heappushpop_impl
        private static PyObject PushPop(PyList heap, PyObject item)
        {
            if (heap.RawCount() == 0)
                return item;
            var top = heap.RawGet(0);
            if (!Less(top, item))
                return item;
            if (heap.RawCount() == 0)
                throw PyIndexError.Create("index out of range");
            var returnitem = heap.RawGet(0);
            heap.RawSet(0, item);
            SiftUp(heap, 0);
            return returnitem;
        }

        // keep_top_bit
        private static int KeepTopBit(int n)
        {
            int i = 0;
            while (n > 1)
            {
                n >>= 1;
                i++;
            }
            return n << i;
        }

        // cache_friendly_heapify: the same comparisons as the plain loop, in a cache friendly order.
        private static void CacheFriendlyHeapify(PyList heap, Action<PyList, int> siftup)
        {
            int m = heap.RawCount() >> 1;
            int leftmost = KeepTopBit(m + 1) - 1;
            int mhalf = m >> 1;
            for (int i = leftmost - 1; i >= mhalf; i--)
            {
                int j = i;
                while (true)
                {
                    siftup(heap, j);
                    if ((j & 1) == 0)
                        break;
                    j >>= 1;
                }
            }
            for (int i = m - 1; i >= leftmost; i--)
            {
                int j = i;
                while (true)
                {
                    siftup(heap, j);
                    if ((j & 1) == 0)
                        break;
                    j >>= 1;
                }
            }
        }

        // heapify_internal
        private static PyObject HeapifyInternal(PyList heap, Action<PyList, int> siftup)
        {
            int n = heap.RawCount();
            if (n > 2500)
            {
                CacheFriendlyHeapify(heap, siftup);
                return PyNone.Instance;
            }
            for (int i = (n >> 1) - 1; i >= 0; i--)
                siftup(heap, i);
            return PyNone.Instance;
        }
    }
}
