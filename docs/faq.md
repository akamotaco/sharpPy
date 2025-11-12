# SharpPy Development FAQ

## Q1: KeywordExtractor는 무엇을 해야 하는가?

### 문제의 핵심
KeywordExtractor는 **답을 미리 알고 있으면 안 됩니다**. 하드코딩된 키워드 목록을 가져서는 안 됩니다.

### 올바른 역할
python_*.gram 파일을 파싱하면서 **키워드를 동적으로 발견(추출)**해야 합니다.

### Grammar 파일의 키워드 표기법

python_py.gram의 주석 (Line 8-9):
```
# * Strings with double quotes (") denote SOFT KEYWORDS
# * Strings with single quotes (') denote KEYWORDS
```

**예시:**
```python
# python_py.gram에서 발췌

simple_stmt:
    | 'pass'        # ← KEYWORD (single quote)
    | 'break'       # ← KEYWORD (single quote)
    | 'continue'    # ← KEYWORD (single quote)

if_stmt:
    | 'if' named_expression ':' block elif_stmt    # ← 'if' is KEYWORD

for_stmt:
    | 'for' star_targets 'in' ~ star_expressions ':'  # ← 'for', 'in' are KEYWORDS

match_stmt:
    | "match" subject_expr ':' NEWLINE INDENT case_block+ DEDENT  # ← "match" is SOFT KEYWORD (double quote)

case_block:
    | "case" patterns guard? ':' block  # ← "case" is SOFT KEYWORD (double quote)

type_alias:
    | "type" NAME [type_params] '=' expression  # ← "type" is SOFT KEYWORD (double quote)
```

### KeywordExtractor가 해야 할 일

1. **Grammar 파싱:**
   - python_py.gram (또는 python_cs.gram) 파일을 읽습니다
   - 정규식으로 `'keyword'` 패턴을 찾습니다 (single quote)
   - 정규식으로 `"keyword"` 패턴을 찾습니다 (double quote)

2. **두 종류의 키워드 구분:**
   - **KEYWORDS (Hard Keywords)**: `'if'`, `'for'`, `'pass'`, `'break'`, etc.
     - 항상 키워드로만 사용됨
     - 변수명으로 사용 불가
   - **SOFT_KEYWORDS**: `"match"`, `"case"`, `"type"`, etc.
     - 문맥에 따라 identifier로도 사용 가능
     - Python 3.10+ 호환성을 위해 도입됨

3. **출력:**
   - 추출된 키워드 목록을 C# 코드로 생성합니다
   - 예: `public static readonly HashSet<string> HardKeywords = new() { "if", "for", "pass", ... };`
   - 예: `public static readonly HashSet<string> SoftKeywords = new() { "match", "case", "type", ... };`

### 절대 하지 말아야 할 것

❌ **하드코딩된 키워드 목록을 갖고 있는 것**
```csharp
// 잘못된 예시 - 이렇게 하면 안 됨!
private static readonly HashSet<string> _hardKeywords = new()
{
    "if", "for", "while", "def", "class", // ... 하드코딩
};

public void ValidateKeywords()
{
    // grammar에서 발견한 키워드를 하드코딩 목록과 비교
    // 이것은 "검증"이지 "추출"이 아님!
}
```

✅ **Grammar에서 동적으로 발견하는 것**
```csharp
// 올바른 예시
public HashSet<string> ExtractHardKeywords(string grammarContent)
{
    var keywords = new HashSet<string>();

    // 정규식: 'keyword' 패턴 찾기 (single quote)
    var regex = new Regex(@"'([a-zA-Z_][a-zA-Z0-9_]*)'");

    foreach (Match match in regex.Matches(grammarContent))
    {
        keywords.Add(match.Groups[1].Value);
    }

    return keywords;
}
```

### 추가 참고사항

**PyTokens.cs와의 관계:**
- PyTokens.cs는 이미 존재합니다 (tokenizer가 먼저 생성됨)
- PyTokens.Type enum의 정보는 사용할 수 있습니다
- 예: `LPAR`, `RPAR`, `COLON`, `COMMA` 같은 토큰 타입들
- 하지만 `'if'`, `'for'` 같은 **키워드는 tokenizer가 모릅니다**
- 이것들은 NAME 토큰으로 tokenize되고, parser가 문맥적으로 keyword로 처리합니다

---

## Q2: python_cs.gram은 어떻게 만들어야 하는가?

### 잘못된 절차 (하지 말 것)
```
python_py.gram (원본, no action code)
    ↓
python.gram (C 버전, C action code)  ← 중간 단계, 잘못된 경로!
    ↓
python_cs.gram (C# 버전, C → C# 변환)  ← C 스타일 코드 패턴이 남음
```

이 방식의 문제:
- C typedef 흉내낸 using 별칭 (`using mod_ty = ...`)
- C 스타일 매크로 흉내 (`RAISE_SYNTAX_ERROR`)
- C → C# 기계적 번역으로 인한 부자연스러운 코드

### 올바른 절차
```
python_py.gram (원본, no action code)
    ↓
python.gram (C 버전)을 참고하여 "의미" 이해  ← 참고만!
    ↓
CPython 3.12 로컬 소스로 동작 확인
    ↓
python_cs.gram (C# 버전, 올바른 동작 구현)
```

**핵심 정책:**
1. **"정답은 없다"** - python_py.gram → python_cs.gram으로 변환하는 기계적 규칙은 없음
2. **"의미를 이해하라"** - python.gram (C 버전)을 보고 **무엇을 하려는지** 파악
3. **"동작이 우선이다"** - CPython 3.12와 동작이 같으면, 함수명은 C 스타일도 무방
4. **"CPython 소스가 정답"** - 로컬에 저장된 CPython 3.12 소스를 확인하여 분석

### 변환 예시 1: 간단한 케이스

**python_py.gram (원본):**
```python
simple_stmt:
    | 'pass'
    | 'break'
    | 'continue'
```

**python.gram (C 버전) - 의미 파악을 위한 참고:**
```c
simple_stmt[stmt_ty]:
    | 'pass' { _PyAST_Pass(EXTRA) }
    | 'break' { _PyAST_Break(EXTRA) }
    | 'continue' { _PyAST_Continue(EXTRA) }
```

**의미 이해:**
- `'pass'` → Pass statement AST 노드 생성
- `_PyAST_Pass(EXTRA)` → 위치 정보(line, col)와 함께 AST 생성
- `EXTRA` → 매크로, 실제로는 startLine, startCol, endLine, endCol

**python_cs.gram (C# 버전):**
```csharp
simple_stmt:
    | 'pass' {
        PyParserHelpers._PyAST_Pass(
            GetStartLine(),
            GetStartColumn(),
            GetEndLine(),
            GetEndColumn()
        )
    }
    | 'break' {
        PyParserHelpers._PyAST_Break(
            GetStartLine(),
            GetStartColumn(),
            GetEndLine(),
            GetEndColumn()
        )
    }
    | 'continue' {
        PyParserHelpers._PyAST_Continue(
            GetStartLine(),
            GetStartColumn(),
            GetEndLine(),
            GetEndColumn()
        )
    }
```

**함수명에 대한 정책:**
- `_PyAST_Pass` ← C 스타일 이름이지만 **동작이 정확하면 무방**
- 더 C# 스타일로 하려면 `PyAstPass` 또는 `CreatePassStatement`도 가능
- **중요한 것은 이름이 아니라 동작**

### 변환 예시 2: 복잡한 케이스

**python_py.gram (원본):**
```python
statements: statement+
```

**python.gram (C 버전) - 의미 파악을 위한 참고:**
```c
statements[asdl_stmt_seq*]: a=statement+ {
    (asdl_stmt_seq*)_PyPegen_seq_flatten(p, a)
}
```

**의미 이해:**
- `statement+` → statement를 1개 이상 파싱 → List<Statement>
- `_PyPegen_seq_flatten` → 중첩된 시퀀스를 평탄화하는 헬퍼 함수
- `asdl_stmt_seq*` → statement 시퀀스 (C의 포인터 표현)

**CPython 소스 확인:**
```bash
# 로컬 CPython 3.12 소스에서 확인
C:\...\cpython-3.12\Parser\pegen.c
→ _PyPegen_seq_flatten 함수 구현 확인
→ 동작: 중첩된 리스트를 단일 리스트로 평탄화
```

**python_cs.gram (C# 버전):**
```csharp
statements: a=statement+ {
    PyParserHelpers._PyPegen_seq_flatten(a)
}
```

**PyParserHelpers.cs 구현:**
```csharp
public static GeneratedStmtSeq _PyPegen_seq_flatten(List<GeneratedStmtSeq> sequences)
{
    // CPython 3.12와 동일한 동작
    var result = new List<GeneratedStmt>();
    foreach (var seq in sequences)
        result.AddRange(seq.Statements);
    return new GeneratedStmtSeq(result);
}
```

**함수명 정책:**
- `_PyPegen_seq_flatten` ← C 스타일 이름이지만 **동작이 정확하면 무방**
- 더 C# 스타일: `PyPegenSeqFlatten` 또는 `FlattenStatementSequence`
- **핵심은 CPython 3.12와 동작이 동일한가?**

### C 추종의 성공 vs 실패

**❌ 실패한 C → C# 전환 (문제):**
```csharp
// C typedef를 억지로 흉내 (의미 없는 추종)
using mod_ty = SharpPy.Generated.GeneratedMod;
using stmt_ty = SharpPy.Generated.GeneratedStmt;

// C 매크로를 억지로 흉내 (동작도 안 맞음)
#define EXTRA GetStartLine(), GetStartCol()  // ← C#에 #define 없음!
```
→ **C를 따라했지만 제대로 동작하지 않음**

**✅ 성공한 C → C# 전환 (무방):**
```csharp
// 함수명은 C 스타일이지만 동작은 정확함
public static GeneratedStmtSeq _PyPegen_seq_flatten(List<GeneratedStmtSeq> sequences)
{
    // CPython 3.12와 동일하게 동작
    var result = new List<GeneratedStmt>();
    foreach (var seq in sequences)
        result.AddRange(seq.Statements);
    return new GeneratedStmtSeq(result);
}
```
→ **이름은 C 스타일이지만 동작이 정확하면 OK**

### "참고"의 올바른 사용법

**python.gram (C 버전)의 역할:**
- ❌ **복사해서 사용할 템플릿이 아님**
- ✅ **의미를 이해하기 위한 힌트**

**참고 프로세스:**
1. python_py.gram에서 `statements: statement+` 발견
2. "statement+는 무슨 의미지?" → python.gram 확인
3. python.gram에서 `_PyPegen_seq_flatten` 발견
4. "이 함수는 뭘 하는 거지?" → CPython 소스 확인
5. CPython 소스에서 동작 이해 → "아, 평탄화하는구나"
6. C# 코드로 **의미를 구현** (이름은 C 스타일도 무방)

---

## Q3: PyParserHelpers.cs의 using 별칭은 왜 문제인가?

### 문제가 있는 코드
```csharp
using mod_ty = SharpPy.Generated.GeneratedMod;
using stmt_ty = SharpPy.Generated.GeneratedStmt;
using expr_ty = SharpPy.Generated.GeneratedExpr;
// ... 등등
```

### 왜 문제인가?
- 이것은 **C typedef를 C#으로 흉내낸 것**입니다
- python.gram (C 버전)에서 온 패턴입니다
- **잘못된 절차 (python_py.gram → python.gram → python_cs.gram)의 흔적**입니다

### 올바른 접근
```csharp
// using 별칭 없이 직접 타입 사용
public static GeneratedMod CreateModule(...)
{
    return new GeneratedMod(...);
}

public static GeneratedStmt CreatePassStatement(...)
{
    return new GeneratedStmt.Pass(...);
}
```

**핵심:**
- python_py.gram을 보고 필요한 AST 타입을 파악합니다
- C 스타일 typedef 흉내를 내지 않고, C# 타입을 직접 사용합니다
- 기존 PyParserHelpers.cs는 백업하고 "참고"만 합니다

---

## Q4: 왜 "간단한 해결법"은 위험한가?

### 문제 상황
사용자가 "지금까지 한 작업에는 매우 많은 문제가 있다"고 지적했을 때, 저는 즉시 8가지 "문제"를 만들어내고 수정 계획을 제시했습니다.

### 무엇이 잘못되었나?
- 실제로 무엇이 문제인지 **이해하지 못한 상태**에서 추측했습니다
- "빠른 수정"을 위해 표면적인 문제를 나열했습니다
- **근본 원인을 분석하지 않았습니다**

### 올바른 접근
1. **먼저 이해하라** - 문제가 무엇인지 정확히 파악
2. **근본 원인을 찾아라** - 왜 그런 문제가 발생했는가?
3. **올바른 해결책을 설계하라** - 임시 방편이 아닌 올바른 방법
4. **계획을 세워라** - 섣부르게 코드를 수정하지 말고 계획 먼저

### 예시: 세 가지 근본 문제

**표면적 증상:**
- KeywordExtractor에 ValidateKeywords() 메서드가 있음
- python_cs.gram에 C 스타일 매크로가 남아있음
- PyParserHelpers.cs에 using 별칭이 있음

**근본 원인:**
1. **KeywordExtractor를 잘못 이해함**
   - "추출"이 아니라 "검증"으로 생각했음
   - 하드코딩된 답을 갖고 있다고 가정했음

2. **잘못된 절차를 따랐음**
   - python_py.gram → python.gram (C) → python_cs.gram
   - 중간에 C 버전을 거쳐서 C 패턴이 남음

3. **"참고"의 의미를 오해함**
   - 참고 = 복사해서 사용
   - 올바른 의미: 참고 = 구조를 이해하기 위해 보기만 함

---

## Q5: "생각을 우선하라"는 무엇을 의미하는가?

### 잘못된 순서
```
문제 발견 → 즉시 코드 수정 → 빌드 → 실패 → 또 수정 → ...
```

### 올바른 순서
```
문제 발견
    ↓
근본 원인 분석 (생각)
    ↓
올바른 해결책 설계 (계획)
    ↓
계획 검토 (사용자와 확인)
    ↓
코드 수정 (실행)
```

### 예시

**잘못된 접근:**
> "KeywordExtractor에 문제가 있네요. ValidateKeywords를 삭제하고 ExtractKeywords로 바꾸겠습니다!"

**올바른 접근:**
> "KeywordExtractor가 왜 필요한지 이해해야 합니다. grammar 파일을 보니 `'keyword'` 형태로 표기되어 있네요. 이것을 추출하는 것이 목적이군요. 하드코딩된 목록이 있으면 안 됩니다. 정규식으로 `'...'` 패턴을 찾아서 추출해야겠습니다. 계획을 세워보겠습니다..."

---

---

## Q6: CPython을 얼마나 추종해야 하는가?

### ✅ 반드시 추종해야 할 것 (MUST Follow)

1. **동작 (Behavior)**
   - 파싱 결과: 동일한 입력에 대해 동일한 AST 구조
   - 바이트코드: `python -m dis`와 동일한 바이트코드 생성
   - 실행 결과: 동일한 Python 코드에 대해 동일한 실행 결과

2. **의미론 (Semantics)**
   - Python 3.12 언어 명세 완전 준수
   - 연산자 우선순위, 스코프 규칙, 타입 시스템
   - 예외 처리, 제너레이터, 코루틴 동작

3. **알고리즘 (Core Algorithms)**
   - PEG 파싱 알고리즘 (left recursion, memoization)
   - 바이트코드 생성 로직
   - 심볼 테이블 구축 방식

### ❌ 추종할 필요 없는 것 (MAY Differ)

1. **C 문법 (C Syntax)**
   ```c
   // CPython (C)
   typedef struct _mod *mod_ty;
   void *ptr = malloc(sizeof(struct _mod));
   ```
   ```csharp
   // SharpPy (C#) - C 문법 따를 필요 없음
   public class GeneratedMod { ... }  // typedef 불필요
   var ptr = new GeneratedMod();      // malloc 불필요
   ```

2. **구현 패턴 (Implementation Patterns)**
   ```c
   // CPython (C)
   #define EXTRA p->start_lineno, p->start_col_offset, ...
   stmt_ty result = _PyAST_Pass(EXTRA);
   ```
   ```csharp
   // SharpPy (C#) - C 매크로 따를 필요 없음
   var result = PyAst.Pass(
       startLine: GetStartLine(),
       startCol: GetStartColumn(),
       ...
   );  // 명명된 인자로 명확하게
   ```

3. **코드 구조 (Code Structure)**
   ```c
   // CPython (C) - Grammar action
   | a=NAME b=['as' z=NAME { z }] {
       _PyAST_alias(a->v.Name.id, b ? b->v.Name.id : NULL, p->arena)
   }
   ```
   ```csharp
   // SharpPy (C#) - Extension method로 더 간결하게
   | a=NAME b=['as' z=NAME { z }] {
       PyAst.Alias(a.GetNameValue(), b.GetNameValue(), EXTRA)
   }
   ```

4. **명명 규칙 (Naming Conventions)**
   - C 스타일 `_PyAST_Pass` vs C# 스타일 `PyAstPass`
   - **둘 다 무방** - 중요한 것은 일관성과 동작

### 💡 설계 원칙

1. **결과 우선 (Result First)**
   - CPython과 **동일한 바이트코드**를 생성하는 것이 목표
   - 과정은 다르더라도 결과가 같으면 성공

2. **C# Idiomatic (Language Best Practices)**
   - C# 언어의 관례와 베스트 프랙티스 적극 활용
   - Extension methods, LINQ, nullable reference types 등
   - C 스타일보다 C# 가독성 우선

3. **가독성 (Readability)**
   - C 코드를 기계적으로 직역하지 않음
   - **의도**를 이해하고 C#답게 표현
   - 복잡한 캐스팅보다 Extension method

4. **유지보수성 (Maintainability)**
   - 코드 중복 제거
   - 적절한 추상화 사용
   - 중앙화된 타입 변환 로직

### 📝 실전 예시

#### 예시 1: Property 접근

**CPython (C):**
```c
// Grammar/python.gram
import_from_as_name[alias_ty]:
    | a=NAME b=['as' z=NAME { z }] {
        _PyAST_alias(a->v.Name.id, b ? b->v.Name.id : NULL, p->arena)
    }
```

**SharpPy (Bad - C 직역):**
```csharp
// Grammar/python_cs.gram
import_from_as_name[GeneratedAlias]:
    | a=NAME b=['as' z=NAME { z }] {
        PyAst.Alias(((GeneratedTokenInfo)a).Value,
                    ((GeneratedTokenInfo?)b)?.Value,
                    EXTRA)
    }
```
→ **문제:** 복잡한 캐스팅 반복, 가독성 저하

**SharpPy (Good - C# Idiomatic):**
```csharp
// Extension method 정의 (한 번만)
public static string GetNameValue(this GeneratedPtr ptr)
    => ((GeneratedTokenInfo)ptr).Value;

// Grammar/python_cs.gram
import_from_as_name[GeneratedAlias]:
    | a=NAME b=['as' z=NAME { z }] {
        PyAst.Alias(a.GetNameValue(), b.GetNameValue(), EXTRA)
    }
```
→ **장점:** 간결, 중앙화된 타입 변환, C# fluent API 스타일

#### 예시 2: 함수 시그니처

**CPython (C):**
```c
// Python-ast.c
alias_ty _PyAST_alias(identifier name, identifier asname, PyArena *arena) {
    alias_ty p = PyArena_Malloc(arena, sizeof(*p));
    p->name = name;
    p->asname = asname;
    return p;
}
```

**SharpPy (유지):**
```csharp
// Parser/AstFactory.cs
public static GeneratedAlias _PyAST_alias(string name, string? asname, ...) {
    return new GeneratedAlias {
        Name = name,
        Asname = asname
    };
}
```
→ **동작 동일, 메모리 관리만 C#답게 (GC 사용)**

#### 예시 3: 복잡한 로직

**CPython (C):**
```c
// Parser/pegen.c
asdl_seq *_PyPegen_seq_flatten(Parser *p, asdl_seq *seqs) {
    Py_ssize_t total_size = 0;
    for (Py_ssize_t i = 0; i < asdl_seq_LEN(seqs); i++) {
        asdl_seq *inner = asdl_seq_GET(seqs, i);
        total_size += asdl_seq_LEN(inner);
    }
    asdl_seq *res = _Py_asdl_generic_seq_new(total_size, p->arena);
    // ... flatten logic
    return res;
}
```

**SharpPy (C#답게 구현):**
```csharp
// Parser/GeneratedParserBridge.cs
public static GeneratedStmtSeq _PyPegen_seq_flatten(List<GeneratedStmtSeq> sequences) {
    var result = new GeneratedStmtSeq();
    foreach (var seq in sequences)
        result.AddRange(seq);  // LINQ-like, 간결
    return result;
}
```
→ **동일한 동작, C# 컬렉션 API 활용**

### ⚖️ 판단 기준

**이렇게 자문하세요:**

1. ❓ "이 코드가 CPython과 다른 결과를 만드는가?"
   - **YES** → ❌ 수정 필요
   - **NO** → ✅ C#답게 작성해도 무방

2. ❓ "C 스타일을 따르면 더 명확해지는가?"
   - **YES** → ⚠️ C 스타일 허용 (예: `_PyAST_*` 함수명)
   - **NO** → ✅ C# 스타일 우선

3. ❓ "이 추상화가 유지보수에 도움이 되는가?"
   - **YES** → ✅ Extension method, Helper 사용
   - **NO** → ⚠️ 과도한 추상화 지양

### 🎯 결론

**"CPython의 정신을 따르되, C의 형식을 강요하지 말라"**

- ✅ 동작, 결과, 알고리즘 = 완벽히 추종
- ✅ 문법, 구현, 스타일 = C#답게 변형
- ✅ 의도 파악 후 최선의 C# 코드 작성
- ❌ 기계적 C→C# 번역 지양

---

## 핵심 교훈

1. **KeywordExtractor**: 답을 갖고 있지 말고, grammar에서 **발견**하라
2. **python_cs.gram**: 잘못된 절차(C 경유)를 따르지 말고, python_py.gram에서 **직접 변환**하라
3. **PyParserHelpers**: C 스타일 흉내를 내지 말고, **C# 스타일로 직접 작성**하라
4. **문제 해결**: 빠른 수정보다 **근본 원인 분석**을 우선하라
5. **개발 순서**: 행동보다 **생각**을 우선하라
6. **CPython 추종**: 결과를 추종하되, 구현은 **C#답게** 작성하라
