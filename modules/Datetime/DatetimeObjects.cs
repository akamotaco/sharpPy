namespace SharpPy.Modules.Datetime
{
    // The object structs of Include/datetime.h. Each type's slots (tp_repr, tp_hash, tp_richcompare,
    // nb_*) are the C# virtuals, which call the C functions in DatetimeModule. An instance of a
    // Python subclass is the same struct with its class in _customType (tp_alloc of the subclass).

    /// <summary>PyDateTime_Delta: days, seconds, microseconds, normalized; hashcode -1 until computed.</summary>
    internal class PyDateTimeDelta : PyObject
    {
        internal long Hashcode = -1;
        internal int Days;
        internal int Seconds;
        internal int Microseconds;

        internal PyDateTimeDelta(PyType type)
        {
            if (!ReferenceEquals(type, DatetimeModule.DeltaType))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? DatetimeModule.DeltaType;

        protected internal override PyObject Repr() => DatetimeModule.DeltaRepr(this);
        protected internal override PyObject Str() => DatetimeModule.DeltaStr(this);
        protected internal override long Hash() => DatetimeModule.DeltaHash(this);
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => DatetimeModule.DeltaRichCompare(this, other, op);
        protected internal override bool Bool() => DatetimeModule.DeltaBool(this);
        public override PyObject Add(PyObject other) => DatetimeModule.DeltaAdd(this, other);
        public override PyObject Subtract(PyObject other) => DatetimeModule.DeltaSubtract(this, other);
        public override PyObject Multiply(PyObject other) => DatetimeModule.DeltaMultiply(this, other);
        public override PyObject Divide(PyObject other) => DatetimeModule.DeltaTruedivide(this, other);
        public override PyObject FloorDivide(PyObject other) => DatetimeModule.DeltaDivide(this, other);
        public override PyObject Modulo(PyObject other) => DatetimeModule.DeltaRemainder(this, other);
        public override PyObject DivMod(PyObject other) => DatetimeModule.DeltaDivmod(this, other);
        protected internal override PyObject NbNegative() => DatetimeModule.DeltaNegative(this);
        protected internal override PyObject NbPositive() => DatetimeModule.DeltaPositive(this);
        protected internal override PyObject NbAbsolute() => DatetimeModule.DeltaAbs(this);
    }

    /// <summary>PyDateTime_Date: year, month, day (the data bytes of the C struct).</summary>
    internal class PyDateTimeDate : PyObject
    {
        internal long Hashcode = -1;
        internal int Year;
        internal int Month;
        internal int Day;

        internal PyDateTimeDate(PyType type)
        {
            if (!ReferenceEquals(type, DefaultType))
                SetCustomType(type);
        }

        protected virtual PyType DefaultType => DatetimeModule.DateType;

        public override PyType GetPyType() => _customType ?? DefaultType;

        protected internal override PyObject Repr() => DatetimeModule.DateRepr(this);
        protected internal override PyObject Str() => DatetimeModule.DateStr(this);
        protected internal override long Hash() => DatetimeModule.DateHash(this);
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => DatetimeModule.DateRichCompare(this, other, op);
        public override PyObject Add(PyObject other) => DatetimeModule.DateAdd(this, other);
        public override PyObject Subtract(PyObject other) => DatetimeModule.DateSubtract(this, other);
    }

    /// <summary>
    /// PyDateTime_DateTime: the date fields, then hour, minute, second, microsecond, fold and, when
    /// hastzinfo, the tzinfo.
    /// </summary>
    internal sealed class PyDateTimeDateTime : PyDateTimeDate
    {
        internal int Hour;
        internal int Minute;
        internal int Second;
        internal int Microsecond;
        internal int Fold;
        internal bool HasTzinfo;
        internal PyObject Tzinfo;

        internal PyDateTimeDateTime(PyType type) : base(type)
        {
        }

        protected override PyType DefaultType => DatetimeModule.DateTimeType;

        // GET_DT_TZINFO: the tzinfo member, or None.
        internal PyObject TzinfoOrNone => HasTzinfo ? Tzinfo : PyNone.Instance;

        protected internal override PyObject Repr() => DatetimeModule.DatetimeRepr(this);
        protected internal override PyObject Str() => DatetimeModule.DatetimeStr(this);
        protected internal override long Hash() => DatetimeModule.DatetimeHash(this);
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => DatetimeModule.DatetimeRichCompare(this, other, op);
        public override PyObject Add(PyObject other) => DatetimeModule.DatetimeAdd(this, other);
        public override PyObject Subtract(PyObject other) => DatetimeModule.DatetimeSubtract(this, other);
    }

    /// <summary>PyDateTime_Time: hour, minute, second, microsecond, fold and the tzinfo when hastzinfo.</summary>
    internal sealed class PyDateTimeTime : PyObject
    {
        internal long Hashcode = -1;
        internal int Hour;
        internal int Minute;
        internal int Second;
        internal int Microsecond;
        internal int Fold;
        internal bool HasTzinfo;
        internal PyObject Tzinfo;

        internal PyDateTimeTime(PyType type)
        {
            if (!ReferenceEquals(type, DatetimeModule.TimeType))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? DatetimeModule.TimeType;

        // GET_TIME_TZINFO
        internal PyObject TzinfoOrNone => HasTzinfo ? Tzinfo : PyNone.Instance;

        protected internal override PyObject Repr() => DatetimeModule.TimeRepr(this);
        protected internal override PyObject Str() => DatetimeModule.TimeStr(this);
        protected internal override long Hash() => DatetimeModule.TimeHash(this);
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => DatetimeModule.TimeRichCompare(this, other, op);
    }

    /// <summary>PyDateTime_TZInfo: no fields of its own; Python subclasses keep their __dict__.</summary>
    internal class PyDateTimeTZInfo : PyObject
    {
        internal PyDateTimeTZInfo(PyType type)
        {
            if (!ReferenceEquals(type, DefaultType))
                SetCustomType(type);
        }

        // A struct that starts with PyDateTime_TZInfo in another module (_zoneinfo's
        // PyZoneInfo_ZoneInfo), whose type is that module's: it records its class itself.
        protected PyDateTimeTZInfo()
        {
        }

        protected virtual PyType DefaultType => DatetimeModule.TZInfoType;

        public override PyType GetPyType() => _customType ?? DefaultType;
    }

    /// <summary>PyDateTime_TimeZone: the offset timedelta and the name (null when not given).</summary>
    internal sealed class PyDateTimeTimeZone : PyDateTimeTZInfo
    {
        internal PyObject Offset;
        internal PyObject Name;

        internal PyDateTimeTimeZone(PyType type) : base(type)
        {
        }

        protected override PyType DefaultType => DatetimeModule.TimeZoneType;

        protected internal override PyObject Repr() => DatetimeModule.TimezoneRepr(this);
        protected internal override PyObject Str() => DatetimeModule.TimezoneStr(this);
        protected internal override long Hash() => DatetimeModule.TimezoneHash(this);
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => DatetimeModule.TimezoneRichCompare(this, other, op);
    }

    /// <summary>
    /// PyDateTime_IsoCalendarDate: a tuple (year, week, weekday) of type IsoCalendarDate, a final
    /// subclass of tuple.
    /// </summary>
    internal sealed class PyDateTimeIsoCalendarDate : PyTuple
    {
        internal PyDateTimeIsoCalendarDate(PyObject[] items) : base(items)
        {
        }

        public override PyType GetPyType() => DatetimeModule.IsoCalendarDateType;
        public override string GetTypeName() => DatetimeModule.IsoCalendarDateType.FullName;

        protected internal override PyString Repr() => (PyString)DatetimeModule.IsoCalendarDateRepr(this);
    }
}
