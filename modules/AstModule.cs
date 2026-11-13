using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// AST_object of Python/Python-ast.c: a node, whose fields and attributes are its __dict__.
    /// </summary>
    public sealed class PyAstObject : PyObject, IObjectDict
    {
        private readonly PyType _builtin;
        private PyDict _dict = new PyDict();

        internal PyAstObject(PyType type) => _builtin = Allocated(type);

        public override PyType GetPyType() => _customType ?? _builtin;

        public PyDict ObjectDict(bool create) => _dict;
        public void SetObjectDict(PyDict dict) => _dict = dict;
    }

    /// <summary>
    /// The _ast module of Python/Python-ast.c: AST (AST_type_spec), and the node classes init_types
    /// makes from it with type() - their table is AstModule.g.cs, generated from Python-ast.c by
    /// tools/gen_ast_types.py. The compiler's conversions (runtime/compiler/PythonAst.cs) use the
    /// classes and singletons of this state, as ast2obj and obj2ast use theirs.
    /// </summary>
    internal static partial class AstModule
    {
        // struct ast_state
        private static readonly Dictionary<string, PyType> Types = new();
        private static readonly Dictionary<string, PyObject> Singletons = new();
        private static PyType _astType;
        private static readonly object InitLock = new();
        private static bool _initialized;

        internal static PyType AstType
        {
            get
            {
                EnsureInitialized();
                return _astType;
            }
        }

        /// <summary>state->&lt;name&gt;_type.</summary>
        internal static PyType Type(string name)
        {
            EnsureInitialized();
            return Types[name];
        }

        internal static bool TryGetType(string name, out PyType type)
        {
            EnsureInitialized();
            return Types.TryGetValue(name, out type);
        }

        /// <summary>state->&lt;name&gt;_singleton: Load, Add, ... - what ast2obj answers for them.</summary>
        internal static PyObject Singleton(string name)
        {
            EnsureInitialized();
            return Singletons[name];
        }

        /// <summary>ast2obj's PyType_GenericNew(tp, NULL, NULL): a node without __init__.</summary>
        internal static PyObject NewNode(string name) => new PyAstObject(Type(name));

        // init_types, once.
        private static void EnsureInitialized()
        {
            if (_initialized)
                return;
            lock (InitLock)
            {
                if (_initialized)
                    return;
                _astType = MakeAstType();
                Types["AST"] = _astType;
                // add_ast_fields
                var empty = PyTuple.From(Array.Empty<PyObject>());
                _astType.SetAttribute("_fields", empty);
                _astType.SetAttribute("__match_args__", empty);
                _astType.SetAttribute("_attributes", empty);
                InitTypes();
                _initialized = true;
            }
        }

        // AST_type_spec: "ast.AST", GC, a dict, ast_type_init, ast_type_reduce, PyType_GenericNew.
        private static PyType MakeAstType()
        {
            var type = new PyType("AST", new[] { PyType.ObjectType }, "ast");
            type.MarkSpecHeapType();
            type.TypeDict["__module__"] = PyString.From("ast");
            type.TypeDict["__doc__"] = PyNone.Instance;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => new PyAstObject((PyType)cls),
                0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                Init(self, args, kwargs);
                return PyNone.Instance;
            });
            type.TypeDict["__reduce__"] = PyArgs.Method(type, "__reduce__", PyCall.NoArgs, (self, args) => Reduce(self));
            type.TypeDict["__dict__"] = SubclassInstanceSupport.GenericDictGetSet(type);
            TypeTables.GenericAttributes(type);
            TypeTables.GenericSetAttributes(type);
            return type;
        }

        // ast_type_init: positional arguments are the fields in order, keywords any attribute.
        private static void Init(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var type = self.GetPyType();
            var fields = type.GetAttribute("_fields");
            long count = fields.Length();
            if (count < args.Length)
                throw PyTypeError.Create($"{type.Name} constructor takes at most {count} positional argument{(count == 1 ? "" : "s")}");
            for (int i = 0; i < args.Length; i++)
            {
                var name = fields.GetItem(PyInt.From(i));
                self.SetAttribute(name.AsString().Value, args[i]);
            }
            if (kwargs == null)
                return;
            foreach (var key in kwargs.Keys().Items)
            {
                if (fields.Contains(key).IsTrue())
                {
                    long position = ((PyInt)fields.GetAttribute("index").Call(new[] { key }, null)).ToLong();
                    if (position < args.Length)
                        throw PyTypeError.Create($"{type.Name} got multiple values for argument '{((PyString)key).Value}'");
                }
                self.SetAttribute(((PyString)key).Value, kwargs.GetItem(key));
            }
        }

        // ast_type_reduce: (type, (), __dict__), or (type, ()) without one.
        private static PyObject Reduce(PyObject self)
        {
            var dict = self.LookupAttribute("__dict__");
            var empty = PyTuple.From(Array.Empty<PyObject>());
            return dict != null
                ? PyTuple.From(new PyObject[] { self.GetPyType(), empty, dict })
                : PyTuple.From(new PyObject[] { self.GetPyType(), empty });
        }

        // make_type: type(name, (base,), {_fields, __match_args__, __module__: 'ast', __doc__}).
        private static void MakeType(string name, string baseName, string[] fields, string doc)
        {
            var names = new PyObject[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                names[i] = PyString.From(fields[i]);
            var fieldTuple = PyTuple.From(names);
            var dict = new PyDict();
            dict.SetItem(PyString.From("_fields"), fieldTuple);
            dict.SetItem(PyString.From("__match_args__"), fieldTuple);
            dict.SetItem(PyString.From("__module__"), PyString.From("ast"));
            dict.SetItem(PyString.From("__doc__"), PyString.From(doc));
            Types[name] = (PyType)PyType.TypeType.Call(new PyObject[]
                { PyString.From(name), PyTuple.From(new PyObject[] { Types[baseName] }), dict }, null);
        }

        // add_attributes
        private static void AddAttributes(string name, string[] attributes)
        {
            var names = new PyObject[attributes.Length];
            for (int i = 0; i < attributes.Length; i++)
                names[i] = PyString.From(attributes[i]);
            Types[name].SetAttribute("_attributes", PyTuple.From(names));
        }

        // PyObject_SetAttr(state->X_type, field, Py_None): an optional field or attribute defaults to None.
        private static void SetNone(string name, string field) => Types[name].SetAttribute(field, PyNone.Instance);

        // state->X_singleton = PyType_GenericNew(state->X_type, NULL, NULL)
        private static void MakeSingleton(string name) => Singletons[name] = new PyAstObject(Types[name]);

        /// <summary>astmodule_exec: AST, the PyCF_ flags and every class, in its order.</summary>
        internal static PyModule CreateModule()
        {
            EnsureInitialized();
            var module = new PyModule("_ast");
            // PyModuleDef m_doc is NULL
            module.ModuleDict["__doc__"] = PyNone.Instance;
            foreach (var name in Exported)
            {
                module.ModuleDict[name] = name switch
                {
                    "PyCF_ALLOW_TOP_LEVEL_AWAIT" => PyInt.From(PythonAst.PyCF_ALLOW_TOP_LEVEL_AWAIT),
                    "PyCF_ONLY_AST" => PyInt.From(PythonAst.PyCF_ONLY_AST),
                    "PyCF_TYPE_COMMENTS" => PyInt.From(0x1000),
                    _ => Types[name],
                };
            }
            return module;
        }
    }
}
