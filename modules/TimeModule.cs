using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace SharpPy
{
    // CPython 3.12 time.struct_time 구현
    public class PyStructTime : PyObject
    {
        public int Year { get; }
        public int Month { get; }
        public int Day { get; }
        public int Hour { get; }
        public int Minute { get; }
        public int Second { get; }
        public int WeekDay { get; }
        public int YearDay { get; }
        public int IsDst { get; }
        public int GmtOffset { get; }
        public string Zone { get; }

        public PyStructTime(int year, int month, int day, int hour, int minute, int second,
            int weekday, int yearday, int isdst, int gmtoff = 0, string zone = "")
        {
            Year = year;
            Month = month;
            Day = day;
            Hour = hour;
            Minute = minute;
            Second = second;
            WeekDay = weekday;
            YearDay = yearday;
            IsDst = isdst;
            GmtOffset = gmtoff;
            Zone = zone;
        }

        public static PyStructTime FromDateTime(DateTime dt, int isdst = -1)
        {
            int weekday = (int)dt.DayOfWeek;
            // Python의 월요일=0 규칙으로 변환 (C#의 일요일=0과 다름)
            weekday = (weekday + 6) % 7;

            int yearday = dt.DayOfYear;

            return new PyStructTime(
                dt.Year, dt.Month, dt.Day,
                dt.Hour, dt.Minute, dt.Second,
                weekday, yearday, isdst
            );
        }

        public static PyStructTime FromDateTimeOffset(DateTimeOffset dto)
        {
            var dt = dto.DateTime;
            int weekday = (int)dt.DayOfWeek;
            weekday = (weekday + 6) % 7;
            int yearday = dt.DayOfYear;
            int gmtoff = (int)dto.Offset.TotalSeconds;
            string zone = dto.Offset.ToString(@"hh\:mm");

            // IsDst 결정: TimeZoneInfo를 사용
            var isDst = TimeZoneInfo.Local.IsDaylightSavingTime(dt) ? 1 : 0;

            return new PyStructTime(
                dt.Year, dt.Month, dt.Day,
                dt.Hour, dt.Minute, dt.Second,
                weekday, yearday, isDst, gmtoff, zone
            );
        }

        public DateTime ToDateTime()
        {
            return new DateTime(Year, Month, Day, Hour, Minute, Second, DateTimeKind.Unspecified);
        }

        // Named tuple처럼 인덱스 접근 지원
        public PyObject GetItem(int index)
        {
            return index switch
            {
                0 => new PyInt(Year),
                1 => new PyInt(Month),
                2 => new PyInt(Day),
                3 => new PyInt(Hour),
                4 => new PyInt(Minute),
                5 => new PyInt(Second),
                6 => new PyInt(WeekDay),
                7 => new PyInt(YearDay),
                8 => new PyInt(IsDst),
                _ => throw PyIndexError.Create($"tuple index out of range")
            };
        }

        public override string ToString()
        {
            return $"time.struct_time(tm_year={Year}, tm_mon={Month}, tm_mday={Day}, " +
                   $"tm_hour={Hour}, tm_min={Minute}, tm_sec={Second}, " +
                   $"tm_wday={WeekDay}, tm_yday={YearDay}, tm_isdst={IsDst})";
        }
    }

    public class TimeModule : PyModule
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly long ProcessStartTime = Stopwatch.GetTimestamp();
        private static readonly long ProcessStartTicks = Process.GetCurrentProcess().TotalProcessorTime.Ticks;

        public TimeModule() : base("time")
        {
            InitializeFunctions();
            InitializeConstants();

            // 모듈 문서
            SetAttribute("__doc__", new PyString("Time module - CPython 3.12 compatible implementation"));
        }

        private void InitializeFunctions()
        {
            // ===== 기존 함수들 =====

            // time.time() - Unix timestamp (seconds since epoch)
            SetAttribute("time", new PyFunction("time", args =>
            {
                var now = DateTime.UtcNow;
                var elapsed = now - UnixEpoch;
                return new PyFloat(elapsed.TotalSeconds);
            }));

            // time.sleep(seconds) - Suspend execution
            SetAttribute("sleep", new PyFunction("sleep", args =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create("sleep() takes exactly one argument");

                var seconds = args[0];
                // CPython 3.12: Modules/timemodule.c:397-420 - time_sleep
                double delay;

                if (seconds is PyFloat pyFloat)
                    delay = pyFloat.Value;
                else if (seconds is PyInt pyInt)
                    delay = (double)pyInt.Value;
                else
                    throw PyTypeError.Create("sleep() argument must be a number");

                if (delay < 0)
                    throw PyValueError.Create("sleep() argument must be non-negative");

                int delayMs = (int)(delay * 1000);
                if (delayMs > 0)
                    Thread.Sleep(delayMs);

                return PyNone.Instance;
            }));

            // time.perf_counter() - Performance counter
            SetAttribute("perf_counter", new PyFunction("perf_counter", args =>
            {
                var ticks = Stopwatch.GetTimestamp();
                var seconds = (double)ticks / Stopwatch.Frequency;
                return new PyFloat(seconds);
            }));

            // time.monotonic() - Monotonic clock
            SetAttribute("monotonic", new PyFunction("monotonic", args =>
            {
                var elapsed = Environment.TickCount64 / 1000.0;
                return new PyFloat(elapsed);
            }));

            // ===== 새로운 함수들 =====

            // time.time_ns() - Current time in nanoseconds
            SetAttribute("time_ns", new PyFunction("time_ns", args =>
            {
                var now = DateTime.UtcNow;
                var elapsed = now - UnixEpoch;
                // Convert to nanoseconds (1 tick = 100 nanoseconds)
                long nanoseconds = elapsed.Ticks * 100;
                return new PyInt(nanoseconds);
            }));

            // time.monotonic_ns() - Monotonic clock in nanoseconds
            SetAttribute("monotonic_ns", new PyFunction("monotonic_ns", args =>
            {
                long nanoseconds = Environment.TickCount64 * 1_000_000; // ms to ns
                return new PyInt(nanoseconds);
            }));

            // time.perf_counter_ns() - Performance counter in nanoseconds
            SetAttribute("perf_counter_ns", new PyFunction("perf_counter_ns", args =>
            {
                var ticks = Stopwatch.GetTimestamp();
                // Convert to nanoseconds
                long nanoseconds = (long)((double)ticks / Stopwatch.Frequency * 1_000_000_000);
                return new PyInt(nanoseconds);
            }));

            // time.process_time() - Process CPU time
            SetAttribute("process_time", new PyFunction("process_time", args =>
            {
                var process = Process.GetCurrentProcess();
                var cpuTime = process.TotalProcessorTime;
                return new PyFloat(cpuTime.TotalSeconds);
            }));

            // time.process_time_ns() - Process CPU time in nanoseconds
            SetAttribute("process_time_ns", new PyFunction("process_time_ns", args =>
            {
                var process = Process.GetCurrentProcess();
                var cpuTime = process.TotalProcessorTime;
                long nanoseconds = cpuTime.Ticks * 100; // 1 tick = 100 ns
                return new PyInt(nanoseconds);
            }));

            // time.thread_time() - Thread CPU time
            SetAttribute("thread_time", new PyFunction("thread_time", args =>
            {
                // C#에서는 정확한 스레드 CPU 시간을 얻기 어려우므로 process_time과 동일하게 처리
                // 실제 구현에서는 플랫폼별 API 호출이 필요
                var process = Process.GetCurrentProcess();
                var cpuTime = process.TotalProcessorTime;
                return new PyFloat(cpuTime.TotalSeconds);
            }));

            // time.thread_time_ns() - Thread CPU time in nanoseconds
            SetAttribute("thread_time_ns", new PyFunction("thread_time_ns", args =>
            {
                var process = Process.GetCurrentProcess();
                var cpuTime = process.TotalProcessorTime;
                long nanoseconds = cpuTime.Ticks * 100;
                return new PyInt(nanoseconds);
            }));

            // time.gmtime([seconds]) - Convert seconds to UTC struct_time
            SetAttribute("gmtime", new PyFunction("gmtime", args =>
            {
                DateTime dt;

                if (args.Length == 0)
                {
                    // No argument: use current time
                    dt = DateTime.UtcNow;
                }
                else if (args.Length == 1)
                {
                    var seconds = GetSecondsArgument(args[0]);
                    dt = UnixEpoch.AddSeconds(seconds);
                }
                else
                {
                    throw PyTypeError.Create("gmtime() takes at most 1 argument");
                }

                return PyStructTime.FromDateTime(dt, 0); // UTC has no DST
            }));

            // time.localtime([seconds]) - Convert seconds to local struct_time
            SetAttribute("localtime", new PyFunction("localtime", args =>
            {
                DateTime dt;

                if (args.Length == 0)
                {
                    dt = DateTime.Now;
                }
                else if (args.Length == 1)
                {
                    var seconds = GetSecondsArgument(args[0]);
                    dt = UnixEpoch.AddSeconds(seconds).ToLocalTime();
                }
                else
                {
                    throw PyTypeError.Create("localtime() takes at most 1 argument");
                }

                int isdst = TimeZoneInfo.Local.IsDaylightSavingTime(dt) ? 1 : 0;
                return PyStructTime.FromDateTime(dt, isdst);
            }));

            // time.mktime(tuple) - Convert struct_time to seconds
            SetAttribute("mktime", new PyFunction("mktime", args =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create("mktime() takes exactly one argument");

                var structTime = ParseStructTime(args[0]);
                var dt = structTime.ToDateTime();

                // Convert to local time, then to Unix timestamp
                var localDt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
                var utcDt = localDt.ToUniversalTime();
                var elapsed = utcDt - UnixEpoch;

                return new PyFloat(elapsed.TotalSeconds);
            }));

            // time.asctime([tuple]) - Convert struct_time to string
            SetAttribute("asctime", new PyFunction("asctime", args =>
            {
                PyStructTime structTime;

                if (args.Length == 0)
                {
                    structTime = PyStructTime.FromDateTime(DateTime.Now);
                }
                else if (args.Length == 1)
                {
                    structTime = ParseStructTime(args[0]);
                }
                else
                {
                    throw PyTypeError.Create("asctime() takes at most 1 argument");
                }

                return new PyString(FormatAsctime(structTime));
            }));

            // time.ctime([seconds]) - Convert seconds to string
            SetAttribute("ctime", new PyFunction("ctime", args =>
            {
                DateTime dt;

                if (args.Length == 0)
                {
                    dt = DateTime.Now;
                }
                else if (args.Length == 1)
                {
                    var seconds = GetSecondsArgument(args[0]);
                    dt = UnixEpoch.AddSeconds(seconds).ToLocalTime();
                }
                else
                {
                    throw PyTypeError.Create("ctime() takes at most 1 argument");
                }

                var structTime = PyStructTime.FromDateTime(dt);
                return new PyString(FormatAsctime(structTime));
            }));

            // time.strftime(format, tuple) - Format struct_time to string
            SetAttribute("strftime", new PyFunction("strftime", args =>
            {
                if (args.Length < 1 || args.Length > 2)
                    throw PyTypeError.Create($"strftime() takes 1 or 2 arguments ({args.Length} given)");

                if (args[0] is not PyString formatStr)
                    throw PyTypeError.Create("strftime() argument 1 must be str");

                PyStructTime structTime;
                if (args.Length == 1)
                {
                    structTime = PyStructTime.FromDateTime(DateTime.Now);
                }
                else
                {
                    structTime = ParseStructTime(args[1]);
                }

                var dt = structTime.ToDateTime();
                var result = FormatStrftime(formatStr.Value, dt, structTime);
                return new PyString(result);
            }));

            // time.strptime(string, format) - Parse string to struct_time
            SetAttribute("strptime", new PyFunction("strptime", args =>
            {
                if (args.Length != 2)
                    throw PyTypeError.Create($"strptime() takes exactly 2 arguments ({args.Length} given)");

                if (args[0] is not PyString timeStr)
                    throw PyTypeError.Create("strptime() argument 1 must be str");

                if (args[1] is not PyString formatStr)
                    throw PyTypeError.Create("strptime() argument 2 must be str");

                try
                {
                    // Python 포맷 → C# 포맷 변환
                    var dotnetFormat = ConvertPythonFormatToDotNet(formatStr.Value);
                    var dt = DateTime.ParseExact(timeStr.Value, dotnetFormat, CultureInfo.InvariantCulture);

                    return PyStructTime.FromDateTime(dt);
                }
                catch (FormatException)
                {
                    throw PyValueError.Create($"time data '{timeStr.Value}' does not match format '{formatStr.Value}'");
                }
            }));

            // time.get_clock_info(name) - Get clock information
            SetAttribute("get_clock_info", new PyFunction("get_clock_info", args =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"get_clock_info() takes exactly 1 argument ({args.Length} given)");

                if (args[0] is not PyString name)
                    throw PyTypeError.Create("get_clock_info() argument must be str");

                return GetClockInfo(name.Value);
            }));

            // struct_time 타입을 모듈에 추가 (튜플의 서브타입처럼 동작)
            SetAttribute("struct_time", new PyType("struct_time", new[] { PyType.TupleType }));
        }

        private void InitializeConstants()
        {
            var tz = TimeZoneInfo.Local;
            var baseUtcOffset = tz.BaseUtcOffset;

            // timezone: 로컬 타임존 오프셋 (초, UTC 서쪽)
            // Python은 서쪽을 양수로 표현하므로 부호 반전
            int timezone = -(int)baseUtcOffset.TotalSeconds;
            SetAttribute("timezone", new PyInt(timezone));

            // altzone: DST 타임존 오프셋
            var adjustmentRules = tz.GetAdjustmentRules();
            int altzone = timezone;
            if (adjustmentRules.Length > 0)
            {
                var dstOffset = adjustmentRules[0].DaylightDelta;
                altzone = -(int)(baseUtcOffset + dstOffset).TotalSeconds;
            }
            SetAttribute("altzone", new PyInt(altzone));

            // daylight: DST 정의 여부
            int daylight = tz.SupportsDaylightSavingTime ? 1 : 0;
            SetAttribute("daylight", new PyInt(daylight));

            // tzname: 타임존 이름 튜플
            var standardName = tz.StandardName;
            var daylightName = tz.DaylightName;
            var tznameTuple = new PyTuple(new PyObject[]
            {
                new PyString(standardName),
                new PyString(daylightName)
            });
            SetAttribute("tzname", tznameTuple);
        }

        // CPython 3.12: Modules/timemodule.c - seconds argument parsing
        // Helper: seconds 인자 파싱
        private double GetSecondsArgument(PyObject arg)
        {
            if (arg is PyFloat pyFloat)
                return pyFloat.Value;
            else if (arg is PyInt pyInt)
                return (double)pyInt.Value;
            else if (arg is PyNone)
                return (DateTime.UtcNow - UnixEpoch).TotalSeconds;
            else
                throw PyTypeError.Create("argument must be a number or None");
        }

        // Helper: tuple/sequence → PyStructTime
        private PyStructTime ParseStructTime(PyObject obj)
        {
            if (obj is PyStructTime st)
                return st;

            if (obj is PyTuple tuple)
            {
                if (tuple.Items.Length < 9)
                    throw PyTypeError.Create("function takes a sequence of at least 9 elements");

                int[] values = new int[9];
                for (int i = 0; i < 9; i++)
                {
                    if (tuple.Items[i] is PyInt pyInt)
                        values[i] = (int)pyInt.Value;
                    else
                        throw PyTypeError.Create($"an integer is required (got type {tuple.Items[i].GetType().Name})");
                }

                return new PyStructTime(
                    values[0], values[1], values[2], // year, month, day
                    values[3], values[4], values[5], // hour, min, sec
                    values[6], values[7], values[8]  // wday, yday, isdst
                );
            }

            throw PyTypeError.Create($"expected time.struct_time or tuple, got {obj.GetType().Name}");
        }

        // Helper: asctime 포맷 (예: "Mon Jan 01 00:00:00 2024")
        private string FormatAsctime(PyStructTime st)
        {
            string[] dayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            string[] monthNames = { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                                    "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

            string dayName = st.WeekDay >= 0 && st.WeekDay < 7 ? dayNames[st.WeekDay] : "???";
            string monthName = st.Month >= 1 && st.Month <= 12 ? monthNames[st.Month] : "???";

            return $"{dayName} {monthName} {st.Day,2:D2} {st.Hour:D2}:{st.Minute:D2}:{st.Second:D2} {st.Year}";
        }

        // Helper: strftime 포맷 변환
        private string FormatStrftime(string format, DateTime dt, PyStructTime st)
        {
            // Python 포맷 → .NET 포맷 변환 (주요 지시자만)
            var result = format;

            // 연도
            result = result.Replace("%Y", dt.Year.ToString("D4"));
            result = result.Replace("%y", (dt.Year % 100).ToString("D2"));

            // 월
            result = result.Replace("%m", dt.Month.ToString("D2"));
            result = result.Replace("%B", dt.ToString("MMMM", CultureInfo.InvariantCulture));
            result = result.Replace("%b", dt.ToString("MMM", CultureInfo.InvariantCulture));

            // 일
            result = result.Replace("%d", dt.Day.ToString("D2"));
            result = result.Replace("%e", dt.Day.ToString("D2"));

            // 시간
            result = result.Replace("%H", dt.Hour.ToString("D2"));
            result = result.Replace("%I", ((dt.Hour % 12 == 0 ? 12 : dt.Hour % 12)).ToString("D2"));
            result = result.Replace("%M", dt.Minute.ToString("D2"));
            result = result.Replace("%S", dt.Second.ToString("D2"));
            result = result.Replace("%p", dt.Hour < 12 ? "AM" : "PM");

            // 요일
            result = result.Replace("%A", dt.ToString("dddd", CultureInfo.InvariantCulture));
            result = result.Replace("%a", dt.ToString("ddd", CultureInfo.InvariantCulture));
            result = result.Replace("%w", ((int)dt.DayOfWeek).ToString());

            // 기타
            result = result.Replace("%j", dt.DayOfYear.ToString("D3"));
            result = result.Replace("%U", GetWeekOfYear(dt, DayOfWeek.Sunday).ToString("D2"));
            result = result.Replace("%W", GetWeekOfYear(dt, DayOfWeek.Monday).ToString("D2"));
            result = result.Replace("%%", "%");

            return result;
        }

        // Helper: 주차 계산
        private int GetWeekOfYear(DateTime dt, DayOfWeek firstDayOfWeek)
        {
            var ci = CultureInfo.InvariantCulture;
            var cal = ci.Calendar;
            var rule = firstDayOfWeek == DayOfWeek.Sunday ?
                CalendarWeekRule.FirstDay : CalendarWeekRule.FirstDay;
            return cal.GetWeekOfYear(dt, rule, firstDayOfWeek);
        }

        // Helper: Python 포맷 → .NET 포맷
        private string ConvertPythonFormatToDotNet(string pythonFormat)
        {
            var dotnetFormat = pythonFormat;

            // 주요 변환만 구현 (완전한 구현은 복잡함)
            dotnetFormat = dotnetFormat.Replace("%Y", "yyyy");
            dotnetFormat = dotnetFormat.Replace("%y", "yy");
            dotnetFormat = dotnetFormat.Replace("%m", "MM");
            dotnetFormat = dotnetFormat.Replace("%d", "dd");
            dotnetFormat = dotnetFormat.Replace("%H", "HH");
            dotnetFormat = dotnetFormat.Replace("%M", "mm");
            dotnetFormat = dotnetFormat.Replace("%S", "ss");
            dotnetFormat = dotnetFormat.Replace("%B", "MMMM");
            dotnetFormat = dotnetFormat.Replace("%b", "MMM");
            dotnetFormat = dotnetFormat.Replace("%A", "dddd");
            dotnetFormat = dotnetFormat.Replace("%a", "ddd");

            return dotnetFormat;
        }

        // Helper: get_clock_info 구현
        private PyObject GetClockInfo(string name)
        {
            // CPython의 clock_info namedtuple과 유사한 딕셔너리 반환
            var info = new PyDict();

            switch (name)
            {
                case "time":
                    info.SetItem(new PyString("implementation"), new PyString("DateTime.UtcNow"));
                    info.SetItem(new PyString("monotonic"), PyBool.False);
                    info.SetItem(new PyString("resolution"), new PyFloat(1.0 / TimeSpan.TicksPerSecond));
                    info.SetItem(new PyString("adjustable"), PyBool.True);
                    break;

                case "monotonic":
                    info.SetItem(new PyString("implementation"), new PyString("Environment.TickCount64"));
                    info.SetItem(new PyString("monotonic"), PyBool.True);
                    info.SetItem(new PyString("resolution"), new PyFloat(0.001)); // 1ms
                    info.SetItem(new PyString("adjustable"), PyBool.False);
                    break;

                case "perf_counter":
                    info.SetItem(new PyString("implementation"), new PyString("Stopwatch"));
                    info.SetItem(new PyString("monotonic"), PyBool.True);
                    info.SetItem(new PyString("resolution"), new PyFloat(1.0 / Stopwatch.Frequency));
                    info.SetItem(new PyString("adjustable"), PyBool.False);
                    break;

                case "process_time":
                case "thread_time":
                    info.SetItem(new PyString("implementation"), new PyString("Process.TotalProcessorTime"));
                    info.SetItem(new PyString("monotonic"), PyBool.True);
                    info.SetItem(new PyString("resolution"), new PyFloat(1.0 / TimeSpan.TicksPerSecond));
                    info.SetItem(new PyString("adjustable"), PyBool.False);
                    break;

                default:
                    throw PyValueError.Create($"unknown clock: {name}");
            }

            return info;
        }
    }
}
