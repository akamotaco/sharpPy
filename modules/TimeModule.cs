using System;
using System.Text;
using System.Threading;

namespace SharpPy
{
    /// <summary>
    /// Modules/timemodule.c. Times are _PyTime_t nanoseconds (core/PyTime.cs); the broken-down
    /// times go through the C library's functions (modules/CTimeRuntime.cs), as CPython's do.
    /// </summary>
    public class TimeModule : PyModule
    {
        private static readonly bool Windows = OperatingSystem.IsWindows();

        // struct_time_type_desc
        internal static readonly PyStructSequenceType StructTimeType = new("time", "struct_time", new[]
        {
            "tm_year", "tm_mon", "tm_mday", "tm_hour", "tm_min", "tm_sec", "tm_wday", "tm_yday", "tm_isdst",
            "tm_zone", "tm_gmtoff",
        }, 9, heap: true);

        static TimeModule()
        {
            if (PyDocs.Lookup("time.struct_time") is { } doc)
                StructTimeType.TypeDict["__doc__"] = PyDocs.DocFromInternal("struct_time", doc);
        }

        public TimeModule() : base("time")
        {
            if (PyDocs.Lookup("time.__doc__") is { } doc)
                SetAttribute("__doc__", PyString.From(doc));
            Function("time", (args, kwargs) => { PyArgs.NoArgs("time.time", args, kwargs); return FloatFromPyTime(SystemTime()); });
            Function("time_ns", (args, kwargs) => { PyArgs.NoArgs("time.time_ns", args, kwargs); return PyTime.AsNanosecondsObject(SystemTime()); });
            Function("sleep", Sleep);
            Function("gmtime", GmTime);
            Function("localtime", LocalTime);
            Function("asctime", AscTime);
            Function("ctime", CTime);
            Function("mktime", MkTime);
            Function("strftime", StrFTime);
            Function("strptime", StrPTime);
            Function("monotonic", (args, kwargs) => { PyArgs.NoArgs("time.monotonic", args, kwargs); return FloatFromPyTime(Monotonic()); });
            Function("monotonic_ns", (args, kwargs) => { PyArgs.NoArgs("time.monotonic_ns", args, kwargs); return PyTime.AsNanosecondsObject(Monotonic()); });
            Function("process_time", (args, kwargs) => { PyArgs.NoArgs("time.process_time", args, kwargs); return FloatFromPyTime(ProcessTime()); });
            Function("process_time_ns", (args, kwargs) => { PyArgs.NoArgs("time.process_time_ns", args, kwargs); return PyTime.AsNanosecondsObject(ProcessTime()); });
            Function("thread_time", (args, kwargs) => { PyArgs.NoArgs("time.thread_time", args, kwargs); return FloatFromPyTime(ThreadTime()); });
            Function("thread_time_ns", (args, kwargs) => { PyArgs.NoArgs("time.thread_time_ns", args, kwargs); return PyTime.AsNanosecondsObject(ThreadTime()); });
            Function("perf_counter", (args, kwargs) => { PyArgs.NoArgs("time.perf_counter", args, kwargs); return FloatFromPyTime(PerfCounter()); });
            Function("perf_counter_ns", (args, kwargs) => { PyArgs.NoArgs("time.perf_counter_ns", args, kwargs); return PyTime.AsNanosecondsObject(PerfCounter()); });
            Function("get_clock_info", GetClockInfo);
            if (!Windows)
                Function("tzset", TzSet);
            InitTimezone();
            SetAttribute("struct_time", StructTimeType);
            SetAttribute("_STRUCT_TM_ITEMS", PyInt.From(11));
        }

        private void Function(string name, Func<PyObject[], PyDict, PyObject> body) =>
            SetAttribute(name, new PyBuiltinFunction(name, body));

        // METH_VARARGS: no keywords, then PyArg_ParseTuple's count.
        private static void VarArgs(string name, PyObject[] args, PyDict kwargs, int min, int max)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{name}() takes no keyword arguments");
            PyArgs.ParseTuple(name, args, min, max);
        }

        private static PyObject FloatFromPyTime(long t) => new PyFloat(PyTime.AsSecondsDouble(t));

        private static long SystemTime() => PyTime.GetSystemClock();
        private static long Monotonic() { var info = default(PyClockInfo); return PyTime.GetMonotonicClock(ref info, false); }
        private static long PerfCounter() { var info = default(PyClockInfo); return PyTime.GetPerfCounter(ref info, false); }
        private static long ProcessTime() { var info = default(PyClockInfo); return PyTime.GetProcessTime(ref info, false); }
        private static long ThreadTime() { var info = default(PyClockInfo); return PyTime.GetThreadTime(ref info, false); }

        // time(NULL)
        private static long Now() => Math.DivRem(PyTime.GetSystemClock(), 1_000_000_000L, out long rem) - (rem < 0 ? 1 : 0);

        // time_sleep
        private static PyObject Sleep(PyObject[] args, PyDict kwargs)
        {
            PyArgs.One("time.sleep", args, kwargs);
            long timeout = PyTime.FromSecondsObject(args[0], PyTimeRound.Timeout);
            if (timeout < 0)
                throw PyValueError.Create("sleep length must be non-negative");
            PySleep(timeout);
            return PyNone.Instance;
        }

        // pysleep: Sleep(0) for a zero timeout, otherwise at least the timeout, rounded up.
        private static void PySleep(long timeout)
        {
            long timeout100ns = PyTime.As100Nanoseconds(timeout, PyTimeRound.Ceiling);
            PyGil.AllowThreads(() =>
            {
                if (timeout100ns == 0)
                {
                    Thread.Sleep(0);
                    return;
                }
                long ms = (timeout100ns + 9_999) / 10_000;
                for (long left = ms; left > 0; left -= int.MaxValue)
                    Thread.Sleep((int)Math.Min(left, int.MaxValue));
            });
        }

        private static Exception FromErrno(int errno)
        {
            string message = errno == CTimeRuntime.EINVAL ? "Invalid argument" : "Value too large for defined data type";
            return new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(errno), PyString.From(message) }, null));
        }

        // _PyTime_gmtime
        private static CTm GmTimeOf(long when)
        {
            int error = CTimeRuntime.GmTime(when, out var tm);
            if (error != 0)
                throw FromErrno(error);
            return tm;
        }

        // _PyTime_localtime
        private static CTm LocalTimeOf(long when)
        {
            int error = CTimeRuntime.LocalTime(when, out var tm);
            if (error != 0)
                throw FromErrno(error);
            return tm;
        }

        // tmtotuple
        private static PyObject ToTuple(in CTm p, string zone, long gmtoff) => new PyStructSequence(StructTimeType, new PyObject[]
        {
            PyInt.From(p.Year + 1900L), PyInt.From(p.Mon + 1), PyInt.From(p.Mday), PyInt.From(p.Hour), PyInt.From(p.Min),
            PyInt.From(p.Sec), PyInt.From((p.Wday + 6) % 7), PyInt.From(p.Yday + 1), PyInt.From(p.Isdst),
            PyString.From(zone), PyInt.From(gmtoff),
        });

        // parse_time_t_args
        private static long ParseTimeT(string name, PyObject[] args, PyDict kwargs)
        {
            VarArgs(name, args, kwargs, 0, 1);
            if (args.Length == 0 || args[0] is PyNone)
                return Now();
            return PyTime.ObjectToTimeT(args[0], PyTimeRound.Floor);
        }

        // timegm, for the tm_gmtoff of a platform without one in struct tm.
        private static long TimeGm(in CTm p) =>
            p.Sec + p.Min * 60L + p.Hour * 3600L + p.Yday * 86400L +
            (p.Year - 70L) * 31536000L + ((p.Year - 69L) / 4) * 86400L -
            ((p.Year - 1L) / 100) * 86400L + ((p.Year + 299L) / 400) * 86400L;

        // time_gmtime
        private static PyObject GmTime(PyObject[] args, PyDict kwargs)
        {
            long when = ParseTimeT("gmtime", args, kwargs);
            var tm = GmTimeOf(when);
            return ToTuple(tm, "UTC", 0);
        }

        // time_localtime: the zone is strftime's %Z, the offset timegm(&buf) - when.
        private static PyObject LocalTime(PyObject[] args, PyDict kwargs)
        {
            long when = ParseTimeT("localtime", args, kwargs);
            var tm = LocalTimeOf(when);
            return ToTuple(tm, CTimeRuntime.TzName[tm.Isdst != 0 ? 1 : 0], TimeGm(tm) - when);
        }

        // gettmarg: nine ints from a tuple; struct tm has no tm_zone on Windows to read.
        private static CTm GetTmArg(PyObject args, string message)
        {
            if (args is not PyTuple tuple)
                throw PyTypeError.Create("Tuple or struct_time argument required");
            var items = tuple.Items;
            if (items.Length != 9)
                throw PyTypeError.Create(message);
            var values = new int[9];
            for (int i = 0; i < 9; i++)
                values[i] = PyArgs.ParseInt(items[i]);
            int y = values[0];
            if (y < int.MinValue + 1900)
                throw PyOverflowError.Create("year out of range");
            var p = new CTm
            {
                Year = y - 1900,
                Mon = values[1] - 1,
                Mday = values[2],
                Hour = values[3],
                Min = values[4],
                Sec = values[5],
                Wday = (int)((values[6] + 1L) % 7),
                Yday = values[7] - 1,
                Isdst = values[8],
            };
            return p;
        }

        // checktm: zero is taken for the lowest month, day and day of year.
        private static void CheckTm(ref CTm buf)
        {
            if (buf.Mon == -1)
                buf.Mon = 0;
            else if (buf.Mon < 0 || buf.Mon > 11)
                throw PyValueError.Create("month out of range");
            if (buf.Mday == 0)
                buf.Mday = 1;
            else if (buf.Mday < 0 || buf.Mday > 31)
                throw PyValueError.Create("day of month out of range");
            if (buf.Hour < 0 || buf.Hour > 23)
                throw PyValueError.Create("hour out of range");
            if (buf.Min < 0 || buf.Min > 59)
                throw PyValueError.Create("minute out of range");
            if (buf.Sec < 0 || buf.Sec > 61)
                throw PyValueError.Create("seconds out of range");
            if (buf.Wday < 0)
                throw PyValueError.Create("day of week out of range");
            if (buf.Yday == -1)
                buf.Yday = 0;
            else if (buf.Yday < 0 || buf.Yday > 365)
                throw PyValueError.Create("day of year out of range");
        }

        // time_strftime1: the pre-check of Windows, then the C strftime.
        private static string StrFTime1(string format, in CTm tm)
        {
            if (Windows)
            {
                for (int f = format.IndexOf('%'); f >= 0; f = f + 2 < format.Length ? format.IndexOf('%', f + 2) : -1)
                {
                    if (f + 1 < format.Length && format[f + 1] == '#')
                        ++f;
                    if (f + 1 >= format.Length)
                        break;
                    if (format[f + 1] == 'y' && tm.Year < 0)
                        throw PyValueError.Create("format %y requires year >= 1900 on Windows");
                }
            }
            try
            {
                return CTimeRuntime.StrFTime(format, tm);
            }
            catch (CTimeRuntime.InvalidFormat)
            {
                throw PyValueError.Create("Invalid format string");
            }
        }

        // time_strftime: runs of ASCII go to strftime, everything else is copied as it is.
        private static PyObject StrFTime(PyObject[] args, PyDict kwargs)
        {
            VarArgs("strftime", args, kwargs, 1, 2);
            if (args[0] is not PyString formatArg)
                throw PyArgs.BadArgument("strftime", "argument 1", "str", args[0]);
            CTm buf;
            if (args.Length < 2)
            {
                buf = LocalTimeOf(Now());
            }
            else
            {
                buf = GetTmArg(args[1], "strftime(): illegal time tuple argument");
                CheckTm(ref buf);
            }
            if (Windows && (buf.Year + 1900L < 1 || 9999 < buf.Year + 1900L))
                throw PyValueError.Create("strftime() requires year in [1; 9999]");
            if (buf.Isdst < -1)
                buf.Isdst = -1;
            else if (buf.Isdst > 1)
                buf.Isdst = 1;
            string format = formatArg.Value;
            var writer = new StringBuilder();
            int i = 0;
            while (i < format.Length)
            {
                int start = i;
                for (; i < format.Length; i++)
                {
                    char c = format[i];
                    if (c == 0 || c > 127)
                        break;
                }
                if (i > start)
                    writer.Append(StrFTime1(format.Substring(start, i - start), buf));
                start = i;
                for (; i < format.Length; i++)
                    if (format[i] == '%')
                        break;
                if (start < i)
                    writer.Append(format, start, i - start);
            }
            return PyString.From(writer.ToString());
        }

        // time_strptime: _strptime._strptime_time(*args)
        private static PyObject StrPTime(PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("strptime() takes no keyword arguments");
            var func = PyImportSystem.Import("_strptime").GetAttribute("_strptime_time");
            return func.Call(args, null);
        }

        private static readonly string[] WdayName = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        private static readonly string[] MonName = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        // _asctime: "%s %s%3d %.2d:%.2d:%.2d %d"
        private static PyObject AscTimeOf(in CTm t) => PyString.From(
            $"{WdayName[t.Wday]} {MonName[t.Mon]}{t.Mday,3} {t.Hour:00}:{t.Min:00}:{t.Sec:00} {1900L + t.Year}");

        // time_asctime
        private static PyObject AscTime(PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("asctime() takes no keyword arguments");
            if (args.Length > 1)
                throw PyTypeError.Create($"asctime expected at most 1 argument, got {args.Length}");
            CTm buf;
            if (args.Length == 0)
            {
                buf = LocalTimeOf(Now());
            }
            else
            {
                buf = GetTmArg(args[0], "asctime(): illegal time tuple argument");
                CheckTm(ref buf);
            }
            return AscTimeOf(buf);
        }

        // time_ctime
        private static PyObject CTime(PyObject[] args, PyDict kwargs)
        {
            long tt = ParseTimeT("ctime", args, kwargs);
            return AscTimeOf(LocalTimeOf(tt));
        }

        // time_mktime
        private static PyObject MkTime(PyObject[] args, PyDict kwargs)
        {
            PyArgs.One("time.mktime", args, kwargs);
            var tm = GetTmArg(args[0], "mktime(): illegal time tuple argument");
            tm.Wday = -1;
            long tt = CTimeRuntime.MkTime(ref tm);
            if (tt == -1 && tm.Wday == -1)
                throw PyOverflowError.Create("mktime argument out of range");
            return new PyFloat(tt);
        }

        // time_tzset, where tzset works.
        private PyObject TzSet(PyObject[] args, PyDict kwargs)
        {
            PyArgs.NoArgs("time.tzset", args, kwargs);
            CTimeRuntime.TzSet();
            InitTimezone();
            return PyNone.Instance;
        }

        // time_get_clock_info
        private static PyObject GetClockInfo(PyObject[] args, PyDict kwargs)
        {
            VarArgs("get_clock_info", args, kwargs, 1, 1);
            string name = PyArgs.ClinicStr("get_clock_info", "argument 1", "str", args[0]);
            var info = new PyClockInfo { Implementation = "", Monotonic = false, Adjustable = false, Resolution = 1.0 };
            switch (name)
            {
                case "time": PyTime.GetSystemClock(ref info, true); break;
                case "monotonic": PyTime.GetMonotonicClock(ref info, true); break;
                case "perf_counter": PyTime.GetPerfCounter(ref info, true); break;
                case "process_time": PyTime.GetProcessTime(ref info, true); break;
                case "thread_time": PyTime.GetThreadTime(ref info, true); break;
                default: throw PyValueError.Create("unknown clock");
            }
            var dict = new PyDict();
            dict.SetItem(PyString.From("implementation"), PyString.From(info.Implementation));
            dict.SetItem(PyString.From("monotonic"), PyBool.FromBool(info.Monotonic));
            dict.SetItem(PyString.From("adjustable"), PyBool.FromBool(info.Adjustable));
            dict.SetItem(PyString.From("resolution"), new PyFloat(info.Resolution));
            return new SharpPy.Modules.PySimpleNamespace(dict);
        }

        // init_timezone with HAVE_DECL_TZNAME: _timezone, no altzone, _daylight, and the names.
        private void InitTimezone()
        {
            SetAttribute("timezone", PyInt.From(CTimeRuntime.Timezone));
            SetAttribute("altzone", PyInt.From(CTimeRuntime.Timezone - 3600));
            SetAttribute("daylight", PyInt.From(CTimeRuntime.Daylight));
            SetAttribute("tzname", PyTuple.From(new PyObject[]
            {
                PyString.From(CTimeRuntime.TzName[0]), PyString.From(CTimeRuntime.TzName[1]),
            }));
        }
    }
}
