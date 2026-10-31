# What ordinary programs found

qwen wrote 40 game-script programs. 37 run on CPython; SharpPy failed 14 of them while every gate passed. Each defect
below was fixed at its route and pinned by a probe.

| 결함 | 경로 | 고친 곳 |
|---|---|---|
| `.5` 는 `SyntaxError` | tokenizer dispatch | `StartsNumber`, both dispatchers |
| 뒤에 정의한 형제 함수는 `NameError`, `json.dumps` 중첩 실패 | resolving each function as it ended | one pass after collection, as `symtable_analyze` |
| 클로저에 잡힌 매개변수를 재대입하면 컴프리헨션이 옛 값을 순회 | `LOAD_FAST` on a name that is also a cell | cell first |
| `class N(NamedTuple)`, `class C(Generic[T])` | no `__mro_entries__` | `UpdateBases`, as `update_bases` |
| `type.__new__(M, ...)` 가 메타클래스를 버림 | second class creation route | sets `Metaclass`; merge recorded as P0-80 |
| `C.__bases__ = ...` | setter missing | `PyType.SetBases` |
| 튜플 서브클래스의 `__repr__`, `__str__` 무시 | CLR virtual | declared method wins |
| `random` 시드가 CPython과 다른 수열 | `System.Random` | MT19937 port of `_randommodule.c` |
| `math` 16개, `itertools` 4개 없음 | module tables | added; `gamma`, `fsum`, `erf` ported from C after qwen's versions failed comparison |
| `_string`, `_thread` 없음 | C modules | added |

`_thread.start_new_thread` raises `RuntimeError: can't start new thread`. SharpPy's VM is single threaded, and a clear error
beats wrong behaviour. Locks work, so modules that only guard with them can run.

`math.erf` and `erfc` can differ from CPython in the last digit, because CPython calls the platform libm.

Gates: bughunt 126/126, evaluate 225/3/2, similarity 0, Godot 82/0. Programs: 29 of 37 match. Still failing: `asyncio.gather`,
`Counter` repr, `dataclass(order=True)`, `add_note`, `itertools.product(repeat=)`, `groupby`, `lru_cache.cache_info`.

## Later in the same night

`itertools.groupby` was an empty stub and now works; keyword arguments reach `product`, `zip_longest`, `accumulate`, `count`.
Two defs of one name under if and else each get their own symbol table (`TakeChild`). `hash(...)` no longer overflows the stack.
`dict`, `set`, `bytes`, `range` expose slot methods through `PySlotWrappers`; `__repr__` and `__str__` stay out because
`AsString` looks them up and would recurse. Regex `(?a:` flags are stripped. `sys.excepthook` exists.

The parked grafts are in now. The failure blamed on them was a heredoc that turned `'\\\\'` into `'\\'` in `Lib/ntpath.py`.

## Text routes and libraries

| 결함 | 고친 곳 |
|---|---|
| `f"{{x}}"` printed `{{x}}` | `AddToken` gives `{{` and `}}` back as one brace |
| A multi-line string in a CRLF file held `\\r` | newlines normalized on entry, as `translate_newlines` |
| `re.VERBOSE` ignored, `.flags` was a .NET value | `StripVerbose`, Python flags kept on the pattern |

Added from CPython: `string`, `reprlib`, `pprint`, `queue`, `threading`, `logging`, `fnmatch`, `posixpath`, `genericpath`, `textwrap`.
Grafted into the existing shims: `Counter`, `ChainMap`, `UserDict`, `UserList`, `UserString`, `lru_cache`, `cache`.
Programs matching: 33 of 37.

## One exception object (P0-70, E1)

C# has no multiple inheritance, so `PyClassInstance` and `PyBaseException` stay two classes. What changed is what Python can see.
Each `PyExceptionInstance` has one cached `PyException` transport that a raise carries, and every way out to Python gives the
instance back (`PyExceptionSlots.Visible`). `e is z`, `e.mark = 1`, `add_note`, `__traceback__` before a raise, `sys.exc_info`,
`__exit__`, `gen.throw`, `raise X from E()` and `raise X from TypeError` all match. Builtin exceptions have a `__dict__`.
`__traceback__`, `__cause__`, `__context__`, `__suppress_context__`, `with_traceback`, `add_note`, `__reduce__` and
`__setstate__` have one table. Still open: subclass fields of `OSError`, `StopIteration`, `ImportError`, `SyntaxError`.
Gates: bughunt 132/132, evaluate 225/3/2, similarity 0, Godot 82/0. Programs matching: 36 of 37.
