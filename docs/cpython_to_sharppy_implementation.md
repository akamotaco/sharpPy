# CPython 3.12 → SharpPy C# 구현 가이드

## 📋 목차
1. [CPython 3.12 구조 분석](#cpython-312-구조-분석)
2. [SharpPy 현재 구조](#sharppy-현재-구조)
3. [핵심 매핑 관계](#핵심-매핑-관계)
4. [구현 계획](#구현-계획)
5. [참조 자료](#참조-자료)

---

## CPython 3.12 구조 분석

### 디렉토리 구조
```
cpython-3.12/
├── Parser/
│   ├── pegen.h              # Parser 구조체, 매크로 정의
│   ├── pegen.c              # 파서 엔진 (memoization, lookahead)
│   ├── action_helpers.c     # 액션 헬퍼 함수들
│   ├── parser.c             # python.gram에서 생성된 파서
│   ├── pegen_errors.c       # 에러 처리
│   └── Python.asdl          # AST 정의
├── Grammar/
│   └── python.gram          # PEG 문법 (193개 규칙)
└── Include/internal/
    └── pycore_ast.h         # AST 타입 정의 (자동 생성)
```

### 핵심 파일별 역할

#### 1. Parser/pegen.h
**역할:** 파서 구조체, 매크로, 헬퍼 함수 선언

**주요 정의:**
```c
// Parser 구조체 (lines 60-83)
typedef struct {
    struct tok_state *tok;
    Token **tokens;
    int mark;                    // 현재 파싱 위치
    int fill, size;
    PyArena *arena;              // 메모리 관리
    KeywordToken **keywords;
    char **soft_keywords;
    int n_keyword_lists;
    int start_rule;
    int *errcode;
    int parsing_started;
    PyObject* normalize;
    int starting_lineno;
    int starting_col_offset;
    int error_indicator;         // 에러 발생 플래그
    int flags;
    int feature_version;         // Python 버전 체크용
    growable_comment_array type_ignore_comments;
    Token *known_err_token;
    int level;                   // 재귀 깊이
    int call_invalid_rules;      // invalid_* 규칙 호출 여부
    int debug;
} Parser;

// EXTRA 매크로 - AST 노드 위치 정보 (line 258)
#define EXTRA _start_lineno, _start_col_offset, _end_lineno, _end_col_offset, p->arena

// CHECK 매크로 - 에러 처리 (line 222)
#define CHECK(type, result) ((type) CHECK_CALL(p, result))

// CHECK_VERSION 매크로 - 버전 체크 (line 296)
#define CHECK_VERSION(type, version, msg, node) \
    ((type) INVALID_VERSION_CHECK(p, version, msg, node))

// 에러 발생 매크로들 (lines 190-199)
#define RAISE_SYNTAX_ERROR(msg, ...) \
    _PyPegen_raise_error(p, PyExc_SyntaxError, 0, msg, ##__VA_ARGS__)

#define RAISE_SYNTAX_ERROR_KNOWN_LOCATION(a, msg, ...) \
    RAISE_ERROR_KNOWN_LOCATION(p, PyExc_SyntaxError, \
        (a)->lineno, (a)->col_offset, (a)->end_lineno, (a)->end_col_offset, msg, ##__VA_ARGS__)

#define RAISE_SYNTAX_ERROR_STARTING_FROM(a, msg, ...) \
    RAISE_ERROR_KNOWN_LOCATION(p, PyExc_SyntaxError, \
        (a)->lineno, (a)->col_offset, CURRENT_POS, CURRENT_POS, msg, ##__VA_ARGS__)
```

**Helper 구조체들 (lines 85-120):**
```c
typedef struct {
    cmpop_ty cmpop;
    expr_ty expr;
} CmpopExprPair;

typedef struct {
    expr_ty key;
    expr_ty value;
} KeyValuePair;

typedef struct {
    expr_ty key;
    pattern_ty pattern;
} KeyPatternPair;

typedef struct {
    arg_ty arg;
    expr_ty value;
} NameDefaultPair;

typedef struct {
    asdl_arg_seq *plain_names;
    asdl_seq *names_with_defaults;
} SlashWithDefault;

typedef struct {
    arg_ty vararg;
    asdl_seq *kwonlyargs;
    arg_ty kwarg;
} StarEtc;

typedef struct { operator_ty kind; } AugOperator;

typedef struct {
    void *element;
    int is_keyword;
} KeywordOrStarred;
```

#### 2. Parser/action_helpers.c
**역할:** 액션 코드에서 사용하는 헬퍼 함수들

**주요 함수들:**
```c
// 시퀀스 조작 (lines 14-62)
asdl_seq *_PyPegen_singleton_seq(Parser *p, void *a);
asdl_seq *_PyPegen_seq_insert_in_front(Parser *p, void *a, asdl_seq *seq);
asdl_seq *_PyPegen_seq_append_to_end(Parser *p, asdl_seq *seq, void *a);
asdl_seq *_PyPegen_seq_flatten(Parser *p, asdl_seq *seqs);

// 시퀀스 접근 (lines 97-104)
void *_PyPegen_seq_last_item(asdl_seq *seq);
void *_PyPegen_seq_first_item(asdl_seq *seq);

// 이름 처리
expr_ty _PyPegen_join_names_with_dot(Parser *p, expr_ty, expr_ty);
asdl_identifier_seq *_PyPegen_map_names_to_ids(Parser *p, asdl_expr_seq *);

// Arguments 생성
arguments_ty _PyPegen_make_arguments(Parser *, asdl_arg_seq *, SlashWithDefault *,
                                     asdl_arg_seq *, asdl_seq *, StarEtc *);
arguments_ty _PyPegen_empty_arguments(Parser *);

// 비교 연산자
CmpopExprPair *_PyPegen_cmpop_expr_pair(Parser *, cmpop_ty, expr_ty);
asdl_int_seq *_PyPegen_get_cmpops(Parser *p, asdl_seq *);
asdl_expr_seq *_PyPegen_get_exprs(Parser *, asdl_seq *);

// 딕셔너리/패턴
KeyValuePair *_PyPegen_key_value_pair(Parser *, expr_ty, expr_ty);
asdl_expr_seq *_PyPegen_get_keys(Parser *, asdl_seq *);
asdl_expr_seq *_PyPegen_get_values(Parser *, asdl_seq *);
KeyPatternPair *_PyPegen_key_pattern_pair(Parser *, expr_ty, pattern_ty);
asdl_expr_seq *_PyPegen_get_pattern_keys(Parser *, asdl_seq *);
asdl_pattern_seq *_PyPegen_get_patterns(Parser *, asdl_seq *);

// 문자열 처리
expr_ty _PyPegen_constant_from_token(Parser* p, Token* tok);
expr_ty _PyPegen_decoded_constant_from_token(Parser* p, Token* tok);
expr_ty _PyPegen_constant_from_string(Parser* p, Token* tok);
expr_ty _PyPegen_concatenate_strings(Parser *p, asdl_expr_seq *, int, int, int, int, PyArena *);

// 함수 정의
stmt_ty _PyPegen_function_def_decorators(Parser *, asdl_expr_seq *, stmt_ty);
stmt_ty _PyPegen_class_def_decorators(Parser *, asdl_expr_seq *, stmt_ty);

// 함수 호출
KeywordOrStarred *_PyPegen_keyword_or_starred(Parser *, void *, int);
asdl_expr_seq *_PyPegen_seq_extract_starred_exprs(Parser *, asdl_seq *);
asdl_keyword_seq *_PyPegen_seq_delete_starred_exprs(Parser *, asdl_seq *);
expr_ty _PyPegen_collect_call_seqs(Parser *, asdl_expr_seq *, asdl_seq *,
                     int lineno, int col_offset, int end_lineno,
                     int end_col_offset, PyArena *arena);
```

#### 3. Include/internal/pycore_ast.h
**역할:** AST 타입 정의 (Parser/asdl_c.py에서 자동 생성)

**Enum 타입들 (lines 21-32):**
```c
// 표현식 컨텍스트 (Load, Store, Del)
typedef enum _expr_context {
    Load=1, Store=2, Del=3
} expr_context_ty;

// Boolean 연산자 (And, Or)
typedef enum _boolop {
    And=1, Or=2
} boolop_ty;

// 이항 연산자
typedef enum _operator {
    Add=1, Sub=2, Mult=3, MatMult=4, Div=5, Mod=6, Pow=7,
    LShift=8, RShift=9, BitOr=10, BitXor=11, BitAnd=12,
    FloorDiv=13
} operator_ty;

// 단항 연산자
typedef enum _unaryop {
    Invert=1, Not=2, UAdd=3, USub=4
} unaryop_ty;

// 비교 연산자
typedef enum _cmpop {
    Eq=1, NotEq=2, Lt=3, LtE=4, Gt=5, GtE=6, Is=7, IsNot=8,
    In=9, NotIn=10
} cmpop_ty;
```

**중요:** 이것들은 enum **값**으로 사용됨. 타입이 아님!
```c
// 올바른 사용
_PyAST_BinOp(a, Add, b, EXTRA)  // Add는 operator_ty의 enum 값

// 잘못된 사용 (C#에서 흔한 실수)
PyAst.BinOp(a, Add, b, EXTRA)  // C#: 'Add' is a type, not valid
```

#### 4. Grammar/python.gram
**역할:** Python 문법 정의 (PEG 형식)

**문법 패턴 예시:**
```python
# 단순 구문 (line 113)
simple_stmt[stmt_ty]:
    | e=star_expressions { _PyAST_Expr(e, EXTRA) }
    | 'pass' { _PyAST_Pass(EXTRA) }
    | 'break' { _PyAST_Break(EXTRA) }
    | 'continue' { _PyAST_Continue(EXTRA) }

# 이항 연산 (lines 786-793)
sum[expr_ty]:
    | a=sum '+' b=term { _PyAST_BinOp(a, Add, b, EXTRA) }
    | a=sum '-' b=term { _PyAST_BinOp(a, Sub, b, EXTRA) }
    | term

term[expr_ty]:
    | a=term '*' b=factor { _PyAST_BinOp(a, Mult, b, EXTRA) }
    | a=term '/' b=factor { _PyAST_BinOp(a, Div, b, EXTRA) }
    | a=term '//' b=factor { _PyAST_BinOp(a, FloorDiv, b, EXTRA) }
    | factor

# 함수 정의 (lines 268-276)
function_def[stmt_ty]:
    | d=decorators f=function_def_raw { _PyPegen_function_def_decorators(p, d, f) }
    | function_def_raw

function_def_raw[stmt_ty]:
    | 'def' n=NAME t=[type_params] &&'(' params=[params] ')' a=['->' z=expression { z }] &&':' tc=[func_type_comment] b=block {
        _PyAST_FunctionDef(n->v.Name.id,
                          (params) ? params : CHECK(arguments_ty, _PyPegen_empty_arguments(p)),
                          b, NULL, a, tc, t, EXTRA) }

# 에러 처리 (lines 647-657)
type_param[type_param_ty] (memo):
    | a=NAME b=[type_param_bound] { _PyAST_TypeVar(a->v.Name.id, b, EXTRA) }
    | '*' a=NAME colon=':' e=expression {
            RAISE_SYNTAX_ERROR_STARTING_FROM(colon, e->kind == Tuple_kind
                ? "cannot use constraints with TypeVarTuple"
                : "cannot use bound with TypeVarTuple")
        }
```

**핵심 패턴:**
1. `{ }` 안의 코드가 액션 코드 (C 코드)
2. `_PyAST_*` 함수로 AST 노드 생성
3. `EXTRA` 매크로로 위치 정보 전달
4. `CHECK()` 매크로로 에러 처리
5. `RAISE_SYNTAX_ERROR*` 매크로로 에러 발생

---

## SharpPy 현재 구조

### 디렉토리 구조
```
SharpPy/
├── Parser/
│   ├── PyParserBase.cs      # 파서 기본 기능
│   └── PyParserHelpers.cs   # AST 팩토리 함수들
├── Grammar/
│   ├── python_cs.gram       # C# 스타일 문법 (194개 규칙)
│   ├── Python.asdl          # CPython과 동일
│   └── Tokens               # 토큰 정의
├── Generated/
│   ├── PyParser.cs          # python_cs.gram에서 생성 (9021줄)
│   ├── AstTypes.cs          # Python.asdl에서 생성
│   └── PyTokens.cs          # Tokens에서 생성
└── SharpPy.PegGenerator/
    ├── Readers/
    │   ├── GrammarReaderV2.cs    # grammar 파서
    │   └── GrammarTokenizer.cs   # grammar 토크나이저
    └── Generators/
        ├── ParserGenerator.cs    # PyParser.cs 생성기
        └── AstGenerator.cs       # AstTypes.cs 생성기
```

### 핵심 클래스

#### 1. Parser/PyParserBase.cs
**역할:** CPython의 pegen.c + pegen.h 일부 기능

**이미 구현된 것:**
```csharp
public abstract class PyParserBase<TResult>
{
    // Parser 상태
    protected List<GeneratedTokenInfo> _tokens;
    protected int _position;              // CPython: mark
    protected string _filename;
    protected int _errorIndicator;        // CPython: error_indicator
    protected int _level;                 // CPython: level (재귀 깊이)

    // 위치 추적 (EXTRA 매크로용)
    protected int _startMark;
    protected GeneratedTokenInfo? _startToken;
    protected GeneratedTokenInfo? _lastToken;

    // 기본 파싱 메서드
    protected int Mark();
    protected void Reset(int mark);
    protected GeneratedTokenInfo ExpectToken(PyToken.Type type);
    protected GeneratedTokenInfo ExpectKeyword(string keyword);
    protected GeneratedTokenInfo ExpectSoftKeyword(string keyword);
    protected GeneratedTokenInfo ExpectOp(string op);

    // PEG 연산자들
    protected GeneratedPtr ParseOptional(Func<GeneratedPtr> parser);
    protected GeneratedSeq ParseZeroOrMore(Func<GeneratedPtr> parser);
    protected GeneratedSeq ParseOneOrMore(Func<GeneratedPtr> parser);
    protected GeneratedPtr PositiveLookahead(Func<GeneratedPtr> parser);
    protected GeneratedPtr NegativeLookahead(Func<GeneratedPtr> parser);
    protected GeneratedSeq ParseGatherPlus(...);
    protected GeneratedSeq ParseGatherStar(...);

    // 위치 정보 추적
    protected void CaptureStart();
    protected int GetStartLine();
    protected int GetStartColumn();
    protected int GetEndLine();
    protected int GetEndColumn();

    // 에러 처리
    protected GeneratedPtr RaiseSyntaxError(string message);
    protected GeneratedPtr RaiseSyntaxErrorKnownLocation(...);
    protected GeneratedPtr RaiseSyntaxErrorKnownRange(...);
    protected GeneratedPtr RaiseSyntaxErrorStartingFrom(...);
}
```

**아직 없는 것 (구현 필요):**
```csharp
// EXTRA 매크로 - 가장 시급!
protected (int, int, int, int) EXTRA { get; }

// CHECK 매크로
protected T Check<T>(T result) where T : class;

// CHECK_VERSION 매크로
protected T CheckVersion<T>(int version, string msg, T node) where T : class;

// 버전 필드
protected int _featureVersion = 12;  // Python 3.12
```

#### 2. Parser/PyParserHelpers.cs
**역할:** CPython의 action_helpers.c + Python-ast.c

**이미 구현된 것:**
```csharp
// AST 팩토리 (AstFactory 클래스)
public static class AstFactory
{
    // _PyAST_* 함수들 (일부만 구현됨)
    public static GeneratedMod _PyAST_Module(...);
    public static GeneratedMod _PyAST_Interactive(...);
    public static GeneratedStmt _PyAST_FunctionDef(...);
    public static GeneratedStmt _PyAST_AsyncFunctionDef(...);
    // ... 약 30개 정도 구현됨
}
```

**아직 없는 것 (구현 필요):**
```csharp
// PyAst 래퍼 클래스 - grammar에서 사용
public static class PyAst
{
    // 단순 래퍼 (EXTRA를 풀어서 전달)
    public static GeneratedStmt Pass(int lineno, int col, int endline, int endcol)
        => AstFactory._PyAST_Pass(lineno, col, endline, endcol);

    // 100+ 메서드 필요
}

// PyParserHelpers 클래스 - 헬퍼 함수들
public static class PyParserHelpers
{
    // 시퀀스 조작
    public static GeneratedSeq SingletonSeq(GeneratedPtr item);
    public static GeneratedSeq SeqInsertInFront(GeneratedPtr item, GeneratedSeq seq);
    public static GeneratedSeq SeqAppendToEnd(GeneratedSeq seq, GeneratedPtr item);

    // Arguments 생성
    public static GeneratedArguments MakeArguments(...);
    public static GeneratedArguments EmptyArguments();

    // 비교 연산
    public static CmpopExprPair CmpopExprPair(...);
    public static GeneratedCmpopSeq GetCmpops(GeneratedSeq pairs);
    public static GeneratedExprSeq GetExprs(GeneratedSeq pairs);

    // 50+ 메서드 필요
}
```

#### 3. Grammar/python_cs.gram
**역할:** CPython의 python.gram을 C# 스타일로 변환

**현재 상태:**
```python
# C# 스타일 문법 (194개 규칙 파싱됨)

# 예시 1: 단순 구문
simple_stmt[GeneratedStmt]:
    | e=star_expressions { PyAst.Expr(e, EXTRA) }
    | 'pass' { PyAst.Pass(EXTRA) }
    | 'break' { PyAst.Break(EXTRA) }

# 예시 2: 이항 연산 (문제 있음!)
sum[GeneratedExpr]:
    | a=sum '+' b=term { PyAst.BinOp(a, Add, b, EXTRA) }
    #                                  ^^^ 에러: 'Add' is a type

# 예시 3: 함수 정의
function_def_raw[GeneratedStmt]:
    | 'def' n=NAME t=[type_params] &&'(' params_=[params] ')'
      a=['->' z=expression { z }] &&':' tc=[func_type_comment] b=block {
        PyAst.FunctionDef(n.Id,
                        (params_) ? params_ : Check<GeneratedArguments>(PyParserHelpers.EmptyArguments()),
                        b, null, a, tc?.Value, t, EXTRA) }
```

**문제점:**
1. `EXTRA` 매크로가 정의되지 않음 → 322개 에러
2. `Add`, `Load` 등이 타입으로 인식됨 → 100개 에러
3. `PyAst.*` 메서드가 없음 → 150개 에러
4. `Check<T>()` 메서드가 없음 → 84개 에러
5. `PyParserHelpers.*` 메서드가 부족함 → 200개 에러

---

## 핵심 매핑 관계

### 1. EXTRA 매크로

**CPython (pegen.h:258):**
```c
#define EXTRA _start_lineno, _start_col_offset, _end_lineno, _end_col_offset, p->arena
```

**C# 구현 방안:**
```csharp
// 방안 1: Tuple 프로퍼티
protected (int, int, int, int) EXTRA
{
    get => (GetStartLine(), GetStartColumn(), GetEndLine(), GetEndColumn());
}

// 사용 예:
var (line, col, endLine, endCol) = EXTRA;
PyAst.Pass(line, col, endLine, endCol);

// 방안 2: 개별 프로퍼티 (더 읽기 쉬움)
protected int _start_lineno => GetStartLine();
protected int _start_col_offset => GetStartColumn();
protected int _end_lineno => GetEndLine();
protected int _end_col_offset => GetEndColumn();

// 사용 예:
PyAst.Pass(_start_lineno, _start_col_offset, _end_lineno, _end_col_offset);
```

**선택:** 방안 2 추천 (CPython 코드와 1:1 대응)

### 2. CHECK 매크로

**CPython (pegen.h:222):**
```c
#define CHECK(type, result) ((type) CHECK_CALL(p, result))

Py_LOCAL_INLINE(void *)
CHECK_CALL(Parser *p, void *result)
{
    if (result == NULL) {
        assert(PyErr_Occurred());
        p->error_indicator = 1;
    }
    return result;
}
```

**C# 구현:**
```csharp
protected T Check<T>(T result) where T : class
{
    if (result == null)
    {
        _errorIndicator = 1;
    }
    return result;
}

// 사용 예:
Check<GeneratedArguments>(PyParserHelpers.EmptyArguments())
```

### 3. 연산자 Enum

**CPython (pycore_ast.h:25-27):**
```c
typedef enum _operator {
    Add=1, Sub=2, Mult=3, MatMult=4, Div=5, Mod=6, Pow=7,
    LShift=8, RShift=9, BitOr=10, BitXor=11, BitAnd=12,
    FloorDiv=13
} operator_ty;

// 사용: enum 값으로 전달
_PyAST_BinOp(a, Add, b, EXTRA)
```

**SharpPy (AstTypes.cs에 이미 생성됨):**
```csharp
public enum GeneratedOperator {
    Add = 1, Sub = 2, Mult = 3, MatMult = 4, Div = 5, Mod = 6, Pow = 7,
    LShift = 8, RShift = 9, BitOr = 10, BitXor = 11, BitAnd = 12,
    FloorDiv = 13
}

// 문제: C#에서는 타입 이름으로 인식됨
PyAst.BinOp(a, Add, b, EXTRA)  // 에러!
```

**해결 방안:**
```csharp
// 방안 1: 전체 이름 사용
PyAst.BinOp(a, GeneratedOperator.Add, b, EXTRA)

// 방안 2: using static (python_cs.gram 상단에)
using static SharpPy.Generated.GeneratedOperator;
using static SharpPy.Generated.GeneratedExprContext;
// 그러면 grammar에서:
PyAst.BinOp(a, Add, b, EXTRA)  // OK!

// 방안 3: PyAst 클래스 내부에 상수 정의
public static class PyAst
{
    // 연산자 상수들
    public const GeneratedOperator Add = GeneratedOperator.Add;
    public const GeneratedOperator Sub = GeneratedOperator.Sub;
    // ... 모든 enum 값들

    // 컨텍스트 상수들
    public const GeneratedExprContext Load = GeneratedExprContext.Load;
    public const GeneratedExprContext Store = GeneratedExprContext.Store;
    public const GeneratedExprContext Del = GeneratedExprContext.Del;
}
```

**선택:** 방안 2 추천 (가장 CPython과 유사)

### 4. AST 생성 함수

**CPython:**
```c
// Python-ast.c
stmt_ty _PyAST_Pass(int lineno, int col_offset, int end_lineno, int end_col_offset, PyArena *arena);
stmt_ty _PyAST_FunctionDef(identifier name, arguments_ty args, ...);

// python.gram에서 사용
'pass' { _PyAST_Pass(EXTRA) }
```

**SharpPy:**
```csharp
// PyParserHelpers.cs - AstFactory 클래스 (일부 구현됨)
public static class AstFactory
{
    public static GeneratedStmt _PyAST_Pass(int lineno, int col_offset,
                                            int end_lineno, int end_col_offset)
    {
        var node = new GeneratedPass();
        node.LineNo = lineno;
        node.ColOffset = col_offset;
        node.EndLineNo = end_lineno;
        node.EndColOffset = end_col_offset;
        return node;
    }
}

// PyAst 래퍼 클래스 (새로 추가 필요)
public static class PyAst
{
    public static GeneratedStmt Pass(int lineno, int col, int endline, int endcol)
        => AstFactory._PyAST_Pass(lineno, col, endline, endcol);
}

// python_cs.gram에서 사용
'pass' { PyAst.Pass(EXTRA) }
// EXTRA가 4개 인자로 풀림
```

### 5. 헬퍼 함수

**CPython (action_helpers.c):**
```c
asdl_seq *_PyPegen_singleton_seq(Parser *p, void *a)
{
    assert(a != NULL);
    asdl_seq *seq = (asdl_seq *)_Py_asdl_generic_seq_new(1, p->arena);
    if (!seq) return NULL;
    asdl_seq_SET_UNTYPED(seq, 0, a);
    return seq;
}
```

**SharpPy 구현:**
```csharp
// PyParserHelpers.cs
public static class PyParserHelpers
{
    public static GeneratedSeq SingletonSeq(GeneratedPtr item)
    {
        if (item == null) return null;
        var seq = new GeneratedSeq();
        seq.Add(item);
        return seq;
    }

    // 타입별 오버로드
    public static GeneratedStmtSeq SingletonSequence(GeneratedStmt item)
    {
        if (item == null) return null;
        var seq = new GeneratedStmtSeq();
        seq.Add(item);
        return seq;
    }
}
```

---

## 구현 계획

### Phase 1: EXTRA 매크로 (322개 에러 해결)
**우선순위:** 최고 (다른 모든 Phase의 기반)

**파일:** `Parser/PyParserBase.cs`

**구현:**
```csharp
// PyParserBase.cs 클래스에 추가
protected int _start_lineno => GetStartLine();
protected int _start_col_offset => GetStartColumn();
protected int _end_lineno => GetEndLine();
protected int _end_col_offset => GetEndColumn();
```

**검증:**
```bash
dotnet build 2>&1 | grep "EXTRA" | wc -l
# 0이 나와야 함
```

### Phase 2: CHECK 매크로 (122개 에러 해결)
**우선순위:** 높음 (Phase 3, 5에서 필요)

**파일:** `Parser/PyParserBase.cs`

**구현:**
```csharp
// PyParserBase.cs 클래스에 추가
protected int _featureVersion = 12;  // Python 3.12

protected T Check<T>(T result) where T : class
{
    if (result == null)
    {
        _errorIndicator = 1;
    }
    return result;
}

protected T CheckVersion<T>(int version, string msg, T node) where T : class
{
    if (node == null)
    {
        _errorIndicator = 1;
        return null;
    }
    if (_featureVersion < version)
    {
        _errorIndicator = 1;
        return RaiseSyntaxError(
            $"{msg} only supported in Python 3.{version} and greater") as T;
    }
    return node;
}

protected bool CheckNullAllowed(GeneratedPtr result)
{
    if (result == null && _errorIndicator != 0)
    {
        return false;
    }
    return true;
}
```

### Phase 3: 연산자 Enum (100개 에러 해결)
**우선순위:** 중상

**파일:** `Grammar/python_cs.gram` (상단에 추가)

**구현:**
```python
# python_cs.gram 맨 위에 추가 (Trailer 이전)

# C# using static for enum values
# @using_static '''
# using static SharpPy.Generated.GeneratedOperator;
# using static SharpPy.Generated.GeneratedUnaryOp;
# using static SharpPy.Generated.GeneratedBoolOp;
# using static SharpPy.Generated.GeneratedCmpOp;
# using static SharpPy.Generated.GeneratedExprContext;
# '''

# 그러면 grammar에서:
sum[GeneratedExpr]:
    | a=sum '+' b=term { PyAst.BinOp(a, Add, b, EXTRA) }
    #                                  ^^^ 이제 OK!
```

**대안:** ParserGenerator.cs에서 자동으로 using static 추가

### Phase 4: PyAst 클래스 (150개 에러 해결)
**우선순위:** 중

**파일:** `Parser/PyParserHelpers.cs`

**구현 전략:**
1. 에러 로그에서 사용되는 메서드 목록 추출
2. CPython의 Python-ast.c 참조하여 구현
3. 단순 래퍼 함수들

**구현 예시:**
```csharp
// PyParserHelpers.cs 파일에 추가
public static class PyAst
{
    // ============================================================
    // Statements
    // ============================================================

    public static GeneratedStmt Pass(int lineno, int col_offset,
                                     int end_lineno, int end_col_offset)
        => AstFactory._PyAST_Pass(lineno, col_offset, end_lineno, end_col_offset);

    public static GeneratedStmt Break(int lineno, int col_offset,
                                      int end_lineno, int end_col_offset)
        => AstFactory._PyAST_Break(lineno, col_offset, end_lineno, end_col_offset);

    public static GeneratedStmt Continue(int lineno, int col_offset,
                                         int end_lineno, int end_col_offset)
        => AstFactory._PyAST_Continue(lineno, col_offset, end_lineno, end_col_offset);

    // ... 100+ 메서드
}
```

**우선순위별 구현:**
1. 단순 구문: Pass, Break, Continue, etc. (20개)
2. 표현식: BinOp, UnaryOp, BoolOp, Compare (20개)
3. 할당: Assign, AugAssign, AnnAssign (10개)
4. 제어 흐름: If, While, For, Try, With (20개)
5. 함수/클래스: FunctionDef, ClassDef, Lambda (10개)
6. 나머지: Match, Import, etc. (70개)

### Phase 5: PyParserHelpers 확장 (200개 에러 해결)
**우선순위:** 중하

**파일:** `Parser/PyParserHelpers.cs`

**우선순위별 구현:**

**1. 시퀀스 조작 (40개 에러):**
```csharp
public static class PyParserHelpers
{
    // CPython: _PyPegen_singleton_seq
    public static GeneratedSeq SingletonSeq(GeneratedPtr item)
    {
        if (item == null) return null;
        var seq = new GeneratedSeq();
        seq.Add(item);
        return seq;
    }

    // 타입별 오버로드들
    public static GeneratedStmtSeq SingletonSequence(GeneratedStmt item) { ... }
    public static GeneratedExprSeq SingletonSequence(GeneratedExpr item) { ... }

    // CPython: _PyPegen_seq_insert_in_front
    public static GeneratedSeq SeqInsertInFront(GeneratedPtr item, GeneratedSeq seq) { ... }

    // CPython: _PyPegen_seq_append_to_end
    public static GeneratedSeq SeqAppendToEnd(GeneratedSeq seq, GeneratedPtr item) { ... }

    // CPython: _PyPegen_join_sequences
    public static GeneratedSeq JoinSequences(GeneratedSeq a, GeneratedSeq b) { ... }
}
```

**2. Arguments 생성 (30개 에러):**
```csharp
// CPython: _PyPegen_empty_arguments
public static GeneratedArguments EmptyArguments()
{
    return new GeneratedArguments
    {
        Posonlyargs = new GeneratedArgSeq(),
        Args = new GeneratedArgSeq(),
        Vararg = null,
        Kwonlyargs = new GeneratedArgSeq(),
        KwDefaults = new GeneratedExprSeq(),
        Kwarg = null,
        Defaults = new GeneratedExprSeq()
    };
}

// CPython: _PyPegen_make_arguments
public static GeneratedArguments MakeArguments(
    GeneratedArgSeq posonlyargs,
    SlashWithDefault slash_without_default,
    GeneratedArgSeq plain_names,
    GeneratedSeq names_with_default,
    StarEtc star_etc)
{
    // 복잡한 로직 - CPython 참조
}
```

**3. 나머지 (130개 에러):**
- 비교 연산자 처리 (20개)
- 문자열 처리 (20개)
- 패턴 매칭 (20개)
- 함수 호출 (20개)
- 기타 (50개)

### Phase 6: GeneratedPtr 확장 (40개 에러 해결)
**우선순위:** 낮음 (선택적)

**문제:**
```csharp
// 에러: GeneratedPtr에 Id 프로퍼티가 없음
var name = token.Id;
```

**해결 방안 1: 명시적 캐스팅 (grammar 수정):**
```csharp
// python_cs.gram에서
var name = ((GeneratedName)token).Id;
```

**해결 방안 2: GeneratedPtr 확장 (추천):**
```csharp
// AstTypes.cs - GeneratedPtr partial class에 추가
public partial class GeneratedPtr
{
    public virtual string Id
        => throw new NotSupportedException($"{GetType().Name} does not have Id");

    public virtual object Value
        => throw new NotSupportedException($"{GetType().Name} does not have Value");
}

// GeneratedName partial class에 추가
public partial class GeneratedName
{
    public override string Id => base.Id;  // 실제 필드 반환
}
```

### Phase 7: Invalid Rules (50개 에러 해결)
**우선순위:** 최저 (선택적)

**목표:** 더 나은 에러 메시지

**전략:**
- 에러 발생시 필요한 것만 추가
- CPython의 python.gram에서 복사

---

## 참조 자료

### CPython 3.12 소스 위치
```
C:\Users\m11\Desktop\work\dup\cpython-3.12\
├── Parser/
│   ├── pegen.h (중요!)
│   ├── pegen.c
│   ├── action_helpers.c (중요!)
│   └── Python.asdl
├── Grammar/
│   └── python.gram (중요!)
└── Include/internal/
    └── pycore_ast.h (중요!)
```

### 핵심 참조 라인
- **pegen.h:258** - EXTRA 매크로 정의
- **pegen.h:222** - CHECK 매크로 정의
- **pegen.h:296** - CHECK_VERSION 매크로 정의
- **pegen.h:190-199** - RAISE_SYNTAX_ERROR* 매크로들
- **pegen.h:85-120** - Helper 구조체들
- **action_helpers.c:14-104** - 시퀀스 조작 함수들
- **pycore_ast.h:21-32** - Enum 타입 정의들
- **python.gram** - 전체 문법 (193개 규칙)

### 도구
```bash
# 에러 분석
dotnet build 2>&1 | grep "error CS" | cut -d: -f4 | sort | uniq -c | sort -rn

# 특정 에러 찾기
dotnet build 2>&1 | grep "EXTRA"
dotnet build 2>&1 | grep "does not contain a definition"

# 에러 개수 확인
dotnet build 2>&1 | tail -5
```

### 문서
- `docs/c_to_csharp_patterns.md` - C → C# 변환 패턴
- `docs/python_cs_gram_rewrite_plan.md` - Grammar 재작성 계획
- 이 문서 - CPython → SharpPy 구현 가이드
