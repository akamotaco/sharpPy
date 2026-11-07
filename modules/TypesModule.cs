using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{

    /// <summary>types.SimpleNamespace (Objects/namespaceobject.c): a dict and nothing else.</summary>
    public sealed class PySimpleNamespaceType : PyType
    {
        public static readonly PySimpleNamespaceType Instance = new PySimpleNamespaceType();

        internal override string FullName => "types.SimpleNamespace";

        private PySimpleNamespaceType() : base("SimpleNamespace", new PyType[] { PyType.ObjectType }, "types")
        {
            TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", this,
                (cls, args, kwargs) => new PySimpleNamespace((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            TypeDict["__init__"] = new PyWrapperDescriptor("__init__", this, (self, args, kwargs) =>
            {
                if (args.Length != 0)
                    throw PyTypeError.Create("no positional arguments expected");
                ((PySimpleNamespace)self).Update(kwargs);
                return PyNone.Instance;
            });
            // namespace_members: __dict__ is READONLY.
            TypeDict["__dict__"] = new PyMemberDescriptor("__dict__", this, self => ((PySimpleNamespace)self).Dict, readOnly: true);
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
        private readonly PyType _builtin;
        public Dictionary<string, PyObject> InstanceDict { get; } = new();

        internal PySimpleNamespace(PyType type) => _builtin = Allocated(type);

        public PySimpleNamespace(PyDict kwargs = null) : this(PySimpleNamespaceType.Instance) => Update(kwargs);

        internal void Update(PyDict kwargs)
        {
            if (kwargs == null)
                return;
            foreach (var pair in kwargs.InternalDict)
                InstanceDict[((PyString)pair.Key).Value] = pair.Value;
        }

        internal PyObject Dict => PyGlobalsDict.OfInstance(InstanceDict);

        public override PyType GetPyType() => _customType ?? _builtin;
        public override string GetTypeName() => GetPyType().FullName;


        // namespace_repr
        protected internal override PyString Repr()
        {
            string name = ReferenceEquals(GetPyType(), PySimpleNamespaceType.Instance) ? "namespace" : GetPyType().FullName;
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
