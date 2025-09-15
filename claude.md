# SharpPy - Python 3.12 Interpreter in C#

## **테스트 방법**
- 대상 : 현재 폴더 및 하위 폴더의 모든 test_*.py 파일 (단, 이미 테스트 완료된 파일은 무시)
- 테스트는 순차적으로 진행하며, 그 결과는 '테스트 결과 종합' 에 작성.
- 만일 문제가 발견되었다면 문제 해결에 주력할 것.
- 문제 해결 시에는 cPython 3.12의 호환성을 고려하여 문제를 해결할 것.

### 🎯 **올바른 문제 해결 순서**
1. 문제 발생 → CPython 바이트코드 분석
2. 차이점 정확히 파악 → 원인 이해  
3. 최적화를 비활성화하고 문제 해결
4. 최적화를 활성화하고 문제 해결 (단계적 해결)
5. **기존 테스트들 회귀 검증**
6. 문제 완전 해결 확인

## **심층 테스트 코드**
- test_ultimate_complex_features.py
- test_python312_advanced_features.py
- test_python312_missing_features

## 🔧 **개발 환경**
- **CPython 3.12**: `C:\Users\m11\miniforge3\envs\py312\python.exe`
- **SharpPy 빌드**: `dotnet build`
- **SharpPy 실행**: `dotnet run [filename]`
- **CPython 3.12 바이트코드 비교**: `export PYTHONUTF8=1 && "C:\Users\m11\miniforge3\envs\py312\python.exe" -m dis [filename]`
- **SharpPy 바이트코드 비교**: `dotnet run --dis [filename]`
- **utf-8 인코딩 설정** : `set PYTHONUTF8=1` 또는 `export PYTHONUTF8=1`
- **최적화 비사용** : `--no-optimize`
- **토큰 시각화** : ` --tokens`
- **세부 디버그 로그 트리거** : `<DefineConstants>DEBUG;TRACE;DEBUG_LOG</DefineConstants>`

## 🎯 **개발/테스트 원칙**
- **구현 정책** : 간단하고 쉬운 방법보다는 올바른 방법으로 해결하라
- **바이트코드 레벨 호환성**: CPython 3.12와 동일한 바이트코드
- **실용적 디버깅**: `python -m dis`로 즉시 정답 확인
- **표준 준수**: Python 3.12 언어 명세 완전 준수
- **SharpPy와 CPython 3.12의 비교** : CPython의 바이트 코드는 SharpPy 의 최적화 On 바이트 코드와 동일해야 하고, SharpPy의 최적화 On 결과와 SharPy의 최적화 Off 결과가 동일해야 한다.
- **추측성 변경 절대 금지** : 반드시 CPython 패턴 확인. 임시 해결보다는 근본 원인 분석 및 해결이 중요.
- **빌드의 성공 여부 확인** : dotent run 으로 빌드시 가장 첫 문장을 먼저 확인할 것. "Error: The build failed. Fix the build errors and run again."
- **exception table** : python 3.12 부터는 loop stack 은 사용하지 않음. exception table은 사용됨
- **최적화/비화적화 차이** : 최적화 비활성화시에는 byte offset, 최적화 활성화시에는 instruction offset을 사용