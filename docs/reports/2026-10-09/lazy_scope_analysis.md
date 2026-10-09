# 6차 최적화 함수 scope dictionary 지연 생성

기준일: 2026-10-09. 기준 바이너리는 `f742c29`에 5차 변경을 적용한 Release 결과다. 5·6차 변경은 `fcd610b`로 커밋했다. 기준/최종 DLL hash는 성능 원자료에 기록했다.

## 구조와 변경

CPython 3.12.15의 [ceval.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/ceval.c) `initialize_locals`는 매개변수를 localsplus에 바인딩한다. [bytecodes.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/bytecodes.c)의 일반 함수 호출 경로는 `CO_OPTIMIZED`일 때 초기 locals mapping을 NULL로 전달한다. 외부 조회 시 mapping을 구성하는 경로는 [5차 분석](locals_observation_analysis.md)에 기록했다. 소스 출처와 hash는 [manifest](round5-cpython-manifest.json)를 재사용했다.

SharpPy에서는 일반 함수도 local scope dictionary를 즉시 생성하고 모든 매개변수를 fast locals와 dictionary 양쪽에 저장했다. 함수 내부 opcode는 대부분 fast locals/cells만 사용하지만 C# scope 조회와 기본 eval/exec locals 준비에는 dictionary를 읽는 경로가 있었다.

`CO_OPTIMIZED` 함수에만 `PyFastLocalScope`를 사용한다. 매개변수는 fast locals에 바인딩하고 scope 복사는 생략한다. C#의 공개 `Variables` getter를 읽으면 dictionary를 지연 생성하고 fast locals/cells의 현재 값으로 갱신한다. getter가 반환하는 dictionary identity는 유지하며 사용자 추가 키는 보존한다. module/class 및 일반 PyScope의 생성 동작은 유지한다.

Python `locals()`·`f_locals`의 lazy dictionary와 C# scope dictionary는 각각 필요할 때만 생성된다. 둘 다 관찰하면 두 표현이 존재한다. scope와 frame 자체를 없애거나 CPython interpreter frame 레이아웃을 완전히 복제한 것은 아니다. C# scope가 frame을 참조하므로 해당 scope를 외부에서 보관하면 frame도 함께 살아 있다. 객체 풀링은 적용하지 않았다.

기본 `eval()`·`exec()`는 최적화 함수의 5차 locals 갱신 경로를 사용하도록 수정했다. `exec()`가 추가한 이름이 이후 `locals()`에서 누락되던 기존 차이도 해결했다. 명시적으로 전달한 globals/locals 경로는 유지했다.

## 회귀 테스트

- [동적 locals](../../../tests/dynamic_locals_regression.py): 최신 매개변수·신규 지역 변수 eval, exec 추가 이름, 같은 dictionary의 갱신, exec에서 매개변수 이름을 바꿔도 fast local은 그대로인 동작, 빈 함수 exec와 전역 오염 방지, closure eval. 기준 SharpPy는 실패하고 CPython은 통과했다. 최종 SharpPy는 CPython과 일치했다.
- [namespace locals](../../../tests/namespace_locals_regression.py): module/class 기본 조회, Python 3.12 함수 내부 inline comprehension의 지역 변수 조회와 반복 변수 복원. 기준과 최종 모두 CPython과 일치한다.
- C# `--frame-check`: 공개 scope의 매개변수 조회, fast locals 변경 후 갱신, dictionary identity, 삭제, 추가 키 보존을 추가 검증했다.

최종 Release 비교 35개 항목은 출력 일치 29개, 기존 출력 차이 4개, native 실패 보고 없음 2개다. 기존 차이를 호환성 통과로 계산하지 않는다.

Debug는 로그가 있으므로 출력 전체 일치 대신 종료 코드와 회귀 assertion으로 검증한다. Debug 본체와 harness를 새로 빌드하여 C# frame checks 및 dynamic locals, namespace locals, locals observation, function creation 회귀를 실행했다. harness 참조 경로도 빌드 Configuration에 맞춰 Debug 검증에 Release DLL이 섞이지 않도록 수정했다.

class/module mapping의 전체 identity·mutation 규칙, module/class comprehension의 hidden locals, tracing writeback은 이 기본 회귀만으로 검증 완료를 주장하지 않는다.

## 할당 분해

동일 thread의 할당량을 각 1,000회 측정했다. 준비된 scope chain의 frame 측정은 local scope 생성과 binding을 포함한다. 서로 겹치는 측정이므로 합산하지 않는다.

| 경로 | 이전 B/회 | 이후 B/회 |
|---|---:|---:|
| 빈 함수 frame, 준비된 scope chain | 376 | 304 |
| 두 인자 frame, 준비된 scope chain | 552 | 344 |
| host 빈 함수 호출 | 680 | 608 |
| host 두 인자 호출 | 896 | 688 |

일반 local scope probe, 인자 배열, global scope chain의 비용은 그대로다. 이번 probe는 scope dictionary를 관찰하지 않는 호출의 비용이며 관찰 경로와 최대/보유 메모리를 측정하지 않는다.

## 반복 성능 측정

같은 8개 작업, 크기 10,000, process별 10회 warmup과 10개 sample. 기준/최종 실행 순서를 번갈아 각각 3개 독립 process에서 수행했다. 모든 반환값을 검증했다. 아래는 총 30개 sample의 중앙값이다.

| 작업 | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
|---|---:|---:|---:|---:|
| addition | 14.006 | 14.925 | 1,841,560 | 1,841,352 |
| inplace_addition | 16.391 | 17.075 | 1,841,560 | 1,841,352 |
| arithmetic | 14.272 | 14.946 | 2,401,560 | 2,401,352 |
| function_call | 14.384 | 13.544 | 10,162,768 | 8,082,560 |
| attributes | 13.977 | 13.855 | 2,254,248 | 2,254,048 |
| dictionary | 13.428 | 13.199 | 3,926,824 | 3,926,616 |
| comprehension | 3.926 | 3.980 | 1,464,704 | 1,464,496 |
| bound_builtin | 7.218 | 7.258 | 1,784,672 | 1,784,464 |

function_call의 할당량은 2,080,208B, 약 20.5% 감소했다. 두 인자 호출 10,000회에서 호출당 208B 감소한 분해 측정과 부합한다. 시간 중앙값은 약 5.8% 감소했지만 사분위 범위가 이전 13.899–14.790ms, 이후 12.849–15.655ms로 겹쳐 속도 향상을 확정하지 않는다. addition 등에서는 중앙값 증가도 관찰됐고 사분위 범위가 겹쳤다. 채택 근거는 재현된 할당 감소와 회귀 결과이며 모든 작업의 실행 속도 개선을 주장하지 않는다.

원자료: [성능](round6-performance.json), [할당](round6-allocations.json), [변경 전 새 회귀](round6-regressions-before.json), [최종 전체 비교](round6-tests.json).

## 다음 후보

관찰하는 호출의 비용을 별도 workload로 추가하고 class/module mapping의 identity·mutation 및 hidden locals 최소 사례를 늘린다. frame과 scope chain 자체의 생성 비용은 남아 있다. 다음 구조 변경은 외부 참조 수명과 generator/closure 독립성을 같은 회귀 파일로 확인한다.
