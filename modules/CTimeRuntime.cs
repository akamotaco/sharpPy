using System;
using System.Text;

namespace SharpPy
{
    /// <summary>struct tm of the C library: years since 1900, months from 0, Sunday is day 0.</summary>
    internal struct CTm
    {
        internal int Sec, Min, Hour, Mday, Mon, Year, Wday, Yday, Isdst;
    }

    /// <summary>
    /// The C library time functions timemodule.c calls: gmtime_s, localtime_s, mktime, strftime and
    /// the _timezone, _daylight, _dstbias and _tzname that _tzset leaves. On Windows they are the
    /// UCRT's: its time_t range ends at _MAX__TIME64_T (3001-01-19 07:59:59 UTC), localtime refuses
    /// a negative time, and daylight saving time follows the one rule GetTimeZoneInformation gives
    /// for the current year, applied to every year. Elsewhere the zone's whole history is used.
    /// </summary>
    internal static class CTimeRuntime
    {
        private static readonly bool Windows = OperatingSystem.IsWindows();

        internal const int EINVAL = 22;
        private static readonly int EOVERFLOW = OperatingSystem.IsMacOS() ? 84 : 75;

        // _MAX__TIME64_T, _MIN_LOCAL_TIME and _MAX_LOCAL_TIME of the UCRT.
        private const long MaxTime64 = 32536799999L;
        private const long MinLocalTime = -12 * 3600;
        private const long MaxLocalTime = 14 * 3600;

        /// <summary>_timezone: seconds west of UTC of standard time.</summary>
        internal static int Timezone;
        /// <summary>_daylight: whether the zone has a daylight saving rule.</summary>
        internal static int Daylight;
        /// <summary>_dstbias: what daylight time adds to _timezone, in seconds (negative).</summary>
        internal static int DstBias;
        /// <summary>_tzname: the standard and the daylight names.</summary>
        internal static string[] TzName;

        private static TimeZoneInfo.AdjustmentRule _rule;

        static CTimeRuntime() => TzSet();

        // _tzset from GetTimeZoneInformation: the rule in force today.
        internal static void TzSet()
        {
            var zone = TimeZoneInfo.Local;
            var today = DateTime.Now.Date;
            _rule = null;
            foreach (var rule in zone.GetAdjustmentRules())
                if (rule.DateStart <= today && today <= rule.DateEnd)
                    _rule = rule;
            var standard = zone.BaseUtcOffset + (_rule?.BaseUtcOffsetDelta ?? TimeSpan.Zero);
            Timezone = -(int)standard.TotalSeconds;
            if (_rule != null && _rule.DaylightDelta != TimeSpan.Zero)
            {
                Daylight = 1;
                DstBias = -(int)_rule.DaylightDelta.TotalSeconds;
            }
            else
            {
                Daylight = 0;
                DstBias = 0;
                _rule = null;
            }
            TzName = new[] { zone.StandardName, zone.DaylightName };
        }

        // ----- the proleptic Gregorian calendar over 64-bit day counts -----

        private static readonly int[] DaysBefore = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };

        internal static bool IsLeap(long year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }

        // Days from 1970-01-01 to the given date.
        private static long DaysFromCivil(long y, int m, int d)
        {
            y -= m <= 2 ? 1 : 0;
            long era = FloorDiv(y, 400);
            long yoe = y - era * 400;
            long doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
            long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468;
        }

        private static void CivilFromDays(long z, out long y, out int m, out int d)
        {
            z += 719468;
            long era = FloorDiv(z, 146097);
            long doe = z - era * 146097;
            long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
            y = yoe + era * 400;
            long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
            long mp = (5 * doy + 2) / 153;
            d = (int)(doy - (153 * mp + 2) / 5 + 1);
            m = (int)(mp < 10 ? mp + 3 : mp - 9);
            if (m <= 2)
                y++;
        }

        // The fields of a time counted from the epoch, false when the year does not fit an int.
        private static bool Breakdown(long t, out CTm tm)
        {
            tm = default;
            long days = FloorDiv(t, 86400);
            long rest = t - days * 86400;
            CivilFromDays(days, out long year, out int month, out int day);
            if (year - 1900 > int.MaxValue || year - 1900 < int.MinValue)
                return false;
            tm.Year = (int)(year - 1900);
            tm.Mon = month - 1;
            tm.Mday = day;
            tm.Hour = (int)(rest / 3600);
            tm.Min = (int)(rest / 60 % 60);
            tm.Sec = (int)(rest % 60);
            tm.Wday = (int)(((days + 4) % 7 + 7) % 7);
            tm.Yday = DaysBefore[month - 1] + (month > 2 && IsLeap(year) ? 1 : 0) + day - 1;
            return true;
        }

        /// <summary>gmtime_s: 0, or the errno it fails with.</summary>
        internal static int GmTime(long t, out CTm tm)
        {
            tm = default;
            if (Windows && (t < MinLocalTime || t > MaxTime64 + MaxLocalTime))
                return EINVAL;
            if (!Breakdown(t, out tm))
                return EOVERFLOW;
            // The UCRT counts the weekday from the days of t / 86400 truncated toward zero, so the
            // half day before the epoch it accepts is a Thursday, like the epoch.
            if (Windows && t < 0)
                tm.Wday = (int)((t / 86400 + 4) % 7);
            tm.Isdst = 0;
            return 0;
        }

        /// <summary>localtime_s: 0, or the errno it fails with.</summary>
        internal static int LocalTime(long t, out CTm tm)
        {
            tm = default;
            if (Windows)
            {
                if (t < 0 || t > MaxTime64)
                    return EINVAL;
                long local = t - Timezone;
                Breakdown(local, out tm);
                tm.Isdst = 0;
                if (Daylight != 0 && IsInDst(tm))
                {
                    Breakdown(local - DstBias, out tm);
                    tm.Isdst = 1;
                }
                return 0;
            }
            var (offset, dst) = OffsetAt(t);
            if (!Breakdown(t + offset, out tm))
                return EOVERFLOW;
            tm.Isdst = dst ? 1 : 0;
            return 0;
        }

        // The zone's offset east of UTC at a time, by its history where DateTime reaches.
        private static (long Offset, bool Dst) OffsetAt(long t)
        {
            var zone = TimeZoneInfo.Local;
            const long first = -62135596800L, last = 253402300799L;
            if (t < first || t > last)
                return ((long)zone.BaseUtcOffset.TotalSeconds, false);
            var instant = DateTimeOffset.FromUnixTimeSeconds(t);
            return ((long)zone.GetUtcOffset(instant).TotalSeconds, zone.IsDaylightSavingTime(instant));
        }

        // The day of the year (from 0) and the milliseconds into it of a transition of the rule.
        private static (int Yday, long Ms) Transition(TimeZoneInfo.TransitionTime transition, long year)
        {
            int month = transition.Month;
            int leap = month > 2 && IsLeap(year) ? 1 : 0;
            int yday;
            if (transition.IsFixedDateRule)
            {
                yday = DaysBefore[month - 1] + leap + transition.Day - 1;
            }
            else
            {
                int first = DaysBefore[month - 1] + leap;
                long firstDays = DaysFromCivil(year, month, 1);
                int firstWday = (int)(((firstDays + 4) % 7 + 7) % 7);
                yday = first + ((int)transition.DayOfWeek - firstWday + 7) % 7 + (transition.Week - 1) * 7;
                int monthDays = (month == 12 ? 365 + (IsLeap(year) ? 1 : 0) : DaysBefore[month] + (month >= 2 && IsLeap(year) ? 1 : 0)) - first;
                if (yday >= first + monthDays)
                    yday -= 7;
            }
            return (yday, (long)transition.TimeOfDay.TimeOfDay.TotalMilliseconds);
        }

        /// <summary>
        /// _isindst: whether the local standard time of tm falls in daylight time. The end of the
        /// rule is given in daylight time and moved back by the bias.
        /// </summary>
        internal static bool IsInDst(in CTm tm)
        {
            if (Daylight == 0 || _rule == null)
                return false;
            long year = tm.Year + 1900L;
            var (startYd, startMs) = Transition(_rule.DaylightTransitionStart, year);
            var (endYd, endMs) = Transition(_rule.DaylightTransitionEnd, year);
            endMs += DstBias * 1000L;
            if (endMs < 0)
            {
                endMs += 86400000L;
                endYd -= 1;
            }
            if (startYd < endYd)
            {
                if (tm.Yday < startYd || tm.Yday > endYd) return false;
                if (tm.Yday > startYd && tm.Yday < endYd) return true;
            }
            else
            {
                if (tm.Yday < endYd || tm.Yday > startYd) return true;
                if (tm.Yday > endYd && tm.Yday < startYd) return false;
            }
            long ms = 1000L * (tm.Sec + 60L * tm.Min + 3600L * tm.Hour);
            return tm.Yday == startYd ? ms >= startMs : ms < endMs;
        }

        /// <summary>
        /// mktime (the UCRT's _make_time_t): the fields are normalized, read as local time, and
        /// written back. -1 when the time is out of range, with tm untouched.
        /// </summary>
        internal static long MkTime(ref CTm tm)
        {
            long year = tm.Year;
            if (Windows && (year < 69 || year > 1101))
                return -1;
            long month = tm.Mon;
            year += month / 12;
            month %= 12;
            if (month < 0)
            {
                month += 12;
                year--;
            }
            long days = DaysFromCivil(year + 1900, (int)month + 1, 1) + (tm.Mday - 1L);
            long local = days * 86400 + tm.Hour * 3600L + tm.Min * 60L + tm.Sec;
            long t;
            if (Windows)
            {
                if (!Breakdown(local, out var wall))
                    return -1;
                t = local + Timezone;
                if (tm.Isdst > 0 || (tm.Isdst < 0 && IsInDst(wall)))
                    t += DstBias;
                if (t < 0 || t > MaxTime64)
                    return -1;
            }
            else
            {
                // The offset in force at the result, found by iterating from the standard one.
                var (offset, _) = OffsetAt(local - (long)TimeZoneInfo.Local.BaseUtcOffset.TotalSeconds);
                t = local - offset;
                var (again, dst) = OffsetAt(t);
                if (again != offset)
                    t = local - again;
                if (tm.Isdst == 0 && dst)
                    t += -DstBias;
            }
            if (LocalTime(t, out var result) != 0)
                return -1;
            tm = result;
            return t;
        }

        // ----- strftime in the "C" locale -----

        private static readonly string[] DayAbbr = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        private static readonly string[] DayFull = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        private static readonly string[] MonthAbbr = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        private static readonly string[] MonthFull =
        {
            "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
        };

        /// <summary>The errno of a strftime that fails: EINVAL is CPython's "Invalid format string".</summary>
        internal sealed class InvalidFormat : Exception
        {
        }

        // A number zero padded to width; the alternate form (#) drops the leading zeros.
        private static void Number(StringBuilder output, long value, int width, bool alternate)
        {
            string text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!alternate && text.Length < width)
                output.Append('0', width - text.Length);
            output.Append(text);
        }

        // iso_week_days: the days since the Monday that starts the ISO week 1 the date is in.
        private static int IsoWeekDays(int yday, int wday)
        {
            const int bigEnoughMultipleOf7 = (366 / 7 + 2) * 7;
            return yday - (yday - wday + 4 + bigEnoughMultipleOf7) % 7 + 4 - 1;
        }

        // The ISO 8601 year and week of tm, from its own tm_yday and tm_wday.
        private static (long Year, int Week) IsoWeek(in CTm tm)
        {
            long year = tm.Year + 1900L;
            int days = IsoWeekDays(tm.Yday, tm.Wday);
            if (days < 0)
            {
                year--;
                days = IsoWeekDays(tm.Yday + (365 + (IsLeap(year) ? 1 : 0)), tm.Wday);
            }
            else
            {
                int next = IsoWeekDays(tm.Yday - (365 + (IsLeap(year) ? 1 : 0)), tm.Wday);
                if (next >= 0)
                {
                    year++;
                    days = next;
                }
            }
            return (year, days / 7 + 1);
        }

        private static void CheckSeconds(in CTm tm)
        {
            if (tm.Sec < 0 || tm.Sec > 60)
                throw new InvalidFormat();
        }

        /// <summary>
        /// strftime of one format of ASCII characters. A directive is %, then the alternate flag #,
        /// then the E or O modifier, then its letter; anything else is EINVAL.
        /// </summary>
        internal static string StrFTime(string format, in CTm tm)
        {
            var output = new StringBuilder();
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c != '%')
                {
                    output.Append(c);
                    continue;
                }
                if (++i >= format.Length)
                    throw new InvalidFormat();
                bool alternate = false;
                if (format[i] == '#')
                {
                    alternate = true;
                    if (++i >= format.Length)
                        throw new InvalidFormat();
                }
                if (format[i] == 'E' || format[i] == 'O')
                {
                    if (++i >= format.Length)
                        throw new InvalidFormat();
                }
                Expand(output, format[i], alternate, tm);
            }
            return output.ToString();
        }

        private static void Expand(StringBuilder output, char directive, bool alternate, in CTm tm)
        {
            long year = tm.Year + 1900L;
            switch (directive)
            {
                case 'a': output.Append(DayAbbr[tm.Wday]); break;
                case 'A': output.Append(DayFull[tm.Wday]); break;
                case 'b':
                case 'h': output.Append(MonthAbbr[tm.Mon]); break;
                case 'B': output.Append(MonthFull[tm.Mon]); break;
                case 'c':
                    CheckSeconds(tm);
                    if (alternate)
                    {
                        LongDate(output, tm);
                        output.Append(' ');
                        Time(output, tm, false);
                    }
                    else
                    {
                        output.Append(DayAbbr[tm.Wday]).Append(' ').Append(MonthAbbr[tm.Mon]).Append(' ');
                        if (tm.Mday < 10)
                            output.Append(' ');
                        output.Append(tm.Mday).Append(' ');
                        Time(output, tm, false);
                        output.Append(' ');
                        Number(output, year, 4, false);
                    }
                    break;
                case 'C': Number(output, year / 100, 2, alternate); break;
                case 'd': Number(output, tm.Mday, 2, alternate); break;
                case 'D':
                    Number(output, tm.Mon + 1, 2, alternate);
                    output.Append('/');
                    Number(output, tm.Mday, 2, alternate);
                    output.Append('/');
                    Number(output, year % 100, 2, alternate);
                    break;
                case 'e':
                    if (!alternate && tm.Mday < 10)
                        output.Append(' ');
                    output.Append(tm.Mday);
                    break;
                case 'F':
                    Number(output, year, 4, alternate);
                    output.Append('-');
                    Number(output, tm.Mon + 1, 2, alternate);
                    output.Append('-');
                    Number(output, tm.Mday, 2, alternate);
                    break;
                case 'g': Number(output, ((IsoWeek(tm).Year % 100) + 100) % 100, 2, false); break;
                case 'G': Number(output, IsoWeek(tm).Year, 4, false); break;
                case 'H': Number(output, tm.Hour, 2, alternate); break;
                case 'I': Number(output, tm.Hour % 12 == 0 ? 12 : tm.Hour % 12, 2, alternate); break;
                case 'j': Number(output, tm.Yday + 1, 3, alternate); break;
                case 'm': Number(output, tm.Mon + 1, 2, alternate); break;
                case 'M': Number(output, tm.Min, 2, alternate); break;
                case 'n': output.Append('\n'); break;
                case 'p': output.Append(tm.Hour < 12 ? "AM" : "PM"); break;
                case 'r':
                    CheckSeconds(tm);
                    Number(output, tm.Hour % 12 == 0 ? 12 : tm.Hour % 12, 2, alternate);
                    output.Append(':');
                    Number(output, tm.Min, 2, alternate);
                    output.Append(':');
                    Number(output, tm.Sec, 2, alternate);
                    output.Append(' ').Append(tm.Hour < 12 ? "AM" : "PM");
                    break;
                case 'R':
                    Number(output, tm.Hour, 2, alternate);
                    output.Append(':');
                    Number(output, tm.Min, 2, alternate);
                    break;
                case 'S':
                    CheckSeconds(tm);
                    Number(output, tm.Sec, 2, alternate);
                    break;
                case 't': output.Append('\t'); break;
                case 'T':
                    CheckSeconds(tm);
                    Time(output, tm, alternate);
                    break;
                case 'u': output.Append(tm.Wday == 0 ? 7 : tm.Wday); break;
                case 'U': Number(output, (tm.Yday + 7 - tm.Wday) / 7, 2, alternate); break;
                case 'V': Number(output, IsoWeek(tm).Week, 2, alternate); break;
                case 'w': output.Append(tm.Wday); break;
                case 'W': Number(output, (tm.Yday - (tm.Wday - 1 + 7) % 7 + 7) / 7, 2, alternate); break;
                case 'x':
                    if (alternate)
                        LongDate(output, tm);
                    else
                    {
                        Number(output, tm.Mon + 1, 2, false);
                        output.Append('/');
                        Number(output, tm.Mday, 2, false);
                        output.Append('/');
                        Number(output, year % 100, 2, false);
                    }
                    break;
                case 'X':
                    CheckSeconds(tm);
                    Time(output, tm, false);
                    break;
                case 'y': Number(output, (year % 100 + 100) % 100, 2, alternate); break;
                case 'Y': Number(output, year, 4, alternate); break;
                case 'z':
                {
                    long east = -(Timezone + (tm.Isdst != 0 ? DstBias : 0));
                    output.Append(east < 0 ? '-' : '+');
                    east = Math.Abs(east) / 60;
                    Number(output, east / 60, 2, false);
                    Number(output, east % 60, 2, false);
                    break;
                }
                case 'Z': output.Append(TzName[tm.Isdst != 0 ? 1 : 0]); break;
                case '%': output.Append('%'); break;
                default: throw new InvalidFormat();
            }
        }

        // The "C" locale's long date, "dddd, MMMM dd, yyyy".
        private static void LongDate(StringBuilder output, in CTm tm)
        {
            output.Append(DayFull[tm.Wday]).Append(", ").Append(MonthFull[tm.Mon]).Append(' ');
            Number(output, tm.Mday, 2, false);
            output.Append(", ");
            Number(output, tm.Year + 1900L, 4, false);
        }

        // "HH:mm:ss"
        private static void Time(StringBuilder output, in CTm tm, bool alternate)
        {
            Number(output, tm.Hour, 2, alternate);
            output.Append(':');
            Number(output, tm.Min, 2, alternate);
            output.Append(':');
            Number(output, tm.Sec, 2, alternate);
        }
    }
}
