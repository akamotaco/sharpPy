using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SharpPy.Generated;

namespace SharpPy
{
    /// <summary>
    /// ast.dump of what the parser built: the ASDL tree, before any conversion.
    /// Fields come in declaration order, which is Python.asdl's order.
    /// </summary>
    public static class AsdlDumper
    {
        private static readonly HashSet<string> Location = new() { "LineNo", "ColOffset", "EndLineNo", "EndColOffset" };

        // Python.asdl's product types and the two names C# could not keep.
        private static readonly Dictionary<string, string> LowerCaseProducts = new()
        {
            ["Arguments"] = "arguments", ["Arg"] = "arg", ["Keyword"] = "keyword", ["Alias"] = "alias",
            ["Withitem"] = "withitem", ["Comprehension"] = "comprehension", ["MatchCase"] = "match_case",
            ["ExprStmt"] = "Expr", ["Mod_"] = "Mod",
        };

        public static string Dump(GeneratedAstNode node)
        {
            var text = new StringBuilder();
            Write(text, node);
            return text.ToString();
        }

        private static string FieldName(string property)
        {
            var name = new StringBuilder();
            for (int i = 0; i < property.Length; i++)
            {
                if (char.IsUpper(property[i]) && i > 0)
                    name.Append('_');
                name.Append(char.ToLowerInvariant(property[i]));
            }
            return name.ToString();
        }

        private static void Write(StringBuilder text, object? value)
        {
            switch (value)
            {
                case GeneratedSeq sequence:
                    text.Append('[');
                    for (int i = 0; i < sequence.Count; i++)
                    {
                        if (i > 0)
                            text.Append(", ");
                        Write(text, ((IList<GeneratedPtr>)sequence)[i]);
                    }
                    text.Append(']');
                    break;
                case GeneratedIdentifier identifier:
                    text.Append(new PyString(identifier.Value).ToRepr().Value);
                    break;
                case GeneratedPyConstant constant:
                    text.Append(ConstantRepr(constant));
                    break;
                case string plain:
                    text.Append(new PyString(plain).ToRepr().Value);
                    break;
                case int number:
                    text.Append(number);
                    break;
                case bool truth:
                    text.Append(truth ? 1 : 0);
                    break;
                case null:
                    text.Append("None");
                    break;
                default:
                    WriteNode(text, value);
                    break;
            }
        }

        private static void WriteNode(StringBuilder text, object node)
        {
            var type = node.GetType();
            string name = type.Name.StartsWith("Generated") ? type.Name.Substring("Generated".Length) : type.Name;
            text.Append(LowerCaseProducts.TryGetValue(name, out var asdlName) ? asdlName : name);
            text.Append('(');
            bool first = true;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (Location.Contains(property.Name) || property.GetIndexParameters().Length > 0)
                    continue;
                var value = property.GetValue(node);
                bool isSequence = typeof(GeneratedSeq).IsAssignableFrom(property.PropertyType);
                if (value == null && !isSequence)
                    continue;
                if (!first)
                    text.Append(", ");
                first = false;
                text.Append(FieldName(property.Name)).Append('=');
                if (value == null)
                    text.Append("[]");
                else
                    Write(text, value);
            }
            text.Append(')');
        }

        private static string ConstantRepr(GeneratedPyConstant constant) => constant switch
        {
            GeneratedPyConstantNone => "None",
            GeneratedPyConstantBool truth => truth.Value ? "True" : "False",
            GeneratedPyConstantInt number => new PyInt(number.Value).ToRepr().Value,
            GeneratedPyConstantFloat real => new PyFloat(real.Value).ToRepr().Value,
            GeneratedPyConstantString text => new PyString(text.Value).ToRepr().Value,
            GeneratedPyConstantBytes bytes => new PyBytes(bytes.Value).ToRepr().Value,
            GeneratedPyConstantEllipsis => "Ellipsis",
            GeneratedPyConstantComplex complex => new PyComplex(complex.Real, complex.Imag).ToRepr().Value,
            _ => constant.ToString() ?? "",
        };
    }
}
