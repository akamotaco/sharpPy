# io B2: `_io.StringIO`를 C#으로 (2026-10-29)

B1(`_io`의 native abstract 베이스)에 이은 두 번째 단계. `Lib/io.py`가 대신 지고 있던
`StringIO` 93줄을 지우고 `core/PyStringIO.cs`(`Modules/_io/stringio.c` 대응)로 옮겼다.

## 성능 (이 작업을 택한 이유)

같은 워크로드, 같은 머신. "before"는 Python `StringIO`, "after"는 C# 타입.

| workload | before | after | CPython 3.12.15 |
|---|---|---|---|
| `StringIO.write` 20만 회 | 12,318 ms | **110.7 ms** | 7.0 ms |
| `StringIO.readline` 2만 회 | 112.6 ms | **49.5 ms** | 1.0 ms |
| `for line in StringIO(...)` | **TypeError: not iterable** | **144.9 ms** | 0.9 ms |
| baseline_int_loop | 96.3 ms | 94.7 ms | 6.9 ms |

before 기준 write는 baseline 대비 **128배** 추가 비용이 있었다(1760/14). after는 write가
110.7ms로 **baseline(94.7ms)과 같은 자리**로 내려왔다. 즉 Python 레이어 비용 125배가 그대로
사라졌고, 남은 차이는 인터프리터 자체의 바닥(14배)뿐이다. 반복은 아예 동작하지 않던 것이
동작하게 됐다.

## CPython과 맞추기 위해 필요한 것 (전부 측정)

- 파라미터는 **positional-only**. `StringIO(initial='x')`는
  `'initial' is an invalid keyword argument for StringIO()` — 키워드는 `newline`만 허용
- `newline` 검증: `''`, `'
'`, `''`, `'
'` 외에는 `illegal newline value: 'q'`
- 초기값 변환은 **생성 시 1회**, write는 **호출마다** 별도 변환:
  - `newline=None` → `'
'`/`''`을 `'
'`으로 collapse
  - `newline='
'` → 그대로
  - 그 외 → `'
'`을 그 문자열로 치환
  - 측정: `"a
bc"` → None: `'a
b
c'`, `'
'`: 원문, `''`: `'abc'`
  - write는 `newline=None`에서 **변환하지 않는다**(`write("a
b")` → `'a
b'`)
- `write`의 반환값은 **저장된 길이가 아니라 넘겨받은 문자 수**. `newline='
'`에
  `"a
b"`를 쓰면 버퍼는 `'a
b'`(4)인데 반환은 **3**
- `truncate()`는 현재 위치, `truncate(n)`은 n. 음수는 `Negative size value -1`(대문자 N)
- `seek`는 whence 1에서 오프셋이 있으면 `OSError: Can't do nonzero cur-relative seeks`,
  whence가 0/1/2 외면 `ValueError: Invalid whence (9, should be 0, 1 or 2)`
- 닫힌 뒤 `read`/`getvalue`/`write`는 `ValueError: I/O operation on closed file`
  — **마침표 없음**. B1에서 기록했듯 iobase.c는 마침표가 있으므로 두 문자열이 공존한다
- `readable/writable/seekable` → True, `isatty` → False(베이스 상속), `flush` → None(베이스),
  `fileno` → `UnsupportedOperation: fileno`(베이스), `newlines` → None, `line_buffering` → False
- `readlines`/`writelines`/`__iter__`/`__next__`/`__enter__`/`__exit__`는 **B1 베이스에서 상속**
  — 이것이 B1을 먼저 한 이유다

## 구조 변화

- `Lib/io.py` 303줄 → **215줄**. `from _io import (StringIO, ...)`로 재export, Python 클래스 삭제,
  `StringIO.__module__ = '_io'` 사칭 삭제(native가 됐으므로 자동으로 `_io`)
- `PyStringIO : PyIOBaseInstance` → MRO가 `StringIO → _TextIOBase → _IOBase → object`로 실제
  CPython과 같은 형태가 됐다

## 검증

- `tests/bughunt/p54_stringio_native.py` 53케이스 stdout diff **0**
  (주소 포함 두 줄은 `__name__`만 비교하도록 변경, `io.IOBase.__mro__` 한 줄은 B4 소유라 제외)
- `tools/run_bughunt.py` 58/58, `tools/evaluate.py` 225/3/2 불변
- Godot 4.7.2 mono replay **82 matched, 0 other** — in-memory 스트림이라 `#if GODOT` 분기 없이
  양쪽 빌드에서 동일 코드 경로

## 남은 것

- **B3**: `PyBytesIO` (`_io.BytesIO`) — 아직 `Lib/io.py`에 Python 구현이 남아 있다(104줄)
- **B4**: `Lib/io.py`를 CPython 복사본으로. ABC 4개를 `class IOBase(_io._IOBase, metaclass=abc.ABCMeta)`
  형태로 바꾸고 구체 클래스를 `register()` 하면 `io.IOBase.__mro__`가
  `['IOBase','_IOBase','object']`로 맞는다. `UnsupportedOperation.__module__ = "io"` 라인 포함
- B4 후에는 `io.py`가 99줄짜리 CPython 형태가 되고, BytesIO/StringIO 재구현 라인이 사라진다
