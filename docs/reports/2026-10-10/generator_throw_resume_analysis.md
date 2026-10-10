# 51차: generator.throw 재개 위치와 불필요한 결과 값 수정

50차 CACHE 개선은 `ddec605`로 커밋했다. 해당 조사에서 발견한 루프 중간 throw 후 조기 종료를 수정했다.

## 원인과 변경

PyGenerator.Next는 예외가 주입된 경우에도 IP를 YIELD_VALUE 다음으로 이동하고 정상 send 결과 값을 스택에 넣었다. 예외 처리 후에도 이 값이 남아 루프 반복자 위를 가리므로 다음 yield 대신 종료되는 문제가 발생했다.

시작된 generator에 PendingException이 있을 때는 중단된 yield의 IP를 유지하고 _sentValue를 넣지 않는다. next/send는 기존처럼 IP를 이동하고 값을 넣는다. CPython v3.12.15 Python/ceval.c의 throwflag 분기도 RESUME을 거치지 않고 resume_with_error로 진행하며, 예외 lookup은 중단 지점을 기준으로 한다. 이는 C 구현의 스택 push/pop을 그대로 복제하는 대신 SharpPy VM에서 같은 결과를 만드는 변경이다.

## 검증

- 최소 재현의 `throw(ValueError())`가 StopIteration 대신 다음 루프 yield를 반환한다.
- 반복 주입, 두 겹 for, throw 이후 next/send, finally와 close를 보강하여 실제 CPython 3.12.15와 일치했다. 통과한 fixture를 기본 평가 목록에 추가했다.
- CACHE 관련 traceback·generator 회귀와 두 EXTENDED_ARG가 필요한 큰 generator 회귀도 통과했다.
- 기본 94개 평가: 89개 완전 일치, 기존 변동값 제외 일치 3개, C# 전용 검사 2개 실패 보고 없음.
- 일반 Release 빌드 통과(기존 tokenizer nullable 경고 1개, 오류 0개). Godot 소스 포함 빌드 경고/오류 0개, 기존 17개 및 추가 5개 fixture와 C# helper 검사 통과.
- 실행 시간 개선율은 이번에 측정하지 않았다. 예외 주입 경로에서 불필요한 값 추가를 없앴으며 새 할당이나 외부 의존성을 추가하지 않았다.

## 남은 구조적 문제: 예외 스택 depth

`100 + (yield i)`처럼 yield 전에 피연산자가 스택에 남아 있는 경우는 아직 실패한다. `tests/compatibility/generator_throw_expression_stack.py`에 최소 재현을 보존했다. CPython은 통과하고 a4da1e9 기준 DLL과 이번 변경본 모두 실패한다. 이 파일은 기본 통과 94개에 포함하지 않는다.

조사에서 확인한 현재 구조:

- flowgraph는 SETUP_FINALLY/SETUP_CLEANUP 등에 대해 handler depth를 0/1로 고정한다. 중첩 루프의 실제 value stack 깊이를 반영하지 못한다.
- VM의 depth=0 처리는 최대 3개 값을 임시로 꺼내 PyExceptionInfo만 제외하고 복구하는 방식이다. CPython ceval.c의 `stackbase + level`까지 unwind하는 규칙과 다르다.
- 최소 재현의 CPython try-handler depth는 1인데 SharpPy는 0이다. 기존 table 비교 도구는 presence/bounds/protected opcode를 검사하므로 이 차이가 있어도 PASS한다. 이를 전체 예외 테이블 동등성으로 해석하면 안 된다.

따라서 다음은 CFG에서 실제 stack depth 계산과 VM unwind를 함께 수정하는 작업이다. depth 계산을 고치지 않고 VM만 고정 depth까지 pop하면 유지해야 할 반복자도 제거할 수 있으므로 두 부분을 함께 검증해야 한다. generator 전체 예외 호환성이 해결됐다고 주장하지 않는다.

원시 검증과 남은 실패는 round51-validation.json 및 `.artifacts/round51-tests.json`에 기록했다. 이번 변경은 미커밋 상태다.
