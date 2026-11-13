// Python/Python-ast.c of CPython v3.12.15: the compiler's tree as Python objects and back.
// The per-node code is generated into PythonAst.g.cs; the classes and singletons are the _ast
// module's state (modules/AstModule.cs), as ast2obj and obj2ast use state->X_type.
using System;
using SharpPy.Generated;

namespace SharpPy
{
    public sealed partial class PythonAst
    {

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
            var wantedType = Modules.AstModule.Type(wanted);
            if (!value.GetPyType().IsSubclassOf(wantedType) && !ReferenceEquals(value.GetPyType(), wantedType))
                throw PyTypeError.Create($"expected {wanted} node, got {value.GetTypeName()}");
            return (GeneratedMod)ast.ToNode(value)!;
        }

        public static bool IsAst(PyObject value) => value.GetPyType().IsSubclassOf(Modules.AstModule.AstType);

        // ast2obj_<name>: PyType_GenericNew(state->X_type, NULL, NULL), no __init__.
        private PyObject New(string name) => Modules.AstModule.NewNode(name);

        // ast2obj of a simple sum: state->X_singleton.
        private PyObject Singleton(string name) => Modules.AstModule.Singleton(name);

        private PyObject Convert(GeneratedPtr? node) => node == null ? PyNone.Instance : Node(node);

        private static PyObject Identifier(GeneratedIdentifier? identifier) =>
            identifier == null ? PyNone.Instance : PyString.From(identifier.Value);

        private static PyObject Str(string? text) => text == null ? PyNone.Instance : PyString.From(text);

        private static PyObject Int(int? value) => value == null ? PyNone.Instance : PyInt.From(value.Value);

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
                    // obj2ast_<kind>: _Py_EnterRecursiveCall(" while traversing '<kind>' node"), the
                    // kind being the abstract base (expr, stmt, ...) or the product type itself.
                    var kindName = type.BaseTypes.Length > 0 && type.BaseTypes[0].Name != "AST" ? type.BaseTypes[0].Name : type.Name;
                    // The simple sums (operators, contexts) are singletons, read without the guard.
                    if (kindName is "boolop" or "operator" or "unaryop" or "cmpop" or "expr_context")
                        return Build(value, type.Name);
                    PyCRecursion.Enter($" while traversing '{kindName}' node");
                    try
                    {
                        return Build(value, type.Name);
                    }
                    finally
                    {
                        PyCRecursion.Leave();
                    }
                }
            }
            throw PyTypeError.Create($"expected some sort of AST node, but got {value.ToRepr().Value}");
        }

        private PyObject? ModuleAttribute(string name) => Modules.AstModule.TryGetType(name, out var type) ? type : null;

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

        // obj2ast_identifier
        private static GeneratedIdentifier? ToIdentifier(PyObject? value) =>
            value is null or PyNone ? null
            : value is PyString text ? new GeneratedIdentifier(text.Value)
            : throw PyTypeError.Create("AST identifier must be of type str");

        private static string? ToStr(PyObject? value) => value is PyString text ? text.Value : null;

        // An optional int field (ImportFrom.level): missing or None is 0.
        private static int ToOptionalInt(PyObject? value) => value is null or PyNone ? 0 : ToInt(value, "");

        // A sequence field: its list, or an empty one when the node has no such attribute.
        private static PyObject SeqField(PyObject o, string name) => Attribute(o, name) ?? new PyList();

        // _PyObject_LookupAttr: the value, or null when there is none.
        private static PyObject? Attribute(PyObject o, string name)
        {
            try
            {
                return o.GetAttribute(name);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return null;
            }
        }

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

        private T Fill<T>(T seq, PyObject? value, string owner, string name) where T : GeneratedSeq
        {
            if (value is not PyList list)
                throw PyTypeError.Create($"{owner} field \"{name}\" must be a list, not a {value?.GetPyType().Name}");
            for (int i = 0; i < list.Length(); i++)
                seq.Add(ToNode(list.GetItem(PyInt.From(i))));
            return seq;
        }

        private static T FillIdentifiers<T>(T seq, PyObject? value, string owner, string name) where T : GeneratedSeq
        {
            if (value is not PyList list)
                throw PyTypeError.Create($"{owner} field \"{name}\" must be a list, not a {value?.GetPyType().Name}");
            for (int i = 0; i < list.Length(); i++)
                seq.Add(ToIdentifier(list.GetItem(PyInt.From(i))));
            return seq;
        }

        // The attributes of obj2ast_<kind>: lineno and col_offset are required ints, end_lineno and
        // end_col_offset default to them when missing or None.
        private static void Locate(GeneratedAstNode node, PyObject o, string kind)
        {
            node.LineNo = ToInt(Attribute(o, "lineno") ?? throw Missing("lineno", kind), "lineno");
            node.ColOffset = ToInt(Attribute(o, "col_offset") ?? throw Missing("col_offset", kind), "col_offset");
            node.EndLineNo = Attribute(o, "end_lineno") is { } end and not PyNone ? ToInt(end, "end_lineno") : node.LineNo;
            node.EndColOffset = Attribute(o, "end_col_offset") is { } endColumn and not PyNone ? ToInt(endColumn, "end_col_offset") : node.ColOffset;
        }

        private static System.Exception Missing(string field, string kind) =>
            PyTypeError.Create("required field \"" + field + "\" missing from " + kind);
    }
}
