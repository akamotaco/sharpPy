# benchmark_baseline.py - SharpPy vs CPython 3.12 성능 비교
#
# 사용법:
#   CPython: python benchmark_baseline.py
#   SharpPy: dotnet run benchmark_baseline.py

import time

def bench_arithmetic(n):
    """산술 연산 집중"""
    x = 0
    for i in range(n):
        x = x + i
        x = x - 1
        x = x * 2
        x = x // 2
    return x

def bench_attribute(n):
    """속성 접근 집중"""
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
    """함수 호출 집중"""
    def f(a, b, c=1):
        return a + b + c

    for i in range(n):
        f(1, 2)
        f(1, 2, 3)
        f(1, 2, c=4)

def bench_condition(n):
    """조건문 집중 (IsTrue 테스트)"""
    items = [1, "", [], None, True, False, 0, "text"]
    count = 0
    for i in range(n):
        for item in items:
            if item:
                count += 1
    return count

def bench_list_ops(n):
    """리스트 연산"""
    lst = []
    for i in range(n):
        lst.append(i)
    for i in range(n):
        _ = lst[i]
    return len(lst)

def bench_dict_ops(n):
    """딕셔너리 연산"""
    d = {}
    for i in range(n):
        d[i] = i * 2
    for i in range(n):
        _ = d[i]
    return len(d)

def run_benchmark(name, func, n, runs=3):
    """벤치마크 실행 (여러 번 실행 후 중앙값)"""
    times = []
    for _ in range(runs):
        start = time.time()
        func(n)
        elapsed = time.time() - start
        times.append(elapsed)
    times.sort()
    median = times[len(times) // 2]
    return median * 1000  # ms로 변환

def main():
    print("=" * 50)
    print("SharpPy Benchmark Baseline")
    print("=" * 50)
    print()

    # CPython용 (높은 반복 횟수)
    cpython_benchmarks = [
        ("arithmetic", bench_arithmetic, 100000),
        ("attribute", bench_attribute, 50000),
        ("function_call", bench_function_call, 50000),
        ("condition", bench_condition, 10000),
        ("list_ops", bench_list_ops, 50000),
        ("dict_ops", bench_dict_ops, 50000),
    ]

    # SharpPy용 (낮은 반복 횟수 - instruction limit 고려)
    sharppy_benchmarks = [
        ("arithmetic", bench_arithmetic, 500),
        ("attribute", bench_attribute, 500),
        ("function_call", bench_function_call, 300),
        ("condition", bench_condition, 200),
        ("list_ops", bench_list_ops, 500),
        ("dict_ops", bench_dict_ops, 500),
    ]

    # 환경 감지: SharpPy는 time.time()이 float을 반환하지만
    # 첫 벤치마크 실행으로 감지
    import sys
    is_sharppy = 'SharpPy' in sys.version if hasattr(sys, 'version') else False

    # 간단한 감지: SharpPy는 보통 느리므로 첫 테스트로 판단
    # 여기서는 명시적으로 선택
    try:
        # SharpPy 감지 시도
        _ = sys.implementation
        benchmarks = cpython_benchmarks  # CPython
    except:
        benchmarks = sharppy_benchmarks  # SharpPy

    results = []
    for name, func, n in benchmarks:
        ms = run_benchmark(name, func, n)
        results.append((name, n, ms))
        print(f"{name:20} (n={n:6}): {ms:8.2f} ms")

    print()
    print("=" * 50)
    print("Benchmark completed")
    print("=" * 50)

if __name__ == "__main__":
    main()
