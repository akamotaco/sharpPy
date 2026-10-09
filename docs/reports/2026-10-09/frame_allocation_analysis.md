# 4차 함수 프레임 할당 분석과 개선

기준일: 2026-10-09. 기준 커밋은 `8089546`이며 4차 구현·검증 자료는 `f742c29`로 커밋했다.

## 결과와 변경

함수 호출 비용을 인자 배열, scope, frame, 직접 호출로 나눠 측정했다. 불필요한 빈 배열 생성은 `Array.Empty<T>()`로 바꾸고, legacy 예외 handler stack은 실제 사용 시 생성한다. Python 3.12 exception table 경로는 유지했다. `DebugWrite`와 `DebugWriteInternal`에는 `Conditional("DEBUG_LOG")`을 적용해 Release에서 메시지 문자열을 만드는 호출 자체를 제거했다.

살아 있는 frame, closure와 generator는 재사용하지 않는다. 비어 있지 않은 배열과 stack의 소유권도 유지한다. C#에서 공개된 `ExceptionHandlers` getter를 사용하면 이전처럼 변경 가능한 독립 stack을 얻는다.

## 할당 분해

동일 thread의 할당량을 1,000회 측정했다. 준비용 배열은 측정 밖에서 생성했다. 아래 측정은 서로 포함 관계가 있어 합산할 수 없다. 프레임 측정에는 local scope 생성과 매개변수 binding이 포함된다.

| 측정 | 이전 B/회 | 이후 B/회 |
|---|---:|---:|
| 두 인자 배열 | 40 | 40 |
| 빈 local scope | 136 | 136 |
| 두 매개변수 local scope | 272 | 272 |
| global scope chain | 256 | 176 |
| 빈 함수 frame, 준비된 scope chain | 520 | 368 |
| 두 인자 함수 frame, 준비된 scope chain | 672 | 544 |
| host에서 빈 함수 직접 호출 | 880 | 672 |
| host에서 두 인자 함수 직접 호출 | 1,064 | 888 |

직접 호출 결과는 별도 Python 호출 workload와 경로가 다르므로 감소량도 다르다. 이는 누적 할당량이며 peak memory나 retained memory가 아니다.

## 반복 성능 측정

변경 전 바이너리를 별도 디렉터리에 보존했다. 변경 전후 순서를 번갈아 3개 독립 process씩 실행하고, 각 process에서 작업당 10회 warmup과 10개 sample을 수집했다. 모든 반환값을 검증했다. 표는 총 30개 sample의 중앙값이다. 작업 크기는 10,000이다.

| 작업 | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
|---|---:|---:|---:|---:|
| addition | 14.010 | 14.492 | 1,841,744 | 1,841,552 |
| inplace_addition | 16.709 | 16.004 | 1,841,760 | 1,841,552 |
| arithmetic | 14.839 | 14.873 | 2,401,744 | 2,401,552 |
| function_call | 14.962 | 14.705 | 12,002,960 | 10,082,760 |
| attributes | 13.896 | 14.050 | 2,254,736 | 2,254,224 |
| dictionary | 13.139 | 13.411 | 3,927,008 | 3,926,816 |
| comprehension | 3.933 | 4.004 | 1,464,896 | 1,464,696 |
| bound_builtin | 7.180 | 7.346 | 1,784,864 | 1,784,664 |

function_call의 할당량은 1,920,200B, 약 16.0% 감소했다. 시간의 사분위 범위는 이전 14.571–15.854ms, 이후 14.274–15.246ms로 겹치므로 속도 향상은 단정하지 않는다.

addition은 중앙값이 약 3.4% 증가했고 사분위 범위도 이전 13.917–14.224ms, 이후 14.251–14.615ms로 약간 분리됐다. 다른 작업에서도 작은 증가가 있어, 이 측정만으로 할당 감소가 전체 처리 시간 개선으로 이어진다고 주장하지 않는다. 더 큰 frame 변경에서는 이 작업을 비교 대상으로 유지한다.

## 검증과 다음 후보

Release와 Debug 빌드가 성공했다. CPython 3.12.15 비교 31개 항목은 출력 일치 25개, 기존 출력 차이 4개, native 테스트에서 실패 보고 없음 2개다. 새 실패는 없다. 출력 차이를 호환성 통과로 세지 않는다.

추가 Python 회귀 테스트는 `sys._getframe()`으로 얻은 frame의 독립성, 반환 후 참조 유지, generator 교차 실행, 빈 함수, exception table 처리를 검증한다. C# 테스트는 공개 legacy handler stack의 identity, push/pop과 frame 간 독립성을 검증한다. Debug 실행에서도 frame 회귀 테스트가 통과했다.

남은 local scope와 매개변수 dictionary 비용은 그대로다. 다음 후보는 fast locals와 dictionary의 중복 저장을 줄이는 구조다. CPython 3.12의 interpreter frame과 외부에 노출되는 frame 표현을 참고하되, locals 관찰과 변경, closure, class body, generator, C# 호출 동작을 먼저 검증해야 한다.

## 재현 자료

- [변경 전후 할당 분해](round4-allocations.json)
- [반복 성능 원자료](round4-performance.json)
- [전체 비교 테스트 결과](round4-tests.json)

기존 보고서의 환경에서 Release로 본체와 harness를 빌드한 뒤 실행한다.

```powershell
& "$taskDotnet\dotnet.exe" tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll --frame-allocations
& "$taskDotnet\dotnet.exe" tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll --frame-check
& $referencePython tools/evaluate.py --dotnet "$taskDotnet\dotnet.exe"
```
