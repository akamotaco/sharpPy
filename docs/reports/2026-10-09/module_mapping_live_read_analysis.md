# 17차 module mapping의 원본 조회

16차를 `d536978`로 커밋했다. 이번 변경은 아직 커밋하지 않았다.

## 변경과 발견

PyGlobalsDict의 string key GetItem/Get/Contains/ContainsKey는 snapshot 대신 원본 string namespace를 조회한다. 조회를 위해 namespace 전체를 복사하거나 snapshot에 새 항목을 추가하지 않는다. non-string key는 기존 PyDict 저장 경로를 유지한다. Get과 ContainsKey는 내부 adapter에서 재정의할 수 있도록 virtual로 바꿨다.

초기 구현에서 C# GetItem override만 변경했을 때 실제 Python subscript 회귀가 실패했다. VM의 BINARY_SUBSCR는 builtin dict.__getitem__ descriptor를 호출하고 descriptor는 _dict를 직접 읽었다. 이를 위해 내부 TryGetStoredValue primitive를 추가했다. builtin descriptor는 Python __getitem__ override를 호출하지 않으면서 내부 adapter의 원본 저장소를 읽을 수 있다. dict subclass의 기존 storage/__missing__ 경로는 유지했다.

dict_items membership도 이 primitive를 사용한다. 이는 module의 현재 값에 대한 membership 조회를 보완하지만 view의 전체 iteration 저장소를 통합한 것은 아니다.

## CPython 및 회귀

고정 CPython 3.12.15 로컬 Python/bltinmodule.c의 builtin_globals_impl과 양방향 반영 주석을 다시 확인했다([공식 고정 소스](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/bltinmodule.c)). actual CPython 3.12.15와 새 [live read 회귀](../../../tests/module_mapping_live_reads_regression.py)를 비교했다. 기존 값 교체, 관찰 이후 생성, 삭제 후 KeyError/default/membership, explicit dict.get, wrapper 쓰기와 non-string 기존 경로 및 globals/locals identity를 검증한다.

기준은 새 회귀와 기존 module_locals_live가 실패했고 최종은 통과한다. module mutation/clear 및 일반 dict subclass 회귀도 유지했다. Release 본체/harness 빌드 성공. 전체 52개: 출력 일치 46, 기존 출력 차이 4, native no_reported_failure 2. [기준 결과](round17-before.json), [전체 결과](round17-tests.json), [focused 결과](round17-focused.json).

## 남은 차이

Length/Copy/Repr/view iteration 등은 아직 snapshot을 사용한다. 원본을 읽는 일부 연산과 snapshot을 읽는 연산이 혼재하므로 namespace 전체가 coherent live dict라고 주장하지 않는다. [retained module view fixture](../../../tests/compatibility/module_mapping_retained_views.py)는 membership뿐 아니라 실제 list(keys)도 확인해 이 차이를 보존한다. PopItem 동기화 및 CPython resize mutation도 기존 미해결 항목이다.

다른 raw InternalDict 사용처를 전부 변경한 것은 아니다. values/items iterator와 일부 merge/eval 관찰 경로는 저장소 통합 시 함께 확인해야 한다. 사용자 정의 hash/str subclass 및 재진입은 이번 검증 범위에 포함하지 않았다. missing-key 메시지의 기존 표현 차이도 해결한 작업이 아니다.

## 성능

기준 commit을 git archive로 추출해 별도 Release 빌드했다. 동일 harness로 전후 process 3쌍을 교차 실행했고 각 process에서 warmup 10회와 sample 10개를 수집했다. builds는 측정과 분리했다. [native raw/IQR 및 DLL hash](round17-probe.json), [15개 VM workload와 CPython 결과](round17-workloads.json).

| 작업 | 전 중앙값 ms | 후 중앙값 ms | 할당 B, 전후 동일 |
| --- | ---: | ---: | ---: |
| Native: GetItem/Get/Contains 각각 10,000회 | 0.65340 | 0.26665 | 0 |
| Python VM: namespace_read(n=10,000) | 8.52415 | 8.44130 | 1,924,968 |

native probe는 초기 namespace 크기 100에서 이미 존재하는 string key를 조회하고 반환값을 검증한다. constructor 및 missing-key 예외 비용은 제외한다. native 중앙값은 약 59.2% 감소했지만 Python VM workload는 IQR이 겹쳐 안정적인 실행 속도 개선을 확정하지 않는다.

namespace_read는 wrapper로 0 값을 저장한 뒤 반복 subscript 조회와 i의 합계를 계산한다. 기준과 최종 모두 49,995,000의 정답을 검증했고 actual CPython에도 같은 workload를 추가했다. live 변경을 만드는 회귀와, 기준에서도 정답을 계산할 수 있는 성능 workload를 구분했다.

일반 dictionary 중앙값은 12.40890→12.80945ms, dictionary_iteration은 12.11550→12.49335ms였다. IQR은 겹치지만 내부 storage read dispatch 추가가 일반 dict 경로에 미치는 비용은 후속 관찰 대상이다. 고정 할당 workload의 할당은 동일했고 locals_observation의 일부 변동은 raw를 보존했다. 광범위한 성능 무회귀나 다른 workload의 개선을 단정하지 않는다.

## 다음 후보

string namespace와 PyObject dict 저장소의 Length/Copy/iterator/view 일관성을 함께 설계해야 한다. 조회 시마다 snapshot을 전체 refresh하는 방식으로 해결하지 않는다. 기존 원본 조회 primitive를 기반으로 정확한 저장소 abstraction 범위와 일반 dict dispatch 비용을 평가한다.
