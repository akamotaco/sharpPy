using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// _string module - CPython 3.12: Modules/stringmodule.c
    /// Provides formatter_parser and formatter_field_name_split used by str.format.
    /// </summary>
    internal static class _StringModule
    {
        internal static PyModule CreateStringModule()
        {
            var module = new PyModule("_string");
            module.ModuleDict["formatter_parser"] = new PyBuiltinFunction("formatter_parser", FormatterParser);
            module.ModuleDict["formatter_field_name_split"] = new PyBuiltinFunction("formatter_field_name_split", FormatterFieldNameSplit);
            return module;
        }

        /// <summary>
        /// formatter_parser(str) -> iterator of (literal_text, field_name, format_spec, conversion)
        /// CPython 3.12: Objects/stringlib/unicode_format.h MarkupIterator
        /// </summary>
        private static PyObject FormatterParser(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"formatter_parser() takes exactly one argument ({args.Length} given)");

            if (args[0] is not PyString s)
                throw PyTypeError.Create($"formatter_parser() argument must be str, not {args[0].GetTypeName()}");

            return new FormatterParserIterator(s.Value);
        }

        /// <summary>
        /// formatter_field_name_split(str) -> (first, iterator of (is_attr, name))
        /// CPython 3.12: Objects/stringlib/unicode_format.h
        /// </summary>
        private static PyObject FormatterFieldNameSplit(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"formatter_field_name_split() takes exactly one argument ({args.Length} given)");

            if (args[0] is not PyString s)
                throw PyTypeError.Create($"formatter_field_name_split() argument must be str, not {args[0].GetTypeName()}");

            string name = s.Value;
            if (name.Length == 0)
                throw PyValueError.Create("empty attribute name");

            // Split on first '.' or '['
            int firstEnd = name.Length;
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] == '.' || name[i] == '[')
                {
                    firstEnd = i;
                    break;
                }
            }

            string firstStr = name.Substring(0, firstEnd);
            PyObject first;
            if (firstStr.Length > 0 && IsAllDigits(firstStr))
                first = new PyInt(long.Parse(firstStr));
            else
                first = new PyString(firstStr);

            // Remaining parts are (is_attr, name) pairs
            var parts = new List<PyObject>();
            int pos = firstEnd;
            while (pos < name.Length)
            {
                char sep = name[pos];
                pos++; // skip separator
                if (sep == '.')
                {
                    int end = pos;
                    while (end < name.Length && name[end] != '.' && name[end] != '[')
                        end++;
                    string attrName = name.Substring(pos, end - pos);
                    parts.Add(new PyTuple(new PyObject[] { PyBool.True, new PyString(attrName) }));
                    pos = end;
                }
                else // '['
                {
                    int end = name.IndexOf(']', pos);
                    if (end < 0)
                        throw PyValueError.Create("invalid format specifier");
                    string indexName = name.Substring(pos, end - pos);
                    parts.Add(new PyTuple(new PyObject[] { PyBool.False, IsAllDigits(indexName) ? new PyInt(System.Numerics.BigInteger.Parse(indexName)) : new PyString(indexName) }));
                    pos = end + 1;
                }
            }

            var iter = new ListIterator(parts);
            return new PyTuple(new PyObject[] { first, iter });
        }

        private static bool IsAllDigits(string s)
        {
            foreach (char c in s)
            {
                if (c < '0' || c > '9')
                    return false;
            }
            return s.Length > 0;
        }
    }

    /// <summary>
    /// Iterator for formatter_parser - yields (literal_text, field_name, format_spec, conversion) tuples.
    /// CPython 3.12: Objects/stringlib/unicode_format.h MarkupIterator
    /// </summary>
    /// <summary>MarkupIterator_next (Objects/stringlib/unicode_format.h).</summary>
    internal class FormatterParserIterator : PyIterator
    {
        private readonly string _text;
        private int _at;

        internal FormatterParserIterator(string text) { _text = text; }

        public override PyObject Next()
        {
            int n = _text.Length;
            if (_at >= n) throw PyStopIteration.Create();
            int start = _at;
            bool markup = false;
            char c = '\0';
            while (_at < n)
            {
                c = _text[_at++];
                if (c == '{' || c == '}') { markup = true; break; }
            }
            bool atEnd = _at >= n;
            int length = _at - start;
            if (c == '}' && (atEnd || _text[_at] != '}'))
                throw PyValueError.Create("Single '}' encountered in format string");
            if (atEnd && c == '{')
                throw PyValueError.Create("Single '{' encountered in format string");
            if (!atEnd && markup)
            {
                if (_text[_at] == c) { _at++; markup = false; }
                else length--;
            }
            var literal = new PyString(_text.Substring(start, length));
            if (!markup)
                return new PyTuple(new PyObject[] { literal, PyNone.Instance, PyNone.Instance, PyNone.Instance });

            int depth = 1, fieldStart = _at;
            bool bracket = false;
            while (_at < n)
            {
                c = _text[_at++];
                if (c == '[') bracket = true;
                else if (c == ']') bracket = false;
                else if (bracket) continue;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) break;
            }
            if (depth > 0) throw PyValueError.Create("expected '}' before end of string");
            var field = _text.Substring(fieldStart, _at - 1 - fieldStart);

            int stop = 0;
            bool inIndex = false;
            for (; stop < field.Length; stop++)
            {
                char f = field[stop];
                if (f == '[') inIndex = true;
                else if (f == ']') inIndex = false;
                else if (!inIndex && (f == ':' || f == '!')) break;
            }
            var name = field.Substring(0, stop);
            PyObject conversion = PyNone.Instance;
            string spec = "";
            if (stop < field.Length && field[stop] == '!')
            {
                if (stop + 1 >= field.Length)
                    throw PyValueError.Create("end of string while looking for conversion specifier");
                conversion = new PyString(field.Substring(stop + 1, 1));
                stop += 2;
                if (stop < field.Length && field[stop] != ':')
                    throw PyValueError.Create("expected ':' after conversion specifier");
            }
            if (stop < field.Length && field[stop] == ':') spec = field.Substring(stop + 1);
            return new PyTuple(new PyObject[] { literal, new PyString(name), new PyString(spec), conversion });
        }
    }

    /// <summary>
    /// Simple list-based iterator used by formatter_field_name_split.
    /// </summary>
    internal class ListIterator : PyIterator
    {
        private readonly List<PyObject> _items;
        private int _index = 0;

        public ListIterator(List<PyObject> items)
        {
            _items = items;
        }

        public override PyObject Next()
        {
            if (_index >= _items.Count)
                throw PyStopIteration.Create();
            return _items[_index++];
        }
    }
}
