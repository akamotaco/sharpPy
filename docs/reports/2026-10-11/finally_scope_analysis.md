# 65차: finally 정리 블록의 수명과 제어 이동

64차를 `e8d2be5`로 커밋하고, 별도 재현으로 보존했던 continue/finally 스택 오류 및 nested finally 중복 실행을 수정했다.

## 원인과 변경

except 없는 try/finally에서는 FINALLY_TRY를 compiler fblock stack에서 제거하지 않았다. 이후 return이 이미 끝난 finally와 loop의 정리를 다시 방출하여, 부작용이 중복되거나 없는 iterator를 SWAP/POP하려 했다. FINALLY_TRY를 except 유무와 무관하게 정상 finally 본문 직전에 pop한다.

try/except 본문의 TRY_EXCEPT 범위도 누락되어 있었다. 해당 범위 안 return/break/continue가 보호 구간을 올바르게 벗어나도록 push/pop을 추가했다. 예외 경로의 finally 본문에는 FINALLY_END를 설정해 pending exception과 이전 예외 상태를 정리한다. 기존 UnwindFBlock 구현을 사용하며 VM 실행 분기·캐시·의존성을 추가하지 않았다.

CPython 3.12.15 고정 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Python/compile.c에서 compiler_try_finally, compiler_try_except, compiler_unwind_fblock을 확인했다. 명령열 완전 일치보다 반환값·예외·정리 부작용 일치를 기준으로 삼았다.

## 검증

- Release 빌드 성공, 기존 TokensReader nullable 경고 1개.
- 기본 123개: 완전 일치 118개, 지정된 변동값 제외 일치 3개, native 검사 2개 통과.
- 기존 최소 재현 continue_finally_stack.py, nested_finally_return_duplicate.py를 기본 비교 목록에 편입했다.
- 신규 finally_scope_regression.py: 연속 try/finally 후 반환, exceptional finally의 상수/비상수 반환 및 예외 교체, 자기 except로 들어가면 안 되는 cleanup 오류, return을 break/continue로 덮어쓰기, outer 예외 상태 복원, 정상/예외 finally의 generator next/throw/close를 검사한다. 실제 CPython 3.12.15와 일치한다.
- CFG native probe 통과.
- Godot 소스 포함 빌드 및 기본 17개+추가 35개=52개 fixture와 native range helper 통과. 외부 SharpPy DLL과 모바일 실기는 사용하지 않았다.
- git diff --check 통과.

요약·측정 원본은 [round65-validation.json](round65-validation.json), 전체 로그는 `.artifacts/round65-full.json`, `round65-godot-*.log`에 보존했다.

## 구조 비교와 성능의 한계

예외 table 구조 검사는 FAIL이다. 새 fixture의 모든 code unit 범위와 offset 왕복은 유효하지만 return_cleanup_error의 protected raise 유무, suspended의 exceptional yield depth가 CPython과 다르다. 이 검사와 실제 실행 회귀 통과를 구분한다. baseline harness로 같은 fixture의 table을 조사한 시도는 exit 3762504530으로 종료되어 이전 table 보고서가 생성되지 않았다. 전후 구조가 모두 검증됐다고 주장하지 않는다.

중복 정리를 제거한 exceptional 재현 함수는 149→128 instruction word로 줄었다. 수정 전 결과가 틀린 프로그램의 실행 시간을 개선율로 비교하지 않았다.

.NET 8.0.31, TieredCompilation=0, 1만 회 workload, 프로세스당 warmup 10회/측정 10회, 전후 교대 3쌍. 빌드·회귀 종료 후 측정, 합계 검증 포함. 시간은 프로세스 중앙값들의 중앙값이다.

| workload | 이전 ms | 이후 ms | 전후 할당 B |
| --- | ---: | ---: | ---: |
| generator_close | 287.230 | 286.874 | 27,361,256 |
| generator_iteration | 60.456 | 61.146 | 6,242,424 |

할당은 동일하다. 시간은 각각 약 -0.1%, +1.1%로, 뚜렷한 성능 개선을 주장하지 않는다. 이번 변경의 주목적은 잘못된 정리 결과를 수정하는 것이다.

## 다음 순서

미초기화 지역 변수에서 SharpPy가 NameError와 이전 형식의 메시지를 내는 차이를 `tests/compatibility/unbound_fast_local.py`에 보존했다. CPython 3.12는 UnboundLocalError와 cannot access local variable 메시지를 사용한다. LOAD_FAST/LOAD_FAST_CHECK, DELETE_FAST와 cell/free variable 경로를 구분해 오류 타입·메시지를 맞추는 것이 다음 우선순위다.

그 뒤 async/lambda 플래그 결정을 정리해 IsGenerator 명령열 검색 fallback을 제거할 수 있는지 검토한다. 이번 변경은 except* 컴파일 경로의 정리 수명까지 해결했다고 주장하지 않는다.
