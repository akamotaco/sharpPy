# The weakref library, and what kept objects alive (L3)

Branch `work/bug-hunt`, base `24f4d40`. `Lib/weakref.py` and `Lib/_weakrefset.py` are byte-identical to CPython 3.12.15.

## Single-route table

| 동작 | CPython | SharpPy before | SharpPy after |
|---|---|---|---|
| 약참조 가능 여부 | `tp_weaklistoffset` | temporary type list | `PyTypeFlags.ManagedWeakRef`, table measured with `__weakrefoffset__` (`WeakListFlag`) |
| 서브클래스 슬롯 | `update_one_slot` | CLR virtual only | declared method wins, `Slot*` methods end `super()` |
| 함수가 붙잡는 것 | code, cells, defaults, globals | also the defining frame, through a C# lambda and `ParentScope` | code, cells, defaults, globals |
| 생성자 인자 | not kept | kept on every instance | kept only for str and frozenset subclasses, which build their value from them later |

## Two retention defects outside weakref

`weakref.finalize` and `WeakMethod` exposed them. Both affected ordinary programs as memory that never came back.

- `PyClassInstance.ConstructorArgs` held every constructor argument for the life of the instance.
- A function defined inside a call kept all locals of that call, used or not, because the implementation lambda named
  `frame` and `ParentScope` held the scope chain.

`tests/bughunt/p124_no_retention.py` covers both, including lambda, generator and async forms.

## A regression from commit e9ad1a6, fixed here

`Mangle(null)` threw inside a `try` in the import conversion when an `import x` without `as` stood in a method, so the
module name became `SharpPy.Generated.GeneratedAlias`. `import os` inside any method failed. Bughunt, evaluate and
Godot all passed with this defect in place. `p123_mangle.py` now covers plain imports in methods.

A reference and its target that become unreachable together no longer call back. `IsCondemned` covers a reference that
has a guard of its own, which brings it through finalization.

## H3: cost of weak references

`WeakValueDictionary` with 100,000 entries, one run, `.artifacts/tmp/perf_weak.py`.

| phase | CPython 3.12.15 | SharpPy |
|---|---|---|
| fill | 0.069 s | 1.845 s |
| read | 0.014 s | 0.319 s |
| all die | 0.013 s | 0.774 s |

No limit was set for this. An object with no weak reference pays nothing extra.

## Gates

`q4_weakref.py` matches, `l3_no_retention.py` matches, bughunt 123/123, evaluate 225/3/2, similarity 0, Godot 82/0.
