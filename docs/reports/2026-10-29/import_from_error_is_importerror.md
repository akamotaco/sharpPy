# `from 모듈이름 없는이름`은 AttributeError가 아니라 ImportError다 (2026-10-29)

B4(`Lib/io.py`를 CPython 형태로)를 하려다 막힌 지점이 이 일반 결함이었다. CPython `Lib/io.py`
끝부분:

```python
try:
    from _io import _WindowsConsoleIO
except ImportError:
    pass
```

SharpPy는 없는 이름을 `AttributeError: module '_io' has no attribute '_WindowsConsoleIO'`로
바깥으로 흘려서 `except ImportError`가 잡지 못했다. io뿐 아니라 모든 `from X import Y`에
해당하는 결함이다.

## 측정된 CPython 동작 (8개 모듈)

```
_io    -> cannot import name 'NOPE' from '_io' (unknown location)
math   -> cannot import name 'NOPE' from 'math' (unknown location)
time   -> cannot import name 'NOPE' from 'time' (unknown location)
sys    -> cannot import name 'NOPE' from 'sys' (unknown location)
json   -> cannot import name 'NOPE' from 'json' (LIBDIR\Lib\json\__init__.py)
os     -> cannot import name 'NOPE' from 'os' (LIBDIR\Lib\os.py)
abc    -> cannot import name 'NOPE' from 'abc' (LIBDIR\Lib\abc.py)
```

- 타입은 `ImportError`, 메시지는 `cannot import name 'X' from 'M' (origin)`
- origin은 `__spec__.origin`. source 모듈이면 파일 경로, builtin/extension에서는 없어서
  `unknown location`
- `'M'`은 모듈의 `__name__`이라 `_collections_abc`가 `collections.abc`로 나온다 (SharpPy는 아직
  `_collections_abc` — 별도 갭으로 기록)

## 고치면서 나온 함정

`__spec__`이 없는 SharpPy 모듈에서 `__file__`로 폴백했더니 native 모듈의 플레이스홀더 `__file__`
값이 그대로 노출됐다: `_io.py`, `time.py`, `<builtin sys module>`, 게다가 빌드 머신 경로
`C:\Users\m11\Desktop\work\sharpPy\...` 가 네이티브 상수에 박혀 있었다. CPython은
extension/builtin에 origin이 없으므로 **실제로 디스크에 존재하는 파일만** origin으로 인정하도록
gating 했더니 8개 케이스가 전부 CPython과 일치했다.

남은 잔여(ledger에 기록): native 모듈 `__file__`에 빌드 머신 절대 경로가 박혀 있다는 것 자체,
`_collections_abc.__name__` 별칭.

## 검증

- `tests/bughunt/p56_import_from_error.py` — CPython/SharpPy stdout diff **0**
  (경로는 `(PATH)`로 정규화, `_WindowsConsoleIO`처럼 플랫폼 의존적인 이름은 쓰지 않고 캐치
  가능성만 검증)
- `tools/run_bughunt.py` 61/61, `tools/evaluate.py` 225/3/2 불변
