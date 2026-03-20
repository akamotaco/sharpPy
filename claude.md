# SharpPy - Python 3.12 Interpreter in C#

## **심층 테스트 코드**
- test_ultimate_complex_features.py
- test_python312_advanced_features.py
- test_python312_missing_features.py
- test_comprehensive_python312.py

## **회귀 테스트**
- tests/test_abc_isinstance.py — ABC isinstance 호환 (내장타입 dunder, ABCMeta 체인, random.sample, range type)
- tests/test_builtin_kwargs.py — builtin kwargs 호환 (int base, dict kwargs, sorted reverse/key, class __init__ kwargs)

## 🔧 **개발 환경**
- **OS** : windows
- **CPython 3.12**: ` C:\ProgramData\miniforge3\python.exe`
- **SharpPy 빌드**: `dotnet build`
- **SharpPy 실행**: `dotnet run [filename]`
- **CPython 3.12 바이트코드 비교**: `export PYTHONUTF8=1 && " C:\ProgramData\miniforge3\python.exe" -m dis [filename]`
- **SharpPy 토큰 비교**: `dotnet run --tokens [filename]`
- **SharpPy 바이트코드 비교**: `dotnet run --dis [filename]`
- **SharpPy 바이트코드 비교**: `dotnet run --ast [filename]`
- **SharpPy 로그 최소화**: `dotnet run -c release [filename]`
- **utf-8 인코딩 설정** : `set PYTHONUTF8=1` 또는 `export PYTHONUTF8=1`
- **빌드 순서** : Tokenizer 빌드&실행 -> PEG Interpreter 빌드&실행 -> SharpPy 빌드&실행
- **CPython 3.12 로컬 소스코드 위치** : `C:\Users\akamo\Desktop\work\cpython-3.12`

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
- **CPython 연관 코드 주석** : CPython 과 연관있는 주석은 CPython 의 파일이름과 함께 lines 번호를 주석으로 작성하라.
- **구현 정책** : 간단하고 쉬운 방법보다는 올바른 방법으로 해결하라
- **바이트코드 레벨 호환성**: CPython 3.12와 동일한 바이트코드
- **실용적 디버깅**: `python -m dis`로 즉시 정답 확인
- **표준 준수**: Python 3.12 언어 명세 완전 준수, object/dynamic 타입 사용 금지.
- **SharpPy와 CPython 3.12의 비교** : CPython의 바이트 코드는 SharpPy 의 최적화 On 바이트 코드와 동일해야 하고, SharpPy의 최적화 On 결과와 SharPy의 최적화 Off 결과가 동일해야 한다.
- **추측성 변경 절대 금지** : 반드시 CPython 패턴 확인. 임시 해결보다는 근본 원인 분석 및 해결이 중요.
- **빌드의 성공 여부 확인** : dotent run 으로 빌드시 가장 첫 문장을 먼저 확인할 것. "Error: The build failed. Fix the build errors and run again."
- **exception table** : python 3.12 부터는 loop stack 은 사용하지 않음. exception table은 사용됨
- **Offset vs Index 정책** (자세한 내용: `docs/offset_vs_index_analysis.md`):
  - **CPython 3.12**: 내부적으로 instruction word offset 사용 (포인터 연산), 표시는 byte offset
  - **SharpPy**: instruction index 기반 (C# 배열 인덱싱), bytecode 출력시에만 byte offset (index × 2)
  - **최적화 on/off는 offset/index 선택과 무관**

## 📚 **중요 참고 문서**
- **FAQ**: `docs/faq.md` - 개발 중 자주 발생하는 질문과 정책
  - KeywordExtractor 역할 (동적 추출, 하드코딩 금지)
  - python_cs.gram 작성 방법 (python_py.gram → python_cs.gram 직접 변환)
  - C 추종 성공/실패 기준 (동작이 우선, 이름은 부차적)
  - 문제 해결 접근법 (생각 우선, 행동은 나중)

- **리팩토링 계획**: `docs/refactoring_plan.md` - 현재 진행 중인 리팩토링 작업
  - Phase 1: KeywordExtractor 수정
  - Phase 2: python_cs.gram 재작성
  - Phase 3: PyParserHelpers.cs 재작성
  - 작업 우선순위, 검증 방법, 백업 전략

**중요:** 새 세션 시작 시 반드시 위 문서들을 먼저 읽고 컨텍스트를 파악할 것

---

## 🏗️ **컴파일러 아키텍처 (CPython 3.12)**

SharpPy는 CPython 3.12의 CFG 기반 컴파일 파이프라인을 구현합니다.

### **컴파일 파이프라인**
```
AST → InstructionSequence → CFG → Optimize → ByteCode
```

1. **InstructionSequence** (`runtime/InstructionSequence.cs`)
   - CPython의 `_PyCompile_InstructionSequence` 구현
   - 라벨 기반 중간 표현
   - `NewLabel()`: 라벨 생성
   - `UseLabel()`: 라벨 위치 마킹
   - `AddOp*()`: 명령어 추가
   - `ToByteCodeInstructions()`: ByteCodeInstruction[] 변환

2. **PyFlowGraph** (`runtime/flowgraph.cs`)
   - CPython의 `Python/flowgraph.c` 구현
   - InstructionSequence → CFG 변환
   - Basic block 생성 및 연결

3. **ControlFlowGraph** (`runtime/ControlFlowGraph.cs`)
   - Basic block 단위 코드 구조
   - Instruction-level exception handler 관리
   - `BuildExceptionTable()`: Exception table 생성

4. **PyAssemble** (`runtime/assemble.cs`)
   - CPython의 `Python/assemble.c` 구현
   - CFG → 최종 바이트코드 변환
   - 라벨 → 오프셋 해결

### **Exception Handling**
- Instruction-level handler offset 관리
- `ByteCodeInstruction.ExceptionHandlerOffset`
- `BuildExceptionTable()`: CFG에서 exception table 재구축

### **주요 파일**
- `runtime/InstructionSequence.cs`: 라벨 기반 IR
- `runtime/flowgraph.cs`: InstructionSequence → CFG
- `runtime/assemble.cs`: CFG → ByteCode
- `runtime/ControlFlowGraph.cs`: CFG 관리
- `runtime/compile.cs`: AST → InstructionSequence

---

## ⚡ **CPython 3.12 스타일 최적화**

SharpPy는 CPython 3.12의 성능 최적화 패턴을 따릅니다.

### **PyCodeObject 캐시 (runtime/PyBytecode.cs)**
- `CellVarIndexMap`: CellVar name → index Dictionary (O(1) lookup)
- `FreeVarIndexMap`: FreeVar name → index Dictionary (O(1) lookup)
- `VarNameIndexMap`: VarName name → index Dictionary (O(1) lookup)
- `VarNameSet`: VarNames HashSet (O(1) Contains)
- `ClassCellIndex`: `__class__` 셀 인덱스 캐시 (-1이면 없음)
- `HasClassCell`: `__class__` 존재 여부 (O(1))

### **PyGlobalStrings (runtime/PyGlobalStrings.cs)**
- CPython의 `_Py_global_strings` / `_Py_ID()` 매크로 구현
- 모든 매직 메서드/속성 이름 interned string 상수
- `PyGlobalStrings.Id.__class__`, `PyGlobalStrings.Id.__init__` 등
- `PyGlobalStrings.Literals.ListComp`, `PyGlobalStrings.Literals.Module` 등

### **ComprehensionType Enum (runtime/PySymbolTable.cs)**
- CPython의 `_Py_comprehension_ty` 구현
- `ComprehensionType.None`, `ListComp`, `SetComp`, `DictComp`, `GeneratorExp`
- `IsInlined()` 확장 메서드: PEP 709 인라인 여부 (genexpr 제외)
- 문자열 비교 대신 정수 비교로 성능 향상

### **정적 HashSet (runtime/PySymbolTable.cs)**
- `_pythonKeywords`: Python 키워드 정적 HashSet
- 매 호출마다 새 HashSet 생성 대신 정적 참조

### **PyVM.cs DEREF 명령어 최적화**
- `CellVars.IndexOf()` → `CellVarIndexMap[name]` (O(n) → O(1))
- `VarNames.Contains()` → `VarNameSet.Contains()` (O(n) → O(1))
- `VarNames.IndexOf()` → `VarNameIndexMap[name]` (O(n) → O(1))

---
