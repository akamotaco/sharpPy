using System;
using System.Collections.Generic;
using SharpPy.Modules.Datetime;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_testcapi/datetime.c (CPython 3.12.15): the datetime C API (Include/datetime.h) reached
    /// from Python, through the capsule _datetime publishes as datetime.datetime_CAPI.
    /// </summary>
    internal static partial class _TestCapiModule
    {
        // PyDateTimeAPI: set by PyDateTime_IMPORT.
        private static DatetimeModule.DatetimeCapi PyDateTimeAPI;
        private static int test_run_counter;

        // PyCapsule_Import(PyDateTime_CAPSULE_NAME, 0): the module "datetime", its attribute
        // datetime_CAPI, a capsule of that name.
        private static DatetimeModule.DatetimeCapi PyCapsuleImport(string name)
        {
            int dot = name.LastIndexOf('.');
            PyObject obj = PyImportSystem.Import(name.Substring(0, dot));
            obj = obj.GetAttribute(name.Substring(dot + 1));
            if (obj is PyCapsule capsule && capsule.Name == name && capsule.Pointer is DatetimeModule.DatetimeCapi capi)
                return capi;
            throw PyAttributeError.Create($"PyCapsule_Import \"{name}\" is not valid");
        }

        // "p": PyObject_IsTrue.
        private static int Predicate(PyObject value) => value.IsTrue() ? 1 : 0;

        // test_datetime_capi
        private static PyObject TestDatetimeCapi()
        {
            if (PyDateTimeAPI != null)
            {
                if (test_run_counter != 0)
                    return PyNone.Instance;
                throw PyAssertionError.Create("PyDateTime_CAPI somehow initialized");
            }
            test_run_counter++;
            PyDateTimeAPI = PyCapsuleImport("datetime.datetime_CAPI");
            return PyNone.Instance;
        }

        // MAKE_DATETIME_CHECK_FUNC: PyArg_ParseTuple(args, "O|p"), then the check or the exact check.
        private static PyObject MakeDatetimeCheckFunc(string name, PyObject[] args, Func<PyType> type)
        {
            PyArgs.ParseTuple(name, args, 1, 2);
            int exact = args.Length > 1 ? Predicate(args[1]) : 0;
            var obj = args[0];
            bool rv = exact != 0 ? ReferenceEquals(obj.GetPyType(), type()) : obj.GetPyType().IsSubclassOf(type());
            return PyBool.FromBool(rv);
        }

        // make_timezones_capi
        private static PyObject MakeTimezonesCapi()
        {
            var offset = PyDateTimeAPI.Delta_FromDelta(0, -18000, 0, true, PyDateTimeAPI.DeltaType);
            var name = PyString.From("EST");
            var est_zone_capi = PyDateTimeAPI.TimeZone_FromTimeZone(offset, name);
            var est_zone_macro = PyDateTimeAPI.TimeZone_FromTimeZone(offset, name);
            var est_zone_macro_noname = PyDateTimeAPI.TimeZone_FromTimeZone(offset, null);
            return PyTuple.From(est_zone_capi, est_zone_macro, est_zone_macro_noname);
        }

        // get_timezones_offset_zero
        private static PyObject GetTimezonesOffsetZero()
        {
            var offset = PyDateTimeAPI.Delta_FromDelta(0, 0, 0, true, PyDateTimeAPI.DeltaType);
            var name = PyString.From("");
            var utc_singleton_0 = PyDateTimeAPI.TimeZone_FromTimeZone(offset, null);
            var utc_singleton_1 = PyDateTimeAPI.TimeZone_FromTimeZone(offset, null);
            var non_utc_zone = PyDateTimeAPI.TimeZone_FromTimeZone(offset, name);
            return PyTuple.From(utc_singleton_0, utc_singleton_1, non_utc_zone);
        }

        // get_timezone_utc_capi: the macro PyDateTime_TimeZone_UTC is PyDateTimeAPI->TimeZone_UTC.
        private static PyObject GetTimezoneUtcCapi(PyObject[] args)
        {
            PyArgs.ParseTuple("get_timezone_utc_capi", args, 0, 1);
            return PyDateTimeAPI.TimeZone_UTC;
        }

        // The "p" then "i" items of a format.
        private static int[] PredicateAndInts(string name, PyObject[] args, int count)
        {
            PyArgs.ParseTuple(name, args, count, count);
            var values = new int[count];
            values[0] = Predicate(args[0]);
            for (int i = 1; i < count; i++)
                values[i] = PyArgs.ParseInt(args[i]);
            return values;
        }

        // get_date_fromdate: PyDate_FromDate is Date_FromDate with DateType.
        private static PyObject GetDateFromdate(PyObject[] args)
        {
            var v = PredicateAndInts("get_date_fromdate", args, 4);
            return PyDateTimeAPI.Date_FromDate(v[1], v[2], v[3], PyDateTimeAPI.DateType);
        }

        // get_datetime_fromdateandtime
        private static PyObject GetDatetimeFromdateandtime(PyObject[] args)
        {
            var v = PredicateAndInts("get_datetime_fromdateandtime", args, 8);
            return PyDateTimeAPI.DateTime_FromDateAndTime(v[1], v[2], v[3], v[4], v[5], v[6], v[7],
                PyNone.Instance, PyDateTimeAPI.DateTimeType);
        }

        // get_datetime_fromdateandtimeandfold
        private static PyObject GetDatetimeFromdateandtimeandfold(PyObject[] args)
        {
            var v = PredicateAndInts("get_datetime_fromdateandtimeandfold", args, 9);
            return PyDateTimeAPI.DateTime_FromDateAndTimeAndFold(v[1], v[2], v[3], v[4], v[5], v[6], v[7],
                PyNone.Instance, v[8], PyDateTimeAPI.DateTimeType);
        }

        // get_time_fromtime
        private static PyObject GetTimeFromtime(PyObject[] args)
        {
            var v = PredicateAndInts("get_time_fromtime", args, 5);
            return PyDateTimeAPI.Time_FromTime(v[1], v[2], v[3], v[4], PyNone.Instance, PyDateTimeAPI.TimeType);
        }

        // get_time_fromtimeandfold
        private static PyObject GetTimeFromtimeandfold(PyObject[] args)
        {
            var v = PredicateAndInts("get_time_fromtimeandfold", args, 6);
            return PyDateTimeAPI.Time_FromTimeAndFold(v[1], v[2], v[3], v[4], PyNone.Instance, v[5], PyDateTimeAPI.TimeType);
        }

        // get_delta_fromdsu: PyDelta_FromDSU normalizes.
        private static PyObject GetDeltaFromdsu(PyObject[] args)
        {
            var v = PredicateAndInts("get_delta_fromdsu", args, 4);
            return PyDateTimeAPI.Delta_FromDelta(v[1], v[2], v[3], true, PyDateTimeAPI.DeltaType);
        }

        // get_date_fromtimestamp
        private static PyObject GetDateFromtimestamp(PyObject[] args)
        {
            PyArgs.ParseTuple("get_date_fromtimestamp", args, 1, 2);
            var tsargs = new[] { args[0] };
            return PyDateTimeAPI.Date_FromTimestamp(PyDateTimeAPI.DateType, tsargs);
        }

        // get_datetime_fromtimestamp
        private static PyObject GetDatetimeFromtimestamp(PyObject[] args)
        {
            PyArgs.ParseTuple("get_datetime_fromtimestamp", args, 2, 4);
            int usetz = args.Length > 2 ? Predicate(args[2]) : 0;
            var tsargs = usetz != 0 ? new[] { args[0], args[1] } : new[] { args[0] };
            return PyDateTimeAPI.DateTime_FromTimestamp(PyDateTimeAPI.DateTimeType, tsargs, null);
        }

        // test_PyDateTime_GET: the date bytes of any date or datetime.
        private static PyObject TestPyDateTimeGet(PyObject obj)
        {
            var date = (PyDateTimeDate)obj;
            return PyTuple.From(PyInt.From(date.Year), PyInt.From(date.Month), PyInt.From(date.Day));
        }

        // test_PyDateTime_DATE_GET
        private static PyObject TestPyDateTimeDateGet(PyObject obj)
        {
            var dt = (PyDateTimeDateTime)obj;
            return PyTuple.From(PyInt.From(dt.Hour), PyInt.From(dt.Minute), PyInt.From(dt.Second),
                PyInt.From(dt.Microsecond), dt.TzinfoOrNone);
        }

        // test_PyDateTime_TIME_GET
        private static PyObject TestPyDateTimeTimeGet(PyObject obj)
        {
            var t = (PyDateTimeTime)obj;
            return PyTuple.From(PyInt.From(t.Hour), PyInt.From(t.Minute), PyInt.From(t.Second),
                PyInt.From(t.Microsecond), t.TzinfoOrNone);
        }

        // test_PyDateTime_DELTA_GET
        private static PyObject TestPyDateTimeDeltaGet(PyObject obj)
        {
            var delta = (PyDateTimeDelta)obj;
            return PyTuple.From(PyInt.From(delta.Days), PyInt.From(delta.Seconds), PyInt.From(delta.Microseconds));
        }

        // _PyTestCapi_Init_DateTime: test_methods
        private static void InitDateTime(IDictionary<string, PyObject> d)
        {
            Add(d, "PyDateTime_DATE_GET", args => { One("PyDateTime_DATE_GET", args); return TestPyDateTimeDateGet(args[0]); });
            Add(d, "PyDateTime_DELTA_GET", args => { One("PyDateTime_DELTA_GET", args); return TestPyDateTimeDeltaGet(args[0]); });
            Add(d, "PyDateTime_GET", args => { One("PyDateTime_GET", args); return TestPyDateTimeGet(args[0]); });
            Add(d, "PyDateTime_TIME_GET", args => { One("PyDateTime_TIME_GET", args); return TestPyDateTimeTimeGet(args[0]); });
            Add(d, "datetime_check_date", args => MakeDatetimeCheckFunc("datetime_check_date", args, () => DatetimeModule.DateType));
            Add(d, "datetime_check_datetime", args => MakeDatetimeCheckFunc("datetime_check_datetime", args, () => DatetimeModule.DateTimeType));
            Add(d, "datetime_check_delta", args => MakeDatetimeCheckFunc("datetime_check_delta", args, () => DatetimeModule.DeltaType));
            Add(d, "datetime_check_time", args => MakeDatetimeCheckFunc("datetime_check_time", args, () => DatetimeModule.TimeType));
            Add(d, "datetime_check_tzinfo", args => MakeDatetimeCheckFunc("datetime_check_tzinfo", args, () => DatetimeModule.TZInfoType));
            Add(d, "get_date_fromdate", GetDateFromdate);
            Add(d, "get_date_fromtimestamp", GetDateFromtimestamp);
            Add(d, "get_datetime_fromdateandtime", GetDatetimeFromdateandtime);
            Add(d, "get_datetime_fromdateandtimeandfold", GetDatetimeFromdateandtimeandfold);
            Add(d, "get_datetime_fromtimestamp", GetDatetimeFromtimestamp);
            Add(d, "get_delta_fromdsu", GetDeltaFromdsu);
            Add(d, "get_time_fromtime", GetTimeFromtime);
            Add(d, "get_time_fromtimeandfold", GetTimeFromtimeandfold);
            Add(d, "get_timezone_utc_capi", GetTimezoneUtcCapi);
            Add(d, "get_timezones_offset_zero", args => { NoArgs("get_timezones_offset_zero", args); return GetTimezonesOffsetZero(); });
            Add(d, "make_timezones_capi", args => { NoArgs("make_timezones_capi", args); return MakeTimezonesCapi(); });
            Add(d, "test_datetime_capi", args => { NoArgs("test_datetime_capi", args); return TestDatetimeCapi(); });
        }

        // METH_O and METH_NOARGS counts of a module function.
        private static void One(string name, PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"{name}() takes exactly one argument ({args.Length} given)");
        }

        private static void NoArgs(string name, PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"{name}() takes no arguments ({args.Length} given)");
        }
    }
}
