# 16차 module mapping mutation 동기화

15차를 `4d16131`로 커밋했다. 이번 변경은 아직 커밋하지 않았다.

## 변경 범위

PyGlobalsDict는 module의 string-key namespace를 참조하면서 Python dict용 snapshot을 별도로 보관한다. 이번에는 전체 저장소 통합 대신 명확하게 재현된 mutation 경로를 수정했다.

- string key의 DelItem/Pop은 원본 namespace를 기준으로 삭제한다. Pop은 원본의 최신 값을 반환하고 snapshot의 해당 항목도 제거한다. 관찰 후에 생성된 binding도 삭제할 수 있다.
- Clear는 원본 namespace와 snapshot을 모두 비운다.
- SetDefault는 원본에서 기존 값을 찾고 해당 snapshot 항목을 갱신하거나, 새 값을 원본과 snapshot에 함께 추가한다. 이를 위해 기본 PyDict.SetDefault를 virtual로 변경했다.
- Update는 입력 항목만 SetItem 경로로 반영한다. 기존 방식처럼 snapshot 전체를 원본에 replay하지 않아 무관한 최신 binding을 덮어쓰지 않는다. self 및 동일 원본을 공유하는 wrapper의 update는 replay를 생략한다.
- PyGlobalsDict의 `|=`는 같은 update 경로를 사용한다.

dict 입력은 compact key list의 struct enumerator를 사용한다. yield 기반 GetInternalDict를 매 update마다 호출해 enumerator 객체를 할당하지 않으며 입력의 삽입 순서를 유지한다. mappingproxy 입력도 기존 허용 범위에서 처리한다. 일반 PyDict.Update 구현은 유지했다.

## CPython 및 회귀

고정 CPython 3.12.15의 로컬 `Python/bltinmodule.c`에서 builtin_globals_impl이 PyEval_GetGlobals 결과를 반환하고, 문서 주석이 name lookup과 dictionary 변경의 양방향 반영을 명시하는 것을 확인했다. 출처는 기존 [manifest](round5-cpython-manifest.json)의 고정 commit이다([공식 소스](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/bltinmodule.c)).

[mutation 회귀](../../../tests/module_mapping_mutation_regression.py)는 최신 기존 값의 setdefault/pop, 새 binding 생성·삭제, missing/default/KeyError, unrelated 최신 값 보존, self-update, explicit dict.update, mappingproxy 입력, `|=` 및 기존 non-string snapshot 경로를 actual CPython 3.12.15와 비교한다. [clear 회귀](../../../tests/module_mapping_clear_regression.py)는 함수에서 module namespace를 비워 함수명 등 실제 binding도 제거하는 것을 검증한다. 기존 [module_mapping_pop](../../../tests/compatibility/module_mapping_pop.py)도 이제 통과한다.

기준 binary에서는 세 fixture가 실패했다. 최종 Release 본체/harness 빌드 성공. 전체 51개 결과: 출력 일치 45, 기존 출력 차이 4, native no_reported_failure 2. [수정 전](round16-before.json), [전체 결과](round16-tests.json), [추가된 descriptor/proxy 및 미해결 사례 포함 focused 결과](round16-focused.json).

## 미해결 항목

일반 GetItem/Get/Contains/Length/Copy/view/iteration의 snapshot은 여전히 namespace의 이후 쓰기를 모두 관찰하지 못한다. module_locals_live가 실패한다. PopItem도 원본을 삭제하지 않는 기존 차이가 남아 [별도 fixture](../../../tests/compatibility/module_mapping_popitem.py)로 보존했다. CLR Dictionary의 삭제/재삽입 이후 순서와 Python dict 순서는 같다고 가정하지 않는다.

따라서 module namespace 전체가 live dict가 됐다는 주장은 하지 않는다. 원본 string 저장소와 non-string Python keys의 통합도 미완료다. 지원하지 않던 일반 mapping/iterable update 입력, kwargs 처리 및 사용자 정의 key hash/재진입은 이번 검증 범위에 포함하지 않았다. 일반 dict subclass override semantics를 전반적으로 변경한 작업도 아니다.

## 성능

기준 commit을 git archive로 추출해 별도 Release 빌드하고, 동일 최종 harness를 기준/최종 DLL과 실행했다. 전후 process 3쌍을 교차 실행했으며 각 process에서 10회 warmup 및 10개 sample을 수집했다. 빌드는 측정과 분리했다. raw 및 DLL hash는 [native probe](round16-probe.json), [14개 공통 workload/CPython](round16-workloads.json)에 보존했다.

| 작업 | 전 중앙값 ms | 후 중앙값 ms | 할당 B, 전후 동일 |
| --- | ---: | ---: | ---: |
| Native: 100개 기존 namespace에서 1개 key update 10,000회 | 18.46030 | 1.07135 | 0 |
| Python VM: namespace_update(n=10,000) | 22.94835 | 19.23360 | 6,164,952 |

native probe는 기존 모든 namespace 항목을 다시 쓰는 비용을 제거한 측정이며 Python VM은 포함하지 않는다. Python workload는 매번 새 1-key dict를 만들고 globals().update한 뒤 실제 global binding을 조회해 합계를 계산한다. C# 및 actual CPython 모두 49,995,000의 정답을 검증했다.

namespace_update의 중앙값은 약 16.2% 감소했고 IQR은 전 22.7659–23.5771ms, 후 18.1931–19.3862ms로 겹치지 않았다. 이 workload에 한정된 결과다. 일반 Python workload 전체의 동일한 개선율을 주장하지 않는다. 다른 고정 할당 workload는 전후 동일했으며 locals_observation의 일부 변동은 raw 그대로 보존했다. 다른 workload의 시간 차이는 이 update 변경의 효과라고 단정하지 않는다.

Update의 대상 namespace 전체 replay를 제거해 대상 크기에 비례하던 비용을 입력 key 처리 비용으로 줄였다. constructor의 초기 snapshot 생성이나 view 관찰 비용을 해결한 것은 아니다.

## 다음 후보

module namespace의 string 저장소와 PyDict storage/view를 일관되게 연결하는 구조를 검토한다. 그 전에는 PopItem만 CLR Dictionary의 마지막 열거 항목으로 구현해 Python의 순서를 가정하지 않는다. retained mapping의 live 조회와 양방향 mutation/identity/view를 함께 비교해야 한다.
