# SharpPy - Python 3.12 Interpreter in C#

## **테스트 방법**
- 대상 : 현재 폴더 및 하위 폴더의 모든 test_*.py 파일 (단, 이미 테스트 완료된 파일은 무시)
- 테스트는 순차적으로 진행하며, 그 결과는 '테스트 결과 종합' 에 작성.
- 만일 문제가 발견되었다면 문제 해결에 주력할 것.
- 문제 해결 시에는 cPython 3.12의 호환성을 고려하여 문제를 해결할 것.

### 🎯 **올바른 문제 해결 순서**
1. 문제 발생 → CPython tokenize/AST/bytecode 분석
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
- **OS** : windows
- **CPython 3.12**: `C:\Users\m11\miniforge3\envs\py312\python.exe`
- **SharpPy 빌드**: `dotnet build`
- **SharpPy 실행**: `dotnet run [filename]`
- **CPython 3.12 바이트코드 비교**: `export PYTHONUTF8=1 && "C:\Users\m11\miniforge3\envs\py312\python.exe" -m dis [filename]`
- **SharpPy 바이트코드 비교**: `dotnet run --dis [filename]`
- **SharpPy 바이트코드 비교**: `dotnet run --ast [filename]`
- **SharpPy 로그 최소화**: `dotnet run -c release [filename]`
- **utf-8 인코딩 설정** : `set PYTHONUTF8=1` 또는 `export PYTHONUTF8=1`
- **최적화 비사용** : `--no-optimize`
- **토큰 시각화** : ` --tokens`
- **빌드 순서** : Tokenizer 빌드&실행 -> PEG Interpreter 빌드&실행 -> SharpPy 빌드&실행

### 🔍 **디버그 로그 카테고리**

SharpPy는 세분화된 디버그 로그를 지원합니다. sharppy.csproj에서 필요한 로그만 활성화하세요:

```xml
<!-- 모든 로그 활성화 (Debug 모드 기본값) -->
<DefineConstants>DEBUG;TRACE;DEBUG_TOKEN_LOG;DEBUG_PARSE_LOG;DEBUG_AST_LOG;DEBUG_COMPILER_LOG;DEBUG_VM_LOG</DefineConstants>

<!-- 특정 로그만 활성화 예시 -->
<DefineConstants>DEBUG;TRACE;DEBUG_PARSE_LOG;DEBUG_AST_LOG</DefineConstants>

<!-- Release 모드: 모든 로그 비활성화 -->
<DefineConstants>TRACE</DefineConstants>
```

**로그 카테고리:**
- `DEBUG_TOKEN_LOG`: 토크나이저 로그
  - 토큰 생성 과정
  - 들여쓰기 처리 (INDENT/DEDENT)
  - 줄바꿈 및 공백 처리
  - 위치: `Generated/PyTokenizer.cs` (CSharpTokenizerGenerator에서 생성)

- `DEBUG_PARSE_LOG`: 파서 로그
  - 문법 규칙 매칭 과정
  - 메모이제이션 (캐시 히트/미스)
  - Left recursion 처리
  - 위치: `Generated/PyParserBase.cs`, `Generated/PyParser.cs`

- `DEBUG_AST_LOG`: AST 변환 로그
  - Generated AST → SharpPy AST 변환
  - AST 노드 생성
  - 위치: `runtime/GeneratedParserBridge.cs`

- `DEBUG_COMPILER_LOG`: 컴파일러 로그
  - 심볼 테이블 구축
  - AST → 바이트코드 변환
  - 최적화 과정
  - 위치: `SharpPy.Compiler/*.cs`

- `DEBUG_VM_LOG`: VM 실행 로그
  - 바이트코드 실행 과정
  - 스택 상태
  - 변수 조회 (LEGB)
  - 위치: `SharpPy.VM/*.cs`

**사용 예시:**
```bash
# 파싱만 디버그 (토큰화 + 파서 규칙)
dotnet build /p:DefineConstants="DEBUG;TRACE;DEBUG_TOKEN_LOG;DEBUG_PARSE_LOG"

# AST 변환만 디버그
dotnet build /p:DefineConstants="DEBUG;TRACE;DEBUG_AST_LOG"

# 전체 디버그 로그 비활성화 (빠른 실행)
dotnet run -c Release test.py
```

## 🎯 **개발/테스트 원칙**
- **구현 정책** : 간단하고 쉬운 방법보다는 올바른 방법으로 해결하라
- **바이트코드 레벨 호환성**: CPython 3.12와 동일한 바이트코드
- **실용적 디버깅**: `python -m dis`로 즉시 정답 확인
- **표준 준수**: Python 3.12 언어 명세 완전 준수, object/dynamic 타입 사용 금지.
- **SharpPy와 CPython 3.12의 비교** : CPython의 바이트 코드는 SharpPy 의 최적화 On 바이트 코드와 동일해야 하고, SharpPy의 최적화 On 결과와 SharPy의 최적화 Off 결과가 동일해야 한다.
- **추측성 변경 절대 금지** : 반드시 CPython 패턴 확인. 임시 해결보다는 근본 원인 분석 및 해결이 중요.
- **빌드의 성공 여부 확인** : dotent run 으로 빌드시 가장 첫 문장을 먼저 확인할 것. "Error: The build failed. Fix the build errors and run again."
- **exception table** : python 3.12 부터는 loop stack 은 사용하지 않음. exception table은 사용됨
- **최적화/비화적화 차이** : 최적화 비활성화시에는 byte offset, 최적화 활성화시에는 instruction offset을 사용

---

## 📂 **프로젝트 구조 및 빌드 순서**

### **3단계 순차 빌드 프로세스**

SharpPy는 **코드 생성 기반 아키텍처**로, 반드시 순차적으로 빌드해야 합니다:

```
1단계: SharpPy.Tokenizer (독립 빌드 도구)
   입력: Grammar/Tokens
   실행: dotnet run --project SharpPy.Tokenizer --tokens Grammar/Tokens --output Generated/PyTokenizer.cs
   출력: Generated/PyTokenizer.cs (1,302 라인)
   내용:
     - GeneratedPtr 클래스 (모든 타입의 베이스 클래스)
     - GeneratedTokenType enum (토큰 타입 정의)
     - GeneratedTokenInfo class (토큰 정보)
     - PyTokenizer class (토큰화 로직)

2단계: SharpPy.PegGenerator (Tokenizer 의존 빌드 도구)
   입력: Grammar/Python.asdl + Grammar/python.gram + Generated/PyTokenizer.cs
   실행: dotnet run --project SharpPy.PegGenerator --asdl ... --grammar ... --tokens ...
   출력:
     - Generated/GeneratedAstTypes.cs (1,817 라인) - AST 노드 타입들
     - Generated/PyParserBase.cs (1,869 라인) - 파서 기본 클래스
     - Generated/PyParser.cs (36,855 라인) - 244개 문법 규칙 파서
   의존성: PyTokenizer.cs의 GeneratedPtr 사용

3단계: sharppy (메인 프로젝트)
   입력: 모든 Generated 파일 + runtime/ 소스
   실행: dotnet build
   출력: 최종 실행 파일
   의존성: 모든 Generated 파일 필요
```

### **Generated 폴더 파일 역할**

| 파일 | 생성자 | 크기 | 역할 |
|------|--------|------|------|
| **PyTokenizer.cs** | SharpPy.Tokenizer | 1,302줄 | 토큰 정의 및 토큰화, **GeneratedPtr 정의** |
| **GeneratedAstTypes.cs** | SharpPy.PegGenerator | 1,817줄 | AST 노드 타입 (ASDL → C# 클래스) |
| **PyParserBase.cs** | SharpPy.PegGenerator | 1,869줄 | 파서 베이스, PegenHelpers, AstFactory |
| **PyParser.cs** | SharpPy.PegGenerator | 36,855줄 | 실제 파싱 로직 (Grammar → 파서 메서드) |

**중요**: 모든 Generated 파일은 `namespace SharpPy.Generated` 사용

---

## 🔒 **파일 수정 가이드라인**

### ✅ **수정 가능 파일** (빌드 도구 소스)

**SharpPy.Tokenizer/** (토큰화 생성기)
- `CSharpTokenizerGenerator.cs` - PyTokenizer.cs 생성 로직
- `TokensReader.cs` - Grammar/Tokens 파일 파서
- `Program.cs` - 진입점

**SharpPy.PegGenerator/** (파서 생성기)
- `Asdl/AsdlCodeGenerator.cs` - GeneratedAstTypes.cs 생성
- `Asdl/PyParserBaseGenerator.cs` - PyParserBase.cs 생성
- `CodeGenerator/CSharpCodeGenerator.cs` - PyParser.cs 생성
- `CodeGenerator/ActionMapper.cs` - AST 액션 매핑
- `CodeGenerator/AlternativeCodeGenerator.cs` - 문법 대안 처리
- `Grammar/GrammarParser.cs` - python.gram 파서
- `Program.cs` - 진입점

**runtime/** (런타임 라이브러리)
- 모든 .cs 파일 수정 가능 (AST → 바이트코드 변환 등)

**Grammar/** (문법 정의 파일)
- `Tokens` - 토큰 정의
- `Python.asdl` - AST 명세
- `python.gram` - PEG 문법 규칙

### ❌ **수정 금지 파일** (자동 생성됨)

**Generated/** - **절대 직접 수정 금지!**
- `PyTokenizer.cs` - 다음 빌드 시 덮어씀
- `GeneratedAstTypes.cs` - 다음 빌드 시 덮어씀
- `PyParserBase.cs` - 다음 빌드 시 덮어씀
- `PyParser.cs` - 다음 빌드 시 덮어씀

**수정 방법**: 생성기 소스(SharpPy.Tokenizer/ 또는 SharpPy.PegGenerator/)를 수정 후 재생성

---

## 🔗 **주요 의존성 관계**

### **GeneratedPtr 위치** (중요!)
```csharp
// PyTokenizer.cs에 정의 (첫 번째 생성 파일)
public abstract class GeneratedPtr { }

// GeneratedAstTypes.cs에서 사용
public abstract class GeneratedAstNode : GeneratedPtr { }
public abstract class GeneratedSeq : GeneratedPtr { }
```

**주의**:
- GeneratedPtr은 PyTokenizer.cs에 정의되지만, 코드 주석에는 "GeneratedPtr.cs"라고 표기됨
- 실제로는 PyTokenizer.cs가 먼저 생성되어야 PegGenerator가 작동함

### **파일 간 의존성 체인**
```
PyTokenizer.cs (GeneratedPtr 정의)
    ↓ (사용)
GeneratedAstTypes.cs (AST 타입들, GeneratedPtr 상속)
    ↓ (사용)
PyParserBase.cs (헬퍼 함수들, AST 타입 사용)
    ↓ (상속 및 사용)
PyParser.cs (실제 파서, 모든 것 사용)
```

### **빌드 도구와 메인 프로젝트 관계**
- sharppy.csproj는 SharpPy.Tokenizer/PegGenerator의 **소스 코드를 포함**
- 단, Program.cs는 제외 (중복 Main 방지)
- 빌드 시 도구를 **별도 프로젝트로 실행**하여 코드 생성
- **동일 클래스가 두 번 컴파일되는 구조** (빌드 도구 + 메인 프로젝트)

---

## ⚠️ **수정 시 주의사항**

### **1. Generated 파일 직접 수정 금지**
```bash
# 잘못된 방법 (다음 빌드 시 사라짐)
vim Generated/PyParser.cs  # ❌ 절대 금지

# 올바른 방법
vim SharpPy.PegGenerator/CodeGenerator/ActionMapper.cs  # ✅ 생성기 수정
dotnet build  # 자동으로 재생성됨
```

### **2. 수정 후 재생성 필요 범위**

| 수정 파일 | 재생성 필요 | 명령 |
|----------|-----------|------|
| SharpPy.Tokenizer/* | 1단계부터 전체 | `dotnet build` (자동) |
| SharpPy.PegGenerator/* | 2단계부터 | `dotnet build` (자동) |
| runtime/* | 재생성 불필요 | `dotnet build` |
| Grammar/* | 해당 단계부터 | `dotnet build` (자동) |

### **3. 전체 재빌드 권장 상황**
- 빌드 도구(Tokenizer/PegGenerator) 수정 후
- Grammar 파일 수정 후
- GeneratedPtr 관련 변경 후
- 타입 시스템 변경 후

```bash
# 안전한 전체 재빌드
rm -rf Generated/*.cs  # Generated 파일 삭제
dotnet clean           # 빌드 캐시 정리
dotnet build           # 처음부터 재빌드
```

### **4. 순환 의존성 주의**
- 메인 프로젝트가 빌드 도구 소스를 포함하므로, 빌드 도구는 메인 프로젝트를 참조하면 안 됨
- 빌드 도구는 완전 독립적이어야 함
- Generated 폴더 파일만 상호 의존 가능

---

## 🐛 **디버깅 팁**

### **생성 파일 확인**
```bash
# 각 단계별 생성 확인
ls -lh Generated/PyTokenizer.cs        # 1단계 확인
ls -lh Generated/GeneratedAstTypes.cs  # 2단계 확인
ls -lh Generated/PyParser.cs           # 2단계 확인
```

### **빌드 순서 문제 해결**
```bash
# 빌드 로그 확인
dotnet build -v detailed 2>&1 | grep "Generating"

# 예상 순서:
# [PEG] Generating tokenizer from Grammar/Tokens...
# Generating AST types and PEG parser from ASDL + Grammar...
```

### **GeneratedPtr 관련 에러**
- "GeneratedPtr could not be found" → PyTokenizer.cs 먼저 생성 확인
- "circular dependency" → 빌드 도구가 메인 프로젝트 참조하는지 확인