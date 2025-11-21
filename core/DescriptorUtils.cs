using System;

namespace SharpPy
{
    /// <summary>
    /// Descriptor Protocol utility functions.
    /// CPython 3.12: Objects/descrobject.c
    ///
    /// Consolidates descriptor-related operations that were duplicated across:
    /// - PyObject.cs (GetAttribute/SetAttribute)
    /// - PyClass.cs
    /// - PyType.cs
    /// </summary>
    public static class DescriptorUtils
    {
        /// <summary>
        /// Check if an object is a descriptor (has __get__).
        /// CPython 3.12: Objects/typeobject.c:3172 (Py_TYPE(descr)->tp_descr_get)
        /// </summary>
        public static bool IsDescriptor(PyObject obj)
        {
            // C# interface check
            if (obj is IDescriptor)
                return true;

            // Python-level descriptor check (__get__ method)
            try
            {
                obj.GetAttribute("__get__");
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if an object is a data descriptor (has __set__ or __delete__).
        /// CPython 3.12: Objects/typeobject.c:3175 (Py_TYPE(descr)->tp_descr_set)
        /// Data descriptors take precedence over instance dictionaries.
        /// </summary>
        public static bool IsDataDescriptor(PyObject obj)
        {
            // C# interface check
            if (obj is IDescriptor descriptor)
                return descriptor.IsDataDescriptor();

            // Python-level data descriptor check (__set__ or __delete__)
            try
            {
                obj.GetAttribute("__set__");
                return true;
            }
            catch { }

            try
            {
                obj.GetAttribute("__delete__");
                return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Invoke descriptor __get__ method.
        /// CPython 3.12: Objects/typeobject.c:3282 (descr->ob_type->tp_descr_get)
        /// </summary>
        public static PyObject InvokeGet(PyObject descriptor, PyObject instance, PyType owner)
        {
            // C# interface
            if (descriptor is IDescriptor desc)
            {
                return desc.Get(instance, owner);
            }

            // Python-level __get__
            try
            {
                var getMethod = descriptor.GetAttribute("__get__");
                if (getMethod != null && getMethod.IsCallable())
                {
                    return getMethod.Call(new PyObject[] { instance, owner }, null);
                }
            }
            catch { }

            // Not a descriptor, return as-is
            return descriptor;
        }

        /// <summary>
        /// Invoke descriptor __set__ method.
        /// CPython 3.12: Objects/typeobject.c:3283 (descr->ob_type->tp_descr_set)
        /// </summary>
        public static void InvokeSet(PyObject descriptor, PyObject instance, PyObject value)
        {
            // C# interface
            if (descriptor is IDescriptor desc)
            {
                desc.Set(instance, value);
                return;
            }

            // Python-level __set__
            try
            {
                var setMethod = descriptor.GetAttribute("__set__");
                if (setMethod != null && setMethod.IsCallable())
                {
                    setMethod.Call(new PyObject[] { instance, value }, null);
                    return;
                }
            }
            catch { }

            throw PyAttributeError.Create($"'{descriptor.GetTypeName()}' object has no attribute '__set__'");
        }

        /// <summary>
        /// Invoke descriptor __delete__ method.
        /// CPython 3.12: Objects/typeobject.c:3284 (descr->ob_type->tp_descr_set with NULL value)
        /// </summary>
        public static void InvokeDelete(PyObject descriptor, PyObject instance)
        {
            // C# interface
            if (descriptor is IDescriptor desc)
            {
                desc.Delete(instance);
                return;
            }

            // Python-level __delete__
            try
            {
                var deleteMethod = descriptor.GetAttribute("__delete__");
                if (deleteMethod != null && deleteMethod.IsCallable())
                {
                    deleteMethod.Call(new PyObject[] { instance }, null);
                    return;
                }
            }
            catch { }

            throw PyAttributeError.Create($"'{descriptor.GetTypeName()}' object has no attribute '__delete__'");
        }

        /// <summary>
        /// Get attribute with full descriptor protocol support.
        /// CPython 3.12: Objects/object.c:1293 (PyObject_GenericGetAttr)
        /// </summary>
        public static PyObject GetAttributeWithDescriptor(
            PyObject obj,
            string name,
            PyObject typeAttr,
            Func<string, PyObject> instanceDictLookup)
        {
            // 1. Data descriptor from type takes precedence
            if (typeAttr != null && IsDataDescriptor(typeAttr))
            {
                return InvokeGet(typeAttr, obj, obj.GetPyType());
            }

            // 2. Instance dictionary lookup
            var instValue = instanceDictLookup?.Invoke(name);
            if (instValue != null)
            {
                return instValue;
            }

            // 3. Non-data descriptor or plain type attribute
            if (typeAttr != null)
            {
                if (IsDescriptor(typeAttr))
                {
                    return InvokeGet(typeAttr, obj, obj.GetPyType());
                }
                return typeAttr;
            }

            return null;
        }
    }
}
