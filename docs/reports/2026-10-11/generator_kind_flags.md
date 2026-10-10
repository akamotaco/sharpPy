# 67차: 함수 종류의 플래그 결정과 호출 중 명령열 검색 제거

66차를 `bafc2cd` (`fix: align unbound variable errors and streamline closure access`)로 커밋했다. 이번 변경은 검증 후 미커밋으로 남긴다.

## 원인과 수정

IsGenerator는 CO_GENERATOR가 없으면 전체 명령에서 YIELD_VALUE를 검색했다. 정상 함수 호출에도 검색과 열거자 할당이 발생했다. async 함수는 컴파일 후 이 판정으로 async generator 여부를 다시 정하고 code object를 재생성했다. await도 YIELD_VALUE를 사용하고, 도달 불가 yield는 CFG가 제거하므로 명령 존재 여부는 함수 종류의 올바른 기준이 아니다.

async 함수의 AST node를 기존 symbol table lookup에 등록했다. 이름 부분 문자열로 root child를 찾던 경로 대신 정확한 AST node의 scope를 사용한다. scope의 generator 여부로 CO_COROUTINE 또는 CO_ASYNC_GENERATOR를 본문 컴파일 전에 정한다. nested compiler에도 builder를 전달해 안쪽 lambda의 분석 결과를 찾게 했다. 기존 Expression lookup API는 유지했다.

컴파일 후 code object를 다시 만드는 코드를 제거했다. 원래 CompilerFunctionBody가 생성한 객체를 사용하므로 예외 table과 keyword-only 인자 수·default metadata가 보존된다. generator lambda에도 scope의 CO_GENERATOR를 전달한다. IsGenerator는 이제 synchronous generator flag만 읽으며, VM은 coroutine/async generator를 별도로 구분한다.

## CPython 확인에서 바로잡은 가정

실제 CPython 3.12.15에서 `lambda: (yield stop())`의 stop 함수가 StopIteration(5)를 내면 StopIteration이 그대로 전달됐다. 일반 def generator와 같을 것이라는 초기 가정은 틀렸다. 고정 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Python/compile.c:compiler_lambda 역시 def에 사용하는 wrap_in_stopiteration_handler를 호출하지 않는다. 추가하려던 lambda wrapper를 제거하고, 실제 동작을 회귀 assertion으로 남겼다.

같은 파일의 compute_code_flags에서 generator/coroutine 조합에 따라 세 종류를 구분하는 것도 확인했다. 전체 co_flags 숫자나 bytecode를 맞추는 작업은 아니며, 이번 회귀는 32/128/512 kind bit를 비교한다.

## 검증

- .NET Release, Godot 소스 포함 빌드 성공.
- 기본 126개: 완전 일치 121개, 지정된 변동값 제외 일치 3개, native 검사 2개 통과.
- 신규 generator_kind_regression.py: 일반/generator lambda, yield from, send 반환값, 도달 불가 yield, lambda StopIteration 전달, await-only coroutine, async generator, nested generator를 포함한 coroutine, keyword-only 인자 수, 서로 다른 scope의 동명 async 함수, async 안 lambda의 kind flag를 실제 CPython 3.12.15와 비교했다.
- CFG native probe 통과.
- Godot 소스 통합 기본 17개+추가 38개=55개 fixture와 native range helper 통과. 외부 SharpPy DLL이나 신규 의존성을 추가하지 않았다. 모바일 실기는 검사하지 않았다.
- git diff --check 통과.

최종 로그는 `.artifacts/round67-full.json`, `round67-expanded.json`, `round67-godot-*.log`에 있다. 요약·측정 원본은 [round67-validation.json](round67-validation.json)에 보존했다.

예외 table 비교 도구는 전후 모두 FAIL이다. 전후 모든 code unit의 범위/offset 왕복은 유효했다. 변경 전 async generator values와 first/same의 table이 없었지만 변경 후에는 보존된다. lambda yield-from code object의 table 누락은 전후 모두 남아 있다. 함수 종류 회귀 통과를 예외 table 전체 동등성으로 해석하지 않는다.

## 성능

.NET 8.0.31, TieredCompilation=0. 1만 회 workload, 프로세스당 warmup 10회/측정 10회, 전후 교대 3쌍. 빌드·전체 회귀·Godot 실행 후 측정했고 합계도 확인했다. 시간은 프로세스 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| constant_return | 13.152 | 12.698 | 8,321,336 | 7,601,264 |
| closure_access | 9.471 | 8.367 | 7,763,752 | 7,043,680 |

각 workload에서 720,072 B가 감소했다. 측정 시간은 약 3.5%, 11.7% 감소했다. constant_return의 전후 프로세스 중앙값 범위는 겹치므로 그 시간 차이는 확정적인 개선으로 주장하지 않는다. closure_access의 범위는 겹치지 않았지만 이 제한된 workload의 결과로만 해석한다. 컴파일 시간이나 전체 프로그램의 개선율은 측정하지 않았다.

## 남은 문제와 다음 순서

아래 두 문제는 수정 전 bafc2cd와 수정 후 모두 재현되었으며 별도 fixture로 보존했다. 기본 통과 목록에는 넣지 않았다.

1. `tests/compatibility/coroutine_initial_send.py`: 단순 coroutine의 최초 send(None)가 RETURN_GENERATOR 뒤 POP_TOP에서 빈 스택 오류를 낸다. CPython은 StopIteration.value=11로 완료한다. 다음은 coroutine 시작·재개 시의 스택/명령 위치 처리다.
2. `tests/compatibility/async_generator_kwdefaults.py`: async generator 함수의 __kwdefaults__ 노출이 없어 AttributeError가 난다. code object의 keyword-only 인자 수 보존과 Python 함수 attribute 공개는 별도 경로다.

lambda yield-from 예외 table 보존도 이어서 확인한다. 이번에는 coroutine 실행·async generator 프로토콜 전체를 고쳤다고 주장하지 않는다. LOAD_CLOSURE의 남은 offset 해석 중복, 전역 NameError metadata와 except* 정리 수명도 후속 항목으로 유지한다.
