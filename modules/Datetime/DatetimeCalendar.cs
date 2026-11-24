using System;

namespace SharpPy.Modules.Datetime
{
    /// <summary>
    /// Modules/_datetimemodule.c (CPython 3.12.15): the math utilities, the calendrical helpers, the
    /// range checkers and the normalization utilities (lines 144-648). Each method is the C function
    /// of the comment above it; "out" parameters are the C pointers, a thrown exception is the C
    /// "return -1 with an exception set".
    /// </summary>
    internal static partial class DatetimeModule
    {
        internal const int MINYEAR = 1;
        internal const int MAXYEAR = 9999;
        internal const int MAXORDINAL = 3652059; // date(9999,12,31).toordinal()
        internal const int MAX_DELTA_DAYS = 999999999;

        // MONTH_IS_SANE
        private static bool MonthIsSane(int m) => (uint)m - 1 < 12;

        // divmod: the floor of x/y; y > 0.
        internal static int Divmod(int x, int y, out int r)
        {
            int quo = x / y;
            r = x - quo * y;
            if (r < 0)
            {
                --quo;
                r += y;
            }
            return quo;
        }

        // divide_nearest: _PyLong_DivmodNear(m, n)[0], halves to even.
        internal static PyObject DivideNearest(PyObject m, PyObject n)
        {
            var a = ((PyInt)m).Value;
            var b = ((PyInt)n).Value;
            return PyInt.From(DivmodNear(a, b, out _));
        }

        // _PyLong_DivmodNear (Objects/longobject.c): the quotient rounded half to even, and the
        // remainder of that quotient.
        internal static System.Numerics.BigInteger DivmodNear(System.Numerics.BigInteger a, System.Numerics.BigInteger b,
            out System.Numerics.BigInteger rem)
        {
            if (b.IsZero)
                throw PyZeroDivisionError.Create("integer division or modulo by zero");
            var quo = System.Numerics.BigInteger.DivRem(a, b, out rem);
            // floor division first
            if (!rem.IsZero && (rem.Sign < 0) != (b.Sign < 0))
            {
                quo -= 1;
                rem += b;
            }
            // compare 2*rem with b; round up when greater, or equal and quo odd
            var twice = rem * 2;
            int cmp = b.Sign < 0 ? -twice.CompareTo(b) : twice.CompareTo(b);
            bool quoIsOdd = !quo.IsEven;
            if (cmp > 0 || (cmp == 0 && quoIsOdd))
            {
                quo += 1;
                rem -= b;
            }
            return quo;
        }

        private static readonly int[] _days_in_month = { 0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        private static readonly int[] _days_before_month = { 0, 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };

        // is_leap
        internal static bool IsLeap(int year)
        {
            uint ayear = (uint)year;
            return ayear % 4 == 0 && (ayear % 100 != 0 || ayear % 400 == 0);
        }

        // days_in_month
        internal static int DaysInMonth(int year, int month) =>
            month == 2 && IsLeap(year) ? 29 : _days_in_month[month];

        // days_before_month
        internal static int DaysBeforeMonth(int year, int month)
        {
            int days = _days_before_month[month];
            if (month > 2 && IsLeap(year))
                ++days;
            return days;
        }

        // days_before_year
        internal static int DaysBeforeYear(int year)
        {
            int y = year - 1;
            return y * 365 + y / 4 - y / 100 + y / 400;
        }

        private const int DI4Y = 1461;
        private const int DI100Y = 36524;
        private const int DI400Y = 146097;

        // ord_to_ymd
        internal static void OrdToYmd(int ordinal, out int year, out int month, out int day)
        {
            --ordinal;
            int n400 = ordinal / DI400Y;
            int n = ordinal % DI400Y;
            year = n400 * 400 + 1;
            int n100 = n / DI100Y;
            n = n % DI100Y;
            int n4 = n / DI4Y;
            n = n % DI4Y;
            int n1 = n / 365;
            n = n % 365;
            year += n100 * 100 + n4 * 4 + n1;
            if (n1 == 4 || n100 == 4)
            {
                year -= 1;
                month = 12;
                day = 31;
                return;
            }
            bool leapyear = n1 == 3 && (n4 != 24 || n100 == 3);
            month = (n + 50) >> 5;
            int preceding = _days_before_month[month] + (month > 2 && leapyear ? 1 : 0);
            if (preceding > n)
            {
                month -= 1;
                preceding -= DaysInMonth(year, month);
            }
            n -= preceding;
            day = n + 1;
        }

        // ymd_to_ord
        internal static int YmdToOrd(int year, int month, int day) =>
            DaysBeforeYear(year) + DaysBeforeMonth(year, month) + day;

        // weekday: Monday is 0.
        internal static int Weekday(int year, int month, int day) => (YmdToOrd(year, month, day) + 6) % 7;

        // iso_week1_monday
        internal static int IsoWeek1Monday(int year)
        {
            int first_day = YmdToOrd(year, 1, 1);
            int first_weekday = (first_day + 6) % 7;
            int week1_monday = first_day - first_weekday;
            if (first_weekday > 3)
                week1_monday += 7;
            return week1_monday;
        }

        // iso_to_ymd
        internal static int IsoToYmd(int iso_year, int iso_week, int iso_day, out int year, out int month, out int day)
        {
            year = month = day = 0;
            if (iso_year < MINYEAR || iso_year > MAXYEAR)
                return -4;
            if (iso_week <= 0 || iso_week >= 53)
            {
                bool out_of_range = true;
                if (iso_week == 53)
                {
                    int first_weekday = Weekday(iso_year, 1, 1);
                    if (first_weekday == 3 || (first_weekday == 2 && IsLeap(iso_year)))
                        out_of_range = false;
                }
                if (out_of_range)
                    return -2;
            }
            if (iso_day <= 0 || iso_day >= 8)
                return -3;
            int day_1 = IsoWeek1Monday(iso_year);
            int day_offset = (iso_week - 1) * 7 + iso_day - 1;
            OrdToYmd(day_1 + day_offset, out year, out month, out day);
            return 0;
        }

        // check_delta_day_range
        internal static void CheckDeltaDayRange(int days)
        {
            if (-MAX_DELTA_DAYS <= days && days <= MAX_DELTA_DAYS)
                return;
            throw PyOverflowError.Create($"days={days}; must have magnitude <= {MAX_DELTA_DAYS}");
        }

        // check_date_args
        internal static void CheckDateArgs(int year, int month, int day)
        {
            if (year < MINYEAR || year > MAXYEAR)
                throw PyValueError.Create($"year {year} is out of range");
            if (month < 1 || month > 12)
                throw PyValueError.Create("month must be in 1..12");
            if (day < 1 || day > DaysInMonth(year, month))
                throw PyValueError.Create("day is out of range for month");
        }

        // check_time_args
        internal static void CheckTimeArgs(int h, int m, int s, int us, int fold)
        {
            if (h < 0 || h > 23)
                throw PyValueError.Create("hour must be in 0..23");
            if (m < 0 || m > 59)
                throw PyValueError.Create("minute must be in 0..59");
            if (s < 0 || s > 59)
                throw PyValueError.Create("second must be in 0..59");
            if (us < 0 || us > 999999)
                throw PyValueError.Create("microsecond must be in 0..999999");
            if (fold != 0 && fold != 1)
                throw PyValueError.Create("fold must be either 0 or 1");
        }

        // normalize_pair
        internal static void NormalizePair(ref int hi, ref int lo, int factor)
        {
            if (lo < 0 || lo >= factor)
            {
                int num_hi = Divmod(lo, factor, out lo);
                hi = hi + num_hi;
            }
        }

        // normalize_d_s_us
        internal static void NormalizeDSUs(ref int d, ref int s, ref int us)
        {
            if (us < 0 || us >= 1000000)
                NormalizePair(ref s, ref us, 1000000);
            if (s < 0 || s >= 24 * 3600)
                NormalizePair(ref d, ref s, 24 * 3600);
        }

        // normalize_y_m_d
        internal static void NormalizeYMD(ref int y, ref int m, ref int d)
        {
            int dim = DaysInMonth(y, m);
            if (d < 1 || d > dim)
            {
                if (d == 0)
                {
                    --m;
                    if (m > 0)
                        d = DaysInMonth(y, m);
                    else
                    {
                        --y;
                        m = 12;
                        d = 31;
                    }
                }
                else if (d == dim + 1)
                {
                    ++m;
                    d = 1;
                    if (m > 12)
                    {
                        m = 1;
                        ++y;
                    }
                }
                else
                {
                    int ordinal = YmdToOrd(y, m, 1) + d - 1;
                    if (ordinal < 1 || ordinal > MAXORDINAL)
                        throw PyOverflowError.Create("date value out of range");
                    OrdToYmd(ordinal, out y, out m, out d);
                    return;
                }
            }
            if (MINYEAR <= y && y <= MAXYEAR)
                return;
            throw PyOverflowError.Create("date value out of range");
        }

        // normalize_date
        internal static void NormalizeDate(ref int year, ref int month, ref int day) =>
            NormalizeYMD(ref year, ref month, ref day);

        // normalize_datetime
        internal static void NormalizeDatetime(ref int year, ref int month, ref int day,
            ref int hour, ref int minute, ref int second, ref int microsecond)
        {
            NormalizePair(ref second, ref microsecond, 1000000);
            NormalizePair(ref minute, ref second, 60);
            NormalizePair(ref hour, ref minute, 60);
            NormalizePair(ref day, ref hour, 24);
            NormalizeDate(ref year, ref month, ref day);
        }
    }
}
