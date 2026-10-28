# p15 모듈·import·sys/os·stdlib 표면 (2026-10-28)

`tests/bughunt/p15_modules_stdlib.py` 추가 → bughunt **22/22 matching**.

## 수정

1. **sys** (`modules/SysModule.cs`):
   - `version_info` → `PyVersionInfo`(tuple 서브클래스, major/micro… 속성,
     micro=15, `type().__name__=='version_info'`)
   - `sys.flags` → `PySysFlags`(type name `flags`, 일반 플래트 필드)
   - `getrecursionlimit`/`setrecursionlimit`(기본 1000)/`intern`
   - `implementation`: 관측 파라리티로 `name='cpython'`, hexversion `0x030c0f00`,
     cache_tag `cpython-312` (판단: AGENTS의 CPython 관측 동일성 우선)
   - `PyFile`(stdout/stderr/stdin): `write/writelines/flush/close/isatty/
     writable/readable/seekable/fileno` + `name='<stdout>'`, mode, encoding,
     errors, closed, line_buffering. repr 도 cp 형식(`name='<stdout>'`)
2. **traceback 타입 동일성** (`core/PyException.cs`): `PyTraceback.GetPyType` →
   전용 `traceback` PyType (`exc_info()[2]`의 `type().__name__`).
3. **`import a.b as X` 바인딩** (`runtime/compile.cs`): cp대로
   `IMPORT_NAME a.b / IMPORT_FROM b / STORE X / POP_TOP`.
   - 도입 실수: `as` 없는 `import a.b`까지 IMPORT_FROM 경로에 빠져
     `collections`가 abc 스텁으로 묶임 → `hadAs` 게이트로 교정
     (회귀 감지: `matrix_nameerror_suggestions` ImportError).
4. **ModuleNotFoundError.name**: raise 지점에 `name` 전달,
   `ImportError/ModuleNotFoundError` TypeDict에 `name` getset(`dir(e)` 노출).
5. **os.environ `_Environ`** (`Lib/os.py`): dict 래퍼 클래스(get/keys/values/
   items/pop/setdefault/copy, setitem→nt.putenv), 타입명 `_Environ`.
   nt에 `unsetenv`/`getpid` 추가, os re-export.
6. **ntpath** (`Lib/ntpath.py`): `normpath`가 `.`/`..` 축약·루트 보존
   (`a\b\..\c`→`a\c`), UNC/drive handling, `splitdrive` 신규.
7. **re.sub/subn callable repl** (`modules/_SreModule.cs`): MatchEvaluator로
   Python 호출 위임. 이건 **json 인코더 탈출 문자열 버그의 실제 원인**
   (`Lib/json/encoder.py`가 `ESCAPE.sub(replace, s)` 사용 → 함수 repr이
   문자열로 삽입됨). `ensure_ascii` 경로도 동일 해결.
8. **object.__class__ getset** (`core/PyType.cs`): 내장 인스턴스(set 등)에서
   `o.__class__` 조회 가능(json 기본 encoder 경로), 읽기 전용.
9. **float.__repr__ 언바운드** (`type/PyFloat.cs`): `float.__repr__(1.5)` (json
   `floatstr`가 사용), slot wrapper语义.
10. **예외 args 단일 슬롯 의미** (`core/PyType.cs`, `core/PyException.cs`):
    - `BaseException.__init__`이 네이티브 래퍼는 Args 재바인딩, 하이브리드
      사용자 인스턴스는 dict `args` 갱신, 순수 사용자 인스턴스는 dict 저장.
    - `args` getset getter/setter도 래퍼 대응.
    - 하이브리드 `GetAttribute('args')`: 인스턴스 dict 값이 우선
      (`ValueError.__init__(self, msg)` 후 `e.args == ('X1-2',)`).
    - `PyType.GetAttribute` MRO 스캔(예외 파생 타입만): `ValueError.__init__`가
      메타클래스 object.__init__이 아니라 BaseException slot wrapper를 찾음.
    - JSONDecodeError 메시지 재현:
      `Expecting property name enclosed in double quotes: line 1 column 2 (char 1)`
11. **importlib 네이티브 미니 모듈** (`modules/ImportlibModule.cs`):
    `import_module/reload/invalidate_caches`. reload는 동일 모듈 반환
    (재실행 미구현, 문서화). `_builtinModules` 등록.

##KNOWN-GAP (남은 것, probe 범위 밖)

- `reload()` 실제 재실행 아님.
- `ValueError.__init__`의 자기-클래스 타입 검사 완화(cp는 ValueError가
  아닌 인스턴스에 TypeError). 예외 MRO 스캔의 허용 범위.
- `Lib/io.py`에 `TextIOWrapper` 없음(기존 결함, tokenize 경로).
- json set 직렬화 메세지는 이제 일치하지만 커스텀 encoder/tuple key 등 미조사.

## 검증

- p15 stdout diff 0 (sys 20케이스, os/path 17케이스, import/json 20케이스).
- `tools/run_bughunt.py` 22/22, `tools/evaluate.py` 225 + 3 + 2.
- perf probe: str_fmt/list_comp 최초 +16% 플래그 → 재측정 -0.1~+7.6%(노이즈).
