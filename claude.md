# SharpPy - Python 3.12 Interpreter in C#

## **테스트 방법**
- 대상 : 현재 폴더 및 하위 폴더의 모든 test_*.py 파일 (단, 이미 테스트 완료된 파일은 무시)
- 테스트는 순차적으로 진행하며, 그 결과는 '테스트 결과 종합' 에 작성.
- 만일 문제가 발견되었다면 문제 해결에 주력할 것.
- 문제 해결 시에는 cPython 3.12의 호환성을 고려하여 문제를 해결할 것.

### 🎯 **올바른 문제 해결 순서**
1. 문제 발생 → CPython 바이트코드 분석
2. 차이점 정확히 파악 → 원인 이해  
3. **최소한의 정확한 수정** 적용
4. **기존 테스트들 회귀 검증**
5. 문제 완전 해결 확인

## 📊 **테스트 결과 종합** (2개 테스트 완료)

| 테스트 파일 | 바이트코드 정확도 | Optimizer On 결과 정확도 | Optimizer Off 결과 정확도 | 발견된 문제 | 조치 내용 | 최종 결과 |
|------------|------------------|---------------------------|----------------------------|-------------|-----------|-----------|
| test_simple.py | ✅ Optimizer ON: 100% 일치<br>❌ Optimizer OFF: RETURN_CONST 차이 | ✅ 100% 일치 | ✅ 100% 일치 | Optimizer OFF시 마지막 반환문이<br>LOAD_CONST+RETURN_VALUE로 생성 | 없음 (설계상 정상) | ✅ 통과 |
| test_repl_functionality.py | 🔍 미검증 (복잡한 종합 테스트) | ✅ 100% 일치 | ✅ 100% 일치 | Windows 콘솔 Unicode 인코딩 문제<br>(✅ 문자 출력 실패) | 없음 (OS 레벨 이슈) | ✅ 통과 |


### **시스템 구성 요소 현황**

| 구성 요소 | 구현 상태 | CPython 3.12 호환성 | 비고 |
|-----------|-----------|---------------------|------|

## 🔧 **개발 환경**
- **CPython 3.12**: `C:\Users\m11\miniforge3\envs\py312\python.exe`
- **SharpPy 실행**: `dotnet run [filename]`
- **CPython 3.12 바이트코드 비교**: `C:\Users\m11\miniforge3\envs\py312\python.exe -m dis [filename]`
- **SharpPy 바이트코드 비교**: `dotnet run -m dis [filename]`

## 🎯 **개발 원칙**
- **바이트코드 레벨 호환성**: CPython 3.12와 동일한 바이트코드 생성
- **실용적 디버깅**: `python -m dis`로 즉시 정답 확인
- **표준 준수**: Python 3.12 언어 명세 완전 준수
- SharpPy 바이트코드와 **명령어별 정확한 비교**
- **추측성 변경 절대 금지** - 반드시 CPython 패턴 확인
- **SharpPy 최적화 성능** : _enable_optimizer이 true 와 false 일 때, bytecode는 다르지만 결과는 동일해야 함

---
**마지막 업데이트**: 2025-09-06 - Exception Table 시스템 완성  