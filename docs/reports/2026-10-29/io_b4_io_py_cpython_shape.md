# io B4: `Lib/io.py`를 CPython 복사본 형태로 (2026-10-29)

io 작업의 마지막 단계. B1(abstract 베이스)·B2(`StringIO`)·B3(`BytesIO`)로 native 층이 갖춰지자
`Lib/io.py`를 **CPython 3.12.15 원문과 같은 형태**로 바꿀 수 있었다.

| | B 이전 | B 이후 |
|---|---|---|
| `Lib/io.py` 줄 수 | 303 | **99** (CPython과 같음) |
| `io.IOBase.__mro__` | `['IOBase','ABC','object']` | `['IOBase','_IOBase','object']` |
| `io.RawIOBase.__mro__` | Python 전용 | `['RawIOBase','_RawIOBase','IOBase','_IOBase','object']` |
| `io.TextIOBase.__mro__` | Python 전용 | `['TextIOBase','_TextIOBase','IOBase','_IOBase','object']` |
| `io.BufferedIOBase.__mro__` | Python 전용 | `['BufferedIOBase','_BufferedIOBase','IOBase','_IOBase','object']` |
| `io.open is builtins.open` | False | **True** |
| `UnsupportedOperation.__module__` | 우회 사칭 | CPython과 같은 경로로 `io` |
| `isinstance(StringIO(), TextIOBase)` / `(BytesIO(), BufferedIOBase)` | 우회 | `register()`로 CPython과 같음 |
| `isinstance(BytesIO(), RawIOBase)` / `(StringIO(), BufferedIOBase)` | — | 둘 다 False로 일치 |

구조는 CPython 그대로다: `from _io import (...)` 재export +
`class IOBase(_io._IOBase, metaclass=abc.ABCMeta)` 4개 + `register()` 루프.
`_WindowsConsoleIO` try/except도 그대로 뒀고, 그것이 돌아가려면 `from X 없는이름` 결함부터
고쳐져야 했다(같은 날짜의 별도 보고서).

## 이 과정에서 필요했던 코어 변경

1. **`UnsupportedOperation.__module__` 대입 허용** — `core/PyType.AllowsModuleAssignment`
   측정 결과 CPython은 `ValueError`/`OSError`/`_io._IOBase`/`_io.StringIO`에서는
   `cannot set '__module__' attribute of immutable type` 으로 거부하면서 `UnsupportedOperation`만
   허용한다. `_io`가 이 예외를 heap 타입으로 만들기 때문이다. p28의 불변 가드는 옳았고, 예외는
   `name == "__module__"` 인 그 한 타입으로만 좁혔다. 저장은 `type_set_module`처럼 타입 dict에 쓴다.
2. **`open` 객체 단일화** — `modules/IoModule.OpenFunction`
   `builtins.open`과 `_io.open`이 각각 `new PyBuiltinFunction("open")`이었다. CPython은
   `_io.open`을 `builtins.open`으로 쓰므로 같은 객체이고 `io.open is builtins.open`이 True.
   이 회귀는 B4 첫 측정에서 p19가 잡아 줬다 (60개 중 유일하게 깨진 항목).
3. **`from X 없는이름` → ImportError** — 앞서 따로 기록한 일반 결함.

## B1 probe에서 열었던 제약도 제거

`p53`은 "native 베이스를 상속받아 인스턴스를 만들기"를 `if False else "skip"`으로 막아 뒀다.
public ABC가 Python 전용이던 시절엔 그 경로가 깨져 있었기 때문이다. 이제 동작해서 실측 비교로
바꿨고 diff 0 — `type("T", (_io._TextIOBase,), {})()` 의 MRO가
`['T','_TextIOBase','_IOBase','object']`. `p54`에서 제외했던 `io.IOBase.__mro__`도 복원했다.

## 남은 잔여 (ledger로 이동)

- `io.IOBase.__doc__` 등이 `None`. CPython은 `__doc__ = _io._IOBase.__doc__`로 docstring을 걸지만
  SharpPy native 타입에는 `__doc__`가 없어서 그 라인을 현재 생략한다. 표시 전용이라 후순위.
- `_io._WindowsConsoleIO` 미구현(Windows CPython에는 존재). `io.py`의 try/except가 의도대로
  ImportError로 처리하므로 `import io`는 깨지지 않는다.
- `_collections_abc.__name__`이 `collections.abc`가 아니다.

## 검증

- `p53`(제약 제거 후) / `p54`(`iobase_mro` 복원) / `p55` / `p56` 모두 CPython 대비 stdout diff **0**
- `tools/run_bughunt.py` **61/61**, `tools/evaluate.py` 225/3/2 불변
- Godot 4.7.2 mono replay **82 matched, 0 other** (dotnet 10.0.12 호스트, net8.0 TFM)

## io B 시리즈 결과 (정책 관점)

`Lib/io.py`는 303줄 Python 대체 구현에서 99줄 CPython 재export가 됐다. 대체 구현이 생긴 이유는
CPython의 C 층(`Modules/_io` 8,987줄)이 없었기 때문이고, 그 층의 필요한 부분 — abstract 베이스,
in-memory 스트림 2종 — 을 C#으로 옮긴 뒤에는 Python 측 코드량이 CPython과 같은 자리에 왔다.
측정된 성능 근거(B2 보고서): `StringIO.write` 20만 회 12,318ms → 110.7ms, 반복은 아예
동작하지 않던 것이 동작. 즉 이 라인은 "의미 없는 증폭"이 아니라 빠진 C 층의 대리물이었고, 이제
그 대리물이 제거됐다.
