# Performance Benchmark: CPython 3.12 vs SharpPy
# Tests closure/cell variable heavy operations

import time

def benchmark_closure_heavy():
    """Test closure variable access performance"""
    def make_counter():
        count = 0
        def increment():
            nonlocal count
            count += 1
            return count
        return increment

    counter = make_counter()
    for _ in range(10000):
        counter()
    return counter()

def benchmark_nested_closures():
    """Test deeply nested closure access"""
    def level1():
        a = 1
        def level2():
            b = 2
            def level3():
                c = 3
                def level4():
                    return a + b + c + 4
                return level4
            return level3
        return level2

    func = level1()()()
    total = 0
    for _ in range(10000):
        total += func()
    return total

def benchmark_comprehension_closure():
    """Test comprehension with closure variables (PEP 709)"""
    factor = 2
    offset = 10
    result = 0
    for _ in range(1000):
        result += sum([x * factor + offset for x in range(100)])
    return result

def benchmark_nested_comprehension():
    """Test nested comprehensions with closures"""
    multiplier = 3
    result = 0
    for _ in range(500):
        matrix = [[x * y * multiplier for x in range(10)] for y in range(10)]
        result += sum(sum(row) for row in matrix)
    return result

def benchmark_class_cell():
    """Test __class__ cell access (super())"""
    class Base:
        def method(self):
            return 1

    class Derived(Base):
        def method(self):
            return super().method() + 1

    obj = Derived()
    total = 0
    for _ in range(10000):
        total += obj.method()
    return total

def benchmark_walrus_comprehension():
    """Test walrus operator in comprehension"""
    result = 0
    for _ in range(1000):
        data = [y for x in range(100) if (y := x * 2) > 50]
        result += sum(data)
    return result

def benchmark_generator_closure():
    """Test generator with closure"""
    def make_gen(factor):
        def gen():
            for i in range(100):
                yield i * factor
        return gen

    total = 0
    for _ in range(1000):
        g = make_gen(3)
        total += sum(g())
    return total

def run_benchmarks():
    benchmarks = [
        ("Closure Heavy", benchmark_closure_heavy),
        ("Nested Closures", benchmark_nested_closures),
        ("Comprehension Closure", benchmark_comprehension_closure),
        ("Nested Comprehension", benchmark_nested_comprehension),
        ("Class Cell (super)", benchmark_class_cell),
        ("Walrus Comprehension", benchmark_walrus_comprehension),
        ("Generator Closure", benchmark_generator_closure),
    ]

    print("=" * 60)
    print("Performance Benchmark")
    print("=" * 60)

    results = []
    for name, func in benchmarks:
        # Warmup
        func()

        # Measure
        start = time.perf_counter()
        result = func()
        end = time.perf_counter()
        elapsed = (end - start) * 1000  # ms

        results.append((name, elapsed, result))
        print(f"{name:25} : {elapsed:8.2f} ms (result: {result})")

    print("=" * 60)
    total_time = sum(r[1] for r in results)
    print(f"{'Total':25} : {total_time:8.2f} ms")
    print("=" * 60)

    return results

if __name__ == "__main__":
    run_benchmarks()
