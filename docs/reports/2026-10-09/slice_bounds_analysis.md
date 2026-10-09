# 25차: 큰 slice 경계값

24차 memoryview 슬라이스 대입은 `2d56ba0`으로 커밋했다. 이번에는 공통 `PySlice.Indices`에서 큰 start/stop을 int로 변환하다 실패하던 동작을 수정했다. step 변환은 변경하지 않았다.

## CPython 근거와 수정

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 [PySlice_Unpack/AdjustIndices](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/sliceobject.c#L227), [_PyEval_SliceIndex](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/ceval.c#L2447), [PyNumber_AsSsize_t](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/abstract.c#L1455)를 확인했다. 인덱스 프로토콜을 호출하고 정수 범위 초과를 부호에 따라 포화시킨 뒤 시퀀스 길이에 맞춘다.

SharpPy 저장소의 길이는 int 범위다. start/stop이 이 범위를 넘으면 int.MinValue/MaxValue로 포화시킨 후 기존 경계 정규화를 적용해 동일한 끝점을 얻는다. `__index__` 호출을 다시 실행하지 않으며 콜백 예외도 삼키지 않는다. 일반 변환은 기존 캐스트를 유지하고 그 캐스트의 CLR OverflowException만 처리한다. 임의의 slice 정책이나 타입별 보정은 추가하지 않았다.

## 검증과 새로 발견한 차이

실제 CPython 3.12.15와 비교했다. 신규 회귀는 13개 start × 13개 stop × 4개 step × 6개 시퀀스 타입(list, tuple, str, bytes, bytearray, memoryview), 총 4,056개 결과의 원소를 검사한다. int 최소/최대 경계와 그 바깥, 64비트 바깥, ±10**100을 포함한다. 추가로 mutable slice 대입과 사용자 `__index__`의 호출 순서·예외 전파를 검사한다.

Release 본체/harness 빌드 성공. 최종 전체 61개: raw 출력 일치 56, 지정된 시간·주소 필드만 다른 3, native `no_reported_failure` 2. 기본 검사에서 새 실패 없음. 이는 프로젝트 전체의 호환성 완료를 뜻하지 않는다.

별도 compatibility 실행에서는 다음 두 파일이 CPython에서 성공하고 SharpPy에서 실패했다. 기본 성공 집계에 포함하지 않았다.

- `slice_large_step.py`: 매우 큰 step의 변환 실패. 이를 포화시키기만 하면 int 반복문 증가와 memoryview stride 곱셈에서 또 넘칠 수 있으므로 소비 경로를 함께 수정해야 한다.
- `bytes_slice_equality.py`: 원소가 같은 bytes slice와 bytes 리터럴의 Python `==`가 거짓이다. CLR `Equals` 구현은 있지만 Python 비교 경로를 더 확인해야 한다. 경계 회귀에서는 원소 비교를 사용하고 동등성 실패는 별도 파일에 보존했다.

## 성능과 선택 근거

동일 native harness에서 전후 DLL만 달리해 process 3쌍, 각 10 warmup/10 sample을 측정했다. 생성/slice는 10,000회, 읽기/쓰기는 100,000회다. 빌드·테스트와 동시에 측정하지 않았다.

| 작업 | 기준 중앙값 ms (Q1–Q3) | 변경 중앙값 ms (Q1–Q3) | 전후 할당 B |
| --- | ---: | ---: | ---: |
| 생성 | 0.25680 (0.2455–0.2673) | 0.25865 (0.2545–0.2654) | 560,000 동일 |
| slice 생성 | 0.52955 (0.5228–0.5377) | 0.53490 (0.5279–0.5439) | 560,000 동일 |
| 읽기 | 1.57375 (1.5499–1.6095) | 1.53935 (1.5326–1.5518) | 0 동일 |
| 쓰기 | 2.37870 (2.3479–2.4131) | 2.37710 (2.3446–2.4722) | 0 동일 |

시간 범위가 겹치고 할당량은 동일하다. 일반 경로의 속도 향상은 주장하지 않는다. 중간 `int.CreateSaturating` 구현은 slice 중앙값 0.53870→0.63130ms로 약 17% 증가했다. 비트 길이 검사와 정규화 helper 분리도 측정했으나 일반 경로 비용이 남아 채택하지 않았다. 중간 측정 요약도 보존했다.

**한계:** 최종 방식은 큰 경계값을 처리할 때 CLR 예외 생성·처리 비용을 지불한다. 위 측정은 일반 크기의 경계값이므로 큰 경계값의 할당이 없다는 뜻이 아니다. 기존 구현은 해당 입력에 실패하므로 처리 속도의 전후 개선율도 제시하지 않는다. 이번 성과는 일반 경로의 측정 범위를 유지하면서 관찰 가능한 결과를 바로잡은 것이다.

[표본·hash·회귀 결과](round25-slice-bounds.json). 다음에는 재현한 bytes 동등성 경로를 우선 확인하고, 큰 step/stride 산술은 별도 범위로 이어간다. memoryview 자동 해제와 module namespace 저장소 통합 과제도 여전히 남아 있다.
