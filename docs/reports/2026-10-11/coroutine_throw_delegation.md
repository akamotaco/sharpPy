# Coroutine throw 위임과 예외 정규화 — 72차

71차를 `5cf7f00` (`fix: report unraisable close lookup errors and preserve Godot paths`)로 커밋했다. 이번 변경은 다음 검토를 위해 작업 트리에 남긴다.

## 문제와 수정

coroutine과 coroutine_wrapper에 throw가 없어 중단된 await 대상으로 예외를 보낼 수 없었다. throw descriptor를 추가하고 CPython의 위임 순서를 적용했다. iterator의 throw 조회가 AttributeError이면 바깥 coroutine에 예외를 주입한다. 다른 조회 오류는 coroutine을 재개하지 않고 호출자에게 전달한다. 메서드 호출이 반환하면 그 값을 yield 결과로 전달하고, 호출 중 발생한 예외는 원래 await 위치에서 처리한다. StopIteration의 값 회수는 기존 CLEANUP_THROW 명령을 사용한다.

GeneratorExit는 delegate를 먼저 close한 뒤 바깥 frame으로 주입한다. close와 throw가 동일한 delegate 종료 처리를 공유한다. 실행 중 상태는 delegate 호출 동안 설정하고 finally에서 복원한다. 종료되었거나 실행 중인 coroutine에 예외를 넣으려는 경우 pending exception을 남기지 않는다. 정확한 native generator/wrapper 타입은 직접 호출해 임시 bound descriptor 생성과 인자 복사를 피한다. 사용자 iterator는 실제 속성 조회와 호출을 사용한다.

기존 generator.Throw에는 예외 이름별 switch와 임의의 `generator exception` 메시지가 있었다. 이를 삭제하고 coroutine과 공통인 PySuspensionException.Normalize에서 실제 예외 생성자를 호출한다. 예외 인스턴스와 traceback은 보존하며 생성자에서 발생한 Python 예외는 중단된 frame으로 주입한다. 종료된 generator는 전달된 예외를 발생시키도록 수정했다. 기존 무조건 StopIteration 반환과 중복 종료 catch를 제거했다. 인스턴스 경로에 정규화용 새 인자 배열을 만들지 않는다.

참조는 실제 CPython 3.12.15 및 pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/genobject.c:_gen_throw, gen_throw, gen_send_ex2와 Python/errors.c의 예외 정규화다. bytecode를 동일하게 만드는 작업은 아니다.

## 검증

- .NET Release 빌드 성공.
- 기본 137개: 출력 완전 일치 132개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- native 직접 위임 경로 추가 후 throw·close·unraisable 관련 4개를 최종 재검증했다.
- 새 coroutine_throw_regression.py는 coroutine/wrapper, 예외 수신 후 다시 yield 및 반환, 중첩 await, 시작 전 throw, 완료 후 재사용, 인자 오류, 조회 오류와 호출 오류 구분, 임의 객체의 원형 전달, GeneratorExit 종료, 재진입, 생성자 오류, 예외·traceback 보존, generator 직접 throw를 비교한다.
- 기존 coroutine_throw_delegation.py를 기본 통과 목록에 포함했다. 두 fixture 모두 이전 DLL에서 실패하는 것도 확인했다.
- Godot에서 저장소 소스를 직접 포함해 빌드하고 기본 17개 및 native helper를 실행했다. 추가 49개를 CPython 출력과 비교해 모두 통과했다. 합계 66개 fixture이며 별도 SharpPy DLL 참조를 추가하지 않았다.
- Godot traceback fixture 1개는 기존과 같이 컴파일 입력 경로만 절대 경로에서 res:// 경로로 바꾸어 비교했다. 줄 번호와 소스 출력은 그대로 비교했다.
- CFG reachability native probe 통과. 모바일 실기는 실행하지 않았다.

검증 요약과 새 fixture 출력, 실패 재현, 성능 원본은 [round72-validation.json](round72-validation.json)에 보존한다. 전체 실행 로그는 `.artifacts/round72-full.json`, 최종 대상 검증은 `round72-final-targeted.json`, Godot 비교는 `round72-godot-comparison.json`이다.

## 성능

정상 send 경로에는 변경이 없다. close의 delegate 조회와 종료 코드는 throw와 공유하도록 분리했다. 이전 버전의 coroutine.throw는 동작하지 않으므로 전후 처리량 개선율을 산출하지 않는다. generator_close와 coroutine_send를 대조군으로 사용했다.

.NET 8.0.31, TieredCompilation=0, 1만 회, warmup 10회·측정 10회, 전후 교대 3쌍이다. 빌드·기능 검증과 성능 측정은 겹치지 않았다. 모든 workload 반환 합계 검증을 통과했다. 시간은 각 프로세스 중앙값 3개의 중앙값이다.

| workload | before ms | after ms | before allocated B | after allocated B |
| --- | ---: | ---: | ---: | ---: |
| generator_close | 274.121 | 271.674 | 27,361,184 | 27,361,184 |
| coroutine_send | 313.348 | 312.856 | 45,034,600 | 45,034,600 |


두 대조군의 할당량은 동일하다. 시간 중앙값은 각각 약 0.89%, 0.16% 감소했다. generator_close 프로세스 중앙값 범위는 이전 273.456–276.975 ms, 이후 269.951–272.602 ms이고 coroutine_send는 이전 309.145–313.580 ms, 이후 306.371–316.137 ms다. 작은 차이이며 send 범위는 겹친다. 이를 일반적인 속도 개선으로 주장하지 않는다.
## 다음 순서와 남은 차이

아래는 통과 목록에 넣지 않은 실패 재현이다. 호환성이 완성되었다고 보지 않는다.

1. coroutine_throw_yield_from.py: __await__ generator 내부에 yield from이 있으면 generator.Throw가 내부 iterator까지 위임하지 않는다. CPython은 내부에서 ValueError를 잡고 31을 반환하지만 SharpPy는 ValueError를 밖으로 보낸다. 다음은 generator의 재귀 위임과 그 호출·종료 상태 처리다.
2. coroutine_throw_empty_args.py: 실제 ValueError 생성자를 쓰지만 기존 생성자 자체가 인자 없는 경우 args를 () 대신 ('',)로 만든다. throw에만 보정 코드를 넣지 않고 공통 예외 생성자를 분석해야 한다.
3. coroutine_throw_legacy_warning.py: 2~3개 인자 형태의 DeprecationWarning이 없다. 기존 generator도 같은 경고 체계를 갖추지 않았다. 이번에는 인자 전달·정규화 경로를 연결했으며 경고 호환성은 별도 미해결이다.
4. 기존 문서의 awaitable 검증, coroutine kwargs, async generator, traceback 세부 형식, module delattr 등은 그대로 후속 항목으로 유지한다.
