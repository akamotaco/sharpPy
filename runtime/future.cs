// Python/future.c of CPython v3.12.15.
using System.Collections.Generic;
using SharpPy.Generated;

namespace SharpPy
{
    public sealed class PyFuture
    {
        public bool Annotations;
        public bool BarryAsBdfl;
        public int LastLine = -1;   // ff_location: the last future statement
        public int LastColumn = -1;

        // _PyAST_GetDocString
        public static GeneratedExpr? GetDocString(GeneratedSeq? body)
        {
            if (body == null || body.Count == 0)
                return null;
            return body[0] is GeneratedExprStmt { Value: GeneratedConstant { Value: GeneratedPyConstantString } value } ? value : null;
        }

        private static PythonException Error(string message, string filename, GeneratedAstNode at, List<string>? lines) =>
            PySyntaxError.At(message, filename, at.LineNo, at.ColOffset + 1, at.EndLineNo, at.EndColOffset + 1, lines);

        private void CheckFeatures(GeneratedImportFrom s, string filename, List<string>? lines)
        {
            foreach (GeneratedAlias name in s.Names)
            {
                switch (name.Name.Value)
                {
                    case "nested_scopes":
                    case "generators":
                    case "division":
                    case "absolute_import":
                    case "with_statement":
                    case "print_function":
                    case "unicode_literals":
                    case "generator_stop":
                        break;
                    case "barry_as_FLUFL":
                        BarryAsBdfl = true;
                        break;
                    case "annotations":
                        Annotations = true;
                        break;
                    case "braces":
                        throw Error("not a chance", filename, name, lines);
                    default:
                        throw Error($"future feature {name.Name.Value} is not defined", filename, name, lines);
                }
            }
        }

        // _PyFuture_FromAST
        public static PyFuture FromAst(GeneratedMod mod, string filename, List<string>? lines = null)
        {
            var future = new PyFuture();
            GeneratedSeq? body = mod switch
            {
                GeneratedModule module => module.Body,
                GeneratedInteractive interactive => interactive.Body,
                _ => null,
            };
            if (body == null || body.Count == 0)
                return future;
            for (int i = GetDocString(body) != null ? 1 : 0; i < body.Count; i++)
            {
                if (body[i] is not GeneratedImportFrom { Module.Value: "__future__" } statement)
                    return future;
                future.CheckFeatures(statement, filename, lines);
                future.LastLine = statement.LineNo;
                future.LastColumn = statement.ColOffset;
            }
            return future;
        }
    }
}
