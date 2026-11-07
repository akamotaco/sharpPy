using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// What classmethod and staticmethod share in Objects/funcobject.c: the wrapped callable, a
    /// __dict__ filled by functools_wraps, and a type that may be a Python subclass.
    /// </summary>
    public abstract class PyCallableWrapper : PyObject, IDescriptor, IObjectDict
    {
        private static readonly string[] WrappedAttributes = { "__module__", "__name__", "__qualname__", "__doc__", "__annotations__" };

        private readonly PyType _builtin;

        public PyObject Callable { get; private set; }
        // cm_dict / sm_dict: the wrapper's one __dict__, which functools_wraps fills.
        private PyDict _dict;
        public PyDict ObjectDict(bool create) => create ? _dict ??= new PyDict() : _dict;
        public void SetObjectDict(PyDict dict) => _dict = dict;

        protected PyCallableWrapper(PyType type, PyObject callable)
        {
            _builtin = Allocated(type);
            if (callable != null)
                Initialize(callable);
        }

        // cm_init and sm_init, then functools_wraps
        internal void Initialize(PyObject callable)
        {
            Callable = callable;
            foreach (var name in WrappedAttributes)
            {
                try
                {
                    ObjectDict(true).SetItem(PyString.From(name), callable.GetAttribute(name));
                }
                catch (PythonException error) when (error.PyException is PyAttributeError)
                {
                }
            }
        }

        public override PyType GetPyType() => _customType ?? _builtin;
        public override string GetTypeName() => GetPyType().Name;

        public abstract PyObject Get(PyObject instance, PyType owner);

        public void Set(PyObject instance, PyObject value) =>
            throw PyTypeError.Create($"'{GetTypeName()}' object has no attribute '__set__'");

        public void Delete(PyObject instance) =>
            throw PyTypeError.Create($"'{GetTypeName()}' object has no attribute '__delete__'");

        public bool IsDataDescriptor() => false;

        // _PyObject_IsAbstract
        internal bool IsAbstract()
        {
            try
            {
                return Callable.GetAttribute("__isabstractmethod__").IsTrue();
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return false;
            }
        }

        internal static void Install(PyType type, string name)
        {
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
                ReferenceEquals(type, PyType.ClassMethodType)
                    ? new PyClassmethod((PyType)cls, null)
                    : new PyStaticmethod((PyType)cls, null), acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{name}() takes no keyword arguments");
                if (args.Length != 1)
                    throw PyTypeError.Create($"{name} expected 1 argument, got {args.Length}");
                ((PyCallableWrapper)self).Initialize(args[0]);
                return PyNone.Instance;
            }, 0, int.MaxValue);
            // cm_memberlist / sm_memberlist, and cm_getsetlist's __dict__.
            type.TypeDict["__func__"] = new PyMemberDescriptor("__func__", type,
                self => ((PyCallableWrapper)self).Callable, readOnly: true);
            type.TypeDict["__wrapped__"] = new PyMemberDescriptor("__wrapped__", type,
                self => ((PyCallableWrapper)self).Callable, readOnly: true);
            type.TypeDict["__dict__"] = SubclassInstanceSupport.GenericDictGetSet(type);
            type.TypeDict["__isabstractmethod__"] = new PyGetSetDescriptor("__isabstractmethod__", type,
                self => ((PyCallableWrapper)self).IsAbstract() ? PyBool.True : PyBool.False, null);
            type.TypeDict["__get__"] = new PyWrapperDescriptor("__get__", type, (self, args, kwargs) =>
            {
                PyObject instance = args.Length > 0 && args[0] is not PyNone ? args[0] : null;
                PyObject owner = args.Length > 1 && args[1] is not PyNone ? args[1] : null;
                if (instance == null && owner == null)
                    throw PyTypeError.Create("__get__(None, None) is invalid");
                return ((PyCallableWrapper)self).Get(instance, owner as PyType);
            }, 1, 2);
        }
    }

    // Objects/funcobject.c: classmethod
    public class PyClassmethod : PyCallableWrapper
    {
        public PyClassmethod(PyObject callable) : base(PyType.ClassMethodType, callable) { }

        internal PyClassmethod(PyType type, PyObject callable) : base(type, callable) { }

        // cm_descr_get
        public override PyObject Get(PyObject instance, PyType owner)
        {
            if (Callable == null)
                throw PyRuntimeError.Create("uninitialized classmethod object");
            PyObject type = owner ?? instance.GetPyType();
            var get = Callable is PyFunction ? null : Callable.LookupSpecialMethodUncached("__get__");
            if (get != null)
                return Callable.BindSpecialMethod(get).Call(new[] { type, type }, null);
            return new PyMethod(type, Callable);
        }

        public override bool IsCallable() => false;

        public override string ToString() => $"<classmethod({Callable?.ToRepr().Value})>";

        protected internal override PyString Repr() => new(ToString());
    }

    // Objects/funcobject.c: staticmethod
    public class PyStaticmethod : PyCallableWrapper
    {
        public PyStaticmethod(PyObject callable) : base(PyType.StaticMethodType, callable) { }

        internal PyStaticmethod(PyType type, PyObject callable) : base(type, callable) { }

        /// <summary>
        /// A METH_STATIC entry of a builtin's methods: add_methods wraps the function, created with
        /// the type as its self, in a staticmethod (Objects/typeobject.c).
        /// </summary>
        internal static PyStaticmethod ForBuiltin(PyType type, string name, System.Func<PyObject[], PyDict, PyObject> body) =>
            new(new PyStaticBuiltinMethod(name, body, type));

        // sm_descr_get
        public override PyObject Get(PyObject instance, PyType owner) =>
            Callable ?? throw PyRuntimeError.Create("uninitialized staticmethod object");

        public override bool IsCallable() => true;

        // sm_call
        public override PyObject Call(PyObject[] args, PyDict kwargs = null) => Callable.Call(args, kwargs);

        public override string ToString() => $"<staticmethod({Callable?.ToRepr().Value})>";

        protected internal override PyString Repr() => new(ToString());
    }
}
