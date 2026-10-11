# 84차: winerror의 int 하위 타입과 큰 정수 저장

83차를 `51b3bf1`으로 커밋했다. 이번 변경은 Windows winerror가 int 하위 타입일 때의 변환과, 이를 검증하는 과정에서 확인한 정수 하위 타입 생성·repr의 값 축소 문제를 수정한다. errno의 사용자 정의 해시·동등 비교 선택은 별도 과제로 남긴다.

## errno와 winerror를 같은 방법으로 처리하면 안 되는 이유

CPython 3.12.15 고정 소스 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Objects/exceptions.c`를 확인했다. oserror_parse_args는 Windows 정수 winerror를 PyLong_AsLong으로 읽는다. int 하위 타입의 __int__/__index__/__hash__/__eq__를 호출하지 않는다.

반면 OSError_new는 errno가 PyLong_Check를 통과하면 errnomap 사전을 조회한다. 이때 int 하위 타입의 사용자 정의 __hash__/__eq__가 실행될 수 있다. 실제 CPython에서도 이를 확인했다. 예를 들어 값은 999이지만 hash가 2이고 비교가 True인 하위 타입은 FileNotFoundError를 선택할 수 있다. __hash__=None이면 TypeError가 발생한다. 따라서 errno까지 무조건 내부 정수값으로 바꾸면 결과와 callback을 잃는다.

이번 수정은 winerror 경로만 내부 값으로 읽는다. 일반 PyInt를 먼저 확인하고 실패했을 때만 기존 PyIntSubclass.GetIntValue를 사용한다. 임시 정수 객체를 추가하지 않고 원래 winerror 객체와 인자 identity를 보존한다. 정규화된 errno는 기존처럼 일반 int다.

## 큰 값의 선행 오류 수정

경계 테스트에서 int.__new__의 하위 타입 생성 경로가 기존 PyInt 값을 long으로 축소하는 문제가 드러났다. 임의 정밀도 값을 그대로 보존하고 기존 PyIntSubclass(PyClass, PyInt) 생성자로 전달하도록 바꿨다. 이로써 큰 int 하위 타입을 먼저 만들고 Windows C-long 범위 검사에서 올바른 OverflowError를 낼 수 있다.

추가 검증에서 PyIntSubclass.ToRepr도 값을 32-bit 정수로 축소하고 있었다. 기존 내부 PyInt의 ToRepr로 위임해 큰 값의 표시와 비교 검증을 통과시켰다. 별도 숫자 표현이나 캐시는 만들지 않았다.

큰 문자열의 파싱은 별개다. 기존 FromString 경로에서 큰 십진 문자열이 실패하는 현상은 `tests/compatibility/int_subclass_large_string.py`로 보존했다. 이번 변경을 모든 int 하위 타입 변환의 완성으로 표현하지 않는다.

## 검증

`tests/os_error_winerror_subclass_regression.py`를 기본 평가에 추가했다.

- __int__/__index__/__hash__/__eq__가 호출되면 즉시 실패하는 int 하위 타입으로 winerror 변환을 검사했다.
- OSError 및 이미 지원하는 직접 하위 타입 5개에서 errno, 타입 선택, args, filename2, str, 원본 winerror identity를 비교했다.
- 32-bit 범위 경계와 초과, 큰 정수, HRESULT, Winsock, 파일명 유무, 6개 인자에서 변환 생략, kwargs 오류 우선순위를 검사했다.
- 해시 불가능한 int 하위 타입은 winerror로 허용되고, 단순 __index__ 객체는 변환하지 않음을 확인했다.
- 큰 양수/음수의 하위 타입 생성, str/repr 및 동등 비교를 CPython과 대조했다.
- 수정 전 DLL에서 새 회귀 파일이 실패함을 확인했다.

최종 Release 빌드 및 전체 평가 150개 통과: 출력 일치 145개, 지정 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개다. Godot 소스 포함 자체 빌드와 기본 17개 및 관련 6개, 총 23개 fixture가 통과했다. 관련 6개 출력은 CPython과 일치한다. `git diff --check`도 통과했다.

검증 환경은 Windows, CPython 3.12.15, .NET 8.0.31이다. 모바일 실기와 비Windows 실행은 하지 않았다. 별도 SharpPy DLL이나 외부 native 의존성을 추가하지 않았다.

## 성능 대조

기존 세 OSError workload와 작은 정수의 int 하위 타입 생성 workload를 비교했다. 수정 전후 결과가 같은 범위로 제한했으며, 이전에 실패하던 큰 값이나 winerror 하위 타입을 속도 비교의 기준으로 사용하지 않았다.

Release, TieredCompilation=0, 반복 10만 회, 워밍업 10회, 표본 15회, 전후 3쌍 교대 실행이다. 빌드/기능 검증과 겹치지 않았다. 아래는 프로세스 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 변화 | 양쪽 할당량 |
|---|---:|---:|---:|---:|
| os_error_fallback | 77.4132 | 77.6085 | +0.25% | 25,904,120 B |
| os_error_construction | 105.1842 | 104.6274 | -0.53% | 41,104,040 B |
| os_error_winerror | 90.8269 | 93.0413 | +2.44% | 41,104,120 B |
| int_subclass_construction | 540.2178 | 534.5120 | -1.06% | 124,304,032 B |

모든 workload의 프로세스 중앙값 범위가 겹친다. 일반 winerror 생성의 증가 관측은 남기며, 감소한 다른 workload도 유의한 속도 향상으로 주장하지 않는다. 일반 생성과 작은 int 하위 타입 생성의 할당량은 동일하다. 앞선 회차에서 관찰한 생성 시간 증가가 해결되었다고 결론 내리지 않는다.

[검증 출력과 측정 표본](round84-validation.json)을 보존한다.

## 다음 작업

`tests/compatibility/os_error_int_subclass.py`를 errno의 일반 하위 타입, __hash__/__eq__ callback, 해시 불가능한 타입 사례로 갱신했다. 실제 CPython과 출력 불일치를 확인했으며 기본 통과 목록에 넣지 않았다. 다음에는 이 사전 조회 의미를 보존하면서 일반 정수 경로에 불필요한 비용을 넣지 않는 방법을 검토한다. 큰 문자열 파싱 실패도 별도 파일로 추적한다.
