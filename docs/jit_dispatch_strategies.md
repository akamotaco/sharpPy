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

## 다음 단계 (미실행)

### Phase B: ExecuteInstruction 슬림화
- 39,685B → 목표 ~12,000B
- MAKE_FUNCTION(386줄), RAISE_VARARGS(279줄), LOAD_ATTR(263줄) 등 거대 핸들러를 [NoInlining] 메서드로 추출
- slow path opcode의 JIT 품질 향상 기대

### Strategy B/C/D 검토
- Phase A/B 완료 후 프로파일링 결과에 따라 결정
- 벤치마크 없이 도입 불가
