# p26 — `type.__module__` metaclass self-resolution

Date: 2026-10-29 (overnight autonomous work)
Branch: `work/bug-hunt`

## Measured divergence (before)

`type.__module__` returned a raw descriptor object instead of `'builtins'`:

| expression | CPython 3.12.15 | SharpPy (before) |
|---|---|---|
| `type.__module__` | `'builtins'` | `<getset_descriptor object at 0x…>` |
| `type(type).__module__` | `'builtins'` | `<getset_descriptor object at 0x…>` |
| `type(C).__module__` (C a user class) | `'builtins'` | `<getset_descriptor object at 0x…>` |
| `C.__class__.__module__` | `'builtins'` | `<getset_descriptor object at 0x…>` |
| `int.__module__` / `str.__module__` | `'builtins'` | `'builtins'` (already OK) |
| `type.__name__` / `type.__qualname__` | `'type'` | `'type'` (already OK) |

Any expression whose value is the `type` object (`type`, `type(C)`,
`C.__class__`) returned the descriptor instead of the module name.

## Root cause (C vs C#/CLR)

CPython models `type` as a static `PyType_Type`; `type.__module__` runs
`type_module()` which sees a non-heap type and returns `builtins`. Other
classes (`int.__module__`) also run `type_module()` through the metaclass.

SharpPy models `type` as `PyTypeMetaclass.Instance`, and
`PyTypeMetaclass : PyClass` (a heap-style class). There are two module
providers:

- `type.ClassDict["__module__"] = new PyModuleDescriptor()` — the metaclass
  provider that serves **other** classes (`int.__module__` resolves through
  this: `PyModuleDescriptor.Get(int)` sees a native `PyType` → `'builtins'`).
  This path was already correct.
- `type.TypeDict["__module__"] = new PyGetSetDescriptor(...)` — the
  `type.__dict__['__module__']` getset.

`type.__module__` is resolved by `PyTypeMetaclass.GetAttribute`, which searches
`type.MRO` *TypeDicts* and finds the `PyGetSetDescriptor`, then invokes its
getter with `self = type`. Inside that getter (in `PyType.cs`), because
`type is PyClass` is **true** (PyTypeMetaclass derives from PyClass), it read
`type.ClassDict["__module__"]` and returned it verbatim — which is the
`PyModuleDescriptor` object, not a module string. Hence the leaked descriptor.

## Fix

`core/PyType.cs` `__module__` getset getter: when the class-dict entry is
itself an `IDescriptor` (the metaclass provider), skip it and fall through to
the native module logic (`Module` field → `'builtins'`). A plain module string
in a heap class dict is still returned unchanged.

```csharp
if (customType.ClassDict.TryGetValue("__module__", out var mod) && mod is not IDescriptor)
    return mod;
// fall through: Module field, tp_name '.', else "builtins"
```

`type.__module__` now falls through to `'builtins'`; `int.__module__` and every
other-class path is untouched (they use `PyModuleDescriptor`).

## Verification

- New probe `tests/bughunt/p26_type_module.py`: stdout diff `0` vs CPython,
  covering `type.__module__`, `type(type)`, `type(C)`, `C.__class__`, native
  types, exception types, and the p19/p25 continuity cases
  (`io.TextIOWrapper.__module__ == '_io'`, `type(iter([])).__module__ == 'builtins'`).
- `tools/run_bughunt.py`: **33/33 matching**; `mismatches: []`.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: the change is only in the `__module__` getter, which
  no benchmark path calls. One run flagged `str_fmt +21.8% / list_comp +65.7%`,
  but re-measurement of the **same binary** gave `list_comp` `+0.0% / +0.9% /
  +83.8%` and `str_fmt` `+3.5% / +8.1% / +19.5%` — a single DLL spanning 0% and
  83% is conclusive machine noise, not a code regression.
- Godot 4.7.2 mono replay (Release DLL): `REPLAY_RESULT 82 matched, 0 other`.

## Residual gaps recorded

- `super.__module__` (and `type(super)`) still diverge: the `super` **global**
  is a `builtin_function_or_method` (`CallSuper`), whereas CPython's `super` is
  a real `type` object (`super.__module__ == 'builtins'`,
  `type(super) is type`). `type(super(B, x))` is already `super` (p24); making
  the global `super` name itself the callable `PyType.SuperType` is a broader
  change to the super construction/opcode path and is deferred with p24's other
  "super as class attribute / subclass" residuals.
