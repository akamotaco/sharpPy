# Top 10 성능 갭 정밀 분석 (v0.4.9, 2026-06-11)

측정: benchmark_comprehensive.py, SharpPy 87.4ms vs CPython 16.0ms = **5.4x**
CPython 측은 `dis.dis(f, adaptive=True)`로 워밍업 후 **PEP 659 특수화 opcode**를 확인했다.

## 핵심 구조: CPython 3.12이 빠른 두 가지 메커니즘

### (A) Specializing Adaptive Interpreter (PEP 659)
워밍업 후 generic opcode가 타입 특화 버전으로 치환된다 (Python/specialize.c):
- `CALL` → `CALL_PY_EXACT_ARGS` (인자 수 일치하는 순수 Python 함수)
- `LOAD_ATTR` → `LOAD_ATTR_INSTANCE_VALUE` / `LOAD_ATTR_METHOD_WITH_VALUES` (버전 태그 검사 + 인라인 캐시)
- `BINARY_OP` → `BINARY_OP_ADD_INT` / `_ADD_FLOAT` / `_ADD_UNICODE` / `_MULTIPLY_INT`
- `COMPARE_OP` → `COMPARE_OP_INT`
- `FOR_ITER` → `FOR_ITER_RANGE`
- `LOAD_GLOBAL` → `LOAD_GLOBAL_BUILTIN` (dict version 검사 + 캐시 슬롯)
- `LOAD_SUPER_ATTR` → `LOAD_SUPER_ATTR_METHOD` (super 인스턴스 생성 생략)
- `str(x)` → `CALL_NO_KW_STR_1`, `len(x)` → `CALL_NO_KW_LEN`, `s.upper()` → `CALL_NO_KW_METHOD_DESCRIPTOR_NOARGS`

SharpPy는 같은 효과를 "generic opcode 핸들러 안의 타입 분기 fast path"로 구현한다.
기능적으로 대등하지만, **CPython은 특수화 시점에 분기를 제거**하는 반면 SharpPy는
매 실행마다 분기를 다시 탄다 (분기 예측이 흡수하지만 코드 크기/레지스터 압력 비용).

### (B) 연속 datastack 프레임 (Python/ceval.c `_PyEvalFramePushAndInit`)
CPython의 결정적 우위. 호출자가 스택에 쌓은 인자들이 **그 자리 그대로** 새 프레임의
localsplus가 된다 (datastack 포인터 bump ~2ns, 인자 복사 0회, 변환 0회).
SharpPy는 프레임 풀 Rent → `InitDirect` (~25 필드 쓰기 + Array.Copy + Array.Fill)
≈ 수십 ns. 호출 1회당 격차 ~10-20x가 여기서 발생하고, 호출이 끼는 모든 벤치에 복리로 작용.
(Phase 17에서 구조 한계로 판정: C#은 managed 객체의 스택 할당/프레임 윈도우가 불가)

---

## 항목별 분석

### 1. inheritance — 13.6x (3.08μs vs 227ns / iter)
**CPython**: 외부 `obj.compute(i)` → `LOAD_ATTR_METHOD_WITH_VALUES` + `CALL_PY_EXACT_ARGS`.
내부 `super().compute(x)` →
```
LOAD_GLOBAL_BUILTIN (super) → LOAD_DEREF __class__ → LOAD_FAST self
→ LOAD_SUPER_ATTR_METHOD (super 객체 생성 없이 _PySuper_Lookup, MRO 직접 탐색)
→ CALL_PY_EXACT_ARGS → BINARY_OP_MULTIPLY_INT
```
주의: CPython도 **매 호출마다 MRO를 선형 탐색**한다 (Objects/typeobject.c
`do_super_lookup` — __class__의 MRO 내 위치 검색 + 이후 클래스들의 tp_dict 조회).
다만 dict 조회가 interned string + 버전 캐시로 매우 싸다.

**SharpPy**: 동일 알고리즘의 fast path 보유 (PyVM.cs `ExecuteLoadSuperAttr` — PySuper
프록시 없이 MRO 직접 탐색, [func, self] push). 갭의 본체는 super 로직이 아니라
**iter당 중첩 2호출의 프레임 비용** (위 B) + MRO 탐색의 Dictionary 조회 비용.

**공략**: ① instruction-level 캐시: LOAD_SUPER_ATTR 위치별로 (selfClass, classObj,
attrName) → 결과 PyFunction 캐시 (클래스 버전 태그로 무효화). ② 프레임 비용은 구조 한계.
예상: 13.6x → 9~10x (호출 2회 floor에 수렴).

### 2. closure — 9.7x (948ns vs 98ns / iter)
**CPython**: `counter()` → `CALL_PY_EXACT_ARGS` (closure 함수도 동일 — freevars는
함수 객체에 있고 `COPY_FREE_VARS`가 프레임으로 참조 복사). cell 접근은
`LOAD_DEREF`/`STORE_DEREF` = 포인터 역참조 1회.

**SharpPy**: `InitDirectClosure` (프로파일 self 5.6~8%) — InitDirect 대비 추가로
Cells 배열 설정. cell 쓰기 경로 `STORE_DEREF`는 inline fast path에 **없음** →
ExecuteInstruction 경유. `BINARY_OP_ADD_INT`/`LOAD_DEREF`는 fast path 있음.

**공략**: ① freevar-only 클로저는 `Cells = Closure` 재사용이 이미 있어 할당은 없음 —
남는 건 호출 floor. ② STORE_DEREF를 inline fast path에 추가 (소형, IL 작음).
예상: 9.7x → 7~8x.

### 3. exception_raise — 8.4x (1.68μs vs 200ns / raise)
**CPython**: `ValueError("test")` 인스턴스 생성(`CALL` → type_call, freelist 활용)
→ `RAISE_VARARGS` → 같은 프레임 exception table 조회 → `PUSH_EXC_INFO` →
`CHECK_EXC_MATCH`(타입 포인터 비교) → 핸들러. 전부 포인터 조작, C 스택 unwinding 없음.
traceback 객체는 raise마다 생성하지만 freelist로 싸다.

**SharpPy**: 이미 같은 구조! `RaiseException`(PyVM.cs:1323)은 same-frame 핸들러를
exception table에서 찾아 **C# throw 없이** sentinel로 점프한다 (cross-frame만 C# throw).
그럼에도 1.68μs인 이유:
- `ValueError("test")` 인스턴스 생성이 generic class call 경로 (인자 tuple, __init__ 등)
- `SetTracebackDirect` — raise마다 traceback 객체 할당
- 핸들러 측 `PUSH_EXC_INFO`/`CHECK_EXC_MATCH`/`POP_EXCEPT`가 모두 ExecuteInstruction 경유
- `entry.Lasti`시 `new PyInt(ip)` 할당

**공략**: ① 예외 인스턴스 생성 fast path (builtin exception 타입 직행). ② traceback
lazy 생성 (CPython도 일단 생성하지만 SharpPy는 더 비쌈 — __traceback__ 접근 시점으로 지연).
예상: 8.4x → 4~5x.

### 4. comparison — 8.1x (1.02μs vs 126ns / iter) ★ 의외의 핫스팟
**CPython**: 연쇄 비교 `0 < i < n`은 `SWAP 2`/`COPY 2`로 중간값 복제 후
`COMPARE_OP_INT` ×2 + `POP_JUMP_IF_FALSE/TRUE`. `n // 2`는 BINARY_OP generic
(floor-div는 특수화 없음 — CPython도 generic). 전 opcode가 거대 switch 내 인라인.

**SharpPy**: iter당 ~14 opcode 중 **`SWAP`, `COPY`, `POP_JUMP_IF_TRUE`,
`JUMP_FORWARD`가 inline fast path에 없음** → 각각 NoInlining ExecuteInstruction
호출 + 거대 switch 디스패치 (~50-80ns each). iter당 4~5회면 그것만 ~300ns.
int_arithmetic이 1.4x인 것과 정확히 대비된다 (그쪽은 전 opcode가 inline).

**공략**: `SWAP`/`COPY`/`POP_JUMP_IF_TRUE`/`JUMP_FORWARD`를 inline fast path에 추가.
4개 모두 구현이 수 IL 수준으로 작아 JIT 크기 제약(L1i) 리스크가 낮은 편이나,
**반드시 추가 후 전체 벤치로 회귀 검증** (jit_method_size.md 원칙).
예상: 8.1x → 3~4x. **ROI 최고 후보.**

### 5. function_call — 7.7x (874ns vs 114ns / iter)
**CPython**: `CALL_PY_EXACT_ARGS` = 버전 검사 2회 + datastack bump + 인자 in-place.
이후 callee의 `BINARY_OP_ADD_INT` + `RETURN_VALUE`. 프레임 push 총 ~20-30ns.

**SharpPy**: ExecuteCall fast path → DISPATCH_INLINED sentinel (재귀 없음, CPython과
동일한 구조) + `PyFrame.Rent` + `InitDirect`. 프로파일 기준 호출 경로 self-time:
InitDirect 18%, ExecuteCall 13.7%. 1호출 ≈ 700ns.

**공략**: 구조 한계 영역. 남은 미세 여지: InitDirect 필드 쓰기 묶기(여러 bool/int를
flags word 하나로), _callValBuf 복사 생략 (스택에서 LocalsPlus로 직접 binding).
후자는 FrameData 병합(Phase 17) 덕에 시도 가능해졌으나 까다로움. 예상: 7.7x → 6x대.

### 6. kwargs_call — 7.5x (1.48μs vs 197ns / iter)
**CPython**: kwargs는 특수화 안 됨 — generic `CALL` + `KW_NAMES`로
`_PyEvalFramePushAndInit(kwnames)` → initialize_locals가 **매 호출** kwname을 파라미터와
바인딩한다. 단 interned string이라 **포인터 비교**로 끝남 → positional 대비 +4%뿐.

**SharpPy**: `CallWithKeywords` fast path 존재 (PyVM.cs:10504) — 그러나
① PyValue fast path가 kwargs면 통째로 스킵되어 Standard CALL로 (인자 PyObject[] 변환),
② kwname마다 `VarNameIndexMap` Dictionary 조회 (string hash + equals),
③ defaults 채움에 Attributes dict 조회.

**공략**: KW_NAMES의 kwnames tuple은 **call site당 동일 객체** (co_consts) →
(kwnames tuple identity, code) → `int[] paramIndices` 매핑을 한 번 계산해 캐시.
이후 호출은 인덱스 배열 복사만. CPython보다 오히려 빨라질 수 있는 지점.
예상: 7.5x → 5x대.

### 7. list_comprehension — 7.5x (26.5μs vs 3.5μs / iter)
**CPython**: PEP 709 인라인 comprehension — `BUILD_LIST 0` + `FOR_ITER_RANGE` +
`BINARY_OP_MULTIPLY_INT` + `LIST_APPEND`(ceval 인라인, `_PyList_AppendTakeRef` —
refcount 이전으로 INCREF도 생략) + `sum()`은 `CALL_BUILTIN_FAST_WITH_KEYWORDS` →
C 루프 + `_PyLong_Add` fast path.

**SharpPy**: comprehension 인라인은 동일. `sum` long 누산 fast path도 동일하게 있음.
갭: ① `LIST_APPEND`가 inline fast path에 없음 (100회/iter 전부 ExecuteInstruction),
② append마다 `Pop()` → `ToObject()` (x*2 ≤198이라 SmallIntCache 적중, 할당은 없음),
③ List<PyObject> 성장 재할당 (v0.4.9에서 절반 감축, 잔여 ~10%).

**공략**: LIST_APPEND를 inline fast path에 추가 (PeekAt + Append — 소형).
예상: 7.5x → 4~5x.

### 8. dunder_methods — 6.8x (4.13μs vs 607ns / op)
**CPython**: 흥미롭게도 **custom class의 BINARY_OP는 특수화되지 않는다** (adaptive
dis에서 generic `BINARY_OP` 유지 확인). 경로: `binary_op1` → `tp_as_number->nb_add`
slot → slot_nb_add wrapper → `__add__` 프레임 push. 안에서 `MyNum(...)` 또 호출
(CALL generic — 3.12는 class 호출 특수화 없음 / 3.13의 CALL_ALLOC_AND_ENTER_INIT는 미래)
→ object alloc + `__init__` 프레임 push + `STORE_ATTR_INSTANCE_VALUE` (inline values,
인스턴스 dict 없음).

**SharpPy**: 구조 동일 — `CallDunderBinaryDirect` → `CallSimple` → 프레임,
내부 `MyNum(...)` → `CallTypeOrClassFast` → FastInit. 인스턴스 속성도 slot 기반
(`_slotValues`, PyClass.cs:1500 — CPython inline values와 동등). 갭 = **op당 프레임
2회 push + 인스턴스 생성**의 호출 floor 복리. CPython도 같은 일을 하므로 비율이
6.8x로 호출류 중 낮은 편.

**공략**: 별도 신규 최적화보다 5번(호출 floor)과 동반 개선되는 항목.
docs/perf_optimization_status.md의 "dunder DISPATCH_INLINED 재시도"가 유효하면 +α.

### 9. string_ops — 5.3x (1.16μs vs 217ns / iter)
**CPython**: 4연산 전부 특수화 — `CALL_NO_KW_STR_1`(PyObject_Str 직행),
`CALL_NO_KW_LEN`, `LOAD_ATTR_METHOD_NO_DICT`+`CALL_NO_KW_METHOD_DESCRIPTOR_NOARGS`
(upper), `BINARY_OP_ADD_UNICODE` (+= 패턴이면 in-place 연결 시도까지).

**SharpPy**: len inline fast path 있음, str()는 CallTypeOrClassFast, upper는
CallMethodDescriptorFast. 갭: str(i) 변환 + upper 결과 + concat까지 **iter당 PyStr
3개 할당** (CPython은 1글자~수글자 str도 freelist/메모리풀로 쌈) + 메서드 디스패치.

**공략**: ① 소형 정수 → str 캐시 (0-999 등, CPython엔 없지만 합법), ② PyStr
인터닝/캐시 (StringCache 활용 확대). 예상: 5.3x → 4x. 우선순위 낮음.

### 10. class_method — 5.1x (2.53μs vs 500ns / iter)
**CPython**: `Point(i, i+1)` → generic `CALL` → `type_call` → `object_new` +
`__init__` 프레임 (STORE_ATTR_INSTANCE_VALUE ×2) / `p.distance()` →
`LOAD_ATTR_METHOD_WITH_VALUES` + `CALL_PY_EXACT_ARGS`, 내부 `**`는 generic
BINARY_OP (pow 특수화 없음) + `BINARY_OP_ADD_FLOAT`.

**SharpPy**: CallTypeOrClassFast + FastInit( __init__ 프레임 생략 가능 케이스) +
slot 속성. 이미 잘 최적화됨 — 5.1x는 인스턴스 생성 + 호출 floor + pow 경로.
8번과 동일 묶음. 별도 공략 불필요.

---

## 종합: 갭 원인 4분류와 우선순위

| 분류 | 해당 항목 | 원인 | 공략 가능성 |
|---|---|---|---|
| **B. 비inline opcode** | comparison(4), list_comp(7), closure 일부(2) | SWAP/COPY/POP_JUMP_IF_TRUE/JUMP_FORWARD/LIST_APPEND/STORE_DEREF가 ExecuteInstruction 경유 (호출+거대 switch ~50-80ns/op) | **높음** — 소형 op라 IL 증가 미미. 단 L1i 회귀 검증 필수 |
| **A. 호출/프레임 floor** | inheritance(1), closure(2), function_call(5), kwargs(6), dunder(8), class_method(10) | CPython datastack bump+in-place args vs Rent+InitDirect 25필드+복사 | 낮음 (구조 한계) — 미세 여지: 필드 패킹, kwargs 바인딩 캐시, super 캐시 |
| **C. 예외 기계** | exception_raise(3) | 예외 인스턴스 생성 경로 + traceback 할당 (same-frame raise는 이미 throw-free) | 중간 |
| **D. 소형 객체 할당** | string_ops(9), list_comp 일부(7) | PyStr/배열 할당 vs CPython freelist | 중간-낮음 |

### 다음 라운드 추천 순서
1. **inline fast path 확장**: SWAP, COPY, POP_JUMP_IF_TRUE, JUMP_FORWARD, LIST_APPEND, STORE_DEREF
   — comparison 8.1x→3~4x, list_comp 7.5x→4~5x 기대. IL 크기 측정 동반 (ilmeasure).
2. **kwargs 바인딩 캐시**: kwnames tuple identity → param index 배열. kwargs 7.5x→5x.
3. **LOAD_SUPER_ATTR 결과 캐시**: 사이트별 (class, attr) → method. inheritance 13.6x→9~10x.
4. **예외 생성 fast path + traceback lazy**: exception_raise 8.4x→4~5x.

전부 적중 시 TOTAL 예상: 87ms → **~70ms (≈4.4x)**. 그 이하는 프레임 floor 영역.
