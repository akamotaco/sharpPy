using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// defdictobject (Modules/_collectionsmodule.c): a dict with default_factory, which __missing__
    /// calls. Its tp_new is dict_new, which allocates this layout for defaultdict and its subclasses.
    /// </summary>
    public sealed class PyDefaultDict : PyDict
    {
        internal static readonly PyType DefDictType = CreateType();

        /// <summary>default_factory: null is the C NULL a fresh object or a del leaves.</summary>
        internal PyObject DefaultFactory;

        internal PyDefaultDict(PyType type)
        {
            if (!ReferenceEquals(type, DefDictType))
                SetCustomType(type);
        }

        // ob_type: defaultdict, or the Python class that allocated the defaultdict layout.
        public override PyType GetPyType() => _customType ?? DefDictType;

        /// <summary>Whether dict_new allocates this layout for the type.</summary>
        internal static bool IsLayoutOf(PyType type) =>
            ReferenceEquals(type, DefDictType) || type.IsSubclassOf(DefDictType);

        // defdict_missing
        private PyObject Missing(PyObject key)
        {
            var factory = DefaultFactory;
            if (factory == null || factory is PyNone)
                throw PyKeyError.Create(key);
            var value = factory.Call(Array.Empty<PyObject>(), null);
            SetItem(key, value);
            return value;
        }

        // new_defdict: the object's class, called with the factory (None for NULL) and arg.
        private PyObject NewDefDict(PyObject arg) =>
            GetPyType().Call(new[] { DefaultFactory ?? PyNone.Instance, arg }, null);

        // defdict_reduce
        private PyObject Reduce()
        {
            var args = DefaultFactory == null || DefaultFactory is PyNone
                ? PyTuple.From(Array.Empty<PyObject>())
                : PyTuple.From(new[] { DefaultFactory });
            var items = GetAttribute("items").Call(Array.Empty<PyObject>(), null);
            return PyTuple.From(new[] { GetPyType(), args, PyNone.Instance, PyNone.Instance, items.GetIterator() });
        }

        // defdict_repr: dict's repr, then the factory's under Py_ReprEnter of the factory.
        protected internal override PyString Repr()
        {
            var baseRepr = DictRepr();
            string factoryRepr;
            if (DefaultFactory == null)
                factoryRepr = "None";
            else if (!PyReprGuard.Enter(DefaultFactory))
                factoryRepr = "...";
            else
            {
                try { factoryRepr = DefaultFactory.ToRepr().Value; }
                finally { PyReprGuard.Leave(DefaultFactory); }
            }
            return PyString.From($"{GetPyType().Name}({factoryRepr}, {baseRepr.Value})");
        }

        // defdict_or: whichever operand is the defaultdict decides; the other must be a dict.
        private static PyObject Or(PyObject left, PyObject right)
        {
            PyDefaultDict self;
            PyObject other;
            if (left is PyDefaultDict l)
            {
                self = l;
                other = right;
            }
            else
            {
                self = (PyDefaultDict)right;
                other = left;
            }
            if (other is not PyDict)
                return PyNotImplemented.Instance;
            var result = self.NewDefDict(left);
            if (result is not PyDict merged)
                throw PySystemError.Create("bad internal call");
            UpdateCommon(merged, right);
            return result;
        }

        public override PyObject BitwiseOr(PyObject other) => Or(this, other);

        // defdict_init: the first argument is the factory; the rest are dict_init's.
        private static void Init(PyDefaultDict self, PyObject[] args, PyDict kwargs)
        {
            PyObject factory = null;
            if (args.Length > 0)
            {
                factory = args[0];
                if (!factory.IsCallable() && factory is not PyNone)
                    throw PyTypeError.Create("first argument must be callable or None");
            }
            self.DefaultFactory = factory;
            var rest = args.Length > 1 ? args[1..] : Array.Empty<PyObject>();
            UpdateCommon(self, rest, kwargs, "dict");
        }

        private static PyType CreateType()
        {
            var type = new PyType("defaultdict", new[] { PyType.DictType }, "collections");
            type.TypeDict["__module__"] = PyString.From("collections");
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                Init((PyDefaultDict)self, args, kwargs);
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type, (self, args, kwargs) => ((PyDefaultDict)self).Repr());
            type.TypeDict["__or__"] = new PyWrapperDescriptor("__or__", type, (self, args, kwargs) => Or(self, args[0]));
            type.TypeDict["__ror__"] = new PyWrapperDescriptor("__ror__", type, (self, args, kwargs) => Or(args[0], self));
            PyArgs.Define(type, "__missing__", PyCall.One, (self, args) => ((PyDefaultDict)self).Missing(args[0]));
            PyArgs.Define(type, "copy", PyCall.NoArgs, (self, args) => ((PyDefaultDict)self).NewDefDict(self));
            PyArgs.Define(type, "__copy__", PyCall.NoArgs, (self, args) => ((PyDefaultDict)self).NewDefDict(self));
            PyArgs.Define(type, "__reduce__", PyCall.NoArgs, (self, args) => ((PyDefaultDict)self).Reduce());
            type.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(type);
            // defdict_members: default_factory, T_OBJECT (NULL reads as None, del makes it NULL).
            type.TypeDict["default_factory"] = new PyMemberDescriptor("default_factory", type,
                self => ((PyDefaultDict)self).DefaultFactory ?? PyNone.Instance,
                (self, value) => ((PyDefaultDict)self).DefaultFactory = value,
                deleter: self => ((PyDefaultDict)self).DefaultFactory = null);
            if (PyDocs.Lookup("collections.defaultdict") is { } doc)
                type.TypeDict["__doc__"] = PyDocs.DocFromInternal("defaultdict", doc);
            return type;
        }
    }

    public static class _CollectionsModule
    {
        public static PyModule CreateCollectionsModule()
        {
            var module = new PyModule("_collections", "High performance container datatypes");
            module.ModuleDict["deque"] = PyDequeModule.DequeType;
            module.ModuleDict["defaultdict"] = PyDefaultDict.DefDictType;
            module.ModuleDict["_deque_iterator"] = PyDequeModule.DequeIterType;
            module.ModuleDict["_deque_reverse_iterator"] = PyDequeModule.DequeRevIterType;
            return module;
        }
    }
}
