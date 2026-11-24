// Modules/_decimal/_decimal.c (CPython 3.12.15), part 3: the macros that turn mpdecimal functions
// into Decimal methods (one C# helper per macro), the number methods, the Decimal methods, the
// special methods (richcompare, hash, reduce, ...), and PyDec_Type.
using System;
using SharpPy.Modules.Mpdecimal;
using static SharpPy.Modules.Mpdecimal.Mpd;

namespace SharpPy.Modules.CDecimal
{
    internal static partial class DecimalModule
    {
        // The libmpdec function shapes the macros take.
        internal delegate void UnaryMpdFunc(mpd_t result, mpd_t a, ref mpd_context_t ctx, ref uint status);
        internal delegate void BinaryMpdFunc(mpd_t result, mpd_t a, mpd_t b, ref mpd_context_t ctx, ref uint status);
        internal delegate void TernaryMpdFunc(mpd_t result, mpd_t a, mpd_t b, mpd_t c, ref mpd_context_t ctx, ref uint status);
        internal delegate bool BoolCtxMpdFunc(mpd_t a, ref mpd_context_t ctx);

        /******************************************************************************/
        /*         Macros for converting mpdecimal functions to Decimal methods       */
        /******************************************************************************/

        /* Unary number method that uses the default module context. */
        private static PyObject Dec_UnaryNumberMethod(UnaryMpdFunc MPDFUNC, PyObject self)
        {
            uint status = 0;

            var context = CURRENT_CONTEXT();
            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(self), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }

        /* Binary number method that uses default module context. */
        private static PyObject Dec_BinaryNumberMethod(BinaryMpdFunc MPDFUNC, PyObject self, PyObject other)
        {
            uint status = 0;

            var context = CURRENT_CONTEXT();
            // CONVERT_BINOP(&a, &b, self, other, context)
            var a = convert_op(NOT_IMPL, self, context);
            if (a == null)
                return PyNotImplemented.Instance;
            var b = convert_op(NOT_IMPL, other, context);
            if (b == null)
                return PyNotImplemented.Instance;

            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(a), MPD(b), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }

        /* Boolean function without a context arg. */
        private static PyObject Dec_BoolFunc(Func<mpd_t, bool> MPDFUNC, PyObject self) =>
            MPDFUNC(MPD(self)) ? incr_true() : incr_false();

        private static readonly string[] context_only_kwlist = { "context" };

        /* Boolean function with an optional context arg. */
        private static PyObject Dec_BoolFuncVA(BoolCtxMpdFunc MPDFUNC, PyObject self, PyObject[] args, PyDict kwds)
        {
            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, context_only_kwlist, 0);
            var context = CONTEXT_CHECK_VA(parsed[0] ?? PyNone.Instance);

            return MPDFUNC(MPD(self), ref CTX(context)) ? incr_true() : incr_false();
        }

        /* Unary function with an optional context arg. */
        private static PyObject Dec_UnaryFuncVA(UnaryMpdFunc MPDFUNC, PyObject self, PyObject[] args, PyDict kwds)
        {
            uint status = 0;

            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, context_only_kwlist, 0);
            var context = CONTEXT_CHECK_VA(parsed[0] ?? PyNone.Instance);

            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(self), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }

        private static readonly string[] other_context_kwlist = { "other", "context" };

        /* Binary function with an optional context arg. */
        private static PyObject Dec_BinaryFuncVA(BinaryMpdFunc MPDFUNC, PyObject self, PyObject[] args, PyDict kwds)
        {
            uint status = 0;

            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, other_context_kwlist, 1);
            var other = parsed[0];
            var context = CONTEXT_CHECK_VA(parsed[1] ?? PyNone.Instance);
            // CONVERT_BINOP_RAISE(&a, &b, self, other, context)
            var a = convert_op(TYPE_ERR, self, context);
            var b = convert_op(TYPE_ERR, other, context);

            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(a), MPD(b), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }

        /* Binary function with an optional context arg. Actual MPDFUNC does
           NOT take a context. The context is used to record InvalidOperation
           if the second operand cannot be converted exactly. */
        private static PyObject Dec_BinaryFuncVA_NO_CTX(Func<mpd_t, mpd_t, mpd_t, int> MPDFUNC, PyObject self, PyObject[] args, PyDict kwds)
        {
            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, other_context_kwlist, 1);
            var other = parsed[0];
            var context = CONTEXT_CHECK_VA(parsed[1] ?? PyNone.Instance);
            var a = convert_op(TYPE_ERR, self, context);
            var b = convert_op(TYPE_ERR, other, context);

            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(a), MPD(b));

            return result;
        }

        private static readonly string[] other_third_context_kwlist = { "other", "third", "context" };

        /* Ternary function with an optional context arg. */
        private static PyObject Dec_TernaryFuncVA(TernaryMpdFunc MPDFUNC, PyObject self, PyObject[] args, PyDict kwds)
        {
            uint status = 0;

            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, other_third_context_kwlist, 2);
            var other = parsed[0];
            var third = parsed[1];
            var context = CONTEXT_CHECK_VA(parsed[2] ?? PyNone.Instance);
            // CONVERT_TERNOP_RAISE(&a, &b, &c, self, other, third, context)
            var a = convert_op(TYPE_ERR, self, context);
            var b = convert_op(TYPE_ERR, other, context);
            var c = convert_op(TYPE_ERR, third, context);

            var result = dec_alloc();

            MPDFUNC(MPD(result), MPD(a), MPD(b), MPD(c), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }


        /**********************************************/
        /*              Number methods                */
        /**********************************************/

        internal static PyObject nm_mpd_qminus(PyObject self) => Dec_UnaryNumberMethod(mpd_qminus, self);
        internal static PyObject nm_mpd_qplus(PyObject self) => Dec_UnaryNumberMethod(mpd_qplus, self);
        internal static PyObject nm_mpd_qabs(PyObject self) => Dec_UnaryNumberMethod(mpd_qabs, self);

        internal static PyObject nm_mpd_qadd(PyObject self, PyObject other) => Dec_BinaryNumberMethod(mpd_qadd, self, other);
        internal static PyObject nm_mpd_qsub(PyObject self, PyObject other) => Dec_BinaryNumberMethod(mpd_qsub, self, other);
        internal static PyObject nm_mpd_qmul(PyObject self, PyObject other) => Dec_BinaryNumberMethod(mpd_qmul, self, other);
        internal static PyObject nm_mpd_qdiv(PyObject self, PyObject other) => Dec_BinaryNumberMethod(mpd_qdiv, self, other);
        internal static PyObject nm_mpd_qrem(PyObject self, PyObject other) => Dec_BinaryNumberMethod(mpd_qrem, self, other);
        internal static PyObject nm_mpd_qdivint(PyObject self, PyObject other) => Dec_BinaryNumberMethod(mpd_qdivint, self, other);

        // nm_dec_as_long
        internal static PyObject nm_dec_as_long(PyObject dec)
        {
            var context = CURRENT_CONTEXT();
            return dec_as_long(dec, context, MPD_ROUND_DOWN);
        }

        // nm_nonzero
        internal static bool nm_nonzero(PyObject v) => !mpd_iszero(MPD(v));

        // nm_mpd_qdivmod
        internal static PyObject nm_mpd_qdivmod(PyObject v, PyObject w)
        {
            uint status = 0;

            var context = CURRENT_CONTEXT();
            var a = convert_op(NOT_IMPL, v, context);
            if (a == null)
                return PyNotImplemented.Instance;
            var b = convert_op(NOT_IMPL, w, context);
            if (b == null)
                return PyNotImplemented.Instance;

            var q = dec_alloc();
            var r = dec_alloc();

            mpd_qdivmod(MPD(q), MPD(r), MPD(a), MPD(b), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return PyTuple.From(q, r);
        }

        // nm_mpd_qpow
        internal static PyObject nm_mpd_qpow(PyObject @base, PyObject exp, PyObject mod)
        {
            PyObject c = null;
            uint status = 0;

            var context = CURRENT_CONTEXT();
            var a = convert_op(NOT_IMPL, @base, context);
            if (a == null)
                return PyNotImplemented.Instance;
            var b = convert_op(NOT_IMPL, exp, context);
            if (b == null)
                return PyNotImplemented.Instance;

            if (mod is not PyNone)
            {
                c = convert_op(NOT_IMPL, mod, context);
                if (c == null)
                    return PyNotImplemented.Instance;
            }

            var result = dec_alloc();

            if (c == null)
                mpd_qpow(MPD(result), MPD(a), MPD(b), ref CTX(context), ref status);
            else
                mpd_qpowmod(MPD(result), MPD(a), MPD(b), MPD(c), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }


        /******************************************************************************/
        /*                             Decimal Methods                                */
        /******************************************************************************/

        /* Unary functions, no context arg */
        private static PyObject dec_mpd_adjexp(PyObject self)
        {
            long retval;

            if (mpd_isspecial(MPD(self)))
                retval = 0;
            else
                retval = mpd_adjexp(MPD(self));

            return PyInt.From(retval);
        }

        // dec_canonical
        private static PyObject dec_canonical(PyObject self) => self;

        // dec_conjugate
        private static PyObject dec_conjugate(PyObject self) => self;

        // dec_mpd_radix
        private static PyObject dec_mpd_radix(PyObject self)
        {
            var result = dec_alloc();

            _dec_settriple(result, MPD_POS, 10, 0);
            return result;
        }

        // dec_mpd_qcopy_abs
        private static PyObject dec_mpd_qcopy_abs(PyObject self)
        {
            uint status = 0;

            var result = dec_alloc();

            mpd_qcopy_abs(MPD(result), MPD(self), ref status);
            if ((status & MPD_Malloc_error) != 0)
                throw PyErr_NoMemory();

            return result;
        }

        // dec_mpd_qcopy_negate
        private static PyObject dec_mpd_qcopy_negate(PyObject self)
        {
            uint status = 0;

            var result = dec_alloc();

            mpd_qcopy_negate(MPD(result), MPD(self), ref status);
            if ((status & MPD_Malloc_error) != 0)
                throw PyErr_NoMemory();

            return result;
        }

        // dec_mpd_class
        private static PyObject dec_mpd_class(PyObject self, PyObject[] args, PyDict kwds)
        {
            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, context_only_kwlist, 0);
            var context = CONTEXT_CHECK_VA(parsed[0] ?? PyNone.Instance);

            var cp = mpd_class(MPD(self), ref CTX(context));
            return PyString.From(cp);
        }

        // dec_mpd_to_eng
        private static PyObject dec_mpd_to_eng(PyObject self, PyObject[] args, PyDict kwds)
        {
            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, context_only_kwlist, 0);
            var context = CONTEXT_CHECK_VA(parsed[0] ?? PyNone.Instance);

            long size = mpd_to_eng_size(out string s, MPD(self), CtxCaps(context));
            if (size < 0)
                throw PyErr_NoMemory();

            return unicode_fromascii(s);
        }

        // dec_mpd_qcopy_sign
        private static PyObject dec_mpd_qcopy_sign(PyObject self, PyObject[] args, PyDict kwds)
        {
            uint status = 0;

            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, other_context_kwlist, 1);
            var other = parsed[0];
            var context = CONTEXT_CHECK_VA(parsed[1] ?? PyNone.Instance);
            var a = convert_op(TYPE_ERR, self, context);
            var b = convert_op(TYPE_ERR, other, context);

            var result = dec_alloc();

            mpd_qcopy_sign(MPD(result), MPD(a), MPD(b), ref status);
            dec_addstatus(context, status);

            return result;
        }

        // dec_mpd_same_quantum
        private static PyObject dec_mpd_same_quantum(PyObject self, PyObject[] args, PyDict kwds)
        {
            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, other_context_kwlist, 1);
            var other = parsed[0];
            var context = CONTEXT_CHECK_VA(parsed[1] ?? PyNone.Instance);
            var a = convert_op(TYPE_ERR, self, context);
            var b = convert_op(TYPE_ERR, other, context);

            return mpd_same_quantum(MPD(a), MPD(b)) ? incr_true() : incr_false();
        }

        private static readonly string[] quantize_kwlist = { "exp", "rounding", "context" };

        // dec_mpd_qquantize
        private static PyObject dec_mpd_qquantize(PyObject v, PyObject[] args, PyDict kwds)
        {
            uint status = 0;

            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, quantize_kwlist, 1);
            var w = parsed[0];
            var rounding = parsed[1] ?? PyNone.Instance;
            var context = CONTEXT_CHECK_VA(parsed[2] ?? PyNone.Instance);

            mpd_context_t workctx = CTX(context);
            if (rounding is not PyNone)
            {
                int round = getround(rounding);
                if (!mpd_qsetround(ref workctx, round))
                    throw INTERNAL_ERROR("dec_mpd_qquantize"); /* GCOV_NOT_REACHED */
            }

            var a = convert_op(TYPE_ERR, v, context);
            var b = convert_op(TYPE_ERR, w, context);

            var result = dec_alloc();

            mpd_qquantize(MPD(result), MPD(a), MPD(b), ref workctx, ref status);
            dec_addstatus(context, status);

            return result;
        }

        /* Special methods */
        internal static PyObject dec_richcompare(PyObject v, PyObject w, PyObject.CompareOp op)
        {
            uint status = 0;

            var context = CURRENT_CONTEXT();
            // CONVERT_BINOP_CMP(&a, &b, v, w, op, context)
            if (!convert_op_cmp(out var a, out var b, v, w, op, context))
                return b;

            bool a_issnan = mpd_issnan(MPD(a));
            bool b_issnan = mpd_issnan(MPD(b));

            int r = mpd_qcmp(MPD(a), MPD(b), ref status);
            if (r == int.MaxValue)
            {
                /* sNaNs or op={le,ge,lt,gt} always signal. */
                if (a_issnan || b_issnan || (op != PyObject.CompareOp.EQ && op != PyObject.CompareOp.NE))
                    dec_addstatus(context, status);
                /* qNaN comparison with op={eq,ne} or comparison
                 * with InvalidOperation disabled. */
                return (op == PyObject.CompareOp.NE) ? incr_true() : incr_false();
            }

            bool res = op switch
            {
                PyObject.CompareOp.EQ => r == 0,
                PyObject.CompareOp.NE => r != 0,
                PyObject.CompareOp.LE => r <= 0,
                PyObject.CompareOp.GE => r >= 0,
                PyObject.CompareOp.LT => r == -1,
                _ => r == 1,
            };

            return PyBool.FromBool(res);
        }

        /* __ceil__ */
        private static PyObject dec_ceil(PyObject self)
        {
            var context = CURRENT_CONTEXT();
            return dec_as_long(self, context, MPD_ROUND_CEILING);
        }

        /* __complex__ */
        private static PyObject dec_complex(PyObject self)
        {
            var f = PyDec_AsFloat(self);

            double x = PyFloat.FloatValue(f).Value;

            return new PyComplex(x, 0);
        }

        /* __copy__ and __deepcopy__ */
        private static PyObject dec_copy(PyObject self) => self;

        /* __floor__ */
        private static PyObject dec_floor(PyObject self)
        {
            var context = CURRENT_CONTEXT();
            return dec_as_long(self, context, MPD_ROUND_FLOOR);
        }

        /* Always uses the module context */
        private static long _dec_hash(PyDecObject v)
        {
            // CONFIG_64 && _PyHASH_BITS == 61
            /* 2**61 - 1 */
            var p = new mpd_t(MPD_POS | MPD_STATIC | MPD_CONST_DATA, 0, 19, 1, 1, new ulong[] { 2305843009213693951UL });
            /* Inverse of 10 modulo p */
            var inv10_p = new mpd_t(MPD_POS | MPD_STATIC | MPD_CONST_DATA, 0, 19, 1, 1, new ulong[] { 2075258708292324556UL });
            const long py_hash_inf = 314159;
            var ten = new mpd_t(MPD_POS | MPD_STATIC | MPD_CONST_DATA, 0, 2, 1, 1, new ulong[] { 10 });
            long result;
            uint status = 0;
            mpd_context_t maxctx = default;

            if (mpd_isspecial(MPD(v)))
            {
                if (mpd_issnan(MPD(v)))
                    throw PyTypeError.Create("Cannot hash a signaling NaN value");
                else if (mpd_isnan(MPD(v)))
                    return _Py_HashPointer(v);
                else
                    return py_hash_inf * mpd_arith_sign(MPD(v));
            }

            mpd_maxcontext(ref maxctx);
            var exp_hash = mpd_qnew() ?? throw PyErr_NoMemory();
            var tmp = mpd_qnew() ?? throw PyErr_NoMemory();

            /*
             * exp(v): exponent of v
             * int(v): coefficient of v
             */
            long exp = MPD(v).exp;
            if (exp >= 0)
            {
                /* 10**exp(v) % p */
                mpd_qsset_ssize(tmp, exp, ref maxctx, ref status);
                mpd_qpowmod(exp_hash, ten, tmp, p, ref maxctx, ref status);
            }
            else
            {
                /* inv10_p**(-exp(v)) % p */
                mpd_qsset_ssize(tmp, -exp, ref maxctx, ref status);
                mpd_qpowmod(exp_hash, inv10_p, tmp, p, ref maxctx, ref status);
            }

            /* hash = (int(v) * exp_hash) % p */
            if (!mpd_qcopy(tmp, MPD(v), ref status))
                throw PyErr_NoMemory();
            tmp.exp = 0;
            mpd_set_positive(tmp);

            maxctx.prec = MPD_MAX_PREC + 21;
            maxctx.emax = MPD_MAX_EMAX + 21;
            maxctx.emin = MPD_MIN_EMIN - 21;

            mpd_qmul(tmp, tmp, exp_hash, ref maxctx, ref status);
            mpd_qrem(tmp, tmp, p, ref maxctx, ref status);

            result = mpd_qget_ssize(tmp, ref status);
            result = mpd_ispositive(MPD(v)) ? result : -result;
            result = (result == -1) ? -2 : result;

            if (status != 0)
            {
                if ((status & MPD_Malloc_error) != 0)
                    throw PyErr_NoMemory();
                throw PyRuntimeError.Create("dec_hash: internal error: please report"); /* GCOV_NOT_REACHED */
            }

            mpd_del(exp_hash);
            mpd_del(tmp);
            return result;
        }

        // _Py_HashPointer: the identity hash object.__hash__ gives.
        private static long _Py_HashPointer(PyObject v) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v);

        // dec_hash
        internal static long dec_hash(PyDecObject self)
        {
            if (self.hash == -1)
                self.hash = _dec_hash(self);

            return self.hash;
        }

        /* __reduce__ */
        private static PyObject dec_reduce(PyObject self)
        {
            var str = dec_str(self);

            return PyTuple.From(self.GetPyType(), PyTuple.From(str));
        }

        /* __sizeof__ */
        private static PyObject dec_sizeof(PyObject v)
        {
            // _PyObject_SIZE(Py_TYPE(v)): tp_basicsize of PyDecObject (104 on x64) or the subclass's.
            long res = v.GetPyType().Layout.BasicSize;
            if (mpd_isdynamic_data(MPD(v)))
                res += MPD(v).alloc * sizeof(ulong);
            return PyInt.From(res);
        }

        /* __trunc__ */
        private static PyObject dec_trunc(PyObject self)
        {
            var context = CURRENT_CONTEXT();
            return dec_as_long(self, context, MPD_ROUND_DOWN);
        }

        /* real and imag */
        private static PyObject dec_real(PyObject self) => self;

        // dec_imag
        private static PyObject dec_imag(PyObject self)
        {
            var result = dec_alloc();

            _dec_settriple(result, MPD_POS, 0, 0);
            return result;
        }
    }
}
