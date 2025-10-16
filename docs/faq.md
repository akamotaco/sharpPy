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

## 핵심 교훈

1. **KeywordExtractor**: 답을 갖고 있지 말고, grammar에서 **발견**하라
2. **python_cs.gram**: 잘못된 절차(C 경유)를 따르지 말고, python_py.gram에서 **직접 변환**하라
3. **PyParserHelpers**: C 스타일 흉내를 내지 말고, **C# 스타일로 직접 작성**하라
4. **문제 해결**: 빠른 수정보다 **근본 원인 분석**을 우선하라
5. **개발 순서**: 행동보다 **생각**을 우선하라
