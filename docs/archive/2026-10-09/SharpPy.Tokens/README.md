# SharpPy.Tokens

Python 3.12 토큰 정의 파일(`Grammar/Tokens`)로부터 C# 토큰 타입 코드를 자동 생성하는 도구입니다.

## 개요

SharpPy.Tokens는 CPython 3.12의 토큰 정의를 읽어 `PyToken.Type` enum과 `Literals` 리스트를 포함하는 C# 코드를 생성합니다. 이를 통해 Python 토큰 시스템과 완벽하게 호환되는 타입 안전한 토큰 처리가 가능합니다.

## 주요 기능

- **토큰 타입 자동 생성**: `Grammar/Tokens` 파일에서 68개의 CPython 3.12 토큰 타입을 읽어 C# enum 생성
- **리터럴 매핑**: 연산자 및 구두점 리터럴을 토큰 타입에 자동 매핑
- **CPython 3.12 호환**: 토큰 인덱스가 CPython과 정확히 일치
- **타입 안전성**: 문자열 대신 enum을 사용하여 컴파일 타임 검증

## 빌드 및 실행

### 빌드
```bash
dotnet build
```

### 실행
```bash
# 기본 경로 사용 (Grammar/Tokens → Generated/PyTokens.cs)
dotnet run --project SharpPy.Tokens

# 사용자 정의 경로
dotnet run --project SharpPy.Tokens --tokens Grammar/Tokens --output Generated/PyTokens.cs
```

## 입력 파일 형식

`Grammar/Tokens` 파일 형식:

```
# 복잡한 토큰 (값 없음)
ENDMARKER
NAME
NUMBER
STRING

# 리터럴 토큰 (값 있음)
LPAR                    '('
RPAR                    ')'
PLUS                    '+'
EQEQUAL                 '=='
DOUBLESTAR              '**'

# 주석
# 이 줄은 무시됩니다
```

### 토큰 정의 규칙

1. **복잡한 토큰**: 토큰 이름만 작성 (예: `NAME`, `NUMBER`)
2. **리터럴 토큰**: 토큰 이름과 리터럴 값 작성 (예: `PLUS '+'`)
3. **주석**: `#`으로 시작하는 줄은 무시됨
4. **빈 줄**: 자동으로 무시됨

## 출력 코드

생성되는 `Generated/PyTokens.cs` 파일 구조:

```csharp
namespace SharpPy.Generated
{
    public static class PyToken
    {
        /// <summary>
        /// CPython 3.12 compatible token types
        /// Explicit values to match CPython token indices
        /// </summary>
        public enum Type
        {
            ENDMARKER = 0,
            NAME = 1,
            NUMBER = 2,
            STRING = 3,
            NEWLINE = 4,
            INDENT = 5,
            DEDENT = 6,
            LPAR = 7,
            RPAR = 8,
            // ... (총 68개 토큰)
            ENCODING = 67,
        }

        public static readonly List<(string name, Type type)> Literals = new()
        {
            ( "(", Type.LPAR ),
            ( ")", Type.RPAR ),
            ( "[", Type.LSQB ),
            ( "]", Type.RSQB ),
            // ... (총 48개 리터럴)
            ( "!", Type.EXCLAMATION ),
        };

        static public int GetLiteralIndex(string srcString, int srcPosition)
        {
            // 소스 문자열에서 리터럴 매칭
        }
    }
}
```

## CPython 3.12 토큰 타입

### 기본 토큰 (0-6)
- `ENDMARKER`: 파일 끝 마커
- `NAME`: 식별자 (변수명, 함수명 등)
- `NUMBER`: 숫자 리터럴
- `STRING`: 문자열 리터럴
- `NEWLINE`: 줄바꿈 (문법적 의미 있음)
- `INDENT`: 들여쓰기 증가
- `DEDENT`: 들여쓰기 감소

### 구두점 및 연산자 (7-56)
- 괄호: `LPAR (`, `RPAR )`, `LSQB [`, `RSQB ]`, `LBRACE {`, `RBRACE }`
- 기본 연산자: `PLUS +`, `MINUS -`, `STAR *`, `SLASH /`
- 비교 연산자: `LESS <`, `GREATER >`, `EQEQUAL ==`, `NOTEQUAL !=`
- 복합 대입: `PLUSEQUAL +=`, `MINEQUAL -=`, `STAREQUAL *=`
- 비트 연산: `AMPER &`, `VBAR |`, `CIRCUMFLEX ^`, `TILDE ~`
- 시프트: `LEFTSHIFT <<`, `RIGHTSHIFT >>`
- 기타: `DOT .`, `COMMA ,`, `COLON :`, `SEMI ;`, `EQUAL =`
- Python 3 전용: `AT @`, `RARROW ->`, `COLONEQUAL :=`, `ELLIPSIS ...`

### 특수 토큰 (57-67)
- `OP`: 일반 연산자 (파서에서 재분류)
- `AWAIT`, `ASYNC`: 비동기 키워드
- `TYPE_IGNORE`, `TYPE_COMMENT`: 타입 힌트 주석
- `SOFT_KEYWORD`: 문맥 의존 키워드
- **`FSTRING_START`, `FSTRING_MIDDLE`, `FSTRING_END`**: PEP 701 f-string 토큰 (Python 3.12+)
- `COMMENT`: 주석
- `NL`: 줄바꿈 (문법적 의미 없음)
- `ERRORTOKEN`: 오류 토큰
- `ENCODING`: 파일 인코딩

## 프로젝트 구조

```
SharpPy.Tokens/
├── Program.cs              # 진입점 및 CLI 인자 처리
├── TokensReader.cs         # Grammar/Tokens 파일 파서
├── TokensGenerator.cs      # C# 코드 생성기
├── SharpPy.Tokens.csproj   # 프로젝트 파일
└── README.md               # 이 문서
```

## 코드 구성

### Program.cs
- CLI 인자 파싱 (`--tokens`, `--output`)
- 파일 입출력 관리
- 에러 핸들링

### TokensReader.cs
- `TokenDefinition` 클래스: 토큰 정의 표현
- `ReadTokens()`: Grammar/Tokens 파일 파싱
- 정규식 기반 토큰 라인 파싱

### TokensGenerator.cs
- `CSharpTokenGenerator` 클래스: C# 코드 생성
- `GenerateTokenTypeEnum()`: PyToken.Type enum 생성
- `GenerateLiteralslist()`: Literals 리스트 생성
- `GetLiteralIndex()`: 리터럴 매칭 함수 생성

## 사용 예시

### SharpPy 빌드 파이프라인에서 사용

```bash
# 1단계: 토큰 타입 생성
dotnet run --project SharpPy.Tokens --tokens Grammar/Tokens --output Generated/PyTokens.cs

# 2단계: PEG 파서 생성 (PyTokens.cs 사용)
dotnet run --project SharpPy.PegGenerator ...

# 3단계: SharpPy 빌드 (생성된 코드 사용)
dotnet build
```

### 토큰 타입 사용 예시 (Tokenizer.cs)

```csharp
using SharpPy.Generated;

// 토큰 생성
AddToken(PyToken.Type.NAME, "hello", 1, 0);
AddToken(PyToken.Type.LPAR, "(", 1, 5);
AddToken(PyToken.Type.RPAR, ")", 1, 6);

// 리터럴 매칭
int index = PyToken.GetLiteralIndex(source, position);
if (index != -1)
{
    var (name, type) = PyToken.Literals[index];
    AddToken(type, name, line, column);
}
```

## Python 3.12 PEP 701 지원

이 도구는 Python 3.12의 PEP 701 (f-string 재구현)을 완벽하게 지원합니다:

- `FSTRING_START`: f-string 시작 (예: `f"`, `f"""`, `rf'`)
- `FSTRING_MIDDLE`: f-string 리터럴 부분 (예: `hello `)
- `FSTRING_END`: f-string 종료 (예: `"`, `"""`, `'`)

### f-string 토큰화 예시

```python
f'{x} + {y}'
```

생성되는 토큰:
```
FSTRING_START   "f'"
LBRACE          "{"
NAME            "x"
RBRACE          "}"
FSTRING_MIDDLE  " + "
LBRACE          "{"
NAME            "y"
RBRACE          "}"
FSTRING_END     "'"
```

## 의존성

- **.NET 8.0**: 타겟 프레임워크
- **System.Text.RegularExpressions**: 토큰 정의 파싱

## 라이센스

SharpPy 프로젝트의 라이센스를 따릅니다.

## 참고 자료

- [CPython 3.12 Grammar/Tokens](https://github.com/python/cpython/blob/3.12/Grammar/Tokens)
- [PEP 701: Syntactic formalization of f-strings](https://peps.python.org/pep-0701/)
- [CPython tokenizer.c](https://github.com/python/cpython/blob/3.12/Parser/tokenizer.c)
