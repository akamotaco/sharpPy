using System;

namespace SharpPy;

/// <summary>
/// A Python-defined subclass of an exception type. CPython gives every exception object two slots in
/// its layout - <c>args</c> (Objects/exceptions.c:23, written by BaseException_new at :56) and, for
/// SystemExit, <c>code</c> (Objects/exceptions.c:674, written by SystemExit_init at :633). They are
/// reached through member descriptors, never through the instance dict, so a user
/// <c>__setattr__</c> is not called to build them, a read-only property in a subclass cannot block
/// construction, and <c>__dict__</c> stays empty.
///
/// PyClassInstance cannot grow by two fields: it is the instance type of every user class, and C1
/// measures the cost of that layout (H3). So the slots live on a CLR subclass that only exception
/// instances are allocated as, which is where CPython puts them too - in the object, not beside it.
/// </summary>
public class PyExceptionInstance : PyClassInstance
{
    /// <summary>BaseException.args. Kept as the tuple object Python observes, the way CPython keeps a
    /// tuple in the slot rather than recomputing it per read.</summary>
    internal PyObject ArgsSlot = PyTuple.Empty;

    /// <summary>SystemExit.code. A null field is an unset member; PyMember_GetOne reads a NULL
    /// T_OBJECT back as None (Objects/structmember.c), which is what `del e.code` leaves behind.</summary>
    internal PyObject CodeSlot;

    public PyExceptionInstance(PyClass instanceType) : base(instanceType) { }
}

/// <summary>
/// The single route to those two slots. Every reader - the <c>args</c> member descriptor,
/// BaseException_repr, BaseException_str, the exit handler - and every writer - BaseException_new,
/// BaseException_init, SystemExit_init, the member setters - goes through here, so "repr shows
/// something else than .args" cannot happen by picking a different source.
///
/// The two CLR branches are the C-to-C# boundary, not a per-call-site choice: a builtin exception
/// object has the fields on its own class, a Python subclass instance has them on PyExceptionInstance,
/// and the raise path may still hand back a wrapper around one of those (see PyException.OriginalInstance).
/// </summary>
internal static class PyExceptionSlots
{
    /// <summary>
    /// The instance a Python-visible exception name resolves to. The wrapper the raise path builds
    /// for a PyClassInstance is a transport object; CPython has only the instance.
    /// </summary>
    private static PyObject Owner(PyObject self)
        => self is PyException wrapper && wrapper.OriginalInstance != null
            ? wrapper.OriginalInstance
            : self;

    internal static PyObject[] ArgsOf(PyObject self)
    {
        var owner = Owner(self);
        if (owner is PyExceptionInstance slot)
            return slot.ArgsSlot is PyTuple tuple ? tuple.Items : Array.Empty<PyObject>();
        if (owner is PyBaseException native)
            return native.Args;
        return Array.Empty<PyObject>();
    }

    internal static void StoreArgs(PyObject self, PyObject[] args)
    {
        var owner = Owner(self);
        if (owner is PyExceptionInstance slot)
        {
            slot.ArgsSlot = args.Length == 0 ? PyTuple.Empty : new PyTuple(args);
            return;
        }
        if (owner is PyBaseException native)
        {
            native.SetArgsInternal(args);
            return;
        }
        throw PyTypeError.Create($"cannot store exception arguments on '{self.GetTypeName()}'");
    }

    /// <summary>The stored code, or null when the member is unset. Readers that must show None say
    /// so themselves, so that "no code yet" and "code is None" stay distinguishable in C#.</summary>
    internal static PyObject CodeOf(PyObject self)
    {
        var owner = Owner(self);
        if (owner is PyExceptionInstance slot)
            return slot.CodeSlot;
        if (owner is PySystemExit native)
            return native.Code;
        return null;
    }

    internal static void StoreCode(PyObject self, PyObject value)
    {
        var owner = Owner(self);
        // member_set with NULL is what `del e.code` does; the getter reads NULL back as None.
        var stored = value is PyNone ? null : value;
        if (owner is PyExceptionInstance slot)
        {
            slot.CodeSlot = stored;
            return;
        }
        if (owner is PySystemExit native)
        {
            native.SetCode(value);
            return;
        }
        throw PyAttributeError.Create(
            $"attribute 'code' of 'SystemExit' objects is not writable");
    }
}
