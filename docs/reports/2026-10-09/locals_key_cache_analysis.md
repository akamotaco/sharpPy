# 7차 locals 관찰용 이름 키 재사용

기준일: 2026-10-09. 5·6차 변경은 `fcd610b`로 커밋했다. 이 보고서는 7차 구현·검증 자료와 함께 커밋한다.

## 개선 근거와 구조

`locals()` 및 `frame.f_locals`를 반복 조회하는 두 작업을 공통 workload에 추가했다. 기존 8개 작업에 이 두 경로가 없어서, 관찰하지 않는 호출에서의 할당 감소를 관찰하는 호출에도 적용할 수 있다고 가정하지 않았다.

SharpPy의 갱신 경로는 매번 각 변수 이름에 `new PyString(name)`을 호출했다. CPython 3.12.15 [Objects/frameobject.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/frameobject.c)의 `_PyFrame_GetLocals`는 code object의 `co_localsplusnames`에 저장된 Python 이름 객체를 꺼내 dictionary 키로 사용한다. 출처는 [기존 manifest](round5-cpython-manifest.json)에 고정했다.

SharpPy code object에 local/cell/free 이름 키 배열을 지연 생성하도록 추가했다. 조회 시 키를 재사용한다. Python 값과 dictionary 갱신 규칙은 유지한다. 이름 배열은 code object에 보관하며 전역 문자열 캐시는 사용하지 않는다.

CPython의 코드 이름 tuple과 달리 SharpPy의 C# metadata는 공개된 mutable list다. 배열 길이와 각 이름을 확인하고 바뀐 항목은 새 키로 교체한다. C# 회귀에서 이름 변경과 목록 길이 증가 후 올바른 이름으로 조회되는지 확인한다. 다른 기존 metadata index cache까지 임의 변경에 대한 전체 호환성을 보장하는 것은 아니다.

code object마다 참조 필드 3개, 총 24B가 추가되고 최초 관찰 시 키 배열/문자열이 생성된다. 이번 표는 warmup 뒤 정상 상태의 누적 thread 할당량이다. 시작·컴파일 비용과 최대/보유 메모리는 이 측정으로 판단하지 않는다.

## 회귀와 namespace 차이

[locals refresh 회귀](../../../tests/locals_refresh_regression.py)는 반복 재할당·삭제·None 값, `locals()`와 `f_locals`의 identity, 호출 간 독립성을 검증한다. 변경 전후 모두 CPython 3.12.15와 일치한다. 기존 locals/closure/generator 회귀도 유지했다.

Release 전체 비교 36개 항목: 출력 일치 30개, 기존 출력 차이 4개, native 실패 보고 없음 2개. 새 실패는 없다. Release 및 Debug 빌드, C# frame checks, Debug의 새 locals refresh 회귀도 통과했다.

module/class namespace 조사에서 다음 기존 차이를 별도 최소 fixture로 기록했다. 두 fixture는 CPython 3.12.15에서 통과하고 변경 전 SharpPy에서 실패했다. 이번 키 재사용 변경은 이 경로를 고치지 않으므로 기본 통과 목록에 포함하지 않았다.

- [module locals live](../../../tests/compatibility/module_locals_live.py): 보관한 module locals dictionary가 이후 변수 생성·삭제를 관찰해야 한다. SharpPy wrapper는 namespace 쓰기의 일부를 반영하지만 이미 반환된 dictionary의 읽기는 오래된 snapshot을 사용한다.
- [class locals mapping](../../../tests/compatibility/class_locals_mapping.py): class body의 `locals()`와 `f_locals`는 같은 namespace mapping을 노출해야 한다. SharpPy는 현재 별도 dictionary를 구성해서 identity, 후속 변수 관찰, mapping 쓰기에서 차이가 난다.

known-gap 결과는 전체 36개 평가와 별도로 보관했다. 실제 실패를 측정 변동이나 출력 순서 차이로 분류하지 않았다.

## 반복 성능 결과

크기 10,000, 작업당 process별 10회 warmup 및 10개 sample, 기준/최종 각 3개 독립 process. 실행 순서를 번갈아 수집했다. 모든 반환값을 검증했다. 초기 수집 중 Debug 빌드가 겹친 시간 자료는 최종 비교에서 제외하고, 빌드가 끝난 뒤 전부 다시 수집했다. 아래는 총 30개 sample의 중앙값이다.

| 작업 | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
|---|---:|---:|---:|---:|
| addition | 14.527 | 13.877 | 1,841,352 | 1,841,352 |
| inplace_addition | 16.638 | 16.442 | 1,841,352 | 1,841,352 |
| arithmetic | 14.713 | 14.730 | 2,401,352 | 2,401,352 |
| function_call | 13.151 | 12.841 | 8,082,560 | 8,082,560 |
| attributes | 13.704 | 13.722 | 2,254,216 | 2,254,216 |
| dictionary | 13.108 | 13.146 | 3,926,616 | 3,926,616 |
| comprehension | 3.775 | 3.852 | 1,464,496 | 1,464,496 |
| bound_builtin | 6.920 | 7.085 | 1,784,464 | 1,784,464 |
| locals_observation | 12.831 | 13.057 | 2,721,720 | 1,761,720 |
| frame_observation | 12.567 | 12.660 | 2,802,000 | 1,522,000 |

locals_observation의 할당은 960,000B, 약 35.3% 감소했다. 3개 이름 × 조회 10,000회 × 키 객체 32B에 해당한다. frame_observation은 4개 이름으로 1,280,000B, 약 45.7% 감소했다. 나머지 8개 작업의 할당량은 동일했다.

시간 중앙값은 locals_observation 약 1.8%, frame_observation 약 0.7% 증가했다. 사분위 범위는 locals 이전 12.250–13.219ms/이후 12.424–13.474ms, frame 이전 12.287–12.938ms/이후 12.336–12.862ms로 겹친다. 이름 검증 루프가 추가되었지만 이 측정만으로 안정적인 시간 퇴행이나 개선을 확정하지 않는다. 채택 근거는 관찰 경로의 재현 가능한 할당 감소다.

원자료: [성능](round7-performance.json), [전체 비교](round7-tests.json), [기존 namespace 차이](round7-known-gaps.json), [변경 전 refresh 회귀](round7-refresh-before.json).

## 다음 작업

class body에서 실제 namespace mapping을 그대로 노출하는 경로를 먼저 검증한다. module wrapper의 bidirectional synchronization은 생성·삭제·update·views·iteration을 함께 확인해야 한다. getter 하나의 snapshot 갱신으로 전체 dictionary 호환성을 해결했다고 판단하지 않는다.

benchmark는 이제 10개 작업을 수집한다. 기존 benchmark 명령을 그대로 사용하며 새 두 작업도 CPython과 동일한 반환값을 검증한다.
