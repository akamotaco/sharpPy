# 27차: 확장 list slice 대입의 계산과 복사

26차 bytes 비교는 `0a52dc0`으로 커밋했다. 큰 step/stride 조사 중 확인한 공통 길이 계산과 list 확장 slice 대입 오류를 먼저 수정했다. 이번 변경으로 모든 큰 step을 지원하게 된 것은 아니다.

## 확인한 문제와 수정

`PySlice.GetLength`의 양수 계산은 `stop - start + step - 1`에서 넘칠 수 있고, 음수 계산은 `-int.MinValue`에서 넘쳤다. 정규화한 start/stop/step을 받아 차이를 먼저 계산하고, 음수 분모는 long으로 처리하는 길이 함수를 추가했다. 기존 GetLength, GetIndexAt, GetIndices도 이미 계산한 정규화 결과를 재사용한다. GetIndices는 생성할 원소 수로 반복 횟수를 제한한다. 마지막 위치 증가가 넘쳐도 그 값을 다시 읽거나 반복 조건으로 쓰지 않는다.

list 확장 대입은 Indices 다음 GetIndices/GetLength를 호출해 사용자 `__index__`를 반복 실행했다. 이 재계산과 임시 인덱스 배열을 제거하고, 한 번 정규화한 경계 및 계산한 원소 수로 대입한다.

또한 `valueList.Items`는 전체 원소 배열의 복사본을 반환하는데 기존 대입 루프는 이를 원소마다 호출했다. 별도 list는 내부 저장소를 직접 읽는다. 자기 대입은 쓰기 전에 한 번 snapshot을 만든다. 따라서 `items[::-1] = items`도 원본을 중간에 덮어쓰지 않는다. 대입 루프 안에서 Python 코드를 호출하지 않는다.

근거는 고정 CPython 3.12.15 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 [PySlice_AdjustIndices](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/sliceobject.c#L269), [list_ass_subscript](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/listobject.c#L2956)다. 후자는 자기 대입을 먼저 복사하고, 길이를 확인한 뒤 원소 수만큼 대입한다. C의 refcount/garbage 배열을 CLR에 그대로 옮기지는 않았다.

## 검증

신규 `slice_assignment_indices_regression.py`는 int.MaxValue/int.MinValue 및 ±2**30 step, 빈 결과, 양방향 대입, `__index__` 호출 횟수와 step/start/stop 순서, 길이 불일치의 예외·대상 불변, 자기 자신을 역순 대입하는 경우를 검사한다. 기준 DLL은 실패했고, 최종 DLL은 실제 CPython 3.12.15와 일치했다.

Release 본체/harness 빌드 성공. 전체 64개: raw 일치 59, 지정 시간·주소 필드만 다른 3, native `no_reported_failure` 2. 새 실패 없음.

별도 compatibility 검사에서는 `slice_large_step.py`와 신규 `slice_step_iteration_overflow.py` 모두 CPython 성공/SharpPy 실패다. 전자는 int 범위를 넘는 step 변환, 후자는 `b'abcdef'[1::2147483647]`처럼 step 자체는 int지만 마지막 증가식이 넘치는 읽기 경로를 재현한다. 두 실패를 기본 성공 수에 포함하지 않는다.

## 성능

별도 list를 step=2인 확장 slice에 대입한다. 데이터와 slice는 측정 전에 만들고 원소 결과를 검사한다. 같은 harness로 기준/최종 DLL을 바꿔 process 3쌍, 각 10 warmup/10 sample을 순차 실행했다. 빌드·회귀 검사와 병행하지 않았다.

| 대상 크기 / 대입 횟수 | 기준 중앙값 ms (Q1–Q3) | 최종 중앙값 ms (Q1–Q3) | 기준 → 최종 할당 B |
| --- | ---: | ---: | ---: |
| 16 / 10,000 | 3.4960 (3.4071–3.6429) | 1.1282 (1.1112–1.2929) | 8,480,000 → 0 |
| 1,024 / 100 | 5.4879 (4.9639–5.7972) | 0.35985 (0.3528–0.4025) | 211,563,200 → 0 |

중앙값은 각각 약 67.7%, 93.4% 감소했고 사분위 범위가 겹치지 않았다. 임시 인덱스 배열과 원소마다 일어나던 전체 복사를 제거한 효과다. 이는 native SetItem 호출 측정이며 Python VM 전체의 개선율이 아니다. 자기 대입 snapshot과 iterable의 list 변환에는 여전히 할당이 필요하다.

[표본·hash·회귀 및 미해결 재현 결과](round27-list-slice.json). 전체 raw 출력은 `.artifacts/round27-tests.json`에 있다.

## 다음 범위

다른 시퀀스의 읽기/삭제 루프를 원소 수 기준으로 검토한 뒤, 큰 step 변환과 memoryview stride 표현을 함께 다룬다. step만 임의로 잘라 전체 지원으로 간주하지 않는다. range의 정수 표현, iterator와 slice 콜백 사이의 재진입, Python 참조 계수에 따른 소멸 시점까지 이번 수정으로 해결한 것은 아니다. 기존 memoryview 자동 해제와 module namespace 통합 과제도 남아 있다.
