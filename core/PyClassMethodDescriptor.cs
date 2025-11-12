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
            _callable = callable;
        }

        /// <summary>
        /// CPython 3.12: Objects/funcobject.c:1034-1050 - cm_descr_get
        /// </summary>
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

        public override string GetTypeName() => "classmethod_descriptor";
        public override PyType GetPyType() => PyType.ClassMethodDescriptorType;
    }
}
