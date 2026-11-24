using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/symtablemodule.c: _symtable.symtable over the compiler's own table (runtime/symtable.cs)
    /// and the flag constants Lib/symtable.py reads.
    /// </summary>
    internal static class SymtableModule
    {
        internal static PyModule Create()
        {
            var module = new PyModule("_symtable");
            var d = module.ModuleDict;
            d["symtable"] = PyArgs.FastCall("_symtable", "symtable", 3, 3, Symtable);
            // symtable_init_constants
            d["USE"] = PyInt.From(PySymtable.USE);
            d["DEF_GLOBAL"] = PyInt.From(PySymtable.DEF_GLOBAL);
            d["DEF_NONLOCAL"] = PyInt.From(PySymtable.DEF_NONLOCAL);
            d["DEF_LOCAL"] = PyInt.From(PySymtable.DEF_LOCAL);
            d["DEF_PARAM"] = PyInt.From(PySymtable.DEF_PARAM);
            d["DEF_FREE"] = PyInt.From(PySymtable.DEF_FREE);
            d["DEF_FREE_CLASS"] = PyInt.From(PySymtable.DEF_FREE_CLASS);
            d["DEF_IMPORT"] = PyInt.From(PySymtable.DEF_IMPORT);
            d["DEF_BOUND"] = PyInt.From(PySymtable.DEF_BOUND);
            d["DEF_ANNOT"] = PyInt.From(PySymtable.DEF_ANNOT);
            d["TYPE_FUNCTION"] = PyInt.From((int)BlockType.FunctionBlock);
            d["TYPE_CLASS"] = PyInt.From((int)BlockType.ClassBlock);
            d["TYPE_MODULE"] = PyInt.From((int)BlockType.ModuleBlock);
            d["TYPE_ANNOTATION"] = PyInt.From((int)BlockType.AnnotationBlock);
            d["TYPE_TYPE_VAR_BOUND"] = PyInt.From((int)BlockType.TypeVarBoundBlock);
            d["TYPE_TYPE_ALIAS"] = PyInt.From((int)BlockType.TypeAliasBlock);
            d["TYPE_TYPE_PARAM"] = PyInt.From((int)BlockType.TypeParamBlock);
            d["LOCAL"] = PyInt.From(PySymtable.LOCAL);
            d["GLOBAL_EXPLICIT"] = PyInt.From(PySymtable.GLOBAL_EXPLICIT);
            d["GLOBAL_IMPLICIT"] = PyInt.From(PySymtable.GLOBAL_IMPLICIT);
            d["FREE"] = PyInt.From(PySymtable.FREE);
            d["CELL"] = PyInt.From(PySymtable.CELL);
            d["SCOPE_OFF"] = PyInt.From(PySymtable.SCOPE_OFFSET);
            d["SCOPE_MASK"] = PyInt.From(PySymtable.SCOPE_MASK);
            return module;
        }

        // _symtable_symtable_impl: (source, filename: PyUnicode_FSDecoder, startstr: str, /)
        private static PyObject Symtable(PyObject[] args)
        {
            var filenameObject = PyArgs.FSDecoderObject(args[1]);
            var startstr = PyArgs.ClinicStr("symtable", "argument 3", "str", args[2]);
            var source = PyBuiltinFunction.SourceAsString(args[0], "symtable", "string or bytes", startstr == "exec", filenameObject.Value);
            var mode = startstr switch
            {
                "exec" => CompileMode.File,
                "eval" => CompileMode.Eval,
                "single" => CompileMode.Single,
                _ => throw PyValueError.Create("symtable() arg 3 must be 'exec' or 'eval' or 'single'"),
            };
            // _Py_SymtableStringObjectFlags: _PyParser_ASTFromString, _PyFuture_FromAST, _PySymtable_Build.
            var filename = filenameObject.Value;
            var mod = Compiler.Parse(source, filename, mode);
            var future = PyFuture.FromAst(mod, filename);
            var table = PySymtable.Build(mod, filename, future.Annotations);
            return PySymtableEntry.From(table.Top);
        }
    }

    /// <summary>
    /// PySTEntryObject (Python/symtable.c:186): "symtable entry", the members ste_memberlist reads.
    /// The tree is made once per table, so children, symbols and varnames are the same objects on
    /// every read, as the C entry's fields are.
    /// </summary>
    internal sealed class PySymtableEntry : PyObject
    {
        private static readonly PyType EntryType = MakeType();

        private PyObject _id, _name, _symbols, _varnames, _children;
        private int _nested, _type, _lineno;

        public override PyType GetPyType() => EntryType;

        private static PyType MakeType()
        {
            var type = new PyType("symtable entry", new[] { PyType.ObjectType });
            Member(type, "id", entry => entry._id);
            Member(type, "name", entry => entry._name);
            Member(type, "symbols", entry => entry._symbols);
            Member(type, "varnames", entry => entry._varnames);
            Member(type, "children", entry => entry._children);
            Member(type, "nested", entry => PyInt.From(entry._nested));
            Member(type, "type", entry => PyInt.From(entry._type));
            Member(type, "lineno", entry => PyInt.From(entry._lineno));
            return type;
        }

        private static void Member(PyType type, string name, Func<PySymtableEntry, PyObject> read) =>
            type.TypeDict[name] = new PyMemberDescriptor(name, type, self => read((PySymtableEntry)self), readOnly: true);

        internal static PySymtableEntry From(PySTEntry ste)
        {
            var entry = new PySymtableEntry();
            // ste_id: PyLong_FromVoidPtr(key), the address of the AST node; here the number of the entry.
            entry._id = PyInt.From(PyObjectId.Of(entry));
            entry._name = PyString.From(ste.Name);
            var symbols = new PyDict();
            foreach (var (name, flags) in ste.Symbols)
                symbols.SetItem(PyString.From(name), PyInt.From(flags));
            entry._symbols = symbols;
            var varnames = new List<PyObject>();
            foreach (var name in ste.Varnames)
                varnames.Add(PyString.From(name));
            entry._varnames = new PyList(varnames);
            var children = new List<PyObject>();
            foreach (var child in ste.Children)
                children.Add(From(child));
            entry._children = new PyList(children);
            entry._nested = ste.Nested ? 1 : 0;
            entry._type = (int)ste.Type;
            entry._lineno = ste.Lineno;
            return entry;
        }

        // ste_repr: "<symtable entry %U(%R), line %d>"
        protected internal override PyObject Repr() =>
            PyString.From($"<symtable entry {((PyString)_name).Value}({_id.ToRepr().Value}), line {_lineno}>");
    }
}
