# benchmark_sharppy.py - SharpPy용 벤치마크
# 사용법: dotnet run -c Release benchmark_sharppy.py

import time

def bench_arithmetic(n):
    x = 0
    for i in range(n):
        x = x + i
        x = x - 1
        x = x * 2
        x = x // 2
    return x

def bench_attribute(n):
    class Obj:
        def __init__(self):
            self.value = 0
        def method(self):
            return self.value

    obj = Obj()
    for i in range(n):
        _ = obj.value
        _ = obj.method()

def bench_function_call(n):
    def f(a, b, c=1):
        return a + b + c

    for i in range(n):
        f(1, 2)
        f(1, 2, 3)
        f(1, 2, c=4)

def bench_condition(n):
    items = [1, "", [], None, True, False, 0, "text"]
    count = 0
    for i in range(n):
        for item in items:
            if item:
                count += 1
    return count

def bench_list_ops(n):
    lst = []
    for i in range(n):
        lst.append(i)
    for i in range(n):
        _ = lst[i]
    return len(lst)

def bench_dict_ops(n):
    d = {}
    for i in range(n):
        d[i] = i * 2
    for i in range(n):
        _ = d[i]
    return len(d)

def run_benchmark(name, func, n, runs=3):
    times = []
    for _ in range(runs):
        start = time.time()
        func(n)
        elapsed = time.time() - start
        times.append(elapsed)
    times.sort()
    median = times[len(times) // 2]
    return median * 1000

def main():
    print("=" * 50)
    print("SharpPy Benchmark")
    print("=" * 50)
    print()

    # SharpPy용: 낮은 반복 횟수 (instruction limit 고려)
    benchmarks = [
        ("arithmetic", bench_arithmetic, 500),
        ("attribute", bench_attribute, 500),
        ("function_call", bench_function_call, 300),
        ("condition", bench_condition, 200),
        ("list_ops", bench_list_ops, 500),
        ("dict_ops", bench_dict_ops, 500),
    ]

    for name, func, n in benchmarks:
        ms = run_benchmark(name, func, n)
        ops_per_ms = n / ms if ms > 0 else 0
        print(f"{name:20} (n={n:6}): {ms:8.2f} ms  ({ops_per_ms:.0f} ops/ms)")

    print()
    print("=" * 50)

if __name__ == "__main__":
    main()
