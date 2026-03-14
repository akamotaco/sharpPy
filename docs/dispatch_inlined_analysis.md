# DISPATCH_INLINED 전면 확장 분석

## 1. 현황 비교: CPython vs SharpPy

### CPython 3.12 (ceval.c)
```
_PyEval_EvalFrameDefault:
    frame = entry_frame;

start_frame:                          // 재귀 깊이 체크
    _Py_EnterRecursivePy(tstate);

resume_frame:                         // 실행 재개
    SET_LOCALS_FROM_FRAME();
    DISPATCH();                       // → opcode 루프

    case CALL:
        new_frame->previous = frame;
        frame = new_frame;            // frame swap
        goto start_frame;             // 반복! 재귀 아님!

    case RETURN_VALUE:
        frame = dying->previous;      // caller 복원
        _PyFrame_StackPush(frame, retval);
        goto resume_frame;            // 반복! return 아님!

    exception_unwind:
        if (no_handler) {
            frame = dying->previous;  // caller로 unwind
            goto resume_with_error;
        }
```

**핵심: C 함수 호출 스택을 전혀 사용하지 않는다.** goto로 같은 함수 내에서 frame만 교체.

### SharpPy 현재 상태
```
ExecuteFrame(PyFrame frame):
    entryFrame = frame;
    while (ip < count):
        // inline fast path (LOAD_FAST, STORE_FAST, BINARY_OP int, ...)
        // warm dispatch path (CALL → DISPATCH_INLINED, LOAD_DEREF)
        // cold path (ExecuteInstruction switch)

        case CALL (warm path):
            return _dispatchInlinedSentinel;  // ✅ DISPATCH_INLINED
            // main loop에서 frame swap → 재귀 없음

        case RETURN_VALUE (inline):
            if (frame != entryFrame):
                RestoreCallerAfterInlinedReturn();  // ✅ 재귀 없음
            else:
                return result;                       // entry frame은 실제 return

        case BINARY_OP dunder (BinaryOpWarm):
            CallDunderBinaryDirect()
                → ExecuteFrame(newFrame)  // ❌ 재귀 호출!
```

### 재귀 호출이 발생하는 경우들

| 경로 | 재귀? | 설명 |
|------|-------|------|
| CALL (inline fast path의 warm dispatch) | ❌ No | _dispatchInlinedSentinel → frame swap |
| FOR_ITER generator (ForIterCold) | ❌ No | 직접 frame swap (ref 파라미터) |
| BINARY_OP dunder (BinaryOpWarm) | ✅ **Yes** | CallDunderBinaryDirect → ExecuteFrame |
| ExecuteInstruction CALL | ✅ **Yes** | ExecuteCall → ExecuteFunctionCall → pyFunc.Call → ExecuteFrame |
| CallMagicMethodBinary/Unary | ✅ **Yes** | func.CallSimple → ExecuteFrame |

---

## 2. dunder_add 병목 분석

### `a + b` 실행 흐름 (Num.__add__ 호출)

```python
class Num:
    def __init__(self, v):
        self.v = v
    def __add__(self, other):
        return Num(self.v + other.v)

a = Num(1); b = Num(2)
a + b  # BINARY_OP ADD
```

#### Step-by-step 실행 경로:

```
1. BINARY_OP ADD (inline)
   → PeekValueAt(0), PeekValueAt(1) → not int → skip int fast path
   → BinaryOpWarm(frame, ADD) [NoInlining]

2. BinaryOpWarm:
   → Pop left, right → leftInst is PyClassInstance
   → CallDunderBinaryDirect(leftInst, "__add__", right) [NoInlining]

3. CallDunderBinaryDirect:
   → GetCachedMagicMethod("__add__") → PyFunction
   → code.IsSimpleCallTarget && ArgCount==2 && no closures
   → PyFrame.Rent() + InitDunderBinary(code, self, other, scope)
   → ❌ ExecuteFrame(frame)  ← 재귀 호출 #1

4. ExecuteFrame (재귀 #1): __add__ body 실행
   → LOAD_FAST 0 (self)      → inline
   → LOAD_ATTR v (self.v)    → inline
   → LOAD_FAST 1 (other)     → inline
   → LOAD_ATTR v (other.v)   → inline
   → BINARY_OP ADD (int+int)  → inline
   → LOAD_GLOBAL Num          → inline
   → PUSH_NULL                → inline
   → CALL 1 (Num(sum))       → warm path
     → ExecuteCall → _dispatchInlinedSentinel → frame swap

5. Frame swap → __init__ body 실행 (재귀 #1 안에서 DISPATCH_INLINED)
   → LOAD_FAST 1 (v)          → inline
   → LOAD_FAST 0 (self)       → inline
   → STORE_ATTR v             → inline
   → RETURN_CONST None        → RestoreCallerAfterInlinedReturn

6. __add__ frame으로 복귀
   → RETURN_VALUE             → return to caller

7. 재귀 #1의 ExecuteFrame가 return
   → CallDunderBinaryDirect가 결과 받음
   → BinaryOpWarm이 결과를 stack에 push
   → main loop 계속
```

#### 오버헤드 분석:

| 단계 | 비용 | 설명 |
|------|------|------|
| BinaryOpWarm 호출 | ~10ns | NoInlining method call |
| CallDunderBinaryDirect 호출 | ~10ns | NoInlining method call |
| GetCachedMagicMethod | ~20ns | Dictionary lookup |
| PyFrame.Rent() | ~5ns | ThreadStatic pool |
| InitDunderBinary | ~30ns | 2 args copy, scope setup |
| **ExecuteFrame 재귀 호출** | **~300ns** | **C# 스택 프레임 생성 + 로컬 변수 초기화 (instructions, ci, ip, instructionCount2 등)** |
| __add__ body 실행 | ~800ns | 8 opcodes (LOAD_FAST×3, LOAD_ATTR×2, BINARY_OP, LOAD_GLOBAL, CALL) |
| __init__ 실행 (DISPATCH_INLINED) | ~200ns | 4 opcodes (LOAD_FAST×2, STORE_ATTR, RETURN_CONST) |
| **ExecuteFrame 반환** | **~100ns** | **C# 스택 프레임 해제 + finally 블록** |
| result push + continue | ~10ns | |
| **합계** | **~1485ns** | |
| **재귀 오버헤드** | **~400ns** | ExecuteFrame 호출+반환만 (~27% of total) |

**CPython은 이 ~400ns가 0ns** (goto로 frame swap하므로 C 호출 스택 사용 안 함).

---

## 3. 이전 실패 분석

### 시도 1: BinaryOpWarm에서 _pendingInlinedFrame 설정

**접근:** BinaryOpWarm이 CallDunderBinaryDirect 대신 _pendingInlinedFrame을 설정하고 true를 반환.
Main loop에서 sentinel 감지 → frame swap.

**문제:**
```
BinaryOpWarm:
  Pop left, right from stack     ← operand 이미 제거됨
  _pendingInlinedFrame = newFrame
  return true                    ← main loop에 "handled" 신호

Main loop:
  if (BinaryOpWarm(...)) { ip += 2; continue; }  ← ip 증가 후 continue
  // 하지만 _pendingInlinedFrame은 어디서 감지?
```

**근본 원인:** BinaryOpWarm은 `bool` 반환. CALL의 `_dispatchInlinedSentinel`은 `PyObject` 반환.
두 프로토콜이 호환되지 않음. BinaryOpWarm이 true를 반환하면 main loop은 "결과가 이미 stack에 push됨"으로 해석.

### 시도 2: BinaryOpWarm에서 PyClassInstance를 제외하여 fall-through

**접근:** BinaryOpWarm이 PyClassInstance dunder를 처리하지 않고 false 반환 → ExecuteInstruction이 처리.

**문제:**
```
BinaryOpWarm:
  Pop left, right                ← operand 이미 제거됨
  if (leftInst is PyClassInstance) return false  ← "not handled"

ExecuteInstruction BINARY_OP:
  Pop left, right                ← ❌ 다시 pop! 잘못된 값 꺼냄
```

**근본 원인:** BinaryOpWarm이 이미 stack에서 pop한 상태에서 false를 반환하면,
ExecuteInstruction이 같은 operand를 다시 pop하려고 시도. Stack corruption.

### 시도 3: _pendingInlinedFrame + bool 반환 조합

**접근:** BinaryOpWarm이 true를 반환하면서 동시에 _pendingInlinedFrame을 설정.
Main loop에서 true 후 _pendingInlinedFrame 체크 추가.

**문제:** 50,000회 반복 후 NullReferenceException in ExecuteLoadAttr.

**근본 원인 (추정):**
```
BinaryOpWarm:
  Pop left, right
  frame.InstructionPointer = ???  ← IP가 BinaryOpWarm 호출 전? 후?
  _pendingInlinedFrame = newFrame
  return true

Main loop:
  if (BinaryOpWarm(...)) {
    if (_pendingInlinedFrame != null) {
      frame = _pendingInlinedFrame;  // frame swap
      ip = frame.InstructionPointer; // 0
      continue;
    }
    ip += 2; continue;
  }
```

IP 동기화 문제:
- BinaryOpWarm 진입 전: `frame.InstructionPointer = ip` (line 2064에서 sync됨)
- __add__ 실행 후 RETURN_VALUE → RestoreCallerAfterInlinedReturn
  - `ip = frame.InstructionPointer + (CALL ? 4 : 2)`
  - 하지만 frame.InstructionPointer는 BINARY_OP의 IP!
  - BINARY_OP 후 skip은 2인데, __add__ 안의 CALL 후 skip은 4
  - **IP가 잘못 계산됨**

Frame pooling 충돌:
- __add__ frame이 Pool로 반환됨
- 하지만 __add__ 안에서 Num() CALL이 또 다른 frame을 DISPATCH_INLINED으로 설정
- 이 frame이 __init__ 완료 후 pool로 반환
- **같은 pool에서 두 frame이 교차 사용되면서 field 오염**

---

## 4. 해결 방안 분석

### 방안 A: ExecuteFrame 반복화 (Iterative Main Loop)

**아이디어:** ExecuteFrame의 while loop을 확장하여 모든 함수 호출을 frame swap으로 처리.

```csharp
PyObject ExecuteFrame(PyFrame entryFrame)
{
    PyFrame frame = entryFrame;
    while (ip < count)
    {
        // ... inline fast path ...

        // BINARY_OP dunder:
        else if (inlineOp == ByteCodeOp.BINARY_OP)
        {
            // ... int fast path ...
            if (BinaryOpWarm(frame, op))
            {
                // BinaryOpWarm이 _pendingInlinedFrame을 설정했는지 체크
                if (_pendingInlinedFrame != null)
                {
                    frame = _pendingInlinedFrame;
                    _pendingInlinedFrame = null;
                    // ... update cached locals ...
                    ip = 0;
                    continue;  // goto start_frame
                }
                ip += 2; continue;
            }
        }

        // RETURN_VALUE:
        else if (inlineOp == ByteCodeOp.RETURN_VALUE)
        {
            var retVal = stack.Pop();
            if (frame == entryFrame) return retVal;

            // Restore caller frame
            var caller = frame.ParentFrame;
            PoolInlinedFrame(frame);
            frame = caller;
            // ... update cached locals ...

            // caller에서 어떤 opcode가 이 frame을 만들었는지에 따라 처리
            var callerOp = instructions[frame.InstructionPointer].OpCode;
            if (callerOp == ByteCodeOp.CALL)
            {
                ip = frame.InstructionPointer + 4;
                frame.ValueStack.Push(retVal);
            }
            else if (callerOp == ByteCodeOp.BINARY_OP)
            {
                ip = frame.InstructionPointer + 2;
                frame.ValueStack.Push(retVal);
            }
            continue;
        }
    }
}
```

**장점:**
- CPython과 동일한 구조 (goto → while loop + continue)
- 재귀 호출 완전 제거
- 모든 함수 호출에 적용 가능

**문제점:**
1. **RETURN_VALUE에서 caller 식별**: RETURN_VALUE 시점에 "누가 나를 호출했는가"를 알아야 함
   - `instructions[frame.InstructionPointer].OpCode`로 판단 가능 (CALL vs BINARY_OP)
   - 하지만 이것은 frame.InstructionPointer가 정확해야 함

2. **BinaryOpWarm의 stack 상태**: BinaryOpWarm은 이미 operand를 pop하고 결과를 push하는 구조.
   DISPATCH_INLINED을 쓰려면 pop만 하고 push는 RETURN_VALUE가 해야 함.
   → **BinaryOpWarm 리팩토링 필요**

3. **중첩 호출**: `a + b`에서 `__add__` body의 `Num(sum)` CALL이 또 frame swap을 함.
   → 2단계 중첩: BINARY_OP → __add__ → CALL → __init__
   → RETURN_VALUE가 2번: __init__ return → __add__ return
   → __init__ return 시: caller는 __add__ frame, callerOp는 CALL → ip += 4 ✅
   → __add__ return 시: caller는 원래 frame, callerOp는 BINARY_OP → ip += 2 ✅
   → **정상 동작 가능!**

4. **IL 크기 제약**: ExecuteFrame inline path에 코드를 추가하면 JIT 역효과.
   → _pendingInlinedFrame 체크를 BinaryOpWarm(true) 이후에 추가하는 것은
     inline path의 if/else chain을 늘리지 않음 (이미 BinaryOpWarm 호출 위치에 있음)
   → **OK, 기존 warm path 내에서 처리 가능**

### 방안 B: CallDunderBinaryDirect를 CALL opcode 에뮬레이션으로 변환

**아이디어:** CallDunderBinaryDirect가 직접 ExecuteFrame을 호출하지 않고,
__add__ 함수의 CALL을 에뮬레이션하여 _pendingInlinedFrame을 설정.

```csharp
private bool CallDunderBinaryDirect_Inlined(PyClassInstance leftInst, string methodName, PyObject rightObj, PyFrame callerFrame)
{
    var method = leftInst.InstanceType.GetCachedMagicMethod(methodName);
    if (method is not PyFunction func || func.CodeObject == null) return false;

    var code = func.CodeObject;
    if (!code.IsSimpleCallTarget || code.ArgCount != 2) return false;

    var scope = func.CreateCachedScopeChain() ?? ...;
    var newFrame = PyFrame.Rent();
    newFrame.InitDunderBinary(code, leftInst, rightObj, scope);
    newFrame.ParentFrame = callerFrame;

    _pendingInlinedFrame = newFrame;
    return true;  // signals "DISPATCH_INLINED needed"
}
```

**문제점:**
- BinaryOpWarm이 operand를 이미 pop한 상태 → RETURN_VALUE가 결과를 push할 때
  stack에는 pop된 자리가 비어있음 → 정상 (RETURN_VALUE가 push하면 됨)
- 하지만 BinaryOpWarm이 true를 반환하면 main loop은 "결과가 이미 push됨"으로 해석
- **반환 프로토콜 변경 필요**: bool → int (0=not handled, 1=handled+pushed, 2=DISPATCH_INLINED)

### 방안 C: BinaryOpWarm 반환 타입을 int로 변경

**아이디어:** `bool BinaryOpWarm` → `int BinaryOpWarm`
- 0: not handled (fall through to ExecuteInstruction)
- 1: handled (result already pushed to stack)
- 2: DISPATCH_INLINED (_pendingInlinedFrame set, stack already popped)

```csharp
// inline fast path에서:
else if (inlineOp == ByteCodeOp.BINARY_OP)
{
    // ... int fast path ...
    var warmResult = BinaryOpWarm(frame, (BinaryOpType)cip.Arg);
    if (warmResult == 1)
    {
        ip += 2; continue;  // 결과 push됨
    }
    if (warmResult == 2)
    {
        // DISPATCH_INLINED: frame swap
        frame = _pendingInlinedFrame;
        _pendingInlinedFrame = null;
        _currentFrame = frame;
        instructions = frame.Code.InstructionsArray;
        ci = frame.Code.CompactInstructions;
        instructionCount2 = instructions.Length;
        ip = frame.InstructionPointer;
        continue;
    }
    // warmResult == 0: fall through to ExecuteInstruction
}
```

**장점:**
- 기존 프로토콜과의 충돌 없음 (반환 타입으로 명확히 구분)
- BinaryOpWarm 내부에서 operand pop은 동일하게 유지
- DISPATCH_INLINED 시에는 operand만 pop하고 결과는 push하지 않음
  → RETURN_VALUE가 결과를 push

**문제점:**
1. **caller의 IP 설정**: BinaryOpWarm이 DISPATCH_INLINED을 반환하기 전에
   `frame.InstructionPointer`가 올바른 값이어야 함.
   → 이미 line 2064에서 `frame.InstructionPointer = ip`로 sync됨 ✅

2. **RETURN_VALUE에서 ip skip 계산**: __add__가 RETURN_VALUE로 돌아올 때
   `instructions[frame.InstructionPointer].OpCode`가 `ByteCodeOp.BINARY_OP`
   → `ip = frame.InstructionPointer + 2` (BINARY_OP + 1 CACHE) ✅

3. **__add__ 안의 Num() CALL**: __add__ body에서 CALL이 발생 →
   이것은 기존 DISPATCH_INLINED 경로 (ExecuteCall → sentinel) 사용
   → __init__ 완료 후 RestoreCallerAfterInlinedReturn → __add__ frame 복원
   → **정상 동작** (기존 CALL DISPATCH_INLINED과 동일) ✅

4. **Exception unwinding**: __add__ 실행 중 예외 발생 시
   → catch (PythonException)의 unwind loop이 처리
   → frame != entryFrame → caller로 unwind → caller의 BINARY_OP IP에서 handler 검색
   → **정상 동작** ✅

5. **IL 크기**: inline fast path에 `warmResult == 2` 분기 추가
   → if/else chain에 코드 추가 → **JIT 역효과 우려**
   → 하지만 이것은 기존 `if (BinaryOpWarm(...)) { ip += 2; continue; }` 위치를 확장하는 것
   → **새로운 opcode 추가가 아니라 기존 분기 확장** → IL 증가량 최소

---

## 5. 방안 C 세부 설계 (권장)

### 5.1 BinaryOpWarm 수정

```csharp
// 반환값: 0=not handled, 1=handled (result pushed), 2=DISPATCH_INLINED
[NoInlining]
private int BinaryOpWarm(PyFrame frame, BinaryOpType op)
{
    var ro = frame.ValueStack.Pop();
    var lo = frame.ValueStack.Pop();

    // ... float/int overflow 처리 (기존 코드, return 1) ...

    // PyClassInstance dunder dispatch
    if (lo is PyClassInstance leftInst)
    {
        var magicName = op switch { ADD or INPLACE_ADD => "__add__", ... };
        if (magicName != null)
        {
            var method = leftInst.InstanceType.GetCachedMagicMethod(magicName);
            if (method is PyFunction func && func.CodeObject != null)
            {
                var code = func.CodeObject;
                if (code.IsSimpleCallTarget && code.ArgCount == 2
                    && (code.CellVars?.Count ?? 0) == 0
                    && (code.FreeVars?.Count ?? 0) == 0)
                {
                    // DISPATCH_INLINED path
                    var scope = func.CreateCachedScopeChain() ?? ...;
                    var newFrame = PyFrame.Rent();
                    newFrame.InitDunderBinary(code, leftInst, ro, scope);
                    newFrame.ParentFrame = frame;  // ← 중요!
                    _pendingInlinedFrame = newFrame;
                    return 2;  // operand popped, result NOT pushed
                }
                // Fallback: 재귀 호출 (closure 등)
                var result = CallDunderBinaryDirect(leftInst, magicName, ro);
                if (result != null && result != PyNotImplemented.Instance)
                { frame.ValueStack.Push(result); return 1; }
            }
        }
    }

    // Not handled: push back
    frame.ValueStack.Push(lo);
    frame.ValueStack.Push(ro);
    return 0;
}
```

### 5.2 Main loop 수정 (inline fast path)

```csharp
else if (inlineOp == ByteCodeOp.BINARY_OP)
{
    // ... int fast path (unchanged) ...

    var warmResult = BinaryOpWarm(frame, (BinaryOpType)cip.Arg);
    if (warmResult >= 1)  // 1 or 2
    {
        if (warmResult == 2)
        {
            // DISPATCH_INLINED: swap to dunder method frame
            frame.InstructionPointer = ip;  // save caller IP (이미 sync됨)
            frame = _pendingInlinedFrame;
            _pendingInlinedFrame = null;
            _currentFrame = frame;
            instructions = frame.Code.InstructionsArray;
            ci = frame.Code.CompactInstructions;
            instructionCount2 = instructions.Length;
            ip = 0;  // __add__ starts at instruction 0
            continue;
        }
        ip += 2; continue;  // warmResult == 1: result already pushed
    }
    // warmResult == 0: fall through to ExecuteInstruction
}
```

### 5.3 RETURN_VALUE 처리 (변경 불필요)

기존 RestoreCallerAfterInlinedReturn이 이미 처리:
```csharp
// line 2317
var dispatchOp = instructions[frame.InstructionPointer].OpCode;
ip = frame.InstructionPointer + (dispatchOp == ByteCodeOp.CALL ? 4 : 2);
frame.ValueStack.Push(retVal);
```
- callerOp가 `BINARY_OP`이면 → `ip += 2` ✅
- callerOp가 `CALL`이면 → `ip += 4` ✅

### 5.4 Exception handling (변경 불필요)

기존 unwind loop이 이미 처리:
```csharp
// frame != entryFrame → unwind
var callerFrame = frame.ParentFrame;
PoolInlinedFrame(frame);
frame = callerFrame;
ip = frame.InstructionPointer;
```
- __add__ frame에서 예외 → caller로 unwind → caller의 BINARY_OP IP에서 handler 검색 ✅

---

## 6. 예상 실행 흐름 (방안 C 적용 후)

```
1. BINARY_OP ADD (inline)
   → not int → BinaryOpWarm(frame, ADD)

2. BinaryOpWarm:
   → Pop left, right → leftInst is PyClassInstance
   → GetCachedMagicMethod("__add__") → PyFunction
   → IsSimpleCallTarget && ArgCount==2
   → PyFrame.Rent() + InitDunderBinary
   → _pendingInlinedFrame = newFrame
   → return 2  (DISPATCH_INLINED)

3. Main loop: warmResult == 2
   → frame = _pendingInlinedFrame (frame swap)
   → ip = 0, continue

4. __add__ body 실행 (같은 ExecuteFrame, 재귀 없음!):
   → LOAD_FAST, LOAD_ATTR, ... (inline fast path)
   → CALL 1 (Num(sum)) → warm path → _dispatchInlinedSentinel
   → frame swap to __init__

5. __init__ body 실행:
   → LOAD_FAST, STORE_ATTR, RETURN_CONST
   → RETURN_CONST: frame != entryFrame → RestoreCallerAfterInlinedReturn
   → callerOp = CALL → ip += 4 → push result → __add__ frame 복원

6. __add__ RETURN_VALUE:
   → frame != entryFrame → RestoreCallerAfterInlinedReturn
   → callerOp = BINARY_OP → ip += 2 → push result → 원래 frame 복원

7. 원래 frame에서 계속 실행
```

**재귀 호출: 0회** (현재 1회 → 0회)
**예상 절감: ~400ns per dunder call** (~300ns 재귀 호출 + ~100ns 반환)

---

## 7. 위험 요소 & 검증 항목

### 7.1 IP 동기화
- BinaryOpWarm 진입 전 `frame.InstructionPointer = ip` (line 2064) ✅
- 하지만 BinaryOpWarm은 **inline fast path에서 호출** (line 1869)
- inline fast path에서는 `frame.InstructionPointer = ip` 동기화가 **없음!**
- → **반드시 BinaryOpWarm 호출 전 또는 warmResult==2 분기에서 ip sync 필요**

### 7.2 Stack 상태
- BinaryOpWarm(DISPATCH_INLINED): operand 2개 pop됨, result push 안 됨
- __add__ RETURN_VALUE: result를 caller stack에 push
- **검증: __add__ 반환 시 caller stack depth가 정확한지**

### 7.3 Frame pooling
- __add__ frame: RETURN_VALUE → PoolInlinedFrame → pool 반환
- __init__ frame: RETURN_CONST → PoolInlinedFrame → pool 반환
- **두 frame이 동시에 pool에 있는 경우는 없음** (순차 실행이므로) ✅

### 7.4 Closure/FreeVars
- 방안 C는 `IsSimpleCallTarget && no closures` 조건에서만 DISPATCH_INLINED
- Closure가 있는 dunder method → 기존 재귀 경로 (CallDunderBinaryDirect)
- **안전** ✅

### 7.5 __radd__ / NotImplemented
- 현재 BinaryOpWarm은 __add__만 시도 → NotImplemented면 push+return 1
- DISPATCH_INLINED 경로에서는 __add__를 inlined로 실행
- **__add__가 NotImplemented를 반환하면?**
  → RETURN_VALUE → RestoreCallerAfterInlinedReturn → push NotImplemented
  → main loop은 `ip += 2; continue` → 다음 opcode 실행
  → **하지만 __radd__는 시도되지 않음!**
  → **BinaryOpWarm에서 NotImplemented 처리 추가 필요**
  → 또는: DISPATCH_INLINED 경로에서 반환값이 NotImplemented면 __radd__ 시도
  → **이것은 inline path에서 처리하기 복잡 → 일단 __radd__가 필요한 케이스는 재귀 경로 유지**

### 7.6 비-함수 dunder method (descriptor, callable)
- `method is PyFunction` 체크에서 걸러짐 → 기존 fallback 경로 ✅

### 7.7 JIT IL 크기
- inline fast path에 `warmResult == 2` 분기 추가
- 코드 추가량: ~10줄 (frame swap + continue)
- 기존 `if (BinaryOpWarm(...)) { ip += 2; continue; }` 확장
- **측정 필요**: 전체 벤치마크에서 regression 없는지

---

## 8. 구현 순서

1. **BinaryOpWarm 반환 타입 변경**: bool → int (0/1/2)
   - 기존 float/int overflow 경로: return 1 (was true)
   - 기존 not handled: return 0 (was false)
   - 새 DISPATCH_INLINED: return 2

2. **inline fast path 수정**: warmResult 분기 처리
   - IP sync 추가 (frame.InstructionPointer = ip)
   - frame swap 코드

3. **테스트**: test_dunder_simple.py (정확성)
4. **벤치마크**: bench_dunder_profile.py, 전체 benchmark (regression 체크)
5. **Edge case**: NotImplemented, exception, closure dunder methods

---

## 9. CPython과의 차이점 요약

| 측면 | CPython 3.12 | SharpPy (현재) | SharpPy (방안 C) |
|------|-------------|---------------|-----------------|
| CALL | goto start_frame | _dispatchInlinedSentinel | 동일 |
| BINARY_OP dunder | C slot 함수 직접 호출 (재귀 없음) | ExecuteFrame 재귀 | DISPATCH_INLINED (재귀 없음) |
| RETURN_VALUE | goto resume_frame | return + RestoreCallerAfterInlinedReturn | 동일 |
| Exception unwind | goto exit_unwind | catch + while loop | 동일 |
| Frame swap 비용 | goto (~0ns) | frame field update (~10ns) | 동일 |

**참고:** CPython의 BINARY_OP dunder는 `slot_nb_add` → C 함수 직접 호출.
이것이 재귀가 아닌 이유는 `__add__`가 Python 함수여도 `vectorcall` → `_PyEval_Vector` → 새로운 `_PyEval_EvalFrameDefault` 호출... **사실 CPython도 재귀다!**

CPython 3.12에서 DISPATCH_INLINED는 **CALL opcode에서만** 사용된다.
BINARY_OP의 dunder dispatch는 C 레벨에서 `PyObject_CallOneArg` → `_PyEval_Vector` → 재귀 `_PyEval_EvalFrameDefault`.

**결론:** CPython도 dunder method에서는 재귀 호출한다. SharpPy만의 문제가 아니다.
하지만 CPython의 재귀 비용이 SharpPy보다 훨씬 낮다:
- CPython: C 함수 호출 (~50-100ns)
- SharpPy: C# 함수 호출 + 로컬 변수 초기화 (~300ns)

→ 방안 C로 재귀를 제거하면 이 격차를 해소할 수 있다.
