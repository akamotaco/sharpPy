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
            module.ModuleDict["QUOTE_MINIMAL"] = new PyInt(0);
            module.ModuleDict["QUOTE_ALL"] = new PyInt(1);
            module.ModuleDict["QUOTE_NONNUMERIC"] = new PyInt(2);
            module.ModuleDict["QUOTE_NONE"] = new PyInt(3);
            module.ModuleDict["QUOTE_STRINGS"] = new PyInt(4);
            module.ModuleDict["QUOTE_NOTNULL"] = new PyInt(5);
            module.ModuleDict["__version__"] = new PyString("1.0");
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
            var t = new PyType("Dialect", new[] { PyType.ObjectType }, "_csv") { ConstructedByNewSlot = true };
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

        private static PyObject DialectNew(PyObject cls, PyObject[] args, PyDict kwargs)
        {
            var d = new PyDialect();
            d.Delimiter = new PyString(",");
            d.DoubleQuote = PyBool.FromBool(true);
            d.EscapeChar = PyNone.Instance;
            d.LineTerminator = new PyString("\r\n");
            d.QuoteChar = new PyString("\"");
            d.Quoting = new PyInt(0);
            d.SkipInitialSpace = PyBool.FromBool(false);
            d.Strict = PyBool.FromBool(false);

            PyObject dialectArg = null;
            if (args.Length > 0)
                dialectArg = args[0];

            if (dialectArg != null && dialectArg != PyNone.Instance)
            {
                if (dialectArg is PyString nameStr)
                {
                    if (!_dialects.TryGetStoredValue(nameStr, out var reg))
                        throw PyExceptionSlots.Raise(ErrorType, "unknown dialect");
                    dialectArg = reg;
                }
                CopyDialectAttrs(d, dialectArg);
            }

            if (kwargs != null)
            {
                foreach (var key in kwargs.Keys().Items)
                {
                    var k = key.ToStr().Value;
                    if (k == "delimiter") d.Delimiter = ValidateChar(kwargs.GetItem(key), "delimiter");
                    else if (k == "doublequote") d.DoubleQuote = PyBool.FromBool(kwargs.GetItem(key).IsTrue());
                    else if (k == "escapechar") d.EscapeChar = ValidateOptionalChar(kwargs.GetItem(key), "escapechar");
                    else if (k == "lineterminator") d.LineTerminator = ValidateString(kwargs.GetItem(key), "lineterminator");
                    else if (k == "quotechar") d.QuoteChar = ValidateOptionalChar(kwargs.GetItem(key), "quotechar");
                    else if (k == "quoting") d.Quoting = ValidateQuoting(kwargs.GetItem(key));
                    else if (k == "skipinitialspace") d.SkipInitialSpace = PyBool.FromBool(kwargs.GetItem(key).IsTrue());
                    else if (k == "strict") d.Strict = PyBool.FromBool(kwargs.GetItem(key).IsTrue());
                }
            }

            ValidateDialect(d);
            return d;
        }

        private static void CopyDialectAttrs(PyDialect d, PyObject src)
        {
            if (src is PyDialect pd)
            {
                d.Delimiter = pd.Delimiter; d.DoubleQuote = pd.DoubleQuote;
                d.EscapeChar = pd.EscapeChar; d.LineTerminator = pd.LineTerminator;
                d.QuoteChar = pd.QuoteChar; d.Quoting = pd.Quoting;
                d.SkipInitialSpace = pd.SkipInitialSpace; d.Strict = pd.Strict;
                return;
            }
            try { d.Delimiter = ValidateChar(src.GetAttribute("delimiter"), "delimiter"); } catch { }
            try { d.DoubleQuote = PyBool.FromBool(src.GetAttribute("doublequote").IsTrue()); } catch { }
            try { d.EscapeChar = ValidateOptionalChar(src.GetAttribute("escapechar"), "escapechar"); } catch { }
            try { d.LineTerminator = ValidateString(src.GetAttribute("lineterminator"), "lineterminator"); } catch { }
            try { d.QuoteChar = ValidateOptionalChar(src.GetAttribute("quotechar"), "quotechar"); } catch { }
            try { d.Quoting = ValidateQuoting(src.GetAttribute("quoting")); } catch { }
            try { d.SkipInitialSpace = PyBool.FromBool(src.GetAttribute("skipinitialspace").IsTrue()); } catch { }
            try { d.Strict = PyBool.FromBool(src.GetAttribute("strict").IsTrue()); } catch { }
        }

        private static PyObject ValidateChar(PyObject val, string name)
        {
            if (val is PyString s && s.Value.Length == 1) return val;
            throw PyTypeError.Create("\"" + name + "\" must be a 1-character string");
        }

        private static PyObject ValidateOptionalChar(PyObject val, string name)
        {
            if (val == PyNone.Instance) return val;
            return ValidateChar(val, name);
        }

        private static PyObject ValidateString(PyObject val, string name)
        {
            if (val is PyString) return val;
            throw PyTypeError.Create(name + " must be set");
        }

        private static PyObject ValidateQuoting(PyObject val)
        {
            int q = (int)val.ToInt();
            if (q < 0 || q > 5) throw PyTypeError.Create("bad \"quoting\" value");
            return new PyInt(q);
        }

        private static void ValidateDialect(PyDialect d)
        {
            int quoting = (int)d.Quoting.ToInt();
            if (quoting != 3 && d.QuoteChar == PyNone.Instance)
                throw PyTypeError.Create("quotechar must be set if quoting enabled");
            if (d.LineTerminator == PyNone.Instance)
                throw PyTypeError.Create("lineterminator must be set");
        }

        // --- Reader ---
        private static PyObject ReaderFunc(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1) throw PyTypeError.Create("reader() requires an iterable argument");
            var iterable = args[0];
            var dialect = ParseDialect(args, kwargs);
            return new PyReader(iterable, dialect);
        }

        private static PyDialect ParseDialect(PyObject[] args, PyDict kwargs)
        {
            var dialectArg = args.Length > 1 ? args[1] : PyNone.Instance;
            var d = new PyDialect();
            d.Delimiter = new PyString(",");
            d.DoubleQuote = PyBool.FromBool(true);
            d.EscapeChar = PyNone.Instance;
            d.LineTerminator = new PyString("\r\n");
            d.QuoteChar = new PyString("\"");
            d.Quoting = new PyInt(0);
            d.SkipInitialSpace = PyBool.FromBool(false);
            d.Strict = PyBool.FromBool(false);

            if (dialectArg != PyNone.Instance)
            {
                if (dialectArg is PyString nameStr)
                {
                    if (!_dialects.TryGetStoredValue(nameStr, out var reg))
                        throw PyExceptionSlots.Raise(ErrorType, "unknown dialect");
                    dialectArg = reg;
                }
                CopyDialectAttrs(d, dialectArg);
            }

            if (kwargs != null)
            {
                foreach (var key in kwargs.Keys().Items)
                {
                    var k = key.ToStr().Value;
                    if (k == "delimiter") d.Delimiter = ValidateChar(kwargs.GetItem(key), "delimiter");
                    else if (k == "doublequote") d.DoubleQuote = PyBool.FromBool(kwargs.GetItem(key).IsTrue());
                    else if (k == "escapechar") d.EscapeChar = ValidateOptionalChar(kwargs.GetItem(key), "escapechar");
                    else if (k == "lineterminator") d.LineTerminator = ValidateString(kwargs.GetItem(key), "lineterminator");
                    else if (k == "quotechar") d.QuoteChar = ValidateOptionalChar(kwargs.GetItem(key), "quotechar");
                    else if (k == "quoting") d.Quoting = ValidateQuoting(kwargs.GetItem(key));
                    else if (k == "skipinitialspace") d.SkipInitialSpace = PyBool.FromBool(kwargs.GetItem(key).IsTrue());
                    else if (k == "strict") d.Strict = PyBool.FromBool(kwargs.GetItem(key).IsTrue());
                }
            }
            ValidateDialect(d);
            return d;
        }

        // --- Writer ---
        private static PyObject WriterFunc(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1) throw PyTypeError.Create("writer() requires a file object");
            var fileobj = args[0];
            try { fileobj.GetAttribute("write"); }
            catch { throw PyTypeError.Create("argument 1 must have a \"write\" method"); }
            var dialect = ParseDialect(args, kwargs);
            return new PyWriter(fileobj, dialect);
        }

        // --- Dialect registry ---
        private static PyObject RegisterDialectFunc(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1) throw PyTypeError.Create("register_dialect() requires a name");
            var name = args[0];
            PyObject dialect;
            if (args.Length > 1 && args[1] != PyNone.Instance)
                dialect = args[1];
            else
                dialect = DialectNew(DialectType, Array.Empty<PyObject>(), kwargs);
            _dialects.SetItem(name, dialect);
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
            return new PyInt(old);
        }

        // --- PyDialect ---
        private class PyDialect : PyObject
        {
            public PyObject Delimiter = new PyString(",");
            public PyObject DoubleQuote = PyBool.FromBool(true);
            public PyObject EscapeChar = PyNone.Instance;
            public PyObject LineTerminator = new PyString("\r\n");
            public PyObject QuoteChar = new PyString("\"");
            public PyObject Quoting = new PyInt(0);
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

            public override PyObject GetIterator() => this;

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
                        list.Append(PyType.FloatType.Call(new PyObject[] { new PyString(f) }, null));
                    else
                        list.Append(new PyString(f));
                }
                return list;
            }
        }

        private static readonly PyType ReaderType = CreateReaderType();

        private static PyType CreateReaderType()
        {
            var t = new PyType("reader", new[] { PyType.ObjectType }, "_csv");
            t.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", t, (self, args, kwargs) => self.GetIterator());
            t.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", t, (self, args, kwargs) => self.IterNext());
            t.TypeDict["dialect"] = new PyGetSetDescriptor("dialect", t, self => ((PyReader)self)._dialect);
            t.TypeDict["line_num"] = new PyGetSetDescriptor("line_num", t, self => new PyInt(((PyReader)self)._lineNum));
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
                _fileobj.GetAttribute("write").Call(new PyObject[] { new PyString(sb.ToString()) }, null);
                return PyNone.Instance;
            }

            public PyObject WriteRows(PyObject[] args, PyDict kwargs)
            {
                if (args.Length < 1) throw PyTypeError.Create("writerows() requires an iterable argument");
                var rows = args[0];
                var it = rows.GetIterator();
                while (true)
                {
                    PyObject row;
                    try { row = it.Next(); }
                    catch (PythonException e) when (e.PyException is PyStopIteration) { break; }
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
                    4 => field is PyString or PyStrSubclass,
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