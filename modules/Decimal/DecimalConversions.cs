// Modules/_decimal/_decimal.c (CPython 3.12.15), part 2: new Decimal objects, conversions to
// Decimal (strings, ints, floats, tuples, Decimals), the implicit conversions of the operators and
// of the comparisons, and the conversions from Decimal (str, repr, format, int, integer ratio,
// float, round, as_tuple).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using SharpPy.Modules.Mpdecimal;
using static SharpPy.Modules.Mpdecimal.Mpd;

namespace SharpPy.Modules.CDecimal
{
    internal static partial class DecimalModule
    {
        /******************************************************************************/
        /*                           New Decimal Object                               */
        /******************************************************************************/

        // PyDecType_New
        internal static PyObject PyDecType_New(PyType type) => new PyDecObject(type);

        // #define dec_alloc() PyDecType_New(&PyDec_Type)
        internal static PyObject dec_alloc() => new PyDecObject(PyDec_Type);


        /******************************************************************************/
        /*                           Conversions to Decimal                           */
        /******************************************************************************/

        // is_space
        private static bool is_space(int ch) => UnicodeType.IsSpace(ch);

        // PyUnicode_AsUTF8AndSize: the UTF-8 bytes (UnicodeEncodeError for a lone surrogate).
        private static byte[] Utf8Bytes(PyString text) => Encoding.UTF8.GetBytes(PyArgs.AsUtf8(text));

        // PyUnicode_DecodeUTF8(s, size, NULL): strict decoding.
        private static string DecodeUtf8(byte[] s, int size) =>
            ((PyString)PyCodecRegistry.Decode(PyBytes.From(s.AsSpan(0, size).ToArray()), "utf-8", "strict")).Value;

        // The code points of a str (PyUnicode_READ over its kind).
        private static int[] CodePoints(PyString u)
        {
            var s = u.Value;
            var points = new List<int>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    points.Add(char.ConvertToUtf32(c, s[i + 1]));
                    i++;
                }
                else
                {
                    points.Add(c);
                }
            }
            return points.ToArray();
        }

        /* Return the ASCII representation of a numeric Unicode string. The numeric
           string may contain ascii characters in the range [1, 127], any Unicode
           space and any unicode digit. If strip_ws is true, leading and trailing
           whitespace is stripped. If ignore_underscores is true, underscores are
           ignored.

           Return NULL if malloc fails and an empty string if invalid characters
           are found. */
        private static string numeric_as_ascii(PyObject u, bool strip_ws, bool ignore_underscores)
        {
            var data = CodePoints((PyString)u);
            int len = data.Length;
            var res = new StringBuilder(len);

            int j = 0;
            if (strip_ws)
            {
                while (len > 0 && is_space(data[len - 1]))
                    len--;
                while (j < len && is_space(data[j]))
                    j++;
            }

            for (; j < len; j++)
            {
                int ch = data[j];
                if (ignore_underscores && ch == '_')
                    continue;
                if (0 < ch && ch <= 127)
                {
                    res.Append((char)ch);
                    continue;
                }
                if (UnicodeType.IsSpace(ch))
                {
                    res.Append(' ');
                    continue;
                }
                int d = UnicodeType.ToDecimalDigit(ch);
                if (d < 0)
                {
                    /* empty string triggers ConversionSyntax */
                    return "";
                }
                res.Append((char)('0' + d));
            }
            return res.ToString();
        }

        /* Return a new PyDecObject or a subtype from a C string. Use the context
           during conversion. */
        private static PyObject PyDecType_FromCString(PyType type, string s, PyObject context)
        {
            uint status = 0;

            var dec = PyDecType_New(type);

            mpd_qset_string(MPD(dec), s, ref CTX(context), ref status);
            dec_addstatus(context, status);
            return dec;
        }

        /* Return a new PyDecObject or a subtype from a C string. Attempt exact
           conversion. If the operand cannot be converted exactly, set
           InvalidOperation. */
        private static PyObject PyDecType_FromCStringExact(PyType type, string s, PyObject context)
        {
            uint status = 0;
            mpd_context_t maxctx = default;

            var dec = PyDecType_New(type);

            mpd_maxcontext(ref maxctx);

            mpd_qset_string(MPD(dec), s, ref maxctx, ref status);
            if ((status & (MPD_Inexact | MPD_Rounded | MPD_Clamped)) != 0)
            {
                /* we want exact results */
                mpd_seterror(MPD(dec), MPD_Invalid_operation, ref status);
            }
            status &= MPD_Errors;
            dec_addstatus(context, status);

            return dec;
        }

        /* Return a new PyDecObject or a subtype from a PyUnicodeObject. */
        private static PyObject PyDecType_FromUnicode(PyType type, PyObject u, PyObject context)
        {
            var s = numeric_as_ascii(u, false, false);

            return PyDecType_FromCString(type, s, context);
        }

        /* Return a new PyDecObject or a subtype from a PyUnicodeObject. Attempt exact
         * conversion. If the conversion is not exact, fail with InvalidOperation.
         * Allow leading and trailing whitespace in the input operand. */
        private static PyObject PyDecType_FromUnicodeExactWS(PyType type, PyObject u, PyObject context)
        {
            var s = numeric_as_ascii(u, true, true);

            return PyDecType_FromCStringExact(type, s, context);
        }

        /* Set PyDecObject from triple without any error checking. */
        private static void _dec_settriple(PyObject dec, byte sign, uint v, long exp)
        {
            // CONFIG_64
            MPD(dec).data[0] = v;
            MPD(dec).len = 1;
            mpd_set_flags(MPD(dec), sign);
            MPD(dec).exp = exp;
            mpd_setdigits(MPD(dec));
        }

        /* Return a new PyDecObject from an mpd_ssize_t. */
        private static PyObject PyDecType_FromSsize(PyType type, long v, PyObject context)
        {
            uint status = 0;

            var dec = PyDecType_New(type);

            mpd_qset_ssize(MPD(dec), v, ref CTX(context), ref status);
            dec_addstatus(context, status);
            return dec;
        }

        /* Return a new PyDecObject from an mpd_ssize_t. Conversion is exact. */
        private static PyObject PyDecType_FromSsizeExact(PyType type, long v, PyObject context)
        {
            uint status = 0;
            mpd_context_t maxctx = default;

            var dec = PyDecType_New(type);

            mpd_maxcontext(ref maxctx);

            mpd_qset_ssize(MPD(dec), v, ref maxctx, ref status);
            dec_addstatus(context, status);
            return dec;
        }

        // PyLong_SHIFT, PyLong_BASE (PYLONG_BITS_IN_DIGIT == 30)
        private const int PyLong_SHIFT = 30;
        private const uint PyLong_BASE = 1U << PyLong_SHIFT;
        private const uint PyLong_MASK = PyLong_BASE - 1;

        // long_value.ob_digit of an int: its absolute value in 30-bit digits, least significant first.
        private static uint[] PyLongDigits(BigInteger value)
        {
            var abs = BigInteger.Abs(value);
            var bytes = abs.ToByteArray(isUnsigned: true, isBigEndian: false);
            long bits = (long)bytes.Length * 8;
            int ndigits = (int)((bits + PyLong_SHIFT - 1) / PyLong_SHIFT);
            var digits = new uint[Math.Max(ndigits, 1)];
            int count = 0;
            ulong acc = 0;
            int accbits = 0;
            foreach (var b in bytes)
            {
                acc |= (ulong)b << accbits;
                accbits += 8;
                if (accbits >= PyLong_SHIFT)
                {
                    digits[count++] = (uint)(acc & PyLong_MASK);
                    acc >>= PyLong_SHIFT;
                    accbits -= PyLong_SHIFT;
                }
            }
            if (accbits > 0)
                digits[count++] = (uint)acc;
            // _PyLong_DigitCount: without the leading zero digits.
            while (count > 0 && digits[count - 1] == 0)
                count--;
            Array.Resize(ref digits, count);
            return digits;
        }

        // _PyLong_FromDigits(negative, n, digits)
        private static PyObject _PyLong_FromDigits(bool negative, ulong n, uint[] digits)
        {
            var bytes = new byte[(n * PyLong_SHIFT + 7) / 8 + 1];
            ulong acc = 0;
            int accbits = 0;
            int pos = 0;
            for (ulong i = 0; i < n; i++)
            {
                acc |= (ulong)digits[i] << accbits;
                accbits += PyLong_SHIFT;
                while (accbits >= 8)
                {
                    bytes[pos++] = (byte)acc;
                    acc >>= 8;
                    accbits -= 8;
                }
            }
            while (accbits > 0)
            {
                bytes[pos++] = (byte)acc;
                acc >>= 8;
                accbits -= 8;
            }
            var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
            return PyInt.From(negative ? -value : value);
        }

        /* Convert from a PyLongObject. The context is not modified; flags set
           during conversion are accumulated in the status parameter. */
        private static PyObject dec_from_long(PyType type, PyObject v, ref mpd_context_t ctx, ref uint status)
        {
            var l = PyInt.LongValue(v).Value;

            var dec = PyDecType_New(type);

            if (l.IsZero)
            {
                _dec_settriple(dec, MPD_POS, 0, 0);
                return dec;
            }

            byte sign = l.Sign < 0 ? MPD_NEG : MPD_POS;

            // _PyLong_IsCompact: one digit.
            if (l > -(long)PyLong_BASE && l < PyLong_BASE)
            {
                _dec_settriple(dec, sign, (uint)(long)BigInteger.Abs(l), 0);
                mpd_qfinalize(MPD(dec), ref ctx, ref status);
                return dec;
            }
            var ob_digit = PyLongDigits(l);
            ulong len = (ulong)ob_digit.Length;

            // PYLONG_BITS_IN_DIGIT == 30
            mpd_qimport_u32(MPD(dec), ob_digit, len, sign, PyLong_BASE, ref ctx, ref status);

            return dec;
        }

        /* Return a new PyDecObject from a PyLongObject. Use the context for
           conversion. */
        private static PyObject PyDecType_FromLong(PyType type, PyObject v, PyObject context)
        {
            uint status = 0;

            if (!PyInt.LongCheck(v))
                throw PyTypeError.Create("argument must be an integer");

            var dec = dec_from_long(type, v, ref CTX(context), ref status);

            dec_addstatus(context, status);

            return dec;
        }

        /* Return a new PyDecObject from a PyLongObject. Use a maximum context
           for conversion. If the conversion is not exact, set InvalidOperation. */
        private static PyObject PyDecType_FromLongExact(PyType type, PyObject v, PyObject context)
        {
            uint status = 0;
            mpd_context_t maxctx = default;

            if (!PyInt.LongCheck(v))
                throw PyTypeError.Create("argument must be an integer");

            mpd_maxcontext(ref maxctx);
            var dec = dec_from_long(type, v, ref maxctx, ref status);

            if ((status & (MPD_Inexact | MPD_Rounded | MPD_Clamped)) != 0)
            {
                /* we want exact results */
                mpd_seterror(MPD(dec), MPD_Invalid_operation, ref status);
            }
            status &= MPD_Errors;
            dec_addstatus(context, status);

            return dec;
        }

        /* Return a PyDecObject or a subtype from a PyFloatObject.
           Conversion is exact. */
        private static PyObject PyDecType_FromFloatExact(PyType type, PyObject v, PyObject context)
        {
            uint status = 0;
            mpd_context_t maxctx = default;

            if (PyInt.LongCheck(v))
                return PyDecType_FromLongExact(type, v, context);
            if (v is not PyFloat)
                throw PyTypeError.Create("argument must be int or float");

            double x = PyFloat.FloatValue(v).Value;
            int sign = (Math.CopySign(1.0, x) == 1.0) ? 0 : 1;

            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                var special = PyDecType_New(type);
                if (double.IsNaN(x))
                {
                    /* decimal.py calls repr(float(+-nan)),
                     * which always gives a positive result. */
                    mpd_setspecial(MPD(special), MPD_POS, MPD_NAN);
                }
                else
                {
                    mpd_setspecial(MPD(special), (byte)sign, MPD_INF);
                }
                return special;
            }

            /* absolute value of the float */
            var tmp = _py_float_abs(v);

            /* float as integer ratio: numerator/denominator */
            var n_d = (PyTuple)_py_float_as_integer_ratio.Call(new[] { tmp }, null);
            var n = n_d.Items[0];
            var d = n_d.Items[1];

            tmp = _py_long_bit_length.Call(new[] { d }, null);
            long k = PyLong_AsSsize_t(tmp);
            k--;

            var dec = PyDecType_FromLongExact(type, n, context);

            var d1 = mpd_qnew() ?? throw PyErr_NoMemory();
            var d2 = mpd_qnew() ?? throw PyErr_NoMemory();

            mpd_maxcontext(ref maxctx);
            mpd_qset_uint(d1, 5, ref maxctx, ref status);
            mpd_qset_ssize(d2, k, ref maxctx, ref status);
            mpd_qpow(d1, d1, d2, ref maxctx, ref status);
            dec_addstatus(context, status);

            /* result = n * 5**k */
            mpd_qmul(MPD(dec), MPD(dec), d1, ref maxctx, ref status);
            mpd_del(d1);
            mpd_del(d2);
            dec_addstatus(context, status);
            /* result = +- n * 5**k * 10**-k */
            mpd_set_sign(MPD(dec), (byte)sign);
            MPD(dec).exp = -k;

            return dec;
        }

        // PyDecType_FromFloat
        private static PyObject PyDecType_FromFloat(PyType type, PyObject v, PyObject context)
        {
            uint status = 0;

            var dec = PyDecType_FromFloatExact(type, v, context);

            mpd_qfinalize(MPD(dec), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return dec;
        }

        /* Return a new PyDecObject or a subtype from a Decimal. */
        private static PyObject PyDecType_FromDecimalExact(PyType type, PyObject v, PyObject context)
        {
            uint status = 0;

            if (ReferenceEquals(type, PyDec_Type) && PyDec_CheckExact(v))
                return v;

            var dec = PyDecType_New(type);

            mpd_qcopy(MPD(dec), MPD(v), ref status);
            dec_addstatus(context, status);

            return dec;
        }

        // sequence_as_tuple
        private static PyTuple sequence_as_tuple(PyObject v, Func<string, Exception> ex, string mesg)
        {
            if (v is PyTuple tuple)
                return tuple;
            if (v is PyList list)
                return PyTuple.From(list.Items);

            throw ex(mesg);
        }

        /* Return a new C string representation of a DecimalTuple. */
        private static string dectuple_as_str(PyTuple dectuple)
        {
            long exp = 0;
            bool is_infinite = false;

            if (dectuple.Items.Length != 3)
                throw PyValueError.Create("argument must be a sequence of length 3");

            /* sign */
            var tmp = dectuple.Items[0];
            if (!PyInt.LongCheck(tmp))
                throw PyValueError.Create("sign must be an integer with the value 0 or 1");
            long sign = PyLong_AsLong(tmp);
            if (sign != 0 && sign != 1)
                throw PyValueError.Create("sign must be an integer with the value 0 or 1");
            var sign_special = new StringBuilder(sign != 0 ? "-" : "+");

            /* exponent or encoding for a special number */
            tmp = dectuple.Items[2];
            if (tmp is PyString special)
            {
                /* special */
                if (special.Value == "F")
                {
                    sign_special.Append("Inf");
                    is_infinite = true;
                }
                else if (special.Value == "n")
                {
                    sign_special.Append("NaN");
                }
                else if (special.Value == "N")
                {
                    sign_special.Append("sNaN");
                }
                else
                {
                    throw PyValueError.Create("string argument in the third position must be 'F', 'n' or 'N'");
                }
            }
            else
            {
                /* exponent */
                if (!PyInt.LongCheck(tmp))
                    throw PyValueError.Create("exponent must be an integer");
                exp = PyLong_AsSsize_t(tmp);
            }

            /* coefficient */
            var digits = sequence_as_tuple(dectuple.Items[1], m => PyValueError.Create(m),
                "coefficient must be a tuple of digits");

            var decstring = new StringBuilder();
            decstring.Append(sign_special);

            int tsize = digits.Items.Length;
            if (tsize == 0 && sign_special.Length == 1)
            {
                /* empty tuple: zero coefficient, except for special numbers */
                decstring.Append('0');
            }
            for (int i = 0; i < tsize; i++)
            {
                tmp = digits.Items[i];
                if (!PyInt.LongCheck(tmp))
                    throw PyValueError.Create("coefficient must be a tuple of digits");
                long l = PyLong_AsLong(tmp);
                if (l < 0 || l > 9)
                    throw PyValueError.Create("coefficient must be a tuple of digits");
                if (is_infinite)
                {
                    /* accept but ignore any well-formed coefficient for compatibility
                       with decimal.py */
                    continue;
                }
                decstring.Append((char)(l + '0'));
            }

            if (sign_special.Length == 1)
            {
                /* not a special number */
                decstring.Append('E');
                decstring.Append(exp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return decstring.ToString();
        }

        /* Currently accepts tuples and lists. */
        private static PyObject PyDecType_FromSequence(PyType type, PyObject v, PyObject context)
        {
            var dectuple = sequence_as_tuple(v, m => PyTypeError.Create(m), "argument must be a tuple or list");

            var s = dectuple_as_str(dectuple);

            return PyDecType_FromCString(type, s, context);
        }

        /* Currently accepts tuples and lists. */
        private static PyObject PyDecType_FromSequenceExact(PyType type, PyObject v, PyObject context)
        {
            var dectuple = sequence_as_tuple(v, m => PyTypeError.Create(m), "argument must be a tuple or list");

            var s = dectuple_as_str(dectuple);

            return PyDecType_FromCStringExact(type, s, context);
        }

        // PyDec_FromCString ... PyDec_FromSequenceExact
        private static PyObject PyDec_FromUnicode(PyObject unicode, PyObject context) => PyDecType_FromUnicode(PyDec_Type, unicode, context);
        private static PyObject PyDec_FromSsize(long v, PyObject context) => PyDecType_FromSsize(PyDec_Type, v, context);
        private static PyObject PyDec_FromLong(PyObject pylong, PyObject context) => PyDecType_FromLong(PyDec_Type, pylong, context);
        private static PyObject PyDec_FromLongExact(PyObject pylong, PyObject context) => PyDecType_FromLongExact(PyDec_Type, pylong, context);
        private static PyObject PyDec_FromFloat(PyObject pyfloat, PyObject context) => PyDecType_FromFloat(PyDec_Type, pyfloat, context);
        private static PyObject PyDec_FromFloatExact(PyObject pyfloat, PyObject context) => PyDecType_FromFloatExact(PyDec_Type, pyfloat, context);
        private static PyObject PyDec_FromSequence(PyObject sequence, PyObject context) => PyDecType_FromSequence(PyDec_Type, sequence, context);

        /* class method */
        private static PyObject dec_from_float(PyObject type, PyObject pyfloat)
        {
            var context = CURRENT_CONTEXT();
            var result = PyDecType_FromFloatExact(PyDec_Type, pyfloat, context);
            if (!ReferenceEquals(type, PyDec_Type) && result != null)
                result = type.Call(new[] { result }, null);

            return result;
        }

        /* create_decimal_from_float */
        private static PyObject ctx_from_float(PyObject context, PyObject v) => PyDec_FromFloat(v, context);

        /* Apply the context to the input operand. Return a new PyDecObject. */
        private static PyObject dec_apply(PyObject v, PyObject context)
        {
            uint status = 0;

            var result = dec_alloc();

            mpd_qcopy(MPD(result), MPD(v), ref status);
            dec_addstatus(context, status);

            mpd_qfinalize(MPD(result), ref CTX(context), ref status);
            dec_addstatus(context, status);

            return result;
        }

        /* 'v' can have any type accepted by the Decimal constructor. Attempt
           an exact conversion. If the result does not meet the restrictions
           for an mpd_t, fail with InvalidOperation. */
        private static PyObject PyDecType_FromObjectExact(PyType type, PyObject v, PyObject context)
        {
            if (v == null)
                return PyDecType_FromSsizeExact(type, 0, context);
            else if (PyDec_Check(v))
                return PyDecType_FromDecimalExact(type, v, context);
            else if (v is PyString)
                return PyDecType_FromUnicodeExactWS(type, v, context);
            else if (PyInt.LongCheck(v))
                return PyDecType_FromLongExact(type, v, context);
            else if (v is PyTuple || v is PyList)
                return PyDecType_FromSequenceExact(type, v, context);
            else if (v is PyFloat)
            {
                dec_addstatus(context, MPD_Float_operation);
                return PyDecType_FromFloatExact(type, v, context);
            }
            else
            {
                throw PyTypeError.Create($"conversion from {v.GetPyType().FullName} to Decimal is not supported");
            }
        }

        /* The context is used during conversion. This function is the
           equivalent of context.create_decimal(). */
        private static PyObject PyDec_FromObject(PyObject v, PyObject context)
        {
            if (v == null)
            {
                return PyDec_FromSsize(0, context);
            }
            else if (PyDec_Check(v))
            {
                ref mpd_context_t ctx = ref CTX(context);
                if (mpd_isnan(MPD(v)) && MPD(v).digits > ctx.prec - ctx.clamp)
                {
                    /* Special case: too many NaN payload digits */
                    dec_addstatus(context, MPD_Conversion_syntax);
                    var result = dec_alloc();
                    mpd_setspecial(MPD(result), MPD_POS, MPD_NAN);
                    return result;
                }
                return dec_apply(v, context);
            }
            else if (v is PyString)
            {
                return PyDec_FromUnicode(v, context);
            }
            else if (PyInt.LongCheck(v))
            {
                return PyDec_FromLong(v, context);
            }
            else if (v is PyTuple || v is PyList)
            {
                return PyDec_FromSequence(v, context);
            }
            else if (v is PyFloat)
            {
                dec_addstatus(context, MPD_Float_operation);
                return PyDec_FromFloat(v, context);
            }
            else
            {
                throw PyTypeError.Create($"conversion from {v.GetPyType().FullName} to Decimal is not supported");
            }
        }

        private static readonly string[] dec_new_kwlist = { "value", "context" };

        // dec_new
        private static PyObject dec_new(PyType type, PyObject[] args, PyDict kwds)
        {
            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, dec_new_kwlist, 0);
            var v = parsed[0];
            var context = CONTEXT_CHECK_VA(parsed[1] ?? PyNone.Instance);

            return PyDecType_FromObjectExact(type, v, context);
        }

        // ctx_create_decimal
        private static PyObject ctx_create_decimal(PyObject context, PyObject[] args)
        {
            PyArg_ParseTuple(args, 0, 1);
            var v = args.Length > 0 ? args[0] : null;

            return PyDec_FromObject(v, context);
        }


        /******************************************************************************/
        /*                        Implicit conversions to Decimal                     */
        /******************************************************************************/

        /* Try to convert PyObject v to a new PyDecObject conv. If the conversion
           fails, set conv to NULL (exception is set). If the conversion is not
           implemented, set conv to Py_NotImplemented. */
        private const bool NOT_IMPL = false;
        private const bool TYPE_ERR = true;

        // convert_op: the Decimal, or null for NotImplemented; TypeError when type_err.
        private static PyObject convert_op(bool type_err, PyObject v, PyObject context)
        {
            if (PyDec_Check(v))
                return v;
            if (PyInt.LongCheck(v))
                return PyDec_FromLongExact(v, context);

            if (type_err)
                throw PyTypeError.Create($"conversion from {v.GetPyType().FullName} to Decimal is not supported");
            return null;
        }


        /******************************************************************************/
        /*              Implicit conversions to Decimal for comparison                */
        /******************************************************************************/

        /* Convert rationals for comparison */
        private static PyObject Rational;

        // multiply_by_denominator
        private static PyObject multiply_by_denominator(PyObject v, PyObject r, PyObject context)
        {
            uint status = 0;
            mpd_context_t maxctx = default;

            /* v is not special, r is a rational */
            var tmp = r.GetAttribute("denominator");
            var denom = PyDec_FromLongExact(tmp, context);

            var vv = mpd_qncopy(MPD(v)) ?? throw PyErr_NoMemory();
            var result = dec_alloc();

            mpd_maxcontext(ref maxctx);
            /* Prevent Overflow in the following multiplication. The result of
               the multiplication is only used in mpd_qcmp, which can handle
               values that are technically out of bounds, like (for 32-bit)
               99999999999999999999...99999999e+425000000. */
            long exp = vv.exp;
            vv.exp = 0;
            mpd_qmul(MPD(result), vv, MPD(denom), ref maxctx, ref status);
            MPD(result).exp = exp;

            mpd_del(vv);
            /* If any status has been accumulated during the multiplication,
               the result is invalid. This is very unlikely, since even the
               32-bit version supports 425000000 digits. */
            if (status != 0)
                throw PyValueError.Create("exact conversion for comparison failed");

            return result;
        }

        // numerator_as_decimal
        private static PyObject numerator_as_decimal(PyObject r, PyObject context)
        {
            var tmp = r.GetAttribute("numerator");

            return PyDec_FromLongExact(tmp, context);
        }

        /* Convert v and w for comparison. v is a Decimal. If w is a Rational, both
           v and w have to be transformed. Return 1 for success, with new references
           to the converted objects in vcmp and wcmp. Return 0 for failure. In that
           case wcmp is either NULL or Py_NotImplemented (new reference) and vcmp
           is undefined. */
        private static bool convert_op_cmp(out PyObject vcmp, out PyObject wcmp, PyObject v, PyObject w,
            PyObject.CompareOp op, PyObject context)
        {
            ref mpd_context_t ctx = ref CTX(context);

            vcmp = v;

            if (PyDec_Check(w))
            {
                wcmp = w;
            }
            else if (PyInt.LongCheck(w))
            {
                wcmp = PyDec_FromLongExact(w, context);
            }
            else if (w is PyFloat)
            {
                if (op != PyObject.CompareOp.EQ && op != PyObject.CompareOp.NE)
                    dec_addstatus(context, MPD_Float_operation);
                else
                    ctx.status |= MPD_Float_operation;
                wcmp = PyDec_FromFloatExact(w, context);
            }
            else if (w is PyComplex c && (op == PyObject.CompareOp.EQ || op == PyObject.CompareOp.NE))
            {
                if (c.Imag == 0.0)
                {
                    var tmp = new PyFloat(c.Real);
                    ctx.status |= MPD_Float_operation;
                    wcmp = PyDec_FromFloatExact(tmp, context);
                }
                else
                {
                    wcmp = PyNotImplemented.Instance;
                }
            }
            else
            {
                bool is_rational = BuiltinsModule.IsInstance(new[] { w, Rational }).IsTrue();
                if (is_rational)
                {
                    wcmp = numerator_as_decimal(w, context);
                    if (!mpd_isspecial(MPD(v)))
                        vcmp = multiply_by_denominator(v, w, context);
                }
                else
                {
                    wcmp = PyNotImplemented.Instance;
                }
            }

            if (ReferenceEquals(wcmp, PyNotImplemented.Instance))
                return false;
            return true;
        }


        /******************************************************************************/
        /*                          Conversions from decimal                          */
        /******************************************************************************/

        // unicode_fromascii
        private static PyObject unicode_fromascii(string s) => PyString.From(s);

        /* PyDecObject as a string. The default module context is only used for
           the value of 'capitals'. */
        internal static PyObject dec_str(PyObject dec)
        {
            var context = CURRENT_CONTEXT();
            long size = mpd_to_sci_size(out string cp, MPD(dec), CtxCaps(context));
            if (size < 0)
                throw PyErr_NoMemory();

            return unicode_fromascii(cp);
        }

        /* Representation of a PyDecObject. */
        internal static PyObject dec_repr(PyObject dec)
        {
            var context = CURRENT_CONTEXT();
            var cp = mpd_to_sci(MPD(dec), CtxCaps(context)) ?? throw PyErr_NoMemory();

            return PyString.From("Decimal('" + cp + "')");
        }

        // dec_replace_fillchar
        private static void dec_replace_fillchar(byte[] dest)
        {
            for (int i = 0; i < dest.Length; i++)
            {
                if (dest[i] == 0xff) dest[i] = 0;
            }
        }

        /* Convert decimal_point or thousands_sep, which may be multibyte or in
           the range [128, 255], to a UTF8 string. */
        private static byte[] dotsep_as_utf8(byte[] s)
        {
            // mbstowcs(buf, s, 2): the localeconv strings are UTF-8 here (the hook gives them so).
            string decoded;
            try
            {
                decoded = new UTF8Encoding(false, true).GetString(s);
            }
            catch (ArgumentException)
            {
                decoded = null;
            }
            if (decoded == null || CodePointCount(decoded) != 1)
            {
                /* Issue #7442 */
                throw PyValueError.Create("invalid decimal point or unsupported combination of LC_CTYPE and LC_NUMERIC");
            }
            return Encoding.UTF8.GetBytes(decoded);
        }

        private static int CodePointCount(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++, n++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    i++;
            }
            return n;
        }

        // dict_get_item_string: the value's UTF-8 bytes, or null when the key is absent.
        private static byte[] dict_get_item_string(PyDict dict, string key)
        {
            if (!dict.TryGetStoredValue(PyString.From(key), out var value))
                return null;
            // PyUnicode_AsUTF8String
            if (value is not PyString text)
                throw PyTypeError.Create("bad argument type for built-in operation");
            return Utf8Bytes(text);
        }

        /*
         * Fallback _pydecimal formatting for new format specifiers that mpdecimal does
         * not yet support. As documented, libmpdec follows the PEP-3101 format language:
         * https://www.bytereef.org/mpdecimal/doc/libmpdec/assign-convert.html#to-string
         */
        private static PyObject pydec_format(PyObject dec, PyObject context, PyObject fmt)
        {
            if (PyDecimal == null)
                PyDecimal = PyImportSystem.Import("_pydecimal").GetAttribute("Decimal");

            var u = dec_str(dec);

            var pydec = PyDecimal.Call(new[] { u }, null);

            try
            {
                return pydec.GetAttribute("__format__").Call(new[] { fmt, context }, null);
            }
            catch (PythonException e) when (e.PyException is PyValueError)
            {
                /* Do not confuse users with the _pydecimal exception */
                throw PyValueError.Create("invalid format string");
            }
        }

        /* Formatted representation of a PyDecObject. */
        private static PyObject dec_format(PyObject dec, PyObject[] args)
        {
            var context = CURRENT_CONTEXT();
            PyArg_ParseTuple(args, 1, 2);
            var fmtarg = args[0];
            var @override = args.Length > 1 ? args[1] : null;
            var spec = new mpd_spec_t();
            uint status = 0;
            bool replace_fillchar = false;
            byte[] fmt;

            if (fmtarg is PyString fmtstr)
            {
                fmt = Utf8Bytes(fmtstr);

                if (fmt.Length > 0 && fmt[0] == 0)
                {
                    /* NUL fill character: must be replaced with a valid UTF-8 char
                       before calling mpd_parse_fmt_str(). */
                    replace_fillchar = true;
                    fmt = (byte[])fmt.Clone();
                    fmt[0] = (byte)'_';
                }
            }
            else
            {
                throw PyTypeError.Create("format arg must be str");
            }

            if (!mpd_parse_fmt_str(spec, fmt, CtxCaps(context)))
                return pydec_format(dec, context, fmtarg);

            if (replace_fillchar)
            {
                /* In order to avoid clobbering parts of UTF-8 thousands separators or
                   decimal points when the substitution is reversed later, the actual
                   placeholder must be an invalid UTF-8 byte. */
                spec.fill[0] = 0xff;
                spec.fill[1] = 0;
            }

            if (@override != null)
            {
                /* Values for decimal_point, thousands_sep and grouping can
                   be explicitly specified in the override dict. These values
                   take precedence over the values obtained from localeconv()
                   in mpd_parse_fmt_str(). The feature is not documented and
                   is only used in test_decimal. */
                if (@override is not PyDict overrides)
                    throw PyTypeError.Create("optional argument must be a dict");
                spec.dot = dict_get_item_string(overrides, "decimal_point") ?? spec.dot;
                spec.sep = dict_get_item_string(overrides, "thousands_sep") ?? spec.sep;
                spec.grouping = dict_get_item_string(overrides, "grouping") ?? spec.grouping;
                if (mpd_validate_lconv(spec) < 0)
                    throw PyValueError.Create("invalid override dict");
            }
            else
            {
                int n = spec.dot.Length;
                if (n > 1 || (n == 1 && spec.dot[0] >= 128))
                {
                    /* fix locale dependent non-ascii characters */
                    spec.dot = dotsep_as_utf8(spec.dot);
                }
                n = spec.sep.Length;
                if (n > 1 || (n == 1 && spec.sep[0] >= 128))
                {
                    /* fix locale dependent non-ascii characters */
                    spec.sep = dotsep_as_utf8(spec.sep);
                }
            }

            var decstring = mpd_qformat_spec(MPD(dec), spec, ref CTX(context), ref status);
            if (decstring == null)
            {
                if ((status & MPD_Malloc_error) != 0)
                    throw PyErr_NoMemory();
                throw PyValueError.Create("format specification exceeds internal limits of _decimal");
            }
            // size = strlen(decstring)
            int size = Array.IndexOf(decstring, (byte)0);
            if (size < 0)
                size = decstring.Length;
            if (replace_fillchar)
                dec_replace_fillchar(decstring);

            // PyUnicode_DecodeUTF8(decstring, size, NULL)
            return PyString.From(DecodeUtf8(decstring, size));
        }

        /* Return a PyLongObject from a PyDecObject, using the specified rounding
         * mode. The context precision is not observed. */
        private static PyObject dec_as_long(PyObject dec, PyObject context, int round)
        {
            uint status = 0;

            if (mpd_isspecial(MPD(dec)))
            {
                if (mpd_isnan(MPD(dec)))
                    throw PyValueError.Create("cannot convert NaN to integer");
                throw PyOverflowError.Create("cannot convert Infinity to integer");
            }

            var x = mpd_qnew() ?? throw PyErr_NoMemory();
            mpd_context_t workctx = CTX(context);
            workctx.round = round;
            mpd_qround_to_int(x, MPD(dec), ref workctx, ref status);
            dec_addstatus(context, status);

            status = 0;
            uint[] ob_digit = null;
            // PYLONG_BITS_IN_DIGIT == 30
            ulong n = mpd_qexport_u32(ref ob_digit, 0, PyLong_BASE, x, ref status);

            if (n == MPD_SIZE_MAX)
                throw PyErr_NoMemory();

            if (n == 1)
            {
                long val = mpd_arith_sign(x) * (long)ob_digit[0];
                return PyInt.From(val);
            }

            return _PyLong_FromDigits(mpd_isnegative(x), n, ob_digit);
        }

        /* Convert a Decimal to its exact integer ratio representation. */
        private static PyObject dec_as_integer_ratio(PyObject self)
        {
            uint status = 0;

            if (mpd_isspecial(MPD(self)))
            {
                if (mpd_isnan(MPD(self)))
                    throw PyValueError.Create("cannot convert NaN to integer ratio");
                throw PyOverflowError.Create("cannot convert Infinity to integer ratio");
            }

            var context = CURRENT_CONTEXT();

            var tmp = dec_alloc();

            if (!mpd_qcopy(MPD(tmp), MPD(self), ref status))
                throw PyErr_NoMemory();

            long exp = mpd_iszero(MPD(tmp)) ? 0 : MPD(tmp).exp;
            MPD(tmp).exp = 0;

            /* context and rounding are unused here: the conversion is exact */
            var numerator = dec_as_long(tmp, context, MPD_ROUND_FLOOR);

            PyObject exponent = PyInt.From(exp < 0 ? -exp : exp);

            tmp = PyInt.From(10);

            exponent = _py_long_power(tmp, exponent, PyNone.Instance);

            PyObject denominator;
            if (exp >= 0)
            {
                numerator = _py_long_multiply(numerator, exponent);
                denominator = PyInt.From(1);
            }
            else
            {
                denominator = exponent;
                // _PyLong_GCD
                tmp = PyInt.From(BigInteger.GreatestCommonDivisor(PyInt.LongValue(numerator).Value, PyInt.LongValue(denominator).Value));
                numerator = _py_long_floor_divide(numerator, tmp);
                denominator = _py_long_floor_divide(denominator, tmp);
            }

            return PyTuple.From(numerator, denominator);
        }

        private static readonly string[] to_integral_kwlist = { "rounding", "context" };

        // PyDec_ToIntegralValue
        private static PyObject PyDec_ToIntegralValue(PyObject dec, PyObject[] args, PyDict kwds)
        {
            uint status = 0;

            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, to_integral_kwlist, 0);
            var rounding = parsed[0] ?? PyNone.Instance;
            var context = CONTEXT_CHECK_VA(parsed[1] ?? PyNone.Instance);

            mpd_context_t workctx = CTX(context);
            if (rounding is not PyNone)
            {
                int round = getround(rounding);
                if (!mpd_qsetround(ref workctx, round))
                    throw INTERNAL_ERROR("PyDec_ToIntegralValue"); /* GCOV_NOT_REACHED */
            }

            var result = dec_alloc();

            mpd_qround_to_int(MPD(result), MPD(dec), ref workctx, ref status);
            dec_addstatus(context, status);

            return result;
        }

        // PyDec_ToIntegralExact
        private static PyObject PyDec_ToIntegralExact(PyObject dec, PyObject[] args, PyDict kwds)
        {
            uint status = 0;

            var parsed = PyArg_ParseTupleAndKeywords(args, kwds, to_integral_kwlist, 0);
            var rounding = parsed[0] ?? PyNone.Instance;
            var context = CONTEXT_CHECK_VA(parsed[1] ?? PyNone.Instance);

            mpd_context_t workctx = CTX(context);
            if (rounding is not PyNone)
            {
                int round = getround(rounding);
                if (!mpd_qsetround(ref workctx, round))
                    throw INTERNAL_ERROR("PyDec_ToIntegralExact"); /* GCOV_NOT_REACHED */
            }

            var result = dec_alloc();

            mpd_qround_to_intx(MPD(result), MPD(dec), ref workctx, ref status);
            dec_addstatus(context, status);

            return result;
        }

        // PyDec_AsFloat
        internal static PyObject PyDec_AsFloat(PyObject dec)
        {
            PyObject s;

            if (mpd_isnan(MPD(dec)))
            {
                if (mpd_issnan(MPD(dec)))
                    throw PyValueError.Create("cannot convert signaling NaN to float");
                if (mpd_isnegative(MPD(dec)))
                    s = PyString.From("-nan");
                else
                    s = PyString.From("nan");
            }
            else
            {
                s = dec_str(dec);
            }

            // PyFloat_FromString
            return PyFloat.FromObject(s);
        }

        // PyDec_Round
        private static PyObject PyDec_Round(PyObject dec, PyObject[] args)
        {
            uint status = 0;

            var context = CURRENT_CONTEXT();
            PyArg_ParseTuple(args, 0, 1);
            var x = args.Length > 0 ? args[0] : null;

            if (x != null)
            {
                var q = new mpd_t(MPD_STATIC | MPD_CONST_DATA, 0, 1, 1, 1, new ulong[] { 1 });

                if (!PyInt.LongCheck(x))
                    throw PyTypeError.Create("optional arg must be an integer");

                long y = PyLong_AsSsize_t(x);
                var result = dec_alloc();

                q.exp = (y == MPD_SSIZE_MIN) ? MPD_SSIZE_MAX : -y;
                mpd_qquantize(MPD(result), MPD(dec), q, ref CTX(context), ref status);
                dec_addstatus(context, status);

                return result;
            }
            else
            {
                return dec_as_long(dec, context, MPD_ROUND_HALF_EVEN);
            }
        }

        private static PyObject DecimalTuple;

        /* Return the DecimalTuple representation of a PyDecObject. */
        private static PyObject PyDec_AsTuple(PyObject dec)
        {
            PyObject coeff;
            PyObject expt;

            var x = mpd_qncopy(MPD(dec)) ?? throw PyErr_NoMemory();

            PyObject sign = PyInt.From(mpd_sign(MPD(dec)));

            if (mpd_isinfinite(x))
            {
                expt = PyString.From("F");
                /* decimal.py has non-compliant infinity payloads. */
                coeff = PyTuple.From(PyInt.From(0));
            }
            else
            {
                if (mpd_isnan(x))
                    expt = PyString.From(mpd_isqnan(x) ? "n" : "N");
                else
                    expt = PyInt.From(MPD(dec).exp);

                /* coefficient is defined */
                if (x.len > 0)
                {
                    /* make an integer */
                    x.exp = 0;
                    /* clear NaN and sign */
                    mpd_clear_flags(x);
                    var intstring = mpd_to_sci(x, 1) ?? throw PyErr_NoMemory();

                    int intlen = intstring.Length;
                    var items = new PyObject[intlen];
                    for (int i = 0; i < intlen; i++)
                        items[i] = PyInt.From(intstring[i] - '0');
                    coeff = PyTuple.From(items);
                }
                else
                {
                    coeff = PyTuple.From(Array.Empty<PyObject>());
                }
            }

            mpd_del(x);
            return DecimalTuple.Call(new[] { sign, coeff, expt }, null);
        }
    }
}
