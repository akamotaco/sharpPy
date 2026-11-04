using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// PyCFunctionObject (Include/cpython/methodobject.h): m_ml->ml_name, m_self and m_module. Every
    /// object whose type is builtin_function_or_method carries these three, and the type's
    /// meth_getsets and meth_members (Objects/methodobject.c) read nothing else.
    /// </summary>
    internal interface ICFunction
    {
        string MethodName { get; }

        /// <summary>m_self; null is NULL.</summary>
        PyObject MethodSelf { get; }

        /// <summary>m_module as the function was made; null is NULL.</summary>
        PyObject MethodModule { get; }

        /// <summary>METH_STATIC: PyCFunction_GET_SELF reads NULL although m_self holds the type.</summary>
        bool MethodIsStatic => false;
    }

    /// <summary>
    /// PyDescrObject (Include/descrobject.h): d_type and d_name, the head every method, classmethod,
    /// wrapper, getset and member descriptor shares. descr_members and descr_getset read only these.
    /// </summary>
    internal interface IDescrObject
    {
        string DescrName { get; }
        PyType DescrType { get; }
    }

    internal static class PyDescrObject
    {
        private static IDescrObject Of(PyObject self) => (IDescrObject)self;

        // descr_get_qualname / calculate_qualname: d_type.__qualname__ + "." + d_name.
        internal static PyObject QualName(PyObject self)
        {
            var descr = Of(self);
            return new PyString(descr.DescrType != null
                ? $"{PyBuiltinFunction.QualifiedClassLabel(descr.DescrType)}.{descr.DescrName}"
                : descr.DescrName);
        }

        internal static void Install(PyType type)
        {
            type.TypeDict["__name__"] = new PyMemberDescriptor("__name__", type, self => new PyString(Of(self).DescrName), readOnly: true);
            type.TypeDict["__objclass__"] = new PyMemberDescriptor("__objclass__", type,
                self => (PyObject)Of(self).DescrType ?? PyNone.Instance, readOnly: true);
            type.TypeDict["__qualname__"] = new PyGetSetDescriptor("__qualname__", type, QualName);
            // descr_methods, which method, member and wrapper descriptors have: getattr(d_type, d_name).
            if (ReferenceEquals(type, PyType.MethodDescriptorType) || ReferenceEquals(type, PyType.MemberDescriptorType)
                || ReferenceEquals(type, PyType.WrapperDescriptorType))
                type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type, (self, args, kwargs) =>
                    new PyTuple(new PyObject[]
                    {
                        PyImportSystem.Import("builtins").GetAttribute("getattr"),
                        new PyTuple(new PyObject[] { Of(self).DescrType, new PyString(Of(self).DescrName) }),
                    }), 0, 0);
        }
    }

    /// <summary>wrapperobject (Objects/descrobject.c): a wrapper descriptor and the self it is bound to.</summary>
    internal interface IWrapperObject
    {
        IDescrObject WrapperDescr { get; }
        PyObject WrapperSelf { get; }
    }

    /// <summary>wrapper_getsets, wrapper_members and wrapper_methods.</summary>
    internal static class PyMethodWrapper
    {
        private static IWrapperObject Of(PyObject self) => (IWrapperObject)self;

        internal static void Install()
        {
            var type = PyType.MethodWrapperType;
            type.TypeDict["__objclass__"] = new PyGetSetDescriptor("__objclass__", type,
                self => (PyObject)Of(self).WrapperDescr.DescrType ?? PyNone.Instance);
            type.TypeDict["__name__"] = new PyGetSetDescriptor("__name__", type, self => new PyString(Of(self).WrapperDescr.DescrName));
            type.TypeDict["__qualname__"] = new PyGetSetDescriptor("__qualname__", type,
                self => PyDescrObject.QualName((PyObject)Of(self).WrapperDescr));
            type.TypeDict["__self__"] = new PyMemberDescriptor("__self__", type, self => Of(self).WrapperSelf, readOnly: true);
            // wrapper_reduce: getattr(self, name).
            type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type, (self, args, kwargs) =>
                new PyTuple(new PyObject[]
                {
                    PyImportSystem.Import("builtins").GetAttribute("getattr"),
                    new PyTuple(new PyObject[] { Of(self).WrapperSelf, new PyString(Of(self).WrapperDescr.DescrName) }),
                }), 0, 0);
        }
    }

    /// <summary>The getsets, members and methods of builtin_function_or_method (Objects/methodobject.c).</summary>
    internal static class PyCFunction
    {
        // m_module is a writable T_OBJECT member: what an assignment stored replaces what the function was made with.
        private sealed class Slot { internal PyObject Value; }
        private static readonly ConditionalWeakTable<PyObject, Slot> WrittenModule = new();

        private static ICFunction Of(PyObject self) => (ICFunction)self;

        // meth_get__self__
        internal static PyObject Self(PyObject self) =>
            (Of(self).MethodIsStatic ? null : Of(self).MethodSelf) ?? PyNone.Instance;

        // meth_get__qualname__: the bare name for no self or a module; otherwise the qualname of the
        // self's type, or of the self when it is a type.
        internal static PyObject QualName(PyObject self)
        {
            var function = Of(self);
            var bound = function.MethodSelf;
            if (bound == null || bound is PyModule)
                return new PyString(function.MethodName);
            var type = bound as PyType ?? bound.GetPyType();
            return new PyString($"{PyBuiltinFunction.QualifiedClassLabel(type)}.{function.MethodName}");
        }

        internal static PyObject Module(PyObject self) =>
            WrittenModule.TryGetValue(self, out var slot) ? slot.Value ?? PyNone.Instance : Of(self).MethodModule ?? PyNone.Instance;

        internal static void SetModule(PyObject self, PyObject value) => WrittenModule.GetOrCreateValue(self).Value = value;

        // meth_reduce: a function of a module pickles by name, a method as getattr(self, name).
        internal static PyObject Reduce(PyObject self)
        {
            var function = Of(self);
            var bound = function.MethodSelf;
            if (bound == null || bound is PyModule)
                return new PyString(function.MethodName);
            var getattr = PyImportSystem.Import("builtins").GetAttribute("getattr");
            return new PyTuple(new PyObject[] { getattr, new PyTuple(new PyObject[] { bound, new PyString(function.MethodName) }) });
        }

        internal static void Install()
        {
            var type = PyType.BuiltinFunctionOrMethodType;
            type.TypeDict["__name__"] = new PyGetSetDescriptor("__name__", type, self => new PyString(Of(self).MethodName));
            type.TypeDict["__qualname__"] = new PyGetSetDescriptor("__qualname__", type, QualName);
            type.TypeDict["__self__"] = new PyGetSetDescriptor("__self__", type, Self);
            type.TypeDict["__module__"] = new PyMemberDescriptor("__module__", type, Module, SetModule,
                deleter: self => SetModule(self, null));
            type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type, (self, args, kwargs) => Reduce(self), 0, 0);
        }
    }
}
