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
