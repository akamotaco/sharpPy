# 44차: range slice step 중복 변환 제거

43차 큰 정수 range 지원과 검증 기록은 `fef86bd`로 커밋했다. 이번 변경은 일반 slice 비용을 줄이는 후속 작업이다.

## 변경 근거와 구현

PyRange.SliceRange는 step이 int 범위인지 확인한 뒤 PySlice.Indices에서 같은 step을 다시 __index__ 프로토콜과 BigInteger 변환 경로로 읽었다. 일반 int/bool/None step은 한 번만 변환하고, 내부 Indices(length, step)에 전달한다. 범위 밖 정수와 사용자 callback step은 기존 ExactIndices로 처리한다. 객체 필드나 캐시, 외부 의존성은 추가하지 않았다.

CPython v3.12.15의 Objects/rangeobject.c compute_slice 및 Objects/sliceobject.c _PySlice_GetLongIndices를 기준으로 step→start→stop 평가 순서, 0 step의 선행 오류, 정규화한 인덱스로 결과 start/stop/step을 계산하는 동작을 유지한다. 로컬 참고 소스 커밋은 e848e4b09ca4bc67c0041c9ce7526dae471d4c36이다.

## 검증

- 신규 range_slice_step_regression.py: 1,800개 조합. 빈 range, 역방향, signed-long 경계, 큰 정수, None/bool/int step 및 int 경계 안팎, 큰 경계값을 포함한다. 결과 속성과 선택된 원소를 검증하고 checksum을 실제 CPython과 대조했다. 0 step과 실패하는 __index__의 호출 순서도 검사한다.
- 실제 CPython 3.12.15와 전체 90개 평가: 85개 완전 일치, 기존 변동값 제외 일치 3개, C# 전용 검사 2개 실패 보고 없음.
- 일반 Release 빌드 통과. tokenizer의 기존 nullable 경고 1개, 오류 0개. 성능 harness 빌드는 기존 nullable 경고 2개, 오류 0개.
- Godot 4.7.2/.NET 8.0.31 소스 직접 포함 빌드 경고/오류 0개. 기존 17개 fixture 및 C# helper 검사, 신규 fixture 별도 실행 모두 통과했다. 별도 SharpPy DLL 참조를 추가하지 않았다.

## 성능

TieredCompilation=0, 작업당 10,000회, 프로세스별 warmup 20회와 측정 10회. 전후 프로세스는 순차 실행했다. 숫자는 측정값을 합친 중앙값(ms)이며 애플리케이션 전체 속도를 뜻하지 않는다.

| standalone .NET, 5쌍 | fef86bd | 변경 후 |
|---|---:|---:|
| 직접 생성 | 0.14900 | 0.14570 |
| builtin 생성 | 0.22770 | 0.22670 |
| slice | 0.13115 | 0.11450 |
| index | 0.08480 | 0.08550 |

slice는 약 12.7% 단축됐다. 생성/builtin/slice 할당은 작업당 48B, index는 40B로 동일하다. 다른 작업은 이번 수정 대상이 아니며 성능 개선으로 해석하지 않는다.

큰 정수 지원 전 8365517과 Godot 소스 빌드로도 3쌍 비교했다. 양쪽 Optimize=true, GODOT 심볼, DEBUG 제외 조건이다.

| Godot 소스 빌드, 3쌍 | 8365517 | 변경 후 |
|---|---:|---:|
| 직접 생성 | 0.01985 | 0.01930 |
| builtin 생성 | 0.17080 | 0.21910 |
| slice | 0.11545 | 0.11280 |
| index | 0.08100 | 0.08140 |

일반 slice의 이전 지연은 이 조건에서 해소됐다. **builtin 생성은 큰 정수 지원 전보다 약 28% 느리며 아직 해결하지 않았다.** 서로 다른 호스트의 절대 시간을 직접 비교하지 않는다.

원시 측정과 검증 요약은 round44-validation.json에 보존했다. 상세 전체 평가 로그는 `.artifacts/round44-tests.json`, Godot 로그는 `.artifacts/round44-godot-{build,run,step}.log`다.

## 다음 과제

1. builtin range 생성의 프로토콜 변환·정수 범위 확인 비용을 분석한다. CPython의 인수 평가 순서와 예외 결과를 유지해야 한다.
2. 43차 Start/Stop/Step의 BigInteger 공개 타입 변경과 외부 C# 소비자 호환을 검토한다. 이번 변경은 공개 API를 추가로 변경하지 않았다.
3. 기존 range equality/ordering/hash 및 interop/legacy AST 호출부 검증은 별도 과제다. 이번 회귀 통과가 range 전체 호환성을 보장하지 않는다.
