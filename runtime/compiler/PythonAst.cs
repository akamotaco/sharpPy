// Python/Python-ast.c of CPython v3.12.15: the compiler's tree as Python objects and back.
// The per-node code is generated into PythonAst.g.cs; the classes are in Lib/_ast.py.
using System;
using SharpPy.Generated;

namespace SharpPy
{
    public sealed partial class PythonAst
    {
        private readonly PyObject _module = PyImportSystem.Import("_ast");
        private readonly System.Collections.Generic.Dictionary<string, PyObject> _singletons = new();

        public const int PyCF_ONLY_AST = 0x0400;
        public const int PyCF_ALLOW_TOP_LEVEL_AWAIT = 0x2000;

        // PyAST_mod2obj
        public static PyObject ModToObject(GeneratedMod mod) => new PythonAst().Node(mod);

        // PyAST_obj2mod
        public static GeneratedMod ObjectToMod(PyObject value, CompileMode mode)
        {
            var ast = new PythonAst();
            string wanted = mode switch
            {
                CompileMode.Eval => "Expression",
                CompileMode.Single => "Interactive",
                _ => "Module",
            };
            if (!value.GetPyType().IsSubclassOf((PyType)ast._module.GetAttribute(wanted)) &&
                !ReferenceEquals(value.GetPyType(), ast._module.GetAttribute(wanted)))
                throw PyTypeError.Create($"expected {wanted} node, got {value.GetTypeName()}");
            return (GeneratedMod)ast.ToNode(value)!;
        }

        public static bool IsAst(PyObject value)
        {
            if (!PyImportSystem.TryGetModule("_ast", out var module))
                return false;
            return value.GetPyType().IsSubclassOf((PyType)module.GetAttribute("AST"));
        }

        private PyObject New(string name) => _module.GetAttribute(name).Call(Array.Empty<PyObject>(), null);

        private PyObject Singleton(string name)
        {
            if (!_singletons.TryGetValue(name, out var value))
                _singletons[name] = value = New(name);
            return value;
        }

        private PyObject Convert(GeneratedPtr? node) => node == null ? PyNone.Instance : Node(node);

        private static PyObject Identifier(GeneratedIdentifier? identifier) =>
            identifier == null ? PyNone.Instance : new PyString(identifier.Value);

        private static PyObject Str(string? text) => text == null ? PyNone.Instance : new PyString(text);

        private static PyObject Int(int? value) => value == null ? PyNone.Instance : new PyInt(value.Value);

        private static PyObject ConstantObject(GeneratedPyConstant value) => AstOptimizer.Value(value);

        private PyObject Seq(GeneratedSeq? seq)
        {
            var list = new PyList();
            for (int i = 0; i < (seq?.Count ?? 0); i++)
                list.Append(Convert(seq![i]));
            return list;
        }

        private PyObject Seq(GeneratedSeq? seq, Func<GeneratedPtr, PyObject> convert)
        {
            var list = new PyList();
            for (int i = 0; i < (seq?.Count ?? 0); i++)
                list.Append(convert(seq![i]));
            return list;
        }

        // The class of an object names the case of the sum, as PyObject_IsInstance does in order.
        private GeneratedPtr? ToNode(PyObject value)
        {
            if (value is PyNone)
                return null;
            foreach (var type in value.GetPyType().MRO)
            {
                if (type is PyClass && ReferenceEquals(ModuleAttribute(type.Name), type))
                {
                    if (type.Name is "Num" or "Str" or "Bytes" or "NameConstant" or "Ellipsis")
                        continue;
                    return Build(value, type.Name);
                }
            }
            throw PyTypeError.Create($"expected some sort of AST node, but got {value.ToRepr().Value}");
        }

        private PyObject? ModuleAttribute(string name)
        {
            try
            {
                return _module.GetAttribute(name);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return null;
            }
        }

        private static PyObject? Field(PyObject o, string name, bool optional, string owner)
        {
            try
            {
                var value = o.GetAttribute(name);
                if (value is PyNone && !optional && owner != "Constant") // obj2ast_constant takes None
                    throw PyValueError.Create($"field '{name}' is required for {owner}");
                return value;
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                if (optional)
                    return PyNone.Instance;
                throw PyTypeError.Create($"required field \"{name}\" missing from {owner}");
            }
        }

        private static GeneratedIdentifier? ToIdentifier(PyObject? value) =>
            value is null or PyNone ? null : new GeneratedIdentifier(((PyString)value).Value);

        private static string? ToStr(PyObject? value) => value is PyString text ? text.Value : null;

        private static int ToInt(PyObject? value, string name) =>
            value is PyInt number ? number.ToInt() : value is PyBool truth ? (truth.Value ? 1 : 0)
                : throw PyValueError.Create($"invalid integer value: {value?.ToRepr().Value}");

        private static GeneratedPyConstant ToConstant(PyObject? value) => value switch
        {
            null or PyNone => GeneratedPyConstant.None,
            PyBool truth => truth.Value ? GeneratedPyConstant.True : GeneratedPyConstant.False,
            PyEllipsis => GeneratedPyConstant.Ellipsis,
            PyString text => new GeneratedPyConstantString(text.Value),
            _ => new GeneratedPyConstantObject(value),
        };

        private T Fill<T>(T seq, PyObject? value) where T : GeneratedSeq
        {
            if (value is not PyList list)
                throw PyTypeError.Create($"field must be a list, not a {value?.GetTypeName()}");
            for (int i = 0; i < list.Length(); i++)
                seq.Add(ToNode(list.GetItem(new PyInt(i))));
            return seq;
        }

        private static T FillIdentifiers<T>(T seq, PyObject? value) where T : GeneratedSeq
        {
            if (value is not PyList list)
                throw PyTypeError.Create($"field must be a list, not a {value?.GetTypeName()}");
            for (int i = 0; i < list.Length(); i++)
                seq.Add(ToIdentifier(list.GetItem(new PyInt(i))));
            return seq;
        }

        private static void Locate(GeneratedAstNode node, PyObject o)
        {
            node.LineNo = ToInt(Field(o, "lineno", false, "node"), "lineno");
            node.ColOffset = ToInt(Field(o, "col_offset", false, "node"), "col_offset");
            node.EndLineNo = Field(o, "end_lineno", true, "node") is PyInt end ? end.ToInt() : node.LineNo;
            node.EndColOffset = Field(o, "end_col_offset", true, "node") is PyInt endColumn ? endColumn.ToInt() : node.ColOffset;
        }
    }
}
