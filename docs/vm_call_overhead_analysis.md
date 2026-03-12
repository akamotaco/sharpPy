# VM 함수 호출 오버헤드 분석

## 1. 벤치마크 결과 (2026-03-12)

SharpPy vs CPython 3.12 함수 호출 비용:

| Test | SharpPy (ns/call) | CPython (ns/call) | 비율 |
|------|---:|---:|---:|
| empty() | 2880 | 102 | 28.2x |
| trivial(1) | 2378 | 96 | 24.8x |
| two_args(1,2) | 2988 | 100 | 29.9x |
| three_args(1,2,3) | 3036 | 111 | 27.4x |

비용 분해:

| 항목 | SharpPy (ns) | CPython (ns) |
|------|---:|---:|
| bare loop (FOR_ITER cycle) | 1501 | 40 |
| x+1 (inline op) - bare loop | 355 | 33 |
| builtin len() - bare loop (CALL dispatch) | 415 | 124 |
| func call - bare loop (frame creation) | 1287 | 61 |
| func call - builtin = 순수 frame 비용 | 872 | -63 (CPython에서 함수가 builtin보다 빠름) |

## 2. SharpPy 2800ns 비용 분해

| 비용 원인 | 추정 비용 | CPython 대응 |
|-----------|---:|---|
| ExecuteFrame 재귀 호출 (prologue/epilogue + try/catch) | 500-1000ns | goto start_frame (0ns) |
| 3개 풀 TLS 조회 (Frame + Locals + Stack) | 200-400ns | pointer bump 1회 (2ns) |
| ScopeChain 구성 | 100-300ns | 포인터 2개 복사 (2ns) |
| CALL opcode slow path (ExecuteInstruction 18KB switch) | 400-800ns | CALL_PY_EXACT_ARGS 인라인 |
| 타입 체크 chain (10+ 조건 분기) | 200-500ns | func_version 1회 비교 |
| 인자 Pop 루프 + 중간 버퍼 | 50-100ns | caller 스택에서 직접 복사 |
| 13개 필드 초기화 | 20-30ns | 9개 필드 (offset 기반) |

## 3. 주요 VM 함수 호출 전략

### CPython 3.12
- **Datastack pointer-bump**: Frame + LocalsPlus + Stack이 하나의 연속 메모리 블록. `tstate->datastack_top += code->co_framesize` 한 줄로 "할당"
- **DISPATCH_INLINED**: `goto start_frame` — 같은 eval loop 안에서 frame만 교체. C 함수 호출/반환 없음
- **Frame 필드 9개**: f_code, previous, f_globals, f_builtins, f_locals, prev_instr, stacktop, owner, return_offset
- **CALL_PY_EXACT_ARGS**: func_version 1회 비교로 가드, 5개 조건만 확인

### V8 (JavaScript) — Ignition Interpreter
- Register-based bytecode: caller/callee 레지스터 윈도우 오버랩으로 인자 복사 불필요
- Machine stack 직접 사용: Frame 할당 = RSP 조정
- Inline caching: 단형(monomorphic) 호출 사이트 캐시

### LuaJIT
- Assembly로 작성된 인터프리터: C 함수 호출 오버헤드 제거
- Register-based bytecode: 레지스터 윈도우로 인자 전달
- Frame 객체 없음: machine stack에 마커만 사용

### PyPy
- Meta-tracing JIT: 함수 경계를 넘어 trace 기록, hot loop에서 함수 호출 제거
- Virtualized frame: JIT가 frame이 escape하지 않음을 증명하면 할당 자체를 제거

### GraalPython (GraalVM)
- AST interpretation + partial evaluation: hot call site에서 callee AST를 caller에 인라인
- Self-modifying AST node: 호출 사이트가 monomorphic → polymorphic → megamorphic으로 자가 특화

### JVM (HotSpot) — Template Interpreter
- 각 bytecode가 machine code snippet에 매핑
- Method handle / invokedynamic: 동적 dispatch 사이트 프로파일링 후 JIT 컴파일

## 4. 핵심 원칙

> **모든 고성능 VM의 공통 원칙: 함수 호출을 호스트 언어의 함수 호출로 매핑하지 않는다**

CPython, LuaJIT, V8 Ignition, HotSpot 모두 하나의 dispatch loop 안에서 frame만 교체한다.

## 5. SharpPy 적용 가능 전략 (영향 순)

| 전략 | 예상 절감 | 난이도 | 비고 |
|------|---:|---|---|
| DISPATCH_INLINED (frame swap) | 500-1000ns | 높음 | eval loop 재구조화 필요. Phase 16 시도 시 IL 크기 증가로 역효과 |
| 통합 datastack (frame+locals+stack 단일 버퍼) | 200-400ns | 중간 | PyValue[] 단일 배열, 포인터 bump 방식 |
| CO_OPTIMIZED ScopeChain 제거 | 100-300ns | 낮음-중간 | Globals/Builtins 포인터 직접 복사 |
| 인자 직접 복사 (중간 버퍼 제거) | 50-100ns | 낮음 | caller stack에서 callee LocalsPlus로 직접 |
| Frame 필드 축소 (lazy init) | 50-100ns | 낮음 | CurrentFileName 제거 등 |

## 6. 실험 결과 (2026-03-12)

### 성공한 최적화

#### IL 슬림화 Phase 1: ExecuteFrame cold path 추출 (-17%)
- LOAD_ATTR method cache, FOR_ITER cold path, RETURN scope cleanup을 [NoInlining] 헬퍼로 추출
- 결과: 206ms → 171ms (min). ExecuteFrame IL 감소 → RyuJIT 레지스터 할당/분기 예측 개선
- 교훈: inline fast path에서 cold 분기를 NoInlining으로 분리하면 hot path의 JIT 품질이 향상

#### IL 슬림화 Phase 2: CALL warm dispatch + BINARY_OP 추출 조합 (-12%)
- CALL warm dispatch: slow path에서 CALL만 ExecuteCall 직접 호출 (ExecuteInstruction switch 우회)
- BINARY_OP 추출: 284줄 case를 [NoInlining] ExecuteBinaryOpFull로 분리 → ExecuteInstruction IL ~15% 축소
- **각각 단독 적용 시 역효과**, 조합 시 시너지: 171ms → 174ms (min)
- 교훈: JIT 최적화는 비선형적 — 단독 역효과인 변경이 조합 시 시너지를 낼 수 있음

### 실패한 실험

| 실험 | 위치 | IL 영향 | 결과 |
|------|------|---------|------|
| Init 필드 이동 (Init → Return) | PyFrame | 비용 이동일 뿐 | +20% |
| 4-opcode warm dispatch | ExecuteFrame slow path | +IL | +22% |
| 1-opcode CALL ternary | ExecuteFrame slow path | 미세 +IL | ±0% (micro: 역효과) |
| CALL early-exit (if before switch) | ExecuteInstruction 진입부 | 미세 +IL | +11% |
| LIST_APPEND inline 제거 | ExecuteFrame inline path | -IL | +26% (list comp 역효과) |
| BINARY_OP 추출 단독 | ExecuteInstruction | -IL | +11% (2단 호출 비용) |
| CALL warm dispatch 단독 | ExecuteFrame slow path | +IL | +6% |
| DISPATCH_INLINED (frame swap) | 구조적 불가 | N/A | C# goto 한계로 불가 |

### 누적 결과
- **Before**: 206ms (min, benchmark_comprehensive.py)
- **After Phase 1**: 171ms (-17%)
- **After Phase 1+2**: 174ms (-15.5%, function_call 벤치: 18.2ms → 12.3ms)

## 7. 제약 사항

- **Godot 타겟**: iOS/Web/Console → JIT 불가. Python bytecode를 native code로 컴파일할 수 없음
- **ExecuteFrame IL 한계**: ~3.5KB. 코드 추가 시 RyuJIT 최적화 임계치 초과
- **ExecuteInstruction IL**: 18KB → ~15KB (BINARY_OP 추출 후)
- **C# 구조적 한계**: goto로 메서드 경계를 넘을 수 없음 → DISPATCH_INLINED 직접 구현 불가
- **비선형 JIT 동작**: 단일 변경의 영향을 예측할 수 없음 — 항상 조합 테스트 필요

## 8. 향후 탐색 방향

- **통합 datastack**: frame+locals+stack을 단일 PyValue[]로 pointer bump 할당
- **CO_OPTIMIZED ScopeChain 완전 제거**: Globals/Builtins를 PyFrame 필드로 직접 참조
- **추가 ExecuteInstruction case 추출**: LOAD_DEREF/STORE_DEREF (88줄), UNPACK_SEQUENCE (90줄) 등
- **PyFrame 필드 축소**: CurrentFileName 제거 (Code.FileName 사용), lazy init 패턴
