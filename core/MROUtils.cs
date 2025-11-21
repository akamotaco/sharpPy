using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// MRO (Method Resolution Order) utility functions.
    /// CPython 3.12: Objects/typeobject.c - mro_* functions
    ///
    /// Consolidates MRO-related operations that were duplicated across:
    /// - PyObject.cs
    /// - PyClass.cs
    /// - PyType.cs
    /// - PyTypeMetaclass.cs
    /// </summary>
    public static class MROUtils
    {
        /// <summary>
        /// Convert MRO list to PyTuple for Python access.
        /// CPython 3.12: Objects/typeobject.c:2261 (type_get_mro)
        /// </summary>
        public static PyTuple ToTuple(IReadOnlyList<PyType> mro)
        {
            var mroArray = new PyObject[mro.Count];
            for (int i = 0; i < mro.Count; i++)
            {
                mroArray[i] = mro[i];
            }
            return new PyTuple(mroArray);
        }

        /// <summary>
        /// Convert MRO list to PyList for Python access.
        /// </summary>
        public static PyList ToList(IReadOnlyList<PyType> mro)
        {
            var mroList = new List<PyObject>(mro.Count);
            for (int i = 0; i < mro.Count; i++)
            {
                mroList.Add(mro[i]);
            }
            return new PyList(mroList);
        }

        /// <summary>
        /// Get MRO names as string array (for debugging/display).
        /// </summary>
        public static string[] GetNames(IReadOnlyList<PyType> mro)
        {
            var names = new string[mro.Count];
            for (int i = 0; i < mro.Count; i++)
            {
                names[i] = mro[i].Name;
            }
            return names;
        }

        /// <summary>
        /// Lookup attribute in MRO chain.
        /// CPython 3.12: Objects/typeobject.c:4725 (_PyType_Lookup)
        /// </summary>
        public static PyObject LookupAttribute(IReadOnlyList<PyType> mro, string name)
        {
            foreach (var mroType in mro)
            {
                // For PyClass, check ClassDict
                if (mroType is PyClass pyClass)
                {
                    if (pyClass.ClassDict.TryGetValue(name, out PyObject value))
                    {
                        return value;
                    }
                }
                // For PyType, check TypeDict
                else if (mroType.TypeDict.TryGetValue(name, out PyObject typeValue))
                {
                    return typeValue;
                }
            }
            return null;
        }

        /// <summary>
        /// Check if a type is in the MRO chain.
        /// CPython 3.12: Objects/typeobject.c:1605 (type_is_subtype_base_chain)
        /// </summary>
        public static bool Contains(IReadOnlyList<PyType> mro, PyType targetType)
        {
            foreach (var mroType in mro)
            {
                if (ReferenceEquals(mroType, targetType))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Find index of type in MRO, or -1 if not found.
        /// </summary>
        public static int IndexOf(IReadOnlyList<PyType> mro, PyType targetType)
        {
            for (int i = 0; i < mro.Count; i++)
            {
                if (ReferenceEquals(mro[i], targetType))
                    return i;
            }
            return -1;
        }
    }
}
