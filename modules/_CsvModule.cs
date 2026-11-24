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
            // _csv_exec: PyModule_AddObjectRef of the reader and writer types as Reader and Writer.
            module.ModuleDict["Reader"] = ReaderType;
            module.ModuleDict["Writer"] = WriterType;
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
            // csv_reader: PyObject_GetIter of the input first, then the dialect
            var inputIter = iterable.GetIterator();
            var dialect = CallDialect(args.Length > 1 ? args[1] : null, kwargs);
            return new PyReader(inputIter, dialect);
        }


        // --- Writer ---
        private static PyObject WriterFunc(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1) throw PyTypeError.Create("writer() requires a file object");
            var fileobj = args[0];
            // csv_writer: PyObject_GetAttr(output_file, "write"); only its absence is this TypeError.
            var write = fileobj.LookupAttribute("write")
                ?? throw PyTypeError.Create("argument 1 must have a \"write\" method");
            if (args.Length > 2) throw PyTypeError.Create($" expected at most 2 arguments, got {args.Length}");
            var dialect = CallDialect(args.Length > 1 ? args[1] : null, kwargs);
            return new PyWriter(write, dialect);
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
                // _csv_field_size_limit_impl: an exact int, then PyLong_AsLong.
                if (args[0].GetPyType() != PyType.IntType)
                    throw PyTypeError.Create("limit must be an integer");
                _fieldSizeLimit = (int)PyArgs.CLong(args[0]);
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
        }

        // --- PyReader (Modules/_csv.c READER) ---
        private enum ReaderState
        {
            START_RECORD, START_FIELD, ESCAPED_CHAR, IN_FIELD, IN_QUOTED_FIELD, ESCAPE_IN_QUOTED_FIELD,
            QUOTE_IN_QUOTED_FIELD, EAT_CRNL, AFTER_ESCAPED_CRNL,
        }

        // NOT_SET and EOL: values no character has.
        private const int NotSet = -1, Eol = -2;
        private const int QUOTE_MINIMAL = 0, QUOTE_ALL = 1, QUOTE_NONNUMERIC = 2, QUOTE_NONE = 3, QUOTE_STRINGS = 4, QUOTE_NOTNULL = 5;

        // DialectObj's Py_UCS4 fields: the one character of a str, NOT_SET for None.
        private static int CharOf(PyObject value) => value is PyString s ? s.ReadChar(0) : NotSet;

        // PyErr_Format's %c: the character itself.
        private static string Shown(int c) => PyString.FromCodePoint(c).Value;

        private class PyReader : PyObject
        {
            private readonly PyObject _inputIter;
            internal readonly PyDialect _dialect;
            internal int _lineNum;
            private PyList _fields;
            private readonly List<int> _field = new();
            private ReaderState _state;
            private bool _numericField;

            public PyReader(PyObject inputIter, PyDialect dialect)
            {
                _inputIter = inputIter;
                _dialect = dialect;
            }

            public override PyType GetPyType() => ReaderType;

            protected internal override PyObject Iter() => this;

            // parse_save_field: the field as a str, a float when it was an unquoted QUOTE_NONNUMERIC one
            private void SaveField()
            {
                PyObject field = PyString.FromCodePoints(_field.ToArray());
                _field.Clear();
                if (_numericField)
                {
                    _numericField = false;
                    field = PyType.FloatType.Call(new[] { field }, null);
                }
                _fields.Append(field);
            }

            // parse_add_char
            private void AddChar(int c)
            {
                if (_field.Count >= _fieldSizeLimit)
                    throw PyExceptionSlots.Raise(ErrorType, $"field larger than field limit ({_fieldSizeLimit})");
                _field.Add(c);
            }

            // parse_process_char
            private void ProcessChar(int c)
            {
                var dialect = _dialect;
                int delimiter = CharOf(dialect.Delimiter), quotechar = CharOf(dialect.QuoteChar), escapechar = CharOf(dialect.EscapeChar);
                int quoting = (int)dialect.Quoting.ToInt();
                switch (_state)
                {
                    case ReaderState.START_RECORD:
                        if (c == Eol)
                            break;
                        if (c == '\n' || c == '\r')
                        {
                            _state = ReaderState.EAT_CRNL;
                            break;
                        }
                        _state = ReaderState.START_FIELD;
                        goto case ReaderState.START_FIELD;

                    case ReaderState.START_FIELD:
                        if (c == '\n' || c == '\r' || c == Eol)
                        {
                            SaveField();
                            _state = c == Eol ? ReaderState.START_RECORD : ReaderState.EAT_CRNL;
                        }
                        else if (c == quotechar && quoting != QUOTE_NONE)
                            _state = ReaderState.IN_QUOTED_FIELD;
                        else if (c == escapechar)
                        {
                            if (quoting == QUOTE_NONNUMERIC)
                                _numericField = true;
                            _state = ReaderState.ESCAPED_CHAR;
                        }
                        else if (c == ' ' && dialect.SkipInitialSpace.IsTrue())
                        {
                            // ignore spaces at start of field
                        }
                        else if (c == delimiter)
                            SaveField();
                        else
                        {
                            if (quoting == QUOTE_NONNUMERIC)
                                _numericField = true;
                            AddChar(c);
                            _state = ReaderState.IN_FIELD;
                        }
                        break;

                    case ReaderState.ESCAPED_CHAR:
                        if (c == '\n' || c == '\r')
                        {
                            AddChar(c);
                            _state = ReaderState.AFTER_ESCAPED_CRNL;
                            break;
                        }
                        if (c == Eol)
                            c = '\n';
                        AddChar(c);
                        _state = ReaderState.IN_FIELD;
                        break;

                    case ReaderState.AFTER_ESCAPED_CRNL:
                        if (c == Eol)
                            break;
                        goto case ReaderState.IN_FIELD;

                    case ReaderState.IN_FIELD:
                        if (c == '\n' || c == '\r' || c == Eol)
                        {
                            SaveField();
                            _state = c == Eol ? ReaderState.START_RECORD : ReaderState.EAT_CRNL;
                        }
                        else if (c == escapechar)
                            _state = ReaderState.ESCAPED_CHAR;
                        else if (c == delimiter)
                        {
                            SaveField();
                            _state = ReaderState.START_FIELD;
                        }
                        else
                            AddChar(c);
                        break;

                    case ReaderState.IN_QUOTED_FIELD:
                        if (c == Eol)
                        {
                        }
                        else if (c == escapechar)
                            _state = ReaderState.ESCAPE_IN_QUOTED_FIELD;
                        else if (c == quotechar && quoting != QUOTE_NONE)
                            _state = dialect.DoubleQuote.IsTrue() ? ReaderState.QUOTE_IN_QUOTED_FIELD : ReaderState.IN_FIELD;
                        else
                            AddChar(c);
                        break;

                    case ReaderState.ESCAPE_IN_QUOTED_FIELD:
                        if (c == Eol)
                            c = '\n';
                        AddChar(c);
                        _state = ReaderState.IN_QUOTED_FIELD;
                        break;

                    case ReaderState.QUOTE_IN_QUOTED_FIELD:
                        if (quoting != QUOTE_NONE && c == quotechar)
                        {
                            AddChar(c);
                            _state = ReaderState.IN_QUOTED_FIELD;
                        }
                        else if (c == delimiter)
                        {
                            SaveField();
                            _state = ReaderState.START_FIELD;
                        }
                        else if (c == '\n' || c == '\r' || c == Eol)
                        {
                            SaveField();
                            _state = c == Eol ? ReaderState.START_RECORD : ReaderState.EAT_CRNL;
                        }
                        else if (!dialect.Strict.IsTrue())
                        {
                            AddChar(c);
                            _state = ReaderState.IN_FIELD;
                        }
                        else
                            throw PyExceptionSlots.Raise(ErrorType, $"'{Shown(delimiter)}' expected after '{Shown(quotechar)}'");
                        break;

                    case ReaderState.EAT_CRNL:
                        if (c == '\n' || c == '\r')
                        {
                        }
                        else if (c == Eol)
                            _state = ReaderState.START_RECORD;
                        else
                            throw PyExceptionSlots.Raise(ErrorType,
                                "new-line character seen in unquoted field - do you need to open the file with newline=''?");
                        break;
                }
            }

            // Reader_iternext: parse_reset, then one line at a time, each character and then EOL.
            protected internal override PyObject IterNext()
            {
                _fields = new PyList();
                _field.Clear();
                _state = ReaderState.START_RECORD;
                _numericField = false;
                do
                {
                    PyObject lineobj;
                    try
                    {
                        lineobj = _inputIter.Next();
                    }
                    catch (PythonException e) when (e.PyException is PyStopIteration)
                    {
                        if (_field.Count != 0 || _state == ReaderState.IN_QUOTED_FIELD)
                        {
                            if (_dialect.Strict.IsTrue())
                                throw PyExceptionSlots.Raise(ErrorType, "unexpected end of data");
                            SaveField();
                            break;
                        }
                        throw;
                    }
                    if (lineobj is not PyString line)
                        throw PyExceptionSlots.Raise(ErrorType,
                            $"iterator should return strings, not {lineobj.GetTypeName()} (the file should be opened in text mode)");
                    ++_lineNum;
                    int linelen = line.CodePointLength();
                    for (int pos = 0; pos < linelen; pos++)
                        ProcessChar(line.ReadChar(pos));
                    ProcessChar(Eol);
                } while (_state != ReaderState.START_RECORD);
                var fields = _fields;
                _fields = null;
                return fields;
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

        // --- PyWriter (Modules/_csv.c WRITER) ---
        private class PyWriter : PyObject
        {
            private readonly PyObject _write;
            internal readonly PyDialect _dialect;
            // rec, rec_len and num_fields
            private readonly List<int> _rec = new();
            private int _numFields;

            public PyWriter(PyObject write, PyDialect dialect)
            {
                _write = write;
                _dialect = dialect;
            }

            public override PyType GetPyType() => WriterType;

            // join_append_data: counts (copy false) or writes (copy true) the field into the record;
            // a character that needs it is quoted, doubled or escaped.
            private void JoinAppendData(PyString field, ref bool quoted, bool copy)
            {
                var dialect = _dialect;
                int delimiter = CharOf(dialect.Delimiter), quotechar = CharOf(dialect.QuoteChar), escapechar = CharOf(dialect.EscapeChar);
                int quoting = (int)dialect.Quoting.ToInt();
                var lineterminator = (PyString)dialect.LineTerminator;
                var rec = copy ? _rec : null;
                if (_numFields > 0)
                    rec?.Add(delimiter);
                if (copy && quoted)
                    rec.Add(quotechar);
                int fieldLen = field?.CodePointLength() ?? 0;
                for (int i = 0; i < fieldLen; i++)
                {
                    int c = field.ReadChar(i);
                    bool wantEscape = false;
                    if (c == delimiter || c == escapechar || c == quotechar || c == '\n' || c == '\r' ||
                        LineTerminatorHas(lineterminator, c))
                    {
                        if (quoting == QUOTE_NONE)
                            wantEscape = true;
                        else
                        {
                            if (c == quotechar)
                            {
                                if (dialect.DoubleQuote.IsTrue())
                                    rec?.Add(quotechar);
                                else
                                    wantEscape = true;
                            }
                            else if (c == escapechar)
                                wantEscape = true;
                            if (!wantEscape)
                                quoted = true;
                        }
                        if (wantEscape)
                        {
                            if (escapechar == NotSet)
                                throw PyExceptionSlots.Raise(ErrorType, "need to escape, but no escapechar set");
                            rec?.Add(escapechar);
                        }
                    }
                    rec?.Add(c);
                }
                if (quoted && copy)
                    rec.Add(quotechar);
            }

            // PyUnicode_FindChar over the line terminator
            private static bool LineTerminatorHas(PyString lineterminator, int c)
            {
                int n = lineterminator.CodePointLength();
                for (int i = 0; i < n; i++)
                    if (lineterminator.ReadChar(i) == c)
                        return true;
                return false;
            }

            // join_append: null is a None field
            private void JoinAppend(PyString field, bool quoted)
            {
                var dialect = _dialect;
                int quoting = (int)dialect.Quoting.ToInt();
                int fieldLen = field?.CodePointLength() ?? 0;
                if (fieldLen == 0 && CharOf(dialect.Delimiter) == ' ' && dialect.SkipInitialSpace.IsTrue())
                {
                    if (quoting == QUOTE_NONE || (field == null && (quoting == QUOTE_STRINGS || quoting == QUOTE_NOTNULL)))
                        throw PyExceptionSlots.Raise(ErrorType,
                            "empty field must be quoted if delimiter is a space and skipinitialspace is true");
                    quoted = true;
                }
                JoinAppendData(field, ref quoted, false);
                JoinAppendData(field, ref quoted, true);
                _numFields++;
            }

            // csv_writerow: the fields joined into one record, the line terminator, then write(line)
            public PyObject WriteRow(PyObject seq)
            {
                var dialect = _dialect;
                PyObject iter;
                try
                {
                    iter = seq.GetIterator();
                }
                catch (PythonException e) when (e.PyException is PyTypeError)
                {
                    throw PyExceptionSlots.Raise(ErrorType, $"iterable expected, not {seq.GetTypeName()}");
                }
                _rec.Clear();
                _numFields = 0;
                int quoting = (int)dialect.Quoting.ToInt();
                bool nullField = false;
                while (true)
                {
                    PyObject field;
                    try { field = iter.Next(); }
                    catch (PythonException e) when (e.PyException is PyStopIteration) { break; }
                    bool quoted = quoting switch
                    {
                        QUOTE_NONNUMERIC => !PyObject.NumberCheck(field),
                        QUOTE_ALL => true,
                        QUOTE_STRINGS => field is PyString,
                        QUOTE_NOTNULL => field is not PyNone,
                        _ => false,
                    };
                    nullField = field is PyNone;
                    if (field is PyString text)
                        JoinAppend(text, quoted);
                    else if (nullField)
                        JoinAppend(null, quoted);
                    else
                        JoinAppend(field.ToStr(), quoted);
                }
                if (_numFields > 0 && _rec.Count == 0)
                {
                    if (quoting == QUOTE_NONE || (nullField && (quoting == QUOTE_STRINGS || quoting == QUOTE_NOTNULL)))
                        throw PyExceptionSlots.Raise(ErrorType, "single empty field record must be quoted");
                    _numFields--;
                    JoinAppend(null, true);
                }
                // join_append_lineterminator
                _rec.AddRange(((PyString)dialect.LineTerminator).CodePoints());
                var line = PyString.FromCodePoints(_rec.ToArray());
                return _write.Call(new PyObject[] { line }, null);
            }

            // csv_writerows
            public PyObject WriteRows(PyObject seqseq)
            {
                foreach (var row in PyObject.IterateItems(seqseq))
                    WriteRow(row);
                return PyNone.Instance;
            }
        }

        private static readonly PyType WriterType = CreateWriterType();

        private static PyType CreateWriterType()
        {
            var t = new PyType("writer", new[] { PyType.ObjectType }, "_csv");
            t.TypeDict["writerow"] = new PyMethodDescriptor("writerow", t, (self, args, kwargs) => ((PyWriter)self).WriteRow(args[0]), 1, 1);
            t.TypeDict["writerows"] = new PyMethodDescriptor("writerows", t, (self, args, kwargs) => ((PyWriter)self).WriteRows(args[0]), 1, 1);
            t.TypeDict["dialect"] = new PyGetSetDescriptor("dialect", t, self => ((PyWriter)self)._dialect);
            return t;
        }
    }
}
