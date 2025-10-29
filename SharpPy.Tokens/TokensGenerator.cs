
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;

namespace SharpPy.Tokenizer
{
    /// <summary>
    /// Generates C# tokenizer code from PEG grammar tokens
    /// CPython 3.12 compatible tokenizer generation
    /// </summary>
    public class CSharpTokenGenerator
    {
        private readonly List<TokenDefinition> _tokens;
        private readonly StringBuilder _output;
        private int _indentLevel;

        public CSharpTokenGenerator(List<TokenDefinition> tokens)
        {
            _tokens = tokens;
            _output = new StringBuilder();
            _indentLevel = 0;
        }

        /// <summary>
        /// Generate the complete C# tokenizer class
        /// </summary>
        public string GenerateTokenizer()
        {
            GenerateTokenizerClass();
            return _output.ToString();
        }

        private void GenerateTokenizerClass()
        {
            WriteLine("namespace SharpPy.Generated");
            WriteLine("{");
            Indent();
            WriteLine("public static class PyToken");
            WriteLine("{");
            Indent();
            GenerateTokenTypeEnum(); // Generate enum from Grammar/Tokens
            GenerateLiteralslist();
            GenerateHelperFunctions();
            Dedent();

            WriteLine("}"); // Close class

            Dedent();
            WriteLine("}"); // Close namespace
        }

        void GenerateLiteralslist()
        {

            // Generate operator map
            var literals = _tokens.Where(t => t.IsLiteral).ToList();
            if (literals.Any())
            {
                WriteLine("public static readonly List<(string name, Type type)> Literals = new()");
                WriteLine("{");
                Indent();

                // Sort by length (longer first) to match properly (e.g., "==" before "=")
                var sortedLiterals = literals.OrderByDescending(op => op.Value.Length);
                foreach (var op in sortedLiterals)
                {
                    // Map literal string to its exact type (e.g., "==" → EQEQUAL, "+" → PLUS)
                    WriteLine($"( \"{EscapeString(op.Value)}\", Type.{op.Name} ),");
                }

                Dedent();
                WriteLine("};");
                WriteLine();
            }

        }

        void GenerateHelperFunctions()
        {
            WriteLine();
            WriteLine("/// <summary>");
            WriteLine("/// Get literal index by matching string at position");
            WriteLine("/// Returns the index of the first matching literal (longest match first)");
            WriteLine("/// </summary>");
            WriteLine("/// <param name=\"srcString\">Source string to search in</param>");
            WriteLine("/// <param name=\"srcPosition\">Position to start matching from</param>");
            WriteLine("/// <returns>Index in Literals list, or -1 if not found</returns>");
            WriteLine("public static int GetLiteralIndex(string srcString, int srcPosition)");
            WriteLine("{");
            WriteLine("    for (int i = 0; i < Literals.Count; ++i)");
            WriteLine("    {");
            WriteLine("        var lit = Literals[i];");
            WriteLine("        if (string.Compare(srcString, srcPosition, lit.name, 0, lit.name.Length) == 0)");
            WriteLine("            return i;  // Longest match first (literals are sorted by length)");
            WriteLine("    }");
            WriteLine("    return -1;");
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// Get operator token type");
            WriteLine("/// CPython 3.12: Tokenizer outputs OP for all operators, parser uses exact type");
            WriteLine("/// </summary>");
            WriteLine("/// <param name=\"name\">Operator literal string (e.g., \"==\", \"+\")</param>");
            WriteLine("/// <param name=\"exactType\">If true, return exact type (EQEQUAL, PLUS), if false return OP</param>");
            WriteLine("/// <returns>Token type for the operator</returns>");
            WriteLine("public static Type GetOpType(string name, bool exactType = true)");
            WriteLine("{");
            WriteLine("    // Search in Literals list");
            WriteLine("    foreach (var lit in Literals)");
            WriteLine("    {");
            WriteLine("        if (lit.name == name)");
            WriteLine("            return exactType ? lit.type : Type.OP;");
            WriteLine("    }");
            WriteLine("    throw new ArgumentException($\"Unknown literal: {name}\");");
            WriteLine("}");
            WriteLine();
        }

        private void GenerateTokenTypeEnum()
        {
            WriteLine("/// <summary>");
            WriteLine("/// CPython 3.12 compatible token types");
            WriteLine("/// Explicit values to match CPython token indices");
            WriteLine("/// </summary>");
            WriteLine("public enum Type");
            WriteLine("{");
            Indent();

            for (int i = 0; i < _tokens.Count; i++)
            {
                var token = _tokens[i];
                WriteLine($"{token.Name} = {i},");
            }

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private string EscapeString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        // Output helper methods
        private void WriteLine(string line = "")
        {
            if (!string.IsNullOrEmpty(line))
            {
                _output.Append(new string(' ', _indentLevel * 4));
                _output.AppendLine(line);
            }
            else
            {
                _output.AppendLine();
            }
        }

        private void Indent() => _indentLevel++;
        private void Dedent() => _indentLevel--;
    }
}