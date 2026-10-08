# SharpPy

Python 3.12와 CPython의 실행 동작 및 바이트코드 호환성을 목표로 하는 C# 인터프리터다. 기본 라이브러리는 동작하는 수준으로 구현되어 있으며, 현재 작업의 중심은 검증 가능한 호환성 평가와 성능 개선이다. 완전한 호환성이나 성능 수준은 아직 이번 환경에서 측정하지 않았다.

Python 소스를 토큰화하고 PEG 파서로 AST를 생성한 뒤, 컴파일러·CFG·어셈블러를 거쳐 자체 VM에서 실행한다. 일반 .NET 실행 및 Godot 내장 실행을 지향한다.

- [평가 및 성능 개선 계획](docs/evaluation_and_performance_plan.md)
- [문서 보관 안내](docs/archive/2026-10-09/INDEX.md)
- [기존 프로젝트 설명](docs/archive/2026-10-09/readme.md)
- [라이선스](LICENSE)

## 현재 테스트 환경

프로젝트의 대상 프레임워크는 `net8.0`이다. 2026-10-09 확인한 이 PC의 환경은 다음과 같다.

| 도구 | 경로 | 버전 |
|---|---|---|
| .NET SDK | `C:\Program Files\dotnet\dotnet.exe` | 10.0.401 |
| 비교용 CPython | `C:\Users\AMD-MAX395-128G-2T\miniforge3\envs\sharppy-test-py312\python.exe` | 3.12.15 |

SDK 설치 확인과 Python 간단 실행을 완료했다. .NET 8 런타임 유무, SharpPy 빌드 및 전체 테스트는 아직 확인하지 않았다. 실행 경로는 다른 PC에서 재탐색해야 한다.

PowerShell에서 프로젝트 루트를 기준으로 실행한다.

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build -c Release
& 'C:\Program Files\dotnet\dotnet.exe' 'bin\Release\net8.0\sharppy.dll' 'examples\hello.py'
& 'C:\Users\AMD-MAX395-128G-2T\miniforge3\envs\sharppy-test-py312\python.exe' 'examples\hello.py'
```

위 명령은 실행 안내이며 이 문서 작성 중 빌드를 수행하지 않았다. 빌드 시 토큰 및 파서 생성 도구가 실행되므로, PATH에 `C:\Program Files\dotnet`이 있어야 한다. 성능 측정은 빌드를 완료한 산출물로 수행한다.
