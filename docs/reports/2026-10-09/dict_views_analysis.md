# 12차 retained dict view와 생성 할당

11차는 `a097221`로 커밋했다. 이번 변경은 아직 커밋하지 않았다.

## 구현 범위

Python의 dict.keys/values/items가 snapshot list 대신 원본 PyDict를 참조하는 PyDictView를 반환한다. 객체 생성 시 dictionary를 순회하거나 복사하지 않는다. len/bool/membership, 새로운 관찰 시 list/tuple/repr 및 iteration, indexing 거부를 구현했다. dict_keys/dict_values/dict_items 타입을 분리하고 기본 __len__/__iter__/__contains__/__repr__ descriptor도 제공한다.

일반 dict, dict의 명시적 descriptor 호출 및 dict subclass helper 세 경로를 변경했다. 기존 C# Keys/Values/Items snapshot API와 이를 쓰는 interop/compiler 코드는 유지한다. items membership은 unhashable value도 비교하며 tuple이 아닌 입력은 False다.

고정 CPython [Objects/dictobject.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/dictobject.c)의 `_PyDictView_New`, `dictview_len`, keys/items/values의 iterator 생성 경로를 확인했다. 실제 CPython 3.12.15로 출력과 assertion을 비교했다. CPython의 전체 view API와 동일하다는 주장은 하지 않는다.

## 회귀 및 남은 차이

[새 회귀](../../../tests/dict_views_regression.py)는 type 이름, 길이/진리값, membership, repr, list/tuple, 반복 관찰, 생성/값 변경/삭제/clear, 명시적 descriptor, indexing 거부, dict subclass 및 원본 변수 삭제 후 view 참조 유지를 검증한다. 기존 [retained view 실패 fixture](../../../tests/compatibility/dict_retained_views.py)도 이제 CPython과 일치한다.

Release 본체/harness 빌드 성공. 전체 47개 평가: 출력 일치 41, 기존 출력 차이 4, native no_reported_failure 2. [전체 결과](round12-tests.json), [focused 결과](round12-focused.json).

현재 iterator 자체는 기존 snapshot iterator다. view를 보관한 뒤 새 iterator를 만들면 최신 상태를 보지만, 이미 생성된 iterator에 이후 값 변경을 반영하는 동작이나 크기 변경 시 RuntimeError는 해결하지 않았다. [live iterator fixture](../../../tests/compatibility/dict_view_iterator_live_values.py)로 실패를 보존했다.

keys/items의 set-like equality 및 연산은 미구현이며 [set operation fixture](../../../tests/compatibility/dict_view_set_operations.py)가 실패한다. reversed/isdisjoint/mapping proxy 등도 후속 범위다. view가 참조하는 PyGlobalsDict의 stale 저장소 문제는 그대로다. module_locals_live와 module_mapping_pop은 여전히 실패한다. 이 네 실패 fixture는 실제 CPython에서 통과하며 성공 회귀 집계에 포함하지 않았다.

## 측정

`PerformanceHarness --dict-view-probe`는 100개 키의 dict에서 미리 바인딩한 keys 메서드를 10,000번 호출해 반환값을 생성한다. iteration 및 Python VM은 측정하지 않는다. 10회 warmup, process별 10 sample, 전후 각각 3 process다. 빌드는 측정과 분리했다. 전후 순서를 교차하지 않은 native probe이며 애플리케이션 전체 속도 개선으로 해석하지 않는다.

| 10,000회 생성 | snapshot | view |
| --- | ---: | ---: |
| thread 할당 B | 17,120,000 | 400,000 |
| 30 sample 중앙값 ms | 7.52965 | 0.29270 |

할당은 모든 sample에서 동일했으며 약 97.7% 감소했다. dict 크기에 따른 snapshot 배열 복사를 없앤 결과다. 시간은 이 짧은 native 생성 workload의 관찰값이며 일반 Python workload의 개선율로 확대하지 않는다. list(view)처럼 materialize하는 작업은 여전히 복사 비용을 갖는다. [원시 probe와 DLL hash](round12-probe.json).

## 다음 후보

먼저 view iterator의 snapshot 제거와 값 변경·size mutation·exhaustion 규칙을 actual CPython으로 검증한다. same-size delete/reinsert는 별도 최소 사례가 필요하다. 이후 namespace 저장소를 통합하되 view가 같은 원본을 관찰하도록 연결한다. keys/items의 set 연산은 독립적인 호환성 항목으로 처리한다.
