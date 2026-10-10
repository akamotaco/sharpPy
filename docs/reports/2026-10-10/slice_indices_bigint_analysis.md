# 42차: 임의 정밀도 slice 정규화

41차는 `c22cf46`로 커밋했다. range의 큰 step을 다루기 위한 첫 단계로 CPython의 임의 정밀도 정규화를 구현하고, 누락되어 있던 Python `slice.indices()`에 연결했다. **range의 임의 정밀도 저장 표현은 아직 구현하지 않았다.**

## CPython 비교와 구현 범위

고정 CPython 3.12.15 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Objects/sliceobject.c`에서 `_PySlice_GetLongIndices`, `slice_indices`를 대조했다. CPython range의 `compute_slice`가 사용하는 정규화는 native 저장소용 `PySlice_Unpack`과 달리 step을 제한하지 않는다.

`PySlice.ExactIndices(BigInteger)`는 길이와 start/stop/step을 임의 정밀도로 처리한다. 길이를 먼저 검증하고 step, start, stop 순으로 변환한다. 음수 길이와 0 step은 이후 callback보다 먼저 오류를 낸다. 음수 step에서 None stop과 명시적 -1도 구분한다. 임의 정밀도 산술은 이미 사용하는 .NET `System.Numerics.BigInteger`를 사용하며 의존 DLL을 추가하지 않았다.

`slice` 타입 초기화에서 기존 `PyMethodDescriptor`로 `indices`를 등록했다. 첫 instance 생성 전 클래스 조회, bound/unbound 호출, receiver 검사, positional-only 인자와 오류 메시지를 검증했다. 인자 없이 unbound 메서드를 호출할 때의 기존 공통 descriptor 오류 문구도 실제 CPython의 slice.indices/list.append/str.upper와 맞췄다. 메서드별 예외 처리 코드를 추가하지 않았다.

기존 `StorageIndices`, `ViewIndices`, int-sized C# `Indices`의 알고리즘과 range 저장 필드는 그대로다. 새 경로를 범용 배열 접근에 넣지 않았으며, 다음 range 표현 변경에서 재사용할 수 있도록 내부 메서드로 분리했다.

## 검증

`tests/slice_indices_bigint_regression.py`는 길이 0, 6, 2**63, 10**100과 None/음수/양수 경계·step의 500개 조합을 검사한다. 같은 fixture를 실제 CPython 3.12.15와 SharpPy에서 실행했다. 독립 Python 계산식의 결과도 CPython에서 검증된다.

추가로 bool, __index__ 호출 순서, callback 예외, 잘못된 반환 타입, 음수 길이 우선 검증, 0 step, 메서드 인자 수·keyword·receiver 오류의 출력과 메시지를 비교했다. 기준 DLL은 `indices` 속성이 없어 실패했고, 최종 DLL의 전체 출력은 CPython과 일치한다. 최종 fixture는 최초 기준 실행 이후 클래스 조회와 오류 사례를 보강했다.

- Release 빌드 성공. 기본 평가 87개: raw 출력 일치 82개, 지정 변동 필드만 다른 3개, native no_reported_failure 2개. 전체 Python 호환율을 의미하지 않는다.
- Godot 4.7.2/.NET 8.0.31에 SharpPy 소스를 직접 포함하여 빌드·실행, 회귀 15개 통과. SharpPy DLL/ProjectReference 없이 GODOT 심볼을 사용하고 기존 DEBUG instruction cap은 제외했다.
- `tests/compatibility/range_large_step.py`는 여전히 SharpPy에서 실패하는 것을 재확인했다. 기본 통과 집합에 추가하지 않았다.

## 기존 경로의 성능 확인

동일 harness로 변경 전/후 프로세스 3쌍, 각각 10개 측정, DOTNET_TieredCompilation=0으로 비교했다. Range probe는 10 warmup, sequence slice probe는 100 warmup이며 Python VM 비용은 제외한다. 새 slice.indices 메서드는 기존 구현이 없으므로 속도 개선 비교 대상이 아니다.

Range 24개 및 bytes/bytearray/str/list/tuple slice 30개 조건에서 전후 할당량이 모두 동일했다. 대표 조건은 다음과 같다. 전체 표본은 JSON에 보관했다.

| 조건 | 기준 중앙값 ms (Q1–Q3) | 변경 중앙값 ms (Q1–Q3) | 전후 할당 B |
| --- | ---: | ---: | ---: |
| range 1024개, step 1, 100회 순회 | 0.83100 (0.8265–0.9467) | 0.84025 (0.8303–0.8621) | 4,101,600 → 동일 |
| list 1024개, step 1, 100회 slice | 0.43900 (0.4208–0.4518) | 0.44580 (0.4217–0.4530) | 1,652,800 → 동일 |

대표 시간 범위는 겹친다. 기존 경로를 빠르게 만든 변경이 아니므로 속도 향상은 주장하지 않는다. 새 메서드의 결과 tuple/정수 할당을 최적화했다는 주장도 하지 않는다.

## 다음 작업의 구체적 범위

range는 Start/Stop/Step이 모두 long이고, 생성자와 Python 인수 전달도 long 변환을 전제로 한다. 정규화 결과만 BigInteger로 바꾸어서는 큰 step의 repr/중첩 slice/반복 결과를 보존할 수 없다. 모든 range 필드를 무조건 BigInteger로 바꾸면 일반 객체 크기와 산술 비용에 영향을 주므로, 일반 long 경로와 큰 정수 경로를 나누는 표현을 먼저 측정해야 한다.

다음 변경에는 생성·slice 결과 저장·repr·index·길이·반복의 연결 검증이 필요하다. `interop/CSharpPythonInterop.cs`와 `Parser/ast.cs`에는 Start/Stop/Step을 직접 읽는 별도 for 루프가 있어 음수 step 및 overflow 검증도 함께 필요하다. 이 두 경로는 41차 PyRangeIterator/GetValues 수정만으로 해결되지 않는다. Python len의 int 한계 및 range equality/ordering도 남은 과제다.

실행 출력, 소스/DLL 해시, Godot 로그, 성능 표본은 [round42-slice-indices.json](round42-slice-indices.json)에 기록했다. 원본 로컬 로그는 `.artifacts/round42-*`다.
