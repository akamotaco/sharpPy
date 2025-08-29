using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// collections.abc module implementation
    /// Contains abstract base classes for collections and protocols
    /// </summary>
    public static class CollectionsABC
    {
        /// <summary>
        /// Initialize collections.abc module with Buffer ABC (PEP 688)
        /// </summary>
        public static Dictionary<string, PyObject> GetModule()
        {
            var moduleDict = new Dictionary<string, PyObject>();

            // PEP 688: Buffer ABC
            moduleDict["Buffer"] = new PyBufferABC();

            return moduleDict;
        }
    }

    /// <summary>
    /// PEP 688: collections.abc.Buffer abstract base class
    /// Used for type checking and isinstance/issubclass checks
    /// </summary>
    public class PyBufferABC : PyType
    {
        public PyBufferABC() : base("Buffer", new[] { PyType.ObjectType })
        {
        }

        public override PyType GetPyType() => PyType.TypeType;

        /// <summary>
        /// Buffer ABC cannot be instantiated directly
        /// </summary>
        public override PyObject CreateInstance(params PyObject[] args)
        {
            throw PyTypeError.Create("Buffer is an abstract base class and cannot be instantiated directly");
        }

        /// <summary>
        /// Check if an object implements the buffer protocol
        /// Used by isinstance() and issubclass()
        /// </summary>
        public static bool IsBufferProtocol(PyObject obj)
        {
            // Check if object has __buffer__ method or implements buffer protocol
            return obj.SupportsBuffer() || HasBufferMethod(obj);
        }

        private static bool HasBufferMethod(PyObject obj)
        {
            try
            {
                var bufferMethod = obj.GetAttribute("__buffer__");
                return bufferMethod != null && bufferMethod.IsCallable();
            }
            catch (PythonException pe) when (pe.PyException is PyAttributeError)
            {
                return false;
            }
        }

        /// <summary>
        /// Register a type as implementing the Buffer protocol
        /// </summary>
        public void Register(PyType type)
        {
            // In a full implementation, this would add the type to a registry
            // For now, we'll use the SupportsBuffer() method
        }

        public override string ToString() => "<class 'collections.abc.Buffer'>";
        public override string ToRepr() => ToString();
    }
}