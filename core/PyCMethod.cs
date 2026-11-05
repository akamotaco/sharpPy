namespace SharpPy
{
    /// <summary>
    /// CPython 3.12: Objects/methodobject.c:44-120 - PyCMethod_New
    /// Represents a bound class method where the class (not instance) is bound as self.
    ///
    /// This is returned when accessing a classmethod descriptor.
    /// </summary>
    public class PyCMethod : PyObject, ICFunction
    {
        // m_self is the class; the name is the wrapped function's.
        string ICFunction.MethodName => _callable is ICFunction function ? function.MethodName : _callable.GetAttribute("__name__").AsString().Value;
        PyObject ICFunction.MethodSelf => _boundClass;
        PyObject ICFunction.MethodModule => null;

        private readonly PyObject _callable;
        private readonly PyType _boundClass;

        internal PyObject Callable => _callable;

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

        /// <summary>
        /// meth_richcompare (Objects/methodobject.c:292). A builtin classmethod is a PyCFunction with
        /// the class as m_self, so two lookups of dict.fromkeys are equal when the class is the same
        /// object and the wrapped implementation is the same object (:306-:308). Only == and !=
        /// compare (:298-:303). Measured: dict.fromkeys == dict.fromkeys is True.
        /// </summary>
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            if ((op != CompareOp.EQ && op != CompareOp.NE) || !(other is PyCMethod bound))
                return PyNotImplemented.Instance;
            bool equal = ReferenceEquals(_boundClass, bound._boundClass) &&
                ReferenceEquals(_callable, bound._callable);
            return op == CompareOp.EQ ? PyBool.FromBool(equal) : PyBool.FromBool(!equal);
        }

        /// <summary>
        /// meth_hash (Objects/methodobject.c:316-:331): the function part xored with m_self's hash.
        /// The class hashes by identity because that is what equality reads.
        /// </summary>
        public override int ToHash()
        {
            var self = PyObjectId.Of(_boundClass);
            return (int)(self ^ (self >> 32)) ^ (int)_callable.GetPythonHash();
        }

        // meth_repr: "<built-in method %s of %s object at %p>" with the type of m_self.
        protected internal override PyString Repr() => new PyString(
            $"<built-in method {((ICFunction)this).MethodName} of {_boundClass.GetPyType().Name} object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_boundClass):x}>");

        public override string GetTypeName() => "builtin_function_or_method";
        // CPython: builtin classmethods (dict.fromkeys) are seen as PyCFunction.
        public override PyType GetPyType() => PyType.BuiltinFunctionOrMethodType;

        public override string ToString() => $"<built-in method {_callable}>";
    }
}
