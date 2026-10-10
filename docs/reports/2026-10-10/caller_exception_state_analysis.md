# 54차: 호출자와 generator의 처리 중인 예외 공유

53차를 `165ef9e`로 커밋했다. 이번 변경은 검토할 수 있도록 작업 트리에 남긴다.

## 구현과 CPython 대조

호출된 함수의 bare raise가 프레임 자신의 CurrentException만 확인해 호출자의 처리 중인 예외를 찾지 못했다. 기존 GetTopmostException은 실행 중인 프레임 스택을 순회하지만 LastException까지 후보로 포함해 처리 완료된 예외도 관찰했다.

GetTopmostException은 이제 CurrentException만 확인한다. RAISE_VARARGS는 이 조회를 한 번 수행해 bare raise와 명시적 raise의 context 연결에 공통으로 사용한다. sys.exception()/sys.exc_info()도 이미 이 함수를 사용하므로 같은 상태를 관찰한다.

프레임 생성·호출 시 상태 복사나 새 상태 객체를 추가하지 않았다. 기존 실행 스택의 순서로 조회하므로 suspend된 generator는 호출자에게 노출되지 않는다. 재개된 generator 자신의 handler 상태가 있으면 그것이 우선하고, 없으면 현재 재개한 호출자의 상태가 보인다. generator 생성 당시의 ParentFrame에 의존하지 않는다. 조회 비용은 활성 호출 깊이에 비례하며 예외 발생·관찰 시에만 지불한다.

CPython v3.12.15 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Python/errors.c `_PyErr_GetTopmostException`, Python/ceval.c `do_raise`, Objects/genobject.c `gen_send_ex2`의 exc_state 연결과 대조했다. 누락돼 있던 errors.c와 genobject.c는 해당 commit의 원문을 `.artifacts/cpython-locals/v3.12.15`에 내려받았다. CPython의 상태 구조체를 복제하지 않고 SharpPy의 기존 실행 스택으로 관찰 결과를 맞춘다.

## 검증

- CPython 3.12.15 비교 기본 100개: 출력 완전 일치 95개, 지정한 주소·시간 필드 제외 일치 3개, C# native 검사 2개.
- 기존 exception_state_in_callee.py와 새 exception_caller_state_regression.py를 기본 목록에 편입했다. 변경 전 DLL에서는 둘 다 실패했다.
- 새 테스트는 다중 호출의 sys.exception()/sys.exc_info(), nested handler 복원, callee의 bare raise·raise from None, generator 자체 handler 유지, 재개 호출자 변경, 생성 호출자의 예외가 누출되지 않음을 검사한다. 마지막 두 명시적 raise 경로는 전체 실행 뒤 fixture를 확장해 별도 재검증했다.
- Godot 4.7.2에서 SharpPy 소스를 직접 포함해 빌드했다. 기존 17개 및 추가 11개 fixture와 native range helper가 통과했다. 실행 로그의 host는 .NET 10.0.12이며 독립 CLI는 .NET 8.0.31이다. 모바일 실기 검증은 하지 않았다.

## 성능

동일 harness에 변경 전·후 DLL을 사용했다. .NET 8.0.31 Release, TieredCompilation=0, 10,000회, warmup 10회, 측정 10회, 이전/이후 3쌍을 순차 실행했다. 시간은 각 프로세스 중앙값 세 개의 중앙값이다.

| workload | 이전 | 이후 | 10,000회 할당(전후 동일) |
| --- | ---: | ---: | ---: |
| function_call | 10.526 ms | 10.371 ms | 7,682,488 B |
| exception_handling | 92.697 ms | 92.815 ms | 8,641,280 B |

예외 처리 시간은 약 0.1% 증가로 측정 변동 범위에 가깝다. 함수 호출 시간 차이도 이번 수정의 속도 향상으로 주장하지 않는다. 기존 workload의 할당량은 같다. 이 측정은 깊은 호출 스택에서 반복 관찰하는 비용까지 보증하지 않는다. 원시 결과와 fixture 해시는 [round54-validation.json](round54-validation.json)에 보존한다.

## 다음 작업

generator_close_exception.py에서 finally가 ValueError를 던지면 close()가 이를 그대로 전파해야 한다. 현재는 RuntimeError("generator ignored GeneratorExit")로 바꾼다. 실제 CPython은 통과하고 이전·현재 DLL 모두 실패한다. 이 재현은 기본 통과 목록에 포함하지 않았다. 다음 단계에서 gen_close의 정상 종료, cleanup 예외, GeneratorExit를 무시하고 yield하는 경우를 구분해 수정한다. GeneratorExit builtin 이름 노출 문제와 내장 연산 오류의 context 연결도 후속 검토 대상이다.
