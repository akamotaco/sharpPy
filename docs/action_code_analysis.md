# Action Code 분석 및 처리 계획

## 📊 분석 결과 요약

### 1. **python_cs.gram vs CPython python.gram 비교**

| 항목 | CPython python.gram | python_cs.gram | 차이 |
|------|---------------------|----------------|------|
| 전체 라인 수 | 1,412 | 1,971 | +559 |
| Action Code 개수 | 289 | 398 | +109 |
| Action Code 없는 규칙 | 5개 | 5개 (동일) | 0 |

**결론**: python_cs.gram은 CPython보다 오히려 **109개 더 많은 Action Code**를 보유하고 있음. Action Code 부재는 문제가 아님.

### 2. **Action Code 없는 5개 규칙** (CPython과 동일)

#### 1. `compound_stmt` (Line 274)
**타입**: Dispatcher 규칙
```python
compound_stmt[stmt_ty]:
    | &('def' | '@' | ASYNC) function_def
    | &'if' if_stmt
    | &('class' | '@') class_def
    | &('with' | ASYNC) with_stmt
    | &('for' | ASYNC) for_stmt
    | &'try' try_stmt
    | &'while' while_stmt
    | match_stmt
```
**동작**: 8개 alternative 중 매칭된 것의 결과를 반환

#### 2. `annotated_rhs` (Line 320)
**타입**: Simple choice 규칙
```python
annotated_rhs[expr_ty]: yield_expr | star_expressions
```
**동작**: yield_expr 또는 star_expressions 중 매칭된 것을 반환

#### 3. `params` (Line 515)
**타입**: Error wrapper 규칙
```python
params[arguments_ty]:
    | invalid_parameters
    | parameters
```
**동작**: invalid_parameters (에러 처리) 또는 parameters (정상) 반환

#### 4. `closed_pattern` (Line 788)
**타입**: Dispatcher 규칙 (memoized)
```python
closed_pattern[pattern_ty] (memo):
    | literal_pattern
    | capture_pattern
    | wildcard_pattern
    | value_pattern
    | group_pattern
    | sequence_pattern
    | mapping_pattern
    | class_pattern
```
**동작**: 8개 패턴 중 매칭된 것을 반환

#### 5. `lambda_params` (Line 1302)
**타입**: Error wrapper 규칙
```python
lambda_params[arguments_ty]:
    | invalid_lambda_parameters
    | lambda_parameters
```
**동작**: invalid_lambda_parameters (에러 처리) 또는 lambda_parameters (정상) 반환

### 3. **GeneratedPlaceholder 사용 현황**

**PyParser.cs에서 97개 발견:**
- **5개**: 위 5개 규칙 (완전히 Action Code 없음)
- **92개**: Action Code가 일부 alternative에만 있는 혼합 규칙

**혼합 규칙 예시:**
- `assignment`: 4개 alternative에 Action Code 있음, `invalid_assignment`에는 없음
- `arguments`: `a=args ... { a }`에 Action Code 있음, `invalid_arguments`에는 없음
- `block`: indented blocks에 Action Code 있음, `simple_stmts`/`invalid_block`에는 없음

---

## 🔍 CPython의 해결 방식

### CPython PEG Generator 동작

**파일**: `cpython-3.12/Tools/peg_generator/pegen/c_generator.py`

**로직**:
```python
def emit_default_action(self, is_gather: bool, node: Alt) -> None:
    if len(self.local_variable_names) > 1:
        if is_gather:
            # Gather 패턴: sep.item+ 또는 sep.item*
            _res = _PyPegen_seq_insert_in_front(p, var1, var2)
        else:
            # 복수 변수: dummy name 생성
            _res = _PyPegen_dummy_name(p, var1, var2, ...)
    else:
        # 단일 변수: 그대로 반환
        _res = var1
```

**핵심**: Action Code가 없으면 **캡처된 변수를 자동으로 반환**

---

## 🎯 SharpPy 해결 계획

### **현재 문제**

**ParserGenerator.cs Line 243-245**:
```csharp
// Placeholder for rules without action code (python_py.gram)
WriteLine($"// TODO: Return appropriate AST node for {ruleName}");
WriteLine("return GeneratedPlaceholder.Instance; // Placeholder");
```

**문제**: 무조건 Placeholder 반환 → 실제 AST 노드를 반환해야 함

### **해결 방법**

**ParserGenerator.cs GenerateAlternative 메서드 수정** (Line 209-246):

```csharp
// Generate return statement
WriteLine();
if (!string.IsNullOrEmpty(alt.ActionCode))
{
    // User-defined action code from python_cs.gram
    WriteLine($"// Action code from grammar");
    var expandedCode = alt.ActionCode.Replace("EXTRA", "...");
    expandedCode = ExpandSingletons(expandedCode);

    // ... (기존 코드)
}
else
{
    // CPython-style default action: return captured variables
    var namedItems = alt.Items.Where(i => !string.IsNullOrEmpty(i.Name)).ToList();

    if (namedItems.Count == 0)
    {
        // No captured variables: this shouldn't happen in valid grammar
        WriteLine($"// No captures - default to placeholder");
        WriteLine("return GeneratedPlaceholder.Instance;");
    }
    else if (namedItems.Count == 1)
    {
        // Single variable: return it directly (CPython pattern)
        WriteLine($"// Default action: return single capture");
        WriteLine($"return {namedItems[0].Name};");
    }
    else
    {
        // Multiple variables: return first non-null (CPython _PyPegen_dummy_name pattern)
        WriteLine($"// Default action: return first non-null of {namedItems.Count} captures");
        WriteLine($"return {string.Join(" ?? ", namedItems.Select(i => i.Name))};");
    }
}
```

---

## 📝 작업 순서

### Phase 1: ParserGenerator.cs 수정 ✅
- [x] GenerateAlternative 메서드 수정
- [x] CPython default action 패턴 구현
- [x] 단일/복수 변수 처리 로직 추가

### Phase 2: 재생성 및 테스트 ⏭️
- [ ] PegGenerator 실행 (PyParser.cs 재생성)
- [ ] 5개 규칙의 생성 코드 확인
- [ ] 빌드 테스트 (에러 개수 확인)
- [ ] 실행 테스트 (파싱 동작 확인)

### Phase 3: 검증 ⏭️
- [ ] compound_stmt: 각 alternative가 해당 함수 결과 반환하는지 확인
- [ ] annotated_rhs: yield_expr 또는 star_expressions 반환 확인
- [ ] params/lambda_params: invalid/valid 분기 반환 확인
- [ ] closed_pattern: 매칭된 패턴 반환 확인

---

## 🔄 예상 변화

### Before (현재)
```csharp
// compound_stmt alternative 1: &('def' | '@' | ASYNC) function_def
private GeneratedPtr? Parse_CompoundStmt()
{
    int _mark = Mark();

    Reset(_mark);
    {
        CaptureStart();

        GeneratedPtr? function_def = null;

        if (PositiveLookahead(() => /* ... */) == null) return null;
        if ((function_def = Parse_FunctionDef()) == null) return null;

        // TODO: Return appropriate AST node for compound_stmt
        return GeneratedPlaceholder.Instance; // ❌ Placeholder
    }

    // ... 7 more alternatives

    Reset(_mark);
    return null;
}
```

### After (수정 후)
```csharp
// compound_stmt alternative 1: &('def' | '@' | ASYNC) function_def
private GeneratedPtr? Parse_CompoundStmt()
{
    int _mark = Mark();

    Reset(_mark);
    {
        CaptureStart();

        GeneratedPtr? function_def = null;

        if (PositiveLookahead(() => /* ... */) == null) return null;
        if ((function_def = Parse_FunctionDef()) == null) return null;

        // Default action: return single capture
        return function_def; // ✅ 올바른 반환
    }

    // ... 7 more alternatives

    Reset(_mark);
    return null;
}
```

---

## 📈 예상 효과

1. **빌드 에러 감소**: 421개 → 예상 300개 이하
2. **실행 정확도 향상**: Placeholder 대신 실제 AST 노드 반환
3. **CPython 패턴 준수**: 100% CPython 3.12 호환
4. **유지보수성 향상**: 명확한 default action 로직

---

## 🔗 관련 파일

- **분석 대상**: `Grammar/python_cs.gram`, `CPython/Grammar/python.gram`
- **수정 파일**: `SharpPy.PegGenerator/Generators/ParserGenerator.cs`
- **생성 파일**: `Generated/PyParser.cs`
- **참고 구현**: `cpython-3.12/Tools/peg_generator/pegen/c_generator.py`

---

**날짜**: 2025-10-17
**작성자**: Claude Code
**상태**: ✅ 구현 완료

---

## 🎯 구현 결과

### Phase 1: ParserGenerator.cs 수정 ✅
**완료**: Line 243-266에 CPython default action 패턴 구현
- 단일 변수: `return variable;`
- 복수 변수: `return var1 ?? var2 ?? ...;`
- 변수 없음: `return GeneratedPlaceholder.Instance;`

### Phase 2: python_cs.gram 수정 ✅
**문제 발견**: 5개 규칙의 alternatives에 변수 이름이 없었음
- CPython도 동일하지만, PEG generator가 자동 처리
- SharpPy는 명시적으로 변수 이름과 action code 필요

**수정 내용**:
```python
# Before
compound_stmt[GeneratedStmt]:
    | &('def' | '@' | ASYNC) function_def
    | &'if' if_stmt
    ...

# After
compound_stmt[GeneratedStmt]:
    | &('def' | '@' | ASYNC) a=function_def { a }
    | &'if' a=if_stmt { a }
    ...
```

**수정된 5개 규칙**:
1. ✅ compound_stmt (8 alternatives)
2. ✅ annotated_rhs (2 alternatives)
3. ✅ params (2 alternatives)
4. ✅ closed_pattern (8 alternatives)
5. ✅ lambda_params (2 alternatives)

### Phase 3: 재생성 및 검증 ✅
- ✅ PegGenerator 재실행 성공
- ✅ PyParser.cs 재생성 (11,544 lines)
- ✅ 5개 규칙 모두 `return a;` 생성 확인
- ✅ GeneratedPlaceholder 대신 실제 값 반환

**검증 결과**:
- `compound_stmt`: `return a;` (function_def, if_stmt 등 반환)
- `annotated_rhs`: `return a;` (yield_expr 또는 star_expressions)
- `params`: `return a;` (parameters 또는 invalid_parameters)
- `closed_pattern`: `return a;` (매칭된 pattern)
- `lambda_params`: `return a;` (lambda_parameters 또는 invalid_lambda_parameters)

### Phase 4: 빌드 테스트 ⚠️
**결과**: 842개 에러 (기존 421개에서 증가)
- **원인**: 타입 변환 문제 (`GeneratedPtr` → 구체적 타입)
- **분석**: Action code 추가로 인한 타입 불일치 노출
- **다음 단계**: 타입 캐스팅 또는 helper method 구현 필요

---

## 📝 추가 작업 필요

현재 구현으로 5개 규칙은 올바른 값을 반환하지만, 타입 시스템 문제가 남아있음:

1. **GeneratedPtr vs 구체적 타입**:
   - PyParser 메서드들이 `GeneratedPtr?` 반환
   - Action code는 구체적 타입 요구 (GeneratedStmt, GeneratedExpr 등)
   - 타입 캐스팅 또는 제네릭 메서드 필요

2. **PyAst/PyParserHelpers 미구현**:
   - PyAst.Expr, PyAst.AnnAssign 등 미구현
   - PyParserHelpers.AugOperator 등 미구현

**다음 단계**: Helper 메서드 구현 또는 타입 시스템 재검토
