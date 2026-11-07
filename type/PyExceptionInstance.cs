using System;

namespace SharpPy;

/// <summary>
/// The single route to the args and code slots. Every reader - the <c>args</c> member descriptor,
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
    /// <summary>Objects/exceptions.c: the *_members and *_getset tables of the types with fields.</summary>
    internal static string[] FieldsOfType(string typeName) => typeName switch
    {
        "OSError" => OperatingSystem.IsWindows()
            ? new[] { "errno", "strerror", "filename", "filename2", "winerror" }
            : new[] { "errno", "strerror", "filename", "filename2" },
        "StopIteration" => new[] { "value" },
        "ImportError" => new[] { "msg", "name", "path", "name_from" },
        "AttributeError" => new[] { "name", "obj" },
        "NameError" => new[] { "name" },
        "SyntaxError" => new[] { "msg", "filename", "lineno", "offset", "text", "end_lineno", "end_offset",
                                 "print_file_and_line" },
        "UnicodeDecodeError" or "UnicodeEncodeError" => new[] { "encoding", "object", "start", "end", "reason" },
        "UnicodeTranslateError" => new[] { "encoding", "object", "start", "end", "reason" },
        _ => null,
    };

    private static PyObject StateOf(PyObject self)
    {
        var dict = self.GetAttribute("__dict__");
        return dict.Length() == 0 ? null : dict;
    }

    private static PyObject ReduceValue(PyObject self, PyObject args, PyObject state) => state == null
        ? PyTuple.From(new[] { self.GetPyType(), args })
        : PyTuple.From(new[] { self.GetPyType(), args, state });

    // ImportError_getstate and AttributeError_getstate: the dict with the fields put in it.
    private static PyObject StateWith(PyObject self, params string[] fields)
    {
        PyDict state = null;
        foreach (var name in fields)
        {
            var value = self.GetAttribute(name);
            if (value is PyNone) continue;
            state ??= new PyDict();
            state.SetItem(PyString.From(name), value);
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
                    ? PyTuple.From(new[] { arguments.Items[0], arguments.Items[1], filename })
                    : PyTuple.From(new[] { arguments.Items[0], arguments.Items[1], filename, PyNone.Instance, filename2 });
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
                getter: self => MemberLayout(self) is { } layout && layout.TryGetMember(name, out var value)
                    ? value ?? PyNone.Instance : PyNone.Instance,
                setter: (self, value) => MemberLayout(self)?.TrySetMember(name, value),
                deleter: self => MemberLayout(self)?.TrySetMember(name, null));
        }
        if (type.Name == "OSError")
            type.TypeDict["characters_written"] = new PyGetSetDescriptor("characters_written", type,
                self => ((PyOSError)MemberLayout(self)).CharactersWritten,
                (self, value) => ((PyOSError)MemberLayout(self)).SetCharactersWritten(value),
                deleter: self => ((PyOSError)MemberLayout(self)).SetCharactersWritten(null));
        // STORE_init of the type: it reads the arguments into the struct that tp_new allocated.
        type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, InitOf(type.Name));
        // OSError_new: the one tp_new of a type with fields that is not BaseException_new.
        if (type.Name == "OSError")
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, PyOSError.NewSlot, acceptsKwargs: true);
    }

    private static Func<PyObject, PyObject[], PyDict, PyObject> InitOf(string store) => store switch
    {
        "OSError" => PyOSError.InitSlot,
        "StopIteration" => (self, args, kwargs) => { ((PyStopIteration)StructOf(self)).StopIterationInit(self, args, kwargs); return PyNone.Instance; },
        "ImportError" => (self, args, kwargs) => { ((PyImportError)StructOf(self)).ImportErrorInit(self, args, kwargs); return PyNone.Instance; },
        "AttributeError" => (self, args, kwargs) => { ((PyAttributeError)StructOf(self)).AttributeErrorInit(self, args, kwargs); return PyNone.Instance; },
        "NameError" => (self, args, kwargs) => { ((PyNameError)StructOf(self)).NameErrorInit(self, args, kwargs); return PyNone.Instance; },
        "SyntaxError" => (self, args, kwargs) => { ((PySyntaxError)StructOf(self)).SyntaxErrorInit(self, args, kwargs); return PyNone.Instance; },
        "UnicodeEncodeError" => (self, args, kwargs) => { ((PyUnicodeEncodeError)StructOf(self)).UnicodeEncodeErrorInit(self, args, kwargs); return PyNone.Instance; },
        "UnicodeDecodeError" => (self, args, kwargs) => { ((PyUnicodeDecodeError)StructOf(self)).UnicodeDecodeErrorInit(self, args, kwargs); return PyNone.Instance; },
        "UnicodeTranslateError" => (self, args, kwargs) => { ((PyUnicodeTranslateError)StructOf(self)).UnicodeTranslateErrorInit(self, args, kwargs); return PyNone.Instance; },
        _ => throw new InvalidOperationException($"no {store}_init"),
    };

    /// <summary>The exception object self is; a subclass instance is one too.</summary>
    internal static PyBaseException StructOf(PyObject self) =>
        self as PyBaseException ?? throw PyTypeError.Create($"'{self.GetTypeName()}' object is not an exception");

    /// <summary>PyErr_NewException: a module's exception class is an ordinary heap class.</summary>
    internal static PyType NewException(string module, string name, PyType baseType)
    {
        var namespaceDict = new PyDict();
        namespaceDict.SetItem(PyString.From("__module__"), PyString.From(module));
        return (PyType)PyType.TypeType.Call(
            new PyObject[] { PyString.From(name), PyTuple.From(new PyObject[] { baseType }), namespaceDict }, null);
    }

    /// <summary>PyErr_SetString for such a class.</summary>
    internal static PythonException Raise(PyType type, string message)
        => new PythonException((PyBaseException)type.Call(new PyObject[] { PyString.From(message) }, null));

    // The struct a member descriptor reads: the exception object itself.
    private static PyBaseException MemberLayout(PyObject self) => self as PyBaseException;

    /// <summary>
    /// BaseException_new's type->tp_alloc for a class written in Python: the struct of the nearest
    /// builtin exception, fields all NULL until an __init__ reads its arguments, with ob_type the
    /// class. A group subclass is made by BaseExceptionGroup_new from the arguments.
    /// </summary>
    internal static PyBaseException Allocate(PyClass heap, PyObject[] args)
    {
        var builtin = NearestBuiltin(heap);
        PyBaseException created;
        if (builtin.IsSubclassOf(PyType.BaseExceptionGroupType))
            created = (PyBaseException)PyBaseExceptionGroup.NewGroup(args, heap);
        else
            created = (PyBaseException)builtin.CreateInstance(new PyObject[0], null);
        created.SetCustomType(heap);
        return created;
    }

    private static PyType NearestBuiltin(PyClass type)
    {
        foreach (var entry in type.MRO)
            if (entry is not PyClass && entry.DerivesFromType(PyType.BaseExceptionType))
                return entry;
        return PyType.BaseExceptionType;
    }

    internal static PyObject[] ArgsOf(PyObject self) =>
        self is PyBaseException native ? native.Args : Array.Empty<PyObject>();

    /// <summary>The stored tuple object itself, so identity is preserved across reads.</summary>
    internal static PyTuple ArgsTuple(PyObject self) =>
        self is PyBaseException native ? native.ArgsTuple : PyTuple.Empty;

    /// <summary>BaseException_set_args (Objects/exceptions.c:261): PySequence_Tuple(val), store.</summary>
    internal static void AssignArgs(PyObject self, PyObject value)
    {
        var tuple = value is PyTuple ? (PyTuple)value
            : (PyTuple)PyType.TupleType.Call(new PyObject[] { value }, null);
        if (self is not PyBaseException native)
            throw PyTypeError.Create($"cannot store exception arguments on '{self.GetTypeName()}'");
        native.SetArgsTuple(tuple);
    }

    internal static void StoreArgs(PyObject self, PyObject[] args)
    {
        if (self is not PyBaseException native)
            throw PyTypeError.Create($"cannot store exception arguments on '{self.GetTypeName()}'");
        native.SetArgsInternal(args);
    }

    /// <summary>The stored code, or null when the member is unset. Readers that must show None say
    /// so themselves, so that "no code yet" and "code is None" stay distinguishable in C#.</summary>
    internal static PyObject CodeOf(PyObject self) => (self as PySystemExit)?.Code;

    internal static void StoreCode(PyObject self, PyObject value)
    {
        if (self is PySystemExit native)
        {
            native.SetCode(value);
            return;
        }
        throw PyAttributeError.Create(
            $"attribute 'code' of 'SystemExit' objects is not writable");
    }
}
