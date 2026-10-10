# 45차: range 생성 비용 조사와 회귀 보강

44차 slice 최적화는 `688d46e`로 커밋했다. 이번에는 생성 경로의 후보를 측정했지만 유의미하고 일관된 개선을 확인하지 못해 **런타임 구현 변경은 남기지 않았다.** 일반 builtin 생성의 43차 비용 증가는 미해결이다.

## 남긴 변경

- RangeConstructionProbe에 인수 1개/2개, bool 인수, 큰 정수 인수 측정을 추가했다. 기존 3개 인수 측정만으로 일반화하지 않는다. 인수 배열과 정수 객체는 측정 전에 준비한다.
- range_bigint_regression에 __index__가 잘못된 타입을 반환했을 때 첫 실패 이후 인수를 변환하지 않는 검사, 1개 인수 callback, bool 인수와 0 step 예외 검사를 추가했다.

## 분석

CPython v3.12.15 Objects/rangeobject.c의 range_from_array는 start→stop→step 순서로 PyNumber_Index를 적용한다. 같은 순서를 유지하며 다음 후보를 실험했다.

1. Index helper가 BigInteger 대신 PyInt 참조를 반환: 일반 생성 시간 변화가 거의 없었다.
2. FitsLong의 GetBitLength를 long 최소/최대 CompareTo 검사로 교체: 생성과 index가 느려져 제외했다.
3. CallRange의 강제 인라이닝 제거: 일반 생성 시간 변화가 거의 없었다.
4. int 경로와 프로토콜/오류 경로 분리: 초기 3개 인수만 측정했을 때 약 4% 차이가 보였으나 bool 경로가 느려졌다. bool 직접 처리를 추가하고 조건을 넓힌 최종 측정에서는 일반 생성 차이가 약 1%에 그쳤다. 복잡도를 늘릴 근거가 부족하여 제외했다.

최종 후보 4의 보정본과 기준 688d46e를 같은 확장 harness로 비교했다. .NET 8.0.31 Release, TieredCompilation=0, 순차 전후 5쌍, 프로세스마다 20회 warmup 및 10회 측정, 작업당 10,000회. 아래는 ms 중앙값이며 **채택한 최적화 결과가 아니다.**

| 작업 | 기준 | 제외한 후보 |
|---|---:|---:|
| builtin 3인수 | 0.22390 | 0.22125 |
| builtin 1인수 | 0.20650 | 0.20845 |
| builtin 2인수 | 0.21580 | 0.21665 |
| builtin bool | 0.26060 | 0.25760 |
| builtin 큰 정수 | 0.93655 | 0.93085 |

할당은 모든 조건에서 동일했다. 일반 range 생성은 48B, 측정한 큰 정수 range 생성은 112B다. 원시 측정은 round45-validation.json에 후보 상태임을 명시해 보존했다. 향후에는 JIT 생성 코드와 더 긴 측정 구간을 통해 범위 확인 및 정수 변환 비용을 분리할 필요가 있다.

## 최종 상태 검증

실험한 런타임 변경을 모두 되돌린 후 일반 Release 빌드를 수행했다(경고/오류 0개). 실제 CPython 3.12.15와 수정한 range_bigint_regression 및 range_slice_step_regression을 대조해 둘 다 출력이 일치했다. Godot 소스 포함 프로젝트도 다시 빌드하고 수정한 큰 정수 회귀를 실행해 통과했다.

런타임 소스는 688d46e와 동일하다. 44차 전체 90개 검증을 이번에 다시 실행한 것으로 표기하지 않는다. 상세 기록은 `.artifacts/round45-tests.json`, `.artifacts/round45-godot-{build,run}.log`다.

다음 과제는 builtin 생성 비용의 JIT 분석, 43차 공개 Start/Stop/Step 타입 변경의 C# 소비자 호환성, 기존 range equality/ordering/hash 차이다. 결과 일치성을 희생하는 최적화는 적용하지 않는다.
