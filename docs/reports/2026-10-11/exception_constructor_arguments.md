# 기본 예외 생성자의 인자 보존 — 74차

73차를 `1834719` (`fix: forward generator throw through yield-from delegates`)로 커밋했다. 이번 변경은 다음 검토를 위해 작업 트리에 남긴다.

## 원인과 수정

PyType.CreateInstance가 Python의 인자 배열에서 첫 문자열만 추출해 C# 메시지 생성자를 호출했다. 인자가 없으면 빈 문자열 하나가 args에 들어가고, 숫자·None·여러 인자는 손실되었다. coroutine.throw(ValueError)의 빈 args 불일치도 이 공통 생성 경로에서 발생했다.

BaseException 방식으로 생성되는 15종에 인자 배열을 그대로 전달하는 생성 경로를 연결했다. BaseException, Exception, TypeError, ValueError, ArithmeticError, ZeroDivisionError, OverflowError, LookupError, IndexError, RuntimeError, SystemError, NotImplementedError, RecursionError, AssertionError, GeneratorExit가 대상이다. 기존 내부 배열/관측 시 tuple 보존 방식을 사용하며, 사용자 객체의 str/repr을 생성 시점에 호출하지 않는다. 빈 인자와 명시적인 빈 문자열을 구별하고 키워드 인자를 거부한다.

C# 내부에서 메시지로 오류를 만드는 기존 생성자는 유지했다. Python 인자 보존 경로는 기존 배열 생성자를 확장했으며 BaseException/Exception 파생 클래스의 접근도 유지했다. 별도 속성·생성 규칙을 가진 예외에 일괄 적용하지 않았다.

CPython 3.12.15 및 pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/exceptions.c:BaseException_new, BaseException_init를 참조했다. CPython은 원래 인자를 보존하고 기본 init에서 키워드를 거부한다.

## 검증

- .NET Release 빌드 성공.
- 기본 141개: 완전 출력 일치 136개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- exception_constructor_arguments_regression.py에서 15종의 빈 인자·빈 문자열·일반 문자열·정수·None·여러 인자·tuple 인자, str/repr, args tuple identity, 키워드 거부를 비교했다. 사용자 객체의 identity와 str/repr 호출 시점, args 변경, generator.throw(ValueError)도 비교했다.
- coroutine_throw_empty_args.py를 기본 통과 목록으로 승격했다. 두 새 통과 fixture는 이전 DLL에서 실패하거나 출력이 달랐다.
- 생성자 접근 지정자를 최종 정리한 뒤 두 fixture를 .NET/Godot에서 재검증했다.
- Godot 소스 포함 빌드 성공. 기본 17개 및 native helper, 추가 53개 출력 비교 통과. 합계 70개 fixture. 기존 traceback 1개는 컴파일 입력 파일명만 res:// 경로로 바꾸어 비교했다. 별도 SharpPy DLL 의존성을 추가하지 않았으며 모바일 실기는 실행하지 않았다.
- CFG reachability probe 및 git diff --check 통과.

검증 원본 요약은 [round74-validation.json](round74-validation.json), 상세 로그는 `.artifacts/round74-full.json`, `round74-final-targeted.json`, `round74-godot-comparison.json`에 보존했다.

## 성능

수정 전후 같은 결과인 ValueError("message") 반복 생성 workload를 추가했다. 인자 배열과 PyString을 다시 만들지 않아 생성 1만 회당 640,000 B가 감소했다. 인자 없는 예외의 잘못된 결과와 정상 결과를 성능 개선율로 비교하지 않는다. generator_close는 기존 내부 메시지/종료 경로 대조군이다.

.NET 8.0.31, TieredCompilation=0, 반복 1만 회, warmup 10회·측정 10회, 전후 교대 3쌍이다. 빌드·검증과 측정은 겹치지 않았다. workload 반환 합계 검증을 통과했다. 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | before ms | after ms | before allocated B | after allocated B |
| --- | ---: | ---: | ---: | ---: |
| exception_construction | 6.771 | 6.582 | 2,641,184 | 2,001,184 |
| generator_close | 270.567 | 271.696 | 27,361,184 | 27,361,184 |


예외 생성 시간 중앙값은 약 2.79% 감소했다. 프로세스 중앙값 범위는 이전 6.685–6.790 ms, 이후 6.482–6.665 ms다. 할당 감소는 24.2%이며, 전체 애플리케이션 속도 향상을 뜻하지 않는다. generator_close는 할당량이 동일하고 시간 약 0.42% 증가했다. 이전 269.806–270.725 ms/이후 269.603–272.523 ms로 범위가 겹친다.
## 다음 순서와 범위

1. exception_specialized_arguments.py에서 NameError()/AttributeError()의 빈 args, KeyError('key')의 따옴표 표현, OSError(2, 'missing')의 인자·표현 차이를 재현했다. 다음은 이 예외들의 개별 생성·속성 규칙을 CPython과 비교하는 작업이다.
2. ArithmeticError의 builtin 이름 등록이 없고 type.__base__도 지원하지 않는 기존 차이를 확인했다. 이번 생성자 회귀에서는 CPython에도 유효한 ZeroDivisionError.__bases__[0]로 해당 타입을 가져왔다. 일부 이름은 builtins에서 명시적으로 import했다. 이름 등록·type 메타데이터는 별도 후속 항목이다.
3. 구식 throw 인자 형태의 DeprecationWarning, generator close 재진입 순서, traceback 세부 형식, awaitable 검증, coroutine kwargs 및 기존 미해결 항목은 유지한다. 이번 결과를 전체 예외 API 호환성으로 확대하지 않는다.
