using System;
using System.Diagnostics;
using System.Numerics;

namespace SharpPy
{
    /// <summary>_PyTime_round_t (Include/cpython/pytime.h).</summary>
    internal enum PyTimeRound
    {
        Floor = 0,
        Ceiling = 1,
        HalfEven = 2,
        Up = 3,
        Timeout = Up,
    }

    /// <summary>_Py_clock_info_t: what get_clock_info reports about a clock.</summary>
    internal struct PyClockInfo
    {
        internal string Implementation;
        internal bool Monotonic;
        internal bool Adjustable;
        internal double Resolution;
    }

    /// <summary>
    /// Python/pytime.c: a _PyTime_t is a count of nanoseconds in an int64, and every conversion
    /// to and from Python objects, C structures and clocks goes through these functions with an
    /// explicit rounding mode. time_t is 64 bits; on Windows the tv_sec of a timeval is a C long.
    /// </summary>
    internal static class PyTime
    {
        internal const long Min = long.MinValue;
        internal const long Max = long.MaxValue;

        private const long SecToMs = 1000;
        private const long MsToUs = 1000;
        private const long SecToUs = SecToMs * MsToUs;
        private const long UsToNs = 1000;
        private const long MsToNs = MsToUs * UsToNs;
        private const long SecToNs = SecToMs * MsToNs;
        private const long NsToMs = 1000 * 1000;
        private const long NsToUs = 1000;
        private const long NsTo100Ns = 100;

        private static readonly bool Windows = OperatingSystem.IsWindows();

        internal static Exception TimeTOverflow() => PyOverflowError.Create("timestamp out of range for platform time_t");

        private static Exception Overflow() => PyOverflowError.Create("timestamp too large to convert to C _PyTime_t");

        // pytime_add: t1 + t2, clamped, -1 on overflow.
        private static int AddInPlace(ref long t1, long t2)
        {
            if (t2 > 0 && t1 > Max - t2) { t1 = Max; return -1; }
            if (t2 < 0 && t1 < Min - t2) { t1 = Min; return -1; }
            t1 += t2;
            return 0;
        }

        internal static long Add(long t1, long t2)
        {
            AddInPlace(ref t1, t2);
            return t1;
        }

        private static bool MulCheckOverflow(long a, long b) => b != 0 && (a < Min / b || Max / b < a);

        // pytime_mul: t * k for k >= 0, clamped, -1 on overflow.
        private static int MulInPlace(ref long t, long k)
        {
            if (MulCheckOverflow(t, k))
            {
                t = t >= 0 ? Max : Min;
                return -1;
            }
            t *= k;
            return 0;
        }

        private static long Mul(long t, long k)
        {
            MulInPlace(ref t, k);
            return t;
        }

        // _PyTime_MulDiv: (ticks * mul / div) in two parts to keep clear of overflow.
        internal static long MulDiv(long ticks, long mul, long div)
        {
            long intpart = ticks / div;
            ticks %= div;
            long remaining = Mul(ticks, mul) / div;
            return Add(Mul(intpart, mul), remaining);
        }

        // PyLong_AsLongLong: __index__ for what is not an int, OverflowError past 64 bits.
        private static long AsLongLong(PyObject obj, Func<Exception> overflow)
        {
            var index = PyObject.TryGetIndex(obj)
                ?? throw PyTypeError.Create($"'{obj.GetTypeName()}' object cannot be interpreted as an integer");
            if (index.Value < long.MinValue || index.Value > long.MaxValue)
                throw overflow();
            return (long)index.Value;
        }

        // _PyLong_AsTime_t
        internal static long LongAsTimeT(PyObject obj) => AsLongLong(obj, TimeTOverflow);

        // round() of C: half away from zero.
        private static double CRound(double x) => Math.Round(x, MidpointRounding.AwayFromZero);

        // pytime_round_half_even
        private static double RoundHalfEven(double x)
        {
            double rounded = CRound(x);
            if (Math.Abs(x - rounded) == 0.5)
                rounded = 2.0 * CRound(x / 2.0);
            return rounded;
        }

        // pytime_round
        private static double Round(double x, PyTimeRound round) => round switch
        {
            PyTimeRound.HalfEven => RoundHalfEven(x),
            PyTimeRound.Ceiling => Math.Ceiling(x),
            PyTimeRound.Floor => Math.Floor(x),
            _ => x >= 0.0 ? Math.Ceiling(x) : Math.Floor(x),
        };

        // The range test of pytime_double_to_denominator: true for a NaN too.
        private static bool OutOfInt64(double d) => !((double)long.MinValue <= d && d < -(double)long.MinValue);

        // pytime_double_to_denominator
        private static (long Sec, long Numerator) DoubleToDenominator(double d, long idenominator, PyTimeRound round)
        {
            double denominator = idenominator;
            double intpart = Math.Truncate(d);
            double floatpart = d - intpart;
            if (double.IsInfinity(d))
                floatpart = 0.0;
            floatpart *= denominator;
            floatpart = Round(floatpart, round);
            if (floatpart >= denominator)
            {
                floatpart -= denominator;
                intpart += 1.0;
            }
            else if (floatpart < 0)
            {
                floatpart += denominator;
                intpart -= 1.0;
            }
            if (OutOfInt64(intpart))
                throw TimeTOverflow();
            return ((long)intpart, (long)floatpart);
        }

        private static Exception NaN() => PyValueError.Create("Invalid value NaN (not a number)");

        // pytime_object_to_denominator
        private static (long Sec, long Numerator) ObjectToDenominator(PyObject obj, long denominator, PyTimeRound round)
        {
            if (PyFloat.TryFloatSelf(obj, out var number))
            {
                double d = number.Value;
                if (double.IsNaN(d))
                    throw NaN();
                return DoubleToDenominator(d, denominator, round);
            }
            return (LongAsTimeT(obj), 0);
        }

        // _PyTime_ObjectToTime_t
        internal static long ObjectToTimeT(PyObject obj, PyTimeRound round)
        {
            if (PyFloat.TryFloatSelf(obj, out var number))
            {
                double d = number.Value;
                if (double.IsNaN(d))
                    throw NaN();
                d = Round(d, round);
                double intpart = Math.Truncate(d);
                if (OutOfInt64(intpart))
                    throw TimeTOverflow();
                return (long)intpart;
            }
            return LongAsTimeT(obj);
        }

        // _PyTime_ObjectToTimespec
        internal static (long Sec, long Nsec) ObjectToTimespec(PyObject obj, PyTimeRound round) =>
            ObjectToDenominator(obj, SecToNs, round);

        // _PyTime_ObjectToTimeval
        internal static (long Sec, long Usec) ObjectToTimeval(PyObject obj, PyTimeRound round) =>
            ObjectToDenominator(obj, SecToUs, round);

        // _PyTime_FromSeconds: an int never overflows.
        internal static long FromSeconds(int seconds) => seconds * SecToNs;

        internal static long FromNanoseconds(long ns) => ns;

        // _PyTime_FromNanosecondsObject: an int and nothing else.
        internal static long FromNanosecondsObject(PyObject obj)
        {
            if (!PyInt.LongCheck(obj))
                throw PyTypeError.Create($"expect int, got {obj.GetTypeName()}");
            return AsLongLong(obj, Overflow);
        }

        // pytime_from_double
        private static long FromDouble(double value, PyTimeRound round, long unitToNs)
        {
            double d = value * unitToNs;
            d = Round(d, round);
            if (OutOfInt64(d))
                throw TimeTOverflow();
            return (long)d;
        }

        // pytime_from_object
        private static long FromObject(PyObject obj, PyTimeRound round, long unitToNs)
        {
            if (PyFloat.TryFloatSelf(obj, out var number))
            {
                double d = number.Value;
                if (double.IsNaN(d))
                    throw NaN();
                return FromDouble(d, round, unitToNs);
            }
            long ns = AsLongLong(obj, Overflow);
            if (MulInPlace(ref ns, unitToNs) < 0)
                throw Overflow();
            return ns;
        }

        // _PyTime_FromSecondsObject
        internal static long FromSecondsObject(PyObject obj, PyTimeRound round) => FromObject(obj, round, SecToNs);

        // _PyTime_FromMillisecondsObject

        // _PyTime_AsSecondsDouble: whole seconds are divided as integers, 1e-9 being inexact.
        internal static double AsSecondsDouble(long t)
        {
            if (t % SecToNs == 0)
                return (double)(t / SecToNs);
            return t / 1e9;
        }

        // _PyTime_AsNanosecondsObject
        internal static PyObject AsNanosecondsObject(long t) => PyInt.From(t);

        // pytime_divide_round_up
        private static long DivideRoundUp(long t, long k)
        {
            long q = t / k;
            if (t % k != 0)
                q += t >= 0 ? 1 : -1;
            return q;
        }

        // pytime_divide
        private static long Divide(long t, long k, PyTimeRound round)
        {
            switch (round)
            {
                case PyTimeRound.HalfEven:
                {
                    long x = t / k;
                    long r = t % k;
                    long absR = Math.Abs(r);
                    if (absR > k / 2 || (absR == k / 2 && (Math.Abs(x) & 1) != 0))
                        x += t >= 0 ? 1 : -1;
                    return x;
                }
                case PyTimeRound.Ceiling:
                    return t >= 0 ? DivideRoundUp(t, k) : t / k;
                case PyTimeRound.Floor:
                    return t >= 0 ? t / k : DivideRoundUp(t, k);
                default:
                    return DivideRoundUp(t, k);
            }
        }

        // pytime_divmod: a remainder in [0, k).
        private static int DivMod(long t, long k, out long q, out long r)
        {
            q = t / k;
            r = t % k;
            if (r < 0)
            {
                if (q == Min)
                {
                    q = Min;
                    r = 0;
                    return -1;
                }
                r += k;
                q -= 1;
            }
            return 0;
        }

        internal static long As100Nanoseconds(long t, PyTimeRound round) => Divide(t, NsTo100Ns, round);

        internal static long AsMicroseconds(long t, PyTimeRound round) => Divide(t, NsToUs, round);

        internal static long AsMilliseconds(long t, PyTimeRound round) => Divide(t, NsToMs, round);

        // pytime_as_timeval
        private static int AsTimevalParts(long t, out long sec, out int usec, PyTimeRound round)
        {
            long us = Divide(t, UsToNs, round);
            int res = DivMod(us, SecToUs, out sec, out long rest);
            usec = (int)rest;
            return res;
        }

        // pytime_as_timeval_struct: tv_sec is a C long on Windows and a time_t elsewhere.
        private static (long Sec, long Usec) AsTimevalStruct(long t, PyTimeRound round, bool raise)
        {
            int res = AsTimevalParts(t, out long sec, out int usec, round);
            int res2 = 0;
            if (Windows)
            {
                if (sec > int.MaxValue) { sec = int.MaxValue; res2 = -1; }
                else if (sec < int.MinValue) { sec = int.MinValue; res2 = -1; }
            }
            if (res2 < 0)
                usec = 0;
            if (raise && (res < 0 || res2 < 0))
                throw TimeTOverflow();
            return (sec, usec);
        }

        internal static (long Sec, long Usec) AsTimeval(long t, PyTimeRound round) => AsTimevalStruct(t, round, true);

        internal static (long Sec, long Usec) AsTimevalClamp(long t, PyTimeRound round) => AsTimevalStruct(t, round, false);

        // pytime_as_timespec (only where clock_gettime is)
        internal static (long Sec, long Nsec) AsTimespec(long t, bool raise)
        {
            int res = DivMod(t, SecToNs, out long sec, out long nsec);
            if (raise && res < 0)
                throw TimeTOverflow();
            return (sec, nsec);
        }

        // ----- clocks -----

        private const long UnixEpochTicks = 621355968000000000L;

        // py_get_system_clock
        internal static long GetSystemClock(ref PyClockInfo info, bool wantInfo)
        {
            long ns = (DateTime.UtcNow.Ticks - UnixEpochTicks) * 100;
            if (wantInfo)
            {
                // DateTime.UtcNow reads GetSystemTimePreciseAsFileTime on Windows and
                // clock_gettime(CLOCK_REALTIME) elsewhere, in 100 ns ticks.
                info.Implementation = Windows ? "GetSystemTimePreciseAsFileTime()" : "clock_gettime(CLOCK_REALTIME)";
                info.Monotonic = false;
                info.Resolution = 1e-7;
                info.Adjustable = true;
            }
            return ns;
        }

        internal static long GetSystemClock()
        {
            var info = default(PyClockInfo);
            return GetSystemClock(ref info, false);
        }

        // py_get_monotonic_clock: GetTickCount64 on Windows, CLOCK_MONOTONIC elsewhere.
        internal static long GetMonotonicClock(ref PyClockInfo info, bool wantInfo)
        {
            if (Windows)
            {
                long t = Environment.TickCount64;
                if (MulInPlace(ref t, MsToNs) < 0)
                    throw Overflow();
                if (wantInfo)
                {
                    info.Implementation = "GetTickCount64()";
                    info.Monotonic = true;
                    // GetSystemTimeAdjustment's increment: the 64 Hz clock interrupt.
                    info.Resolution = 0.015625;
                    info.Adjustable = false;
                }
                return t;
            }
            long ns = MulDiv(Stopwatch.GetTimestamp(), SecToNs, Stopwatch.Frequency);
            if (wantInfo)
            {
                info.Implementation = "clock_gettime(CLOCK_MONOTONIC)";
                info.Monotonic = true;
                info.Resolution = 1e-9;
                info.Adjustable = false;
            }
            return ns;
        }

        internal static long GetMonotonicClock()
        {
            var info = default(PyClockInfo);
            return GetMonotonicClock(ref info, false);
        }

        // py_get_win_perf_counter, or the monotonic clock elsewhere.
        internal static long GetPerfCounter(ref PyClockInfo info, bool wantInfo)
        {
            if (!Windows)
                return GetMonotonicClock(ref info, wantInfo);
            if (wantInfo)
            {
                info.Implementation = "QueryPerformanceCounter()";
                info.Resolution = 1.0 / Stopwatch.Frequency;
                info.Monotonic = true;
                info.Adjustable = false;
            }
            return MulDiv(Stopwatch.GetTimestamp(), SecToNs, Stopwatch.Frequency);
        }

        internal static long GetPerfCounter()
        {
            var info = default(PyClockInfo);
            return GetPerfCounter(ref info, false);
        }

        // _PyTime_GetProcessTimeWithInfo: kernel plus user time, in 100 ns units.
        internal static long GetProcessTime(ref PyClockInfo info, bool wantInfo)
        {
            if (wantInfo)
            {
                info.Implementation = Windows ? "GetProcessTimes()" : "clock_gettime(CLOCK_PROCESS_CPUTIME_ID)";
                info.Resolution = Windows ? 1e-7 : 1e-9;
                info.Monotonic = true;
                info.Adjustable = false;
            }
            using var process = Process.GetCurrentProcess();
            return process.TotalProcessorTime.Ticks * 100;
        }

        // _PyTime_GetThreadTimeWithInfo: the CPU time of the calling OS thread.
        internal static long GetThreadTime(ref PyClockInfo info, bool wantInfo)
        {
            if (wantInfo)
            {
                info.Implementation = Windows ? "GetThreadTimes()" : "clock_gettime(CLOCK_THREAD_CPUTIME_ID)";
                info.Resolution = Windows ? 1e-7 : 1e-9;
                info.Monotonic = true;
                info.Adjustable = false;
            }
#pragma warning disable CS0618 // the OS thread id, which ProcessThread.Id is
            int id = AppDomain.GetCurrentThreadId();
#pragma warning restore CS0618
            using var process = Process.GetCurrentProcess();
            foreach (ProcessThread thread in process.Threads)
            {
                using (thread)
                    if (thread.Id == id)
                        return thread.TotalProcessorTime.Ticks * 100;
            }
            return process.TotalProcessorTime.Ticks * 100;
        }
    }
}
