# KeyError의 key 보존과 지연 포맷 — 76차

75차를 `997d2bc` (`fix: preserve lookup exception arguments and keyword attributes`)로 커밋했다. 이번 변경은 다음 검토를 위해 작업 트리에 남긴다.

## 수정

KeyError의 Python 생성자는 원래 위치 인자 배열을 보존하고 키워드를 거부한다. C# PyObject key 생성자도 문자열로 바꾸지 않고 key 참조를 args에 넣는다. 문자열 key를 받는 기존 C# 생성자는 문자열 인자 하나를 보존한다.

str(KeyError)는 인자가 정확히 하나이면 그 인자의 repr을 사용하고, 그 외에는 BaseException의 형식을 사용한다. args를 나중에 변경해도 현재 인자로 출력한다. 사용자 클래스의 repr은 기존 특수 메서드 조회·바인딩으로 호출해 사용자 예외와 잘못된 반환 타입을 전달한다. 문자열로 바꾸는 과정에서 실패를 삼키지 않는다.

dict 및 globals dict, instance/function 속성 dict, set.remove, defaultdict, mappingproxy에서 key.ToRepr()를 미리 계산하거나 따옴표를 붙이던 부분을 제거했다. key를 그대로 전달한다. 빈 set.pop의 메시지도 CPython의 'pop from an empty set'과 맞췄다. PyType과 기존 PyBuiltinType의 KeyError 생성 경로가 같은 인자 보존 생성자를 사용한다.

PythonException의 생성자 세 곳이 즉시 pyException.ToStr()을 호출하던 문제도 수정했다. 이를 유지하면 dict 조회 실패를 잡는 것만으로 key의 repr이 실행된다. 이제 생성 시 사용자 포맷을 실행하지 않고 C# Message를 요청할 때 현재 Python 예외를 포맷한다. Python에서 raise/catch만 하는 경우 KeyError뿐 아니라 ValueError의 사용자 str도 실행하지 않는다.

참조는 CPython 3.12.15 및 pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/exceptions.c:KeyError_str와 실제 CPython 실행 결과다.

## 검증

- .NET Release 빌드 성공.
- 기본 143개: 완전 출력 일치 138개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- key_error_regression.py: 빈 인자·문자열·숫자·None·tuple·여러 인자, args 변경, 키워드 거부, dict get/delete/pop 및 set.remove의 원래 key identity와 포맷 지연, 빈 pop 메시지, raise/catch의 사용자 포맷 미호출, 사용자 repr 실패·잘못된 반환 타입을 비교했다.
- instance/function 속성 dict와 globals.pop, 내장 _collections.defaultdict, 클래스 mappingproxy의 누락 key도 비교했다. 최종 확장 fixture를 .NET/Godot에서 재검증했다. 이전 DLL은 회귀 fixture에서 실패한다.
- Godot 소스 포함 빌드 성공. 기본 17개 및 native helper, 추가 55개 출력 비교 통과. 합계 72개. 기존 traceback fixture 1개는 컴파일 입력 파일명만 res://로 바꾸어 비교했다. 별도 SharpPy DLL 의존성은 추가하지 않았다. 모바일 실기는 실행하지 않았다.
- Godot 테스트 프로젝트에는 Python collections 패키지가 배치되지 않아 해당 import가 실패했다. 수정 대상인 내장 _collections를 직접 import하도록 fixture를 구성해 CPython과 비교했다. Python 표준 라이브러리 전체 배치 검증을 했다는 의미는 아니다.
- CFG reachability probe 및 git diff --check 통과.

원본 요약은 [round76-validation.json](round76-validation.json), 상세 로그는 `.artifacts/round76-full.json`, `round76-final-targeted.json`, `round76-godot-comparison.json`에 보존했다.

## 성능

이전의 key 문자열화 경로는 결과와 부수 효과가 다르므로 수정 전후 속도 개선율을 산출하지 않는다. 기존 ValueError 문자열 생성과 generator_close를 대조군으로 사용했다.

.NET 8.0.31, TieredCompilation=0, 반복 1만 회, warmup 10회·측정 10회, 전후 교대 3쌍. 빌드·기능 검증과 겹치지 않게 실행했고 workload 반환 합계 검증을 통과했다. 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| ValueError 생성 | 6.402 | 6.525 | 2,001,184 | 2,001,184 |
| generator_close | 260.621 | 260.382 | 27,361,184 | 27,361,184 |

할당량은 동일하다. 시간 중앙값은 ValueError 생성 약 1.93% 증가, close 약 0.09% 감소했다. 프로세스 중앙값 범위는 각각 이전 6.335–6.601 ms/이후 6.465–6.564 ms, 이전 259.285–260.639 ms/이후 259.447–261.853 ms로 겹친다. 유의한 속도 개선 또는 확정적 시간 회귀로 주장하지 않는다.

## 다음 순서와 범위

exception_specialized_arguments.py를 다시 실행했다. NameError, AttributeError, KeyError 결과는 이제 일치한다. OSError(2, 'missing')는 여전히 args와 표현이 다르므로 다음은 errno·strerror·filename 및 errno별 하위 타입 선택 규칙 분석이다. Windows와 Godot 호스트의 차이도 구분해야 한다.

이번 회귀는 직접 KeyError 인자와 위에 명시한 mapping 경로를 검증한다. 모든 컨테이너의 재귀 repr, 예외 직렬화, C# 외부 소비자의 Message 사용까지 전부 검증했다는 의미는 아니다. 이전 문서의 구식 throw 경고·builtin 등록·type 메타데이터 등 미해결 항목은 유지한다.
