# 26차: bytes 비교와 내용 해시 연결

25차 큰 slice 경계 수정은 `ef1d5aa`로 커밋했다. 이번에는 `b'abc'[:] == b'abc'`가 거짓이던 기존 실패를 해결했다.

## 원인과 CPython 근거

`PyBytes`에는 CLR `Equals`와 내용 기반 `GetHashCode`가 있었지만 Python `RichCompare`와 `ToHash`는 기본 객체 경로를 사용했다. 같은 바이트 내용이 다른 것으로 비교되고, 같은 내용으로 만든 dict 키도 조회에 실패했다.

[CPython 3.12.15 bytes_richcompare](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/bytesobject.c#L1545)를 기준으로 bytes끼리는 내용으로 동등성·부등성을, unsigned byte의 사전식 순서로 대소관계를 판단한다. bytes가 아닌 상대에는 NotImplemented를 반환한다. 구현은 기존 타입 저장소 위에서 `Span.SequenceEqual`/`SequenceCompareTo`를 사용하고 bool 캐시를 반환한다. 동등성의 길이·첫 바이트 불일치는 먼저 탈락시킨다. `ToHash`는 기존 내용 해시로 연결했다.

추가로 사용자 비교 메서드가 반환한 NotImplemented를 `PyInstance.RichCompare`에서 기본 비교로 바꿔버리던 처리를 제거했다. VM은 양쪽이 비교를 거부하면 EQ/NE를 객체 동일성으로 판단하고 순서 비교는 TypeError로 처리한다. 이는 [do_richcompare](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/object.c#L800)의 마지막 fallback에 해당한다. 사용자 메서드의 다른 반환값과 예외는 유지한다.

## 검증

실제 CPython 3.12.15와 신규 회귀를 비교했다. 빈 값, prefix, NUL, 0x7f/0x80/0xff 등 10개 값의 쌍에 대해 6개 연산을 bytes/bytes 및 bytes/bytearray로 검사했다. list/tuple 안의 비교, containment, dict 조회, set 중복 제거, 동등한 값의 hash 일치, 다른 타입, 사용자 반사 비교의 비-bool 반환값, NotImplemented 및 최종 TypeError 메시지도 확인했다.

기준 DLL은 신규 회귀에 실패했고 최종 DLL은 통과했다. 기존 `tests/compatibility/bytes_slice_equality.py`도 통과해 기본 검사에 편입했다. Release 본체와 harness 빌드 성공. 최종 전체 63개: raw 일치 58, 지정된 시간·주소 필드만 다른 3, native `no_reported_failure` 2. 새 실패 없음.

## 성능 측정과 제외한 변경

CLR Equals의 기존 scalar 루프를 span 비교로 교체하는 후보는 제외했다. 긴 동일 값/마지막 바이트 불일치는 빨라졌으나 첫 바이트·길이 불일치에서 비용 증가가 반복됐다. 인라인 조정 후에도 남아 기존 CLR 구현을 유지했다. 새 Python 비교 경로에만 span 비교를 사용한다.

동일 harness의 기준/최종 DLL로 process 3쌍을 순차 실행했다. 각 10 warmup/10 sample, 표본당 100,000회 비교다. 최종 Python RichCompare는 별도 process에서 측정했다. 빌드와 회귀 검사를 병행하지 않았다.

| 크기/입력 | CLR 기준 중앙값 ms | CLR 최종 중앙값 ms | 최종 Python RichCompare 중앙값 ms |
| --- | ---: | ---: | ---: |
| 32 / 동일 | 1.50725 | 1.49245 | 0.29830 |
| 32 / 첫 바이트 다름 | 0.18600 | 0.18720 | 0.23120 |
| 32 / 마지막 바이트 다름 | 1.57475 | 1.52475 | 0.29220 |
| 32 / 길이 다름 | 0.14350 | 0.14630 | 0.18580 |
| 1024 / 동일 | 42.44340 | 42.17380 | 0.90425 |
| 1024 / 첫 바이트 다름 | 0.15570 | 0.16155 | 0.22385 |
| 1024 / 마지막 바이트 다름 | 31.84950 | 31.88815 | 1.08575 |
| 1024 / 길이 다름 | 0.12390 | 0.12335 | 0.17950 |

모든 CLR 전후 사분위 범위가 겹쳤다. 세 경로 모두 측정 루프의 추가 할당은 0바이트다. **Python 비교의 기준 구현은 잘못된 결과를 냈으므로 전후 속도 개선율은 제시하지 않는다.** CLR와 Python 경로의 호출 비용도 다르며, 표는 Python VM 전체 성능 측정이 아니다.

[원시 표본·사분위·후보 결과·DLL hash·회귀](round26-bytes-comparison.json). 최종 raw 출력은 `.artifacts/round26-adopted-tests.json`에 있다.

## 남은 범위

기존 내용 해시를 연결한 것으로 CPython의 randomized hash 숫자나 bytes 경고 옵션까지 구현한 것은 아니다. 비교 dispatch 전체의 subtype 우선순위, 모든 내부 직접 RichCompare 호출, memoryview와의 비교는 이번 검증 범위 밖이다. 다음에는 남겨 둔 큰 slice step/stride 산술을 이어서 확인한다. memoryview 자동 해제와 module namespace 이중 저장소도 구조 과제로 남아 있다.
