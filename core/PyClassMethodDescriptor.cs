namespace SharpPy
{
    /// <summary>
    /// CPython 3.12: Objects/funcobject.c:1034-1050 - cm_descr_get
    /// Implements classmethod descriptor protocol.
    ///
    /// Key behavior:
    /// - When accessed via instance or class, returns a bound method with the CLASS as first argument
    /// - If type is None, uses type(obj) to get the class
    /// - Delegates to wrapped callable's descriptor protocol if it has one
    /// - Otherwise creates a bound method with PyMethod_New(callable, type)
    /// </summary>
    public class PyClassMethodDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => _name;
        PyType IDescrObject.DescrType => _ownerType;

        private readonly string _name;
        private readonly PyType _ownerType;
        private readonly PyObject _callable;

        internal string Name => _name;
        internal PyType OwnerType => _ownerType;

        public PyClassMethodDescriptor(string name, PyType ownerType, PyObject callable)
        {
            _name = name;
            _ownerType = ownerType;
            // METH_CLASS: the first argument is the class, so no descr_check of an instance.
            if (callable is PyMethodDescriptor method)
                callable = new PyBuiltinFunction(name, (args, kwargs) =>
                {
                    var rest = new PyObject[args.Length - 1];
                    System.Array.Copy(args, 1, rest, 0, rest.Length);
                    return method._implementation(args[0], rest, kwargs);
                }) { OwnerClass = ownerType?.Name };
            if (callable is PyBuiltinFunction function)
                function.OwnerClass = ownerType.Name;
            // The method's calling convention checks what follows the class (PyArity).
            if (PyArity.Of(ownerType, name) is { } convention)
            {
                var checkedCallable = callable;
                callable = new PyBuiltinFunction(name, (args, kwargs) =>
                {
                    PyArity.Check(convention, ownerType, name, args[1..], kwargs, cls: args[0]);
                    return checkedCallable.Call(args, kwargs);
                }) { OwnerClass = ownerType.Name };
            }
            _callable = callable;
        }

        /// <summary>
        /// CPython 3.12: Objects/funcobject.c:1034-1050 - cm_descr_get
        /// </summary>
        /// <summary>
        /// The wrapped callable, for the one case that needs it: bytearray reuses bytes.fromhex and
        /// bytes.maketrans rather than implementing them again, and must call that implementation with
        /// its own receiver instead of going through __get__, which would move the class into the
        /// positional argument list.
        /// </summary>
        internal PyObject CallableForDelegation => _callable;

        // tp_descr_get is classmethod_get, from Python (__get__) and from attribute lookup alike.
        public PyObject Get(PyObject instance, PyType owner) => ClassMethodGet(instance, owner);

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"attribute '{_name}' of '{_ownerType.Name}' objects is not writable");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"attribute '{_name}' of '{_ownerType.Name}' objects is not writable");
        }

        // classmethod는 non-data descriptor (no __set__)
        public bool IsDataDescriptor() => false;

        // classmethod_get (Objects/descrobject.c)
        private PyObject ClassMethodGet(PyObject target, PyObject type)
        {
            type ??= target?.GetPyType();
            if (type == null)
                throw PyTypeError.Create($"descriptor '{_name}' for type '{_ownerType.Name}' needs either an object or a type");
            if (type is not PyType given)
                throw PyTypeError.Create($"descriptor '{_name}' for type '{_ownerType.Name}' needs a type, not a '{type.GetTypeName()}' as arg 2");
            if (!ReferenceEquals(given, _ownerType) && !given.IsSubclassOf(_ownerType))
                throw PyTypeError.Create($"descriptor '{_name}' requires a subtype of '{_ownerType.Name}' but received '{given.Name}'");
            return new PyCMethod(_callable, given);
        }

        // classmethoddescr_call: the class is the first argument.
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"descriptor '{_name}' of '{_ownerType.Name}' object needs an argument");
            return _callable.Call(args, kwargs);
        }

        public override bool IsCallable() => true;

        // method_repr
        public override PyString ToRepr() => new PyString($"<method '{_name}' of '{_ownerType.Name}' objects>");

        public override string GetTypeName() => "classmethod_descriptor";
        public override PyType GetPyType() => PyType.ClassMethodDescriptorType;
    }
}
