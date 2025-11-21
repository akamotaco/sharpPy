using System;

namespace SharpPy
{
    /// <summary>
    /// Type-related extension methods.
    /// CPython 3.12: Objects/typeobject.c
    ///
    /// Provides convenient methods for type attribute lookup.
    /// </summary>
    public static class PyTypeExtensions
    {
        /// <summary>
        /// Try to get an attribute from a type's TypeDict or ClassDict.
        /// CPython 3.12: Objects/typeobject.c:4725 (_PyType_Lookup)
        /// </summary>
        /// <returns>True if found, false otherwise</returns>
        public static bool TryGetTypeAttr(this PyType type, string name, out PyObject value)
        {
            value = null;

            // For PyClass, check ClassDict
            if (type is PyClass pyClass)
            {
                return pyClass.ClassDict.TryGetValue(name, out value);
            }

            // For PyType, check TypeDict
            return type.TypeDict.TryGetValue(name, out value);
        }

        /// <summary>
        /// Check if a type has an attribute in its TypeDict or ClassDict.
        /// </summary>
        public static bool HasTypeAttr(this PyType type, string name)
        {
            return TryGetTypeAttr(type, name, out _);
        }

        /// <summary>
        /// Get type attribute, or null if not found.
        /// </summary>
        public static PyObject GetTypeAttrOrNull(this PyType type, string name)
        {
            TryGetTypeAttr(type, name, out PyObject value);
            return value;
        }

        /// <summary>
        /// Try to get an attribute from type's MRO chain.
        /// CPython 3.12: Objects/typeobject.c:4725 (_PyType_Lookup)
        /// </summary>
        public static bool TryGetMROAttr(this PyType type, string name, out PyObject value)
        {
            value = null;

            foreach (var mroType in type.MRO)
            {
                if (mroType is PyClass pyClass)
                {
                    if (pyClass.ClassDict.TryGetValue(name, out value))
                        return true;
                }
                else if (mroType.TypeDict.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if an attribute exists anywhere in type's MRO chain.
        /// </summary>
        public static bool HasMROAttr(this PyType type, string name)
        {
            return TryGetMROAttr(type, name, out _);
        }

        /// <summary>
        /// Get attribute from MRO chain, or null if not found.
        /// </summary>
        public static PyObject GetMROAttrOrNull(this PyType type, string name)
        {
            TryGetMROAttr(type, name, out PyObject value);
            return value;
        }

        /// <summary>
        /// Get the effective metaclass for a type.
        /// CPython 3.12: Objects/typeobject.c:2726 (get_proper_type)
        /// </summary>
        public static PyType GetMetaclass(this PyType type)
        {
            if (type is PyClass pyClass && pyClass.Metaclass != null)
            {
                return pyClass.Metaclass;
            }
            return PyTypeMetaclass.Instance;
        }

        /// <summary>
        /// Check if this type is a subclass of another type.
        /// Uses the MRO for efficient lookup.
        /// </summary>
        public static bool IsSubtypeOf(this PyType type, PyType other)
        {
            return MROUtils.Contains(type.MRO, other);
        }
    }
}
