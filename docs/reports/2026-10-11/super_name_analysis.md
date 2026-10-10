# 61차: super 이름 사용의 implicit class cell 분석

60차 변경을 `f39cc36` (`fix: create implicit class cells and preserve lexical method closures`)로 커밋했다. 이번 변경은 미커밋 상태로 남긴다.

## 기준과 변경

실제 CPython 3.12.15 및 고정 source commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Python/symtable.c: Name_kind`를 기준으로 삼았다. CPython은 함수와 같은 scope에서 `super` 이름을 **읽으면**, 호출 여부나 현재 그 이름이 가리키는 값과 관계없이 `__class__`도 사용한 것으로 분석한다.

SharpPy는 별도의 AST 순회로 특정 형태의 super 호출을 찾아 class free variable을 추가했다. `factory = super; factory()`는 누락됐고, local/global `__class__`를 가진 함수나 클래스 밖의 함수에도 compiler 보정이 free variable을 강제로 추가할 수 있었다.

이제 일반 이름 분석에서 super 사용을 기록하고 기존 scope 분석과 class cell 생성을 따른다. `PySymbolTable`의 함수 전체 재검색과 compiler의 사후 class free variable 강제 추가를 제거했다. 구형 fallback `FreeVariableAnalyzer`도 이름 읽기에서 implicit 사용을 기록하되 실제 외부 변수와 local/global 조건을 따른다.

기존 public `PythonCompiler.ContainsSuperCalls` AST query는 외부 C# 호출 호환성을 위해 남겼지만, 컴파일러와 심볼 분석에서는 더 이상 호출하지 않는다. 새 AST 검색이나 런타임 캐시는 추가하지 않았다. 함수 본문을 다시 검색하는 작업을 줄였으나 컴파일 시간의 감소량은 따로 측정하지 않았다.

## 검증

- Release 빌드 성공, 최종 빌드 경고·오류 0개. 최초 정리 중 남은 지역 변수 참조 때문에 빌드가 한 차례 실패했고, 이를 제거한 뒤 새 DLL로 재검증했다. 최초 실패 직후의 구 DLL 결과는 최종 검증에서 제외했다.
- 기본 116개: 출력 완전 일치 111개, 지정된 변동값 제외 일치 3개, native 검사 2개 통과.
- 신규 `super_name_analysis_regression.py`와 기존 `super_alias_class_cell.py`는 기준 DLL에서 실패하고 수정 후 통과한다.
- alias, 리스트 저장, 루프, 중첩 함수, lambda, generator, 명시적 두 인자 super, 같은 이름의 매개변수·지역 변수, 쓰기 전용 super, local/global class 이름을 검사했다. `co_freevars` 및 `__closure__`도 CPython과 비교했다.
- 클래스 본문의 super 참조와 클래스 밖 함수에 불필요한 closure가 생기지 않는지 검사했다.
- Godot에 SharpPy 소스를 직접 포함한 빌드 및 44개 회귀, native range helper 통과. 외부 SharpPy DLL은 참조하지 않았다. 모바일 실기 검사는 수행하지 않았다.
- `git diff --check` 통과.

전체 로그는 `.artifacts/round61-full.json`, 수정 전 비교는 `round61-before-targeted.json`, Godot 검사는 `round61-godot-*.log`다. 측정 원본 및 요약은 [round61-validation.json](round61-validation.json)에 기록했다.

## 성능 확인

.NET 8.0.31, tiered compilation 비활성. 빌드·회귀 종료 후 동일 harness에 이전/이후 DLL을 교대로 연결했다. 각 workload 1만 회, 프로세스당 warmup 10회와 측정 10회, 전후 3쌍이다. 합계 결과도 검사했다. 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| class cell을 가진 metaclass 메서드 바인딩+호출 | 17.652 | 17.813 | 9,041,336 | 9,041,336 |
| 사용자 iterator iter + next | 31.020 | 30.937 | 17,443,392 | 17,443,392 |

할당량은 동일하다. 시간 차이는 각각 약 +0.91%, -0.27%이고 전후 표본 범위가 겹친다. 실행 속도 개선이나 유의한 회귀로 해석하지 않는다. 기존 bound method 최적화의 할당 감소는 유지된다.

## 다음: 인자 없는 super의 frame 조회

`tests/compatibility/super_captured_first_argument.py`에서는 메서드의 self를 lambda가 캡처한다. 이 상태에서 super alias를 호출하면 CPython은 정상 동작하지만 현재 SharpPy는 class cell을 찾지 못했다는 TypeError를 낸다. 이 사례는 기본 통과 목록과 분리했다.

CPython `Objects/typeobject.c: super_init_without_args`는 첫 인자가 cell이면 값을 꺼내고, free variable 영역에서 class cell을 찾으며, 인자 개수·삭제·빈 cell 등의 오류를 구별한다. 다음에는 SharpPy `CallSuper`의 cell 인덱스와 frame 저장 구조, 첫 인자 추출 및 오류 순서를 조사한다. 이번 결과만으로 런타임 super 전체 호환성을 주장하지 않는다.

기존 classcell metaclass 전달, generator throw/close, native iterator 등의 남은 차이도 유지된다.
