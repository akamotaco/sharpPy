// Python/Python-tokenize.c of CPython v3.12.15: tokenize.py on the same tokenizer the parser reads.
using System.Text;
using SharpPy.Generated;

namespace SharpPy.Modules
{
    internal static class _TokenizeModule
    {
        public static PyModule Create()
        {
            var module = new PyModule("_tokenize");
            module.ModuleDict["TokenizerIter"] = new PyBuiltinFunction("TokenizerIter", (args, kwargs) =>
            {
                var readline = args.Length > 0 ? args[0] : PyObject.KwValue(kwargs, "source");
                var extra = args.Length > 1 ? args[1] : PyObject.KwValue(kwargs, "extra_tokens");
                var encoding = args.Length > 2 ? args[2] : PyObject.KwValue(kwargs, "encoding");
                return new TokenizerIter(readline, extra != null && extra.IsTrue(), (encoding as PyString)?.Value);
            });
            return module;
        }

        private sealed class TokenizerIter : PyIterator
        {
            private readonly Tokenizer _tok;
            private readonly bool _extra;
            private readonly bool _implicitNewline;
            private bool _done;

            protected override string IteratorName => "TokenizerIter";

            public TokenizerIter(PyObject readline, bool extraTokens, string encoding)
            {
                // _PyTokenizer_FromReadline reads a line at a time; the lines make the same text.
                var source = new StringBuilder();
                while (true)
                {
                    PyObject line;
                    try
                    {
                        line = readline.Call(new PyObject[0], null);
                    }
                    catch (PythonException error) when (error.PyException is PyStopIteration)
                    {
                        break;
                    }
                    // tok_readline_string: bytes decoded with "replace" when there is an encoding,
                    // otherwise str.
                    string text;
                    if (encoding != null)
                    {
                        if (line is not PyBytes bytes)
                            throw new PythonException(new PyTypeError("readline() returned a non-bytes object"));
                        text = ((PyString)bytes.GetAttribute("decode").Call(
                            new PyObject[] { PyString.From(encoding), PyString.From("replace") }, null)).Value;
                    }
                    else if (line is PyString str)
                        text = str.Value;
                    else
                        throw new PythonException(new PyTypeError("readline() returned a non-string object"));
                    if (text.Length == 0)
                        break;
                    source.Append(text);
                }
                _implicitNewline = source.Length != 0 && source[source.Length - 1] != '\n';
                _tok = new Tokenizer(source.ToString(), true, extraTokens, "<string>", preserveCrlf: true);
                _extra = extraTokens;
            }

            // _tokenizer_error
            private PythonException TokenizerError()
            {
                string message;
                PyType errtype = PyType.SyntaxErrorType;
                switch (_tok.Done)
                {
                    case Tokenizer.State.E_EOF:
                        message = "unexpected EOF in multi-line statement";
                        return PySyntaxError.Located(new PySyntaxError(message), message, "<string>", _tok.Lineno, 0, _tok.Lineno, 0, PyNone.Instance);
                    case Tokenizer.State.E_DEDENT:
                        message = "unindent does not match any outer indentation level";
                        errtype = PyType.IndentationErrorType;
                        break;
                    case Tokenizer.State.E_TABSPACE:
                        message = "inconsistent use of tabs and spaces in indentation";
                        errtype = PyType.TabErrorType;
                        break;
                    case Tokenizer.State.E_TOODEEP:
                        message = "too many levels of indentation";
                        errtype = PyType.IndentationErrorType;
                        break;
                    case Tokenizer.State.E_LINECONT:
                        message = "unexpected character after line continuation character";
                        break;
                    default:
                        message = "unknown tokenization error";
                        break;
                }
                // The line read so far without its newline; the offset is one past its end
                // (_PyPegen_byte_offset_to_character_offset of tok->inp - tok->buf).
                string buffer = _tok.Buffer;
                int size = _tok.Inp - _tok.Buf;
                if (size > 0 && buffer[_tok.Buf + size - 1] == '\n')
                    size--;
                var errorLine = PyString.From(buffer.Substring(_tok.Buf, size));
                var location = PyTuple.From(PyString.From("<string>"), PyInt.From(_tok.Lineno), PyInt.From(Characters(_tok.Buf, _tok.Buf + size) + 1),
                    errorLine, PyNone.Instance, PyNone.Instance);
                // PyErr_SetObject(errtype, (msg, location))
                return new PythonException((PyBaseException)errtype.Call(new PyObject[] { PyString.From(message), location }, null));
            }

            // tok->implicit_newline holds for the last line read.
            private bool ImplicitNewline => _implicitNewline && _tok.Inp == _tok.Buffer.Length;

            private int Characters(int from, int to)
            {
                int count = 0;
                string text = _tok.Buffer;
                for (int i = from; i < to && i < text.Length; i++)
                    if (!char.IsLowSurrogate(text[i]))
                        count++;
                return count;
            }

            // tokenizeriter_next
            protected internal override PyObject IterNext()
            {
                if (_done)
                    throw PyStopIteration.Create();
                var token = _tok.Get();
                if (token.Type == PyToken.Type.ERRORTOKEN)
                {
                    _done = true;
                    throw _tok.Error ?? TokenizerError();
                }

                var type = token.Type;
                bool stringLiteral = type is PyToken.Type.STRING or PyToken.Type.FSTRING_MIDDLE;
                bool trailing = type == PyToken.Type.ENDMARKER || (type == PyToken.Type.DEDENT && _tok.Done == Tokenizer.State.E_EOF);
                int lineStart = stringLiteral ? _tok.MultiLineStart : _tok.LineStart;
                string line = "";
                if (!(_extra && trailing))
                {
                    int size = _tok.Inp - lineStart;
                    if (size >= 1 && ImplicitNewline)
                        size -= 1;
                    line = _tok.Buffer.Substring(lineStart, size);
                }

                int lineno = stringLiteral ? _tok.FirstLineno : _tok.Lineno;
                int endLineno = _tok.Lineno;
                int column = token.StartIndex >= 0 && token.StartIndex >= lineStart ? Characters(lineStart, token.StartIndex) : -1;
                int endColumn = token.EndIndex >= 0 && token.EndIndex >= _tok.LineStart ? Characters(_tok.LineStart, token.EndIndex) : -1;
                string text = token.Value;

                if (_extra)
                {
                    if (trailing)
                    {
                        lineno = endLineno = lineno + 1;
                        column = endColumn = 0;
                    }
                    if (type is PyToken.Type.ASYNC or PyToken.Type.AWAIT)
                        type = PyToken.Type.NAME;
                    else if (type == PyToken.Type.NEWLINE)
                    {
                        if (!ImplicitNewline)
                            text = _tok.Buffer[_tok.Start] == '\r' ? "\r\n" : "\n";
                        endColumn++;
                    }
                    else if (type == PyToken.Type.NL && ImplicitNewline)
                        text = "";
                }

                if (type == PyToken.Type.ENDMARKER)
                    _done = true;
                return PyTuple.From(new PyObject[]
                {
                    PyInt.From((int)type), PyString.From(text),
                    PyTuple.From(new PyObject[] { PyInt.From(lineno), PyInt.From(column) }),
                    PyTuple.From(new PyObject[] { PyInt.From(endLineno), PyInt.From(endColumn) }),
                    PyString.From(line),
                });
            }
        }
    }
}
