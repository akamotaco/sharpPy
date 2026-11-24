// Modules/_decimal/_decimal.c (CPython 3.12.15), part 4: Context part 2 (the macros that turn
// mpdecimal functions into Context methods and the methods themselves), and the static types:
// PyDecSignalDictMixin_Type, PyDecContextManager_Type, PyDec_Type and PyDecContext_Type with the
// dicts type_ready gives them (__new__, the slot wrappers of add_operators in slotdefs order, the
// methods, the getsets).
using System;
using SharpPy.Modules.Mpdecimal;
using static SharpPy.Modules.Mpdecimal.Mpd;

namespace SharpPy.Modules.CDecimal
{
    internal static partial class DecimalModule
    {
        /************************************************************************/
        /*     Macros for converting mpdecimal functions to Context methods     */
        /************************************************************************/

        /* Boolean context method. */
        private static PyObject DecCtx_BoolFunc(BoolCtxMpdFunc MPDFUNC, PyObject context, PyObject v)
        {
            var a = convert_op(TYPE_ERR, v, context);

            return MPDFUNC(MPD(a), ref CTX(context)) ? incr_true() : incr_false();
        }

        /* Boolean context method. MPDFUNC does NOT use a context. */
        private static PyObject DecCtx_BoolFunc_NO_CTX(Func<mpd_t, bool> MPDFUNC, PyObject context, PyObject v)
        {
            var a = convert_op(TYPE_ERR, v, context);

            return MPDFUNC(MPD(a)) ? incr_true() : incr_false();
        }

        /* Unary context method. */
        private static PyObject DecCtx_UnaryFunc(UnaryMpdFunc MPDFUNC, PyObject context, PyObject v)
        {
            uint status = 0;

            var a = convert_op(TYPE_ERR, v, context);

            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(a), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }

        /* Binary context method. */
        private static PyObject DecCtx_BinaryFunc(BinaryMpdFunc MPDFUNC, PyObject context, PyObject[] args)
        {
            uint status = 0;

            PyArg_ParseTuple(args, 2, 2);
            var v = args[0];
            var w = args[1];

            var a = convert_op(TYPE_ERR, v, context);
            var b = convert_op(TYPE_ERR, w, context);

            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(a), MPD(b), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }

        /*
         * Binary context method. The context is only used for conversion.
         * The actual MPDFUNC does NOT take a context arg.
         */
        private static PyObject DecCtx_BinaryFunc_NO_CTX(Func<mpd_t, mpd_t, mpd_t, int> MPDFUNC, PyObject context, PyObject[] args)
        {
            PyArg_ParseTuple(args, 2, 2);
            var v = args[0];
            var w = args[1];

            var a = convert_op(TYPE_ERR, v, context);
            var b = convert_op(TYPE_ERR, w, context);

            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(a), MPD(b));

            return result;
        }

        /* Ternary context method. */
        private static PyObject DecCtx_TernaryFunc(TernaryMpdFunc MPDFUNC, PyObject context, PyObject[] args)
        {
            uint status = 0;

            PyArg_ParseTuple(args, 3, 3);
            var v = args[0];
            var w = args[1];
            var x = args[2];

            var a = convert_op(TYPE_ERR, v, context);
            var b = convert_op(TYPE_ERR, w, context);
            var c = convert_op(TYPE_ERR, x, context);

            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(a), MPD(b), MPD(c), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }

        // ctx_mpd_qdivmod
        private static PyObject ctx_mpd_qdivmod(PyObject context, PyObject[] args)
        {
            uint status = 0;

            PyArg_ParseTuple(args, 2, 2);
            var v = args[0];
            var w = args[1];

            var a = convert_op(TYPE_ERR, v, context);
            var b = convert_op(TYPE_ERR, w, context);

            var q = dec_alloc();
            var r = dec_alloc();

            mpd_qdivmod(MPD(q), MPD(r), MPD(a), MPD(b), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return PyTuple.From(q, r);
        }

        private static readonly string[] ctx_power_kwlist = { "a", "b", "modulo" };

        /* Binary or ternary arithmetic functions */
        private static PyObject ctx_mpd_qpow(PyObject context, PyObject[] args, PyDict kwds)
        {
            PyObject c = null;
            uint status = 0;

            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, ctx_power_kwlist, 2);
            var @base = parsed[0];
            var exp = parsed[1];
            var mod = parsed[2] ?? PyNone.Instance;

            var a = convert_op(TYPE_ERR, @base, context);
            var b = convert_op(TYPE_ERR, exp, context);

            if (mod is not PyNone)
                c = convert_op(TYPE_ERR, mod, context);

            var result = dec_alloc();

            if (c == null)
                mpd_qpow(MPD(result), MPD(a), MPD(b), ref CTX(context), ref status);
            else
                mpd_qpowmod(MPD(result), MPD(a), MPD(b), MPD(c), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }

        /* No argument */
        private static PyObject ctx_mpd_radix(PyObject context) => dec_mpd_radix(context);

        // ctx_iscanonical
        private static PyObject ctx_iscanonical(PyObject context, PyObject v)
        {
            if (!PyDec_Check(v))
                throw PyTypeError.Create("argument must be a Decimal");

            return mpd_iscanonical(MPD(v)) ? incr_true() : incr_false();
        }

        /* Functions with a single decimal argument */
        private static PyObject PyDecContext_Apply(PyObject context, PyObject v)
        {
            var a = convert_op(TYPE_ERR, v, context);

            return dec_apply(a, context);
        }

        // ctx_canonical
        private static PyObject ctx_canonical(PyObject context, PyObject v)
        {
            if (!PyDec_Check(v))
                throw PyTypeError.Create("argument must be a Decimal");

            return v;
        }

        // ctx_mpd_qcopy_abs
        private static PyObject ctx_mpd_qcopy_abs(PyObject context, PyObject v)
        {
            uint status = 0;

            var a = convert_op(TYPE_ERR, v, context);

            var result = dec_alloc();

            mpd_qcopy_abs(MPD(result), MPD(a), ref status);
            dec_addstatus(context, status);

            return result;
        }

        // ctx_copy_decimal
        private static PyObject ctx_copy_decimal(PyObject context, PyObject v) => convert_op(TYPE_ERR, v, context);

        // ctx_mpd_qcopy_negate
        private static PyObject ctx_mpd_qcopy_negate(PyObject context, PyObject v)
        {
            uint status = 0;

            var a = convert_op(TYPE_ERR, v, context);

            var result = dec_alloc();

            mpd_qcopy_negate(MPD(result), MPD(a), ref status);
            dec_addstatus(context, status);

            return result;
        }

        // ctx_mpd_class
        private static PyObject ctx_mpd_class(PyObject context, PyObject v)
        {
            var a = convert_op(TYPE_ERR, v, context);

            var cp = mpd_class(MPD(a), ref CTX(context));

            return PyString.From(cp);
        }

        // ctx_mpd_to_sci
        private static PyObject ctx_mpd_to_sci(PyObject context, PyObject v)
        {
            var a = convert_op(TYPE_ERR, v, context);

            long size = mpd_to_sci_size(out string s, MPD(a), CtxCaps(context));
            if (size < 0)
                throw PyErr_NoMemory();

            return unicode_fromascii(s);
        }

        // ctx_mpd_to_eng
        private static PyObject ctx_mpd_to_eng(PyObject context, PyObject v)
        {
            var a = convert_op(TYPE_ERR, v, context);

            long size = mpd_to_eng_size(out string s, MPD(a), CtxCaps(context));
            if (size < 0)
                throw PyErr_NoMemory();

            return unicode_fromascii(s);
        }

        // ctx_mpd_qcopy_sign
        private static PyObject ctx_mpd_qcopy_sign(PyObject context, PyObject[] args)
        {
            uint status = 0;

            PyArg_ParseTuple(args, 2, 2);
            var v = args[0];
            var w = args[1];

            var a = convert_op(TYPE_ERR, v, context);
            var b = convert_op(TYPE_ERR, w, context);

            var result = dec_alloc();

            mpd_qcopy_sign(MPD(result), MPD(a), MPD(b), ref status);
            dec_addstatus(context, status);

            return result;
        }

        // ctx_mpd_same_quantum
        private static PyObject ctx_mpd_same_quantum(PyObject context, PyObject[] args)
        {
            PyArg_ParseTuple(args, 2, 2);
            var v = args[0];
            var w = args[1];

            var a = convert_op(TYPE_ERR, v, context);
            var b = convert_op(TYPE_ERR, w, context);

            return mpd_same_quantum(MPD(a), MPD(b)) ? incr_true() : incr_false();
        }


        /******************************************************************************/
        /*                        The static types and their dicts                    */
        /******************************************************************************/

        // A slot wrapper of add_operators: (self, args) to the slot function.
        // A reflected wrapper (__radd__ ...) is not the CLR virtual: binary_op1 calls it for the
        // right operand.
        private static void Slot(PyType type, string name, Func<PyObject, PyObject[], PyObject> slot) =>
            type.TypeDict[name] = new PyWrapperDescriptor(name, type, (self, args, kwargs) => slot(self, args))
            {
                CallsVirtual = !(name.StartsWith("__r") && name != "__repr__"),
            };

        // richcmp_lt ... richcmp_ge: the wrappers of tp_richcompare, in slotdefs order.
        private static void RichCompareSlots(PyType type, Func<PyObject, PyObject, PyObject.CompareOp, PyObject> richcompare)
        {
            foreach (var (name, op) in new[]
            {
                ("__lt__", PyObject.CompareOp.LT), ("__le__", PyObject.CompareOp.LE), ("__eq__", PyObject.CompareOp.EQ),
                ("__ne__", PyObject.CompareOp.NE), ("__gt__", PyObject.CompareOp.GT), ("__ge__", PyObject.CompareOp.GE),
            })
                Slot(type, name, (self, args) => richcompare(self, args[0], op));
        }

        // wrap_binaryfunc_l and wrap_binaryfunc_r
        private static void BinarySlots(PyType type, string op, Func<PyObject, PyObject, PyObject> slot)
        {
            Slot(type, $"__{op}__", (self, args) => slot(self, args[0]));
            Slot(type, $"__r{op}__", (self, args) => slot(args[0], self));
        }

        // PyObject_GenericGetAttr's wrapper (wrap_binaryfunc over tp_getattro).
        private static void GetattroSlot(PyType type) =>
            Slot(type, "__getattribute__", (self, args) =>
            {
                PyObject.AttributeName(args[0]);
                return self.TpGetattro((PyString)args[0]);
            });

        // wrap_setattr and wrap_delattr (hackcheck first).
        private static void SetattroSlots(PyType type)
        {
            Slot(type, "__setattr__", (self, args) =>
            {
                PyType.Hackcheck(self, "__setattr__");
                PyObject.AttributeName(args[0]);
                self.TpSetattro((PyString)args[0], args[1]);
                return PyNone.Instance;
            });
            Slot(type, "__delattr__", (self, args) =>
            {
                PyType.Hackcheck(self, "__delattr__");
                PyObject.AttributeName(args[0]);
                self.TpDelattro((PyString)args[0]);
                return PyNone.Instance;
            });
        }

        // A PyMethodDef row: METH_NOARGS, METH_O, METH_VARARGS (keywords refused) or
        // METH_VARARGS|METH_KEYWORDS (the body parses everything).
        private static void Method(PyType type, string name, PyCall kind, Func<PyObject, PyObject[], PyDict, PyObject> body)
        {
            type.TypeDict[name] = kind switch
            {
                PyCall.NoArgs or PyCall.One => new PyMethodDescriptor(name, type, body, 0, int.MaxValue, acceptsKwargs: true)
                {
                    Convention = new PyArity.Convention(kind, 0, 0),
                },
                PyCall.ParseTuple => new PyMethodDescriptor(name, type, body, 0, int.MaxValue, acceptsKwargs: true)
                {
                    Convention = new PyArity.Convention(PyCall.ParseTuple, 0, int.MaxValue),
                },
                _ => new PyMethodDescriptor(name, type, body, 0, int.MaxValue, acceptsKwargs: true),
            };
        }

        private static void NoArgs(PyType type, string name, Func<PyObject, PyObject> body) =>
            Method(type, name, PyCall.NoArgs, (self, args, kwargs) => body(self));

        private static void MethO(PyType type, string name, Func<PyObject, PyObject, PyObject> body) =>
            Method(type, name, PyCall.One, (self, args, kwargs) => body(self, args[0]));

        private static void VarArgs(PyType type, string name, Func<PyObject, PyObject[], PyObject> body) =>
            Method(type, name, PyCall.ParseTuple, (self, args, kwargs) => body(self, args));

        private static void VarArgsKeywords(PyType type, string name, Func<PyObject, PyObject[], PyDict, PyObject> body) =>
            Method(type, name, (PyCall)(-1), body);

        // A METH_O|METH_CLASS row: the class first.
        private static void ClassMethO(PyType type, string name, Func<PyObject, PyObject, PyObject> body)
        {
            type.TypeDict[name] = new PyClassMethodDescriptor(name, type, new PyBuiltinFunction(name, (args, kwargs) =>
            {
                var cls = args[0];
                var rest = args[1..];
                PyArgs.One(PyBuiltinFunction.QualifiedClassLabel(cls) + "." + name, rest, kwargs);
                return body(cls, rest[0]);
            }));
        }

        // A PyGetSetDef row.
        private static void GetSet(PyType type, string name, Func<PyObject, PyObject> get, Action<PyObject, PyObject> set = null) =>
            type.TypeDict[name] = new PyGetSetDescriptor(name, type, get, set);

        private static void MakeTypes()
        {
            if (PyDec_Type != null)
                return;
            MakeSignalDictMixinType();
            MakeContextManagerType();
            MakeDecType();
            MakeContextType();
        }

        // PyDecSignalDictMixin_Type
        private static void MakeSignalDictMixinType()
        {
            var type = new PyType("SignalDictMixin", new[] { PyType.ObjectType }, "decimal");
            PyDecSignalDictMixin_Type = type;
            // PyType_GenericNew
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => new PyDecSignalDictObject((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            Slot(type, "__repr__", (self, a) => signaldict_repr(self));
            // PyObject_HashNotImplemented
            type.TypeDict["__hash__"] = PyNone.Instance;
            GetattroSlot(type);
            RichCompareSlots(type, signaldict_richcompare);
            Slot(type, "__iter__", (self, a) => signaldict_iter(self));
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                signaldict_init(self, args, kwargs);
                return PyNone.Instance;
            });
            Slot(type, "__len__", (self, a) => PyInt.From(signaldict_len(self)));
            Slot(type, "__getitem__", (self, a) => signaldict_getitem(self, a[0]));
            Slot(type, "__setitem__", (self, a) =>
            {
                signaldict_setitem(self, a[0], a[1]);
                return PyNone.Instance;
            });
            Slot(type, "__delitem__", (self, a) =>
            {
                signaldict_setitem(self, a[0], null);
                return PyNone.Instance;
            });
            // signaldict_methods
            NoArgs(type, "copy", signaldict_copy);
            type.TypeDict["__doc__"] = PyNone.Instance;
        }

        // PyDecContextManager_Type
        private static void MakeContextManagerType()
        {
            var type = new PyType("ContextManager", new[] { PyType.ObjectType }, "decimal");
            PyDecContextManager_Type = type;
            GetattroSlot(type);
            // ctxmanager_methods
            NoArgs(type, "__enter__", self => ctxmanager_set_local((PyDecContextManagerObject)self));
            VarArgs(type, "__exit__", (self, args) => ctxmanager_restore_global((PyDecContextManagerObject)self));
            type.TypeDict["__doc__"] = PyNone.Instance;
        }

        // PyDec_Type
        private static void MakeDecType()
        {
            var type = new PyType("Decimal", new[] { PyType.ObjectType }, "decimal");
            PyDec_Type = type;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => dec_new((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            // add_operators, slotdefs order
            Slot(type, "__repr__", (self, a) => dec_repr(self));
            Slot(type, "__hash__", (self, a) => PyInt.From(dec_hash((PyDecObject)self)));
            Slot(type, "__str__", (self, a) => dec_str(self));
            GetattroSlot(type);
            RichCompareSlots(type, dec_richcompare);
            BinarySlots(type, "add", nm_mpd_qadd);
            BinarySlots(type, "sub", nm_mpd_qsub);
            BinarySlots(type, "mul", nm_mpd_qmul);
            BinarySlots(type, "mod", nm_mpd_qrem);
            BinarySlots(type, "divmod", nm_mpd_qdivmod);
            // wrap_ternaryfunc: __pow__(self, value, mod=None, /); wrap_ternaryfunc_r: __rpow__.
            Slot(type, "__pow__", (self, a) => nm_mpd_qpow(self, a[0], a.Length > 1 ? a[1] : PyNone.Instance));
            Slot(type, "__rpow__", (self, a) => nm_mpd_qpow(a[0], self, a.Length > 1 ? a[1] : PyNone.Instance));
            Slot(type, "__neg__", (self, a) => nm_mpd_qminus(self));
            Slot(type, "__pos__", (self, a) => nm_mpd_qplus(self));
            Slot(type, "__abs__", (self, a) => nm_mpd_qabs(self));
            Slot(type, "__bool__", (self, a) => PyBool.FromBool(nm_nonzero(self)));
            Slot(type, "__int__", (self, a) => nm_dec_as_long(self));
            Slot(type, "__float__", (self, a) => PyDec_AsFloat(self));
            BinarySlots(type, "floordiv", nm_mpd_qdivint);
            BinarySlots(type, "truediv", nm_mpd_qdiv);

            // dec_methods
            /* Unary arithmetic functions, optional context arg */
            VarArgsKeywords(type, "exp", (s, a, k) => Dec_UnaryFuncVA(mpd_qexp, s, a, k));
            VarArgsKeywords(type, "ln", (s, a, k) => Dec_UnaryFuncVA(mpd_qln, s, a, k));
            VarArgsKeywords(type, "log10", (s, a, k) => Dec_UnaryFuncVA(mpd_qlog10, s, a, k));
            VarArgsKeywords(type, "next_minus", (s, a, k) => Dec_UnaryFuncVA(mpd_qnext_minus, s, a, k));
            VarArgsKeywords(type, "next_plus", (s, a, k) => Dec_UnaryFuncVA(mpd_qnext_plus, s, a, k));
            VarArgsKeywords(type, "normalize", (s, a, k) => Dec_UnaryFuncVA(mpd_qreduce, s, a, k));
            VarArgsKeywords(type, "to_integral", PyDec_ToIntegralValue);
            VarArgsKeywords(type, "to_integral_exact", PyDec_ToIntegralExact);
            VarArgsKeywords(type, "to_integral_value", PyDec_ToIntegralValue);
            VarArgsKeywords(type, "sqrt", (s, a, k) => Dec_UnaryFuncVA(mpd_qsqrt, s, a, k));

            /* Binary arithmetic functions, optional context arg */
            VarArgsKeywords(type, "compare", (s, a, k) => Dec_BinaryFuncVA(mpd_qcompare_void, s, a, k));
            VarArgsKeywords(type, "compare_signal", (s, a, k) => Dec_BinaryFuncVA(mpd_qcompare_signal_void, s, a, k));
            VarArgsKeywords(type, "max", (s, a, k) => Dec_BinaryFuncVA(mpd_qmax, s, a, k));
            VarArgsKeywords(type, "max_mag", (s, a, k) => Dec_BinaryFuncVA(mpd_qmax_mag, s, a, k));
            VarArgsKeywords(type, "min", (s, a, k) => Dec_BinaryFuncVA(mpd_qmin, s, a, k));
            VarArgsKeywords(type, "min_mag", (s, a, k) => Dec_BinaryFuncVA(mpd_qmin_mag, s, a, k));
            VarArgsKeywords(type, "next_toward", (s, a, k) => Dec_BinaryFuncVA(mpd_qnext_toward, s, a, k));
            VarArgsKeywords(type, "quantize", dec_mpd_qquantize);
            VarArgsKeywords(type, "remainder_near", (s, a, k) => Dec_BinaryFuncVA(mpd_qrem_near, s, a, k));

            /* Ternary arithmetic functions, optional context arg */
            VarArgsKeywords(type, "fma", (s, a, k) => Dec_TernaryFuncVA(mpd_qfma, s, a, k));

            /* Boolean functions, no context arg */
            NoArgs(type, "is_canonical", s => Dec_BoolFunc(mpd_iscanonical, s));
            NoArgs(type, "is_finite", s => Dec_BoolFunc(mpd_isfinite, s));
            NoArgs(type, "is_infinite", s => Dec_BoolFunc(mpd_isinfinite, s));
            NoArgs(type, "is_nan", s => Dec_BoolFunc(mpd_isnan, s));
            NoArgs(type, "is_qnan", s => Dec_BoolFunc(mpd_isqnan, s));
            NoArgs(type, "is_snan", s => Dec_BoolFunc(mpd_issnan, s));
            NoArgs(type, "is_signed", s => Dec_BoolFunc(mpd_issigned, s));
            NoArgs(type, "is_zero", s => Dec_BoolFunc(mpd_iszero, s));

            /* Boolean functions, optional context arg */
            VarArgsKeywords(type, "is_normal", (s, a, k) => Dec_BoolFuncVA(mpd_isnormal, s, a, k));
            VarArgsKeywords(type, "is_subnormal", (s, a, k) => Dec_BoolFuncVA(mpd_issubnormal, s, a, k));

            /* Unary functions, no context arg */
            NoArgs(type, "adjusted", dec_mpd_adjexp);
            NoArgs(type, "canonical", dec_canonical);
            NoArgs(type, "conjugate", dec_conjugate);
            NoArgs(type, "radix", dec_mpd_radix);

            /* Unary functions, optional context arg for conversion errors */
            NoArgs(type, "copy_abs", dec_mpd_qcopy_abs);
            NoArgs(type, "copy_negate", dec_mpd_qcopy_negate);

            /* Unary functions, optional context arg */
            VarArgsKeywords(type, "logb", (s, a, k) => Dec_UnaryFuncVA(mpd_qlogb, s, a, k));
            VarArgsKeywords(type, "logical_invert", (s, a, k) => Dec_UnaryFuncVA(mpd_qinvert, s, a, k));
            VarArgsKeywords(type, "number_class", dec_mpd_class);
            VarArgsKeywords(type, "to_eng_string", dec_mpd_to_eng);

            /* Binary functions, optional context arg for conversion errors */
            VarArgsKeywords(type, "compare_total", (s, a, k) => Dec_BinaryFuncVA_NO_CTX(mpd_compare_total, s, a, k));
            VarArgsKeywords(type, "compare_total_mag", (s, a, k) => Dec_BinaryFuncVA_NO_CTX(mpd_compare_total_mag, s, a, k));
            VarArgsKeywords(type, "copy_sign", dec_mpd_qcopy_sign);
            VarArgsKeywords(type, "same_quantum", dec_mpd_same_quantum);

            /* Binary functions, optional context arg */
            VarArgsKeywords(type, "logical_and", (s, a, k) => Dec_BinaryFuncVA(mpd_qand, s, a, k));
            VarArgsKeywords(type, "logical_or", (s, a, k) => Dec_BinaryFuncVA(mpd_qor, s, a, k));
            VarArgsKeywords(type, "logical_xor", (s, a, k) => Dec_BinaryFuncVA(mpd_qxor, s, a, k));
            VarArgsKeywords(type, "rotate", (s, a, k) => Dec_BinaryFuncVA(mpd_qrotate, s, a, k));
            VarArgsKeywords(type, "scaleb", (s, a, k) => Dec_BinaryFuncVA(mpd_qscaleb, s, a, k));
            VarArgsKeywords(type, "shift", (s, a, k) => Dec_BinaryFuncVA(mpd_qshift, s, a, k));

            /* Miscellaneous */
            ClassMethO(type, "from_float", dec_from_float);
            NoArgs(type, "as_tuple", PyDec_AsTuple);
            NoArgs(type, "as_integer_ratio", dec_as_integer_ratio);

            /* Special methods */
            NoArgs(type, "__copy__", dec_copy);
            MethO(type, "__deepcopy__", (s, memo) => dec_copy(s));
            VarArgs(type, "__format__", dec_format);
            NoArgs(type, "__reduce__", dec_reduce);
            VarArgs(type, "__round__", PyDec_Round);
            NoArgs(type, "__ceil__", dec_ceil);
            NoArgs(type, "__floor__", dec_floor);
            NoArgs(type, "__trunc__", dec_trunc);
            NoArgs(type, "__complex__", dec_complex);
            NoArgs(type, "__sizeof__", dec_sizeof);

            // dec_getsets
            GetSet(type, "real", dec_real);
            GetSet(type, "imag", dec_imag);

            // PyDict_SetItemString(PyDec_Type.tp_dict, "__module__", "decimal")
            type.TypeDict["__module__"] = PyString.From("decimal");
        }

        // mpd_qcompare and mpd_qcompare_signal return the comparison; the macros drop it.
        private static void mpd_qcompare_void(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status) =>
            mpd_qcompare(result, a, b, ref ctx, ref status);
        private static void mpd_qcompare_signal_void(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status) =>
            mpd_qcompare_signal(result, a, b, ref ctx, ref status);

        // PyDecContext_Type
        private static void MakeContextType()
        {
            var type = new PyType("Context", new[] { PyType.ObjectType }, "decimal");
            PyDecContext_Type = type;
            // decimal.Context, not _contextvars.Context: tp_basicsize 96, Py_TPFLAGS_BASETYPE.
            type.SetStaticLayout(new TypeLayout(96, 0, 0, 0, false));
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => context_new((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            Slot(type, "__repr__", (self, a) => context_repr((PyDecContextObject)self));
            GetattroSlot(type);
            SetattroSlots(type);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                context_init(self, args, kwargs);
                return PyNone.Instance;
            });

            // context_methods
            /* Unary arithmetic functions */
            MethO(type, "abs", (c, v) => DecCtx_UnaryFunc(mpd_qabs, c, v));
            MethO(type, "exp", (c, v) => DecCtx_UnaryFunc(mpd_qexp, c, v));
            MethO(type, "ln", (c, v) => DecCtx_UnaryFunc(mpd_qln, c, v));
            MethO(type, "log10", (c, v) => DecCtx_UnaryFunc(mpd_qlog10, c, v));
            MethO(type, "minus", (c, v) => DecCtx_UnaryFunc(mpd_qminus, c, v));
            MethO(type, "next_minus", (c, v) => DecCtx_UnaryFunc(mpd_qnext_minus, c, v));
            MethO(type, "next_plus", (c, v) => DecCtx_UnaryFunc(mpd_qnext_plus, c, v));
            MethO(type, "normalize", (c, v) => DecCtx_UnaryFunc(mpd_qreduce, c, v));
            MethO(type, "plus", (c, v) => DecCtx_UnaryFunc(mpd_qplus, c, v));
            MethO(type, "to_integral", (c, v) => DecCtx_UnaryFunc(mpd_qround_to_int, c, v));
            MethO(type, "to_integral_exact", (c, v) => DecCtx_UnaryFunc(mpd_qround_to_intx, c, v));
            MethO(type, "to_integral_value", (c, v) => DecCtx_UnaryFunc(mpd_qround_to_int, c, v));
            MethO(type, "sqrt", (c, v) => DecCtx_UnaryFunc(mpd_qsqrt, c, v));

            /* Binary arithmetic functions */
            VarArgs(type, "add", (c, a) => DecCtx_BinaryFunc(mpd_qadd, c, a));
            VarArgs(type, "compare", (c, a) => DecCtx_BinaryFunc(mpd_qcompare_void, c, a));
            VarArgs(type, "compare_signal", (c, a) => DecCtx_BinaryFunc(mpd_qcompare_signal_void, c, a));
            VarArgs(type, "divide", (c, a) => DecCtx_BinaryFunc(mpd_qdiv, c, a));
            VarArgs(type, "divide_int", (c, a) => DecCtx_BinaryFunc(mpd_qdivint, c, a));
            VarArgs(type, "divmod", ctx_mpd_qdivmod);
            VarArgs(type, "max", (c, a) => DecCtx_BinaryFunc(mpd_qmax, c, a));
            VarArgs(type, "max_mag", (c, a) => DecCtx_BinaryFunc(mpd_qmax_mag, c, a));
            VarArgs(type, "min", (c, a) => DecCtx_BinaryFunc(mpd_qmin, c, a));
            VarArgs(type, "min_mag", (c, a) => DecCtx_BinaryFunc(mpd_qmin_mag, c, a));
            VarArgs(type, "multiply", (c, a) => DecCtx_BinaryFunc(mpd_qmul, c, a));
            VarArgs(type, "next_toward", (c, a) => DecCtx_BinaryFunc(mpd_qnext_toward, c, a));
            VarArgs(type, "quantize", (c, a) => DecCtx_BinaryFunc(mpd_qquantize, c, a));
            VarArgs(type, "remainder", (c, a) => DecCtx_BinaryFunc(mpd_qrem, c, a));
            VarArgs(type, "remainder_near", (c, a) => DecCtx_BinaryFunc(mpd_qrem_near, c, a));
            VarArgs(type, "subtract", (c, a) => DecCtx_BinaryFunc(mpd_qsub, c, a));

            /* Binary or ternary arithmetic functions */
            VarArgsKeywords(type, "power", ctx_mpd_qpow);

            /* Ternary arithmetic functions */
            VarArgs(type, "fma", (c, a) => DecCtx_TernaryFunc(mpd_qfma, c, a));

            /* No argument */
            NoArgs(type, "Etiny", context_getetiny);
            NoArgs(type, "Etop", context_getetop);
            NoArgs(type, "radix", ctx_mpd_radix);

            /* Boolean functions */
            MethO(type, "is_canonical", ctx_iscanonical);
            MethO(type, "is_finite", (c, v) => DecCtx_BoolFunc_NO_CTX(mpd_isfinite, c, v));
            MethO(type, "is_infinite", (c, v) => DecCtx_BoolFunc_NO_CTX(mpd_isinfinite, c, v));
            MethO(type, "is_nan", (c, v) => DecCtx_BoolFunc_NO_CTX(mpd_isnan, c, v));
            MethO(type, "is_normal", (c, v) => DecCtx_BoolFunc(mpd_isnormal, c, v));
            MethO(type, "is_qnan", (c, v) => DecCtx_BoolFunc_NO_CTX(mpd_isqnan, c, v));
            MethO(type, "is_signed", (c, v) => DecCtx_BoolFunc_NO_CTX(mpd_issigned, c, v));
            MethO(type, "is_snan", (c, v) => DecCtx_BoolFunc_NO_CTX(mpd_issnan, c, v));
            MethO(type, "is_subnormal", (c, v) => DecCtx_BoolFunc(mpd_issubnormal, c, v));
            MethO(type, "is_zero", (c, v) => DecCtx_BoolFunc_NO_CTX(mpd_iszero, c, v));

            /* Functions with a single decimal argument */
            MethO(type, "_apply", PyDecContext_Apply); /* alias for apply */
            MethO(type, "canonical", ctx_canonical);
            MethO(type, "copy_abs", ctx_mpd_qcopy_abs);
            MethO(type, "copy_decimal", ctx_copy_decimal);
            MethO(type, "copy_negate", ctx_mpd_qcopy_negate);
            MethO(type, "logb", (c, v) => DecCtx_UnaryFunc(mpd_qlogb, c, v));
            MethO(type, "logical_invert", (c, v) => DecCtx_UnaryFunc(mpd_qinvert, c, v));
            MethO(type, "number_class", ctx_mpd_class);
            MethO(type, "to_sci_string", ctx_mpd_to_sci);
            MethO(type, "to_eng_string", ctx_mpd_to_eng);

            /* Functions with two decimal arguments */
            VarArgs(type, "compare_total", (c, a) => DecCtx_BinaryFunc_NO_CTX(mpd_compare_total, c, a));
            VarArgs(type, "compare_total_mag", (c, a) => DecCtx_BinaryFunc_NO_CTX(mpd_compare_total_mag, c, a));
            VarArgs(type, "copy_sign", ctx_mpd_qcopy_sign);
            VarArgs(type, "logical_and", (c, a) => DecCtx_BinaryFunc(mpd_qand, c, a));
            VarArgs(type, "logical_or", (c, a) => DecCtx_BinaryFunc(mpd_qor, c, a));
            VarArgs(type, "logical_xor", (c, a) => DecCtx_BinaryFunc(mpd_qxor, c, a));
            VarArgs(type, "rotate", (c, a) => DecCtx_BinaryFunc(mpd_qrotate, c, a));
            VarArgs(type, "same_quantum", ctx_mpd_same_quantum);
            VarArgs(type, "scaleb", (c, a) => DecCtx_BinaryFunc(mpd_qscaleb, c, a));
            VarArgs(type, "shift", (c, a) => DecCtx_BinaryFunc(mpd_qshift, c, a));

            /* Set context values */
            NoArgs(type, "clear_flags", context_clear_flags);
            NoArgs(type, "clear_traps", context_clear_traps);

            /* Miscellaneous */
            NoArgs(type, "__copy__", context_copy);
            NoArgs(type, "__reduce__", context_reduce);
            NoArgs(type, "copy", context_copy);
            VarArgs(type, "create_decimal", ctx_create_decimal);
            MethO(type, "create_decimal_from_float", ctx_from_float);

            // context_getsets
            GetSet(type, "prec", context_getprec, context_setprec);
            GetSet(type, "Emax", context_getemax, context_setemax);
            GetSet(type, "Emin", context_getemin, context_setemin);
            GetSet(type, "rounding", context_getround, context_setround);
            GetSet(type, "capitals", context_getcapitals, context_setcapitals);
            GetSet(type, "clamp", context_getclamp, context_setclamp);

            // PyDict_SetItemString(PyDecContext_Type.tp_dict, "__module__", "decimal")
            type.TypeDict["__module__"] = PyString.From("decimal");
        }
    }
}
