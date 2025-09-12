using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.Modules
{
    /// <summary>
    /// abc module implementation - Abstract Base Classes infrastructure
    /// Provides ABCMeta, ABC, and @abstractmethod for CPython 3.12 compatibility
    /// </summary>
    public static class AbcModule
    {
        /// <summary>
        /// Initialize abc module with core ABC infrastructure
        /// </summary>
        public static Dictionary<string, PyObject> GetModule()
        {
            var moduleDict = new Dictionary<string, PyObject>();

            // Core ABC infrastructure - simplified for now
            moduleDict["ABCMeta"] = new PyABCMeta();
            moduleDict["ABC"] = new PyABC();
            moduleDict["abstractmethod"] = new PyAbstractMethodDecorator();

            return moduleDict;
        }
    }

    /// <summary>
    /// ABCMeta - Simplified metaclass for ABC (basic functionality)
    /// </summary>
    public class PyABCMeta : PyType
    {
        public PyABCMeta() : base("ABCMeta", new PyType[] { PyType.TypeType })
        {
        }
    }

    /// <summary>
    /// ABC - Helper base class
    /// </summary>
    public class PyABC : PyType
    {
        public PyABC() : base("ABC", new PyType[] { PyType.ObjectType })
        {
        }
    }

    /// <summary>
    /// @abstractmethod decorator - simplified version
    /// </summary>
    public class PyAbstractMethodDecorator : PyBuiltinFunction
    {
        public PyAbstractMethodDecorator() : base("abstractmethod")
        {
        }

        public override PyObject Call(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("abstractmethod() takes exactly one argument");

            var func = args[0];
            
            // Mark function as abstract (basic implementation)
            if (func is PyFunction pyFunc)
            {
                pyFunc.SetAttribute("__isabstractmethod__", PyBool.True);
            }
            
            return func;
        }
    }
}