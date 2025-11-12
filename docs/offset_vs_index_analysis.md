# CPython 3.12 Offset/Index Analysis: 정확한 이해

## Executive Summary

**핵심 발견**: CPython 3.12는 **3가지 다른 개념**을 사용합니다:
1. **Byte Offset** (dis 모듈의 표시 전용)
2. **Instruction Word Offset** (내부 계산 및 VM 실행)
3. **Instruction Index** (CFG 빌드 전 InstructionSequence에서만)

**SharpPy의 선택**: **Instruction INDEX** 기반 구현 (C# 배열 인덱싱)

---

## 1. 용어 정의

### 1.1 CPython 3.12 Instruction Word 구조

```c
// Include/internal/pycore_code.h
typedef union {
    uint16_t cache;
    struct {
        uint8_t code;  // opcode
        uint8_t arg;   // argument
    } op;
} _Py_CODEUNIT;
```

- 1 instruction word = 2 bytes (16 bits)
- 일부 instruction은 inline cache 포함 (각 1 word)
- EXTENDED_ARG는 별도의 1 word

### 1.2 세 가지 개념

| 개념 | 정의 | 사용처 | 예시 |
|------|------|--------|------|
| **Byte Offset** | 바이트 단위 위치 (항상 짝수) | dis 모듈 표시 전용 | `14 SEND` (byte 14) |
| **Instruction Word Offset** | Instruction word 단위 위치 | 내부 계산, VM 실행 | Instruction word 7 |
| **Instruction Index** | CFG 내 배열 인덱스 | InstructionSequence에서만 | 배열의 5번째 요소 (index 4) |

**관계**:
- Byte Offset = Instruction Word Offset × 2
- Instruction Index ≠ Instruction Word Offset (EXTENDED_ARG와 cache 때문)

---

## 2. CPython 3.12 소스코드 분석

### 2.1 Compilation Phase (Python/compile.c)

**사용하는 것**: **Instruction Index** (InstructionSequence 내부)

**근거**:
```c
// Python/compile.c:157
int _PyCompile_InstrSize(int opcode, int oparg)
{
    assert(!IS_PSEUDO_OPCODE(opcode));
    assert(HAS_ARG(opcode) || oparg == 0);
    int extended_args = (0xFFFFFF < oparg) + (0xFFFF < oparg) + (0xFF < oparg);
    int caches = _PyOpcode_Caches[opcode];
    return extended_args + 1 + caches;  // Returns instruction WORDS count
}
```

**핵심**: InstructionSequence는 label 기반. 아직 offset 계산 안 함.

---

### 2.2 CFG Building Phase (Python/flowgraph.c)

**사용하는 것**: **Instruction Word Offset**

**근거**:
```c
// Python/flowgraph.c:481-536
static void resolve_jump_offsets(basicblock *entryblock)
{
    int bsize, totsize, extended_arg_recompile;

    do {
        totsize = 0;
        for (basicblock *b = entryblock; b != NULL; b = b->b_next) {
            bsize = blocksize(b);  // Returns instruction WORD count
            b->b_offset = totsize; // ← INSTRUCTION WORD OFFSET
            totsize += bsize;
        }

        extended_arg_recompile = 0;
        for (basicblock *b = entryblock; b != NULL; b = b->b_next) {
            bsize = b->b_offset;  // Current instruction word offset
            for (int i = 0; i < b->b_iused; i++) {
                cfg_instr *instr = &b->b_instr[i];
                int isize = instr_size(instr);  // Instruction word count

                bsize += isize;  // bsize = offset AFTER this instruction

                if (is_jump(instr)) {
                    instr->i_oparg = instr->i_target->b_offset;  // Target offset

                    if (instr->i_oparg < bsize) {
                        // Backward jump
                        assert(IS_BACKWARDS_JUMP_OPCODE(instr->i_opcode));
                        instr->i_oparg = bsize - instr->i_oparg;  // Delta in instruction words
                    }
                    else {
                        // Forward jump
                        assert(!IS_BACKWARDS_JUMP_OPCODE(instr->i_opcode));
                        instr->i_oparg -= bsize;  // Delta from next instruction
                    }

                    if (instr_size(instr) != isize) {
                        extended_arg_recompile = 1;  // Need recompilation
                    }
                }
            }
        }
    } while (extended_arg_recompile);  // Iterate until stable
}
```

**핵심**:
- `b->b_offset`은 **instruction word offset**
- Jump delta도 **instruction word** 단위
- `bsize`는 instruction 실행 **후**의 offset

**공식**:
- BACKWARD jump: `delta = current_offset_after - target_offset`
- FORWARD jump: `delta = target_offset - current_offset_after`

---

### 2.3 VM Execution Phase (Python/ceval_macros.h, Python/bytecodes.c)

**사용하는 것**: **Instruction Word Offset** (포인터 연산)

**근거**:
```c
// Python/ceval_macros.h:141-148
#define INSTR_OFFSET() ((int)(next_instr - _PyCode_CODE(frame->f_code)))
#define NEXTOPARG()  do { \
        _Py_CODEUNIT word = *next_instr; \
        opcode = word.op.code; \
        oparg = word.op.arg; \
    } while (0)
#define JUMPTO(x)       (next_instr = _PyCode_CODE(frame->f_code) + (x))
#define JUMPBY(x)       (next_instr += (x))
//                                      ^^^^ POINTER ARITHMETIC on _Py_CODEUNIT*
```

**핵심**:
- `next_instr`는 `_Py_CODEUNIT*` (instruction word 포인터)
- `JUMPBY(x)`는 `next_instr += x` → x개의 instruction word만큼 이동
- 포인터 연산이므로 자동으로 instruction word 단위

**JUMP_BACKWARD_NO_INTERRUPT 구현**:
```c
// Python/bytecodes.c:2213-2220
inst(JUMP_BACKWARD_NO_INTERRUPT, (--)) {
    /* This bytecode is used in the `yield from` or `await` loop.
     * If there is an interrupt, we want it handled in the innermost
     * generator or coroutine, so we deliberately do not check it here.
     * (see bpo-30039).
     */
    JUMPBY(-oparg);  // oparg는 instruction word offset delta
}
```

**실행 흐름**:
```
1. next_instr points to offset 7 (JUMP_BACKWARD_NO_INTERRUPT)
2. Main loop: next_instr++ → next_instr = offset 8 (post-increment)
3. oparg = 5 (delta in instruction words)
4. JUMPBY(-5): next_instr = 8 + (-5) = 3
5. Next instruction fetched from offset 3
```

---

### 2.4 Disassembly (Lib/dis.py)

**표시하는 것**: **Byte Offset**

**근거**:
```python
# Lib/dis.py:580
maxoffset = len(code) - 2  # code is bytes object
```

**실제 출력 예시**:
```
  2           6 LOAD_CONST               1 (1)
              8 BUILD_LIST               1
             10 GET_YIELD_FROM_ITER
             12 LOAD_CONST               0 (None)
        >>   14 SEND                     3 (to 24)
             18 YIELD_VALUE              2
             20 RESUME                   2
             22 JUMP_BACKWARD_NO_INTERRUPT     5 (to 14)
        >>   24 END_SEND
```

**분석**:
- 6, 8, 10, 12, 14... 모두 **짝수** (byte offset)
- SEND at byte 14 = instruction word offset 7
- JUMP_BACKWARD_NO_INTERRUPT 5 (to 14):
  - 현재: byte 22 = instruction word offset 11
  - 실행 후: instruction word offset 12
  - 목표: byte 14 = instruction word offset 7
  - delta: 12 - 7 = 5 ✓

**핵심**: dis는 사람이 읽기 쉽도록 **byte offset**으로 표시하지만, 내부는 **instruction word offset**

---

## 3. 정확한 비교표

| 단계 | CPython 3.12 | SharpPy 권장 |
|------|-------------|--------------|
| **Compilation (InstructionSequence)** | Instruction index + labels | Instruction index + labels ✓ |
| **CFG Building (flowgraph)** | Instruction word offset | Instruction INDEX (배열) |
| **Assembly** | Instruction word offset | Instruction INDEX (배열) |
| **VM Execution** | Instruction word offset (pointer) | Instruction INDEX (배열) |
| **Disassembly** | Byte offset (display only) | Byte offset (= index × 2) |
| **Exception Table** | Instruction word offset | Instruction INDEX |

---

## 4. SharpPy 구현 전략

### 4.1 선택: Instruction INDEX 기반

**이유**:
1. C#은 포인터 연산이 없음 (`Instructions[index]`로 접근)
2. EXTENDED_ARG도 별도 배열 요소로 추가됨
3. Cache entries도 별도 배열 요소 (만약 구현한다면)
4. 배열 인덱싱이 자연스럽고 직관적

**일관성 유지**:
- Compilation: Index ✓
- CFG: Index로 변환
- Assembly: Index 기반 delta 계산
- VM: Index 기반 jump
- Disassembly: Index × 2로 표시 (CPython과 시각적 일관성)

### 4.2 assemble.cs 수정 필요

**현재 문제** (runtime/assemble.cs:134-211):
```csharp
int currentOffset = result.Count;  // ❌ 변수명이 "offset"이지만 실제로는 index
int targetOffset = FindInstructionOffset(cfg, targetIndex);  // ❌ 불필요한 변환

jumpArg = currentOffset - targetOffset;  // ❌ 혼란스러운 계산
```

**올바른 구현**:
```csharp
int currentIndex = result.Count;  // ✓ 명확한 변수명
int targetIndex = instr.Argument;  // ✓ CFG에서 이미 index

// EXTENDED_ARG를 고려한 index 계산
int instructionWords = CountInstructionWords(instr);
int currentIndexAfter = currentIndex + instructionWords;

if (isBackwardJump) {
    jumpArg = currentIndexAfter - targetIndex;  // ✓ Index delta
}
else {
    jumpArg = targetIndex - currentIndexAfter;  // ✓ Index delta
}
```

**Chicken-Egg 문제 해결**: Iterative refinement (CPython 방식)

```csharp
bool needRecompile;
do {
    needRecompile = false;

    // Pass 1: Calculate indices and jump deltas
    int currentIndex = 0;
    foreach (var block in cfg.AllBlocks) {
        foreach (var instr in block.Instructions) {
            int instrWords = CountInstructionWords(instr);

            if (IsJumpInstruction(instr.OpCode)) {
                int targetIndex = FindTargetIndex(cfg, instr.Argument);
                int currentIndexAfter = currentIndex + instrWords;

                int newJumpArg = isBackwardJump
                    ? currentIndexAfter - targetIndex
                    : targetIndex - currentIndexAfter;

                int oldInstrWords = instrWords;
                instr.Argument = newJumpArg;
                int newInstrWords = CountInstructionWords(instr);

                if (oldInstrWords != newInstrWords) {
                    needRecompile = true;  // EXTENDED_ARG changed
                }
            }

            currentIndex += instrWords;
        }
    }
} while (needRecompile);

// Pass 2: Emit final bytecode
// ...
```

### 4.3 PyVM.cs 검증

**현재 구현** (runtime/PyVM.cs:2823-2842):
```csharp
case ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT:
    int jumpBackNoIntDelta = instruction.Argument;
    int jumpBackNoIntTarget = (frame.InstructionPointer + 1) - jumpBackNoIntDelta;

    // Main loop will increment IP, so subtract 1
    frame.InstructionPointer = jumpBackNoIntTarget - 1;
    return null;
```

**검증**:
```
가정:
- Current IP = 9 (JUMP_BACKWARD_NO_INTERRUPT)
- Jump delta = 5
- 목표 = index 6

실행:
1. instruction = Instructions[9]
2. IP++ → IP = 10 (main loop post-increment)
3. jumpBackNoIntTarget = (10) - 5 = 5  ❌ WRONG!
   Should be: (10) - 5 = 5, but we want 6!

올바른 계산:
- Delta가 (IP_after) - target이라면:
  delta = (9 + 1) - 6 = 4 (not 5!)

문제: Assembly에서 delta를 잘못 계산하고 있거나,
     VM에서 계산을 잘못하고 있음
```

**올바른 VM 구현**:
```csharp
// CPython pattern: next_instr += (-oparg)
// where next_instr already points to NEXT instruction

// In SharpPy:
// IP was already incremented (IP = 10)
// delta = (IP after jump) - target = 10 - 6 = 4
// So: IP = IP - delta = 10 - 4 = 6
// But main loop will ++, so: IP = 6 - 1 = 5

// Wait, let's trace again with correct delta:
// If assembly correctly calculates: delta = 10 - 6 = 4
// Then VM: new_IP = 10 - 4 = 6
// Main loop: IP++ → 7  ❌ WRONG!

// Correct:
// IP = IP - delta - 1 (to compensate for main loop increment)
frame.InstructionPointer = frame.InstructionPointer - jumpBackNoIntDelta - 1;
```

### 4.4 Exception Table

**변경 필요** (runtime/ControlFlowGraph.cs:519-590):
```csharp
// 현재: instructionIndex 사용 ✓ (이미 index 기반)
// 문제 없음 - index 기반으로 일관성 있음
```

---

## 5. 결론

### 5.1 CPython 3.12의 정확한 동작

1. **Compilation**: Instruction index + labels
2. **CFG Building**: Label → Instruction word offset 변환
3. **Assembly**: Instruction word offset 기반 delta 계산
4. **VM**: Instruction word offset (포인터 연산)
5. **Display**: Byte offset (instruction word offset × 2)

### 5.2 SharpPy의 선택

1. **전체**: Instruction INDEX 기반 (배열 인덱싱)
2. **이유**: C#의 자연스러운 배열 접근 방식
3. **일관성**: Compilation → CFG → Assembly → VM 모두 index
4. **표시**: Byte offset처럼 보이도록 index × 2 (선택사항)

### 5.3 claude.md 수정 필요

**삭제해야 할 잘못된 내용**:
- ❌ "최적화 비활성화시에는 byte offset, 최적화 활성화시에는 instruction offset을 사용"

**추가해야 할 올바른 내용**:
- ✅ "CPython 3.12는 내부적으로 instruction word offset을 사용 (표시는 byte offset)"
- ✅ "SharpPy는 instruction index 기반 (C# 배열 인덱싱)"
- ✅ "최적화 on/off는 offset/index 선택과 무관"

### 5.4 핵심 수정 사항

**파일별 수정**:
1. **runtime/assemble.cs**:
   - Index 기반 delta 계산
   - Iterative refinement 구현

2. **runtime/PyVM.cs**:
   - Jump 계산 공식 검증 및 수정

3. **docs/offset_vs_index_analysis.md**:
   - 이 문서로 대체됨

4. **CLAUDE.md**:
   - 잘못된 offset/index 설명 수정

---

## 6. 참고 자료

### 6.1 CPython 3.12 소스코드 위치

- `C:\Users\m11\Desktop\work\dup\cpython-3.12\Python\compile.c:157` - `_PyCompile_InstrSize()`
- `C:\Users\m11\Desktop\work\dup\cpython-3.12\Python\flowgraph.c:481-536` - `resolve_jump_offsets()`
- `C:\Users\m11\Desktop\work\dup\cpython-3.12\Python\ceval_macros.h:141-148` - `JUMPBY()` 매크로
- `C:\Users\m11\Desktop\work\dup\cpython-3.12\Python\bytecodes.c:2213-2220` - JUMP_BACKWARD_NO_INTERRUPT
- `C:\Users\m11\Desktop\work\dup\cpython-3.12\Lib\dis.py:580` - Byte offset 표시

### 6.2 테스트 명령어

```bash
# CPython dis 출력 (byte offset)
export PYTHONUTF8=1
"C:\Users\m11\miniforge3\envs\py312\python.exe" -m dis test_yield_simple_minimal.py

# SharpPy dis 출력
dotnet run --dis test_yield_simple_minimal.py
```
