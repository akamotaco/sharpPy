using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// What classmethod and staticmethod share in Objects/funcobject.c: the wrapped callable, a
    /// __dict__ filled by functools_wraps, and a type that may be a Python subclass.
    /// </summary>
    public abstract class PyCallableWrapper : PyObject, IDescriptor, IDictBackedInstance
    {
        private static readonly string[] WrappedAttributes = { "__module__", "__name__", "__qualname__", "__doc__" };

        private readonly PyType _type;

        public PyObject Callable { get; private set; }
        public Dictionary<string, PyObject> InstanceDict { get; set; } = new();

        protected PyCallableWrapper(PyType type, PyObject callable)
        {
            _type = type;
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
                    InstanceDict[name] = callable.GetAttribute(name);
                }
                catch (PythonException error) when (error.PyException is PyAttributeError)
                {
                }
            }
        }

        public override PyType GetPyType() => _type;
        public override string GetTypeName() => _type.Name;

        // cm_dict: generic attribute access over the wrapper's own __dict__.
        private bool TypeHasDataDescriptor(string name) =>
            _type.LookupSpecial(name) is IDescriptor descriptor && descriptor.IsDataDescriptor();

        public override PyObject GetAttribute(string name)
        {
            if (!TypeHasDataDescriptor(name) && InstanceDict.TryGetValue(name, out var value))
                return value;
            return base.GetAttribute(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            if (name == "__dict__" || TypeHasDataDescriptor(name))
                base.SetAttribute(name, value);
            else
                InstanceDict[name] = value;
        }

        public override void DelAttribute(string name)
        {
            if (name == "__dict__" || TypeHasDataDescriptor(name))
                base.DelAttribute(name);
            else if (!InstanceDict.Remove(name))
                throw PyAttributeError.Create(name);
        }

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
            type.TypeDict["__func__"] = new PyGetSetDescriptor("__func__", type,
                self => ((PyCallableWrapper)self).Callable, null);
            type.TypeDict["__wrapped__"] = new PyGetSetDescriptor("__wrapped__", type,
                self => ((PyCallableWrapper)self).Callable, null);
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
            type.ConstructedByNewSlot = true;
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

        public override PyString ToRepr() => new(ToString());
    }

    // Objects/funcobject.c: staticmethod
    public class PyStaticmethod : PyCallableWrapper
    {
        public PyStaticmethod(PyObject callable) : base(PyType.StaticMethodType, callable) { }

        internal PyStaticmethod(PyType type, PyObject callable) : base(type, callable) { }

        // sm_descr_get
        public override PyObject Get(PyObject instance, PyType owner) =>
            Callable ?? throw PyRuntimeError.Create("uninitialized staticmethod object");

        public override bool IsCallable() => true;

        // sm_call
        public override PyObject Call(PyObject[] args, PyDict kwargs = null) => Callable.Call(args, kwargs);

        public override string ToString() => $"<staticmethod({Callable?.ToRepr().Value})>";

        public override PyString ToRepr() => new(ToString());
    }
}
