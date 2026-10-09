# 31차: range slice 경계와 callback 중복 수정

30차 삭제 최적화와 Godot 소스 통합 기준은 `cd6a17e`로 커밋했다. 큰 step 경로를 조사하다 발견한 range의 경계 계산과 중복 callback을 이번 범위로 수정했다.

`range(1, 11)[::3]`의 요소는 기존에도 맞았지만 repr은 `range(1, 13, 3)`이었다. CPython은 `range(1, 11, 3)`을 반환한다. 결과 stop은 마지막 원소에 step을 더한 값이 아니라 정규화한 slice stop을 원래 range 좌표로 변환한 값이다. 빈 slice에서도 이 차이는 관찰된다.

고정 CPython 3.12.15 커밋의 [compute_slice](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/rangeobject.c#L396)를 대조했다. SharpPy도 start/stop을 각각 원래 range에 매핑하도록 바꿨다. 기존 `slice.GetLength(length)` 재호출을 제거하여 `__index__`를 step/start/stop 순서로 한 번씩만 실행한다. step=0 검사는 최초 정규화에서 처리하므로 중복 검사도 제거했다. 새 의존성이나 별도 보정 객체는 추가하지 않았다.

## 검증

- 새 `range_slice_indices_regression.py`: 양·음수 원래 range와 slice, 빈 결과의 repr, callback 순서·횟수, int 경계 step, step=0의 예외 및 후속 callback 미실행을 검사한다.
- 변경 전 DLL은 새 테스트에서 실제 AssertionError로 실패했고 CPython은 통과했다. 변경 후 결과는 CPython과 일치한다.
- 일반 Release 빌드 성공. 기본 69개는 raw 일치 64, 지정 변동 필드만 다른 3, native no_reported_failure 2다.
- Godot 4.7.2/.NET 8.0.31에서 SharpPy 소스를 프로젝트에 직접 포함해 빌드하고 실행했다. 삭제 압축, memoryview 대입, bytes 비교, 새 range 회귀까지 4개 모두 PASS다. SharpPy DLL 참조나 ProjectReference는 사용하지 않았다. 30차와 동일하게 GODOT 심볼을 유지하고 기존 DEBUG 명령 수 제한을 제외했다.

[실행 결과·소스 hash](round31-range-slice.json)에 근거를 보존했다. 성능 수치는 새로 측정하지 않았으며 중복 정규화·callback·길이 계산의 제거만 확인했다.

## 다음 범위

`tests/compatibility/slice_large_step.py`의 10**100 step은 현재도 실패한다. PySlice의 step은 int, range 저장은 long, memoryview stride는 int이므로 하나의 int clipping 변경을 모든 소비자에 적용하면 range와 view의 관찰 가능한 정보가 달라진다. 저장된 시퀀스의 위치 정규화와 range/view의 결과 정보를 구분해 CPython과 비교해야 한다. range의 임의 정밀도, 기존 C# GetSlice 보조 API, 동등성·repr의 다른 차이는 이번 수정으로 해결했다고 주장하지 않는다.
