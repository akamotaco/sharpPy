import datetime as dt
import statistics
import sys
from decimal import ROUND_HALF_UP, Decimal, getcontext, localcontext
from fractions import Fraction

print(sys.float_info.max, sys.float_info.min, sys.float_info.epsilon, sys.float_info.dig, sys.float_info.mant_dig)

a = dt.date(2024, 2, 28)
b = dt.date(2024, 3, 1)
print(b - a, (b - a).days, a + dt.timedelta(days=2), a < b, a.isoformat(), a.weekday(), a.isoweekday(), a.toordinal())
print(a.strftime("%Y/%m/%d %a %b %j"), a.replace(day=1), repr(a), dt.date.fromisoformat("2023-01-02"))
c = dt.datetime(2024, 1, 31, 23, 59, 30, 500)
print(c, repr(c), c + dt.timedelta(seconds=45), c.date(), c.time(), c.isoformat(), c.timetuple().tm_yday)
print(c.strftime("%H:%M:%S.%f %p"), dt.datetime.strptime("2021-03-04 05:06", "%Y-%m-%d %H:%M"))
d = dt.timedelta(days=1, hours=2, minutes=3, seconds=4)
print(d, repr(d), d.total_seconds(), d * 2, d / 2, -d, d // dt.timedelta(hours=1), abs(-d), dt.timedelta(0))
u = dt.datetime(2024, 1, 1, tzinfo=dt.timezone.utc)
k = dt.timezone(dt.timedelta(hours=9), "KST")
print(u, u.astimezone(k), k, u.utcoffset(), u.tzname(), dt.timezone.utc)
print(dt.time(1, 2, 3), dt.time(1, 2).isoformat(), dt.date.min, dt.date.max, dt.timedelta.max)
print({a: 1}[dt.date(2024, 2, 28)], sorted([b, a]), max(a, b), dt.MINYEAR, dt.MAXYEAR)
try:
    dt.date(2023, 2, 30)
except ValueError:
    print("ValueError")

x = Decimal("1.10")
y = Decimal("2.20")
print(x + y, x * y, y / x, y - x, -x, abs(-y), x ** 2, repr(x), str(y), x == Decimal("1.1"), x < y, hash(Decimal(2)) == hash(2))
print(Decimal("1.005").quantize(Decimal("0.01"), rounding=ROUND_HALF_UP), round(Decimal("2.675"), 2), Decimal(1) / 3)
print(Decimal(0.5), Decimal("0.1") + Decimal("0.2"), int(Decimal("3.9")), float(Decimal("1.5")), Decimal("1E+3"), Decimal("NaN"), Decimal("Inf"))
print(f"{Decimal('3.14159'):.2f}", f"{Decimal('1234.5'):,}", sum([Decimal("0.1")] * 3), Decimal("10").sqrt())
with localcontext() as ctx:
    ctx.prec = 5
    print(Decimal(1) / 7, getcontext().prec)
print(getcontext().prec)

f = Fraction(1, 3)
g = Fraction("2/5")
print(f + g, f * g, f - g, f / g, f ** 2, repr(f), str(g), float(f), f < g, -f, abs(-g), hash(Fraction(2)) == hash(2))
print(Fraction(0.5), Fraction(3), Fraction("1.25"), Fraction(1, 3).limit_denominator(2), f.numerator, f.denominator, int(Fraction(7, 2)))
print(round(Fraction(7, 3), 1), Fraction(1, 2) + 1, 1 + Fraction(1, 2), Fraction(1, 2) + 0.5, Fraction(4, 6) == Fraction(2, 3), sum([f, f, f]))

v = [2, 4, 4, 4, 5, 5, 7, 9]
print(statistics.mean(v), statistics.median(v), statistics.mode(v), statistics.stdev(v), statistics.pstdev(v))
print(statistics.variance(v), statistics.pvariance(v), statistics.median_low(v), statistics.median_high(v), statistics.harmonic_mean([1, 2, 4]))
print(statistics.geometric_mean([1, 4]), statistics.quantiles(v, n=4), statistics.multimode([1, 1, 2, 2]), statistics.fmean([1, 2]))
print(statistics.mean([Fraction(1, 2), Fraction(1, 3)]), statistics.mean([Decimal("1.5"), Decimal("2.5")]))
try:
    statistics.mean([])
except statistics.StatisticsError as e:
    print(e)
