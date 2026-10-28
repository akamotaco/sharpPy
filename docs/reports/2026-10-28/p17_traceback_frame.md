# p17 — traceback / frame / code object parity (2026-10-28)

## 스코프
`tests/bughunt/p17_traceback_frame.py`: `traceback` 모듈(print_exception/format_exc/
format_tb/extract_tb/TracebackException, `file=` 리다이렉트, `from`/암시적 체인 마커),
frame 객체(sys._getframe, f_code/f_locals/f_globals/f_builtins/f_back/f_lineno,
깊이 초과, 타입 이름), code 객체(co_name/argcount/kwonly/varnames/qualname/firstlineno,
읽기 전용, repr). 기준 CPython 3.12.15.

## 수정 (8종)
1. **print(file=) 무시** — `core/PyBuiltin.cs`: `print`가 `file`을 추출해놓고
   `TODO`로 Console에만 출력. `file.write(...)`(+`flush`면 `file.flush()`)로 위임,
   오류(속성 없음/닫힌 파일)는 호출자로 전파. → `traceback.print_exception(file=buf)`
   계열이 한 번에 복원. (p17의 28줄 diff 중 대부분이 이것이 단일 원인.)
2. **type(frame).__name__** — `PyFrame.GetPyType`이 `ObjectType` 반환 → 전용 `frame`
   PyType.
3. **type(code).__name__** — `PyCodeObject.GetPyType` → 전용 `code` PyType.
4. **co_qualname 누락** → `DisplayQualName`(없으면 co_name) 노출.
5. **co_argcount** — sp는 non-variadic 합계를 저장해 kwonly를 포함. cp 정의
   (posonly+pos/kw)에 맞게 `ArgCount - KwonlyArgCount`로 노출(내부 호출 규약 불변).
6. **code 속성 쓰기** → CPython과 동일 `AttributeError: readonly attribute`.
7. **co_name / repr의 lambda 이름** — sp 내부 고유명 `<lambda_N>`은 호출 에러 매핑용이라
   유지하고, 표시 경로(co_name/co_qualname/`repr(func)`)만 `<lambda>`로 매핑.
   `PyFunction.ToRepr` → `<function NAME at 0x..>`.
8. **sys._getframe 깊이 초과 문구** — `call stack is not deep enough (depth N)` →
   cp와 동일하게 접미 제거.

## 핵심 발견 — 예외 스택 스코프 누출 (raise-from/chain 포맷 오염의 근원)
증상: 앞선 try/except가 끝난 뒤 **독립적**으로 raise된 예외의 `__context__`가 지난
핸들러 예외를 가리킴(`te_z2`/`p17c3`에서 8줄 재현). 결과적으로 `print_exception`이
`from` 체인에서 "During handling..." 마커를 초과 출력.

원인(추측 아닌 CPython 3.12 소스로 확정):
- `Python/bytecodes.c` PUSH_EXC_INFO/POP_EXCEPT + `errors.c:_PyErr_GetTopmostException`.
- CPython은 **__context__를 raise 시점(unwind 전)의 topmost**로 잡고, unwind로
  탈출한 핸들러의 `_PyErr_StackItem`을 버린 뒤의 상태를 `CurrentException`으로 쓴다.
  sp는 둘을 단일 `frame.CurrentException` 필드에서 혼동했고, 핸들러 본문에서
  raise로 이탈할 때 그 항목이 남았다(POP_EXCEPT가 오히려 stale 값을 복원).

수정(`runtime/PyVM.cs`, 최소):
- `PyFrame.HandledScopes`: `(tryStartDepth, exc)` 스코프 스택(lazy — 예외 미사용
  프레임은 할당 없음).
- 예외 dispatch에서: unwind **전** `CurrentException`으로 `__context__` 캡처 →
  depth ≥ target 스코프 폐기 → 살아남은 enclosing 핸들러를 `CurrentException`으로 설정
  → 새 스코프 push.
- POP_EXCEPT: 스코프 최상위 폐기 후 `CurrentException` = 상위 스코프 또는 None.
- PUSH_EXC_INFO: `__context__` 캡처 제거(이제 dispatch 소관), prev/current/스택만.

## 검증
- p17 stdout **diff 0**. `te_z2`/`te_seq`/`p17c3` 단독 재현군 CPython 일치.
- bughunt **24/24**, evaluate `225 + 3 + 2`(불변).
- Godot 4.7.2 mono: 기본 프로브(Debug DLL) `SHARPPY_GODOT_PASS`,
  replay(Release DLL) **82 matched, 0 other** — 예외 중심 82 fixture 무회귀.
- perf: 예외 스택은 dispatch 전용 경로라 무관. str_fmt/list_comp의 ±표시는
  **HEAD 재빌드 대조로 동일 노이즈 확인**(HEAD도 str_fmt +24~+29% 요동).
  p17 변경 자체는 이 workload 코드 경로에 영향 없음.

## 알려진 한계
- frame의 f_trace/f_trace_lines/f_trace_opcodes는 still 스텁.
- code의 co_code(CPython 바이트코드)는 None — SharpPy 바이트코드는 비노출(의도).
- co_names/co_consts/co_stacksize는 완전 정밀 미보장(p17 범위 외).
