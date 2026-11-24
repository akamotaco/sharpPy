using System;
using System.Text;

namespace SharpPy.Modules.Datetime
{
    /// <summary>
    /// Modules/_datetimemodule.c: the string parsing utilities, the object constructors without
    /// range checking, the tzinfo helpers, the string format helpers and the wrappers of the time
    /// module (lines 710-1836).
    /// </summary>
    internal static partial class DatetimeModule
    {
        // ---- the abstract object protocol the C code calls -------------------------------------

        // PyNumber_* on two objects: binary_op1, then the TypeError.
        internal static PyObject Number(PyObject a, PyObject b, BinaryOpType op) =>
            PyVM.Instance.ExecuteBinaryOpType(a, b, op);

        // PyLong_Check: int, bool or a subclass of int.
        internal static bool PyLongCheck(PyObject o) => o is PyInt or PyBool;

        // PyFloat_Check
        internal static bool PyFloatCheck(PyObject o) => o is PyFloat;

        // PyUnicode_Check
        internal static bool PyUnicodeCheck(PyObject o) => o is PyString;

        // PyObject_CallMethod(obj, name, "O", arg)
        internal static PyObject CallMethod(PyObject obj, string name, params PyObject[] args) =>
            obj.GetAttribute(name).Call(args, null);

        // _PyImport_GetModuleAttrString
        internal static PyObject GetModuleAttr(string module, string name) =>
            PyImportSystem.Import(module).GetAttribute(name);

        // Py_TYPE(o)->tp_name
        internal static string TpName(PyObject o) => o.GetPyType().FullName;

        // PyArg_ParseTupleAndKeywords (Python/getargs.c vgetargskeywords) for a kwlist without
        // positional-only names: min is where '|' stands, max where '$' stands (int.MaxValue when
        // absent). Each argument found is handed to convert in the order of the loop; the result
        // has null for what was not given.
        internal static PyObject[] ParseTupleAndKeywords(string fname, PyObject[] args, PyDict kwargs, string[] kwlist,
            int min, int max = int.MaxValue, Action<int, PyObject> convert = null)
        {
            string name = fname ?? "function";
            string call = fname == null ? "" : "()";
            int len = kwlist.Length;
            int nargs = args.Length;
            int nkwargs = kwargs?.Length() ?? 0;
            if (nargs + nkwargs > len)
                throw PyTypeError.Create($"{name}{call} takes at most {len} {(nargs == 0 ? "keyword " : "")}argument{(len == 1 ? "" : "s")} ({nargs + nkwargs} given)");
            var result = new PyObject[len];
            for (int i = 0; i < len; i++)
            {
                if (i == max && max < nargs)
                {
                    if (max == 0)
                        throw PyTypeError.Create($"{name}{call} takes no positional arguments");
                    throw PyTypeError.Create($"{name}{call} takes {(min != int.MaxValue ? "at most" : "exactly")} {max} positional argument{(max == 1 ? "" : "s")} ({nargs} given)");
                }
                PyObject current = null;
                if (i < nargs)
                    current = args[i];
                else if (nkwargs != 0)
                {
                    current = KeywordArgument(kwargs, kwlist[i]);
                    if (current != null)
                        --nkwargs;
                }
                if (current != null)
                {
                    result[i] = current;
                    convert?.Invoke(i, current);
                    continue;
                }
                if (i < min)
                    throw PyTypeError.Create($"{name}{call} missing required argument '{kwlist[i]}' (pos {i + 1})");
                if (nkwargs == 0)
                    return result;
            }
            if (nkwargs > 0)
            {
                for (int i = 0; i < nargs; i++)
                    if (KeywordArgument(kwargs, kwlist[i]) != null)
                        throw PyTypeError.Create($"argument for {name}{call} given by name ('{kwlist[i]}') and position ({i + 1})");
                string where = fname ?? "this function";
                foreach (var pair in kwargs.InternalDict)
                {
                    if (pair.Key is not PyString key)
                        throw PyTypeError.Create("keywords must be strings");
                    if (Array.IndexOf(kwlist, key.Value) < 0)
                        throw PyTypeError.Create($"'{key.Value}' is an invalid keyword argument for {where}{call}");
                }
                throw PyTypeError.Create($"invalid keyword argument for {where}{call}");
            }
            return result;
        }

        // _PyDict_GetItemStringWithError
        private static PyObject KeywordArgument(PyDict kwargs, string name)
        {
            foreach (var pair in kwargs.InternalDict)
                if (pair.Key is PyString key && key.Value == name)
                    return pair.Value;
            return null;
        }

        // seterror of a converter's message: "fname() argument N must be T, not U".
        internal static Exception ConverterError(string fname, int iarg, string expected, PyObject arg) =>
            PyTypeError.Create($"{(fname == null ? "" : fname + "() ")}argument {iarg} must be {expected}, not {(arg is PyNone ? "None" : TpName(arg))}");

        // ---- String parsing utilities and helper functions --------------------------------------

        // is_digit
        private static bool IsDigit(byte c) => (uint)(c - '0') < 10;

        // parse_digits: the position after the digits, or -1 (NULL).
        private static int ParseDigits(byte[] s, int p, ref int var, int num_digits)
        {
            for (int i = 0; i < num_digits; ++i)
            {
                uint tmp = (uint)(s[p++] - '0');
                if (tmp > 9)
                    return -1;
                var *= 10;
                var += (int)tmp;
            }
            return p;
        }

        // The UTF-8 bytes of a string with the NUL that ends a C string (and room to read past it).
        internal static byte[] CString(string text)
        {
            int n = Encoding.UTF8.GetByteCount(text);
            var buffer = new byte[n + 4];
            Encoding.UTF8.GetBytes(text, 0, text.Length, buffer, 0);
            return buffer;
        }

        // parse_isoformat_date
        internal static int ParseIsoformatDate(byte[] dtstr, int start, int len, out int year, out int month, out int day)
        {
            year = month = day = 0;
            int p = start;
            p = ParseDigits(dtstr, p, ref year, 4);
            if (p < 0)
                return -1;
            bool uses_separator = dtstr[p] == '-';
            if (uses_separator)
                ++p;
            if (dtstr[p] == 'W')
            {
                p++;
                int iso_week = 0;
                int iso_day = 0;
                p = ParseDigits(dtstr, p, ref iso_week, 2);
                if (p < 0)
                    return -3;
                // len is a size_t: the -1 of an unknown separator is the largest length.
                if ((ulong)(p - start) < unchecked((ulong)(long)len))
                {
                    if (uses_separator && dtstr[p++] != '-')
                        return -2;
                    p = ParseDigits(dtstr, p, ref iso_day, 1);
                    if (p < 0)
                        return -4;
                }
                else
                {
                    iso_day = 1;
                }
                int rv = IsoToYmd(year, iso_week, iso_day, out year, out month, out day);
                return rv != 0 ? -3 + rv : 0;
            }
            p = ParseDigits(dtstr, p, ref month, 2);
            if (p < 0)
                return -1;
            if (uses_separator && dtstr[p++] != '-')
                return -2;
            p = ParseDigits(dtstr, p, ref day, 2);
            if (p < 0)
                return -1;
            return 0;
        }

        private static readonly int[] correction = { 100000, 10000, 1000, 100, 10 };

        // parse_hh_mm_ss_ff
        private static int ParseHhMmSsFf(byte[] s, int tstr, int tstr_end, out int hour, out int minute,
            out int second, out int microsecond)
        {
            var vals = new int[3];
            hour = minute = second = microsecond = 0;
            int p = tstr;
            int p_end = tstr_end;
            bool has_separator = true;
            for (int i = 0; i < 3; ++i)
            {
                p = ParseDigits(s, p, ref vals[i], 2);
                if (p < 0)
                {
                    (hour, minute, second) = (vals[0], vals[1], vals[2]);
                    return -3;
                }
                byte c = s[p++];
                if (i == 0)
                    has_separator = c == ':';
                if (p >= p_end)
                {
                    (hour, minute, second) = (vals[0], vals[1], vals[2]);
                    return c != 0 ? 1 : 0;
                }
                else if (has_separator && c == ':')
                    continue;
                else if (c == '.' || c == ',')
                    break;
                else if (!has_separator)
                    --p;
                else
                {
                    (hour, minute, second) = (vals[0], vals[1], vals[2]);
                    return -4;
                }
            }
            (hour, minute, second) = (vals[0], vals[1], vals[2]);
            int len_remains = p_end - p;
            int to_parse = len_remains;
            if (len_remains >= 6)
                to_parse = 6;
            p = ParseDigits(s, p, ref microsecond, to_parse);
            if (p < 0)
                return -3;
            if (to_parse < 6)
                microsecond *= correction[to_parse - 1];
            while (IsDigit(s[p]))
                ++p;
            return s[p] != 0 ? 1 : 0;
        }

        // parse_isoformat_time
        internal static int ParseIsoformatTime(byte[] s, int dtstr, int dtlen, out int hour, out int minute,
            out int second, out int microsecond, out int tzoffset, out int tzmicrosecond)
        {
            tzoffset = tzmicrosecond = 0;
            int p_end = dtstr + dtlen;
            int tzinfo_pos = dtstr;
            do
            {
                if (s[tzinfo_pos] == 'Z' || s[tzinfo_pos] == '+' || s[tzinfo_pos] == '-')
                    break;
            } while (++tzinfo_pos < p_end);
            int rv = ParseHhMmSsFf(s, dtstr, tzinfo_pos, out hour, out minute, out second, out microsecond);
            if (rv < 0)
                return rv;
            else if (tzinfo_pos == p_end)
                return rv == 1 ? -5 : 0;
            if (s[tzinfo_pos] == 'Z')
            {
                tzoffset = 0;
                tzmicrosecond = 0;
                return s[tzinfo_pos + 1] != 0 ? -5 : 1;
            }
            int tzsign = s[tzinfo_pos] == '-' ? -1 : 1;
            tzinfo_pos++;
            rv = ParseHhMmSsFf(s, tzinfo_pos, p_end, out int tzhour, out int tzminute, out int tzsecond, out tzmicrosecond);
            tzoffset = tzsign * ((tzhour * 3600) + (tzminute * 60) + tzsecond);
            tzmicrosecond *= tzsign;
            return rv != 0 ? -5 : 1;
        }

        // ---- Create various objects, mostly without range checking ------------------------------

        // date tp_alloc: a datetime struct for datetime and its subclasses.
        private static PyDateTimeDate DateAlloc(PyType type) =>
            type.IsSubclassOf(DateTimeType) ? new PyDateTimeDateTime(type) : new PyDateTimeDate(type);

        // set_date_fields
        internal static void SetDateFields(PyDateTimeDate self, int y, int m, int d)
        {
            self.Hashcode = -1;
            self.Year = y;
            self.Month = m;
            self.Day = d;
        }

        // new_date_ex
        internal static PyObject NewDateEx(int year, int month, int day, PyType type)
        {
            CheckDateArgs(year, month, day);
            var self = DateAlloc(type);
            SetDateFields(self, year, month, day);
            return self;
        }

        // new_date
        internal static PyObject NewDate(int year, int month, int day) => NewDateEx(year, month, day, DateType);

        // new_date_subclass_ex
        internal static PyObject NewDateSubclassEx(int year, int month, int day, PyObject cls)
        {
            if (ReferenceEquals(cls, DateType))
                return NewDateEx(year, month, day, (PyType)cls);
            if (ReferenceEquals(cls, DateTimeType))
                return NewDatetimeEx(year, month, day, 0, 0, 0, 0, PyNone.Instance, (PyType)cls);
            return cls.Call(new PyObject[] { PyInt.From(year), PyInt.From(month), PyInt.From(day) }, null);
        }

        // new_datetime_ex2
        internal static PyObject NewDatetimeEx2(int year, int month, int day, int hour, int minute,
            int second, int usecond, PyObject tzinfo, int fold, PyType type)
        {
            bool aware = tzinfo is not PyNone;
            CheckDateArgs(year, month, day);
            CheckTimeArgs(hour, minute, second, usecond, fold);
            CheckTzinfoSubclass(tzinfo);
            var self = new PyDateTimeDateTime(type);
            self.HasTzinfo = aware;
            SetDateFields(self, year, month, day);
            self.Hour = hour;
            self.Minute = minute;
            self.Second = second;
            self.Microsecond = usecond;
            if (aware)
                self.Tzinfo = tzinfo;
            self.Fold = fold;
            return self;
        }

        // new_datetime_ex
        internal static PyObject NewDatetimeEx(int year, int month, int day, int hour, int minute,
            int second, int usecond, PyObject tzinfo, PyType type) =>
            NewDatetimeEx2(year, month, day, hour, minute, second, usecond, tzinfo, 0, type);

        // new_datetime
        internal static PyObject NewDatetime(int y, int m, int d, int hh, int mm, int ss, int us, PyObject tzinfo, int fold) =>
            NewDatetimeEx2(y, m, d, hh, mm, ss, us, tzinfo, fold, DateTimeType);

        // call_subclass_fold
        private static PyObject CallSubclassFold(PyObject cls, int fold, PyObject[] args)
        {
            PyDict kwargs = null;
            if (fold != 0)
            {
                kwargs = new PyDict();
                kwargs.SetItem(PyString.From("fold"), PyInt.From(fold));
            }
            return cls.Call(args, kwargs);
        }

        // new_datetime_subclass_fold_ex
        internal static PyObject NewDatetimeSubclassFoldEx(int year, int month, int day, int hour, int minute,
            int second, int usecond, PyObject tzinfo, int fold, PyObject cls)
        {
            if (ReferenceEquals(cls, DateTimeType))
                return NewDatetime(year, month, day, hour, minute, second, usecond, tzinfo, fold);
            return CallSubclassFold(cls, fold, new PyObject[]
            {
                PyInt.From(year), PyInt.From(month), PyInt.From(day), PyInt.From(hour), PyInt.From(minute),
                PyInt.From(second), PyInt.From(usecond), tzinfo,
            });
        }

        // new_datetime_subclass_ex
        internal static PyObject NewDatetimeSubclassEx(int year, int month, int day, int hour, int minute,
            int second, int usecond, PyObject tzinfo, PyObject cls) =>
            NewDatetimeSubclassFoldEx(year, month, day, hour, minute, second, usecond, tzinfo, 0, cls);

        // new_time_ex2
        internal static PyObject NewTimeEx2(int hour, int minute, int second, int usecond, PyObject tzinfo, int fold, PyType type)
        {
            bool aware = tzinfo is not PyNone;
            CheckTimeArgs(hour, minute, second, usecond, fold);
            CheckTzinfoSubclass(tzinfo);
            var self = new PyDateTimeTime(type);
            self.HasTzinfo = aware;
            self.Hashcode = -1;
            self.Hour = hour;
            self.Minute = minute;
            self.Second = second;
            self.Microsecond = usecond;
            if (aware)
                self.Tzinfo = tzinfo;
            self.Fold = fold;
            return self;
        }

        // new_time_ex
        internal static PyObject NewTimeEx(int hour, int minute, int second, int usecond, PyObject tzinfo, PyType type) =>
            NewTimeEx2(hour, minute, second, usecond, tzinfo, 0, type);

        // new_time
        internal static PyObject NewTime(int hh, int mm, int ss, int us, PyObject tzinfo, int fold) =>
            NewTimeEx2(hh, mm, ss, us, tzinfo, fold, TimeType);

        // new_time_subclass_fold_ex
        internal static PyObject NewTimeSubclassFoldEx(int hour, int minute, int second, int usecond,
            PyObject tzinfo, int fold, PyObject cls)
        {
            if (ReferenceEquals(cls, TimeType))
                return NewTime(hour, minute, second, usecond, tzinfo, fold);
            return CallSubclassFold(cls, fold, new PyObject[]
            {
                PyInt.From(hour), PyInt.From(minute), PyInt.From(second), PyInt.From(usecond), tzinfo,
            });
        }

        // new_delta_ex
        internal static PyObject NewDeltaEx(int days, int seconds, int microseconds, bool normalize, PyType type)
        {
            if (normalize)
                NormalizeDSUs(ref days, ref seconds, ref microseconds);
            CheckDeltaDayRange(days);
            return new PyDateTimeDelta(type)
            {
                Hashcode = -1,
                Days = days,
                Seconds = seconds,
                Microseconds = microseconds,
            };
        }

        // new_delta
        internal static PyObject NewDelta(int d, int s, int us, bool normalize) => NewDeltaEx(d, s, us, normalize, DeltaType);

        // The interned UTC timezone instance; the interned Epoch datetime instance.
        internal static PyObject PyDateTime_TimeZone_UTC;
        internal static PyObject PyDateTime_Epoch;

        // create_timezone
        internal static PyObject CreateTimezone(PyObject offset, PyObject name) =>
            new PyDateTimeTimeZone(TimeZoneType) { Offset = offset, Name = name };

        // new_timezone
        internal static PyObject NewTimezone(PyObject offset, PyObject name)
        {
            var delta = (PyDateTimeDelta)offset;
            if (name == null && !DeltaBool(delta))
                return PyDateTime_TimeZone_UTC;
            if ((delta.Days == -1 && delta.Seconds == 0 && delta.Microseconds < 1) || delta.Days < -1 || delta.Days >= 1)
                throw PyValueError.Create("offset must be a timedelta strictly between -timedelta(hours=24) and " +
                                          $"timedelta(hours=24), not {offset.ToRepr().Value}.");
            return CreateTimezone(offset, name);
        }

        // ---- tzinfo helpers ---------------------------------------------------------------------

        // check_tzinfo_subclass
        internal static void CheckTzinfoSubclass(PyObject p)
        {
            if (p is PyNone || p is PyDateTimeTZInfo)
                return;
            throw PyTypeError.Create($"tzinfo argument must be None or of a tzinfo subclass, not type '{TpName(p)}'");
        }

        // get_tzinfo_member: the tzinfo, or null.
        internal static PyObject GetTzinfoMember(PyObject self) => self switch
        {
            PyDateTimeDateTime { HasTzinfo: true } dt => dt.Tzinfo,
            PyDateTimeTime { HasTzinfo: true } t => t.Tzinfo,
            _ => null,
        };

        // call_tzinfo_method
        internal static PyObject CallTzinfoMethod(PyObject tzinfo, string name, PyObject tzinfoarg)
        {
            if (tzinfo is PyNone)
                return PyNone.Instance;
            var offset = CallMethod(tzinfo, name, tzinfoarg);
            if (offset is PyNone)
                return offset;
            if (offset is PyDateTimeDelta delta)
            {
                if ((delta.Days == -1 && delta.Seconds == 0 && delta.Microseconds < 1) || delta.Days < -1 || delta.Days >= 1)
                    throw PyValueError.Create("offset must be a timedelta strictly between -timedelta(hours=24) and timedelta(hours=24).");
            }
            else
            {
                throw PyTypeError.Create($"tzinfo.{name}() must return None or timedelta, not '{TpName(offset)}'");
            }
            return offset;
        }

        // call_utcoffset
        internal static PyObject CallUtcoffset(PyObject tzinfo, PyObject tzinfoarg) => CallTzinfoMethod(tzinfo, "utcoffset", tzinfoarg);

        // call_dst
        internal static PyObject CallDst(PyObject tzinfo, PyObject tzinfoarg) => CallTzinfoMethod(tzinfo, "dst", tzinfoarg);

        // call_tzname
        internal static PyObject CallTzname(PyObject tzinfo, PyObject tzinfoarg)
        {
            if (tzinfo is PyNone)
                return PyNone.Instance;
            var result = CallMethod(tzinfo, "tzname", tzinfoarg);
            if (result is PyNone)
                return result;
            if (!PyUnicodeCheck(result))
                throw PyTypeError.Create($"tzinfo.tzname() must return None or a string, not '{TpName(result)}'");
            return result;
        }

        // append_keyword_tzinfo
        internal static string AppendKeywordTzinfo(string repr, PyObject tzinfo)
        {
            if (tzinfo is PyNone)
                return repr;
            return $"{repr.Substring(0, repr.Length - 1)}, tzinfo={tzinfo.ToRepr().Value})";
        }

        // append_keyword_fold
        internal static string AppendKeywordFold(string repr, int fold)
        {
            if (fold == 0)
                return repr;
            return $"{repr.Substring(0, repr.Length - 1)}, fold={fold})";
        }

        // tzinfo_from_isoformat_results
        internal static PyObject TzinfoFromIsoformatResults(int rv, int tzoffset, int tz_useconds)
        {
            if (rv == 1)
            {
                if (tzoffset == 0)
                    return PyDateTime_TimeZone_UTC;
                var delta = NewDelta(0, tzoffset, tz_useconds, true);
                return NewTimezone(delta, null);
            }
            return PyNone.Instance;
        }

        // ---- String format helpers --------------------------------------------------------------

        private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        private static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        // format_ctime
        internal static PyObject FormatCtime(PyDateTimeDate date, int hours, int minutes, int seconds)
        {
            int wday = Weekday(date.Year, date.Month, date.Day);
            return PyString.From($"{DayNames[wday]} {MonthNames[date.Month - 1]} {date.Day,2} {hours:D2}:{minutes:D2}:{seconds:D2} {date.Year:D4}");
        }

        // format_utcoffset: null (buf "") when utcoffset() is None.
        internal static string FormatUtcoffset(string sep, PyObject tzinfo, PyObject tzinfoarg)
        {
            var offset = CallUtcoffset(tzinfo, tzinfoarg);
            if (offset is PyNone)
                return "";
            char sign;
            var delta = (PyDateTimeDelta)offset;
            if (delta.Days < 0)
            {
                sign = '-';
                delta = (PyDateTimeDelta)DeltaNegative(delta);
            }
            else
            {
                sign = '+';
            }
            int microseconds = delta.Microseconds;
            int seconds = delta.Seconds;
            int minutes = Divmod(seconds, 60, out seconds);
            int hours = Divmod(minutes, 60, out minutes);
            if (microseconds != 0)
                return $"{sign}{hours:D2}{sep}{minutes:D2}{sep}{seconds:D2}.{microseconds:D6}";
            if (seconds != 0)
                return $"{sign}{hours:D2}{sep}{minutes:D2}{sep}{seconds:D2}";
            return $"{sign}{hours:D2}{sep}{minutes:D2}";
        }

        // make_somezreplacement
        private static PyObject MakeSomezreplacement(PyObject obj, string sep, PyObject tzinfoarg)
        {
            var tzinfo = GetTzinfoMember(obj);
            if (tzinfo == null || tzinfo is PyNone)
                return PyString.From("");
            return PyString.From(FormatUtcoffset(sep, tzinfo, tzinfoarg));
        }

        // make_Zreplacement
        private static PyObject MakeZreplacement(PyObject obj, PyObject tzinfoarg)
        {
            var tzinfo = GetTzinfoMember(obj);
            var Zreplacement = PyString.From("");
            if (tzinfo == null || tzinfo is PyNone)
                return Zreplacement;
            var temp = CallTzname(tzinfo, tzinfoarg);
            if (temp is PyNone)
                return Zreplacement;
            var replaced = CallMethod(temp, "replace", PyString.From("%"), PyString.From("%%"));
            if (!PyUnicodeCheck(replaced))
                throw PyTypeError.Create("tzname.replace() did not return a string");
            return replaced;
        }

        // make_freplacement
        private static PyObject MakeFreplacement(PyObject obj)
        {
            int us = obj switch
            {
                PyDateTimeTime t => t.Microsecond,
                PyDateTimeDateTime dt => dt.Microsecond,
                _ => 0,
            };
            return PyString.From(us.ToString("D6"));
        }

        // wrap_strftime
        internal static PyObject WrapStrftime(PyObject obj, PyObject format, PyObject timetuple, PyObject tzinfoarg)
        {
            var strftime = GetModuleAttr("time", "strftime");
            PyObject zreplacement = null, colonzreplacement = null, Zreplacement = null, freplacement = null;
            var fmt = (PyString)format;
            var writer = new PyUnicodeWriter();
            int flen = fmt.CodePointLength();
            int i = 0, start = 0, end;
            while (i != flen)
            {
                i = fmt.FindChar('%', i, flen, 1);
                if (i < 0)
                    break;
                end = i;
                i++;
                if (i == flen)
                    break;
                int ch = fmt.ReadChar(i);
                i++;
                PyObject replacement;
                if (ch == 'z')
                {
                    zreplacement ??= MakeSomezreplacement(obj, "", tzinfoarg);
                    replacement = zreplacement;
                }
                else if (ch == ':' && i < flen && fmt.ReadChar(i) == 'z')
                {
                    i++;
                    colonzreplacement ??= MakeSomezreplacement(obj, ":", tzinfoarg);
                    replacement = colonzreplacement;
                }
                else if (ch == 'Z')
                {
                    Zreplacement ??= MakeZreplacement(obj, tzinfoarg);
                    replacement = Zreplacement;
                }
                else if (ch == 'f')
                {
                    freplacement ??= MakeFreplacement(obj);
                    replacement = freplacement;
                }
                else
                {
                    continue;
                }
                writer.WriteSubstring(fmt, start, end);
                start = i;
                writer.WriteStr((PyString)replacement);
            }
            PyObject newformat;
            if (start == 0)
                newformat = format;
            else
            {
                writer.WriteSubstring(fmt, start, flen);
                newformat = writer.Finish();
            }
            return strftime.Call(new[] { newformat, timetuple }, null);
        }

        // ---- Wrap functions from the time module ------------------------------------------------

        // time_time
        internal static PyObject TimeTime() => GetModuleAttr("time", "time").Call(Array.Empty<PyObject>(), null);

        // build_struct_time
        internal static PyObject BuildStructTime(int y, int m, int d, int hh, int mm, int ss, int dstflag)
        {
            var struct_time = GetModuleAttr("time", "struct_time");
            var items = new PyObject[]
            {
                PyInt.From(y), PyInt.From(m), PyInt.From(d), PyInt.From(hh), PyInt.From(mm), PyInt.From(ss),
                PyInt.From(Weekday(y, m, d)), PyInt.From(DaysBeforeMonth(y, m) + d), PyInt.From(dstflag),
            };
            return struct_time.Call(new PyObject[] { PyTuple.From(items) }, null);
        }

        // ---- Miscellaneous helpers --------------------------------------------------------------

        // diff_to_bool: Py_RETURN_RICHCOMPARE(diff, 0, op)
        internal static PyObject DiffToBool(int diff, PyObject.CompareOp op) => PyBool.FromBool(op switch
        {
            PyObject.CompareOp.EQ => diff == 0,
            PyObject.CompareOp.NE => diff != 0,
            PyObject.CompareOp.LT => diff < 0,
            PyObject.CompareOp.LE => diff <= 0,
            PyObject.CompareOp.GT => diff > 0,
            _ => diff >= 0,
        });

        // cmperror
        internal static Exception Cmperror(PyObject a, PyObject b) =>
            PyTypeError.Create($"can't compare {TpName(a)} to {TpName(b)}");

        // Conversion factors, set by the module init function.
        internal static readonly PyObject us_per_ms = PyInt.From(1000);
        internal static readonly PyObject us_per_second = PyInt.From(1000000);
        internal static readonly PyObject us_per_minute = PyInt.From(60000000L);
        internal static readonly PyObject us_per_hour = PyInt.From(3600000000L);
        internal static readonly PyObject us_per_day = PyInt.From(86400000000L);
        internal static readonly PyObject us_per_week = PyInt.From(604800000000L);
        internal static readonly PyObject seconds_per_day = PyInt.From(86400);
    }
}
