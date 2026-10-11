# 70차: coroutine close의 위임과 finally 정리

69차를 `7559718` (`fix: forward coroutine wrapper send and reduce dispatch allocations`)로 커밋했다. 이번 변경은 검증 후 미커밋으로 남긴다.

## 원인과 수정

기존 PyCoroutine.Close는 완료 상태만 표시했다. 따라서 중단된 await의 iterator와 coroutine 본문의 finally가 실행되지 않았다. 정상 반환/본문 예외 후의 상태 정리를 private Complete로 분리하고, 공개 Close는 위임 대상의 close를 호출한 뒤 GeneratorExit를 프레임에 주입하도록 수정했다. wrapper.close도 같은 coroutine 종료 경로로 연결했다.

예외를 주입할 때는 중단했던 YIELD_VALUE의 명령 위치를 유지한다. 기존 PendingException과 VM 예외 table 처리로 finally를 실행하며, 정상 send만 다음 명령으로 진행한다. delegate의 close 호출이 발생시킨 예외도 같은 위치에 주입해 외부 coroutine의 except/finally가 처리하도록 한다. 중첩 native wrapper는 직접 닫고, 일반 사용자 iterator는 close 속성을 조회한다. close가 없는 iterator는 허용한다.

생성만 하고 실행하지 않은 coroutine은 본문을 실행하지 않고 닫는다. 정상 종료, 반복 close, 인자 검사, 완료 뒤 send 오류를 유지한다. close 중 다시 await하여 값을 yield하면 RuntimeError를 내되 중단 상태를 보존한다. 이후 send로 마치거나 다시 close할 수 있다.

## CPython 비교에서 바로잡은 부분

실제 CPython 3.12.15와 pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/genobject.c:gen_close, gen_close_iter, _PyGen_yf, coro_wrapper_close를 확인했다.

처음에는 Close 입구에서 실행 중 여부를 검사했다. 그러나 실제 CPython은 위임 대상의 close를 호출한 뒤 gen_send_ex 단계에서 실행 중인 coroutine의 재진입을 거부한다. callback에서 바깥 coroutine.close를 다시 호출하면 delegate.close가 한 번 더 호출될 수 있다. 상태 검사 위치를 바로잡고, 저장한 원래 상태를 복원한다. 무제한 재귀에 의존하지 않는 callback으로 실제 호출 순서와 횟수를 회귀에 남겼다.

delegate가 직접 GeneratorExit를 발생시키는 테스트에서는 VM의 단일 인자 raise 경로가 PyException만 인정하고 PyBaseException을 제외하는 기존 문제가 드러났다. 클래스 생성 결과와 이미 생성된 인스턴스 판정을 PyBaseException으로 넓혔다. GeneratorExit가 일반 Exception으로 바뀌거나 잘못된 TypeError가 되지 않으며, 인스턴스 정체성을 보존한다. BaseException 및 GeneratorExit의 클래스/인스턴스 raise를 검증했다. raise ... from 경로 전체를 수정한 것은 아니다.

## 검증

- .NET Release 빌드 성공.
- 기본 132개: 완전 일치 127개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- 신규 coroutine_close_regression.py: 미시작/완료/반복 close, 중첩 정리 순서, wrapper.close, close 없는 iterator, delegate 오류와 바깥 catch, delegate/body 재진입, GeneratorExit args, 종료 중 await 후 재개/재종료, close 인자 오류, delegate의 StopIteration/GeneratorExit, finally 자체의 예외, BaseException 클래스/인스턴스 raise를 CPython과 비교했다.
- 기존 coroutine_close_cleanup.py를 기본 통과 목록으로 승격했다. 변경 전에는 두 파일이 실패 또는 출력 차이를 냈다.
- CFG native probe 통과.
- Godot 소스 포함 빌드 성공. 기본 17개 및 native helper 통과, 추가 44개 모두 CPython stdout과 정확히 일치했다. 합계 61개 fixture. 모바일 실기는 검사하지 않았다.
- git diff --check 통과.

원본 출력과 성능 측정은 [round70-validation.json](round70-validation.json)에 보존했다. 전체 로그는 `.artifacts/round70-full.json`, `round70-godot-*.log`에 있다.

## 성능 범위

기존 close는 정리를 실행하지 않았으므로 전후 close 시간을 개선율로 비교하지 않는다. delegate 종료는 기존 generator 및 wrapper의 native 호출을 사용하고, 스택 복사나 별도 실행 엔진을 추가하지 않았다.

일반 generator_iteration과 정상 완료하는 coroutine_send를 대조군으로 사용했다. .NET 8.0.31, TieredCompilation=0, 1만 회, 프로세스당 warmup 10회/측정 10회, 순서를 바꾼 전후 3쌍이다. 빌드·회귀·Godot 검증이 끝난 뒤 순차 측정했으며 모든 합계 검사를 통과했다. 시간은 프로세스 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| generator_iteration | 53.933 | 54.193 | 6,242,352 | 6,242,352 |
| coroutine_send | 293.492 | 295.613 | 45,034,600 | 45,034,600 |

할당은 동일하다. 시간 중앙값은 약 0.48%, 0.72% 증가했다. 각 프로세스 중앙값 범위는 generator 이전 53.534–53.993 ms/이후 53.863–54.425 ms, coroutine 이전 292.500–294.850 ms/이후 293.904–296.912 ms로 겹친다. 이 결과로 속도 개선이나 성능 회귀 부재를 단정하지 않는다. 이번 변경의 주목적은 누락된 종료 동작의 복구이며 대조군에는 추가 임시 객체 할당이 관측되지 않았다.

## 남은 문제와 다음 순서

1. coroutine_close_lookup_error.py: close 메서드 호출의 오류와 close 속성 조회 자체의 오류는 다르다. CPython은 후자를 sys.unraisablehook으로 보고한 뒤 GeneratorExit 종료를 계속한다. 현재 SharpPy에는 공통 unraisable 보고 경로가 없으며 조회 오류를 본문으로 전달한다. 수정 전후 모두 CPython과 차이가 있어 기본 통과 목록에 넣지 않았다. 다음은 이 조회 오류와 hook 처리를 먼저 해결한다. 이번 변경을 close 전체 호환성 완료로 해석하지 않는다.
2. coroutine 및 wrapper의 throw 예외 정규화·위임·exception table 적용을 이어서 검증한다. 기존 generator.Throw의 제한된 타입 변환을 그대로 복사하지 않는다.
3. 추가 회귀를 작성하면서 SystemExit 이름의 기본 namespace 미등록과 KeyError 문자열의 따옴표 차이도 확인했다. 이번 close 검증은 등록된 BaseException/GeneratorExit 및 ValueError를 사용하며, 앞의 별도 문제를 고쳤다고 주장하지 않는다.
4. awaitable 검증·예외 전달, coroutine kwargs, async generator __kwdefaults__, lambda yield-from 예외 table, 공통 descriptor __get__, 기존 성능 관측 및 나머지 후속 항목은 유지한다.
