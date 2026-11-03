# SharpPy TODO

측정일 2026-11-02, 브랜치 `work/core-semantics`. 항목은 증상이 아니라 경로 단위로 쓴다. 절차는 [AGENTS.md](../AGENTS.md)의 "단일 경로 점검"을 따르고, 결함의 상세 기록은 [known_gaps.md](known_gaps.md)에 둔다.

## 작업 원칙

- 순서는 언어와 내장 동작, 그다음 라이브러리다. 실행 속도는 1000배 이상 느린 경우가 아니면 뒤로 미룬다.
- 고치기 전에 CPython 3.12.15의 해당 함수를 읽고 같은 구조로 옮긴다. 증상만 맞추는 분기, 이름 비교, 특수 사례 목록은 넣지 않는다.
- 순수 dotnet만 쓴다. `unsafe`와 C 어셈블리는 쓰지 않는다. Godot에서 돌아가야 한다.
- 라이브러리는 가능하면 CPython의 `.py`를 복사한다.

## 현재 수치

| 검증 | 값 | 재현 |
|---|---|---|
| CPython 언어 테스트 | 3431 / 3475 통과 (새로 돌린 모듈을 더하면 5850 / 7223) | `python tools/run_cpython_tests.py` |
| AST (Lib/*.py) | 5550 / 5550 문장, 위치 포함 5550 | `python tools/compare_ast.py --dotnet <dotnet> [--attributes] <files>` |
| AST (프로브) | 6397 / 6397 문장 | `python tools/compare_ast.py --dotnet <dotnet> --attributes` |
| 바이트코드 (Lib/*.py) | 코드 객체 8039개 전부 `co_code`, `co_linetable`(열 번호 포함), `co_exceptiontable`, `co_stacksize` 바이트 일치 | `python tools/compare_bytecode.py --dotnet <dotnet> --level raw <files>` |
| 바이트코드 (프로브) | 코드 객체 5325개 전부 일치 | `python tools/compare_bytecode.py --dotnet <dotnet> --level exceptions` |
| 심볼 테이블 (Lib/*.py) | 스코프 8039개 전부 플래그까지 일치 | `python tools/compare_symtable.py --dotnet <dotnet> <files>` |
| 버그 프로브 | 179 / 179 | `python tools/run_bughunt.py` |
| 평가 | 225 일치, 3 변동 필드, 2 참조 없음 | `python tools/evaluate.py --dotnet <dotnet> --timeout 90` |
| Godot | 82 일치 | `.artifacts/round30-godot`에서 `--replay` |

언어 테스트 수치의 한계:

- `test.support`는 양쪽 모두 `tools/cpython_test_shim`의 작은 스텁이다. CPython 전체 테스트 환경의 수치가 아니다.
- `test_builtin`, `test_asyncgen`은 CPython 쪽에서도 로드 단계에서 건너뛰고, `test_extcall`은 doctest만 있어 0개로 잡힌다. 세 모듈은 분모에 없다.
- `.artifacts/cpython_tests/*.cpython.txt`는 캐시다. 드라이버나 스텁을 바꾸면 지우고 다시 만든다. 이전 보고의 1762/2597은 오래된 캐시 때문에 분모가 빠진 값이다.

바이트코드 게이트는 `--level names|arguments|lines|exceptions|raw`로 비교한다. `raw`는 코드 객체의 표를 바이트 단위로 본다. 대상을 주지 않으면 `tests/bughunt`의 프로브다.

## 0. 배선 점검

출력 비교는 틀린 경로가 같은 값을 내면 잡지 못한다. 2026-11-01 측정에서 프로브 전부 통과 상태의 동일 코드 객체는 5175개 중 713개였다. 다음 항목은 출력이 맞아도 CPython과 구조가 다르다. 항목을 고치거나, C#과 C의 차이로 받아들인다면 이유를 여기에 적는다.

| 경로 | CPython | SharpPy | 판단 |
|---|---|---|---|
| AST | ASDL 하나 | ASDL 하나. 구형 AST와 `Bridge`는 지웠다 | 끝 |
| 이항 연산 | `binary_op1`, `binary_iop1`, `SLOT1BINFULL`, `sequence_repeat` | 같은 구조로 포팅 (`PyVM.BinaryOp1`, `SlotNumberBinary`, `InPlaceNumber`). C# 진입점도 모두 `ExecuteBinaryOpType`을 지난다. `int`, `float`, `str` 하위 클래스의 이항 슬롯은 값의 슬롯만 부른다. 단항 연산은 아직 CLR 가상 메서드다 | 끝 (단항은 3절) |
| 속성 조회 | `tp_getattro` 슬롯, 대부분 `PyObject_GenericGetAttr` | `_PyType_Lookup`은 `PyType.FindNameInMro` 하나다. 내장 타입의 Python 하위 클래스는 `slot_tp_getattr_hook`(`SubclassInstanceSupport.GetAttribute`)과 `GenericGetAttr`로 나뉘고 `object.__getattribute__`는 뒤쪽을 부른다. `LOAD_ATTR`은 타입의 메서드 디스크립터를 묶지 않고 `[descriptor, self]`를 쌓는다(`_PyObject_GetMethod`). 남은 것: 내장 타입 자체의 `GetAttribute` override가 이름별 switch로 답하는 곳 | 진행 중 |
| 호출 | `tp_call`, vectorcall | Python 클래스의 인스턴스는 `slot_tp_call`(`PyObject.Call`): 타입의 `__call__`을 `lookup_maybe_method`로 묶는다. 내장 메서드의 인자 개수 오류는 호출 규약 표(`core/BuiltinArity.g.cs`, `tools/gen_builtin_arity.py`)를 따른다: `METH_NOARGS`, `METH_O`, clinic, `PyArg_ParseTuple`. `**mapping`으로 부른 `METH_VARARGS` 메서드의 키워드 거부 문구(`tp_call` 경로)는 구분하지 않는다 | 끝 |
| 인스턴스 생성 | `tp_new` + `tp_init` | 내장 `__new__`는 `tp_new_wrapper`(`PyType.CheckNewArguments`)를 지난다. 클래스 본문에 둔 내장 `__new__`는 `update_one_slot`처럼 물려받은 `tp_new`를 바로 부른다. 남은 것: 가상 메서드 `CreateInstance` | 진행 중 |
| 오류 전달 | NULL 반환과 스레드 상태 | CLR 예외. `ExecuteFrame`이 어떤 CLR 예외든 Python 예외로 바꾼다 | 유지 |
| `FOR_ITER`/`END_FOR` | 3.12의 `FOR_ITER`는 소진 시 이터레이터를 꺼내고 `END_FOR`를 건너뛴다(`JUMPBY(oparg + 1)`). `END_FOR`는 `FOR_ITER_GEN` 경로에서만 실행된다 | `FOR_ITER`가 꺼낸다. `FOR_ITER_GEN` 특수화는 없다 | 같음. 점프 계산이 코드 종류별로 셋이다 |
| 프레임 | `localsplus` 안에 셀 | 같은 구조. `MAKE_CELL`, `COPY_FREE_VARS`가 슬롯에 셀을 두고 `f_locals`는 `frame_get_var`를 따른다 | 끝 |
| 컴파일러 | `symtable.c`, `ast_opt.c`, `compile.c` | `runtime/symtable.cs`, `runtime/future.cs`, `runtime/compiler/`가 같은 함수 이름으로 대응한다. 진입점은 `Compiler.CompileSource` 하나 | 끝 |
| import | `importlib._bootstrap` | C# 로더 | 라이브러리 정책과 함께 결정 |

구조: 토큰, ASDL 트리, `AstOptimizer`, `PySymtable`, `Compiler`, `InstructionSequence`, `PyFlowGraph.Build`, `CFGOptimizer`, `PyAssemble`. CPython과 단계가 같다.

## 1. 예외 경로

**1-1. 시간 초과로 끝나는 모듈은 없다.** CLR 예외는 `ExecuteFrame`에서 Python 예외가 되고, `bytes.find`와 `count`는 최악에도 선형이다.

## 2. 컴파일 시 검사

`Parser/Tokenizer.cs`는 `tokenizer.c`, `Parser/PyParserBase.Strings.cs`는 `string_parser.c`다. 파서는 토큰을 규칙이 요구할 때 읽고, 오류 우선순위는 `pegen_errors.c`를 따른다. `tests/bughunt/p173`이 오류 약 110건의 메시지, 줄, 열, 끝 열, 원문을 CPython과 비교한다. 남은 것:

- 2-1. `type_comments`와 `compile(..., "func_type")`: 토크나이저와 파서가 `TYPE_COMMENT`, `TYPE_IGNORE`를 내지 않는다.
- 2-2. 원본이 UTF-8이 아닌 바이트 소스의 끝 열: 깨진 바이트를 U+DC80~DCFF로 들고 있어 폭이 1바이트 어긋난다.
- 2-3. test_string_literals는 `os.open`이 없어 먼저 막힌다.

## 3. 타입과 디스크립터

`Objects/typeobject.c`, `Objects/descrobject.c`, `Objects/funcobject.c`와 대조한다.

- 3-0. `add_operators`: `PySlotWrappers`와 생성 표 `core/SlotTable.g.cs`가 슬롯 래퍼를 준다. `PyType.SlotDict`에 따로 두었고 디스패치는 읽지 않는다(`vars`, `dir`, 속성 조회만). 슬롯이 `TypeDict`로 들어가면 특수 메서드 조회가 같은 CLR 가상 메서드로 되돌아오기 때문이다. `tools/type_surface_probe.py`로 비교하면 빠진 항목이 273개 남았다(`co_*`, `f_*`, `tb_*`, `start` 같은 getset과 `__sizeof__`, `__reduce__` 등).
- 3-1. C# 클래스로 된 내장 타입의 상속: `StringIO`, `BytesIO`, `bytearray`, `classmethod`, `staticmethod`는 `tp_new`가 하위 타입을 할당한다. `complex` 등 남은 타입은 같은 방식으로 옮긴다. test_descr 112/148, 남은 항목은 개별 동작 차이다. test_extcall이 여기에 걸린다.
- 3-2. `object.__init__` 인자 규칙 9건, `__init_subclass__`의 키워드 전달. `object_init`, `excess_args`.
- 3-4. 함수 속성: `__defaults__` 등 setter (P0-71). test_funcattrs 15/33.
- 3-5. 비교와 동등성: slot wrapper와 바인드 메서드 비교 (P0-56, P0-67, P0-69).
- 3-6. `raise`한 객체와 `except`에서 받은 객체의 동일성 (P0-70).
- 3-7. 내장 이터레이터를 상속한 클래스, `set` 하위 클래스의 `repr` 경로, `__bases__` 대입 검사 (P0-83).

## 4. 이터레이터와 제너레이터

- 4-1. 이터레이터의 `__reduce__`, `__setstate__`, `__length_hint__`는 끝났다. `list()`, `tuple()`이 길이 힌트를 호출하는지는 대조가 남았다.
- 4-3. 제너레이터와 코루틴: test_generators 22/39, test_coroutines 70/89. `cr_frame` 등.

## 5. 모듈 표면

- 5-0. 라이브러리 복사: `encodings`, `pickle`, `warnings`, `_collections_abc`, `dis`, `opcode`, `inspect`, `dataclasses`, `tokenize`, `linecache`, `tracemalloc`, `abc`, `types`, `keyword`, `io`, `stat`, `colorsys`, `collections.abc`는 CPython 원본이다. `_warnings`는 `Lib/_warnings.py`, `_tracemalloc`는 추적 없는 스텁이다. `collections/__init__.py`, `os.py`, `pathlib.py`, `hashlib.py`, `shutil.py`, `asyncio`는 아직 수정본이다.
- 5-1. `importlib`: `Lib/importlib/`는 네이티브 import 위의 얇은 패키지다. `machinery`의 클래스는 이름만 있다.
- 5-2. `os.open` 20건, `tempfile` 없음.
- 5-4. `weakref` 프록시, `bytearray %`.
- 5-5. 그 밖의 라이브러리 목록은 P0-83. 필요성이 확인된 것만 한다.
- 5-6. `io` 계층: `open()`과 표준 스트림 모두 `FileIO`→`Buffered*`→`TextIOWrapper`이다(`_iomodule.c`, `pylifecycle.c create_stdio`). 남은 차이: fd 0~2는 설계상 콘솔이라, 표준 출력이 파일로 리디렉션되어도 `seekable()`이 False다(CPython은 True). `sys.stdin`의 `isatty`는 NUL 장치에서 False다(Windows CPython은 True).
- 5-6a. 수명 모델: CPython gc는 순환 쓰레기의 약한 참조를 `tp_finalize`보다 먼저 지운다(`handle_weakrefs`). SharpPy 가드는 종결자 뒤에 지우므로, 종결자가 `ResourceWarning(source=...)` 등으로 객체를 되살리면 `test_io`의 `test_garbage_collection` 6건처럼 약한 참조가 살아 있다.
- 5-7. 2026-11-03 새로 돌린 모듈의 큰 묶음: `test_array` 404건(메서드 인자 검사, `fromfile`), `test_itertools`·`test_weakref` 시간 초과, `test_types` `_testinternalcapi`, `test_collections` doctest가 `inspect.getmodule`에서 멈춤, `test_time` `mktime`, `test_sys` 13건, `test_frame` 11건(`frame.clear`, `f_locals`), `test_memoryview` 18건(다차원 형식), `test_struct` 23건.
- 5-8. `sys.path_hooks`, `sys.path_importer_cache`는 빈 목록이다. C# 내장 모듈(`math` 등)에 `__spec__`이 없다. 파일 찾기는 C# `PathFinder` 하나다. 테스트 드라이버 밖의 자식 SharpPy에서 `import test.test_threading`은 CPython `importlib/_bootstrap_external.py`가 `winreg`를 찾다 실패한다(`test_4_daemon_threads`를 드라이버에서 건너뛰는 이유). `importlib.util.cache_from_source`는 `__pycache__`와 태그 없이 `a/b.pyc`를 돌려준다.

## 6. PEP 695

컴파일은 끝났다. 남은 것은 `typing` 쪽 런타임이다: `TypeAliasType.__value__`, `__type_params__`, 제네릭 클래스 피클. test_type_params 27/38.

## 8. 코드의 미완성 표시

`TODO`, `임시`, `For now` 등이 120곳 있었고(2026-11-01 측정, 이번 대조 이전 수치) 상당수는 `runtime/AdaptiveSpecializer.cs` 골격이다(비활성). 표시는 동작을 숨길 수 있으므로 CPython과 대조해 고치거나 지운다. 먼저 볼 곳:

- `core/PyFunction.cs:403`: kwargs를 무시한다는 표시.
- `core/PyClassmethod.cs:38`: 그대로 반환하는 분기.
- `core/PyType.cs:307`: 하위 클래스 캐시 무효화가 없다는 표시. 잘못된 메서드 조회로 이어질 수 있다.
- `core/PyModule.cs:835`: `__spec__.parent` 미사용.
- `runtime/PyVM.cs` `RERAISE`: 인자 0일 때 처리 중인 예외가 없으면 넘어가는 분기. CPython에는 없다.

## 구조적 차이 (의도적으로 남김)

- 객체 수명은 참조 카운트가 아니라 CLR GC를 따른다. `del` 직후 `weakref`, `__del__` 시점, 버려진 제너레이터의 `finally` 시점이 CPython과 다르다.
- 네이티브 재귀 한계는 `TryEnsureSufficientExecutionStack`로 판단하므로 `RecursionError`가 나는 깊이가 CPython과 다르다.
- `str` 인덱싱의 UTF-16 문제는 P0-34에 기록되어 있다.
