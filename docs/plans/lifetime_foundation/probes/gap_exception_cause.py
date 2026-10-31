# P0-65 measurement (registered in docs/known_gaps.md, not fixed).
# Run from the repository root under both interpreters:
#   C:/ProgramData/miniforge3/envs/sharppy-test-py312/python.exe  docs/plans/lifetime_foundation/probes/gap_exception_cause.py
#   <dotnet> bin/Release/net8.0/sharppy.dll                       docs/plans/lifetime_foundation/probes/gap_exception_cause.py
# Not an acceptance probe: it measures a gap that is still open, so the two interpreters
# disagree. Every value goes through ascii() so the diff helper cannot die on cp949.
#
# What is NOT in here, because it already matches: `raise ... from` (cause, context,
# __suppress_context__ after the raise), and the refusal message for
# `e.__cause__ = 5`. Checked with the same command.

# Assigning __cause__ must also set __suppress_context__ (PyException_SetCause,
# Objects/exceptions.c:399 - base_self->suppress_context = 1 at :402).
e = TypeError("t")
e.__cause__ = ValueError("c")
print("suppress after assign:", ascii(e.__suppress_context__), ascii(type(e.__cause__).__name__))

# Assigning None goes through the same setter, so it suppresses too.
e2 = TypeError("t2")
e2.__cause__ = None
print("suppress after None:", ascii(e2.__suppress_context__), ascii(repr(e2.__cause__)))

# Deleting raises TypeError in CPython (BaseException_set_cause, Objects/exceptions.c:344,
# "may not be deleted" at :347 and the __context__ twin further down the getset table).
for name in ("__cause__", "__context__"):
    exc = ValueError("del me")
    try:
        delexc = None
        exec("del exc." + name)
        print("del", name, "-> no error")
    except Exception as caught:
        print("del", name, "->", type(caught).__name__, ascii(str(caught)))

# The refusing shape, for the record: this one already agrees.
try:
    TypeError("x").__cause__ = 5
except TypeError as caught:
    print("int cause:", ascii(str(caught)))

# __context__ assignment itself agrees.
e3 = ValueError("p3")
e3.__context__ = IndexError("c")
print("context assign:", ascii(type(e3.__context__).__name__))
