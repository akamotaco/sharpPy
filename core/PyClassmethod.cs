namespace SharpPy
{
    // CPython 3.12: Objects/funcobject.c:1001-1123 (classmethod)
    public class PyClassmethod : PyObject, IDescriptor
    {
        // CPython 3.12: Objects/funcobject.c:1003 - cm_callable can be ANY callable
        public PyObject Callable { get; }

        public PyClassmethod(PyObject callable)
        {
            Callable = callable ?? throw new ArgumentNullException(nameof(callable));
        }

        public override string GetTypeName() => "classmethod";

        // CPython 3.12: Return proper type
        public override PyType GetPyType() => PyType.ClassMethodType;

        // IDescriptor implementation
        // CPython 3.12: Objects/funcobject.c:1011-1044 (cm_descr_get)
        public PyObject Get(PyObject instance, PyType owner)
        {
            // For classmethod, the first argument is always the class, not the instance
            var actualOwner = owner ?? instance?.GetPyType();
            if (actualOwner == null)
            {
                return Callable;
            }

            // Return bound method with class as first argument
            // For PyFunction, create PyMethod; for other callables, return as-is
            if (Callable is PyFunction func)
            {
                return new PyMethod(actualOwner, func);
            }

            // For non-PyFunction callables, we'd need to wrap them properly
            // For now, return as-is (enum module might need this)
            return Callable;
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
            return Callable.Call(args, kwargs);
        }

        // CPython 3.12: Expose descriptor protocol methods as attributes
        // Objects/funcobject.c:1089-1091 (cm_getset) exposes __func__, __wrapped__
        // tp_descr_get (line 1157) makes __get__ available
        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12: Objects/funcobject.c:1089-1091 - Support __func__ and __wrapped__ attributes
            if (name == "__func__" || name == "__wrapped__")
            {
                return Callable;
            }

            // CPython 3.12: tp_descr_get makes __get__ available as a method
            if (name == "__get__")
            {
                // Return a bound descriptor method
                return new PyBuiltinFunction("__get__", (args) =>
                {
                    // __get__(self, obj, type=None)
                    PyObject instance = args.Length > 0 ? args[0] : null;
                    PyType owner = args.Length > 1 && args[1] is PyType t ? t : null;
                    return Get(instance, owner);
                });
            }

            return base.GetAttribute(name);
        }

        // CPython 3.12: Objects/funcobject.c:1096-1100 (cm_repr)
        public override string ToString()
        {
            return $"<classmethod({Callable})>";
        }
    }

    // CPython 3.12: Objects/funcobject.c:1196-1357 (staticmethod)
    public class PyStaticmethod : PyObject, IDescriptor
    {
        // CPython 3.12: Objects/funcobject.c:1198 - sm_callable can be ANY callable
        public PyObject Callable { get; }

        public PyStaticmethod(PyObject callable)
        {
            Callable = callable ?? throw new ArgumentNullException(nameof(callable));
        }

        public override string GetTypeName() => "staticmethod";

        // CPython 3.12: Return proper type
        public override PyType GetPyType() => PyType.StaticMethodType;

        // IDescriptor implementation
        // CPython 3.12: Objects/funcobject.c:1228-1238 (sm_descr_get)
        public PyObject Get(PyObject instance, PyType owner)
        {
            // For staticmethod, just return the original callable
            return Callable;
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

        // CPython 3.12: Objects/funcobject.c:1332 - staticmethod IS callable (has tp_call)
        public override bool IsCallable() => true;

        // CPython 3.12: Objects/funcobject.c:1259-1263 (sm_call)
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return Callable.Call(args, kwargs);
        }

        // CPython 3.12: Expose descriptor protocol methods as attributes
        // Objects/funcobject.c:1265-1268 - Support __func__ and __wrapped__ attributes
        // tp_descr_get (line 1352) makes __get__ available
        public override PyObject GetAttribute(string name)
        {
            if (name == "__func__" || name == "__wrapped__")
            {
                return Callable;
            }

            // CPython 3.12: tp_descr_get makes __get__ available as a method
            if (name == "__get__")
            {
                // Return a bound descriptor method
                return new PyBuiltinFunction("__get__", (args) =>
                {
                    // __get__(self, obj, type=None)
                    // For staticmethod, just return the callable regardless of instance/owner
                    return Callable;
                });
            }

            return base.GetAttribute(name);
        }

        // CPython 3.12: Objects/funcobject.c:1291-1295 (sm_repr)
        public override string ToString()
        {
            return $"<staticmethod({Callable})>";
        }
    }
}