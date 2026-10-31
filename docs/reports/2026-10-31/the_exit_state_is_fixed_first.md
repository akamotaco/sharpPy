# The exit state is fixed before the guards run (L2.3)

Branch `work/bug-hunt`, base `48379c9`. One commit, then stop: the queue says L2.3 is the current
work and C1 has not landed. Boundary files `core/PyFinalizer.cs` and `modules/AtexitModule.cs`
were not touched.

## What was wrong, and what CPython does

| # | defect | CPython anchor | shape of the fix |
|---|---|---|---|
| 1 | the exit status was read after `PyFinalizer.Shutdown()` | `Python/pythonrun.c:708 _Py_HandleSystemExit` then `Py_Exit` | `ReportExitState` runs before `Shutdown()` at both `Environment.Exit` sites (`core/FileExecutor.cs`) |
| 2 | the message went to CLR `Console.Error`, and a raising `__str__` killed the process with CLR rc `3762504530` | `:755 PyFile_WriteObject` on `sys.stderr`, `:761 PySys_WriteStderr("\n")` | write through the `sys.stderr` of that moment; a reporting failure still gives status 1 and `Shutdown()` still runs |
| 3 | `SystemExit.code` was a view of `args` | `Objects/exceptions.c:633 SystemExit_init` | stored field, settable, `del` reads back as `None` (`:668` member delete semantics) |
| 4 | `class E(SystemExit)` was not a SystemExit to the interpreter | `PyErr_GivenExceptionMatches` | `PySystemExit.Matches` (type based) used by `runtime/PyInterpreter.cs` and the exit path; `raise E(4)` now gives rc 4 and `.code` works on subclasses |
| 5 | `KeyboardInterrupt(...)` threw its arguments away | `BaseException_init` | shared args initialisation, `KeyboardInterrupt()` has `args == ()` |
| 6 | `exit(code=2)` / `exit(1, 2)` | `Lib/_sitebuiltins.py:38 Quitter.__call__` | `PyQuitter` keyword support and CPython arity wording |
| 7 | `type(o.m)(func, obj)` raised `NotImplementedError` | `Objects/classobject.c:207 method_new` | `method` is callable: `method expected 2 arguments, got N`, `first argument must be callable`, `instance must not be None`, `method() takes no keyword arguments`; `PyMethod.Function` is now `PyObject` because `method_new` accepts any callable |
| 8 | `dict.fromkeys == dict.fromkeys` was False | `Objects/methodobject.c:292 meth_richcompare`, `:316 meth_hash` | `PyCMethod` carries the same rule as `PyMethod` and `PyBuiltinBoundMethod` |

`method_repr` (`Objects/classobject.c:273`) reads `__qualname__`, then `__name__`, then keeps `"?"`.
For a function those names are its own fields, so `ReprName` still comes from `PyFunction`
(`core/PyFunction.cs:601`); the attribute walk is only the fallback for other callables. That
regression appeared once (`p23_repr_matrix.py` lost `C.m`) and is now covered by `p104`.

`method_hash` (`Objects/classobject.c:299`) mixes the receiver address with **`PyObject_Hash(im_func)`**
- the Python-level hash. Using `Function.ToHash()` (CLR identity) made
`hash(Method(a, h)) == hash(Method(b, h))` False for a callable whose `__hash__` returns 1.
The three method-ish hashes now use `GetPythonHash()`, which is the `PyObject_Hash` equivalent
(`core/PyObject.cs:132`: consults `__hash__`, raises `unhashable type`, maps `-1`).

## The record correction, measured rather than asserted

The queue said the P0-53 claim "the status is decided before teardown" was false at L2.2 and that
the L2.2 measurement could not distinguish the order. I built the base commit in a worktree
(`48379c9`) and re-ran the five-program table plus a two-statement program:

| program | CPython | L2.2 build | L2.3 build |
|---|---|---|---|
| `atexit.register(sys.exit, 5)` + `sys.exit(3)` | rc 3, ignored callback | rc 3 | rc 3 |
| `sys.stderr = W()` then `sys.exit("boom")` | `captured: 'boom'`, `captured: '\n'` | stdout empty, real stderr `boom` | `captured: 'boom'`, `captured: '\n'` |

So the rc table really did match at L2.2 - that measurement only exercised callbacks that *raise*,
which cannot reveal the order. What was observably broken at L2.2 was the output side: the message
ignored a replaced `sys.stderr`, and the trailing newline of `_Py_HandleSystemExit` was missing
(that is the `to stdoutatexit after the message` line the harness diffed). Both are fixed. The
ordering change is still made, so a callback can no longer move a status that is already decided.

One of my own probes was wrong while measuring this: `raise E(4)` seemed to give rc 1 on CPython
because the script forgot `import atexit` and died with `NameError`. With the import, CPython gives
rc 4 and SharpPy gives rc 4. Probe-authoring error, not a SharpPy divergence.

## Side effect worth naming: P0-59

`l23_exit_fields.py` pins `repr(SubK(1))`. To match it, `BaseException_repr`
(`Objects/exceptions.c:131`, args part `:120`) is now registered as `__repr__` on the
`BaseException` type dict, and `PyClassInstance.ToRepr()` consults `__repr__` in the builtin levels
of the MRO instead of falling through to the CLR default
(`core/PyType.cs`, `type/PyClass.cs`). That closes registered gap **P0-59**
(`repr(Sub())` = `'Sub()'`, `repr(Sub('x'))` = `"Sub('x')"`), verified by
`probes/gap_l2b_measure.py` line a). Lines b)-e) of that probe still differ (P0-60, P0-61, P0-62,
P0-63).

## Newly registered (not fixed)

- **P0-67** slot wrapper compare: `h.__str__ == h.__str__` and `n.__add__ == n.__add__` are False;
  `type(l.__len__)` is `builtin_function_or_method` where CPython says `method-wrapper`. Measured by
  `probes/gap_l23_slot_wrapper.py`. `wrapper_richcompare` (`Objects/descrobject.c:1297`) compares
  `descr` and `self` pointers (`:1313`); SharpPy has the rule on `PyCMethod` but not on
  `PyWrapperDescriptor` / `PyMemberDescriptor`.
- **P0-68** uncaught `KeyboardInterrupt`: rc 130 (3221225786 when it arrives as a real Ctrl-C) and
  the traceback on stderr, vs SharpPy rc 1 on stdout. The suppression test itself is now type based.
- **P0-69** `s.upper == 'ab'.upper` is False. Measured with the cause separated in the same probe:
  `s is "ab"` is True on CPython and False on SharpPy, so this is **the P0-56 string-constant
  cause**, not a method-comparison defect. No content comparison was added to method equality.

## Reproducible probes moved into the repository

`.artifacts/tmp/l22_*.py` were cited as evidence while being gitignored. They now live in the
repository: `probes/gap_l22_exit_rc.py`, `gap_l22_exit_atexit.py`, `gap_l22_method_eq.py`,
`gap_l22_builtin_method.py`, `gap_l22_unregister.py`, `gap_l22_quit_msgs.py`, plus the two new
`gap_l22_two_statements.py` and `gap_l23_stream.py`. Citations in
`docs/reports/2026-10-31/the_exit_code_arrives.md` and `a_bound_method_equals_itself.md` point at
the new paths. Dates `2026-11-01` in `known_gaps.md` are back to `2026-10-31`, and the corrupted
characters in the `todo.md` L2.2 row (`낌`/`임`) plus `튜풀` are repaired.

## Gates

| gate | value |
|---|---|
| build | 0 errors |
| `--filter l23_` | 5/5 matching |
| `--filter l2` | criterion files match (`gap_*` measurement files are outside the criteria) |
| `tools/run_bughunt.py` | **103/103** matching |
| `tools/evaluate.py --timeout 90` | `225 / 3 / 2` |
| `tools/cpython_similarity.py` | exit 0 |
| Godot replay (Release assembly) | `82 matched, 0 other`, `REPLAY_THREADS 2 main=2` |

Copies: `p100_exit_fields.py`, `p101_exit_message.py`, `p102_exit_bad_str.py`,
`p103_exit_keyword.py`, `p104_method_type.py`.

## Performance

`tools/perf_probe.py` flagged `str_fmt +21.2%` and `list_comp +71.4%` against the stale baseline.
The A/B against the `48379c9` worktree build (6 alternating rounds, plus a same-binary control)
says suite noise:

| workload | base | new | ratio | control spread (same binary) |
|---|---:|---:|---:|---|
| int_loop | 0.2119 | 0.2122 | 1.00 | 0.99 - 1.02 |
| str_fmt | 0.0936 | 0.0970 | 1.04 | 0.82 - 1.06 |
| list_comp | 0.0934 | 0.0940 | 1.01 | 0.50 - 1.07 |
| dict_ops | 0.0616 | 0.0599 | 0.97 | 0.91 - 1.08 |
| closure_calls | 0.0539 | 0.0519 | 0.96 | 0.95 - 1.33 |
| comp_lambda | 0.0331 | 0.0330 | 1.00 | 0.90 - 1.08 |
| unpack_star | 0.2281 | 0.2291 | 1.00 | 1.00 - 1.02 |
| call_heavy_fib20 | 0.0195 | 0.0190 | 0.98 | 0.80 - 1.03 |

Every ratio sits inside the control spread, and none of these workloads compare, hash, or repr a
method or exception. `list_comp` alone swings 0.50-1.07 with the same binary.

Diff: code `+234 / -50`, comment `+111 / -17` across `core`, `runtime`, `type`, `modules`.

## Residual

- `type(math.sqrt).__name__` is still `object` (P0-66).
- Slot wrappers, uncaught `KeyboardInterrupt` rc/stream, and shared string constants (P0-67, P0-68,
  P0-56/P0-69).
- The uncaught-exception traceback still goes to stdout rather than stderr, which is why P0-68's
  stream half is not a new gap but the older registered one.
- `PyBoundClassMethod` (`core/PyBuiltinClassMethod.cs:81`) is private and still compares by
  reference; the visible `dict.fromkeys` path is `PyCMethod`, which now has the rule.
- C1 has not landed, so L3 (`weakref`) stays gated. Stop here.
