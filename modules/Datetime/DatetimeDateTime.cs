using System;
using System.Text;

namespace SharpPy.Modules.Datetime
{
    /// <summary>Modules/_datetimemodule.c: PyDateTime_DateTime implementation (lines 4784-6651).</summary>
    internal static partial class DatetimeModule
    {
        internal const int _PyDateTime_DATETIME_DATASIZE = 10;

        // datetime_hour, datetime_minute, datetime_second, datetime_microsecond, datetime_tzinfo, datetime_fold
        private static PyObject DatetimeHour(PyDateTimeDateTime self) => PyInt.From(self.Hour);
        private static PyObject DatetimeMinute(PyDateTimeDateTime self) => PyInt.From(self.Minute);
        private static PyObject DatetimeSecond(PyDateTimeDateTime self) => PyInt.From(self.Second);
        private static PyObject DatetimeMicrosecond(PyDateTimeDateTime self) => PyInt.From(self.Microsecond);
        private static PyObject DatetimeTzinfo(PyDateTimeDateTime self) => self.TzinfoOrNone;
        private static PyObject DatetimeFold(PyDateTimeDateTime self) => PyInt.From(self.Fold);

        private static readonly string[] datetime_kws =
            { "year", "month", "day", "hour", "minute", "second", "microsecond", "tzinfo", "fold" };

        // The data bytes of PyDateTime_DateTime: the date's four, then hour, minute, second and the
        // microsecond in three bytes, big endian.
        internal static byte[] DatetimeData(PyDateTimeDateTime self) => new[]
        {
            (byte)((self.Year & 0xff00) >> 8), (byte)(self.Year & 0x00ff), (byte)self.Month, (byte)self.Day,
            (byte)self.Hour, (byte)self.Minute, (byte)self.Second,
            (byte)((self.Microsecond & 0xff0000) >> 16), (byte)((self.Microsecond & 0x00ff00) >> 8), (byte)(self.Microsecond & 0x0000ff),
        };

        // datetime_from_pickle
        private static PyObject DatetimeFromPickle(PyType type, byte[] pdata, PyObject tzinfo)
        {
            bool aware = tzinfo is not PyNone;
            if (aware && tzinfo is not PyDateTimeTZInfo)
                throw PyTypeError.Create("bad tzinfo state arg");
            var me = new PyDateTimeDateTime(type);
            SetDateData(me, pdata);
            me.Hour = pdata[4];
            me.Minute = pdata[5];
            me.Second = pdata[6];
            me.Microsecond = (pdata[7] << 16) | (pdata[8] << 8) | pdata[9];
            me.Hashcode = -1;
            me.HasTzinfo = aware;
            if (aware)
                me.Tzinfo = tzinfo;
            if ((pdata[2] & (1 << 7)) != 0)
            {
                me.Month -= 128;
                me.Fold = 1;
            }
            else
            {
                me.Fold = 0;
            }
            return me;
        }

        // datetime_new
        private static PyObject DatetimeNew(PyType type, PyObject[] args, PyDict kw)
        {
            int year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0, usecond = 0, fold = 0;
            PyObject tzinfo = PyNone.Instance;
            if (args.Length >= 1 && args.Length <= 2)
            {
                var state = args[0];
                if (args.Length == 2)
                    tzinfo = args[1];
                if (state is PyBytes bytes)
                {
                    var data = bytes.Value;
                    if (data.Length == _PyDateTime_DATETIME_DATASIZE && MonthIsSane(data[2] & 0x7F))
                        return DatetimeFromPickle(type, data, tzinfo);
                }
                else if (state is PyString text)
                {
                    if (text.CodePointLength() == _PyDateTime_DATETIME_DATASIZE && MonthIsSane(text.ReadChar(2) & 0x7F))
                        return DatetimeFromPickle(type, PickledLatin1(text, "datetime"), tzinfo);
                }
                tzinfo = PyNone.Instance;
            }
            ParseTupleAndKeywords(null, args, kw, datetime_kws, 3, 8, (i, v) =>
            {
                switch (i)
                {
                    case 0: year = PyArgs.ParseInt(v); break;
                    case 1: month = PyArgs.ParseInt(v); break;
                    case 2: day = PyArgs.ParseInt(v); break;
                    case 3: hour = PyArgs.ParseInt(v); break;
                    case 4: minute = PyArgs.ParseInt(v); break;
                    case 5: second = PyArgs.ParseInt(v); break;
                    case 6: usecond = PyArgs.ParseInt(v); break;
                    case 7: tzinfo = v; break;
                    default: fold = PyArgs.ParseInt(v); break;
                }
            });
            return NewDatetimeEx2(year, month, day, hour, minute, second, usecond, tzinfo, fold, type);
        }

        // TM_FUNC: _PyTime_localtime or _PyTime_gmtime.
        private static readonly Func<long, CTm> _PyTime_localtime = TimeModule.LocalTimeOf;
        private static readonly Func<long, CTm> _PyTime_gmtime = TimeModule.GmTimeOf;

        // As of version 2015f max fold in IANA database is 23 hours at 1969-09-30 13:00:00 in Kwajalein.
        private const long max_fold_seconds = 24 * 3600;
        // NB: date(1970,1,1).toordinal() == 719163
        private const long epoch = 719163L * 24 * 60 * 60;

        // utc_to_seconds
        private static long UtcToSeconds(int year, int month, int day, int hour, int minute, int second)
        {
            if (year < MINYEAR || year > MAXYEAR)
                throw PyValueError.Create($"year {year} is out of range");
            long ordinal = YmdToOrd(year, month, day);
            return ((ordinal * 24 + hour) * 60 + minute) * 60 + second;
        }

        // local
        private static long Local(long u)
        {
            u -= epoch;
            long t = u;
            var local_time = _PyTime_localtime(t);
            return UtcToSeconds(local_time.Year + 1900, local_time.Mon + 1, local_time.Mday,
                local_time.Hour, local_time.Min, local_time.Sec);
        }

        // datetime_from_timet_and_us
        private static PyObject DatetimeFromTimetAndUs(PyObject cls, Func<long, CTm> f, long timet, int us, PyObject tzinfo)
        {
            var tm = f(timet);
            int year = tm.Year + 1900;
            int month = tm.Mon + 1;
            int day = tm.Mday;
            int hour = tm.Hour;
            int minute = tm.Min;
            int second = Math.Min(59, tm.Sec);
            int fold = 0;
            // On Windows, localtime_s refuses negative timestamps: no fold detection near the epoch.
            if (tzinfo is PyNone && ReferenceEquals(f, _PyTime_localtime)
                && (!OperatingSystem.IsWindows() || timet - max_fold_seconds > 0))
            {
                long result_seconds = UtcToSeconds(year, month, day, hour, minute, second);
                long probe_seconds = Local(epoch + timet - max_fold_seconds);
                long transition = result_seconds - probe_seconds - max_fold_seconds;
                if (transition < 0)
                {
                    probe_seconds = Local(epoch + timet + transition);
                    if (probe_seconds == result_seconds)
                        fold = 1;
                }
            }
            return NewDatetimeSubclassFoldEx(year, month, day, hour, minute, second, us, tzinfo, fold, cls);
        }

        // datetime_from_timestamp
        private static PyObject DatetimeFromTimestamp(PyObject cls, Func<long, CTm> f, PyObject timestamp, PyObject tzinfo)
        {
            var (timet, us) = SharpPy.PyTime.ObjectToTimeval(timestamp, PyTimeRound.HalfEven);
            return DatetimeFromTimetAndUs(cls, f, timet, (int)us, tzinfo);
        }

        // datetime_best_possible
        private static PyObject DatetimeBestPossible(PyObject cls, Func<long, CTm> f, PyObject tzinfo)
        {
            long ts = SharpPy.PyTime.GetSystemClock();
            var (secs, us) = SharpPy.PyTime.AsTimeval(ts, PyTimeRound.Floor);
            return DatetimeFromTimetAndUs(cls, f, secs, (int)us, tzinfo);
        }

        private static readonly string[] now_keywords = { "tz" };

        // datetime_datetime_now (clinic): tz=None
        private static PyObject DatetimeDatetimeNow(PyType type, PyObject[] args, PyDict kw)
        {
            var parsed = PyArgs.Unpack("now", args, kw, now_keywords, 0, 0, 1);
            return DatetimeDatetimeNowImpl(type, parsed[0] ?? PyNone.Instance);
        }

        // datetime_datetime_now_impl
        private static PyObject DatetimeDatetimeNowImpl(PyType type, PyObject tz)
        {
            CheckTzinfoSubclass(tz);
            var self = DatetimeBestPossible(type, tz is PyNone ? _PyTime_localtime : _PyTime_gmtime, tz);
            if (tz is not PyNone)
                return CallMethod(tz, "fromutc", self);
            return self;
        }

        // datetime_utcnow
        private static PyObject DatetimeUtcnow(PyObject cls)
        {
            PyWarnings.Warn(PyType.DeprecationWarningType,
                "datetime.datetime.utcnow() is deprecated and scheduled for removal in a " +
                "future version. Use timezone-aware objects to represent datetimes " +
                "in UTC: datetime.datetime.now(datetime.UTC).", 1);
            return DatetimeBestPossible(cls, _PyTime_gmtime, PyNone.Instance);
        }

        private static readonly string[] fromtimestamp_keywords = { "timestamp", "tz" };

        // datetime_fromtimestamp
        internal static PyObject DatetimeFromtimestamp(PyObject cls, PyObject[] args, PyDict kw)
        {
            var parsed = ParseTupleAndKeywords("fromtimestamp", args, kw, fromtimestamp_keywords, 1);
            var timestamp = parsed[0];
            var tzinfo = parsed[1] ?? PyNone.Instance;
            CheckTzinfoSubclass(tzinfo);
            var self = DatetimeFromTimestamp(cls, tzinfo is PyNone ? _PyTime_localtime : _PyTime_gmtime, timestamp, tzinfo);
            if (tzinfo is not PyNone)
                return CallMethod(tzinfo, "fromutc", self);
            return self;
        }

        // datetime_utcfromtimestamp
        private static PyObject DatetimeUtcfromtimestamp(PyObject cls, PyObject[] args)
        {
            PyWarnings.Warn(PyType.DeprecationWarningType,
                "datetime.datetime.utcfromtimestamp() is deprecated and scheduled for removal " +
                "in a future version. Use timezone-aware objects to represent " +
                "datetimes in UTC: datetime.datetime.fromtimestamp(timestamp, datetime.UTC).", 1);
            PyArgs.ParseTuple("utcfromtimestamp", args, 1, 1);
            return DatetimeFromTimestamp(cls, _PyTime_gmtime, args[0], PyNone.Instance);
        }

        // datetime_strptime
        private static PyObject DatetimeStrptime(PyObject cls, PyObject[] args)
        {
            PyArgs.ParseTuple("strptime", args, 2, 2);
            var str = UnicodeArg("strptime", 1, args[0]);
            var format = UnicodeArg("strptime", 2, args[1]);
            var module = PyImportSystem.Import("_strptime");
            return CallMethod(module, "_strptime_datetime", cls, str, format);
        }

        private static readonly string[] combine_keywords = { "date", "time", "tzinfo" };

        // datetime_combine
        private static PyObject DatetimeCombine(PyObject cls, PyObject[] args, PyDict kw)
        {
            var parsed = ParseTupleAndKeywords("combine", args, kw, combine_keywords, 2, convert: (i, v) =>
            {
                if (i == 0 && v is not PyDateTimeDate)
                    throw ConverterError("combine", 1, "datetime.date", v);
                if (i == 1 && v is not PyDateTimeTime)
                    throw ConverterError("combine", 2, "datetime.time", v);
            });
            var date = (PyDateTimeDate)parsed[0];
            var time = (PyDateTimeTime)parsed[1];
            var tzinfo = parsed[2];
            if (tzinfo == null)
                tzinfo = time.HasTzinfo ? time.Tzinfo : PyNone.Instance;
            return NewDatetimeSubclassFoldEx(date.Year, date.Month, date.Day, time.Hour, time.Minute,
                time.Second, time.Microsecond, tzinfo, time.Fold, cls);
        }

        private static readonly int[] potential_separators = { 7, 8, 10 };

        // _sanitize_isoformat_str: null when too short; the separator, if a surrogate, made 'T'.
        private static PyString SanitizeIsoformatStr(PyString dtstr)
        {
            int len = dtstr.CodePointLength();
            if (len < 7)
                return null;
            int surrogate_separator = 0;
            foreach (int pos in potential_separators)
            {
                if (pos > len)
                    break;
                // PyUnicode_READ at the length reads the terminating NUL.
                int ch = pos < len ? dtstr.ReadChar(pos) : 0;
                if (ch >= 0xD800 && ch <= 0xDFFF)
                {
                    surrogate_separator = pos;
                    break;
                }
            }
            if (surrogate_separator == 0)
                return dtstr;
            var str_out = new PyUnicodeWriter();
            str_out.WriteSubstring(dtstr, 0, surrogate_separator);
            str_out.WriteChar('T');
            str_out.WriteSubstring(dtstr, surrogate_separator + 1, len);
            return str_out.Finish();
        }

        // _find_isoformat_datetime_separator
        private static int FindIsoformatDatetimeSeparator(byte[] dtstr, int len)
        {
            const byte date_separator = (byte)'-';
            const byte week_indicator = (byte)'W';
            if (len == 7)
                return 7;
            if (dtstr[4] == date_separator)
            {
                if (dtstr[5] == week_indicator)
                {
                    if (len < 8)
                        return -1;
                    if (len > 8 && dtstr[8] == date_separator)
                    {
                        if (len == 9)
                            return -1;
                        if (len > 10 && IsDigit(dtstr[10]))
                            return 8;
                        return 10;
                    }
                    return 8;
                }
                return 10;
            }
            if (dtstr[4] == week_indicator)
            {
                int idx = 7;
                for (; idx < len; ++idx)
                    if (!IsDigit(dtstr[idx]))
                        break;
                if (idx < 9)
                    return idx;
                return idx % 2 == 0 ? 7 : 8;
            }
            return 8;
        }

        // datetime_fromisoformat
        private static PyObject DatetimeFromisoformat(PyObject cls, PyObject dtstr)
        {
            if (!PyUnicodeCheck(dtstr))
                throw PyTypeError.Create("fromisoformat: argument must be str");
            var dtstr_clean = SanitizeIsoformatStr((PyString)dtstr);
            if (dtstr_clean == null)
                throw PyValueError.Create($"Invalid isoformat string: {dtstr.ToRepr().Value}");
            var dt_ptr = AsUtf8OrNull(dtstr_clean, out int len);
            if (dt_ptr == null)
                throw PyValueError.Create($"Invalid isoformat string: {dtstr.ToRepr().Value}");
            int separator_location = FindIsoformatDatetimeSeparator(dt_ptr, len);
            int p = 0;
            int hour = 0, minute = 0, second = 0, microsecond = 0, tzoffset = 0, tzusec = 0;
            // A separator of -1 is read by parse_isoformat_date as a length: it never ends the week loop.
            int rv = ParseIsoformatDate(dt_ptr, p, separator_location, out int year, out int month, out int day);
            if (rv == 0 && len > separator_location)
            {
                p += separator_location;
                if ((dt_ptr[p] & 0x80) == 0)
                    p += 1;
                else
                {
                    switch (dt_ptr[p] & 0xf0)
                    {
                        case 0xe0: p += 3; break;
                        case 0xf0: p += 4; break;
                        default: p += 2; break;
                    }
                }
                len -= p;
                rv = ParseIsoformatTime(dt_ptr, p, len, out hour, out minute, out second, out microsecond, out tzoffset, out tzusec);
            }
            if (rv < 0)
                throw PyValueError.Create($"Invalid isoformat string: {dtstr.ToRepr().Value}");
            var tzinfo = TzinfoFromIsoformatResults(rv, tzoffset, tzusec);
            return NewDatetimeSubclassEx(year, month, day, hour, minute, second, microsecond, tzinfo, cls);
        }

        // datetime_utcoffset
        internal static PyObject DatetimeUtcoffset(PyObject self) => CallUtcoffset(((PyDateTimeDateTime)self).TzinfoOrNone, self);

        // datetime_dst
        internal static PyObject DatetimeDst(PyObject self) => CallDst(((PyDateTimeDateTime)self).TzinfoOrNone, self);

        // datetime_tzname
        private static PyObject DatetimeTzname(PyObject self) => CallTzname(((PyDateTimeDateTime)self).TzinfoOrNone, self);

        // add_datetime_timedelta
        internal static PyObject AddDatetimeTimedelta(PyDateTimeDateTime date, PyDateTimeDelta delta, int factor)
        {
            int year = date.Year;
            int month = date.Month;
            int day = date.Day + delta.Days * factor;
            int hour = date.Hour;
            int minute = date.Minute;
            int second = date.Second + delta.Seconds * factor;
            int microsecond = date.Microsecond + delta.Microseconds * factor;
            NormalizeDatetime(ref year, ref month, ref day, ref hour, ref minute, ref second, ref microsecond);
            return NewDatetimeSubclassEx(year, month, day, hour, minute, second, microsecond,
                date.TzinfoOrNone, date.GetPyType());
        }

        // datetime_add
        internal static PyObject DatetimeAdd(PyObject left, PyObject right)
        {
            if (left is PyDateTimeDateTime dt)
            {
                if (right is PyDateTimeDelta delta)
                    return AddDatetimeTimedelta(dt, delta, 1);
            }
            else if (left is PyDateTimeDelta delta)
            {
                return AddDatetimeTimedelta((PyDateTimeDateTime)right, delta, 1);
            }
            return PyNotImplemented.Instance;
        }

        // datetime_subtract
        internal static PyObject DatetimeSubtract(PyObject left, PyObject right)
        {
            if (left is not PyDateTimeDateTime l)
                return PyNotImplemented.Instance;
            if (right is PyDateTimeDateTime r)
            {
                PyObject offset1, offset2, offdiff = null;
                if (ReferenceEquals(l.TzinfoOrNone, r.TzinfoOrNone))
                {
                    offset1 = PyNone.Instance;
                    offset2 = PyNone.Instance;
                }
                else
                {
                    offset1 = DatetimeUtcoffset(left);
                    offset2 = DatetimeUtcoffset(right);
                    if ((offset1 is not PyNone) != (offset2 is not PyNone))
                        throw PyTypeError.Create("can't subtract offset-naive and offset-aware datetimes");
                }
                if (!ReferenceEquals(offset1, offset2) &&
                    DeltaCmp((PyDateTimeDelta)offset1, (PyDateTimeDelta)offset2) != 0)
                    offdiff = DeltaSubtract(offset1, offset2);
                int delta_d = YmdToOrd(l.Year, l.Month, l.Day) - YmdToOrd(r.Year, r.Month, r.Day);
                int delta_s = (l.Hour - r.Hour) * 3600 + (l.Minute - r.Minute) * 60 + (l.Second - r.Second);
                int delta_us = l.Microsecond - r.Microsecond;
                var result = NewDelta(delta_d, delta_s, delta_us, true);
                if (offdiff != null)
                    result = DeltaSubtract(result, offdiff);
                return result;
            }
            if (right is PyDateTimeDelta delta)
                return AddDatetimeTimedelta(l, delta, -1);
            return PyNotImplemented.Instance;
        }

        // datetime_repr
        internal static PyObject DatetimeRepr(PyDateTimeDateTime self)
        {
            string type_name = TpName(self);
            string baserepr;
            if (self.Microsecond != 0)
                baserepr = $"{type_name}({self.Year}, {self.Month}, {self.Day}, {self.Hour}, {self.Minute}, {self.Second}, {self.Microsecond})";
            else if (self.Second != 0)
                baserepr = $"{type_name}({self.Year}, {self.Month}, {self.Day}, {self.Hour}, {self.Minute}, {self.Second})";
            else
                baserepr = $"{type_name}({self.Year}, {self.Month}, {self.Day}, {self.Hour}, {self.Minute})";
            if (self.Fold != 0)
                baserepr = AppendKeywordFold(baserepr, self.Fold);
            if (!self.HasTzinfo)
                return PyString.From(baserepr);
            return PyString.From(AppendKeywordTzinfo(baserepr, self.Tzinfo));
        }

        // datetime_str
        internal static PyObject DatetimeStr(PyDateTimeDateTime self) =>
            CallMethod(self, "isoformat", PyString.From(" "));

        private static readonly string[] isoformat_datetime_keywords = { "sep", "timespec" };

        // The "C" converter: a str of one character.
        private static int CharArg(string fname, int iarg, PyObject arg)
        {
            if (arg is not PyString text || text.CodePointLength() != 1)
                throw ConverterError(fname, iarg, "a unicode character", arg);
            return text.ReadChar(0);
        }

        // datetime_isoformat
        private static PyObject DatetimeIsoformat(PyDateTimeDateTime self, PyObject[] args, PyDict kw)
        {
            int sep = 'T';
            string timespec = null;
            ParseTupleAndKeywords("isoformat", args, kw, isoformat_datetime_keywords, 0, convert: (i, v) =>
            {
                if (i == 0)
                    sep = CharArg("isoformat", 1, v);
                else
                    timespec = StringArg("isoformat", 2, v);
            });
            int us = self.Microsecond;
            int given_spec;
            if (timespec == null || timespec == "auto")
            {
                given_spec = us == 0 ? 2 : 4;
            }
            else
            {
                for (given_spec = 0; given_spec < timespec_names.Length; given_spec++)
                {
                    if (timespec == timespec_names[given_spec])
                    {
                        if (given_spec == 3)
                            us = us / 1000;
                        break;
                    }
                }
            }
            if (given_spec == timespec_names.Length)
                throw PyValueError.Create("Unknown timespec value");
            // PyUnicode_FromFormat's %c: the separator is one code point.
            var result = new PyUnicodeWriter();
            result.WriteUnits($"{self.Year:D4}-{self.Month:D2}-{self.Day:D2}");
            result.WriteChar(sep);
            result.WriteUnits(FormatTimespec(given_spec, self.Hour, self.Minute, self.Second, us));
            if (self.HasTzinfo)
                result.WriteUnits(FormatUtcoffset(":", self.Tzinfo, self));
            return result.Finish();
        }

        // datetime_ctime
        private static PyObject DatetimeCtime(PyDateTimeDateTime self) => FormatCtime(self, self.Hour, self.Minute, self.Second);

        // flip_fold
        private static PyObject FlipFold(PyDateTimeDateTime dt) =>
            NewDatetimeEx2(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Microsecond,
                dt.TzinfoOrNone, dt.Fold == 0 ? 1 : 0, dt.GetPyType());

        // get_flip_fold_offset
        private static PyObject GetFlipFoldOffset(PyDateTimeDateTime dt) => DatetimeUtcoffset(FlipFold(dt));

        // delta_cmp of two offsets where either may be None.
        private static bool OffsetsDiffer(PyObject a, PyObject b) =>
            !ReferenceEquals(a, b) && (a is not PyDateTimeDelta x || b is not PyDateTimeDelta y || DeltaCmp(x, y) != 0);

        // pep495_eq_exception
        private static bool Pep495EqException(PyDateTimeDateTime self, PyDateTimeDateTime other, PyObject offset_self, PyObject offset_other)
        {
            var flip_offset = GetFlipFoldOffset(self);
            if (OffsetsDiffer(flip_offset, offset_self))
                return true;
            flip_offset = GetFlipFoldOffset(other);
            return OffsetsDiffer(flip_offset, offset_other);
        }

        // datetime_richcompare
        internal static PyObject DatetimeRichCompare(PyObject self, PyObject other, PyObject.CompareOp op)
        {
            if (other is not PyDateTimeDateTime o)
            {
                if (other is PyDateTimeDate)
                {
                    if (op == PyObject.CompareOp.EQ)
                        return PyBool.False;
                    if (op == PyObject.CompareOp.NE)
                        return PyBool.True;
                    throw Cmperror(self, other);
                }
                return PyNotImplemented.Instance;
            }
            var s = (PyDateTimeDateTime)self;
            int diff;
            if (ReferenceEquals(s.TzinfoOrNone, o.TzinfoOrNone))
            {
                diff = Memcmp(DatetimeData(s), DatetimeData(o), _PyDateTime_DATETIME_DATASIZE);
                return DiffToBool(diff, op);
            }
            var offset1 = DatetimeUtcoffset(self);
            var offset2 = DatetimeUtcoffset(other);
            if (ReferenceEquals(offset1, offset2) ||
                (offset1 is PyDateTimeDelta d1 && offset2 is PyDateTimeDelta d2 && DeltaCmp(d1, d2) == 0))
            {
                diff = Memcmp(DatetimeData(s), DatetimeData(o), _PyDateTime_DATETIME_DATASIZE);
                if ((op == PyObject.CompareOp.EQ || op == PyObject.CompareOp.NE) && diff == 0)
                {
                    if (Pep495EqException(s, o, offset1, offset2))
                        diff = 1;
                }
                return DiffToBool(diff, op);
            }
            if (offset1 is not PyNone && offset2 is not PyNone)
            {
                var delta = (PyDateTimeDelta)DatetimeSubtract(self, other);
                diff = delta.Days;
                if (diff == 0)
                    diff = delta.Seconds | delta.Microseconds;
                if ((op == PyObject.CompareOp.EQ || op == PyObject.CompareOp.NE) && diff == 0)
                {
                    if (Pep495EqException(s, o, offset1, offset2))
                        diff = 1;
                }
                return DiffToBool(diff, op);
            }
            if (op == PyObject.CompareOp.EQ)
                return PyBool.False;
            if (op == PyObject.CompareOp.NE)
                return PyBool.True;
            throw PyTypeError.Create("can't compare offset-naive and offset-aware datetimes");
        }

        // datetime_hash
        internal static long DatetimeHash(PyDateTimeDateTime self)
        {
            if (self.Hashcode == -1)
            {
                PyObject self0;
                if (self.Fold != 0)
                    self0 = NewDatetimeEx2(self.Year, self.Month, self.Day, self.Hour, self.Minute, self.Second,
                        self.Microsecond, self.TzinfoOrNone, 0, self.GetPyType());
                else
                    self0 = self;
                var offset = DatetimeUtcoffset(self0);
                if (offset is PyNone)
                    self.Hashcode = GenericHash(DatetimeData(self), _PyDateTime_DATETIME_DATASIZE);
                else
                {
                    int days = YmdToOrd(self.Year, self.Month, self.Day);
                    int seconds = self.Hour * 3600 + self.Minute * 60 + self.Second;
                    var temp1 = NewDelta(days, seconds, self.Microsecond, true);
                    var temp2 = DeltaSubtract(temp1, offset);
                    self.Hashcode = temp2.GetPythonHash();
                }
            }
            return self.Hashcode;
        }

        // datetime_replace
        private static PyObject DatetimeReplace(PyDateTimeDateTime self, PyObject[] args, PyDict kw)
        {
            int y = self.Year, m = self.Month, d = self.Day, hh = self.Hour, mm = self.Minute,
                ss = self.Second, us = self.Microsecond, fold = self.Fold;
            PyObject tzinfo = self.TzinfoOrNone;
            ParseTupleAndKeywords("replace", args, kw, datetime_kws, 0, 8, (i, v) =>
            {
                switch (i)
                {
                    case 0: y = PyArgs.ParseInt(v); break;
                    case 1: m = PyArgs.ParseInt(v); break;
                    case 2: d = PyArgs.ParseInt(v); break;
                    case 3: hh = PyArgs.ParseInt(v); break;
                    case 4: mm = PyArgs.ParseInt(v); break;
                    case 5: ss = PyArgs.ParseInt(v); break;
                    case 6: us = PyArgs.ParseInt(v); break;
                    case 7: tzinfo = v; break;
                    default: fold = PyArgs.ParseInt(v); break;
                }
            });
            if (fold != 0 && fold != 1)
                throw PyValueError.Create("fold must be either 0 or 1");
            return NewDatetimeSubclassFoldEx(y, m, d, hh, mm, ss, us, tzinfo, fold, self.GetPyType());
        }

        // local_timezone_from_timestamp: struct tm has no tm_zone here (the time module's route), so
        // the name is strftime's %Z and the offset local time minus UTC.
        private static PyObject LocalTimezoneFromTimestamp(long timestamp)
        {
            var local_time_tm = _PyTime_localtime(timestamp);
            string zone = CTimeRuntime.StrFTime("%Z", local_time_tm);
            var local_time = NewDatetime(local_time_tm.Year + 1900, local_time_tm.Mon + 1, local_time_tm.Mday,
                local_time_tm.Hour, local_time_tm.Min, local_time_tm.Sec, 0, PyNone.Instance, 0);
            var utc_time_tm = _PyTime_gmtime(timestamp);
            var utc_time = NewDatetime(utc_time_tm.Year + 1900, utc_time_tm.Mon + 1, utc_time_tm.Mday,
                utc_time_tm.Hour, utc_time_tm.Min, utc_time_tm.Sec, 0, PyNone.Instance, 0);
            var delta = DatetimeSubtract(local_time, utc_time);
            PyObject nameo = zone != null ? PyString.From(zone) : null;
            return NewTimezone(delta, nameo);
        }

        // local_timezone
        private static PyObject LocalTimezone(PyDateTimeDateTime utc_time)
        {
            var delta = (PyDateTimeDelta)DatetimeSubtract(utc_time, PyDateTime_Epoch);
            var one_second = (PyDateTimeDelta)NewDelta(0, 1, 0, false);
            var seconds = DivideTimedeltaTimedelta(delta, one_second);
            long timestamp = SharpPy.PyTime.LongAsTimeT(seconds);
            return LocalTimezoneFromTimestamp(timestamp);
        }

        // local_timezone_from_local
        private static PyObject LocalTimezoneFromLocal(PyDateTimeDateTime local_dt)
        {
            int fold = local_dt.Fold;
            long seconds = LocalToSeconds(local_dt.Year, local_dt.Month, local_dt.Day,
                local_dt.Hour, local_dt.Minute, local_dt.Second, fold);
            long seconds2 = LocalToSeconds(local_dt.Year, local_dt.Month, local_dt.Day,
                local_dt.Hour, local_dt.Minute, local_dt.Second, fold == 0 ? 1 : 0);
            if (seconds2 != seconds && (seconds2 > seconds) == (fold != 0))
                seconds = seconds2;
            long timestamp = seconds - epoch;
            return LocalTimezoneFromTimestamp(timestamp);
        }

        private static readonly string[] astimezone_keywords = { "tz" };

        // datetime_astimezone
        private static PyObject DatetimeAstimezone(PyDateTimeDateTime self, PyObject[] args, PyDict kw)
        {
            var tzinfo = ParseTupleAndKeywords("astimezone", args, kw, astimezone_keywords, 0)[0] ?? PyNone.Instance;
            CheckTzinfoSubclass(tzinfo);
            PyObject self_tzinfo = !self.HasTzinfo || self.Tzinfo is PyNone ? null : self.Tzinfo;
            PyObject offset;
            while (true)
            {
                // naive:
                self_tzinfo ??= LocalTimezoneFromLocal(self);
                // Conversion to self's own time zone is a NOP.
                if (ReferenceEquals(self_tzinfo, tzinfo))
                    return self;
                offset = CallUtcoffset(self_tzinfo, self);
                if (offset is not PyNone)
                    break;
                self_tzinfo = null;
            }
            if (offset is not PyDateTimeDelta)
                throw PyTypeError.Create($"utcoffset() returned {TpName(offset)}, expected timedelta or None");
            var result = (PyDateTimeDateTime)AddDatetimeTimedelta(self, (PyDateTimeDelta)offset, -1);
            if (!result.HasTzinfo)
            {
                result = (PyDateTimeDateTime)NewDatetimeEx2(result.Year, result.Month, result.Day, result.Hour,
                    result.Minute, result.Second, result.Microsecond, PyDateTime_TimeZone_UTC, result.Fold, result.GetPyType());
            }
            else
            {
                result.Tzinfo = PyDateTime_TimeZone_UTC;
            }
            if (tzinfo is PyNone)
                tzinfo = LocalTimezone(result);
            result.Tzinfo = tzinfo;
            return CallMethod(tzinfo, "fromutc", result);
        }

        // datetime_timetuple
        private static PyObject DatetimeTimetuple(PyDateTimeDateTime self)
        {
            int dstflag = -1;
            if (self.HasTzinfo && self.Tzinfo is not PyNone)
            {
                var dst = CallDst(self.Tzinfo, self);
                if (dst is not PyNone)
                    dstflag = DeltaBool((PyDateTimeDelta)dst) ? 1 : 0;
            }
            return BuildStructTime(self.Year, self.Month, self.Day, self.Hour, self.Minute, self.Second, dstflag);
        }

        // local_to_seconds
        private static long LocalToSeconds(int year, int month, int day, int hour, int minute, int second, int fold)
        {
            long t, a, b, u1, u2, t1, t2, lt;
            t = UtcToSeconds(year, month, day, hour, minute, second);
            lt = Local(t);
            a = lt - t;
            u1 = t - a;
            t1 = Local(u1);
            if (t1 == t)
            {
                if (fold != 0)
                    u2 = u1 + max_fold_seconds;
                else
                    u2 = u1 - max_fold_seconds;
                lt = Local(u2);
                b = lt - u2;
                if (a == b)
                    return u1;
            }
            else
            {
                b = t1 - u1;
            }
            u2 = t - b;
            t2 = Local(u2);
            if (t2 == t)
                return u2;
            if (t1 == t)
                return u1;
            return fold != 0 ? Math.Min(u1, u2) : Math.Max(u1, u2);
        }

        // date(1970,1,1).toordinal() == 719163
        private const long EPOCH_SECONDS = 719163L * 24 * 60 * 60;

        // datetime_timestamp
        private static PyObject DatetimeTimestamp(PyDateTimeDateTime self)
        {
            if (self.HasTzinfo && self.Tzinfo is not PyNone)
            {
                var delta = DatetimeSubtract(self, PyDateTime_Epoch);
                return DeltaTotalSeconds(delta);
            }
            long seconds = LocalToSeconds(self.Year, self.Month, self.Day, self.Hour, self.Minute, self.Second, self.Fold);
            return new PyFloat(seconds - EPOCH_SECONDS + self.Microsecond / 1e6);
        }

        // datetime_getdate
        private static PyObject DatetimeGetdate(PyDateTimeDateTime self) => NewDate(self.Year, self.Month, self.Day);

        // datetime_gettime
        private static PyObject DatetimeGettime(PyDateTimeDateTime self) =>
            NewTime(self.Hour, self.Minute, self.Second, self.Microsecond, PyNone.Instance, self.Fold);

        // datetime_gettimetz
        private static PyObject DatetimeGettimetz(PyDateTimeDateTime self) =>
            NewTime(self.Hour, self.Minute, self.Second, self.Microsecond, self.TzinfoOrNone, self.Fold);

        // datetime_utctimetuple
        private static PyObject DatetimeUtctimetuple(PyDateTimeDateTime self)
        {
            PyDateTimeDateTime utcself;
            var tzinfo = self.TzinfoOrNone;
            if (tzinfo is PyNone)
                utcself = self;
            else
            {
                var offset = CallUtcoffset(tzinfo, self);
                if (offset is PyNone)
                    utcself = self;
                else
                    utcself = (PyDateTimeDateTime)AddDatetimeTimedelta(self, (PyDateTimeDelta)offset, -1);
            }
            return BuildStructTime(utcself.Year, utcself.Month, utcself.Day, utcself.Hour, utcself.Minute, utcself.Second, 0);
        }

        // datetime_getstate
        private static PyObject DatetimeGetstate(PyDateTimeDateTime self, int proto)
        {
            var basestate = DatetimeData(self);
            if (proto > 3 && self.Fold != 0)
                basestate[2] |= 1 << 7;
            var bytes = new PyBytes(basestate);
            if (!self.HasTzinfo || self.Tzinfo is PyNone)
                return PyTuple.From(bytes);
            return PyTuple.From(bytes, self.Tzinfo);
        }

        // datetime_reduce_ex
        private static PyObject DatetimeReduceEx(PyDateTimeDateTime self, PyObject[] args)
        {
            PyArgs.ParseTuple("__reduce_ex__", args, 1, 1);
            int proto = PyArgs.ParseInt(args[0]);
            return PyTuple.From(self.GetPyType(), DatetimeGetstate(self, proto));
        }

        // datetime_reduce
        private static PyObject DatetimeReduce(PyDateTimeDateTime self) => PyTuple.From(self.GetPyType(), DatetimeGetstate(self, 2));

        // PyDateTime_DateTimeType: tp_base is date, filled in by PyInit__datetime.
        private static PyType MakeDateTimeType()
        {
            var type = new PyType("datetime", new[] { DateType }, "datetime");
            DateTimeType = type;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => DatetimeNew((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            Slot(type, "__repr__", (self, a) => DatetimeRepr((PyDateTimeDateTime)self));
            Slot(type, "__hash__", (self, a) => PyInt.From(DatetimeHash((PyDateTimeDateTime)self)));
            Slot(type, "__str__", (self, a) => DatetimeStr((PyDateTimeDateTime)self));
            TypeTables.GenericAttributes(type);
            RichCompareSlots(type, (self, other, op) => DatetimeRichCompare(self, other, op));
            BinarySlots(type, "add", DatetimeAdd);
            BinarySlots(type, "sub", DatetimeSubtract);
            // datetime_methods
            ClassMethod(type, "now", PyCall.Defining, (cls, args, kw) => DatetimeDatetimeNow((PyType)cls, args, kw));
            ClassMethod(type, "utcnow", PyCall.NoArgs, (cls, args, kw) => DatetimeUtcnow(cls));
            ClassMethod(type, "fromtimestamp", PyCall.Defining, (cls, args, kw) => DatetimeFromtimestamp(cls, args, kw));
            ClassMethod(type, "utcfromtimestamp", PyCall.ParseTuple, (cls, args, kw) => DatetimeUtcfromtimestamp(cls, args));
            ClassMethod(type, "strptime", PyCall.ParseTuple, (cls, args, kw) => DatetimeStrptime(cls, args));
            ClassMethod(type, "combine", PyCall.Defining, (cls, args, kw) => DatetimeCombine(cls, args, kw));
            ClassMethod(type, "fromisoformat", PyCall.One, (cls, args, kw) => DatetimeFromisoformat(cls, args[0]));
            Method(type, "date", PyCall.NoArgs, (self, args, kw) => DatetimeGetdate((PyDateTimeDateTime)self));
            Method(type, "time", PyCall.NoArgs, (self, args, kw) => DatetimeGettime((PyDateTimeDateTime)self));
            Method(type, "timetz", PyCall.NoArgs, (self, args, kw) => DatetimeGettimetz((PyDateTimeDateTime)self));
            Method(type, "ctime", PyCall.NoArgs, (self, args, kw) => DatetimeCtime((PyDateTimeDateTime)self));
            Method(type, "timetuple", PyCall.NoArgs, (self, args, kw) => DatetimeTimetuple((PyDateTimeDateTime)self));
            Method(type, "timestamp", PyCall.NoArgs, (self, args, kw) => DatetimeTimestamp((PyDateTimeDateTime)self));
            Method(type, "utctimetuple", PyCall.NoArgs, (self, args, kw) => DatetimeUtctimetuple((PyDateTimeDateTime)self));
            Method(type, "isoformat", PyCall.Defining, (self, args, kw) => DatetimeIsoformat((PyDateTimeDateTime)self, args, kw));
            Method(type, "utcoffset", PyCall.NoArgs, (self, args, kw) => DatetimeUtcoffset(self));
            Method(type, "tzname", PyCall.NoArgs, (self, args, kw) => DatetimeTzname(self));
            Method(type, "dst", PyCall.NoArgs, (self, args, kw) => DatetimeDst(self));
            Method(type, "replace", PyCall.Defining, (self, args, kw) => DatetimeReplace((PyDateTimeDateTime)self, args, kw));
            Method(type, "astimezone", PyCall.Defining, (self, args, kw) => DatetimeAstimezone((PyDateTimeDateTime)self, args, kw));
            Method(type, "__reduce_ex__", PyCall.ParseTuple, (self, args, kw) => DatetimeReduceEx((PyDateTimeDateTime)self, args));
            Method(type, "__reduce__", PyCall.NoArgs, (self, args, kw) => DatetimeReduce((PyDateTimeDateTime)self));
            // datetime_getset
            Getter(type, "hour", self => DatetimeHour((PyDateTimeDateTime)self));
            Getter(type, "minute", self => DatetimeMinute((PyDateTimeDateTime)self));
            Getter(type, "second", self => DatetimeSecond((PyDateTimeDateTime)self));
            Getter(type, "microsecond", self => DatetimeMicrosecond((PyDateTimeDateTime)self));
            Getter(type, "tzinfo", self => DatetimeTzinfo((PyDateTimeDateTime)self));
            Getter(type, "fold", self => DatetimeFold((PyDateTimeDateTime)self));
            return type;
        }
    }
}
