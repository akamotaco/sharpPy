# 1차 구현 평가와 성능 개선 결과

기준일: 2026-10-09. 기준 커밋: `55171f5`. 이 보고서의 개선은 아직 커밋하지 않은 작업 폴더 변경이다.

## 결과

Release 실행에서도 수행되던 디버그 스택 복사·문자열 생성을 제거했다. 이항 연산 fallback의 캡처 람다도 제거하고, in-place fallback을 역방향 연산까지 처리하는 일반 연산 경로로 연결했다.

같은 입력 10,000개를 처리하는 list comprehension에서 실행 시간 중앙값은 9.63ms → 4.40ms, 호출당 스레드 할당량은 12.35MB → 4.35MB로 감소했다. 이 결과는 한 종류의 작업에 대한 측정이며 전체 프로그램이나 모든 Python 코드의 속도 개선율이 아니다.

## 환경과 범위

- CPU: AMD RYZEN AI MAX+ 395, 16코어 / 32 논리 프로세서, Windows x64.
- 프로젝트: `net8.0`, Release, 측정 런타임 .NET 8.0.31.
- 사용자 전용 SDK 8.0.425를 `%LOCALAPPDATA%\SharpPy\dotnet`에 설치했다. 기존 SDK 10.0.401은 유지했다.
- CPython: 전용 conda 환경 `sharppy-test-py312`, 3.12.15.
- 측정: 같은 Python 함수·입력·예상값, 프로세스별 워밍업 10회 + 표본 10개, 단계별 독립 프로세스 3개.
- 표의 시간은 프로세스별 중앙값 3개의 중앙값이다. 원시 표본과 사분위 범위는 [performance.json](performance.json)에 보관했다.
- C# 호스트에서 컴파일된 Python 함수를 반복 호출한다. 호스트 호출 비용은 포함하고 프로세스 시작·소스 처리 비용은 제외한다. 초기 소스 실행 시간은 따로 기록하지만 파이프라인 단계별 측정은 아직 하지 않았다.
- 할당량은 `GC.GetAllocatedBytesForCurrentThread()`의 차이다. 최대 메모리나 살아 있는 객체량이 아니다. GC 세대별 횟수도 원시 결과에 포함했다.

기존 벤치마크의 낮은 반복량은 Debug의 명령 수 제한을 고려한 것이었다. 해당 제한은 `#if DEBUG` 안에 있고 Release에는 적용되지 않아, 두 엔진에 같은 반복량을 사용할 수 있었다.

## 기존 구현 평가

기존 파일·예제 23개, 새 이항 연산 회귀 테스트 1개, C# 호출 테스트 2개를 실행했다.

| 결과 | 수 |
|---|---|
| CPython과 표준 출력·표준 오류가 그대로 일치 | 20 |
| 출력 차이 존재 | 4 |
| C# 호출 테스트에서 실패 보고 없음 | 2 |

이 수치는 파일 단위 관찰 결과이며 언어 기능 통과율이 아니다. 출력만 확인하는 테스트와 실제 assertion 테스트가 섞여 있다. C# 테스트도 전용 테스트 프레임워크의 독립 assertion 집계가 아니라 실행 결과와 실패 보고를 확인했다.

출력 차이는 다음과 같다.

- `level7_comprehensions.py`: set의 표시 순서 차이. 이 출력만으로 의미 불일치를 판정하지 않는다.
- `examples/advanced.py`: 실제 실행 시간 출력 차이.
- `test_python312_missing_features.py`: bound method에서 함수의 사용자 속성을 찾지 못하며, memoryview 쓰기가 원본 bytearray에 반영되지 않는다. 메모리 주소 출력도 다르다.
- `test_ultimate_complex_features.py`: 실행 시간·주소와 enum 관련 Release 진단 출력 등이 섞인다. 사례별 의미 비교가 더 필요하다.

초기 평가 도구는 모든 ❌ 출력 줄을 실패로 분류해 정상적인 “invalid item skipped” 로그를 오판했다. 최종 도구에서는 명시적 실패 집계와 해당 테스트의 실패 출력 형태로 범위를 좁혔다. 여전히 범용 assertion 프레임워크가 아니므로 새 테스트 형식을 추가할 때 집계 규칙을 검토해야 한다.

`examples/demo.py`는 CPython에서도 30초간 실행하는 예제여서 초기 20초 제한에 걸렸다. 테스트 실패로 해석하지 않고 기본 자동 목록에서 제외했다. 별도의 짧은 사례로 실행 파일 옆 모듈 import 문제를 재현했다.

최종 집계는 [test-summary.json](test-summary.json), 원시 stdout/stderr와 타임아웃 정보는 로컬 `.artifacts/evaluation/final.json`에 있다.

## 기존 호환성 문제의 최소 사례

이 항목들은 이번 성능 개선 이전부터 존재하는 문제이며 이번에 고치지 않았다. `tests/compatibility`는 일반 회귀 목록과 별도로 실행하는 알려진 문제 재현 사례다. CPython에서는 세 사례 모두 통과하고 SharpPy에서는 모두 실패한다.

| 사례 | 관찰한 차이 |
|---|---|
| `memoryview_write.py` | view에 쓰더라도 원본 bytearray가 바뀌지 않음 |
| `bound_method_attributes.py` | 함수의 `__override__`가 bound method에서 조회되지 않음 |
| `script_import/entry.py` | 프로젝트 루트에서 실행할 때 실행 파일 옆 `sibling.py`를 찾지 못함 |

`typing.override`를 독립 함수에 적용한 사례는 실제로 통과했다. 종합 테스트의 표시 속성 차이는 decorator 자체가 아니라 bound method의 함수 속성 전달 문제로 좁혔다.

## CPython 참조와 variation

공식 태그를 실제 커밋으로 해석하고 각 버전의 핵심 파일 9개를 다운로드했다. 출처 URL·커밋·SHA-256은 [참조 manifest](cpython-reference-manifest.json)에 기록했다. 소스 자체는 `.artifacts/cpython`에 있고 저장소에 추가하지 않았다.

| 태그 | 커밋 |
|---|---|
| v3.12.0 | `0fb18b02c8ad56299d6a2910be0bab8ad601ef24` |
| v3.12.15 | `e848e4b09ca4bc67c0041c9ce7526dae471d4c36` |

로컬 `Grammar/python.gram`은 줄바꿈을 정규화하면 v3.12.15와 일치하며 v3.12.0과는 다르다. 로컬 `Grammar/Python.asdl`은 두 버전 모두와 일치한다. 비교한 공식 `Lib/opcode.py`와 `Objects/abstract.c`도 두 태그 사이에서 동일하다.

따라서 보관된 문법은 3.12.0 이후 변경을 포함한다고 판단할 수 있다. 그러나 여러 패치 버전이 같은 파일을 공유할 수 있으므로, 초기 프로젝트 전체의 참조 버전을 3.12.15로 확정하지 않는다. 실제 사용되는 `python_cs.gram`과 변환 도구 및 VM 전체의 variation 조사는 남아 있다.

in-place fallback은 두 태그의 `Objects/abstract.c::binary_iop1`을 확인했다. in-place slot이 없거나 NotImplemented를 반환하면 일반 `binary_op1`으로 넘어간다. 이 근거로 SharpPy에서도 단순 `left.Add` 등의 호출이 아니라 역방향 연산을 포함하는 일반 dispatch로 연결했다.

## 변경과 측정 근거

### 디버그 준비 작업

`MAKE_FUNCTION`, `FOR_ITER`, `LIST_APPEND`, `WITH_EXCEPT_START`에서 출력문만 조건부 컴파일되어 있었다. 스택 복사·배열 할당·문자열 생성 또는 순회는 Release에서도 실행됐다. 준비 작업 전체를 해당 출력과 같은 `DEBUG_LOG` / `DEBUG_VM_LOG` 조건 안에 넣었다.

클로저만 제거한 중간 버전의 CPU sampling에서 `ToArray()` 항목들이 exclusive sample 기준 약 29.9%와 4.8%를 차지했다. 최종 프로파일에서는 이 항목들이 상위 15개에서 사라졌다. 프로파일은 setup·워밍업·모든 작은 작업을 포함하므로 비율을 실제 애플리케이션의 전체 CPU 비중으로 해석하지 않는다.

### 이항 연산 fallback

in-place switch의 캡처 람다가 같은 메서드의 일반 연산에도 display class 할당을 발생시켰다. fallback을 enum 인자로 바꾼 중간 버전에서 10,000회 덧셈의 할당량이 정확히 320,000바이트 감소했다. 산술 작업에서는 1,280,000바이트 감소했다. 실행 시간 차이는 작으므로 이 변경의 주된 확실한 효과는 할당량 감소다.

일반 연산 dispatch를 사용하면서 `2 += Reflected()` 형태의 `__radd__` fallback도 정상 동작한다. 이전 구현은 이 사례에서 TypeError를 발생시켰다. 회귀 테스트는 12종 in-place 연산, 큰 정수, 음수 floor division, 0으로 나누기, 문자열·tuple fallback, list 별칭 유지, 사용자 `__iadd__`, NotImplemented 및 `__radd__`를 검증한다.

## 성능 결과

MB는 1,000,000바이트 기준이며 한 함수 호출에서 10,000개 입력을 처리할 때의 할당량이다.

| 작업 | 변경 전 ms | 변경 후 ms | 시간 감소 | 변경 전 MB | 변경 후 MB |
|---|---:|---:|---:|---:|---:|
| 덧셈 | 16.62 | 15.11 | 9.1% | 7.76 | 6.16 |
| in-place 덧셈 | 45.76 | 44.97 | 1.7% | 16.40 | 14.16 |
| 혼합 산술 | 17.26 | 16.70 | 3.2% | 16.48 | 13.92 |
| 함수 호출 | 30.21 | 28.67 | 5.1% | 22.72 | 21.12 |
| 속성 읽기·쓰기 | 15.83 | 15.12 | 4.5% | 15.38 | 13.78 |
| dict 저장·조회 | 17.40 | 16.53 | 5.0% | 17.77 | 14.89 |
| list comprehension | 9.63 | 4.40 | 54.3% | 12.35 | 4.35 |

작은 시간 차이는 실행 순서·전원·열 상태에 따른 변동의 영향을 받을 수 있다. 특히 in-place 덧셈의 1.7%를 확정적 성능 향상으로 주장하지 않는다. 전원 모드와 열 상태를 자동 기록하지 못했으므로 다음 측정에서는 통제를 강화한다. 확실한 결과는 전 작업의 할당량 감소와 comprehension의 큰 시간 감소다. 최종 재현 도구도 별도로 실행해 같은 예상값과 측정 수집을 확인했다.

CPython의 같은 작업은 약 0.17–0.76ms였다. 최종 SharpPy는 이 작은 작업들에서 대략 22–171배 느렸다. 이는 정상 상태 함수 호출이라는 제한된 측정의 비율이며 .NET 런타임 자체의 성능 비율이 아니다. 큰 개선 여지가 남아 있다.

## 검증

- .NET 8 Release 빌드 성공. 생성 도구의 기존 nullable warning 1개.
- 기존 평가 목록에서 새 실패 보고 없음. 출력 불일치 항목은 기존 현상으로 분류했다.
- 새 이항 연산 회귀 테스트의 CPython/SharpPy 출력 일치.
- 7개 성능 작업의 모든 워밍업·표본에서 정답 검증.
- `DEBUG_VM_LOG`를 켠 Debug 빌드 및 comprehension 실행 성공. 기본 `DEBUG_LOG` 조건의 빌드도 확인했다.
- CPU sampling은 시간 측정과 분리했다. `dotnet-trace` 8.0.547301 사용.
- 알려진 호환성 문제 3개는 별도 실패 보고서로 유지한다.

## 다음 후보

| 우선순위 | 후보 | 근거와 필요한 검증 |
|---|---|---|
| 1 | in-place 연산의 누락 속성 조회·예외 기반 fallback | 최종 CPU sampling에서 GenericGetAttribute가 exclusive 약 43.3%. `+=`가 일반 덧셈보다 약 3배 느림. 호출별 프로파일로 비중을 좁히고 built-in·서브클래스·descriptor 동작을 검증해야 함 |
| 2 | 일반 속성 및 메서드 호출 비용 | PyMethodDescriptor.Call과 문자열 StartsWith/EndsWith 비용이 관찰됨. 실제 호출자와 사용자 attribute override를 확인하고 캐시 무효화 설계 필요 |
| 3 | 실행 파일 디렉터리 import | 실제 예제 실행을 막는 짧고 재현 가능한 오류. sys.path의 CPython 동작과 내장 실행 영향을 대조 |
| 4 | memoryview 버퍼 공유, bound method 속성 전달 | 테스트가 성공 문구를 출력해도 실제 동작이 다른 사례. 각각 독립적으로 수정·회귀 검증 |
| 5 | 파이프라인 단계별 비용·실제 게임 작업 | 이번 측정은 VM 작은 작업 위주. 소스 처리 비용과 실제 내장 실행 작업의 우선순위는 아직 판단 불가 |

적응형 특수화 전체나 객체 풀링은 아직 우선 과제로 확정하지 않는다. 우선 불필요한 비용과 실패하는 실행 의미를 작은 변경으로 줄인 뒤 재측정한다.

## 재현 명령

프로젝트 루트의 PowerShell에서 실행한다. 경로는 다른 PC에서 재탐색해야 한다.

```powershell
$taskDotnet = Join-Path $env:LOCALAPPDATA 'SharpPy\dotnet'
$referencePython = Join-Path $env:USERPROFILE 'miniforge3\envs\sharppy-test-py312\python.exe'
$env:PATH = $taskDotnet + ';' + $env:PATH
$env:DOTNET_ROOT = $taskDotnet
$env:PYTHONUTF8 = '1'

& "$taskDotnet\dotnet.exe" build -c Release
& "$taskDotnet\dotnet.exe" build tools/PerformanceHarness/PerformanceHarness.csproj -c Release
& $referencePython tools/evaluate.py --dotnet "$taskDotnet\dotnet.exe" --output .artifacts/evaluation/current.json
& $referencePython tools/benchmark.py --dotnet "$taskDotnet\dotnet.exe" --output .artifacts/evaluation/performance-current.json
& $referencePython tools/fetch_cpython_reference.py

# 아래 세 사례는 현재 SharpPy에서 실패할 것으로 알려져 있다.
& $referencePython tools/evaluate.py --dotnet "$taskDotnet\dotnet.exe" --output .artifacts/evaluation/known-findings.json --files tests/compatibility/memoryview_write.py tests/compatibility/bound_method_attributes.py tests/compatibility/script_import/entry.py
```

프로파일 도구는 `.artifacts/dotnet-tools`에 로컬 설치했다.

```powershell
& "$taskDotnet\dotnet.exe" tool install dotnet-trace --tool-path .artifacts/dotnet-tools --version 8.0.547301
& .artifacts/dotnet-tools/dotnet-trace.exe collect --profile cpu-sampling --output .artifacts/evaluation/current.nettrace -- "$taskDotnet\dotnet.exe" tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll tools/PerformanceHarness/workloads.py 10000 10
& .artifacts/dotnet-tools/dotnet-trace.exe report .artifacts/evaluation/current.nettrace topN -n 20
```
