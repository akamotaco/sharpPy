# P0-37 - `gc` reaches Python, and `__del__` gets one honest call site

Date: 2026-10-30. Branch `work/bug-hunt`, base `fb625c4`.
Probe: `tests/bughunt/p76_gc_and_finalizers.py` (111 lines, diff 0 against CPython 3.12.15).

## Why this exists

`import gc` and `import weakref` raised `ModuleNotFoundError`. A missing module is not a
degraded behaviour to argue about, it is an error where CPython produces none, so the two are
treated as defects. This commit closes `gc` and the `__del__` dispatch `gc` interacts with;
`weakref` is the next item and is registered below (P0-37 remainder 8) rather than half-shipped.

The design question behind it - reference counting versus the CLR collector - was settled as:
**lifetime stays with the CLR collector; only the observable consequences are reproduced, and
where they cannot be, the difference is named.** What follows is what that produced, plus the
measurements that say where it stops working.

## CPython's mechanism, from the pinned source

- `Objects/typeobject.c:9499` maps `__del__` to `tp_finalize` through
  `TPSLOT(__del__, tp_finalize, slot_tp_finalize, ...)`, and `slot_tp_finalize`
  (`Objects/typeobject.c:9071`) looks the method up on the type. So an override wins over a base
  implementation and `super().__del__()` reaches the base - `__del__` is an ordinary special
  method, not a callback registry.
- `Objects/object.c:357 PyObject_CallFinalizer` runs it, with the comment
  `tp_finalize should only be called once` (:362), a `_PyGC_FINALIZED` flag check, and
  `_PyGC_SET_FINALIZED` after the call. `Objects/object.c:398` handles the case where the
  finalizer resurrected the object.
- The call is reached from `deallocate`, i.e. from the moment the reference count reaches zero.
  That is the whole reason `__del__` timing is observable in CPython.
- `Modules/gcmodule.c:1973 gc__doc__` / `:2054 .m_doc` make `gc` a built-in module: there is no
  `Lib/gc.py` to copy, so SharpPy implements it (library policy: CPython's C-only primitives get a
  C# module). The refusals come from two different mechanisms in the C file, which is why their
  wording differs:
  - clinic-generated `METH_NOARGS` / one-object entries name the module:
    `gc.freeze() takes no arguments (1 given)`, `gc.is_tracked() takes exactly one argument (0 given)`
  - `gc_set_threshold` is handwritten `METH_VARARGS`, and the optional-argument parsers use the
    bare function name: `set_threshold() takes at least 1 argument (0 given)`,
    `collect() takes at most 1 argument (2 given)`
  - `Modules/gcmodule.c:1552` `invalid generation` for `collect`, and `:1795`
    `generation parameter must be less than the number of available generations (3)` for
    `get_objects` - two different messages for two different bounds, both reproduced.
- `gcstate->callbacks = PyList_New(0)` (`Modules/gcmodule.c:169`) is a real list, and `collect()`
  calls each callback with `(phase, info)` where `info` is a dict with keys `collected`,
  `generation`, `uncollectable` - measured on both the `start` and the `stop` call.

## What SharpPy now has

`modules/GcModule.cs` (registered as `["gc"]`): the whole visible surface
(`enable`, `disable`, `isenabled`, `set_debug`, `get_debug`, `collect`, `get_count`,
`get_threshold`, `set_threshold`, `get_objects`, `is_tracked`, `is_finalized`, `get_referents`,
`get_referrers`, `get_stats`, `freeze`, `unfreeze`, `get_freeze_count`, `callbacks`, `garbage`,
five `DEBUG_*` flags) with CPython's module docstring.

Faithful, each pinned by p76:

| item | measured answer reproduced |
|---|---|
| state | `isenabled` True, `disable()`/`enable()`, `get_debug()` 0, `set_debug('x')` refusal |
| thresholds | `(700, 10, 10)`, `set_threshold(1,2,3)` accepted, 0 args and 4 args refused |
| `collect` | `collect()`/`collect(0..2)` return int; `'x'`/`None` -> `'str' object cannot be interpreted as an integer`; `3`/`-1` -> `ValueError: invalid generation` |
| `get_objects` | `'x'` -> `argument should be integer or None, not 'str'`; `5` -> generation-parameter `ValueError` |
| `is_tracked` | list/set/frozenset True; `()` and a tuple of atomics False; `([],)` True; `{}`/`{'a': 1}` False, `{'a': []}` True; `bytearray()`/`int`/`str`/`bytes`/float/None False; user class, instance, function, `len`, module True |
| `get_referents` | dict yields values; tuple and list yield items **in reverse** (`get_referents([3, 4])` is `[4, 3]`); an instance yields dict values then its class; atomics yield nothing |
| `callbacks` | identity-preserving list, actually invoked by `collect()` with the info dict |
| arity family | module-prefixed and unprefixed refusals, as the two C mechanisms produce them |

`core/PyFinalizer.cs` plus one line in `PyClass.CreateInstance`: instances of classes whose MRO
declares `__del__` are recorded at creation (the class caches the answer, mirroring CPython filling
`tp_finalize` once at type creation), and `RunAtShutdown()` invokes them in creation order. That
order is measured, not assumed: two module-level objects printed `del first`, then
`B del start`, `del second`, with `super().__del__()` reaching the base. A raising finalizer reports
CPython's `Exception ignored in: <function B.__del__ at 0x...>` plus `Traceback (most recent call
last):` and the error line on **stderr**, leaving the exit status alone.

Shutdown is the chosen call site because it is the one moment CPython and SharpPy agree: dropping
module globals drops the last reference to everything, so every finalizer must run before the
process ends.

## Cost, measured against a worktree build of `fb625c4`

| workload | new | old | ratio |
|---|---|---|---|
| 300,000 plain instances | 2498 ms | 2511 ms | 1.00x |
| 100,000 finalizable instances | 1002 ms | 976 ms | 1.03x |

Creating an instance of an ordinary class pays one cached field read. A class that declares
`__del__` pays a `WeakReference` registration, ~2.6%, and the registration list does not shrink
until shutdown - both registered (P0-37 remainder 5) instead of hidden.

## Gates

- build 0 errors, 0 warnings
- `tools/run_bughunt.py`: **81/81 matching** (p76 included, diff 0)
- `tools/evaluate.py`: `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`
- Godot 4.7.2 mono replay: **`REPLAY_RESULT 82 matched, 0 other`**

## Side findings, both pre-existing and registered separately

- `python -c` is inert in SharpPy: `Program.cs:82` parses `-c` and the call to
  `ExecuteCodeString` is commented out, so `-c "print(123)"` prints nothing and exits 0.
- The gate runner uses `sys.executable` as the reference interpreter, so it must be launched with
  the pinned 3.12 interpreter. Run with the base miniforge 3.14.7 on `PATH`, `tools/run_bughunt.py`
  reports 50/80 - a reference-version artifact, not a SharpPy regression. Worth a pinned
  interpreter argument in the runner.
