# 59차: metaclass iterator 프로토콜과 특수 메서드 호출 비용

58차 변경을 `cc36b07` (`fix: validate iterator results and preserve sequence iteration errors`)로 커밋했다. 이번 변경은 미커밋 상태로 남긴다.

## 동작과 구조

기준은 실제 CPython 3.12.15와 고정 source commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `PyObject_GetIter`, `PyIter_Check`, `slot_tp_iter`, `slot_tp_iternext`다.

`PyClass.GetIterator`는 metaclass의 callable 속성에 cls 인수를 직접 붙여 호출하고 반환값을 그대로 내보냈다. 그 결과 invalid iterator를 허용했으며, descriptor·staticmethod·classmethod 바인딩, None에 의한 상속 메서드 차단, sequence fallback을 제대로 처리하지 않았다.

클래스 객체도 실제 타입인 metaclass MRO에서 특수 메서드를 찾는다. 사용자 인스턴스와 클래스 객체가 `InvokeIteratorMethod` 및 `InvokeNextMethod`를 공유해 바인딩·예외 처리·반환 검증을 중복 구현하지 않는다. 클래스가 iterator 자체인 경우를 위해 metaclass의 next 슬롯 검사와 호출도 연결했다. 오류 메시지는 metaclass의 실제 타입 이름을 사용한다.

Python 함수는 self/cls를 직접 넘겨 호출한다. CPython의 unbound method 호출과 같이 중간 bound method 객체를 만들 필요가 없으며, 사용자 descriptor는 기존 바인딩 경로로 처리한다. 이 경로는 기존 `PyFunction.Get`의 metaclass closure 재작성도 거치지 않는다. closure 생성 자체의 기존 문제는 아래 한계에 기록한다. 별도 reflection, 슬롯 캐시, 라이브러리 의존성은 추가하지 않았다.

## 검증

- Release 빌드 성공. 기존 TokensReader nullable 경고 1개.
- 기본 112개: 완전 일치 107개, 지정된 변동 필드 제외 일치 3개, native 검사 2개 통과.
- 신규 metaclass 프로토콜 회귀와 이전 invalid iterator fixture는 기준 DLL에서 실패하고 수정 후 통과한다.
- 상속된 iter, 클래스 자체의 동명 속성 무시, None·비호출 값, 사용자 descriptor의 instance/owner, staticmethod·classmethod, metaclass getitem fallback, 클래스 자체의 next 및 yield from 반환값, 호출 예외 identity, descriptor 조회 오류 fallback을 검사했다.
- Godot에 SharpPy 소스를 직접 포함한 빌드 및 40개 회귀, native range helper 통과. 외부 SharpPy DLL 참조 없음. 모바일 실기 검증은 하지 않았다.
- `git diff --check` 통과.

로그: `.artifacts/round59-full.json`, `round59-before-targeted.json`, `round59-godot-*.log`. 측정 원본과 요약: [round59-validation.json](round59-validation.json).

## 성능

.NET 8.0.31, tiered compilation 비활성. 빌드·회귀 종료 후 같은 harness로 이전/이후 DLL을 교대로 실행했다. 각 workload 1만 회, 프로세스별 warmup 10회와 측정 10회, 전후 3쌍이다. 결과 합계도 확인한다. 시간은 각 프로세스 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| 사용자 iterator의 iter + next | 31.109 | 30.474 | 18,243,392 | 17,443,392 |
| 사용자 iterator의 next | 24.708 | 23.252 | 10,803,392 | 10,403,392 |
| metaclass iter + native next | 22.390 | 22.451 | 11,521,336 | 11,521,336 |

사용자 iter+next 시간은 약 2.0%, next는 약 5.9% 감소했다. 작은 표본의 제한된 workload 결과이므로 전체 실행 속도로 일반화하지 않는다. 임시 PyMethod 제거로 호출당 40 B를 줄였으며, iter+next 1만 회는 800,000 B, next 1만 회는 400,000 B 감소했다. metaclass 경로는 기존에도 Python 함수를 직접 호출했으므로 할당은 동일하다. 반환 검증 등을 추가한 시간 차이는 약 +0.27%로 표본 범위가 겹친다.

## 다음 조사: __class__ closure

추가 탐색에서 metaclass 메서드가 직접 `__class__`를 읽으면 수정 전후 모두 NameError가 발생했다. 실제 CPython은 메서드를 정의한 metaclass를 반환한다. 이 사례는 `tests/compatibility/metaclass_bound_method_closure.py`로 분리해 보존했고 기본 통과 목록에서는 제외했다. 최초 확장 검사 실패 및 분리 후 통과를 구분한다.

`runtime/PySymbolTable.cs`는 클래스에 implicit class cell을 미리 만들 때 `ContainsSuperCalls`를 조건으로 사용한다. 직접 `__class__`를 참조하는 경우의 심볼 분석·cell 생성이 다음 조사 대상이다. 또한 `core/PyFunction.cs`의 Get은 metaclass에 바인딩할 때 class cell을 호출 대상 클래스로 바꾸는 기존 코드가 있다. 컴파일 문제를 해결한 뒤 명시적 bound method 호출에서도 lexical class cell을 보존하는지 CPython과 대조해야 한다. 이 두 코드의 주석을 호환성의 근거로 간주하지 않는다.

이전 generator throw/close 예외 처리, native iterator 타입 정보 및 sequence length hint의 남은 차이도 유지된다.
