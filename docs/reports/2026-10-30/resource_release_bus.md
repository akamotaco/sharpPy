# P0-38: the release bus — a file Python cannot reach must not stay locked

Date 2026-10-30, branch `work/bug-hunt`, base `132c65d`.
Policy: `docs/lifetime_and_gc_policy.md` room C. Probe: `tests/bughunt/p77_resource_release.py`.

## The failure, and the probe bug that nearly hid it

The measured divergence that opened this item was 4000 abandoned `open(p, 'a')` calls followed by
`os.remove(p)`: CPython answers `remove ok`, SharpPy answered `remove failed: PermissionError`.

The first probe written for this session (`e1_collect_releases.py`) reported that CPython also
failed after `handles = None; gc.collect()`. That was wrong, and the cause was the probe: the loop
was `g = open(p, 'a')`, so the module-level name `g` still held the last file object, which on
Windows is enough to refuse the delete. A second probe (`e2_who_holds.py`) took a weakref of the
first element, confirmed the objects really are freed when the list is dropped, and CPython answered
`remove ok`. Recording this because the wrong probe would have justified "CPython differs too, so
this is a lifetime room B problem" — which would have been false, and would have filed a
resource-state failure under the one room where we accept divergence.

Ground truth, CPython 3.12.15, verified twice:

| shape | CPython |
|---|---|
| file object rebound each iteration, loop ends, `os.remove` | `remove ok` |
| 200 files in a list, list dropped, `gc.collect()`, `os.remove` | `remove ok` (objects freed: weakref returns `None`) |
| last file still bound to a name, `os.remove` | `PermissionError` |
| explicit `close()` of every file, `os.remove` | `remove ok` |

The last row and the third row are what define the requirement: release exactly what Python cannot
reach, and never release what it can.

## CPython's mechanism, from the pinned source

```
Include/object.h:690        Py_DECREF reaching zero -> _Py_Dealloc(op)
Objects/object.c:2623       _Py_Dealloc -> tp_dealloc
Modules/_io/fileio.c:530    fileio_dealloc   (bufferedio.c:408, iobase.c:382 likewise)
Modules/_io/iobase.c:349    _PyIOBase_finalize: refcnt == 0 means zombie, so
Objects/object.c:374        PyObject_CallFinalizerFromDealloc - "Temporarily resurrect the object"
Objects/object.c:357        PyObject_CallFinalizer - "tp_finalize should only be called once"
Modules/_io/iobase.c:298    iobase_finalize (installed as Py_tp_finalize at :896):
                              reads self.closed, sets self._finalizing = True,
                              calls the PYTHON-LEVEL close(), silences errors unless dev mode
Modules/_io/fileio.c:90     fileio_dealloc_warn -> ResourceWarning for a file collected still open
Python/pylifecycle.c:1673   flush_std_files(), reached from the shutdown paths (:1896, :1925, :2952)
```

Two things follow. First, the closing time is a pure function of the reference count, which is the
one thing SharpPy does not have. Second, CPython closes by calling the object's own `close()` method,
so a subclass override participates - something SharpPy cannot do at this point, because by the time
it can tell the owner is gone, the owner has been collected and there is nothing left to dispatch on.

One mitigating detail specific to this host: SharpPy opens with `FileShare.ReadWrite`, so a lingering
handle does not stop another `open()`. What it does stop is delete and rename - which is exactly the
observable failure - plus, at scale, handle consumption.

## What was built

`core/PyResourceBus.cs` — a registry of `(WeakReference<PyObject> owner, Action release)`.

The one constraint that decides the shape: **the release action must capture the streams, never the
file object.** An `Action` closure over instance members captures `this`, the registry would hold a
strong reference to every open file, no `WeakReference` would ever report dead, and nothing would
ever be released. Hence `ReleaseOwned(Stream, StreamWriter)` is a static method and the registration
passes a lambda over locals.

Release points, in order of how deterministic they are:

1. `close()` / `with` exit — identical to CPython, unchanged code path, the registry entry becomes a
   tombstone (`MarkReleased`).
2. Registration-driven sweep — every `open()` may sweep, on a geometric threshold
   (`sweepAt = max(16, count * 2)`) so 20 000 opens do not become quadratic. A collection is forced
   only when the list is already at `ForceFloor = 64` **and** `GC.CollectionCount(0)` has not moved
   since the last look, because a `WeakReference` can only look dead after the CLR has collected.
3. `gc.collect()` (`modules/GcModule.cs`) — the reconciliation anchor; it now calls
   `PyResourceBus.ReleaseUnreferenced(forceCollection: true)`. A program that never opens a file pays
   nothing, since the force only happens when something is still open and the cheap pass freed nothing.
4. Interpreter shutdown (`PyFinalizer.RunAtShutdown` → `ReleaseAllAtShutdown`), after the `__del__`
   hooks, because a finalizer may still write to a file it kept open. CPython's own shutdown flushes
   the standard streams last for the same reason.

And the piece that makes the measured outcome identical: `os.remove`, `os.unlink`, `os.rmdir` and
`os.rename` retry **once** after a release pass (`RetryAfterRelease` in `modules/NtModule.cs`). The
filter is `attempt == 0 and (IOException or UnauthorizedAccessException) and released > 0`, so the
success path pays nothing and only an already-failing call pays one collection. CPython never needs
the retry because it never reaches that state.

`FileShare.ReadWrite` is untouched; no semantics were loosened to make the test pass.

## Gates

- build: 0 errors, 0 warnings
- `tools/run_bughunt.py`: **82/82 matching** (p77 added, 116 lines, diff 0)
- `tools/evaluate.py`: `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`
- Godot 4.7.2 mono source build + recorded replay: **82 matched, 0 other**
- `.artifacts/tmp/lock.py`: `remove ok` under both engines

p77 pins, all identical to CPython: rebinding leak loop removable; dropped list plus `collect()`
removable; an abandoned `open(p,'w').write('hello')` reads back `'hello'`; `.closed is True` after
`with`; **a file still referenced by Python survives a `gc.collect()` and keeps accepting writes**;
`rename` after the writer is dropped; and the honest negative case — while the last handle is still
bound, `os.remove` raises `PermissionError` in both engines.

## Performance (alternating rounds against a `132c65d` worktree, 5 rounds each, best-of)

| workload | base | now | vs CPython |
|---|---|---|---|
| `closed` — 4000 × `with open(p,'a')` | 397 ms | 387 ms (**0.97x**) | 0.91x |
| `abandoned` — 3000 × `open(p,'a')` never closed | 174 ms | 239 ms (**1.37x**) | **0.93x** |
| `compute` — no file IO | 295 ms | 294 ms (**1.00x**) | — |

The 1.37x on `abandoned` is real and accepted. The base number was not honest speed: it deferred
3000 stream teardowns to the CLR finalizer thread, so the work happened in the background while the
loop kept running and 3000 OS handles stayed open meanwhile. The new code performs the same closes
inline, which is also what CPython does — and lands at 239 ms where CPython takes 257 ms. Trading a
background-only speedup for a bounded handle count and a matching observable result is the trade the
policy asks for in room C.

## Left open (docs/known_gaps.md, P0-38 remainder)

1. Release still happens at observation points, not at the instant of death. Delete/rename are
   defended by the retry; a race with another process over a lock is not.
2. A `close()` override on a subclass of `_io.TextIOWrapper` is not dispatched on death.
3. No `ResourceWarning` — SharpPy closes the file instead of warning about it, so `-W error` runs
   differ.
4. `open()` itself does not retry; handle exhaustion was not probed.
5. Sockets and Godot resources are not registered yet. The registry takes an arbitrary `Action`, so
   the generalisation is already there.

## Found while measuring, filed separately (P0-39)

`sys.argv` is empty in SharpPy: `script.py one two` gives `['script.py', 'one', 'two']` under
CPython and `[]` under SharpPy — the script path itself is missing. Same neighbourhood as the
commented-out `ExecuteCodeString` call in `Program.cs`, which makes `-c "print(123)"` exit 0 with no
output. Both are room A failures (deterministic output), and this benchmark had to select its
workload through an environment variable because of it.
