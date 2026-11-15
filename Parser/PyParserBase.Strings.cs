// Parser/string_parser.c and the f-string actions of Parser/action_helpers.c, CPython v3.12.15.
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Generated
{
    public abstract partial class PyParserBase<TResult>
    {
        // warn_invalid_escape_sequence
        private void WarnInvalidEscapeSequence(string buffer, int firstInvalidEscape, GeneratedTokenInfo t)
        {
            if (_callInvalidRules)
                return; // Do not report warnings twice.
            char c = buffer[firstInvalidEscape];
            // the tokenizer has already warned about these
            if (t.Type is PyToken.Type.FSTRING_MIDDLE or PyToken.Type.FSTRING_END && (c == '{' || c == '}'))
                return;

            bool octal = c >= '4' && c <= '7';
            string message = octal
                ? $"invalid octal escape sequence '\\{buffer.Substring(firstInvalidEscape, Math.Min(3, buffer.Length - firstInvalidEscape))}'"
                : $"invalid escape sequence '\\{c}'";

            // the lineno and the col_offset of the invalid escape sequence
            int lineno = t.Line;
            int colOffset = t.Column;
            for (int i = 0; i < firstInvalidEscape; i++)
            {
                if (buffer[i] == '\n')
                {
                    lineno++;
                    colOffset = 0;
                }
                else
                    colOffset++;
            }

            // the number of quotes in the token
            if (lineno == t.Line)
            {
                int quoteCount = 0;
                char firstQuote = '\0';
                foreach (char ch in t.Value)
                {
                    if (ch != '\'' && ch != '"')
                        break;
                    if (quoteCount == 0)
                        firstQuote = ch;
                    if (ch == firstQuote)
                        quoteCount++;
                }
                colOffset += quoteCount;
            }

            // the category is SyntaxWarning from 3.12, DeprecationWarning before
            string category = FeatureVersion >= 12 ? "SyntaxWarning" : "DeprecationWarning";
            try
            {
                Compiler.WarnExplicit(category, message, _filename, lineno);
            }
            // PyErr_ExceptionMatches(category)
            catch (PythonException error) when (error.PyException.GetPyType().IsSubclassOf(
                category == "SyntaxWarning" ? PyType.SyntaxWarningType : PyType.DeprecationWarningType))
            {
                _knownErrToken = t;
                throw KnownLocation(new PySyntaxError(message), lineno, colOffset, lineno, colOffset + 2, message);
            }
        }

        // decode_unicode_with_escapes: what is not ASCII goes through as \UXXXXXXXX, so the
        // positions of a decoding error are those of this buffer and not of the source.
        private PyString DecodeUnicodeWithEscapes(string s, GeneratedTokenInfo t)
        {
            var buffer = new StringBuilder(s.Length * 2);
            int index = 0;
            while (index < s.Length)
            {
                if (s[index] == '\\')
                {
                    buffer.Append(s[index++]);
                    if (index >= s.Length || s[index] >= 0x80)
                    {
                        buffer.Append("u005c");
                        if (index >= s.Length)
                            break;
                    }
                }
                if (s[index] >= 0x80)
                {
                    while (index < s.Length && s[index] >= 0x80)
                    {
                        int chr = PyString.CodePointAt(s, index, out int width);
                        index += width;
                        buffer.Append("\\U").Append(chr.ToString("x8"));
                    }
                }
                else
                    buffer.Append(s[index++]);
            }

            string escaped = buffer.ToString();
            PyString decoded = DecodeUnicodeEscape(escaped, out int firstInvalidEscape);
            if (firstInvalidEscape >= 0 && t != null)
                WarnInvalidEscapeSequence(escaped, firstInvalidEscape, t);
            return decoded;
        }

        // The buffer the parser hands the escape decoders holds only ASCII (decode_unicode_with_escapes
        // wrote the rest as \UXXXXXXXX) or a bytes literal's characters.
        private static byte[] Latin1Bytes(string s)
        {
            var data = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
                data[i] = (byte)s[i];
            return data;
        }

        // _PyPegen_raise_decode_error: "(unicode error) ..." or "(value error) ..." from the exception.
        private Exception DecodeErrorOf(PythonException error)
        {
            var type = error.PyException.GetPyType();
            string errtype = type.IsSubclassOf(PyType.UnicodeErrorType) ? "unicode error"
                : type.IsSubclassOf(PyType.ValueErrorType) ? "value error" : null;
            if (errtype == null)
                return error;
            return RaiseDecodeError($"({errtype}) {error.PyException.Str().Value}");
        }

        // _PyUnicode_DecodeUnicodeEscapeInternal2(s, len, NULL, NULL, ...)
        private PyString DecodeUnicodeEscape(string s, out int firstInvalidEscape)
        {
            try
            {
                return EscapeCodecs.DecodeUnicodeEscapeInternal2(Latin1Bytes(s), null, null, out _, out firstInvalidEscape);
            }
            catch (PythonException error)
            {
                throw DecodeErrorOf(error);
            }
        }

        // decode_bytes_with_escapes: _PyBytes_DecodeEscape2(s, len, NULL, ...)
        private byte[] DecodeBytesWithEscapes(string s, GeneratedTokenInfo t)
        {
            byte[] result;
            int firstInvalidEscape;
            try
            {
                result = EscapeCodecs.DecodeEscape2(Latin1Bytes(s), null, out _, out firstInvalidEscape);
            }
            catch (PythonException error)
            {
                throw DecodeErrorOf(error);
            }
            if (firstInvalidEscape >= 0)
                WarnInvalidEscapeSequence(s, firstInvalidEscape, t);
            return result;
        }

        // PyUnicode_DecodeUTF8 of a part that was no UTF-8 (the source came as bytes)
        private void CheckUtf8(string s)
        {
            for (int index = 0; index < s.Length; index++)
            {
                if (char.IsHighSurrogate(s[index]))
                    index++;
                else if (s[index] >= 0xDC80 && s[index] <= 0xDCFF)
                    throw RaiseDecodeError($"(unicode error) {Utf8Failure(s)}");
            }
        }

        private static string Utf8Failure(string s)
        {
            var bytes = new List<byte>();
            for (int index = 0; index < s.Length; index++)
            {
                if (s[index] >= 0xDC80 && s[index] <= 0xDCFF && (index == 0 || !char.IsHighSurrogate(s[index - 1])))
                    bytes.Add((byte)(s[index] - 0xDC00));
                else
                {
                    int cp = PyString.CodePointAt(s, index, out int width);
                    bytes.AddRange(Encoding.UTF8.GetBytes(PyString.CodePointText(cp)));
                    index += width - 1;
                }
            }
            try
            {
                PyBytes.From(bytes.ToArray()).GetAttribute("decode").Call(new PyObject[] { PyString.From("utf-8") }, null);
                return "";
            }
            catch (PythonException error)
            {
                return error.PyException.AsString().Value;
            }
        }

        // _PyPegen_decode_string
        private PyString DecodeString(bool raw, string s, GeneratedTokenInfo t)
        {
            CheckUtf8(s);
            // the source is wchar_t text: a raw part is PyUnicode_FromWideChar of it
            return raw ? PyString.From(s) : DecodeUnicodeWithEscapes(s, t);
        }

        // _PyPegen_ensure_imaginary
        protected GeneratedExpr EnsureImaginary(GeneratedExpr exp)
        {
            if (!(exp is GeneratedConstant { Value: GeneratedPyConstantComplex }))
                RaiseSyntaxErrorKnownLocation(exp, "imaginary number required in complex literal");
            return exp;
        }

        // _PyPegen_ensure_real
        protected GeneratedExpr EnsureReal(GeneratedExpr exp)
        {
            if (!(exp is GeneratedConstant { Value: not GeneratedPyConstantComplex }))
                RaiseSyntaxErrorKnownLocation(exp, "real number required in complex literal");
            return exp;
        }

        // _PyPegen_check_barry_as_flufl: true fails the rule
        protected bool CheckBarryAsFlufl(GeneratedTokenInfo token)
        {
            if ((_flags & CO_FUTURE_BARRY_AS_BDFL) != 0)
            {
                if (token.Value != "<>")
                    RaiseSyntaxError("with Barry as BDFL, use '<>' instead of '!='");
                return false;
            }
            return token.Value != "!=";
        }

        // _PyPegen_checked_future_import
        protected GeneratedStmt CheckedFutureImport(GeneratedIdentifier module, GeneratedAliasSeq names, int level,
            int lineno, int colOffset, int endLineno, int endColOffset)
        {
            if (level == 0 && module != null && module.Value == "__future__")
                foreach (var item in names)
                    if (item is GeneratedAlias alias && alias.Name.Value == "barry_as_FLUFL")
                        _flags |= CO_FUTURE_BARRY_AS_BDFL;
            return PyParserRuntime.CheckedFutureImport(module, names, level, lineno, colOffset, endLineno, endColOffset);
        }

        // _PyPegen_parse_string
        private GeneratedPyConstant ParseString(GeneratedTokenInfo t)
        {
            string s = t.Value;
            int index = 0;
            bool bytesMode = false, rawMode = false;
            char quote = s[0];
            if (char.IsAsciiLetter(quote))
            {
                while (!bytesMode || !rawMode)
                {
                    if (quote is 'b' or 'B')
                    {
                        quote = s[++index];
                        bytesMode = true;
                    }
                    else if (quote is 'u' or 'U')
                        quote = s[++index];
                    else if (quote is 'r' or 'R')
                    {
                        quote = s[++index];
                        rawMode = true;
                    }
                    else
                        break;
                }
            }

            index++; // Skip the leading quote char.
            int length = s.Length - index - 1;
            if (length >= 4 && s[index] == quote && s[index + 1] == quote)
            {
                index += 2;
                length -= 4;
            }
            string text = s.Substring(index, length);

            // Avoid invoking escape decoding routines if possible.
            rawMode = rawMode || text.IndexOf('\\') < 0;
            if (bytesMode)
            {
                foreach (char ch in text)
                    if (ch >= 0x80)
                        RaiseSyntaxErrorKnownLocation(t, "bytes can only contain ASCII literal characters");
                if (rawMode)
                    return new GeneratedPyConstantBytes(Encoding.Latin1.GetBytes(text));
                return new GeneratedPyConstantBytes(DecodeBytesWithEscapes(text, t));
            }
            return new GeneratedPyConstantString(DecodeString(rawMode, text, t));
        }

        // _PyPegen_constant_from_token
        protected GeneratedExpr ConstantFromToken(GeneratedTokenInfo t) => new GeneratedConstant
        {
            Value = new GeneratedPyConstantString(t.Value),
            LineNo = t.Line, ColOffset = t.Column, EndLineNo = t.EndLine, EndColOffset = t.EndColumn,
        };

        // _PyPegen_decoded_constant_from_token
        protected GeneratedExpr DecodedConstantFromToken(GeneratedTokenInfo t) => new GeneratedConstant
        {
            Value = new GeneratedPyConstantString(DecodeString(false, t.Value, t)),
            LineNo = t.Line, ColOffset = t.Column, EndLineNo = t.EndLine, EndColOffset = t.EndColumn,
        };

        // _PyPegen_constant_from_string
        protected GeneratedExpr ConstantFromString(GeneratedTokenInfo t) => new GeneratedConstant
        {
            Value = ParseString(t),
            Kind = t.Value[0] == 'u' ? "u" : null,
            LineNo = t.Line, ColOffset = t.Column, EndLineNo = t.EndLine, EndColOffset = t.EndColumn,
        };

        // _PyPegen_decode_fstring_part
        private GeneratedConstant DecodeFStringPart(bool isRaw, GeneratedConstant constant, GeneratedTokenInfo token)
        {
            string text = ((GeneratedPyConstantString)constant.Value).Value;
            if (text == "{{" || text == "}}")
                text = text.Substring(0, 1);
            isRaw = isRaw || text.IndexOf('\\') < 0;
            return new GeneratedConstant
            {
                Value = new GeneratedPyConstantString(DecodeString(isRaw, text, token)),
                LineNo = constant.LineNo, ColOffset = constant.ColOffset,
                EndLineNo = constant.EndLineNo, EndColOffset = constant.EndColOffset,
            };
        }

        // _PyPegen_joined_str
        protected GeneratedExpr JoinedStr(GeneratedTokenInfo a, GeneratedExprSeq expr, GeneratedTokenInfo b)
        {
            bool isRaw = a.Value.IndexOfAny(new[] { 'r', 'R' }) >= 0;
            var seq = new GeneratedExprSeq();
            foreach (GeneratedExpr element in expr)
            {
                var item = element;
                // A debug field: the text and the value, promoted to this level.
                if (item is GeneratedJoinedStr debug)
                {
                    seq.Add(debug.Values[0]);
                    seq.Add(debug.Values[1]);
                    continue;
                }
                if (item is GeneratedConstant constant)
                {
                    var decoded = DecodeFStringPart(isRaw, constant, b);
                    // The tokenizer gives parts that may become empty (\<newline>).
                    if (((GeneratedPyConstantString)decoded.Value).Value.Length == 0)
                        continue;
                    item = decoded;
                }
                seq.Add(item);
            }
            return new GeneratedJoinedStr
            {
                Values = seq,
                LineNo = a.Line, ColOffset = a.Column, EndLineNo = b.EndLine, EndColOffset = b.EndColumn,
            };
        }

        // _PyPegen_check_fstring_conversion
        protected GeneratedResultTokenWithMetadata CheckFstringConversion(GeneratedTokenInfo convToken, GeneratedExpr conv)
        {
            if (convToken.Line != conv.LineNo || convToken.EndColumn != conv.ColOffset)
                RaiseSyntaxErrorKnownRange(convToken, conv, "f-string: conversion type must come right after the exclamanation mark");
            return new GeneratedResultTokenWithMetadata { Result = conv, Metadata = convToken.Metadata };
        }

        // _PyPegen_setup_full_format_spec
        protected GeneratedResultTokenWithMetadata SetupFullFormatSpec(GeneratedTokenInfo colon, GeneratedExprSeq spec,
            int lineno, int colOffset, int endLineno, int endColOffset)
        {
            // 3.11 compatibility: an empty format spec is an *empty* JoinedStr.
            if (spec.Count == 1 && spec[0] is GeneratedConstant { Value: GeneratedPyConstantString { Value: "" } })
                spec = new GeneratedExprSeq();
            GeneratedExpr res;
            int n = spec.Count;
            if (n == 0 || (n == 1 && spec[0] is GeneratedConstant))
                res = new GeneratedJoinedStr { Values = spec, LineNo = lineno, ColOffset = colOffset, EndLineNo = endLineno, EndColOffset = endColOffset };
            else
                res = PyParserRuntime.ConcatenateStrings(spec, lineno, colOffset, endLineno, endColOffset);
            return new GeneratedResultTokenWithMetadata { Result = res, Metadata = colon.Metadata };
        }

        // _PyPegen_formatted_value
        protected GeneratedExpr FormattedValue(GeneratedExpr expression, GeneratedTokenInfo debug,
            GeneratedResultTokenWithMetadata conversion, GeneratedResultTokenWithMetadata format, GeneratedTokenInfo closingBrace,
            int lineno, int colOffset, int endLineno, int endColOffset)
        {
            int conversionVal = -1;
            if (conversion != null)
            {
                var conversionExpr = (GeneratedName)conversion.Result;
                string id = conversionExpr.Id.Value;
                char first = id[0];
                if (id.Length > 1 || !(first == 's' || first == 'r' || first == 'a'))
                    RaiseSyntaxErrorKnownLocation(conversionExpr,
                        "f-string: invalid conversion character %R: expected 's', 'r', or 'a'", PyString.From(id).ToRepr().Value);
                conversionVal = first;
            }
            else if (debug != null && format == null)
            {
                // If no conversion is specified, use !r for debug expressions
                conversionVal = 'r';
            }

            var formattedValue = new GeneratedFormattedValue
            {
                Value = expression,
                Conversion = conversionVal,
                FormatSpec = format?.Result,
                LineNo = lineno, ColOffset = colOffset, EndLineNo = endLineno, EndColOffset = endColOffset,
            };
            if (debug == null)
                return formattedValue;

            // Find the non whitespace token after the "="
            int debugEndLine, debugEndOffset;
            string debugMetadata;
            if (conversion != null)
            {
                debugEndLine = conversion.Result.LineNo;
                debugEndOffset = conversion.Result.ColOffset;
                debugMetadata = conversion.Metadata;
            }
            else if (format != null)
            {
                debugEndLine = format.Result.LineNo;
                debugEndOffset = format.Result.ColOffset + 1;
                debugMetadata = format.Metadata;
            }
            else
            {
                debugEndLine = endLineno;
                debugEndOffset = endColOffset;
                debugMetadata = closingBrace.Metadata;
            }
            var values = new GeneratedExprSeq(2);
            values.Add(new GeneratedConstant
            {
                Value = new GeneratedPyConstantString(debugMetadata),
                LineNo = lineno, ColOffset = colOffset + 1, EndLineNo = debugEndLine, EndColOffset = debugEndOffset - 1,
            });
            values.Add(formattedValue);
            return new GeneratedJoinedStr
            {
                Values = values,
                LineNo = lineno, ColOffset = colOffset, EndLineNo = debugEndLine, EndColOffset = debugEndOffset,
            };
        }

        // _PyPegen_concatenate_strings: the literals may not be of both kinds.
        protected GeneratedExpr ConcatenateStrings(GeneratedExprSeq strings, int lineno, int colOffset, int endLineno, int endColOffset)
        {
            bool bytesFound = false, otherFound = false;
            foreach (GeneratedExpr element in strings)
            {
                if (element is GeneratedConstant { Value: GeneratedPyConstantBytes })
                    bytesFound = true;
                else
                    otherFound = true;
            }
            if (bytesFound && otherFound)
                RaiseSyntaxError("cannot mix bytes and nonbytes literals");
            return PyParserRuntime.ConcatenateStrings(strings, lineno, colOffset, endLineno, endColOffset);
        }
    }
}
