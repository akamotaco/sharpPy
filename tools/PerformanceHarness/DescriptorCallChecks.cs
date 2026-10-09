using SharpPy;

internal static class DescriptorCallChecks
{
    public static void Run()
    {
        var receiver = new PyInt(7);
        var argument = new PyInt(2);
        var kwargs = new PyDict();
        kwargs.SetItem(new PyString("flag"), PyBool.True);
        PyObject Handler(PyObject self, PyObject[] args, PyDict keywords)
        {
            if (!ReferenceEquals(self, receiver) || args.Length != 1 ||
                !ReferenceEquals(args[0], argument) || !ReferenceEquals(keywords, kwargs))
                throw new InvalidOperationException("Descriptor receiver/arguments/kwargs changed");
            return argument;
        }
        var descriptor = new PyMethodDescriptor("probe", PyType.IntType, Handler);
        var bound = descriptor.Get(receiver, PyType.IntType);
        if (!ReferenceEquals(bound.Call(new PyObject[] { argument }, kwargs), argument))
            throw new InvalidOperationException("Bound descriptor result changed");
        descriptor.Call(new PyObject[] { receiver, argument }, kwargs);

        var derived = new ObservingDescriptor(Handler);
        derived.Get(receiver, PyType.IntType).Call(new PyObject[] { argument }, kwargs);
        if (derived.Calls != 0) throw new InvalidOperationException("Existing bound descriptor dispatch changed");

        // VM subscription must still use Call overrides on descriptor subclasses.
        var getter = new ObservingDescriptor((self, args, keywords) =>
        {
            if (!ReferenceEquals(self, receiver) || args.Length != 1 || !ReferenceEquals(args[0], argument))
                throw new InvalidOperationException("VM subscription arguments changed");
            return argument;
        });
        var owner = PyType.IntType;
        bool hadOriginal = owner.TypeDict.TryGetValue("__getitem__", out var original);
        try
        {
            owner.TypeDict["__getitem__"] = getter;
            owner.InvalidateTypeCache();
            var interpreter = new IntegratedPythonInterpreter();
            interpreter.Execute("def probe(receiver, key):\n    return receiver[key]\n");
            var function = interpreter.GetGlobalVariable("probe");
            if (!ReferenceEquals(function.Call(new PyObject[] { receiver, argument }, null), argument) || getter.Calls != 1)
                throw new InvalidOperationException("VM descriptor subclass override bypassed");
        }
        finally
        {
            if (hadOriginal) owner.TypeDict["__getitem__"] = original!;
            else owner.TypeDict.Remove("__getitem__");
            owner.InvalidateTypeCache();
        }

        var throwing = new PyMethodDescriptor("error", PyType.IntType,
            (self, args, keywords) => throw PyValueError.Create("descriptor error"));
        try
        {
            throwing.Get(receiver, PyType.IntType).Call(Array.Empty<PyObject>(), null);
            throw new InvalidOperationException("Descriptor exception swallowed");
        }
        catch (PythonException error) when (error.PyException is PyValueError) { }
    }

    private sealed class ObservingDescriptor : PyMethodDescriptor
    {
        public int Calls { get; private set; }
        public ObservingDescriptor(Func<PyObject, PyObject[], PyDict, PyObject> handler)
            : base("derived", PyType.IntType, handler) { }
        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            Calls++;
            return base.Call(args, kwargs);
        }
    }
}
