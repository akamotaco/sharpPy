using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// The character mapping codec of Objects/unicodeobject.c (CPython 3.12.15):
    /// PyUnicode_DecodeCharmap (charmap_decode_string, charmap_decode_mapping),
    /// PyUnicode_BuildEncodingMap and its EncodingMap, and _PyUnicode_EncodeCharmap
    /// (charmapencode_output, charmap_encoding_error). A null mapping is Latin-1.
    /// </summary>
    internal static class CharmapCodec
    {
        private const int MAX_UNICODE = 0x10FFFF;
        private const string Reason = "character maps to <undefined>";

        // The code points of a str, one per index as PyUnicode_READ reads them.
        private static int[] CodePoints(string text)
        {
            var points = new int[PyString.CodePointCount(text)];
            for (int i = 0, n = 0; i < text.Length; n++)
            {
                points[n] = PyString.CodePointAt(text, i, out int width);
                i += width;
            }
            return points;
        }

        /// <summary>PyUnicode_DecodeCharmap</summary>
        internal static PyString Decode(byte[] s, PyObject mapping, string errors)
        {
            // Default to Latin-1
            if (mapping == null)
                return PyStringNewArguments.DecodeBytes(s, "iso8859-1", errors);
            if (s.Length == 0)
                return PyString.Empty;
            var writer = new PyUnicodeWriter();
            if (mapping is PyString table && mapping.IsExactly(PyType.StrType))
                DecodeString(s, CodePoints(table.Value), errors, writer);
            else
                DecodeMapping(s, mapping, errors, writer);
            return writer.Finish();
        }

        // charmap_decode_string: the str's character at each byte's index; U+FFFE and an index past
        // its end are undefined.
        private static void DecodeString(byte[] s, int[] map, string errors, PyUnicodeWriter writer)
        {
            int p = 0;
            while (p < s.Length)
            {
                int ch = s[p];
                int x = ch < map.Length ? map[ch] : 0xfffe;  // invalid value
                if (x == 0xfffe)
                {
                    // undefined mapping
                    p = PyCodecErrors.CallDecode(errors, "charmap", Reason, ref s, p, p + 1, writer);
                    continue;
                }
                writer.WriteChar(x);
                ++p;
            }
        }

        // charmap_decode_mapping: mapping[byte] is an int, a str or None.
        private static void DecodeMapping(byte[] s, PyObject mapping, string errors, PyUnicodeWriter writer)
        {
            int p = 0;
            while (p < s.Length)
            {
                int ch = s[p];
                // Get mapping (char ordinal -> integer, Unicode char or None)
                PyObject item;
                try
                {
                    item = mapping.GetItem(PyInt.From(ch));
                }
                catch (PythonException e) when (e.PyException.GetPyType().IsSubclassOf(PyType.LookupErrorType))
                {
                    // No mapping found means: mapping is undefined.
                    item = null;
                }
                if (item != null && item is not PyNone)
                {
                    if (item is PyInt number)
                    {
                        long value = number.Value >= long.MinValue && number.Value <= long.MaxValue ? (long)number.Value : -1;
                        if (value != 0xFFFE)
                        {
                            if (value < 0 || value > MAX_UNICODE)
                                throw PyTypeError.Create($"character mapping must be in range(0x{MAX_UNICODE + 1:x})");
                            writer.WriteChar((int)value);
                            ++p;
                            continue;
                        }
                    }
                    else if (item is PyString text)
                    {
                        if (text.CodePointLength() != 1 || PyString.CodePointAt(text.Value, 0, out _) != 0xFFFE)
                        {
                            writer.WriteStr(text);
                            ++p;
                            continue;
                        }
                    }
                    else
                        // wrong return value
                        throw PyTypeError.Create("character mapping must return integer, None or str");
                }
                // Undefined: undefined mapping
                p = PyCodecErrors.CallDecode(errors, "charmap", Reason, ref s, p, p + 1, writer);
            }
        }

        /// <summary>struct encoding_map: a three-level trie from BMP characters to bytes.</summary>
        internal sealed class EncodingMap : PyObject
        {
            internal static readonly PyType Type = MakeType();

            internal readonly byte[] Level1 = new byte[32];
            internal readonly int Count2, Count3;
            internal readonly byte[] Level23;

            internal EncodingMap(int count2, int count3)
            {
                Count2 = count2;
                Count3 = count3;
                Level23 = new byte[16 * count2 + 128 * count3];
            }

            public override PyType GetPyType() => Type;

            private static PyType MakeType()
            {
                var type = new PyType("EncodingMap", new[] { PyType.ObjectType });
                // EncodingMap_size_impl: (sizeof(*self) - 1) + 16*count2 + 128*count3, where the
                // struct is PyObject_HEAD, level1[32], two ints and level23[1].
                PyArgs.Define(type, "size", PyCall.NoArgs, (self, args) =>
                {
                    var map = (EncodingMap)self;
                    const int sizeofSelf = 64;  // 57, aligned to 8
                    return PyInt.From((long)(sizeofSelf - 1) + 16 * map.Count2 + 128 * map.Count3);
                });
                return type;
            }
        }

        /// <summary>PyUnicode_BuildEncodingMap</summary>
        internal static PyObject BuildEncodingMap(string text)
        {
            var data = CodePoints(text);
            if (data.Length == 0)
                // PyErr_BadArgument
                throw PyTypeError.Create("bad argument type for built-in operation");
            int length = Math.Min(data.Length, 256);
            var level1 = new byte[32];
            var level2 = new byte[512];
            Array.Fill(level1, (byte)0xFF);
            Array.Fill(level2, (byte)0xFF);
            int count2 = 0, count3 = 0;

            // If there isn't a one-to-one mapping of NULL to \0, or if there are non-BMP
            // characters, we need to use a mapping dictionary.
            bool needDict = data[0] != 0;
            for (int i = 1; i < length; i++)
            {
                int ch = data[i];
                if (ch == 0 || ch > 0xFFFF)
                {
                    needDict = true;
                    break;
                }
                if (ch == 0xFFFE)
                    continue;  // unmapped character
                int l1 = ch >> 11;
                int l2 = ch >> 7;
                if (level1[l1] == 0xFF)
                    level1[l1] = (byte)count2++;
                if (level2[l2] == 0xFF)
                    level2[l2] = (byte)count3++;
            }

            if (count2 >= 0xFF || count3 >= 0xFF)
                needDict = true;

            if (needDict)
            {
                var result = new PyDict();
                for (int i = 0; i < length; i++)
                    result.SetItem(PyInt.From(data[i]), PyInt.From(i));
                return result;
            }

            // Create a three-level trie
            var map = new EncodingMap(count2, count3);
            var mlevel23 = map.Level23;
            int mlevel3 = 16 * count2;
            Array.Copy(level1, map.Level1, 32);
            Array.Fill(mlevel23, (byte)0xFF, 0, 16 * count2);
            count3 = 0;
            for (int i = 1; i < length; i++)
            {
                int ch = data[i];
                if (ch == 0xFFFE)
                    continue;  // unmapped character
                int o1 = ch >> 11;
                int o2 = (ch >> 7) & 0xF;
                int i2 = 16 * map.Level1[o1] + o2;
                if (mlevel23[i2] == 0xFF)
                    mlevel23[i2] = (byte)count3++;
                int o3 = ch & 0x7F;
                int i3 = 128 * mlevel23[i2] + o3;
                mlevel23[mlevel3 + i3] = (byte)i;
            }
            return map;
        }

        // encoding_map_lookup
        private static int EncodingMapLookup(int c, EncodingMap map)
        {
            int l1 = c >> 11;
            int l2 = (c >> 7) & 0xF;
            int l3 = c & 0x7F;
            if (c > 0xFFFF)
                return -1;
            if (c == 0)
                return 0;
            // level 1
            int i = map.Level1[l1];
            if (i == 0xFF)
                return -1;
            // level 2
            i = map.Level23[16 * i + l2];
            if (i == 0xFF)
                return -1;
            // level 3
            i = map.Level23[16 * map.Count2 + 128 * i + l3];
            if (i == 0)
                return -1;
            return i;
        }

        // charmapencode_lookup: None when the mapping does not define the character.
        private static PyObject EncodeLookup(int c, PyObject mapping)
        {
            PyObject x;
            try
            {
                x = mapping.GetItem(PyInt.From(c));
            }
            catch (PythonException e) when (e.PyException.GetPyType().IsSubclassOf(PyType.LookupErrorType))
            {
                // No mapping found means: mapping is undefined.
                return PyNone.Instance;
            }
            if (x is PyNone)
                return x;
            if (x is PyInt number)
            {
                if (number.Value < 0 || number.Value > 255)
                    throw PyTypeError.Create("character mapping must be in range(256)");
                return x;
            }
            if (x is PyBytes)
                return x;
            // wrong return value
            throw PyTypeError.Create($"character mapping must return integer, bytes or None, not {x.GetPyType().FullName}");
        }

        // charmapencode_output: false is enc_FAILED, nothing written.
        private static bool Output(int c, PyObject mapping, List<byte> res)
        {
            if (mapping is EncodingMap map && mapping.IsExactly(EncodingMap.Type))
            {
                int r = EncodingMapLookup(c, map);
                if (r == -1)
                    return false;
                res.Add((byte)r);
                return true;
            }
            var rep = EncodeLookup(c, mapping);
            if (rep is PyNone)
                return false;
            if (rep is PyInt number)
                res.Add((byte)(int)number.Value);
            else
                res.AddRange(((PyBytes)rep).Value);
            return true;
        }

        private static bool Undefined(int c, PyObject mapping) =>
            mapping is EncodingMap map && mapping.IsExactly(EncodingMap.Type)
                ? EncodingMapLookup(c, map) == -1
                : EncodeLookup(c, mapping) is PyNone;

        /// <summary>_PyUnicode_EncodeCharmap; the null mapping (Latin-1) is the caller's.</summary>
        internal static byte[] Encode(PyString unicode, PyObject mapping, string errors)
        {
            var data = unicode.ToCodePoints();
            int size = data.Length;
            var res = new List<byte>(size);
            int inpos = 0;
            while (inpos < size)
            {
                // try to encode it
                if (!Output(data[inpos], mapping, res))
                    // unencodable character
                    inpos = EncodingError(unicode, data, inpos, mapping, errors, res);
                else
                    // done with this character => adjust input position
                    ++inpos;
            }
            return res.ToArray();
        }

        // charmap_encoding_error: the run of unencodable characters from inpos, handled.
        private static int EncodingError(PyString unicode, int[] data, int inpos, PyObject mapping, string errors, List<byte> res)
        {
            int size = data.Length;
            // startpos for collecting unencodable chars
            int collstartpos = inpos;
            int collendpos = inpos + 1;
            // find all unencodable characters
            while (collendpos < size && Undefined(data[collendpos], mapping))
                ++collendpos;

            PythonException Raise() => PyUnicodeEncodeError.Failure("charmap", unicode, collstartpos, collendpos, Reason);

            // _Py_GetErrorHandler
            switch (errors ?? "strict")
            {
                case "strict":
                    throw Raise();
                case "replace":
                    for (int collpos = collstartpos; collpos < collendpos; ++collpos)
                        if (!Output('?', mapping, res))
                            throw Raise();
                    return collendpos;
                case "ignore":
                    return collendpos;
                case "xmlcharrefreplace":
                    // generate replacement
                    for (int collpos = collstartpos; collpos < collendpos; ++collpos)
                        foreach (char cp in $"&#{data[collpos]};")
                            if (!Output(cp, mapping, res))
                                throw Raise();
                    return collendpos;
                default:
                    var repunicode = PyCodecErrors.CallEncode(errors, "charmap", Reason, unicode, collstartpos, collendpos, size, out int newpos);
                    if (repunicode is PyBytes bytes)
                    {
                        // Directly copy bytes result to output.
                        res.AddRange(bytes.Value);
                        return newpos;
                    }
                    // generate replacement
                    foreach (int repch in ((PyString)repunicode).CodePoints())
                        if (!Output(repch, mapping, res))
                            throw Raise();
                    return newpos;
            }
        }
    }
}
