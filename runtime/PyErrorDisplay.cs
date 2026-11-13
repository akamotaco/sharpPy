// The exception writer of CPython v3.12.15: Python/pythonrun.c (_PyErr_Display and the print_exception
// family) and Python/traceback.c (_PyTraceBack_Print_Indented, tb_printinternal, tb_displayline,
// display_source_line_with_margin, the anchors and carets). sys.excepthook, sys.__excepthook__ and
// PyErr_PrintEx all write through here; traceback.py is the library's own formatter.
using System;
using System.Collections.Generic;
using System.Text;
using SharpPy.Generated;

namespace SharpPy
{
    internal static class PyErrorDisplay
    {
        private const string TracebackHeader = "Traceback (most recent call last):\n";
        private const string GroupTracebackHeader = "Exception Group Traceback (most recent call last):\n";
        private const string CauseMessage = "The above exception was the direct cause of the following exception:\n";
        private const string ContextMessage = "During handling of the above exception, another exception occurred:\n";
        private const int MaxGroupWidth = 15;   // PyErr_MAX_GROUP_WIDTH
        private const int MaxGroupDepth = 10;   // PyErr_MAX_GROUP_DEPTH
        private const int SourceLineIndent = 4; // _TRACEBACK_SOURCE_LINE_INDENT
        private const int RecursiveCutoff = 3;  // TB_RECURSIVE_CUTOFF
        private const long TracebackLimit = 1000; // PyTraceBack_LIMIT

        // struct exception_print_context
        private sealed class Context
        {
            public PyObject File;
            public HashSet<PyObject> Seen;
            public int GroupDepth;
            public bool NeedClose;
            public string Margin => GroupDepth != 0 ? "| " : "";
            public int Indent => 2 * GroupDepth;
        }

        /// <summary>PyErr_Display: to sys.stderr, nothing when it is None.</summary>
        internal static void Display(PyObject value, PyObject tb)
        {
            PyObject file = null;
            try
            {
                var sys = PyImportSystem.Import("sys");
                if (sys.ModuleDict.TryGetValue("stderr", out var found))
                    file = found;
            }
            catch (PythonException)
            {
            }
            if (file == null)
            {
                Console.Error.WriteLine(Dump(value));
                Console.Error.WriteLine("lost sys.stderr");
                return;
            }
            if (file is PyNone)
                return;
            Display(file, value, tb);
        }

        /// <summary>_PyErr_Display into a str, for a host that logs the exception rather than printing it.</summary>
        internal static string Format(PyBaseException exception)
        {
            try
            {
                var buffer = PyImportSystem.Import("io").GetAttribute("StringIO").Call(Array.Empty<PyObject>(), null);
                Display(buffer, exception, null);
                return ((PyString)buffer.GetAttribute("getvalue").Call(Array.Empty<PyObject>(), null)).Value;
            }
            catch (Exception)
            {
                return $"{exception.GetTypeName()}: {exception.Message}";
            }
        }

        /// <summary>_PyErr_Display</summary>
        internal static void Display(PyObject file, PyObject value, PyObject tb)
        {
            // Put the traceback on the exception, otherwise it won't get displayed (issue #18776).
            if (value is PyBaseException exception && tb is PyTraceback traceback && exception.__traceback__ == null)
                exception.__traceback__ = traceback;
            var ctx = new Context { File = file, Seen = new HashSet<PyObject>(ReferenceEqualityComparer.Instance) };
            try
            {
                PrintExceptionRecursive(ctx, value);
            }
            catch (PythonException)
            {
                Console.Error.WriteLine(Dump(value));
                Console.Error.WriteLine("lost sys.stderr");
            }
            // Call file.flush(), silently ignoring its error.
            try
            {
                file.GetAttribute("flush").Call(Array.Empty<PyObject>(), null);
            }
            catch (PythonException)
            {
            }
        }

        // _PyObject_Dump, as far as a writer that lost its file can say
        private static string Dump(PyObject value)
        {
            try
            {
                return $"object type name: {value.GetPyType().Name}";
            }
            catch (Exception)
            {
                return "<object at unknown address>";
            }
        }

        // PyFile_WriteString / PyFile_WriteObject(..., Py_PRINT_RAW): file.write(str(v))
        private static void Write(PyObject file, string text) =>
            file.GetAttribute("write").Call(new PyObject[] { PyString.From(text) }, null);

        private static void WriteObject(PyObject file, PyObject value) =>
            file.GetAttribute("write").Call(new PyObject[] { value.AsString() }, null);

        // _Py_WriteIndent and _Py_WriteIndentedMargin
        private static void WriteIndent(int indent, PyObject file)
        {
            if (indent > 0)
                Write(file, new string(' ', indent));
        }

        private static void WriteIndentedMargin(int indent, string margin, PyObject file)
        {
            WriteIndent(indent, file);
            if (!string.IsNullOrEmpty(margin))
                Write(file, margin);
        }

        private static void WriteIndentedMargin(Context ctx) => WriteIndentedMargin(ctx.Indent, ctx.Margin, ctx.File);

        // print_exception_recursive
        private static void PrintExceptionRecursive(Context ctx, PyObject value)
        {
            var recursion = PyCRecursion.EnterState(" in print_exception_recursive");
            try
            {
                if (ctx.Seen != null)
                    PrintExceptionCauseAndContext(ctx, value);
                if (value is PyBaseExceptionGroup group)
                    PrintExceptionGroup(ctx, group);
                else
                    PrintException(ctx, value);
            }
            finally
            {
                recursion.Depth--;
            }
        }

        // print_chained
        private static void PrintChained(Context ctx, PyObject value, string message)
        {
            var recursion = PyCRecursion.EnterState(" in print_chained");
            bool needClose = ctx.NeedClose;
            try
            {
                PrintExceptionRecursive(ctx, value);
            }
            finally
            {
                ctx.NeedClose = needClose;
                recursion.Depth--;
            }
            WriteIndentedMargin(ctx);
            Write(ctx.File, "\n");
            WriteIndentedMargin(ctx);
            Write(ctx.File, message);
            WriteIndentedMargin(ctx);
            Write(ctx.File, "\n");
        }

        // print_exception_cause_and_context
        private static void PrintExceptionCauseAndContext(Context ctx, PyObject value)
        {
            ctx.Seen.Add(value);
            if (value is not PyBaseException exception)
                return;
            var cause = exception.__cause__;
            if (cause != null)
            {
                if (!ctx.Seen.Contains(cause))
                    PrintChained(ctx, cause, CauseMessage);
                return;
            }
            if (exception.__suppress_context__)
                return;
            var context = exception.__context__;
            if (context != null && !ctx.Seen.Contains(context))
                PrintChained(ctx, context, ContextMessage);
        }

        // print_exception_group
        private static void PrintExceptionGroup(Context ctx, PyBaseExceptionGroup value)
        {
            var file = ctx.File;
            if (ctx.GroupDepth > MaxGroupDepth)
            {
                WriteIndentedMargin(ctx);
                Write(file, $"... (max_group_depth is {MaxGroupDepth})\n");
                return;
            }
            if (ctx.GroupDepth == 0)
                ctx.GroupDepth += 1;
            PrintException(ctx, value);

            var excs = value.Excs.Items;
            int count = excs.Length;
            int n = count <= MaxGroupWidth ? count : MaxGroupWidth + 1;
            ctx.NeedClose = false;
            for (int i = 0; i < n; i++)
            {
                bool last = i == n - 1;
                if (last)
                    ctx.NeedClose = true;
                WriteIndent(ctx.Indent, file);
                bool truncated = i >= MaxGroupWidth;
                Write(file, truncated
                    ? $"{(i == 0 ? "+-" : "  ")}+---------------- ... ----------------\n"
                    : $"{(i == 0 ? "+-" : "  ")}+---------------- {i + 1} ----------------\n");
                ctx.GroupDepth += 1;
                if (!truncated)
                {
                    var recursion = PyCRecursion.EnterState(" in print_exception_group");
                    try
                    {
                        PrintExceptionRecursive(ctx, excs[i]);
                    }
                    finally
                    {
                        recursion.Depth--;
                    }
                }
                else
                {
                    int remaining = count - MaxGroupWidth;
                    WriteIndentedMargin(ctx);
                    Write(file, $"and {remaining} more exception{(remaining > 1 ? "s" : "")}\n");
                }
                if (last && ctx.NeedClose)
                {
                    WriteIndent(ctx.Indent, file);
                    Write(file, "+------------------------------------\n");
                    ctx.NeedClose = false;
                }
                ctx.GroupDepth -= 1;
            }
            if (ctx.GroupDepth == 1)
                ctx.GroupDepth -= 1;
        }

        // print_exception
        private static void PrintException(Context ctx, PyObject value)
        {
            var file = ctx.File;
            if (value is not PyBaseException exception)
            {
                // print_exception_invalid_type
                WriteIndent(ctx.Indent, file);
                Write(file, "TypeError: print_exception(): Exception expected for value, ");
                Write(file, value.GetPyType().FullName);
                Write(file, " found\n");
                return;
            }
            Console.Out.Flush();
            PrintExceptionTraceback(ctx, exception);
            // grab the type and notes now because value can change below
            var type = value.GetPyType();
            var notes = GetExceptionNotes(value);
            PrintExceptionFileAndLine(ctx, ref value);
            PrintExceptionMessage(ctx, type, value);
            PrintExceptionSuggestions(ctx, value);
            Write(file, "\n");
            PrintExceptionNotes(ctx, notes);
        }

        // print_exception_traceback
        private static void PrintExceptionTraceback(Context ctx, PyBaseException value)
        {
            var tb = value.__traceback__;
            if (tb == null)
                return;
            string header = TracebackHeader;
            string headerMargin = ctx.Margin;
            if (value is PyBaseExceptionGroup)
            {
                header = GroupTracebackHeader;
                if (ctx.GroupDepth == 1)
                    headerMargin = "+ ";
            }
            PrintTracebackIndented(tb, ctx.Indent, ctx.Margin, headerMargin, header, ctx.File);
        }

        // get_exception_notes: what __notes__ answers, or a note about the error reading it.
        private static PyObject GetExceptionNotes(PyObject value)
        {
            try
            {
                return value.LookupAttribute("__notes__");
            }
            catch (PythonException error)
            {
                return new PyList(new PyObject[] { PyString.From($"Ignored error getting __notes__: {error.PyException.ToRepr().Value}") });
            }
        }

        // print_exception_file_and_line: a SyntaxError (anything with print_file_and_line) shows its
        // location and its text; the message then is its msg.
        private static void PrintExceptionFileAndLine(Context ctx, ref PyObject value)
        {
            try
            {
                if (value.LookupAttribute("print_file_and_line") == null)
                    return;
            }
            catch (PythonException)
            {
                return;
            }
            PyObject message, filename, text;
            long lineno, offset, endLineno, endOffset;
            try
            {
                if (!ParseSyntaxError(value, out message, out filename, out lineno, out offset, out endLineno, out endOffset, out text))
                    return;
            }
            catch (PythonException)
            {
                return;
            }
            value = message;
            var file = ctx.File;
            WriteIndentedMargin(ctx);
            Write(file, $"  File \"{filename.AsString().Value}\", line {lineno}\n");
            if (text != null)
            {
                byte[] errorLine = text is PyString textString ? Encoding.UTF8.GetBytes(textString.Value) : null;
                long lineSize = errorLine?.Length ?? 0;
                // A location over several lines highlights to the end of the first one.
                if (endLineno > lineno)
                    endOffset = errorLine != null ? lineSize : -1;
                // No more '^' than the source line has room for.
                if (errorLine != null && endOffset > lineSize + 1)
                    endOffset = lineSize + 1;
                PrintErrorText(file, offset, endOffset, text);
            }
        }

        // parse_syntax_error
        private static bool ParseSyntaxError(PyObject err, out PyObject message, out PyObject filename, out long lineno,
            out long offset, out long endLineno, out long endOffset, out PyObject text)
        {
            message = err.GetAttribute("msg");
            var v = err.GetAttribute("filename");
            filename = v is PyNone ? PyString.From("<string>") : v;
            lineno = AsSsize(err.GetAttribute("lineno"));
            v = err.GetAttribute("offset");
            offset = v is PyNone ? -1 : AsSsize(v);
            endLineno = lineno;
            endOffset = -1;
            try
            {
                v = err.GetAttribute("end_lineno");
                endLineno = v is PyNone ? lineno : AsSsize(v);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
            }
            try
            {
                v = err.GetAttribute("end_offset");
                endOffset = v is PyNone ? -1 : AsSsize(v);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
            }
            v = err.GetAttribute("text");
            text = v is PyNone ? null : v;
            return true;
        }

        // PyLong_AsSsize_t
        private static long AsSsize(PyObject value) => PyArgs.LongAsSsize(value);

        // print_error_text: the text as the C string it is, offsets counted in its bytes.
        private static void PrintErrorText(PyObject file, long offset, long endOffset, PyObject textObject)
        {
            long caretRepetitions = endOffset > 0 && endOffset > offset ? endOffset - offset : 1;
            if (textObject is not PyString textString)
                throw PyTypeError.Create($"bad argument type for built-in operation");
            byte[] bytes = Encoding.UTF8.GetBytes(textString.Value);
            int nul = Array.IndexOf(bytes, (byte)0);
            int end = nul < 0 ? bytes.Length : nul;
            int start = 0;
            // Convert offset from 1-based to 0-based
            offset--;
            // Strip leading whitespace from text, adjusting offset as we go
            while (start < end && (bytes[start] == ' ' || bytes[start] == '\t' || bytes[start] == '\f'))
            {
                start++;
                offset--;
            }
            // Calculate text length excluding trailing newline
            long len = end - start;
            if (len > 0 && bytes[start + len - 1] == '\n')
                len--;
            // Clip offset to at most len
            if (offset > len)
                offset = len;
            // Skip past newlines embedded in text
            while (true)
            {
                int nl = Array.IndexOf(bytes, (byte)'\n', start, end - start);
                if (nl < 0)
                    break;
                long inl = nl - start;
                if (inl >= offset)
                    break;
                inl += 1;
                start += (int)inl;
                len -= inl;
                offset -= inl;
            }
            Write(file, "    ");
            Write(file, Encoding.UTF8.GetString(bytes, start, end - start));
            // Make sure there's a newline at the end
            if (start + len >= end || bytes[start + len] != '\n')
                Write(file, "\n");
            // Don't print caret if it points to the left of the text
            if (offset < 0)
                return;
            var caret = new StringBuilder("    ");
            caret.Append(' ', (int)offset);
            caret.Append('^', (int)Math.Min(caretRepetitions, int.MaxValue / 2));
            caret.Append('\n');
            Write(file, caret.ToString());
        }

        // print_exception_message: module.qualname[: str(exc)]
        private static void PrintExceptionMessage(Context ctx, PyType type, PyObject value)
        {
            var file = ctx.File;
            WriteIndentedMargin(ctx);
            PyObject moduleName = null;
            try
            {
                moduleName = type.GetAttribute("__module__");
            }
            catch (PythonException)
            {
            }
            if (moduleName is not PyString module)
                Write(file, "<unknown>.");
            else if (module.Value != "builtins" && module.Value != "__main__")
            {
                WriteObject(file, module);
                Write(file, ".");
            }
            // PyType_GetQualName
            Write(file, type.IsHeapType ? type.QualNameText : type.Name);
            if (value is PyNone)
                return;
            PyString text;
            try
            {
                text = value.AsString();
            }
            catch (PythonException)
            {
                Write(file, ": <exception str() failed>");
                return;
            }
            // only print colon if the str() of the object is not the empty string
            if (text.Value.Length != 0)
                Write(file, ": ");
            WriteObject(file, text);
        }

        // print_exception_suggestions
        private static void PrintExceptionSuggestions(Context ctx, PyObject value)
        {
            string suggestion;
            try
            {
                suggestion = ErrorSuggestions.Offer(value);
            }
            catch (PythonException)
            {
                return;
            }
            if (suggestion != null)
                Write(ctx.File, suggestion);
        }

        // print_exception_notes
        private static void PrintExceptionNotes(Context ctx, PyObject notes)
        {
            var file = ctx.File;
            if (notes == null)
                return;
            if (!PyVM.IsSequence(notes) || notes is PyString || notes is PyBytes)
            {
                WriteIndentedMargin(ctx);
                PyString text;
                try
                {
                    text = notes.ToRepr();
                }
                catch (PythonException)
                {
                    Write(file, "<__notes__ repr() failed>");
                    Write(file, "\n");
                    return;
                }
                WriteObject(file, text);
                Write(file, "\n");
                return;
            }
            long count = PyAbstract.SequenceSize(notes);
            for (long i = 0; i < count; i++)
            {
                var note = PyAbstract.SequenceGetItem(notes, i);
                PyString noteText;
                try
                {
                    noteText = note.AsString();
                }
                catch (PythonException)
                {
                    Write(file, "<note str() failed>");
                    Write(file, "\n");
                    continue;
                }
                // PyUnicode_Splitlines(note_str, 1)
                var lines = (PyList)noteText.GetAttribute("splitlines").Call(new PyObject[] { PyBool.True }, null);
                foreach (var line in lines.Items)
                {
                    WriteIndentedMargin(ctx);
                    WriteObject(file, line);
                }
                Write(file, "\n");
            }
        }

        // _PyTraceBack_Print_Indented
        private static void PrintTracebackIndented(PyTraceback tb, int indent, string margin, string headerMargin, string header, PyObject file)
        {
            long limit = TracebackLimit;
            var sys = PyImportSystem.Import("sys");
            if (sys.ModuleDict.TryGetValue("tracebacklimit", out var limitObject) && limitObject is PyInt limitValue)
            {
                if (limitValue.Value > long.MaxValue)
                    limit = long.MaxValue;
                else if (limitValue.Value <= 0)
                    return;
                else
                    limit = (long)limitValue.Value;
            }
            WriteIndentedMargin(indent, headerMargin, file);
            Write(file, header);
            PrintTracebackInternal(tb, file, limit, indent, margin);
        }

        // tb_print_line_repeated
        private static void PrintLineRepeated(PyObject file, long count)
        {
            count -= RecursiveCutoff;
            Write(file, count > 1
                ? $"  [Previous line repeated {count} more times]\n"
                : $"  [Previous line repeated {count} more time]\n");
        }

        // tb_printinternal
        private static void PrintTracebackInternal(PyTraceback tb, PyObject file, long limit, int indent, string margin)
        {
            long depth = 0;
            for (var walk = tb; walk != null; walk = walk.Next)
                depth++;
            while (tb != null && depth > limit)
            {
                depth--;
                tb = tb.Next;
            }
            string lastFile = null, lastName = null;
            int lastLine = -1;
            long count = 0;
            while (tb != null)
            {
                var code = tb.Frame.Code;
                int lineno = tb.LineNo;
                if (lineno == -1)
                    lineno = LineOf(code, tb.LastI);
                if (lastFile == null || code.FileName != lastFile || lastLine == -1 || lineno != lastLine ||
                    lastName == null || code.Name != lastName)
                {
                    if (count > RecursiveCutoff)
                        PrintLineRepeated(file, count);
                    lastFile = code.FileName;
                    lastLine = lineno;
                    lastName = code.Name;
                    count = 0;
                }
                count++;
                if (count <= RecursiveCutoff)
                    DisplayLine(tb, file, code.FileName, lineno, code, code.Name, indent, margin);
                tb = tb.Next;
            }
            if (count > RecursiveCutoff)
                PrintLineRepeated(file, count);
        }

        // PyCode_Addr2Line through the location table
        private static int LineOf(PyCodeObject code, int lasti) => Location(code, lasti).Line;

        // PyCode_Addr2Location: the entry of the code unit, the first line for a negative offset.
        private static SourcePosition Location(PyCodeObject code, int lasti)
        {
            int first = code.GetFirstLineNo();
            if (lasti < 0)
                return new SourcePosition(first, first, 0, 0);
            int index = lasti / 2, i = 0;
            foreach (var position in LocationTable.Read(code.Tables.LineTable, first))
            {
                if (i++ == index)
                    return position;
            }
            return new SourcePosition(-1, -1, -1, -1);
        }

        // tb_displayline
        private static void DisplayLine(PyTraceback tb, PyObject file, string filename, int lineno, PyCodeObject code,
            string name, int marginIndent, string margin)
        {
            if (filename == null || name == null)
                throw PySystemError.Create("tb_displayline: no filename or name");
            WriteIndentedMargin(marginIndent, margin, file);
            Write(file, $"  File \"{filename}\", line {lineno}, in {name}\n");

            int truncation = SourceLineIndent;
            string sourceLine;
            try
            {
                if (!DisplaySourceLineWithMargin(file, filename, lineno, SourceLineIndent, marginIndent, margin,
                        ref truncation, out sourceLine) || sourceLine == null)
                    return;
            }
            catch (PythonException error) when (!error.PyException.GetPyType().IsSubclassOf(PyType.KeyboardInterruptType))
            {
                // ignore errors since we can't report them, can we?
                return;
            }

            var position = Location(code, tb.LastI);
            if (position.Line < 0 || position.EndLine < 0 || position.Column < 0 || position.EndColumn < 0)
                return;
            int sourceLineLength = sourceLine.Length;
            long startOffset = ByteOffsetToCharacterOffset(sourceLine, position.Column);
            long endOffset = ByteOffsetToCharacterOffset(sourceLine, position.EndColumn);
            long leftEndOffset = -1, rightStartOffset = -1;
            string primary = "^", secondary = "^";
            if (position.Line == position.EndLine)
            {
                try
                {
                    ExtractAnchorsFromLine(filename, sourceLine, startOffset, endOffset, ref leftEndOffset,
                        ref rightStartOffset, ref primary, ref secondary);
                }
                catch (PythonException error) when (!error.PyException.GetPyType().IsSubclassOf(PyType.KeyboardInterruptType))
                {
                    leftEndOffset = rightStartOffset = -1;
                    primary = secondary = "^";
                }
            }
            else
            {
                // A multi-line expression is highlighted until the last non-whitespace character.
                byte[] bytes = Encoding.UTF8.GetBytes(sourceLine);
                long i = sourceLineLength;
                while (--i >= 0)
                {
                    if (!IsWhitespace(bytes[i]))
                        break;
                }
                endOffset = i + 1;
            }
            // Elide indicators if primary char spans the frame line
            long strippedLineLength = sourceLineLength - truncation - SourceLineIndent;
            bool hasSecondaryRanges = leftEndOffset != -1 || rightStartOffset != -1;
            if (endOffset - startOffset == strippedLineLength && !hasSecondaryRanges)
                return;
            WriteIndentedMargin(marginIndent, margin, file);
            long displayStart = DisplayWidth(sourceLine, startOffset);
            long displayEnd = DisplayWidth(sourceLine, endOffset);
            long displayLeftEnd = -1, displayRightStart = -1;
            if (hasSecondaryRanges)
            {
                displayLeftEnd = DisplayWidth(sourceLine, leftEndOffset);
                displayRightStart = DisplayWidth(sourceLine, rightStartOffset);
            }
            PrintErrorLocationCarets(file, truncation, displayStart, displayEnd, displayRightStart, displayLeftEnd, primary, secondary);
        }

        private static bool IsWhitespace(byte c) => c == ' ' || c == '\t' || c == '\f';

        // print_error_location_carets
        private static void PrintErrorLocationCarets(PyObject file, long offset, long startOffset, long endOffset,
            long rightStartOffset, long leftEndOffset, string primary, string secondary)
        {
            bool specialChars = leftEndOffset != -1 || rightStartOffset != -1;
            var line = new StringBuilder();
            while (++offset <= endOffset)
            {
                if (offset <= startOffset)
                    line.Append(' ');
                else if (specialChars && leftEndOffset < offset && offset <= rightStartOffset)
                    line.Append(secondary);
                else
                    line.Append(primary);
            }
            line.Append('\n');
            Write(file, line.ToString());
        }

        // _PyPegen_byte_offset_to_character_offset: the characters a UTF-8 prefix of that many bytes decodes to.
        internal static long ByteOffsetToCharacterOffset(string line, long colOffset)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            long length = bytes.Length;
            if (colOffset > length + 1)
                colOffset = length + 1;
            // past the end, the C string's terminating NUL is read as one more character
            if (colOffset > length)
                return Encoding.UTF8.GetString(bytes).Length + 1;
            // PyUnicode_DecodeUTF8(str, col_offset, "replace"), counted in code points
            string text = Encoding.UTF8.GetString(bytes, 0, (int)colOffset);
            return CodePoints(text);
        }

        private static long CodePoints(string text)
        {
            long count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    i++;
                count++;
            }
            return count;
        }

        // The UTF-16 index of a code point offset into the line.
        private static int CharIndex(string line, long codePoints)
        {
            int index = 0;
            for (long seen = 0; seen < codePoints && index < line.Length; seen++)
                index += char.IsHighSurrogate(line[index]) && index + 1 < line.Length && char.IsLowSurrogate(line[index + 1]) ? 2 : 1;
            return index;
        }

        // _PyPegen_calculate_display_width: two columns for what unicodedata calls wide or full.
        private static long DisplayWidth(string line, long characterOffset)
        {
            string segment = line.Substring(0, Math.Min(line.Length, CharIndex(line, characterOffset)));
            bool ascii = true;
            foreach (char c in segment)
            {
                if (c > 0x7f)
                {
                    ascii = false;
                    break;
                }
            }
            if (ascii)
                return characterOffset;
            var widthFunction = PyImportSystem.Import("unicodedata").GetAttribute("east_asian_width");
            long width = 0;
            for (int i = 0; i < segment.Length; i++)
            {
                int length = char.IsHighSurrogate(segment[i]) && i + 1 < segment.Length ? 2 : 1;
                var specifier = widthFunction.Call(new PyObject[] { PyString.From(segment.Substring(i, length)) }, null);
                width += specifier is PyString { Value: "W" or "F" } ? 2 : 1;
                i += length - 1;
            }
            return width;
        }

        // extract_anchors_from_line: the segment parsed alone; a BinOp or a Subscript statement names
        // the part of the range the error came from.
        private static void ExtractAnchorsFromLine(string filename, string line, long startOffset, long endOffset,
            ref long leftAnchor, ref long rightAnchor, ref string primary, ref string secondary)
        {
            int from = CharIndex(line, startOffset), to = CharIndex(line, endOffset);
            if (from > line.Length || to < from)
                return;
            string segment = line.Substring(from, Math.Min(line.Length, to) - from);
            var module = Compiler.Parse(segment, filename, CompileMode.File) as GeneratedModule;
            if (module == null)
                return;
            new AstOptimizer(0, false).Optimize(module);
            if (module.Body.Count != 1 || module.Body[0] is not GeneratedExprStmt statement)
                return;
            byte[] segmentBytes = Encoding.UTF8.GetBytes(segment);
            long left = -1, right = -1;
            string first = primary, second = secondary;
            if (!ExtractAnchorsFromExpr(segmentBytes, statement.Value, ref left, ref right, ref first, ref second))
                return;
            // Normalize the AST offsets to character offsets of the whole line.
            leftAnchor = ByteOffsetToCharacterOffset(segment, left) + startOffset;
            rightAnchor = ByteOffsetToCharacterOffset(segment, right) + startOffset;
            primary = first;
            secondary = second;
        }

        // extract_anchors_from_expr
        private static bool ExtractAnchorsFromExpr(byte[] segment, GeneratedExpr expr, ref long leftAnchor, ref long rightAnchor,
            ref string primary, ref string secondary)
        {
            byte At(long i) => i >= 0 && i < segment.Length ? segment[i] : (byte)0;
            switch (expr)
            {
                case GeneratedBinOp binOp:
                {
                    var leftExpr = binOp.Left;
                    var rightExpr = binOp.Right;
                    for (long i = leftExpr.EndColOffset; i < rightExpr.ColOffset; i++)
                    {
                        if (IsWhitespace(At(i)))
                            continue;
                        leftAnchor = i;
                        rightAnchor = i + 1;
                        // a two-character operator (e.g //)
                        if (i + 1 < rightExpr.ColOffset && !IsWhitespace(At(i + 1)))
                            ++rightAnchor;
                        // Keep going if the current char is not ')'
                        if (i + 1 < rightExpr.ColOffset && At(i) == ')')
                            continue;
                        primary = "~";
                        secondary = "^";
                        break;
                    }
                    return true;
                }
                case GeneratedSubscript subscript:
                {
                    leftAnchor = subscript.Value.EndColOffset;
                    rightAnchor = subscript.Slice.EndColOffset + 1;
                    long length = segment.Length;
                    while (leftAnchor < length && (IsWhitespace(At(leftAnchor)) || At(leftAnchor) != '['))
                        ++leftAnchor;
                    while (rightAnchor < length && (IsWhitespace(At(rightAnchor)) || At(rightAnchor) != ']'))
                        ++rightAnchor;
                    if (rightAnchor < length)
                        rightAnchor += 1;
                    primary = "~";
                    secondary = "^";
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>_Py_DisplaySourceLine: the line, indented, with no margin.</summary>
        internal static void DisplaySourceLine(PyObject file, string filename, int lineno, int indent)
        {
            int truncation = 0;
            DisplaySourceLineWithMargin(file, filename, lineno, indent, 0, null, ref truncation, out _);
        }

        // display_source_line_with_margin: the line read from the file the code names, its
        // indentation removed and counted in truncation. False when there is no line.
        private static bool DisplaySourceLineWithMargin(PyObject file, string filename, int lineno, int indent,
            int marginIndent, string margin, ref int truncation, out string line)
        {
            line = null;
            if (filename == null)
                return true;
            // Do not attempt to open things like <string> or <stdin>
            if (filename.Length > 0 && filename[0] == '<' && filename[filename.Length - 1] == '>')
                return true;
            var io = PyImportSystem.Import("io");
            PyObject binary;
            try
            {
                binary = io.GetAttribute("open").Call(new PyObject[] { PyString.From(filename), PyString.From("rb") }, null);
            }
            catch (PythonException)
            {
                binary = FindSourceFile(filename, io);
                if (binary == null)
                    return false;
            }
            byte[] content;
            try
            {
                content = ((PyBytes)binary.GetAttribute("read").Call(Array.Empty<PyObject>(), null)).Value;
            }
            finally
            {
                try
                {
                    binary.GetAttribute("close").Call(Array.Empty<PyObject>(), null);
                }
                catch (PythonException)
                {
                }
            }
            // _PyTokenizer_FindEncodingFilename and TextIOWrapper(binary, encoding): the text by its
            // BOM and coding cookie, newlines universal, read line by line up to lineno.
            string text;
            try
            {
                text = Tokenizer.DecodeFile(content, filename);
            }
            catch (PythonException)
            {
                return false;
            }
            string found = NthLine(text, lineno);
            if (found == null)
                return false;
            line = found;
            // remove the indentation of the line
            int i = 0;
            while (i < found.Length && (found[i] == ' ' || found[i] == '\t' || found[i] == '\f'))
                i++;
            string stripped = found.Substring(i);
            truncation = (int)CodePoints(found.Substring(0, i)) - indent;
            WriteIndentedMargin(marginIndent, margin, file);
            WriteIndent(indent, file);
            Write(file, stripped);
            Write(file, "\n");
            return true;
        }

        // PyFile_GetLine(fob, -1) lineno times: the line without its '\n', null past the end.
        private static string NthLine(string text, int lineno)
        {
            int position = 0;
            string current = null;
            for (int n = 0; n < lineno; n++)
            {
                if (position >= text.Length)
                    return null;
                int end = position;
                while (end < text.Length && text[end] != '\n' && text[end] != '\r')
                    end++;
                current = text.Substring(position, end - position);
                if (end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n')
                    end++;
                position = end + 1;
            }
            return current;
        }

        // _Py_FindSourceFile: the tail of the name under each entry of sys.path.
        private static PyObject FindSourceFile(string filename, PyObject io)
        {
            int separator = filename.LastIndexOf(System.IO.Path.DirectorySeparatorChar);
            string tail = separator < 0 ? filename : filename.Substring(separator + 1);
            PyObject path;
            try
            {
                if (!PyImportSystem.Import("sys").ModuleDict.TryGetValue("path", out path) || path is not PyList)
                    return null;
            }
            catch (PythonException)
            {
                return null;
            }
            var open = io.GetAttribute("open");
            foreach (var entry in ((PyList)path).Items)
            {
                if (entry is not PyString directory || directory.Value.Contains('\0'))
                    continue;
                string candidate = directory.Value;
                if (candidate.Length > 0 && candidate[candidate.Length - 1] != System.IO.Path.DirectorySeparatorChar)
                    candidate += System.IO.Path.DirectorySeparatorChar;
                candidate += tail;
                try
                {
                    return open.Call(new PyObject[] { PyString.From(candidate), PyString.From("rb") }, null);
                }
                catch (PythonException)
                {
                }
            }
            return null;
        }
    }
}
