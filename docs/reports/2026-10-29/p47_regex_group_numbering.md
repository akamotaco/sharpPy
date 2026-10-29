# p47 — capture-group numbering: the compiler owns it, not the engine

Date: 2026-10-29
Branch: `work/bug-hunt`
Closes: P0-17 (registered in p46)

## The question I got wrong, and the answer the source already gave

I framed P0-17 as needing a design decision. It did not. Two facts settle it:

1. **CPython's structure dictates the split.** `re/_parser.py` assigns a group number at each
   capturing `(`, and `_compiler.py` emits `GROUPREF n`/`MARK n` with that number resolved.
   `_sre.c` never re-derives numbering from pattern text; it consumes resolved opcodes.
2. **SharpPy already builds the right metadata and discards it.** Measured identical in both
   engines for `(?P<a>\w) (\w) (?P=a) \2`:
   ```
   ngroups    2
   groupindex {'a': 1}
   indexgroup (None, 'a', None)
   ```
   `Lib/re/_compiler.py` passes those to `_sre.compile(pattern, flags, code, ngroups,
   groupindex, indexgroup)`, and native `Compile(PyObject[] args)` used only `args[0]` and
   `args[1]`, handing the raw Python pattern to `System.Text.RegularExpressions`.

So P0-17 was not "pick a design"; it was "stop throwing away the authoritative input". The only
C#-specific rule needed: **engine-assigned ids are not observable identity, so pin numbering into
group names**, because .NET assigns capture ids with the *unnamed* groups first while CPython uses
purely positional order.

## Measured divergence (before)

```
re.findall(r"(?P<a>\w)(\w)", "ab")      CPython [('a','b')]   SharpPy [('b','a')]
m = re.match(r"(?P<a>\w) (\w)", "a b")
m.span(1)                               CPython (0,1)         SharpPy (2,3)
m.span(2)                               CPython (2,3)         SharpPy (0,1)
m.group("a")                            'a'                   'a'     (names were fine)
re.findall(r"(?P<a>\w) (\w) (?P=a) \2", "a b a b")
                                        CPython [('a','b')]   SharpPy []      # stops matching
re.findall(r"(?P<a>\w) (\w) (\w) (?P=a) \2 \3", "a b c a b c")
                                        CPython [('a','b','c')] SharpPy []
```

## Implementation

* `ToCanonicalPattern(pattern, out names, out count)` — one pass over the pattern that renames
  every capturing group to `__p<N>` where **N is the CPython positional number**, and rewrites
  `\1..\99` and `(?P=name)` into `\k<__pN>`. `(?P=name)` is deferred to a second pass because the
  name may be declared later. Character classes are copied verbatim (a `(` inside `[...]` is
  literal), escapes are copied in pairs, and `(?:`, `(?=`, `(?!`, `(?<=`, `(?<!`, `(?i:` are
  non-capturing. Unsupported constructs — `(?(id)yes|no)` conditionals, `(?#comment)`, an
  unterminated class — return `null`, and the caller keeps the plain translation rather than
  reporting wrong groups.
* `BuildRegex` is the single decision point: canonical renaming only when the pattern actually
  contains `(?P`. With no named groups .NET's numbering and CPython's already agree, which keeps
  the blast radius at exactly the divergent class.
* `PySrePattern` keeps `names`/`count` next to the compiled `Regex` and hands them to every
  `PySreMatch`. `group(n)`, `span(n)`, `start()`, `end()`, `groups()`, `groupdict()`, `findall()`
  and `ToDotNetReplacement` resolve through the canonical name, so `.NET` capture ids are never
  consulted.
* Two CPython details surfaced by the edge matrix and fixed:
  * `group(0)` is the whole match and has **no** canonical name (it was returning `''`);
  * a group that did not participate yields `None`, not `''` (`match_group_impl`), and an unknown
    name raises `IndexError: no such group` (the previous text quoted the name).
  `span(n)` for a non-participating group gives `(-1, -1)` for free, because .NET reports index
  `-1`.

## Verification

- `tests/bughunt/p47_regex_group_numbering.py`: 43 lines, stdout diff **0** — 24 scanner edge
  cases (paren inside a character class, nested groups, captures inside lookaround, escaped
  parens, an 11-group pattern using `\10`/`\11`, inline flags, `(?#comment)`, conditionals,
  `split()` with a named group, mixed `\2`/`\g<a>` templates, `groups()`/`groupdict()`/`start()`/
  `end()`, unknown name, out-of-range number) plus the numbering spot checks and mixed
  named/unnamed references.
- `tests/bughunt/p46_regex_python_forms.py`: the three lines previously excluded *because* of
  P0-17 are restored; the probe is now 29 lines with diff **0** (one line still excluded for
  P1-10, the native `re.error` type name).
- `tools/run_bughunt.py`: **52/52 matching**.
- `tools/evaluate.py`: unchanged `{"matching_output": 225,
  "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`.
- `tools/perf_probe.py`: see the ledger caveat. It flagged `str_fmt +30 %` / `list_comp +35 %`
  consistently, so I separated it properly instead of calling it noise: an isolated `%` loop run
  against both binaries alternately five times each gave means 0.1235 vs 0.1228, and the whole
  `bench_suite.py` total was median 2.900 vs 2.869 s (min 2.788 vs 2.800). No effect. The suite
  never imports `re`, and the diff is confined to `modules/_SreModule.cs`.
- Godot replay not re-run (module-local change; no core/type/attribute/VM/compile semantics).

## Residual

- **P1-10**: a native `re.error` still reports `__name__ == 'ValueError'` and lacks
  `.pos`/`.msg`/`.lineno`/`.pattern`.
- The scanner deliberately bails on conditionals and comments instead of guessing; those patterns
  keep the old translation, which matches CPython on the cases probed but is not canonicalized.
- `_sre.compile` still receives `code`/`ngroups`/`groupindex`/`indexgroup` without validating
  them. The canonical scan currently re-derives numbering from the pattern instead of consuming
  the compiler's numbers; wiring it to *trust* `ngroups`/`groupindex` (and falling back when they
  disagree) is the natural follow-up and would remove the scanner's last assumption.
