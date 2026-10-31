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

Parked, not committed: `.artifacts/wip-l6/` holds grafts of `Counter`, `UserDict`, `lru_cache`, plus copies of `string`,
`reprlib`, `pprint`, `queue`, `threading`, `fnmatch`. With them bughunt fell to 113/126: `import threading` raises a SyntaxError
with no location, which still has to be traced.
