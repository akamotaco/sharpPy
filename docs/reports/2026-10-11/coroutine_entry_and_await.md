# 68차: coroutine 시작·재개와 await 실행 경로

67차를 `97e0282` (`fix: derive generator and coroutine kinds from compiler flags`)로 커밋했다. 이번 변경은 검증 후 미커밋으로 남긴다.

## 원인과 수정

단순 coroutine도 최초 send(None)에서 빈 스택 오류를 냈다. 시작 시 POP_TOP이 소비할 값을 넣지 않았기 때문이다. 중단 후에는 YIELD_VALUE 자체를 다시 실행하고 있었으며, 내부 Next() 경로에서 null로 전달된 None도 스택에 넣지 않았다.

PyCoroutine.Send는 최초 진입과 재개 모두 전달값을 한 번 push한다. 최초에는 instruction 0에서 시작해 COPY_FREE_VARS도 수행하며, 재개는 중단한 YIELD_VALUE 다음 명령에서 시작한다. 최초 non-None 값과 실행 중 재진입을 거부하고, 정상 반환과 본문 예외 모두 완료 상태로 전환한다. 완료 이후 send는 CPython과 같은 RuntimeError를 낸다. 중복 저장하던 결과와 명령 위치 필드를 제거했다.

실제 await 회귀를 실행하면서 연결된 두 누락도 확인했다. coroutine frame 생성 시 함수의 정의 시점 globals를 전달하지 않아 전역 이름을 찾지 못했다. 기존 함수/generator처럼 캡처한 globals를 전달하도록 수정했다. AwaitExpression 컴파일은 GET_AWAITABLE 뒤에 필요한 SEND 루프가 없었다. 이미 있는 CompileYieldFrom(isAwait: true)를 재사용한다. VM의 native coroutine GET_AWAITABLE은 awaiter를 반환한다. 별도 SimpleEventLoop를 할당해 즉시 완료시키고 예외를 삼킨 뒤 awaiter로 돌아가던 경로는 제거했다.

별도 라이브러리·DLL 의존성이나 새로운 실행 엔진은 추가하지 않았다. async 전체를 구현한 변경은 아니다.

## CPython 근거와 회귀

실제 CPython 3.12.15와 pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/genobject.c:gen_send_ex2, Python/compile.c:Await_kind를 확인했다. 시작/재진입/완료 오류, 전달값 push, GET_AWAITABLE 이후 yield-from 실행 순서가 근거다. CPython의 bytecode 배치 전체를 복제하는 목적은 아니다.

신규 coroutine_send_regression.py는 최초 non-None 거부 후 정상 시작, 두 차례 중단·재개와 전달값, 중첩 await, None 반환, 본문 예외, 재진입, StopIteration 변환, 완료 후 재사용, 중첩 await 예외 처리, closure 보존을 출력으로 비교한다. 기존 coroutine_initial_send.py도 기본 통과 목록으로 승격했다. 이전 DLL에서는 두 파일 모두 실패했고, 변경 후 실제 CPython 출력과 일치했다.

- .NET Release 빌드 성공.
- 기본 128개: 완전 일치 123개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- CFG native probe 통과.
- Godot 소스 포함 빌드 성공. 기본 17개 및 native helper 통과, 추가 40개 모두 CPython stdout과 정확히 일치. 합계 57개 fixture. 모바일 실기는 검사하지 않았다.
- git diff --check 통과.

검증 원본 요약은 [round68-validation.json](round68-validation.json)에 보존했다. 상세 로그는 `.artifacts/round68-full.json`, `round68-godot-*.log`에 있다.

## 성능 평가 범위

이전 coroutine 경로는 정상 실행되지 않았으므로 전후 실행 시간을 개선율로 비교하지 않는다. 중복 필드와 불필요한 이벤트 루프 생성 경로를 제거했지만 coroutine 처리량 개선을 측정했다고 주장하지 않는다.

일반 generator 반복을 대조군으로 사용했다. .NET 8.0.31, TieredCompilation=0, 1만 회, 프로세스당 warmup 10회/측정 10회. 빌드·회귀·Godot 실행이 끝난 뒤 전후 교대 3쌍을 측정했고, 약 2% 시간 증가가 보여 순서를 뒤집은 추가 3쌍으로 확인했다. 모든 측정의 합계 검증을 통과했다. 수치는 아래에 기록한다.
| 대조 workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| generator_iteration | 54.344 | 55.201 | 6,242,352 | 6,242,352 |

시간은 6개 프로세스 중앙값의 중앙값이다. 추가 3쌍에서도 약간의 증가가 관측되어 전체 약 1.6% 증가로 기록한다. 프로세스 중앙값 범위는 이전 54.128–54.417 ms, 이후 54.391–55.549 ms로 일부 겹친다. 할당은 동일하다. 대조군의 generator 실행 알고리즘은 바뀌지 않았지만 이 측정을 근거 없이 잡음이라고 지우거나 성능 회귀가 없다고 단정하지 않는다. 원인 분리 전까지 작은 시간 증가 관측으로 남기며, 다음 성능 검증에서 VM 변경의 영향을 확인한다.

## 남은 문제와 다음 순서

1. `tests/compatibility/coroutine_nested_send.py`: 중첩 await의 coroutine_wrapper에 non-None을 보내면 send 속성이 없어 실패한다. 이전 DLL에서도 실패하며, 변경 후에는 최초 yield까지 진행하지만 값 전달에서 실패한다. 기본 통과 목록에는 넣지 않았다. 다음 작업은 wrapper의 send 전달과 예외·종료 위임이다.
2. coroutine의 close는 아직 GeneratorExit를 주입하지 않아 중단된 본문의 finally 정리를 수행하지 않는다. throw 프로토콜, send 인자 수 검사, awaitable 검증·예외 전달, kwargs도 후속 범위다. 이번 정상 완료 경로의 상태 변경과 공개 close 프로토콜을 구분해서 다룬다.
3. async generator __kwdefaults__ 공개, lambda yield-from 예외 table 보존, LOAD_CLOSURE offset 중복, 전역 NameError metadata 및 except* 정리 수명은 이전 후속 항목으로 유지한다.
