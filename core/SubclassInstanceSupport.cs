using System;
using System.Collections.Generic;

namespace SharpPy
{
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
        internal static PyString Repr(PyObject self, Func<PyString> inherited)
        {
            var declared = self.CallUserSpecialOrNull("__repr__", Array.Empty<PyObject>());
            if (declared == null)
                return inherited();
            if (!(declared is PyString text))
                throw PyTypeError.Create(
                    $"__repr__ returned non-string (type {declared.GetTypeName()})");
            return text;
        }

        internal static int Length(PyObject self, Func<int> inherited)
        {
            var declared = self.CallUserSpecialOrNull("__len__", Array.Empty<PyObject>());
            return declared != null ? PyObject.ToLengthResult(declared) : inherited();
        }

        /// <summary>sq_contains/mp_contains: a declared __contains__ replaces the slot, and its
        /// result is coerced to a bool the way sq_contains returns 0/1/-1.</summary>
        internal static PyBool Contains(PyObject self, PyObject item, Func<PyBool> inherited)
        {
            var declared = self.CallUserSpecialOrNull("__contains__", new[] { item });
            return declared != null ? PyBool.FromBool(declared.IsTrue()) : inherited();
        }

        internal static PyObject Iterate(PyObject self, Func<PyObject> inherited)
        {
            var declared = self.CallUserSpecialOrNull("__iter__", Array.Empty<PyObject>());
            return declared ?? inherited();
        }

        /// <summary>
        /// Objects/object.c:1675 PyObject_IsTrue: nb_bool, then sq_length/mp_length. A class that
        /// declares only __len__ still decides its own truthiness.
        /// </summary>
        internal static bool IsTrue(PyObject self, Func<bool> inherited)
        {
            var declaredBool = self.CallUserSpecialOrNull("__bool__", Array.Empty<PyObject>());
            if (declaredBool != null) return declaredBool.IsTrue();
            var declaredLength = self.CallUserSpecialOrNull("__len__", Array.Empty<PyObject>());
            if (declaredLength != null) return PyObject.ToLengthResult(declaredLength) != 0;
            return inherited();
        }

        internal static PyObject GetAttribute(PyObject self, PyClass instanceType,
            Dictionary<string, PyObject> instanceDict, string name, Func<string, PyObject> baseGetAttribute)
        {
            PyObject classAttribute = FirstClassDictEntry(instanceType, name);

            if (classAttribute is IDescriptor descriptor)
                return descriptor.Get(self, instanceType);
            if (classAttribute is PyFunction function)
                return new PyMethod(self, function);
            if (instanceDict.TryGetValue(name, out PyObject instanceValue))
                return instanceValue;
            if (classAttribute != null)
                return classAttribute;

            return baseGetAttribute(name);
        }

        internal static void SetAttribute(PyObject self, PyClass instanceType,
            Dictionary<string, PyObject> instanceDict, string name, PyObject value)
        {
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

            instanceDict[name] = value;
        }
    }
}
