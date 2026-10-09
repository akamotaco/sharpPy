# 29차: slice 대입·삭제의 증가식과 빈 bytearray 대입

28차 읽기 변경은 `c63cb7a`으로 커밋했다. 이번에는 bytearray 확장 대입·삭제와 list slice 삭제에서 int 범위 step의 증가식 overflow 및 list 경계 콜백 중복 실행을 수정했다.

## 구현과 근거

bytearray 확장 대입은 정규화 결과에서 원소 수를 구하고 길이를 확인한 뒤 그 수만큼 대입한다. 인덱스 배열을 만들지 않는다. bytes/bytearray 원본은 기존처럼 독립된 내용 복사본을 사용하되 빈 List에 AddRange하는 대신 컬렉션 생성자로 바로 복사한다. 따라서 자기 대입에도 원본이 보존된다.

bytearray/list 삭제는 정규화 결과를 한 번만 사용하고 높은 실제 위치부터 제거한다. 끝 위치와 이동량은 long으로 계산한다. 임시 인덱스 리스트, 정렬, 역순 변환을 제거했다. list의 기존 GetLength 재호출도 없어져 `__index__`가 step/start/stop 순서로 한 번씩 실행된다.

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 [bytearray_ass_subscript](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/bytearrayobject.c#L591)에서 빈 값 대입은 삭제라는 점도 확인했다. `data[::2] = b''`는 기존 SharpPy에서 길이 오류였으나 이제 선택된 원소를 삭제한다. 이 경로와 del은 같은 삭제 함수를 사용한다. 활성 buffer export가 있으면 CPython처럼 크기 변경을 거부한다. [list_ass_subscript](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/listobject.c#L2956)의 정규화/길이 기반 처리도 대조했다.

## 검증

신규 회귀는 1/2/-1/-2, int.MaxValue/int.MinValue, ±2**30 step, 여러 시작·끝 경계와 빈 결과를 사용한다. bytearray 대입·빈 값 대입·삭제 및 list 삭제의 최종 원소를 실제 CPython 3.12.15와 비교한다. 자기 대입, 길이 오류 시 대상 불변, 콜백 호출 순서·횟수, buffer export 중 빈 값 대입의 예외도 검사했다.

기준 DLL은 신규 회귀에 실패하고 최종 DLL은 일치했다. Release 본체/harness 빌드 성공. 전체 기본 67개: raw 일치 62, 지정된 시간·주소 필드만 다른 3, native `no_reported_failure` 2. 새 실패 없음. 큰 step 변환의 별도 기존 실패는 이 성공 수에 포함하지 않는다.

## 성능

native bytearray 대입/삭제만 측정했다. 전후 같은 harness, process 3쌍, 각각 100 warmup/10 sample이며 두 버전 모두 `DOTNET_TieredCompilation=0`이다. 회귀 검사와 빌드는 측정과 병행하지 않았다. 작은 입력은 1,000회, 큰 입력은 100회 작업한다. **삭제에는 매번 대상 bytearray를 새로 만드는 비용도 포함**되며 전후 동일하다.

크기 1,024의 대표 결과:

| 작업 / step | 기준 중앙값 ms (Q1–Q3) | 최종 중앙값 ms (Q1–Q3) | 기준 → 최종 할당 B |
| --- | ---: | ---: | ---: |
| 대입 / 2 | 0.09965 (0.0963–0.1134) | 0.05080 (0.0498–0.0516) | 487,200 → 56,800 |
| 대입 / -2 | 0.11470 (0.1009–0.1334) | 0.05095 (0.0496–0.0534) | 487,200 → 56,800 |
| 삭제 / 2 | 0.81600 (0.7886–1.0805) | 0.60865 (0.5956–0.6305) | 542,400 → 112,000 |
| 삭제 / -2 | 0.86270 (0.8472–0.9014) | 0.62110 (0.5852–0.6534) | 542,400 → 112,000 |

이 네 조건의 중앙값은 약 49.0~55.6%, 25.4~28.0% 감소했고 사분위 범위는 겹치지 않았다. 할당은 각각 약 88.3%, 79.4% 감소했다. 작은 양수 대입은 0.06460→0.07090ms로 중앙값이 증가했지만 범위가 겹쳐 속도 향상이나 퇴행을 확정하지 않는다. 그 조건의 할당은 192,000→64,000바이트로 감소했다. 모든 조건의 원자료와 초기 후보 측정도 보존했다.

[표본·사분위·hash·회귀 상태](round29-slice-mutation.json). 전체 raw 출력은 `.artifacts/round29-final-tests.json`이다. 이 수치는 고정 JIT 조건의 native bytearray 작업이며 list 삭제나 전체 Python VM 성능 수치가 아니다.

## 다음 범위

step 자체가 int 범위를 넘는 변환, range의 정수 표현, memoryview stride는 아직 남아 있다. `slice_large_step.py`를 기준으로 저장소 인덱스 정규화와 관찰 가능한 range/view 정보를 구분해 검토한다. 일반 iterator의 종료·재진입, callback 중 크기 변경, Python 소멸 시점도 이번에 완성하지 않았다.

삭제는 여전히 CLR List.RemoveAt/Pop을 반복하므로 최악에는 제곱 시간이다. CPython처럼 남는 구간을 한 번씩 옮기는 선형 처리와 비교할 가치가 있다. 이번 변경은 임시 배열·정렬과 불필요한 콜백을 제거한 단계로 기록한다.
