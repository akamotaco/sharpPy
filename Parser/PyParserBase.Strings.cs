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

            try
            {
                Compiler.WarnExplicit("SyntaxWarning", message, _filename, lineno);
            }
            catch (PythonException error) when (error.PyException.GetTypeName() == "SyntaxWarning")
            {
                _knownErrToken = t;
                throw KnownLocation(new PySyntaxError(message), lineno, colOffset, lineno, colOffset + 2, message);
            }
        }

        // decode_unicode_with_escapes: what is not ASCII goes through as \UXXXXXXXX, so the
        // positions of a decoding error are those of this buffer and not of the source.
        private string DecodeUnicodeWithEscapes(string s, GeneratedTokenInfo t)
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
            string decoded = DecodeUnicodeEscape(escaped, out int firstInvalidEscape);
            if (firstInvalidEscape >= 0 && t != null)
                WarnInvalidEscapeSequence(escaped, firstInvalidEscape, t);
            return decoded;
        }

        private Exception UnicodeEscapeError(string s, int start, int end, string reason)
        {
            string where = end == start + 1
                ? $"can't decode byte 0x{(int)s[start]:x2} in position {start}"
                : $"can't decode bytes in position {start}-{end - 1}";
            return RaiseDecodeError($"(unicode error) 'unicodeescape' codec {where}: {reason}");
        }

        // _PyUnicode_DecodeUnicodeEscapeInternal2
        private string DecodeUnicodeEscape(string s, out int firstInvalidEscape)
        {
            firstInvalidEscape = -1;
            var result = new StringBuilder(s.Length);
            int position = 0;
            int end = s.Length;
            while (position < end)
            {
                char c = s[position++];
                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }

                int start = position - 1;
                if (position >= end)
                    throw UnicodeEscapeError(s, start, position, "\\ at end of string");
                c = s[position++];
                string message;
                switch (c)
                {
                    case '\n': continue;
                    case '\\': result.Append('\\'); continue;
                    case '\'': result.Append('\''); continue;
                    case '"': result.Append('"'); continue;
                    case 'b': result.Append('\b'); continue;
                    case 'f': result.Append('\f'); continue;
                    case 't': result.Append('\t'); continue;
                    case 'n': result.Append('\n'); continue;
                    case 'r': result.Append('\r'); continue;
                    case 'v': result.Append('\v'); continue;
                    case 'a': result.Append('\a'); continue;

                    case >= '0' and <= '7':
                        int value = c - '0';
                        if (position < end && s[position] >= '0' && s[position] <= '7')
                        {
                            value = (value << 3) + s[position++] - '0';
                            if (position < end && s[position] >= '0' && s[position] <= '7')
                                value = (value << 3) + s[position++] - '0';
                        }
                        if (value > 255 && firstInvalidEscape == -1)
                            firstInvalidEscape = position - 3;
                        result.Append((char)value);
                        continue;

                    case 'x':
                    case 'u':
                    case 'U':
                        int count = c == 'x' ? 2 : c == 'u' ? 4 : 8;
                        message = c == 'x' ? "truncated \\xXX escape" : c == 'u' ? "truncated \\uXXXX escape" : "truncated \\UXXXXXXXX escape";
                        long ch = 0;
                        for (; count > 0; position++, count--)
                        {
                            if (position >= end)
                                throw UnicodeEscapeError(s, start, position, message);
                            int digit = HexDigit(s[position]);
                            if (digit < 0)
                                throw UnicodeEscapeError(s, start, position, message);
                            ch = ch << 4 | (long)digit;
                        }
                        if (ch > 0x10FFFF)
                            throw UnicodeEscapeError(s, start, position, "illegal Unicode character");
                        AppendCodePoint(result, (int)ch);
                        continue;

                    case 'N':
                        message = "malformed \\N character escape";
                        if (position >= end)
                            throw UnicodeEscapeError(s, start, position, message);
                        if (s[position] == '{')
                        {
                            int nameStart = ++position;
                            while (position < end && s[position] != '}')
                                position++;
                            if (position >= end)
                                throw UnicodeEscapeError(s, start, position, message);
                            int length = position - nameStart;
                            if (length != 0)
                            {
                                position++;
                                int named = UnicodeNames.Lookup(s.Substring(nameStart, length));
                                if (named < 0)
                                    throw UnicodeEscapeError(s, start, position, "unknown Unicode character name");
                                AppendCodePoint(result, named);
                                continue;
                            }
                        }
                        throw UnicodeEscapeError(s, start, position, message);

                    default:
                        if (firstInvalidEscape == -1)
                            firstInvalidEscape = position - 1;
                        result.Append('\\').Append(c);
                        continue;
                }
            }
            return result.ToString();
        }

        private static void AppendCodePoint(StringBuilder result, int ch)
        {
            if (ch >= 0x10000)
                result.Append(char.ConvertFromUtf32(ch));
            else
                result.Append((char)ch);
        }

        private static int HexDigit(char c) =>
            c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        // _PyBytes_DecodeEscape2 and decode_bytes_with_escapes
        private byte[] DecodeBytesWithEscapes(string s, GeneratedTokenInfo t)
        {
            var result = new List<byte>(s.Length);
            int firstInvalidEscape = -1;
            int position = 0;
            int end = s.Length;
            while (position < end)
            {
                if (s[position] != '\\')
                {
                    result.Add((byte)s[position++]);
                    continue;
                }
                position++;
                if (position == end)
                    throw RaiseDecodeError("(value error) Trailing \\ in string");
                char c = s[position++];
                switch (c)
                {
                    case '\n': break;
                    case '\\': result.Add((byte)'\\'); break;
                    case '\'': result.Add((byte)'\''); break;
                    case '"': result.Add((byte)'"'); break;
                    case 'b': result.Add(8); break;
                    case 'f': result.Add(12); break;
                    case 't': result.Add(9); break;
                    case 'n': result.Add(10); break;
                    case 'r': result.Add(13); break;
                    case 'v': result.Add(11); break;
                    case 'a': result.Add(7); break;
                    case >= '0' and <= '7':
                        int value = c - '0';
                        if (position < end && s[position] >= '0' && s[position] <= '7')
                        {
                            value = (value << 3) + s[position++] - '0';
                            if (position < end && s[position] >= '0' && s[position] <= '7')
                                value = (value << 3) + s[position++] - '0';
                        }
                        if (value > 255 && firstInvalidEscape == -1)
                            firstInvalidEscape = position - 3;
                        result.Add((byte)value);
                        break;
                    case 'x':
                        if (position + 1 < end)
                        {
                            int digit1 = HexDigit(s[position]);
                            int digit2 = HexDigit(s[position + 1]);
                            if (digit1 >= 0 && digit2 >= 0)
                            {
                                result.Add((byte)(digit1 << 4 | digit2));
                                position += 2;
                                break;
                            }
                        }
                        if (position < end && HexDigit(s[position]) >= 0)
                            position++;
                        throw RaiseDecodeError($"(value error) invalid \\x escape at position {position - 2}");
                    default:
                        if (firstInvalidEscape == -1)
                            firstInvalidEscape = position - 1;
                        result.Add((byte)'\\');
                        position--;
                        break;
                }
            }
            if (firstInvalidEscape >= 0)
                WarnInvalidEscapeSequence(s, firstInvalidEscape, t);
            return result.ToArray();
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
                new PyBytes(bytes.ToArray()).GetAttribute("decode").Call(new PyObject[] { new PyString("utf-8") }, null);
                return "";
            }
            catch (PythonException error)
            {
                return error.PyException.AsString().Value;
            }
        }

        // _PyPegen_decode_string
        private string DecodeString(bool raw, string s, GeneratedTokenInfo t)
        {
            CheckUtf8(s);
            return raw ? s : DecodeUnicodeWithEscapes(s, t);
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
            return PyParserHelpers.CheckedFutureImport(module, names, level, lineno, colOffset, endLineno, endColOffset);
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
                res = PyParserHelpers.ConcatenateStrings(spec, lineno, colOffset, endLineno, endColOffset);
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
                        "f-string: invalid conversion character %R: expected 's', 'r', or 'a'", new PyString(id).ToRepr().Value);
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
            return PyParserHelpers.ConcatenateStrings(strings, lineno, colOffset, endLineno, endColOffset);
        }
    }
}
