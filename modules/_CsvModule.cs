using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SharpPy.Modules
{
    internal static class _CsvModule
    {
        private static int _fieldSizeLimit = 131072;

        internal static PyModule CreateCsvModule()
        {
            var module = new PyModule("_csv");
            module.ModuleDict["QUOTE_MINIMAL"] = PyInt.From(0);
            module.ModuleDict["QUOTE_ALL"] = PyInt.From(1);
            module.ModuleDict["QUOTE_NONNUMERIC"] = PyInt.From(2);
            module.ModuleDict["QUOTE_NONE"] = PyInt.From(3);
            module.ModuleDict["QUOTE_STRINGS"] = PyInt.From(4);
            module.ModuleDict["QUOTE_NOTNULL"] = PyInt.From(5);
            module.ModuleDict["__version__"] = PyString.From("1.0");
            module.ModuleDict["Error"] = ErrorType;
            module.ModuleDict["Dialect"] = DialectType;
            module.ModuleDict["reader"] = new PyBuiltinFunction("reader", ReaderFunc);
            module.ModuleDict["writer"] = new PyBuiltinFunction("writer", WriterFunc);
            module.ModuleDict["register_dialect"] = new PyBuiltinFunction("register_dialect", RegisterDialectFunc);
            module.ModuleDict["unregister_dialect"] = new PyBuiltinFunction("unregister_dialect", UnregisterDialectFunc);
            module.ModuleDict["get_dialect"] = new PyBuiltinFunction("get_dialect", GetDialectFunc);
            module.ModuleDict["list_dialects"] = new PyBuiltinFunction("list_dialects", ListDialectsFunc);
            module.ModuleDict["field_size_limit"] = new PyBuiltinFunction("field_size_limit", FieldSizeLimitFunc);
            module.ModuleDict["_dialects"] = _dialects;
            return module;
        }

        private static readonly PyDict _dialects = new PyDict();

        internal static readonly PyType ErrorType = PyExceptionSlots.NewException("_csv", "Error", PyType.ExceptionType);

        // --- Dialect ---
        private static readonly PyType DialectType = CreateDialectType();

        private static PyType CreateDialectType()
        {
            var t = new PyType("Dialect", new[] { PyType.ObjectType }, "_csv");
            t.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", t, (cls, args, kwargs) => DialectNew(cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            t.TypeDict["delimiter"] = new PyGetSetDescriptor("delimiter", t, self => ((PyDialect)self).Delimiter);
            t.TypeDict["doublequote"] = new PyGetSetDescriptor("doublequote", t, self => ((PyDialect)self).DoubleQuote);
            t.TypeDict["escapechar"] = new PyGetSetDescriptor("escapechar", t, self => ((PyDialect)self).EscapeChar);
            t.TypeDict["lineterminator"] = new PyGetSetDescriptor("lineterminator", t, self => ((PyDialect)self).LineTerminator);
            t.TypeDict["quotechar"] = new PyGetSetDescriptor("quotechar", t, self => ((PyDialect)self).QuoteChar);
            t.TypeDict["quoting"] = new PyGetSetDescriptor("quoting", t, self => ((PyDialect)self).Quoting);
            t.TypeDict["skipinitialspace"] = new PyGetSetDescriptor("skipinitialspace", t, self => ((PyDialect)self).SkipInitialSpace);
            t.TypeDict["strict"] = new PyGetSetDescriptor("strict", t, self => ((PyDialect)self).Strict);
            return t;
        }

        private static readonly string[] DialectArgs =
            { "dialect", "delimiter", "doublequote", "escapechar", "lineterminator", "quotechar", "quoting", "skipinitialspace", "strict" };

        // dialect_new (Modules/_csv.c): the one place a Dialect is made; reader, writer and
        // register_dialect come here through _call_dialect.
        private static PyObject DialectNew(PyObject cls, PyObject[] args, PyDict kwargs)
        {
            // PyArg_ParseTupleAndKeywords(args, kwargs, "|OOOOOOOOO", ...)
            if (args.Length > DialectArgs.Length)
                throw PyTypeError.Create($"function takes at most {DialectArgs.Length} arguments ({args.Length + (kwargs?.InternalDict.Count ?? 0)} given)");
            var v = new PyObject[DialectArgs.Length];
            Array.Copy(args, v, args.Length);
            if (kwargs != null)
                foreach (var key in kwargs.Keys().Items)
                {
                    int i = key is PyString k ? Array.IndexOf(DialectArgs, k.Value) : -1;
                    if (i < 0)
                        throw PyTypeError.Create($"'{key.ToStr().Value}' is an invalid keyword argument for this function");
                    if (i < args.Length)
                        throw PyTypeError.Create($"argument for function given by name ('{DialectArgs[i]}') and position ({i + 1})");
                    v[i] = kwargs.GetItem(key);
                }
            var dialect = v[0];
            if (dialect is PyString name)
                dialect = GetDialectFromRegistry(name);
            // Can we reuse this instance?
            if (dialect is PyDialect existing && Array.TrueForAll(v[1..], x => x == null))
                return existing;
            // DIALECT_GETATTR: an attribute the dialect cannot give is cleared and keeps the default.
            if (dialect != null)
                for (int i = 1; i < v.Length; i++)
                    v[i] ??= DialectAttr(dialect, DialectArgs[i]);
            PyObject delimiter = v[1], doublequote = v[2], escapechar = v[3], lineterminator = v[4],
                quotechar = v[5], quoting = v[6], skipinitialspace = v[7], strict = v[8];
            // quotechar=None without a quoting is QUOTE_NONE.
            bool quoteNone = quotechar == PyNone.Instance && quoting == null;
            var d = new PyDialect();
            d.Delimiter = SetChar("delimiter", delimiter, ",");
            d.DoubleQuote = PyBool.FromBool(doublequote == null || doublequote.IsTrue());
            d.EscapeChar = SetCharOrNone("escapechar", escapechar, null);
            d.LineTerminator = SetStr("lineterminator", lineterminator, "\r\n");
            d.QuoteChar = SetCharOrNone("quotechar", quotechar, "\"");
            d.Quoting = SetInt("quoting", quoting, 0);
            d.SkipInitialSpace = PyBool.FromBool(skipinitialspace != null && skipinitialspace.IsTrue());
            d.Strict = PyBool.FromBool(strict != null && strict.IsTrue());
            if (quoteNone)
                d.Quoting = PyInt.From(3);
            // dialect_check_quoting
            int q = (int)((PyInt)d.Quoting).Value;
            if (q < 0 || q > 5)
                throw PyTypeError.Create("bad \"quoting\" value");
            if (q != 3 && d.QuoteChar == PyNone.Instance)
                throw PyTypeError.Create("quotechar must be set if quoting enabled");
            if (d.LineTerminator == PyNone.Instance)
                throw PyTypeError.Create("lineterminator must be set");
            return d;
        }

        // _call_dialect
        private static PyDialect CallDialect(PyObject dialect, PyDict kwargs) =>
            (PyDialect)DialectNew(DialectType, dialect == null ? Array.Empty<PyObject>() : new[] { dialect }, kwargs);

        // get_dialect_from_registry
        private static PyObject GetDialectFromRegistry(PyObject name) =>
            _dialects.TryGetStoredValue(name, out var found) ? found : throw PyExceptionSlots.Raise(ErrorType, "unknown dialect");

        // PyObject_GetAttrString, with whatever it raises cleared.
        private static PyObject DialectAttr(PyObject src, string name)
        {
            try { return src.GetAttribute(name); }
            catch (PythonException) { return null; }
        }

        // _set_char: a str of one code point.
        private static PyObject SetChar(string name, PyObject src, string dflt)
        {
            if (src == null) return PyString.From(dflt);
            if (src is not PyString text)
                throw PyTypeError.Create($"\"{name}\" must be string, not {src.GetTypeName()}");
            if (text.CodePointLength() != 1)
                throw PyTypeError.Create($"\"{name}\" must be a 1-character string");
            return src;
        }

        // _set_char_or_none
        private static PyObject SetCharOrNone(string name, PyObject src, string dflt)
        {
            if (src == null) return dflt == null ? PyNone.Instance : PyString.From(dflt);
            if (src == PyNone.Instance) return src;
            if (src is not PyString text)
                throw PyTypeError.Create($"\"{name}\" must be string or None, not {src.GetTypeName()}");
            if (text.CodePointLength() != 1)
                throw PyTypeError.Create($"\"{name}\" must be a 1-character string");
            return src;
        }

        // _set_str
        private static PyObject SetStr(string name, PyObject src, string dflt)
        {
            if (src == null) return PyString.From(dflt);
            if (src == PyNone.Instance) return src;
            if (src is not PyString)
                throw PyTypeError.Create($"\"{name}\" must be a string");
            return src;
        }

        // _set_int: PyLong_CheckExact, then _PyLong_AsInt.
        private static PyObject SetInt(string name, PyObject src, int dflt)
        {
            if (src == null) return PyInt.From(dflt);
            if (!src.IsExactly(PyType.IntType))
                throw PyTypeError.Create($"\"{name}\" must be an integer");
            return PyInt.From(PyArgs.Int(src));
        }









        // --- Reader ---
        private static PyObject ReaderFunc(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1) throw PyTypeError.Create("reader() requires an iterable argument");
            var iterable = args[0];
            if (args.Length > 2) throw PyTypeError.Create($" expected at most 2 arguments, got {args.Length}");
            var dialect = CallDialect(args.Length > 1 ? args[1] : null, kwargs);
            return new PyReader(iterable, dialect);
        }


        // --- Writer ---
        private static PyObject WriterFunc(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1) throw PyTypeError.Create("writer() requires a file object");
            var fileobj = args[0];
            // csv_writer: PyObject_GetAttr(output_file, "write"); only its absence is this TypeError.
            if (fileobj.LookupAttribute("write") == null)
                throw PyTypeError.Create("argument 1 must have a \"write\" method");
            if (args.Length > 2) throw PyTypeError.Create($" expected at most 2 arguments, got {args.Length}");
            var dialect = CallDialect(args.Length > 1 ? args[1] : null, kwargs);
            return new PyWriter(fileobj, dialect);
        }

        // --- Dialect registry ---
        private static PyObject RegisterDialectFunc(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1) throw PyTypeError.Create("register_dialect() requires a name");
            var name = args[0];
            // _csv_register_dialect: the name is a str, the dialect is what _call_dialect makes.
            if (name is not PyString)
                throw PyTypeError.Create("dialect name must be a string");
            _dialects.SetItem(name, CallDialect(args.Length > 1 ? args[1] : null, kwargs));
            return PyNone.Instance;
        }

        private static PyObject UnregisterDialectFunc(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1) throw PyTypeError.Create("unregister_dialect() requires a name");
            var name = args[0];
            if (!_dialects.TryGetStoredValue(name, out _))
                throw PyExceptionSlots.Raise(ErrorType, "unknown dialect");
            _dialects.DelItem(name);
            return PyNone.Instance;
        }

        private static PyObject GetDialectFunc(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1) throw PyTypeError.Create("get_dialect() requires a name");
            var name = args[0];
            if (!_dialects.TryGetStoredValue(name, out var d))
                throw PyExceptionSlots.Raise(ErrorType, "unknown dialect");
            return d;
        }

        private static PyObject ListDialectsFunc(PyObject[] args, PyDict kwargs)
        {
            var list = new PyList();
            foreach (var key in _dialects.Keys().Items)
                list.Append(key);
            return list;
        }

        private static PyObject FieldSizeLimitFunc(PyObject[] args, PyDict kwargs)
        {
            int old = _fieldSizeLimit;
            if (args.Length > 0)
            {
                long val = args[0].ToInt();
                if (val < 1) throw PyValueError.Create("field size limit must be >= 1");
                _fieldSizeLimit = (int)val;
            }
            return PyInt.From(old);
        }

        // --- PyDialect ---
        private class PyDialect : PyObject
        {
            public PyObject Delimiter = PyString.From(",");
            public PyObject DoubleQuote = PyBool.FromBool(true);
            public PyObject EscapeChar = PyNone.Instance;
            public PyObject LineTerminator = PyString.From("\r\n");
            public PyObject QuoteChar = PyString.From("\"");
            public PyObject Quoting = PyInt.From(0);
            public PyObject SkipInitialSpace = PyBool.FromBool(false);
            public PyObject Strict = PyBool.FromBool(false);

            public override PyType GetPyType() => DialectType;
            public override string GetTypeName() => "Dialect";
        }

        // --- PyReader ---
        private enum ReaderState { START_RECORD, START_FIELD, ESCAPED, IN_FIELD, IN_QUOTED_FIELD, ESCAPE_IN_QUOTED_FIELD, QUOTE_IN_QUOTED_FIELD, EAT_CRNL, AFTER_ESCAPED_CRNL }

        private class PyReader : PyObject
        {
            private readonly PyObject _iterable;
            internal readonly PyDialect _dialect;
            internal int _lineNum;
            private bool _started;

            public PyReader(PyObject iterable, PyDialect dialect)
            {
                _iterable = iterable;
                _dialect = dialect;
                _lineNum = 0;
                _started = false;
            }

            public override PyType GetPyType() => ReaderType;
            public override string GetTypeName() => "reader";

            protected internal override PyObject Iter() => this;

            protected internal override PyObject IterNext()
            {
                if (!_started) { _started = true; _iter = _iterable.GetIterator(); }
                return ParseNextRecord();
            }

            private PyObject _iter;

            private PyObject ParseNextRecord()
            {
                var state = ReaderState.START_RECORD;
                var fields = new List<string>();
                var fieldQuoted = new List<bool>();
                var field = new StringBuilder();
                bool quoted = false;
                bool eof = false;

                while (true)
                {
                    string line;
                    try
                    {
                        var item = _iter.Next();
                        if (item is PyString ps)
                            line = ps.Value;
                        else
                            throw PyExceptionSlots.Raise(ErrorType, "iterator should return strings, not " + item.GetTypeName() + " (the file should be opened in text mode)");
                        _lineNum++;
                    }
                    catch (PythonException e) when (e.PyException is PyStopIteration)
                    {
                        eof = true;
                        line = null;
                    }

                    if (eof)
                    {
                        if (state == ReaderState.IN_QUOTED_FIELD)
                            throw PyExceptionSlots.Raise(ErrorType, "unexpected end of data");
                        if (state == ReaderState.START_RECORD && fields.Count == 0)
                            throw PyStopIteration.Create();
                        if (field.Length > 0 || fields.Count > 0 || state == ReaderState.IN_FIELD)
                        {
                            fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                            return MakeResult(fields, fieldQuoted);
                        }
                        throw PyStopIteration.Create();
                    }

                    // An empty line in START_RECORD is an empty record.
                    if (line.Length == 0 && state == ReaderState.START_RECORD)
                        return new PyList();

                    if (line.IndexOf('\0') >= 0)
                        throw PyExceptionSlots.Raise(ErrorType, "line contains NUL");

                    for (int i = 0; i < line.Length; i++)
                    {
                        char c = line[i];
                        char delim = _dialect.Delimiter is PyString ds ? ds.Value[0] : ',';
                        char quote = _dialect.QuoteChar is PyString qs && qs.Value.Length > 0 ? qs.Value[0] : '"';
                        char esc = _dialect.EscapeChar is PyString es && es.Value.Length > 0 ? es.Value[0] : '\0';
                        bool hasEsc = _dialect.EscapeChar != PyNone.Instance;
                        bool doublequote = _dialect.DoubleQuote.IsTrue();
                        bool skipinitialspace = _dialect.SkipInitialSpace.IsTrue();
                        bool strict = _dialect.Strict.IsTrue();

                        switch (state)
                        {
                            case ReaderState.START_RECORD:
                                if (c == '\r' || c == '\n')
                                {
                                    if (c == '\r' && i + 1 < line.Length && line[i + 1] == '\n') i++;
                                    if (fields.Count > 0 || field.Length > 0)
                                    {
                                        fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                                        return MakeResult(fields, fieldQuoted);
                                    }
                                    continue;
                                }
                                state = ReaderState.START_FIELD;
                                goto case ReaderState.START_FIELD;

                            case ReaderState.START_FIELD:
                                if (skipinitialspace && c == ' ') continue;
                                if (c == quote)
                                {
                                    quoted = true;
                                    state = ReaderState.IN_QUOTED_FIELD;
                                }
                                else if (hasEsc && c == esc)
                                {
                                    state = ReaderState.ESCAPED;
                                }
                                else if (c == delim)
                                {
                                    fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                                    field.Clear();
                                    quoted = false;
                                }
                                else if (c == '\r' || c == '\n')
                                {
                                    if (c == '\r' && i + 1 < line.Length && line[i + 1] == '\n') i++;
                                    fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                                    return MakeResult(fields, fieldQuoted);
                                }
                                else
                                {
                                    field.Append(c);
                                    state = ReaderState.IN_FIELD;
                                }
                                break;

                            case ReaderState.ESCAPED:
                                if (c == '\r' || c == '\n')
                                {
                                    field.Append(c);
                                    state = ReaderState.AFTER_ESCAPED_CRNL;
                                }
                                else
                                {
                                    field.Append(c);
                                    state = ReaderState.IN_FIELD;
                                }
                                break;

                            case ReaderState.IN_FIELD:
                                if (c == delim)
                                {
                                    fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                                    field.Clear();
                                    quoted = false;
                                    state = ReaderState.START_FIELD;
                                }
                                else if (hasEsc && c == esc)
                                {
                                    state = ReaderState.ESCAPED;
                                }
                                else if (c == '\r' || c == '\n')
                                {
                                    if (c == '\r' && i + 1 < line.Length && line[i + 1] == '\n') i++;
                                    fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                                    return MakeResult(fields, fieldQuoted);
                                }
                                else
                                {
                                    field.Append(c);
                                }
                                break;

                            case ReaderState.IN_QUOTED_FIELD:
                                if (hasEsc && c == esc)
                                {
                                    state = ReaderState.ESCAPE_IN_QUOTED_FIELD;
                                }
                                else if (c == quote)
                                {
                                    state = ReaderState.QUOTE_IN_QUOTED_FIELD;
                                }
                                else if (c == '\r' || c == '\n')
                                {
                                    field.Append(c);
                                }
                                else
                                {
                                    field.Append(c);
                                }
                                break;

                            case ReaderState.ESCAPE_IN_QUOTED_FIELD:
                                field.Append(c);
                                state = ReaderState.IN_QUOTED_FIELD;
                                break;

                            case ReaderState.QUOTE_IN_QUOTED_FIELD:
                                if (c == quote)
                                {
                                    field.Append(quote);
                                    state = ReaderState.IN_QUOTED_FIELD;
                                }
                                else if (c == delim)
                                {
                                    fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                                    field.Clear();
                                    quoted = false;
                                    state = ReaderState.START_FIELD;
                                }
                                else if (c == '\r' || c == '\n')
                                {
                                    if (c == '\r' && i + 1 < line.Length && line[i + 1] == '\n') i++;
                                    fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                                    return MakeResult(fields, fieldQuoted);
                                }
                                else
                                {
                                    if (strict)
                                        throw PyExceptionSlots.Raise(ErrorType, "'" + quote + "' expected after '" + quote + "'");
                                    field.Append(c);
                                    state = ReaderState.IN_FIELD;
                                }
                                break;

                            case ReaderState.EAT_CRNL:
                                if (c != '\r' && c != '\n')
                                {
                                    state = ReaderState.START_RECORD;
                                    goto case ReaderState.START_RECORD;
                                }
                                break;

                            case ReaderState.AFTER_ESCAPED_CRNL:
                                if (c == '\r' || c == '\n')
                                {
                                    field.Append(c);
                                }
                                else
                                {
                                    field.Append(c);
                                    state = ReaderState.IN_FIELD;
                                }
                                break;
                        }
                    }

                    // End of line processing
                    if (state == ReaderState.IN_QUOTED_FIELD)
                    {
                        // Continue reading next line for quoted field
                        continue;
                    }
                    if (state == ReaderState.IN_FIELD || state == ReaderState.START_FIELD || state == ReaderState.ESCAPED)
                    {
                        fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                        return MakeResult(fields, fieldQuoted);
                    }
                    if (state == ReaderState.QUOTE_IN_QUOTED_FIELD)
                    {
                        fields.Add(field.ToString());
 fieldQuoted.Add(quoted);
                        return MakeResult(fields, fieldQuoted);
                    }
                    if (state == ReaderState.START_RECORD)
                    {
                        if (fields.Count > 0)
                            return MakeResult(fields, fieldQuoted);
                        continue;
                    }
                    if (state == ReaderState.AFTER_ESCAPED_CRNL)
                    {
                        continue;
                    }
                }
            }

            // parse_save_field: an unquoted field may be converted, by the quoting mode.
            private PyObject MakeResult(List<string> fields, List<bool> fieldQuoted)
            {
                int quoting = (int)_dialect.Quoting.ToInt();
                var list = new PyList();
                for (int n = 0; n < fields.Count; n++)
                {
                    string f = fields[n];
                    if (f.Length > _fieldSizeLimit)
                        throw PyExceptionSlots.Raise(ErrorType, $"field larger than field limit ({_fieldSizeLimit})");
                    bool unquoted = !fieldQuoted[n];
                    if (unquoted && f.Length == 0 && (quoting == 4 || quoting == 5))
                        list.Append(PyNone.Instance);
                    else if (unquoted && (quoting == 2 || quoting == 4))
                        list.Append(PyType.FloatType.Call(new PyObject[] { PyString.From(f) }, null));
                    else
                        list.Append(PyString.From(f));
                }
                return list;
            }
        }

        private static readonly PyType ReaderType = CreateReaderType();

        private static PyType CreateReaderType()
        {
            var t = new PyType("reader", new[] { PyType.ObjectType }, "_csv");
            t.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", t, (self, args, kwargs) => self.Iter());
            t.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", t, (self, args, kwargs) => self.IterNext());
            t.TypeDict["dialect"] = new PyGetSetDescriptor("dialect", t, self => ((PyReader)self)._dialect);
            t.TypeDict["line_num"] = new PyGetSetDescriptor("line_num", t, self => PyInt.From(((PyReader)self)._lineNum));
            return t;
        }

        // --- PyWriter ---
        private class PyWriter : PyObject
        {
            private readonly PyObject _fileobj;
            internal readonly PyDialect _dialect;

            public PyWriter(PyObject fileobj, PyDialect dialect)
            {
                _fileobj = fileobj;
                _dialect = dialect;
            }

            public override PyType GetPyType() => WriterType;
            public override string GetTypeName() => "writer";

            public PyObject WriteRow(PyObject[] args, PyDict kwargs)
            {
                if (args.Length < 1) throw PyTypeError.Create("writerow() requires an iterable argument");
                var row = args[0];
                if (!row.HasTypeSlot("__iter__") && !row.HasTypeSlot("__getitem__"))
                    throw PyExceptionSlots.Raise(ErrorType, $"iterable expected, not {row.GetTypeName()}");
                var sb = new StringBuilder();
                bool first = true;
                char delim = _dialect.Delimiter is PyString ds ? ds.Value[0] : ',';

                var it = row.GetIterator();
                while (true)
                {
                    PyObject item;
                    try { item = it.Next(); }
                    catch (PythonException e) when (e.PyException is PyStopIteration) { break; }

                    if (!first) sb.Append(delim);
                    first = false;
                    JoinAppendData(sb, item);
                }

                // QUOTE_MINIMAL: single empty field -> quoted empty string
                if (first && (int)_dialect.Quoting.ToInt() == 0)
                {
                    char quote = _dialect.QuoteChar is PyString qs && qs.Value.Length > 0 ? qs.Value[0] : '"';
                    sb.Append(quote);
                    sb.Append(quote);
                }

                string lt = _dialect.LineTerminator is PyString ls ? ls.Value : "\r\n";
                sb.Append(lt);
                _fileobj.GetAttribute("write").Call(new PyObject[] { PyString.From(sb.ToString()) }, null);
                return PyNone.Instance;
            }

            public PyObject WriteRows(PyObject[] args, PyDict kwargs)
            {
                if (args.Length < 1) throw PyTypeError.Create("writerows() requires an iterable argument");
                var rows = args[0];
                foreach (var row in PyObject.IterateItems(rows))
                {
                    WriteRow(new PyObject[] { row }, null);
                }
                return PyNone.Instance;
            }

            private void JoinAppendData(StringBuilder sb, PyObject field)
            {
                int quoting = (int)_dialect.Quoting.ToInt();
                char delim = _dialect.Delimiter is PyString ds ? ds.Value[0] : ',';
                char quote = _dialect.QuoteChar is PyString qs && qs.Value.Length > 0 ? qs.Value[0] : '"';
                char esc = _dialect.EscapeChar is PyString es && es.Value.Length > 0 ? es.Value[0] : '\0';
                bool hasEsc = _dialect.EscapeChar != PyNone.Instance;
                bool doublequote = _dialect.DoubleQuote.IsTrue();
                string lt = _dialect.LineTerminator is PyString ls ? ls.Value : "\r\n";

                // join_append_data: the mode asks for quotes, and each character may ask too or be escaped.
                string text = field is PyNone ? "" : field is PyFloat ? field.ToRepr().Value : field.ToStr().Value;
                bool isNumber = field is PyInt or PyBool or PyFloat or PyIntSubclass or PyFloatSubclass;
                bool quoted = quoting switch
                {
                    1 => true,
                    2 => !isNumber,
                    4 => field is PyString,
                    5 => field is not PyNone,
                    _ => false,
                };
                var body = new StringBuilder(text.Length);
                foreach (char c in text)
                {
                    bool wantEscape = false;
                    if (c == delim || (hasEsc && c == esc) || c == quote || c == '\n' || c == '\r' || lt.IndexOf(c, StringComparison.Ordinal) >= 0)
                    {
                        if (quoting == 3)
                        {
                            wantEscape = true;
                        }
                        else
                        {
                            if (c == quote)
                            {
                                if (doublequote) body.Append(quote);
                                else wantEscape = true;
                            }
                            else if (hasEsc && c == esc)
                            {
                                wantEscape = true;
                            }
                            if (!wantEscape) quoted = true;
                        }
                        if (wantEscape)
                        {
                            if (!hasEsc) throw PyExceptionSlots.Raise(ErrorType, "need to escape, but no escapechar set");
                            body.Append(esc);
                        }
                    }
                    body.Append(c);
                }
                if (quoted) sb.Append(quote);
                sb.Append(body);
                if (quoted) sb.Append(quote);
            }

        }

        private static readonly PyType WriterType = CreateWriterType();

        private static PyType CreateWriterType()
        {
            var t = new PyType("writer", new[] { PyType.ObjectType }, "_csv");
            t.TypeDict["writerow"] = new PyMethodDescriptor("writerow", t, (self, args, kwargs) => ((PyWriter)self).WriteRow(args, kwargs), 1, 1);
            t.TypeDict["writerows"] = new PyMethodDescriptor("writerows", t, (self, args, kwargs) => ((PyWriter)self).WriteRows(args, kwargs), 1, 1);
            t.TypeDict["dialect"] = new PyGetSetDescriptor("dialect", t, self => ((PyWriter)self)._dialect);
            return t;
        }
    }
}