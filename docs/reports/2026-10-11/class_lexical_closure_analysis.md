# 60차: implicit class cell과 lexical closure 보존

59차 변경을 `6b21479` (`fix: align metaclass iteration and avoid temporary bound methods`)로 커밋했다. 이번 변경은 검토를 위해 미커밋으로 남긴다.

## 근거와 수정

실제 conda CPython 3.12.15 및 고정 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Python/symtable.c`와 `Objects/funcobject.c`를 비교했다. 두 source 파일은 같은 commit에서 추가로 내려받았다. CPython의 class 분석은 자식 scope의 `__class__` 요구를 class closure로 해결하며, `func_descr_get`은 원래 함수로 PyMethod를 만든다. 호출 대상에 맞춰 함수의 closure를 다시 쓰지 않는다.

SharpPy는 클래스의 super 호출을 AST에서 미리 검색할 때만 class cell을 만들었다. 직접 `__class__`를 읽는 메서드는 NameError로 실패했다. 이제 enclosing scope 조회가 클래스의 implicit cell을 필요할 때 만든다. 이 cell은 클래스 namespace의 `__class__` 속성과 별개다. 자식의 요구는 해당 클래스에서 해결하고 바깥으로 전파하지 않는다. 컴파일러는 심볼 분석의 결과를 사용해 MAKE_CELL을 내보내며 클래스 본문의 super 사전 검색을 제거했다.

메서드 바인딩의 `PyFunction.Get`에 있던 함수·closure 배열·cell 복제와 class cell 덮어쓰기를 제거했다. 상속한 메서드, classmethod, metaclass 메서드도 원래 정의 위치의 class cell과 함수 identity를 유지한다.

신규 테스트에서 메서드의 global 선언 때문에 클래스 본문의 같은 이름 대입까지 STORE_GLOBAL로 바뀌는 문제가 드러났다. 클래스 본문 대입은 현재 클래스 심볼을 따라 STORE_NAME, 명시적 global, nonlocal을 구분하게 했다. 일반 클래스 namespace 속성을 메서드 closure의 cell로 승격시키는 후처리도 건너뛴다.

## 검증

- Release 빌드 성공. 기존 TokensReader nullable 경고 1개.
- 기본 114개: 출력 완전 일치 109개, 지정된 변동값 제외 일치 3개, native 검사 2개 통과.
- `class_lexical_closure_regression.py`, 기존 `metaclass_bound_method_closure.py`는 기준 DLL에서 실패하고 수정 후 통과한다.
- 직접 참조, 상속, nested function/lambda, staticmethod/classmethod, 클래스 namespace의 동명 속성, local/global 구분, 중첩 클래스, 바깥 함수의 동명 변수, metaclass super 및 함수·closure identity를 비교했다. 불필요한 cell이 없는 메서드의 `__closure__ is None`도 추가 검사했다.
- 마지막 identity/unused-cell assertion은 전체 검사 이후 추가했으며 별도 CPython 비교 및 Godot 재실행으로 통과를 확인했다.
- Godot에서 SharpPy 소스를 직접 포함해 빌드하고 기존 회귀 및 신규 2개를 합쳐 42개, native range helper를 통과했다. 외부 SharpPy DLL은 사용하지 않았다. 모바일 실기 검사는 하지 않았다.
- `git diff --check` 통과.

전체 로그는 `.artifacts/round60-full.json`, 확장 검사는 `round60-expanded.json`, 이전 DLL 비교는 `round60-before-targeted.json`, Godot 로그는 `round60-godot-*.log`다. 확장 보고서에는 아래 미해결 super alias 검사도 함께 있어 의도적으로 실패 1개가 기록된다. 기본 통과 수에는 포함하지 않았다.

## 성능

.NET 8.0.31, tiered compilation 비활성. 빌드·회귀 종료 후 동일 harness로 이전/이후 DLL을 교대 실행했다. 각 workload는 1만 회, 프로세스마다 warmup 10회 및 측정 10회, 전후 3쌍이다. 결과 합계도 검증한다. 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| class cell이 있는 metaclass 메서드 바인딩+호출 | 19.922 | 16.621 | 19,681,336 | 9,041,336 |
| 사용자 iterator iter + next | 29.272 | 29.336 | 17,443,392 | 17,443,392 |

첫 workload는 class cell을 만드는 super 분기를 포함하되 측정 인수에서는 실행하지 않는다. 따라서 이전 구현의 잘못된 super 결과를 성능 비교에 섞지 않고, 바인딩·정상 호출의 복제 비용을 비교한다. 1만 회당 10,640,000 B 감소, 시간 약 16.6% 감소다. 이 특정 경로의 결과이며 전체 프로그램 속도로 일반화하지 않는다. iterator workload의 할당은 동일하고 시간 차이 약 +0.22%는 표본 범위가 겹친다. 컴파일 시 클래스 본문 AST 검색 제거 효과는 별도로 측정하지 않았다.

측정 원본과 요약은 [round60-validation.json](round60-validation.json)에 보존했다.

## 다음 순서

`tests/compatibility/super_alias_class_cell.py`에서 `factory = super; factory()`가 실제 CPython에서는 동작하지만 SharpPy는 수정 전후 모두 실패한다. CPython은 함수에서 `super` 이름을 사용하는 것을 implicit `__class__` 사용으로 취급한다. 현재 SharpPy에는 함수 본문의 super **호출** 검색과 여러 compiler fallback이 남아 있다. 다음에는 이 이름 분석을 수정하고 중복된 compiler 보정이 필요한지 순차 검증한다.

이번 작업으로 class cell 전체 호환성을 완성했다고 주장하지 않는다. `__classcell__`의 metaclass 전달·오류 검증과 더 복잡한 comprehension/nonlocal 경계도 후속 검토 대상이다. 기존 generator throw/close 및 native iterator 관련 남은 차이도 유지된다.
