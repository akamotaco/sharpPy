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

    /// <summary>types.SimpleNamespace (Objects/namespaceobject.c): a dict and nothing else.</summary>
    public sealed class PySimpleNamespaceType : PyType
    {
        public static readonly PySimpleNamespaceType Instance = new PySimpleNamespaceType();

        internal override string FullName => "types.SimpleNamespace";

        private PySimpleNamespaceType() : base("SimpleNamespace", new PyType[] { PyType.ObjectType }, "types")
        {
            ConstructedByNewSlot = true;
            TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", this,
                (cls, args, kwargs) => new PySimpleNamespace((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            TypeDict["__init__"] = new PyWrapperDescriptor("__init__", this, (self, args, kwargs) =>
            {
                if (args.Length != 0)
                    throw PyTypeError.Create("no positional arguments expected");
                ((PySimpleNamespace)self).Update(kwargs);
                return PyNone.Instance;
            });
            TypeDict["__dict__"] = new PyMemberDescriptor("__dict__", this, self => ((PySimpleNamespace)self).Dict);
            TypeDict["__hash__"] = PyNone.Instance;
            TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", this, (self, args, kwargs) => new PyTuple(new PyObject[]
            {
                self.GetPyType(), new PyTuple(Array.Empty<PyObject>()), ((PySimpleNamespace)self).Dict,
            }));
            TypeTables.Repr(this);
            TypeTables.Compared(this);
        }
    }

    public sealed class PySimpleNamespace : PyObject, IDictBackedInstance
    {
        private readonly PyType _type;
        public Dictionary<string, PyObject> InstanceDict { get; } = new();

        internal PySimpleNamespace(PyType type) => _type = type;

        public PySimpleNamespace(PyDict kwargs = null) : this(PySimpleNamespaceType.Instance) => Update(kwargs);

        internal void Update(PyDict kwargs)
        {
            if (kwargs == null)
                return;
            foreach (var pair in kwargs.InternalDict)
                InstanceDict[((PyString)pair.Key).Value] = pair.Value;
        }

        internal PyObject Dict => PyGlobalsDict.OfInstance(InstanceDict);

        public override PyType GetPyType() => _type;
        public override string GetTypeName() => _type.FullName;

        protected internal override void TpSetattro(string name, PyObject value)
        {
            if (name == "__dict__")
                throw PyAttributeError.Create("readonly attribute");
            else
                InstanceDict[name] = value;
        }

        protected internal override void TpDelattro(string name)
        {
            if (!InstanceDict.Remove(name))
                throw PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '{name}'");
        }

        // namespace_repr
        protected internal override PyString Repr()
        {
            string name = ReferenceEquals(_type, PySimpleNamespaceType.Instance) ? "namespace" : _type.FullName;
            if (!PyReprGuard.Enter(this))
                return new PyString($"{name}(...)");
            try
            {
                var pairs = new List<string>();
                foreach (var pair in InstanceDict)
                    if (pair.Key.Length > 0)
                        pairs.Add($"{pair.Key}={pair.Value.ToRepr().Value}");
                return new PyString($"{name}({string.Join(", ", pairs)})");
            }
            finally
            {
                PyReprGuard.Leave(this);
            }
        }

        protected internal override PyString Str() => ToRepr();
        public override string ToString() => ToRepr().Value;

        // namespace_richcompare
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => other is PySimpleNamespace namespaceObject
            ? Compare(Dict, namespaceObject.Dict, op)
            : PyNotImplemented.Instance;

        public override bool Equals(object other) => other is PySimpleNamespace && RichCompare((PyObject)other, CompareOp.EQ).IsTrue();
        public override int ToHash() => throw PyTypeError.Create($"unhashable type: '{GetTypeName()}'");
    }
}
