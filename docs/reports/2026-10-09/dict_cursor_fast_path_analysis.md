# 15차 변경 없는 dict cursor 경로 분리

14차를 `9b58b77`로 커밋했다. 이번 변경은 아직 커밋하지 않았다.

## 변경

OrderedIterationState.TryNext에서 삭제 이력이 없는 compact key list 경로를 분리했다. 일반 경로는 유효 인덱스에서 키 하나를 읽으며 tombstone 순회와 null 검사를 하지 않는다. 삭제 이력이 있는 경우에는 기존 tombstone 순회 helper를 사용한다. hot helper에는 AggressiveInlining 힌트를 추가했다. 인덱스 경계 검사, remaining 검사, size 오류, clear/popitem 및 이력 상한 정책은 유지한다.

JIT 출력은 별도 실행으로 `.artifacts/round15-jit-*`에 보존했다. 수집된 PyDictIterator.Next의 Tier0 코드에는 전후 모두 TryNext 호출이 남아 있다. 따라서 힌트를 추가했다는 이유만으로 실제 모든 실행 경로에서 인라이닝됐다고 주장하지 않는다. caller의 Tier1-OSR 출력도 수집했으나 profiler 실행 결과를 최종 시간 표본과 섞지 않았다.

## 회귀 및 CPython 비교

Release 본체/harness 빌드 성공. 전체 49개 결과는 출력 일치 43, 기존 출력 차이 4, native no_reported_failure 2로 유지됐다. dict mutation의 검증된 사례와 live iterator 값 조회는 actual CPython 3.12.15와 일치한다. resize compaction, view set 연산 및 module live 조회/삭제는 여전히 별도 실패 fixture다. [전체 결과](round15-tests.json), [focused 결과](round15-focused.json).

## Native 측정

동일한 harness를 기준/최종 DLL에 사용했다. 측정별 전후 process 3쌍, 각 process warmup 10회와 sample 10개를 사용했다. builds와 JIT 출력 수집은 시간 측정에서 분리했다. 아래는 총 30 sample 중앙값이며 [raw/IQR 및 DLL hash](round15-probe.json)를 보존했다.

| 같은 dict에서 iterator 생성+100 Next, 1,000회 | 전 ms | 후 ms | 할당 B, 전후 동일 |
| --- | ---: | ---: | ---: |
| keys | 2.15315 | 1.92925 | 64,000 |
| values | 4.60390 | 4.26690 | 64,000 |
| items | 6.31285 | 6.05060 | 7,264,000 |

keys 중앙값은 약 10.4% 감소했고 IQR은 전 2.1315–2.2385ms, 후 1.9189–1.9441ms로 겹치지 않았다. 이 native workload에 한정된 개선이다. values/items는 IQR이 겹쳐 안정적인 개선율로 확정하지 않는다.

새 `--fresh` 모드는 측정 직전에 100개 키의 fresh dict 1,000개를 준비하고 각 dict의 첫 iterator 생성과 첫 Next를 측정한다. dict 준비는 할당/시간 범위에서 제외한다. JIT/타입 초기화의 cold start가 아니라 dictionary별 최초 cursor 상태 생성 비용이다.

| Fresh dict별 첫 iterator+첫 Next, 1,000회 | 전 ms | 후 ms | 할당 B, 전후 동일 |
| --- | ---: | ---: | ---: |
| keys | 0.09010 | 0.07700 | 120,000 |
| values | 0.10590 | 0.09685 | 120,000 |
| items | 0.11675 | 0.10655 | 192,000 |

첫 iterator의 관리 할당은 keys/values 120B, items 192B로 그대로다. 반복 생성의 keys/values 64B와 비교하면 최초 상태/weak reference 비용이 56B 추가되는 것이 관찰된다. weak handle의 전체 native 메모리를 측정한 것은 아니다. 첫 사용의 할당 최적화는 아직 미완료다. 14차에서 늘어난 dict당 8B도 유지한다.

## Python workload 측정과 한계

공통 workload에 dictionary_iteration을 추가했다. dict에 0..n-1을 저장하고 `for key in values`로 조회/합계를 계산한다. C# 및 CPython benchmark 모두 13개 workload를 사용하고 n=10,000의 정답 49,995,000을 검증했다. 기존 12개도 그대로 측정했다. [raw/IQR 및 CPython 결과](round15-workloads.json).

dictionary_iteration은 전 12.49005ms, 후 12.41050ms이며 IQR이 크게 겹친다. 할당은 3,526,584B로 같다. native 개선을 실제 Python 전체 실행의 개선율로 확대하지 않는다. CPython 측정은 reference 결과 검증과 자체 steady-state timing이며 native probe와 같은 측정 범위가 아니다.

class_locals_observation은 초기 13-workload 실행에서 10.78865→11.22725ms로 약 4.1% 증가했고 IQR도 좁게 분리됐다. 이를 숨기지 않고 별도로 반복 확인했다. harness의 네 번째 위치 인자로 workload 하나를 선택하는 기능을 추가했다. 단독 class 측정은 전 29.13770ms, 후 29.53415ms이며 IQR이 겹쳤다([반복 자료](round15-class-repeat.json)). 단독 실행은 이전 workload에 의한 warmup 이력이 달라 원래 측정을 대체하거나 최초 증가를 반증하지 않는다. 절대 시간 차이의 정확한 원인은 미규명이며 광범위한 성능 무회귀를 확정하지 않는다.

일부 inplace_addition/locals_observation sample의 할당값이 달라 raw를 그대로 보존했다. 이를 이번 helper 분리의 효과로 설명하지 않는다.

## 다음 항목

첫 iterator 상태 비용과 mutation 이력 복사 비용은 아직 남는다. class workload의 실행 순서·warmup 의존성도 재현 조건을 고정해 확인해야 한다. namespace 저장소 통합 및 CPython resize 정책 차이는 별도 구조 후보로 유지한다.

```powershell
& $taskDotnetExe $harness --dict-iterator-probe --traverse
& $taskDotnetExe $harness --dict-iterator-probe --fresh
& $taskDotnetExe $harness tools/PerformanceHarness/workloads.py 10000 10 class_locals_observation
```
