#!/usr/bin/env python3
"""
p74: PEP 654 exception groups.

CPython implements the whole group in Objects/exceptions.c: BaseExceptionGroup_new decides the
argument-error family and whether BaseExceptionGroup(...) is reported as an ExceptionGroup,
exceptiongroup_split_recursive is the one matcher used by subgroup(), split() and by the
interpreter's except* dispatch, and _PyExc_PrepReraiseStar rebuilds what the handlers re-raised.
Python/ceval.c then matches a clause by calling exc.split(match_type) as an ordinary attribute
lookup.

Before this probe the interpreter had three weaker copies of that logic (a CLR-type matcher in
the VM, a CLR-name matcher in the group class, a second constructor in two different files), so
codecs... no, so groups lost their members: repr showed no sub-exceptions, .message was missing,
split() did not exist, and the VM invented a group with the message "unhandled exceptions".

Deliberately NOT pinned here, because they stay open in docs/known_gaps.md: the group traceback
layout of an unhandled group (this interpreter prints a plain traceback, CPython prints
"+ Exception Group Traceback" with sub-exception blocks), the stream the traceback is written
to, __notes__/add_note, and KeyboardInterrupt / SystemExit / EncodingWarning, which this
interpreter does not define as builtins yet - so the BaseException-only half of the group rules
cannot be exercised from Python.
"""


def show(label, fn):
    try:
        value = fn()
    except BaseException as exc:
        value = f"{type(exc).__name__}: {exc}"
    print(f"{label:<50} -> {ascii(value)}")


print("=== construction and the argument error family ===")
g = ExceptionGroup("boom", [ValueError("a"), TypeError("b")])
show("repr", lambda: repr(g))
show("message", lambda: g.message)
show("exceptions", lambda: g.exceptions)
show("args", lambda: g.args)
show("str", lambda: str(g))
show("type name", lambda: type(g).__name__)
show("isinstance BaseExceptionGroup", lambda: isinstance(g, BaseExceptionGroup))
show("issubclass ExceptionGroup<:BaseExceptionGroup",
     lambda: issubclass(ExceptionGroup, BaseExceptionGroup))
show("issubclass ExceptionGroup<:Exception", lambda: issubclass(ExceptionGroup, Exception))
show("ExceptionGroup()", lambda: ExceptionGroup())
show("ExceptionGroup('m')", lambda: ExceptionGroup("m"))
show("ExceptionGroup('m', [ValueError()], 3)", lambda: ExceptionGroup("m", [ValueError()], 3))
show("ExceptionGroup(5, [ValueError()])", lambda: ExceptionGroup(5, [ValueError()]))
show("ExceptionGroup('m', 7)", lambda: ExceptionGroup("m", 7))
show("ExceptionGroup('m', [])", lambda: ExceptionGroup("m", []))
show("ExceptionGroup('m', ['x'])", lambda: ExceptionGroup("m", ["x"]))
show("ExceptionGroup('m', [ValueError(), 'x'])", lambda: ExceptionGroup("m", [ValueError(), "x"]))
show("ExceptionGroup('m', 'ab')", lambda: ExceptionGroup("m", "ab"))
show("BaseExceptionGroup('m', ())", lambda: BaseExceptionGroup("m", ()))
show("tuple member accepted", lambda: repr(BaseExceptionGroup("m", (ValueError("v"),))))
show("generator member", lambda: repr(ExceptionGroup("m", (e for e in [ValueError("g")]))))
show("message is read-only", lambda: setattr(g, "message", "other"))
show("exceptions is read-only", lambda: setattr(g, "exceptions", ()))
show("group of group", lambda: repr(ExceptionGroup("outer", [ExceptionGroup("in", [KeyError("k")])])))


def one_base():
    # BaseExceptionGroup with only Exception members is reported as ExceptionGroup.
    built = BaseExceptionGroup("m", [ValueError("v")])
    return (type(built).__name__, repr(built))


show("BaseExceptionGroup upgrades", one_base)

print("=== derive keeps the message and takes new members ===")
show("derive repr", lambda: repr(g.derive([KeyError("k")])))
show("derive type", lambda: type(g.derive([KeyError("k")])).__name__)
show("derive message", lambda: g.derive([KeyError("k")]).message)
show("derive arity", lambda: g.derive())
show("derive bad members", lambda: g.derive([1]))

print("=== split and subgroup ===")
g2 = ExceptionGroup("g", [ValueError("a"), KeyError("k")])
match, rest = g2.split(ValueError)
show("split match", lambda: repr(match))
show("split rest", lambda: repr(rest))
show("split no match", lambda: tuple(repr(x) for x in g2.split(RuntimeError)))
show("split all match", lambda: tuple(repr(x) for x in g2.split((ValueError, KeyError))))
show("subgroup", lambda: repr(g2.subgroup(ValueError)))
show("subgroup none", lambda: g2.subgroup(RuntimeError))
show("split callable predicate", lambda: tuple(repr(x) for x in g2.split(lambda e: isinstance(e, KeyError))))
show("split bad matcher", lambda: g2.split(5))
show("split arity", lambda: g2.split())
show("subgroup arity", lambda: g2.subgroup(ValueError, KeyError))

nested = ExceptionGroup("outer", [
    ExceptionGroup("inner", [KeyError("k"), ValueError("v")]),
    RuntimeError("r"),
])
show("nested repr", lambda: repr(nested))
show("nested split match", lambda: repr(nested.split(ValueError)[0]))
show("nested split rest", lambda: repr(nested.split(ValueError)[1]))
show("nested subgroup", lambda: repr(nested.subgroup((KeyError, RuntimeError))))


def predicate_sees_members():
    seen = []

    def note(exc):
        seen.append(type(exc).__name__)
        return False

    nested.split(note)
    return seen


show("predicate sees every leaf", predicate_sees_members)

print("=== except* dispatch ===")


def collect(pattern, excs):
    out = []
    try:
        raise ExceptionGroup("g", excs)
    except* pattern as eg:
        out.append(("caught", type(eg).__name__, repr(eg),
                    [type(x).__name__ for x in eg.exceptions]))
    except* BaseException as eg:
        out.append(("other", type(eg).__name__, repr(eg),
                    [type(x).__name__ for x in eg.exceptions]))
    return out


show("except* ValueError over [V,K]", lambda: collect(ValueError, [ValueError("a"), KeyError("k")]))
show("except* KeyError over [V,K]", lambda: collect(KeyError, [ValueError("a"), KeyError("k")]))
show("except* Exception over [V]", lambda: collect(Exception, [ValueError("a")]))
show("except* ValueError over [V,V]",
     lambda: collect(ValueError, [ValueError("a"), ValueError("b")]))
show("except* tuple over [V,K]",
     lambda: collect((ValueError, KeyError), [ValueError("a"), KeyError("k")]))


def three_handlers():
    out = []
    try:
        raise ExceptionGroup("g", [ValueError("a"), KeyError("k"), RuntimeError("r")])
    except* ValueError as eg:
        out.append(("V", repr(eg)))
    except* KeyError as eg:
        out.append(("K", repr(eg)))
    except* RuntimeError as eg:
        out.append(("R", repr(eg)))
    return out


show("three handlers", three_handlers)


def unmatched_escapes():
    out = []
    try:
        try:
            raise ExceptionGroup("g", [ValueError("a"), KeyError("k")])
        except* KeyError as eg:
            out.append(("K", repr(eg)))
    except* ValueError as eg:
        out.append(("V-escaped", repr(eg)))
    return out


show("unmatched half re-raises", unmatched_escapes)


def nested_group_handling():
    out = []
    inner = ExceptionGroup("inner", [KeyError("k"), ValueError("v")])
    try:
        raise ExceptionGroup("outer", [inner, RuntimeError("r")])
    except* ValueError as eg:
        out.append(("V", repr(eg)))
    except* RuntimeError as eg:
        out.append(("R", repr(eg)))
    return out


show("nested group handling", nested_group_handling)


def handler_then_finally():
    log = []
    try:
        raise ExceptionGroup("g", [ValueError("a")])
    except* ValueError as eg:
        log.append("handler")
    finally:
        log.append("finally")
    return log


show("handler then finally", handler_then_finally)


def naked_exception():
    out = []
    try:
        raise ValueError("plain")
    except* ValueError as eg:
        out.append((type(eg).__name__, repr(eg), [repr(x) for x in eg.exceptions]))
    return out


show("except* wraps a naked exception", naked_exception)


def naked_then_raise_other():
    out = []
    try:
        try:
            raise ValueError("plain")
        except* ValueError as eg:
            out.append(("caught", repr(eg)))
            raise KeyError("fresh")
    except* KeyError as eg:
        out.append(("fresh", repr(eg)))
    return out


show("naked catch then new raise", naked_then_raise_other)


def bare_except_star():
    out = []
    try:
        raise ExceptionGroup("g", [ValueError("a")])
    except* Exception as eg:
        out.append(repr(eg))
    return out


show("except* Exception", bare_except_star)


def handled_is_the_matched_part():
    seen = []
    try:
        raise ExceptionGroup("g", [ValueError("a"), KeyError("k")])
    except* ValueError as eg:
        # CPython: PyErr_SetHandledException(match) - sys.exc_info() inside the clause is the
        # matched part, not the group that arrived.
        import sys
        seen.append(repr(sys.exc_info()[1]))
    return seen


show("sys.exc_info inside clause", handled_is_the_matched_part)

print("=== raising through a handler ===")


def reraise_original():
    out = []
    try:
        raise ExceptionGroup("g", [ValueError("a"), KeyError("k")])
    except* ValueError as eg:
        raise eg
    except* Exception as eg:
        out.append(("outer", repr(eg)))
    return out


show("handler re-raises its group", reraise_original)


def else_clause():
    out = []
    try:
        pass
    except* ValueError as eg:
        out.append("handler")
    else:
        out.append("else")
    return out


show("else when nothing raised", else_clause)


def invalid_match_type():
    out = []
    try:
        try:
            raise ExceptionGroup("g", [ValueError("a")])
        except* 5 as eg:
            out.append("handler")
    except TypeError as te:
        out.append(f"TypeError: {te}")
    return out


show("except* 5", invalid_match_type)

print("=== split is overridable, because ceval calls it ===")
# A subclass of ExceptionGroup is NOT pinned here: constructing one in this interpreter yields an
# ExceptionGroup, not the user class, so the overridden split() is never consulted. That is the
# builtin-subclass identity gap tracked in docs/known_gaps.md, not something this probe can pin
# without freezing a known wrong answer.
print("DONE")
