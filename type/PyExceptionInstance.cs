using System;

namespace SharpPy;

/// <summary>
/// A Python-defined subclass of an exception type. CPython gives every exception object two slots in
/// its layout - <c>args</c> (Include/cpython/pyerrors.h:9, written by BaseException_new at :56) and, for
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
    internal PyTuple ArgsSlot = PyTuple.Empty;

    /// <summary>SystemExit.code. A null field is an unset member; PyMember_GetOne reads a NULL
    /// T_OBJECT back as None (Objects/structmember.c), which is what `del e.code` leaves behind.</summary>
    internal PyObject CodeSlot;

    /// <summary>The struct a C subclass inherits: fields such as OSError.errno or ImportError.name, with
    /// the arguments their __init__ parsed. An instance of the nearest builtin exception, which already
    /// is that struct and that parser.</summary>
    internal PyBaseException Layout;

    public PyExceptionInstance(PyClass instanceType) : base(instanceType) { }

    private PyException _transport;

    /// <summary>
    /// What carries this exception through the CLR. There is one per instance, so raising an object
    /// twice raises one thing, and __traceback__, __cause__ and __context__ have one home. Python never
    /// sees it: PyExceptionSlots.Visible turns it back into this instance at every way out.
    /// </summary>
    internal PyException Transport => _transport ??= new PyException(ToString(), InstanceType, this);
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
    internal static PyObject Visible(PyObject exception) => exception == null ? null : Owner(exception);

    /// <summary>The object a raise carries: itself for a builtin exception, otherwise the instance's one
    /// transport. Anything that is no exception comes back unchanged for the caller to refuse.</summary>
    internal static PyObject ToTransport(PyObject value)
    {
        if (value is PyExceptionInstance instance) return instance.Transport;
        if (value is PyClassInstance other && other.InstanceType.IsSubclassOf(PyType.BaseExceptionType))
            return new PyException(other.ToString(), other.InstanceType, other);
        return value;
    }

    private static PyObject Owner(PyObject self)
        => self is PyException wrapper && wrapper.OriginalInstance != null
            ? wrapper.OriginalInstance
            : self;

    /// <summary>Objects/exceptions.c: the *_members and *_getset tables of the types with fields.</summary>
    internal static string[] FieldsOfType(string typeName) => typeName switch
    {
        "OSError" => new[] { "errno", "strerror", "filename", "filename2" },
        "StopIteration" => new[] { "value" },
        "ImportError" => new[] { "name", "path", "msg" },
        "AttributeError" => new[] { "name", "obj" },
        "NameError" => new[] { "name" },
        "SyntaxError" => new[] { "msg", "filename", "lineno", "offset", "text", "end_lineno", "end_offset",
                                 "print_file_and_line" },
        "UnicodeDecodeError" or "UnicodeEncodeError" => new[] { "encoding", "object", "start", "end", "reason" },
        "UnicodeTranslateError" => new[] { "object", "start", "end", "reason" },
        _ => null,
    };

    private static PyObject StateOf(PyObject self)
    {
        var dict = self.GetAttribute("__dict__");
        return dict.Length() == 0 ? null : dict;
    }

    private static PyObject ReduceValue(PyObject self, PyObject args, PyObject state) => state == null
        ? new PyTuple(new[] { self.GetPyType(), args })
        : new PyTuple(new[] { self.GetPyType(), args, state });

    // ImportError_getstate and AttributeError_getstate: the dict with the fields put in it.
    private static PyObject StateWith(PyObject self, params string[] fields)
    {
        PyDict state = null;
        foreach (var name in fields)
        {
            var value = self.GetAttribute(name);
            if (value is PyNone) continue;
            state ??= new PyDict();
            state.SetItem(new PyString(name), value);
        }
        var dict = StateOf(self);
        if (state == null) return dict;
        if (dict != null)
            foreach (var entry in new List<KeyValuePair<PyObject, PyObject>>(((PyDict)dict).GetInternalDict()))
                if (!state.ContainsKey(entry.Key)) state.SetItem(entry.Key, entry.Value);
        return state;
    }

    private static PyObject ArgsTupleOf(PyObject self) => self.GetAttribute("args");

    /// <summary>BaseException_methods and the reductions of the types with fields.</summary>
    internal static void InstallReduce()
    {
        void Method(PyType type, string name, Func<PyObject, PyObject[], PyObject> body, int count = 0) =>
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) => body(self, args), count, count);
        Method(PyType.BaseExceptionType, "__reduce__", (self, args) => ReduceValue(self, ArgsTupleOf(self), StateOf(self)));
        Method(PyType.BaseExceptionType, "__setstate__", (self, args) =>
        {
            if (args[0] is PyNone) return PyNone.Instance;
            if (args[0] is not PyDict state)
                throw PyTypeError.Create("state is not a dictionary");
            foreach (var entry in new List<KeyValuePair<PyObject, PyObject>>(state.GetInternalDict()))
                self.SetAttribute(((PyString)entry.Key).Value, entry.Value);
            return PyNone.Instance;
        }, 1);
        Method(PyType.ImportErrorType, "__reduce__", (self, args) => ReduceValue(self, ArgsTupleOf(self), StateWith(self, "name", "path")));
        Method(PyType.ImportErrorType, "__getstate__", (self, args) => StateWith(self, "name", "path") ?? PyNone.Instance);
        Method(PyType.AttributeErrorType, "__getstate__", (self, args) => StateWith(self, "name", "args") ?? PyNone.Instance);
        Method(PyType.AttributeErrorType, "__reduce__", (self, args) => ReduceValue(self, ArgsTupleOf(self), StateWith(self, "name", "args")));
        Method(PyType.OSErrorType, "__reduce__", (self, args) =>
        {
            // args holds only the first two real arguments when a file name was given.
            var arguments = (PyTuple)ArgsTupleOf(self);
            var filename = self.GetAttribute("filename");
            if (arguments.Items.Length == 2 && filename is not PyNone)
            {
                var filename2 = self.GetAttribute("filename2");
                arguments = filename2 is PyNone
                    ? new PyTuple(new[] { arguments.Items[0], arguments.Items[1], filename })
                    : new PyTuple(new[] { arguments.Items[0], arguments.Items[1], filename, PyNone.Instance, filename2 });
            }
            return ReduceValue(self, arguments, StateOf(self));
        });
    }

    /// <summary>The member descriptors and __init__ of such a type. A builtin object is built by its
    /// constructor and answers for its own fields; only a Python subclass comes through here.</summary>
    internal static void InstallFields(PyType type, string[] names)
    {
        foreach (var name in names)
        {
            type.TypeDict[name] = new PyMemberDescriptor(name, type,
                getter: self => LayoutOf(self)?.GetAttribute(name) ?? PyNone.Instance,
                setter: (self, value) => LayoutOf(self)?.SetAttribute(name, value));
        }
        type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
        {
            if (Owner(self) is PyExceptionInstance instance)
            {
                instance.Layout = (PyBaseException)NearestBuiltin(instance.InstanceType).Call(args, kwargs);
                instance.ArgsSlot = instance.Layout.ArgsTuple;
            }
            else if (self is PyBaseException exception)
            {
                // A second __init__ resets every field, what it was not given included.
                var fresh = (PyBaseException)exception.GetPyType().Call(args, kwargs);
                exception.SetArgsTuple(fresh.ArgsTuple);
                foreach (var name in names)
                    exception.SetAttribute(name, fresh.GetAttribute(name));
            }
            return PyNone.Instance;
        });
    }

    /// <summary>PyErr_NewException: a module's exception class is an ordinary heap class.</summary>
    internal static PyType NewException(string module, string name, PyType baseType)
    {
        var namespaceDict = new PyDict();
        namespaceDict.SetItem(new PyString("__module__"), new PyString(module));
        return (PyType)PyType.TypeType.Call(
            new PyObject[] { new PyString(name), new PyTuple(new PyObject[] { baseType }), namespaceDict }, null);
    }

    /// <summary>PyErr_SetString for such a class.</summary>
    internal static PythonException Raise(PyType type, string message)
        => new PythonException((PyBaseException)ToTransport(type.Call(new PyObject[] { new PyString(message) }, null)));

    internal static PyBaseException LayoutOf(PyObject self) => (Owner(self) as PyExceptionInstance)?.Layout;

    private static PyType NearestBuiltin(PyClass type)
    {
        foreach (var entry in ((PyTuple)type.GetAttribute("__mro__")).Items)
            if (entry is PyType builtin && entry is not PyClass && builtin.DerivesFromType(PyType.BaseExceptionType))
                return builtin;
        return PyType.BaseExceptionType;
    }

    internal static PyObject[] ArgsOf(PyObject self)
    {
        var owner = Owner(self);
        if (owner is PyExceptionInstance slot)
            return slot.ArgsSlot.Items;
        if (owner is PyBaseException native)
            return native.Args;
        return Array.Empty<PyObject>();
    }

    /// <summary>The stored tuple object itself, so identity is preserved across reads.</summary>
    internal static PyTuple ArgsTuple(PyObject self)
    {
        var owner = Owner(self);
        if (owner is PyExceptionInstance slot)
            return slot.ArgsSlot;
        if (owner is PyBaseException native)
            return native.ArgsTuple;
        return PyTuple.Empty;
    }

    /// <summary>BaseException_set_args (Objects/exceptions.c:261): PySequence_Tuple(val), store.</summary>
    internal static void AssignArgs(PyObject self, PyObject value)
    {
        var owner = Owner(self);
        var tuple = value is PyTuple ? (PyTuple)value
            : (PyTuple)PyType.TupleType.Call(new PyObject[] { value }, null);
        if (owner is PyExceptionInstance slot)
        {
            slot.ArgsSlot = tuple;
            return;
        }
        if (owner is PyBaseException native)
        {
            native.SetArgsTuple(tuple);
            return;
        }
        throw PyTypeError.Create($"cannot store exception arguments on '{self.GetTypeName()}'");
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
