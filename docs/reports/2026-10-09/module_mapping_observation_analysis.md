# 18차 module mapping 길이·진리값·복사

17차는 `fb5afe5`로 커밋했다. 이번 변경은 아직 커밋하지 않았다.

## 변경 및 검증

PyGlobalsDict의 Length는 원본 string namespace의 Count와 wrapper의 non-string key 계수를 합한다. IsTrue/PyBoolValue/AsBool도 이 길이를 사용한다. non-string 계수는 SetItem/SetDefault/DelItem/Pop/PopItem/Clear 경로에서 유지한다. 원본 namespace를 매 조회마다 순회하거나 복사하지 않아 길이는 O(1)이다.

Copy는 원본 namespace에서 현재 string binding을 읽고 non-string snapshot 항목을 함께 복사한다. 기본 PyDict.Copy를 virtual로 바꿔 명시적 dict.copy 및 기존 AsDict 경로도 adapter의 copy를 사용할 수 있도록 했다. 반환값은 원본과 독립적인 얕은 복사다. copy 자체의 O(n) materialization은 필요하지만 다른 조회를 위해 namespace snapshot 전체를 refresh하지 않는다.

[새 회귀](../../../tests/module_mapping_observation_regression.py)는 관찰 이후 global 생성·변경·삭제의 길이/복사, 독립적인 얕은 복사, explicit dict.copy, non-string 삽입/중복/삭제/기본값/popitem, bool 및 int의 동등 키를 actual CPython 3.12.15와 비교한다. 기존 clear 회귀에는 non-string 항목, 비어 있는 진리값 및 copy를 추가했다. 기준 binary는 새 회귀가 실패했고 최종은 통과했다.

Release 본체/harness 빌드 성공. 전체 53개: 출력 일치 47, 기존 출력 차이 4, native no_reported_failure 2. [전체 결과](round18-tests.json), [기준 실패](round18-before.json), [최종 focused](round18-focused.json).

## 남은 차이

원본은 CLR Dictionary<string, PyObject>라 삭제/재삽입의 열거 순서를 Python 삽입 순서로 취급할 수 없다. [copy order fixture](../../../tests/compatibility/module_mapping_copy_order.py)가 이 차이를 재현한다. non-string 항목은 copy의 뒤에 추가하므로 string/non-string 혼합 삽입 순서도 완전하게 보존하지 않는다.

view의 길이는 개선된 Length를 사용하지만 실제 iteration/list/repr 등은 아직 기존 snapshot 경로다. retained module view와 string-key popitem도 여전히 실패한다. raw InternalDict 사용처, dict(g) 등 모든 materialization 경로를 통합했다고 주장하지 않는다. 사용자 정의 key hash의 예외·재진입 및 우회된 내부 mutation은 non-string 계수 검증 범위에 포함하지 않았다.

## 비용 평가

기준 commit을 git archive로 추출해 Release 빌드했다. 동일 harness로 전후 process 3쌍을 교차 실행하고 process별 10회 warmup/10개 sample을 수집했다. [native 자료 및 DLL hash](round18-probe.json), [15개 공통 Python VM workload](round18-workloads.json).

| Native: namespace 크기 100 | 전 중앙값 ms | 후 중앙값 ms | 전 할당 B | 후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| Length 10,000회 | 0.02035 | 0.02175 | 0 | 0 |
| Copy 1,000회 | 8.26820 | 5.89405 | 12,432,000 | 15,632,000 |
| Wrapper 생성 1,000회 | 3.49210 | 10.33780 | 15,640,000 | 15,648,000 |

wrapper의 계수 필드는 생성당 8B를 추가한다. Copy는 원본 string key를 PyString으로 다시 생성해 이 probe에서 복사당 3,200B가 추가됐다. Copy 시간 중앙값은 줄었지만 할당 감소로 평가하지 않는다. Length의 시간은 매우 짧은 native probe이며 개선을 주장하지 않는다.

초기 combined probe에서 wrapper 생성 시간 증가가 커 단독 construct 모드로 다시 측정했다. 전 7.81810ms(IQR 7.5408–8.0433), 후 7.53095ms(IQR 7.2927–7.7193)로 범위가 겹쳤다([반복 자료](round18-construct-repeat.json)). warmup 이력이 달라 원래 결과를 대체하거나 최초 증가를 반증하지 않는다. 정확한 원인은 미규명이다.

일반 VM namespace_update는 20.29085→20.16230ms, namespace_read는 9.10485→9.09965ms로 IQR이 겹쳤다. 두 workload의 관리 할당은 wrapper 필드로 8B 증가했다. 이 변경은 최신 상태를 관찰하는 호환성 보완이며 광범위한 성능 향상을 주장하지 않는다.

## 다음 후보

원본 string key와 PyString key identity/순서를 함께 보관하는 저장소 연결이 필요하다. 이후 Copy의 반복 key 생성과 view iteration의 불일치를 함께 줄여야 한다. 원본 Dictionary 열거 순서를 그대로 사용해 popitem을 완료 처리하지 않는다. mixed-key 순서와 raw storage 읽기도 함께 검증한다.

```powershell
& $taskDotnetExe $harness --globals-observation-probe
& $taskDotnetExe $harness --globals-observation-probe construct
```
