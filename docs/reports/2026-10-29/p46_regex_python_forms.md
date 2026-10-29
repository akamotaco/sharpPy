# p46 — named backreferences and `re.sub` templates on the .NET engine

Date: 2026-10-29
Branch: `work/bug-hunt`
Closes: P0-2 (partially — see P0-17 for what remains)
Adds: P0-17 (regex group numbering), P1-10 (native `re.error` identity)

## Measured before the fix

`re.findall(r"(?P<c>\w) (?P=c)", "a a b c c")` → CPython `['a','c']`, SharpPy `[]`.
A whole class of Python-only constructs failed, and silently:

| expression | CPython | SharpPy before |
|---|---|---|
| `findall(r"(?P<c>\w) (?P=c)", "a a b c c")` | `['a','c']` | `[]` |
| `search(r"(?P<x>\w) (?P=x)", "p q q").group(0)` | `'q q'` | `AttributeError` on `None` |
| `compile(r"(?P<a>[ab]+) (?P=a)").findall("aa aa b b")` | `['aa','b']` | `[]` |
| `findall(r"(?P<a>\w)(?P<b>\w) (?P=a)(?P=b)", "ab ab cd cd")` | `[('a','b'),('c','d')]` | `[]` |
| `sub(r"(?P<w>\w+) (?P=w)", r"[\1]", "ab ab c d")` | `'[ab] c d'` | `'ab ab c d'` |
| `sub(r"(\w)", r"[\1]", "ab")` | `'[a][b]'` | `'[\\1][\\1]'` |
| `sub(r"(?P<x>\w)", r"[\g<x>]", "ab")` | `'[a][b]'` | `'[\\g<x>][\\g<x>]'` |
| `sub(r"(\w)", r"[\g<1>]", "ab")` | `'[a][b]'` | `'[\\g<1>][\\g<1>]'` |
| `sub(r"(\w)", r"\\\1", "ab")` | `'\\a\\b'` | `'\\\\\\1\\\\\\1'` |
| `sub(r"(\w)", r"$\1", "ab")` | `'$a$b'` | `'$\\1$\\1'` |
| `sub(r"(\w)", r"\\n\1", "ab")` | `'\\na\\nb'` | `'\\\\n\\1\\\\n\\1'` |
| `sub(r"(a)(b)…(j)", r"\10\1", "abcdefghij")` | `'ja'` | `'\\10\\1'` |
| `sub(r"(\w)", r"\q", "ab")` | `error: bad escape \q at position 0` | returned `'\\q\\q'` |
| `(?<c>\w) \k<c>` (.NET syntax) | `error: unknown extension ?<c` | same ✓ |

Nothing raised, which is why the old ledger called this a "hybrid-engine result". It was two
concrete defects.

## Defect 1 — the replacement argument of `Regex.Replace` is not a regex

`ToDotNetPattern` translated `(?P=name)` with

```csharp
Regex.Replace(p, @"\(\?P=(\w+)\)", @"\\k<$1>");   // wrong
```

In .NET the *pattern* is a regex but the **replacement is a literal string where only `$` is
special**. `@"\\k<$1>"` therefore yields `\\k<c>`, i.e. an escaped `k`, and the compiled pattern
looked for a literal `k<c>` — so any named backreference matched nothing. One backslash is the
fix. C# vs CPython trap worth keeping: in Python `re.sub` the template *is* documented to
process backslashes, so `r"\\k"`-style habits carry over wrongly here.

## Defect 2 — `re.sub` templates were passed through untranslated

CPython parses replacement templates in their own function, `re/_parser.py parse_template`,
with rules that match neither .NET nor ordinary escaping:

* `\g<name>` and `\g<1>` substitute a group;
* `\1`…`\99` substitute a group number, taking at most two digits (`\10` = group 10);
* `\0` is an **octal NUL**, not group 0 (`\g<0>` is the whole match);
* `\a \b \f \n \r \t \v` are characters (`\b` is backspace, not a word boundary);
* a backslash before punctuation is dropped;
* a backslash before an unknown letter raises `bad escape \q at position 0`.

`ToDotNetReplacement` now emits the .NET form (`${name}`, `${1}`, `$$` for a literal `$`) and
raises CPython's message for bad escapes. Call sites: `PySrePattern.Sub` and `Subn` (the module
-level `re.sub` comes from `Lib/re/__init__.py`, which forwards here).

## The remaining failure is numbering, not syntax — new P0-17

Isolating the mixed case exposed something bigger than backreferences:

```
re.findall(r"(?P<a>\w)(\w)", "ab")   CPython [('a','b')]   SharpPy [('b','a')]
m = re.match(r"(?P<a>\w) (\w)", "a b")
m.span(1)                            CPython (0, 1)         SharpPy (2, 3)
m.span(2)                            CPython (2, 3)         SharpPy (0, 1)
m.group("a")                         'a'                    'a'      ✓
re.findall(r"(?P<a>\w) (\w) (?P=a) \2", "a b a b")  CPython [('a','b')]  SharpPy []
```

.NET assigns capture numbers with the **unnamed groups first**, CPython purely by position of
`(`. So `(?P<a>…)(…)` is groups `1,2` in CPython and `2,1` in .NET: numeric references,
`group(n)`, `span(n)` and `findall` tuples are all shifted whenever a named group precedes an
unnamed one, and a numeric backreference can point at a group that has not captured yet, which
is why the pattern stops matching entirely. Name-based access is unaffected.

Fixing it properly means pinning CPython's numbering at translation time — rewriting every
capture group to a canonical name (`(?<__g1>…)`), rewriting `\1`, `(?P=name)` and `$n` to those
names, and routing `group(n)` / `span(n)` / `findall` through the mapping. That is a design
change to the pattern/match boundary rather than a patch, so it is registered as **P0-17** and
the affected probe lines are excluded with a comment instead of being masked.

Also registered: **P1-10** — an `re.error` raised from native code reports
`type(e).__name__ == 'ValueError'`, and lacks `.pos` / `.msg` / `.lineno` / `.pattern`, while
CPython's `re.error` is `error → Exception` (not a `ValueError` subclass) and carries them.
`re.error` as a class is already correct in SharpPy, so the loss happens in the hybrid
exception wrapper. Message text matches.

## Verification

- `tests/bughunt/p46_regex_python_forms.py`: 27 lines, stdout diff **0**; three lines excluded
  and labelled (two P0-17 numbering cases, one P1-10 error-identity case).
- Supporting matrices: `.artifacts/tmp/p2re2.py`, `p2sub.py`, `p2mix.py`, `p2grp.py`, `p2err.py`.
  Probe authoring note: the first attempt was written with `printf` and GNU printf turned `\\1`
  into an octal `0x01` byte, which made CPython itself report `'\x01 c d'`; the scripts are now
  written with the file-writing tool, as the hygiene rule requires.
- `tools/run_bughunt.py`: **51/51 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: no flags (`int_loop +0.5 %`, `str_fmt +2.1 %`, `list_comp +0.9 %`,
  `dict_ops −1.0 %`).
- Godot replay not re-run: the change is confined to `modules/_SreModule.cs` (no core, type,
  attribute, VM or compile semantics), which is the boundary where the replay protocol applies.
