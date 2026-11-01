using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpPy.Generated;

namespace SharpPy
{
    /// <summary>The entry tree of runtime/symtable.cs, in the form tools/compare_symtable.py expects.</summary>
    public static class SymtableDumper
    {
        public static string Dump(GeneratedMod mod, string filename)
        {
            var table = PySymtable.Build(mod, filename, PyFuture.FromAst(mod, filename).Annotations);
            var text = new StringBuilder();
            Entry(table.Top, "", text);
            return text.ToString();
        }

        private static void Entry(PySTEntry ste, string prefix, StringBuilder text)
        {
            var symbols = ste.Symbols.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}");
            text.Append(prefix).Append(ste.Name).Append(':').Append((int)ste.Type).Append(':').Append(ste.Lineno)
                .Append(':').Append(ste.Nested ? 1 : 0).Append(" vars=").Append(string.Join(",", ste.Varnames))
                .Append(' ').Append(string.Join(" ", symbols)).Append('\n');
            foreach (var child in ste.Children)
                Entry(child, prefix + " ", text);
        }
    }
}
