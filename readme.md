# SharpPy

Python 3.12와 CPython의 실행 동작 및 바이트코드 호환성을 목표로 하는 C# 인터프리터다. 기본 라이브러리는 동작하는 수준으로 구현되어 있으며, 현재 작업의 중심은 검증 가능한 호환성 평가와 성능 개선이다. 1차 테스트 평가와 작은 작업의 성능 측정을 완료했으며, 전체 호환성은 계속 검증한다.

Python 소스를 토큰화하고 PEG 파서로 AST를 생성한 뒤, 컴파일러·CFG·어셈블러를 거쳐 자체 VM에서 실행한다. 일반 .NET 실행 및 Godot 내장 실행을 지향한다.

- [평가 및 성능 개선 계획](docs/evaluation_and_performance_plan.md)
- [1차 분석·개선 결과](docs/reports/2026-10-09/analysis.md)
- [2차 구조 비교·할당 개선](docs/reports/2026-10-09/structural_analysis.md)
- [문서 보관 안내](docs/archive/2026-10-09/INDEX.md)
- [기존 프로젝트 설명](docs/archive/2026-10-09/readme.md)
- [라이선스](LICENSE)

## 현재 테스트 환경

프로젝트의 대상 프레임워크는 `net8.0`이다. 2026-10-09 확인한 이 PC의 환경은 다음과 같다.

| 도구 | 경로 | 버전 |
|---|---|---|
| 테스트용 .NET SDK | `%LOCALAPPDATA%\SharpPy\dotnet\dotnet.exe` | SDK 8.0.425 / 런타임 8.0.31 |
| 기존 .NET SDK | `C:\Program Files\dotnet\dotnet.exe` | 10.0.401 |
| 비교용 CPython | `C:\Users\AMD-MAX395-128G-2T\miniforge3\envs\sharppy-test-py312\python.exe` | 3.12.15 |

테스트용 .NET 8 환경에서 Release 빌드와 기존 테스트 평가, 첫 성능 개선을 수행했다. 알려진 호환성 문제와 측정 범위는 결과 보고서에 명시했다. 실행 경로는 다른 PC에서 재탐색해야 한다.

PowerShell에서 프로젝트 루트를 기준으로 실행한다.

```powershell
$taskDotnet = Join-Path $env:LOCALAPPDATA 'SharpPy\dotnet'
$env:PATH = $taskDotnet + ';' + $env:PATH
& "$taskDotnet\dotnet.exe" build -c Release
& "$taskDotnet\dotnet.exe" 'bin\Release\net8.0\sharppy.dll' 'examples\hello.py'
& 'C:\Users\AMD-MAX395-128G-2T\miniforge3\envs\sharppy-test-py312\python.exe' 'examples\hello.py'
```

빌드 시 토큰 및 파서 생성 도구가 실행되므로 테스트용 SDK 경로를 PATH에 추가한다. 성능 측정은 빌드를 완료한 산출물로 수행한다. 평가·벤치마크·CPython 참조 다운로드 명령은 결과 보고서에 있다.
