using System;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12: METH_O|METH_CLASS - classmethod for built-in types
    /// Like @classmethod but for built-in methods (e.g., __class_getitem__)
    /// Objects/descrobject.c: classmethod_descriptor_type
    /// </summary>
    public class PyBuiltinClassMethod : PyObject, IDescriptor
    {
        public string Name { get; }
        private readonly Func<PyObject, PyObject, PyObject> _method;

        public PyBuiltinClassMethod(string name, Func<PyObject, PyObject, PyObject> method)
        {
            Name = name;
            _method = method;
        }

        public override string ToString() => $"<built-in classmethod '{Name}'>";
        public override PyType GetPyType() => PyType.MethodType;
        public override string GetTypeName() => "classmethod_descriptor";

        // IDescriptor implementation
        public PyObject Get(PyObject instance, PyType owner)
        {
            // CPython: classmethod always binds to the class, not the instance
            // For type objects: instance is the type (e.g., list), owner is the metaclass (type)
            // We want to bind to the instance (list), not the owner (type)
            // For regular objects: instance is the object, owner is the class
            // We want to bind to the owner (the class)

            PyObject boundClass;
            if (instance is PyType)
            {
                // Accessing on a type object: list.__class_getitem__
                // Bind to the type itself (instance), not its metaclass (owner)
                boundClass = instance;
            }
            else if (instance != null)
            {
                // Accessing on an instance: obj.__class_getitem__
                // Bind to its class (owner)
                boundClass = owner ?? instance.GetPyType();
            }
            else
            {
                // Unbound access
                return this;
            }

            return new PyBoundClassMethod(this, boundClass);
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"can't set attribute '{Name}'");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"can't delete attribute '{Name}'");
        }

        public bool IsDataDescriptor() => false;

        // Direct call (unbound)
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 2)
                throw PyTypeError.Create($"{Name}() missing required arguments");

            return _method(args[0], args[1]);
        }

        public override bool IsCallable() => true;

        private class PyBoundClassMethod : PyObject
        {
            private readonly PyBuiltinClassMethod _method;
            private readonly PyObject _class;

            public PyBoundClassMethod(PyBuiltinClassMethod method, PyObject cls)
            {
                _method = method;
                _class = cls;
            }

            public override string ToString() => $"<bound classmethod '{_method.Name}' of {_class}>";
            public override PyType GetPyType() => PyType.MethodType;

            public override PyObject Call(PyObject[] args, PyDict kwargs = null)
            {
                // Bound call: prepend the class as first argument
                if (args.Length < 1)
                    throw PyTypeError.Create($"{_method.Name}() missing required argument");

                return _method._method(_class, args[0]);
            }

            public override bool IsCallable() => true;
        }
    }
}
