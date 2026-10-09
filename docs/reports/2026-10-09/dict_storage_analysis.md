# 11차 dict 삭제 정합성과 comparer 할당

기준 commit: `a58310c` (9·10차 및 종료 진단 기록 포함). 이번 변경은 아직 커밋하지 않았다.

## 수정

`PyDict.Pop`은 해시 저장소에서 Python 동등성으로 키를 찾아 제거하지만 순서 목록에서는 CLR `List.Remove`를 사용했다. 별도로 생성된 동등한 정수 키로 pop하면 순서 목록에 삭제된 키가 남아 iteration/views/copy/popitem이 틀리거나 실패했다. 순서 목록 삭제를 기존 DelItem과 공통 helper로 묶어 같은 Python 비교 기준을 적용했다. Pop의 조회와 제거는 Dictionary.Remove(key, out value) 한 번으로 합쳤다.

comparer는 객체별 상태가 없어서 모든 PyDict 생성자 및 순서 목록 삭제에서 공유한다. 일반 dict 삭제의 순서 목록 탐색은 여전히 O(n)이다. 이는 CPython 저장 구조와 동일하다는 주장이 아니다. 사용자 정의 키의 동등성·hash 재진입 및 예외 발생 중 mutation은 이번 검증 범위에 포함하지 않았으며, 순서 목록의 추가 동등성 비교도 여전히 존재한다.

CPython 고정 소스 [dictobject.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/dictobject.c)의 `_PyDict_Pop_KnownHash`는 lookup 결과를 공통 삭제 경로로 전달한다. sharpPy의 이중 저장소에서는 조회 저장소와 순서 목록의 정합성을 별도로 유지해야 한다.

## 검증

[회귀 fixture](../../../tests/dict_pop_order_regression.py)는 distinct equal integer 키, bool/int 동등 키, 남은 keys/values/items, copy, 삭제 후 재삽입 순서, LIFO popitem, empty/default/KeyError, DelItem을 actual CPython 3.12.15와 비교한다. 기존 binary는 실패했고 수정 후 출력이 일치했다.

Release 본체와 harness 빌드 성공. 전체 평가 46개: 출력 일치 40, 기존 출력 차이 4, native no_reported_failure 2. 마지막에 추가한 bool/int 사례는 focused 평가에서도 통과했다. [기존 실패](round11-pop-before.json), [전체 평가](round11-tests.json), [최종 focused 평가](round11-focused.json).

## 할당 측정

`PerformanceHarness --dict-storage-probe`는 native C# PyDict 생성, 하나의 키 삽입 및 동일 키 pop을 10,000회 반복한다. 10회 warmup, process별 10 sample, 각각 3 process를 사용했다. Python VM 및 임의 크기의 namespace 작업을 포함하지 않는다.

comparer 공유 직전 snapshot(이미 Pop 정합성 수정 적용)과 공유 후를 비교했다. 이는 a58310c 대비 전체 변경의 성능 비교가 아니라 comparer 공유만 분리한 비교다. raw [probe 자료](round11-probe.json).

| 항목 | 공유 전 | 공유 후 |
| --- | ---: | ---: |
| 10,000회 thread 할당 B | 3,680,000 | 3,440,000 |
| 30 sample 시간 중앙값 ms | 1.03125 | 1.08455 |

모든 sample에서 dict 하나당 24B 감소했다(이 probe 총 할당 약 6.5% 감소). 시간은 약 5.2% 증가한 중앙값이며, 짧은 측정 및 실행 순서 영향 때문에 속도 개선은 주장하지 않는다. 이전/이후를 번갈아 실행한 측정도 아니며 일반 애플리케이션 성능으로 확대하지 않는다. 삭제 helper 변경 자체의 성능은 이 probe에서 분리 평가하지 않았다.

## namespace 변경 전 확보한 별도 실패

- 기존 `module_locals_live.py`: retained module mapping이 이후 생성/삭제를 관찰하지 못한다.
- 새 [module_mapping_pop.py](../../../tests/compatibility/module_mapping_pop.py): wrapper를 통해 생성한 binding을 pop해도 실제 global binding이 남는다. STORE_NAME stale snapshot 문제와 분리했다.
- 새 [dict_retained_views.py](../../../tests/compatibility/dict_retained_views.py): 일반 dict도 keys/values/items가 live view가 아니라 snapshot list다.

세 fixture는 CPython에서 통과하고 sharpPy에서 실패한다. 성공 회귀 집계에 넣지 않았다. namespace 통합을 위해 wrapper 일부 메서드만 바꾸거나 매 조회 전체 복사를 추가하지 않는다. 다음 설계는 string namespace와 PyObject dict를 공통 저장소로 연결하고 순서·삭제 이벤트를 추적할 수 있어야 하며, dict view/iterator의 live 관찰과 mutation 규칙을 별도로 검증해야 한다.
