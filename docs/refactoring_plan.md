# SharpPy Refactoring Plan

## 목표
잘못된 절차(python_py.gram → python.gram → python_cs.gram)로 만들어진 코드를 올바른 절차(python_py.gram → python_cs.gram)로 재구성

---

## Phase 1: KeywordExtractor 수정

### 현재 문제
- 하드코딩된 키워드 목록을 갖고 있음 (`_hardKeywords` HashSet)
- `ValidateKeywords()` 메서드 존재 → "검증"을 하고 있음 (추출이 아님)
- "답을 미리 알고 있다"는 잘못된 가정

### 수정 방향
1. **하드코딩 제거**
   - `_hardKeywords`, `_softKeywords` 같은 하드코딩 목록 삭제
   - `ValidateKeywords()` 메서드 삭제

2. **동적 추출 구현**
   ```csharp
   public (HashSet<string> hardKeywords, HashSet<string> softKeywords) ExtractKeywords(string grammarPath)
   {
       var grammarContent = File.ReadAllText(grammarPath);

       // 'keyword' 패턴 추출 (single quote) → Hard Keywords
       var hardKeywordRegex = new Regex(@"'([a-zA-Z_][a-zA-Z0-9_]*)'");
       var hardKeywords = new HashSet<string>();
       foreach (Match match in hardKeywordRegex.Matches(grammarContent))
       {
           hardKeywords.Add(match.Groups[1].Value);
       }

       // "keyword" 패턴 추출 (double quote) → Soft Keywords
       var softKeywordRegex = new Regex(@"""([a-zA-Z_][a-zA-Z0-9_]*)""");
       var softKeywords = new HashSet<string>();
       foreach (Match match in softKeywordRegex.Matches(grammarContent))
       {
           softKeywords.Add(match.Groups[1].Value);
       }

       return (hardKeywords, softKeywords);
   }
   ```

3. **생성 코드 개선**
   - 추출된 키워드를 C# 코드로 생성
   - PyParser.cs에 포함될 HashSet 생성

### 검증 방법
- python_py.gram에서 추출한 키워드 목록 출력
- 예상 키워드: `if`, `for`, `while`, `def`, `class`, `pass`, `break`, `continue`, etc.
- Soft Keywords: `match`, `case`, `type`

---

## Phase 2: python_cs.gram 재작성

### 현재 문제
- 잘못된 절차(python.gram 경유)로 만들어짐
- C 스타일 패턴 남아있음 (using 별칭, 매크로 흉내)
- Invalid rules가 C 매크로 그대로 (`RAISE_SYNTAX_ERROR` 등)

### 수정 방향

#### Step 1: python_py.gram 분석
- python_py.gram을 규칙별로 분석
- action code가 없는 규칙들 확인

#### Step 2: python.gram (C 버전) 참고
- 각 규칙이 **무엇을 하는지** 의미 파악
- 필요한 헬퍼 함수 파악
- 예시:
  ```
  python_py.gram:  statements: statement+
  python.gram:     statements[asdl_stmt_seq*]: a=statement+ { _PyPegen_seq_flatten(p, a) }
  의미:            "statement 시퀀스를 평탄화"
  ```

#### Step 3: CPython 3.12 소스 확인
- 로컬 CPython 3.12 소스에서 헬퍼 함수 동작 확인
- 경로: `C:\...\cpython-3.12\Parser\pegen.c`
- 확인할 함수들:
  - `_PyPegen_seq_flatten`
  - `_PyPegen_set_expr_context`
  - `_PyAST_Pass`, `_PyAST_Break`, `_PyAST_Continue`
  - `RAISE_SYNTAX_ERROR` 관련 함수들
  - 기타 헬퍼 함수들

#### Step 4: python_cs.gram 작성
- python_py.gram 각 규칙을 순회하며 action code 추가
- C# 스타일로 작성 (C 매크로 흉내 내지 않음)
- 함수명은 C 스타일도 무방 (동작이 정확하면)

**예시 - 간단한 규칙:**
```csharp
# python_py.gram
simple_stmt:
    | 'pass'
    | 'break'
    | 'continue'

# python_cs.gram (수정 후)
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

**예시 - 복잡한 규칙:**
```csharp
# python_py.gram
statements: statement+

# python_cs.gram (수정 후)
statements: a=statement+ {
    PyParserHelpers._PyPegen_seq_flatten(a)
}
```

#### Step 5: Invalid Rules 재작성
- 현재 C 매크로 그대로 있음 (`RAISE_SYNTAX_ERROR` 등)
- C# 메서드 호출로 변환
- 예시:
  ```csharp
  # 현재 (잘못됨)
  invalid_stmt:
      | a='pass' b='pass' {
          RAISE_SYNTAX_ERROR_KNOWN_RANGE(a, b, "multiple statements on single line")
      }

  # 수정 후
  invalid_stmt:
      | a='pass' b='pass' {
          RaiseSyntaxErrorKnownRange(a, b, "multiple statements on single line")
      }
  ```

### 작업 순서
1. python_py.gram의 규칙 수 파악 (총 몇 개?)
2. 우선순위 결정:
   - Phase 1: Simple statements (pass, break, continue, return, etc.)
   - Phase 2: Compound statements (if, for, while, def, class, etc.)
   - Phase 3: Expressions
   - Phase 4: Invalid rules
3. 각 phase별로 순차 작업

---

## Phase 3: PyParserHelpers.cs 재작성

### 현재 문제
- C typedef 흉내낸 using 별칭:
  ```csharp
  using mod_ty = SharpPy.Generated.GeneratedMod;
  using stmt_ty = SharpPy.Generated.GeneratedStmt;
  using expr_ty = SharpPy.Generated.GeneratedExpr;
  // ... 등등
  ```
- C 스타일 함수명과 패턴

### 수정 방향

#### Step 1: using 별칭 제거
- 모든 `using X_ty = ...` 제거
- 코드에서 `mod_ty`, `stmt_ty` 등을 `GeneratedMod`, `GeneratedStmt`로 직접 사용

#### Step 2: 헬퍼 함수 재구현
- CPython 3.12 소스를 참고하여 동작 확인
- C# 스타일로 재구현
- 함수명은 유지해도 무방 (동작이 정확하면)

**예시:**
```csharp
// 현재 (using 별칭 사용)
public static stmt_ty _PyAST_Pass(int lineno, int col_offset, int end_lineno, int end_col_offset)
{
    return new stmt_ty { /* ... */ };
}

// 수정 후 (직접 타입 사용)
public static GeneratedStmt _PyAST_Pass(int lineno, int col_offset, int end_lineno, int end_col_offset)
{
    return new GeneratedStmt.Pass(lineno, col_offset, end_lineno, end_col_offset);
}
```

#### Step 3: 에러 핸들링 메서드 검증
- `RaiseSyntaxError`, `RaiseSyntaxErrorKnownLocation` 등의 메서드
- CPython 3.12 동작과 일치하는지 확인
- 필요시 수정

---

## Phase 4: 생성 도구 업데이트

### CSharpParserGenerator 확인
- python_cs.gram → PyParser.cs 생성 도구
- action code 처리 로직 확인
- 필요시 수정

### CSharpTokenizerGenerator 확인
- 키워드 생성 부분 확인
- KeywordExtractor 연동 확인

---

## 작업 우선순위

### 즉시 시작 (Phase 1)
1. **KeywordExtractor.cs 수정**
   - 하드코딩 제거
   - 동적 추출 구현
   - 테스트: python_py.gram에서 키워드 추출

### 다음 단계 (Phase 2-A: 검증)
2. **python_cs.gram 현황 파악**
   - 현재 파일 분석
   - 어떤 규칙들이 있는지 확인
   - 어떤 부분이 C 스타일인지 확인

3. **CPython 3.12 소스 탐색**
   - 로컬 소스 위치 확인
   - 주요 헬퍼 함수 동작 확인

### 본격 작업 (Phase 2-B: 재작성)
4. **python_cs.gram 재작성 시작**
   - Simple statements부터 시작
   - 점진적으로 확장

5. **PyParserHelpers.cs 수정**
   - using 별칭 제거
   - 함수 동작 검증

---

## 검증 방법

### 단계별 검증
1. **KeywordExtractor 검증**
   - python_py.gram에서 키워드 추출
   - 결과 출력 및 확인
   - 예상 키워드와 비교

2. **python_cs.gram 검증**
   - PEG generator로 PyParser.cs 생성
   - 빌드 성공 확인
   - 간단한 테스트 파일 파싱 (pass.py, break.py)

3. **전체 시스템 검증**
   - test_python312_compatibility.py 실행
   - CPython 3.12와 바이트코드 비교
   - 토큰 비교: `dotnet run --tokens test.py`
   - AST 비교: `dotnet run --ast test.py`
   - 바이트코드 비교: `dotnet run --dis test.py`

---

## 백업 전략

### 작업 전 백업
```bash
# 현재 파일들 백업
cp Grammar/python_cs.gram Grammar/python_cs.gram.backup
cp Parser/PyParserHelpers.cs Parser/PyParserHelpers.cs.backup
cp Tools/KeywordExtractor.cs Tools/KeywordExtractor.cs.backup
```

### Git 커밋 전략
- Phase별로 커밋
- 커밋 메시지:
  - `Refactor: KeywordExtractor - remove hardcoded keywords, implement dynamic extraction`
  - `Refactor: python_cs.gram - rewrite simple statements from python_py.gram`
  - `Refactor: PyParserHelpers - remove using aliases, use direct types`

---

## 위험 요소 및 대응

### 위험 1: python_cs.gram 재작성이 방대함
**대응:** 점진적 접근
- 먼저 작은 부분(simple statements)부터 시작
- 검증 후 다음 단계 진행
- 각 단계마다 빌드 및 테스트

### 위험 2: CPython 소스 분석이 어려움
**대응:** 문서화
- 이해한 내용을 주석으로 남김
- FAQ에 추가
- 사용자에게 질문

### 위험 3: 생성된 코드가 기존과 다름
**대응:** 동작 검증 우선
- 이름이 달라도 동작이 같으면 OK
- 바이트코드 비교로 검증
- CPython 3.12와 일치 확인

---

## 예상 소요 시간

- Phase 1 (KeywordExtractor): 1-2 hours
- Phase 2 (python_cs.gram 분석 및 계획): 2-3 hours
- Phase 2 (python_cs.gram 재작성): 8-12 hours (규칙 수에 따라)
- Phase 3 (PyParserHelpers): 2-3 hours
- Phase 4 (검증 및 테스트): 2-3 hours

**총 예상 시간: 15-23 hours**

---

## 성공 기준

### 최소 성공 기준
1. ✅ KeywordExtractor가 python_py.gram에서 키워드를 동적으로 추출
2. ✅ python_cs.gram이 python_py.gram에서 직접 변환됨 (python.gram 경유 없음)
3. ✅ PyParserHelpers.cs에 using 별칭 없음
4. ✅ 빌드 성공 (0 errors, 0 warnings)
5. ✅ 간단한 테스트 파일 파싱 성공 (pass.py, break.py, simple_if.py)

### 이상적인 성공 기준
1. ✅ 위의 모든 기준 달성
2. ✅ test_python312_compatibility.py 전체 통과
3. ✅ CPython 3.12와 바이트코드 일치
4. ✅ Invalid rules도 모두 올바르게 동작 (좋은 에러 메시지)
5. ✅ 코드가 이해하기 쉽고 유지보수 가능

---

## 첫 번째 작업: KeywordExtractor 수정

### 상세 계획

#### 1. 현재 코드 분석
- `Tools/KeywordExtractor.cs` 파일 읽기
- 하드코딩된 부분 확인
- ValidateKeywords 로직 확인

#### 2. 백업
```bash
cp Tools/KeywordExtractor.cs Tools/KeywordExtractor.cs.backup
```

#### 3. 코드 수정
- 하드코딩 제거
- ExtractKeywords 메서드 구현
- 테스트 코드 추가 (Main 메서드에서)

#### 4. 테스트
```bash
# KeywordExtractor 실행
dotnet run --project Tools/KeywordExtractor

# 출력 확인
# Expected: if, for, while, def, class, pass, break, continue, return, ...
# Soft Keywords: match, case, type
```

#### 5. 생성 코드 확인
- 생성된 C# 코드 확인
- PyParser.cs에 포함될 HashSet 확인

---

## 다음 단계 미리보기

Phase 1 완료 후:
1. python_cs.gram 현황 분석
2. CPython 3.12 소스 탐색 (로컬 경로 확인)
3. python_cs.gram 재작성 계획 수립
4. 사용자 승인 후 Phase 2 시작
