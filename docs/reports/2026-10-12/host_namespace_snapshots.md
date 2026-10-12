# 119차: 외부 Dictionary 변경과 mapping snapshot

공개 생성자로 만든 PyGlobalsDict는 호스트가 원본 Dictionary를 변경할 수 있는
adapter로 취급한다. Copy 직전에 삭제/교체/추가를 반영하며 Python의 비문자열 키는
보존한다. 기존 실패 3개와 추가 clear/명시적 동기화 검사를 포함해 native 11개가 일치한다.
`SynchronizeFromHost()`는 호스트가 bulk observation 전에 호출할 수 있는 경계다.

VM 내부 canonical mapping은 별도 생성 경로로 만든다. Store/Delete를 통한 변경이
이미 동기화되므로 이 경로의 Copy에는 전체 검사를 추가하지 않았다.
일반 Python 회귀 5개(전역 identity, observation, mutation, clear, dict followup)가
CPython 3.12.15와 일치한다. 전체/Godot 소스 검증은 이번 6번의 통합 검사에 포함한다.

## 비용과 범위

100개 키의 호스트 adapter를 1000회 복사하는 native 단일 프로세스 측정에서,
기존 0.1437ms → 2.9727ms, 할당은 3,952,000B로 동일했다. warmup 10,
표본 10, tiering off. **외부 변경 검사의 시간 비용은 크며 최적화라고 부르지 않는다.**
정상 VM mapping은 이 검사를 하지 않는다. 일반 Dictionary는 값 교체를 알리는
통지가 없어 현재 API를 유지한 채 변경 여부를 O(1)에 판정할 수 없다.

전체 호스트 저장소 통합은 아직 완료되지 않았다. raw Dictionary의 삭제 후 재삽입
이력과 Python 비문자열 키와의 상대 삽입 순서는 snapshot만으로 복원할 수 없다.
정확한 과정이 필요한 호스트는 mapping의 SetItem/DelItem을 사용해야 한다.
이미 실행 중인 iterator에 대한 원본 Dictionary 직접 변경이나 runtime canonical
mapping의 원본 공개 Dictionary를 우회 변경하는 경로까지 자동 처리하지 않는다.
후속은 raw storage 노출을 추적 가능한 API로 전환하고 전체 검사 비용을 없애는 작업이다.
원시 결과는 round119-validation.json.
