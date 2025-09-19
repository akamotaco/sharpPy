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

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return Function.Call(args, kwargs);
        }

        public override string ToString()
        {
            return $"<staticmethod({Function})>";
        }
    }
}