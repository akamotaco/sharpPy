# 101차: set/frozenset 해시 감사와 선별 수정

기준: [CPython v3.12.15 setobject.c](https://github.com/python/cpython/blob/v3.12.15/Objects/setobject.c)의 set_lookkey, set_add_entry, set_merge, frozenset_hash.
Windows x64 실제 CPython 3.12.15와 대조했다.

채택한 변경은 기존 HashSet 비교기의 native Python 해시 사용과 identity 우선이다.
int/float/bool/tuple의 동등한 키, NaN 동일 객체, 비교 예외를 확인했다.
비교기를 singleton으로 공유해 CLR HashSet 복사 시 재해시를 없앴다.
기본 회귀 174개, Godot 소스 빌드 및 기본 17개+관련 3개 통과.

10000회, 워밍업 10회, 표본 10개, 교차 3쌍, CPU mask 0x4,
TieredCompilation=0으로 기존/최종 DLL을 비교했다.
- set.copy(64개): 프로세스 중앙값의 중앙값 20.5281→12.0758 ms, 약 41% 감소.
- 생성/add/contains/remove: 16.89555→17.33725 ms, 약 2.6% 증가 관측.
  정확한 Python hash 적용 비용이 포함되어 있고 속도 개선으로 주장하지 않는다.
- 조회: 6.03085→6.0781 ms, 표본 범위 겹침.
- comparer 객체 생성을 제거했다. 할당 원시값은 singleton 측정 JSON에 있다.

기각한 후보: CPython의 전체 폭 해시·probe·재진입·frozen hash를 구현한
저장소 교체는 별도 회귀가 일치했지만, 밀집 인덱스를 적용해도 생성/갱신이
16.84075→18.40905 ms로 약 9.3% 느렸다. 이 런타임 코드는 채택하지 않았다.
따라서 **전체 set 해시 호환성 완료가 아니다**.

`tests/compatibility/set_hash_storage_remaining.py`와 audit 원시 출력은
32비트 bucket 해시 충돌 callback, 생성/복사의 재해시, 재진입,
frozenset native hash 차이를 보존한다. 실패 fixture는 통과 집계에서 제외했다.
조회 순서가 관찰되는 사용자 키와 기본 키를 구분해 저장소 설계를 다시
검토해야 한다. 이번에는 실패를 숨기기 위해 정규화하지 않았다.
