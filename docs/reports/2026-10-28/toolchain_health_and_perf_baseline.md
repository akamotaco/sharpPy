# 2026-10-28 — 공식 성능 툴체인 복구 검증 및 HEAD 벤치마크 기준선

커밋 `10500ef` 기준. 회귀복구 작업(별표 언팩, closure, unpack 메시지, 중복 기저류) 후
`tools/` 검증 체인의 실제 상태와 성능 기준선을 고정한다.

## 결론: PerformanceHarness는 clean HEAD에서 정상 빌드·실행된다

`work/bug-hunt` 이전 조사에서 "PerformanceHarness가 HEAD에서 컴파일되지 않는다
(CS0246 PyIntSubclass, CS1061 CalculatePredecessors/EliminateUnreachableCode)"라고
기록한 것은 **판단 오류**였다. 원인은 저장소가 아니라 제 bisect 작업이 남긴 오염된
working tree였다(특정 구 커밋으로 체크아웃한_probe_ 파일들이 심볼을 찾지 못하던 상태).

clean HEAD에서 검증한 사실:

- `type/PyIntSubclass.cs`: `PyIntSubclass` 실재
- `runtime/ControlFlowGraph.cs:543`: `CalculatePredecessors()` 실재
- `runtime/ControlFlowGraph.cs:591`: `EliminateUnreachableCode()` 실재
- `dotnet clean` + `bin/obj` 삭제 후 `dotnet build tools/PerformanceHarness -c Release` → **오류 0**
- `tools/benchmark.py` 전 과정(SharpPy harness ↔ CPython 3.12 교차 검증) 정상:
  6개 process 모두 `correct results recorded`

probe 파일을 수정·삭제하지 않았다. 고칠 것이 없었으므로 **문서로 closing** 한다.

## HEAD 성능 기준선 (공식 harness, iterations=5000, samples=4, processes=3)

Tree-walking CFG 인터프리터로서의 절대치는 CPython 3.12(adaptive specializing VM) 대비 아래 배수.
`S/C ratio = min ms(SharpPy) / min ms(CPython)`. 원자료: `.artifacts/bench_head.json`.

| workload | sharppy ms | cpython ms | S/C |
|---|---:|---:|---:|
| addition | 4.875 | 0.141 | 34.7 |
| inplace_addition | 5.202 | 0.142 | 36.6 |
| arithmetic | 13.262 | 0.363 | 36.5 |
| function_call | 6.998 | 0.240 | 29.2 |
| attributes | 4.276 | 0.197 | 21.8 |
| dictionary | 5.619 | 0.352 | 16.0 |
| dictionary_iteration | 5.395 | 0.286 | 18.9 |
| dictionary_copy | 9.051 | 1.157 | 7.8 |
| namespace_update | 8.247 | 0.446 | 18.5 |
| namespace_read | 3.796 | 0.289 | 13.1 |
| comprehension | 1.551 | 0.095 | 16.3 |
| bound_builtin | 2.866 | 0.137 | 20.9 |
| locals_observation | 6.037 | 0.416 | 14.5 |
| frame_observation | 5.161 | 0.458 | 11.3 |
| class_locals_observation | 6.307 | 0.489 | 12.9 |
| exception_handling | 46.904 | 0.624 | **75.2** |

가장 취약한 경로: **exception_handling(75x)**, 그다음이 int 산수/addition(~35x).
성능 최적화의 우선순위 근거로 사용한다(예외 경로의 할당량, 정수 연산 디스패치).

## 커밋별 회귀·성능 점검 절차 (이번 작업부터 정례화)

1. `tools/evaluate.py` (CPython 3.12.15) — 기준 **225 일치 + 3 volatile + 2 native**. 커밋마다 이 값 유지 확인.
2. `tools/run_bughunt.py` — 프로브별 CPython 대비 상이 줄 수 추적.
3. `tools/perf_probe.py` — 고정 마이크로 워크로드 best-of-N을 `baseline.json`(f464bf6)과
   비교, 임의 워크로드 15% 이상 악화 시 exit 2. 단, str_fmt/closure_calls 같은 무관
   워크로드의 ±6%는 프로세스 기동/JIT 노이즈이므로 **변경한 코드 경로의 워크로드**를
   기준으로 판정한다.
4. 거시 성능은 `tools/benchmark.py`(공식)를 지점 기록으로 남긴다.

## 이번 세션으로 닫힌 항목 / 남은 항목

닫힘: 별표 언팩 반복프로토콜(`bb053b6`), 중첩 closure·lambda·comprehension 컴파일
(`f464bf6`), UNPACK_SEQUENCE non-iterable 메시지(`65554f9`), 중복/MRO 기저류 CLR 유출
(`10500ef`), PerformanceHarness 툴체인 헬스(본 문서).

남은 known gap(회귀 아님): NameError 제안 문구 과잉, property setter/deleter 메시지
차이(p08 잔여 14줄 중 대부분), set/dict unhashable 검증 누락, `float.hex()` 형식,
`round(int, 음수)` 무시, f-string `{x=}`, `%` 포맷 세부.
