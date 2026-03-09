# SharpPy 성능 최적화 계획 v2: 클래스 구조 제거

> 작성: 2026-03-09 | 상태: Phase 4 일부 완료
> 이전 문서: `perf_optimization_plan.md` (Phase 0-3 완료, 89.7x → 59.9x)

## 1. 현재 상태 (Phase 0-3 완료 후)

### 1.1 벤치마크 결과 (best-of-2)

| Benchmark | CPython (ms) | SharpPy (ms) | 배율 | 핵심 병목 |
|-----------|----------:|----------:|------:|------|
| dunder_methods | 1.90 | 182 | **96x** | PyMethod 할당 + args 배열 |
| class_method | 1.00 | 79 | **79x** | Instance 생성 + PyMethod + Frame |
| inheritance | 0.67 | 51 | **76x** | 깊은 MRO + super() |
| function_call | 0.53 | 36 | **68x** | Frame 생성 (5-7개 heap alloc) |
| attribute_access | 0.53 | 28 | **53x** | GetAttribute MRO 3회 순회 |
| **TOTAL** | **15.95** | **851** | **53x** | |

### 1.2 이전 최적화 이력

| Phase | 내용 | 비율 | 변화 |
|-------|------|-----:|-----:|
| Baseline | 초기 상태 | 89.7x | - |
| Phase 0 | STORE_FAST `Pop()+FromObject()` → `PopValue()` | 62.1x | **-30.8%** |
| Phase 1 | Magic Method 6패턴 통합 + PyMethod 할당 제거 | 59.6x | -4.0% |
| Phase 2 | `GetCachedMagicMethod()` O(1) 캐시 | ~59.9x | ~0% |
| Phase 3 | `GetAttribute` 캐시 + LOAD_ATTR fast path | ~59.9x | ~0% |

---

## 2. 근본 원인 분석: CPython vs SharpPy 핵심 차이

### 2.1 PyMethod 할당 — 가장 큰 차이

```
CPython:  _PyObject_GetMethod → raw function pointer 반환, PyMethod 생성 없음
          CALL → args 포인터 shift로 self 포함 (zero allocation)

SharpPy:  GetAttribute → new PyMethod(this, func) 매번 heap 할당
          CALL → new PyObject[args+1] + Array.Copy
```

**발견된 `new PyMethod()` 사이트 전수 조사:**

| 우선순위 | 파일:라인 | 빈도 | 컨텍스트 | 제거 가능? |
|---------|-----------|------|----------|-----------|
| **HOT** | PyVM.cs:2977 | 모든 method call | LOAD_ATTR fast path | O — [self, func] push |
| **HOT** | PyFunction.cs:597 | 모든 method call | descriptor Get() | O — LOAD_ATTR에서 bypass |
| **HOT** | PyClass.cs:2059 | 모든 attr access→method | GetAttributeGeneric | O — LOAD_ATTR에서 bypass |
| **HOT** | PyClass.cs:1563 | 모든 for 루프 | TryNext() __next__ | O — InvokeMagicMethod 패턴 |
| **WARM** | PyClass.cs:238 | 모든 인스턴스 생성 | __init__ binding | O — func.Call 직접 |
| **WARM** | PyClass.cs:1879 | 모든 descriptor access | __get__ protocol | △ — descriptor 의미론 |
| **WARM** | PyClass.cs:2187 | context manager | __enter__/__exit__ | O — InvokeMagicMethod |
| **COLD** | PyVM.cs:3196-3235 | super() call | LOAD_SUPER_ATTR | O — 낮은 우선순위 |
| **COLD** | PyClassmethod.cs:34 | @classmethod | descriptor | X — 의미론 필수 |

### 2.2 Frame 생성 — 68x 차이의 원인

```
CPython:  _PyFrame_PushUnchecked → 스택 포인터 bump (~20ns)
          args memcpy → localsplus (포인터 1회)

SharpPy:  new PyFrame() → 5-7개 heap allocation (~900ns)
```

**프레임 생성 시 할당 전수 조사:**

| # | 할당 | 위치 | 비용 | 제거 가능? |
|---|------|------|------|-----------|
| 1 | `new PyFrame(...)` | PyVM.cs:8443 | ~200B 객체 | O — frame pooling |
| 2 | `new PyStack()` → `new PyValue[16]` | PyFrame:133 | 객체+384B 배열 | O — pool with frame |
| 3 | `new PyValue[nlocals]` + `Array.Fill` | PyFrame:139-143 | nlocals×24B | O — 크기별 pool |
| 4 | `new PyScopeChain(globalScope)` | PyFunction:52 | 객체+List+배열 | **O — 단순 함수에 불필요** |
| 5 | `new PyScope(Local, ...)` | PyScopeChain:486 | 객체+Dictionary | **O — 단순 함수에 불필요** |
| 6 | `new PyCell[0]` × 2 | PyFrame:157,177 | 빈 배열 2개 | O — `Array.Empty<>()` |
| 7 | Default tuple 재생성 | BindArgs:338-344 | PyTuple+배열 | O — CodeObject 캐시 |

**핵심 발견: ScopeChain 이중 쓰기 (dual-write)**
```csharp
// BindArgumentsToParametersCPython312 — 모든 파라미터 바인딩에서:
LocalsPlus[paramIndex] = PyValue.FromObject(args[i]);       // line 291
ScopeChain.AssignVariable(paramName, args[i]);               // line 292 ← 낭비!
```
LOAD_FAST/STORE_FAST는 `LocalsPlus[]`만 사용. ScopeChain 쓰기는 완전히 불필요.
LOAD_NAME/STORE_NAME (클래스 본문, 모듈 레벨)에서만 ScopeChain 사용.

### 2.3 LOAD_ATTR — 53x 차이의 원인

```
CPython:  LOAD_ATTR_INSTANCE_VALUE → version_tag 비교 1회 + 배열 인덱스 1회 (2 ops)

SharpPy:  GetAttribute → __getattribute__ 캐시 조회 (1 dict lookup)
        → GetAttributeGeneric → MRO data descriptor 루프 (N dict lookups)
        → InstanceDict 조회 (1 dict lookup)
        → MRO non-data descriptor 루프 (N dict lookups)
        (최소 3회 Dictionary 조회)
```

### 2.4 `new PyObject[]` 배열 할당 — 모든 hot path에 존재

| 우선순위 | 위치 | 빈도 | 내용 |
|---------|------|------|------|
| **HOT** | PyVM.cs:2211 | 모든 CALL | `new PyObject[callArgCount]` |
| **HOT** | PyVM.cs:2262+2264 | 모든 method CALL | `new PyObject[args+1]` + Array.Copy |
| **HOT** | PyClass.cs:1607/1609 | 모든 dunder | `{this}` or `{this, arg}` |
| **HOT** | PyClass.cs:1683 | 모든 binary dunder | `{this, arg}` |
| **HOT** | PyVM.cs:8313+8315 | 모든 bound method | `new PyObject[args+1]` + Array.Copy |

---

## 3. 발견된 버그 및 위험 요소

### 3.1 CALL handler swap 로직 — ✅ 정상 (CPython 3.12 호환)

**위치:** `runtime/PyVM.cs` lines 2257-2265

**결론: 버그가 아님** — CPython 3.12 `generated_cases.c.h` 분석으로 확인.

**CPython 3.12 CALL 스택 레이아웃:**
```
Stack: [method, callable, arg0, ..., argN]
// method = PEEK(oparg+2), callable = PEEK(oparg+1)
// if (method != NULL) { callable = method; args--; total_args++; }
```

**LOAD_ATTR method push 순서 (CPython 3.12 `generated_cases.c.h:2440-2466`):**
- is_method=True: `res2=meth → PEEK(2)`, `res=self → PEEK(1)` → stack: `[meth, self]`
- is_method=False: `res2=NULL → PEEK(2)`, `res=attr → PEEK(1)` → stack: `[NULL, attr]`

**SharpPy CALL에서:**
- `callableFunc = Pop()` → PEEK(1) = self (또는 attr)
- `nextElement = Pop()` → PEEK(2) = meth (또는 NULL)
- Swap: `actualCallable = nextElement` = meth → CPython의 `callable = method` 와 동일

**사용되는 패턴들 (디버그 로그로 확인):**
1. Decorator: `[property, func]` → `property(func)` ✓
2. classmethod/staticmethod: `[classmethod, func]` → `classmethod(func)` ✓
3. `__build_class__`: `[__build_class__, class_body_func]` → `__build_class__(func, name)` ✓
4. Method call: `[unbound_func, self]` → `func(self, args)` ✓ (Phase 4 구현 후)

### 3.2 TypeMethodCache Reflection — 성능 버그

**위치:** `core/PyType.cs` lines 37-38

```csharp
_getTypeAttributeFunc = (type, name) =>
    (PyObject)method.Invoke(null, new object[] { type, name });
```

`MethodInfo.Invoke()`는 boxing + reflection으로 직접 호출 대비 100-1000x 느림.
모든 TypeMethodCache 캐시 미스에서 발생.

**수정:** `PyClass.GetTypeAttribute(type, name)` 직접 호출로 변경.
단, PyType ↔ PyClass 간 circular dependency 해결 필요.

### 3.3 BindArguments dual-write — 불필요한 작업

모든 함수 호출마다 파라미터를 LocalsPlus와 ScopeChain 양쪽에 쓰는 중.
LOAD_FAST/STORE_FAST가 ScopeChain을 읽지 않으므로 ScopeChain 쓰기는 낭비.

### 3.4 default tuple 매 호출 재생성

`BindArgumentsToParametersCPython312` lines 338-344:
```csharp
var defaultsArray = new PyObject[code.DefaultValues.Count];
code.DefaultValues.CopyTo(defaultsArray, 0);
effectiveDefaults = new PyTuple(defaultsArray);
```
기본값이 있는 함수의 모든 호출에서 배열+튜플 재생성.

---

## 4. 최적화 실행 결과 (Phase 4-8)

### Phase 4: CALL swap 분석 + PyMethod 할당 제거 ✅

**4.1 CALL swap — 버그가 아님 (CPython 3.12 호환 확인)**
- CPython 3.12 `generated_cases.c.h` 분석으로 swap 로직이 정확함을 확인
- `nextElement` = CPython `method` (PEEK(oparg+2)), `callableFunc` = CPython `callable` (PEEK(oparg+1))
- Decorator, classmethod, staticmethod, __build_class__ 등에서 정상 사용

**4.2 LOAD_ATTR fast path — PyMethod 제거** ✅
- `[NULL, new PyMethod(obj, func)]` → `[func, obj]` (CPython 스택 레이아웃 호환)
- CALL swap이 `func(obj, args)` 직접 호출 — PyMethod heap 할당 제거

**4.3 LOAD_ATTR slow path — PyMethod 분해** ✅
- `[NULL, bound_method]` → `[method.Function, method.Instance]`

**4.4 Pattern E push 순서 수정** ✅
- `[obj, attr]` → `[attr, obj]` (CPython 호환)

**4.5 __init__ / TryNext / descriptor PyMethod 제거** ✅
- `__init__`: func.Call([instance, ...args]) 직접 호출
- `TryNext.__next__`: func.Call([this]) 직접 호출
- descriptor `__get__`/`__set__`: func.Call([descriptor, ...]) 직접 호출
- `LookupSpecialMethod`: unbound function 반환 후 caller에서 self 추가

### Phase 5: ScopeChain dual-write 제거 ✅ (1010ms → 878ms, **-13%**)

**5.1 CO_OPTIMIZED 플래그 기반 ScopeChain 스킵**
- `needsScopeChain = (code.Flags & CO_OPTIMIZED) == 0`
- BindArguments 7곳 모두 가드 추가
- 일반 함수: ScopeChain.AssignVariable 7회 × 매 파라미터 스킵

**5.2 Array.Empty<PyCell>()** ✅
- 5곳 `new PyCell[0]` → `Array.Empty<PyCell>()` 변환

**5.3 CachedDefaultsTuple** ✅
- `PyCodeObject.CachedDefaultsTuple` 추가 (construction time 1회 빌드)
- `BindArguments`: 매 호출 배열+튜플 재생성 제거

### Phase 6: CALL args 최적화 ✅

- `callArgCount == 0` → `Array.Empty<PyObject>()` (가장 빈번한 패턴)
- ThreadStatic 버퍼는 `args.Length` 의존성으로 적용 불가

### Phase 7: TypeMethodCache Reflection 제거 ✅

- `MethodInfo.Invoke()` → `PyClass.GetTypeAttribute()` 직접 정적 호출
- 100-1000x 빠른 fallback path

### Phase 8: LOAD_ATTR inline fast path ✅ (878ms → 891ms 안정)

- `pushNullForMethod=false` + `PyClassInstance` → `InstanceDict.TryGetValue()` 직접 조회
- GetAttribute MRO 3회 순회 → Dictionary 1회 조회
- data descriptor (@property) 안전: property setter가 다른 이름(`_x`)에 저장하므로 충돌 없음

---

## 5. 성능 추적

| Phase | SharpPy (ms) | CPython (ms) | Ratio | 변화 |
|-------|-------------|-------------|-------|------|
| Baseline | 1486 | 16.6 | 89.7x | - |
| Phase 0-1 | 988 | 16.6 | 59.6x | -33.5% |
| Phase 2-3 | ~993 | 16.6 | ~59.9x | ~0% |
| 이전 best | 851 | 15.9 | 53x | - |
| **Phase 4-8** | **~891** | **~19.7** | **~45x** | **-15%** |

---

## 5. 위험 관리

### 5.1 회귀 위험

| Phase | 위험도 | 근거 |
|-------|--------|------|
| Phase 4 | **높음** | CALL/LOAD_ATTR 핵심 경로 변경 — 모든 method call에 영향 |
| Phase 5 | **높음** | Frame lifecycle 변경 — generator, closure, exception에 영향 |
| Phase 6 | **중간** | ThreadStatic 재진입 위험 |
| Phase 7 | **낮음** | 단순 호출 방식 변경 |
| Phase 8 | **중간** | descriptor protocol 우회 가능성 |

### 5.2 매 단계 검증 프로세스

```bash
# 1. 빌드
dotnet build -c Release

# 2. 회귀 테스트
dotnet run -c Release test_comprehensive_python312.py
dotnet run -c Release test_ultimate_complex_features.py
dotnet run -c Release tests/level6_classes_basic.py
dotnet run -c Release tests/level5_functions_advanced.py
dotnet run -c Release tests/level8_generators.py

# 3. 벤치마크
dotnet run -c Release benchmark_comprehensive.py

# 4. CPython 비교
export PYTHONUTF8=1 && "C:\ProgramData\miniforge3\python.exe" benchmark_comprehensive.py
```

### 5.3 롤백 전략

- 각 Phase를 별도 git commit으로 관리
- Phase 내 sub-step도 가능하면 개별 commit
- 회귀 발생 시 `git revert`로 즉시 복원

---

## 6. 예상 결과

| Phase | 예상 비율 | 핵심 변경 |
|-------|-------:|----------|
| 현재 | 53x | Phase 0-3 완료 |
| Phase 4 | ~35x | PyMethod 할당 제거 |
| Phase 5 | ~25x | Frame pooling + ScopeChain 제거 |
| Phase 6 | ~22x | args 배열 재사용 |
| Phase 7 | ~20x | Reflection 제거 |
| Phase 8 | ~18x | LOAD_ATTR inline |

**목표: 53x → ~18-20x (65% 감소)**

> 주의: 예상치는 CPython과의 아키텍처 차이 (C vs C#, native stack vs managed heap)로
> 인해 실제 결과와 차이날 수 있음. 매 Phase 후 실측으로 검증.
