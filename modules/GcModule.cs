using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpPy.Modules
{
    /// <summary>
    /// gc: CPython's collector module (Modules/gcmodule.c), built-in there (gc.__spec__.origin is
    /// 'built-in'), so it has no .py file to copy and is implemented here.
    /// POLICY: docs/lifetime_and_gc_policy.md (Korean) - the surface stays CPython's, the numbers
    /// are the ones SharpPy can answer truthfully, and a missing introspection answer is written
    /// down rather than faked.
    ///
    /// Which half of this file is which, using the policy's three classes:
    ///   A (identical to CPython, pinned by p76): the attribute set, both refusal mechanisms and
    ///     their wording, enable/disable/debug state, threshold values, the tracked-object rule,
    ///     get_referents traversal order, and gc.callbacks actually being called by collect().
    ///   B (permanently different, see get_* below): anything that would require knowing what is
    ///     unreachable at this instant.
    ///   C (must end up identical): not this file's business - resource release is the release bus.
    ///
    /// What is faithful: the whole visible surface and its error families, the enable/disable and
    /// debug state, the threshold values, the refusal wording, gc.callbacks being a real list that
    /// collect() actually calls, gc.garbage, per-generation get_stats(), the tracking rule for
    /// is_tracked, and the referent graph get_referents() reports for containers.
    ///
    /// What cannot be faithful while object lifetime belongs to the CLR collector, and is recorded
    /// in docs/known_gaps.md (P0-37 remainder 2-4) rather than papered over:
    ///   get_objects()   - CPython walks its tracked-object list; SharpPy keeps none, so it returns
    ///                     an empty list. A program that enumerates live objects sees fewer objects.
    ///   get_referrers() - needs the reverse of that list, so it returns an empty list too.
    ///   get_count()     - the three counters stay 0; nothing counts allocations per generation.
    ///   collect()       - returns 0. It cannot count what a cycle pass removed, because the CLR
    ///                     is the one that removed it. What it does do is run the __del__ hooks of
    ///                     everything that became unreachable: that is the anchor this design
    ///                     offers in place of a death instant (PyFinalizer.CollectAndDrain).
    /// The trade is deliberate: SharpPy gains cycle collection for free from the CLR, which CPython
    /// has to implement by hand, and pays for it with these four introspection answers.
    /// </summary>
    internal static class GcModule
    {
        private const int Generations = 3;

        private static bool _enabled = true;
        private static long _debug;
        private static long _threshold0 = 700;
        private static long _threshold1 = 10;
        private static long _threshold2 = 10;

        // CPython's gc module keeps these as module-level lists that callers mutate; collect()
        // reads gc.callbacks on every pass, so identity matters more than content.
        private static readonly PyList Garbage = new PyList();
        private static readonly PyList Callbacks = new PyList();

        private const string ModuleDoc =
            "This module provides access to the garbage collector for reference cycles.\n" +
            "\n" +
            "enable() -- Enable automatic garbage collection.\n" +
            "disable() -- Disable automatic garbage collection.\n" +
            "isenabled() -- Returns true if automatic collection is enabled.\n" +
            "collect() -- Do a full collection right now.\n" +
            "get_count() -- Return the current collection counts.\n" +
            "get_stats() -- Return list of dictionaries containing per-generation stats.\n" +
            "set_debug() -- Set debugging flags.\n" +
            "get_debug() -- Get debugging flags.\n" +
            "set_threshold() -- Set the collection thresholds.\n" +
            "get_threshold() -- Return the current the collection thresholds.\n" +
            "get_objects() -- Return a list of all objects tracked by the collector.\n" +
            "is_tracked() -- Returns true if a given object is tracked.\n" +
            "is_finalized() -- Returns true if a given object has been already finalized.\n" +
            "get_referrers() -- Return the list of objects that refer to an object.\n" +
            "get_referents() -- Return the list of objects that an object refers to.\n" +
            "freeze() -- Freeze all tracked objects and ignore them for future collections.\n" +
            "unfreeze() -- Unfreeze all objects in the permanent generation.\n" +
            "get_freeze_count() -- Return the number of objects in the permanent generation.";

        public static PyModule CreateGcModule()
        {
            var module = new PyModule("gc");
            module.ModuleDict["__doc__"] = new PyString(ModuleDoc);

            module.ModuleDict["enable"] = new PyBuiltinFunction("enable", a =>
            {
                NoArgs(a, "enable");
                _enabled = true;
                return PyNone.Instance;
            });
            module.ModuleDict["disable"] = new PyBuiltinFunction("disable", a =>
            {
                NoArgs(a, "disable");
                _enabled = false;
                return PyNone.Instance;
            });
            module.ModuleDict["isenabled"] = new PyBuiltinFunction("isenabled", a =>
            {
                NoArgs(a, "isenabled");
                return _enabled ? PyBool.True : PyBool.False;
            });

            module.ModuleDict["set_debug"] = new PyBuiltinFunction("set_debug", a =>
            {
                ExactlyOne(a, "set_debug");
                _debug = (long)Index(a[0]);
                return PyNone.Instance;
            });
            module.ModuleDict["get_debug"] = new PyBuiltinFunction("get_debug", a =>
            {
                NoArgs(a, "get_debug");
                return new PyInt(_debug);
            });

            module.ModuleDict["get_count"] = new PyBuiltinFunction("get_count", a =>
            {
                NoArgs(a, "get_count");
                return new PyTuple(new PyInt(0), new PyInt(0), new PyInt(0));
            });
            module.ModuleDict["get_threshold"] = new PyBuiltinFunction("get_threshold", a =>
            {
                NoArgs(a, "get_threshold");
                return new PyTuple(
                    new PyInt(_threshold0), new PyInt(_threshold1), new PyInt(_threshold2));
            });
            module.ModuleDict["set_threshold"] = new PyBuiltinFunction("set_threshold", a =>
            {
                // Measured refusal: set_threshold() takes at most 3 arguments (4 given)
                if (a.Length > Generations)
                    throw PyTypeError.Create(
                        $"set_threshold() takes at most {Generations} arguments ({a.Length} given)");
                if (a.Length == 0)
                    throw PyTypeError.Create(
                        $"set_threshold() takes at least 1 argument ({a.Length} given)");
                _threshold0 = (long)Index(a[0]);
                if (a.Length >= 2) _threshold1 = (long)Index(a[1]);
                if (a.Length >= 3) _threshold2 = (long)Index(a[2]);
                return PyNone.Instance;
            });

            module.ModuleDict["collect"] = new PyBuiltinFunction("collect", a =>
            {
                // collect($module, /, generation=2); measured refusals:
                //   collect('x') -> TypeError: 'str' object cannot be interpreted as an integer
                //   collect(3)   -> ValueError: invalid generation
                int generation = 2;
                if (a.Length > 1)
                    throw PyTypeError.Create($"collect() takes at most 1 argument ({a.Length} given)");
                if (a.Length == 1)
                {
                    generation = (int)Index(a[0]);
                    if (generation < 0 || generation >= Generations)
                        throw PyValueError.Create("invalid generation");
                }
                // CPython's order, and the reason for it: Modules/gcmodule.c:1425 invokes the
                // "start" callbacks, the collection runs, :1427 invokes "stop" with the counts. A
                // callback therefore never observes a pass that already finished, and the finalizers
                // of the objects that pass freed happen between the two.
                InvokeCallbacks("start", generation);
                // The deterministic anchor. This is a real collection: everything the CLR found
                // unreachable gets its __del__ before gc.collect() returns, and rounds repeat while
                // a finalizer keeps dropping more objects.
                PyFinalizer.CollectAndDrain();
                // Files are reconciled in the same breath: CPython's collect closes them
                // (Modules/_io/iobase.c:298), and the bus no longer forces a collection of its own
                // because this pass already made one.
                PyResourceBus.ReleaseUnreferenced(forceCollection: false);
                InvokeCallbacks("stop", generation);
                return new PyInt(0);
            });

            module.ModuleDict["get_objects"] = new PyBuiltinFunction("get_objects", a =>
            {
                // Measured refusals:
                //   get_objects('x') -> TypeError: argument should be integer or None, not 'str'
                //   get_objects(5)   -> ValueError: generation parameter must be less than the
                //                       number of available generations (3)
                if (a.Length > 1)
                    throw PyTypeError.Create($"get_objects() takes at most 1 argument ({a.Length} given)");
                if (a.Length == 1 && !(a[0] is PyNone))
                {
                    var integer = PyObject.TryGetIndex(a[0]);
                    if (integer == null)
                        throw PyTypeError.Create(
                            $"argument should be integer or None, not '{a[0].GetTypeName()}'");
                    if (integer.Value >= Generations)
                        throw PyValueError.Create(
                            $"generation parameter must be less than the number of available " +
                            $"generations ({Generations})");
                }
                // Empty, and registered as a difference: SharpPy tracks no objects, so there is no
                // list of live objects to hand back.
                return new PyList();
            });

            module.ModuleDict["is_tracked"] = new PyBuiltinFunction("is_tracked", a =>
            {
                ExactlyOne(a, "is_tracked");
                return IsTracked(a[0]) ? PyBool.True : PyBool.False;
            });
            module.ModuleDict["is_finalized"] = new PyBuiltinFunction("is_finalized", a =>
            {
                ExactlyOne(a, "is_finalized");
                return PyFinalizer.IsFinalized(a[0]) ? PyBool.True : PyBool.False;
            });

            module.ModuleDict["get_referents"] = new PyBuiltinFunction("get_referents", a =>
            {
                var result = new List<PyObject>();
                foreach (var obj in a) ReferentsOf(obj, result);
                return new PyList(result);
            });
            module.ModuleDict["get_referrers"] = new PyBuiltinFunction("get_referrers", a =>
            {
                // Reverse edges need CPython's tracked-object list; there is none. Empty list, and
                // registered: a program that hunts for referrers finds nothing rather than an error.
                return new PyList();
            });

            module.ModuleDict["get_stats"] = new PyBuiltinFunction("get_stats", a =>
            {
                NoArgs(a, "get_stats");
                var stats = new List<PyObject>();
                for (int i = 0; i < Generations; i++)
                {
                    var entry = new PyDict();
                    entry.SetItem(new PyString("collected"), new PyInt(0));
                    entry.SetItem(new PyString("collections"), new PyInt(0));
                    entry.SetItem(new PyString("uncollectable"), new PyInt(0));
                    stats.Add(entry);
                }
                return new PyList(stats);
            });

            module.ModuleDict["freeze"] = new PyBuiltinFunction("freeze", a =>
            {
                NoArgs(a, "freeze");
                return PyNone.Instance;
            });
            module.ModuleDict["unfreeze"] = new PyBuiltinFunction("unfreeze", a =>
            {
                NoArgs(a, "unfreeze");
                return PyNone.Instance;
            });
            module.ModuleDict["get_freeze_count"] = new PyBuiltinFunction("get_freeze_count", a =>
            {
                NoArgs(a, "get_freeze_count");
                return new PyInt(0);
            });

            module.ModuleDict["garbage"] = Garbage;
            module.ModuleDict["callbacks"] = Callbacks;

            // Measured flag values in CPython 3.12.15 (gc.DEBUG_*).
            module.ModuleDict["DEBUG_STATS"] = new PyInt(1);
            module.ModuleDict["DEBUG_COLLECTABLE"] = new PyInt(2);
            module.ModuleDict["DEBUG_UNCOLLECTABLE"] = new PyInt(4);
            module.ModuleDict["DEBUG_SAVEALL"] = new PyInt(32);
            module.ModuleDict["DEBUG_LEAK"] = new PyInt(38);

            return module;
        }

        // Measured: the clinic-generated refusals name the module, gc.freeze() takes no arguments
        // (1 given), while the two functions that take an optional argument do not, collect()
        // takes at most 1 argument (2 given).
        private static void NoArgs(PyObject[] args, string name)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"gc.{name}() takes no arguments ({args.Length} given)");
        }

        private static void ExactlyOne(PyObject[] args, string name)
        {
            if (args.Length != 1)
                throw PyTypeError.Create(
                    $"gc.{name}() takes exactly one argument ({args.Length} given)");
        }

        // Measured refusal: '<type>' object cannot be interpreted as an integer
        private static BigInteger Index(PyObject value)
        {
            var integer = PyObject.TryGetIndex(value);
            if (integer == null)
                throw PyTypeError.Create(
                    $"'{value.GetTypeName()}' object cannot be interpreted as an integer");
            return integer.Value;
        }

        /// <summary>
        /// CPython's rule, from the measurements on the right: an object is tracked when its type
        /// can take part in a cycle, and a tuple or dict drops out of the tracked set while every
        /// item in it is itself untracked.
        ///
        ///   list, set, frozenset                     True
        ///   (1,) True   () False                     tuple: tracked unless empty
        ///   {} False  {'a':1} False  {'a':[]} True    dict: tracked when an item is tracked
        ///   bytearray() False  'a' b'a' 1.5 None False
        ///   int False   type('Y',(),{}) True          builtin type untracked, user class tracked
        ///   user instance, Python function, len, module   True
        /// </summary>
        private static bool IsTracked(PyObject obj)
        {
            switch (obj)
            {
                case PyList _:
                case PySet _:
                case PyFrozenSet _:
                    return true;
                case PyTuple tuple:
                    // A tuple leaves the tracked set once nothing inside it can close a cycle, so
                    // (1,) answers False in a settled process while ([],) answers True.
                    foreach (var item in tuple.Items)
                        if (IsTracked(item)) return true;
                    return false;
                case PyDict dict:
                    if (dict.InternalDict == null) return false;
                    foreach (var pair in dict.InternalDict)
                        if (IsTracked(pair.Key) || IsTracked(pair.Value)) return true;
                    return false;
                case PyClass cls:
                    return !SlotDefs.IsBuiltinType(cls);
                case PyClassInstance _:
                case PyFunction _:
                case PyBuiltinFunction _:
                case PyModule _:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Measured referent shapes: get_referents({'a': 1}) is [1] (values, not keys),
        /// get_referents((1, 2)) is [2, 1] (reverse visit order), an instance refers to its class,
        /// and an int refers to nothing.
        /// </summary>
        private static void ReferentsOf(PyObject obj, List<PyObject> into)
        {
            switch (obj)
            {
                case PyList list:
                    // Measured: get_referents([3, 4]) is [4, 3]. A list is traversed backwards.
                    for (int i = list.Length() - 1; i >= 0; i--) into.Add(list.GetItem(new PyInt(i)));
                    break;
                case PyDict dict:
                    if (dict.InternalDict != null)
                        foreach (var value in dict.InternalDict.Values) into.Add(value);
                    break;
                case PyTuple tuple:
                    for (int i = tuple.Items.Length - 1; i >= 0; i--) into.Add(tuple.Items[i]);
                    break;
                case PySet set:
                    foreach (var item in PySet.IterableItems(set)) into.Add(item);
                    break;
                case PyClass cls:
                    // A class traverses its bases, its mro and its dict; the bases are the part a
                    // probe can check without depending on interpreter internals.
                    if (cls._cachedBasesTuple != null) into.AddRange(cls._cachedBasesTuple.Items);
                    break;
                case PyClassInstance instance:
                    // Measured order for an instance holding one attribute: dict values first, then
                    // the class (referents instance -> ['int', 'type']).
                    if (instance.InstanceDict != null)
                        foreach (var value in instance.InstanceDict.Values) into.Add(value);
                    if (instance.InstanceType != null) into.Add(instance.InstanceType);
                    break;
                default:
                    break;
            }
        }

        // Measured: a gc callback is called with (phase, info) where info is a dict with the keys
        // 'collected', 'generation' and 'uncollectable' on both the start and the stop call.
        private static void InvokeCallbacks(string phase, int generation)
        {
            if (Callbacks.Length() == 0) return;
            var snapshot = new PyObject[Callbacks.Length()];
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i] = Callbacks.GetItem(new PyInt(i));
            var info = new PyDict();
            info.SetItem(new PyString("collected"), new PyInt(0));
            info.SetItem(new PyString("generation"), new PyInt(generation));
            info.SetItem(new PyString("uncollectable"), new PyInt(0));
            foreach (var callback in snapshot)
            {
                if (callback == null || callback is PyNone) continue;
                try
                {
                    callback.Call(new PyObject[] { new PyString(phase), info }, null);
                }
                catch (PythonException)
                {
                    // CPython does not report an exception raised by a gc callback from collect();
                    // matching that keeps a failing hook from replacing the program's result.
                }
            }
        }
    }
}
