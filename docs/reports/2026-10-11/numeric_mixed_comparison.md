# 89차: int/float 비교의 정밀도 보존

88차를 `3533981`로 커밋한 뒤 진행했다. 이번 변경은 아직 커밋하지 않았다.

## 문제와 범위

수치 dict 키의 해시 계약을 조사하면서 그 전제인 동등 비교부터 틀리는 경우를 확인했다. 기존 PyInt/PyFloat 비교는 정수를 double로 변환했다. `2**53 + 1 == float(2**53)`이 True가 되며, double 범위를 넘는 정수가 무한대와 같다고 판단될 수도 있다. 해시만 일치시키면 잘못된 동등 비교로 서로 다른 키를 합칠 위험이 있어 기본 수치 비교를 먼저 수정했다.

참조는 [CPython v3.12.15 float_richcompare](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/floatobject.c#L418)와 실제 conda CPython 3.12.15 실행이다. 해당 고정 commit의 소스를 로컬에 내려받아 확인했다. CPython은 비유한 float, 부호, 정수 비트 수와 소수부를 구분해 정보 손실 없는 비교를 수행한다.

이번 범위는 기본 int/float 양방향 비교다. int 하위 타입의 사용자 비교 dispatch 전체나 float hash()를 해결한 것으로 해석하지 않는다.

## 구현

`PyFloat.CompareToInteger`를 두 타입의 동등·순서 비교에서 공유한다. 부호 있는 비교값을 반환하고, 순서를 정할 수 없는 NaN은 NaN으로 유지한다. `!=`도 기존의 동등 비교 반전을 통해 반영된다.

- 53비트 이하 정수는 double 변환으로 정확하게 비교한다. BigInteger의 음수 GetBitLength가 경계에서 한 비트 작게 나오는 값은 음의 2의 거듭제곱이므로 이 경로에서도 정확하다.
- 비유한 float는 정수를 변환하지 않고 처리한다. NaN은 모든 순서 비교 및 동등 비교에서 False, 부등 비교에서 True다.
- 큰 정수와 Int64 범위 float의 비교는 `BigInteger.CompareTo(long)`을 사용한다. 이 경로에서 양쪽 정수부가 같을 수 있는 float는 이미 소수부가 없는 크기다.
- 유한 float보다 큰 1024비트 초과 정수는 부호로 판단한다.
- 큰 정수도 하위 0비트를 제외한 유효 비트가 53개 이하면 정확한 double 변환을 사용한다. 그 외에는 유한 float의 정수값을 BigInteger로 만들어 비교한다. Int64 밖의 double에는 소수부가 없으므로 이 변환에서 정보가 손실되지 않는다.

CPython의 임시 객체·shift·OR 절차를 그대로 복제하지 않고 .NET BigInteger 연산을 사용했다. 새로운 외부 라이브러리나 DLL 참조는 없다. 정확히 표현되지 않는 큰 정수와 Int64 밖의 float를 비교하는 마지막 경로에는 임시 BigInteger backing array 할당이 남는다.

## 회귀 검증

`tests/numeric_mixed_comparison_regression.py`를 기본 평가에 추가했다. 577개 수치 조합에 대해 양방향 6종 비교, 총 6,924개 결과를 assertion으로 확인하며 리스트 membership·동등·순서 비교도 검사한다.

53비트 정밀도 경계, Int64 경계, 일반 가수와 2의 거듭제곱의 인접 정수, 음수, signed zero, 최소 subnormal, 소수부, 최대 유한 double, 큰 유한 정수와 ±무한대·NaN을 포함한다. 수정 전 DLL로 최종 fixture의 실패를 재확인했고, 수정 후 출력은 실제 CPython과 일치한다.

- Release 빌드: 경고 0, 오류 0.
- 전체 155개: 완전 일치 150개, 지정 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개.
- Godot: SharpPy 소스 직접 포함 빌드 성공. 기본 17개와 관련 11개, 총 28개 fixture 통과. 관련 11개 출력은 새 CPython 실행과 대조했다.
- 최종 DLL SHA256과 전체 평가 기록의 SHA256 일치.
- Windows x64/.NET 8.0.31에서 검증했다. 비Windows·모바일 실기 검증은 포함하지 않는다.

## 성능

새 `numeric_comparison_workloads.py`의 세 작업은 수정 전후 모두 올바른 합계를 반환하며 하네스가 이를 검사한다. 작은 int/float 혼합 비교, 정확히 표현되는 큰 정수 `2**100`과 float 비교, int끼리 비교하는 대조군을 분리했다. 잘못된 기존 결과를 내던 경로를 속도 개선의 기준으로 삼지 않았다.

Release, TieredCompilation=0, 100,000회, 워밍업 10회, 표본 15회, 전후 3쌍 교대 실행. 빌드·기능 검사와 겹치지 않았다. 시간은 프로세스별 중앙값 세 개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 변화 | 전후 할당 B |
|---|---:|---:|---:|---:|
| mixed_compare_small | 140.3134 | 140.3286 | +0.01% | 9,104,032 |
| mixed_compare_large | 85.5397 | 86.1484 | +0.71% | 9,104,248 |
| integer_compare | 136.9714 | 137.1104 | +0.10% | 9,104,032 |

세 작업 모두 전후 프로세스 시간 범위가 겹치며 모든 할당 표본이 전후 동일하다. 속도 개선을 주장하지 않는다.

초기 구현은 큰 float마다 BigInteger를 만들어 mixed_compare_large에서 17,104,248 B를 할당했다. 정확한 큰 정수의 변환 경로를 추가해 10만 회당 8,000,000 B의 추가 할당을 제거하고 이전 구현의 9,104,248 B로 복귀했다. 초기 확인은 표본 5회로 수행했으며 최종 전후 비교와 구분해 보존했다. 이 수치는 이전 릴리스 대비 할당 감소가 아니다.

[실행 출력·환경·표본 및 미해결 재현](round89-validation.json)

## 후속 작업

1. `tests/compatibility/numeric_int_subclass_comparison.py`: 기본 동작을 상속한 int 하위 타입에서도 큰 값의 순서 비교가 Int64 축소로 OverflowException을 내며 float가 왼쪽인 동등 비교는 False를 반환한다. 비교의 전체 정수값 보존과 반대편 dispatch를 다음에 수정한다. 사용자 메서드 재정의와 NotImplemented 순서를 함께 검증한다.
2. `tests/compatibility/numeric_dict_hash.py`: `2**31`부터 선별한 큰 정수와 동등 float의 dict 조회가 양방향 missing이다. 기본 평가 통과 목록에 넣지 않았으며 이번 수정으로 해결됐다고 주장하지 않는다. 수치 해시 계약과 저장된 전체 폭 해시 구조를 계속 검토한다.
3. 기존 int 하위 타입 dict callback 누락, 큰 문자열 파싱, 85→86 일반 예외 생성 시간 증가의 원인도 미해결이다.
