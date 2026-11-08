using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// <c>_weakref</c> - the C module behind <c>weakref</c> (Modules/_weakref.c). CPython has no
    /// Lib/_weakref.c equivalent, so this is implemented natively, and Lib/weakref.py plus
    /// Lib/_weakrefset.py are copies of CPython's, which import this name.
    ///
    /// Measured on the reference interpreter 3.12.15, the public names are exactly:
    ///   CallableProxyType, ProxyType, ReferenceType, _remove_dead_weakref, getweakrefcount,
    ///   getweakrefs, proxy, ref
    /// with _weakref.ref is ReferenceType, __name__ 'ReferenceType', __module__ 'weakref'.
    /// </summary>
    internal static class WeakrefModule
    {
        internal static PyModule CreateWeakrefModule()
        {
            var module = new PyModule("_weakref", "<_weakref>");
            RegisterWeakRefType();

            module.ModuleDict["ref"] = PyType.WeakRefType;
            module.ModuleDict["ReferenceType"] = PyType.WeakRefType;

            module.ModuleDict["getweakrefcount"] = new PyBuiltinFunction("getweakrefcount", a =>
            {
                if (a.Length != 1)
                    throw PyTypeError.Create($"getweakrefcount() takes exactly one argument ({a.Length} given)");
                return PyInt.From(PyFinalizer.WeakReferencesOf(a[0]).Count);
            });

            module.ModuleDict["getweakrefs"] = new PyBuiltinFunction("getweakrefs", a =>
            {
                if (a.Length != 1)
                    throw PyTypeError.Create($"getweakrefs() takes exactly one argument ({a.Length} given)");
                var list = new PyList();
                foreach (var reference in PyFinalizer.WeakReferencesOf(a[0]))
                    list.Append(reference);
                return list;
            });

            // _remove_dead_weakref(dict, key): Lib/_weakrefset.py calls it from its reaper.
            module.ModuleDict["_remove_dead_weakref"] = new PyBuiltinFunction("_remove_dead_weakref", a =>
            {
                if (a.Length != 2)
                    throw PyTypeError.Create($"_remove_dead_weakref() takes exactly two arguments ({a.Length} given)");
                if (a[0] is PyDict dict && dict.GetItem(a[1]) is PyWeakRef reference)
                {
                    if (reference.Dead) dict.DelItem(a[1]);
                }
                return PyNone.Instance;
            });

            module.ModuleDict["proxy"] = new PyBuiltinFunction("proxy", a =>
            {
                if (a.Length is < 1 or > 2)
                    throw PyTypeError.Create($"proxy expected at most 2 arguments, got {a.Length}");
                return PyWeakProxy.Create(a[0], a.Length > 1 ? a[1] : PyNone.Instance);
            });
            PyWeakProxy.InstallTypes();
            module.ModuleDict["ProxyType"] = PyType.ProxyType;
            module.ModuleDict["CallableProxyType"] = PyType.CallableProxyType;

            return module;
        }

        /// <summary>
        /// The type object for weak references. Objects/weakrefobject.c:699 PyWeakReference_Type is
        /// the spec; tp_new rejects uncollectable targets with
        /// "cannot create weak reference to '%s' object" (Objects/_weakref.c:58).
        /// </summary>
        internal static void RegisterWeakRefType()
        {
            var type = PyType.WeakRefType;

            type.TypeDict["__new__"] = new PyNewMethodDescriptor(
                "__new__", type,
                (cls, args, kwargs) =>
                {
                    if (args.Length < 1)
                        throw PyTypeError.Create("__new__ expected at least 1 argument, got 0");
                    var target = args[0];
                    PyObject callback = args.Length > 1 ? args[1] : PyNone.Instance;
                    bool exact = !(cls is PyClass);
                    if (exact && args.Length > 2)
                        throw PyTypeError.Create($"__new__ expected at most 2 arguments, got {args.Length}");
                    if (exact && kwargs != null && kwargs.Length() > 0)
                        throw PyTypeError.Create("ref() takes no keyword arguments");
                    if (!target.GetPyType().SupportsWeakReference())
                        throw PyTypeError.Create(
                            $"cannot create weak reference to '{target.GetTypeName()}' object");
                    PyObject callbackObj = callback is PyNone ? null : callback;
                    if (exact && callbackObj == null)
                    {
                        foreach (var r in PyFinalizer.WeakReferencesOf(target))
                        {
                            if (r.Callback == null && r.IsExactly(PyType.WeakRefType))
                                return r;
                        }
                    }
                    PyWeakRef reference = exact
                        ? new PyWeakRef(target, callbackObj)
                        : new PyWeakRefSubclass((PyClass)cls, target, callbackObj);
                    PyFinalizer.TrackWeakReference(target, reference);
                    return reference;
                },
                minArgs: 0,
                maxArgs: int.MaxValue);

            // weakref___init__: no keywords, and the arguments __new__ took (PyArg_UnpackTuple 1..2).
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("ref() takes no keyword arguments");
                if (args.Length < 1)
                    throw PyTypeError.Create($"__init__ expected at least 1 argument, got {args.Length}");
                if (args.Length > 2)
                    throw PyTypeError.Create($"__init__ expected at most 2 arguments, got {args.Length}");
                return PyNone.Instance;
            });

            // tp_call, tp_repr, tp_hash and tp_richcompare are slots: their wrappers.
            type.TypeDict["__call__"] = new PyWrapperDescriptor("__call__", type,
                (self, args, kwargs) => ((PyWeakRef)self).SlotCall(args, kwargs));
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type,
                (self, args, kwargs) => ((PyWeakRef)self).SlotRepr(), minArgs: 0, maxArgs: 0);
            type.TypeDict["__hash__"] = new PyWrapperDescriptor("__hash__", type,
                (self, args, kwargs) => PyInt.From(((PyWeakRef)self).SlotHash()), minArgs: 0, maxArgs: 0);
            foreach (var (name, op) in new[]
            {
                ("__lt__", PyObject.CompareOp.LT), ("__le__", PyObject.CompareOp.LE), ("__eq__", PyObject.CompareOp.EQ),
                ("__ne__", PyObject.CompareOp.NE), ("__gt__", PyObject.CompareOp.GT), ("__ge__", PyObject.CompareOp.GE),
            })
            {
                var compareOp = op;
                type.TypeDict[name] = new PyWrapperDescriptor(name, type,
                    (self, args, kwargs) => ((PyWeakRef)self).SlotCompare(args[0], compareOp), minArgs: 1, maxArgs: 1);
            }

            // weakref_members (Objects/weakrefobject.c:356): {"__callback__", T_OBJECT, wr_callback, READONLY}.
            type.TypeDict["__callback__"] = new PyMemberDescriptor("__callback__", type,
                self =>
                {
                    var r = (PyWeakRef)self;
                    return r.Dead || r.Callback == null ? PyNone.Instance : r.Callback;
                });
        }
    }
}
