// Modules/_decimal/libmpdec/io.c and io.h (CPython 3.12.15, libmpdec 2.5.1)
//
// This file contains functions for decimal <-> string conversions, including
// PEP-3101 formatting for numeric types.
//
// The C char buffers are byte[] with int indexes (-1 for a NULL pointer). C strings that come in
// (the numeric string, the format string) are copied into a NUL-terminated byte[] first, so every
// read the C code makes up to and including the terminating NUL is the same here. mpd_spec_t's
// dot/sep/grouping are byte[] without the NUL (strlen is Length), fill is NUL-terminated.
using System;
using System.Diagnostics;
using System.Text;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        // localeconv() for mpd_parse_fmt_str's 'n' type: the bytes of the C runtime's
        // lconv.decimal_point, thousands_sep and grouping (without NUL). The host sets this.
        public static Func<(byte[] decimal_point, byte[] thousands_sep, byte[] grouping)> mpd_localeconv =
            () => (new byte[] { (byte)'.' }, new byte[0], new byte[0]);

        private const int CHAR_MAX = 127; /* char is signed with MSVC */
        private const int EINVAL = 22;
        private const int ERANGE = 34;

        // A C string argument as NUL-terminated bytes: each char of s is one byte.
        private static byte[] _mpd_cstring(string s)
        {
            var b = new byte[s.Length + 1];
            for (int i = 0; i < s.Length; i++)
                b[i] = unchecked((byte)s[i]);
            return b;
        }

        private static byte[] _mpd_cstring(byte[] s)
        {
            var b = new byte[s.Length + 1];
            Array.Copy(s, b, s.Length);
            return b;
        }

        // isdigit() in the "C" locale
        private static bool _mpd_isdigit(byte c) => c >= (byte)'0' && c <= (byte)'9';

        // strlen() of a byte[] that holds a C string without its NUL (an embedded NUL ends it)
        private static int _mpd_strlen(byte[] s)
        {
            if (s == null) return 0;
            int n = Array.IndexOf(s, (byte)0);
            return n < 0 ? s.Length : n;
        }

        // *spec->grouping at index i: a signed char, 0 at the end of the string
        private static int _mpd_gchar(byte[] g, int i) => (i < _mpd_strlen(g)) ? (sbyte)g[i] : 0;

        // io.h: mpd_strtossize == strtoll (SIZE_MAX == MPD_SIZE_MAX); errno is returned in 'err'.
        private static long mpd_strtossize(byte[] s, int start, out int end, ref int err)
        {
            int i = start;
            while (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\v' || s[i] == '\f' || s[i] == '\r')
                i++;
            bool neg = false;
            if (s[i] == '+' || s[i] == '-')
            {
                neg = s[i] == '-';
                i++;
            }
            if (!_mpd_isdigit(s[i]))
            {
                end = start;
                return 0;
            }
            ulong acc = 0;
            bool overflow = false;
            ulong limit = neg ? (ulong)long.MaxValue + 1 : (ulong)long.MaxValue;
            while (_mpd_isdigit(s[i]))
            {
                ulong d = (ulong)(s[i] - '0');
                if (!overflow)
                {
                    if (acc > (limit - d) / 10)
                        overflow = true;
                    else
                        acc = acc * 10 + d;
                }
                i++;
            }
            end = i;
            if (overflow)
            {
                err = ERANGE;
                return neg ? long.MinValue : long.MaxValue;
            }
            return neg ? unchecked(-(long)acc) : (long)acc;
        }

        /*
         * Work around the behavior of tolower() and strcasecmp() in certain
         * locales. For example, in tr_TR.utf8:
         *
         * tolower((unsigned char)'I') == 'I'
         *
         * u is the exact uppercase version of l; n is strlen(l) or strlen(l)+1
         */
        private static bool _mpd_strneq(byte[] s, int si, string l, string u, int n)
        {
            int k = 0;
            while (--n != -1)
            {
                byte lc = k < l.Length ? (byte)l[k] : (byte)0;
                byte uc = k < u.Length ? (byte)u[k] : (byte)0;
                if (s[si] != lc && s[si] != uc)
                {
                    return false;
                }
                si++; k++;
            }

            return true;
        }

        // strtoexp
        private static long strtoexp(byte[] s, int si, ref int errno)
        {
            long retval;

            errno = 0;
            retval = mpd_strtossize(s, si, out int end, ref errno);
            if (errno == 0 && !(s[si] != '\0' && s[end] == '\0'))
                errno = EINVAL;

            return retval;
        }

        /*
         * Scan 'len' words. The most significant word contains 'r' digits,
         * the remaining words are full words. Skip dpoint. The string 's' must
         * consist of digits and an optional single decimal point at 'dpoint'.
         */
        private static void string_to_coeff(ulong[] data, byte[] buf, int s, int dpoint, int r, ulong len)
        {
            int j;

            if (r > 0)
            {
                data[--len] = 0;
                for (j = 0; j < r; j++, s++)
                {
                    if (s == dpoint) s++;
                    data[len] = 10 * data[len] + (ulong)(buf[s] - '0');
                }
            }

            while (--len != MPD_SIZE_MAX)
            {
                data[len] = 0;
                for (j = 0; j < MPD_RDIGITS; j++, s++)
                {
                    if (s == dpoint) s++;
                    data[len] = 10 * data[len] + (ulong)(buf[s] - '0');
                }
            }
        }

        /*
         * Partially verify a numeric string of the form:
         *
         *     [cdigits][.][cdigits][eE][+-][edigits]
         *
         * If successful, return a pointer to the location of the first
         * relevant coefficient digit. This digit is either non-zero or
         * part of one of the following patterns:
         *
         *     ["0\x00", "0.\x00", "0.E", "0.e", "0E", "0e"]
         *
         * The locations of a single optional dot or indicator are stored
         * in 'dpoint' and 'exp'.
         *
         * The end of the string is stored in 'end'. If an indicator [eE]
         * occurs without trailing [edigits], the condition is caught
         * later by strtoexp().
         */
        private static int scan_dpoint_exp(byte[] buf, int s, out int dpoint, out int exp, out int end)
        {
            int coeff = -1;

            dpoint = -1;
            exp = -1;
            end = -1;
            for (; buf[s] != '\0'; s++)
            {
                switch (buf[s])
                {
                case (byte)'.':
                    if (dpoint != -1 || exp != -1)
                        return -1;
                    dpoint = s;
                    break;
                case (byte)'E':
                case (byte)'e':
                    if (exp != -1)
                        return -1;
                    exp = s;
                    if (buf[s + 1] == '+' || buf[s + 1] == '-')
                        s++;
                    break;
                default:
                    if (!_mpd_isdigit(buf[s]))
                        return -1;
                    if (coeff == -1 && exp == -1)
                    {
                        if (buf[s] == '0')
                        {
                            if (!_mpd_isdigit(buf[s + 1]))
                                if (!(buf[s + 1] == '.' &&
                                      _mpd_isdigit(buf[s + 2])))
                                    coeff = s;
                        }
                        else
                        {
                            coeff = s;
                        }
                    }
                    break;
                }
            }

            end = s;
            return coeff;
        }

        /* scan the payload of a NaN */
        private static int scan_payload(byte[] buf, int s, out int end)
        {
            int coeff;

            while (buf[s] == '0')
                s++;
            coeff = s;

            while (_mpd_isdigit(buf[s]))
                s++;
            end = s;

            return (buf[s] == '\0') ? coeff : -1;
        }

        /* convert a character string to a decimal */
        public static void mpd_qset_string(mpd_t dec, string str, ref mpd_context_t ctx, ref uint status)
        {
            byte[] buf = _mpd_cstring(str);
            int s = 0;
            long q, r, len;
            int coeff, end;
            int dpoint = -1, exp = -1;
            ulong digits;
            byte sign = MPD_POS;
            int errno = 0;

            mpd_set_flags(dec, 0);
            dec.len = 0;
            dec.exp = 0;

            /* sign */
            if (buf[s] == '+')
            {
                s++;
            }
            else if (buf[s] == '-')
            {
                mpd_set_negative(dec);
                sign = MPD_NEG;
                s++;
            }

            if (_mpd_strneq(buf, s, "nan", "NAN", 3))
            { /* NaN */
                s += 3;
                mpd_setspecial(dec, sign, MPD_NAN);
                if (buf[s] == '\0')
                    return;
                /* validate payload: digits only */
                if ((coeff = scan_payload(buf, s, out end)) == -1)
                    goto conversion_error;
                /* payload consists entirely of zeros */
                if (buf[coeff] == '\0')
                    return;
                digits = (ulong)(end - coeff);
                /* prec >= 1, clamp is 0 or 1 */
                if (digits > (ulong)(ctx.prec - ctx.clamp))
                    goto conversion_error;
            } /* sNaN */
            else if (_mpd_strneq(buf, s, "snan", "SNAN", 4))
            {
                s += 4;
                mpd_setspecial(dec, sign, MPD_SNAN);
                if (buf[s] == '\0')
                    return;
                /* validate payload: digits only */
                if ((coeff = scan_payload(buf, s, out end)) == -1)
                    goto conversion_error;
                /* payload consists entirely of zeros */
                if (buf[coeff] == '\0')
                    return;
                digits = (ulong)(end - coeff);
                if (digits > (ulong)(ctx.prec - ctx.clamp))
                    goto conversion_error;
            }
            else if (_mpd_strneq(buf, s, "inf", "INF", 3))
            {
                s += 3;
                if (buf[s] == '\0' || _mpd_strneq(buf, s, "inity", "INITY", 6))
                {
                    /* numeric-value: infinity */
                    mpd_setspecial(dec, sign, MPD_INF);
                    return;
                }
                goto conversion_error;
            }
            else
            {
                /* scan for start of coefficient, decimal point, indicator, end */
                if ((coeff = scan_dpoint_exp(buf, s, out dpoint, out exp, out end)) == -1)
                    goto conversion_error;

                /* numeric-value: [exponent-part] */
                if (exp != -1)
                {
                    /* exponent-part */
                    end = exp; exp++;
                    dec.exp = strtoexp(buf, exp, ref errno);
                    if (errno != 0)
                    {
                        if (!(errno == ERANGE &&
                             (dec.exp == MPD_SSIZE_MAX ||
                              dec.exp == MPD_SSIZE_MIN)))
                            goto conversion_error;
                    }
                }

                digits = (ulong)(end - coeff);
                if (dpoint != -1)
                {
                    ulong fracdigits = (ulong)(end - dpoint - 1);
                    if (dpoint > coeff) digits--;

                    if (fracdigits > MPD_MAX_PREC)
                    {
                        goto conversion_error;
                    }
                    if (dec.exp < MPD_SSIZE_MIN + (long)fracdigits)
                    {
                        dec.exp = MPD_SSIZE_MIN;
                    }
                    else
                    {
                        dec.exp -= (long)fracdigits;
                    }
                }
                if (digits > MPD_MAX_PREC)
                {
                    goto conversion_error;
                }
                if (dec.exp > MPD_EXP_INF)
                {
                    dec.exp = MPD_EXP_INF;
                }
                if (dec.exp == MPD_SSIZE_MIN)
                {
                    dec.exp = MPD_SSIZE_MIN + 1;
                }
            }

            _mpd_idiv_word(out q, out r, (long)digits, MPD_RDIGITS);

            len = (r == 0) ? q : q + 1;
            if (len == 0)
            {
                goto conversion_error; /* GCOV_NOT_REACHED */
            }
            if (!mpd_qresize(dec, len, ref status))
            {
                mpd_seterror(dec, MPD_Malloc_error, ref status);
                return;
            }
            dec.len = len;

            string_to_coeff(dec.data, buf, coeff, dpoint, (int)r, (ulong)len);

            mpd_setdigits(dec);
            mpd_qfinalize(dec, ref ctx, ref status);
            return;

        conversion_error:
            /* standard wants a positive NaN */
            mpd_seterror(dec, MPD_Conversion_syntax, ref status);
        }

        /* convert a character string to a decimal, use a maxcontext for conversion */
        public static void mpd_qset_string_exact(mpd_t dec, string s, ref uint status)
        {
            mpd_context_t maxcontext = default;

            mpd_maxcontext(ref maxcontext);
            mpd_qset_string(dec, s, ref maxcontext, ref status);

            if ((status & (MPD_Inexact | MPD_Rounded | MPD_Clamped)) != 0)
            {
                /* we want exact results */
                mpd_seterror(dec, MPD_Invalid_operation, ref status);
            }
            status &= MPD_Errors;
        }

        /* Print word x with n decimal digits to string s. dot is either NULL
           or the location of a decimal point. */
        // #define EXTRACT_DIGIT(s, x, d, dot): the switch falls through from case n to case 2.
        private static int word_to_string(byte[] buf, int s, ulong x, int n, int dot)
        {
            for (int k = n; k >= 2; k--)
            {
                ulong d = mpd_pow10[k - 1];
                if (s == dot) buf[s++] = (byte)'.';
                buf[s++] = (byte)('0' + (byte)(x / d));
                x %= d;
            }
            /* default: */
            if (s == dot) buf[s++] = (byte)'.';
            buf[s++] = (byte)('0' + (byte)x);

            buf[s] = (byte)'\0';
            return s;
        }

        /* Print exponent x to string s. Undefined for MPD_SSIZE_MIN. */
        private static int exp_to_string(byte[] buf, int s, long x)
        {
            byte sign = (byte)'+';

            if (x < 0)
            {
                sign = (byte)'-';
                x = -x;
            }
            buf[s++] = sign;

            return word_to_string(buf, s, (ulong)x, mpd_word_digits((ulong)x), -1);
        }

        /* Print the coefficient of dec to string s. len(dec) > 0. */
        private static int coeff_to_string(byte[] buf, int s, mpd_t dec)
        {
            ulong x;
            long i;

            /* most significant word */
            x = mpd_msword(dec);
            s = word_to_string(buf, s, x, mpd_word_digits(x), -1);

            /* remaining full words */
            for (i = dec.len - 2; i >= 0; --i)
            {
                x = dec.data[i];
                s = word_to_string(buf, s, x, MPD_RDIGITS, -1);
            }

            return s;
        }

        /* Print the coefficient of dec to string s. len(dec) > 0. dot is either
           NULL or a pointer to the location of a decimal point. */
        private static int coeff_to_string_dot(byte[] buf, int s, int dot, mpd_t dec)
        {
            ulong x;
            long i;

            /* most significant word */
            x = mpd_msword(dec);
            s = word_to_string(buf, s, x, mpd_word_digits(x), dot);

            /* remaining full words */
            for (i = dec.len - 2; i >= 0; --i)
            {
                x = dec.data[i];
                s = word_to_string(buf, s, x, MPD_RDIGITS, dot);
            }

            return s;
        }

        /* Format type */
        private const int MPD_FMT_LOWER = 0x00000000;
        private const int MPD_FMT_UPPER = 0x00000001;
        private const int MPD_FMT_TOSCI = 0x00000002;
        private const int MPD_FMT_TOENG = 0x00000004;
        private const int MPD_FMT_EXP = 0x00000008;
        private const int MPD_FMT_FIXED = 0x00000010;
        private const int MPD_FMT_PERCENT = 0x00000020;
        private const int MPD_FMT_SIGN_SPACE = 0x00000040;
        private const int MPD_FMT_SIGN_PLUS = 0x00000080;

        /* Default place of the decimal point for MPD_FMT_TOSCI, MPD_FMT_EXP */
        private const long MPD_DEFAULT_DOTPLACE = 1;

        private static void _mpd_strcpy(byte[] buf, int cp, string s)
        {
            for (int i = 0; i < s.Length; i++)
                buf[cp + i] = (byte)s[i];
            buf[cp + s.Length] = 0;
        }

        /*
         * Set *result to the string representation of a decimal. Return the length
         * of *result, not including the terminating '\0' character.
         *
         * Formatting is done according to 'flags'. A return value of -1 with *result
         * set to NULL indicates MPD_Malloc_error.
         *
         * 'dplace' is the default place of the decimal point. It is always set to
         * MPD_DEFAULT_DOTPLACE except for zeros in combination with MPD_FMT_EXP.
         */
        private static long _mpd_to_string(out byte[] result, mpd_t dec, int flags, long dplace)
        {
            byte[] decstring = null;
            int cp = 0;
            long ldigits;
            long mem = 0, k;

            if (mpd_isspecial(dec))
            {
                mem = "-Infinity%".Length + 1; /* sizeof "-Infinity%" */
                if (mpd_isnan(dec) && dec.len > 0)
                {
                    /* diagnostic code */
                    mem += dec.digits;
                }
                decstring = mpd_alloc<byte>((ulong)mem);
                cp = 0;
                if (decstring == null)
                {
                    result = null;
                    return -1;
                }

                if (mpd_isnegative(dec))
                {
                    decstring[cp++] = (byte)'-';
                }
                else if ((flags & MPD_FMT_SIGN_SPACE) != 0)
                {
                    decstring[cp++] = (byte)' ';
                }
                else if ((flags & MPD_FMT_SIGN_PLUS) != 0)
                {
                    decstring[cp++] = (byte)'+';
                }

                if (mpd_isnan(dec))
                {
                    if (mpd_isqnan(dec))
                    {
                        _mpd_strcpy(decstring, cp, "NaN");
                        cp += 3;
                    }
                    else
                    {
                        _mpd_strcpy(decstring, cp, "sNaN");
                        cp += 4;
                    }
                    if (dec.len > 0)
                    { /* diagnostic code */
                        cp = coeff_to_string(decstring, cp, dec);
                    }
                }
                else if (mpd_isinfinite(dec))
                {
                    _mpd_strcpy(decstring, cp, "Infinity");
                    cp += 8;
                }
                else
                { /* debug */
                    throw mpd_err_fatal("_mpd_to_string: abort"); /* GCOV_NOT_REACHED */
                }
            }
            else
            {
                Debug.Assert(dec.len > 0);

                /*
                 * For easier manipulation of the decimal point's location
                 * and the exponent that is finally printed, the number is
                 * rescaled to a virtual representation with exp = 0. Here
                 * ldigits denotes the number of decimal digits to the left
                 * of the decimal point and remains constant once initialized.
                 *
                 * dplace is the location of the decimal point relative to
                 * the start of the coefficient. Note that 3) always holds
                 * when dplace is shifted.
                 *
                 *   1) ldigits := dec->digits - dec->exp
                 *   2) dplace  := ldigits            (initially)
                 *   3) exp     := ldigits - dplace   (initially exp = 0)
                 *
                 *   0.00000_.____._____000000.
                 *    ^      ^    ^           ^
                 *    |      |    |           |
                 *    |      |    |           `- dplace >= digits
                 *    |      |    `- dplace in the middle of the coefficient
                 *    |      ` dplace = 1 (after the first coefficient digit)
                 *    `- dplace <= 0
                 */

                ldigits = dec.digits + dec.exp;

                if ((flags & MPD_FMT_EXP) != 0)
                {
                    ;
                }
                else if ((flags & MPD_FMT_FIXED) != 0 || (dec.exp <= 0 && ldigits > -6))
                {
                    /* MPD_FMT_FIXED: always use fixed point notation.
                     * MPD_FMT_TOSCI, MPD_FMT_TOENG: for a certain range,
                     * override exponent notation. */
                    dplace = ldigits;
                }
                else if ((flags & MPD_FMT_TOENG) != 0)
                {
                    if (mpd_iszero(dec))
                    {
                        /* If the exponent is divisible by three,
                         * dplace = 1. Otherwise, move dplace one
                         * or two places to the left. */
                        dplace = -1 + mod_mpd_ssize_t(dec.exp + 2, 3);
                    }
                    else
                    { /* ldigits-1 is the adjusted exponent, which
                       * should be divisible by three. If not, move
                       * dplace one or two places to the right. */
                        dplace += mod_mpd_ssize_t(ldigits - 1, 3);
                    }
                }

                /*
                 * Basic space requirements:
                 *
                 * [-][.][coeffdigits][E][-][expdigits+1][%]['\0']
                 *
                 * If the decimal point lies outside of the coefficient digits,
                 * space is adjusted accordingly.
                 */
                if (dplace <= 0)
                {
                    mem = -dplace + dec.digits + 2;
                }
                else if (dplace >= dec.digits)
                {
                    mem = dplace;
                }
                else
                {
                    mem = dec.digits;
                }
                mem += (MPD_EXPDIGITS + 1 + 6);

                decstring = mpd_alloc<byte>((ulong)mem);
                cp = 0;
                if (decstring == null)
                {
                    result = null;
                    return -1;
                }


                if (mpd_isnegative(dec))
                {
                    decstring[cp++] = (byte)'-';
                }
                else if ((flags & MPD_FMT_SIGN_SPACE) != 0)
                {
                    decstring[cp++] = (byte)' ';
                }
                else if ((flags & MPD_FMT_SIGN_PLUS) != 0)
                {
                    decstring[cp++] = (byte)'+';
                }

                if (dplace <= 0)
                {
                    /* space: -dplace+dec->digits+2 */
                    decstring[cp++] = (byte)'0';
                    decstring[cp++] = (byte)'.';
                    for (k = 0; k < -dplace; k++)
                    {
                        decstring[cp++] = (byte)'0';
                    }
                    cp = coeff_to_string(decstring, cp, dec);
                }
                else if (dplace >= dec.digits)
                {
                    /* space: dplace */
                    cp = coeff_to_string(decstring, cp, dec);
                    for (k = 0; k < dplace - dec.digits; k++)
                    {
                        decstring[cp++] = (byte)'0';
                    }
                }
                else
                {
                    /* space: dec->digits+1 */
                    cp = coeff_to_string_dot(decstring, cp, cp + (int)dplace, dec);
                }

                /*
                 * Conditions for printing an exponent:
                 *
                 *   MPD_FMT_TOSCI, MPD_FMT_TOENG: only if ldigits != dplace
                 *   MPD_FMT_FIXED:                never (ldigits == dplace)
                 *   MPD_FMT_EXP:                  always
                 */
                if (ldigits != dplace || (flags & MPD_FMT_EXP) != 0)
                {
                    /* space: expdigits+2 */
                    decstring[cp++] = (byte)(((flags & MPD_FMT_UPPER) != 0) ? 'E' : 'e');
                    cp = exp_to_string(decstring, cp, ldigits - dplace);
                }
            }

            if ((flags & MPD_FMT_PERCENT) != 0)
            {
                decstring[cp++] = (byte)'%';
            }

            Debug.Assert(cp < mem);

            decstring[cp] = (byte)'\0';
            result = decstring;
            return cp;
        }

        // The C string of _mpd_to_string as a C# string (ASCII), null for NULL.
        private static string _mpd_ascii(byte[] s, long n) =>
            s == null ? null : Encoding.Latin1.GetString(s, 0, (int)n);

        // mpd_to_sci
        public static string mpd_to_sci(mpd_t dec, int fmt)
        {
            int flags = MPD_FMT_TOSCI;

            flags |= fmt != 0 ? MPD_FMT_UPPER : MPD_FMT_LOWER;
            long n = _mpd_to_string(out byte[] res, dec, flags, MPD_DEFAULT_DOTPLACE);
            return _mpd_ascii(res, n);
        }

        // mpd_to_eng
        public static string mpd_to_eng(mpd_t dec, int fmt)
        {
            int flags = MPD_FMT_TOENG;

            flags |= fmt != 0 ? MPD_FMT_UPPER : MPD_FMT_LOWER;
            long n = _mpd_to_string(out byte[] res, dec, flags, MPD_DEFAULT_DOTPLACE);
            return _mpd_ascii(res, n);
        }

        // mpd_to_sci_size
        public static long mpd_to_sci_size(out string res, mpd_t dec, int fmt)
        {
            int flags = MPD_FMT_TOSCI;

            flags |= fmt != 0 ? MPD_FMT_UPPER : MPD_FMT_LOWER;
            long n = _mpd_to_string(out byte[] buf, dec, flags, MPD_DEFAULT_DOTPLACE);
            res = _mpd_ascii(buf, n);
            return n;
        }

        // mpd_to_eng_size
        public static long mpd_to_eng_size(out string res, mpd_t dec, int fmt)
        {
            int flags = MPD_FMT_TOENG;

            flags |= fmt != 0 ? MPD_FMT_UPPER : MPD_FMT_LOWER;
            long n = _mpd_to_string(out byte[] buf, dec, flags, MPD_DEFAULT_DOTPLACE);
            res = _mpd_ascii(buf, n);
            return n;
        }

        /* Copy a single UTF-8 char to dest. See: The Unicode Standard, version 5.2,
           chapter 3.9: Well-formed UTF-8 byte sequences. */
        private static int _mpd_copy_utf8(byte[] dest, byte[] s, int cp)
        {
            byte lb, ub;
            int count, i;


            if (s[cp] == 0)
            {
                /* empty string */
                dest[0] = (byte)'\0';
                return 0;
            }
            else if (s[cp] <= 0x7f)
            {
                /* ascii */
                dest[0] = s[cp];
                dest[1] = (byte)'\0';
                return 1;
            }
            else if (0xc2 <= s[cp] && s[cp] <= 0xdf)
            {
                lb = 0x80; ub = 0xbf;
                count = 2;
            }
            else if (s[cp] == 0xe0)
            {
                lb = 0xa0; ub = 0xbf;
                count = 3;
            }
            else if (s[cp] <= 0xec)
            {
                lb = 0x80; ub = 0xbf;
                count = 3;
            }
            else if (s[cp] == 0xed)
            {
                lb = 0x80; ub = 0x9f;
                count = 3;
            }
            else if (s[cp] <= 0xef)
            {
                lb = 0x80; ub = 0xbf;
                count = 3;
            }
            else if (s[cp] == 0xf0)
            {
                lb = 0x90; ub = 0xbf;
                count = 4;
            }
            else if (s[cp] <= 0xf3)
            {
                lb = 0x80; ub = 0xbf;
                count = 4;
            }
            else if (s[cp] == 0xf4)
            {
                lb = 0x80; ub = 0x8f;
                count = 4;
            }
            else
            {
                /* invalid */
                goto error;
            }

            dest[0] = s[cp++];
            if (s[cp] < lb || ub < s[cp])
            {
                goto error;
            }
            dest[1] = s[cp++];
            for (i = 2; i < count; i++)
            {
                if (s[cp] < 0x80 || 0xbf < s[cp])
                {
                    goto error;
                }
                dest[i] = s[cp++];
            }
            dest[i] = (byte)'\0';

            return count;

        error:
            dest[0] = (byte)'\0';
            return -1;
        }

        // mpd_validate_lconv
        public static int mpd_validate_lconv(mpd_spec_t spec)
        {
            int n;
            /* CHAR_MAX == SCHAR_MAX (MSVC) */
            int glen = _mpd_strlen(spec.grouping);
            for (int i = 0; i < glen; i++)
            {
                if ((sbyte)spec.grouping[i] < 0)
                {
                    return -1;
                }
            }
            n = _mpd_strlen(spec.dot);
            if (n == 0 || n > 4)
            {
                return -1;
            }
            if (_mpd_strlen(spec.sep) > 4)
            {
                return -1;
            }

            return 0;
        }

        // mpd_parse_fmt_str
        public static bool mpd_parse_fmt_str(mpd_spec_t spec, byte[] fmtbytes, int caps)
        {
            byte[] fmt = _mpd_cstring(fmtbytes);
            int cp = 0;
            bool have_align = false;
            int n;
            int errno;

            /* defaults */
            spec.min_width = 0;
            spec.prec = -1;
            spec.type = caps != 0 ? 'G' : 'g';
            spec.align = '>';
            spec.sign = '-';
            spec.dot = Array.Empty<byte>();
            spec.sep = Array.Empty<byte>();
            spec.grouping = Array.Empty<byte>();
            if (spec.fill == null || spec.fill.Length < 5)
                spec.fill = new byte[5];


            /* presume that the first character is a UTF-8 fill character */
            if ((n = _mpd_copy_utf8(spec.fill, fmt, cp)) < 0)
            {
                return false;
            }

            /* alignment directive, prefixed by a fill character */
            if (fmt[cp] != 0 && (fmt[cp + n] == '<' || fmt[cp + n] == '>' ||
                                 fmt[cp + n] == '=' || fmt[cp + n] == '^'))
            {
                cp += n;
                spec.align = (char)fmt[cp++];
                have_align = true;
            } /* alignment directive */
            else
            {
                /* default fill character */
                spec.fill[0] = (byte)' ';
                spec.fill[1] = (byte)'\0';
                if (fmt[cp] == '<' || fmt[cp] == '>' ||
                    fmt[cp] == '=' || fmt[cp] == '^')
                {
                    spec.align = (char)fmt[cp++];
                    have_align = true;
                }
            }

            /* sign formatting */
            if (fmt[cp] == '+' || fmt[cp] == '-' || fmt[cp] == ' ')
            {
                spec.sign = (char)fmt[cp++];
            }

            /* zero padding */
            if (fmt[cp] == '0')
            {
                /* zero padding implies alignment, which should not be
                 * specified twice. */
                if (have_align)
                {
                    return false;
                }
                spec.align = 'z';
                spec.fill[0] = fmt[cp++];
                spec.fill[1] = (byte)'\0';
            }

            /* minimum width */
            if (_mpd_isdigit(fmt[cp]))
            {
                if (fmt[cp] == '0')
                {
                    return false;
                }
                errno = 0;
                spec.min_width = mpd_strtossize(fmt, cp, out cp, ref errno);
                if (errno == ERANGE || errno == EINVAL)
                {
                    return false;
                }
            }

            /* thousands separator */
            if (fmt[cp] == ',')
            {
                spec.dot = new byte[] { (byte)'.' };
                spec.sep = new byte[] { (byte)',' };
                spec.grouping = new byte[] { 3, 3 };
                cp++;
            }

            /* fraction digits or significant digits */
            if (fmt[cp] == '.')
            {
                cp++;
                if (!_mpd_isdigit(fmt[cp]))
                {
                    return false;
                }
                errno = 0;
                spec.prec = mpd_strtossize(fmt, cp, out cp, ref errno);
                if (errno == ERANGE || errno == EINVAL)
                {
                    return false;
                }
            }

            /* type */
            if (fmt[cp] == 'E' || fmt[cp] == 'e' || fmt[cp] == 'F' || fmt[cp] == 'f' ||
                fmt[cp] == 'G' || fmt[cp] == 'g' || fmt[cp] == '%')
            {
                spec.type = (char)fmt[cp++];
            }
            else if (fmt[cp] == 'N' || fmt[cp] == 'n')
            {
                /* locale specific conversion */
                /* separator has already been specified */
                if (_mpd_strlen(spec.sep) != 0)
                {
                    return false;
                }
                spec.type = (char)fmt[cp++];
                spec.type = (spec.type == 'N') ? 'G' : 'g';
                var lc = mpd_localeconv();
                spec.dot = lc.decimal_point ?? Array.Empty<byte>();
                spec.sep = lc.thousands_sep ?? Array.Empty<byte>();
                spec.grouping = lc.grouping ?? Array.Empty<byte>();
                if (mpd_validate_lconv(spec) < 0)
                {
                    return false; /* GCOV_NOT_REACHED */
                }
            }

            /* check correctness */
            if (fmt[cp] != '\0')
            {
                return false;
            }

            return true;
        }

        /*
         * The following functions assume that spec->min_width <= MPD_MAX_PREC, which
         * is made sure in mpd_qformat_spec. Then, even with a spec that inserts a
         * four-byte separator after each digit, nbytes in the following struct
         * cannot overflow.
         */

        /* Multibyte string */
        private sealed class mpd_mbstr_t
        {
            public long nbytes; /* length in bytes */
            public long nchars; /* length in chars */
            public long cur;    /* current write index */
            public byte[] data;
        }

        // _mpd_bcopy
        private static void _mpd_bcopy(byte[] dest, long di, byte[] src, long si, long n)
        {
            while (--n >= 0)
            {
                dest[di + n] = src[si + n];
            }
        }

        // _mbstr_copy_char
        private static void _mbstr_copy_char(mpd_mbstr_t dest, byte[] src, long si, long n)
        {
            dest.nbytes += n;
            dest.nchars += (n > 0 ? 1 : 0);
            dest.cur -= n;

            if (dest.data != null)
            {
                _mpd_bcopy(dest.data, dest.cur, src, si, n);
            }
        }

        // _mbstr_copy_ascii
        private static void _mbstr_copy_ascii(mpd_mbstr_t dest, byte[] src, long si, long n)
        {
            dest.nbytes += n;
            dest.nchars += n;
            dest.cur -= n;

            if (dest.data != null)
            {
                _mpd_bcopy(dest.data, dest.cur, src, si, n);
            }
        }

        // _mbstr_copy_pad
        private static void _mbstr_copy_pad(mpd_mbstr_t dest, long n)
        {
            dest.nbytes += n;
            dest.nchars += n;
            dest.cur -= n;

            if (dest.data != null)
            {
                long cp = dest.cur;
                while (--n >= 0)
                {
                    dest.data[cp + n] = (byte)'0';
                }
            }
        }

        /*
         * Copy a numeric string to dest->data, adding separators in the integer
         * part according to spec->grouping. If leading zero padding is enabled
         * and the result is smaller than spec->min_width, continue adding zeros
         * and separators until the minimum width is reached.
         *
         * The final length of dest->data is stored in dest->nbytes. The number
         * of UTF-8 characters is stored in dest->nchars.
         *
         * First run (dest->data == NULL): determine the length of the result
         * string and store it in dest->nbytes.
         *
         * Second run (write to dest->data): data is written in chunks and in
         * reverse order, starting with the rest of the numeric string.
         *
         * 'sign', 'src' and 'rest' are indexes into 'buf' (sign -1 for NULL);
         * 'dot' is spec->dot or NULL.
         */
        private static void _mpd_add_sep_dot(mpd_mbstr_t dest,
                                             byte[] buf,
                                             int sign, /* location of optional sign */
                                             int src, long n_src, /* integer part and length */
                                             byte[] dot, /* optional decimal point */
                                             int rest, long n_rest, /* remaining part and length */
                                             mpd_spec_t spec)
        {
            long n_sep, n_sign, consume;
            int g;
            bool pad = false;

            n_sign = sign != -1 ? 1 : 0;
            n_sep = _mpd_strlen(spec.sep);
            /* Initial write index: set to location of '\0' in the output string.
             * Irrelevant for the first run. */
            dest.cur = dest.nbytes;
            dest.nbytes = dest.nchars = 0;

            _mbstr_copy_ascii(dest, buf, rest, n_rest);

            if (dot != null)
            {
                _mbstr_copy_char(dest, dot, 0, _mpd_strlen(dot));
            }

            g = 0;
            consume = _mpd_gchar(spec.grouping, g);
            while (true)
            {
                /* If the group length is 0 or CHAR_MAX or greater than the
                 * number of source bytes, consume all remaining bytes. */
                if (_mpd_gchar(spec.grouping, g) == 0 || _mpd_gchar(spec.grouping, g) == CHAR_MAX || consume > n_src)
                {
                    consume = n_src;
                }
                n_src -= consume;
                if (pad)
                {
                    _mbstr_copy_pad(dest, consume);
                }
                else
                {
                    _mbstr_copy_ascii(dest, buf, src + n_src, consume);
                }

                if (n_src == 0)
                {
                    /* Either the real source of intpart digits or the virtual
                     * source of padding zeros is exhausted. */
                    if (spec.align == 'z' &&
                        dest.nchars + n_sign < spec.min_width)
                    {
                        /* Zero padding is set and length < min_width:
                         * Generate n_src additional characters. */
                        n_src = spec.min_width - (dest.nchars + n_sign);
                        /* Next iteration:
                         *   case *g == 0 || *g == CHAR_MAX:
                         *      consume all padding characters
                         *   case consume < g*:
                         *      fill remainder of current group
                         *   case consume == g*
                         *      copying is a no-op */
                        consume = _mpd_gchar(spec.grouping, g) - consume;
                        /* Switch on virtual source of zeros. */
                        pad = true;
                        continue;
                    }
                    break;
                }

                if (n_sep > 0)
                {
                    /* If padding is switched on, separators are counted
                     * as padding characters. This rule does not apply if
                     * the separator would be the first character of the
                     * result string. */
                    if (pad && n_src > 1) n_src -= 1;
                    _mbstr_copy_char(dest, spec.sep, 0, n_sep);
                }

                /* If non-NUL, use the next value for grouping. */
                if (_mpd_gchar(spec.grouping, g) != 0 && _mpd_gchar(spec.grouping, g + 1) != 0) g++;
                consume = _mpd_gchar(spec.grouping, g);
            }

            if (sign != -1)
            {
                _mbstr_copy_ascii(dest, buf, sign, 1);
            }

            if (dest.data != null)
            {
                dest.data[dest.nbytes] = (byte)'\0';
            }
        }

        /*
         * Convert a numeric-string to its locale-specific appearance.
         * The string must have one of these forms:
         *
         *     1) [sign] digits [exponent-part]
         *     2) [sign] digits '.' [digits] [exponent-part]
         *
         * Not allowed, since _mpd_to_string() never returns this form:
         *
         *     3) [sign] '.' digits [exponent-part]
         *
         * Input: result->data := original numeric string (ASCII)
         *        result->bytes := strlen(result->data)
         *        result->nchars := strlen(result->data)
         *
         * Output: result->data := modified or original string
         *         result->bytes := strlen(result->data)
         *         result->nchars := number of characters (possibly UTF-8)
         */
        private static bool _mpd_apply_lconv(mpd_mbstr_t result, mpd_spec_t spec, ref uint status)
        {
            int sign = -1, intpart = -1;
            byte[] dot = null;
            int rest, dp;
            byte[] decstring;
            long n_int, n_rest;

            /* original numeric string */
            byte[] data = result.data;
            dp = 0;

            /* sign */
            if (data[dp] == '+' || data[dp] == '-' || data[dp] == ' ')
            {
                sign = dp++;
            }
            /* integer part */
            Debug.Assert(_mpd_isdigit(data[dp]));
            intpart = dp++;
            while (_mpd_isdigit(data[dp]))
            {
                dp++;
            }
            n_int = dp - intpart;
            /* decimal point */
            if (data[dp] == '.')
            {
                dp++; dot = spec.dot;
            }
            /* rest */
            rest = dp;
            n_rest = result.nbytes - dp;

            if (dot == null && (_mpd_strlen(spec.sep) == 0 || _mpd_strlen(spec.grouping) == 0))
            {
                /* _mpd_add_sep_dot() would not change anything */
                return true;
            }

            /* Determine the size of the new decimal string after inserting the
             * decimal point, optional separators and optional padding. */
            decstring = result.data;
            result.data = null;
            _mpd_add_sep_dot(result, decstring, sign, intpart, n_int, dot,
                             rest, n_rest, spec);

            result.data = mpd_alloc<byte>((ulong)(result.nbytes + 1));
            if (result.data == null)
            {
                status |= MPD_Malloc_error;
                mpd_free(decstring);
                return false;
            }

            /* Perform actual writes. */
            _mpd_add_sep_dot(result, decstring, sign, intpart, n_int, dot,
                             rest, n_rest, spec);

            mpd_free(decstring);
            return true;
        }

        /* Add padding to the formatted string if necessary. */
        private static bool _mpd_add_pad(mpd_mbstr_t result, mpd_spec_t spec, ref uint status)
        {
            if (result.nchars < spec.min_width)
            {
                long add_chars, add_bytes;
                ulong lpad = 0, rpad = 0;
                ulong n_fill, len, i, j;
                char align = spec.align;
                byte err = 0;
                long cp;

                n_fill = (ulong)_mpd_strlen(spec.fill);
                add_chars = (spec.min_width - result.nchars);
                /* max value: MPD_MAX_PREC * 4 */
                add_bytes = add_chars * (long)n_fill;

                result.data = mpd_realloc(result.data, (ulong)(result.nbytes + add_bytes + 1), ref err);
                cp = 0;
                if (err != 0)
                {
                    status |= MPD_Malloc_error;
                    mpd_free(result.data);
                    return false;
                }

                if (align == 'z')
                {
                    align = '=';
                }

                if (align == '<')
                {
                    rpad = (ulong)add_chars;
                }
                else if (align == '>' || align == '=')
                {
                    lpad = (ulong)add_chars;
                }
                else
                { /* align == '^' */
                    lpad = (ulong)(add_chars / 2);
                    rpad = (ulong)add_chars - lpad;
                }

                byte[] d = result.data;
                len = (ulong)result.nbytes;
                if (align == '=' && (d[cp] == '-' || d[cp] == '+' || d[cp] == ' '))
                {
                    /* leave sign in the leading position */
                    cp++; len--;
                }

                Array.Copy(d, cp, d, cp + (long)(n_fill * lpad), (long)len); /* memmove */
                for (i = 0; i < lpad; i++)
                {
                    for (j = 0; j < n_fill; j++)
                    {
                        d[cp + (long)(i * n_fill + j)] = spec.fill[j];
                    }
                }
                cp += (long)(n_fill * lpad + len);
                for (i = 0; i < rpad; i++)
                {
                    for (j = 0; j < n_fill; j++)
                    {
                        d[cp + (long)(i * n_fill + j)] = spec.fill[j];
                    }
                }

                result.nbytes += add_bytes;
                result.nchars += add_chars;
                result.data[result.nbytes] = (byte)'\0';
            }

            return true;
        }

        /* Round a number to prec digits. The adjusted exponent stays the same
           or increases by one if rounding up crosses a power of ten boundary.
           If result->digits would exceed MPD_MAX_PREC+1, MPD_Invalid_operation
           is set and the result is NaN. */
        private static void _mpd_round(mpd_t result, mpd_t a, long prec,
                                       ref mpd_context_t ctx, ref uint status)
        {
            long exp = a.exp + a.digits - prec;

            if (prec <= 0)
            {
                mpd_seterror(result, MPD_Invalid_operation, ref status); /* GCOV_NOT_REACHED */
                return; /* GCOV_NOT_REACHED */
            }
            if (mpd_isspecial(a) || mpd_iszero(a))
            {
                mpd_qcopy(result, a, ref status); /* GCOV_NOT_REACHED */
                return; /* GCOV_NOT_REACHED */
            }

            mpd_qrescale_fmt(result, a, exp, ref ctx, ref status);
            if (result.digits > prec)
            {
                mpd_qrescale_fmt(result, result, exp + 1, ref ctx, ref status);
            }
        }

        // stackspec = *spec
        private static mpd_spec_t _mpd_spec_copy(mpd_spec_t spec) => new mpd_spec_t
        {
            min_width = spec.min_width,
            prec = spec.prec,
            type = spec.type,
            align = spec.align,
            sign = spec.sign,
            fill = (byte[])spec.fill.Clone(),
            dot = spec.dot,
            sep = spec.sep,
            grouping = spec.grouping,
        };

        /*
         * Return the string representation of an mpd_t, formatted according to 'spec'.
         * The format specification is assumed to be valid. Memory errors are indicated
         * as usual. This function is quiet.
         *
         * The result is the UTF-8 bytes of the C string without its NUL, or null.
         */
        public static byte[] mpd_qformat_spec(mpd_t dec, mpd_spec_t spec,
                                              ref mpd_context_t ctx, ref uint status)
        {
            mpd_t tmp = new mpd_t(MPD_STATIC | MPD_STATIC_DATA, 0, 0, 0, MPD_MINALLOC_MAX, new ulong[MPD_MINALLOC_MAX]);
            long dplace = MPD_DEFAULT_DOTPLACE;
            mpd_mbstr_t result = new mpd_mbstr_t();
            mpd_spec_t stackspec;
            char type = spec.type;
            int flags = 0;


            if (spec.min_width > MPD_MAX_PREC)
            {
                status |= MPD_Invalid_operation;
                return null;
            }

            if (type >= 'A' && type <= 'Z')
            {
                type = (char)(type - 'A' + 'a');
                flags |= MPD_FMT_UPPER;
            }
            if (spec.sign == ' ')
            {
                flags |= MPD_FMT_SIGN_SPACE;
            }
            else if (spec.sign == '+')
            {
                flags |= MPD_FMT_SIGN_PLUS;
            }

            if (mpd_isspecial(dec))
            {
                if (spec.align == 'z')
                {
                    stackspec = _mpd_spec_copy(spec);
                    stackspec.fill[0] = (byte)' ';
                    stackspec.fill[1] = (byte)'\0';
                    stackspec.align = '>';
                    spec = stackspec;
                }
                Debug.Assert(_mpd_strlen(spec.fill) == 1); /* annotation for scan-build */
                if (type == '%')
                {
                    flags |= MPD_FMT_PERCENT;
                }
            }
            else
            {
                uint workstatus = 0;
                long prec;

                switch (type)
                {
                case 'g': flags |= MPD_FMT_TOSCI; break;
                case 'e': flags |= MPD_FMT_EXP; break;
                case '%':
                    flags |= MPD_FMT_PERCENT;
                    if (!mpd_qcopy(tmp, dec, ref status))
                    {
                        return null;
                    }
                    tmp.exp += 2;
                    dec = tmp;
                    type = 'f'; /* fall through */
                    flags |= MPD_FMT_FIXED;
                    break;
                case 'f': flags |= MPD_FMT_FIXED; break;
                default: throw mpd_err_fatal("mpd_qformat_spec: abort"); /* debug: GCOV_NOT_REACHED */
                }

                if (spec.prec >= 0)
                {
                    if (spec.prec > MPD_MAX_PREC)
                    {
                        status |= MPD_Invalid_operation;
                        goto error;
                    }

                    switch (type)
                    {
                    case 'g':
                        prec = (spec.prec == 0) ? 1 : spec.prec;
                        if (dec.digits > prec)
                        {
                            _mpd_round(tmp, dec, prec, ref ctx,
                                       ref workstatus);
                            dec = tmp;
                        }
                        break;
                    case 'e':
                        if (mpd_iszero(dec))
                        {
                            dplace = 1 - spec.prec;
                        }
                        else
                        {
                            _mpd_round(tmp, dec, spec.prec + 1, ref ctx,
                                       ref workstatus);
                            dec = tmp;
                        }
                        break;
                    case 'f':
                        mpd_qrescale(tmp, dec, -spec.prec, ref ctx,
                                     ref workstatus);
                        dec = tmp;
                        break;
                    }
                }

                if (type == 'f')
                {
                    if (mpd_iszero(dec) && dec.exp > 0)
                    {
                        mpd_qrescale(tmp, dec, 0, ref ctx, ref workstatus);
                        dec = tmp;
                    }
                }

                if ((workstatus & MPD_Errors) != 0)
                {
                    status |= (workstatus & MPD_Errors);
                    goto error;
                }
            }

            /*
             * At this point, for all scaled or non-scaled decimals:
             *   1) 1 <= digits <= MAX_PREC+1
             *   2) adjexp(scaled) = adjexp(orig) [+1]
             *   3)   case 'g': MIN_ETINY <= exp <= MAX_EMAX+1
             *        case 'e': MIN_ETINY-MAX_PREC <= exp <= MAX_EMAX+1
             *        case 'f': MIN_ETINY <= exp <= MAX_EMAX+1
             *   4) max memory alloc in _mpd_to_string:
             *        case 'g': MAX_PREC+36
             *        case 'e': MAX_PREC+36
             *        case 'f': 2*MPD_MAX_PREC+30
             */
            result.nbytes = _mpd_to_string(out result.data, dec, flags, dplace);
            result.nchars = result.nbytes;
            if (result.nbytes < 0)
            {
                status |= MPD_Malloc_error;
                goto error;
            }

            if (_mpd_strlen(spec.dot) != 0 && !mpd_isspecial(dec))
            {
                if (result.nchars > MPD_MAX_PREC + 36)
                {
                    /* Since a group length of one is not explicitly
                     * disallowed, ensure that it is always possible to
                     * insert a four byte separator after each digit. */
                    status |= MPD_Invalid_operation;
                    mpd_free(result.data);
                    goto error;
                }
                if (!_mpd_apply_lconv(result, spec, ref status))
                {
                    goto error;
                }
            }

            if (spec.min_width != 0)
            {
                if (!_mpd_add_pad(result, spec, ref status))
                {
                    goto error;
                }
            }

            mpd_del(tmp);
            {
                var bytes = new byte[result.nbytes];
                Array.Copy(result.data, bytes, result.nbytes);
                return bytes;
            }

        error:
            mpd_del(tmp);
            return null;
        }

        // mpd_qformat
        public static byte[] mpd_qformat(mpd_t dec, byte[] fmt, ref mpd_context_t ctx, ref uint status)
        {
            mpd_spec_t spec = new mpd_spec_t();

            if (!mpd_parse_fmt_str(spec, fmt, 1))
            {
                status |= MPD_Invalid_operation;
                return null;
            }

            return mpd_qformat_spec(dec, spec, ref ctx, ref status);
        }

        /*
         * The specification has a *condition* called Invalid_operation and an
         * IEEE *signal* called Invalid_operation. The former corresponds to
         * MPD_Invalid_operation, the latter to MPD_IEEE_Invalid_operation.
         * MPD_IEEE_Invalid_operation comprises the following conditions:
         *
         * [MPD_Conversion_syntax, MPD_Division_impossible, MPD_Division_undefined,
         *  MPD_Fpu_error, MPD_Invalid_context, MPD_Invalid_operation,
         *  MPD_Malloc_error]
         *
         * In the following functions, 'flag' denotes the condition, 'signal'
         * denotes the IEEE signal.
         */

        private static readonly string[] mpd_flag_string = {
            "Clamped",
            "Conversion_syntax",
            "Division_by_zero",
            "Division_impossible",
            "Division_undefined",
            "Fpu_error",
            "Inexact",
            "Invalid_context",
            "Invalid_operation",
            "Malloc_error",
            "Not_implemented",
            "Overflow",
            "Rounded",
            "Subnormal",
            "Underflow",
        };

        private static readonly string[] mpd_signal_string = {
            "Clamped",
            "IEEE_Invalid_operation",
            "Division_by_zero",
            "IEEE_Invalid_operation",
            "IEEE_Invalid_operation",
            "IEEE_Invalid_operation",
            "Inexact",
            "IEEE_Invalid_operation",
            "IEEE_Invalid_operation",
            "IEEE_Invalid_operation",
            "Not_implemented",
            "Overflow",
            "Rounded",
            "Subnormal",
            "Underflow",
        };

        /* print conditions to buffer, separated by spaces */
        public static int mpd_snprint_flags(out string dest, int nmemb, uint flags)
        {
            var cp = new StringBuilder();
            int n, j;

            Debug.Assert(nmemb >= MPD_MAX_FLAG_STRING);

            dest = "";
            for (j = 0; j < MPD_NUM_FLAGS; j++)
            {
                if ((flags & (1U << j)) != 0)
                {
                    string item = mpd_flag_string[j] + " ";
                    n = item.Length; /* snprintf(cp, nmemb, "%s ", ...) */
                    if (n < 0 || n >= nmemb) { dest = cp.ToString(); return -1; }
                    cp.Append(item); nmemb -= n;
                }
            }

            if (cp.Length != 0)
            {
                cp.Length -= 1;
            }

            dest = cp.ToString();
            return cp.Length;
        }

        /* print conditions to buffer, in list form */
        public static int mpd_lsnprint_flags(out string dest, int nmemb, uint flags, string[] flag_string)
        {
            var cp = new StringBuilder();
            int n, j;

            Debug.Assert(nmemb >= MPD_MAX_FLAG_LIST);
            if (flag_string == null)
            {
                flag_string = mpd_flag_string;
            }

            cp.Append('[');
            --nmemb;

            for (j = 0; j < MPD_NUM_FLAGS; j++)
            {
                if ((flags & (1U << j)) != 0)
                {
                    string item = flag_string[j] + ", ";
                    n = item.Length;
                    if (n < 0 || n >= nmemb) { dest = cp.ToString(); return -1; }
                    cp.Append(item); nmemb -= n;
                }
            }

            /* erase the last ", " */
            if (cp.Length != 1)
            {
                cp.Length -= 2;
            }

            cp.Append(']');

            dest = cp.ToString();
            return cp.Length; /* strlen, without NUL terminator */
        }

        /* print signals to buffer, in list form */
        public static int mpd_lsnprint_signals(out string dest, int nmemb, uint flags, string[] signal_string)
        {
            var cp = new StringBuilder();
            int n, j;
            bool ieee_invalid_done = false;

            Debug.Assert(nmemb >= MPD_MAX_SIGNAL_LIST);
            if (signal_string == null)
            {
                signal_string = mpd_signal_string;
            }

            cp.Append('[');
            --nmemb;

            for (j = 0; j < MPD_NUM_FLAGS; j++)
            {
                uint f = flags & (1U << j);
                if (f != 0)
                {
                    if ((f & MPD_IEEE_Invalid_operation) != 0)
                    {
                        if (ieee_invalid_done)
                        {
                            continue;
                        }
                        ieee_invalid_done = true;
                    }
                    string item = signal_string[j] + ", ";
                    n = item.Length;
                    if (n < 0 || n >= nmemb) { dest = cp.ToString(); return -1; }
                    cp.Append(item); nmemb -= n;
                }
            }

            /* erase the last ", " */
            if (cp.Length != 1)
            {
                cp.Length -= 2;
            }

            cp.Append(']');

            dest = cp.ToString();
            return cp.Length; /* strlen, without NUL terminator */
        }

        /* The following two functions are mainly intended for debugging. */
        public static void mpd_fprint(TextWriter file, mpd_t dec)
        {
            string decstring;

            decstring = mpd_to_sci(dec, 1);
            if (decstring != null)
            {
                file.Write(decstring + "\n");
            }
            else
            {
                file.Write("mpd_fprint: output error\n"); /* GCOV_NOT_REACHED */
            }
        }

        // mpd_print
        public static void mpd_print(mpd_t dec)
        {
            string decstring;

            decstring = mpd_to_sci(dec, 1);
            if (decstring != null)
            {
                Console.Out.Write(decstring + "\n");
            }
            else
            {
                Console.Error.Write("mpd_fprint: output error\n"); /* GCOV_NOT_REACHED */
            }
        }
    }
}
