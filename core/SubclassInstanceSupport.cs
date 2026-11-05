using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>An instance of a Python class over a builtin value: __class__ can change.</summary>
    internal interface IHeapInstance
    {
        void ChangeClass(PyClass target);
    }

    /// <summary>
    /// Attribute access for instances of a builtin type that a Python class derives from
    /// (str/bytes/tuple/int/float subclasses). Their class dicts hold user descriptors and functions,
    /// which the CLR inheritance chain does not see, so MRO lookup plus the instance __dict__ has to
    /// run before falling back to the payload type's own behaviour.
    ///
    /// Shared by PyTupleSubclass and PyBytesSubclass. C# single inheritance prevents a common base
    /// class here (the payload types differ), which is why this is a helper rather than a superclass.
    /// </summary>
    internal static class SubclassInstanceSupport
    {
        internal static PyObject FirstClassDictEntry(PyClass instanceType, string name)
        {
            foreach (var mroType in instanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject value))
                    return value;
            }
            return null;
        }

        // --- inherited slots versus a declared dunder --------------------------------------
        // CPython decides once, in update_one_slot: if the class dict names __len__, __repr__,
        // __contains__..., the slot becomes a wrapper that calls that function; otherwise the slot
        // stays the base's C function. A CLR subclass of PyList/PyDict/PySet inherits the base's C#
        // behaviour instead, so the same decision is needed before each protocol method. Written
        // once here because C# single inheritance gives the four container subclasses no common
        // superclass (Objects/typeobject.c: update_one_slot).

        /// <summary>
        /// tp_repr is inherited, so a subclass prints its contents ([1, 2], {'a': 1}); a class that
        /// declares __repr__ replaces the slot. CPython also insists the declared function returned
        /// a str, and says so when it did not.
        /// </summary>
        // object_set_class
        internal static PyClass ClassAssignment(PyClass current, PyObject value)
        {
            if (value is not PyType target)
                throw PyTypeError.Create($"__class__ must be set to a class, not '{value.GetTypeName()}' object");
            if (target is not PyClass heapTarget || target is PyTypeMetaclass)
                throw PyTypeError.Create("__class__ assignment only supported for mutable types or ModuleType subclasses");
            PyTypeMetaclass.CheckAssignment(current, heapTarget, "__class__");
            return heapTarget;
        }

        // PyNumber_Long: nb_int, then nb_index, and the result is an int.
        internal static PyInt Int(PyObject self)
        {
            var method = self.LookupSpecialMethodUncached("__int__");
            if (method != null)
            {
                var result = self.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
                if (result is PyInt exact)
                    return exact;
                // PyNumber_Long: a strict subclass of int still answers, with a warning.
                if (result is PyBool or PyIntSubclass)
                {
                    PyWarnings.Deprecated($"__int__ returned non-int (type {result.GetTypeName()}).  The ability to return an instance of a strict subclass of int is deprecated, and may be removed in a future version of Python.");
                    return result is PyBool truth ? new PyInt(truth.Value ? 1 : 0) : ((PyIntSubclass)result).GetIntValue();
                }
                throw PyTypeError.Create($"__int__ returned non-int (type {result.GetTypeName()})");
            }
            var index = PyClassInstance.TryGetIndex(self);
            if (index != null) return index;
            // PyNumber_Long: __trunc__, deprecated and still honoured in 3.12.
            var truncate = self.LookupSpecialMethodUncached("__trunc__");
            if (truncate != null)
            {
                PyWarnings.Deprecated("The delegation of int() to __trunc__ is deprecated.");
                var whole = self.BindSpecialMethod(truncate).Call(Array.Empty<PyObject>(), null);
                switch (whole)
                {
                    case PyInt exact: return exact;
                    case PyBool truth: return new PyInt(truth.Value ? 1 : 0);
                    case PyIntSubclass subclass: return subclass.GetIntValue();
                }
                return PyClassInstance.TryGetIndex(whole) ??
                    throw PyTypeError.Create($"__trunc__ returned non-Integral (type {whole.GetTypeName()})");
            }
            throw PyTypeError.Create(
                $"int() argument must be a string, a bytes-like object or a real number, not '{self.GetTypeName()}'");
        }

        // PyNumber_Float: nb_float, then nb_index. PyInt.AsFloat owns the overflow rule.
        internal static PyFloat Float(PyObject self)
        {
            var method = self.LookupSpecialMethodUncached("__float__");
            if (method != null)
            {
                return PyFloat.CheckFloatResult(self, self.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null));
            }
            return PyClassInstance.TryGetIndex(self)?.AsFloat() ?? throw PyTypeError.Create(
                $"float() argument must be a string or a real number, not '{self.GetTypeName()}'");
        }

        /// <summary>sq_contains/mp_contains: a declared __contains__ replaces the slot, and its
        /// result is coerced to a bool the way sq_contains returns 0/1/-1.</summary>
        internal static PyBool Contains(PyObject self, PyObject item, Func<PyBool> inherited)
        {
            var declared = self.CallUserSpecialOrNull("__contains__", new[] { item });
            return declared != null ? PyBool.FromBool(declared.IsTrue()) : inherited();
        }

        /// <summary>
        /// tp_getattro of a Python class over a builtin: slot_tp_getattr_hook (Objects/typeobject.c)
        /// calls a __getattribute__ the class writes, or PyObject_GenericGetAttr (self.GenericGetAttr),
        /// and __getattr__ when that raises AttributeError.
        /// </summary>
        internal static PyObject GetAttribute(PyObject self, PyClass instanceType, string name)
        {
            var getattribute = instanceType.FindNameInMro("__getattribute__", out bool written);
            var hook = instanceType.FindNameInMro("__getattr__");
            if (hook == null)
                return written ? CallHook(self, getattribute, name) : self.GenericGetAttr(name);
            try
            {
                return written ? CallHook(self, getattribute, name) : self.GenericGetAttr(name);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return CallHook(self, hook, name);
            }
        }

        private static PyObject CallHook(PyObject self, PyObject method, string name) =>
            self.BindSpecialMethod(method).Call(new PyObject[] { new PyString(name) }, null);

        /// <summary>
        /// _PyObject_GenericGetAttrWithDict (Objects/object.c): a data descriptor of the type, then the
        /// instance dict, then a non-data descriptor or plain attribute of the type. Only what a
        /// Python class body holds is answered here; a builtin's attribute goes to nativeGetAttribute,
        /// the builtin's own tp_getattro.
        /// </summary>
        internal static PyObject GenericGetAttribute(PyObject self, PyClass instanceType,
            Dictionary<string, PyObject> instanceDict, string name, Func<string, PyObject> nativeGetAttribute)
        {
            // A heap class adds a __dict__ after the base's layout; the live view over the same store.
            if (name == "__dict__" && instanceType.InstancesHaveDict())
                return PyGlobalsDict.For(instanceDict);

            var attribute = instanceType.FindNameInMro(name, out bool inClass);
            IDescriptor descriptor = attribute as IDescriptor
                ?? (attribute != null && attribute is not PyFunction && attribute.LookupSpecialMethodUncached("__get__") != null
                    ? new PyDescriptorWrapper(attribute) : null);
            if (descriptor != null && descriptor.IsDataDescriptor())
                return descriptor.Get(self, instanceType);
            if (instanceDict.TryGetValue(name, out PyObject instanceValue))
                return instanceValue;
            if (inClass)
            {
                if (attribute is PyFunction function)
                    return new PyMethod(self, function);
                return descriptor != null ? descriptor.Get(self, instanceType) : attribute;
            }
            return nativeGetAttribute(name);
        }

        internal static void SetAttribute(PyObject self, PyClass instanceType,
            Dictionary<string, PyObject> instanceDict, string name, PyObject value)
        {
            if (name == "__class__" && self is IHeapInstance heap)
            {
                heap.ChangeClass(ClassAssignment(instanceType, value));
                return;
            }
            // object_set_dict: the storage stays, __dict__ is a live view of it.
            if (name == "__dict__")
            {
                var replacement = PyNativeStorage.ForDict(value)
                    ?? throw PyTypeError.Create($"__dict__ must be set to a dictionary, not a '{value.GetTypeName()}'");
                var copy = new List<KeyValuePair<PyObject, PyObject>>(replacement.InternalDict);
                instanceDict.Clear();
                foreach (var entry in copy) PyGlobalsDict.Store(instanceDict, ((PyString)entry.Key).Value, entry.Value);
                return;
            }
            foreach (var mroType in instanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    if (classValue is IDescriptor descriptor && descriptor.IsDataDescriptor())
                    {
                        descriptor.Set(self, value);
                        return;
                    }
                    break;
                }
            }

            if (!instanceType.InstancesHaveDict())
                throw PyAttributeError.Create($"'{instanceType.Name}' object has no attribute '{name}'");
            PyGlobalsDict.Store(instanceDict, name, value);
        }
    }
}
