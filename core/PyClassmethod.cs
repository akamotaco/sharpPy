namespace SharpPy
{
    public class PyClassmethod : PyObject, IDescriptor
    {
        public PyFunction Function { get; }

        public PyClassmethod(PyFunction function)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
        }

        public override string GetTypeName() => "classmethod";

        // IDescriptor implementation
        public PyObject Get(PyObject instance, PyType owner)
        {
            // For classmethod, the first argument is always the class, not the instance
            var actualOwner = owner ?? instance?.GetPyType();
            if (actualOwner == null)
            {
                return Function;
            }

            // Return bound method with class as first argument
            return new PyMethod(actualOwner, Function);
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyTypeError.Create("'classmethod' object has no attribute '__set__'");
        }

        public void Delete(PyObject instance)
        {
            throw PyTypeError.Create("'classmethod' object has no attribute '__delete__'");
        }

        public bool IsDataDescriptor() => false; // classmethod is not a data descriptor

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return Function.Call(args, kwargs);
        }

        public override string ToString()
        {
            return $"<classmethod({Function})>";
        }
    }

    public class PyStaticmethod : PyObject, IDescriptor
    {
        public PyFunction Function { get; }

        public PyStaticmethod(PyFunction function)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
        }

        public override string GetTypeName() => "staticmethod";

        // CPython 3.12: Return proper type
        public override PyType GetPyType() => PyType.StaticMethodType;

        // IDescriptor implementation
        public PyObject Get(PyObject instance, PyType owner)
        {
            // For staticmethod, just return the original function
            return Function;
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyTypeError.Create("'staticmethod' object has no attribute '__set__'");
        }

        public void Delete(PyObject instance)
        {
            throw PyTypeError.Create("'staticmethod' object has no attribute '__delete__'");
        }

        public bool IsDataDescriptor() => false; // staticmethod is not a data descriptor

        // CPython 3.12: staticmethod object itself is not callable
        public override bool IsCallable() => false;

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // This shouldn't be called since IsCallable() returns false
            throw PyTypeError.Create("'staticmethod' object is not callable");
        }

        // CPython 3.12: Support __func__ attribute
        public override PyObject GetAttribute(string name)
        {
            if (name == "__func__")
            {
                return Function;
            }
            return base.GetAttribute(name);
        }

        public override string ToString()
        {
            return $"<staticmethod object at 0x{GetHashCode():x}>";
        }
    }
}