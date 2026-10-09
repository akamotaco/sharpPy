# 24차: memoryview 슬라이스 대입

23차 변경은 `1be4069`로 커밋했다. 이번에는 기존에 실패하던 1차원 바이트 memoryview의 슬라이스 대입을 구현했다. CPython 3.12.15 실행 결과와 고정 소스 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`을 기준으로 검증했다.

## 동작과 구조

[memory_ass_sub](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/memoryobject.c#L2609)는 원본 버퍼를 확보한 다음 slice 경계의 `__index__`를 실행하고, 성공·실패 모두 원본 버퍼를 반환한다. [copy_single](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/memoryobject.c#L401)은 대상의 해제 상태와 구조를 검사한 뒤 복사한다. 이 순서를 적용해 콜백이 원본 view를 해제하면 BufferError, 대상을 해제하면 ValueError가 발생하게 했다. 임시 버퍼 사용 횟수와 독립적인 자식 view의 수명은 구분한다.

연속 영역은 겹침을 지원하는 `Span.CopyTo`를 사용한다. 같은 저장소의 비연속 영역은 원본을 먼저 임시 배열로 읽어 복사 순서에 따른 데이터 손상을 막는다. 서로 다른 저장소의 비연속 복사는 직접 수행한다. bytearray의 내부 span은 모든 Python 콜백과 검사가 끝난 후에만 얻고 복사 중에는 콜백을 호출하지 않는다. 길이가 다르면 대상 데이터를 바꾸지 않는다.

원본이 memoryview이면 기존 view를 잠시 빌려 별도 view 할당을 피한다. bytes/bytearray 입력은 기존 GetBuffer 경로를 사용하고 임시 view를 반환한다. 추가한 export 카운터를 포함해 view 생성 할당은 기존과 같은 56바이트였다.

## 검증

`tests/memoryview_slice_assignment_regression.py`는 정방향·역방향·간격 slice, 겹침 조합, 빈 slice, 자기 대입, 길이 불일치, 잘못된 원본 타입, 읽기 전용 대상, 해제된 원본, 0 step, 콜백 중 해제 및 실패 후 export 정리를 검사한다. 기준 DLL은 신규 테스트에서 실패했고 변경 후 실제 CPython과 출력이 일치했다.

Release 본체/harness 빌드 성공. 전체 60개 결과는 raw 출력 일치 55, 지정된 시간·주소 필드만 다른 3, native `no_reported_failure` 2였다. 이후 길이 계산 helper에 인라인 속성을 추가한 최종 DLL에서도 memoryview 회귀 4개를 다시 비교해 모두 일치했다. evaluator 자체 단위 검사 3개도 통과했다.

## 성능

사용자가 CPU를 사용 중이었다고 알려 준 초기 측정은 시간 비교 결론에서 제외했다. CPU 사용 종료 통보 후 측정했으며, 중간 결과와 최종 결과를 모두 보존했다. 중간에는 slice 생성 시간이 증가해 공통 길이 계산 helper에 인라인 속성을 추가했다. 이 변경 하나의 인과적 효과까지 입증한 것은 아니다.

최종 측정은 동일 harness에 기준/변경 DLL만 달리한 3개 process 쌍이다. 각 process는 10회 warmup과 10회 표본을 수행했다. 생성/slice는 10,000회, 읽기/쓰기는 100,000회다. 빌드와 테스트는 측정과 동시에 실행하지 않았다.

| 작업 | 기준 중앙값 ms (Q1–Q3) | 변경 중앙값 ms (Q1–Q3) | 전후 할당 B |
| --- | ---: | ---: | ---: |
| 생성 | 0.30160 (0.2282–0.3428) | 0.23720 (0.2278–0.2613) | 560,000 동일 |
| slice 생성 | 0.52655 (0.5147–0.5656) | 0.54040 (0.5216–0.5511) | 560,000 동일 |
| 정수 읽기 | 1.60995 (1.5830–1.6328) | 1.53040 (1.5166–1.6712) | 0 동일 |
| 정수 쓰기 | 2.41455 (2.3369–2.5548) | 2.34545 (2.3176–2.4645) | 0 동일 |

모든 전후 사분위 범위가 겹친다. 현재 자료로 기존 경로의 뚜렷한 속도 개선이나 퇴행을 단정하지 않는다. 할당량은 동일하다.

새 연속 대입은 사전 생성한 memoryview에서 1,024바이트를 10,000회 복사할 때 중앙값 0.89320ms (Q1 0.8723, Q3 1.2510), 루프 내 추가 할당 0바이트였다. 이는 native 호출 측정이며 Python VM 전체 실행 성능이 아니다. 기준 버전은 이 동작을 지원하지 않아 개선율을 계산하지 않는다. strided overlap의 임시 배열 비용과 bytes 입력의 임시 view 할당은 이 0바이트 수치에 포함되지 않는다.

[표본·DLL hash·회귀 근거](round24-memoryview-slice.json). 전체 raw 출력은 `.artifacts/round24-tests-final.json`, 최종 집중 검사는 `.artifacts/round24-inline-tests.json`에 있다.

## 남은 범위와 다음 후보

마지막 Python 참조 제거 시 자동 해제는 여전히 미해결이다. 명시적 해제와 이번 임시 export 정리가 이 문제를 해결한 것으로 해석하지 않는다. 다른 format, 다차원 view, 완전한 PEP 688 사용자 exporter, 매우 큰 slice 경계/step 처리는 이번 완료 범위가 아니다.

다음에는 `PySlice.Indices`의 큰 정수 경계·step을 CPython의 포화 변환과 비교하고, 기존 memoryview stride 계산의 정수 범위까지 함께 확인한다. 별도로 유지 중인 module namespace 이중 저장소와 VM 참조 소유권 문제는 메서드별 보정 대신 구조 변경이 필요한 과제로 남긴다.
