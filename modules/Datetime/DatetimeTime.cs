using System;

namespace SharpPy.Modules.Datetime
{
    /// <summary>Modules/_datetimemodule.c: PyDateTime_Time implementation (lines 4124-4782).</summary>
    internal static partial class DatetimeModule
    {
        internal const int _PyDateTime_TIME_DATASIZE = 6;

        // time_hour, time_minute, py_time_second, time_microsecond, time_tzinfo, time_fold
        private static PyObject TimeHour(PyDateTimeTime self) => PyInt.From(self.Hour);
        private static PyObject TimeMinute(PyDateTimeTime self) => PyInt.From(self.Minute);
        private static PyObject PyTimeSecond(PyDateTimeTime self) => PyInt.From(self.Second);
        private static PyObject TimeMicrosecond(PyDateTimeTime self) => PyInt.From(self.Microsecond);
        private static PyObject TimeTzinfo(PyDateTimeTime self) => self.TzinfoOrNone;
        private static PyObject TimeFold(PyDateTimeTime self) => PyInt.From(self.Fold);

        private static readonly string[] time_kws = { "hour", "minute", "second", "microsecond", "tzinfo", "fold" };

        // The data bytes of PyDateTime_Time: hour, minute, second, microsecond (3 bytes, big endian).
        internal static byte[] TimeData(PyDateTimeTime self) => new[]
        {
            (byte)self.Hour, (byte)self.Minute, (byte)self.Second,
            (byte)((self.Microsecond & 0xff0000) >> 16), (byte)((self.Microsecond & 0x00ff00) >> 8), (byte)(self.Microsecond & 0x0000ff),
        };

        // time_from_pickle
        private static PyObject TimeFromPickle(PyType type, byte[] pdata, PyObject tzinfo)
        {
            bool aware = tzinfo is not PyNone;
            if (aware && tzinfo is not PyDateTimeTZInfo)
                throw PyTypeError.Create("bad tzinfo state arg");
            var me = new PyDateTimeTime(type);
            me.Hour = pdata[0];
            me.Minute = pdata[1];
            me.Second = pdata[2];
            me.Microsecond = (pdata[3] << 16) | (pdata[4] << 8) | pdata[5];
            me.Hashcode = -1;
            me.HasTzinfo = aware;
            if (aware)
                me.Tzinfo = tzinfo;
            if ((pdata[0] & (1 << 7)) != 0)
            {
                me.Hour -= 128;
                me.Fold = 1;
            }
            else
            {
                me.Fold = 0;
            }
            return me;
        }

        // time_new
        private static PyObject TimeNew(PyType type, PyObject[] args, PyDict kw)
        {
            int hour = 0, minute = 0, second = 0, usecond = 0, fold = 0;
            PyObject tzinfo = PyNone.Instance;
            if (args.Length >= 1 && args.Length <= 2)
            {
                var state = args[0];
                if (args.Length == 2)
                    tzinfo = args[1];
                if (state is PyBytes bytes)
                {
                    var data = bytes.Value;
                    if (data.Length == _PyDateTime_TIME_DATASIZE && (0x7F & data[0]) < 24)
                        return TimeFromPickle(type, data, tzinfo);
                }
                else if (state is PyString text)
                {
                    if (text.CodePointLength() == _PyDateTime_TIME_DATASIZE && (0x7F & text.ReadChar(0)) < 24)
                        return TimeFromPickle(type, PickledLatin1(text, "time"), tzinfo);
                }
                tzinfo = PyNone.Instance;
            }
            ParseTupleAndKeywords(null, args, kw, time_kws, 0, 5, (i, v) =>
            {
                switch (i)
                {
                    case 0: hour = PyArgs.ParseInt(v); break;
                    case 1: minute = PyArgs.ParseInt(v); break;
                    case 2: second = PyArgs.ParseInt(v); break;
                    case 3: usecond = PyArgs.ParseInt(v); break;
                    case 4: tzinfo = v; break;
                    default: fold = PyArgs.ParseInt(v); break;
                }
            });
            return NewTimeEx2(hour, minute, second, usecond, tzinfo, fold, type);
        }

        // time_utcoffset
        private static PyObject TimeUtcoffset(PyObject self) => CallUtcoffset(((PyDateTimeTime)self).TzinfoOrNone, PyNone.Instance);

        // time_dst
        private static PyObject TimeDst(PyObject self) => CallDst(((PyDateTimeTime)self).TzinfoOrNone, PyNone.Instance);

        // time_tzname
        private static PyObject TimeTzname(PyDateTimeTime self) => CallTzname(self.TzinfoOrNone, PyNone.Instance);

        // time_repr
        internal static PyObject TimeRepr(PyDateTimeTime self)
        {
            string type_name = TpName(self);
            int h = self.Hour, m = self.Minute, s = self.Second, us = self.Microsecond, fold = self.Fold;
            string result;
            if (us != 0)
                result = $"{type_name}({h}, {m}, {s}, {us})";
            else if (s != 0)
                result = $"{type_name}({h}, {m}, {s})";
            else
                result = $"{type_name}({h}, {m})";
            if (self.HasTzinfo)
                result = AppendKeywordTzinfo(result, self.Tzinfo);
            if (fold != 0)
                result = AppendKeywordFold(result, fold);
            return PyString.From(result);
        }

        // time_str
        internal static PyObject TimeStr(PyDateTimeTime self) =>
            self.GetAttribute("isoformat").Call(Array.Empty<PyObject>(), null);

        private static readonly string[] isoformat_time_keywords = { "timespec" };

        // The "s" converter: a str without NUL, read as UTF-8.
        internal static string StringArg(string fname, int iarg, PyObject arg)
        {
            if (arg is not PyString text)
                throw ConverterError(fname, iarg, "str", arg);
            var value = PyArgs.AsUtf8(text);
            if (value.IndexOf('\0') >= 0)
                throw PyValueError.Create("embedded null character");
            return value;
        }

        // The timespec formats of time_isoformat and datetime_isoformat.
        private static string FormatTimespec(int spec, int h, int m, int s, int us) => spec switch
        {
            0 => $"{h:D2}",
            1 => $"{h:D2}:{m:D2}",
            2 => $"{h:D2}:{m:D2}:{s:D2}",
            3 => $"{h:D2}:{m:D2}:{s:D2}.{us:D3}",
            _ => $"{h:D2}:{m:D2}:{s:D2}.{us:D6}",
        };

        private static readonly string[] timespec_names = { "hours", "minutes", "seconds", "milliseconds", "microseconds" };

        // time_isoformat
        private static PyObject TimeIsoformat(PyDateTimeTime self, PyObject[] args, PyDict kw)
        {
            string timespec = null;
            ParseTupleAndKeywords("isoformat", args, kw, isoformat_time_keywords, 0,
                convert: (i, v) => timespec = StringArg("isoformat", i + 1, v));
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
            string result = FormatTimespec(given_spec, self.Hour, self.Minute, self.Second, us);
            if (!self.HasTzinfo || self.Tzinfo is PyNone)
                return PyString.From(result);
            return PyString.From(result + FormatUtcoffset(":", self.Tzinfo, PyNone.Instance));
        }

        // time_strftime
        private static PyObject TimeStrftime(PyDateTimeTime self, PyObject[] args, PyDict kw)
        {
            var format = ParseTupleAndKeywords("strftime", args, kw, strftime_keywords, 1,
                convert: (i, v) => UnicodeArg("strftime", i + 1, v))[0];
            var tuple = PyTuple.From(PyInt.From(1900), PyInt.From(1), PyInt.From(1),
                PyInt.From(self.Hour), PyInt.From(self.Minute), PyInt.From(self.Second),
                PyInt.From(0), PyInt.From(1), PyInt.From(-1));
            return WrapStrftime(self, format, tuple, PyNone.Instance);
        }

        // time_richcompare
        internal static PyObject TimeRichCompare(PyObject self, PyObject other, PyObject.CompareOp op)
        {
            if (other is not PyDateTimeTime o)
                return PyNotImplemented.Instance;
            var s = (PyDateTimeTime)self;
            int diff;
            if (ReferenceEquals(s.TzinfoOrNone, o.TzinfoOrNone))
            {
                diff = Memcmp(TimeData(s), TimeData(o), _PyDateTime_TIME_DATASIZE);
                return DiffToBool(diff, op);
            }
            var offset1 = TimeUtcoffset(self);
            var offset2 = TimeUtcoffset(other);
            if (ReferenceEquals(offset1, offset2) ||
                (offset1 is PyDateTimeDelta d1 && offset2 is PyDateTimeDelta d2 && DeltaCmp(d1, d2) == 0))
            {
                diff = Memcmp(TimeData(s), TimeData(o), _PyDateTime_TIME_DATASIZE);
                return DiffToBool(diff, op);
            }
            if (offset1 is not PyNone && offset2 is not PyNone)
            {
                var off1 = (PyDateTimeDelta)offset1;
                var off2 = (PyDateTimeDelta)offset2;
                int offsecs1 = s.Hour * 3600 + s.Minute * 60 + s.Second - off1.Days * 86400 - off1.Seconds;
                int offsecs2 = o.Hour * 3600 + o.Minute * 60 + o.Second - off2.Days * 86400 - off2.Seconds;
                diff = offsecs1 - offsecs2;
                if (diff == 0)
                    diff = s.Microsecond - o.Microsecond;
                return DiffToBool(diff, op);
            }
            if (op == PyObject.CompareOp.EQ)
                return PyBool.False;
            if (op == PyObject.CompareOp.NE)
                return PyBool.True;
            throw PyTypeError.Create("can't compare offset-naive and offset-aware times");
        }

        // time_hash
        internal static long TimeHash(PyDateTimeTime self)
        {
            if (self.Hashcode == -1)
            {
                PyObject self0;
                if (self.Fold != 0)
                    self0 = NewTimeEx2(self.Hour, self.Minute, self.Second, self.Microsecond,
                        self.TzinfoOrNone, 0, self.GetPyType());
                else
                    self0 = self;
                var offset = TimeUtcoffset(self0);
                if (offset is PyNone)
                    self.Hashcode = GenericHash(TimeData(self), _PyDateTime_TIME_DATASIZE);
                else
                {
                    int seconds = self.Hour * 3600 + self.Minute * 60 + self.Second;
                    int microseconds = self.Microsecond;
                    var temp1 = NewDelta(0, seconds, microseconds, true);
                    var temp2 = DeltaSubtract(temp1, offset);
                    self.Hashcode = temp2.GetPythonHash();
                }
            }
            return self.Hashcode;
        }

        // time_replace
        private static PyObject TimeReplace(PyDateTimeTime self, PyObject[] args, PyDict kw)
        {
            int hh = self.Hour, mm = self.Minute, ss = self.Second, us = self.Microsecond, fold = self.Fold;
            PyObject tzinfo = self.TzinfoOrNone;
            ParseTupleAndKeywords("replace", args, kw, time_kws, 0, 5, (i, v) =>
            {
                switch (i)
                {
                    case 0: hh = PyArgs.ParseInt(v); break;
                    case 1: mm = PyArgs.ParseInt(v); break;
                    case 2: ss = PyArgs.ParseInt(v); break;
                    case 3: us = PyArgs.ParseInt(v); break;
                    case 4: tzinfo = v; break;
                    default: fold = PyArgs.ParseInt(v); break;
                }
            });
            if (fold != 0 && fold != 1)
                throw PyValueError.Create("fold must be either 0 or 1");
            return NewTimeSubclassFoldEx(hh, mm, ss, us, tzinfo, fold, self.GetPyType());
        }

        // time_fromisoformat
        private static PyObject TimeFromisoformat(PyObject cls, PyObject tstr)
        {
            if (!PyUnicodeCheck(tstr))
                throw PyTypeError.Create("fromisoformat: argument must be str");
            var buffer = AsUtf8OrNull((PyString)tstr, out int len);
            if (buffer == null)
                throw PyValueError.Create($"Invalid isoformat string: {tstr.ToRepr().Value}");
            int p = 0;
            if (buffer[p] == 'T')
            {
                ++p;
                len -= 1;
            }
            int rv = ParseIsoformatTime(buffer, p, len, out int hour, out int minute, out int second,
                out int microsecond, out int tzoffset, out int tzimicrosecond);
            if (rv < 0)
                throw PyValueError.Create($"Invalid isoformat string: {tstr.ToRepr().Value}");
            var tzinfo = TzinfoFromIsoformatResults(rv, tzoffset, tzimicrosecond);
            if (ReferenceEquals(cls, TimeType))
                return NewTime(hour, minute, second, microsecond, tzinfo, 0);
            return cls.Call(new[] { PyInt.From(hour), PyInt.From(minute), PyInt.From(second), PyInt.From(microsecond), tzinfo }, null);
        }

        // time_getstate
        private static PyObject TimeGetstate(PyDateTimeTime self, int proto)
        {
            var basestate = TimeData(self);
            if (proto > 3 && self.Fold != 0)
                basestate[0] |= 1 << 7;
            var bytes = PyBytes.From(basestate);
            if (!self.HasTzinfo || self.Tzinfo is PyNone)
                return PyTuple.From(bytes);
            return PyTuple.From(bytes, self.Tzinfo);
        }

        // time_reduce_ex
        private static PyObject TimeReduceEx(PyDateTimeTime self, PyObject[] args)
        {
            PyArgs.ParseTuple("__reduce_ex__", args, 1, 1);
            int proto = PyArgs.ParseInt(args[0]);
            return PyTuple.From(self.GetPyType(), TimeGetstate(self, proto));
        }

        // time_reduce
        private static PyObject TimeReduce(PyDateTimeTime self) => PyTuple.From(self.GetPyType(), TimeGetstate(self, 2));

        // PyDateTime_TimeType
        private static PyType MakeTimeType()
        {
            var type = new PyType("time", new[] { PyType.ObjectType }, "datetime");
            TimeType = type;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => TimeNew((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            Slot(type, "__repr__", (self, a) => TimeRepr((PyDateTimeTime)self));
            Slot(type, "__hash__", (self, a) => PyInt.From(TimeHash((PyDateTimeTime)self)));
            Slot(type, "__str__", (self, a) => TimeStr((PyDateTimeTime)self));
            TypeTables.GenericAttributes(type);
            RichCompareSlots(type, (self, other, op) => TimeRichCompare(self, other, op));
            // time_methods
            Method(type, "isoformat", PyCall.Defining, (self, args, kw) => TimeIsoformat((PyDateTimeTime)self, args, kw));
            Method(type, "strftime", PyCall.Defining, (self, args, kw) => TimeStrftime((PyDateTimeTime)self, args, kw));
            Method(type, "__format__", PyCall.ParseTuple, (self, args, kw) => DateFormat(self, args));
            Method(type, "utcoffset", PyCall.NoArgs, (self, args, kw) => TimeUtcoffset(self));
            Method(type, "tzname", PyCall.NoArgs, (self, args, kw) => TimeTzname((PyDateTimeTime)self));
            Method(type, "dst", PyCall.NoArgs, (self, args, kw) => TimeDst(self));
            Method(type, "replace", PyCall.Defining, (self, args, kw) => TimeReplace((PyDateTimeTime)self, args, kw));
            ClassMethod(type, "fromisoformat", PyCall.One, (cls, args, kw) => TimeFromisoformat(cls, args[0]));
            Method(type, "__reduce_ex__", PyCall.ParseTuple, (self, args, kw) => TimeReduceEx((PyDateTimeTime)self, args));
            Method(type, "__reduce__", PyCall.NoArgs, (self, args, kw) => TimeReduce((PyDateTimeTime)self));
            // time_getset
            Getter(type, "hour", self => TimeHour((PyDateTimeTime)self));
            Getter(type, "minute", self => TimeMinute((PyDateTimeTime)self));
            Getter(type, "second", self => PyTimeSecond((PyDateTimeTime)self));
            Getter(type, "microsecond", self => TimeMicrosecond((PyDateTimeTime)self));
            Getter(type, "tzinfo", self => TimeTzinfo((PyDateTimeTime)self));
            Getter(type, "fold", self => TimeFold((PyDateTimeTime)self));
            return type;
        }
    }
}
