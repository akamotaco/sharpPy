# The exit code arrives (P0-53, L2.2 두 번째 커밋)

## Before, measured

`.artifacts/tmp/l22_exit_rc.py` runs each shape as its own process on both interpreters and
compares rc, stdout, and stderr. Before the fix SharpPy answered rc `1` to all of them:

| program | CPython rc | SharpPy rc (before) |
|---|---:|---:|
| `sys.exit()` | 0 | 1 |
| `sys.exit(None)` | 0 | 1 |
| `sys.exit(0)` | 0 | 1 |
| `sys.exit(3)` | 3 | 1 |
| `sys.exit(-1)` | 4294967295 | 1 |
| `sys.exit(True)` | 1, stderr empty | 1, stderr empty (by accident) |
| `sys.exit('bye')` | 1, stderr `bye` | 1, stderr empty |
| `sys.exit(1.5)` | 1, stderr `1.5` | 1, stderr empty |
| `sys.exit([1, 2])` | 1, stderr `[1, 2]` | 1, stderr empty |
| `sys.exit(2 ** 40)` | 4294967295 + `OverflowError` line | 1, nothing |

The shape was wrong too:

```python
repr(SystemExit(3))     # CPython 'SystemExit(3)'      SharpPy 'SystemExit(3)'
SystemExit(3).args      # CPython (3,)                 SharpPy ()            # argument dropped
SystemExit(3).code      # NameError: 'SystemExit' object has no attribute 'code'
SystemExit.__module__   # None                         'builtins'
'SystemExit' in dir(builtins)  # True                  False  # and lookup failed too
sys.exit(1, 2)          # TypeError: exit expected at most 1 argument, got 2  # accepted
sys.exit(code=3)        # TypeError: sys.exit() takes no keyword arguments    # accepted
```

`SystemExit` and `KeyboardInterrupt` were not in builtins at all, which is why the exception-group
note in `docs/known_gaps.md` said the `BaseException`-only membership rule could not be written.

## What CPython does

`sys.exit` is `sys_exit_impl` (`Python/sysmodule.c:947`) and does one thing:
`PyErr_SetObject(PyExc_SystemExit, status)` (`:951`). That normalizes through
`_PyErr_CreateException` (`Python/errors.c:39`):

- missing or `None` → constructed with **no arguments** (`:43`)
- a tuple → the tuple **is** the argument list (`:46`)
- anything else → one argument (`:50`)

`SystemExit_init` (`Objects/exceptions.c:633`) then sets `code`: nothing → `None` (`:640`),
one argument → that object (`:643`), more than one → the argument tuple itself (`:646`).
The status is chosen by `_Py_HandleSystemExit` (`Python/pythonrun.c:708`), reached from
`_PyErr_PrintEx` (`:786`): `None` → 0 (`:736`), `PyLong_Check` → `PyLong_AsLong` (`:743`,
leaving `OverflowError` set on overflow), otherwise the object is written to `sys.stderr` followed
by a newline (`:746`-`:762`) and the status is 1. No traceback is printed for `SystemExit`,
which is why `runtime/PyInterpreter.cs:236` re-throws it silently.

## The change

- `core/PyException.cs` — `PySystemExit` keeps `Args`, exposes `Code` with the three-branch rule,
  and `HandleStatus(out toWrite, out tooLarge)` is `_Py_HandleSystemExit` in one place.
  `PyLong_Check` covers `bool` and int subclasses, which are CLR siblings of `PyInt`, so the family
  is spelled out. The narrow step uses `int`, not `long`: **C long is 32 bits on Windows**, which is
  why `sys.exit(2 ** 40)` overflows (`rc 4294967295` + `OverflowError: Python int too large to
  convert to C long`) while a CLR `long` would have accepted the value happily.
- `core/PyType.cs` — the `SystemExit` constructor stores the arguments instead of `new PyInt(code)`.
- `modules/SysModule.cs` — `sys.exit` is positional-only, refuses a second argument with
  `exit expected at most 1 argument, got N`, and reproduces the `PyErr_SetObject` normalization,
  including the tuple splat.
- `core/PyBuiltin.cs` — `_sitebuiltins.Quitter.__call__` (`Lib/_sitebuiltins.py:38`) passes its code
  through, so `exit().args == (None,)` and `exit(2).args == (2,)`.
- `modules/BuiltinsModule.cs` — `SystemExit` and `KeyboardInterrupt` join builtins.
- `core/FileExecutor.cs` — `ExitStatusOf(Exception)` computes the status where the exception reaches
  the top, before `PyFinalizer.Shutdown()`. Both `Environment.Exit` sites use it.

## The L2 question, answered

`.artifacts/tmp/l22_exit_atexit.py` pins what L2 deferred. rc matches in all five cases:

| program | rc (both) | stderr |
|---|---:|---|
| `atexit.register(print, 'bye'); sys.exit(3)` | 3 | — |
| `atexit.register(sys.exit, 5)` alone | 0 | `Exception ignored in atexit callback: ... SystemExit: 5` |
| `atexit.register(sys.exit, 5); sys.exit(3)` | **3** | same ignored line |
| `atexit.register(sys.exit, 5); raise ValueError('boom')` | 1 | traceback |
| callback that prints then `sys.exit(9)` | 0 | ignored line |

So the status really is decided before teardown: the `sys.exit(5)` inside a callback cannot change
a `sys.exit(3)` status, and it is reported rather than run. One case still differs in **stream
placement only** — SharpPy prints the uncaught-exception traceback on stdout — and that is the
older gap registered in `docs/known_gaps.md` (item 2 of the general list), not something P0-53
introduced or hid.

## Residual

- `EncodingWarning` is still missing from builtins (`docs/known_gaps.md` item 4, reworded: `dir` and
  attribute lookup now agree, both missing only that one name).
- `os._exit` still does not exist (**P0-58**), so "skip callbacks" cannot be tested here.
- `raise SystemExit((3, 4))` keeps `args == ((3, 4),)` while `sys.exit((1, 2))` splats — both match
  CPython because the `raise` expression builds the instance first.
- The traceback stream for uncaught exceptions stays on stdout (older gap).

## Gates

| gate | value |
|---|---|
| build | 0 errors |
| `tests/bughunt/p91_sys_exit.py` | matching, and its final `sys.exit(4)` gives rc 4 on both |
| `tools/run_bughunt.py` | **97/97 matching** |
| `tools/evaluate.py --timeout 90` | `225 / 3 / 2` |
| `tools/cpython_similarity.py` | exit 0 |
| Godot replay | `82 matched, 0 other`, `REPLAY_THREADS 2 main=2` |

Performance is not measured as a gate here: the touched code runs once per process (constructor,
`ExitStatusOf`) and once per `sys.exit` call, and no loop in `bench_suite.py` calls `sys.exit`.
