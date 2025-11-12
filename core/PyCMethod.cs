namespace SharpPy
{
    /// <summary>
    /// CPython 3.12: Objects/methodobject.c:44-120 - PyCMethod_New
    /// Represents a bound class method where the class (not instance) is bound as self.
    ///
    /// This is returned when accessing a classmethod descriptor.
    /// </summary>
    public class PyCMethod : PyObject
    {
        private readonly PyObject _callable;
        private readonly PyType _boundClass;

        /// <summary>
        /// CPython 3.12: Objects/methodobject.c:87-92
        /// Create a PyCMethod with the class bound as the first argument.
        /// </summary>
        public PyCMethod(PyObject callable, PyType boundClass)
        {
            _callable = callable;
            _boundClass = boundClass;
        }

        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            // Prepend the bound class as the first argument
            var newArgs = new PyObject[args.Length + 1];
            newArgs[0] = _boundClass;
            Array.Copy(args, 0, newArgs, 1, args.Length);

            return _callable.Call(newArgs, kwargs);
        }

        public override bool IsCallable() => true;

        public override string GetTypeName() => "builtin_function_or_method";
        public override PyType GetPyType() => PyType.MethodType;

        public override string ToString() => $"<built-in method {_callable}>";
    }
}
