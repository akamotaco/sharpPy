"""Close lookup errors: unraisable hook payload, dispatch, and cleanup order."""
import sys

events = []
records = []
original_hook = sys.unraisablehook
original_stderr = sys.stderr
print("original-hook", original_hook is sys.__unraisablehook__)

class BrokenLookup:
    def __iter__(self): return self
    def __await__(self): return self
    def __next__(self): return "paused"
    def __repr__(self): return "<delegate>"
    @property
    def close(self):
        raise ValueError("lookup error")

async def coroutine(obj):
    try:
        await obj
    except GeneratorExit:
        events.append("coroutine exit")
        raise
    finally:
        events.append("coroutine finally")

def generator(obj):
    try:
        yield from obj
    except GeneratorExit:
        events.append("generator exit")
        raise
    finally:
        events.append("generator finally")

def record(args):
    records.append(args)
    events.append("hook")
    print("payload", type(args).__name__, isinstance(args, tuple), len(args), args.err_msg)
    print("exception", args.exc_type is ValueError, args.exc_value.args)
    print("target", args.object is obj, args[4] is obj)
    print("traceback", args.exc_traceback is args.exc_value.__traceback__)
    print("frame", args.exc_traceback.tb_frame.f_code.co_name)
    print("handled", sys.exception() is ambient)

sys.unraisablehook = record
try:
    raise RuntimeError("ambient")
except RuntimeError as ambient:
    for factory in (coroutine, generator):
        events.clear()
        obj = BrokenLookup()
        c = factory(obj)
        print(c.send(None))
        print("close", c.close())
        print(events)
        print("ambient-preserved", sys.exception() is ambient)

args = records[0]
for action in ("set", "delete"):
    try:
        if action == "set": args.object = None
        else: del args.exc_value
    except AttributeError as error:
        print("readonly", str(error))

class Sink:
    def __init__(self): self.parts = []
    def write(self, value): self.parts.append(value)
    def flush(self): pass
    def text(self): return "".join(self.parts)

sink = Sink()
sys.stderr = sink
info = type(args)((ValueError, ValueError("message"), None, None, "target"))
print("default-result", sys.__unraisablehook__(info))
print("default-text", repr(sink.text()))
print("args-repr", repr(type(args)((None,) * 5)))
for values, kwargs in (((), {}), ((None,), {}), ((None, None), {}), ((), {"x": 1})):
    try: sys.__unraisablehook__(*values, **kwargs)
    except TypeError as error: print("hook-arguments", str(error))

class FailingHook:
    def __repr__(self): return "<hook>"
    def __call__(self, args):
        raise RuntimeError("hook failed")

for mode in ("none", "missing", "failing", "not-callable"):
    events.clear()
    sink.parts.clear()
    if mode == "missing": sys.__dict__.pop("unraisablehook")
    elif mode == "none": sys.unraisablehook = None
    elif mode == "failing": sys.unraisablehook = FailingHook()
    else: sys.unraisablehook = 17
    c = coroutine(BrokenLookup())
    c.send(None)
    print(mode, c.close(), events)
    lines = sink.text().splitlines()
    print("fallback", lines[0], lines[-1])

class FailingSink:
    def write(self, value): raise ValueError("write failed")
    def flush(self): raise ValueError("flush failed")
sys.stderr = FailingSink()
sys.unraisablehook = None
c = coroutine(BrokenLookup())
c.send(None)
print("broken-stderr", c.close())
try: sys.__unraisablehook__(info)
except ValueError as error: print("direct-write-error", str(error))
sys.stderr = None
c = coroutine(BrokenLookup())
c.send(None)
print("disabled-stderr", c.close())
sys.stderr = original_stderr
sys.unraisablehook = original_hook
T = type(args)
print("field-counts", T.n_fields, T.n_sequence_fields, T.n_unnamed_fields)
for positional, keywords in (((), {}), (((None,) * 4,), {}), (((None,) * 6,), {}), ((1,), {}), (((None,) * 5, None), {}), (((None,) * 5,), {"bad": 1}), (((None,) * 5,), {"sequence": (None,) * 5})):
    try: T(*positional, **keywords)
    except TypeError as error: print("constructor-error", str(error))
print("constructor-keywords", T(sequence=(None,) * 5, dict={}) == (None,) * 5)

class BadRepr:
    def __repr__(self): raise ValueError("repr failed")
class BadStr:
    def __str__(self): raise ValueError("str failed")
sink.parts.clear()
sys.stderr = sink
sys.__unraisablehook__(T((ValueError, BadStr(), None, "message", BadRepr())))
print("format-failures", repr(sink.text()))
class CustomError(Exception):
    pass
CustomError.__module__ = "custom"
sink.parts.clear()
sys.__unraisablehook__(T((CustomError, None, None, "custom message", None)))
print("custom-exception-name", repr(sink.text()))
sys.stderr = original_stderr

class MissingClose(BrokenLookup):
    @property
    def close(self): raise AttributeError("missing")
class FailingClose(BrokenLookup):
    def close(self): raise ValueError("call error")
sys.unraisablehook = lambda error: events.append("unexpected hook")
for factory in (coroutine, generator):
    for target in (MissingClose(), FailingClose()):
        events.clear()
        c = factory(target)
        c.send(None)
        try: print("other-close", c.close())
        except ValueError as error: print("other-close-error", str(error))
        print(events)
sys.unraisablehook = original_hook
print("unraisable close regression complete")
