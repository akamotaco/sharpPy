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
            // PyModule_AddObjectRef(m, "__about__", ...): Modules/_heapqmodule.c:577
            d["__about__"] = PyString.From("Heap queues\n\n[explanation by Fran\u00e7ois Pinard]\n\nHeaps are arrays for which a[k] <= a[2*k+1] and a[k] <= a[2*k+2] for\nall k, counting elements from 0.  For the sake of comparison,\nnon-existing elements are considered to be infinite.  The interesting\nproperty of a heap is that a[0] is always its smallest element.\n\nThe strange invariant above is meant to be an efficient memory\nrepresentation for a tournament.  The numbers below are `k', not a[k]:\n\n                                   0\n\n                  1                                 2\n\n          3               4                5               6\n\n      7       8       9       10      11      12      13      14\n\n    15 16   17 18   19 20   21 22   23 24   25 26   27 28   29 30\n\n\nIn the tree above, each cell `k' is topping `2*k+1' and `2*k+2'.  In\na usual binary tournament we see in sports, each cell is the winner\nover the two cells it tops, and we can trace the winner down the tree\nto see all opponents s/he had.  However, in many computer applications\nof such tournaments, we do not need to trace the history of a winner.\nTo be more memory efficient, when a winner is promoted, we try to\nreplace it by something else at a lower level, and the rule becomes\nthat a cell and the two cells it tops contain three different items,\nbut the top cell \"wins\" over the two topped cells.\n\nIf this heap invariant is protected at all time, index 0 is clearly\nthe overall winner.  The simplest algorithmic way to remove it and\nfind the \"next\" winner is to move some loser (let's say cell 30 in the\ndiagram above) into the 0 position, and then percolate this new 0 down\nthe tree, exchanging values, until the invariant is re-established.\nThis is clearly logarithmic on the total number of items in the tree.\nBy iterating over all items, you get an O(n ln n) sort.\n\nA nice feature of this sort is that you can efficiently insert new\nitems while the sort is going on, provided that the inserted items are\nnot \"better\" than the last 0'th element you extracted.  This is\nespecially useful in simulation contexts, where the tree holds all\nincoming events, and the \"win\" condition means the smallest scheduled\ntime.  When an event schedule other events for execution, they are\nscheduled into the future, so they can easily go into the heap.  So, a\nheap is a good structure for implementing schedulers (this is what I\nused for my MIDI sequencer :-).\n\nVarious structures for implementing schedulers have been extensively\nstudied, and heaps are good for this, as they are reasonably speedy,\nthe speed is almost constant, and the worst case is not much different\nthan the average case.  However, there are other representations which\nare more efficient overall, yet the worst cases might be terrible.\n\nHeaps are also very useful in big disk sorts.  You most probably all\nknow that a big sort implies producing \"runs\" (which are pre-sorted\nsequences, which size is usually related to the amount of CPU memory),\nfollowed by a merging passes for these runs, which merging is often\nvery cleverly organised[1].  It is very important that the initial\nsort produces the longest runs possible.  Tournaments are a good way\nto that.  If, using all the memory available to hold a tournament, you\nreplace and percolate items that happen to fit the current run, you'll\nproduce runs which are twice the size of the memory for random input,\nand much better for input fuzzily ordered.\n\nMoreover, if you output the 0'th item on disk and get an input which\nmay not fit in the current tournament (because the value \"wins\" over\nthe last output value), it cannot fit in the heap, so the size of the\nheap decreases.  The freed memory could be cleverly reused immediately\nfor progressively building a second heap, which grows at exactly the\nsame rate the first heap is melting.  When the first heap completely\nvanishes, you switch heaps and start a new run.  Clever and quite\neffective!\n\nIn a word, heaps are useful memory structures to know.  I use them in\na few applications, and I think it is good to keep a `heap' module\naround. :-)\n\n--------------------\n[1] The disk balancing algorithms which are current, nowadays, are\nmore annoying than clever, and this is a consequence of the seeking\ncapabilities of the disks.  On devices which cannot seek, like big\ntape drives, the story was quite different, and one had to be very\nclever to ensure (far in advance) that each tape movement will be the\nmost effective possible (that is, will best participate at\n\"progressing\" the merge).  Some tapes were even able to read\nbackwards, and this was also used to avoid the rewinding time.\nBelieve me, real good tape sorts were quite spectacular to watch!\nFrom all times, sorting has always been a Great Art! :-)\n");
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
