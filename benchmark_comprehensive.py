# benchmark_comprehensive.py - Comprehensive Performance Benchmark
# Usage:
#   SharpPy: dotnet run -c Release benchmark_comprehensive.py
#   CPython: python benchmark_comprehensive.py

import time

# ============================================================
# 1. Arithmetic / Integer operations
# ============================================================
def bench_int_arithmetic(n):
    x = 0
    for i in range(n):
        x = x + i
        x = x - 1
        x = x * 2
        x = x // 3
    return x

# ============================================================
# 2. Float arithmetic
# ============================================================
def bench_float_arithmetic(n):
    x = 0.0
    for i in range(n):
        x = x + i * 0.5
        x = x - 0.1
        x = x * 1.01
        x = x / 1.01
    return x

# ============================================================
# 3. String operations
# ============================================================
def bench_string_ops(n):
    s = ""
    for i in range(n):
        s = str(i)
        _ = len(s)
        _ = s.upper()
        _ = s + "_suffix"
    return s

# ============================================================
# 4. List operations (append, index, slice)
# ============================================================
def bench_list_ops(n):
    lst = []
    for i in range(n):
        lst.append(i)
    total = 0
    for i in range(n):
        total += lst[i]
    lst2 = lst[n//4 : n//2]
    return total + len(lst2)

# ============================================================
# 5. Dict operations
# ============================================================
def bench_dict_ops(n):
    d = {}
    for i in range(n):
        d[str(i)] = i
    total = 0
    for i in range(n):
        total += d[str(i)]
    return total

# ============================================================
# 6. Function call overhead
# ============================================================
def bench_function_call(n):
    def f(a, b):
        return a + b

    total = 0
    for i in range(n):
        total += f(i, 1)
    return total

# ============================================================
# 7. Keyword argument call
# ============================================================
def bench_kwargs_call(n):
    def f(a, b, c=0, d=0):
        return a + b + c + d

    total = 0
    for i in range(n):
        total += f(i, 1, c=2, d=3)
    return total

# ============================================================
# 8. Class instantiation and method call
# ============================================================
def bench_class_method(n):
    class Point:
        def __init__(self, x, y):
            self.x = x
            self.y = y
        def distance(self):
            return (self.x ** 2 + self.y ** 2) ** 0.5

    total = 0.0
    for i in range(n):
        p = Point(i, i + 1)
        total += p.distance()
    return total

# ============================================================
# 9. Closure / nonlocal
# ============================================================
def bench_closure(n):
    def make_counter():
        count = 0
        def inc():
            nonlocal count
            count += 1
            return count
        return inc

    counter = make_counter()
    for _ in range(n):
        counter()
    return counter()

# ============================================================
# 10. List comprehension
# ============================================================
def bench_list_comprehension(n):
    total = 0
    for _ in range(n):
        lst = [x * 2 for x in range(100)]
        total += sum(lst)
    return total

# ============================================================
# 11. Generator
# ============================================================
def bench_generator(n):
    def gen(limit):
        for i in range(limit):
            yield i * 2

    total = 0
    for _ in range(n):
        total += sum(gen(100))
    return total

# ============================================================
# 12. Exception handling (try/except overhead)
# ============================================================
def bench_exception(n):
    total = 0
    for i in range(n):
        try:
            total += i
        except Exception:
            pass
    return total

# ============================================================
# 13. Exception raising/catching
# ============================================================
def bench_exception_raise(n):
    count = 0
    for i in range(n):
        try:
            raise ValueError("test")
        except ValueError:
            count += 1
    return count

# ============================================================
# 14. Nested loops
# ============================================================
def bench_nested_loop(n):
    total = 0
    for i in range(n):
        for j in range(100):
            total += i + j
    return total

# ============================================================
# 15. Attribute access
# ============================================================
def bench_attribute_access(n):
    class Obj:
        def __init__(self):
            self.a = 1
            self.b = 2
            self.c = 3

    obj = Obj()
    total = 0
    for _ in range(n):
        total += obj.a + obj.b + obj.c
    return total

# ============================================================
# 16. Global variable access
# ============================================================
GLOBAL_VAL = 42

def bench_global_access(n):
    total = 0
    for _ in range(n):
        total += GLOBAL_VAL
    return total

# ============================================================
# 17. Polymorphic dispatch (__add__, __mul__)
# ============================================================
def bench_dunder_methods(n):
    class MyNum:
        def __init__(self, val):
            self.val = val
        def __add__(self, other):
            return MyNum(self.val + other.val)
        def __mul__(self, other):
            return MyNum(self.val * other.val)

    a = MyNum(1)
    b = MyNum(2)
    for _ in range(n):
        c = a + b
        c = c * a
    return c.val

# ============================================================
# 18. Inheritance and super()
# ============================================================
def bench_inheritance(n):
    class Base:
        def compute(self, x):
            return x + 1

    class Child(Base):
        def compute(self, x):
            return super().compute(x) * 2

    obj = Child()
    total = 0
    for i in range(n):
        total += obj.compute(i)
    return total

# ============================================================
# 19. Unpacking
# ============================================================
def bench_unpacking(n):
    total = 0
    data = (1, 2, 3, 4, 5)
    for _ in range(n):
        a, b, c, d, e = data
        total += a + b + c + d + e
    return total

# ============================================================
# 20. Boolean / comparison chain
# ============================================================
def bench_comparison(n):
    count = 0
    for i in range(n):
        if 0 < i < n and i != n // 2:
            count += 1
    return count

# ============================================================
# Runner
# ============================================================
def run_single(name, func, n, warmup=1, runs=3):
    # warmup
    for _ in range(warmup):
        func(n)

    times = []
    for _ in range(runs):
        start = time.perf_counter()
        result = func(n)
        elapsed = time.perf_counter() - start
        times.append(elapsed)
    times.sort()
    median_ms = times[len(times) // 2] * 1000
    return median_ms, result

def main():
    benchmarks = [
        ("int_arithmetic",     bench_int_arithmetic,     5000),
        ("float_arithmetic",   bench_float_arithmetic,   5000),
        ("string_ops",         bench_string_ops,         3000),
        ("list_ops",           bench_list_ops,           5000),
        ("dict_ops",           bench_dict_ops,           3000),
        ("function_call",      bench_function_call,      5000),
        ("kwargs_call",        bench_kwargs_call,        3000),
        ("class_method",       bench_class_method,       2000),
        ("closure",            bench_closure,            5000),
        ("list_comprehension", bench_list_comprehension,  500),
        ("generator",          bench_generator,           500),
        ("exception_try",      bench_exception,          5000),
        ("exception_raise",    bench_exception_raise,     500),
        ("nested_loop",        bench_nested_loop,         100),
        ("attribute_access",   bench_attribute_access,   5000),
        ("global_access",      bench_global_access,      5000),
        ("dunder_methods",     bench_dunder_methods,     3000),
        ("inheritance",        bench_inheritance,        3000),
        ("unpacking",          bench_unpacking,          5000),
        ("comparison",         bench_comparison,         5000),
    ]

    print("=" * 60)
    print("Comprehensive Performance Benchmark")
    print("=" * 60)
    print()
    print(f"{'Benchmark':<24} {'N':>6}  {'Time (ms)':>10}  {'ops/ms':>10}")
    print("-" * 60)

    total_ms = 0.0
    for name, func, n in benchmarks:
        ms, result = run_single(name, func, n)
        total_ms += ms
        ops = n / ms if ms > 0 else 0
        print(f"{name:<24} {n:>6}  {ms:>10.2f}  {ops:>10.0f}")

    print("-" * 60)
    print(f"{'TOTAL':<24} {'':>6}  {total_ms:>10.2f}")
    print("=" * 60)

if __name__ == "__main__":
    main()
