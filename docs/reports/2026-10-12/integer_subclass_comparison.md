# 90차: int 하위 타입의 기본 비교

89차를 `956dd3e`로 커밋하고 `origin/work/oserror-compatibility`에 푸시했다. 원격 브랜치는 해당 커밋까지 반영되었으며 이번 90차 변경은 미커밋 상태다.

## 문제와 구현

기본 비교를 상속한 int 하위 타입에서 순서 비교가 두 정수값을 Int64로 축소했다. 큰 값은 OverflowException을 내고, float 비교에서는 double 정밀도 손실도 남았다. 반대 방향의 기본 int/float/bool 비교는 PyIntSubclass를 수치 타입으로 인식하지 않아 같은 값도 False 또는 TypeError가 되었다.

고정 CPython v3.12.15 (`e848e4b09ca4bc67c0041c9ce7526dae471d4c36`)의 `Objects/longobject.c:3313` long_richcompare와 CHECK_BINOP, `Objects/floatobject.c:418` float_richcompare 및 실제 CPython 3.12.15 실행을 확인했다. 기본 수치 비교는 하위 타입의 전체 정수값을 사용하며 사용자 변환 메서드를 호출하지 않는다.

- PyIntSubclass의 비교 진입점에서 이미 보관한 PyInt에 연산을 위임한다. 다른 int 하위 타입도 보관된 값을 사용한다. Int64 축소와 중복 연산 분기를 제거했다.
- PyInt/PyFloat/PyBool의 기존 수치 경로 뒤에 PyIntSubclass 값 읽기를 추가했다. float 비교는 89차의 정확한 혼합 비교 함수를 공유한다.
- `__int__`, `__index__`, `__float__`를 호출하거나 새 숫자 wrapper를 만들지 않는다. 기본 타입의 기존 빠른 분기 순서도 유지한다.

이번 범위는 기본 수치 비교다. 사용자 비교 메서드의 재정의 및 하위 타입 우선 호출을 구현한 것으로 해석하지 않는다.

## 검증

`tests/integer_subclass_comparison_regression.py`에서 기본·다단계·서로 다른 int 하위 타입과 변환 메서드가 예외를 내는 타입을 검사했다. 676개 조합의 양방향 6종 비교, 총 8,112개 비교 결과를 CPython과 대조한다. 정밀도 경계, Int64 경계, 큰 양수·음수, float/bool, 무한대·NaN·subnormal을 포함한다.

추가로 리스트 관찰, dict의 기본 int/하위 타입 키 조회·덮어쓰기·기존 키 identity·pop을 검사했다. 기본 순서 비교가 NotImplemented를 반환하면 반대편 사용자 메서드에 원래 하위 타입 객체가 전달되는지도 확인했다.

기존 실패 fixture `tests/compatibility/numeric_int_subclass_comparison.py`도 통과 목록에 편입했다. 최종 새 회귀를 수정 전 DLL로 실행하면 실패하고, 수정 후에는 CPython 출력과 일치한다.

- 전체 157개: 완전 일치 152개, 지정 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개.
- 최종 Release 및 Godot 빌드: 경고 0, 오류 0.
- Godot: SharpPy 소스 직접 포함 빌드 성공. 기본 17개와 관련 13개, 총 30개 fixture 통과. 관련 13개는 새 CPython 실행과 출력 비교했다.
- 최종 변경 후 전체 평가와 Godot 빌드·실행을 다시 수행했다. 최종 DLL SHA256과 전체 평가 기록이 일치한다.
- Windows x64/.NET 8.0.31에서 검증했다. 비Windows·모바일 실기 결과는 포함하지 않는다.

## 성능 측정

기존에 정상 결과를 내던 작은 값의 하위 타입/기본 int 비교, 하위 타입/float 비교와 일반 int/float 및 int끼리의 대조군을 사용한다. 하네스가 반복 결과 합계를 검사한다. 이전에 예외를 내던 큰 값의 경로를 속도 개선 기준으로 삼지 않았다.

Release, TieredCompilation=0, 100,000회, 워밍업 10회, 표본 15회, 전후 3쌍 교대 실행이다. 기능 검사·빌드와 겹치지 않았다.

초기 구현은 각 연산별 메서드에서 PyInt.RichCompare로 위임해 연산 분기가 중복됐다. 하위 타입/float 작업에서 +1.44%와 겹치지 않는 프로세스 중앙값 범위가 관측되어 진입점의 직접 위임으로 정리했다. 초기 표본은 최종 결과와 별도로 보존한다.

최종 전후 비교의 시간은 프로세스별 중앙값 세 개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 변화 | 전후 할당 B |
|---|---:|---:|---:|---:|
| int_subclass_compare | 143.5151 | 142.5320 | -0.69% | 9,105,192 |
| int_subclass_float_compare | 138.7680 | 139.0719 | +0.22% | 9,105,192 |
| mixed_compare_small | 144.6492 | 144.7523 | +0.07% | 9,104,032 |
| integer_compare | 138.3028 | 140.8730 | +1.86% | 9,104,032 |

네 작업 모두 전후 프로세스 시간 범위가 겹치며 할당은 모든 표본에서 전후 동일하다. 초기 하위 타입/float의 분리된 시간 범위는 최종 측정에서 재현되지 않았다. 대조군 변동도 있으므로 초기 차이의 원인을 중복 분기로 확정하거나 속도 개선을 주장하지 않는다. 최종 일반 int 대조군의 +1.86%도 숨기지 않고 원시 표본에 보존한다.

[환경·실행 출력·성능 원시 표본](round90-validation.json)

## 다음 작업

`tests/compatibility/integer_subclass_comparison_dispatch.py`는 미해결 상태다. CPython은 오른쪽의 엄격한 하위 타입 메서드를 먼저 호출하며 사용자 반환값과 NotImplemented를 보존한다. 현재 SharpPy는 사용자 int 하위 타입의 비교 재정의를 건너뛰고 기본 값만 비교한다. 해당 fixture는 기본 통과 목록에 넣지 않았다.

이는 타입별 값 비교와 양쪽 메서드의 호출 순서를 분리해 해결해야 한다. 현재 VM의 CompareOperation만 반대편 호출을 처리하고 엄격한 하위 타입 우선순위가 없다. dict/list/tuple/set은 RichCompare를 직접 호출하며 일부는 결과를 PyBool로 강제 변환한다. 다음은 이 경로들의 공통 비교 판정, callback 순서·진리 판정·동적 메서드 변경을 회귀로 고정하는 것이다. 일반 숫자 경로의 조회·할당 비용도 대조한다.

`numeric_dict_hash.py`의 동등 int/float 키 해시 불일치, int 하위 타입 dict 해시 callback 누락, 큰 문자열 파싱 및 85→86 예외 생성 시간 증가의 원인도 미해결로 유지한다.
