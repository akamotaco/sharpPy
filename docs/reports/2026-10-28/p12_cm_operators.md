# p12 컨텍스트 매니저·연산자 리플렉션 (2026-10-28)

`tests/bughunt/p12_cm_operators.py` 추가 → bughunt **19/19 matching**.

## 프로브 결과

with/`__enter__`/`__exit__`(예외 reraise, swallow), contextlib.contextmanager,
리플렉션 add/mul/sub, subclass 우선, `__index__`, divmod 기본, rich compare
(`__eq__` hash, `__lt__` 반사, 예외 전파)는 이미 CPython과 일치.
실제 갭은 아래 5개뿐이었다.

## 수정

- **슬롯 NotImplemented 시맨틱** (`core/PyObject.cs`):
  `LeftShift/RightShift/BitwiseAnd/BitwiseOr/BitwiseXor/DivMod/MatrixMultiply`
  기본 구현이 TypeError를 직접 던지던 것을 CPython slot 규칙의
  `PyNotImplemented` 반환으로 변경. 이제 `list << User()`가
  `User.__rlshift__`에게 기회를 준다 (CPython binary_op1 순서).
  최종 TypeError는 `ExecuteBinaryOpType`이 기존과 동일한 문구로 생성.
- **일관성 패치**: sentinel을 검사하지 않는 호출처들을 보호 —
  `parser/AstExpressions.cs`(레거시 evaluator), `modules/_OperatorModule.cs`
  (lshift/rshift/and_/or_/xor_/matmul → `Require` 래퍼),
  `core/PyScope|PyType|PyUnionType`의 `type | non-type` fallback.
- **matmul** (`runtime/PyVM.cs`): MATRIX_MULTIPLY case가 항상
  `NotImplementedError("not yet implemented")`를 던지던 자리 →
  `left.MatrixMultiply(right)` + 기존 반사 테이블(`__matmul__/__rmatmul__`)
  연결. INPLACE case도 `TryInplaceOp(__imatmul__ → matmul)` 경로.
  `1 @ 2` → `unsupported operand type(s) for @: 'int' and 'int'`.
- **divmod** (`core/PyBuiltin.CallDivmod`): left 실패 시
  `right.__rdivmod__` 반사, 없으면
  `unsupported operand type(s) for divmod(): 'int' and 'DM'`
  (이전엔 NotImplemented 객체가 그대로 새어 나옴).
- **유니연산자/abs**: `PyClassInstance`가 `__pos__/__neg__/__invert__`
  (`Positive/Negative/BitwiseNot` override)와 `__abs__`
  (`AbsoluteOrNotImplemented`)를 디스크립터 룩업으로 호출. abs는 두 구현
  (`PyBuiltin.CallAbs`, `BuiltinsModule.Abs`) 모두 패치 + bool 회귀
  (`abs(True) == 1`) 추가.

## 검증

- `tools/run_bughunt.py` 19/19, `tools/evaluate.py` 225 + 3 + 2 유지.
- perf probe reps4: 전 워크로드 ±6.3% 이내(노이즈).
- 주의: base 시프트/비트연산이 NotImplemented를 반환하므로 sentinel을
  검사하지 않는 새 호출経路는 반드시 TypeError로 승격해야 한다.
