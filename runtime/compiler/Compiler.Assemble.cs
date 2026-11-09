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
        public static GeneratedMod Parse(string source, string filename, CompileMode mode, int flags = 0, bool fileInput = false)
        {
            var tok = new Tokenizer(source, execInput: mode == CompileMode.File, filename: filename, fileInput: fileInput);
            var parser = new PyParser(tok, filename, source, mode, flags);
            return mode switch
            {
                CompileMode.Eval => parser.ParseEval(),
                CompileMode.Single => parser.ParseInteractive(),
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
            PyImportSystem.Import("warnings").GetAttribute("warn_explicit").Call(
                new PyObject[] { PyString.From(message), PyImportSystem.Import("builtins").GetAttribute(category), PyString.From(filename), PyInt.From(Math.Max(lineno, 1)) }, null);

        // compiler_warn
        private void Warn(Loc loc, string message)
        {
            try
            {
                WarnExplicit("SyntaxWarning", message, _filename, loc.Line);
            }
            catch (PythonException error) when (error.PyException.GetTypeName() == "SyntaxWarning")
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

            var code = new PyCodeObject(u.Name, assembled.Instructions, u.Consts, InOrder(u.Names), varnames,
                u.ArgCount + u.PosonlyArgCount + u.KwonlyArgCount, u.PosonlyArgCount, u.KwonlyArgCount, freevars, cellvars, null, null,
                codeFlags, _filename, _sourceLines);
            code.QualName = u.QualName;
            code.MarkHidden(u.Fasthidden.Keys);
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
