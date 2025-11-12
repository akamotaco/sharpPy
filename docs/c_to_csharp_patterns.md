# C to C# Pattern Translation Guide

python_cs.gram 재작성을 위한 C → C# 변환 패턴 가이드

## 📋 목차
1. [개요](#개요)
2. [타입 변환 패턴](#타입-변환-패턴)
3. [함수 호출 패턴](#함수-호출-패턴)
4. [Parser 컨텍스트 패턴](#parser-컨텍스트-패턴)
5. [NULL/null 처리 패턴](#nullnull-처리-패턴)
6. [캐스팅 패턴](#캐스팅-패턴)
7. [@trailer 처리](#trailer-처리)
8. [실제 변환 예시](#실제-변환-예시)

---

## 개요

**핵심 원칙:**
1. **의미 우선**: python.gram (C 버전)에서 **의미(의도)**를 파악한 후 C# 스타일로 구현
2. **기계적 변환 금지**: C 코드를 단순히 텍스트 치환하지 말 것
3. **동작 검증**: CPython 3.12의 실제 동작과 일치하는지 확인
4. **C# 관용구 사용**: C 패턴이 아닌 C# 관용구 사용

**참조 소스:**
- CPython 3.12 C 소스: `C:\Users\m11\Desktop\work\dup\cpython-3.12`
- python.gram (C): `Grammar/python.gram`
- python_py.gram (순수 PEG): `Grammar/python_py.gram`

---

## 타입 변환 패턴

### 1. C 타입 → C# 타입

| C 타입 | python_cs.gram (현재/잘못됨) | 올바른 C# 타입 | 설명 |
|--------|------------------------------|----------------|------|
| `stmt_ty` | `stmt_ty` | `GeneratedStmt` | 단일 statement |
| `expr_ty` | `expr_ty` | `GeneratedExpr` | 단일 expression |
| `mod_ty` | `mod_ty` | `GeneratedMod` | Module/Interactive/Expression |
| `asdl_stmt_seq*` | `asdl_stmt_seq*` | `List<GeneratedStmt>` | statement 시퀀스 |
| `asdl_expr_seq*` | `asdl_expr_seq*` | `List<GeneratedExpr>` | expression 시퀀스 |
| `asdl_identifier_seq*` | `asdl_identifier_seq*` | `List<string>` | 식별자 시퀀스 |
| `arguments_ty` | `arguments_ty` | `GeneratedArguments` | 함수 파라미터 |
| `arg_ty` | `arg_ty` | `GeneratedArg` | 단일 파라미터 |
| `alias_ty` | `alias_ty` | `GeneratedAlias` | import alias |
| `keyword_ty` | `keyword_ty` | `GeneratedKeyword` | keyword argument |
| `withitem_ty` | `withitem_ty` | `GeneratedWithitem` | with item |
| `match_case_ty` | `match_case_ty` | `GeneratedMatchCase` | match case |
| `pattern_ty` | `pattern_ty` | `GeneratedPattern` | pattern matching |
| `comprehension_ty` | `comprehension_ty` | `GeneratedComprehension` | comprehension |
| `excepthandler_ty` | `excepthandler_ty` | `GeneratedExceptHandler` | except handler |
| `type_param_ty` | `type_param_ty` | `GeneratedTypeParam` | type parameter (3.12+) |

**원칙:**
- `*_ty`는 단일 항목 → `Generated*`
- `asdl_*_seq*`는 시퀀스 → `List<Generated*>` 또는 커스텀 시퀀스 타입

### 2. 특수 타입

| C 타입 | C# 타입 | 설명 |
|--------|---------|------|
| `Token*` | `GeneratedTokenInfo` | 토큰 |
| `void*` | `object` 또는 `GeneratedPtr?` | 제네릭 포인터 (context-dependent) |
| `NULL` | `null` | null 참조 |
| `int` | `int` | 동일 |
| `PyObject*` | `PyObject` | Python 객체 |
| `PyArena*` | (불필요) | C#에서는 GC가 메모리 관리 |

---

## 함수 호출 패턴

### 1. AST 생성 함수

#### `_PyAST_*` 패턴

**C (python.gram):**
```c
'pass' { _PyAST_Pass(EXTRA) }
'break' { _PyAST_Break(EXTRA) }
'continue' { _PyAST_Continue(EXTRA) }
```

**python_cs.gram (현재/잘못됨):**
```csharp
'pass' { _PyAST_Pass(EXTRA) }  // C 함수명 그대로
```

**올바른 C# (python_cs.gram):**
```csharp
'pass' { PyAst.Pass(EXTRA) }
'break' { PyAst.Break(EXTRA) }
'continue' { PyAst.Continue(EXTRA) }
```

**의미:**
- `_PyAST_Pass(EXTRA)`: 위치 정보(line, column)를 포함한 Pass statement AST 노드 생성
- `EXTRA`는 매크로로 `startLine, startCol, endLine, endCol` 전달

**C# 구현 위치:** `PyAst.cs` 헬퍼 클래스

#### `_PyAST_*` 위치 정보 패턴

**C:**
```c
_PyAST_Return(a, EXTRA)
_PyAST_Assign(targets, value, type_comment, EXTRA)
```

**C#:**
```csharp
PyAst.Return(a, EXTRA)
PyAst.Assign(targets, value, typeComment, EXTRA)
```

**주의사항:**
- C의 `type_comment` → C#의 `typeComment` (camelCase)
- C의 `NULL` → C#의 `null`
- C의 `p->arena` → C#에서는 불필요 (GC가 처리)

### 2. Helper 함수 (_PyPegen_*)

#### Sequence 조작 함수

**C (python.gram):**
```c
statements[asdl_stmt_seq*]: a=statement+ {
    (asdl_stmt_seq*)_PyPegen_seq_flatten(p, a)
}
```

**python_cs.gram (현재/잘못됨):**
```csharp
statements[asdl_stmt_seq*]: a=statement+ {
    _PyPegen_seq_flatten(a)  // p 제거했지만 여전히 C 함수명
}
```

**올바른 C# (python_cs.gram):**
```csharp
statements: a=statement+ {
    PyParserHelpers.FlattenStatementSequence(a)
}
```

**의미:**
- `_PyPegen_seq_flatten`: 중첩된 statement 리스트를 평탄화 (flatten)
  - 예: `[[stmt1, stmt2], [stmt3]]` → `[stmt1, stmt2, stmt3]`

**주요 Helper 함수 변환표:**

| C 함수 | 의미 | C# 함수 |
|--------|------|---------|
| `_PyPegen_seq_flatten(p, a)` | 시퀀스 평탄화 | `PyParserHelpers.FlattenSequence(a)` |
| `_PyPegen_singleton_seq(p, a)` | 단일 항목 시퀀스 생성 | `PyParserHelpers.SingletonSequence(a)` |
| `_PyPegen_seq_insert_in_front(p, a, b)` | 시퀀스 앞에 삽입 | `PyParserHelpers.InsertInFront(a, b)` |
| `_PyPegen_seq_append_to_end(p, a, b)` | 시퀀스 끝에 추가 | `PyParserHelpers.AppendToEnd(a, b)` |
| `_PyPegen_join_sequences(p, a, b)` | 두 시퀀스 결합 | `PyParserHelpers.JoinSequences(a, b)` |
| `_PyPegen_set_expr_context(p, a, Store)` | 표현식 context 설정 | `PyParserHelpers.SetExprContext(a, ExprContext.Store)` |
| `_PyPegen_make_module(p, a)` | 모듈 생성 | `PyParserHelpers.MakeModule(a)` |
| `_PyPegen_make_arguments(p, ...)` | arguments_ty 생성 | `PyParserHelpers.MakeArguments(...)` |
| `_PyPegen_map_names_to_ids(p, a)` | NAME 토큰 → 식별자 변환 | `PyParserHelpers.MapNamesToIds(a)` |

**구현 원칙:**
1. 첫 번째 파라미터 `p` (Parser 포인터) 제거 → C#에서는 인스턴스 메서드 또는 static 메서드
2. C 함수명 제거 (`_PyPegen_` prefix 제거)
3. 의미에 맞는 C# 네이밍 (CamelCase, 서술적)

### 3. CHECK 매크로 패턴

**C (python.gram):**
```c
a=NAME ':' b=expression c=['=' d=annotated_rhs { d }] {
    CHECK_VERSION(
        stmt_ty,
        6,
        "Variable annotation syntax is",
        _PyAST_AnnAssign(CHECK(expr_ty, _PyPegen_set_expr_context(p, a, Store)), b, c, 1, EXTRA)
    )
}
```

**의미:**
- `CHECK(type, expr)`: expr이 null이면 파싱 실패, 아니면 type으로 캐스트
- `CHECK_VERSION(type, version, msg, expr)`: Python 버전 체크, 낮으면 에러

**python_cs.gram (현재/잘못됨):**
```csharp
CHECK<expr_ty>(_PyPegen_set_expr_context(a, Store))  // 제네릭으로 변환했지만 여전히 C 패턴
```

**올바른 C# (python_cs.gram):**
```csharp
a=NAME ':' b=expression c=['=' d=annotated_rhs { d }] {
    CheckVersion(
        6,
        "Variable annotation syntax is",
        PyAst.AnnAssign(
            Check<GeneratedExpr>(PyParserHelpers.SetExprContext(a, ExprContext.Store)),
            b,
            c,
            isSimple: true,
            EXTRA
        )
    )
}
```

**CHECK 매크로 변환:**

| C 매크로 | C# 메서드 | 설명 |
|----------|-----------|------|
| `CHECK(type, expr)` | `Check<T>(expr)` | null 체크 + 캐스트 |
| `CHECK_VERSION(type, ver, msg, expr)` | `CheckVersion(ver, msg, expr)` | 버전 체크 |
| `CHECK_NULL_ALLOWED(type, expr)` | `expr` (그대로) | null 허용 |

---

## Parser 컨텍스트 패턴

### C의 Parser 포인터 (`p`)

**C (python.gram):**
```c
// 모든 helper 함수에 p 전달
_PyPegen_seq_flatten(p, a)
_PyAST_Pass(EXTRA)  // EXTRA는 p->tok->lineno, p->tok->col_offset 등 참조

// p 사용 예시
p->arena          // 메모리 allocator
p->keywords       // 키워드 목록
p->mark           // 현재 파싱 위치
p->tokens         // 토큰 배열
```

**C# (python_cs.gram):**
```csharp
// p는 암묵적으로 parser 인스턴스 (_this, this, 또는 base class)
PyParserHelpers.FlattenSequence(a)  // p 불필요
PyAst.Pass(EXTRA)  // EXTRA는 GetStartLine(), GetStartColumn() 등 인스턴스 메서드 사용

// 인스턴스 멤버 접근
_tokens           // private field
_position         // private field
_mark             // private field
```

**원칙:**
- C의 `p->` 패턴 → C# 인스턴스 필드/프로퍼티
- C의 함수 첫 파라미터 `Parser *p` → C#에서는 제거 (인스턴스 메서드)

---

## NULL/null 처리 패턴

### 1. NULL 상수

**C:**
```c
_PyAST_Raise(NULL, NULL, EXTRA)  // no exception, no cause
_PyAST_ImportFrom(NULL, b, count, EXTRA)  // no module name (relative import)
```

**C# (현재/잘못됨):**
```csharp
_PyAST_Raise(null, null, EXTRA)  // null은 맞지만 여전히 C 함수명
```

**C# (올바름):**
```csharp
PyAst.Raise(null, null, EXTRA)
PyAst.ImportFrom(null, b, count, EXTRA)
```

### 2. Optional 값 처리

**C:**
```c
a=['=' d=annotated_rhs { d }]  // a는 optional, 없으면 NULL

// 사용
(a) ? a : NULL
(a) ? ((expr_ty)a)->v.Name.id : NULL
```

**C# (현재/잘못됨):**
```csharp
b?.Value  // nullable operator는 맞지만 아직 부족
```

**C# (올바름):**
```csharp
a=['=' d=annotated_rhs { d }]  // a는 GeneratedExpr?

// 사용
a ?? null  // 또는 그냥 a
a is GeneratedName name ? name.Id : null
```

### 3. Ternary operator

**C:**
```c
(params) ? params : CHECK(arguments_ty, _PyPegen_empty_arguments(p))
```

**C# (현재/잘못됨):**
```csharp
(params) ? params : CHECK<arguments_ty>(_PyPegen_empty_arguments())
```

**C# (올바름):**
```csharp
params ?? Check<GeneratedArguments>(PyParserHelpers.EmptyArguments())
// 또는
params != null ? params : Check<GeneratedArguments>(PyParserHelpers.EmptyArguments())
```

---

## 캐스팅 패턴

### 1. C의 명시적 캐스트

**C:**
```c
statements[asdl_stmt_seq*]: a=statement+ {
    (asdl_stmt_seq*)_PyPegen_seq_flatten(p, a)
}

statement[asdl_stmt_seq*]:
    | a=compound_stmt { (asdl_stmt_seq*)_PyPegen_singleton_seq(p, a) }
```

**의미:**
- `_PyPegen_seq_flatten`는 `asdl_seq*` 반환 → `asdl_stmt_seq*`로 캐스트
- C에서는 타입 안전성 없음 (void* 기반)

**C# (현재/잘못됨):**
```csharp
statements[asdl_stmt_seq*]: a=statement+ {
    _PyPegen_seq_flatten(a)  // 캐스트 제거했지만 여전히 C 패턴
}
```

**C# (올바름):**
```csharp
statements: a=statement+ {
    PyParserHelpers.FlattenStatementSequence(a)  // 반환 타입이 이미 List<GeneratedStmt>
}

statement:
    | a=compound_stmt { PyParserHelpers.SingletonSequence(a) }
```

**원칙:**
- C#은 제네릭으로 타입 안전성 보장 → 명시적 캐스트 불필요
- 함수 시그니처가 이미 올바른 타입 반환

### 2. C의 타입 변환

**C:**
```c
// NAME 토큰에서 식별자 추출
a->v.Name.id

// expression에서 Call 추출
((expr_ty)b)->v.Call.args
```

**C# (현재/잘못됨):**
```csharp
a.Id  // 부분적으로 개선됨
((GeneratedCall)b).Args  // C 스타일 캐스트
```

**C# (올바름):**
```csharp
// NAME 토큰 → 식별자
(a as GeneratedName)?.Id  // safe cast
// 또는
((GeneratedName)a).Id  // 확실한 경우

// expression → Call
(b as GeneratedCall)?.Args
// 또는 pattern matching
b is GeneratedCall call ? call.Args : null
```

---

## @trailer 처리

### 문제

**현재 python_cs.gram:**
```csharp
@trailer '''
// CPython 3.12: Entry point for file parsing
public GeneratedMod ParseFile()
{
    var result = File();
    // ...
}
'''
```

**에러:**
```
[ERROR] Expected ':' after rule name 'trailer'
```

**원인:** 현재 PEG Generator가 `@trailer` 문법 지원 안 함

### 해결 방안

#### Option 1: @trailer 제거 + 수동 구현 (권장)

`@trailer` 코드를 `PyParserBase.cs` 또는 `PyParser.cs`의 partial class에 수동 추가:

**python_cs.gram:**
```csharp
# @trailer 제거
# ========================= START OF THE GRAMMAR =========================

file: a=[statements] ENDMARKER { PyParserHelpers.MakeModule(a) }
...
```

**PyParser.cs (또는 PyParserBase.cs partial class):**
```csharp
public partial class PyParser
{
    // Entry points (원래 @trailer에 있던 코드)
    public GeneratedMod ParseFile()
    {
        var result = File();
        if (result == null || _pendingSyntaxError != null)
        {
            // ... 에러 처리
        }
        return result;
    }

    public GeneratedMod ParseInteractive() { ... }
    public GeneratedMod ParseEval() { ... }
    public GeneratedMod ParseFuncType() { ... }
}
```

#### Option 2: PEG Generator에 @trailer 지원 추가

`GrammarReaderV2.cs`와 `ParserGenerator.cs` 수정하여 `@trailer` 지원:

**장점:**
- python.gram과 구조 일치

**단점:**
- Generator 수정 필요 (복잡도 증가)
- 당장은 불필요한 작업

**결론:** Option 1 권장 (수동 구현)

---

## 실제 변환 예시

### 예시 1: Simple Statement

#### python_py.gram (순수 PEG, action code 없음)
```python
simple_stmt:
    | 'pass'
    | 'break'
    | 'continue'
```

#### python.gram (C, action code 포함)
```c
simple_stmt[stmt_ty] (memo):
    | 'pass' { _PyAST_Pass(EXTRA) }
    | 'break' { _PyAST_Break(EXTRA) }
    | 'continue' { _PyAST_Continue(EXTRA) }
```

**의미 파악:**
- `'pass'` 키워드 → Pass statement AST 노드 생성
- `EXTRA`는 위치 정보 (line, column)

#### python_cs.gram (현재/잘못됨)
```csharp
simple_stmt[stmt_ty] (memo):
    | 'pass' { _PyAST_Pass(EXTRA) }  // C 함수명 그대로
    | 'break' { _PyAST_Break(EXTRA) }
    | 'continue' { _PyAST_Continue(EXTRA) }
```

#### python_cs.gram (올바름)
```csharp
simple_stmt (memo):
    | 'pass' { PyAst.Pass(EXTRA) }
    | 'break' { PyAst.Break(EXTRA) }
    | 'continue' { PyAst.Continue(EXTRA) }
```

**변경사항:**
1. `[stmt_ty]` 제거 (C# 제네릭 메서드에서 자동 추론)
2. `_PyAST_Pass` → `PyAst.Pass` (C# 네이밍)

---

### 예시 2: Statements (Sequence Flattening)

#### python_py.gram
```python
statements: statement+
```

#### python.gram (C)
```c
statements[asdl_stmt_seq*]: a=statement+ {
    (asdl_stmt_seq*)_PyPegen_seq_flatten(p, a)
}
```

**의미:**
- `statement+`는 중첩된 리스트 반환 가능 (compound_stmt vs simple_stmts)
- `_PyPegen_seq_flatten`: 중첩 제거 → 평탄한 statement 리스트

**예:**
```python
# Input
if True:
    x = 1; y = 2  # simple_stmts: [Assign(x, 1), Assign(y, 2)]
    pass          # compound_stmt: [Pass()]

# Before flatten: [[Assign(x, 1), Assign(y, 2)], [Pass()]]
# After flatten:  [Assign(x, 1), Assign(y, 2), Pass()]
```

#### python_cs.gram (현재/잘못됨)
```csharp
statements[asdl_stmt_seq*]: a=statement+ { _PyPegen_seq_flatten(a) }
```

#### python_cs.gram (올바름)
```csharp
statements: a=statement+ { PyParserHelpers.FlattenStatementSequence(a) }
```

**PyParserHelpers.cs 구현:**
```csharp
public static List<GeneratedStmt> FlattenStatementSequence(object input)
{
    var result = new List<GeneratedStmt>();

    if (input is List<object> list)
    {
        foreach (var item in list)
        {
            if (item is GeneratedStmt stmt)
                result.Add(stmt);
            else if (item is List<GeneratedStmt> stmts)
                result.AddRange(stmts);
        }
    }
    else if (input is GeneratedStmt stmt)
    {
        result.Add(stmt);
    }

    return result;
}
```

---

### 예시 3: Assignment (복잡한 예시)

#### python_py.gram
```python
assignment:
    | NAME ':' expression ['=' annotated_rhs]
    | star_targets '=' (yield_expr | star_expressions)
```

#### python.gram (C)
```c
assignment[stmt_ty]:
    | a=NAME ':' b=expression c=['=' d=annotated_rhs { d }] {
        CHECK_VERSION(
            stmt_ty,
            6,
            "Variable annotation syntax is",
            _PyAST_AnnAssign(
                CHECK(expr_ty, _PyPegen_set_expr_context(p, a, Store)),
                b,
                c,
                1,
                EXTRA
            )
        )
    }
    | a[asdl_expr_seq*]=(z=star_targets '=' { z })+ b=(yield_expr | star_expressions) !'=' tc=[TYPE_COMMENT] {
         _PyAST_Assign(a, b, NEW_TYPE_COMMENT(p, tc), EXTRA)
    }
```

**의미:**
1. **첫 번째 alternative (annotated assignment):**
   - `x: int` 또는 `x: int = 5`
   - `_PyPegen_set_expr_context(p, a, Store)`: NAME을 Store context로 변환 (할당 대상)
   - `CHECK_VERSION`: Python 3.6+ 문법 체크
   - `c`는 optional (초기값), 있으면 1 (simple), 없으면 0

2. **두 번째 alternative (regular assignment):**
   - `x = y = 5` (multiple targets)
   - `a`는 target 리스트, `b`는 value
   - `NEW_TYPE_COMMENT`: type comment 처리

#### python_cs.gram (현재/잘못됨)
```csharp
assignment[stmt_ty]:
    | a=NAME ':' b=expression c=['=' d=annotated_rhs { d }] {
        CHECK_VERSION(
            stmt_ty,
            6,
            "Variable annotation syntax is",
            _PyAST_AnnAssign(CHECK<expr_ty>(_PyPegen_set_expr_context(a, Store)), b, c, 1, EXTRA)
        )
    }
    | a[asdl_expr_seq*]=(z=star_targets '=' { z })+ b=(yield_expr | star_expressions) !'=' tc=[TYPE_COMMENT] {
         _PyAST_Assign(a, b, tc?.Value, EXTRA)
    }
```

**부분적 개선:**
- `CHECK<expr_ty>`: 제네릭으로 변환
- `tc?.Value`: nullable operator 사용
- `NEW_TYPE_COMMENT(p, tc)` → `tc?.Value`: 단순화

**여전히 문제:**
- C 함수명 (`_PyAST_AnnAssign`, `_PyPegen_set_expr_context`)
- C 타입 (`stmt_ty`, `expr_ty`, `asdl_expr_seq*`)

#### python_cs.gram (올바름)
```csharp
assignment:
    | a=NAME ':' b=expression c=['=' d=annotated_rhs { d }] {
        CheckVersion(
            6,
            "Variable annotation syntax is",
            PyAst.AnnAssign(
                Check<GeneratedExpr>(PyParserHelpers.SetExprContext(a, ExprContext.Store)),
                b,
                c,
                isSimple: true,
                EXTRA
            )
        )
    }
    | a=(z=star_targets '=' { z })+ b=(yield_expr | star_expressions) !'=' tc=[TYPE_COMMENT] {
         PyAst.Assign(a, b, tc?.Value, EXTRA)
    }
```

**변경사항:**
1. `[stmt_ty]` 제거
2. `CHECK_VERSION(stmt_ty, ...)` → `CheckVersion(...)`
3. `_PyAST_AnnAssign` → `PyAst.AnnAssign`
4. `CHECK(expr_ty, ...)` → `Check<GeneratedExpr>(...)`
5. `_PyPegen_set_expr_context` → `PyParserHelpers.SetExprContext`
6. `a[asdl_expr_seq*]=` → `a=` (타입 제거)
7. `1` → `isSimple: true` (named parameter로 명확화)

---

## 체크리스트

python_cs.gram 재작성 시 다음 항목 확인:

### 타입
- [ ] `*_ty` → `Generated*` 변환
- [ ] `asdl_*_seq*` → `List<Generated*>` 변환
- [ ] `NULL` → `null` 변환
- [ ] Rule 정의에서 `[return_type]` 제거 (C# 자동 추론)

### 함수
- [ ] `_PyAST_*` → `PyAst.*` 변환
- [ ] `_PyPegen_*` → `PyParserHelpers.*` 변환
- [ ] 모든 함수에서 첫 번째 파라미터 `p` 제거
- [ ] `p->arena` 제거 (C# GC)

### 매크로
- [ ] `CHECK(type, expr)` → `Check<T>(expr)` 변환
- [ ] `CHECK_VERSION(type, ver, msg, expr)` → `CheckVersion(ver, msg, expr)` 변환
- [ ] `EXTRA` → `EXTRA` (동일, 하지만 구현은 다름)
- [ ] `NEW_TYPE_COMMENT(p, tc)` → `tc?.Value` 변환

### 캐스트
- [ ] C 스타일 캐스트 `(type)expr` → `expr as Type` 또는 `(Type)expr` (확실한 경우)
- [ ] `a->v.Name.id` → `((GeneratedName)a).Id` 또는 `(a as GeneratedName)?.Id`

### @trailer
- [ ] `@trailer` 제거하고 entry point 메서드를 partial class에 수동 추가

### 네이밍
- [ ] C 스타일 snake_case → C# CamelCase
- [ ] `type_comment` → `typeComment`
- [ ] 의미에 맞는 서술적 이름 사용

### 동작 검증
- [ ] CPython 3.12 소스 코드와 동작 비교
- [ ] 테스트 케이스 실행 확인

---

## 참고 자료

1. **CPython 3.12 소스 코드:**
   - `C:\Users\m11\Desktop\work\dup\cpython-3.12\Parser\pegen.c`: Pegen helper 함수
   - `C:\Users\m11\Desktop\work\dup\cpython-3.12\Parser\Python.asdl`: AST 정의
   - `C:\Users\m11\Desktop\work\dup\cpython-3.12\Python\ast.c`: AST 생성 함수

2. **SharpPy 참조 파일:**
   - `Generated/AstTypes.cs`: AST 타입 정의
   - `runtime/PyParserHelpers.cs`: Helper 함수 구현
   - `runtime/PyAst.cs`: AST 생성 함수

3. **PEG 문법:**
   - PEP 617: Parsing Expression Grammar
   - `Grammar/python_py.gram`: 순수 PEG (action code 없음)
   - `Grammar/python.gram`: C action code
   - `Grammar/python_cs.gram`: C# action code (재작성 대상)

---

## 다음 단계

1. **python_cs.gram 재작성:**
   - 한 섹션씩 (SIMPLE STATEMENTS, COMPOUND STATEMENTS, EXPRESSIONS 등)
   - 각 섹션마다 빌드 + 테스트

2. **PyParserHelpers.cs 구현:**
   - 필요한 helper 함수 추가
   - CPython 동작과 일치 확인

3. **PyAst.cs 구현:**
   - AST 생성 함수 추가
   - 위치 정보 올바르게 전달

4. **테스트:**
   - `test_python312_*.py` 실행
   - CPython 3.12와 바이트코드 비교
