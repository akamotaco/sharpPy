# SharpPy 성능 최적화 계획: 클래스 구조 제거

> 작성: 2026-03-09 | 상태: 코드 리뷰 완료, 구현 대기

## 1. 현재 상태 (2026-03-09 프로파일링)

### 1.1 벤치마크 결과

| Benchmark | CPython (ms) | SharpPy (ms) | 배율 | 카테고리 |
|-----------|----------:|----------:|------:|------|
| dunder_methods | 1.90 | 357.5 | **188x** | 클래스 |
| string_ops | 0.75 | 141.9 | **189x** | 메서드 |
| class_method | 1.13 | 163.6 | **145x** | 클래스 |
| function_call | 0.53 | 72.6 | **137x** | 호출 |
| list_ops | 0.51 | 66.6 | **131x** | 메서드 |
| kwargs_call | 0.53 | 65.3 | **123x** | 호출 |
| inheritance | 0.65 | 80.0 | **123x** | 클래스 |
| exception_raise | 0.10 | 11.4 | **114x** | 예외 |
| attribute_access | 0.54 | 48.6 | **90x** | 클래스 |
| float_arithmetic | 0.61 | 54.8 | **90x** | 연산 |
| int_arithmetic | 0.70 | 49.0 | **70x** | 연산 |
| dict_ops | 1.07 | 55.9 | **52x** | 컬렉션 |
| closure | 0.57 | 27.3 | **48x** | 클로저 |
| list_comprehension | 1.72 | 79.4 | **46x** | 컴프리헨션 |
| generator | 3.00 | 133.5 | **45x** | 제너레이터 |
| global_access | 0.27 | 11.3 | **42x** | 변수 |
| nested_loop | 0.46 | 17.4 | **38x** | 루프 |
| comparison | 0.64 | 22.9 | **36x** | 비교 |
| exception_try | 0.27 | 9.1 | **34x** | 예외 |
| unpacking | 0.62 | 18.4 | **30x** | 언패킹 |
| **TOTAL** | **16.57** | **1486.3** | **89.7x** | |

### 1.2 최적화 결과 (2026-03-09)

| Phase | 내용 | SharpPy (ms) | 비율 | 변화 |
|-------|------|----------:|-----:|-----:|
| Baseline | 초기 상태 | 1486.3 | 89.7x | - |
| Phase 0 | STORE_FAST Pop→PopValue 이중 변환 제거 | 1028.8 | 62.1x | **-30.8%** |
| Phase 1 | Magic Method 6패턴 통합 + PyMethod 할당 제거 | 987.7 | 59.6x | -4.0% |
| Phase 2 | PyClass magic method 캐시 (TypeVersionTag 무효화) | ~993 | ~59.9x | ~0% |
| Phase 3 | GetAttribute 캐시 + LOAD_ATTR fast path | ~993 | ~59.9x | ~0% |

**총 개선: 1486ms → 993ms (33.2% 감소, 89.7x → 59.9x)**

#### Phase별 주요 변경사항:
- **Phase 0**: `ExecuteInstruction` STORE_FAST 경로에서 `Pop()+FromObject()` → `PopValue()`
- **Phase 1**: `FindMagicMethod()`+`InvokeMagicMethod()` 통합, `new PyMethod()` 할당 제거
- **Phase 2**: `GetCachedMagicMethod()` — O(1) 캐시 hit, TypeVersionTag 무효화
- **Phase 3**: `GetAttribute` 내 `__getattribute__`/`__setattr__` 캐시, LOAD_ATTR MRO bypass

### 1.3 병목 분석

**클래스 관련 작업 (188x~90x)이 전체 시간의 58%를 차지:**
- `dunder_methods` + `class_method` + `inheritance` + `attribute_access` = 649.7ms (43.7%)
- `string_ops` + `list_ops` (내부적으로 메서드 호출) = 208.5ms (14.0%)

**비클래스 작업 (30x~52x)은 상대적으로 양호:**
- `unpacking`, `exception_try`, `comparison`, `nested_loop` 등

---

## 2. 근본 원인: CPython vs SharpPy 아키텍처

### 2.1 CPython 3.12 디스패치 (클래스 없음)

```c
// CPython: C struct + 함수 포인터 (~100개 슬롯)
// Include/cpython/object.h:146-231
struct PyTypeObject {
    binaryfunc  nb_add;        // 함수 포인터: 포인터 1회 역참조
    getattrofunc tp_getattro;  // 함수 포인터: 포인터 1회 역참조
    ternaryfunc tp_call;       // 함수 포인터: 포인터 1회 역참조
    // ...
};

// 호출: obj->ob_type->tp_as_number->nb_add(left, right)
// = 2-3회 메모리 접근, vtable 없음
```

### 2.2 SharpPy 디스패치 (C# 클래스 기반)

```csharp
// SharpPy: 84개 virtual 메서드가 있는 추상 클래스
// core/PyObject.cs
abstract class PyObject {
    public virtual PyObject Add(PyObject other) { ... }      // vtable lookup
    public virtual PyObject GetAttribute(string name) { ... } // vtable lookup
    public virtual PyObject Call(...) { ... }                  // vtable lookup
    // ... 84개 virtual 메서드
}

// 호출: left.Add(right)
// = vtable 포인터 로드 → vtable 엔트리 조회 → 간접 호출
// + 서브클래스마다 vtable 별도 존재 (캐시 압력)
```

### 2.3 성능 차이의 원인

| 메커니즘 | CPython | SharpPy | 차이 |
|---------|---------|---------|------|
| 산술 디스패치 | `nb_add` 슬롯 (직접 포인터) | `virtual Add()` (vtable) | vtable 간접 참조 |
| 속성 접근 | inline cache + 타입 버전 태그 | MRO 루프 × 2 + Dictionary | O(1) vs O(n) |
| 메서드 호출 | `LOAD_ATTR_INSTANCE_VALUE` (배열 인덱스) | GenericGetAttribute + descriptor | 직접 vs 탐색 |
| 디스크립터 체크 | `tp_descr_get != NULL` (포인터 비교) | `IDescriptor` is 검사 | O(1) 동일 |
| 인스턴스 생성 | `tp_new`/`tp_init` 슬롯 | `LookupInMRO()` × 2 | O(1) vs O(n) |

---

## 3. 코드 리뷰 결과: 핵심 발견 사항

### 3.1 ★ dunder_methods 188x의 진짜 원인: `CallMagicMethod()` 캐시 부재

**파일**: `type/PyClass.cs:1581-1614`

```csharp
// 현재 코드: 매 호출마다 MRO 순회 + PyMethod 할당
private PyObject CallMagicMethod(string methodName, params PyObject[] args)
{
    // 1. InstanceDict 조회 (보통 __add__는 여기 없음)
    if (InstanceDict.ContainsKey(methodName)) { ... }

    // 2. MRO 루프: 매번 O(MRO depth) × Dictionary lookup
    foreach (var mroType in InstanceType.MRO)           // ← 캐시 없음!
    {
        if (mroType is PyClass pyClass &&
            pyClass.ClassDict.TryGetValue(methodName, out PyObject method))
        {
            if (method is PyFunction func)
            {
                var boundMethod = new PyMethod(this, func);  // ← 매번 힙 할당!
                return boundMethod.Call(args, null);
            }
            else if (method.IsCallable())
            {
                var argsWithSelf = new PyObject[args.Length + 1];  // ← 매번 배열 할당!
                argsWithSelf[0] = this;
                Array.Copy(args, 0, argsWithSelf, 1, args.Length);
                return method.Call(argsWithSelf, null);
            }
            break;
        }
    }
    return null;
}
```

**문제 3가지:**
1. **캐시 없음**: `GlobalMethodCache` (PyType.cs:8-58)를 사용하지 않고 매번 MRO 순회
2. **PyMethod 할당**: 매 호출마다 `new PyMethod(this, func)` 힙 할당 (~2-10μs)
3. **args 배열 할당**: non-function callable 경로에서 `new PyObject[args.Length + 1]`

**비교**: 같은 파일의 `LookupInMRO()` (PyClass.cs:270-275)는 `GlobalMethodCache`를 사용하여 O(1) 캐시 조회. `CallMagicMethod()`만 이 캐시를 우회.

### 3.2 STORE_FAST inline 경로는 이미 최적화됨

**코드 리뷰 결과**:
- inline fast path (PyVM.cs:1222-1231): **이미 `PopValue()` 사용** → 수정 불필요
- ExecuteInstruction case (PyVM.cs:1660-1672): **`Pop()` + `FromObject()` 사용** → 수정 필요

```csharp
// PyVM.cs:1665-1666 (ExecuteInstruction 경로만 비효율)
var storeVal = frame.ValueStack.Pop();                          // PyValue → PyObject
frame.LocalsPlus[storeIndex] = PyValue.FromObject(storeVal);   // PyObject → PyValue
```

### 3.3 PyStack.PopValue() vs Pop() 확인

```csharp
// PyStack.cs:41-48 - Pop(): PyValue → PyObject 변환 (느림)
public PyObject Pop()
{
    var val = _items[--_top];
    _items[_top] = default;
    return val.ToObject();  // ← 변환 발생
}

// PyStack.cs:107-114 - PopValue(): PyValue 그대로 반환 (빠름)
public PyValue PopValue()
{
    var val = _items[--_top];
    _items[_top] = default;
    return val;  // ← 직접 반환, zero-copy
}
```

### 3.4 PyType 슬롯 통합 가능성 확인

- **PyType 생성자** (PyType.cs:338-354): MRO 계산 후 `InitializeDescriptors()` 호출 → 여기에 `InitializeSlots()` 추가 가능
- **TypeVersionTag** (PyType.cs:143-168): `ulong`, 단조 증가, `InvalidateTypeCache()`에서 갱신
- **InvalidateTypeCache 호출처**: PyType.cs:1555, PyClass.cs:601, PyClass.cs:612 → 3곳 모두에 `InvalidateSlots()` 연동 필요
- **InheritSlotMethods** (PyClass.cs:225-257): 이미 존재, 부모 타입의 슬롯 메서드를 자식에 상속하는 인프라

### 3.5 PyClassInstance의 dunder override 확인

**PyClass.cs:1644-1714** — PyClassInstance는 모든 산술 연산을 override:
```csharp
public override PyObject Add(PyObject other)
{
    var result = BinaryOpWithMagicMethod(other, "__add__", "__radd__");
    return result ?? base.Add(other);
}
// Subtract, Multiply, Divide, FloorDivide, Modulo, Power,
// LeftShift, RightShift, BitwiseAnd, BitwiseOr, BitwiseXor 모두 동일 패턴
```

**실행 경로 전체**: `BINARY_OP → ExecuteBinaryOpType → left.Add(right) → PyClassInstance.Add() → BinaryOpWithMagicMethod() → CallMagicMethod()` — 모든 호출마다 MRO 순회 + PyMethod 할당.

---

## 4. 최적화 전략

### 4.0 원칙

1. **회귀 금지**: 모든 변경은 기존 테스트 통과 필수
2. **점진적 적용**: 각 Phase는 독립적으로 동작, 롤백 가능
3. **기존 코드 보존**: virtual 메서드는 유지하되, 핫 패스에서 우회
4. **CPython 패턴 준수**: 함수 포인터 슬롯 + 타입 버전 태그
5. **fallback 보장**: 모든 새 경로는 `if (최적화조건) { 빠른경로 } else { 기존경로 }` 패턴

---

## Phase 0: 즉시 수정 가능한 비효율 (위험도: 최소)

### 0.1 STORE_FAST ExecuteInstruction 경로 수정

> inline fast path는 이미 최적화됨 (PyVM.cs:1227). ExecuteInstruction case만 수정.

**현재** (PyVM.cs:1665-1666):
```csharp
var storeVal = frame.ValueStack.Pop();                          // PyValue → PyObject
frame.LocalsPlus[storeIndex] = PyValue.FromObject(storeVal);   // PyObject → PyValue
```

**수정**:
```csharp
frame.LocalsPlus[storeIndex] = frame.ValueStack.PopValue();    // 직접 PyValue 복사
```

**영향**: ExecuteInstruction을 통과하는 STORE_FAST에만 적용 (inline으로 처리 안 된 경우)
**예상 효과**: 전체 2-5% (inline이 대부분 처리하므로 제한적)
**회귀 위험**: 없음

---

## Phase 0.5: CallMagicMethod 캐시 도입 (위험도: 낮음) ★ 최대 임팩트

> dunder_methods 188x의 직접 원인. PyTypeSlots 없이도 즉시 개선 가능.

### 0.5.1 문제: `CallMagicMethod()` 가 `GlobalMethodCache`를 우회

**현재** (PyClass.cs:1581-1614):
```
CallMagicMethod("__add__", other)
  → InstanceDict.ContainsKey("__add__")  // miss (보통 클래스에 정의)
  → foreach MRO → ClassDict.TryGetValue  // O(MRO depth) 매번 반복
  → new PyMethod(this, func)             // 매번 힙 할당
  → boundMethod.Call(args, null)
```

**수정** (PyClass.cs:1581-1614):
```csharp
private PyObject CallMagicMethod(string methodName, params PyObject[] args)
{
    // 1. InstanceDict 조회 (보통 miss)
    if (InstanceDict.TryGetValue(methodName, out var instMethod))
    {
        return instMethod.Call(args, null);
    }

    // 2. GlobalMethodCache 사용 (O(1) 캐시 히트)
    //    LookupInMRO는 TypeMethodCache를 사용하므로 타입 버전 캐시됨
    var method = InstanceType.LookupInMRO(methodName);
    if (method == null)
        return null;

    // 3. 메서드 바인딩 (PyFunction → direct call, PyMethod 할당 제거)
    if (method is PyFunction func)
    {
        // PyMethod 할당 대신 직접 호출: func(self, *args)
        var fullArgs = new PyObject[args.Length + 1];
        fullArgs[0] = this;
        Array.Copy(args, 0, fullArgs, 1, args.Length);
        return func.Call(fullArgs, null);
    }
    else if (method is IDescriptor desc)
    {
        var bound = desc.Get(this, InstanceType);
        return bound.Call(args, null);
    }
    else if (method.IsCallable())
    {
        var fullArgs = new PyObject[args.Length + 1];
        fullArgs[0] = this;
        Array.Copy(args, 0, fullArgs, 1, args.Length);
        return method.Call(fullArgs, null);
    }

    return null;
}
```

**개선 포인트 3가지:**
1. `GlobalMethodCache` 사용 (`LookupInMRO`) → MRO 순회 O(n) → O(1) 캐시
2. `new PyMethod()` 제거 → `func.Call(fullArgs, null)` 직접 호출
3. `ContainsKey` + `[]` → `TryGetValue` (Dictionary 이중 조회 제거)

### 0.5.2 args 배열 할당 최적화 (추가)

**매 호출마다 `new PyObject[args.Length + 1]` 할당을 줄이기 위해:**

```csharp
// PyClassInstance에 재사용 가능한 버퍼 (단일 인자 magic method용)
[ThreadStatic]
private static PyObject[]? _magicMethodArgs2;  // [self, other]

private PyObject CallMagicMethod(string methodName, params PyObject[] args)
{
    // ... (위의 캐시 로직) ...

    if (method is PyFunction func)
    {
        if (args.Length == 1)
        {
            // 가장 흔한 케이스: __add__(self, other), __eq__(self, other) 등
            var buf = _magicMethodArgs2 ??= new PyObject[2];
            buf[0] = this;
            buf[1] = args[0];
            return func.Call(buf, null);
        }
        else
        {
            var fullArgs = new PyObject[args.Length + 1];
            fullArgs[0] = this;
            Array.Copy(args, 0, fullArgs, 1, args.Length);
            return func.Call(fullArgs, null);
        }
    }
    // ...
}
```

### 0.5.3 예상 효과

| Benchmark | 현재 | 예상 | 개선 이유 |
|-----------|-----:|-----:|----------|
| dunder_methods | 188x | **50-70x** | MRO 캐시 + PyMethod 할당 제거 |
| class_method | 145x | ~100-120x | __init__ 경로는 별도 |
| inheritance | 123x | ~80-100x | super() 경로는 별도 |

**이 Phase만으로 dunder_methods가 2.5-3.5x 빨라질 것으로 예상.**

---

## Phase 1: PyTypeSlots — CPython 슬롯 테이블 도입 (위험도: 낮음)

### 1.1 목표

CPython의 `PyTypeObject` 함수 포인터 슬롯을 C# delegate로 구현.
기존 virtual 메서드는 그대로 두고, 핫 패스에서 슬롯을 통해 우회.

### 1.2 PyTypeSlots 구조체 설계

**파일**: `core/PyTypeSlots.cs` (신규)

```csharp
// CPython: Include/cpython/object.h PyTypeObject 슬롯 대응
public class PyTypeSlots
{
    // === Number Protocol (CPython: PyNumberMethods) ===
    // CPython: Objects/abstract.c binary_op1() → nb_add 슬롯
    public Func<PyObject, PyObject, PyObject>? nb_add;
    public Func<PyObject, PyObject, PyObject>? nb_subtract;
    public Func<PyObject, PyObject, PyObject>? nb_multiply;
    public Func<PyObject, PyObject, PyObject>? nb_true_divide;
    public Func<PyObject, PyObject, PyObject>? nb_floor_divide;
    public Func<PyObject, PyObject, PyObject>? nb_remainder;
    public Func<PyObject, PyObject, PyObject>? nb_power;
    public Func<PyObject, PyObject, PyObject>? nb_lshift;
    public Func<PyObject, PyObject, PyObject>? nb_rshift;
    public Func<PyObject, PyObject, PyObject>? nb_and;
    public Func<PyObject, PyObject, PyObject>? nb_or;
    public Func<PyObject, PyObject, PyObject>? nb_xor;
    public Func<PyObject, PyObject>? nb_negative;
    public Func<PyObject, PyObject>? nb_positive;
    public Func<PyObject, PyObject>? nb_invert;
    public Func<PyObject, bool>? nb_bool;

    // === Inplace Number Protocol ===
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_add;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_subtract;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_multiply;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_true_divide;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_floor_divide;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_remainder;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_power;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_lshift;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_rshift;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_and;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_or;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_xor;
    public Func<PyObject, PyObject, PyObject?>? nb_inplace_matrix_multiply;

    // === Comparison (CPython: tp_richcompare) ===
    public Func<PyObject, PyObject, CompareOp, PyObject>? tp_richcompare;

    // === Attribute Access (CPython: tp_getattro, tp_setattro) ===
    public Func<PyObject, string, PyObject>? tp_getattro;
    public Action<PyObject, string, PyObject>? tp_setattro;

    // === Call Protocol (CPython: tp_call) ===
    public Func<PyObject, PyObject[], PyDict?, PyObject>? tp_call;

    // === Hashing (CPython: tp_hash) ===
    public Func<PyObject, int>? tp_hash;

    // === Repr/Str (CPython: tp_repr, tp_str) ===
    public Func<PyObject, PyStr>? tp_repr;
    public Func<PyObject, PyStr>? tp_str;

    // === Sequence Protocol (CPython: PySequenceMethods) ===
    public Func<PyObject, int>? sq_length;
    public Func<PyObject, PyObject, PyObject>? sq_concat;
    public Func<PyObject, PyObject, PyBool>? sq_contains;

    // === Mapping Protocol (CPython: PyMappingMethods) ===
    public Func<PyObject, PyObject, PyObject>? mp_subscript;
    public Action<PyObject, PyObject, PyObject>? mp_ass_subscript;

    // === Iterator Protocol (CPython: tp_iter, tp_iternext) ===
    public Func<PyObject, PyObject>? tp_iter;
    public Func<PyObject, PyObject>? tp_iternext;

    // === Descriptor Protocol (CPython: tp_descr_get, tp_descr_set) ===
    public Func<PyObject, PyObject?, PyType?, PyObject>? tp_descr_get;
    public Action<PyObject, PyObject, PyObject>? tp_descr_set;

    // === Instance Creation (CPython: tp_new, tp_init) ===
    public Func<PyType, PyObject[], PyDict?, PyObject>? tp_new;
    public Action<PyObject, PyObject[], PyDict?>? tp_init;
}
```

### 1.3 슬롯 초기화 — 기존 패턴 활용

**PyType.cs 생성자** (기존 PyType.cs:338-354, InitializeDescriptors() 패턴 활용):

```csharp
public class PyType : PyObject
{
    public PyTypeSlots Slots { get; private set; }

    internal PyType(string name, PyType[] baseTypes, string module, TypeKind kind)
    {
        // ... 기존 코드 ...
        InitializeDescriptors();  // 기존
        InitializeSlots();        // ← 추가: 바로 뒤에 호출
    }

    protected virtual void InitializeSlots()
    {
        Slots = new PyTypeSlots();
        PopulateSlots(Slots);
    }

    protected virtual void PopulateSlots(PyTypeSlots slots)
    {
        // 기본: 빈 슬롯 (virtual fallback 사용)
    }
}
```

### 1.4 빌트인 타입 슬롯 등록 (예: PyInt)

```csharp
// type/PyInt.cs — PyInt는 PyObject를 상속하지만, 타입은 PyType.IntType
// PyType.IntType의 PopulateSlots에서 등록

// core/PyType.cs의 InitializeDescriptors() switch문에 대응하여
// InitializeSlots()에서도 TypeKind별로 슬롯 등록
protected override void InitializeSlots()
{
    base.InitializeSlots();
    switch (_kind)
    {
        case TypeKind.Int:
            Slots.nb_add = static (a, b) => ((PyInt)a).Add(b);
            Slots.nb_subtract = static (a, b) => ((PyInt)a).Subtract(b);
            // ... 생략 ...
            break;
        case TypeKind.Float:
            Slots.nb_add = static (a, b) => ((PyFloat)a).Add(b);
            // ... 생략 ...
            break;
        case TypeKind.Str:
            Slots.nb_add = static (a, b) => ((PyStr)a).Add(b);
            Slots.nb_multiply = static (a, b) => ((PyStr)a).Multiply(b);
            break;
        // ... 다른 빌트인 타입 ...
    }
}
```

### 1.5 유저 정의 클래스 (PyClass) 슬롯

```csharp
// type/PyClass.cs
protected override void PopulateSlots(PyTypeSlots slots)
{
    // MRO에서 dunder 메서드를 찾아 슬롯에 바인딩
    // LookupInMRO는 GlobalMethodCache 사용 → O(1)

    var addMethod = LookupInMRO("__add__");
    if (addMethod != null)
    {
        slots.nb_add = (self, other) =>
        {
            // 직접 호출 (PyMethod 할당 없음)
            if (addMethod is PyFunction func)
                return func.Call(new[] { self, other }, null);
            if (addMethod is IDescriptor desc)
                return desc.Get(self, self.GetPyType()).Call(new[] { other }, null);
            return addMethod.Call(new[] { self, other }, null);
        };
    }

    // __init__ 캐싱
    var initMethod = LookupInMRO("__init__");
    if (initMethod is PyFunction initFunc)
    {
        slots.tp_init = (self, args, kwargs) =>
        {
            var fullArgs = new PyObject[args.Length + 1];
            fullArgs[0] = self;
            Array.Copy(args, 0, fullArgs, 1, args.Length);
            initFunc.Call(fullArgs, kwargs);
        };
    }

    // __new__ 캐싱
    var newMethod = LookupInMRO("__new__");
    if (newMethod != null)
    {
        slots.tp_new = (type, args, kwargs) =>
        {
            // ... cached new dispatch ...
            return type.CreateDefaultInstance();
        };
    }
}
```

### 1.6 슬롯 무효화 — TypeVersionTag 연동

```csharp
// core/PyType.cs
public void InvalidateTypeCache()
{
    _typeVersionTag = _nextVersionTag++;  // 기존
    InitializeSlots();                     // ← 추가: 슬롯 재구축
}
```

**무효화 호출처 3곳 모두 커버됨:**
- `PyType.cs:1555` (type setattr) → `InvalidateTypeCache()` 호출
- `PyClass.cs:601` (attribute set) → `InvalidateTypeCache()` 호출
- `PyClass.cs:612` (setattr) → `InvalidateTypeCache()` 호출

### 1.7 예상 효과

| 항목 | 변경 전 | 변경 후 | 개선 |
|------|--------|--------|------|
| `left.Add(right)` (빌트인) | vtable lookup | `type.Slots.nb_add(left, right)` delegate | ~10-20% |
| `left.Add(right)` (유저클래스) | vtable → CallMagicMethod → MRO | `type.Slots.nb_add` (캐시된 delegate) | ~60-70% |
| `obj.Call(args)` (PyClass) | isinstance 체인 + LookupInMRO×2 | `type.Slots.tp_new + tp_init` | ~30-50% |

---

## Phase 2: VM 핫 패스 슬롯 디스패치 (위험도: 중간)

### 2.1 BINARY_OP 슬롯 디스패치

**현재** (PyVM.cs ExecuteBinaryOpType, ~line 6679):
```csharp
case BinaryOpType.ADD:
    result = left.Add(right);  // virtual dispatch → PyClassInstance일 때 188x 느림
    break;
```

**수정**:
```csharp
case BinaryOpType.ADD:
{
    var leftType = left.GetPyType();
    var slot = leftType.Slots?.nb_add;
    if (slot != null)
    {
        result = slot(left, right);
        if (result != null && result is not PyNotImplemented)
            break;
    }
    result = left.Add(right);  // fallback
    break;
}
```

**점진적 적용**: 슬롯이 없으면 기존 virtual 경로 사용. 회귀 위험 없음.

### 2.2 BINARY_OP 타입 특화 fast path 확장

**현재**: int+int, float+float만 PyValue fast path 존재
**추가 대상**: str+str, list+list

```csharp
// BINARY_OP ADD에서 PyValue fallback 전에 추가
if (lv.IsObject && rv.IsObject)
{
    var lo = lv.ObjRef;
    var ro = rv.ObjRef;

    // str + str fast path (CPython: unicode_concatenate)
    if (lo is PyStr ls && ro is PyStr rs)
    {
        frame.Stack.PushValue(PyValue.FromObject(ls.Concat(rs)));
        break;
    }

    // list + list fast path
    if (lo is PyList ll && ro is PyList rl)
    {
        frame.Stack.PushValue(PyValue.FromObject(ll.Add(rl)));
        break;
    }
}
```

### 2.3 CALL 슬롯 디스패치 (인스턴스 생성 최적화)

**현재** (PyVM.cs CALL handler, ~line 2280):
```csharp
if (actualCallable is PyBuiltinFunction builtin) { ... }
else if (actualCallable is PyMethod method) { ... }
else if (actualCallable is PyFunction func) { ... }
else { actualCallable.Call(args, null); }
```

**수정 (PyClass 인스턴스 생성 fast path 추가)**:
```csharp
// 기존 instanceof 체인 앞에 추가
if (actualCallable is PyClass pyClass)
{
    var slots = pyClass.Slots;
    if (slots?.tp_new != null && slots.tp_init != null)
    {
        var instance = slots.tp_new(pyClass, args, kwargs);
        slots.tp_init(instance, args, kwargs);
        frame.Stack.Push(instance);
        break;
    }
}
// 기존 instanceof 체인 유지 (fallback)
```

### 2.4 예상 효과

| Benchmark | 현재 배율 | 예상 배율 | 개선율 |
|-----------|------:|------:|------:|
| dunder_methods | 188x | **40-60x** | 65-80% |
| class_method | 145x | **60-80x** | 40-55% |
| inheritance | 123x | **50-70x** | 40-55% |
| string_ops | 189x | **80-100x** | 45-55% |
| list_ops | 131x | **60-80x** | 40-55% |

---

## Phase 3: Inline Cache — 바이트코드 사이트별 캐싱 (위험도: 중간)

### 3.1 개념

CPython 3.12의 PEP 659 적응적 특화(Adaptive Specialization):
- 바이트코드 **위치마다** 캐시 엔트리 보유
- 첫 실행: 제네릭 → 타입 관찰 → 캐시 세팅
- 이후 실행: 타입 버전 태그 확인 → 캐시 히트 시 직접 접근

### 3.2 InlineCache 구조

```csharp
// runtime/InlineCache.cs (신규)
public struct InlineCacheEntry
{
    public ulong TypeVersionTag;    // 캐시된 타입의 버전
    public PyObject CachedValue;    // 캐시된 속성/메서드/descriptor
    public int SlotIndex;           // 인스턴스 dict에서의 키 (LOAD_ATTR_INSTANCE_VALUE용)
}

// PyCodeObject에 인라인 캐시 배열 추가
public class PyCodeObject
{
    public InlineCacheEntry[] InlineCaches;  // Instructions.Count 크기
}
```

### 3.3 LOAD_ATTR Inline Cache

```csharp
case ByteCodeOp.LOAD_ATTR:
{
    var obj = frame.Stack.Pop();
    var nameIndex = arg >> 1;
    var pushNull = (arg & 1) != 0;
    var attrName = frame.Code.Names[nameIndex];

    // === Inline Cache Check ===
    ref var cache = ref frame.Code.InlineCaches[frame.InstructionPointer];
    var objType = obj.GetPyType();

    if (cache.TypeVersionTag == objType.TypeVersionTag && cache.CachedValue != null)
    {
        // Cache HIT: 타입 버전 일치 → 캐시된 descriptor 사용
        var cached = cache.CachedValue;
        if (cached is IDescriptor desc)
        {
            var attr = desc.Get(obj, objType);
            // push with method binding logic...
        }
        else
        {
            // push cached directly...
        }
        break;
    }

    // Cache MISS: 기존 경로 + 캐시 업데이트
    var result = obj.GetAttribute(attrName);
    cache.TypeVersionTag = objType.TypeVersionTag;
    cache.CachedValue = result;  // descriptor 자체를 캐싱 (바인딩은 매번)
    // push result with method binding logic...
    break;
}
```

### 3.4 주의: descriptor 캐싱 전략

**캐싱 대상은 descriptor 자체이지, 바인딩 결과가 아님.**
- `cache.CachedValue = func` (PyFunction) → 매번 `func.Call([self, ...])` 직접 호출
- descriptor의 `Get()` 호출은 불가피하지만, MRO 탐색을 건너뜀
- 타입이 변경되면 TypeVersionTag 불일치 → 캐시 미스 → 재탐색

### 3.5 예상 효과

| Benchmark | Phase 2 배율 | Phase 3 배율 | 개선율 |
|-----------|------:|------:|------:|
| attribute_access | ~60x | **20-30x** | 50-65% |
| class_method | ~70x | **40-50x** | 30-40% |
| inheritance | ~60x | **35-45x** | 30-40% |
| dunder_methods | ~50x | **35-45x** | 20-30% |

---

## Phase 4: 추가 최적화 (위험도: 낮음)

### 4.1 COMPARE_OP 슬롯 디스패치

**현재**: PyValue fast path 이후, `left.RichCompare(right, op)` virtual 호출.
**수정**: `type.Slots.tp_richcompare(left, right, op)` 사용.

### 4.2 BINARY_SUBSCR 타입 특화

**현재**: 항상 virtual `GetItem()` 호출.
**수정**: `list[int]`, `dict[str]`, `tuple[int]` fast path 추가.

```csharp
case ByteCodeOp.BINARY_SUBSCR:
{
    var key = frame.Stack.PopValue();
    var container = frame.Stack.PopValue();

    // list[int] fast path
    if (container.IsObject && container.ObjRef is PyList list && key.IsInt64)
    {
        var index = (int)key.AsInt64;
        if (index < 0) index += list.Count;
        frame.Stack.Push(list.GetItemDirect(index));
        break;
    }

    // dict[str] fast path
    if (container.IsObject && container.ObjRef is PyDict dict
        && key.IsObject && key.ObjRef is PyStr strKey)
    {
        frame.Stack.Push(dict.GetItemDirect(strKey));
        break;
    }

    // fallback
    frame.Stack.Push(container.ToObject().GetItem(key.ToObject()));
    break;
}
```

### 4.3 FOR_ITER_RANGE 특화

**현재**: list, tuple에만 FOR_ITER 특화 존재. range 없음.
**수정**: `FOR_ITER_RANGE` 추가 (PyRangeIterator 직접 접근).

---

## 5. 구현 순서 및 검증 계획

### 5.1 구현 순서

```
Phase 0   (STORE_FAST 수정)           → 벤치마크 → 회귀 테스트
   ↓
Phase 0.5 (CallMagicMethod 캐시) ★    → 벤치마크 → 회귀 테스트
   ↓
Phase 1   (PyTypeSlots 도입)          → 벤치마크 → 회귀 테스트
   ↓
Phase 2   (VM 슬롯 디스패치)           → 벤치마크 → 회귀 테스트
   ↓
Phase 3   (Inline Cache)             → 벤치마크 → 회귀 테스트
   ↓
Phase 4   (추가 fast path)            → 벤치마크 → 회귀 테스트
```

### 5.2 회귀 테스트 체크리스트

```bash
# 매 Phase 완료 후 반드시 실행
dotnet run -c Release test_comprehensive_python312.py
dotnet run -c Release test_ultimate_complex_features.py
dotnet run -c Release tests/level5_functions_advanced.py
dotnet run -c Release tests/level6_classes.py         # 클래스 관련 핵심
dotnet run -c Release tests/level7_comprehensions.py
dotnet run -c Release tests/level8_generators.py
dotnet run -c Release tests/level9_exceptions.py
dotnet run -c Release benchmark_comprehensive.py       # 성능 측정
```

### 5.3 각 Phase별 롤백 전략

- **Phase 0**: STORE_FAST 1줄 변경 → git revert
- **Phase 0.5**: CallMagicMethod 함수 본문만 변경 → git revert
- **Phase 1**: PyTypeSlots는 신규 파일 + PyType에 프로퍼티 추가 → 파일 삭제 + revert
- **Phase 2**: VM 코드 `if (slot != null) { ... } else { 기존코드 }` → else가 항상 fallback
- **Phase 3**: InlineCache 배열 추가 → null 체크로 비활성화 가능
- **Phase 4**: 타입 특화 fast path → 기존 코드 그대로 보존, 새 분기만 추가

---

## 6. 위험 요소 및 대응

### 6.1 C# delegate 호출 비용

**우려**: `Func<>` delegate 호출이 virtual method보다 느릴 수 있음.
**대응**:
- `static` 람다 사용 (클로저 캡처 없음, 인라인 가능)
- Phase 1 직후 micro-benchmark로 실측
- 대안: `delegate*` (unmanaged function pointer) 사용 가능 (unsafe 코드)
- 최악의 경우: delegate 대신 interface 기반 슬롯으로 전환

### 6.2 타입 변경 시 슬롯 무효화

**우려**: 유저 클래스의 ClassDict가 런타임에 변경되면 슬롯 불일치.
**대응**: `InvalidateTypeCache()` 호출 시 `InitializeSlots()` 재호출.
**확인 완료**: 3곳 (PyType.cs:1555, PyClass.cs:601, 612) 모두 커버.

### 6.3 상속 체인의 슬롯 전파

**우려**: `class Child(Parent)` — Child가 `__add__`를 override하지 않으면 Parent의 슬롯 사용.
**대응**: `LookupInMRO()` 가 MRO 순서로 탐색하므로, 슬롯 등록 시 자동으로 부모 메서드 상속.
**확인 완료**: `InheritSlotMethods()` (PyClass.cs:225-257) 인프라 이미 존재.

### 6.4 메타클래스 호환성

**우려**: `type.__call__` 등의 메타프로그래밍.
**대응**: 메타클래스가 있는 경우 슬롯 비활성화 (Slots = null), 기존 virtual 경로 사용.
**판단**: 메타클래스는 드물고, 정확성이 우선.

### 6.5 `params PyObject[] args` 할당 비용

**우려**: `CallMagicMethod`의 `params` 키워드가 매 호출마다 배열 생성.
**대응**: Phase 0.5에서 `[ThreadStatic]` 버퍼 재사용으로 할당 제거.

---

## 7. 목표 성능

| Phase | 예상 전체 배율 | 절대 시간 | 주요 개선 항목 |
|-------|----------:|-------:|-------------|
| 현재 | 89.7x | 1486ms | - |
| Phase 0 | ~87x | ~1440ms | STORE_FAST |
| Phase 0.5 | ~70x | ~1160ms | dunder_methods ★ |
| Phase 1+2 | ~45-55x | ~745-910ms | 전체 슬롯 디스패치 |
| Phase 3 | ~30-40x | ~500-660ms | LOAD_ATTR 인라인 캐시 |
| Phase 4 | ~25-35x | ~415-580ms | SUBSCR, COMPARE, FOR_ITER |

**최종 목표**: 전체 배율 25-35x (현재 89.7x에서 60-70% 개선)

---

## 8. 코드 리뷰 체크리스트 (검토 완료 항목)

### 8.1 Phase 0 ✅

- [x] PyStack.PopValue()는 PyValue를 변환 없이 직접 반환 (PyStack.cs:107-114)
- [x] STORE_FAST inline 경로 (PyVM.cs:1222-1231): **이미 PopValue() 사용** → 수정 불필요
- [x] STORE_FAST ExecuteInstruction (PyVM.cs:1660-1672): **Pop() + FromObject()** → 수정 필요

### 8.2 Phase 0.5 ✅

- [x] CallMagicMethod (PyClass.cs:1581-1614): GlobalMethodCache 미사용 확인
- [x] LookupInMRO (PyClass.cs:270-275): GlobalMethodCache 사용 확인
- [x] PyClassInstance.Add() 등 (PyClass.cs:1644-1714): BinaryOpWithMagicMethod → CallMagicMethod 경로 확인
- [x] 매 호출마다 new PyMethod 할당 확인 (PyClass.cs:1598)

### 8.3 Phase 1 ✅

- [x] PyType 생성자 (PyType.cs:338-354): InitializeDescriptors() 뒤에 InitializeSlots() 추가 가능
- [x] PyClass 생성자 (PyClass.cs:60-99): base.PyType() → InheritSlotMethods() 순서
- [x] TypeVersionTag (PyType.cs:143-168): ulong, 단조 증가
- [x] InvalidateTypeCache 호출처: 3곳 모두 확인
- [ ] `Func<>` delegate vs virtual method 성능 비교 → Phase 1 직후 micro-benchmark

### 8.4 Phase 2 확인 필요

- [x] PyClassInstance가 Add() override → BinaryOpWithMagicMethod → CallMagicMethod 경로 확인
- [ ] TryReverseBinaryOp 경로가 슬롯에서도 동작하는지 확인 (reflected ops: __radd__ 등)
- [ ] CALL handler의 모든 분기 확인 (특히 keyword args 경로)
- [ ] LOAD_ATTR의 pushNullForMethod 로직과 슬롯 호환성 확인

### 8.5 Phase 3 확인 필요

- [ ] InlineCacheEntry 크기가 GC 압력에 미치는 영향 (struct → 스택 할당)
- [ ] 타입 버전 태그 충돌 가능성 (ulong → 사실상 무한)
- [ ] 캐시 무효화 시점: ClassDict 변경 ✅, 상속 체인 변경 (미확인), 메타클래스 변경 (미확인)
