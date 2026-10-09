# 41차: range의 64비트 경계와 반복 종료

40차를 `ab45e7b`로 커밋했다. 임의 정밀도 step 확장 전 조사에서, 현재 지원하는 long 범위에서도 길이 계산이 넘치고 반복자가 종료하지 않는 문제가 발견되어 먼저 수정했다. 임의 정밀도 step 자체는 이번 변경에 포함하지 않는다.

## 기준과 구현

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`, `Objects/rangeobject.c`의 `get_len_of_range`와 `rangeiter_next`를 대조했다. CPython은 native 정수와 Python 정수 반복자를 구분한다. Windows C long 크기를 C# long에 그대로 대응시키지 않고, 길이 계산의 수학적 성질과 실제 Python 결과를 기준으로 삼았다.

- `LengthUnsigned`는 unsigned 차이와 나눗셈으로 정확한 원소 수를 구한다. signed-long 양끝 전체와 long.MinValue step도 처리한다. 원소 수의 최댓값은 ulong.MaxValue다.
- `PyRangeIterator`는 남은 원소 수로 종료한다. 마지막 값 다음 덧셈이 wrap되어도 그 값을 반환하지 않는다. 사용하지 않던 range 참조를 제거했다.
- `GetValues`는 다음 덧셈 전에 unsigned 남은 거리를 검사한다. 초기 countdown 구현은 C# yield 상태 객체를 8B 늘려서, 기존 크기를 유지하는 거리 비교로 바꿨다.
- long 인덱스 접근은 unsigned 길이를 사용하고 음수 인덱스를 안전하게 정규화한다. 유효 원소가 signed-long 양끝 안에 있다는 조건으로 중간 곱셈 wrap을 처리한다. bool/IsEmpty는 경계 비교만으로 계산한다.
- `LengthLong`은 long으로 나타낼 수 없는 길이를 명시적으로 거부한다. Python의 일반 `len`에 남아 있는 int 한계는 별도 과제다.

## 검증

`tests/range_native_boundary_regression.py`는 명시적 양/음수 overflow 사례, 반복 소진 후 재호출, 길이·bool·양/음수 인덱스·list/tuple, long.MaxValue보다 긴 range의 제한된 접근, 경계값 343개 조합을 검사한다. 잘못된 반복자가 있어도 테스트 자체가 무한 순회하지 않도록 next 호출 수를 제한했다. `bytes(range)`의 별도 열거 경로도 검사한다.

기준 DLL에서는 첫 경계 사례가 `iterator did not stay exhausted`로 실패했고 CPython은 통과했다. 최종 구현의 기본 평가 86개는 raw 출력 일치 81개, 지정 변동 필드만 다른 3개, native no_reported_failure 2개다. 마지막 두 항목을 CPython 출력 일치로 계산하지 않는다. Native harness의 `--range-boundary-check`는 `GetValues` 양/음수 경계 5개를 추가 검증한다.

Godot 4.7.2/.NET 8.0.31 프로젝트에 SharpPy 소스를 직접 포함하여 컴파일·빌드·실행했고 회귀 14개가 통과했다. 별도 SharpPy DLL/ProjectReference 없이, 기존과 같이 GODOT 심볼을 켜고 DEBUG instruction cap은 제외했다. 모바일 실기 검증이나 전체 라이브러리 검증을 주장하지 않는다.

## 성능과 할당

동일 native harness로 변경 전/후 프로세스 3쌍, 각 10 warmup/10표본, `DOTNET_TieredCompilation=0` 조건으로 측정했다. VM 실행과 StopIteration 생성 비용은 제외한다. 초기 후보 측정도 JSON에 보관했다.

| 작업 | 조건 | 기준 중앙값 ms (Q1–Q3) | 변경 중앙값 ms (Q1–Q3) | 전후 할당 B |
| --- | --- | ---: | ---: | ---: |
| iterator | 1024개 × 100회, step 1 | 0.88785 (0.8759–0.9176) | 0.87240 (0.8491–0.9118) | 4,102,400 → 4,101,600 |
| iterator | 1024개 × 100회, step -1 | 0.86485 (0.8613–0.8941) | 0.85265 (0.8420–0.8828) | 4,102,400 → 4,101,600 |
| GetValues | 1024개 × 100회, step 1 | 0.91995 (0.9003–0.9347) | 0.95225 (0.9122–0.9771) | 4,100,800 → 동일 |
| GetValues | 1024개 × 100회, step -1 | 0.94580 (0.9264–0.9674) | 0.97395 (0.9481–1.0165) | 4,100,800 → 동일 |
| LengthLong | 빈 양수 range, 10,000회 | 0.01010 (0.0101–0.0101) | 0.01230 (0.0121–0.0123) | 0 → 동일 |
| LengthLong | 1024개, step -1, 10,000회 | 0.01500 (0.0149–0.0151) | 0.01590 (0.0157–0.0160) | 0 → 동일 |

반복자당 할당 64→56B는 일관되게 감소했다. GetValues 할당은 기존과 같다. 순회 시간의 사분위 범위는 겹치며 전체 속도 향상으로 주장하지 않는다. 일부 짧은 길이 조회는 중앙값이 증가했다. 빈 양수 range는 호출당 약 0.22ns 차이이며 길이 overflow 검사와 코드 배치 영향을 분리하지 않았다. 정확성 수정에 비용이 전혀 없다는 결론도 내리지 않는다.

## 다음 작업과 한계

임의 정밀도 생성 인수·step·인덱스, Python len의 int 한계, slice 계산 overflow, range repr/equality/ordering은 아직 남아 있다. `tests/compatibility/range_large_step.py`는 여전히 미해결이며 기본 통과 집합에 넣지 않았다. 다음 단계는 일반 long 반복 경로를 보존하면서 큰 정수를 처리할 표현과 slice 정규화를 설계·검증하는 것이다. 현재 변경이 range 전체 CPython 호환성을 의미하지 않는다.

실행 결과·고정 소스와 DLL 해시·측정 표본은 [round41-range-native.json](round41-range-native.json)에 기록한다. 로컬 로그는 `.artifacts/round41-*`에 보관한다.
