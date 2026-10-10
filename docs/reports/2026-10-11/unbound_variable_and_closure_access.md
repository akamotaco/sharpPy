# 66차: 미초기화 변수의 예외 일치와 closure 접근 할당 제거

작업 중 이어진 커밋 요청에 따라 65차 finally 수명 수정을 `206930d` (`fix: balance finally cleanup scopes during control transfers`)로 커밋했다. 이번 66차 변경은 검증 후 미커밋으로 남긴다.

## 동작 수정

LOAD_FAST/LOAD_FAST_CHECK에서 초기화되지 않은 local을 읽으면 UnboundLocalError와 CPython 3.12 메시지를 사용한다. DELETE_FAST도 값이 없는 변수를 지우면 같은 오류를 낸다. LOAD_FAST_AND_CLEAR는 comprehension의 빈 값 보존에 필요하므로 기존 동작을 유지했다.

cell/free variable 읽기·삭제는 소유 scope에 따라 구분한다. 함수 자신의 cell은 UnboundLocalError, enclosing scope의 free variable은 NameError이며 free variable 이름도 name 속성에 남긴다. NameError/UnboundLocalError의 name 속성은 기본 None, 대입 가능, 삭제 후 None인 실제 CPython 동작을 따른다. 기존 C# 생성자 및 Create(message) API를 유지하고 이름을 받는 overload를 추가했다.

MAKE_CELL은 초기화되지 않은 값을 None으로 바꾸고 있었다. 인자로 받은 None은 값으로 보존하되, 아직 바인딩되지 않은 local은 빈 cell로 시작하게 했다.

`del x`만 있는 함수도 x를 local binding으로 분석해야 한다. symbol table에서 delete target을 binding context로 분석하고, DELETE_FAST의 local slot을 확보하도록 수정했다. global/nonlocal 선언과 attribute/subscript target은 기존 scope 구분을 사용한다.

## 구조와 비용

LOAD_DEREF, STORE_DEREF, DELETE_DEREF, MAKE_CELL의 중복 offset 해석을 기존 ResolveDerefOperand로 통합했다. 매번 non-parameter cell 이름 목록을 할당하던 코드를 없애고 이미 있는 CellVarIndexMap/VarNameSet을 재사용한다. 새로운 offset cache나 외부 라이브러리는 추가하지 않았다. 내부 Cells의 free-first 배치는 유지한다.

이 과정에서 stdlib enum의 nonlocal 저장이 잘못된 offset을 내는 기존 컴파일러 경로를 확인했다. EmitStoreName은 컴파일 중간의 varNames.Count에 free index를 더하고 있었으며, 이후 local이 늘어나면 잘못된 위치를 가리켰다. cell/free index를 방출하고 FixCellOffsets가 최종 localsplus offset을 결정하게 했다. DELETE_DEREF의 nonlocal 경로에도 cell count를 포함했다. local cell과 여러 free variable이 섞인 저장·삭제 회귀를 추가했고 typing→re→enum import를 사용하는 기존 복합 테스트도 통과했다.

CPython 3.12.15 고정 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Python/bytecodes.c(LOAD_FAST_CHECK, DELETE_FAST, MAKE_CELL, LOAD/DELETE_DEREF), Python/ceval.c(format_exc_unbound, format_exc_check_arg), Python/symtable.c(Name context)를 확인했다. CPython의 내부 배열 배치를 복제하는 대신 같은 결과를 내는 SharpPy 경로를 줄였다.

## 검증

- Release 빌드 성공. 기존 TokensReader nullable 경고 1개.
- 기본 125개: 완전 일치 120개, 지정된 변동값 제외 일치 3개, native 검사 2개 통과.
- unbound_fast_local.py를 기본 목록에 편입하고 unbound_variable_regression.py를 추가했다.
- 실제 CPython 3.12.15와 오류 타입·문자열·args·name을 비교한다. 미초기화/삭제된 fast local, 중복 삭제, delete-only cell, captured parameter, free read/delete, nonlocal 재바인딩, mixed local/free cell, except target 삭제, comprehension의 빈 local 복원을 검사한다.
- 전체 검사 후 추가한 captured argument None 보존 assertion도 CPython 비교 및 Godot 재실행 통과.
- CFG native probe 통과.
- Godot 소스 포함 빌드, 기본 17개+추가 37개=54개 fixture 및 native range helper 통과. 외부 SharpPy DLL 의존성을 추가하지 않았으며 모바일 실기는 검사하지 않았다.
- git diff --check 통과.

최종 로그는 `.artifacts/round66-full-final.json`, `round66-final-fixture.json`, `round66-godot-*.log`다. 초기 full 검사에서 enum import가 실패한 기록과 최종 통과를 구분한다. 요약·측정 원본은 [round66-validation.json](round66-validation.json)에 보존했다.

## 성능

.NET 8.0.31, TieredCompilation=0. 프로세스당 warmup 10회/측정 10회, 전후 교대 3쌍. workload는 1만 회이며 각 결과가 sum(range(n))인지 확인했다. 빌드·전체 회귀·Godot 실행이 끝난 뒤 측정했다. 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| closure_access | 12.311 | 9.888 | 8,963,928 | 7,763,752 |
| nonlocal_update | 15.550 | 12.648 | 9,123,976 | 8,163,800 |

시간은 각각 약 19.7%, 18.7% 감소했다. 할당은 각각 1,200,176 B, 960,176 B 감소했으며 세 프로세스에서 같은 값이었다. 전후 프로세스 중앙값 범위도 겹치지 않았다. 정상 closure 읽기/쓰기의 제한된 workload 결과이며 전체 프로그램의 개선율로 일반화하지 않는다. 오류 발생 비용의 성능 측정은 아니다.

## 다음 순서

async/lambda/generator의 컴파일 플래그 결정을 조사해 IsGenerator의 남은 명령열 검색 fallback을 제거할 수 있는지 확인한다. LOAD_CLOSURE 등 남아 있는 cell offset 해석 중복도 같은 기존 resolver로 줄일 수 있는 후보지만, 그 경로의 인자 방출과 실제 CPython 결과를 먼저 확인한다. 전역 이름 조회의 NameError metadata와 except* 정리 수명은 별도 후속 호환성 항목이다.
