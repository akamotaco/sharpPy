# SharpPy Performance Optimization Status (2026-03-14)

## 현재 성능 비교 (bench_micro.py, best of 3)

| Benchmark | SharpPy | CPython 3.12 | 비율 | 병목 |
|---|---|---|---|---|
| **empty_loop** | 64 ns | 45 ns | **1.4x** | 기본 dispatch 오버헤드 |
| **load_attr_inst** | 190 ns | 115 ns | **1.7x** | slot 탐색 vs tp_dictoffset |
| **method_call** | 348 ns | 182 ns | **1.9x** | frame 생성 + ExecuteFrame 재진입 |
| **builtin_method** | 289 ns | 78 ns | **3.7x** | LOAD_ATTR cache + CALL dispatch |
| **str_convert** | 860 ns | 184 ns | **4.7x** | PyStr 힙 할당 + ToString() |
| **str_upper** | 284 ns | 112 ns | **2.5x** | method descriptor dispatch |
| **instance_create** | 1127 ns | 377 ns | **3.0x** | PyClassInstance 할당 + InitFast |
| **dunder_add** | 5184 ns | 570 ns | **9.1x** | 재귀 ExecuteFrame + CreateInstance |
| **generator_yield** | 735 ns | 191 ns | **3.8x** | frame save/restore 비용 |
| **전체 평균** | — | — | **~3.5x** | |

---

## C#/CLR 구조적 한계 (개선 불가)

| 한계 | 영향 | CPython과의 차이 |
|---|---|---|
| **Thread datastack 불가** | ~50ns/call | CPython: 포인터 bump 2ns, C#: managed 힙 할당 |
| **goto 메서드 경계 불가** | ~20ns/call | CPython: `goto start_frame`, C#: sentinel + continue |
| **PyValue 24B vs 포인터 8B** | ~10ns/call | Array.Copy/Fill이 3배 데이터 이동 |
| **GC write barrier** | ~5ns/참조 쓰기 | managed reference 쓰기마다 GC barrier |
| **ExecuteFrame IL 크기** | 5,315B IL | JIT L1i 캐시 압력 → inline path 추가 불가 |

### JIT IL 크기 제한

- ExecuteFrame: 5,315B IL → ~16-20KB native (L1i 32KB의 50-65%)
- ExecuteInstruction: 39,685B IL → 이미 최소 최적화 수준
- **코드 1줄 추가 = 전체 성능 저하** — Phase 14에서 4번 실험으로 확인
- inline path 추가 여유: **0**
- 따라서 남은 개선은 **호출되는 쪽**(PyClassInstance, PyFunction, PyFrame 등)에 집중

### 구조적 하한

- C# 구조적 한계만으로 ~2x
- 현재 ~3.5x → 이론적 ~1.7x 추가 개선 여지

---

## 남은 최적화 후보 (효과 큰 순)

### 1. dunder DISPATCH_INLINED 재시도 (dunder_add 9.1x → 목표 4~5x)
- **현재**: CallMagicMethodBinary → CallSimple → 재귀 ExecuteFrame (2 frame 생성)
- **방향**: BinaryOpWarm에서 dunder frame을 inlined로 실행
- **이전 실패 원인**: inline path에 sentinel 체크 추가 → JIT IL 증가 → 전체 regression
- **새 접근**: ExecuteFrame 밖에서 처리, 또는 별도 mini-loop
- **위험도**: 높음 (이전 3회 시도 모두 리버트)
- **예상 효과**: dunder_add -50% (5184 → ~2600ns)

### 2. Instance pooling (instance_create 3.0x → 목표 2.0x)
- **현재**: 매번 `new PyClassInstance(this)` + `new PyObject[slotCount]`
- **방향**: FastInit 클래스용 PyClassInstance ThreadStatic pool
- **조건**: SlotCount 동일한 클래스끼리 pool 공유, Return 시 slot clear
- **위험도**: 중간 (pool 크기, GC 참조 유지 문제)
- **예상 효과**: instance_create -30~40%, dunder_add -20%

### 3. PyStr interning/pooling (str_convert 4.7x → 목표 3.0x)
- **현재**: 매번 `new PyStr(string)` 힙 할당
- **방향**: 짧은 문자열 (≤16자) intern cache, 또는 string 직접 PyValue 저장
- **대안**: PyValue에 string tag 추가 (ObjRef에 string 직접 저장, PyStr wrapper 제거)
- **위험도**: 중간 (PyStr을 직접 참조하는 코드가 많음)
- **예상 효과**: str_convert -30~40%

### 4. InitFast 필드 수 감소 (method_call 1.9x → 목표 1.6x)
- **현재**: 30개 field write (Return에서 clean 불가 — frame pooling 설계 제약)
- **방향**: `_isClean` 플래그로 조건부 skip
  - Return 시 `_isClean = true` 설정 (non-generator, non-exception 경로)
  - InitFast에서 `if (!_isClean)` 일 때만 10개 필드 초기화
  - InitFull은 항상 전체 초기화 (generator/coroutine 경로)
- **위험도**: 낮음 (기존 설계 보존, 추가 분기 1개)
- **예상 효과**: -5~10ns/call, method_call -3~5%

### 5. PyValue.FromObject 제거 in InitFast (method_call 추가 개선)
- **현재**: `args[i]` (PyObject[]) → `PyValue.FromObject(args[i])` 매 arg마다 호출
- **방향**: `InitFastValues(PyValue[] argValues, int argCount)` — PyValue[] 직접 복사
- **조건**: 호출자가 이미 PyValue[] 형태로 args를 갖고 있을 때 (FOR_ITER inline 등)
- **위험도**: 낮음
- **예상 효과**: -5~10ns/call

### 6. LOAD_ATTR monomorphic inline cache (builtin_method 3.7x → 목표 2.5x)
- **현재**: 매 iteration마다 BuiltinTypeTag `is` 체크 + cache entry 검증
- **방향**: CPython 3.12 LOAD_ATTR_INSTANCE_VALUE 스타일
  - 첫 히트 시 instruction에 type version 직접 기록
  - 이후 version 비교만으로 cache 유효성 판정
- **위험도**: 중간 (instruction mutation 필요)
- **예상 효과**: builtin_method -20~30%

### 7. Generator save/restore 최적화 (generator_yield 3.8x → 목표 2.5x)
- **현재**: yield마다 frame 전체 LocalsPlus PyValue[] 보존
- **방향**: dirty-flag 기반 delta save (변경된 local만 저장)
- **대안**: generator frame을 pool에서 제외하고 영구 보존 (이미 부분 적용)
- **위험도**: 높음 (generator correctness가 중요)
- **예상 효과**: generator_yield -20~30%

---

## 최적화 이력 요약

| Phase | 날짜 | 주요 내용 | 효과 |
|---|---|---|---|
| 1-2 | 02-06 | Cache/Alloc + Structural | 156x → 144x |
| 3 | 02-06 | PyValue tagged union | 144x → 93x (-39%) |
| 4 | 02-06 | Generator yield sentinel | 93x → 54x (-35%) |
| 5 | 03-09 | Class dispatch MRO cache | 89x → 60x (-33%) |
| 6 | 03-09 | PyMethod/Frame/Attr | 53x → 45x (-12%) |
| 7 | 03-09 | Frame/Stack/Scope pool | 45x → 38x (-15%) |
| 8-12 | 03-10 | CallSimple + Inline dispatch | 38x → 21x (-45%) |
| 13-14 | 03-10 | FastInit + CachedLong + Dict | 21x → 17x (-19%) |
| 15-16 | 03-11 | Pool/Closure/CompactOp | 17x → 14x (-18%) |
| 17+ | 03-12~14 | LOAD_ATTR cache + _fastCall + trivial merge | ~3.5x avg |

총 개선: **156x → 3.5x** (약 45배 개선)
