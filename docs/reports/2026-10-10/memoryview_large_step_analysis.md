# 40차: memoryview의 큰 step과 native stride

39차는 `0ea352c`로 커밋했다. memoryview 읽기·대입에서 int 범위를 넘는 step이 실패하던 문제를 수정하고 strides 관찰을 지원했다.

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/memoryobject.c `init_slice`, Objects/sliceobject.c `PySlice_Unpack`/`PySlice_AdjustIndices`를 대조했다. step은 native 정수 범위로 제한하며 최소 음수는 -최대 양수로 조정한다. 중첩 slice의 stride 곱셈은 로컬 CPython에서 native 크기로 wrap되는 것도 확인했다.

PySlice.ViewIndices는 callback을 한 번씩 평가하고 native step을 보존한다. int-sized 저장소의 원소 수 계산만 int로 제한하며 stride 자체는 제한하지 않는다. PyMemoryView는 nint stride를 보관하고 실제 접근 위치를 native 크기로 계산한 뒤 배열 인덱스로 변환한다. nonempty view의 유효 위치는 실제 저장소 범위이며, empty view의 offset은 읽거나 nonempty 자식으로 바뀔 수 없으므로 별도의 넓은 offset 필드를 두지 않는다. sys.maxsize도 int 상수에서 native 정수 한계로 수정했다.

## 검증

새 회귀는 32비트 경계 초과, native 경계, ±10**100, 중첩 역순·양/음수 큰 step, stride wrap, 빈 view, scalar 쓰기, slice 대입, alias인 큰 stride source 대입, callback 순서, 해제 후 strides 오류를 검사한다. 기존 큰 slice step 최소 재현도 이제 통과하여 기본 평가에 포함했다. 기준 DLL은 두 회귀에 실패했고 최종 DLL은 실제 CPython과 일치한다.

Release 빌드 및 기본 85개 검사: raw 일치 80, 지정 변동 필드만 다른 3, native no_reported_failure 2. Godot 4.7.2/.NET 8.0.31 소스 직접 포함 빌드와 회귀 13개 통과. 별도 SharpPy DLL은 참조하지 않으며 기존 GODOT 심볼 및 DEBUG 제한 제외 조건이다.

## 할당과 성능

초기 구현은 view당 할당이 56→64B로 늘었다. readonly와 borrowed-export 횟수를 하나의 uint 상태에 보관하여 이 증가를 제거했다. 기존과 같은 31비트 양수 export 횟수를 표현하며 readonly 비트는 borrow 증가·감소에서 유지한다. export 검사와 해제 회귀도 다시 통과했다. 불필요한 checked 위치 변환 및 정규화 호출 비용을 줄였다.

동일 native probe, DOTNET_TieredCompilation=0, 프로세스 3쌍, 각각 10 warmup/10표본이다.

| 작업 | 작업 수 | 기준 중앙값 ms (Q1–Q3) | 최종 중앙값 ms (Q1–Q3) | 전후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| 생성 | 10,000 | 0.39350 (0.3136–0.4554) | 0.28940 (0.2278–0.4536) | 560,000 |
| slice | 10,000 | 0.14955 (0.1426–0.1519) | 0.14620 (0.1445–0.1497) | 560,000 |
| 읽기 | 100,000 | 0.82880 (0.8235–0.8389) | 0.84325 (0.8147–0.8864) | 0 |
| 쓰기 | 100,000 | 1.08210 (1.0750–1.0971) | 1.07605 (1.0492–1.0940) | 0 |

최종 할당은 모든 조건에서 동일하고 사분위 범위는 겹친다. 속도 향상이나 모든 workload의 무퇴행을 주장하지 않는다. [초기 후보와 최종 표본·소스 hash·회귀·Godot 로그](round40-memoryview-step.json)를 보존했다.

## 다음 범위

range는 step 자체를 임의 정밀도 정수로 보존해야 하므로 이번 native clipping을 적용하지 않았다. `tests/compatibility/range_large_step.py`에서 여전히 CPython 통과·SharpPy 실패를 확인했고 기본 85개에는 포함하지 않았다. 다음은 range의 정수 표현과 일반 작은 range 경로의 비용이다. memoryview의 다차원·format·전체 descriptor 지원 및 자동 해제 문제도 별도 범위다.
