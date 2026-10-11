# 69차: coroutine wrapper의 send 전달과 호출 비용

68차를 `e1eb5e7` (`fix: resume coroutines and compile await delegation`)로 커밋했다. 이번 변경은 검증 후 미커밋으로 남긴다.

## 원인과 수정

PyCoroutineAwaiter에는 Next()만 있어 중첩 await가 최초 None 전달은 처리해도 이후 non-None 전달에서 send 속성을 찾지 못했다. wrapper에 send를 등록하고 같은 PyCoroutine.Send로 전달한다. 서로 다른 두 wrapper도 원래 coroutine의 실행 상태를 공유한다. CPython 3.12.15의 Objects/genobject.c:coro_wrapper_send/coro_wrapper_iternext와 실제 실행 결과를 근거로 삼았다. 소스는 pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`이다.

wrapper의 Python 타입을 coroutine_wrapper로 분리하고 object를 기반으로 기존 descriptor 체계에 send, __iter__, __next__를 등록했다. 기존 iterator 기반 C# 구현은 유지하되 Python에서 callable로 보이거나 호출할 수 없게 했다. coroutine과 wrapper의 send는 정확히 위치 인자 하나만 받고 keyword 인자를 거부한다. 인자 오류는 실행 상태를 바꾸지 않는다. 실제 CPython의 메시지와 오류 순서도 확인했다.

정상 작동하는 descriptor 호출 버전을 먼저 별도 DLL로 보존한 뒤, VM SEND의 non-None 경로에 native wrapper 직접 전달을 추가했다. bound descriptor와 길이 1 인자 배열을 매번 생성하지 않는다. 정확한 C# PyCoroutineAwaiter 타입에만 적용하며 C# 파생 객체는 기존 속성 조회 경로를 유지한다. None 전달과 기존 generator 전달 경로는 그대로 사용한다. 새로운 DLL 의존성이나 실행 엔진은 추가하지 않았다.

## 검증

- .NET Release 빌드 성공.
- 기본 130개: 완전 일치 125개, 지정된 변동값 제외 일치 3개, native 검사 2개 통과.
- 신규 coroutine_wrapper_send_regression.py: 중첩 await 값 전달/최종 반환, wrapper 타입·MRO·반복자, 공유 wrapper의 재개/완료, bound/unbound send, 잘못된 인자·receiver, callable 여부, 재개 뒤 본문 예외를 CPython 3.12.15와 비교했다.
- 기존 coroutine_nested_send.py도 기본 통과 목록으로 승격했다. 두 파일은 이전 DLL에서 실패하고 일반 descriptor 버전 및 최종 직접 전달 버전에서는 모두 일치한다.
- CFG native probe 통과.
- Godot 소스 포함 빌드 성공. 기본 17개 및 native helper 통과, 추가 42개는 CPython stdout과 정확히 일치했다. 합계 59개 fixture. 모바일 실기는 검사하지 않았다.
- 성능 workload의 반환 합계 49,995,000을 CPython과 각 SharpPy 측정에서 확인했다.
- git diff --check 통과.

[round69-validation.json](round69-validation.json)에 출력·비교 결과·성능 원본을 보존했다. 전체 로그는 `.artifacts/round69-full.json`, `round69-godot-*.log`에 있다.

## 성능: 정상 작동하는 두 경로 비교

.NET 8.0.31, TieredCompilation=0. 1만 회, 프로세스당 warmup 10회/측정 10회, 전후 교대 3쌍이다. 빌드·회귀·Godot 실행을 마친 뒤 측정했다. 시간은 프로세스 중앙값 3개의 중앙값이다.

coroutine_send는 한 outer coroutine이 내부 coroutine을 await하고, 내부 coroutine은 매 반복 사용자 정의 awaitable에서 중단된다. 외부 send(i)가 wrapper를 통해 내부로 전달되고 최종 합계를 반환한다. 이전 커밋에서는 실행되지 않으므로 이전 커밋을 속도 비교 기준으로 삼지 않았다. 기준은 이번에 먼저 구현한, 결과가 동일한 일반 descriptor 경로다.

| 경로 | ms | 할당 B |
| --- | ---: | ---: |
| 일반 descriptor 전달 | 294.678 | 45,754,600 |
| native wrapper 직접 전달 | 295.442 | 45,034,600 |

1만 회에서 720,000 B가 줄었다. 시간은 약 0.26% 증가했으므로 속도 개선을 주장하지 않는다. 이번 최적화의 측정 가능한 이득은 호출 중 임시 객체 할당 감소다. 전체 coroutine 실행 비용에는 frame 생성과 yield/StopIteration 처리 등이 포함된다.

## 이전 generator 시간 증가의 분리 실험

68차에서 일반 generator 대조군 시간이 약 1.6% 증가했었다. 동일 harness에 다음 DLL 네 가지를 넣고 3회씩 순서를 바꿔 측정했다. 모두 generator_iteration의 같은 결과를 반환했고 할당은 6,242,352 B였다.

| 빌드 | ms |
| --- | ---: |
| 67차 (97e0282) | 54.132 |
| 68차 (e1eb5e7) | 54.428 |
| 68차에서 native GET_AWAITABLE 부분만 67차로 복원한 진단 빌드 | 54.150 |
| 이번 최종 빌드 | 54.526 |

이번에는 67차 대비 68차 증가가 약 0.55%, 68차 대비 이번 증가가 약 0.18%였다. VM 부분을 복원한 진단 빌드에서는 감소했지만 프로세스 중앙값 범위가 겹친다. 해당 workload는 await를 실행하지 않으므로 coroutine 실행 알고리즘의 비용으로 설명할 수 없다. JIT 코드 배치 등의 간접 영향은 가설이며 원인이 입증된 것은 아니다. 이전 관측을 삭제하거나 성능 회귀가 없다고 단정하지 않는다. 진단 빌드는 동작 수정의 일부를 되돌린 것으로 비교에만 사용했고 실제 코드에는 반영하지 않았다.

## 남은 문제와 다음 순서

1. coroutine_close_cleanup.py로 close 정리 누락을 보존했다. CPython은 위임된 iterator의 finally와 coroutine의 finally를 차례로 실행하지만 SharpPy의 close는 완료 상태만 설정한다. 이전/이후 모두 같은 출력 차이가 있다. 다음은 정상 완료 상태 변경을 공개 close와 분리하고, 위임 대상 close 및 GeneratorExit 주입을 연결하는 작업이다. wrapper.close도 이 경로가 올바르게 동작한 후 연결한다.
2. coroutine 및 wrapper의 throw는 예외 정규화와 위임, 재진입, yield 위치의 exception table 적용을 함께 검증해야 한다. 기존 generator.Throw의 제한된 예외 타입 변환을 그대로 복사하지 않는다. 이번에 throw/close 전체 호환성을 구현했다고 주장하지 않는다.
3. 일반 method_descriptor의 명시적 __get__ 속성이 없는 기존 문제도 테스트 작성 중 확인했다. 이번에는 실제 bound 호출과 unbound 호출을 검증했으며 공통 descriptor 구현까지 변경하지 않았다.
4. awaitable 검증·예외 전달, coroutine kwargs, async generator __kwdefaults__, lambda yield-from 예외 table, LOAD_CLOSURE offset 중복 및 기존 후속 항목은 유지한다.
