namespace SharpPy
{
    #region Descriptor Protocol

// Descriptor 프로토콜 인터페이스
public interface IDescriptor
{
    PyObject Get(PyObject instance, PyType owner);
    void Set(PyObject instance, PyObject value);
    void Delete(PyObject instance);
    bool IsDataDescriptor(); // __set__ 또는 __delete__가 있으면 data descriptor
}

    // Python의 property 구현
    public class PyProperty : PyObject, IDescriptor
    {
        private readonly PyFunction _getter;
        private readonly PyFunction _setter;
        private readonly PyFunction _deleter;

        public PyProperty(PyFunction getter, PyFunction setter = null, PyFunction deleter = null)
        {
            _getter = getter;
            _setter = setter;
            _deleter = deleter;
        }

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (_getter == null)
                throw PyAttributeError.Create("unreadable attribute");

            if (instance == null)
                return this; // 클래스에서 접근할 때는 property 객체 자체 반환

            return _getter.Call(instance);
        }

        public void Set(PyObject instance, PyObject value)
        {
            if (_setter == null)
                throw PyAttributeError.Create("can't set attribute");
            _setter.Call(instance, value);
        }

        public void Delete(PyObject instance)
        {
            if (_deleter == null)
                throw PyAttributeError.Create("can't delete attribute");
            _deleter.Call(instance);
        }

        public bool IsDataDescriptor() => _setter != null || _deleter != null;

        // Property decorator chaining methods
        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "setter":
                    return new PyBuiltinFunction("setter", args => {
                        if (args.Length != 1) throw PyTypeError.Create($"setter() takes exactly one argument ({args.Length} given)");
                        if (args[0] is not PyFunction func) throw PyTypeError.Create("setter() argument must be a function");
                        return new PyProperty(_getter, func, _deleter);
                    });
                case "deleter":
                    return new PyBuiltinFunction("deleter", args => {
                        if (args.Length != 1) throw PyTypeError.Create($"deleter() takes exactly one argument ({args.Length} given)");
                        if (args[0] is not PyFunction func) throw PyTypeError.Create("deleter() argument must be a function");
                        return new PyProperty(_getter, _setter, func);
                    });
                case "getter":
                    return new PyBuiltinFunction("getter", args => {
                        if (args.Length != 1) throw PyTypeError.Create($"getter() takes exactly one argument ({args.Length} given)");
                        if (args[0] is not PyFunction func) throw PyTypeError.Create("getter() argument must be a function");
                        return new PyProperty(func, _setter, _deleter);
                    });
                case "fget":
                    return _getter ?? (PyObject)PyNone.Instance;
                case "fset":
                    return _setter ?? (PyObject)PyNone.Instance;
                case "fdel":
                    return _deleter ?? (PyObject)PyNone.Instance;
                default:
                    return base.GetAttribute(name);
            }
        }

        public override string GetTypeName() => "property";
        public override string ToString() => "<property object>";
    }

    #endregion
}