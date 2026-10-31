# P0-59 ~ P0-63 measurement (registered in docs/known_gaps.md, not fixed).
# Run from the repository root under both interpreters:
#   C:/ProgramData/miniforge3/envs/sharppy-test-py312/python.exe  docs/plans/lifetime_foundation/probes/gap_l2b_measure.py
#   <dotnet> bin/Release/net8.0/sharppy.dll                       docs/plans/lifetime_foundation/probes/gap_l2b_measure.py
# stdout only; the point is the line-by-line difference, so every value is printed with ascii().

import sys

print("interpreter:", sys.version.split()[0])


class Sub(ValueError):
    pass


print("a) repr:", ascii(repr(Sub())), ascii(repr(Sub("x"))), ascii(repr(ValueError("y"))))

e = ValueError("boom")
try:
    e.note = 1
    print("b) attribute set:", ascii(getattr(e, "note")))
except Exception as exc:
    print("b) attribute set:", type(exc).__name__, ascii(str(exc)))

print("c) __doc__:", ascii(ValueError.__doc__), ascii(OSError.__doc__))

try:
    import functools

    pm = functools.partialmethod(len, [1])
    print("d) partialmethod __get__:", ascii(hasattr(pm, "__get__")),
          ascii(functools.partialmethod.__dict__.get("__get__") is not None))
except Exception as exc:
    print("d) partialmethod:", type(exc).__name__, ascii(str(exc)))

try:
    import pickle

    print("e) pickle:", ascii(pickle.dumps([1, 2])))
except Exception as exc:
    print("e) pickle:", type(exc).__name__, ascii(str(exc)))
