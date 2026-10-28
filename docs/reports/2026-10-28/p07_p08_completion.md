# p07 예외 / p08 클래스·디스크립터 완료 (2026-10-28)

첫 bughunt 전체 일치: **16/16 matching** (9 probes + 7 committed matrices).

## p07 exceptions (10 → 0)

- 결손 예외 타입 등록: `AssertionError.GetPyType`, `MemoryError`, `UnicodeError`,
  `UnicodeDecodeError`, `UnicodeEncodeError`, `FloatingPointError`, `TabError`
  (`core/PyException.cs`, `core/PyType.cs`, `modules/BuiltinsModule.cs`,
  `runtime/PyVM.cs GetExceptionTypeByName`).
- **암시적 체이닝**: `PUSH_EXC_INFO`에서 처리 중인 `prevExc`와 다른 새 예외가
  raise되면 `__context__` 설정 (CPython `Python/errors.c`). `implicit.chain` 일치.
- **하이브리드 매칭**: `class MyErr(RuntimeError, ValueError)`를 `except ValueError`로
  잡음. `CHECK_EXC_MATCH`가 래퍼의 `OriginalClass` MRO로 판정 (`runtime/PyVM.cs`).
- `BaseException.with_traceback` 구현 (`core/PyException.cs`):
  None/자기자신/인자 누락(`takes exactly one argument (0 given)`)/잘못된 타입
  (`__traceback__ must be a traceback or None`) 메시지까지 CPython과 동일.
- `type.__base__` read-only descriptor (`core/PyTypeMetaclass.cs PyBaseDescriptor`).

## p08 classes/descriptors (14 → 0)

- **truthiness**: 내장 `bool()`이 CLR `AsBool()` 대신 `IsTrue()` 프로토콜
  (`__bool__` → `__len__`) 사용. `bool.beats.len`, `len.zero.false` 일치.
- **property 메시지**: `__set_name__`를 `PyProperty`가 노출해 클래스 생성 시
  속성명을 바인딩하고, setter/deleter 누락 시 CPython 문자열
  `property 'x' of 'Props' object has no setter|deleter`
  (`Objects/descrobject.c property_set_name/property_descr_set`).
  삭제 경로(`PyClassInstance.CallDescriptorDelete`)도 `IDescriptor.Delete`를
  직접 호출해 정확한 AttributeError를 전파, 임의 예외 삼키지 않음.
- **__slots__**: MRO 전체에서 `__slots__` 누적을 판정해 인스턴스 `__dict__`
  노출과 슬롯 밖 쓰기 거부 (`'T' object has no attribute 'z'`).
  `hasattr(Slotted(), "__dict__") == False` 일치.
- **__init_subclass__ kwargs (PEP 487)**:
  - 클래스 정의 kwargs 전 계열 보완: 파서 브리지가 전체 키워드를 소스 순서로
    `ClassDefStatement.ClassKeywords`에 보존 → 컴파일러가 `KW_NAMES`로 함께 발급
    (`Parser/ast.cs`, `parser/PyParserRuntime_Bridge.cs`, `runtime/compile.cs`).
  - `__build_class__` 말미에 `type_new_init_subclass`等价 훅 추가
    (`core/PyBuiltin.cs CallInitSubclassHook`): 생성 클래스 자기자신 이후 MRO에서
    첫 훅을 찾아 cls+kwargs로 호출.
  - 기본(object/type) 훅이 kwargs를 받으면
    `<__qualname__>.__init_subclass__() takes no keyword arguments` 발생
    (`core/PyType.cs`). zero-arg `super()` 바인딩 시점 클래스명을 쓰는
    CPython `_PyObject_FunctionStr` semantics 재현.
  - 커스텀 메타클래스(예: `Lib/enum.py`의 `Flag(boundary=...)`)에서는 kwargs가
    메타클래스 체인 소속이므로 암시 훅 호출을 생략 (`defaultMetaclassEffective`
    게이트). 이 게이트 없이는 `import re`(enum)이 깨짐 → 회귀로 발견.

## 검증

- `tools/run_bughunt.py`: **16/16** (p07 10→0, p08 14→0).
- `tools/evaluate.py`: `225 matching_output + 3 volatile + 2 self_test_no_reference`
  (기준선 유지).
- perf probe (reps 4): 전 워크로드 ±5.4% 이내, 기준선 대비 유의 변화 없음.
  클래스 정의 경로 kwargs/KW_NAMES 발급은 정적 개수라 런루프 비용 무영향.
- `/tmp/isub.py`, property/slots 매트릭스 CPython 3.12.15 바이트 동일.

## 남은 p07/p08 잔여 (probe 밖)

- 커스텀 메타클래스 + 클래스 kwargs 동시 사용 시 kwargs의 `mcs.__new__` 전달
  (지금은 조용히 드롭; enum은 기본값으로 우회 작동).
- `super()`가 `PyBuiltinFunction` 중 METH_CLASS 계열을 버 binding하는 대상은
  `__init_subclass__`만 특례화. 다른 METH_CLASS C 함수는 아직 unbound.
