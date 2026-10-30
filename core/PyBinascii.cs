using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// binascii: the byte-transform primitives CPython implements in Modules/binascii.c, and the
    /// module the hex / base64 codecs are built on. Only the four functions the codecs need are
    /// here, with the error text measured from CPython 3.12.15:
    ///   unhexlify(b'616')  -> binascii.Error: Odd-length string
    ///   unhexlify(b'zz')   -> binascii.Error: Non-hexadecimal digit found
    ///   a2b_base64(b'YWI') -> binascii.Error: Incorrect padding
    /// The refusals are binascii.Error (a ValueError subclass), not UnicodeError: a codec that
    /// cannot represent the bytes is a different failure from a text encoding that cannot.
    /// </summary>
    public class PyBinasciiError : PyValueError
    {
        public PyBinasciiError(string message) : base(message) { }
        public override string GetTypeName() => "Error";

        public new static PythonException Create(string message)
        {
            return new PythonException(new PyBinasciiError(message));
        }
    }

    internal static class PyBinascii
    {
        private const string Alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        private const string HexDigits = "0123456789abcdef";

        /// <summary>binascii.hexlify - accepts bytes-like input only.</summary>
        internal static byte[] Hexlify(PyObject obj)
        {
            byte[] data = ToBytes(obj, requireStrAllowed: false, strWording: BytesLikeRequired);
            var outBytes = new byte[data.Length * 2];
            for (int i = 0; i < data.Length; i++)
            {
                outBytes[i * 2] = (byte)HexDigits[data[i] >> 4];
                outBytes[i * 2 + 1] = (byte)HexDigits[data[i] & 0xF];
            }
            return outBytes;
        }

        // The two refusals differ per function in CPython and both are measured.
        private const string BytesLikeRequired = "a bytes-like object is required, not {TYPE}";
        // binascii's other refusal names the type without quotes: not str, not int.
        private const string ExpectedBytesLike = "expected bytes-like object, not {BARETYPE}";

        /// <summary>binascii.unhexlify - str or bytes, even length, hex digits only.</summary>
        internal static byte[] Unhexlify(PyObject obj)
        {
            byte[] data = ToBytes(obj, requireStrAllowed: true, strWording: BytesLikeRequired);
            if (data.Length % 2 != 0) throw PyBinasciiError.Create("Odd-length string");
            var outBytes = new byte[data.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                int nibble = HexDigit(data[i]);
                if (nibble < 0) throw PyBinasciiError.Create("Non-hexadecimal digit found");
                outBytes[i / 2] |= (byte)(nibble << ((i % 2) == 0 ? 4 : 0));
            }
            return outBytes;
        }

        /// <summary>binascii.b2a_base64 with newline=True: padded, with a trailing LF.</summary>
        internal static byte[] B2aBase64(PyObject obj)
        {
            byte[] data = ToBytes(obj, requireStrAllowed: false, strWording: ExpectedBytesLike);
            if (data.Length == 0) return Array.Empty<byte>();
            var sb = new StringBuilder((data.Length + 2) / 3 * 4 + 1);
            int i = 0;
            while (i + 3 <= data.Length)
            {
                int v = (data[i] << 16) | (data[i + 1] << 8) | data[i + 2];
                sb.Append(Alphabet[(v >> 18) & 63]).Append(Alphabet[(v >> 12) & 63])
                  .Append(Alphabet[(v >> 6) & 63]).Append(Alphabet[v & 63]);
                i += 3;
            }
            int rest = data.Length - i;
            if (rest == 1)
            {
                int v = data[i] << 16;
                sb.Append(Alphabet[(v >> 18) & 63]).Append(Alphabet[(v >> 12) & 63])
                  .Append("==");
            }
            else if (rest == 2)
            {
                int v = (data[i] << 16) | (data[i + 1] << 8);
                sb.Append(Alphabet[(v >> 18) & 63]).Append(Alphabet[(v >> 12) & 63])
                  .Append(Alphabet[(v >> 6) & 63]).Append('=');
            }
            sb.Append('\n');
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        /// <summary>
        /// binascii.a2b_base64: characters outside the alphabet are skipped, the first padding
        /// character ends the data, and a tail that cannot be decoded is "Incorrect padding".
        /// </summary>
        internal static byte[] A2bBase64(PyObject obj)
        {
            byte[] data = ToBytes(obj, requireStrAllowed: false, strWording: ExpectedBytesLike);
            var bits = new List<int>();
            bool padded = false;
            foreach (byte b in data)
            {
                if (b == (byte)'=') { padded = true; break; }
                int index = Alphabet.IndexOf((char)b);
                if (index >= 0) bits.Add(index);
            }
            if (!padded && bits.Count % 4 == 3) throw PyBinasciiError.Create("Incorrect padding");
            if (bits.Count % 4 == 1) throw PyBinasciiError.Create("Incorrect padding");

            var outBytes = new List<byte>(bits.Count * 3 / 4);
            for (int i = 0; i + 4 <= bits.Count; i += 4)
            {
                int v = (bits[i] << 18) | (bits[i + 1] << 12) | (bits[i + 2] << 6) | bits[i + 3];
                outBytes.Add((byte)((v >> 16) & 0xFF));
                outBytes.Add((byte)((v >> 8) & 0xFF));
                outBytes.Add((byte)(v & 0xFF));
            }
            int tail = bits.Count % 4;
            if (tail == 2)
            {
                int v = (bits[bits.Count - 2] << 18) | (bits[bits.Count - 1] << 12);
                outBytes.Add((byte)((v >> 16) & 0xFF));
            }
            else if (tail == 3)
            {
                int v = (bits[bits.Count - 3] << 18) | (bits[bits.Count - 2] << 12) |
                        (bits[bits.Count - 1] << 6);
                outBytes.Add((byte)((v >> 16) & 0xFF));
                outBytes.Add((byte)((v >> 8) & 0xFF));
            }
            return outBytes.ToArray();
        }

        private static int HexDigit(byte b)
        {
            if (b >= (byte)'0' && b <= (byte)'9') return b - (byte)'0';
            if (b >= (byte)'a' && b <= (byte)'f') return b - (byte)'a' + 10;
            if (b >= (byte)'A' && b <= (byte)'F') return b - (byte)'A' + 10;
            return -1;
        }

        /// <summary>
        /// bytes/bytearray always; str only where binascii allows it (unhexlify). The wording is
        /// per function in CPython, so both shapes are kept: "a bytes-like object is required,
        /// not 'str'" and "expected bytes-like object, not str".
        /// </summary>
        private static byte[] ToBytes(PyObject obj, bool requireStrAllowed, string strWording)
        {
            if (obj is PyBytes b) return b.Value;
            if (obj is PyByteArray ba) return ba.ToByteArrayCopy();
            if (obj is PyString s && requireStrAllowed)
                return Encoding.ASCII.GetBytes(s.Value);
            string typeText = obj is PyString ? "'str'" : obj.GetTypeName();
            string bareType = obj is PyString ? "str" : obj.GetTypeName();
            throw PyTypeError.Create(strWording.Replace("{TYPE}", typeText).Replace("{BARETYPE}", bareType));
        }

        public static PyModule CreateBinasciiModule()
        {
            var module = new PyModule("binascii", "Conversion between binary data and ASCII");
            module.ModuleDict["hexlify"] = new PyBuiltinFunction("hexlify", a =>
                new PyBytes(Hexlify(Check(a, "hexlify"))));
            module.ModuleDict["unhexlify"] = new PyBuiltinFunction("unhexlify", a =>
                new PyBytes(Unhexlify(Check(a, "unhexlify"))));
            module.ModuleDict["b2a_base64"] = new PyBuiltinFunction("b2a_base64", a =>
                new PyBytes(B2aBase64(Check(a, "b2a_base64"))));
            module.ModuleDict["a2b_base64"] = new PyBuiltinFunction("a2b_base64", a =>
                new PyBytes(A2bBase64(Check(a, "a2b_base64"))));
            return module;
        }

        private static PyObject Check(PyObject[] args, string name)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"{name}() takes exactly one argument ({args.Length} given)");
            return args[0];
        }
    }
}
