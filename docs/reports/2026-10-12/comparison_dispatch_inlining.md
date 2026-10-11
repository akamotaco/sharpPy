# 92차: 비교 캐시와 숫자 계층 판정의 반복 비용

91차를 `c28d75f`로 로컬 커밋했다. 이번 변경은 미커밋 상태이며 원격에는 푸시하지 않았다.

## 원인과 변경

91차에서 기본 int 하위 타입의 비교가 느려졌다. MRO 조회 결과를 캐시해도 PyIntSubclass.RichCompare에서 TryCompareSpecial을 호출하며, 이 함수에 descriptor 바인딩·Python 함수 호출·예외 처리까지 포함되어 있었다. 캐시에서 메서드가 없다고 판정하는 반복 경로에도 함수 호출 비용이 남았다.

운영 코드 변경은 core/PyObject.cs와 type/PyClass.cs 두 파일이다. TryCompareSpecial의 실제 호출 처리를 CallComparisonMethod로 옮기고, LookupComparisonMethod의 캐시 조회와 갱신을 분리했다. 짧은 조회 경로에는 AggressiveInlining을 지정했다. 캐시가 무효일 때 필요한 상태는 갱신 함수 안에서 준비한다.

인라인만 적용한 중간 측정은 90차 성능을 회복하지 못했다. 이어서 왼쪽 CLR 타입이 정확히 PyIntSubclass이고 오른쪽이 사용자 지정 타입이 없는 기본 int/float/bool인 경우의 계층 탐색을 생략했다. 기본 타입이 사용자 정의 int 하위 타입의 엄격한 하위 타입일 수 없으므로 이 조합에서는 오른쪽 우선 호출이 발생하지 않는다. 실제 비교 슬롯 호출과 NotImplemented 이후 반대편 호출은 기존 공통 경로를 그대로 사용한다. 오른쪽 사용자 하위 타입·그 외 객체·CLR 파생 타입은 일반 경로로 남는다.

CPython v3.12.15의 do_richcompare·slot_tp_richcompare를 기준으로 맞춘 91차 호출 순서는 유지한다. 클래스별 조회 실패 비트, 전역 무효화 세대, descriptor의 매번 바인딩, 사용자 함수의 직접 self 전달과 예외 처리 범위도 동일하다. 새로운 Python 동작이나 저장 필드를 추가하지 않는다. CPython의 C 슬롯을 복제하는 변경이 아니라 .NET JIT가 반복 경로를 짧게 만들도록 함수를 나눈 변경이다.

## 구조 검증

같은 PerformanceHarness로 전후 DLL을 교체하고 DOTNET_TieredCompilation=0, DOTNET_JitDisasm=SharpPy.PyIntSubclass:RichCompare를 사용했다. 이전 코드에는 TryCompareSpecial 호출이 있고, 이후 코드에서는 조회가 인라인되어 유효한 캐시의 비트 확인 뒤 바로 숫자 비교로 진행한다. 무효 캐시 및 실제 메서드 호출만 별도 함수로 진행한다.

인라인의 비용도 있다. 해당 RichCompare 함수의 기계어 크기는 113→445 B로 늘었다. 이는 그 함수만의 크기이며 호출되는 함수들을 포함한 전체 코드 크기 비교는 아니다. 인라인 여부만으로 속도 향상을 단정하지 않고 실행 시간을 대조했다.

기존 native cache probe에서 새 클래스 1,000개를 생성했다. 전후 모두 클래스 생성 1,032,000 B, 최초 비교 40,000 B, 반복 비교 0 B였다. 클래스당 8 B 및 최초 조회 실패 캐시 40 B라는 91차 비용은 그대로 남는다.

## 기능 검증

- 전체 161개: 완전 출력 일치 156개, 지정 변동 필드 제외 일치 3개, native 실패 보고 없음 2개.
- 새 integer_comparison_fastpath_regression.py는 6종 연산에 대해 int/float/bool 양방향 비교, 부모 메서드 추가·교체·삭제, 임의 반환값·NotImplemented·예외를 확인한다. 91차 DLL과 수정 후 DLL 모두 CPython과 일치하는 보존 회귀다.
- 기존 비교 회귀로 부모 메서드 변경·삭제, 실행 중 변경, descriptor·staticmethod·classmethod, 하위 타입 우선순위와 임의 반환값·예외 전파를 CPython 3.12.15와 대조했다.
- Godot는 SharpPy 소스를 직접 포함해 빌드하고 기본 17개 및 관련 17개, 총 34개를 실행했다. 관련 17개는 실제 CPython 출력과 일치한다.
- 최종 소스의 전체 평가 DLL과 성능 DLL의 SHA256 일치를 확인했다.

Windows x64, .NET 8.0.31, CPython 3.12.15 환경이다. 최종 Release와 Godot 빌드는 경고·오류 0개다. PerformanceHarness는 기존 ByteArraySliceMutationProbe 관련 nullable 경고 2개, 오류 0개다. 새 라이브러리 의존성은 없다.

## 성능 측정

Release, TieredCompilation=0, n=100,000, 워밍업 10회, 표본 15회, 독립 프로세스 3회씩 교대 실행했다. 빌드와 기능 검사를 종료한 뒤 측정했다. 프로세스별 시간 중앙값의 중앙값을 사용한다. 같은 현재 workload와 하네스를 사용하고 SharpPy DLL만 교체했다. checksum은 모든 실행에서 확인한다.

int 하위 타입 두 workload는 퇴행 전 90차 `1e4cd65`도 같은 측정에 포함한다. 이 workload는 사용자 비교 재정의가 없는 경우이므로 90차도 올바른 결과를 낸다. 불완전한 사용자 메서드 처리를 생략해서 얻은 속도를 기준으로 삼는 비교가 아니다.

| workload | 91차 ms | 수정 후 ms | 변화 | 90차 ms | 90차 대비 |
| --- | ---: | ---: | ---: | ---: | ---: |
| int_subclass_compare | 157.824 | 149.642 | -5.18% | 148.719 | +0.62% |
| int_subclass_float_compare | 158.548 | 153.352 | -3.28% | 152.057 | +0.85% |
| mixed_compare_small | 150.152 | 152.508 | +1.57% | — | — |
| integer_compare | 147.710 | 148.770 | +0.72% | — | — |
| user_compare | 93.676 | 93.976 | +0.32% | — | — |
| dictionary | 106.124 | 105.346 | -0.73% | — | — |

대상 두 workload는 91차와 수정 후의 프로세스 중앙값 범위가 겹치지 않았다. 90차 대비 손실은 상당 부분 줄었지만 완전 해소로 단정하지 않는다. int/float 조합은 수정 후 153.302–154.349 ms, 90차 147.227–152.206 ms로 여전히 범위가 분리되어 있다. 일반 정수·사용자 비교·dictionary 대조군의 시간 범위는 겹친다.

mixed_compare_small의 +1.57%는 범위가 분리되어 있어 코드 변경 없이 실행 순서를 반대로 시작해 추가 3쌍을 측정했다. 재측정은 151.043→149.514 ms(-1.01%)로 방향이 바뀌었다. VM CompareOperation의 JIT 출력은 양쪽 모두 499 B이며 네 주소 상수를 제외한 명령열이 같았다. 이 증거만으로 변동 원인을 확정하지 않으며 일반 숫자 경로의 속도 개선이나 지속적인 퇴행을 주장하지 않는다. 원래 표본을 대체하거나 제외하지 않았다.

할당은 숫자·사용자 비교 다섯 workload에서 전후 완전히 동일했다. dictionary 범위는 이전 30,854,592–30,866,552 B, 이후 30,854,592–30,864,992 B로 겹친다. 이번 단계에서 새로운 할당 감소는 없으며 91차의 사용자 비교 호출당 72 B 절감은 유지된다.

최종 수치와 원시 표본은 [검증 기록](round92-validation.json)에 보존한다. 초기 진단과 인라인만 적용한 중간 측정은 별도 필드이며 최종 통계에 섞지 않는다. 재현용 측정 스크립트와 JIT 출력도 기록에 포함한다.

## 다음 작업

int 하위 타입/float 비교에 남은 캐시 확인·payload 위임 비용을 90차 대비 추적한다. 반복 측정의 변동도 구분해야 한다. 다음 구조 변경에서 양쪽 사용자 callback과 NotImplemented를 유지한다. 다른 내장 비교 슬롯, 명시적 type.__setattr__, 정렬, 수치 키 해시 문제는 91차의 실패 재현을 유지한다.
