# SharpPy 성능 리팩토링 옵션 분석

## 현재 상태 (2026-03-14)
- SharpPy: ~214ms (comprehensive benchmark)
- CPython 3.12: ~22ms
- **비율: ~10x** (이전 ~150x에서 개선)
- 함수 호출 단독: ~5.5x floor

---

## CPython 3.12 vs SharpPy 구조 비교

### Frame 구조

| | CPython 3.12 | SharpPy |
|---|---|---|
| **타입** | C struct (62B header) | C# class (~200B) |
| **할당** | Thread datastack bump (~2ns) | ThreadStatic pool Rent (~50ns) |
| **locals+stack** | 단일 `localsplus[]` (8B 포인터) | 분리: `LocalsPlus[]` + `PyStack` (24B PyValue) |
| **필드 수** | 11개 | ~20개 |
| **해제** | 포인터 감소 (0ns) | Pool Return × 3 (frame + locals + stack) |
| **DISPATCH** | `goto start_frame` (0ns) | sentinel + continue (~20ns) |

### 호출 경로 비용 (`def f(x): return x + 1`)

| 단계 | CPython | SharpPy | 차이 |
|---|---|---|---|
| Frame 할당 | ~2ns | ~50ns | TLS pool × 3 |
| 필드 초기화 | ~10ns | ~35ns | 필드 수 + PyValue 24B |
| Arg 복사 | ~1ns | ~5ns | 8B vs 24B |
| 로컬 초기화 | ~1ns | ~3ns | NULL vs PyValue.Null |
| DISPATCH | ~0ns | ~20ns | goto vs sentinel |
| **합계** | **~14ns** | **~113ns** | **~8x** |

---

## 리팩토링 옵션

### Option A: LocalsPlus + Stack 병합 (★★★ 추천)

**핵심 아이디어**: CPython처럼 locals와 stack을 단일 PyValue[] 배열로 통합

```
현재: PyFrame → LocalsPlus[PyValue[]]  (별도 배열)
             → ValueStack[PyStack]     (별도 배열)

변경: PyFrame → FrameData[PyValue[]]   (단일 배열)
             → _stackTop: int          (스택 포인터)

      FrameData 레이아웃:
      [arg0|arg1|...|local_N|stack_0|stack_1|...|stack_top]
       ← locals (nlocals) →  ← evaluation stack →
```

**예상 효과:**
- TLS pool lookup 3회 → 2회 (-1 Rent, -1 Return)
- 캐시 지역성 향상 (locals/stack 연속 메모리)
- GC 추적 배열 1개 감소
- **예상 개선: -10~15% 함수 호출 비용**

**구현 난이도: 중간**
- `frame.ValueStack.Push/Pop` → `frame.Push/Pop` (인터페이스 변경)
- PyStack 클래스 역할 축소 또는 제거
- 풀링: `totalSize = nlocals + maxStackDepth`로 통합 풀
- Generator frame: 동일 방식 (배열이 generator에 유지됨)

**위험:**
- 모든 opcode에서 ValueStack 접근 패턴 변경 필요
- PyStack의 경계 검사 로직 이관
- 배열 크기 계산 오류 시 stack overflow/underflow

**구현 단계:**
1. PyFrame에 `FrameData[]` + `StackBase` + `StackTop` 추가
2. Push/Pop/Peek를 PyFrame 메서드로 구현
3. InitFast 등에서 단일 배열 할당
4. 점진적으로 opcode별 ValueStack → Frame 직접 접근 전환
5. PyStack 제거

---

### Option B: PyValue → NaN-boxing (12B → 8B)

**핵심 아이디어**: PyValue를 24B tagged union에서 8B NaN-boxed value로 축소

```
현재: PyValue { Tag(4B) + padding(4B) + RawBits(8B) + ObjRef(8B) } = 24B
변경: PyValue { double(8B) } = 8B  (NaN bit pattern으로 타입 인코딩)

NaN-boxing 스킴:
- float64: 정상 IEEE 754 값 그대로
- int (48bit): 0xFFF8_xxxx_xxxx_xxxx (signaling NaN + payload)
- object ref: 0xFFFA_xxxx_xxxx_xxxx (상위 16bit 태그 + 48bit 포인터)
- bool/null: 특수 NaN 패턴
```

**예상 효과:**
- Array.Copy 3x 빠름 (24B → 8B per element)
- 캐시 라인에 8개 값 (현재 2.67개)
- **예상 개선: -20~30% 전체 성능**

**구현 난이도: 매우 높음**
- C#에서 unsafe 포인터 조작 필요 (Godot 호환성?)
- GC가 NaN-boxed 포인터를 추적 못함 → GCHandle pinning 필요
- 모든 PyValue 접근 코드 재작성
- 48bit 주소 제한 (현재 x64에서 OK, ARM64도 OK)

**위험:**
- GCHandle pinning은 GC 성능 저하
- unsafe 코드 Godot/iOS에서 제한될 수 있음
- 디버깅 극도로 어려움
- **현실적으로 비추천**

---

### Option C: ReturnLocals Clear 최적화

**핵심 아이디어**: LocalsPlus 반환 시 ObjRef 클리어를 최적화

```csharp
// 현재: ReturnLocals에서 모든 슬롯 ObjRef null 처리
for (int i = 0; i < len; i++) arr[i].ObjRef = null;

// 개선: 사용된 슬롯만 클리어 (stackTop 정보 활용)
// 또는 Array.Clear 사용 (JIT intrinsic)
```

**예상 효과: -2~5%**
**구현 난이도: 낮음**
**위험: 낮음**

---

### Option D: PyValue 직접 반환 (ExecuteFrame → PyValue)

**핵심 아이디어**: ExecuteFrame이 PyObject 대신 PyValue 반환

```csharp
// 현재
public PyObject ExecuteFrame(PyFrame frame)  // RETURN_VALUE: Pop() → ToObject
// 개선
public PyValue ExecuteFrameValue(PyFrame frame)  // RETURN_VALUE: PopValue() (변환 없음)
```

**예상 효과: -5~10% (int/float 반환 시)**
**구현 난이도: 중간**
- ExecuteFrame 호출처 모두 수정 필요
- DISPATCH_INLINED 반환 경로도 수정

---

### Option E: Frame 필드 분리 (Hot/Cold)

**핵심 아이디어**: 자주 접근하는 필드만 inline, 나머지는 별도 객체

```csharp
// Hot fields (매 instruction 접근)
struct FrameHot {
    PyCodeObject Code;
    PyValue[] FrameData;  // locals + stack merged
    int IP;
    int StackTop;
    Dictionary<string, PyObject> Globals;
}

// Cold fields (예외/generator/traceback 시만 접근)
class FrameCold {
    PyFrame ParentFrame;
    PyCell[] Cells;
    PyScopeChain ScopeChain;
    string CurrentFileName;
    PyBaseException LastException;
    // ... 나머지
}
```

**예상 효과: -5~10% (캐시 라인 효율)**
**구현 난이도: 높음** (모든 cold field 접근 경로 변경)

---

## Option A 상세 설계

### CPython localsplus 레이아웃 참고

```c
// CPython: _PyInterpreterFrame
PyObject *localsplus[1];  // flexible array member

// 레이아웃:
// [0 .. co_varcount-1]      = 일반 지역변수 (args 포함)
// [co_varcount .. co_nlocalsplus-1] = cellvars + freevars
// [co_nlocalsplus .. stacktop-1]    = evaluation stack
```

### SharpPy 병합 설계

```csharp
class PyFrame {
    // === Hot fields ===
    internal PyCodeObject Code;
    internal PyValue[] FrameData;     // locals + stack (단일 배열)
    internal int StackTop;            // stack pointer (FrameData index)
    internal int IP;
    internal Dictionary<string, PyObject> Globals;

    // === 스택 연산 (inline) ===
    [MethodImpl(AggressiveInlining)]
    internal void Push(PyValue v) => FrameData[StackTop++] = v;

    [MethodImpl(AggressiveInlining)]
    internal PyValue PopValue() => FrameData[--StackTop];

    [MethodImpl(AggressiveInlining)]
    internal ref PyValue PeekRef() => ref FrameData[StackTop - 1];

    // === 로컬 변수 접근 (LOAD_FAST/STORE_FAST) ===
    [MethodImpl(AggressiveInlining)]
    internal ref PyValue Local(int index) => ref FrameData[index];

    // === 초기화 ===
    internal void Init(PyCodeObject code, int nlocals) {
        Code = code;
        int totalSize = nlocals + code.StackSize;
        FrameData = RentFrameData(totalSize);
        StackTop = nlocals;  // stack starts after locals
        IP = 0;
    }
}
```

### 풀링 전략

```csharp
// 크기별 풀 대신 버킷 풀 사용
// 대부분 함수: nlocals 1-8, stacksize 4-16 → total 5-24
// 버킷: [8, 16, 24, 32, 48, 64]
[ThreadStatic] static PyValue[]? _fd8, _fd16, _fd24, _fd32, _fd48, _fd64;

static PyValue[] RentFrameData(int minSize) {
    int bucket = minSize <= 8 ? 8 : minSize <= 16 ? 16 : ...;
    // 해당 버킷에서 꺼내기
}
```

### 마이그레이션 경로

| 단계 | 작업 | 영향 범위 |
|---|---|---|
| 1 | FrameData[] + StackTop 추가, Push/Pop을 PyFrame에 구현 | PyFrame |
| 2 | InitFast에서 단일 배열 할당, 기존 경로 유지 | InitFast |
| 3 | 인라인 opcode (LOAD_FAST 등)를 FrameData 직접 접근으로 전환 | ExecuteFrame hot path |
| 4 | ValueStack 참조를 frame 직접 접근으로 전환 (점진적) | ExecuteInstruction |
| 5 | PyStack 제거, 나머지 Init 경로 전환 | 전체 |
| 6 | 풀링을 통합 버킷 방식으로 전환 | RentFrameData |

---

## 추천 실행 순서

1. **Option C** (ReturnLocals Clear) — 낮은 위험, 빠른 구현 → 먼저 검증
2. **Option A** (LocalsPlus+Stack 병합) — 가장 큰 구조적 개선 → 핵심 작업
3. **Option D** (PyValue 반환) — Option A와 시너지 → A 이후 진행
4. ~~Option B~~ (NaN-boxing) — Godot 호환성 문제, 현재 비추천
5. ~~Option E~~ (Hot/Cold 분리) — Option A가 이미 캐시 개선 포함
