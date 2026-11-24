using System;

namespace SharpPy.Modules.Datetime
{
    /// <summary>Modules/_datetimemodule.c: PyDateTime_Date and IsoCalendarDate (lines 2851-3664).</summary>
    internal static partial class DatetimeModule
    {
        internal const int _PyDateTime_DATE_DATASIZE = 4;

        // date_year, date_month, date_day
        private static PyObject DateYear(PyDateTimeDate self) => PyInt.From(self.Year);
        private static PyObject DateMonth(PyDateTimeDate self) => PyInt.From(self.Month);
        private static PyObject DateDay(PyDateTimeDate self) => PyInt.From(self.Day);

        private static readonly string[] date_kws = { "year", "month", "day" };

        // The data bytes of PyDateTime_Date: year (big endian), month, day.
        internal static byte[] DateData(PyDateTimeDate self) =>
            new[] { (byte)((self.Year & 0xff00) >> 8), (byte)(self.Year & 0x00ff), (byte)self.Month, (byte)self.Day };

        // memcpy of the pickled data into the struct.
        internal static void SetDateData(PyDateTimeDate me, byte[] pdata)
        {
            me.Year = (pdata[0] << 8) | pdata[1];
            me.Month = pdata[2];
            me.Day = pdata[3];
        }

        // date_from_pickle
        private static PyObject DateFromPickle(PyType type, byte[] state)
        {
            var me = DateAlloc(type);
            SetDateData(me, state);
            me.Hashcode = -1;
            return me;
        }

        // PyUnicode_AsLatin1String of a pickled state, with the hint date_new gives.
        internal static byte[] PickledLatin1(PyString state, string what)
        {
            int n = state.CodePointLength();
            var bytes = new byte[n];
            for (int i = 0; i < n; i++)
            {
                int ch = state.ReadChar(i);
                if (ch > 0xff)
                    throw PyValueError.Create($"Failed to encode latin1 string when unpickling a {what} object. " +
                                              "pickle.load(data, encoding='latin1') is assumed.");
                bytes[i] = (byte)ch;
            }
            return bytes;
        }

        // date_new
        internal static PyObject DateNew(PyType type, PyObject[] args, PyDict kw)
        {
            if (args.Length == 1)
            {
                var state = args[0];
                if (state is PyBytes bytes)
                {
                    var data = bytes.Value;
                    if (data.Length == _PyDateTime_DATE_DATASIZE && MonthIsSane(data[2]))
                        return DateFromPickle(type, data);
                }
                else if (state is PyString text)
                {
                    if (text.CodePointLength() == _PyDateTime_DATE_DATASIZE && MonthIsSane(text.ReadChar(2)))
                        return DateFromPickle(type, PickledLatin1(text, "date"));
                }
            }
            int year = 0, month = 0, day = 0;
            ParseTupleAndKeywords(null, args, kw, date_kws, 3, convert: (i, v) =>
            {
                int value = PyArgs.ParseInt(v);
                if (i == 0) year = value; else if (i == 1) month = value; else day = value;
            });
            return NewDateEx(year, month, day, type);
        }

        // date_fromtimestamp
        internal static PyObject DateFromtimestamp(PyObject cls, PyObject obj)
        {
            long t = SharpPy.PyTime.ObjectToTimeT(obj, PyTimeRound.Floor);
            var tm = TimeModule.LocalTimeOf(t);
            return NewDateSubclassEx(tm.Year + 1900, tm.Mon + 1, tm.Mday, cls);
        }

        // date_today
        private static PyObject DateToday(PyObject cls)
        {
            var time = TimeTime();
            return CallMethod(cls, "fromtimestamp", time);
        }

        // datetime_date_fromtimestamp
        private static PyObject DatetimeDateFromtimestamp(PyType type, PyObject timestamp) => DateFromtimestamp(type, timestamp);

        // datetime_date_fromtimestamp_capi
        internal static PyObject DatetimeDateFromtimestampCapi(PyObject cls, PyObject[] args)
        {
            PyArgs.Positional("fromtimestamp", args, null, 1, 1);
            return DateFromtimestamp(cls, args[0]);
        }

        // date_fromordinal
        private static PyObject DateFromordinal(PyObject cls, PyObject[] args)
        {
            PyArgs.ParseTuple("fromordinal", args, 1, 1);
            int ordinal = PyArgs.ParseInt(args[0]);
            if (ordinal < 1)
                throw PyValueError.Create("ordinal must be >= 1");
            OrdToYmd(ordinal, out int year, out int month, out int day);
            return NewDateSubclassEx(year, month, day, cls);
        }

        // PyUnicode_AsUTF8AndSize: null when the str has a lone surrogate.
        internal static byte[] AsUtf8OrNull(PyString text, out int len)
        {
            string value;
            try
            {
                value = PyArgs.AsUtf8(text);
            }
            catch (PythonException error) when (error.PyException is PyUnicodeEncodeError)
            {
                len = 0;
                return null;
            }
            var buffer = CString(value);
            len = buffer.Length - 4;
            return buffer;
        }

        // date_fromisoformat
        private static PyObject DateFromisoformat(PyObject cls, PyObject dtstr)
        {
            if (!PyUnicodeCheck(dtstr))
                throw PyTypeError.Create("fromisoformat: argument must be str");
            var dt_ptr = AsUtf8OrNull((PyString)dtstr, out int len);
            int year = 0, month = 0, day = 0;
            int rv = -1;
            if (dt_ptr != null && (len == 7 || len == 8 || len == 10))
                rv = ParseIsoformatDate(dt_ptr, 0, len, out year, out month, out day);
            if (rv < 0)
                throw PyValueError.Create($"Invalid isoformat string: {dtstr.ToRepr().Value}");
            return NewDateSubclassEx(year, month, day, cls);
        }

        private static readonly string[] fromisocalendar_keywords = { "year", "week", "day" };

        // date_fromisocalendar
        private static PyObject DateFromisocalendar(PyObject cls, PyObject[] args, PyDict kw)
        {
            int year = 0, week = 0, day = 0;
            try
            {
                ParseTupleAndKeywords("fromisocalendar", args, kw, fromisocalendar_keywords, 3, convert: (i, v) =>
                {
                    int value = PyArgs.ParseInt(v);
                    if (i == 0) year = value; else if (i == 1) week = value; else day = value;
                });
            }
            catch (PythonException error) when (error.PyException is PyOverflowError)
            {
                throw PyValueError.Create("ISO calendar component out of range");
            }
            int rv = IsoToYmd(year, week, day, out int y, out int month, out int d);
            if (rv == -4)
                throw PyValueError.Create($"Year is out of range: {year}");
            if (rv == -2)
                throw PyValueError.Create($"Invalid week: {week}");
            if (rv == -3)
                throw PyValueError.Create($"Invalid day: {day} (range is [1, 7])");
            return NewDateSubclassEx(y, month, d, cls);
        }

        // add_date_timedelta
        private static PyObject AddDateTimedelta(PyDateTimeDate date, PyDateTimeDelta delta, bool negate)
        {
            int year = date.Year;
            int month = date.Month;
            int deltadays = delta.Days;
            int day = date.Day + (negate ? -deltadays : deltadays);
            NormalizeDate(ref year, ref month, ref day);
            return NewDateSubclassEx(year, month, day, date.GetPyType());
        }

        // date_add
        internal static PyObject DateAdd(PyObject left, PyObject right)
        {
            if (left is PyDateTimeDateTime || right is PyDateTimeDateTime)
                return PyNotImplemented.Instance;
            if (left is PyDateTimeDate date)
            {
                if (right is PyDateTimeDelta delta)
                    return AddDateTimedelta(date, delta, false);
            }
            else
            {
                if (left is PyDateTimeDelta delta)
                    return AddDateTimedelta((PyDateTimeDate)right, delta, false);
            }
            return PyNotImplemented.Instance;
        }

        // date_subtract
        internal static PyObject DateSubtract(PyObject left, PyObject right)
        {
            if (left is PyDateTimeDateTime || right is PyDateTimeDateTime)
                return PyNotImplemented.Instance;
            if (left is PyDateTimeDate l)
            {
                if (right is PyDateTimeDate r)
                {
                    int left_ord = YmdToOrd(l.Year, l.Month, l.Day);
                    int right_ord = YmdToOrd(r.Year, r.Month, r.Day);
                    return NewDelta(left_ord - right_ord, 0, 0, false);
                }
                if (right is PyDateTimeDelta delta)
                    return AddDateTimedelta(l, delta, true);
            }
            return PyNotImplemented.Instance;
        }

        // date_repr
        internal static PyObject DateRepr(PyDateTimeDate self) =>
            PyString.From($"{TpName(self)}({self.Year}, {self.Month}, {self.Day})");

        // date_isoformat
        private static PyObject DateIsoformat(PyDateTimeDate self) =>
            PyString.From($"{self.Year:D4}-{self.Month:D2}-{self.Day:D2}");

        // date_str
        internal static PyObject DateStr(PyDateTimeDate self) =>
            self.GetAttribute("isoformat").Call(Array.Empty<PyObject>(), null);

        // date_ctime
        private static PyObject DateCtime(PyDateTimeDate self) => FormatCtime(self, 0, 0, 0);

        private static readonly string[] strftime_keywords = { "format" };

        // The "U" converter: a str, else "argument N must be str, not T".
        internal static PyObject UnicodeArg(string fname, int iarg, PyObject arg) =>
            PyUnicodeCheck(arg) ? arg : throw ConverterError(fname, iarg, "str", arg);

        // date_strftime
        private static PyObject DateStrftime(PyDateTimeDate self, PyObject[] args, PyDict kw)
        {
            var format = ParseTupleAndKeywords("strftime", args, kw, strftime_keywords, 1,
                convert: (i, v) => UnicodeArg("strftime", i + 1, v))[0];
            var tuple = self.GetAttribute("timetuple").Call(Array.Empty<PyObject>(), null);
            return WrapStrftime(self, format, tuple, self);
        }

        // date_format
        internal static PyObject DateFormat(PyObject self, PyObject[] args)
        {
            PyArgs.ParseTuple("__format__", args, 1, 1);
            var format = UnicodeArg("__format__", 1, args[0]);
            if (((PyString)format).CodePointLength() == 0)
                return self.AsString();
            return CallMethod(self, "strftime", format);
        }

        // date_isoweekday
        private static PyObject DateIsoweekday(PyDateTimeDate self) =>
            PyInt.From(Weekday(self.Year, self.Month, self.Day) + 1);

        // iso_calendar_date_repr
        internal static PyObject IsoCalendarDateRepr(PyDateTimeIsoCalendarDate self)
        {
            var items = self.Items;
            return PyString.From($"{TpName(self)}(year={items[0].AsString().Value}, week={items[1].AsString().Value}, weekday={items[2].AsString().Value})");
        }

        // iso_calendar_date_reduce
        private static PyObject IsoCalendarDateReduce(PyObject self)
        {
            var items = ((PyTuple)self).Items;
            return PyTuple.From(PyType.TupleType, PyTuple.From(PyTuple.From(items[0], items[1], items[2])));
        }

        // iso_calendar_date_year, iso_calendar_date_week, iso_calendar_date_weekday
        private static PyObject IsoCalendarDateYear(PyObject self) => ((PyTuple)self).Items[0];
        private static PyObject IsoCalendarDateWeek(PyObject self) => ((PyTuple)self).Items[1];
        private static PyObject IsoCalendarDateWeekday(PyObject self) => ((PyTuple)self).Items[2];

        private static readonly string[] iso_calendar_date_keywords = { "year", "week", "weekday" };

        // iso_calendar_date_new (clinic): year, week, weekday, each an int.
        private static PyObject IsoCalendarDateNew(PyType type, PyObject[] args, PyDict kwargs)
        {
            var parsed = PyArgs.Unpack("IsoCalendarDate", args, kwargs, iso_calendar_date_keywords, 0, 3, 3);
            return IsoCalendarDateNewImpl(type, PyArgs.Int(parsed[0]), PyArgs.Int(parsed[1]), PyArgs.Int(parsed[2]));
        }

        // iso_calendar_date_new_impl
        private static PyObject IsoCalendarDateNewImpl(PyType type, int year, int week, int weekday) =>
            new PyDateTimeIsoCalendarDate(new PyObject[] { PyInt.From(year), PyInt.From(week), PyInt.From(weekday) });

        // date_isocalendar
        private static PyObject DateIsocalendar(PyDateTimeDate self)
        {
            int year = self.Year;
            int week1_monday = IsoWeek1Monday(year);
            int today = YmdToOrd(year, self.Month, self.Day);
            int week = Divmod(today - week1_monday, 7, out int day);
            if (week < 0)
            {
                --year;
                week1_monday = IsoWeek1Monday(year);
                week = Divmod(today - week1_monday, 7, out day);
            }
            else if (week >= 52 && today >= IsoWeek1Monday(year + 1))
            {
                ++year;
                week = 0;
            }
            return IsoCalendarDateNewImpl(IsoCalendarDateType, year, week + 1, day + 1);
        }

        // memcmp of two data byte strings.
        internal static int Memcmp(byte[] a, byte[] b, int n)
        {
            for (int i = 0; i < n; i++)
                if (a[i] != b[i])
                    return a[i] - b[i];
            return 0;
        }

        // date_richcompare
        internal static PyObject DateRichCompare(PyObject self, PyObject other, PyObject.CompareOp op)
        {
            if (other is PyDateTimeDate o)
            {
                int diff = Memcmp(DateData((PyDateTimeDate)self), DateData(o), _PyDateTime_DATE_DATASIZE);
                return DiffToBool(diff, op);
            }
            return PyNotImplemented.Instance;
        }

        // date_timetuple
        private static PyObject DateTimetuple(PyDateTimeDate self) =>
            BuildStructTime(self.Year, self.Month, self.Day, 0, 0, 0, -1);

        // date_replace
        private static PyObject DateReplace(PyDateTimeDate self, PyObject[] args, PyDict kw)
        {
            int year = self.Year, month = self.Month, day = self.Day;
            ParseTupleAndKeywords("replace", args, kw, date_kws, 0, convert: (i, v) =>
            {
                int value = PyArgs.ParseInt(v);
                if (i == 0) year = value; else if (i == 1) month = value; else day = value;
            });
            return NewDateSubclassEx(year, month, day, self.GetPyType());
        }

        // generic_hash
        internal static long GenericHash(byte[] data, int len) => PyHash.Bytes(new ReadOnlySpan<byte>(data, 0, len));

        // date_hash
        internal static long DateHash(PyDateTimeDate self)
        {
            if (self.Hashcode == -1)
                self.Hashcode = GenericHash(DateData(self), _PyDateTime_DATE_DATASIZE);
            return self.Hashcode;
        }

        // date_toordinal
        private static PyObject DateToordinal(PyDateTimeDate self) => PyInt.From(YmdToOrd(self.Year, self.Month, self.Day));

        // date_weekday
        private static PyObject DateWeekday(PyDateTimeDate self) => PyInt.From(Weekday(self.Year, self.Month, self.Day));

        // date_getstate
        private static PyObject DateGetstate(PyDateTimeDate self) => PyTuple.From(PyBytes.From(DateData(self)));

        // date_reduce
        private static PyObject DateReduce(PyDateTimeDate self) => PyTuple.From(self.GetPyType(), DateGetstate(self));

        // PyDateTime_DateType
        private static PyType MakeDateType()
        {
            var type = new PyType("date", new[] { PyType.ObjectType }, "datetime");
            DateType = type;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => DateNew((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            Slot(type, "__repr__", (self, a) => DateRepr((PyDateTimeDate)self));
            Slot(type, "__hash__", (self, a) => PyInt.From(DateHash((PyDateTimeDate)self)));
            Slot(type, "__str__", (self, a) => DateStr((PyDateTimeDate)self));
            TypeTables.GenericAttributes(type);
            RichCompareSlots(type, (self, other, op) => DateRichCompare(self, other, op));
            BinarySlots(type, "add", DateAdd);
            BinarySlots(type, "sub", DateSubtract);
            // date_methods
            ClassMethod(type, "fromtimestamp", PyCall.One, (cls, args, kw) => DatetimeDateFromtimestamp((PyType)cls, args[0]));
            ClassMethod(type, "fromordinal", PyCall.ParseTuple, (cls, args, kw) => DateFromordinal(cls, args));
            ClassMethod(type, "fromisoformat", PyCall.One, (cls, args, kw) => DateFromisoformat(cls, args[0]));
            ClassMethod(type, "fromisocalendar", PyCall.Defining, (cls, args, kw) => DateFromisocalendar(cls, args, kw));
            ClassMethod(type, "today", PyCall.NoArgs, (cls, args, kw) => DateToday(cls));
            Method(type, "ctime", PyCall.NoArgs, (self, args, kw) => DateCtime((PyDateTimeDate)self));
            Method(type, "strftime", PyCall.Defining, (self, args, kw) => DateStrftime((PyDateTimeDate)self, args, kw));
            Method(type, "__format__", PyCall.ParseTuple, (self, args, kw) => DateFormat(self, args));
            Method(type, "timetuple", PyCall.NoArgs, (self, args, kw) => DateTimetuple((PyDateTimeDate)self));
            Method(type, "isocalendar", PyCall.NoArgs, (self, args, kw) => DateIsocalendar((PyDateTimeDate)self));
            Method(type, "isoformat", PyCall.NoArgs, (self, args, kw) => DateIsoformat((PyDateTimeDate)self));
            Method(type, "isoweekday", PyCall.NoArgs, (self, args, kw) => DateIsoweekday((PyDateTimeDate)self));
            Method(type, "toordinal", PyCall.NoArgs, (self, args, kw) => DateToordinal((PyDateTimeDate)self));
            Method(type, "weekday", PyCall.NoArgs, (self, args, kw) => DateWeekday((PyDateTimeDate)self));
            Method(type, "replace", PyCall.Defining, (self, args, kw) => DateReplace((PyDateTimeDate)self, args, kw));
            Method(type, "__reduce__", PyCall.NoArgs, (self, args, kw) => DateReduce((PyDateTimeDate)self));
            // date_getset
            Getter(type, "year", self => DateYear((PyDateTimeDate)self));
            Getter(type, "month", self => DateMonth((PyDateTimeDate)self));
            Getter(type, "day", self => DateDay((PyDateTimeDate)self));
            return type;
        }

        // PyDateTime_IsoCalendarDateType: tp_base is tuple, filled in by PyInit__datetime.
        private static PyType MakeIsoCalendarDateType()
        {
            var type = new PyType("IsoCalendarDate", new[] { PyType.TupleType }, "datetime");
            IsoCalendarDateType = type;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => IsoCalendarDateNew((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            Slot(type, "__repr__", (self, a) => IsoCalendarDateRepr((PyDateTimeIsoCalendarDate)self));
            Method(type, "__reduce__", PyCall.NoArgs, (self, args, kw) => IsoCalendarDateReduce(self));
            Getter(type, "year", IsoCalendarDateYear);
            Getter(type, "week", IsoCalendarDateWeek);
            Getter(type, "weekday", IsoCalendarDateWeekday);
            return type;
        }
    }
}
