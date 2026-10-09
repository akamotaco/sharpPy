# 10차 LookupError builtins 등록

## 변경 및 검증

`PyLookupError`, `PyType.LookupErrorType`, KeyError/IndexError의 상속, 예외 생성 및 VM 예외 이름 매핑은 이미 구현돼 있었다. `modules/BuiltinsModule.cs`의 builtins 등록만 빠져 있어 한 줄을 추가했다. VM 매핑을 중복 추가할 필요는 없었다.

고정 CPython 소스의 [Objects/exceptions.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/exceptions.c)와 실제 CPython 3.12.15를 기준으로 확인했다. 회귀 fixture는 builtins attribute와 전역 타입 identity, 상속 및 isinstance, 생성/문자열 변환, 실제 dict/list 조회 실패 및 명시적 raise를 LookupError로 잡는 경로를 검증한다. 기존에는 실패했고 등록 후 Release에서 reference와 출력이 일치했다.

Release 전체 평가: 45개 중 출력 일치 39, 기존 출력 차이 4, native no_reported_failure 2. 새 실패는 없다. [수정 전](round10-lookup-before.json), [최종 전체 평가](round10-tests.json).

Debug 빌드도 성공했으며 fixture 실행은 종료 코드 0, stderr 없음, 최종 `lookup error builtin: PASS`를 출력했다. 단, 기존 evaluator는 Debug의 `❌ Exception Table: No handler found...` 로그를 failure marker로 집계한다. 이는 inner frame의 예외가 outer handler로 전파될 때의 진단 로그이며 이번 실행의 assertion 실패가 아니다. Debug stdout은 reference와 직접 비교하지 않는다. 원본은 `.artifacts/round10-lookup-debug.json`에 보존했다.

이 변경은 호환성 보완이며 성능 개선을 주장하지 않는다. 기존 round9 변경은 유지했고 이번 작업까지 아직 커밋하지 않았다.

## 다음 구조적 후보

`PyGlobalsDict`는 생성 시 namespace를 복사하고 SetItem/Update 일부 경로만 원본으로 반영한다. 따라서 이후 STORE_NAME에 의한 생성·변경·삭제를 retained locals/globals가 즉시 관찰하지 못한다. base PyDict의 Get/Contains/Length/iterator/views 및 일부 mutation은 내부 저장소에 직접 접근한다. wrapper GetItem만 수정하면 이 경로들 사이에 모순이 남는다.

다음 작업은 module namespace의 단일 저장소 또는 공통 storage adapter를 검토한다. retained mapping identity, 양방향 쓰기/삭제, get/contains/length, retained keys/values/items 및 iteration을 CPython 3.12와 비교하는 fixtures를 먼저 만든다. 일반 dict 작업의 비용과 module globals 관찰 비용을 각각 측정해야 한다. 매 조회마다 namespace 전체를 복사하는 방식은 후보에서 제외한다.
