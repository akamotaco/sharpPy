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
    public class PyClassMethodDescriptor : PyObject, IDescriptor
    {
        private readonly string _name;
        private readonly PyType _ownerType;
        private readonly PyObject _callable;

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
                });
            _callable = callable;
            if (callable is PyBuiltinFunction function)
                function.OwnerClass = ownerType.Name;
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

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (_callable == null)
            {
                throw PyRuntimeError.Create("uninitialized classmethod object");
            }

            // CPython 3.12: line 1043-1044
            // if (type == NULL) type = (PyObject *)(Py_TYPE(obj));
            PyType type = owner;
            if (type == null && instance != null)
            {
                type = instance.GetPyType();
            }

            // CPython 3.12: line 1045-1048
            // If the wrapped callable is itself a descriptor, call its __get__
            if (_callable is IDescriptor descriptor)
            {
                return descriptor.Get(type, type);
            }

            // CPython 3.12: Objects/descrobject.c:146
            // return PyCMethod_New(descr->d_method, type, NULL, cls);
            // Create a PyCMethod object that binds the class as the first argument
            return new PyCMethod(_callable, type);
        }

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

        public override PyObject GetAttribute(string name)
        {
            if (name == "__get__")
                return new PyBuiltinFunction("__get__", args =>
                    ClassMethodGet(args[0] is PyNone ? null : args[0],
                                   args.Length > 1 && args[1] is not PyNone ? args[1] : null));
            return PyMethodDescriptor.DescriptorAttribute(name, _ownerType, _name) ?? base.GetAttribute(name);
        }

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

        public override string GetTypeName() => "classmethod_descriptor";
        public override PyType GetPyType() => PyType.ClassMethodDescriptorType;
    }
}
