# B1: CPython의 native io abstract 베이스를 C#으로 (2026-10-29)

## 왜

`docs/library_surface_policy.md`의 규칙 3(C 전용 구현은 무시하고 C#으로 대체)을 `io`에 적용한 첫
단계다. CPython `Lib/io.py` 99줄을 읽으면 구조가 명확하다:

- `_io`가 C 타입 `_IOBase`/`_RawIOBase`/`_BufferedIOBase`/`_TextIOBase`와 구체 클래스들을 제공한다
- `io.py`는 그것들에 `abc.ABCMeta`를 **섞고**, 구체 클래스를 **가상 등록**한다
  (`RawIOBase.register(FileIO)`, `TextIOBase.register(StringIO, TextIOWrapper)`)
- 따라서 `isinstance(io.StringIO(), io.TextIOBase)`는 True인데
  `io.TextIOBase in type(x).__mro__`는 **False**다 (측정으로 확인)
- 기본 구현(`readlines`, `writelines`, `__iter__`/`__next__`, `flush`, `fileno`, `isatty`,
  `closed`, `__enter__`/`__exit__`)은 전부 C(`Modules/_io/iobase.c`)에 있다

SharpPy에는 이 C 레이어에 대응하는 것이 없어서 `Lib/io.py`가 Python으로 약 200줄을 대신 지고 있었다.

## 무엇을

`core/PyIOBase.cs` (신규):
- `PyIOBaseType` 4종(`_io._IOBase` 등)과 인스턴스 `PyIOBaseInstance`
- `iobase.c`의 기본 동작: `closed`, `readable`/`writable`/`seekable`(False), `fileno`
  (UnsupportedOperation), `isatty`(False), `flush`(None), `seek`/`tell`/`truncate`
  (UnsupportedOperation — `tell`은 CPython에서 **seek** 라벨), `readline`/`readlines(hint)`/
  `writelines`, `close`, `__iter__`/`__next__`, `__enter__`/`__exit__`, `_check*`
- `_RawIOBase`/`_BufferedIOBase`의 primitive는 **빈 메시지의 NotImplementedError**
- `_TextIOBase.read`/`write`는 **UnsupportedOperation**
- `modules/IoModule.cs`: 위 4타입 + Lib/io.py가 re-export하는 `DEFAULT_BUFFER_SIZE`(8192),
  `open`, `BlockingIOError`를 `_io`에 바인딩

## C-to-CLR 변환에서 기록할 차이

- 닫힘 상태: CPython은 C 구조체 필드(`iobase.closed`)에 둔다. Python 서브클래스 인스턴스는
  `PyClassInstance`라 그 필드가 없으므로 **다형적으로** 읽는다 — native는 CLR 필드, 그 외에는
  Python에서 보이는 `closed` 속성.
- 닫힘 메시지는 C 파일마다 다르다: `iobase.c`는 `"I/O operation on closed file."`(마침표),
  `stringio.c`는 마침표 없이. 둘 다 측정됐고 그대로 분리해 뒀다.
- `iobase_flush`는 `self.flush()`를 부르지 않는다(raw stream을 flush한다). 처음에 `CallVoid(self,
  "flush")`로 썼다가 **무한 재귀**로 크래시했다 — 측정으로 잡았다.
- VM은 네이티브 객체의 반복을 TypeDict descriptor가 아니라 `GetIterator()`/`Next()` virtual로
  도달한다. 그래서 `__iter__`/`__next__`를 **두 형태로** 제공했다.
- `iobase`는 `__dictoffset__`을 노출하므로 인스턴스 속성 쓰기가 허용된다.
  `PyObject.PySetAttribute`는 `PyClassInstance`의 dict에만 써서, 이 CLR 클래스는 쓰기를 직접
  라우팅한다.
- AttributeError 메시지는 `tp_name`(도트 포함)을 쓴다: `'_io._IOBase' object has no attribute 'read'`.
  `_IOBase`에는 애초에 `read`/`write`가 없어서 `read()`/`readline()`/`readlines()`/`next()`/
  `writelines("ab")`가 전부 그 AttributeError로 끝난다는 것도 CPython 동작이다.

## 검증

- `tests/bughunt/p53_io_native_bases.py` 41케이스, CPython 대비 stdout diff **0**
  - 제외 1건: `io.IOBase.__mro__`는 `['IOBase','ABC','object']`로 남음. 이건 `Lib/io.py` 자체를
    CPython 형태로 바꾸는 B4의 소유다 (측정 기록은 probe 상단 주석)
- `tools/run_bughunt.py` 57/57, `tools/evaluate.py` 225/3/2 불변
- Godot 4.7.2 mono replay **82 matched, 0 other**

## 다음

- B2: `PyStringIO` (`_io.StringIO`, 베이스 `_TextIOBase`) — 55케이스 명세 측정 완료
  (`.artifacts/tmp/sio_cp.txt`). 반복/`readlines`/newline 변환/`truncate`/closed 동작 포함
- B3: `PyBytesIO`
- B4: `Lib/io.py`를 CPython 복사본으로 (ABC 4개 + register + `UnsupportedOperation.__module__ = "io"`),
  `io.py:271-272`의 `__module__` 사칭 제거
