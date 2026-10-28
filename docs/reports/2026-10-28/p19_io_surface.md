# p19 — io module surface + display-protocol parity (2026-10-28)

## 스코프
`tests/bughunt/p19_io_surface.py`: `dir(io)`, `io.__all__`, base ABC vs concrete `_io`
클래스 repr/`__module__`, `io.open is builtins.open`, `text_encoding`,
`UnsupportedOperation`(raise/MRO/isinstance), `open`의 구체 타입 이름.
기준 CPython 3.12.15.

## 발견 (측정)
- `io`에 `TextIOWrapper/FileIO/BufferedReader/BufferedWriter/BufferedRWPair/BufferedRandom/
  IncrementalNewlineDecoder/UnsupportedOperation/open/open_code/text_encoding/BlockingIOError`
  전부 없음 → `import tokenize` 등 `from io import TextIOWrapper` 경로 차단.
- 클래스 repr이 `__module__` 접두 누락 → p19a(`c9983c6`)에서 선행 수정.
- native 타입 `type(x).__module__`가 항상 `builtins` (Module 필드 무시).
- `dir()`이 문화권 정렬(CP는 ordinal) → `BufferedRWPair`/`BufferedRandom` 순서 역전.

## 수정
1. **native `_io` 모듈** (`modules/IoModule.cs`, 신규) — CPython Modules/_io 대응.
   - `PyFileContextManager`의 구체 파일 타입(TextIOWrapper/BufferedReader/BufferedWriter/
     BufferedRandom)을 **동일 객체로** 바인딩 → `isinstance(open(f), io.TextIOWrapper)` 성립.
   - FileIO/BufferedRWPair/IncrementalNewlineDecoder 타입, `UnsupportedOperation`
     (p16 raise 타입과 동일 개체), `text_encoding`, `open_code`.
   - `text_encoding` 정확 메시지: `text_encoding expected at least 1 argument, got 0`.
   - `_builtinModules["_io"]` 등록.
2. **`Lib/io.py` 재작성** — CPython 구조: base ABC는 io에 두고, concrete은 `from _io import`,
   `from builtins import BlockingIOError, open`(→ `io.open is open` True),
   `StringIO/BytesIO.__module__='_io'`, 정확한 `__all__`.
3. **파일 타입/__module__** — `PyFileContextManager` static ctor에서 4개 타입 `__module__='_io'`
   (`_io` 미import 시에도 open repr 정확). `PyUnsupportedOperation` static ctor `__module__='io'`.
4. **`type.__module__` 게터** (`core/PyTypeMetaclass.cs` `PyModuleDescriptor`) — native 타입은
   `Module` 필드 우선. (이전엔 PyType.cs의 죽은 descriptor만 고쳐져 효과 없었음 — 활성 경로를 측정으로 특정.)
5. **`dir()` ordinal 정렬** (`core/PyBuiltin.cs`) — `StringComparer.Ordinal` / `string.CompareOrdinal`.

## 검증
- p19 stdout diff **0**. bughunt **26/26**, evaluate `225 + 3 + 2` 불변.
- 스팟: `sorted` mixed, class repr, `ca.Iterator.__module__`, `repr(float)`, `str.__module__` CP 일치.

## 남은 한계 (측정 기록, p19 범위 외 — 별도 클러스터 후보)
- **`dir()` 내용**: 임의 클래스/`math`/`list`의 속성 **집합**이 CPython과 다름
  (`__bases__`/`__class_getitem__` 과잉, `__dir__`/`__eq__` 누락 등). 정렬은 일치, 집합이 다름.
- **`type.__module__`** (메타클래스 자기 자신)가 descriptor 객체를 반환 (CPython `builtins`).
- **ABC virtual isinstance/issubclass**: `isinstance(x, io.IOBase)` 등 ABC `__instancecheck__`
  프로토콜을 SharpPy `isinstance`가 native fast-path로 우회. `open()`의 io.IOBase/TextIOBase
  isinstance는 이 때문에 프로브에서 제외(구체 타입 identity 검사는 통과).
- `io.TextIOWrapper(buffer, encoding=...)` 직접 생성 미지원(native 타입 ctor 없음).
  `import tokenize`는 `token`/`_tokenize` 모듈도 필요(미존재).
