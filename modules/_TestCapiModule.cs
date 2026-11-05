using System;
using System.Numerics;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_testcapimodule.c: the C API reached from Python for CPython's own tests. Only the
    /// parts SharpPy has the C function for are here: the limits of the C types, the pytime
    /// conversions (Modules/_testcapi/pytime.c) and PySequence_GetItem and friends
    /// (Modules/_testcapi/abstract.c). A test that needs anything else finds it missing.
    /// </summary>
    internal static class _TestCapiModule
    {
        private static readonly bool Windows = OperatingSystem.IsWindows();

        internal static PyModule Create()
        {
            var module = new PyModule("_testcapi");
            var d = module.ModuleDict;
            // PyInit__testcapi: the limits of <limits.h> and <float.h>. A C long is 32 bits on Windows.
            d["CHAR_MAX"] = new PyInt(127);
            d["CHAR_MIN"] = new PyInt(-128);
            d["UCHAR_MAX"] = new PyInt(255);
            d["SHRT_MAX"] = new PyInt(short.MaxValue);
            d["SHRT_MIN"] = new PyInt(short.MinValue);
            d["USHRT_MAX"] = new PyInt(ushort.MaxValue);
            d["INT_MAX"] = new PyInt(int.MaxValue);
            d["INT_MIN"] = new PyInt(int.MinValue);
            d["UINT_MAX"] = new PyInt(uint.MaxValue);
            d["LONG_MAX"] = new PyInt(Windows ? int.MaxValue : long.MaxValue);
            d["LONG_MIN"] = new PyInt(Windows ? int.MinValue : long.MinValue);
            d["ULONG_MAX"] = new PyInt(Windows ? (BigInteger)uint.MaxValue : ulong.MaxValue);
            d["LLONG_MAX"] = new PyInt(long.MaxValue);
            d["LLONG_MIN"] = new PyInt(long.MinValue);
            d["ULLONG_MAX"] = new PyInt(ulong.MaxValue);
            d["PY_SSIZE_T_MAX"] = new PyInt(long.MaxValue);
            d["PY_SSIZE_T_MIN"] = new PyInt(long.MinValue);
            d["SIZE_MAX"] = new PyInt(ulong.MaxValue);
            d["FLT_MAX"] = new PyFloat(float.MaxValue);
            d["FLT_MIN"] = new PyFloat(1.1754943508222875e-38);
            d["DBL_MAX"] = new PyFloat(double.MaxValue);
            d["DBL_MIN"] = new PyFloat(2.2250738585072014e-308);
            d["SIZEOF_VOID_P"] = new PyInt(IntPtr.Size);
            d["SIZEOF_TIME_T"] = new PyInt(8);
            d["SIZEOF_WCHAR_T"] = new PyInt(Windows ? 2 : 4);
            d["SIZEOF_PID_T"] = new PyInt(4);

            Add(d, "PyTime_FromSeconds", args =>
            {
                Parse("PyTime_FromSeconds", args, 1);
                return PyTime.AsNanosecondsObject(PyTime.FromSeconds(PyArgs.ParseInt(args[0])));
            });
            Add(d, "PyTime_FromSecondsObject", args =>
            {
                Parse("PyTime_FromSecondsObject", args, 2);
                var round = Rounding(args[1]);
                return PyTime.AsNanosecondsObject(PyTime.FromSecondsObject(args[0], round));
            });
            Add(d, "PyTime_AsSecondsDouble", args =>
            {
                Parse("PyTime_AsSecondsDouble", args, 1);
                return new PyFloat(PyTime.AsSecondsDouble(PyTime.FromNanosecondsObject(args[0])));
            });
            Add(d, "PyTime_AsTimeval", args =>
            {
                Parse("PyTime_AsTimeval", args, 2);
                var round = Rounding(args[1]);
                var (sec, usec) = PyTime.AsTimeval(PyTime.FromNanosecondsObject(args[0]), round);
                return Pair(sec, usec);
            });
            Add(d, "PyTime_AsTimeval_clamp", args =>
            {
                Parse("PyTime_AsTimeval_clamp", args, 2);
                var round = Rounding(args[1]);
                var (sec, usec) = PyTime.AsTimevalClamp(PyTime.FromNanosecondsObject(args[0]), round);
                return Pair(sec, usec);
            });
            if (!Windows)
            {
                // HAVE_CLOCK_GETTIME
                Add(d, "PyTime_AsTimespec", args =>
                {
                    Parse("PyTime_AsTimespec", args, 1);
                    var (sec, nsec) = PyTime.AsTimespec(PyTime.FromNanosecondsObject(args[0]), true);
                    return Pair(sec, nsec);
                });
                Add(d, "PyTime_AsTimespec_clamp", args =>
                {
                    Parse("PyTime_AsTimespec_clamp", args, 1);
                    var (sec, nsec) = PyTime.AsTimespec(PyTime.FromNanosecondsObject(args[0]), false);
                    return Pair(sec, nsec);
                });
            }
            Add(d, "PyTime_AsMilliseconds", args =>
            {
                Parse("PyTime_AsMilliseconds", args, 2);
                long t = PyTime.FromNanosecondsObject(args[0]);
                var round = Rounding(args[1]);
                return PyTime.AsNanosecondsObject(PyTime.FromNanoseconds(PyTime.AsMilliseconds(t, round)));
            });
            Add(d, "PyTime_AsMicroseconds", args =>
            {
                Parse("PyTime_AsMicroseconds", args, 2);
                long t = PyTime.FromNanosecondsObject(args[0]);
                var round = Rounding(args[1]);
                return PyTime.AsNanosecondsObject(PyTime.FromNanoseconds(PyTime.AsMicroseconds(t, round)));
            });
            Add(d, "pytime_object_to_time_t", args =>
            {
                Parse("pytime_object_to_time_t", args, 2);
                var round = Rounding(args[1]);
                return new PyInt(PyTime.ObjectToTimeT(args[0], round));
            });
            Add(d, "pytime_object_to_timeval", args =>
            {
                Parse("pytime_object_to_timeval", args, 2);
                var round = Rounding(args[1]);
                var (sec, usec) = PyTime.ObjectToTimeval(args[0], round);
                return Pair(sec, usec);
            });
            Add(d, "pytime_object_to_timespec", args =>
            {
                Parse("pytime_object_to_timespec", args, 2);
                var round = Rounding(args[1]);
                var (sec, nsec) = PyTime.ObjectToTimespec(args[0], round);
                return Pair(sec, nsec);
            });

            // abstract.c: NULLABLE turns None into NULL.
            Add(d, "sequence_getitem", args =>
            {
                Parse("sequence_getitem", args, 2);
                return PyAbstract.SequenceGetItem(Nullable(args[0]), PyArgs.Ssize(args[1]));
            });
            Add(d, "sequence_setitem", args =>
            {
                Parse("sequence_setitem", args, 3);
                long i = PyArgs.Ssize(args[1]);
                PyAbstract.SequenceSetItem(Nullable(args[0]), i, Nullable(args[2]));
                return new PyInt(0);
            });
            Add(d, "sequence_delitem", args =>
            {
                Parse("sequence_delitem", args, 2);
                PyAbstract.SequenceDelItem(Nullable(args[0]), PyArgs.Ssize(args[1]));
                return new PyInt(0);
            });

            // The meth_* functions: a module-level one has the module as its self.
            foreach (var name in MethNames)
            {
                var kind = name;
                d[kind] = new PyBuiltinFunction(kind, (args, kwargs) => Meth(kind, "_testcapi." + kind, module, args, kwargs));
            }
            d["MethInstance"] = MethType("MethInstance", "Class with normal (instance) methods to test calling conventions",
                (type, kind) => new PyMethodDescriptor(kind, type,
                    (self, args, kwargs) => Meth(kind, type.Name + "." + kind, self, args, kwargs),
                    minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true));
            d["MethClass"] = MethType("MethClass", "Class with class methods to test calling conventions",
                (type, kind) => new PyClassMethodDescriptor(kind, type, new PyBuiltinFunction(kind,
                    (args, kwargs) => Meth(kind, type.Name + "." + kind, args[0], args[1..], kwargs))));
            d["MethStatic"] = MethType("MethStatic", "Class with static methods to test calling conventions",
                (type, kind) => PyType.StaticMethodType.Call(new PyObject[] { new PyBuiltinFunction(kind,
                    (args, kwargs) => Meth(kind, kind, null, args, kwargs)) }, null));

            // Modules/_testcapi/vectorcall.c
            Add(d, "pyobject_fastcall", args =>
            {
                Parse("pyobject_fastcall", args, 2);
                return args[0].Call(FastcallArgs(args[1]), null);
            });
            Add(d, "pyobject_fastcalldict", args =>
            {
                Parse("pyobject_fastcalldict", args, 3);
                var stack = FastcallArgs(args[1]);
                PyDict kwargs = args[2] is PyNone ? null
                    : args[2] as PyDict ?? throw PyTypeError.Create("kwnames must be None or a dict");
                return args[0].Call(stack, kwargs);
            });
            Add(d, "pyobject_vectorcall", args =>
            {
                Parse("pyobject_vectorcall", args, 3);
                var stack = FastcallArgs(args[1]);
                if (args[2] is PyNone)
                    return args[0].Call(stack, null);
                if (args[2] is not PyTuple kwnames)
                    throw PyTypeError.Create("kwnames must be None or a tuple");
                int nkw = kwnames.Items.Length;
                if (stack.Length < nkw)
                    throw PyValueError.Create("kwnames longer than args");
                int nargs = stack.Length - nkw;
                var kwargs = new PyDict();
                for (int i = 0; i < nkw; i++)
                    kwargs.SetItem(kwnames.Items[i], stack[nargs + i]);
                return args[0].Call(stack[..nargs], kwargs);
            });
            return module;
        }

        private static readonly string[] MethNames =
        {
            "meth_varargs", "meth_varargs_keywords", "meth_o", "meth_noargs", "meth_fastcall", "meth_fastcall_keywords",
        };

        /// <summary>
        /// The C calling conventions under test: each meth_* returns the C-level arguments it got, None
        /// for a NULL self or kwargs. funcstr is what _PyObject_FunctionStr names the callable by.
        /// </summary>
        private static PyObject Meth(string kind, string funcstr, PyObject self, PyObject[] args, PyDict kwargs)
        {
            self ??= PyNone.Instance;
            bool keywords = kwargs != null && kwargs.Length() != 0;
            switch (kind)
            {
                case "meth_varargs":
                    // cfunction_call: the ml_name alone.
                    if (keywords)
                        throw PyTypeError.Create($"{kind}() takes no keyword arguments");
                    return new PyTuple(new[] { self, new PyTuple(args) });
                case "meth_varargs_keywords":
                    return new PyTuple(new[] { self, new PyTuple(args), keywords ? kwargs.Copy() : PyNone.Instance });
                case "meth_o":
                    PyArgs.One(funcstr, args, kwargs);
                    return new PyTuple(new[] { self, args[0] });
                case "meth_noargs":
                    PyArgs.NoArgs(funcstr, args, kwargs);
                    return self;
                case "meth_fastcall":
                    PyArgs.NoKeywords(funcstr, kwargs);
                    return new PyTuple(new[] { self, new PyTuple(args) });
                default:
                    // meth_fastcall_keywords: PyObject_Vectorcall(dict, ..., kwnames) makes the dict.
                    return new PyTuple(new[] { self, new PyTuple(args), keywords ? kwargs.Copy() : new PyDict() });
            }
        }

        // A static type with PyType_GenericNew and one method of each convention.
        private static PyType MethType(string name, string doc, Func<PyType, string, PyObject> method)
        {
            var type = new PyType(name, new[] { PyType.ObjectType }) { ConstructedByNewSlot = true };
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => new MethObject((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            foreach (var kind in MethNames)
                type.TypeDict[kind] = method(type, kind);
            type.TypeDict["__doc__"] = new PyString(doc);
            return type;
        }

        private sealed class MethObject : PyObject
        {
            internal MethObject(PyType type) => SetCustomType(type);
        }

        // fastcall_args
        private static PyObject[] FastcallArgs(PyObject args) => args switch
        {
            PyNone => Array.Empty<PyObject>(),
            PyTuple tuple => tuple.Items,
            _ => throw PyTypeError.Create("args must be None or a tuple"),
        };

        private static void Add(System.Collections.Generic.IDictionary<string, PyObject> d, string name, Func<PyObject[], PyObject> body) =>
            d[name] = new PyBuiltinFunction(name, (args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{name}() takes no keyword arguments");
                return body(args);
            });

        // PyArg_ParseTuple's count for a format of n items, named or not.
        private static void Parse(string name, PyObject[] args, int count)
        {
            if (args.Length != count)
                throw PyTypeError.Create(count == 1
                    ? $"function takes exactly 1 argument ({args.Length} given)"
                    : $"function takes exactly {count} arguments ({args.Length} given)");
        }

        private static PyObject Nullable(PyObject value) => value is PyNone ? null : value;

        // check_time_rounding
        private static PyTimeRound Rounding(PyObject value)
        {
            int round = PyArgs.ParseInt(value);
            if (round < 0 || round > 3)
                throw PyValueError.Create("invalid rounding");
            return (PyTimeRound)round;
        }

        // Py_BuildValue("Nl", ...)
        private static PyObject Pair(long first, long second) => new PyTuple(new PyObject[] { new PyInt(first), new PyInt(second) });
    }
}
