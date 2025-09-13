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
    /// ABC - Helper base class (실제로는 일반 클래스여야 함)
    /// </summary>
    public class PyABC : PyClass
    {
        public PyABC() : base("ABC", new PyType[] { PyType.ObjectType }, null)
        {
            // CPython 3.12: ABC는 일반 클래스이므로 object.__init__을 상속받음
            // object.__init__ 메서드를 함수로 추가 (바인딩은 런타임에)
            SetAttribute("__init__", new PyBuiltinFunction("__init__", args =>
            {
                // object.__init__(self)는 아무것도 하지 않고 None 반환
                // self 인자가 첫 번째 인자로 자동 전달됨
                return PyNone.Instance;
            }));
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