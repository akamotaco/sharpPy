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
| 속성 조회 | `tp_getattro` 슬롯, 대부분 `PyObject_GenericGetAttr` | `_PyType_Lookup`은 `PyType.FindNameInMro` 하나다. 내장 타입의 Python 하위 클래스는 `slot_tp_getattr_hook`(`SubclassInstanceSupport.GetAttribute`)과 `GenericGetAttr`로 나뉘고 `object.__getattribute__`는 뒤쪽을 부른다. `LOAD_ATTR`은 타입의 메서드 디스크립터를 묶지 않고 `[descriptor, self]`를 쌓는다(`_PyObject_GetMethod`). 슬롯 래퍼(`SlotDict`)도 `FindNameInMro`가 찾고, 모듈은 `md_dict`를 인스턴스 사전으로 쓰는 일반 조회다. `object`의 기본 `GetAttribute`에 있던 `__class__`/`__str__`/`__repr__`/`__init__` switch는 지웠다. 남은 것: 예외 타입들이 멤버(`name`, `errno` 등)를 디스크립터와 별개로 `GetAttribute` switch로 답하는 곳(타입 사전에는 이미 `member_descriptor`가 있다), 함수·코드 객체 등 | 진행 중 |
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
- 2-4. 파일 모드 토크나이저: 주 스크립트도 `compile()`처럼 문자열로 토큰화한다. CPython은 `tok_underflow_file`로 읽고, 오류 줄은 `_PyPegen_raise_error_known_location`이 `_PyErr_ProgramDecodedTextObject`로 파일에서 다시 읽는다. 그래서 파일 끝의 `\`는 CPython에서 `offset` 0(캐럿 없음), `text`에 줄바꿈 없음이고 SharpPy는 문자열 모드 값(`offset` 2)이다. test_eof의 bpo2180 하나.

## 3. 타입과 디스크립터

`Objects/typeobject.c`, `Objects/descrobject.c`, `Objects/funcobject.c`와 대조한다.

- 3-0. `add_operators`: `PySlotWrappers`와 생성 표 `core/SlotTable.g.cs`가 슬롯 래퍼를 준다. `PyType.SlotDict`에 두고 `_PyType_Lookup`(`FindNameInMro`)이 각 MRO 단계에서 함께 본다. CLR 가상 메서드를 부르는 래퍼(`CallsVirtual`)는 디스패치(`LookupSpecialMethodUncached`)가 건너뛴다. 그 가상 메서드가 곧 타입의 C 슬롯이기 때문이다. 슬롯이 있는지만 묻는 곳은 `HasTypeSlot`이다. 남은 차이: `float.__repr__`, `tuple.__len__`, `BaseException.__str__`, `builtin_function_or_method.__call__`처럼 메서드나 함수로 등록된 슬롯. `tools/type_surface_probe.py`로 비교하면 빠진 항목은 `memoryview._from_flags` 하나다(구조적 차이에 적었다; 2026-11-08 측정).
- 3-0a. 타입 조회는 `PyType.FindNameInMro` 하나다(`LookupInMRO`, `LookupSpecial`, `LookupUserSpecial`, `__setattr__`/`__delattr__` 훅이 모두 이것을 부르고, 클래스 본문에 썼는지는 `written`이 말한다). 전역 메서드 캐시는 `ClassDict` 직접 쓰기에 무효화되지 않아 없앴다. 남은 것: `PyClass.GetTypeAttribute`(한 타입의 사전, `LookupOwnDict`와 같은 일)를 부르는 곳들. `super`는 `do_super_lookup`대로 각 기반의 자기 사전(`PyType.LookupOwnDict`, `FindNameInMro`와 공유)을 읽고, `LOAD_SUPER_ATTR`는 그 답을 다시 묶지 않는다. `builtin_function_or_method`, 디스크립터 다섯 종류, `method-wrapper`의 속성은 구조체 모양의 `ICFunction`(`ml_name`, `m_self`, `m_module`), `IDescrObject`(`d_name`, `d_type`), `IWrapperObject`(`descr`, `self`)를 타입 사전의 getset/member만 읽는다(`core/PyCFunction.cs`). 남은 뒤집힌 경로: `TypeTables`의 `Field`(타입 사전의 디스크립터가 객체의 `GetAttribute` override를 되부름)는 `method`, `GenericAlias`, struct에 남았다. 메서드 디스크립터의 세 번째 구현이던 `PyBuiltinMethod`와 쓰이지 않던 `PyBuiltinType`은 없앴다: `BaseExceptionGroup`의 멤버와 메서드, `_random.Random`, `type.mro`(`mro_implementation`)는 `PyMethodDescriptor`/`PyMemberDescriptor`이고, 디스크립터의 `__get__`/`__set__`/`__delete__`는 타입 사전의 `wrap_descr_*` 슬롯 래퍼만 답한다. `type`에는 `__class_getitem__`이 없고 `type[int]`는 `PyObject_GetItem`의 특례다. 남은 차이: 묶인 내장 메서드를 부를 때 인자 수 오류의 이름이 디스크립터 쪽(`type.mro()`)이다(CPython은 `meth_get__qualname__`, `int.mro()`). `PyInstance`(`object()`)의 `GetAttribute` override도 없앴다. 쓰이지 않는 `DescriptorUtils`는 예외를 삼키는 사본이다.
- 3-0b. 인스턴스 속성의 이중 경로: `GetAttribute` override 70개가 이름별로 답한다(약 140건). 예외 멤버는 `PyBaseException.TryGetMember`/`TrySetMember`(구조체 슬롯)를 `member_descriptor`만 읽고 쓴다. `SystemExit.code`는 아직 `CodeOf`/`StoreCode`로 따로 간다. 내장 예외의 타입 사전은 `exceptions.c`의 매크로 표(`core/ExceptionTable.g.cs`, `tools/gen_exception_table.py`)대로 `__init__`/`__new__`/`__str__`을 갖고, `__str__`은 각 `X_str`을 이름으로 부른다(`PyExceptionTable`). 각 `X_init`(`BaseExceptionInit`, `OSErrorFields`/`OSError.InitSlot`, `ImportErrorInit` 등)은 있는 구조체를 고치고, 생성자와 타입 사전의 `__init__`이 같은 함수를 쓴다. `OSError_new`/`oserror_use_init`도 있다. `BaseException.__new__`가 서브클래스 인스턴스의 구조체를 할당한다. 예외의 인스턴스 사전은 `IObjectDict`(`tp_dictoffset`)로 일반 getattr/setattr이 읽고, `BaseException_getset`/`__suppress_context__`(T_BOOL)는 필드를 직접 읽는다. 남은 것: 정확한 내장 타입의 호출은 아직 `CreateInstance`의 이름 switch가 생성자(할당+`X_init`)를 부른다(결과는 같다). `list.__class_getitem__`은 `classmethod_descriptor`가 아니다. 함수, 디스크립터, `GenericAlias`는 타입 사전 쪽도 맞지만 구현이 둘이다. `SetAttribute` 33개, `DelAttribute` 14개도 같다.
- 3-1. C# 클래스로 된 내장 타입의 상속: `StringIO`, `BytesIO`, `bytearray`, `classmethod`, `staticmethod`는 `tp_new`가 하위 타입을 할당한다. `complex` 등 남은 타입은 같은 방식으로 옮긴다. test_descr 112/148, 남은 항목은 개별 동작 차이다. test_extcall이 여기에 걸린다.
- 3-2. `object.__init__` 인자 규칙 9건, `__init_subclass__`의 키워드 전달. `object_init`, `excess_args`.
- 3-4. 함수 속성: `__defaults__` 등 setter (P0-71). test_funcattrs 15/33.
- 3-5. 비교와 동등성: slot wrapper와 바인드 메서드 비교 (P0-56, P0-67, P0-69).
- 3-6. `raise`한 객체와 `except`에서 받은 객체의 동일성 (P0-70).
- 3-7. 내장 이터레이터를 상속한 클래스, `set` 하위 클래스의 `repr` 경로, `__bases__` 대입 검사 (P0-83).
- 3-8. `type_setattro`는 `PyType.TypeSetAttribute` 하나다(불변 검사, 메타타입 데이터 디스크립터, `tp_dict`, `PyType_Modified`). `PyStructSequence_NewType`으로 만드는 타입(`time.struct_time`, `os.stat_result`, `os.terminal_size`, `nt.times_result`)은 힙 타입이다. 남은 것: `os.stat_result`에 `st_birthtime`, `st_birthtime_ns`가 없다(n_fields 18, CPython 20).

## 1a. 예외 출력

- 1a-1. 예외 출력은 `runtime/PyErrorDisplay.cs` 하나다: `_PyErr_Display`(`print_exception_recursive`, 체인, 그룹, 노트, `print_exception_file_and_line`/`print_error_text`, `_Py_Offer_Suggestions`)와 `traceback.c`의 `tb_printinternal`/`tb_displayline`(소스 줄, AST 앵커, 캐럿, 반복 줄 접기). `sys.excepthook`, `sys.__excepthook__`, `PyErr_PrintEx`(훅 실패 시 "Error in sys.excepthook")가 이것을 부른다. `traceback.py`는 라이브러리의 포매터로 따로 남는다(CPython과 같다).
- 1a-2. 줄 번호는 `PyFrame.CurrentLineNumber` 하나다(`PyFrame_GetLineNumber`: 마지막 명령의 `PyCode_Addr2Line`, 아무것도 돌지 않았으면 `co_firstlineno`). `f_lineno`(음수면 `None`), `tb_lineno`, 경고가 이것을 읽는다. 남은 것: `f_lineno` 쓰기(`frame_setlineno`, 추적 함수 안의 점프)가 없다.

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

- 5-10. `Lib/asyncio`는 SharpPy의 것이라 이벤트 루프가 `sys.set_asyncgen_hooks`를 걸지 않는다. 버려진 비동기 생성기는 인터프리터 쪽에서 `_PyGen_Finalize`대로 훅을 부르지만(`PyAsyncGenerator.Finalize`), 훅이 없으니 `gen_close`로 닫혀 `finally` 안의 `await`가 돌지 못한다.
- 5-9. 경로: dotnet 쪽 `platform/helper_dotnet.cs`는 OS 구분자를 그대로 둔다(`sys.path`, 모듈의 `__file__`과 `__path__`가 Windows에서 `\`). Godot 쪽은 `res://` 때문에 `/`다. 주 스크립트는 `config_run_filename_abspath`대로 절대 경로다. `sys.prefix`, `sys.exec_prefix`, `sys.base_prefix`, `sys.base_exec_prefix`는 `Lib`를 담은 디렉터리, `sys._stdlib_dir`은 그 `Lib`다(`SysModule.PrefixPath` 하나).

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

원칙: 이 아래의 차이는 플랫폼 때문에 감수하지만, 어떤 경우에도 **조용히 다른 결과를 내지 않고 예외로 드러나야** 한다. 예외로 드러나지 않는 항목은 그 사실을 명시한다.

- **객체 크기 한계 (예외로 드러남).** CPython은 주소 공간에 들어가는 만큼 만들지만, CLR 문자열은 `String.MaxLength`(0x3FFFFFDF UTF-16 단위, 약 10억 7천만 자), 배열은 `Array.MaxLength`에서 멈춘다. `gcAllowVeryLargeObjects`도 문자열 한계는 올리지 않는다. 그 사이 크기는 할당 단계에서 메시지 없는 `MemoryError`가 된다(`core/PyAllocation.cs` 한 곳). `Py_ssize_t`를 넘는 크기는 CPython과 같은 `OverflowError`다. 예: `"ab" * 2**31`, `"a".center(2**31)`, `bytes(2**31)`는 CPython에서 성공하고 SharpPy에서 `MemoryError`.
- **인접한 단독 서로게이트 (예외로 드러나지 않음).** CPython에서 `"\ud83d" + "\ude00"`은 코드 포인트 2개지만, .NET 문자열(UTF-16)에서는 한 쌍(U+1F600 한 글자)이 된다. `len`, 인덱싱, `strip`, `split` 등이 이 입력에서 CPython과 다른 값을 낸다. 문자열 저장 방식을 바꾸지 않는 한 감지할 수 없으므로 여기 명시한다.
- **참조 카운트에 의존하는 판정 (예외로 드러남, 문구만 다를 수 있음).** `BytesIO`의 `SHARED_BUF`는 CPython에서 버퍼의 참조 카운트가 1보다 큰지로 정해진다. .NET에는 참조 카운트가 없으므로 "공유 bytes를 내준 적이 있거나 버퍼가 비어 있음"으로 근사한다. 차이는 `PY_SSIZE_T_MAX` 근처에서 나는 `SystemError`의 문구에만 나타난다.
- **`__sizeof__`·`sys.getsizeof`의 값 (예외가 아니라 값의 차이, 여기 명시).** 두 함수는 CPython과 같은 경로다. `sys.getsizeof`는 타입의 `__sizeof__`에 GC 헤더와 managed dict 선행 헤더를 더하고, `__sizeof__`는 CPython의 공식(`TypeLayout.g.cs`의 basicsize/itemsize, int의 30비트 자릿수, str의 kind)을 쓴다. 값에서 CPython의 할당 이력에 달린 부분은 같게 만들 수 없어서, 새로 만든 객체 기준으로 계산한다. 해당하는 부분은 list·bytearray의 과할당 여유분, str에 캐시된 UTF-8 사본, 클래스의 공유 키 테이블 크기, dict·set의 내부 테이블이다. 예를 들어 `[1, 2, 3].__sizeof__()`는 CPython 72, SharpPy 64다.
- **`memoryview._from_flags` (없음, 호출하면 `AttributeError`).** CPython의 비공개 API로, 버퍼 요청 플래그(`PyBUF_*`)를 지정해 뷰를 만든다. SharpPy는 버퍼 플래그를 구분해 요청하지 않으므로, 플래그를 무시하고 조용히 다른 뷰를 주는 대신 두지 않았다.
- **OS 오류 코드 (예외로 드러남, 코드 값만 다를 수 있음).** nt 모듈의 OS 오류는 .NET 예외에서 만들어진다. `FileNotFoundException`은 실제 Win32 코드를 담지 않아, 드라이브가 없는 경로의 `stat`/`remove`가 CPython의 `winerror` 3(경로 없음) 대신 2(파일 없음)를 낸다. 예외 종류(`FileNotFoundError`)와 `errno`(2)는 같다.
