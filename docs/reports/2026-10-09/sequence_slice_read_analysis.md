# 28차: slice 읽기의 증가식 overflow

27차 list 확장 대입은 `990e9a3`으로 커밋했다. 이번에는 `b'abcdef'[1::2147483647]`처럼 step이 int에 들어가도 마지막 `position + step`이 넘쳐 잘못 반복하는 읽기 오류를 수정했다.

## 구현과 검증 범위

bytes·str·list는 27차의 안전한 길이 함수를 사용해 결과 원소 수만큼 순회한다. 마지막 위치 증가가 넘쳐도 그 값을 다시 사용하지 않는다. bytes/str은 크기를 아는 배열을 바로 채우고 list는 용량을 미리 잡아 중간 확장을 없앤다. bytes의 기존 연속 복사는 유지한다.

bytearray와 비연속 tuple은 기존 결과 구성 방식을 유지하면서 **양수** 반복 위치만 long으로 계산한다. 정규화한 길이가 int 범위이고 step도 int이므로 종료를 확인할 마지막 증가까지 long에 들어간다. 음수 step은 유효한 비음수 위치에서 한 번 감소하면 최솟값이 int.MinValue 이상이고, 음수가 된 뒤 종료하므로 기존 int 반복식을 유지한다. 연속 tuple은 결과 배열에 Array.Copy한다.

근거는 고정 CPython 3.12.15 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 [slice 길이 정규화](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/sliceobject.c#L269)와 [bytes_subscript](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/bytesobject.c#L1615)다. 저장소 접근 결과를 실제 CPython 실행과 비교했다.

신규 `slice_read_step_regression.py`는 5개 타입에서 일반/역방향 step, int.MaxValue/int.MinValue, ±2**30, 빈 결과, callback 호출 순서를 검사한다. 첫 테스트 작성의 None 끝점 기대값 오류는 CPython에서 먼저 실패해 바로잡았다. 수정된 테스트는 기준 SharpPy DLL에서 실패하고 최종 DLL에서 일치했다. 기존 `slice_step_iteration_overflow.py`도 통과해 기본 검사에 편입했다.

Release 본체/harness 빌드 성공. 최종 기본 검사 66개: raw 일치 61, 지정된 시간·주소 필드만 다른 3, native `no_reported_failure` 2. 새 실패 없음.

## 성능과 채택 과정

5개 타입 × 크기 16/1,024 × step 1/2/-1, 총 30개 조건을 비교했다. 작은 입력은 표본당 1,000회, 큰 입력은 100회 slice를 생성한다. 전후 같은 harness와 다른 본체 DLL, process 3쌍, 각 100 warmup/10 sample이다. 빌드·테스트는 측정과 병행하지 않았다.

초기 측정은 기본 tiered JIT와 10 warmup을 사용했다. tuple/bytearray의 버퍼 구성 변경은 그 측정에서 비용 증가가 나타나 제외했다. 100 warmup 후에도 변경하지 않은 연속 bytearray 경로에서 수배의 시간 차이가 나타나 **DOTNET_TieredCompilation=0**을 두 버전에 동일하게 적용한 별도 측정을 추가했다. JIT 전환의 영향을 의심할 근거이나 원인을 확정한 것은 아니다. 다음 표의 시간은 이 고정 조건에 한정한다. 기본 JIT 원자료와 중간 후보의 수치도 보존했다.

대표 결과는 크기 1,024, 표본당 100회다.

| 타입 / step | 기준 중앙값 ms (Q1–Q3) | 최종 중앙값 ms (Q1–Q3) | 기준 → 최종 할당 B |
| --- | ---: | ---: | ---: |
| bytes / 2 | 0.05370 (0.0500–0.0549) | 0.01915 (0.0189–0.0197) | 181,600 → 56,800 |
| str / 2 | 0.06450 (0.0605–0.0821) | 0.02160 (0.0208–0.0230) | 439,200 → 212,800 |
| list / 2 | 0.24865 (0.2430–0.2598) | 0.22855 (0.2205–0.2364) | 1,256,800 → 833,600 |
| tuple / 1 | 0.46625 (0.4632–0.4785) | 0.01870 (0.0182–0.0231) | 2,484,800 → 824,800 |
| bytearray / 2 | 0.06775 (0.0657–0.0767) | 0.06345 (0.0578–0.0675) | 185,600 동일 |

위 첫 네 조건은 중앙값 약 64.3%, 66.5%, 8.1%, 96.0% 감소이고 사분위 범위가 겹치지 않는다. bytearray 시간 범위는 겹쳐 개선을 단정하지 않는다. 최종 30개 조건에서 최종 Q1이 기준 Q3보다 높은 조건은 없었다. 이는 성능 퇴행이 없다는 일반적 증명이나 Python VM 전체의 개선율이 아니다. 할당은 bytes의 비연속 slice, str, list, tuple의 연속 slice에서 감소했고 나머지는 동일했다.

[전체 조건·표본·중간 측정·hash·회귀 상태](round28-sequence-slice.json). 최종 raw 출력은 `.artifacts/round28-completed-tests.json`이다.

## 남은 범위

int를 넘는 step 변환은 `slice_large_step.py`에서 여전히 실패한다. bytearray/list 삭제 및 bytearray 대입의 증가식, range 표현과 memoryview stride도 별도 검토가 필요하다. Unicode codepoint 처리, 전체 slice의 identity 재사용, callback 중 시퀀스 크기 변경까지 이번 수정으로 완료했다고 해석하지 않는다. 다음에는 쓰기·삭제 경로의 같은 overflow를 먼저 제거한 뒤 큰 step 변환을 확장한다.
