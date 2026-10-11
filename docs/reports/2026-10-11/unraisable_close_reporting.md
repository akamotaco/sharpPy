# 71차: close 조회 오류의 unraisable 보고

70차를 `897397e` (`fix: close coroutine delegates and run suspended cleanup`)로 커밋했다. 이번 변경은 검증 후 미커밋으로 남긴다.

## 원인과 수정

CPython은 delegate.close 속성 조회 중 발생한 AttributeError는 메서드 부재로 처리한다. 다른 조회 오류는 sys.unraisablehook에 보고하고 GeneratorExit 종료를 계속한다. 실제 close 메서드를 호출하다 발생한 오류는 바깥 generator/coroutine으로 전달한다. SharpPy는 조회 오류도 바깥 본문에 전달하고 있어 finally 안에서 관찰되는 예외와 외부 close 결과가 달랐다.

PyUnraisable.Report를 추가해 coroutine과 generator의 close 조회 경로에서 함께 사용한다. 정상 close와 호출 중 오류 경로는 유지한다. hook을 호출한 뒤 바깥 본문은 GeneratorExit를 받으며, hook 자체의 실패는 기본 보고 경로로 처리하고 종료를 계속한다. 보고 기능은 이 두 발생 지점에 연결했으며 모든 finalizer의 unraisable 발생을 새로 지원한 것은 아니다.

sys.unraisablehook과 sys.__unraisablehook__는 같은 기본 함수 객체로 초기화한다. 사용자 hook에는 UnraisableHookArgs를 전달한다. 예외 타입·원래 예외 객체·traceback·err_msg·대상 객체의 다섯 필드, tuple 접근, 읽기 전용 필드 및 기본 생성자 검사를 구현했다. hook이 없거나 None이면 기본 출력으로 돌아간다. 기본 출력은 현재 sys.stderr를 사용하고 flush한다. hook/출력 실패가 원래 종료를 대체하지 않도록 한다. 사용자 객체의 repr/str 실패는 전용 실패 문구로 보고하며, 이 경로는 기존 특수 메서드 조회·바인딩을 재사용한다.

참고한 소스는 CPython 3.12.15, pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/genobject.c:gen_close_iter 및 Python/errors.c:UnraisableHookArgs, _PyErr_WriteUnraisableMsg, _PyErr_WriteUnraisableDefaultHook, write_unraisable_exc_file이다. 실제 CPython으로 hook 호출 내용·순서·인자 오류·출력을 비교했다.

## Godot 검증에서 발견한 경로 문제

기본 traceback을 검증하자 Godot에 전달한 res:// 경로가 Windows Path.GetFullPath를 거쳐 잘못된 일반 경로로 바뀌고 있었다. 그 결과 co_filename이 잘못되고 소스 파일을 읽지 못해 traceback의 소스 줄이 빠졌다.

GODOT 빌드에서는 res:// 및 user:// 경로를 보존하도록 했다. 기존 Godot IO helper가 올바른 경로로 소스를 읽는다. .NET 일반 파일 경로 처리는 유지한다. 별도 라이브러리/DLL 의존성은 추가하지 않았다.

## 검증

- .NET Release 빌드 성공.
- 기본 135개: 완전 일치 130개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- 최종 Godot 전용 경로 수정 뒤 .NET의 신규 세 fixture도 재검증했다.
- unraisable_close_regression.py: coroutine/generator 양쪽 hook payload, 예외 및 객체 정체성, traceback, 현재 처리 중인 예외 보존, 읽기 전용 필드, 기본 hook 출력과 인자 검사, hook 부재/None/실패/호출 불가능 객체, stderr 실패/None, 생성자 검사, repr/str 실패, 사용자 예외 모듈명, AttributeError 조회 및 close 호출 오류와의 구분을 비교했다.
- 기존 coroutine_close_lookup_error.py를 기본 통과 목록으로 승격했다.
- unraisable_traceback_format.py는 기본 hook의 파일명·줄 번호·함수명·소스 줄·예외 출력을 포함한 전체 문자열을 CPython과 비교해 일치했다.
- CFG native probe 통과.
- Godot 소스 포함 빌드 및 기본 17개+native helper 통과. 추가 47개도 비교 통과했다. 이 중 46개는 출력 전체가 그대로 일치하며, traceback 1개는 컴파일 입력 파일명만 CPython의 절대 경로에서 Godot의 res:// 이름으로 바꾼 기대 출력과 비교했다. 소스 줄과 줄 번호 등은 제거하거나 가리지 않았다. 합계 64개 fixture. 모바일 실기는 검사하지 않았다.
- git diff --check 통과.

원본은 [round71-validation.json](round71-validation.json), 상세 로그는 `.artifacts/round71-full.json`, `round71-final-targeted.json`, `round71-godot-*.log`에 보존했다.

## 성능

기존 오류 경로는 잘못된 예외를 전달하므로 정상 구현과 처리량을 비교하지 않는다. 보고용 tuple/인자 배열은 조회 오류가 발생할 때만 생성한다. 정상 generator close와 coroutine send를 대조군으로 측정했다.

.NET 8.0.31, TieredCompilation=0, 1만 회, warmup 10회/측정 10회, 전후 교대 3쌍이다. 빌드·회귀·Godot 검증을 모두 마친 뒤 순차 측정했으며 반환 합계 검사를 통과했다. 시간은 프로세스 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| generator_close | 272.278 | 269.932 | 27,361,184 | 27,361,184 |
| coroutine_send | 297.943 | 297.112 | 45,034,600 | 45,034,600 |

두 대조군의 할당량은 동일하다. 시간 중앙값은 약 0.86%, 0.28% 감소했지만 전후 범위가 겹친다. generator_close는 이전 266.191–280.083 ms/이후 264.334–270.005 ms, coroutine_send는 이전 295.610–308.670 ms/이후 296.477–297.726 ms였다. 이 차이를 속도 개선으로 주장하지 않는다. 정상 경로에 추가 임시 객체 할당은 관측되지 않았다.

## 다음 순서와 범위

1. coroutine_throw_delegation.py로 다음 문제를 재현했다. coroutine.throw가 없어 중단된 await iterator로 예외를 전달할 수 없다. 다음은 coroutine/wrapper의 throw 정규화·위임·예외 table 처리다.
2. 이번 기본 traceback 비교는 단일 raise 프레임을 포함한다. CPython 3.12의 복잡한 표현식 column/caret 표시나 전체 struct-sequence API, audit hook 연동까지 동일하다고 주장하지 않는다.
3. 모듈 속성 삭제가 ModuleDict에 연결되지 않는 기존 문제를 확인했다. hook 부재 회귀에서는 sys.__dict__.pop으로 실제 항목을 제거했다. 일반 module delattr 수정은 별도 항목으로 남긴다.
4. 이전 문서의 awaitable 검증, coroutine kwargs, async generator __kwdefaults__, lambda yield-from table, 공통 descriptor __get__, BaseException raise ... from, 기존 성능 관측 및 나머지 후속 항목을 유지한다.
