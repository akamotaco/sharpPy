# 91차: 비교 호출 순서와 진리 판정

90차를 `1e4cd65`로 로컬 커밋했다. 이번 변경은 미커밋 상태다.

## 구조와 근거

기존 VM은 왼쪽 비교를 먼저 호출하고 NotImplemented일 때만 반대편을 시도했다. 엄격한 오른쪽 하위 타입의 우선순위가 없었고, int 하위 타입은 사용자 비교 재정의 자체를 건너뛰었다. 컨테이너는 한쪽 RichCompare를 직접 호출하거나 반환값을 PyBool로 강제 변환했다. 따라서 단순히 int 하위 타입의 메서드 호출만 추가하면 VM과 컨테이너 결과가 달라진다.

고정 CPython v3.12.15 (`e848e4b09ca4bc67c0041c9ce7526dae471d4c36`)의 다음 코드와 실제 CPython 실행을 기준으로 삼았다.

- `Objects/object.c:800` do_richcompare: 엄격한 오른쪽 하위 타입 우선, 왼쪽, 아직 시도하지 않은 오른쪽, 최종 identity 또는 TypeError.
- `Objects/object.c:873` PyObject_RichCompareBool: EQ/NE의 같은 객체 단축과 결과의 진리 판정.
- `Objects/typeobject.c:8923` slot_tp_richcompare: 타입에서 메서드를 찾고 descriptor를 바인딩한다. 바인딩 오류는 지우지만 호출 본문의 오류는 전파한다.
- `Objects/typeobject.c:5563` object_richcompare: 기본 EQ의 NotImplemented와 기본 NE의 EQ 위임·진리값 반전.

## 구현

`PyObject.Compare`가 양쪽 호출 순서와 최종 fallback을 담당하고, `RichCompare`는 개별 타입의 비교 슬롯으로 유지한다. `CompareBool`은 컨테이너에서 필요한 identity 단축과 Python 진리 판정을 담당한다. 타입 이름 대신 MRO의 실제 타입 참조로 하위 관계를 판정한다.

일반 int/float/bool끼리는 Python 재정의가 개입할 수 없으므로 타입 계층 탐색을 생략한다. 실제 CLR 타입과 사용자 지정 타입 유무를 확인해 하위 타입에 이 경로를 적용하지 않는다.

숫자 타입의 NE는 자체 수치 EQ 결과를 반전하고 NotImplemented를 보존한다. object NE의 사용자 EQ 위임과 분리해 불필요한 RichCompare 재진입과 진리 판정을 생략한다. CPython의 long_richcompare·float_richcompare도 수치 결과에서 NE를 직접 판정한다.

int 하위 타입과 일반 사용자 클래스의 비교는 인스턴스 속성 hook을 거치지 않고 클래스 MRO에서 찾는다. int 네이티브 비교 지점에 도달하면 뒤의 mixin을 탐색하지 않는다. Python 클래스의 ClassDict를 읽으며 TypeDict에 복사된 상속 슬롯이 MRO를 가리지 않게 한다.

메서드가 없다는 조회 결과만 타입별 비트로 캐시한다. 타입 속성 변경 시 전역 세대를 갱신하므로 부모 변경도 무효화에 포함된다. 존재하는 메서드는 매번 조회하며, 삭제된 함수나 바인딩 결과를 캐시에 보관하지 않는다. descriptor는 호출 때마다 바인딩한다. 기존 GlobalMethodCache는 하위 타입 무효화가 미완성이어서 재사용하지 않았다. C#에서 공개 ClassDict를 직접 변경하는 호스트는 기존 InvalidateTypeCache 계약을 지켜야 한다.

정확한 PyFunction은 self와 other를 바로 전달한다. 기존 PyMethod와 1개 인자 배열을 만들지 않아 비교 호출당 72 B를 줄인다. 다른 descriptor는 정상적으로 바인딩하며, 바인딩과 본문 호출의 예외 처리 범위를 구분한다.

VM과 AST 비교 경로, list/tuple/dict/set의 요소 비교, dict view·slice·mappingproxy·일반 sequence membership 및 min/max를 공통 판정에 연결했다. 비교 결과는 객체 그대로 반환하며 조건·not·단축 평가에서는 IsTrue를 사용한다. list membership이 비교 예외를 삼키던 catch도 제거했다.

tuple 순서 비교는 앞에서부터 EQ를 진리 판정한 뒤 최초 다른 원소에 요청된 연산을 수행한다. LT와 GT를 임의로 둘 다 호출하거나 사용자 결과를 bool로 바꾸지 않는다. None의 임의 순서 비교도 제거해 공통 TypeError로 처리한다. 명시적인 object.__eq__ 호출은 사용자 비교로 재진입하지 않고 identity/NotImplemented를 반환한다.

## 회귀 검증

새 `comparison_dispatch_regression.py`와 `comparison_container_regression.py`, 기존 `integer_subclass_comparison_dispatch.py`를 기본 평가에 포함했다. 모두 수정 전 DLL에서 실패 또는 불일치하며, 수정 후 실제 CPython과 출력이 일치한다.

회귀는 하위 타입 우선순위·같은 타입의 양쪽 호출·같은 객체의 연산과 컨테이너 비교 차이, NotImplemented, 임의 반환 객체와 __bool__, 기본 object NE와 int NE의 차이, descriptor·staticmethod·classmethod, 바인딩 오류와 본문 오류, None으로 비활성화한 메서드, 부모의 동적 변경·삭제, 인스턴스 hook 무시, mixin MRO, object 비교 복원, 지원하지 않는 순서 비교의 오류 메시지를 포함한다. 컨테이너의 callback 순서·진리 판정·identity 단축·오류 전파와 tuple 순서 비교의 반환값도 대조했다.

- 최종 Release 및 Godot 빌드: 경고 0, 오류 0.
- PerformanceHarness 빌드: 오류 0, 기존 ByteArraySliceMutationProbe 관련 nullable 경고 2개.
- 전체 160개: 완전 일치 155개, 지정 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개.
- Godot: SharpPy 소스 직접 포함 빌드 및 기본 17개 + 관련 16개, 총 33개 fixture 통과. 관련 16개 출력은 새 CPython 실행과 대조했다.
- 최종 숫자 NE 변경 후 전체 평가와 Godot 빌드·실행을 반복했다. 부모 변경·삭제, setattr/delattr, 비교 실행 중 메서드 교체 및 descriptor 반복 바인딩 회귀도 포함한다. 최종 DLL SHA256과 전체 평가 기록이 일치한다.
- Windows x64/.NET 8.0.31 검증이다. 비Windows·모바일 실기 결과는 포함하지 않는다.

## 성능

기존에 전후 모두 올바른 결과를 내는 작업을 사용하며 합계 checksum을 검사한다. 새로운 user_compare는 일반 사용자 클래스의 Python __lt__ 호출이다. 이전에 사용자 메서드를 건너뛰던 int 하위 타입 재정의 경로를 속도 기준으로 삼지 않았다.

Release, TieredCompilation=0, 100,000회, 워밍업 10회, 표본 15회, 전후 3쌍 교대 실행이다. 빌드·기능 검사 종료 후 측정했으며 workload 소스는 고정했다.

캐시 메모리는 별도 native probe에서 새 타입 1,000개로 측정했다. 클래스 생성은 1,024,000→1,032,000 B로 클래스당 8 B 증가했다. 조회 실패 캐시를 처음 만드는 비교는 합계 40,000 B, 같은 타입의 반복 비교는 0 B였다. 워밍업 후 Python workload의 할당과 이 초기 비용을 구분해야 한다.

초기 진단, 숫자 빠른 경로 이전, 조회 캐시 이전, 전체 메서드 캐시 실험, 숫자 NE 직접 판정 이전의 표본도 단계별로 구분해 보존한다. 다른 시점의 표본을 합쳐 최종 중앙값을 만들지 않는다.

재현 시 직전 커밋 `1e4cd65`와 현재 소스를 각각 Release로 빌드한다. 양쪽에 같은 현재 PerformanceHarness 및 workload 파일을 사용하고, 하네스의 sharppy.dll만 각 빌드로 교체한다. `DOTNET_TieredCompilation=0`으로 설정하고 다음 인자를 전후 교대로 각각 3회 실행한다.

```text
PerformanceHarness.dll tools/PerformanceHarness/numeric_comparison_workloads.py 100000 15 <workload>
PerformanceHarness.dll tools/PerformanceHarness/workloads.py 100000 15 dictionary
PerformanceHarness.dll --comparison-cache-probe
```

`<workload>`는 아래 표의 dictionary 이외 이름이다. 프로세스별 15개 시간 표본의 중앙값을 구한 뒤 3개 프로세스 중앙값의 중앙값으로 비교한다. 원시 표본과 할당 범위도 보존한다.

| workload | 이전 ms | 이후 ms | 시간 변화 | 반복당 할당 변화 |
| --- | ---: | ---: | ---: | ---: |
| int_subclass_compare | 136.523 | 145.670 | +6.70% | 0 B |
| int_subclass_float_compare | 140.459 | 146.996 | +4.65% | 0 B |
| mixed_compare_small | 140.141 | 139.114 | -0.73% | 0 B |
| integer_compare | 135.151 | 135.888 | +0.55% | 0 B |
| user_compare | 92.651 | 89.407 | -3.50% | -7,200,000 B |
| dictionary | 104.918 | 105.720 | +0.76% | 범위 겹침 |

표의 반복은 n=100,000인 workload 전체 실행 1회다. user_compare는 72,306,552→65,106,552 B로 감소했다. 사용자 함수 비교 호출당 72 B 절감과 일치한다. dictionary의 할당 범위는 이전 30,854,592–30,864,992 B, 이후 30,854,592–30,863,512 B이며 개선을 주장하지 않는다.

두 int 하위 타입 workload는 전후 프로세스 중앙값 범위가 겹치지 않으며 초기 여러 단계에서도 증가했다. 호환성 수정으로 새로 수행하는 메서드 확인·하위 타입 우선순위 판정 비용이 남아 있다. 이 결과를 최적화 완료로 간주하지 않는다. 다음 우선 작업은 메서드가 없는 int 하위 타입에서 동적 변경을 보존하면서 반복 판정 비용을 줄이는 것이다.

나머지 네 workload는 시간 범위가 겹친다. 특히 마지막 integer_compare의 이후 프로세스 중앙값은 150.696 ms로 다른 두 회(135.888/134.021 ms)보다 높았다. 이를 임의로 제외하지 않았고 원인도 확정하지 않았다. 사용자 비교의 -3.50%도 속도 개선으로 확정하지 않는다. 숫자 NE 직접 판정 전 일반 정수 비교는 +2.35%였으나, 서로 다른 시점의 측정만으로 원인별 개선율을 단정하지 않는다.

[환경·실행 출력·전체 성능 표본](round91-validation.json)

## 남은 범위와 다음 작업

- `comparison_remaining_slots.py`: 문자열의 반대편 비교, tuple 하위 타입 재정의, 명시적인 int.__eq__ descriptor가 아직 불일치한다. 기본 통과 목록에 넣지 않았다. 다른 내장 슬롯이 NotImplemented를 반환해야 할 곳도 후속 조사 대상이다.
- sorted/list.sort의 기존 비교 절차는 이번 공통 판정 연결 대상에서 제외했다. 비교 callback 순서와 임의 반환값을 따로 검증해야 한다.
- `comparison_class_setattr.py`: 명시적인 type.__setattr__의 인자 바인딩 실패를 보존했다. 수정 전후 모두 실패하는 별도 문제이며, builtin setattr/delattr를 통한 비교 캐시 무효화 회귀와 구분한다.
- 일반 dict의 사용자 해시 및 전체 폭 해시 저장, 동등 int/float 키 해시 불일치도 여전히 남는다. 기존 두 해시 fixture의 불일치를 다시 확인했다. 이번 결과는 dict 전체 프로토콜·비교 중 저장소 변경의 호환성을 보증하지 않는다.
- 기본 하위 타입의 비교 판정 비용을 계속 추적한다. 조회 실패 캐시는 구현했으나 타입 계층 판정과 호출 단계의 비용은 남는다. 후속 슬롯 유지·무효화 구조는 동적 변경과 MRO 정확성을 회귀로 보장한 뒤 측정해야 한다.
- 기존 큰 문자열 파싱과 85→86 예외 생성 비용 증가의 원인도 별도 미해결 사항이다.
