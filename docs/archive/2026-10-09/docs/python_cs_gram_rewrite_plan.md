# python_cs.gram 재작성 계획

## 📋 목표

python_cs.gram을 C 코드 기계적 변환이 아닌, **의미를 이해한 올바른 C# 구현**으로 재작성

---

## 🔍 현재 상태 분석

### 주요 문제점

1. **@trailer 지원 안 됨**: 현재 PEG Generator가 @trailer 문법 파싱 불가
2. **C 함수명 그대로**: `_PyAST_Pass`, `_PyPegen_seq_flatten` 등
3. **C 타입 그대로**: `stmt_ty`, `asdl_stmt_seq*` 등
4. **C 패턴 그대로**: `NULL`, `p->arena`, C 캐스트 등
5. **부분적 변환**: 일부는 C# 스타일 (`tc?.Value`), 일부는 C 스타일 혼재

### 작업 범위

- **총 규칙 수**: ~200+ rules (python.gram 기준)
- **섹션 구분**:
  1. STARTING RULES (4 rules)
  2. GENERAL STATEMENTS (~10 rules)
  3. SIMPLE STATEMENTS (~15 rules)
  4. COMPOUND STATEMENTS (~30 rules)
  5. EXPRESSIONS (~80 rules)
  6. LITERALS (~15 rules)
  7. FUNCTION CALL ARGUMENTS (~10 rules)
  8. ASSIGNMENT TARGETS (~15 rules)
  9. TYPING ELEMENTS (~5 rules)
  10. INVALID RULES (~60 rules)

---

## 📝 재작성 전략

### 단계별 접근

**Phase 1: @trailer 제거 및 Entry Points 분리** ✓
- python_cs.gram에서 @trailer 제거
- Entry point 메서드(`ParseFile`, `ParseInteractive` 등)를 PyParser.cs partial class로 이동

**Phase 2: STARTING RULES (가장 간단)** → 시작점
- `file`, `interactive`, `eval`, `func_type` (4 rules)
- 기본 패턴 확립

**Phase 3: GENERAL STATEMENTS**
- `statements`, `statement`, `simple_stmts` 등
- Sequence flattening 패턴 확립

**Phase 4: SIMPLE STATEMENTS**
- `assignment`, `return_stmt`, `raise_stmt`, `import_stmt` 등
- AST 생성 패턴 확립

**Phase 5: COMPOUND STATEMENTS**
- `if_stmt`, `for_stmt`, `while_stmt`, `class_def`, `function_def` 등
- 복잡한 AST 구조

**Phase 6: EXPRESSIONS**
- `expression`, `disjunction`, `conjunction`, `comparison` 등
- 연산자 우선순위

**Phase 7: LITERALS & REMAINING**
- `strings`, `list`, `dict`, `tuple`, `fstring` 등
- 나머지 규칙들

**Phase 8: INVALID RULES**
- `invalid_*` rules
- 에러 메시지

**Phase 9: 통합 테스트**
- 전체 빌드
- test_python312_*.py 실행
- 바이트코드 비교

---

## 🛠️ Phase 1: @trailer 제거 및 Entry Points 분리

### 목표

`@trailer` 코드를 python_cs.gram에서 제거하고, PyParser.cs partial class로 이동

### 작업 내용

#### 1. python_cs.gram 수정

**제거할 부분 (lines 3-137):**
```csharp
@trailer '''
// CPython 3.12: Entry point for file parsing
public GeneratedMod ParseFile()
{
    // ...
}
// ... (나머지 entry points)
'''
```

**수정 후:**
```csharp
# PEG grammar for Python

# ========================= START OF THE GRAMMAR =========================

# General grammatical elements and rules:
# ...
```

#### 2. PyParser.cs에 partial class 추가

**새 파일 생성: `runtime/PyParserEntryPoints.cs`**
```csharp
using SharpPy.Generated;

namespace SharpPy.Runtime
{
    public partial class PyParser
    {
        // CPython 3.12: Entry point for file parsing
        public GeneratedMod ParseFile()
        {
            var result = File();
            if (result == null || _pendingSyntaxError != null)
            {
                var errorMsg = _pendingSyntaxError ?? "invalid syntax";

                GeneratedTokenInfo errorToken = _knownErrToken;
                if (errorToken == null && _position < _tokens.Count)
                {
                    errorToken = _tokens[_position];
                }
                else if (errorToken == null && _tokens.Count > 0)
                {
                    errorToken = _tokens[0];
                }

                if (errorToken != null)
                {
                    var ex = new PySyntaxErrorException(errorMsg, _filename,
                        errorToken.Line, errorToken.Column,
                        errorToken.EndColumn,
                        GetSourceLine(errorToken.Line));
                    throw ex;
                }

                throw new PySyntaxErrorException(errorMsg);
            }
            return result;
        }

        // CPython 3.12: Entry point for interactive parsing
        public GeneratedMod ParseInteractive()
        {
            var result = Interactive();
            if (result == null || _pendingSyntaxError != null)
            {
                var errorMsg = _pendingSyntaxError ?? "invalid syntax";

                GeneratedTokenInfo errorToken = _knownErrToken;
                if (errorToken == null && _position < _tokens.Count)
                {
                    errorToken = _tokens[_position];
                }
                else if (errorToken == null && _tokens.Count > 0)
                {
                    errorToken = _tokens[0];
                }

                if (errorToken != null)
                {
                    var ex = new PySyntaxErrorException(errorMsg, _filename,
                        errorToken.Line, errorToken.Column,
                        errorToken.EndColumn,
                        GetSourceLine(errorToken.Line));
                    throw ex;
                }

                throw new PySyntaxErrorException(errorMsg);
            }
            return result;
        }

        // CPython 3.12: Entry point for eval parsing
        public GeneratedMod ParseEval()
        {
            var result = Eval();
            if (result == null || _pendingSyntaxError != null)
            {
                var errorMsg = _pendingSyntaxError ?? "invalid syntax";

                GeneratedTokenInfo errorToken = _knownErrToken;
                if (errorToken == null && _position < _tokens.Count)
                {
                    errorToken = _tokens[_position];
                }
                else if (errorToken == null && _tokens.Count > 0)
                {
                    errorToken = _tokens[0];
                }

                if (errorToken != null)
                {
                    var ex = new PySyntaxErrorException(errorMsg, _filename,
                        errorToken.Line, errorToken.Column,
                        errorToken.EndColumn,
                        GetSourceLine(errorToken.Line));
                    throw ex;
                }

                throw new PySyntaxErrorException(errorMsg);
            }
            return result;
        }

        // CPython 3.12: Entry point for function type parsing
        public GeneratedMod ParseFuncType()
        {
            var result = FuncType();
            if (result == null || _pendingSyntaxError != null)
            {
                var errorMsg = _pendingSyntaxError ?? "invalid syntax";

                GeneratedTokenInfo errorToken = _knownErrToken;
                if (errorToken == null && _position < _tokens.Count)
                {
                    errorToken = _tokens[_position];
                }
                else if (errorToken == null && _tokens.Count > 0)
                {
                    errorToken = _tokens[0];
                }

                if (errorToken != null)
                {
                    var ex = new PySyntaxErrorException(errorMsg, _filename,
                        errorToken.Line, errorToken.Column,
                        errorToken.EndColumn,
                        GetSourceLine(errorToken.Line));
                    throw ex;
                }

                throw new PySyntaxErrorException(errorMsg);
            }
            return result;
        }
    }
}
```

#### 3. 검증

- python_cs.gram 파싱 성공 확인
- 빌드 성공 확인
- PyParser.ParseFile() 등 entry point 호출 가능 확인

---

## 🛠️ Phase 2: STARTING RULES 재작성

### 목표

가장 간단한 4개 규칙 재작성하여 기본 패턴 확립

### 작업 내용

#### 1. file rule

**python.gram (C):**
```c
file[mod_ty]: a=[statements] ENDMARKER { _PyPegen_make_module(p, a) }
```

**python_cs.gram (현재):**
```csharp
file[mod_ty]: a=[statements] ENDMARKER { _PyPegen_make_module(a) }
```

**python_cs.gram (목표):**
```csharp
file: a=[statements] ENDMARKER { PyParserHelpers.MakeModule(a) }
```

**변경사항:**
1. `[mod_ty]` 제거
2. `_PyPegen_make_module(a)` → `PyParserHelpers.MakeModule(a)`

#### 2. interactive rule

**python.gram (C):**
```c
interactive[mod_ty]: a=statement_newline { _PyAST_Interactive(a, p->arena) }
```

**python_cs.gram (목표):**
```csharp
interactive: a=statement_newline { PyAst.Interactive(a) }
```

**변경사항:**
1. `[mod_ty]` 제거
2. `_PyAST_Interactive(a, p->arena)` → `PyAst.Interactive(a)` (`p->arena` 제거)

#### 3. eval rule

**python.gram (C):**
```c
eval[mod_ty]: a=expressions NEWLINE* ENDMARKER { _PyAST_Expression(a, p->arena) }
```

**python_cs.gram (목표):**
```csharp
eval: a=expressions NEWLINE* ENDMARKER { PyAst.Expression(a) }
```

#### 4. func_type rule

**python.gram (C):**
```c
func_type[mod_ty]: '(' a=[type_expressions] ')' '->' b=expression NEWLINE* ENDMARKER {
    _PyAST_FunctionType(a, b, p->arena)
}
```

**python_cs.gram (목표):**
```csharp
func_type: '(' a=[type_expressions] ')' '->' b=expression NEWLINE* ENDMARKER {
    PyAst.FunctionType(a, b)
}
```

#### 5. PyParserHelpers.cs 구현

**새 파일: `runtime/PyParserHelpers.cs` (또는 기존 파일 수정)**

```csharp
using SharpPy.Generated;
using System.Collections.Generic;

namespace SharpPy.Runtime
{
    public static class PyParserHelpers
    {
        /// <summary>
        /// CPython: _PyPegen_make_module
        /// Creates a Module with optional statements
        /// </summary>
        public static GeneratedMod MakeModule(List<GeneratedStmt>? statements)
        {
            return new GeneratedModule(
                statements ?? new List<GeneratedStmt>()
            );
        }

        // ... (다른 helper 함수들)
    }
}
```

#### 6. PyAst.cs 구현

**새 파일: `runtime/PyAst.cs` (또는 기존 파일 수정)**

```csharp
using SharpPy.Generated;
using System.Collections.Generic;

namespace SharpPy.Runtime
{
    public static class PyAst
    {
        /// <summary>
        /// CPython: _PyAST_Interactive
        /// </summary>
        public static GeneratedMod Interactive(List<GeneratedStmt> statements)
        {
            return new GeneratedInteractive(statements);
        }

        /// <summary>
        /// CPython: _PyAST_Expression
        /// </summary>
        public static GeneratedMod Expression(GeneratedExpr expr)
        {
            return new GeneratedExpression(expr);
        }

        /// <summary>
        /// CPython: _PyAST_FunctionType
        /// </summary>
        public static GeneratedMod FunctionType(
            List<GeneratedExpr>? argTypes,
            GeneratedExpr returnType)
        {
            return new GeneratedFunctionType(
                argTypes ?? new List<GeneratedExpr>(),
                returnType
            );
        }

        /// <summary>
        /// CPython: _PyAST_Pass
        /// </summary>
        public static GeneratedStmt Pass(int startLine, int startCol, int endLine, int endCol)
        {
            return new GeneratedPass(startLine, startCol, endLine, endCol);
        }

        // ... (다른 AST 생성 함수들)
    }
}
```

#### 7. 검증

- python_cs.gram 파싱 성공
- 빌드 성공
- `file`, `interactive`, `eval`, `func_type` 규칙 테스트

---

## 🛠️ Phase 3-8: 나머지 섹션 재작성

### 접근 방식

각 섹션마다 다음 순서로 작업:

1. **python.gram 분석**: 의미 파악
2. **python_cs.gram 수정**: C# 스타일로 재작성
3. **Helper 함수 구현**: PyParserHelpers.cs, PyAst.cs
4. **빌드 + 테스트**: 섹션별로 검증
5. **다음 섹션 진행**

### 우선순위

**High Priority (핵심 기능):**
- Phase 3: GENERAL STATEMENTS
- Phase 4: SIMPLE STATEMENTS
- Phase 5: COMPOUND STATEMENTS (if, for, while, class, function)
- Phase 6: EXPRESSIONS (기본 연산자)

**Medium Priority:**
- Phase 6 (나머지): EXPRESSIONS (복잡한 연산자, comprehension)
- Phase 7: LITERALS

**Low Priority (나중에 처리 가능):**
- Phase 8: INVALID RULES (에러 메시지는 기능 완성 후)

---

## 📊 진행 상황 추적

### 체크리스트

#### Phase 1: @trailer 제거
- [ ] python_cs.gram에서 @trailer 제거
- [ ] PyParserEntryPoints.cs 생성
- [ ] 빌드 성공 확인

#### Phase 2: STARTING RULES
- [ ] `file` rule 재작성
- [ ] `interactive` rule 재작성
- [ ] `eval` rule 재작성
- [ ] `func_type` rule 재작성
- [ ] PyParserHelpers.MakeModule 구현
- [ ] PyAst.Interactive/Expression/FunctionType 구현
- [ ] 빌드 + 테스트

#### Phase 3: GENERAL STATEMENTS
- [ ] `statements` rule 재작성
- [ ] `statement` rule 재작성
- [ ] `statement_newline` rule 재작성
- [ ] `simple_stmts` rule 재작성
- [ ] `simple_stmt` rule 재작성
- [ ] `compound_stmt` rule 재작성
- [ ] PyParserHelpers.FlattenSequence 구현
- [ ] PyParserHelpers.SingletonSequence 구현
- [ ] 빌드 + 테스트

#### Phase 4: SIMPLE STATEMENTS
- [ ] `assignment` rule 재작성
- [ ] `return_stmt` rule 재작성
- [ ] `raise_stmt` rule 재작성
- [ ] `import_stmt` rule 재작성
- [ ] `del_stmt` rule 재작성
- [ ] `global_stmt` rule 재작성
- [ ] `nonlocal_stmt` rule 재작성
- [ ] `assert_stmt` rule 재작성
- [ ] `yield_stmt` rule 재작성
- [ ] PyAst.* 함수들 구현
- [ ] 빌드 + 테스트

#### Phase 5: COMPOUND STATEMENTS
- [ ] `if_stmt` rule 재작성
- [ ] `for_stmt` rule 재작성
- [ ] `while_stmt` rule 재작성
- [ ] `class_def` rule 재작성
- [ ] `function_def` rule 재작성
- [ ] `with_stmt` rule 재작성
- [ ] `try_stmt` rule 재작성
- [ ] `match_stmt` rule 재작성
- [ ] 관련 helper 함수 구현
- [ ] 빌드 + 테스트

#### Phase 6: EXPRESSIONS
- [ ] `expression` rule 재작성
- [ ] `disjunction` rule 재작성
- [ ] `conjunction` rule 재작성
- [ ] `comparison` rule 재작성
- [ ] `bitwise_*` rules 재작성
- [ ] `shift_expr` rule 재작성
- [ ] `sum` rule 재작성
- [ ] `term` rule 재작성
- [ ] `factor` rule 재작성
- [ ] `power` rule 재작성
- [ ] `primary` rule 재작성
- [ ] `atom` rule 재작성
- [ ] Comprehension rules 재작성
- [ ] 관련 helper 함수 구현
- [ ] 빌드 + 테스트

#### Phase 7: LITERALS
- [ ] `strings` rule 재작성
- [ ] `fstring` rule 재작성
- [ ] `list` rule 재작성
- [ ] `tuple` rule 재작성
- [ ] `dict` rule 재작성
- [ ] `set` rule 재작성
- [ ] 관련 helper 함수 구현
- [ ] 빌드 + 테스트

#### Phase 8: INVALID RULES
- [ ] `invalid_*` rules 재작성
- [ ] 에러 메시지 C# 스타일로 변환
- [ ] 빌드 + 테스트

#### Phase 9: 통합 테스트
- [ ] 전체 빌드 성공
- [ ] test_python312_advanced_features.py 실행
- [ ] test_python312_missing_features.py 실행
- [ ] test_ultimate_complex_features.py 실행
- [ ] CPython 3.12와 바이트코드 비교

---

## 🎯 성공 기준

1. **빌드 성공**: python_cs.gram → PyParser.cs 생성 성공
2. **파싱 성공**: Python 3.12 문법 모두 파싱 가능
3. **AST 정확성**: CPython 3.12와 동일한 AST 생성
4. **바이트코드 일치**: 최적화 On/Off 모두 CPython과 일치
5. **테스트 통과**: 모든 test_python312_*.py 통과

---

## 📅 예상 일정

**Phase 1-2 (기본 인프라):** 1-2일
- @trailer 제거 + STARTING RULES

**Phase 3-4 (핵심 statements):** 2-3일
- GENERAL STATEMENTS + SIMPLE STATEMENTS

**Phase 5 (compound statements):** 2-3일
- IF, FOR, WHILE, CLASS, FUNCTION, WITH, TRY, MATCH

**Phase 6 (expressions):** 3-4일
- 모든 연산자 + comprehensions

**Phase 7 (literals):** 1-2일
- strings, fstring, list, dict, tuple, set

**Phase 8 (invalid rules):** 1-2일
- 에러 메시지

**Phase 9 (통합 테스트):** 1-2일
- 버그 수정 + 최종 검증

**총 예상 기간:** 11-18일

---

## 🚨 주의사항

1. **한 번에 한 섹션씩**: 전체를 한 번에 수정하지 말 것
2. **빌드 자주 확인**: 매 섹션마다 빌드 + 테스트
3. **CPython 소스 참조**: 불확실하면 CPython 3.12 C 소스 코드 확인
4. **의미 우선**: 기계적 변환 금지, 의미 파악 후 C# 스타일로 구현
5. **테스트 주도**: 각 섹션마다 간단한 Python 코드로 테스트

---

## 📚 참고 자료

1. **docs/c_to_csharp_patterns.md**: C → C# 변환 패턴 가이드
2. **docs/faq.md**: 정책 및 자주 묻는 질문
3. **CPython 3.12 소스**: `C:\Users\m11\Desktop\work\dup\cpython-3.12`
4. **PEP 617**: PEG 문법 명세

---

## 💡 다음 단계

**즉시 시작:**
1. Phase 1: @trailer 제거
2. Phase 2: STARTING RULES 재작성

**준비 사항:**
- runtime/PyParserEntryPoints.cs 생성
- runtime/PyParserHelpers.cs 확인/생성
- runtime/PyAst.cs 확인/생성
