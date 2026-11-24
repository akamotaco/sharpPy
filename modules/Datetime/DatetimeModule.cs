using System;

namespace SharpPy.Modules.Datetime
{
    /// <summary>
    /// Modules/_datetimemodule.c (CPython 3.12.15): the _datetime module, "Fast implementation of the
    /// datetime type." The static types are made once (their PyTypeObject definitions and the
    /// tp_dict entries _datetime_exec adds); each import builds the module dict around them.
    /// </summary>
    internal static partial class DatetimeModule
    {
        internal static PyType DeltaType;
        internal static PyType DateType;
        internal static PyType DateTimeType;
        internal static PyType TimeType;
        internal static PyType TZInfoType;
        internal static PyType TimeZoneType;
        internal static PyType IsoCalendarDateType;

        // The static PyTypeObjects, and what _datetime_exec puts in their tp_dict.
        static DatetimeModule()
        {
            MakeDeltaType();
            MakeDateType();
            MakeIsoCalendarDateType();
            MakeTZInfoType();
            MakeTimeZoneType();
            MakeTimeType();
            MakeDateTimeType();

            // timedelta values
            var d = DeltaType.TypeDict;
            d["resolution"] = NewDelta(0, 0, 1, false);
            d["min"] = NewDelta(-MAX_DELTA_DAYS, 0, 0, false);
            d["max"] = NewDelta(MAX_DELTA_DAYS, 24 * 3600 - 1, 1000000 - 1, false);

            // date values
            d = DateType.TypeDict;
            d["min"] = NewDate(1, 1, 1);
            d["max"] = NewDate(MAXYEAR, 12, 31);
            d["resolution"] = NewDelta(1, 0, 0, false);

            // time values
            d = TimeType.TypeDict;
            d["min"] = NewTime(0, 0, 0, 0, PyNone.Instance, 0);
            d["max"] = NewTime(23, 59, 59, 999999, PyNone.Instance, 0);
            d["resolution"] = NewDelta(0, 0, 1, false);

            // datetime values
            d = DateTimeType.TypeDict;
            d["min"] = NewDatetime(1, 1, 1, 0, 0, 0, 0, PyNone.Instance, 0);
            d["max"] = NewDatetime(MAXYEAR, 12, 31, 23, 59, 59, 999999, PyNone.Instance, 0);
            d["resolution"] = NewDelta(0, 0, 1, false);

            // timezone values
            d = TimeZoneType.TypeDict;
            var x = CreateTimezone(NewDelta(0, 0, 0, false), null);
            d["utc"] = x;
            PyDateTime_TimeZone_UTC = x;
            // bpo-37642: rounded to the nearest minute for backwards compatibility.
            d["min"] = CreateTimezone(NewDelta(-1, 60, 0, true), null);
            d["max"] = CreateTimezone(NewDelta(0, (23 * 60 + 59) * 60, 0, false), null);

            // Epoch
            PyDateTime_Epoch = NewDatetime(1970, 1, 1, 0, 0, 0, 0, PyDateTime_TimeZone_UTC, 0);
        }

        /// <summary>PyInit__datetime: the module, and _datetime_exec's names in it.</summary>
        internal static PyModule Create()
        {
            var module = new PyModule("_datetime");
            var dict = module.ModuleDict;
            // PyModule_AddType: the types by their names.
            foreach (var type in new[] { DateType, DateTimeType, TimeType, DeltaType, TZInfoType, TimeZoneType })
                dict[type.Name] = type;
            dict["MINYEAR"] = PyInt.From(MINYEAR);
            dict["MAXYEAR"] = PyInt.From(MAXYEAR);
            dict["datetime_CAPI"] = new PyCapsule("datetime.datetime_CAPI", GetDatetimeCapi());
            dict["UTC"] = PyDateTime_TimeZone_UTC;
            return module;
        }

        /// <summary>
        /// PyDateTime_CAPI (Include/datetime.h): the types and the constructors C extensions reach
        /// through the capsule.
        /// </summary>
        internal sealed class DatetimeCapi
        {
            internal PyType DateType, DateTimeType, TimeType, DeltaType, TZInfoType;
            internal PyObject TimeZone_UTC;
            internal Func<int, int, int, PyType, PyObject> Date_FromDate;
            internal Func<int, int, int, int, int, int, int, PyObject, PyType, PyObject> DateTime_FromDateAndTime;
            internal Func<int, int, int, int, PyObject, PyType, PyObject> Time_FromTime;
            internal Func<int, int, int, bool, PyType, PyObject> Delta_FromDelta;
            internal Func<PyObject, PyObject, PyObject> TimeZone_FromTimeZone;
            internal Func<PyObject, PyObject[], PyDict, PyObject> DateTime_FromTimestamp;
            internal Func<PyObject, PyObject[], PyObject> Date_FromTimestamp;
            internal Func<int, int, int, int, int, int, int, PyObject, int, PyType, PyObject> DateTime_FromDateAndTimeAndFold;
            internal Func<int, int, int, int, PyObject, int, PyType, PyObject> Time_FromTimeAndFold;
        }

        // get_datetime_capi
        private static DatetimeCapi GetDatetimeCapi() => new DatetimeCapi
        {
            DateType = DateType,
            DateTimeType = DateTimeType,
            TimeType = TimeType,
            DeltaType = DeltaType,
            TZInfoType = TZInfoType,
            Date_FromDate = NewDateEx,
            DateTime_FromDateAndTime = NewDatetimeEx,
            Time_FromTime = NewTimeEx,
            Delta_FromDelta = NewDeltaEx,
            TimeZone_FromTimeZone = NewTimezone,
            DateTime_FromTimestamp = DatetimeFromtimestamp,
            Date_FromTimestamp = DatetimeDateFromtimestampCapi,
            DateTime_FromDateAndTimeAndFold = NewDatetimeEx2,
            Time_FromTimeAndFold = NewTimeEx2,
            TimeZone_UTC = PyDateTime_TimeZone_UTC,
        };

        // ---- the PyTypeObject tables: add_operators and the PyMethodDef/PyGetSetDef rows ---------

        // A slot wrapper of add_operators that calls this type's own slot function.
        private static void Slot(PyType type, string name, Func<PyObject, PyObject[], PyObject> slot) =>
            type.TypeDict[name] = new PyWrapperDescriptor(name, type, (self, args, kwargs) => slot(self, args)) { CallsVirtual = true };

        // richcmp_lt ... richcmp_ge: the wrappers of tp_richcompare.
        private static void RichCompareSlots(PyType type, Func<PyObject, PyObject, PyObject.CompareOp, PyObject> richcompare)
        {
            foreach (var (name, op) in new[]
            {
                ("__lt__", PyObject.CompareOp.LT), ("__le__", PyObject.CompareOp.LE), ("__eq__", PyObject.CompareOp.EQ),
                ("__ne__", PyObject.CompareOp.NE), ("__gt__", PyObject.CompareOp.GT), ("__ge__", PyObject.CompareOp.GE),
            })
                Slot(type, name, (self, args) => richcompare(self, args[0], op));
        }

        // wrap_binaryfunc_l and wrap_binaryfunc_r: __op__ calls the slot as (self, other), __rop__
        // as (other, self).
        private static void BinarySlots(PyType type, string op, Func<PyObject, PyObject, PyObject> slot)
        {
            Slot(type, $"__{op}__", (self, args) => slot(self, args[0]));
            string reflected = $"__r{op}__";
            type.TypeDict[reflected] = new PyWrapperDescriptor(reflected, type, (self, args, kwargs) => slot(args[0], self));
        }

        // A PyMethodDef row of an instance method: the descriptor checks its convention.
        private static void Method(PyType type, string name, PyCall kind, Func<PyObject, PyObject[], PyDict, PyObject> body)
        {
            if (kind == PyCall.ParseTuple)
            {
                // METH_VARARGS: cfunction_call refuses keywords by ml_name.
                var inner = body;
                body = (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create($"{name}() takes no keyword arguments");
                    return inner(self, args, kwargs);
                };
            }
            // The measured convention (BuiltinArity.g.cs) when there is one, else METH_NOARGS and
            // METH_O by their flag.
            type.TypeDict[name] = PyArity.Of(type, name) == null && kind is PyCall.NoArgs or PyCall.One
                ? new PyMethodDescriptor(name, type, body, 0, int.MaxValue, acceptsKwargs: true)
                {
                    Convention = new PyArity.Convention(kind, 0, 0),
                }
                : new PyMethodDescriptor(name, type, body, 0, int.MaxValue, acceptsKwargs: true);
        }

        // A METH_CLASS row: the class first, then what the convention admits.
        private static void ClassMethod(PyType type, string name, PyCall kind, Func<PyObject, PyObject[], PyDict, PyObject> body)
        {
            type.TypeDict[name] = new PyClassMethodDescriptor(name, type, new PyBuiltinFunction(name, (args, kwargs) =>
            {
                var cls = args[0];
                var rest = args[1..];
                string label = PyBuiltinFunction.QualifiedClassLabel(cls) + "." + name;
                switch (kind)
                {
                    case PyCall.NoArgs:
                        PyArgs.NoArgs(label, rest, kwargs);
                        break;
                    case PyCall.One:
                        PyArgs.One(label, rest, kwargs);
                        break;
                    case PyCall.ParseTuple:
                        if (kwargs != null && kwargs.Length() != 0)
                            throw PyTypeError.Create($"{name}() takes no keyword arguments");
                        break;
                }
                return body(cls, rest, kwargs);
            }));
        }

        // A PyGetSetDef row with a getter only.
        private static void Getter(PyType type, string name, Func<PyObject, PyObject> get) =>
            type.TypeDict[name] = new PyGetSetDescriptor(name, type, get);
    }
}
