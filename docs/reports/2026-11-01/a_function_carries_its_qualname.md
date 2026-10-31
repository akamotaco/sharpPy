# A function carries its own qualified name (L2.4 item 9, second commit)

Branch `work/bug-hunt`, base `c47c502` (the first L2.4 commit). The queue asked for item 9 as its own
commit. Boundary files `core/PyFinalizer.cs` and `modules/AtexitModule.cs` were not touched.

## What was measured before the change

| observable | CPython 3.12.15 | SharpPy at `c47c502` |
|---|---|---|
| `def f(): pass` then `f.__qualname__` | `f` | `AttributeError: 'function' object has no attribute '__qualname__'` |
| `class C: pass` then `'__qualname__' in C.__dict__` | `False` | `True` |
| `hasattr(C(), '__qualname__')` | `False` | `True` |
| `hasattr(C(), '__name__')` | `False` | `False` |
| `types.MethodType(C(), 1).__qualname__` | `AttributeError: 'C' object has no attribute '__qualname__'` | `C` |

The last two rows are the same defect seen from the other side: SharpPy left the class body's
`__qualname__` string in the class dict, so every instance of every class inherited the name.
`functools.wraps` had been swallowing the first one, which is why nothing failed loudly.

## The two fields CPython keeps

```
func_qualname   Objects/funcobject.c:705  getset, getter :529, setter :533
                                         :536-:541 - deleting and a non-string share one message
ht_qualname     Objects/typeobject.c:3416 type_new_set_ht_name takes the body entry out of the dict
                Objects/typeobject.c:250  type_qualname reads the field
                Objects/typeobject.c:1044 type_set_qualname replaces it, "can only assign string to
                                            %s.__qualname__, not '%s'"
```

## What changed

**Functions.** `PyFunction.QualName` is the `func_qualname` analogue: it starts from
`co_qualname` (`CodeObject.DisplayQualName`, with SharpPy's internal `<lambda_N>` ids mapped the way
`ReprName` already mapped them) and is what an assignment changes, so `__name__` and `__qualname__`
move independently afterwards. Registered as a `PyGetSetDescriptor` on the `function` type with
getter, setter and deleter; both refusals say `__qualname__ must be set to a string object`, which is
the one message CPython uses for a non-string assignment and for deletion
(`tests/bughunt/p117_qualname.py` prints it three times).

`ReprName` now returns `QualName` instead of recomputing from the code object. That keeps the case
CPython shows and SharpPy already matched: after `f.__name__ = "g"`, `repr(f)` is still
`<function f at 0x...>`, because `func_repr` reads the qualname.

**The write route.** `PyObject_GenericSetAttr` consults the MRO before the object's dict, so
`PyFunction.SetAttribute` now hands the value to a getset that has a setting slot, and
`PyFunction.DelAttribute` goes through `descr_delete` rather than the dict. Only descriptors *with* a
setter are routed: SharpPy registers `__name__`, `__doc__` and `__module__` with a getter only, and
routing those would have turned working assignments into errors. `PyGetSetDescriptor` grew
`HasSetter` and an optional deleter for exactly this, so the read-only ones keep the behaviour they
had and the difference is recorded as **P0-71** with a before/after table that shows those rows did
not move.

**Classes.** `PyClass` consumes the body's `__qualname__` into the type's `ht_qualname` field and
removes it from the dict, which is `type_new_set_ht_name`. Non-string values raise
`type __qualname__ must be a str, not X`. The metaclass installs a `PyQualnameDescriptor` of the same
name into `type`'s own dict, and that entry is not a body entry, so the consumption skips it - the
distinction is by descriptor type, not by name, because the same ctor builds both.

Three readers that had been reaching into the dict now read the field, which is what makes it one
route rather than two: `PyQualnameDescriptor` (the metaclass path), `type_repr`
(`core/PyType.cs`), and `_PyObject_FunctionStr`'s owner label (`core/PyBuiltin.cs`).

## Gates

| gate | result |
|---|---|
| build | 0 errors |
| `--filter l24_` | 11/11 `l24_*.py` acceptance probes match. The filter also catches `gap_l24_raise_identity.py`, which is P0-70 and outside the criteria |
| `--filter l23_` | the five `l23_*.py` acceptance probes still match (filter total 6/7; the mismatch is `gap_l23_slot_wrapper.py`, P0-67) |
| `tools/run_bughunt.py` | 114/114 matching (113 + `p117_qualname.py`) |
| `tools/evaluate.py --timeout 90` | `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}` |
| `tools/cpython_similarity.py` | exit 0 |
| Godot 4.7.2 mono replay | `REPLAY_RESULT 82 matched, 0 other`, `REPLAY_THREADS 2 main=2` |

## Performance

The stale baseline flagged `str_fmt +30.3%` and `closure_calls +38.2%`. Both were resolved by
measuring, not by arguing: an alternating run of HEAD and a worktree build of `c47c502`, with the
base binary run twice per round as a control
(`.artifacts/tmp/l24_qn_noise.py`, 7 rounds):

| workload | head min | base min | ratio of mins | control spread |
|---|---|---|---|---|
| str_fmt | 0.0742 | 0.0795 | 0.933 | 1.12 |
| list_comp | 0.0504 | 0.0502 | 1.004 | 1.74 |
| int_loop | 0.2101 | 0.2118 | 0.992 | 1.04 |

`str_fmt` measured faster than the base on its best round and its control spread covers the whole
difference; `list_comp`'s control spread reached 1.74. Neither workload reaches the changed code on
its hot path - the new MRO lookup runs on function attribute *writes*, which happen at definition
time. The six-workload A/B (`.artifacts/tmp/l24_perf_ab_qn.py`) had every other workload inside
1.013.

## Code

`+101 / -19` in `core` and `type`: `core/PyFunction.cs` (field, descriptor, write and delete routes),
`core/PyDescriptor.cs` (`HasSetter`, optional deleter, the `descrobject.c` note), `type/PyClass.cs`
(consumption in the ctor), `core/PyType.cs` (field accessor, `type_repr`),
`core/PyTypeMetaclass.cs` (getset reads and writes the field), `core/PyBuiltin.cs`
(`_PyObject_FunctionStr` label).

## L2.4 status after this commit

Items 1-8 and 10 landed in `c47c502`, item 9 here. The queue's acceptance line - eleven `l24_*.py`
probes matching, the five `l23_*.py` probes holding, gates passing - is met. Left registered by this
work: **P0-70** raise/except identity, **P0-71** function `__doc__`/`__name__` setters, plus the
`SystemExit(1).__dict__` face added to **P0-60**.
