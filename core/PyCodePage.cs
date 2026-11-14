using System;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// The Windows code page codec of Objects/unicodeobject.c (decode_code_page_stateful,
    /// encode_code_page) and the code pages of the system (GetACP, GetOEMCP). CPython calls
    /// MultiByteToWideChar and WideCharToMultiByte; here .NET's code page encodings do that work,
    /// an exception fallback standing for MB_ERR_INVALID_CHARS and WC_NO_BEST_FIT_CHARS. The strict
    /// pass converts the whole input; on a failure the input goes character by character through
    /// the error handler, as there.
    /// </summary>
    internal static class PyCodePage
    {
        internal const int CP_ACP = 0, CP_OEMCP = 1, CP_UTF7 = 65000, CP_UTF8 = 65001;

        private const string DecodeReason = "No mapping for the Unicode character exists in the target code page.";
        private const string EncodeReason = "invalid character";

        /// <summary>GetACP: the system's ANSI code page (what the Nls registry key holds).</summary>
        internal static int GetACP() => SystemCodePage("ACP", info => info.ANSICodePage);

        /// <summary>GetOEMCP: the system's OEM code page.</summary>
        internal static int GetOEMCP() => SystemCodePage("OEMCP", info => info.OEMCodePage);

        private static int SystemCodePage(string name, Func<System.Globalization.TextInfo, int> culture)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    if (Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Nls\CodePage", name, null)
                        is string text && int.TryParse(text, out int page))
                        return page;
                }
                catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
                {
                }
            }
            return culture(System.Globalization.CultureInfo.InstalledUICulture.TextInfo);
        }

        private static int Resolve(int codePage) => codePage switch
        {
            CP_ACP => GetACP(),
            CP_OEMCP => GetOEMCP(),
            _ => codePage,
        };

        // code_page_name
        private static string CodePageName(int codePage) => codePage switch
        {
            CP_ACP => "mbcs",
            CP_UTF7 => "CP_UTF7",
            CP_UTF8 => "CP_UTF8",
            _ => "cp" + (uint)codePage,
        };

        // The converter of a code page, failing where the API would (ERROR_NO_UNICODE_TRANSLATION,
        // or a default character used).
        private static Encoding Strict(int codePage)
        {
            int page = Resolve(codePage);
            // CP_UTF7 takes flags 0 (no MB_ERR_INVALID_CHARS): lenient. .NET 5 and later keep UTF-7
            // out of Encoding.GetEncoding, but the class is there.
#pragma warning disable SYSLIB0001
            Encoding encoding = page == CP_UTF8 ? new UTF8Encoding(false, true)
                : page == CP_UTF7 ? new UTF7Encoding(true)
                : CodePagesEncodingProvider.Instance.GetEncoding(page, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
#pragma warning restore SYSLIB0001
            if (encoding == null)
            {
                try
                {
                    encoding = Encoding.GetEncoding(page, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException)
                {
                    // PyErr_SetFromWindowsErr(ERROR_INVALID_PARAMETER)
                    throw PyOSError.CreateWindowsFileError(87, null);
                }
            }
            return encoding;
        }

        private static bool TryDecode(Encoding encoding, byte[] input, int start, int count, out string text)
        {
            try
            {
                // MB_ERR_INVALID_CHARS refuses a byte the code page leaves undefined; .NET's tables give
                // some of those a best-fit private-use character (cp949's 0xFF is U+F8F7, cp932's
                // U+F8F3), which a single byte never decodes to in Windows' own tables - so a slice
                // starting at such a byte is refused, however long.
                if (input[start] >= 0x80 && IsUndefinedSingleByte(encoding, input[start]))
                {
                    text = null;
                    return false;
                }
                text = encoding.GetString(input, start, count);
                return text.Length > 0;
            }
            catch (DecoderFallbackException)
            {
                text = null;
                return false;
            }
        }

        /// <summary>decode_code_page_stateful: consumed null is final (PyUnicode_DecodeCodePageStateful).</summary>
        internal static string Decode(int codePage, byte[] input, string errors, bool final, out int consumed)
        {
            if (codePage < 0)
                throw PyValueError.Create("invalid code page number");
            consumed = 0;
            if (input.Length == 0)
                return "";
            var encoding = Strict(codePage);
            // decode_code_page_strict
            if (TryDecode(encoding, input, 0, input.Length, out var whole) && !HasUndefinedSingleByte(encoding, input, whole))
            {
                consumed = input.Length;
                return whole;
            }
            // decode_code_page_errors
            string name = CodePageName(codePage);
            if ((errors == null || errors == "strict") && final)
            {
                // The last error was ERROR_NO_UNICODE_TRANSLATION: a UnicodeDecodeError over [0, 0).
                throw new PythonException((PyBaseException)PyType.UnicodeDecodeErrorType.Call(new PyObject[]
                    { PyString.From(name), PyBytes.From(input), PyInt.From(0), PyInt.From(0), PyString.From(DecodeReason) }, null));
            }
            var output = new StringBuilder();
            int position = 0;
            while (position < input.Length)
            {
                int size = 1;
                string decoded = null;
                // a character is at most 4 bytes (a UTF-8 sequence)
                for (; size <= 4 && position + size <= input.Length; size++)
                    if (TryDecode(encoding, input, position, size, out decoded))
                        break;
                if (decoded == null)
                {
                    // the last character of a partial decode
                    if (position + Math.Min(size, input.Length - position) >= input.Length && !final)
                        break;
                    position = PyCodecErrors.CallDecode(errors ?? "strict", name, DecodeReason, ref input, position, position + 1, output);
                }
                else
                {
                    output.Append(decoded);
                    position += size;
                }
            }
            consumed = position;
            return output.ToString();
        }

        private static bool IsUndefinedSingleByte(Encoding encoding, byte b)
        {
            try
            {
                return encoding.GetString(new[] { b }) is { Length: 1 } single && single[0] >= '' && single[0] <= '';
            }
            catch (DecoderFallbackException)
            {
                // a lead byte: no character alone
                return false;
            }
        }

        // Whether a strict result holds a private-use character that a lone byte decoded to (see
        // TryDecode): walked character by character as decode_code_page_errors does.
        private static bool HasUndefinedSingleByte(Encoding encoding, byte[] input, string decoded)
        {
            bool privateUse = false;
            foreach (char c in decoded)
                privateUse |= c >= '' && c <= '';
            if (!privateUse)
                return false;
            int position = 0;
            while (position < input.Length)
            {
                int size = 1;
                bool ok = false;
                for (; size <= 4 && position + size <= input.Length; size++)
                    if (ok = TryDecode(encoding, input, position, size, out _))
                        break;
                if (!ok)
                    return true;
                position += size;
            }
            return false;
        }

        // encode_code_page_flags: "replace" lets WideCharToMultiByte use best-fit characters; a
        // character it still cannot map comes out as the default character, which counts as a failure.
        private static Encoding BestFit(int codePage)
        {
            int page = Resolve(codePage);
            return page == CP_UTF8 ? null : CodePagesEncodingProvider.Instance.GetEncoding(page);
        }

        /// <summary>encode_code_page</summary>
        internal static byte[] Encode(int codePage, string text, string errors)
        {
            if (codePage < 0)
                throw PyValueError.Create("invalid code page number");
            if (text.Length == 0)
                return Array.Empty<byte>();
            var encoding = Strict(codePage);
            // encode_code_page_strict
            try
            {
                return encoding.GetBytes(text);
            }
            catch (EncoderFallbackException)
            {
            }
            // encode_code_page_errors
            string name = CodePageName(codePage);
            if (errors == null || errors == "strict")
                throw new PythonException((PyBaseException)PyType.UnicodeEncodeErrorType.Call(new PyObject[]
                    { PyString.From(name), PyString.From(text), PyInt.From(0), PyInt.From(0), PyString.From(EncodeReason) }, null));
            var output = new System.IO.MemoryStream();
            // the code points, which the handler's positions count
            var points = new System.Collections.Generic.List<int>(text.Length);
            for (int unit = 0; unit < text.Length; unit++)
            {
                if (char.IsHighSurrogate(text[unit]) && unit + 1 < text.Length && char.IsLowSurrogate(text[unit + 1]))
                {
                    points.Add(char.ConvertToUtf32(text[unit], text[unit + 1]));
                    unit++;
                }
                else
                    points.Add(text[unit]);
            }
            int length = points.Count;
            var bestFit = errors == "replace" ? BestFit(codePage) : null;
            int position = 0;
            while (position < length)
            {
                int point = points[position];
                string ch = point is >= 0xD800 and <= 0xDFFF ? ((char)point).ToString() : char.ConvertFromUtf32(point);
                byte[] bytes = null;
                try
                {
                    bytes = encoding.GetBytes(ch);
                }
                catch (EncoderFallbackException)
                {
                    // usedDefaultChar: the best-fit result is the default character '?' for anything but '?'
                    if (bestFit != null && bestFit.GetBytes(ch) is { } fitted && Array.Exists(fitted, b => b != (byte)'?'))
                        bytes = fitted;
                }
                if (bytes != null)
                {
                    output.Write(bytes, 0, bytes.Length);
                    position++;
                    continue;
                }
                var replacement = PyCodecErrors.CallEncode(errors, name, EncodeReason, text, position, position + 1, length, out int newPosition);
                if (replacement is PyBytes raw)
                    output.Write(raw.Value, 0, raw.Value.Length);
                else
                {
                    foreach (char c in ((PyString)replacement).Value)
                    {
                        if (c > 127)
                            throw new PythonException((PyBaseException)PyType.UnicodeEncodeErrorType.Call(new PyObject[]
                            {
                                PyString.From(name), PyString.From(text), PyInt.From(position), PyInt.From(position + 1),
                                PyString.From("unable to encode error handler result to ASCII"),
                            }, null));
                        output.WriteByte((byte)c);
                    }
                }
                position = newPosition;
            }
            return output.ToArray();
        }
    }
}
