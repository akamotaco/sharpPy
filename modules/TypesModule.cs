using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// Python types module implementation
    /// Contains UnionType and other type-related utilities
    /// </summary>
    public static class TypesModule
    {
        public static PyModule CreateTypesModule()
        {
            var module = new PyModule("types", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\types.py");

            // UnionType class (created by | operator)
            module.ModuleDict["UnionType"] = PyType.UnionType;

            // Add other types module contents as needed
            module.ModuleDict["SimpleNamespace"] = new PySimpleNamespaceType();
            module.ModuleDict["GenericAlias"] = PyType.GenericAliasType;

            return module;
        }
    }

    /// <summary>
    /// Simple namespace type for types.SimpleNamespace
    /// </summary>
    public class PySimpleNamespaceType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;
        public override string ToString() => "<class 'types.SimpleNamespace'>";
    }
}