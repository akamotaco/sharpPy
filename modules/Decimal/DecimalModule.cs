// Modules/_decimal/_decimal.c (CPython 3.12.15), part 1: the module state, the signal and
// condition maps, the SignalDict object, Context part 1 (attributes, new, init, repr, copy,
// reduce), the thread local (contextvar) contexts, the context manager and PyInit__decimal.
// Configuration: CONFIG_64, WITH_DECIMAL_CONTEXTVAR, no EXTRA_FUNCTIONALITY (as the reference build).
using System;
using System.Collections.Generic;
using System.Text;
using SharpPy.Modules.Mpdecimal;
using static SharpPy.Modules.Mpdecimal.Mpd;

namespace SharpPy.Modules.CDecimal
{
    internal static partial class DecimalModule
    {
        // #define MPD_Float_operation MPD_Not_implemented
        internal const uint MPD_Float_operation = MPD_Not_implemented;

        /* _Py_DEC_MINALLOC >= MPD_MINALLOC */
        internal const int _Py_DEC_MINALLOC = 4;

        internal static PyType PyDec_Type;
        internal static PyType PyDecSignalDict_Type;
        internal static PyType PyDecContext_Type;
        internal static PyType PyDecContextManager_Type;
        internal static PyType PyDecSignalDictMixin_Type;

        // PyDec_Check, PyDecContext_Check, PyDecSignalDict_Check
        internal static bool PyDec_Check(PyObject v) => v is PyDecObject;
        internal static bool PyDec_CheckExact(PyObject v) => ReferenceEquals(v.GetPyType(), PyDec_Type);
        internal static bool PyDecContext_Check(PyObject v) => v is PyDecContextObject;
        internal static bool PyDecSignalDict_Check(PyObject v) => ReferenceEquals(v.GetPyType(), PyDecSignalDict_Type);

        // MPD(v), CTX(v), CtxCaps(v)
        internal static mpd_t MPD(PyObject v) => ((PyDecObject)v).dec;
        internal static ref mpd_context_t CTX(PyObject v) => ref ((PyDecContextObject)v).ctx;
        internal static ref int CtxCaps(PyObject v) => ref ((PyDecContextObject)v).capitals;

        // SdFlagAddr(v) == NULL, and *SdFlagAddr(v): the traps or the status of the owning context.
        internal static bool SdFlagAddrIsNull(PyObject v) => ((PyDecSignalDictObject)v).flagsOwner == null;
        internal static ref uint SdFlags(PyObject v)
        {
            var sd = (PyDecSignalDictObject)v;
            if (sd.flagsAreTraps)
                return ref sd.flagsOwner.ctx.traps;
            return ref sd.flagsOwner.ctx.status;
        }

        // incr_true, incr_false
        private static PyObject incr_true() => PyBool.True;
        private static PyObject incr_false() => PyBool.False;

        // WITH_DECIMAL_CONTEXTVAR: static PyObject *current_context_var
        private static PyObject current_context_var;

        /* Template for creating new thread contexts, calling Context() without
         * arguments and initializing the module_context on first access. */
        internal static PyObject default_context_template;
        /* Basic and extended context templates */
        private static PyObject basic_context_template;
        private static PyObject extended_context_template;

        /* Invariant: NULL or pointer to _pydecimal.Decimal */
        private static PyObject PyDecimal;

        /* Error codes for functions that return signals or conditions */
        private const uint DEC_INVALID_SIGNALS = MPD_Max_status + 1U;
        private const uint DEC_ERR_OCCURRED = DEC_INVALID_SIGNALS << 1;
        private const uint DEC_ERRORS = DEC_INVALID_SIGNALS | DEC_ERR_OCCURRED;

        // DecCondMap
        private sealed class DecCondMap
        {
            public readonly string name;   /* condition or signal name */
            public readonly string fqname; /* fully qualified name */
            public readonly uint flag;     /* libmpdec flag */
            public PyObject ex;            /* corresponding exception */

            public DecCondMap(string name, string fqname, uint flag)
            {
                this.name = name;
                this.fqname = fqname;
                this.flag = flag;
            }
        }

        /* Top level Exception; inherits from ArithmeticError */
        private static PyObject DecimalException;

        /* Exceptions that correspond to IEEE signals */
        private const int SUBNORMAL = 5;
        private const int INEXACT = 6;
        private const int ROUNDED = 7;
        private const int SIGNAL_MAP_LEN = 9;
        private static readonly DecCondMap[] signal_map =
        {
            new("InvalidOperation", "decimal.InvalidOperation", MPD_IEEE_Invalid_operation),
            new("FloatOperation", "decimal.FloatOperation", MPD_Float_operation),
            new("DivisionByZero", "decimal.DivisionByZero", MPD_Division_by_zero),
            new("Overflow", "decimal.Overflow", MPD_Overflow),
            new("Underflow", "decimal.Underflow", MPD_Underflow),
            new("Subnormal", "decimal.Subnormal", MPD_Subnormal),
            new("Inexact", "decimal.Inexact", MPD_Inexact),
            new("Rounded", "decimal.Rounded", MPD_Rounded),
            new("Clamped", "decimal.Clamped", MPD_Clamped),
        };

        /* Exceptions that inherit from InvalidOperation */
        private static readonly DecCondMap[] cond_map =
        {
            new("InvalidOperation", "decimal.InvalidOperation", MPD_Invalid_operation),
            new("ConversionSyntax", "decimal.ConversionSyntax", MPD_Conversion_syntax),
            new("DivisionImpossible", "decimal.DivisionImpossible", MPD_Division_impossible),
            new("DivisionUndefined", "decimal.DivisionUndefined", MPD_Division_undefined),
            new("InvalidContext", "decimal.InvalidContext", MPD_Invalid_context),
        };

        private static readonly string[] dec_signal_string =
        {
            "Clamped",
            "InvalidOperation",
            "DivisionByZero",
            "InvalidOperation",
            "InvalidOperation",
            "InvalidOperation",
            "Inexact",
            "InvalidOperation",
            "InvalidOperation",
            "InvalidOperation",
            "FloatOperation",
            "Overflow",
            "Rounded",
            "Subnormal",
            "Underflow",
        };

        // #define _PY_DEC_ROUND_GUARD (MPD_ROUND_GUARD-1)
        private const int _PY_DEC_ROUND_GUARD = MPD_ROUND_GUARD - 1;
        private static readonly PyObject[] round_map = new PyObject[_PY_DEC_ROUND_GUARD];

        private const string invalid_rounding_err =
            "valid values for rounding are:\n" +
            "  [ROUND_CEILING, ROUND_FLOOR, ROUND_UP, ROUND_DOWN,\n" +
            "   ROUND_HALF_UP, ROUND_HALF_DOWN, ROUND_HALF_EVEN,\n" +
            "   ROUND_05UP]";

        private const string invalid_signals_err =
            "valid values for signals are:\n" +
            "  [InvalidOperation, FloatOperation, DivisionByZero,\n" +
            "   Overflow, Underflow, Subnormal, Inexact, Rounded,\n" +
            "   Clamped]";

        // value_error_int / value_error_ptr / type_error_int / runtime_error_int / runtime_error_ptr:
        // the exception the C function sets before it returns -1 or NULL.
        private static Exception value_error(string mesg) => PyValueError.Create(mesg);
        private static Exception type_error(string mesg) => PyTypeError.Create(mesg);
        private static Exception runtime_error(string mesg) => PyRuntimeError.Create(mesg);

        // INTERNAL_ERROR_INT(funcname), INTERNAL_ERROR_PTR(funcname)
        private static Exception INTERNAL_ERROR(string funcname) => runtime_error("internal error in " + funcname);

        // PyErr_NoMemory
        private static Exception PyErr_NoMemory() => PyMemoryError.Create();

        // dec_traphandler: libmpdec's signalling functions are not used.
        private static void dec_traphandler(ref mpd_context_t ctx) /* GCOV_NOT_REACHED */
        {
        }

        // flags_as_exception
        private static PyObject flags_as_exception(uint flags)
        {
            foreach (var cm in signal_map)
            {
                if ((flags & cm.flag) != 0)
                    return cm.ex;
            }

            throw INTERNAL_ERROR("flags_as_exception"); /* GCOV_NOT_REACHED */
        }

        // exception_as_flag: DEC_INVALID_SIGNALS with the KeyError raised (the C sets it and
        // returns; the callers return at once).
        private static uint exception_as_flag(PyObject ex)
        {
            foreach (var cm in signal_map)
            {
                if (ReferenceEquals(cm.ex, ex))
                    return cm.flag;
            }

            throw new PythonException(new PyKeyError(PyString.From(invalid_signals_err)));
        }

        // flags_as_list
        private static PyObject flags_as_list(uint flags)
        {
            var list = new PyList();

            foreach (var cm in cond_map)
            {
                if ((flags & cm.flag) != 0)
                    list.Append(cm.ex);
            }
            for (int i = 1; i < signal_map.Length; i++)
            {
                var cm = signal_map[i];
                if ((flags & cm.flag) != 0)
                    list.Append(cm.ex);
            }

            return list;
        }

        // signals_as_list
        private static PyObject signals_as_list(uint flags)
        {
            var list = new PyList();

            foreach (var cm in signal_map)
            {
                if ((flags & cm.flag) != 0)
                    list.Append(cm.ex);
            }

            return list;
        }

        // list_as_flags
        private static uint list_as_flags(PyObject list)
        {
            var items = ((PyList)list).Items;
            uint flags = 0;
            for (int j = 0; j < items.Length; j++)
            {
                uint x = exception_as_flag(items[j]);
                flags |= x;
            }

            return flags;
        }

        // flags_as_dict
        private static PyObject flags_as_dict(uint flags)
        {
            var dict = new PyDict();

            foreach (var cm in signal_map)
            {
                PyObject b = (flags & cm.flag) != 0 ? PyBool.True : PyBool.False;
                dict.SetItem(cm.ex, b);
            }

            return dict;
        }

        // dict_as_flags: the errors are raised; DEC_INVALID_SIGNALS is told apart by the caller
        // through KeyError/TypeError raised here as InvalidSignals.
        private sealed class InvalidSignals : Exception
        {
            public readonly Exception Error;
            public InvalidSignals(Exception error) { Error = error; }
        }

        private static uint dict_as_flags(PyObject val)
        {
            uint flags = 0;

            if (val is not PyDict dict)
                throw new InvalidSignals(PyTypeError.Create("argument must be a signal dict"));

            if (dict.Length() != SIGNAL_MAP_LEN)
                throw new InvalidSignals(new PythonException(new PyKeyError(PyString.From("invalid signal dict"))));

            foreach (var cm in signal_map)
            {
                // PyDict_GetItemWithError: an error of the key's __eq__ propagates (DEC_ERR_OCCURRED).
                if (!dict.TryGetStoredValue(cm.ex, out var b))
                    throw new InvalidSignals(new PythonException(new PyKeyError(PyString.From("invalid signal dict"))));

                bool x = b.IsTrue();
                if (x)
                    flags |= cm.flag;
            }

            return flags;
        }

        // dict_as_flags for the callers that raise whatever it reports.
        private static uint dict_as_flags_raise(PyObject val)
        {
            try
            {
                return dict_as_flags(val);
            }
            catch (InvalidSignals invalid)
            {
                throw invalid.Error;
            }
        }

        // dec_addstatus: true when an exception was raised (thrown here).
        internal static bool dec_addstatus(PyObject context, uint status)
        {
            ref mpd_context_t ctx = ref CTX(context);

            ctx.status |= status;
            if ((status & (ctx.traps | MPD_Malloc_error)) != 0)
            {
                if ((status & MPD_Malloc_error) != 0)
                    throw PyErr_NoMemory();

                var ex = flags_as_exception(ctx.traps & status);
                var siglist = flags_as_list(ctx.traps & status);

                // PyErr_SetObject(ex, siglist): ex(siglist).
                throw new PythonException((PyBaseException)ex.Call(new[] { siglist }, null));
            }
            return false;
        }

        // getround
        private static int getround(PyObject v)
        {
            if (v is PyString s)
            {
                for (int i = 0; i < _PY_DEC_ROUND_GUARD; i++)
                {
                    if (ReferenceEquals(v, round_map[i]))
                        return i;
                }
                for (int i = 0; i < _PY_DEC_ROUND_GUARD; i++)
                {
                    if (s.Value == ((PyString)round_map[i]).Value)
                        return i;
                }
            }

            throw type_error(invalid_rounding_err);
        }


        /******************************************************************************/
        /*                            SignalDict Object                               */
        /******************************************************************************/

        /* The SignalDict is a MutableMapping that provides access to the
           mpd_context_t flags, which reside in the context object. When a
           new context is created, context.traps and context.flags are
           initialized to new SignalDicts. Once a SignalDict is tied to
           a context, it cannot be deleted. */

        private const string INVALID_SIGNALDICT_ERROR_MSG = "invalid signal dict";

        // signaldict_init
        private static void signaldict_init(PyObject self, PyObject[] args, PyDict kwds)
        {
            ((PyDecSignalDictObject)self).flagsOwner = null;
        }

        // signaldict_len
        internal static long signaldict_len(PyObject self)
        {
            if (SdFlagAddrIsNull(self))
                throw value_error(INVALID_SIGNALDICT_ERROR_MSG);
            return SIGNAL_MAP_LEN;
        }

        private static PyObject SignalTuple;

        // signaldict_iter
        internal static PyObject signaldict_iter(PyObject self)
        {
            if (SdFlagAddrIsNull(self))
                throw value_error(INVALID_SIGNALDICT_ERROR_MSG);
            // PyTuple_Type.tp_iter(SignalTuple)
            return SignalTuple.GetIterator();
        }

        // signaldict_getitem
        internal static PyObject signaldict_getitem(PyObject self, PyObject key)
        {
            if (SdFlagAddrIsNull(self))
                throw value_error(INVALID_SIGNALDICT_ERROR_MSG);

            uint flag = exception_as_flag(key);

            return (SdFlags(self) & flag) != 0 ? incr_true() : incr_false();
        }

        // signaldict_setitem
        internal static void signaldict_setitem(PyObject self, PyObject key, PyObject value)
        {
            if (SdFlagAddrIsNull(self))
                throw value_error(INVALID_SIGNALDICT_ERROR_MSG);

            if (value == null)
                throw value_error("signal keys cannot be deleted");

            uint flag = exception_as_flag(key);

            bool x = value.IsTrue();

            if (x)
                SdFlags(self) |= flag;
            else
                SdFlags(self) &= ~flag;
        }

        // signaldict_repr
        internal static PyObject signaldict_repr(PyObject self)
        {
            if (SdFlagAddrIsNull(self))
                throw value_error(INVALID_SIGNALDICT_ERROR_MSG);

            var n = new string[SIGNAL_MAP_LEN]; /* name */
            var b = new string[SIGNAL_MAP_LEN]; /* bool */
            for (int i = 0; i < SIGNAL_MAP_LEN; i++)
            {
                n[i] = signal_map[i].fqname;
                b[i] = (SdFlags(self) & signal_map[i].flag) != 0 ? "True" : "False";
            }
            return PyString.From(
                $"{{<class '{n[0]}'>:{b[0]}, <class '{n[1]}'>:{b[1]}, <class '{n[2]}'>:{b[2]}, " +
                $"<class '{n[3]}'>:{b[3]}, <class '{n[4]}'>:{b[4]}, <class '{n[5]}'>:{b[5]}, " +
                $"<class '{n[6]}'>:{b[6]}, <class '{n[7]}'>:{b[7]}, <class '{n[8]}'>:{b[8]}}}");
        }

        // signaldict_richcompare
        internal static PyObject signaldict_richcompare(PyObject v, PyObject w, PyObject.CompareOp op)
        {
            PyObject res = PyNotImplemented.Instance;

            // SdFlagAddr(w) reads the flags pointer of whatever w is (its struct field when w is
            // not a SignalDict); SharpPy has no such field outside a SignalDict.
            if (SdFlagAddrIsNull(v) || (w is PyDecSignalDictObject && SdFlagAddrIsNull(w)))
                throw value_error(INVALID_SIGNALDICT_ERROR_MSG);

            if (op == PyObject.CompareOp.EQ || op == PyObject.CompareOp.NE)
            {
                if (PyDecSignalDict_Check(w))
                {
                    res = (SdFlags(v) == SdFlags(w)) ^ (op == PyObject.CompareOp.NE) ? PyBool.True : PyBool.False;
                }
                else if (w is PyDict)
                {
                    try
                    {
                        uint flags = dict_as_flags(w);
                        res = (SdFlags(v) == flags) ^ (op == PyObject.CompareOp.NE) ? PyBool.True : PyBool.False;
                    }
                    catch (InvalidSignals)
                    {
                        /* non-comparable: Py_NotImplemented */
                    }
                }
            }

            return res;
        }

        // signaldict_copy
        private static PyObject signaldict_copy(PyObject self)
        {
            if (SdFlagAddrIsNull(self))
                throw value_error(INVALID_SIGNALDICT_ERROR_MSG);
            return flags_as_dict(SdFlags(self));
        }


        /******************************************************************************/
        /*                         Context Object, Part 1                             */
        /******************************************************************************/

        // Dec_CONTEXT_GET_SSIZE(prec), (emax), (emin), (clamp)
        private static PyObject context_getprec(PyObject self) => PyInt.From(mpd_getprec(ref CTX(self)));
        private static PyObject context_getemax(PyObject self) => PyInt.From(mpd_getemax(ref CTX(self)));
        private static PyObject context_getemin(PyObject self) => PyInt.From(mpd_getemin(ref CTX(self)));
        private static PyObject context_getclamp(PyObject self) => PyInt.From(mpd_getclamp(ref CTX(self)));

        // context_getround
        private static PyObject context_getround(PyObject self)
        {
            int i = mpd_getround(ref CTX(self));

            return round_map[i];
        }

        // context_getcapitals
        private static PyObject context_getcapitals(PyObject self) => PyInt.From(CtxCaps(self));

        // context_getetiny
        private static PyObject context_getetiny(PyObject self) => PyInt.From(mpd_etiny(ref CTX(self)));

        // context_getetop
        private static PyObject context_getetop(PyObject self) => PyInt.From(mpd_etop(ref CTX(self)));

        // PyLong_AsSsize_t: the int's value as a Py_ssize_t, TypeError for what is not an int.
        // PyLong_AsSsize_t: an int (no __index__), "an integer is required" otherwise.
        private static long PyLong_AsSsize_t(PyObject value)
        {
            var integer = PyInt.LongValue(value)
                ?? throw PyTypeError.Create("an integer is required");
            return PyArgs.SsizeValue(integer.Value);
        }

        // PyLong_AsLong (C long is 32 bits on Windows)
        private static long PyLong_AsLong(PyObject value)
        {
            var integer = PyInt.LongValue(value)
                ?? throw PyTypeError.Create($"'{value.GetPyType().FullName}' object cannot be interpreted as an integer");
            var v = integer.Value;
            if (v < PyArgs.CLongMin || v > PyArgs.CLongMax)
                throw PyOverflowError.Create("Python int too large to convert to C long");
            return (long)v;
        }

        // context_setprec
        private static void context_setprec(PyObject self, PyObject value)
        {
            long x = PyLong_AsSsize_t(value);

            if (!mpd_qsetprec(ref CTX(self), x))
                throw value_error("valid range for prec is [1, MAX_PREC]");
        }

        // context_setemin
        private static void context_setemin(PyObject self, PyObject value)
        {
            long x = PyLong_AsSsize_t(value);

            if (!mpd_qsetemin(ref CTX(self), x))
                throw value_error("valid range for Emin is [MIN_EMIN, 0]");
        }

        // context_setemax
        private static void context_setemax(PyObject self, PyObject value)
        {
            long x = PyLong_AsSsize_t(value);

            if (!mpd_qsetemax(ref CTX(self), x))
                throw value_error("valid range for Emax is [0, MAX_EMAX]");
        }

        // context_setround
        private static void context_setround(PyObject self, PyObject value)
        {
            int x = getround(value);

            if (!mpd_qsetround(ref CTX(self), x))
                throw INTERNAL_ERROR("context_setround"); /* GCOV_NOT_REACHED */
        }

        // context_setcapitals
        private static void context_setcapitals(PyObject self, PyObject value)
        {
            long x = PyLong_AsSsize_t(value);

            if (x != 0 && x != 1)
                throw value_error("valid values for capitals are 0 or 1");
            CtxCaps(self) = (int)x;
        }

        // context_settraps_list
        private static void context_settraps_list(PyObject self, PyObject value)
        {
            uint flags = list_as_flags(value);

            if (!mpd_qsettraps(ref CTX(self), flags))
                throw INTERNAL_ERROR("context_settraps_list");
        }

        // context_settraps_dict
        private static void context_settraps_dict(PyObject self, PyObject value)
        {
            uint flags;

            if (PyDecSignalDict_Check(value))
                flags = SdFlags(value);
            else
                flags = dict_as_flags_raise(value);

            if (!mpd_qsettraps(ref CTX(self), flags))
                throw INTERNAL_ERROR("context_settraps_dict");
        }

        // context_setstatus_list
        private static void context_setstatus_list(PyObject self, PyObject value)
        {
            uint flags = list_as_flags(value);

            if (!mpd_qsetstatus(ref CTX(self), flags))
                throw INTERNAL_ERROR("context_setstatus_list");
        }

        // context_setstatus_dict
        private static void context_setstatus_dict(PyObject self, PyObject value)
        {
            uint flags;

            if (PyDecSignalDict_Check(value))
                flags = SdFlags(value);
            else
                flags = dict_as_flags_raise(value);

            if (!mpd_qsetstatus(ref CTX(self), flags))
                throw INTERNAL_ERROR("context_setstatus_dict");
        }

        // context_setclamp
        private static void context_setclamp(PyObject self, PyObject value)
        {
            long x = PyLong_AsSsize_t(value);
            // BOUNDS_CHECK(x, INT_MIN, INT_MAX)
            x = (x < int.MinValue || int.MaxValue < x) ? int.MaxValue : x;

            if (!mpd_qsetclamp(ref CTX(self), (int)x))
                throw value_error("valid values for clamp are 0 or 1");
        }

        // context_getattr
        internal static PyObject context_getattr(PyDecContextObject self, PyString name)
        {
            if (name.Value == "traps")
                return self.traps;
            if (name.Value == "flags")
                return self.flags;

            return self.GenericGetAttrBase(name);
        }

        // context_setattr
        internal static void context_setattr(PyDecContextObject self, PyString name, PyObject value)
        {
            if (value == null)
                throw PyAttributeError.Create("context attributes cannot be deleted");

            if (name.Value == "traps")
            {
                context_settraps_dict(self, value);
                return;
            }
            if (name.Value == "flags")
            {
                context_setstatus_dict(self, value);
                return;
            }

            self.GenericSetAttrBase(name, value);
        }

        // context_setattrs
        private static void context_setattrs(PyObject self, PyObject prec, PyObject rounding,
            PyObject emin, PyObject emax, PyObject capitals, PyObject clamp, PyObject status, PyObject traps)
        {
            if (prec is not PyNone)
                context_setprec(self, prec);
            if (rounding is not PyNone)
                context_setround(self, rounding);
            if (emin is not PyNone)
                context_setemin(self, emin);
            if (emax is not PyNone)
                context_setemax(self, emax);
            if (capitals is not PyNone)
                context_setcapitals(self, capitals);
            if (clamp is not PyNone)
                context_setclamp(self, clamp);

            if (traps is not PyNone)
            {
                if (traps is PyList)
                    context_settraps_list(self, traps);
                else
                    context_settraps_dict(self, traps);
            }
            if (status is not PyNone)
            {
                if (status is PyList)
                    context_setstatus_list(self, status);
                else
                    context_setstatus_dict(self, status);
            }
        }

        // context_clear_traps
        private static PyObject context_clear_traps(PyObject self)
        {
            CTX(self).traps = 0;
            return PyNone.Instance;
        }

        // context_clear_flags
        private static PyObject context_clear_flags(PyObject self)
        {
            CTX(self).status = 0;
            return PyNone.Instance;
        }

        private const long DEC_DFLT_EMAX = 999999;
        private const long DEC_DFLT_EMIN = -999999;

        private static readonly mpd_context_t dflt_ctx = new mpd_context_t
        {
            prec = 28, emax = DEC_DFLT_EMAX, emin = DEC_DFLT_EMIN,
            traps = MPD_IEEE_Invalid_operation | MPD_Division_by_zero | MPD_Overflow,
            status = 0, newtrap = 0, round = MPD_ROUND_HALF_EVEN, clamp = 0, allcr = 1,
        };

        // context_new
        private static PyObject context_new(PyType type, PyObject[] args, PyDict kwds)
        {
            var self = new PyDecContextObject(type);

            self.traps = PyDecSignalDict_Type.Call(Array.Empty<PyObject>(), null);
            self.flags = PyDecSignalDict_Type.Call(Array.Empty<PyObject>(), null);

            if (default_context_template != null)
                self.ctx = CTX(default_context_template);
            else
                self.ctx = dflt_ctx;

            ((PyDecSignalDictObject)self.traps).flagsOwner = self;
            ((PyDecSignalDictObject)self.traps).flagsAreTraps = true;
            ((PyDecSignalDictObject)self.flags).flagsOwner = self;
            ((PyDecSignalDictObject)self.flags).flagsAreTraps = false;

            self.capitals = 1;
            self.tstate = null;

            return self;
        }

        private static readonly string[] context_kwlist =
        {
            "prec", "rounding", "Emin", "Emax", "capitals", "clamp", "flags", "traps",
        };

        // context_init
        private static void context_init(PyObject self, PyObject[] args, PyDict kwds)
        {
            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, context_kwlist, 0);
            PyObject Arg(int i) => parsed[i] ?? PyNone.Instance;

            context_setattrs(self, Arg(0), Arg(1), Arg(2), Arg(3), Arg(4), Arg(5), Arg(6), Arg(7));
        }

        // context_repr
        internal static PyObject context_repr(PyDecContextObject self)
        {
            ref mpd_context_t ctx = ref CTX(self);

            int mem = MPD_MAX_SIGNAL_LIST;
            int n = mpd_lsnprint_signals(out string flags, mem, ctx.status, dec_signal_string);
            if (n < 0 || n >= mem)
                throw INTERNAL_ERROR("context_repr");

            n = mpd_lsnprint_signals(out string traps, mem, ctx.traps, dec_signal_string);
            if (n < 0 || n >= mem)
                throw INTERNAL_ERROR("context_repr");

            return PyString.From(
                $"Context(prec={ctx.prec}, rounding={mpd_round_string[ctx.round]}, Emin={ctx.emin}, Emax={ctx.emax}, " +
                $"capitals={self.capitals}, clamp={ctx.clamp}, flags={flags}, traps={traps})");
        }

        // init_basic_context
        private static void init_basic_context(PyObject v)
        {
            mpd_context_t ctx = dflt_ctx;

            ctx.prec = 9;
            ctx.traps |= (MPD_Underflow | MPD_Clamped);
            ctx.round = MPD_ROUND_HALF_UP;

            CTX(v) = ctx;
            CtxCaps(v) = 1;
        }

        // init_extended_context
        private static void init_extended_context(PyObject v)
        {
            mpd_context_t ctx = dflt_ctx;

            ctx.prec = 9;
            ctx.traps = 0;

            CTX(v) = ctx;
            CtxCaps(v) = 1;
        }

        // context_copy
        internal static PyObject context_copy(PyObject self)
        {
            var copy = PyDecContext_Type.Call(Array.Empty<PyObject>(), null);

            CTX(copy) = CTX(self);
            CTX(copy).newtrap = 0;
            CtxCaps(copy) = CtxCaps(self);

            return copy;
        }

        // context_reduce
        private static PyObject context_reduce(PyObject self)
        {
            ref mpd_context_t ctx = ref CTX(self);

            var flags = signals_as_list(ctx.status);
            var traps = signals_as_list(ctx.traps);

            // Py_BuildValue("O(nsnniiOO)", ...)
            return PyTuple.From(self.GetPyType(), PyTuple.From(new PyObject[]
            {
                PyInt.From(ctx.prec), PyString.From(mpd_round_string[ctx.round]), PyInt.From(ctx.emin), PyInt.From(ctx.emax),
                PyInt.From(CtxCaps(self)), PyInt.From(ctx.clamp), flags, traps,
            }));
        }

        // CONTEXT_CHECK(obj)
        private static void CONTEXT_CHECK(PyObject obj)
        {
            if (!PyDecContext_Check(obj))
                throw PyTypeError.Create("argument must be a context");
        }

        // CONTEXT_CHECK_VA(obj)
        private static PyObject CONTEXT_CHECK_VA(PyObject obj)
        {
            if (obj is PyNone)
                return current_context();
            if (!PyDecContext_Check(obj))
                throw PyTypeError.Create("optional argument must be a context");
            return obj;
        }


        /******************************************************************************/
        /*                Global, thread local and temporary contexts                 */
        /******************************************************************************/

        // init_current_context
        private static PyObject init_current_context()
        {
            var tl_context = context_copy(default_context_template);
            CTX(tl_context).status = 0;

            _ContextvarsModule.PyContextVar_Set(current_context_var, tl_context);

            return tl_context;
        }

        // current_context
        internal static PyObject current_context()
        {
            if (_ContextvarsModule.PyContextVar_Get(current_context_var, out var tl_context))
                return tl_context;

            return init_current_context();
        }

        // CURRENT_CONTEXT(ctxobj)
        private static PyObject CURRENT_CONTEXT() => current_context();

        /* Return a new reference to the current context */
        private static PyObject PyDec_GetCurrentContext() => current_context();

        /* Set the thread local context to a new context, decrement old reference */
        private static PyObject PyDec_SetCurrentContext(PyObject v)
        {
            CONTEXT_CHECK(v);

            /* If the new context is one of the templates, make a copy.
             * This is the current behavior of decimal.py. */
            if (ReferenceEquals(v, default_context_template) ||
                ReferenceEquals(v, basic_context_template) ||
                ReferenceEquals(v, extended_context_template))
            {
                v = context_copy(v);
                CTX(v).status = 0;
            }

            _ContextvarsModule.PyContextVar_Set(current_context_var, v);

            return PyNone.Instance;
        }

        private static readonly string[] ctxmanager_kwlist =
        {
            "ctx", "prec", "rounding", "Emin", "Emax", "capitals", "clamp", "flags", "traps",
        };

        /* Context manager object for the 'with' statement. The manager
         * owns one reference to the global (outer) context and one
         * to the local (inner) context. */
        private static PyObject ctxmanager_new(PyObject[] args, PyDict kwds)
        {
            var global = CURRENT_CONTEXT();
            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, ctxmanager_kwlist, 0);
            PyObject Arg(int i) => parsed[i] ?? PyNone.Instance;
            var local = Arg(0);
            if (local is PyNone)
                local = global;
            else if (!PyDecContext_Check(local))
                throw PyTypeError.Create("optional argument must be a context");

            var self = new PyDecContextManagerObject();

            self.local = context_copy(local);
            self.global = global;

            context_setattrs(self.local, Arg(1), Arg(2), Arg(3), Arg(4), Arg(5), Arg(6), Arg(7), Arg(8));

            return self;
        }

        // ctxmanager_set_local
        private static PyObject ctxmanager_set_local(PyDecContextManagerObject self)
        {
            PyDec_SetCurrentContext(self.local);

            return self.local;
        }

        // ctxmanager_restore_global
        private static PyObject ctxmanager_restore_global(PyDecContextManagerObject self)
        {
            PyDec_SetCurrentContext(self.global);

            return PyNone.Instance;
        }


        /******************************************************************************/
        /*                         Argument parsing (Python/getargs.c)                */
        /******************************************************************************/

        // PyArg_ParseTupleAndKeywords(args, kwds, "|OO...", kwlist, ...) with no function name:
        // vgetargskeywords, "function takes at most ..." and "... for this function".
        private static PyObject[] PyArg_ParseTupleAndKeywords(PyObject[] args, PyDict kwds, string[] kwlist, int min) =>
            Datetime.DatetimeModule.ParseTupleAndKeywords(null, args, kwds, kwlist, min);

        // PyArg_ParseTuple(args, "OO|O", ...) with no function name: vgetargs1.
        private static void PyArg_ParseTuple(PyObject[] args, int min, int max)
        {
            int len = args.Length;
            if (len >= min && len <= max)
                return;
            if (min == max)
                throw PyTypeError.Create($"function takes exactly {min} argument{(min == 1 ? "" : "s")} ({len} given)");
            int count = len < min ? min : max;
            throw PyTypeError.Create($"function takes {(len < min ? "at least" : "at most")} {count} argument{(count == 1 ? "" : "s")} ({len} given)");
        }


        /******************************************************************************/
        /*                              Module creation                               */
        /******************************************************************************/

        private static readonly object initLock = new object();
        // The module dict after the first PyInit__decimal: a single-phase module (m_size -1) is
        // initialized once; a fresh import gets a copy of that dict (_PyImport_FixupExtensionObject).
        private static List<KeyValuePair<string, PyObject>> module_dict_copy;

        // ssize_constants
        private static readonly (string name, long val)[] ssize_constants =
        {
            ("MAX_PREC", MPD_MAX_PREC),
            ("MAX_EMAX", MPD_MAX_EMAX),
            ("MIN_EMIN", MPD_MIN_EMIN),
            ("MIN_ETINY", MPD_MIN_ETINY),
        };

        // _py_long_multiply, _py_long_floor_divide, _py_long_power, _py_float_abs,
        // _py_long_bit_length, _py_float_as_integer_ratio
        private static PyObject _py_long_multiply(PyObject a, PyObject b) =>
            PyNumberSlots.Binary(PyNumberSlots.Kind.Long, BinaryOpType.MULTIPLY, a, b);
        private static PyObject _py_long_floor_divide(PyObject a, PyObject b) =>
            PyNumberSlots.Binary(PyNumberSlots.Kind.Long, BinaryOpType.FLOOR_DIVIDE, a, b);
        private static PyObject _py_long_power(PyObject a, PyObject b, PyObject c) =>
            PyNumberSlots.Power(PyNumberSlots.Kind.Long, a, b, c);
        private static PyObject _py_float_abs(PyObject v) => new PyFloat(Math.Abs(PyFloat.FloatValue(v).Value));
        private static PyObject _py_long_bit_length;
        private static PyObject _py_float_as_integer_ratio;

        // cfunc_noargs: the METH_NOARGS method of a builtin type.
        private static PyObject cfunc_noargs(PyType t, string name)
        {
            if (t.TypeDict.TryGetValue(name, out var m) && m is PyMethodDescriptor)
                return m;
            throw PyRuntimeError.Create($"internal error: could not find method {name}");
        }

        /// <summary>PyInit__decimal: the module.</summary>
        internal static PyModule Create()
        {
            lock (initLock)
            {
                if (module_dict_copy == null)
                    module_dict_copy = new List<KeyValuePair<string, PyObject>>(PyInit__decimal().ModuleDict);
                // PyModuleDef.m_name: "decimal".
                var module = new PyModule("decimal");
                foreach (var pair in module_dict_copy)
                    module.ModuleDict[pair.Key] = pair.Value;
                return module;
            }
        }

        // PyInit__decimal
        private static PyModule PyInit__decimal()
        {
            /* Init libmpdec */
            mpd_traphandler = dec_traphandler;
            mpd_setminalloc(_Py_DEC_MINALLOC);
            mpd_localeconv = LocaleNumeric;

            /* Init external C-API functions */
            _py_float_as_integer_ratio = cfunc_noargs(PyType.FloatType, "as_integer_ratio");
            _py_long_bit_length = cfunc_noargs(PyType.IntType, "bit_length");

            /* Init types */
            MakeTypes();

            /* Numeric abstract base classes */
            var numbers = PyImportSystem.Import("numbers");
            var Number = numbers.GetAttribute("Number");
            /* Register Decimal with the Number abstract base class */
            Number.GetAttribute("register").Call(new PyObject[] { PyDec_Type }, null);
            /* Rational is a global variable used for fraction comparisons. */
            Rational = numbers.GetAttribute("Rational");

            /* DecimalTuple */
            var collections = PyImportSystem.Import("collections");
            DecimalTuple = collections.GetAttribute("namedtuple").Call(
                new PyObject[] { PyString.From("DecimalTuple"), PyString.From("sign digits exponent") }, null);
            DecimalTuple.SetAttribute("__module__", PyString.From("decimal"));

            /* MutableMapping */
            var collections_abc = PyImportSystem.Import("collections.abc");
            var MutableMapping = collections_abc.GetAttribute("MutableMapping");
            /* Create SignalDict type */
            PyDecSignalDict_Type = (PyType)PyType.TypeType.Call(new PyObject[]
            {
                PyString.From("SignalDict"), PyTuple.From(new PyObject[] { PyDecSignalDictMixin_Type, MutableMapping }), new PyDict(),
            }, null);

            /* Create the module */
            var m = new PyModule("decimal");
            var d = m.ModuleDict;
            d["__doc__"] = PyString.From("C decimal arithmetic module");

            /* For format specifiers not yet supported by libmpdec */
            PyDecimal = null;

            /* Add types to the module */
            d["Decimal"] = PyDec_Type;
            d["Context"] = PyDecContext_Type;
            d["DecimalTuple"] = DecimalTuple;

            /* Create top level exception */
            DecimalException = PyErr_NewException("decimal.DecimalException", new PyObject[] { PyType.ArithmeticErrorType });
            d["DecimalException"] = DecimalException;

            /* Create signal tuple */
            var signals = new PyObject[SIGNAL_MAP_LEN];

            /* Add exceptions that correspond to IEEE signals */
            for (int i = SIGNAL_MAP_LEN - 1; i >= 0; i--)
            {
                PyObject[] bases;
                var cm = signal_map[i];

                switch (cm.flag)
                {
                    case MPD_Float_operation:
                        bases = new[] { DecimalException, PyType.TypeErrorType };
                        break;
                    case MPD_Division_by_zero:
                        bases = new[] { DecimalException, PyType.ZeroDivisionErrorType };
                        break;
                    case MPD_Overflow:
                        bases = new[] { signal_map[INEXACT].ex, signal_map[ROUNDED].ex };
                        break;
                    case MPD_Underflow:
                        bases = new[] { signal_map[INEXACT].ex, signal_map[ROUNDED].ex, signal_map[SUBNORMAL].ex };
                        break;
                    default:
                        bases = new[] { DecimalException };
                        break;
                }

                cm.ex = PyErr_NewException(cm.fqname, bases);

                /* add to module */
                d[cm.name] = cm.ex;

                /* add to signal tuple */
                signals[i] = cm.ex;
            }
            SignalTuple = PyTuple.From(signals);

            /*
             * Unfortunately, InvalidOperation is a signal that comprises
             * several conditions, including InvalidOperation! Naming the
             * signal IEEEInvalidOperation would prevent the confusion.
             */
            cond_map[0].ex = signal_map[0].ex;

            /* Add remaining exceptions, inherit from InvalidOperation */
            for (int i = 1; i < cond_map.Length; i++)
            {
                var cm = cond_map[i];
                PyObject[] bases;
                if (cm.flag == MPD_Division_undefined)
                    bases = new[] { signal_map[0].ex, PyType.ZeroDivisionErrorType };
                else
                    bases = new[] { signal_map[0].ex };

                cm.ex = PyErr_NewException(cm.fqname, bases);

                d[cm.name] = cm.ex;
            }

            /* Init default context template first */
            default_context_template = PyDecContext_Type.Call(Array.Empty<PyObject>(), null);
            d["DefaultContext"] = default_context_template;

            current_context_var = _ContextvarsModule.PyContextVar_New("decimal_context");
            d["HAVE_CONTEXTVAR"] = PyBool.True;
            d["HAVE_THREADS"] = PyBool.True;

            /* Init basic context template */
            basic_context_template = PyDecContext_Type.Call(Array.Empty<PyObject>(), null);
            init_basic_context(basic_context_template);
            d["BasicContext"] = basic_context_template;

            /* Init extended context template */
            extended_context_template = PyDecContext_Type.Call(Array.Empty<PyObject>(), null);
            init_extended_context(extended_context_template);
            d["ExtendedContext"] = extended_context_template;

            /* Init mpd_ssize_t constants */
            foreach (var (name, val) in ssize_constants)
                d[name] = PyInt.From(val);

            /* Init string constants */
            for (int i = 0; i < _PY_DEC_ROUND_GUARD; i++)
            {
                round_map[i] = PyString.InternFrom(mpd_round_string[i]);
                d[mpd_round_string[i]] = round_map[i];
            }

            /* Add specification version number */
            d["__version__"] = PyString.From("1.70");
            d["__libmpdec_version__"] = PyString.From(mpd_version());

            /* _decimal_methods */
            d["getcontext"] = PyArgs.NoArgsFunction("decimal", "getcontext", PyDec_GetCurrentContext);
            d["setcontext"] = PyArgs.MethO("decimal", "setcontext", args => PyDec_SetCurrentContext(args[0]));
            d["localcontext"] = new PyBuiltinFunction("localcontext", (args, kwargs) => ctxmanager_new(args, kwargs))
            {
                OwnerModule = "decimal",
            };

            return m;
        }

        // PyErr_NewException(name, base, NULL): a class named after the part after the last dot,
        // __module__ the part before it.
        private static PyObject PyErr_NewException(string name, PyObject[] bases)
        {
            int dot = name.LastIndexOf('.');
            var dict = new PyDict();
            dict.SetItem(PyString.From("__module__"), PyString.From(name.Substring(0, dot)));
            return PyType.TypeType.Call(new PyObject[]
            {
                PyString.From(name.Substring(dot + 1)), PyTuple.From(bases), dict,
            }, null);
        }

        // localeconv() as the C runtime gives it to libmpdec: the LC_NUMERIC strings in UTF-8 and the
        // grouping string (sizes up to the terminating 0).
        private static (byte[] decimal_point, byte[] thousands_sep, byte[] grouping) LocaleNumeric()
        {
            var (point, separator, grouping) = LocaleModule.Numeric();
            var bytes = new List<byte>();
            foreach (int size in grouping)
            {
                if (size == 0)
                    break;
                bytes.Add((byte)size);
            }
            return (Encoding.UTF8.GetBytes(point), Encoding.UTF8.GetBytes(separator), bytes.ToArray());
        }
    }
}
