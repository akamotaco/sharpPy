# SharpPy TODO

측정일 2026-11-01, 브랜치 `work/core-semantics`. 항목은 증상이 아니라 경로 단위로 쓴다. 절차는 [AGENTS.md](../AGENTS.md)의 "단일 경로 점검"을 따르고, 결함의 상세 기록은 [known_gaps.md](known_gaps.md)에 둔다.

## 작업 원칙

- 순서는 언어와 내장 동작, 그다음 라이브러리다. 실행 속도는 1000배 이상 느린 경우가 아니면 뒤로 미룬다.
- 고치기 전에 CPython 3.12.15의 해당 함수를 읽고 같은 구조로 옮긴다. 증상만 맞추는 분기, 이름 비교, 특수 사례 목록은 넣지 않는다.
- 순수 dotnet만 쓴다. `unsafe`와 C 어셈블리는 쓰지 않는다. Godot에서 돌아가야 한다.
- 라이브러리는 가능하면 CPython의 `.py`를 복사한다.

## 현재 수치

| 검증 | 값 | 재현 |
|---|---|---|
| CPython 언어 테스트 | 1889 / 2890 통과 | `python tools/run_cpython_tests.py` |
| 버그 프로브 | 166 / 166 | `python tools/run_bughunt.py` |
| 평가 | 225 일치, 3 변동 필드, 2 참조 없음 | `python tools/evaluate.py --dotnet <dotnet> --timeout 90` |
| Godot | 82 일치 | `.artifacts/round30-godot`에서 `--replay` |

언어 테스트 수치의 한계:

- `test.support`는 양쪽 모두 `tools/cpython_test_shim`의 작은 스텁이다. CPython 전체 테스트 환경의 수치가 아니다.
- `test_builtin`, `test_asyncgen`은 CPython 쪽에서도 로드 단계에서 건너뛰고, `test_extcall`은 doctest만 있어 0개로 잡힌다. 세 모듈은 분모에 없다.
- `.artifacts/cpython_tests/*.cpython.txt`는 캐시다. 드라이버나 스텁을 바꾸면 지우고 다시 만든다. 이전 보고의 1762/2597은 오래된 캐시 때문에 분모가 빠진 값이다.

## 1. 예외 경로

**1-1. .NET 예외가 Python 예외가 되지 않는다 (최우선).** VM 안에서 난 `IndexOutOfRangeException` 같은 CLR 예외를 `except BaseException`으로 잡을 수 없고, 실행 전체가 끝난다. 아래 모듈의 미도달 테스트 대부분이 이 때문이다.

| 모듈 | 통과 / CPython | 도달 못함 | 끝난 테스트 |
|---|---|---|---|
| test_bytes | 6 / 279 | 269 | `test_additional_rsplit` |
| test_fstring | 0 / 82 | 82 | 로드 실패, `_ast` 없음 (5-1) |
| test_descr | 54 / 148 | 48 | `test_specialized_method_call` |
| test_float | 3 / 49 | 41 | `test_float_ceil` |
| test_list | 24 / 58 | 31 | `test_index` |
| test_exceptions | 41 / 85 | 28 | `test_unicode_error_str_does_not_crash` |
| test_range | 4 / 28 | 21 | `test_index` |
| test_tuple | 14 / 31 | 15 | `test_index` |
| test_format | 0 / 16 | 11 | `test_locale` |
| test_property | 16 / 29 | 7 | `test_property_set_name_incorrect_args` |

- 경로: `PyVM.ExecuteFrame`의 예외 처리. CPython에는 C 수준 예외가 없으므로, 어떤 CLR 예외를 어떤 Python 예외로 바꾸는지 한 지점에서 정한다.
- `test_range`의 원인은 확인했다. 나머지 모듈은 끝나는 위치만 확인했고 원인이 같은지는 고친 뒤 다시 측정해야 한다.

**1-2. `BuiltinSlots.Method`에 인자 수 검사가 없다.** `range(2).index()`가 `TypeError` 대신 1-1의 크래시가 된다. `core/BuiltinSlots.cs:44`.

- 경로: `Method` 등록 시 인자 범위를 받아 `_PyArg_CheckPositional`에 해당하는 검사를 한 곳에서 한다.

**1-3. `test_abc` 프로세스 종료.** `test_abstractstaticmethod_basics`에서 끝난다. 원인 미확인.

## 2. 컴파일 시 검사

`Python/symtable.c`, `Python/compile.c`, `Parser/string_parser.c`, `pegen_errors.c`와 대조한다.

- 2-1. 대입식의 금지 형태: 컴프리헨션 반복 변수 재바인딩, 반복 가능 식 안의 대입식. `symtable_extend_namedexpr_scope`와 `NAMED_EXPR_COMP_*`. test_named_expressions 46/74.
- 2-2. 올바르게 거부해야 할 소스를 받아들인다. `SyntaxError not raised` 21건, `TypeError not raised` 9건.
- 2-3. SyntaxError 위치: `testSyntaxErrorOffset` 서브테스트 62건, `test_exception_locations` 계열.
- 2-4. 메시지: `positional argument follows keyword argument`가 generic `invalid syntax`로 나온다. `invalid_*` 문법 규칙의 두 번째 파스 경로를 확인한다.
- 2-5. 문자열 리터럴: `warn_invalid_escape_sequence`가 없어 `"\d"`, `"\777"`에 SyntaxWarning이 없다. `b"é"`가 오류가 아니다. test_string_literals 0/20.

## 3. 타입과 디스크립터

`Objects/typeobject.c`, `Objects/descrobject.c`, `Objects/funcobject.c`와 대조한다.

- 3-1. 내장 타입을 상속한 클래스의 `__init__`: `StringIO` 하위 클래스에서 `descriptor '__init__' requires a 'StringIO'` 13건. doctest 전체를 막는다(`_SpoofOut`). test_extcall이 여기에 걸린다.
- 3-2. `object.__init__` 인자 규칙 9건, `__init_subclass__`의 키워드 전달. `object_init`, `excess_args`.
- 3-3. `__subclasshook__` 7건, `classmethod() argument must be callable (got property)`.
- 3-4. 함수 속성: `__defaults__` 등 setter (P0-71). test_funcattrs 15/33.
- 3-5. 비교와 동등성: slot wrapper와 바인드 메서드 비교 (P0-56, P0-67, P0-69).
- 3-6. `raise`한 객체와 `except`에서 받은 객체의 동일성 (P0-70).
- 3-7. 내장 이터레이터를 상속한 클래스, `set` 하위 클래스의 `repr` 경로, `__bases__` 대입 검사 (P0-83).

## 4. 이터레이터와 제너레이터

- 4-1. `__length_hint__`: `set_iterator` 등 7건. `list()`, `tuple()`의 길이 힌트 호출 (P0-83).
- 4-2. 이터레이터 피클: `enumerate`, `set_iterator`, `range_iterator`, `iterator` 등 `__reduce__`.
- 4-3. 제너레이터와 코루틴: test_generators 22/39, test_coroutines 70/89. `cr_frame` 등.
- 4-4. 인라인 컴프리헨션의 셀 격리 (P0-79, P0-81). `push_inlined_comprehension_state`. 현재 셀이 `LocalsPlus` 밖의 `Cells`에 있는 구조가 원인이다.

## 5. 모듈 표면

- 5-1. `_ast`: `ast`가 필요로 하고, test_builtin과 test_fstring의 로드를 막는다.
- 5-2. `os.open` 20건, `raw-unicode-escape` 코덱 9건, `tempfile` 없음.
- 5-3. `KeyError: 'importlib'` 7건. 원인 미확인.
- 5-4. `weakref` 프록시, `bytearray %`.
- 5-5. 그 밖의 라이브러리 목록은 P0-83. 필요성이 확인된 것만 한다.

## 6. PEP 695

- 6-1. 클래스 안의 제네릭 함수가 클래스 이름을 찾지 못한다. `__classdict__` 경로가 없다.
- 6-2. `type X[T] = ...`가 지연 평가를 하지 않고 `TypeAliasType`이 아니다.

## 7. 성능 (1000배 이상만)

- 7-1. `bytes.find`가 최악 입력에서 이차 시간이다. `Objects/stringlib/fastsearch.h`의 two-way를 옮긴다. 그때까지 `tools/cpython_test_driver.py`의 `KNOWN_TOO_SLOW`가 `test_adaptive_find`를 양쪽에서 건너뛴다. 옮긴 뒤 이 목록을 지운다.

## 8. 코드의 미완성 표시

`TODO`, `임시`, `For now` 등이 120곳 있고 상당수는 `runtime/AdaptiveSpecializer.cs` 골격이다(비활성). 표시는 동작을 숨길 수 있으므로 CPython과 대조해 고치거나 지운다. 먼저 볼 곳:

- `core/PyFunction.cs:403`: kwargs를 무시한다는 표시.
- `core/PyClassmethod.cs:38`: 그대로 반환하는 분기.
- `core/PyType.cs:307`: 하위 클래스 캐시 무효화가 없다는 표시. 잘못된 메서드 조회로 이어질 수 있다.
- `core/PyType.cs:1888`: 여분 인자 허용.
- `core/PyModule.cs:835`: `__spec__.parent` 미사용.
- `runtime/compile.cs`, `Parser/ast.cs`: 컴프리헨션 대입식과 제네릭의 `임시` 표시.
- `runtime/CFGOptimizer.cs:38`: `inline_small_exit_blocks` 없음. 동작에는 영향이 없는지 확인한다.

## 구조적 차이 (의도적으로 남김)

- 객체 수명은 참조 카운트가 아니라 CLR GC를 따른다. `del` 직후 `weakref`, `__del__` 시점, 버려진 제너레이터의 `finally` 시점이 CPython과 다르다.
- 네이티브 재귀 한계는 `TryEnsureSufficientExecutionStack`로 판단하므로 `RecursionError`가 나는 깊이가 CPython과 다르다.
- `str` 인덱싱의 UTF-16 문제는 P0-34에 기록되어 있다.
