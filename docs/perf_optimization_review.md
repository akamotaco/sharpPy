# 성능 최적화 계획 코드 리뷰 보고서

> 작성: 2026-03-09 | 대상: `docs/perf_optimization_plan.md`

---

## 리뷰 결과 요약

| 항목 | 판정 | 위험도 |
|------|------|--------|
| Phase 0 (STORE_FAST) | ✅ 문제 없음 | 최소 |
| Phase 0.5 (CallMagicMethod) | ⚠️ 동작 변경 위험 | **높음** |
| Phase 1 (PyTypeSlots) | ❌ 재설계 필요 | 높음 |
| Phase 2 (VM 슬롯 디스패치) | ⚠️ Phase 1 의존 | 중간 |
| Phase 3 (Inline Cache) | ⚠️ InstanceDict 주의 | 중간 |
| Phase 4 (추가 fast path) | ✅ 문제 없음 | 낮음 |

---

## 발견 1: ★ `LookupInMRO`와 `CallMagicMethod`의 탐색 범위 불일치 (Phase 0.5 블로커)

### 현상

`CallMagicMethod` (PyClass.cs:1591)는 **PyClass.ClassDict만** 검색:
```csharp
// PyClass.cs:1591-1593 — 현재 코드
foreach (var mroType in InstanceType.MRO)
{
    if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(methodName, out PyObject method))
    //  ↑ PyClass만 검사, PyType은 건너뜀
```

`LookupInMRO` (PyClass.cs:270) → `SearchMRO` (PyType.cs:64-103)는 **ClassDict + TypeDict + Reflection** 검색:
```csharp
// PyType.cs:66-99 — SearchMRO
foreach (var mroType in type.MRO)
{
    // 1. PyClass.ClassDict 검사
    if (mroType is PyClass customType && customType.ClassDict.TryGetValue(name, out var value))
        return value;

    // 2. PyType.TypeDict 검사 ← CallMagicMethod에는 없음!
    if (mroType.TypeDict != null && mroType.TypeDict.TryGetValue(name, out var typeValue))
        return typeValue;

    // 3. Reflection으로 GetTypeAttribute 호출 ← CallMagicMethod에는 없음!
    try {
        var getTypeAttrMethod = typeof(SharpPy.PyClass).GetMethod("GetTypeAttribute", ...);
        if (getTypeAttrMethod != null) {
            var result = (PyObject)getTypeAttrMethod.Invoke(null, new object[] { mroType, name });
            if (result != null) return result;
        }
    } catch { }
}
```

### 문제: 빌트인 서브클래스에서 동작 변경

```python
class MyList(list):
    pass

ml = MyList([1, 2])
result = ml + [3, 4]  # __add__ 호출
```

| 경로 | 동작 |
|------|------|
| **현재 CallMagicMethod** | MRO=[MyList, list, object] → MyList.ClassDict에 `__add__` 없음 → `list`는 PyType이라 **건너뜀** → null 반환 → `base.Add()` fallback |
| **수정 후 LookupInMRO** | MRO=[MyList, list, object] → MyList.ClassDict 없음 → `list.TypeDict`에서 `__add__` **발견** → TypeDict의 빌트인 descriptor 호출 |

**결론**: 동작이 바뀔 수 있음. 빌트인 서브클래스(MyList, MyDict 등)에서 `__add__` 등이 이중으로 호출되거나, 기존에 `base.Add()`로 가던 경로가 TypeDict descriptor로 변경됨.

### 해결 방안

**방안 A (안전)**: LookupInMRO 대신, ClassDict만 검색하되 GlobalMethodCache를 활용하는 전용 캐시 사용:
```csharp
private PyObject LookupMagicMethodInClassDict(string name)
{
    // GlobalMethodCache와 동일한 캐시 구조지만 ClassDict만 검색
    foreach (var mroType in InstanceType.MRO)
    {
        if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out var method))
            return method;
    }
    return null;
}
```

**방안 B (통합)**: 모든 magic method 경로를 LookupInMRO로 통일하고, 빌트인 서브클래스 회귀 테스트 후 적용.

---

## 발견 2: ★ `SearchMRO`에 Reflection 존재 — 캐시 미스마다 호출 (성능 문제)

### 현상

`PyType.cs:83-99`에서 **매 캐시 미스마다 Reflection 호출**:
```csharp
// PyType.cs:83-99 — SearchMRO 내부
try
{
    var getTypeAttrMethod = typeof(SharpPy.PyClass).GetMethod(
        "GetTypeAttribute",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
    );
    if (getTypeAttrMethod != null)
    {
        var result = (PyObject)getTypeAttrMethod.Invoke(null, new object[] { mroType, name });
        if (result != null)
            return result;
    }
}
catch { /* Ignore reflection errors */ }
```

**문제**: `typeof(...).GetMethod(...)` + `Invoke()` = **수십~수백 μs**. MRO 항목마다 호출됨.

### 영향

LookupInMRO를 사용하는 Phase 0.5에서, 캐시 미스가 발생하면 현재의 수동 MRO 순회보다 **더 느려질 수 있음** (Reflection 오버헤드 추가).

### 해결 방안

SearchMRO의 Reflection을 정적 delegate로 캐시:
```csharp
private static readonly Func<PyType, string, PyObject> _getTypeAttribute;
static TypeMethodCache()
{
    var method = typeof(SharpPy.PyClass).GetMethod("GetTypeAttribute", ...);
    if (method != null)
        _getTypeAttribute = (Func<PyType, string, PyObject>)
            Delegate.CreateDelegate(typeof(Func<PyType, string, PyObject>), method);
}
```

---

## 발견 3: ★ magic method 구현 6곳 일관성 부재

PyClassInstance에서 magic method를 찾는 코드가 **6개의 서로 다른 패턴**으로 구현:

| 메서드 | 위치 | ClassDict | TypeDict | object 중지 | 캐시 |
|--------|------|:---------:|:--------:|:----------:|:----:|
| `CallMagicMethod` | :1581 | ✅ | ❌ | ❌ | ❌ |
| `ToRepr` | :2228 | ✅ | ❌ | ✅ | ❌ |
| `ToStr` | :2279 | ✅ | ✅ | ✅ | ❌ |
| `RichCompare` | :2343 | ✅ | ❌ | ✅ | ❌ |
| `GetItem` | :1303 | ✅ | ❌ | ❌ | ❌ |
| `GetIterator` | :1465 | ✅ | ✅ | ❌ | ❌ |

**문제**: 각각 다른 탐색 범위와 종료 조건.
- `ToStr`만 TypeDict 검사 (빌트인 `__str__` 지원)
- `ToRepr`/`RichCompare`는 `object` 타입에서 중지
- `CallMagicMethod`/`GetItem`은 조건 없이 전체 MRO 순회
- **전부 캐시 없음** — 매번 MRO 순회 + `new PyMethod()` 할당

### 해결 방안

**모든 magic method lookup을 하나의 통합 메서드로 리팩토링:**
```csharp
/// <summary>
/// 통합 magic method 조회: ClassDict만 검색 (현재 동작 보존)
/// GlobalMethodCache와 별도의 MagicMethodCache 사용
/// </summary>
private PyObject FindMagicMethod(string name, bool stopAtObject = false)
{
    // 1. InstanceDict 확인
    if (InstanceDict.TryGetValue(name, out var instMethod))
        return instMethod;

    // 2. MRO에서 ClassDict만 검색 (캐시 적용)
    foreach (var mroType in InstanceType.MRO)
    {
        if (stopAtObject && mroType.Name == "object")
            break;
        if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out var method))
            return method;
    }
    return null;
}

/// <summary>
/// 찾은 method를 self에 바인딩하여 호출 (PyMethod 할당 제거)
/// </summary>
private PyObject InvokeBoundMethod(PyObject method, PyObject[] args)
{
    if (method is PyFunction func)
    {
        // PyMethod 할당 대신 직접 호출
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

---

## 발견 4: ★ PyTypeSlots (Phase 1)의 근본적 문제 — 재설계 필요

### 문제 A: C# delegate가 virtual보다 빠르지 않음

.NET JIT 컴파일러는 **virtual method를 인라인할 수 있지만**, delegate 호출은 **항상 간접 호출**:

```
Virtual method: obj → vtable → method → inline 가능 (JIT devirtualization)
Delegate:       obj → type → slots → delegate → target → invoke (인라인 불가)
```

**결론**: 빌트인 타입(`PyInt.Add`, `PyStr.Add` 등)에서는 delegate 슬롯이 virtual보다 **느릴 수 있음**.

### 문제 B: 생성자에서 virtual 호출 시 ClassDict 미초기화

```csharp
// PyType 생성자 (PyType.cs:338-354)
internal PyType(string name, PyType[] baseTypes, string module, TypeKind kind)
{
    // ... 기존 초기화 ...
    InitializeDescriptors();  // 기존: 여기서 TypeDict 세팅
    InitializeSlots();        // 추가 예정: 여기서 ClassDict 접근?
}

// PyClass 생성자 (PyClass.cs:60-99)
public PyClass(...) : base(name, baseTypes, module)  // ← PyType 생성자 먼저 실행
{
    ClassDict = new Dictionary<string, PyObject>(classDict);  // ← 이후에 ClassDict 초기화!
}
```

**PyType 생성자의 `InitializeSlots()` 시점에서 PyClass의 ClassDict는 아직 null!**
`PopulateSlots`에서 `ClassDict`에 접근하면 `NullReferenceException`.

### 문제 C: 실제 제거되는 virtual 호출이 적음

전체 84개 virtual 메서드 중, PyVM 핫 패스에서 호출되는 것은 ~15-20개.
이 중 PyValue fast path로 이미 우회되는 것을 제외하면 **실제 슬롯으로 대체 가능한 것은 극히 적음**.

**결론**: Phase 1(PyTypeSlots)는 **비용 대비 효과가 낮음**. 재설계 필요.

### 대안: Phase 1을 "magic method 캐시"로 변경

PyTypeSlots 대신, magic method lookup 캐시를 PyClass에 직접 구현:

```csharp
public class PyClass : PyType
{
    // Magic method 캐시 (타입 생성 후 lazy 초기화)
    private Dictionary<string, PyObject> _magicMethodCache;
    private ulong _magicMethodCacheVersion;

    public PyObject GetCachedMagicMethod(string name)
    {
        if (_magicMethodCache != null && _magicMethodCacheVersion == TypeVersionTag)
        {
            if (_magicMethodCache.TryGetValue(name, out var cached))
                return cached;
        }

        // 캐시 미스: MRO에서 ClassDict만 검색
        var method = FindInClassDictMRO(name);

        // 캐시 업데이트
        _magicMethodCache ??= new Dictionary<string, PyObject>();
        _magicMethodCacheVersion = TypeVersionTag;
        _magicMethodCache[name] = method;
        return method;
    }

    private PyObject FindInClassDictMRO(string name)
    {
        foreach (var mroType in MRO)
        {
            if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out var method))
                return method;
        }
        return null;
    }
}
```

**장점**:
- 생성자에서 virtual 호출 불필요 (lazy 초기화)
- ClassDict 범위만 검색 (기존 동작 보존)
- TypeVersionTag로 무효화
- PyTypeSlots보다 훨씬 단순

---

## 발견 5: Inline Cache의 InstanceDict 문제 (Phase 3)

### 현상

TypeVersionTag는 **클래스 속성** 변경 시에만 갱신됨.
**인스턴스 속성**(`a.x = 1`)은 TypeVersionTag를 변경하지 않음.

```python
class Foo:
    x = "class_attr"

a = Foo()
b = Foo()
print(a.x)  # "class_attr" → 캐시됨

a.x = "instance_attr"  # InstanceDict에 추가, TypeVersionTag 변경 안 됨!
print(a.x)  # 캐시 히트 → "class_attr" 반환?? (버그)
```

### 해결 방안

Inline cache 히트 시 반드시 InstanceDict 확인:
```csharp
// 캐시 히트 조건에 InstanceDict 체크 추가
if (cache.TypeVersionTag == objType.TypeVersionTag
    && cache.CachedValue != null
    && (!(obj is IInstanceDictAccessor acc) || !acc.InstanceDict.ContainsKey(attrName)))
{
    // 안전하게 캐시 사용 (클래스 속성만)
}
```

**주의**: InstanceDict.ContainsKey 비용이 추가되므로, 캐시 효과가 감소할 수 있음.

---

## 발견 6: `new PyMethod()` 할당이 모든 magic method에서 반복

`CallMagicMethod` 뿐 아니라 **ToRepr, ToStr, RichCompare, GetItem** 전부 동일 패턴:
```csharp
var boundMethod = new PyMethod(this, func);  // 매번 힙 할당
return boundMethod.Call(args, null);
```

**해결**: `PyMethod` 할당 없이 `func.Call([self, ...args])` 직접 호출로 통합.
이것만으로도 GC 압력이 크게 감소.

---

## 수정된 Phase 구조 제안

기존 계획의 문제점을 반영한 수정안:

### Phase 0: STORE_FAST 수정 (변경 없음) ✅

### Phase 0.5 → Phase 1로 승격: Magic Method 통합 리팩토링

**목표**: 6개 중복 MRO 탐색을 1개 통합 메서드로 리팩토링
**범위**:
1. `FindMagicMethod()` + `InvokeBoundMethod()` 통합 메서드 생성
2. `CallMagicMethod`, `ToRepr`, `ToStr`, `RichCompare`, `GetItem` 등에 적용
3. `new PyMethod()` 할당 제거 → `func.Call([self, ...])` 직접 호출
4. InstanceDict.ContainsKey → TryGetValue 변경

**주의**: 탐색 범위(ClassDict only vs ClassDict+TypeDict)는 **기존 동작 유지**

### Phase 2 (신규): Magic Method 캐시 도입

**목표**: MRO 순회를 TypeVersionTag 기반 캐시로 대체
**구현**: PyClass에 `_magicMethodCache` Dictionary 추가 (lazy 초기화)
**장점**: PyTypeSlots보다 단순, 생성자 문제 없음, 기존 동작 보존

### Phase 3 (기존 Phase 1 대체): PyTypeSlots → 빌트인 타입 전용

**재설계**: 유저 클래스는 Phase 2의 캐시 사용, 빌트인 타입만 슬롯 적용
**범위 축소**: nb_add, nb_subtract 등 산술 연산만 대상 (attribute access 제외)
**구현**: PyType.InitializeDescriptors() 내부에서 등록 (virtual 호출 불필요)

### Phase 4 (기존 Phase 3): Inline Cache

**변경**: InstanceDict 체크 필수 (발견 5)
**범위**: LOAD_ATTR만 우선 적용

### Phase 5 (기존 Phase 4): 추가 fast path (변경 없음) ✅

---

## SearchMRO Reflection 문제 (추가 발견)

### 현상

`PyType.cs:83-99` — SearchMRO에서 **매 캐시 미스마다** `typeof(PyClass).GetMethod("GetTypeAttribute")` Reflection 호출.
MRO 항목마다 호출되므로, depth 3인 경우 캐시 미스 1회에 Reflection 3회.

### 영향

- Phase 0.5에서 LookupInMRO를 사용할 경우, 캐시 미스 시 Reflection 경유
- 현재 CallMagicMethod는 Reflection 경유하지 않으므로, **LookupInMRO 전환 시 성능 악화 가능**

### 해결

Phase 0 또는 Phase 1 시작 전에 SearchMRO의 Reflection을 정적 캐시로 교체:
```csharp
private static readonly Func<PyType, string, PyObject> _getTypeAttributeFunc;
static TypeMethodCache()
{
    var method = typeof(SharpPy.PyClass).GetMethod("GetTypeAttribute",
        BindingFlags.Public | BindingFlags.Static);
    if (method != null)
        _getTypeAttributeFunc = (type, name) =>
            (PyObject)method.Invoke(null, new object[] { type, name });
}
```

---

## 최종 판정

| 질문 | 답변 |
|------|------|
| 클래스 구조는 얼마나 감소하는가? | virtual 메서드 84개는 유지. magic method 경로의 **MRO 순회 + PyMethod 할당**이 제거됨. 클래스 자체를 제거하는 것이 아니라 **핫 패스에서 우회**하는 전략 |
| 회귀 문제가 발생하지 않았는가? | ⚠️ Phase 0.5에서 `LookupInMRO`로 직접 교체하면 **빌트인 서브클래스 동작 변경** (발견 1). ClassDict 전용 탐색으로 수정 필요 |
| 현재 코드에 문제 없이 통합되는가? | ❌ Phase 1(PyTypeSlots)은 **생성자 초기화 순서 문제** (발견 4B). magic method 캐시로 대체 필요 |
| 누락된 문제나 모순되는 문제는 있는가? | ⚠️ magic method 구현 6곳의 **일관성 부재** (발견 3). 통합 리팩토링 선행 필요 |
| 그 외의 잠재적인 문제가 있는가? | ⚠️ SearchMRO Reflection (발견 2), Inline Cache InstanceDict (발견 5), delegate vs virtual 성능 (발견 4A) |
