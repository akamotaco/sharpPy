# JIT Interpreter Dispatch Strategies & Optimization History

## 근본 원인 분석

### 왜 ExecuteFrame에 코드를 추가하면 성능이 저하되는가?

**RyuJIT MIN_OPTS 임계값 (dotnet/runtime#6210)**:
- IL 60,000B / 20,000 instr / 2,000 BB 초과 시 최적화 비활성화
- ExecuteFrame (5,315B), ExecuteInstruction (39,685B) 모두 MIN_OPTS에 빠지지 않음
- **MIN_OPTS가 원인이 아님**

**진짜 원인: L1i 캐시 + 레지스터 압력**:
1. ExecuteFrame 5,315B IL → ~16-20KB native code (3-4x 확장)
2. x86 L1i 캐시 = 32KB/core → hot loop이 캐시의 50-65% 차지
3. 코드 추가 → native code 증가 → L1i miss 급증 + LSRA spill 증가
4. 결과: 추가 코드와 무관한 **모든** opcode 성능 저하

---

## 다른 인터프리터들의 Dispatch 전략

| 인터프리터 | 전략 | 핵심 아이디어 | 참고 |
|---|---|---|---|
| V8 Ignition | Handler Table | 각 handler 독립 코드 객체 + tail call | https://v8.dev/blog/ignition-interpreter |
| HotSpot JVM | Template Interpreter | opcode별 native codelet (10-30 instr) | https://openjdk.org/groups/hotspot/docs/RuntimeOverview.html |
| LuaJIT | Hand-written ASM | handler당 ~15 instructions, dispatch table | https://sillycross.github.io/2022/11/22/2022-11-22/ |
| Wasm3 | Threaded Code | function pointer chain, zero loop overhead | https://github.com/wasm3/wasm3/blob/main/docs/Interpreter.md |
| GraalVM Truffle | Partial Evaluation | AST 노드별 별도 class, JIT 합성 | - |

---

## C#에서 적용 가능한 전략

### Strategy A: Thin Inline + Warm NoInlining Helper ✅ 채택
```csharp
// ExecuteFrame: int+int만 inline
if (lv.IsIntLike && rv.IsIntLike) { /* 10줄 */ }
// Warm helper: float/string/overflow (작은 별도 메서드)
if (BinaryOpWarm(frame, binOp)) { ip++; continue; }
// Cold: ExecuteInstruction (대형 switch)
```

### Strategy B: Handler Delegate Table (V8 스타일)
- `Action<PyVM, PyFrame>[]` — delegate 호출 ~5-10ns/instruction
- LOAD_FAST/STORE_FAST 초당 수백만 회 → 오버헤드 누적 위험

### Strategy C: Unsafe Function Pointer Table (C# 9)
- `delegate*<PyVM, PyFrame, void>[]` — ~2ns, 가장 빠른 간접 호출
- unsafe 필요, Godot AOT 호환성 불확실

### Strategy D: Segmented Switch (3-tier)
- Tier 1 (tiny if/else) → Tier 2 (medium switch) → Tier 3 (large switch)
- 각 메서드 JIT 최적화 범위 내, unsafe 불필요

---

## 최적화 실험 기록

### Phase 17: ExecuteFrame IL 슬림화 (2026-03-12)

**목표**: ExecuteFrame의 L1i 캐시 압력 감소

#### 실험 1: int-only inline (float 제거)
- **변경**: BINARY_OP에서 float/string/power/divide 모두 제거, int+int ADD/SUB/MUL만 유지
- **결과**: ExecuteFrame IL 5,315 → 3,241 bytes (-39%)
- **성능**: comprehensive TOTAL 539ms → 448ms (**-16.9%**)
- **문제**: float_arithmetic 12ms → 21ms (**+74% regression**) — float이 39KB ExecuteInstruction 경유

#### 실험 2: int + float inline (ExecuteFrame에 float 복원)
- **변경**: float+float ADD/SUB/MUL도 inline에 추가
- **결과**: ExecuteFrame IL → 3,566 bytes
- **성능**: comprehensive TOTAL → 493ms (V1보다 나쁨!)
- **교훈**: 325B IL 추가(float 3개 분기)만으로 L1i 압력 증가 → 전체 성능 저하
- **결론**: ❌ ExecuteFrame에 float을 넣으면 int도 느려짐

#### 실험 3: int inline + BinaryOpWarm [NoInlining] helper ✅ 최종 채택
- **변경**: int+int만 inline 유지 + float/string/overflow를 별도 `[NoInlining]` 메서드로 처리
- **결과**: ExecuteFrame IL → 3,257 bytes (V1 대비 +16B만 추가)
- **성능**: comprehensive TOTAL → **434ms** (**-19.5%**)
  - float_arithmetic: 12ms → 11.6ms (regression 없음!)
  - int_arithmetic: 10.3ms → 9.0ms (-12.6%)
  - simple_call: 382ms → 332ms (-13.1%)
  - method_call: 244ms → 194ms (-20.5%)
- **결론**: ✅ 최적의 3-tier 구조 (hot inline / warm helper / cold ExecuteInstruction)

### IL 크기 변화 요약
| Method | Before | After | 변화 |
|---|---|---|---|
| ExecuteFrame | 5,315B | 3,257B | **-38.7%** |
| BinaryOpWarm (new) | - | ~800B | 독립 JIT 최적화 |
| ExecuteInstruction | 39,685B | 39,685B | 미변경 (Phase B 대상) |

---

## 핵심 교훈

1. **L1i 캐시가 핵심**: IL 325B 추가만으로 전체 성능 저하 (MIN_OPTS와 무관)
2. **3-tier 구조가 최적**: hot inline → warm [NoInlining] helper → cold ExecuteInstruction
3. **[NoInlining] helper의 가치**: 39KB ExecuteInstruction 우회로 float 성능 복구
4. **측정 없이 추가 금지**: IL 크기 측정 후 벤치마크 필수
5. **IL 측정 방법**: `dotnet run --project /tmp/ilmeasure -- bin/Release/net8.0/sharppy.dll`

---

### Phase B: ExecuteInstruction 슬림화 (2026-03-12) ✅ 완료

**목표**: ExecuteInstruction의 IL 크기를 39,685B → ~12,000B로 감소

#### 변경 내용
- 15개 거대 핸들러를 별도 `[NoInlining]` 메서드로 추출:
  - MAKE_FUNCTION(~386줄), RAISE_VARARGS(~279줄), LOAD_ATTR(~263줄)
  - CALL(~201줄), IMPORT_FROM(~183줄), LOAD_SUPER_ATTR(~148줄)
  - WITH_EXCEPT_START(~146줄), CHECK_EXC_MATCH(~137줄), MATCH_CLASS(~135줄)
  - CALL_FUNCTION_EX(~132줄), BINARY_SUBSCR(~126줄), MAKE_CELL(~116줄)
  - UNPACK_EX(~110줄), RERAISE(~82줄), SEND(~97줄)
- 각 case body를 `return ExecuteXxx(frame, instruction);`으로 교체
- `break;` → `return null;` 변환

#### 결과
- **ExecuteInstruction IL**: 39,685B → **18,347B** (**-53.8%**)
- **벤치마크**: Phase A 434ms → Phase A+B **320ms** (**-26.3%**, Phase 17 시작 대비 **-40.6%**)

#### 주요 벤치마크 비교 (Phase A → Phase A+B)

| Benchmark | Phase A (ms) | Phase A+B (ms) | 변화 | vs CPython |
|---|---|---|---|---|
| int_arithmetic | 9.0 | 7.2 | -20% | 8.7x |
| float_arithmetic | 11.6 | 9.5 | -18% | 12.3x |
| attribute_access | 19.4 | 7.0 | -64% | 11.3x |
| global_access | 4.1 | 1.2 | -71% | 3.8x |
| nested_loop | 4.2 | 1.5 | -64% | 2.6x |
| unpacking | 5.9 | 2.7 | -54% | 4.3x |
| exception_try | 2.87 | 0.87 | -70% | 2.9x |
| generator | 47.4 | 36.9 | -22% | 12.6x |
| closure | 22.5 | 29.9 | +33% | 48.3x |
| **TOTAL** | **434** | **320** | **-26.3%** | **17.1x** |

#### 교훈
- ExecuteInstruction 39KB → 18KB: 모든 opcode의 JIT 코드 품질 대폭 향상
- attribute_access, global_access, nested_loop 등 ExecuteInstruction 경유 opcode에서 가장 큰 개선
- closure 미약한 regression은 ExecuteFrame inline path와 무관한 호출 경로 차이

---

## IL 크기 변화 종합 요약

| Method | Before | After Phase A | After Phase A+B | 총 변화 |
|---|---|---|---|---|
| ExecuteFrame | 5,315B | 3,257B | 3,257B | **-38.7%** |
| BinaryOpWarm (new) | - | ~800B | ~800B | 독립 JIT |
| ExecuteInstruction | 39,685B | 39,685B | 18,347B | **-53.8%** |
| 15개 추출 메서드 | - | - | ~21,000B | 각각 독립 JIT |

---

## 다음 단계 (미실행)

### Strategy B/C/D 검토
- Phase A/B 완료 후 프로파일링 결과에 따라 결정
- 벤치마크 없이 도입 불가
- 현재 17.1x ratio — 추가 최적화는 호출 경로/타입 디스패치 개선이 주효할 것으로 예상
