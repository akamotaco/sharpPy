# Calls take frames, not host stack (P0-52)

Branch `work/bug-hunt`, base `11d7292`. Written by Claude.

Before: `down(100)` ended the process with a stack overflow. In Godot that ends the game.

| 동작 | CPython | SharpPy before | SharpPy after |
|---|---|---|---|
| Python to Python call | `DISPATCH_INLINED`, frame switch | `ExecuteFrame` recursion on the host stack | `TryInlineCall`, frame switch inside `ExecuteFrame` |
| 깊이 제한 | `_Py_EnterRecursivePy` | none | `EnterRecursiveFrame`, one check |
| 호스트 스택 필요 경로 | `_Py_EnterRecursiveCall` | none, process died | `TryEnsureSufficientExecutionStack`, `RecursionError` |
| 언와인딩 | `exit_unwind` | CLR exception through each frame | caller's `PendingException`, raised at its CALL |

The CLI runs on a 256 MB thread so that calls that still need the host stack reach the limit of 1000. A host such as
Godot keeps its own stack: plain Python recursion is unaffected, other paths raise `RecursionError` earlier than 1000.

Remaining route: keyword calls, `PyMethod`, generators and `__repr__`-style callbacks go through the host stack.
Removal condition: inline them when a probe needs the depth.

Handler entry moved to `TryEnterExceptionHandler`. `ExecuteFrame` now outlives the calls it runs, so a local naming
the exception kept it alive (`p85_retention.py`).

Performance, alternating A/B, 5 rounds, spreads at most 1.05: fib 0.978, method 1.000, closure 0.990, raise through six
frames 0.340. SharpPy is still about 20 times slower than CPython on these (fib 0.48 s against 0.022 s).

Gates: bughunt 124/124 with p125, evaluate 225/3/2, similarity 0, Godot 82/0. `l27_recursion.py` matches, including
the deepest frame number.

## Measured on the Godot main thread

`p125_recursion.py` ran through the `--fixture` route. A plain `TryEnsureSufficientExecutionStack` guard was not enough:
throwing near the end of the 1 MB stack overflowed it and ended Godot with `0xC00000FD`. So `PyVM.HostRecursionLimit`
(64) counts `ExecuteFrame` calls on the host stack, the CLI sets it to `int.MaxValue` on its 256 MB thread, and bound
methods and keyword calls also switch frames. In Godot a recursion that needs the host stack (`__repr__`, generators)
raises `RecursionError` at 64 instead of 1000. The harness copy of `Lib` was stale and is now synced from `Lib`.
`weakref.finalize` at exit does not run in Godot, because the host never reaches `PyFinalizer.Shutdown()`.
