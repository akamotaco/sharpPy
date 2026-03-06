using System;
using System.Collections.Generic;

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

            // PEP 604: UnionType class (created by | operator)
            module.ModuleDict["UnionType"] = PyType.UnionType;

            // Other type utilities
            module.ModuleDict["SimpleNamespace"] = PySimpleNamespaceType.Instance;
            module.ModuleDict["GenericAlias"] = PyType.GenericAliasType;

            // CPython 3.12: types module is implemented in Python (stdlib/types.py)
            // This C# module only provides types that cannot be created in Python

            return module;
        }
    }

    /// <summary>
    /// Simple namespace type for types.SimpleNamespace
    /// CPython 3.12: Objects/namespaceobject.c:20-250 (SimpleNamespace_Type)
    /// </summary>
    public class PySimpleNamespaceType : PyType
    {
        public static readonly PySimpleNamespaceType Instance = new PySimpleNamespaceType();

        private PySimpleNamespaceType() : base("SimpleNamespace", new PyType[] { PyType.ObjectType }, "types")
        {
        }

        public override string ToString() => "<class 'types.SimpleNamespace'>";

        /// <summary>
        /// CPython 3.12: Objects/namespaceobject.c:38-70 (namespace_new)
        /// </summary>
        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            // SimpleNamespace() accepts only keyword arguments
            if (args.Length > 0)
                throw PyTypeError.Create("SimpleNamespace() takes no positional arguments");

            return new PySimpleNamespace(kwargs);
        }
    }

    /// <summary>
    /// SimpleNamespace instance
    /// CPython 3.12: Objects/namespaceobject.c (namespace_* functions)
    /// </summary>
    public class PySimpleNamespace : PyObject
    {
        private readonly Dictionary<string, PyObject> _attrs;

        public PySimpleNamespace(PyDict kwargs = null)
        {
            _attrs = new Dictionary<string, PyObject>();
            if (kwargs != null)
            {
                foreach (var kvp in kwargs.InternalDict)
                {
                    var key = kvp.Key is PyStr ps ? ps.Value : kvp.Key.ToString();
                    _attrs[key] = kvp.Value;
                }
            }
        }

        public override PyType GetPyType() => PySimpleNamespaceType.Instance;
        public override string GetTypeName() => "SimpleNamespace";

        /// <summary>
        /// CPython 3.12: Objects/namespaceobject.c:72-100 (namespace_getattro)
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            // Check stored attributes first
            if (_attrs.TryGetValue(name, out var value))
                return value;

            // Special attributes
            if (name == "__dict__")
            {
                var dictItems = new Dictionary<PyObject, PyObject>();
                foreach (var kvp in _attrs)
                    dictItems[new PyStr(kvp.Key)] = kvp.Value;
                return new PyDict(dictItems);
            }
            if (name == "__class__")
                return PySimpleNamespaceType.Instance;

            throw PyAttributeError.Create($"'SimpleNamespace' object has no attribute '{name}'");
        }

        /// <summary>
        /// CPython 3.12: Objects/namespaceobject.c:102-130 (namespace_setattro)
        /// </summary>
        public override void SetAttribute(string name, PyObject value)
        {
            _attrs[name] = value;
        }

        /// <summary>
        /// CPython 3.12: Objects/namespaceobject.c:132-160 (namespace_delattro)
        /// </summary>
        public override void DelAttribute(string name)
        {
            if (!_attrs.Remove(name))
                throw PyAttributeError.Create($"'SimpleNamespace' object has no attribute '{name}'");
        }

        /// <summary>
        /// CPython 3.12: Objects/namespaceobject.c:162-220 (namespace_repr)
        /// </summary>
        public override string ToString()
        {
            var parts = new List<string>();
            foreach (var kvp in _attrs)
            {
                // Get repr string value properly
                var reprStr = kvp.Value.ToRepr();
                var reprValue = reprStr is PyStr ps ? ps.Value : reprStr.ToString();
                parts.Add($"{kvp.Key}={reprValue}");
            }
            return $"namespace({string.Join(", ", parts)})";
        }

        public override PyStr ToRepr()
        {
            return new PyStr(ToString());
        }

        /// <summary>
        /// CPython 3.12: Objects/namespaceobject.c:222-250 (namespace_richcompare)
        /// </summary>
        public override bool Equals(object other)
        {
            if (other is PySimpleNamespace otherNs)
            {
                return CompareNamespaces(otherNs);
            }
            return false;
        }

        private bool CompareNamespaces(PySimpleNamespace other)
        {
            if (_attrs.Count != other._attrs.Count)
                return false;
            foreach (var kvp in _attrs)
            {
                if (!other._attrs.TryGetValue(kvp.Key, out var otherValue))
                    return false;
                // Use PyObject comparison for proper Python semantics
                var cmp = kvp.Value.RichCompare(otherValue, PyObject.CompareOp.EQ);
                if (cmp is PyBool b && !b.IsTrue())
                    return false;
                if (cmp == PyNone.Instance || cmp == PyNotImplemented.Instance)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// CPython 3.12: Objects/namespaceobject.c:222-250 (namespace_richcompare)
        /// Override RichCompare for proper Python == operator support
        /// </summary>
        public override PyObject RichCompare(PyObject other, PyObject.CompareOp op)
        {
            if (op == PyObject.CompareOp.EQ)
            {
                if (other is PySimpleNamespace otherNs)
                    return PyBool.FromBool(CompareNamespaces(otherNs));
                return PyBool.False;
            }
            if (op == PyObject.CompareOp.NE)
            {
                if (other is PySimpleNamespace otherNs)
                    return PyBool.FromBool(!CompareNamespaces(otherNs));
                return PyBool.True;
            }
            return PyNotImplemented.Instance;
        }

        public override int GetHashCode()
        {
            return _attrs.GetHashCode();
        }
    }
}