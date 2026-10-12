# 125차: 호스트 변경과 Python namespace의 공통 저장소

2026-10-13 재평가: 이 수정은 namespace 구조 정리 완료가 아니다. 새 namespace,
조회 cache와 legacy adapter의 중복 경로 및 read 지연이 남는다. 현재 후속 작업과
판단 오류는 [재발 방지 기록](../2026-10-13/process_failures_and_corrective_actions.md)을 따른다.
아래 구현·측정 내용은 당시 결과로 보존한다.

후속 2번. CPython 3.12.15의 module md_dict, frame f_globals와
Objects/dictobject.c의 일반 키/문자열 키 조회 및 stored-hash merge 구조를 기준으로 했다.

## 구현과 호스트 API 변경

`PyNamespace`는 Python PyDict를 원본 저장소로 삼고, exact str만 있는 동안
문자열 이름 조회용 C# cache를 유지한다. ModuleDict와 전역 scope의 쓰기·삭제는
모두 같은 PyDict에 반영된다. 삭제/재삽입 순서, 보존 view, Python iterator의
크기 변경 검사, copy snapshot이 외부 변경을 즉시 관찰한다.

ModuleDict, frame Globals, scope Variables, function GlobalsDict의 호스트 타입은
`Dictionary<string, PyObject>`에서 `IDictionary<string, PyObject>`로 바뀐다.
호출부는 var 또는 IDictionary를 사용한다. 원본 Dictionary로 cast하여 우회 변경하는
방식은 사용할 수 없다. 새 독립 namespace는 `new PyNamespace()`로 만들며
`PythonDictionary`로 같은 Python mapping을 얻는다.

호스트 인터페이스의 Keys/Values는 보존해도 갱신되는 문자열 키 view다.
호스트 Clear는 문자열 키만 제거하고 Python의 비문자열 키를 보존한다.
Python mapping의 clear는 전체를 제거한다.

기존 `new PyGlobalsDict(Dictionary<...>)`는 외부 raw Dictionary adapter로 남는다.
그 경로의 Copy 스캔과 관찰하지 못한 삭제/재삽입 이력 제한도 그대로다.
정확한 변경 추적과 스캔 없는 복사를 원하는 호스트는 PyNamespace로 이전해야 한다.

## callback과 일반 키

비문자열 또는 사용자 타입 키를 만나면 cache를 무효화하고 Python hash/equality
조회로 전환한다. 이름과 동등한 비문자열 키도 LOAD_GLOBAL/LOAD_NAME에서 읽힌다.
비문자열 키로 기존 문자열 binding을 교체·삭제하는 경우도 같은 저장소를 변경한다.

일반 키 update 중 callback 재진입은 cache를 계속 무효화한 상태로 처리한다.
중첩 update가 끝났다고 바깥 merge 도중 cache를 활성화하지 않는다.
양쪽이 exact str인 update는 callback이 없으므로 stored hash로 merge한 뒤
들어온 항목만 cache에 반영한다. 전체 전역 재스캔을 일반 경로에 넣지 않았다.
function.__globals__ descriptor의 별도 snapshot 생성도 공통 mapping으로 바꿨다.

## 검증과 비용

- native host 19개 일치. 기존 adapter 11개 및 새 순서/view/iterator/clear 8개.
- 신규 namespace key callback/reentrant update와 관련 Python 회귀 5개 일치.
- 기본 226개: 출력 일치 221, 가변 필드 제외 3, native CLI 2.
  이 전체 실행 이후 lookup 경로 inline·host Clear 개선을 적용했으며, 최종 바이너리로
  관련 5개·native 19개를 다시 확인했다.
- Godot 소스 빌드 후 기본 17개·경로 5개·관련 3개 통과. fixture 비교는 stdout 기준.

호스트 adapter에서 owned namespace로 이전한 native Copy(100키, 1000회)는
단일 프로세스 warmup 10, 표본 10, tiering off에서 중앙값 2.86945→0.13645ms였다.
할당은 3,952,000B로 동일하다. 이는 다른 호스트 API 경로로 이전한 측정이다.

VM 측정은 10000회, 표본 15, 교차 3쌍, affinity 0x4, tiering off다.

| 경로 | 이전 ms | 이후 ms | 할당 |
|---|---:|---:|---|
| namespace update | 9.6755 | 9.4292 | 동일 |
| namespace read | 5.5777 | 5.6788 | 동일 |
| function call 대조 | 8.8735 | 8.2294 | 동일 |

read는 약 1.8% 지연이 관측되므로 전체 경로의 성능 개선이라고 하지 않는다.
대조군은 프로세스 중앙값 범위가 겹쳤다. 초기 전체 cache 재구축 후보의 큰 지연은
exact-string merge 경로로 제거했다. startup 전체 비용과 모든 호스트 패턴의
성능을 검증한 것은 아니다. 원시 결과는 round125-validation.json.
