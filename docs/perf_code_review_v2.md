# SharpPy 성능 최적화 코드 리뷰 v2

> 작성: 2026-03-09 | 상태: 리뷰 완료 + 수정 반영 (2026-03-09)

## 1. 발견된 버그

### 1.1 ~~[LATENT BUG]~~ ✅ [CORRECT] CALL handler swap 로직

**파일:** `runtime/PyVM.cs:2257-2265`

**결론: 버그가 아님** — CPython 3.12 `generated_cases.c.h` 분석으로 확인.
초기 분석이 잘못됨. `nextElement`는 CPython의 `method` (PEEK(oparg+2))에 해당하며,
`callableFunc`는 CPython의 `callable` (PEEK(oparg+1))에 해당.

**CPython 3.12 CALL:**
```c
callable = PEEK(oparg + 1);  // SharpPy: callableFunc
method = PEEK(oparg + 2);    // SharpPy: nextElement
if (method != NULL) {
    callable = method;        // SharpPy: actualCallable = nextElement
    args--;                   // SharpPy: finalArgs[0] = callableFunc
    total_args++;
}
```

**LOAD_ATTR push 순서 (CPython `generated_cases.c.h:2440-2466`):**
- is_method=True: `[meth, self]` — meth@PEEK(2), self@PEEK(1)
- is_method=False: `[NULL, attr]` — NULL@PEEK(2), attr@PEEK(1)

**수정 완료:** Pattern E push 순서 `[obj, attr]` → `[attr, obj]` (CPython 호환)
**수정 완료:** LOAD_ATTR fast path `[NULL, PyMethod]` → `[func, self]` (PyMethod 제거)

---

### 1.2 [PERF BUG] TypeMethodCache — MethodInfo.Invoke 사용

**파일:** `core/PyType.cs:37-38`

```csharp
_getTypeAttributeFunc = (type, name) =>
    (PyObject)method.Invoke(null, new object[] { type, name });
```

**문제:** `MethodInfo.Invoke()`는 100-1000x 느린 Reflection + boxing
**빈도:** 모든 TypeMethodCache 캐시 미스 (4096 엔트리 충돌 시)
**수정:** `PyClass.GetTypeAttribute(type, name)` 직접 정적 호출

---

### 1.3 [PERF BUG] BindArguments dual-write

**파일:** `runtime/PyVM.cs:291-292, 325-326, 349-351, 375-377`

```csharp
LocalsPlus[paramIndex] = PyValue.FromObject(args[i]);      // LOAD_FAST 사용
ScopeChain.AssignVariable(paramName, args[i]);               // ← 불필요
```

**문제:** LOAD_FAST/STORE_FAST는 `LocalsPlus[]`만 사용. ScopeChain은 LOAD_NAME/STORE_NAME용.
**빈도:** 모든 함수 호출의 모든 파라미터
**수정:** simple function 감지 후 ScopeChain 쓰기 스킵

---

### 1.4 [PERF BUG] CreateCachedScopeChain — 부분 캐시

**파일:** `core/PyFunction.cs:41-54`

```csharp
// PyScope만 캐시, PyScopeChain은 매 호출 재생성
if (_cachedGlobalScope != null)
    return new PyScopeChain(_cachedGlobalScope);  // 매 호출마다 new!
```

**문제:** `PyScopeChain` 생성 시 `new List<PyScope>(2)` + backing array 할당
**수정:** simple function이면 ScopeChain 자체를 생성하지 않음

---

### 1.5 [PERF BUG] new PyCell[0] — Array.Empty 미사용

**파일:** `runtime/PyVM.cs:43, 47, 157, 177, 2626`

```csharp
public PyCell[] Cells { get; set; } = new PyCell[0];   // 매 frame마다 빈 배열
public PyCell[] Closure { get; set; } = new PyCell[0]; // 매 frame마다 빈 배열
```

**수정:** `Array.Empty<PyCell>()` 사용

---

### 1.6 [PERF BUG] Default tuple 매 호출 재생성

**파일:** `runtime/PyVM.cs` BindArgumentsToParametersCPython312 lines 338-344

```csharp
var defaultsArray = new PyObject[code.DefaultValues.Count];
code.DefaultValues.CopyTo(defaultsArray, 0);
effectiveDefaults = new PyTuple(defaultsArray);  // 매 호출마다 생성
```

**수정:** PyCodeObject에 `CachedDefaultsTuple` 추가

---

## 2. `new PyMethod()` 할당 사이트 전수 조사

### HOT (매 method/dunder call)

| # | 파일:라인 | 컨텍스트 | 제거 방법 |
|---|----------|----------|----------|
| 1 | PyVM.cs:2977 | LOAD_ATTR fast path | [self, func] push + CALL swap 수정 |
| 2 | PyFunction.cs:597 | descriptor Get() | LOAD_ATTR에서 bypass |
| 3 | PyClass.cs:2059 | GetAttributeGeneric | LOAD_ATTR에서 bypass |
| 4 | PyClass.cs:1563 | TryNext() __next__ | InvokeMagicMethod 패턴 |

### WARM (매 인스턴스 생성/descriptor)

| # | 파일:라인 | 컨텍스트 | 제거 방법 |
|---|----------|----------|----------|
| 5 | PyClass.cs:238 | __init__ binding | func.Call 직접 |
| 6 | PyClass.cs:1879 | descriptor __get__ | △ 의미론 필요 |
| 7 | PyClass.cs:2187 | LookupSpecialMethod | InvokeMagicMethod |

### COLD (드물게 발생)

| # | 파일:라인 | 컨텍스트 |
|---|----------|----------|
| 8 | PyVM.cs:3196-3235 | LOAD_SUPER_ATTR |
| 9 | PyClassmethod.cs:34 | @classmethod |
| 10 | PyObject.cs:572 | base GetAttribute |

---

## 3. `new PyObject[]` 배열 할당 전수 조사

### HOT

| 위치 | 내용 | 해결 방법 |
|------|------|----------|
| PyVM.cs:2211 | `new PyObject[callArgCount]` 모든 CALL | 크기별 pool |
| PyVM.cs:2262 | `new PyObject[args+1]` method CALL | CALL swap 수정 후 제거 가능 |
| PyClass.cs:1607 | `{this}` unary dunder | [ThreadStatic] buffer |
| PyClass.cs:1609 | `{this, arg}` binary dunder | [ThreadStatic] buffer |
| PyClass.cs:1683 | `{this, arg}` CallMagicMethodBinary | [ThreadStatic] buffer |
| PyVM.cs:8313 | `new[args+1]` ExecuteFunctionCall(self) | Frame에서 직접 처리 |

---

## 4. Frame 생성 할당 전수 조사

| # | 할당 | 제거 방법 | 예상 절약 |
|---|------|----------|----------|
| 1 | new PyFrame | frame pooling | 15% |
| 2 | new PyStack + PyValue[16] | pool with frame | 10% |
| 3 | new PyValue[nlocals] + Array.Fill | 크기별 pool | 15% |
| 4 | new PyScopeChain + List<PyScope> | simple func에 불필요 | **20%** |
| 5 | new PyScope + Dictionary | simple func에 불필요 | **20%** |
| 6 | new PyCell[0] × 2 | Array.Empty | 3% |
| 7 | default tuple | CodeObject 캐시 | 5% |

---

## 5. 결론

### 수정 우선순위 (위험도 × 영향도)

1. **CALL swap 버그 수정** — Phase 4의 전제조건, 반드시 선행
2. **PyMethod 할당 제거** — HOT path 4곳, 가장 큰 성능 영향
3. **Frame ScopeChain 제거** — simple function의 3-5개 불필요 할당 제거
4. **TypeMethodCache Reflection 제거** — 단순하지만 확실한 개선
5. **args 배열 재사용** — ThreadStatic 버퍼로 HOT path 할당 제거
6. **LOAD_ATTR inline** — GetAttribute 우회로 dict lookup 3회→1회

### 추가 조사 필요 사항

- `m = obj.method; m()` 패턴 — PyMethod 제거 시에도 정상 동작 필요
- Generator frame lifecycle — pooling 시 generator가 frame을 유지하는 케이스
- 재진입 안전성 — ThreadStatic 버퍼가 recursive dunder 호출에서 안전한지
