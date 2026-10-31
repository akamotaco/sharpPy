# One lookup for attribute writes (L2.5 commit 1, items 1-3)

Branch `work/bug-hunt`, base `03430a0`. Written by the local qwen model from a design by Claude, reviewed and
corrected by Claude. `core/PyFinalizer.cs` and `modules/AtexitModule.cs` were not touched.

## Single-route table

| 동작 | CPython | SharpPy before | SharpPy after |
|---|---|---|---|
| 클래스 속성 대입, 삭제 | `type_setattro`, metatype data descriptor first | `PyClass.SetAttribute` wrote `ClassDict`, `__name__`/`__qualname__`/`__module__` registered twice | `PyClass.MetatypeAttribute` then descriptor; one table in `TypeDict`; `PyNameDescriptor`, `PyModuleDescriptor`, `PyQualnameDescriptor` deleted |
| `__name__` 저장 | `tp_name`, `ht_name` | `ClassDict["__name__"]`, visible to instances | `PyType.SetName`, field only |
| 인스턴스 속성 대입, 삭제 | `_PyType_Lookup` then descriptor | two copied MRO loops, delete without builtin levels | `FindDataDescriptorInMro`, one loop |
| `args` | `BaseException_set_args`: `PySequence_Tuple`, one tuple | setter with its own unwrap, non-tuple read as `()`, new tuple per read | `PyExceptionSlots.AssignArgs` and `ArgsTuple` |

Remaining route: `PyDocDescriptor`, `PyBasesDescriptor`, `PyMroDescriptor`, `PyBaseDescriptor` still live in the metaclass
dict. They are not duplicates of a read-only entry that blocks assignment, so they stayed. The wrapper unwrap
(`PyExceptionSlots.Owner`) stays until E1 (P0-70).

## Also fixed on the same route

The default `object.__repr__` printed the class name, CPython prints `__qualname__`. `C.__name__ = "Y"` no longer
leaks into `C.__dict__` or instances. A metaclass property now refuses `D.p = 2`. Assigning to a builtin type's
`__name__` keeps the immutable type message.

## Review of the generated code

qwen read the design correctly. Corrected by the reviewer: a malformed patch block, a re-added `deleter` (the
reviewer had supplied the wrong excerpt), a deleted limitation comment, a broken `__module__` block, `__name__`
stored in the class dict, the lookup missing user metaclasses, and `type.__module__` leaking the descriptor (caught
by `p26`).

## Gates

bughunt 117/117 (114 + p118-p120), `l23_` and `l24_` probes hold, {"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}, similarity exit 0,
Godot 4.7.2 mono replay 82 matched 0 other. `.cs` +182/-320.

Open from L2.5: items 4 and 5.
