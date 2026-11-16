// Python/compile.c of CPython v3.12.15: the end of a unit, from instructions to a code object.
using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Generated;

namespace SharpPy
{
    public sealed partial class Compiler
    {
        private static PyObject ConstantValue(GeneratedPyConstant value) => AstOptimizer.Value(value);

        // _PyParser_ASTFromString
        // fileInput: _PyParser_ASTFromFile, the script pyrun_file runs; anything else is a string.
        internal const int PyCF_TYPE_COMMENTS = 0x1000;

        // PY_MINOR_VERSION
        internal const int PyMinorVersion = 12;

        public static GeneratedMod Parse(string source, string filename, CompileMode mode, int flags = 0, bool fileInput = false,
            int featureVersion = Compiler.PyMinorVersion)
        {
            // _PyPegen_compute_parser_flags: PyPARSE_ASYNC_HACKS below 3.7
            var tok = new Tokenizer(source, execInput: mode == CompileMode.File, filename: filename, fileInput: fileInput)
            {
                AsyncHacks = featureVersion < 7,
                // PyPARSE_TYPE_COMMENTS
                TypeComments = (flags & PyCF_TYPE_COMMENTS) != 0,
            };
            var parser = new PyParser(tok, filename, source, mode, flags) { FeatureVersion = featureVersion };
            return mode switch
            {
                CompileMode.Eval => parser.ParseEval(),
                CompileMode.Single => parser.ParseInteractive(),
                CompileMode.FuncType => parser.ParseFuncType(),
                _ => parser.ParseFile(),
            };
        }

        // Py_CompileStringObject: the single route from text to a code object.
        public static PyCodeObject CompileSource(string source, string filename, CompileMode mode = CompileMode.File,
            int optimize = 0, int inheritedFlags = 0, bool fileInput = false)
        {
            var mod = Parse(source, filename, mode, inheritedFlags, fileInput);
            var lines = new List<string>(source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
            return Compile(mod, filename, optimize, lines, inheritedFlags);
        }

        // PyErr_WarnExplicitObject
        internal static void WarnExplicit(string category, string message, string filename, int lineno) =>
            PyWarnings.WarnExplicit(PyImportSystem.Import("builtins").GetAttribute(category), PyString.From(message),
                PyString.From(filename), Math.Max(lineno, 1), null, null);

        // compiler_warn
        private void Warn(Loc loc, string message)
        {
            try
            {
                WarnExplicit("SyntaxWarning", message, _filename, loc.Line);
            }
            // PyErr_ExceptionMatches(PyExc_SyntaxWarning): the warning turned into an error, a
            // subclass included, becomes the SyntaxError.
            catch (PythonException error) when (error.PyException.GetPyType().IsSubclassOf(PyType.SyntaxWarningType))
            {
                throw Error(loc, message);
            }
        }

        // compute_code_flags
        private int ComputeCodeFlags()
        {
            var ste = _u.Ste;
            int flags = 0;
            if (ste.IsFunctionLike)
            {
                flags |= PyCodeObject.CO_NEWLOCALS | PyCodeObject.CO_OPTIMIZED;
                if (ste.Nested)
                    flags |= PyCodeObject.CO_NESTED;
                if (ste.Generator && !ste.Coroutine)
                    flags |= PyCodeObject.CO_GENERATOR;
                if (!ste.Generator && ste.Coroutine)
                    flags |= PyCodeObject.CO_COROUTINE;
                if (ste.Generator && ste.Coroutine)
                    flags |= PyCodeObject.CO_ASYNC_GENERATOR;
                if (ste.Varargs)
                    flags |= PyCodeObject.CO_VARARGS;
                if (ste.Varkeywords)
                    flags |= PyCodeObject.CO_VARKEYWORDS;
            }
            if (_future.Annotations)
                flags |= CO_FUTURE_ANNOTATIONS;
            if (_future.BarryAsBdfl)
                flags |= CO_FUTURE_BARRY_AS_BDFL;
            if (IsTopLevelAwait && ste.Coroutine && !ste.Generator)
                flags |= PyCodeObject.CO_COROUTINE;
            return flags;
        }

        private static List<string> InOrder(Dictionary<string, int> dict, int offset = 0)
        {
            var names = new string[dict.Count];
            foreach (var (name, index) in dict)
                names[index - offset] = name;
            return new List<string>(names);
        }

        // insert_prefix_instructions: cells of arguments first, at their slots.
        private static List<int> CellsInFrameOrder(List<string> varnames, List<string> cellvars)
        {
            var arguments = new SortedDictionary<int, int>();
            var others = new List<int>();
            for (int i = 0; i < cellvars.Count; i++)
            {
                int slot = varnames.IndexOf(cellvars[i]);
                if (slot >= 0)
                    arguments[slot] = i;
                else
                    others.Add(i);
            }
            var order = new List<int>(arguments.Values);
            order.AddRange(others);
            return order;
        }

        // compute_localsplus_info (Python/assemble.c): the locals, CO_FAST_HIDDEN when the name is
        // in u_fasthidden and CO_FAST_CELL when it is a cell too; then the cells that are not
        // locals; then the free variables.
        private static void ComputeLocalsplusInfo(Unit u, List<string> varnames, List<string> cellvars,
            List<string> freevars, out PyString[] names, out byte[] kinds)
        {
            var nameList = new List<PyString>();
            var kindList = new List<byte>();
            foreach (var name in varnames)
            {
                byte kind = PyCodeObject.FastLocal;
                if (u.Fasthidden.ContainsKey(name))
                    kind |= PyCodeObject.FastHidden;
                if (u.Cellvars.ContainsKey(name))
                    kind |= PyCodeObject.FastCell;
                nameList.Add(PyString.From(name));
                kindList.Add(kind);
            }
            foreach (var name in cellvars)
            {
                // Skip cells that are already covered by locals.
                if (u.Varnames.ContainsKey(name))
                    continue;
                nameList.Add(PyString.From(name));
                kindList.Add(PyCodeObject.FastCell);
            }
            foreach (var name in freevars)
            {
                nameList.Add(PyString.From(name));
                kindList.Add(PyCodeObject.FastFree);
            }
            names = nameList.ToArray();
            kinds = kindList.ToArray();
        }

        // optimize_and_assemble and optimize_and_assemble_code_unit
        private PyCodeObject OptimizeAndAssemble(bool addNone)
        {
            var u = _u;
            int codeFlags = ComputeCodeFlags();

            if (addNone)
                ADDOP_LOAD_CONST(Loc.NoLocation, PyNone.Instance);
            ADDOP(Loc.NoLocation, ByteCodeOp.RETURN_VALUE);

            var varnames = InOrder(u.Varnames);
            var cellvars = InOrder(u.Cellvars);
            var freevars = InOrder(u.Freevars, u.Cellvars.Count);
            var cfg = PyFlowGraph.Build(u.Seq);
            var optimizer = new CFGOptimizer(cfg, u.Consts, AddConst, varnames.Count, u.Ste.Varnames.Count, u.FirstLineNo);
            optimizer.Optimize();
            optimizer.InsertPrefixInstructions(codeFlags, CellsInFrameOrder(varnames, cellvars), freevars.Count, u.FirstLineNo);
            cfg.FixCellOffsets(varnames, cellvars, freevars);
            int maxDepth = optimizer.Stackdepth(codeFlags);
            optimizer.ConvertPseudoOps();
            optimizer.NormalizeJumps();
            var assembled = PyAssemble.Assemble(cfg, _filename);

            ComputeLocalsplusInfo(u, varnames, cellvars, freevars, out var localsPlusNames, out var localsPlusKinds);
            // compiler_set_qualname: a qualname that is the name is the name object itself.
            var nameObject = PyString.InternFrom(u.Name);
            var qualNameObject = string.IsNullOrEmpty(u.QualName) || u.QualName == u.Name ? nameObject : PyString.From(u.QualName);
            var code = new PyCodeObject(nameObject, qualNameObject, assembled.Instructions, u.Consts,
                InOrder(u.Names).ConvertAll(PyString.From).ToArray(), localsPlusNames, localsPlusKinds,
                u.ArgCount + u.PosonlyArgCount + u.KwonlyArgCount, u.PosonlyArgCount, u.KwonlyArgCount,
                codeFlags, _filename, _sourceLines);
            code.FirstLineNo = u.FirstLineNo;
            code.ExceptionTable.AddRange(assembled.ExceptionTable);
            code.StackSize = maxDepth;
            code.Tables = new AssembledTables
            {
                Code = LocationTable.CodeBytes(assembled.Instructions),
                LineTable = LocationTable.LineBytes(assembled.Instructions, u.FirstLineNo),
                ExceptionTable = LocationTable.ExceptionBytes(assembled.ExceptionTable),
            };
            return code;
        }
    }
}
