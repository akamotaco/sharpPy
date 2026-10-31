# The slots an exception owns and the writes the exit path owes (L2.4 part 1)

Branch `work/bug-hunt`, base `4ccb38f`. This is the first of the two L2.4 commits: items 1-8 and 10.
Item 9 (`__qualname__`) is the second commit on its own. Boundary files `core/PyFinalizer.cs` and
`modules/AtexitModule.cs` were not touched.

## The single-path table, with the SharpPy column measured

The queue asked for this table to be filled before the code changed. "before" is `4ccb38f`, measured
with the probe files; "after" is this commit.

| 동작 | CPython의 단일 지점 | SharpPy before | SharpPy after | 항목 |
|---|---|---|---|---|
| 예외 객체 | `do_raise` raises the object it was given | `PyVM.cs:5061` `new PyException(customInstance.ToString(), classInst.InstanceType, classInst)` and four siblings; `e is z` **False** | same wrapper, but every observable the probes reach now reads through it to the original: `raise SystemExit(3) from None` exits 1, `with_traceback` returns the same object. Identity itself is **still False** and is registered as P0-70 | 5, 7 |
| 생성 | `BaseException_new` stores args, `BaseException_init` may re-do it | args written only on the `__init__` route: `X(1).__new__(X, 1)` gave `args == ()`; `class K(Exception): def __init__: pass` gave `args == ()` | `BaseException.__new__` descriptor stores at allocation (`type/PyExceptionInstance.cs`, `core/PyType.cs`); a user `__init__` that never calls `super().__init__` still shows `('z',)` | 3, 4 |
| 저장 | the object's `args`, `code` slots | `self.SetAttribute("code", ...)`, `InstanceDict`: user `__setattr__` fired during construction, a read-only `code` property blocked construction, both names were visible in `__dict__` | `PyExceptionInstance` carries `ArgsSlot`/`CodeSlot`; `PyExceptionSlots.ArgsOf/StoreArgs/CodeOf/StoreCode` is the only route, and it unwraps to the original instance. `Hook(3)` no longer prints during construction; `Prop(SystemExit)` with `code = property(...)` constructs | 2 |
| 읽기 | member descriptors and `BaseException_repr` | `.args` and `repr` could disagree: a caught `K('z')` had `args == ('z',)` and `repr == 'K()'`; `BaseException.__repr__(1)` returned a string instead of the descriptor `TypeError` | both read `PyExceptionSlots`; `BaseException.__repr__`/`__str__` refuse a foreign receiver with `descriptor '__repr__' requires a 'BaseException' object but received a 'int'` | 2, 5 |
| 종료 | `_Py_HandleSystemExit` | file and `-c` went through `ReportExitState`; REPL discarded the status (rc 0) and treated `class E(SystemExit)` as an ordinary error | all three go through `FileExecutor.ReportExitState`; `SharpPyRepl.Start()` returns the status and `Program.cs` exits with it | 1, 6, 8 |

The one row that could not be closed is the first. `except ... as e` binds the CLR wrapper, so
`e is z` is False where CPython is True. The queue's rule was not to patch items 2, 5 and 7 on the
wrapped side but to look at removing the wrapper first; removing it means the CLR type of a heap
exception instance has to become `PyBaseException`, because `PythonException.PyException` is typed
`PyBaseException` (`core/Exception` side, `core/PyException.cs:995`) and 124 places read it. Caller
counts and the anchor are written up as **P0-70** in `docs/known_gaps.md`; the measurement lives in
`docs/plans/lifetime_foundation/probes/gap_l24_raise_identity.py`.

```
new PyException(                    10 sites  (5 of them in the RAISE_VARARGS block)
OriginalInstance                    16 sites
catch (PythonException             144 sites
PythonException.PyException reads  124 sites
```

## What each item changed

| # | defect | CPython anchor | shape of the fix |
|---|---|---|---|
| 1 | a failed `code` lookup produced rc 0 | `Python/pythonrun.c:732` and the else path to `:744` | the failed lookup keeps the exception object as the payload, so the status is 1; the error itself is cleared on the way, exactly as `:753 PyErr_Clear` does |
| 2 | `code`/`args` lived in `InstanceDict` | `Objects/exceptions.c:56` args at allocation, `Objects/structmember.c` member get/set | `PyExceptionInstance : PyClassInstance` with two slots and one accessor class; no field added to `PyClassInstance` (C1's H3 cost baseline stays put), `e.__dict__['code'] = 9` cannot move `.code` |
| 3 | `SystemExit.__init__(e)` reset `code` | `SystemExit_init`, `Objects/exceptions.c:640` | no-argument init changes `args` only; `CodeForArgs` comment now says the None is the field's initial value, not a reset |
| 4 | `args` were missing when `__init__` skipped `super()` | `BaseException_new` (`Objects/exceptions.c:56`), `SystemExit.__new__`, `BaseException.__new__(ValueError, 1, 2)` | `BaseException.__new__` registered as a descriptor; keyword arguments are not stored in `args` and are not refused |
| 5 | `repr` read a different `args` than `.args` | `BaseException_repr` (`Objects/exceptions.c:131`), args part `:120` | both go through the slot; the descriptor receiver check matches `__init__`'s |
| 6 | the exit message and its failures | `Python/pythonrun.c:755` `PyFile_WriteObject`, `:761 PySys_WriteStderr("\n")`, `Python/sysmodule.c:4001` `sys_format`, `Python/pylifecycle.c:1830` → `:3045 PyErr_WriteUnraisable(NULL)` before `_PyAtExit_Call` at `:1845` | message write and `"\n"` write are separate and both attempted, an empty message is still written, a failing `sys.stderr` write falls back to the C stderr only for the newline, and the failure travels as one pending error reported with `PyUnraisable.Report(pending, PyNone.Instance)` before `Shutdown()` - no `Exception ignored in` header, one report |
| 6b | the huge-code path wrote a fixed string | `PyLong_AsLong` leaves `OverflowError` raised and returns -1 | an `OverflowError` object is created and reported through the same unraisable path; rc stays -1 (`p113`, platform status 4294967295) |
| 7 | `raise SystemExit(3) from None` printed a traceback and exited 1 with `Exception: 3`; `with_traceback` threw `InvalidCastException` | `Python/errors.c:207 _PyErr_SetObject`, `PyObject-WithTraceback` on `PyBaseException` | the raise test is `exc is PyBaseException` (was `PyException`, a CLR family class), so `SystemExit`, `KeyboardInterrupt`, `GeneratorExit` and `BaseException` are raised as themselves; the cause test is likewise `PyBaseException`; `with_traceback` casts to `PyBaseException` |
| 8 | the REPL discarded the status | `Modules/main.c` runs stdin then handles the exit | `SharpPyRepl.Start()` returns an int, the escape routes through `ReportExitState`, `Program.cs` calls `Environment.Exit`, and `PyFinalizer.Shutdown()` runs at the end of the session; both `is PySystemExit` tests became `PySystemExit.Matches` |
| 10 | dead code | - | `PySystemExit.HandleStatus` had no caller; removed |

Two extra corrections came out of the same route:

- **`KeyError.__str__` is inheritable now.** `key_str` (`Objects/exceptions.c:1051`) is `tp_str`, so a
  Python subclass of `KeyError` must print `'a'` for `KeyError('a')`. SharpPy had that behaviour on
  the CLR class only. Registering it from `BaseException`'s initialiser crashed the `PyType` static
  constructor (`KeyErrorType` did not exist yet) - it belongs in `InitializeKeyErrorTypeDescriptors`,
  which is also where CPython's locality puts it.
- **the write path now sees builtin MRO levels.** `_PyType_Lookup` searches the MRO once and every
  level is a type. `GetAttributeGeneric` did that; `SetAttributeDefault` only looked in
  `PyClass.ClassDict` and then dropped the value into the instance dict, so `h.code = 4` on a
  SystemExit subclass that defines `__setattr__` stored a dict entry the member never reads
  (`type/PyClass.cs`).

## REPL status on piped stdin

The harness cannot feed standard input, so these four lines were run directly. CPython with
`-i`, SharpPy as the bare interpreter that reads stdin today. Full table generator:
`.artifacts/tmp/l24_repl.py`.

| program | CPython `-i` | SharpPy before | SharpPy after |
|---|---|---|---|
| `raise SystemExit(4)` | rc 4, no `still here` | rc 0 | rc 4 |
| `class E(SystemExit): pass` then `raise E(4)` | rc 4, no `still here` | rc 0 **and** `still here` | rc 4 |
| `import sys; sys.exit("m")` | rc 1, `m` on stderr | rc 0, no message | rc 1, `m` on stderr |
| `exit(4)` | rc 4 | rc 0 | rc 4 |

Still different, and not this item: SharpPy prints its banner and prompts on stdout where CPython
writes the prompt to stderr. That belongs to the P0-41 command-line/REPL surface.

## Gates

| gate | result |
|---|---|
| build (Release, `-p:SkipCodeGeneration=true`) | 0 errors |
| `--filter l24_` | 10/11 matching - `l24_qualname.py` is item 9 and still differs |
| `--filter l23_` | 5/5 still matching |
| `tools/run_bughunt.py` | 113/113 matching (103 + the 10 copies `p107`-`p116`) |
| `tools/evaluate.py --timeout 90` | `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}` |
| `tools/cpython_similarity.py` | exit 0 |
| Godot 4.7.2 mono replay | `REPLAY_RESULT 82 matched, 0 other`, `REPLAY_THREADS 2 main=2` |

## Performance

Exception construction is a hot path candidate, so it was measured rather than assumed. The
profiler suite showed nothing outside noise (`+3.5%` worst on `closure_calls`), and an A/B against a
worktree build of `4ccb38f` on `.artifacts/perf/exc_suite.py` (5 alternating rounds, best-of) is
flat:

(`.artifacts/tmp/l24_perf_ab.py`):
|---|---|---|---|
| no_raise | 0.0256 | 0.0258 | 0.992 |
| raise_builtin | 0.1203 | 0.1195 | 1.007 |
| raise_stmt | 0.0996 | 0.0998 | 0.998 |
| raise_bare | 0.0975 | 0.0974 | 1.001 |
| raise_custom | 0.1990 | 0.1990 | 1.000 |
| catch_basexc | 0.1212 | 0.1208 | 1.003 |
| nested_handler | 0.0829 | 0.0830 | 0.999 |

## Code

`+362 / -188` in `core`, `runtime`, `type` and `Program.cs`, of which 139 added lines are comments.
New file `type/PyExceptionInstance.cs` (107 lines) holds the two slots and the single accessor route.
Modified: `core/FileExecutor.cs`, `core/PyException.cs`, `core/PyType.cs`, `core/SharpPyRepl.cs`,
`runtime/PyVM.cs`, `type/PyClass.cs`, `Program.cs`.

## Left open by this commit

- **P0-70** raise/except identity, with the caller counts above.
- **P0-60** grew a second face: `SystemExit(1).__dict__` is `{}` on CPython and `AttributeError` on
  SharpPy, same missing `tp_dictoffset` root.
- **item 9** `__qualname__`, next commit.
- `docs/known_gaps.md` P0-59 is now recorded as closed by L2.4, and P0-68's rc is corrected from the
  `130` that Git Bash displayed to `3221225786` (`0xC000013A`).
