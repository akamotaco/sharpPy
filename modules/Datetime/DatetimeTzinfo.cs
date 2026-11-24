using System;

namespace SharpPy.Modules.Datetime
{
    /// <summary>Modules/_datetimemodule.c: PyDateTime_TZInfo and PyDateTime_TimeZone (lines 3666-4122).</summary>
    internal static partial class DatetimeModule
    {
        // tzinfo_nogo
        private static Exception TzinfoNogo(string methodname) =>
            PyNotImplementedError.Create($"a tzinfo subclass must implement {methodname}()");

        // tzinfo_tzname, tzinfo_utcoffset, tzinfo_dst
        private static PyObject TzinfoTzname(PyObject self, PyObject dt) => throw TzinfoNogo("tzname");
        private static PyObject TzinfoUtcoffset(PyObject self, PyObject dt) => throw TzinfoNogo("utcoffset");
        private static PyObject TzinfoDst(PyObject self, PyObject dt) => throw TzinfoNogo("dst");

        // tzinfo_fromutc
        private static PyObject TzinfoFromutc(PyObject self, PyObject dt)
        {
            if (dt is not PyDateTimeDateTime datetime)
                throw PyTypeError.Create("fromutc: argument must be a datetime");
            if (!ReferenceEquals(datetime.TzinfoOrNone, self))
                throw PyValueError.Create("fromutc: dt.tzinfo is not self");
            var off = DatetimeUtcoffset(dt);
            if (off is PyNone)
                throw PyValueError.Create("fromutc: non-None utcoffset() result required");
            var dst = DatetimeDst(dt);
            if (dst is PyNone)
                throw PyValueError.Create("fromutc: non-None dst() result required");
            var delta = (PyDateTimeDelta)DeltaSubtract(off, dst);
            var result = AddDatetimeTimedelta(datetime, delta, 1);
            dst = CallDst(datetime.TzinfoOrNone, result);
            if (dst is PyNone)
                throw PyValueError.Create("fromutc: tz.dst() gave inconsistent results; cannot convert");
            if (DeltaBool((PyDateTimeDelta)dst))
                result = AddDatetimeTimedelta((PyDateTimeDateTime)result, (PyDateTimeDelta)dst, 1);
            return result;
        }

        // tzinfo_reduce
        private static PyObject TzinfoReduce(PyObject self)
        {
            var getinitargs = self.LookupAttribute("__getinitargs__");
            PyObject args = getinitargs != null
                ? getinitargs.Call(Array.Empty<PyObject>(), null)
                : PyTuple.From(Array.Empty<PyObject>());
            var state = PyType.GetState(self);
            return PyTuple.From(self.GetPyType(), args, state);
        }

        // PyDateTime_TZInfoType
        private static PyType MakeTZInfoType()
        {
            var type = new PyType("tzinfo", new[] { PyType.ObjectType }, "datetime");
            TZInfoType = type;
            // PyType_GenericNew
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => new PyDateTimeTZInfo((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            TypeTables.GenericAttributes(type);
            Method(type, "tzname", PyCall.One, (self, args, kw) => TzinfoTzname(self, args[0]));
            Method(type, "utcoffset", PyCall.One, (self, args, kw) => TzinfoUtcoffset(self, args[0]));
            Method(type, "dst", PyCall.One, (self, args, kw) => TzinfoDst(self, args[0]));
            Method(type, "fromutc", PyCall.One, (self, args, kw) => TzinfoFromutc(self, args[0]));
            Method(type, "__reduce__", PyCall.NoArgs, (self, args, kw) => TzinfoReduce(self));
            return type;
        }

        private static readonly string[] timezone_kws = { "offset", "name" };

        // timezone_new
        private static PyObject TimezoneNew(PyType type, PyObject[] args, PyDict kw)
        {
            var parsed = ParseTupleAndKeywords("timezone", args, kw, timezone_kws, 1, convert: (i, v) =>
            {
                if (i == 0 && v is not PyDateTimeDelta)
                    throw ConverterError("timezone", 1, "datetime.timedelta", v);
                if (i == 1)
                    UnicodeArg("timezone", 2, v);
            });
            return NewTimezone(parsed[0], parsed[1]);
        }

        // timezone_richcompare
        internal static PyObject TimezoneRichCompare(PyDateTimeTimeZone self, PyObject other, PyObject.CompareOp op)
        {
            if (op != PyObject.CompareOp.EQ && op != PyObject.CompareOp.NE)
                return PyNotImplemented.Instance;
            if (other is not PyDateTimeTimeZone o)
                return PyNotImplemented.Instance;
            return DeltaRichCompare(self.Offset, o.Offset, op);
        }

        // timezone_hash
        internal static long TimezoneHash(PyDateTimeTimeZone self) => DeltaHash((PyDateTimeDelta)self.Offset);

        // _timezone_check_argument
        private static void TimezoneCheckArgument(PyObject dt, string meth)
        {
            if (dt is PyNone || dt is PyDateTimeDateTime)
                return;
            throw PyTypeError.Create($"{meth}(dt) argument must be a datetime instance or None, not {TpName(dt)}");
        }

        // timezone_repr
        internal static PyObject TimezoneRepr(PyDateTimeTimeZone self)
        {
            string type_name = TpName(self);
            if (ReferenceEquals(self, PyDateTime_TimeZone_UTC))
                return PyString.From($"{type_name}.utc");
            if (self.Name == null)
                return PyString.From($"{type_name}({self.Offset.ToRepr().Value})");
            return PyString.From($"{type_name}({self.Offset.ToRepr().Value}, {self.Name.ToRepr().Value})");
        }

        // timezone_str
        internal static PyObject TimezoneStr(PyDateTimeTimeZone self)
        {
            if (self.Name != null)
                return self.Name;
            var offset = (PyDateTimeDelta)self.Offset;
            if (ReferenceEquals(self, PyDateTime_TimeZone_UTC) ||
                (offset.Days == 0 && offset.Seconds == 0 && offset.Microseconds == 0))
                return PyString.From("UTC");
            char sign;
            if (offset.Days < 0)
            {
                sign = '-';
                offset = (PyDateTimeDelta)DeltaNegative(offset);
            }
            else
            {
                sign = '+';
            }
            int microseconds = offset.Microseconds;
            int seconds = offset.Seconds;
            int minutes = Divmod(seconds, 60, out seconds);
            int hours = Divmod(minutes, 60, out minutes);
            if (microseconds != 0)
                return PyString.From($"UTC{sign}{hours:D2}:{minutes:D2}:{seconds:D2}.{microseconds:D6}");
            if (seconds != 0)
                return PyString.From($"UTC{sign}{hours:D2}:{minutes:D2}:{seconds:D2}");
            return PyString.From($"UTC{sign}{hours:D2}:{minutes:D2}");
        }

        // timezone_tzname
        private static PyObject TimezoneTzname(PyDateTimeTimeZone self, PyObject dt)
        {
            TimezoneCheckArgument(dt, "tzname");
            return TimezoneStr(self);
        }

        // timezone_utcoffset
        private static PyObject TimezoneUtcoffset(PyDateTimeTimeZone self, PyObject dt)
        {
            TimezoneCheckArgument(dt, "utcoffset");
            return self.Offset;
        }

        // timezone_dst
        private static PyObject TimezoneDst(PyObject self, PyObject dt)
        {
            TimezoneCheckArgument(dt, "dst");
            return PyNone.Instance;
        }

        // timezone_fromutc
        private static PyObject TimezoneFromutc(PyDateTimeTimeZone self, PyObject dt)
        {
            if (dt is not PyDateTimeDateTime datetime)
                throw PyTypeError.Create("fromutc: argument must be a datetime");
            if (!datetime.HasTzinfo || !ReferenceEquals(datetime.Tzinfo, self))
                throw PyValueError.Create("fromutc: dt.tzinfo is not self");
            return AddDatetimeTimedelta(datetime, (PyDateTimeDelta)self.Offset, 1);
        }

        // timezone_getinitargs
        private static PyObject TimezoneGetinitargs(PyDateTimeTimeZone self) =>
            self.Name == null ? PyTuple.From(self.Offset) : PyTuple.From(self.Offset, self.Name);

        // PyDateTime_TimeZoneType: tp_base is tzinfo, filled in by PyInit__datetime.
        private static PyType MakeTimeZoneType()
        {
            var type = new PyType("timezone", new[] { TZInfoType }, "datetime");
            TimeZoneType = type;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => TimezoneNew((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            Slot(type, "__repr__", (self, a) => TimezoneRepr((PyDateTimeTimeZone)self));
            Slot(type, "__hash__", (self, a) => PyInt.From(TimezoneHash((PyDateTimeTimeZone)self)));
            Slot(type, "__str__", (self, a) => TimezoneStr((PyDateTimeTimeZone)self));
            RichCompareSlots(type, (self, other, op) => TimezoneRichCompare((PyDateTimeTimeZone)self, other, op));
            Method(type, "tzname", PyCall.One, (self, args, kw) => TimezoneTzname((PyDateTimeTimeZone)self, args[0]));
            Method(type, "utcoffset", PyCall.One, (self, args, kw) => TimezoneUtcoffset((PyDateTimeTimeZone)self, args[0]));
            Method(type, "dst", PyCall.One, (self, args, kw) => TimezoneDst(self, args[0]));
            Method(type, "fromutc", PyCall.One, (self, args, kw) => TimezoneFromutc((PyDateTimeTimeZone)self, args[0]));
            Method(type, "__getinitargs__", PyCall.NoArgs, (self, args, kw) => TimezoneGetinitargs((PyDateTimeTimeZone)self));
            return type;
        }
    }
}
