namespace SharpPy.Modules
{
    /// <summary>
    /// _string (Objects/stringlib/unicode_format.h's formatter_parser and
    /// formatter_field_name_split): the MarkupIterator and FieldNameIterator str.format reads
    /// with (PyStringFormatter), handed out as iterators.
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

        // formatter_parser: an iterator of (literal_text, field_name, format_spec, conversion)
        private static PyObject FormatterParser(PyObject[] args)
        {
            if (args[0] is not PyString s)
                throw PyTypeError.Create($"expected str, got {args[0].GetPyType().FullName}");
            return new PyStringFormatter.FormatterIterator(s);
        }

        // formatter_field_name_split: (first, iterator of (is_attr, name))
        private static PyObject FormatterFieldNameSplit(PyObject[] args)
        {
            if (args[0] is not PyString s)
                throw PyTypeError.Create($"expected str, got {args[0].GetPyType().FullName}");
            return PyStringFormatter.FieldNameSplitObject(s);
        }
    }
}
