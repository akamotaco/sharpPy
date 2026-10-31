# L2.4 measurement, outside the acceptance criteria: the object raise made is not the object except
# binds. Expected mismatch - known_gaps P0-70. Everything that looks right here is the CLR wrapper
# forwarding to OriginalInstance; identity is where the wrapper shows.
class Z(Exception):
    pass


z = Z("boom")
for kind in ("Z", "Exception", "BaseException"):
    try:
        raise z
    except eval(kind) as e:
        print(kind, "catch:", e is z, type(e).__name__, repr(e), e.args)

w = Z("keep")
w.extra = 5
try:
    raise w
except Z as e2:
    print("attribute through the wrapper:", e2.extra, e2 is w)

try:
    raise w
except Z as e3:
    e3.__traceback__ = None
    print("traceback write then identity:", e3 is w)
