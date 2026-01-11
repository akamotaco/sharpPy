# SharpPy 성능 최적화 계획

CPython 3.12 대비 SharpPy의 불필요한 오버헤드를 분석하고 개선 계획을 정리합니다.

---

## 0. 최적화 원칙

### 핵심 원칙

1. **회귀 방지 최우선**
   - 모든 기존 테스트가 통과해야 함
   - 최적화로 인한 동작 변경 절대 불가
   - 변경 전후 동일한 결과 보장

2. **C# 특성 고려**
   - CPython은 C, SharpPy는 C#
   - C에서 빠른 패턴이 C#에서 반드시 빠르지 않음
   - JIT 컴파일러의 최적화를 방해하지 않도록 주의
   - 예: 포인터 연산 → 배열 인덱싱, switch → Dictionary

3. **CPython 3.12 동작 준수**
   - 최적화는 구현 세부사항, 동작은 명세
   - CPython과 다른 결과를 내면 안 됨
   - edge case에서도 동일한 예외, 동일한 메시지

4. **빠른 개선보다 올바른 개선**
   - 추측성 최적화 금지
   - 반드시 벤치마크로 효과 검증
   - 복잡도 증가가 성능 향상을 정당화하는지 확인

### 검증 절차 (매 Phase마다)

```
1. 기존 테스트 실행 (회귀 확인)
   - test_comprehensive_python312.py
   - test_ultimate_complex_features.py
   - test_nested_super.py
   - test_global_variable.py

2. 벤치마크 실행 (효과 측정)
   - 최적화 전/후 비교
   - 최소 3회 반복, 중앙값 사용

3. CPython 동작 비교 (명세 준수)
   - edge case 테스트
   - 예외 메시지 동일성 확인
```

### C# vs C 최적화 차이점

| 영역 | C (CPython) | C# (SharpPy) | 비고 |
|------|------------|--------------|------|
| 명령어 디스패치 | computed goto | switch 또는 delegate[] | JIT이 switch 최적화 |
| 메모리 접근 | 포인터 연산 | 배열 인덱싱 | bounds check 제거 가능 |
| 객체 할당 | 커스텀 allocator | GC 의존 | 풀링으로 완화 |
| 함수 호출 | C 함수 포인터 | delegate/virtual | 인라이닝 기대 |
| 타입 체크 | C 매크로 | `is`/`as` 패턴 | JIT 최적화됨 |

---

## 1. 심각도별 오버헤드 분류

### 🔴 HIGH (즉시 개선 필요)

| # | 영역 | 현재 구현 | CPython 3.12 | 파일 위치 |
|---|------|----------|--------------|-----------|
| H1 | IsTrue() | MRO 순회로 `__bool__`, `__len__` 조회 | 슬롯 포인터 O(1) | `core/PyObject.cs:223-295` |
| H2 | GetAttribute() | 매번 MRO 순회 + Dict 해싱 | 50K 엔트리 메서드 캐시 | `core/PyObject.cs:436-530` |
| H3 | LEGB 변수 조회 | enclosing scope 체인 순회 | `f_localsplus[]` 배열 O(1) | `core/PyScope.cs:519-650` |
| H4 | 함수 호출 할당 | 매 호출마다 Dict/List/Array 생성 | 스택/풀 기반 재사용 | `runtime/PyVM.cs:119-440` |
| H5 | Adaptive Specialization | `Enabled = false` (미구현) | PEP 659 완전 구현 | `runtime/AdaptiveSpecializer.cs` |

### 🟡 MEDIUM (개선 권장)

| # | 영역 | 현재 구현 | CPython 3.12 | 파일 위치 |
|---|------|----------|--------------|-----------|
| M1 | VM 명령어 fetch | List 인덱싱 + bounds check | C 포인터 연산 | `runtime/PyVM.cs:1175-1210` |
| M2 | Callable 타입 판별 | `is` 체인 (5+ 타입 체크) | `tp_call` 슬롯 | `runtime/PyVM.cs:2023-2031` |
| M3 | LOAD_ATTR 리플렉션 | `GetMethod()` 호출 | 타입 플래그 | `runtime/PyVM.cs:2707-2715` |
| M4 | BINARY_OP 문자열 | `operation.ToString()` + Replace | 컴파일된 specialized opcode | `runtime/PyVM.cs:1887-1940` |
| M5 | NameError 처리 | 3개 Dictionary 생성 | 빠른 실패 | `core/PyScope.cs:610-630` |
| M6 | 프레임 할당 | 매 호출마다 new PyFrame | 메모리 풀 재사용 | `runtime/PyVM.cs:1085` |

### 🟢 LOW (나중에 개선)

| # | 영역 | 현재 구현 | CPython 3.12 | 파일 위치 |
|---|------|----------|--------------|-----------|
| L1 | Dict views | 새 객체 생성 | 경량 프록시 | `type/PyDict.cs:65-92` |
| L2 | 예외 제어 흐름 | try-catch per instruction | Exception table | `runtime/PyVM.cs:1205-1360` |
| L3 | 디버그 큐 | 100개 명령어 기록 | 없음 | `runtime/PyVM.cs:1106-1110` |

---

## 1.5 Release 모드에서 제거되어야 할 디버그 코드 (2026-01-11 발견)

> **중요:** 아래 코드들은 `#if DEBUG` 블록 바깥에 있어서 Release 모드에서도 실행됨.
> 이것은 명백한 오버헤드이며, 제거 또는 조건부 컴파일로 감싸야 함.

### 🔴 CRITICAL: PyVM.cs 무한루프 디버깅 코드

**위치:** `runtime/PyVM.cs:1091-1136`

**문제:** 매 instruction마다 실행되는 디버그 코드가 Release 모드에서도 활성화됨

```csharp
// Lines 1091-1099: 매 ExecuteFrame 호출마다 생성
var startTime = DateTime.UtcNow;                    // DateTime 호출
var maxInstructions = 50_000;                       // 상수지만 로컬 변수
var instructionCount = 0;                           // 카운터
var lastInstructions = new Queue<string>();         // 매번 Queue 할당!

// Lines 1108-1128: 매 10,000 명령어마다 (조건 체크는 매 instruction)
instructionCount++;
if (instructionCount % 10000 == 0) {
    var elapsed = DateTime.UtcNow - startTime;      // DateTime 호출
    // ... 시간/명령어 제한 검사
}

// Lines 1132-1136: 매 instruction마다 실행! (가장 큰 오버헤드)
var instructionLog = $"[{instructionCount}] IP={...} {instruction.OpCode}..."; // 문자열 생성
if (lastInstructions.Count >= maxLastInstructions)
    lastInstructions.Dequeue();                     // Queue 연산
lastInstructions.Enqueue(instructionLog);           // Queue 연산
```

**오버헤드 분석:**
- 매 instruction마다: string interpolation + Queue.Dequeue + Queue.Enqueue
- string interpolation은 내부적으로 StringBuilder 할당 + 문자열 연결
- 이것이 모든 instruction에서 발생함

**권장 수정:**
```csharp
#if DEBUG
            var startTime = DateTime.UtcNow;
            var lastInstructions = new Queue<string>();
            // ... 디버그 전용 코드
#endif
```

### 🟡 MEDIUM: FrameGeneratorEnumerator.cs Generator 로깅

**위치:** `runtime/FrameGeneratorEnumerator.cs:44, 66, 89`

**문제:** Generator 실행마다 Console.WriteLine 호출

```csharp
// Line 44: 첫 실행
Console.WriteLine("🔄 Generator: First execution, starting from instruction 0");

// Line 66: 재개 시
Console.WriteLine($"🔄 Generator: Resumed with stack size {_frame.ValueStack.Count}...");

// Line 89: yield 시
Console.WriteLine($"🔄 Generator: Yielded {yieldEx.Value}...");
```

**권장 수정:**
```csharp
#if DEBUG_VM_LOG
            Console.WriteLine("🔄 Generator: ...");
#endif
```

### 🟡 MEDIUM: 런타임 Reflection 호출

**문제:** 매 명령어 실행마다 Reflection을 사용하여 메서드를 조회함

#### 1. LOAD_ATTR에서 GetAttribute override 체크
**위치:** `runtime/PyVM.cs:2727-2729`

```csharp
// 매 LOAD_ATTR마다 Reflection 호출!
var getAttrMethod = objType.GetMethod("GetAttribute",
    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
if (getAttrMethod != null && getAttrMethod.DeclaringType != typeof(PyObject))
{
    hasCustomGetAttribute = true;
}
```

**오버헤드:** Reflection은 매우 느림. 매 속성 접근마다 호출됨.

**권장 수정:**
- PyType에 `HasCustomGetAttribute` 플래그를 캐시
- 타입 생성 시 한 번만 체크

#### 2. BINARY_OP에서 in-place 메서드 조회
**위치:** `runtime/PyVM.cs:6537`

```csharp
// 매 in-place 연산마다 Reflection 호출!
var csharpMethod = left.GetType().GetMethod(inplaceMethodName);
```

**오버헤드:** `__iadd__`, `__isub__` 등 매 연산마다 Reflection

**권장 수정:**
- PyType별로 메서드 딕셔너리 캐시
- 또는 virtual 메서드로 대체

### 🔴 CRITICAL: BINARY_OP에서 사용되지 않는 문자열 연산

**위치:** `runtime/PyVM.cs:1894-1902`

```csharp
// 매 BINARY_OP마다 실행되지만 결과가 사용되지 않음!
var location = $"{frame.Code.Name}_{frame.InstructionPointer}";  // 사용 안됨!
var opName = operation.ToString().ToLower().Replace("_", "");     // 사용 안됨!
if (opName == "truedivide") opName = "/";                         // 사용 안됨!
// ... 더 많은 변환 ...
```

**오버헤드:**
- BINARY_OP는 모든 산술/비교 연산에 사용됨 (가장 빈번한 opcode 중 하나)
- 매번 3개의 문자열 할당 + ToString() + ToLower() + Replace()
- `location`과 `opName`은 코드에서 사용되지 않는 **dead code**

**권장 수정:**
```csharp
// 단순히 삭제
// var location = ...  // 삭제
// var opName = ...    // 삭제

var result = ExecuteBinaryOpType(left, right, operation);
frame.ValueStack.Push(result);
```

### 🟡 MEDIUM: 함수 호출마다 배열 할당

**위치:** `runtime/PyVM.cs:1954, 2005`

```csharp
// Line 1954: 매 CALL마다 배열 할당!
var callArgs = new PyObject[callArgCount];

// Line 2005: 메서드 호출 시 추가 배열 할당!
finalArgs = new PyObject[callArgs.Length + 1];
```

**오버헤드:**
- 함수/메서드 호출은 가장 빈번한 연산 중 하나
- 매번 GC 할당 발생
- 작은 배열이라도 할당 오버헤드 존재

**권장 수정:**
```csharp
// ArrayPool<T> 사용
var callArgs = ArrayPool<PyObject>.Shared.Rent(callArgCount);
try
{
    // ... 사용
}
finally
{
    ArrayPool<PyObject>.Shared.Return(callArgs, clearArray: true);
}
```

---

## 1.6 잘못 작성된 CPython 모사 코드 (2026-01-11 발견)

### 내장 타입에 IsTrue() override 누락

**문제:** PyList, PyString, PyDict 등 내장 타입에 `IsTrue()` override가 없음

**현재 동작:**
```csharp
// PyObject.IsTrue() - 모든 객체에서 호출됨
public virtual bool IsTrue()
{
    // Fast path for True/False/None
    if (this == PyBool.True) return true;
    if (this == PyBool.False) return false;
    if (this == PyNone.Instance) return false;

    // MRO 순회로 __bool__ 조회 (느림!)
    var boolAttr = PyGetAttribute("__bool__");
    // ...

    // MRO 순회로 __len__ 조회 (느림!)
    var lenAttr = PyGetAttribute("__len__");
    // ...
}
```

**CPython:**
```c
// Objects/object.c:1678-1696
// 슬롯 포인터로 O(1) 접근
if (type->tp_as_number && type->tp_as_number->nb_bool) {
    return (*type->tp_as_number->nb_bool)(obj);
}
if (type->tp_as_mapping && type->tp_as_mapping->mp_length) {
    // ...
}
```

**권장 수정:**
```csharp
// PyList.cs에 추가
public override bool IsTrue() => _items.Count > 0;

// PyString.cs에 추가
public override bool IsTrue() => _value.Length > 0;

// PyDict.cs에 추가
public override bool IsTrue() => _dict.Count > 0;

// PyTuple.cs에 추가
public override bool IsTrue() => _items.Length > 0;
```

이렇게 하면 MRO 순회 없이 O(1)에 완료됨.

---

## 2. 데이터 기반 최적화 절차

### Step 0: 현재 상태 측정 (필수 선행)

**목표:** 실제 병목 지점 파악, 추측 제거

구현 전에 반드시 다음을 수행:

1. **벤치마크 실행** - SharpPy vs CPython 3.12 비교
2. **프로파일링** - 어떤 함수/메서드가 가장 많이 호출되는지
3. **병목 식별** - 실측 데이터로 우선순위 결정

```
❌ 잘못된 접근: "MRO 순회가 느릴 것 같으니 캐시 구현하자"
✅ 올바른 접근: "프로파일링 결과 GetAttribute가 전체 시간의 X% → 캐시 효과 검증"
```

---

### Step 1: 벤치마크 기준선 확립

**실행할 벤치마크:**
```python
# benchmark_baseline.py
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

# 실행 및 측정
benchmarks = [
    ("arithmetic", bench_arithmetic, 100000),
    ("attribute", bench_attribute, 100000),
    ("function_call", bench_function_call, 100000),
    ("condition", bench_condition, 10000),
]

for name, func, n in benchmarks:
    start = time.time()
    func(n)
    elapsed = time.time() - start
    print(f"{name}: {elapsed*1000:.2f}ms")
```

**측정 결과 (2026-01-11):**

> **주의:** SharpPy는 "인터프리터 위의 인터프리터"이므로 CPython보다 느린 것은 당연함.
> 목표는 "CPython만큼 빠르게"가 아니라 "불필요한 오버헤드 제거".

| 항목 | CPython 3.12 | SharpPy | 비고 |
|------|-------------|---------|------|
| arithmetic | 5,785 ops/ms | 31 ops/ms | 산술 연산 |
| attribute | 12,163 ops/ms | 16 ops/ms | 속성 접근 |
| function_call | 3,909 ops/ms | 7 ops/ms | 함수 호출 |
| condition | 3,331 ops/ms | 6 ops/ms | 조건문 |
| list_ops | 10,004 ops/ms | 35 ops/ms | 리스트 연산 |
| dict_ops | 4,754 ops/ms | 39 ops/ms | 딕셔너리 연산 |

**분석:**
- 모든 영역에서 100x+ 차이는 구조적 한계 (인터프리터 on 인터프리터)
- 개선 대상: 불필요한 오버헤드 (디버그 로깅, 불필요한 할당 등)

---

### Step 2: 병목 분석 후 우선순위 결정

벤치마크 결과에 따라 최적화 대상 결정:

| 배율 | 조치 |
|------|------|
| 1-2x | 허용 범위, 최적화 불필요 |
| 2-5x | 분석 후 간단한 개선 검토 |
| 5x+ | 심각한 병목, 우선 해결 |

**C# 특성 고려사항:**

| 영역 | CPython 기법 | C# 대안 | 비고 |
|------|-------------|---------|------|
| 메서드 캐시 | 50K 해시 테이블 | `ConditionalWeakTable` 또는 `Dictionary` | GC 친화적 |
| 슬롯 포인터 | C 함수 포인터 | `virtual` 메서드 또는 `interface` | JIT 인라이닝 |
| 버퍼 풀 | 커스텀 allocator | `ArrayPool<T>` | .NET 내장 |
| Adaptive Spec | opcode 패치 | 별도 경로 또는 제네릭 | 런타임 수정 비용 |

---

### Step 3: 개선 후보 (벤치마크 결과에 따라 선택)

#### 후보 A: 메서드/속성 캐시 (H2)

**적용 조건:** attribute 벤치마크가 5x+ 느린 경우

**검증 방법:**
1. `GetAttribute` 호출 횟수 카운터 추가
2. MRO 순회 깊이 측정
3. 캐시 적용 전/후 비교

**C# 구현 방안:**
```csharp
// 옵션 1: ConditionalWeakTable (GC 친화적)
private static ConditionalWeakTable<PyType, Dictionary<string, PyObject>> _cache;

// 옵션 2: 간단한 Dictionary + 버전 체크
private static Dictionary<(PyType, string), (PyObject, int)> _cache;
```

---

#### 후보 B: IsTrue() 최적화 (H1)

**적용 조건:** condition 벤치마크가 5x+ 느린 경우

**검증 방법:**
1. `IsTrue()` 호출 횟수 측정
2. `__bool__`/`__len__` 조회 횟수 측정
3. 타입별 분포 파악

**C# 구현 방안:**
```csharp
// 옵션 1: virtual 메서드 오버라이드 (이미 되어 있을 수 있음)
public class PyList : PyObject
{
    public override bool IsTrue() => _items.Count > 0;  // MRO 순회 없음
}

// 옵션 2: 타입 플래그 기반
if ((type.Flags & TypeFlags.HasCustomBool) == 0)
    return DefaultIsTrue();  // 빠른 경로
```

---

#### 후보 C: 함수 호출 최적화 (H4)

**적용 조건:** function_call 벤치마크가 5x+ 느린 경우

**검증 방법:**
1. 호출당 할당 바이트 측정 (`GC.GetAllocatedBytesForCurrentThread()`)
2. kwargs 사용 비율 파악
3. 풀링 적용 전/후 비교

**C# 구현 방안:**
```csharp
// .NET 내장 ArrayPool 사용
var args = ArrayPool<PyObject>.Shared.Rent(argCount);
try
{
    // ... 사용
}
finally
{
    ArrayPool<PyObject>.Shared.Return(args, clearArray: true);
}
```

---

#### 후보 D: LEGB 조회 (H3)

**적용 조건:** 프로파일링에서 `LookupVariable`이 hot path인 경우

**우선 확인:**
- 바이트코드가 이미 인덱스 기반(LOAD_FAST, LOAD_DEREF)이면 문제 없음
- `LookupVariable`이 실제로 호출되는 경로 파악 필요

---

#### 후보 E: Adaptive Specialization (H5)

**적용 조건:** arithmetic 벤치마크가 10x+ 느린 경우

**주의사항:**
- 구현 복잡도 매우 높음
- C#에서 런타임 opcode 변경은 비용이 큼
- 더 간단한 대안: 타입별 분기 최적화

**대안 검토:**
```csharp
// Adaptive Spec 대신 간단한 타입 체크
case ByteCodeOp.BINARY_OP:
    var right = frame.ValueStack.Pop();
    var left = frame.ValueStack.Pop();

    // Fast path: 둘 다 PyInt인 경우
    if (left is PyInt leftInt && right is PyInt rightInt)
    {
        // 직접 연산
    }
    else
    {
        // 일반 경로
    }
```

---

## 3. 실행 계획

```
Week 1: 측정
├─ Day 1-2: 벤치마크 구현 및 실행
├─ Day 3-4: 프로파일링, 병목 식별
└─ Day 5: 결과 분석, 우선순위 결정

Week 2+: 구현 (측정 결과에 따라)
├─ 가장 심각한 병목부터 해결
├─ 각 변경 후 회귀 테스트
└─ 벤치마크로 효과 검증
```

**판단 기준:**
- 복잡도 증가 < 성능 향상이어야 함
- 측정 불가능한 개선은 하지 않음
- CPython 동작과 다르면 롤백

---

## 4. 리스크 및 고려사항

### 호환성
- 최적화로 인한 동작 변경 없어야 함
- 모든 기존 테스트 통과 필수

### 디버깅
- 최적화된 경로에서도 traceback 정확해야 함
- 프로파일링 모드에서 최적화 비활성화 옵션

### 메모리
- 캐시 크기 vs 메모리 사용량 트레이드오프
- 캐시 무효화 정책 명확히

### 스레드 안전성
- 현재 SharpPy는 단일 스레드 가정
- 캐시도 스레드 로컬로 구현

---

## 5. 참고 자료

- [PEP 659 – Specializing Adaptive Interpreter](https://peps.python.org/pep-0659/)
- [CPython 3.12 ceval.c](https://github.com/python/cpython/blob/3.12/Python/ceval.c)
- [CPython 3.12 specialize.c](https://github.com/python/cpython/blob/3.12/Python/specialize.c)
- [Faster CPython Ideas](https://github.com/faster-cpython/ideas)
