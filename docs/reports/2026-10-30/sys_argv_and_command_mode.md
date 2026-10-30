# P0-39: sys.argv reaches Python, and `-c` actually runs something

Date 2026-10-30, branch `work/bug-hunt`, base `b9da790`.
Found while benchmarking P0-38: a workload selector read `sys.argv[1]` and SharpPy answered `[]`.

## Measured start state

`script.py one two` under the pinned reference and under SharpPy:

```
CPython 3.12.15   3  ['.artifacts/tmp/lockcase/argvchk.py', 'one', 'two']
SharpPy           0  []
```

Two separate defects in one place: `sys.argv` was hardcoded to an empty list
(`modules/SysModule.cs:64` "명령줄 인수 (빈 리스트로 초기화)"), and `Program.cs` parsed `-c` but its
`ExecuteCodeString` call was commented out with a stale four-argument signature, so
`sharppy.dll -c "print(123)"` printed nothing and exited 0.

## CPython's rule, from the pinned source

`sys.argv` is not built by the runtime; it is a copy of what the host configured:

```
Python/sysmodule.c:3612      COPY_LIST("argv", config->argv)
Include/cpython/initconfig.h:156-158   parse_argv / orig_argv / argv in PyConfig
Python/initconfig.c:2723     config_update_argv(config, opt_index)
```

`config_update_argv` is the interesting one, and it explains every shape that looks arbitrary:

- it copies the command line **starting at the index where option parsing stopped**, so interpreter
  options never reach `sys.argv`, and `argv[0]` is the script path exactly as typed — not absolute,
  not separator-normalised;
- if that slice is empty it appends `L""`, with the comment *"Ensure at least one (empty) argument is
  seen"* — the origin of `sys.argv == ['']` for a bare interpreter;
- it forces `argv[0] = L"-c"` for command mode (*"Force sys.argv[0] = '-c'"*) and `L"-m"` for module
  mode.

Since `COPY_LIST` makes `sys.argv` a copy of the host's config, an embedding that sets nothing gets
an empty list. That is why the SharpPy default stayed an empty list rather than `['']`: SharpPy
inside Godot is that embedding, and inventing arguments the host never passed would be a guess.

## What changed

- `core/PyArgv.cs` (new) — the SharpPy counterpart of `PyConfig.argv`, with the measured table in its
  header and three constructors that mirror `config_update_argv`: `ForScript` (slice from the script),
  `ForCommand` (forced `"-c"` + operands), `SetEmptyArg` (the "ensure at least one" rule for the REPL).
- `core/CommandLineParser.cs` — returns a `CommandLine` with `PythonFile`, `ScriptArgs`,
  `CodeString`, `CodeArgs`. The important part is positional, mirroring the reference: once the
  script is known, option parsing is over, so nothing downstream may swallow the remaining arguments.
  Previously the loop dropped them silently (`demo`-style commands were recorded into a dead
  `options["command"]` and everything else vanished).
- `Program.cs` — sets `PyArgv` on every path and **enables `-c`** through the current
  `ExecuteCodeString(string)` signature.
- `core/FileExecutor.cs` — `python -c ""` is the empty program and must exit 0 (measured); only a
  missing operand is an error, and the parser cannot produce one.
- `modules/SysModule.cs` — `sys.argv` is built from `PyArgv`, with the `COPY_LIST` citation.
- `tools/argv_matrix.py` (new) — the script harness cannot express *how* the interpreter was started,
  so command-line shapes get their own comparison tool with the measured cases; exit code is the
  number of mismatches, and registered gaps are excluded from it.
- `tests/bughunt/p78_command_line.py` (new, 18 lines, diff 0) — pins what the harness can pin:
  `len(sys.argv) == 1`, argv[0] basename is the probe, argv is a list of str, and it is mutable and
  stable across attribute lookups.

## Result of tools/argv_matrix.py

```
OK   script with args                   rc 0/0
OK   script no args                     rc 0/0
OK   interpreter flag before script     rc 0/0
OK   path with backslashes              rc 0/0
OK   -c code                            rc 0/0
OK   -c code with args                  rc 0/0
OK   -c empty program                   rc 0/0
GAP  dash reads stdin                   rc 0/0
GAP  bare interpreter on stdin          rc 0/0
GAP  -c without operand                 rc 2/0
mismatches: 0 (3 registered gaps excluded)
```

The three gaps are stdin-script behaviour and usage diagnostics, registered as P0-41 rather than
half-implemented here:

- `python - one` runs stdin as a script with `argv == ['-','one']`; SharpPy starts the REPL, whose
  banner and `>>> ` echoes also make `python < file` differ even though `sys.argv == ['']` is
  correct inside it (verified directly).
- `python -c` with no operand prints `Argument expected for the -c option` plus usage and exits 2;
  SharpPy starts the REPL with exit 0.
- The usage text itself contains the program name, so reproducing it is a separate decision.

## Gates

- build: 0 errors, 0 warnings
- `tools/run_bughunt.py`: **83/83 matching**
- `tools/evaluate.py --timeout 90`: `{"matching_output": 225, "matching_except_volatile_fields": 3,
  "self_test_no_reference": 2}` — one run with the default 30 s limit reported `sharppy_timeout: 1`;
  evaluate always invokes `[dotnet, dll, script]` with exactly one argument, so this change cannot
  alter those executions, and the timeout did not recur at 90 s. It is recorded as a limit flake, not
  as a pass.
- Godot 4.7.2 mono source build + recorded replay: **82 matched, 0 other** (argv under Godot stays at
  the empty default, which is what an embedding that sets no `PyConfig.argv` produces)

## Related but not fixed

`sys.executable`, `-m` module execution (`Program.cs` still has the branch commented out), and the
flag set SharpPy accepts (`-u`, `-O`, `-B` and friends are unknown to it and are skipped rather than
rejected) were not touched. Only the position rule that `sys.argv` observes was mirrored, which is
stated in `core/CommandLineParser.cs` so nobody mistakes it for the full option grammar.
