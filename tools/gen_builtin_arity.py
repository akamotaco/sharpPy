"""Generate core/BuiltinArity.g.cs from the running CPython 3.12.

A C method's arity errors come from its calling convention, not from the method: METH_NOARGS
and METH_O (Objects/descrobject.c method_vectorcall_NOARGS/_O), the argument clinic's
_PyArg_CheckPositional and _PyArg_UnpackKeywords, and PyArg_ParseTuple (Python/getargs.c).
ml_flags and the clinic parser are not visible from Python, so each method of a builtin type
is called with 0..7 positional arguments, and with a keyword, and the convention whose message
formula reproduces every answer is recorded. A method no formula fits is left out.

Keys are "<type>.<method>"; values are the convention, min and max positional arguments, and
for the clinic's keyword parser the parameter names, how many are positional-only, and how
many keyword-only ones are required.

usage: python tools/gen_builtin_arity.py   (run with CPython 3.12.15)
"""
import builtins
import inspect
import os
import re
import sys

MAXN = 7


def plural(n):
    return "" if n == 1 else "s"


def sample(t):
    special = {
        bytes: b"ab", bytearray: bytearray(b"ab"), str: "ab", int: 5, float: 1.5, complex: 1j,
        list: [1], tuple: (1,), dict: {1: 2}, set: {1}, frozenset: frozenset([1]),
        range: range(3), slice: slice(1), memoryview: memoryview(b"ab"), bool: True,
        property: property(), staticmethod: staticmethod(len), classmethod: classmethod(len),
        type: int, object: object(), super: super(int, 5), enumerate: enumerate([]),
        reversed: reversed("ab"), zip: zip(), map: map(len, []), filter: filter(None, []),
    }
    if t in special:
        return special[t]
    if issubclass(t, BaseException):
        try:
            return t("x")
        except Exception:
            return None
    try:
        return t()
    except Exception:
        return None


def answer(func, args, kwargs):
    try:
        func(*args, **kwargs)
    except TypeError as e:
        return str(e)
    except Exception:
        return None
    return None


# --- the message formulas, one per convention -----------------------------------------------

def noargs(q, n, kw):
    if kw:
        return f"{q}() takes no keyword arguments"
    return f"{q}() takes no arguments ({n} given)" if n else None


def one(q, n, kw):
    if kw:
        return f"{q}() takes no keyword arguments"
    return f"{q}() takes exactly one argument ({n} given)" if n != 1 else None


def fast(name, n, kw, lo, hi, kwname=None):
    if kw:
        return f"{kwname or name}() takes no keyword arguments"
    if n < lo:
        return f"{name} expected {'' if lo == hi else 'at least '}{lo} argument{plural(lo)}, got {n}"
    if n > hi:
        return f"{name} expected {'' if lo == hi else 'at most '}{hi} argument{plural(hi)}, got {n}"
    return None


def parsetuple(name, n, kw, lo, hi, kwname=None):
    if kw:
        return f"{kwname or name}() takes no keyword arguments"
    if lo <= n <= hi:
        return None
    count = lo if n < lo else hi
    how = "exactly" if lo == hi else "at least" if n < lo else "at most"
    return f"{name}() takes {how} {count} argument{plural(count)} ({n} given)"


def unpack(name, n, kw, names, posonly, minpos, maxpos, minkw):
    """_PyArg_UnpackKeywords for n positional arguments and, if kw, one unknown keyword."""
    nkwargs = 1 if kw else 0
    maxargs = len(names)
    if maxargs == 0 and n + nkwargs > 0:
        return f"{name}() takes no arguments"
    if n + nkwargs > maxargs:
        return f"{name}() takes at most {maxargs} {'keyword ' if n == 0 else ''}argument{plural(maxargs)} ({n + nkwargs} given)"
    if n > maxpos:
        if maxpos == 0:
            return f"{name}() takes no positional arguments"
        return f"{name}() takes {'at most' if minpos < maxpos else 'exactly'} {maxpos} positional argument{plural(maxpos)} ({n} given)"
    minposonly = min(posonly, minpos)
    if n < minposonly:
        return f"{name}() takes {'at least' if minposonly < maxpos else 'exactly'} {minposonly} positional argument{plural(minposonly)} ({n} given)"
    reqlimit = maxpos + minkw if minkw else minpos
    for i in range(max(n, posonly), maxargs):
        if not nkwargs and i >= reqlimit:
            break
        if i < minpos or (maxpos <= i < reqlimit):
            return f"{name}() missing required argument '{names[i]}' (pos {i + 1})"
    if nkwargs:
        return f"'zzunknown' is an invalid keyword argument for {name}()"
    return None


# What an arity error of any convention looks like; any other TypeError is the body's.
ARITY = re.compile(r"takes (no|exactly|at least|at most) .*argument|expected .*argument.*, got \d+$"
                   r"|missing required argument|positional argument|keyword argument")


def observed(func):
    # The keyword goes in a direct call (vectorcall, method_vectorcall_*), the common path; a
    # call through **mapping reaches tp_call (cfunction_call), which names the method alone.
    return [answer(func, (None,) * n, {}) for n in range(MAXN + 1)], keyword_answer(func)


def keyword_answer(func):
    try:
        func(zzunknown=1)
    except TypeError as e:
        return str(e)
    except Exception:
        return None
    return None


def fits(formula, seen, seen_kw):
    """Arity messages must match the formula; other TypeErrors are the body's and only count
    where the formula itself expects no arity error."""
    for n, got in enumerate(seen):
        want = formula(n, False)
        if want is not None and got != want:
            return False
        if want is None and got is not None and ARITY.search(got):
            return False
    want = formula(0, True)
    return want is None or seen_kw == want


def bounds(seen, low, high):
    lo = 0
    while lo <= MAXN and seen[lo] is not None and low.search(seen[lo] or ""):
        lo += 1
    hi = MAXN
    while hi >= 0 and seen[hi] is not None and high.search(seen[hi] or ""):
        hi -= 1
    return lo, (hi if hi < MAXN else 10**6)


def classify(tname, mname, func):
    q = f"{tname}.{mname}"
    seen, seen_kw = observed(func)
    if fits(lambda n, kw: noargs(q, n, kw), seen, seen_kw):
        return ("NoArgs", 0, 0)
    if fits(lambda n, kw: one(q, n, kw), seen, seen_kw):
        return ("One", 1, 1)
    # The keyword refusal names the method alone, or with its type (method_check_args).
    lo, hi = bounds(seen, re.compile(r" expected .*argument.*, got \d+$"), re.compile(r" expected .*argument.*, got \d+$"))
    for kwname, flag in ((None, False), (q, True)):
        if fits(lambda n, kw: fast(mname, n, kw, lo, hi, kwname), seen, seen_kw):
            return ("Fast", lo, hi, flag)
    lo, hi = bounds(seen, re.compile(r"takes (exactly|at least) \d+ argument"), re.compile(r"takes (exactly|at most) \d+ argument"))
    for kwname, flag in ((None, False), (q, True)):
        if fits(lambda n, kw: parsetuple(mname, n, kw, lo, hi, kwname), seen, seen_kw):
            return ("ParseTuple", lo, hi, flag)
    try:
        sig = inspect.signature(func)
    except (TypeError, ValueError):
        return None
    params = list(sig.parameters.values())
    if any(p.kind in (p.VAR_POSITIONAL, p.VAR_KEYWORD) for p in params):
        return None
    names = [p.name for p in params]
    posonly = sum(p.kind == p.POSITIONAL_ONLY for p in params)
    positional = [p for p in params if p.kind in (p.POSITIONAL_ONLY, p.POSITIONAL_OR_KEYWORD)]
    maxpos = len(positional)
    minpos = sum(p.default is p.empty for p in positional)
    minkw = sum(p.kind == p.KEYWORD_ONLY and p.default is p.empty for p in params)
    if fits(lambda n, kw: unpack(mname, n, kw, names, posonly, minpos, maxpos, minkw), seen, seen_kw):
        return ("Defining", minpos, maxpos, names, posonly, minkw)
    return None


def module_sample(t):
    """A sample of a type of one of the C modules, or None where none can be made."""
    import _io, re, array, struct, itertools
    io_samples = {
        _io.TextIOWrapper: lambda: _io.TextIOWrapper(_io.BytesIO(b"ab")),
        _io.BufferedReader: lambda: _io.BufferedReader(_io.BytesIO(b"ab")),
        _io.BufferedWriter: lambda: _io.BufferedWriter(_io.BytesIO()),
        _io.BufferedRandom: lambda: _io.BufferedRandom(_io.BytesIO(b"ab")),
        _io.BufferedRWPair: lambda: _io.BufferedRWPair(_io.BytesIO(b"ab"), _io.BytesIO()),
        _io.BytesIO: lambda: _io.BytesIO(b"ab"), _io.StringIO: lambda: _io.StringIO("ab"),
        re.Pattern: lambda: re.compile("a"), re.Match: lambda: re.compile("a").match("a"),
        type(re.compile("").scanner("")): lambda: re.compile("a").scanner("a"),
        array.array: lambda: array.array("b", [1, 2]), struct.Struct: lambda: struct.Struct("b"),
        itertools.chain: lambda: itertools.chain([1]), itertools.repeat: lambda: itertools.repeat(1),
        itertools.islice: lambda: itertools.islice([1], 1), itertools.cycle: lambda: itertools.cycle([1]),
    }
    import _thread, functools, operator
    io_samples.update({
        _thread.LockType: _thread.allocate_lock, _thread.RLock: _thread.RLock,
        functools.partial: lambda: functools.partial(int),
        operator.itemgetter: lambda: operator.itemgetter(1), operator.attrgetter: lambda: operator.attrgetter("a"),
        operator.methodcaller: lambda: operator.methodcaller("a"),
        type((lambda: 0).__code__): lambda: (lambda: 0).__code__,
        type(iter([])): lambda: iter([]), type(reversed([])): lambda: reversed([]),
        type(iter(())): lambda: iter(()), type(iter("")): lambda: iter(""), type(iter({})): lambda: iter({}),
        type(iter(range(1))): lambda: iter(range(1)), type(iter(set())): lambda: iter(set()),
        type(iter(b"")): lambda: iter(b""), type(iter(bytearray())): lambda: iter(bytearray()),
        type({}.keys()): lambda: {1: 2}.keys(), type({}.values()): lambda: {1: 2}.values(), type({}.items()): lambda: {1: 2}.items(),
        BaseExceptionGroup: lambda: BaseExceptionGroup("m", [ValueError()]),
        ExceptionGroup: lambda: ExceptionGroup("m", [ValueError()]),
    })
    io_samples.update(unnamed_samples())
    make = io_samples.get(t)
    try:
        return make() if make else t()
    except Exception:
        return None


BLOCKING = {"acquire", "acquire_lock", "release", "release_lock", "_release_save", "_acquire_restore",
            "__enter__", "__exit__", "_at_fork_reinit"}


# The C modules whose types SharpPy has, and the types module's types that builtins does not name.
MODULES = ("_io", "re", "array", "_thread", "operator", "itertools", "collections", "functools", "_struct", "types")


def scanner_type():
    import re
    return type(re.compile("").scanner(""))


class _Forever:
    def __await__(self):
        while True:
            yield


def _generator():
    while True:
        yield


async def _coroutine():
    await _Forever()


async def _async_generator():
    yield


def _traceback():
    try:
        raise ValueError
    except ValueError as e:
        return e.__traceback__


class _Plain:
    def method(self):
        pass


def unnamed_samples():
    """Samples of the static types builtins does not name, keyed by their type."""
    import sys, types
    makers = [
        _generator, _coroutine, _async_generator, lambda: types.MappingProxyType({1: 2}), sys._getframe,
        lambda: iter(range(2 ** 70)), lambda: iter({1: 2}.items()), lambda: iter({1: 2}.values()),
        lambda: iter(lambda: 1, 2), lambda: len, lambda: _Plain().method, lambda: (1).__add__,
        lambda: type.__dict__["__dict__"] if False else int.__dict__["real"], lambda: str.__dict__["join"],
        lambda: int.__dict__["__add__"], lambda: sys, _traceback, lambda: list[int], lambda: types.SimpleNamespace(),
        sys.get_asyncgen_hooks, lambda: sys.flags, lambda: sys.float_info, lambda: sys.hash_info, lambda: sys.int_info,
        lambda: sys.thread_info, lambda: sys.version_info,
    ]
    if hasattr(sys, "getwindowsversion"):
        makers.append(sys.getwindowsversion)
    return {type(make()): make for make in makers}


def module_types():
    import importlib
    seen = set(v for v in vars(builtins).values() if isinstance(v, type))
    # Types builtins has but does not name, and the exception groups its sample() cannot make.
    for v in (type(iter([])), type(reversed([])), type(iter(())), type(iter("")), type(iter({})), type(iter(range(1))),
              type(iter(set())), type(iter(b"")), type(iter(bytearray())), type({}.keys()), type({}.values()),
              type({}.items()), BaseExceptionGroup, ExceptionGroup, scanner_type(), *unnamed_samples()):
        if v not in seen or v in (BaseExceptionGroup, ExceptionGroup):
            seen.add(v)
            yield v
    for modname in MODULES:
        for v in vars(importlib.import_module(modname)).values():
            if isinstance(v, type) and v not in seen and not issubclass(v, BaseException) and v.__module__ != "types":
                seen.add(v)
                yield v


def collect():
    entries = {}
    # A type of a module other than builtins is keyed by its module too, as PyArity.Of looks it up.
    for t in sorted(module_types(), key=lambda t: (t.__module__, t.__qualname__)):
        obj = module_sample(t)
        if obj is None or type(obj) is not t:
            continue
        key = t.__qualname__ if t.__module__ == "builtins" else f"{t.__module__}.{t.__qualname__}"
        for mname, value in vars(t).items():
            # A lock's acquiring methods would wait on the lock the previous call took.
            if type(value).__name__ != "method_descriptor" or mname in BLOCKING:
                continue
            # The descriptor itself, bound: a type's attribute can shadow its metatype's method.
            obj = module_sample(t)
            found = classify(t.__qualname__, mname, value.__get__(obj, t))
            if found:
                entries[f"{key}.{mname}"] = found
    for t in sorted((v for v in vars(builtins).values() if isinstance(v, type)), key=lambda t: t.__name__):
        obj = sample(t)
        for mname, value in vars(t).items():
            kind = type(value).__name__
            if kind == "method_descriptor":
                if obj is None:
                    continue
                func = value.__get__(obj, t)
            elif kind == "classmethod_descriptor" or (kind == "staticmethod" and mname != "__new__"):
                func = getattr(t, mname)
            else:
                continue
            # A method bound to a type is named by that type (meth_get__qualname__): int.mro.
            found = classify(obj.__name__ if isinstance(obj, type) and kind == "method_descriptor" else t.__name__, mname, func)
            if found:
                entries[f"{t.__name__}.{mname}"] = found
    return entries


def literal(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    if sys.version_info[:2] != (3, 12):
        raise SystemExit("run with CPython 3.12")
    entries = collect()
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    lines = [
        "// <auto-generated> tools/gen_builtin_arity.py from CPython " + sys.version.split()[0] + " </auto-generated>",
        "using System.Collections.Generic;",
        "",
        "namespace SharpPy",
        "{",
        "    internal static partial class PyArity",
        "    {",
        "        private static readonly Dictionary<string, Convention> Table = new()",
        "        {",
    ]
    for key in sorted(entries):
        kind, lo, hi, *rest = entries[key]
        hi = "int.MaxValue" if hi >= 10**6 else str(hi)
        if len(rest) == 3:
            names, posonly, minkw = rest
            array = "new[] { " + ", ".join(literal(n) for n in names) + " }" if names else "System.Array.Empty<string>()"
            extra = ", Names: " + array + ", " + f"PositionalOnly: {posonly}, RequiredKeywords: {minkw}"
        elif rest and rest[0]:
            extra = ", QualifiedKeywordRefusal: true"
        else:
            extra = ""
        lines.append(f"            [{literal(key)}] = new(PyCall.{kind}, {lo}, {hi}{extra}),")
    lines += ["        };", "    }", "}", ""]
    with open(os.path.join(root, "core", "BuiltinArity.g.cs"), "w", encoding="utf-8", newline="\n") as stream:
        stream.write("\n".join(lines))
    counts = {}
    for value in entries.values():
        counts[value[0]] = counts.get(value[0], 0) + 1
    print(len(entries), "entries", counts)


main()
