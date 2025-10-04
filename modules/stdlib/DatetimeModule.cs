using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SharpPy.Modules.Stdlib
{
    /// <summary>
    /// Python datetime 모듈 구현 - 날짜/시간 처리
    /// System.DateTime과 System.TimeSpan을 활용한 고성능 구현
    /// </summary>
    public static class DatetimeModule
    {
        public static PyModule CreateDatetimeModule()
        {
            var module = new PyModule("datetime", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\datetime.py");

            // 핵심 클래스들
            module.ModuleDict["datetime"] = new PyDateTimeType("datetime");
            module.ModuleDict["date"] = new PyDateType("date");
            module.ModuleDict["time"] = new PyTimeType("time");
            module.ModuleDict["timedelta"] = new PyTimeDeltaType("timedelta");
            module.ModuleDict["timezone"] = new PyTimeZoneType("timezone");

            // 상수들
            module.ModuleDict["MINYEAR"] = new PyInt(1);
            module.ModuleDict["MAXYEAR"] = new PyInt(9999);

            // UTC 타임존
            module.ModuleDict["UTC"] = new PyTimeZone(TimeZoneInfo.Utc);

            return module;
        }
    }

    #region DateTime 클래스

    /// <summary>
    /// Python datetime.datetime 클래스
    /// </summary>
    public class PyDateTime : PyObject
    {
        private readonly DateTime _dateTime;
        private readonly TimeZoneInfo _timeZone;

        public PyDateTime(DateTime dateTime, TimeZoneInfo timeZone = null)
        {
            _dateTime = dateTime;
            _timeZone = timeZone ?? TimeZoneInfo.Local;
        }

        public PyDateTime(int year, int month, int day, int hour = 0, int minute = 0, int second = 0, int microsecond = 0, TimeZoneInfo tzinfo = null)
        {
            try
            {
                var ms = microsecond / 1000.0;
                _dateTime = new DateTime(year, month, day, hour, minute, second, (int)ms);
                _timeZone = tzinfo ?? TimeZoneInfo.Local;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw PyValueError.Create($"Invalid datetime values: {ex.Message}");
            }
        }

        public DateTime Value => _dateTime;
        public TimeZoneInfo TimeZone => _timeZone;

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "datetime";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "year":
                    return new PyInt(_dateTime.Year);
                case "month":
                    return new PyInt(_dateTime.Month);
                case "day":
                    return new PyInt(_dateTime.Day);
                case "hour":
                    return new PyInt(_dateTime.Hour);
                case "minute":
                    return new PyInt(_dateTime.Minute);
                case "second":
                    return new PyInt(_dateTime.Second);
                case "microsecond":
                    return new PyInt(_dateTime.Millisecond * 1000);
                case "tzinfo":
                    return _timeZone != null ? new PyTimeZone(_timeZone) : PyNone.Instance;
                
                // 메서드들
                case "now":
                    return new PyBuiltinFunction("now", Now);
                case "today":
                    return new PyBuiltinFunction("today", Today);
                case "utcnow":
                    return new PyBuiltinFunction("utcnow", UtcNow);
                case "strftime":
                    return new PyBuiltinFunction("strftime", StrfTime);
                case "strptime":
                    return new PyBuiltinFunction("strptime", StrpTime);
                case "isoformat":
                    return new PyBuiltinFunction("isoformat", IsoFormat);
                case "date":
                    return new PyBuiltinFunction("date", GetDate);
                case "time":
                    return new PyBuiltinFunction("time", GetTime);
                case "replace":
                    return new PyBuiltinFunction("replace", Replace);
                case "weekday":
                    return new PyBuiltinFunction("weekday", Weekday);
                case "isoweekday":
                    return new PyBuiltinFunction("isoweekday", IsoWeekday);
                
                default:
                    return base.GetAttribute(name);
            }
        }

        #region 정적 메서드들

        private PyObject Now(PyObject[] args)
        {
            var tzinfo = args.Length > 0 && args[0] is PyTimeZone tz ? tz.TimeZoneInfo : TimeZoneInfo.Local;
            var now = TimeZoneInfo.ConvertTime(DateTime.Now, tzinfo);
            return new PyDateTime(now, tzinfo);
        }

        private PyObject Today(PyObject[] args)
        {
            var today = DateTime.Today;
            return new PyDateTime(today);
        }

        private PyObject UtcNow(PyObject[] args)
        {
            var utcNow = DateTime.UtcNow;
            return new PyDateTime(utcNow, TimeZoneInfo.Utc);
        }

        #endregion

        #region 인스턴스 메서드들

        private PyObject StrfTime(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("strftime() missing 1 required positional argument: 'fmt'");

            var format = args[0].ToStr();
            try
            {
                // Python strftime format을 .NET format으로 변환
                var dotnetFormat = ConvertPythonFormatToDotNet(format);
                var result = _dateTime.ToString(dotnetFormat, CultureInfo.InvariantCulture);
                return new PyString(result);
            }
            catch (Exception ex)
            {
                throw PyValueError.Create($"Invalid format string: {ex.Message}");
            }
        }

        private PyObject StrpTime(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("strptime() missing required arguments");

            var dateString = args[0].ToStr();
            var format = args[1].ToStr();

            try
            {
                var dotnetFormat = ConvertPythonFormatToDotNet(format);
                var parsed = DateTime.ParseExact(dateString, dotnetFormat, CultureInfo.InvariantCulture);
                return new PyDateTime(parsed);
            }
            catch (Exception ex)
            {
                throw PyValueError.Create($"time data '{dateString}' does not match format '{format}': {ex.Message}");
            }
        }

        private PyObject IsoFormat(PyObject[] args)
        {
            var sep = args.Length > 0 ? args[0].ToStr() : "T";
            var result = _dateTime.ToString($"yyyy-MM-dd{sep}HH:mm:ss.ffffff");
            return new PyString(result);
        }

        private PyObject GetDate(PyObject[] args)
        {
            return new PyDate(_dateTime.Year, _dateTime.Month, _dateTime.Day);
        }

        private PyObject GetTime(PyObject[] args)
        {
            return new PyTime(_dateTime.Hour, _dateTime.Minute, _dateTime.Second, _dateTime.Millisecond * 1000);
        }

        private PyObject Replace(PyObject[] args)
        {
            var year = _dateTime.Year;
            var month = _dateTime.Month;
            var day = _dateTime.Day;
            var hour = _dateTime.Hour;
            var minute = _dateTime.Minute;
            var second = _dateTime.Second;
            var microsecond = _dateTime.Millisecond * 1000;
            var tzinfo = _timeZone;

            // 키워드 인자는 간단화된 순서로 처리
            if (args.Length > 0 && args[0] is PyInt yearArg) year = (int)yearArg.Value;
            if (args.Length > 1 && args[1] is PyInt monthArg) month = (int)monthArg.Value;
            if (args.Length > 2 && args[2] is PyInt dayArg) day = (int)dayArg.Value;
            if (args.Length > 3 && args[3] is PyInt hourArg) hour = (int)hourArg.Value;
            if (args.Length > 4 && args[4] is PyInt minuteArg) minute = (int)minuteArg.Value;
            if (args.Length > 5 && args[5] is PyInt secondArg) second = (int)secondArg.Value;
            if (args.Length > 6 && args[6] is PyInt microsecondArg) microsecond = (int)microsecondArg.Value;

            return new PyDateTime(year, month, day, hour, minute, second, microsecond, tzinfo);
        }

        private PyObject Weekday(PyObject[] args)
        {
            // Python: Monday = 0, Sunday = 6
            var dayOfWeek = (int)_dateTime.DayOfWeek;
            return new PyInt((dayOfWeek + 6) % 7); // Convert from .NET (Sunday=0) to Python (Monday=0)
        }

        private PyObject IsoWeekday(PyObject[] args)
        {
            // ISO: Monday = 1, Sunday = 7
            var dayOfWeek = (int)_dateTime.DayOfWeek;
            return new PyInt(dayOfWeek == 0 ? 7 : dayOfWeek); // Convert Sunday from 0 to 7
        }

        #endregion

        #region 연산자 오버로딩

        public override PyObject Add(PyObject other)
        {
            if (other is PyTimeDelta delta)
            {
                var newDateTime = _dateTime.Add(delta.TimeSpan);
                return new PyDateTime(newDateTime, _timeZone);
            }
            throw PyTypeError.Create($"unsupported operand type(s) for +: 'datetime' and '{other.GetTypeName()}'");
        }

        public override PyObject Subtract(PyObject other)
        {
            if (other is PyTimeDelta delta)
            {
                var newDateTime = _dateTime.Subtract(delta.TimeSpan);
                return new PyDateTime(newDateTime, _timeZone);
            }
            else if (other is PyDateTime otherDt)
            {
                var timeDiff = _dateTime.Subtract(otherDt._dateTime);
                return new PyTimeDelta(timeDiff);
            }
            throw PyTypeError.Create($"unsupported operand type(s) for -: 'datetime' and '{other.GetTypeName()}'");
        }

        protected override PyObject PyEquals(PyObject other)
        {
            if (other is PyDateTime otherDt)
                return PyBool.FromBool(_dateTime == otherDt._dateTime);
            return PyBool.False;
        }

        protected override PyObject PyLess(PyObject other)
        {
            if (other is PyDateTime otherDt)
                return PyBool.FromBool(_dateTime < otherDt._dateTime);
            throw PyTypeError.Create($"'<' not supported between instances of 'datetime' and '{other.GetTypeName()}'");
        }

        protected override PyObject PyGreater(PyObject other)
        {
            if (other is PyDateTime otherDt)
                return PyBool.FromBool(_dateTime > otherDt._dateTime);
            throw PyTypeError.Create($"'>' not supported between instances of 'datetime' and '{other.GetTypeName()}'");
        }

        #endregion

        #region 헬퍼 메서드들

        private string ConvertPythonFormatToDotNet(string pythonFormat)
        {
            // 기본적인 Python strftime 형식을 .NET으로 변환
            var result = pythonFormat
                .Replace("%Y", "yyyy")    // 4-digit year
                .Replace("%y", "yy")      // 2-digit year
                .Replace("%m", "MM")      // Month as number
                .Replace("%d", "dd")      // Day of month
                .Replace("%H", "HH")      // Hour (24-hour)
                .Replace("%I", "hh")      // Hour (12-hour)
                .Replace("%M", "mm")      // Minute
                .Replace("%S", "ss")      // Second
                .Replace("%f", "ffffff")  // Microsecond
                .Replace("%p", "tt")      // AM/PM
                .Replace("%A", "dddd")    // Full weekday name
                .Replace("%a", "ddd")     // Abbreviated weekday name
                .Replace("%B", "MMMM")    // Full month name
                .Replace("%b", "MMM");    // Abbreviated month name

            return result;
        }

        #endregion

        public override string ToString()
        {
            return _dateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
        }
    }

    #endregion

    #region Date 클래스

    /// <summary>
    /// Python datetime.date 클래스
    /// </summary>
    public class PyDate : PyObject
    {
        private readonly DateTime _date;

        public PyDate(int year, int month, int day)
        {
            try
            {
                _date = new DateTime(year, month, day);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw PyValueError.Create($"Invalid date values: {ex.Message}");
            }
        }

        public PyDate(DateTime date)
        {
            _date = date.Date;
        }

        public DateTime Value => _date;

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "date";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "year":
                    return new PyInt(_date.Year);
                case "month":
                    return new PyInt(_date.Month);
                case "day":
                    return new PyInt(_date.Day);
                case "today":
                    return new PyBuiltinFunction("today", Today);
                case "strftime":
                    return new PyBuiltinFunction("strftime", StrfTime);
                case "isoformat":
                    return new PyBuiltinFunction("isoformat", IsoFormat);
                case "replace":
                    return new PyBuiltinFunction("replace", Replace);
                case "weekday":
                    return new PyBuiltinFunction("weekday", Weekday);
                case "isoweekday":
                    return new PyBuiltinFunction("isoweekday", IsoWeekday);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject Today(PyObject[] args)
        {
            return new PyDate(DateTime.Today);
        }

        private PyObject StrfTime(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("strftime() missing 1 required positional argument: 'fmt'");

            var format = args[0].ToStr();
            var dotnetFormat = ConvertPythonFormatToDotNet(format);
            var result = _date.ToString(dotnetFormat, CultureInfo.InvariantCulture);
            return new PyString(result);
        }

        private PyObject IsoFormat(PyObject[] args)
        {
            var result = _date.ToString("yyyy-MM-dd");
            return new PyString(result);
        }

        private PyObject Replace(PyObject[] args)
        {
            var year = _date.Year;
            var month = _date.Month;
            var day = _date.Day;

            if (args.Length > 0 && args[0] is PyInt yearArg) year = (int)yearArg.Value;
            if (args.Length > 1 && args[1] is PyInt monthArg) month = (int)monthArg.Value;
            if (args.Length > 2 && args[2] is PyInt dayArg) day = (int)dayArg.Value;

            return new PyDate(year, month, day);
        }

        private PyObject Weekday(PyObject[] args)
        {
            var dayOfWeek = (int)_date.DayOfWeek;
            return new PyInt((dayOfWeek + 6) % 7);
        }

        private PyObject IsoWeekday(PyObject[] args)
        {
            var dayOfWeek = (int)_date.DayOfWeek;
            return new PyInt(dayOfWeek == 0 ? 7 : dayOfWeek);
        }

        private string ConvertPythonFormatToDotNet(string pythonFormat)
        {
            return pythonFormat
                .Replace("%Y", "yyyy")
                .Replace("%y", "yy")
                .Replace("%m", "MM")
                .Replace("%d", "dd")
                .Replace("%A", "dddd")
                .Replace("%a", "ddd")
                .Replace("%B", "MMMM")
                .Replace("%b", "MMM");
        }

        public override string ToString()
        {
            return _date.ToString("yyyy-MM-dd");
        }
    }

    #endregion

    #region Time 클래스

    /// <summary>
    /// Python datetime.time 클래스
    /// </summary>
    public class PyTime : PyObject
    {
        private readonly TimeSpan _time;

        public PyTime(int hour = 0, int minute = 0, int second = 0, int microsecond = 0)
        {
            try
            {
                var ms = microsecond / 1000.0;
                _time = new TimeSpan(0, hour, minute, second, (int)ms);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw PyValueError.Create($"Invalid time values: {ex.Message}");
            }
        }

        public TimeSpan Value => _time;

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "time";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "hour":
                    return new PyInt(_time.Hours);
                case "minute":
                    return new PyInt(_time.Minutes);
                case "second":
                    return new PyInt(_time.Seconds);
                case "microsecond":
                    return new PyInt(_time.Milliseconds * 1000);
                case "strftime":
                    return new PyBuiltinFunction("strftime", StrfTime);
                case "isoformat":
                    return new PyBuiltinFunction("isoformat", IsoFormat);
                case "replace":
                    return new PyBuiltinFunction("replace", Replace);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject StrfTime(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("strftime() missing 1 required positional argument: 'fmt'");

            var format = args[0].ToStr();
            var baseDate = new DateTime(1900, 1, 1).Add(_time);
            var dotnetFormat = ConvertPythonFormatToDotNet(format);
            var result = baseDate.ToString(dotnetFormat, CultureInfo.InvariantCulture);
            return new PyString(result);
        }

        private PyObject IsoFormat(PyObject[] args)
        {
            var result = _time.ToString(@"hh\:mm\:ss\.ffffff");
            return new PyString(result);
        }

        private PyObject Replace(PyObject[] args)
        {
            var hour = _time.Hours;
            var minute = _time.Minutes;
            var second = _time.Seconds;
            var microsecond = _time.Milliseconds * 1000;

            if (args.Length > 0 && args[0] is PyInt hourArg) hour = (int)hourArg.Value;
            if (args.Length > 1 && args[1] is PyInt minuteArg) minute = (int)minuteArg.Value;
            if (args.Length > 2 && args[2] is PyInt secondArg) second = (int)secondArg.Value;
            if (args.Length > 3 && args[3] is PyInt microsecondArg) microsecond = (int)microsecondArg.Value;

            return new PyTime(hour, minute, second, microsecond);
        }

        private string ConvertPythonFormatToDotNet(string pythonFormat)
        {
            return pythonFormat
                .Replace("%H", "HH")
                .Replace("%I", "hh")
                .Replace("%M", "mm")
                .Replace("%S", "ss")
                .Replace("%f", "ffffff")
                .Replace("%p", "tt");
        }

        public override string ToString()
        {
            return _time.ToString(@"hh\:mm\:ss\.ffffff");
        }
    }

    #endregion

    #region TimeDelta 클래스

    /// <summary>
    /// Python datetime.timedelta 클래스
    /// </summary>
    public class PyTimeDelta : PyObject
    {
        private readonly TimeSpan _timeSpan;

        public PyTimeDelta(int days = 0, int seconds = 0, int microseconds = 0, int milliseconds = 0, int minutes = 0, int hours = 0, int weeks = 0)
        {
            try
            {
                _timeSpan = new TimeSpan(
                    days + (weeks * 7),
                    hours,
                    minutes,
                    seconds,
                    milliseconds + (microseconds / 1000)
                );
            }
            catch (Exception ex)
            {
                throw PyOverflowError.Create($"timedelta overflow: {ex.Message}");
            }
        }

        public PyTimeDelta(TimeSpan timeSpan)
        {
            _timeSpan = timeSpan;
        }

        public TimeSpan TimeSpan => _timeSpan;

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "timedelta";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "days":
                    return new PyInt(_timeSpan.Days);
                case "seconds":
                    return new PyInt(_timeSpan.Seconds + (_timeSpan.Minutes * 60) + (_timeSpan.Hours * 3600));
                case "microseconds":
                    return new PyInt(_timeSpan.Milliseconds * 1000);
                case "total_seconds":
                    return new PyBuiltinFunction("total_seconds", TotalSeconds);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject TotalSeconds(PyObject[] args)
        {
            return new PyFloat(_timeSpan.TotalSeconds);
        }

        public override PyObject Add(PyObject other)
        {
            if (other is PyTimeDelta otherDelta)
                return new PyTimeDelta(_timeSpan.Add(otherDelta._timeSpan));
            throw PyTypeError.Create($"unsupported operand type(s) for +: 'timedelta' and '{other.GetTypeName()}'");
        }

        public override PyObject Subtract(PyObject other)
        {
            if (other is PyTimeDelta otherDelta)
                return new PyTimeDelta(_timeSpan.Subtract(otherDelta._timeSpan));
            throw PyTypeError.Create($"unsupported operand type(s) for -: 'timedelta' and '{other.GetTypeName()}'");
        }

        public override PyObject Multiply(PyObject other)
        {
            if (other is PyInt intVal)
                return new PyTimeDelta(new TimeSpan(_timeSpan.Ticks * intVal.Value));
            else if (other is PyFloat floatVal)
                return new PyTimeDelta(new TimeSpan((long)(_timeSpan.Ticks * floatVal.Value)));
            throw PyTypeError.Create($"unsupported operand type(s) for *: 'timedelta' and '{other.GetTypeName()}'");
        }

        protected override PyObject PyEquals(PyObject other)
        {
            if (other is PyTimeDelta otherDelta)
                return PyBool.FromBool(_timeSpan == otherDelta._timeSpan);
            return PyBool.False;
        }

        public override string ToString()
        {
            if (_timeSpan.Days != 0)
            {
                var dayText = _timeSpan.Days == 1 ? "day" : "days";
                return $"{_timeSpan.Days} {dayText}, {_timeSpan:hh\\:mm\\:ss}";
            }
            else
                return _timeSpan.ToString(@"hh\:mm\:ss");
        }
    }

    #endregion

    #region TimeZone 클래스

    /// <summary>
    /// Python datetime.timezone 클래스
    /// </summary>
    public class PyTimeZone : PyObject
    {
        private readonly TimeZoneInfo _timeZoneInfo;

        public PyTimeZone(TimeZoneInfo timeZoneInfo)
        {
            _timeZoneInfo = timeZoneInfo;
        }

        public TimeZoneInfo TimeZoneInfo => _timeZoneInfo;

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "timezone";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "utcoffset":
                    return new PyBuiltinFunction("utcoffset", UtcOffset);
                case "tzname":
                    return new PyBuiltinFunction("tzname", TzName);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject UtcOffset(PyObject[] args)
        {
            var offset = _timeZoneInfo.BaseUtcOffset;
            return new PyTimeDelta(0, (int)offset.TotalSeconds);
        }

        private PyObject TzName(PyObject[] args)
        {
            return new PyString(_timeZoneInfo.StandardName);
        }

        public override string ToString()
        {
            return $"timezone({_timeZoneInfo.StandardName})";
        }
    }

    #endregion

    #region 타입 시스템

    /// <summary>
    /// datetime 클래스 타입
    /// </summary>
    public class PyDateTimeType : PyType
    {
        public PyDateTimeType(string name) : base(name, new PyType[0])
        {
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 3)
                throw PyTypeError.Create("datetime() missing required arguments");

            var year = (int)((PyInt)args[0]).Value;
            var month = (int)((PyInt)args[1]).Value;
            var day = (int)((PyInt)args[2]).Value;
            var hour = args.Length > 3 ? (int)((PyInt)args[3]).Value : 0;
            var minute = args.Length > 4 ? (int)((PyInt)args[4]).Value : 0;
            var second = args.Length > 5 ? (int)((PyInt)args[5]).Value : 0;
            var microsecond = args.Length > 6 ? (int)((PyInt)args[6]).Value : 0;

            return new PyDateTime(year, month, day, hour, minute, second, microsecond);
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "now":
                    return new PyBuiltinFunction("now", Now);
                case "today":
                    return new PyBuiltinFunction("today", Today);
                case "utcnow":
                    return new PyBuiltinFunction("utcnow", UtcNow);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject Now(PyObject[] args)
        {
            var tzinfo = args.Length > 0 && args[0] is PyTimeZone tz ? tz.TimeZoneInfo : TimeZoneInfo.Local;
            var now = TimeZoneInfo.ConvertTime(DateTime.Now, tzinfo);
            return new PyDateTime(now, tzinfo);
        }

        private PyObject Today(PyObject[] args)
        {
            return new PyDateTime(DateTime.Today);
        }

        private PyObject UtcNow(PyObject[] args)
        {
            return new PyDateTime(DateTime.UtcNow, TimeZoneInfo.Utc);
        }
    }

    /// <summary>
    /// date 클래스 타입
    /// </summary>
    public class PyDateType : PyType
    {
        public PyDateType(string name) : base(name, new PyType[0])
        {
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 3)
                throw PyTypeError.Create("date() missing required arguments");

            var year = (int)((PyInt)args[0]).Value;
            var month = (int)((PyInt)args[1]).Value;
            var day = (int)((PyInt)args[2]).Value;

            return new PyDate(year, month, day);
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "today":
                    return new PyBuiltinFunction("today", Today);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject Today(PyObject[] args)
        {
            return new PyDate(DateTime.Today);
        }
    }

    /// <summary>
    /// time 클래스 타입
    /// </summary>
    public class PyTimeType : PyType
    {
        public PyTimeType(string name) : base(name, new PyType[0])
        {
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            var hour = args.Length > 0 ? (int)((PyInt)args[0]).Value : 0;
            var minute = args.Length > 1 ? (int)((PyInt)args[1]).Value : 0;
            var second = args.Length > 2 ? (int)((PyInt)args[2]).Value : 0;
            var microsecond = args.Length > 3 ? (int)((PyInt)args[3]).Value : 0;

            return new PyTime(hour, minute, second, microsecond);
        }
    }

    /// <summary>
    /// timedelta 클래스 타입
    /// </summary>
    public class PyTimeDeltaType : PyType
    {
        public PyTimeDeltaType(string name) : base(name, new PyType[0])
        {
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            var days = 0;
            var seconds = 0;
            var microseconds = 0;
            var milliseconds = 0;
            var minutes = 0;
            var hours = 0;
            var weeks = 0;


            // CALL_FUNCTION_KW를 위한 키워드 인자 처리
            if (args.Length > 0 && args[args.Length - 1] is PyTuple kwNames)
            {
                // 키워드 인자가 있는 경우
                var numKwArgs = kwNames.Items.Length;
                var numPosArgs = args.Length - 1 - numKwArgs;
                
                // 위치 인자 처리
                for (int i = 0; i < numPosArgs && i < 7; i++)
                {
                    if (args[i] is PyInt val)
                    {
                        switch (i)
                        {
                            case 0: days = (int)val.Value; break;
                            case 1: seconds = (int)val.Value; break;
                            case 2: microseconds = (int)val.Value; break;
                            case 3: milliseconds = (int)val.Value; break;
                            case 4: minutes = (int)val.Value; break;
                            case 5: hours = (int)val.Value; break;
                            case 6: weeks = (int)val.Value; break;
                        }
                    }
                }
                
                // 키워드 인자 처리
                for (int i = 0; i < numKwArgs; i++)
                {
                    var kwName = ((PyString)kwNames.Items[i]).Value;
                    var kwValue = args[numPosArgs + i];
                    
                    if (kwValue is PyInt intVal)
                    {
                        switch (kwName)
                        {
                            case "days": days = (int)intVal.Value; break;
                            case "seconds": seconds = (int)intVal.Value; break;
                            case "microseconds": microseconds = (int)intVal.Value; break;
                            case "milliseconds": milliseconds = (int)intVal.Value; break;
                            case "minutes": minutes = (int)intVal.Value; break;
                            case "hours": hours = (int)intVal.Value; break;
                            case "weeks": weeks = (int)intVal.Value; break;
                        }
                    }
                }
            }
            else
            {
                // 순서대로 인자 처리 (위치 인자만 있는 경우)
                if (args.Length > 0 && args[0] is PyInt daysArg) days = (int)daysArg.Value;
                if (args.Length > 1 && args[1] is PyInt secondsArg) seconds = (int)secondsArg.Value;
                if (args.Length > 2 && args[2] is PyInt microsecondsArg) microseconds = (int)microsecondsArg.Value;
                if (args.Length > 3 && args[3] is PyInt millisecondsArg) milliseconds = (int)millisecondsArg.Value;
                if (args.Length > 4 && args[4] is PyInt minutesArg) minutes = (int)minutesArg.Value;
                if (args.Length > 5 && args[5] is PyInt hoursArg) hours = (int)hoursArg.Value;
                if (args.Length > 6 && args[6] is PyInt weeksArg) weeks = (int)weeksArg.Value;
            }

            return new PyTimeDelta(days, seconds, microseconds, milliseconds, minutes, hours, weeks);
        }
    }

    /// <summary>
    /// timezone 클래스 타입
    /// </summary>
    public class PyTimeZoneType : PyType
    {
        public PyTimeZoneType(string name) : base(name, new PyType[0])
        {
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
                return new PyTimeZone(TimeZoneInfo.Utc);

            // 간단화된 구현 - UTC만 지원
            return new PyTimeZone(TimeZoneInfo.Utc);
        }
    }

    #endregion
}