using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible marshal module for serializing Python objects
    /// Reference: Python/marshal.c
    /// </summary>
    public static class PyMarshal
    {
        // CPython 3.12: Python/marshal.c:45-78
        // Type codes for marshal format
        private const byte TYPE_NULL = (byte)'0';
        private const byte TYPE_NONE = (byte)'N';
        private const byte TYPE_FALSE = (byte)'F';
        private const byte TYPE_TRUE = (byte)'T';
        private const byte TYPE_STOPITER = (byte)'S';
        private const byte TYPE_ELLIPSIS = (byte)'.';
        private const byte TYPE_INT = (byte)'i';
        private const byte TYPE_FLOAT = (byte)'f';
        private const byte TYPE_BINARY_FLOAT = (byte)'g';
        private const byte TYPE_COMPLEX = (byte)'x';
        private const byte TYPE_BINARY_COMPLEX = (byte)'y';
        private const byte TYPE_LONG = (byte)'l';
        private const byte TYPE_STRING = (byte)'s';
        private const byte TYPE_INTERNED = (byte)'t';
        private const byte TYPE_REF = (byte)'r';
        private const byte TYPE_TUPLE = (byte)'(';
        private const byte TYPE_LIST = (byte)'[';
        private const byte TYPE_DICT = (byte)'{';
        private const byte TYPE_CODE = (byte)'c';
        private const byte TYPE_UNICODE = (byte)'u';
        private const byte TYPE_UNKNOWN = (byte)'?';
        private const byte TYPE_SET = (byte)'<';
        private const byte TYPE_FROZENSET = (byte)'>';
        private const byte FLAG_REF = 0x80;

        private const byte TYPE_ASCII = (byte)'a';
        private const byte TYPE_ASCII_INTERNED = (byte)'A';
        private const byte TYPE_SMALL_TUPLE = (byte)')';
        private const byte TYPE_SHORT_ASCII = (byte)'z';
        private const byte TYPE_SHORT_ASCII_INTERNED = (byte)'Z';

        /// <summary>
        /// Serialize a PyCodeObject to bytes (marshal format)
        /// CPython 3.12: Python/marshal.c:649-665 (PyMarshal_WriteObjectToFile)
        /// CPython 3.12: Python/marshal.c:553-578 (w_object for TYPE_CODE)
        /// </summary>
        public static byte[] DumpCodeObject(PyCodeObject code)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                WriteCodeObject(writer, code);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Write a PyCodeObject in marshal format
        /// CPython 3.12: Python/marshal.c:553-578
        /// Format:
        ///   TYPE_CODE
        ///   argcount (long)
        ///   posonlyargcount (long)
        ///   kwonlyargcount (long)
        ///   stacksize (long)
        ///   flags (long)
        ///   code (bytes object - bytecode instructions)
        ///   consts (tuple)
        ///   names (tuple)
        ///   localsplusnames (tuple) - varnames + cellvars + freevars
        ///   localspluskinds (bytes) - kind info for each local
        ///   filename (string)
        ///   name (string)
        ///   qualname (string)
        ///   firstlineno (long)
        ///   linetable (bytes object)
        ///   exceptiontable (bytes object)
        /// </summary>
        private static void WriteCodeObject(BinaryWriter writer, PyCodeObject code)
        {
            // CPython 3.12: Python/marshal.c:560
            writer.Write(TYPE_CODE);

            // CPython 3.12: Python/marshal.c:561-565
            WriteLong(writer, code.ArgCount);
            WriteLong(writer, code.PosonlyArgCount);
            WriteLong(writer, code.KwonlyArgCount);
            WriteLong(writer, 10); // Stacksize - estimate for now
            WriteLong(writer, code.Flags);

            // CPython 3.12: Python/marshal.c:566 - bytecode as bytes
            WriteBytes(writer, SerializeBytecode(code.Instructions));

            // CPython 3.12: Python/marshal.c:567 - consts tuple
            WriteTuple(writer, code.Constants);

            // CPython 3.12: Python/marshal.c:568 - names tuple
            WriteStringTuple(writer, code.Names);

            // CPython 3.12: Python/marshal.c:569-570
            // localsplusnames = varnames + cellvars + freevars
            var localsplusnames = new List<string>();
            localsplusnames.AddRange(code.VarNames);
            localsplusnames.AddRange(code.CellVars);
            localsplusnames.AddRange(code.FreeVars);
            WriteStringTuple(writer, localsplusnames);

            // CPython 3.12: Python/marshal.c:570 - localspluskinds (stub for now)
            WriteBytes(writer, new byte[localsplusnames.Count]);

            // CPython 3.12: Python/marshal.c:571-573
            WriteString(writer, code.FileName ?? "<unknown>");
            WriteString(writer, code.Name);
            WriteString(writer, code.Name); // qualname = name for now

            // CPython 3.12: Python/marshal.c:574
            WriteLong(writer, 1); // FirstLineNo - default to 1

            // CPython 3.12: Python/marshal.c:575 - linetable (stub for now)
            WriteBytes(writer, new byte[0]);

            // CPython 3.12: Python/marshal.c:576 - exception table
            WriteBytes(writer, SerializeExceptionTable(code.ExceptionTable));
        }

        /// <summary>
        /// Serialize bytecode instructions to bytes
        /// CPython 3.12: Each instruction is 2 bytes (opcode + arg)
        /// </summary>
        private static byte[] SerializeBytecode(List<ByteCodeInstruction> instructions)
        {
            var bytes = new List<byte>();
            foreach (var instr in instructions)
            {
                // CPython 3.12: instruction word = opcode | (arg << 8)
                // But in .pyc format, it's stored as 2 bytes: opcode, arg
                bytes.Add((byte)instr.OpCode);
                bytes.Add((byte)instr.Argument);
            }
            return bytes.ToArray();
        }

        /// <summary>
        /// Serialize exception table to bytes
        /// CPython 3.12: Objects/exception_handling_notes.txt
        /// Format: varint-encoded triples (start, end, target, depth)
        /// </summary>
        private static byte[] SerializeExceptionTable(List<ExceptionTableEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return new byte[0];

            var bytes = new List<byte>();
            foreach (var entry in entries)
            {
                // CPython 3.12: exception table uses varint encoding
                // Simplified version: just write raw values (TODO: implement varint)
                bytes.AddRange(EncodeVarint(entry.StartOffset));
                bytes.AddRange(EncodeVarint(entry.EndOffset - entry.StartOffset)); // length
                bytes.AddRange(EncodeVarint(entry.HandlerOffset));
                bytes.AddRange(EncodeVarint(entry.Depth | (entry.Lasti ? 1 : 0)));
            }
            return bytes.ToArray();
        }

        /// <summary>
        /// Encode an integer as varint (variable-length integer)
        /// CPython 3.12: Uses 7 bits per byte, MSB = continuation bit
        /// </summary>
        private static byte[] EncodeVarint(int value)
        {
            var bytes = new List<byte>();
            uint uval = (uint)value;

            while (uval >= 0x80)
            {
                bytes.Add((byte)(uval | 0x80));
                uval >>= 7;
            }
            bytes.Add((byte)uval);

            return bytes.ToArray();
        }

        /// <summary>
        /// Write a long (4 bytes, little-endian)
        /// CPython 3.12: Python/marshal.c:179-190 (w_long)
        /// </summary>
        private static void WriteLong(BinaryWriter writer, int value)
        {
            // CPython 3.12: little-endian 32-bit integer
            writer.Write((byte)(value & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 24) & 0xFF));
        }

        /// <summary>
        /// Write a bytes object
        /// CPython 3.12: Python/marshal.c:481-488 (w_pstring for TYPE_STRING)
        /// </summary>
        private static void WriteBytes(BinaryWriter writer, byte[] data)
        {
            writer.Write(TYPE_STRING);
            WriteLong(writer, data.Length);
            writer.Write(data);
        }

        /// <summary>
        /// Write a Unicode string
        /// CPython 3.12: Python/marshal.c:430-480 (w_unicode variants)
        /// </summary>
        private static void WriteString(BinaryWriter writer, string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);

            // CPython 3.12: TYPE_SHORT_ASCII for strings < 256 bytes (all ASCII)
            if (bytes.Length < 256 && IsAscii(str))
            {
                writer.Write(TYPE_SHORT_ASCII);
                writer.Write((byte)bytes.Length);
            }
            // CPython 3.12: TYPE_ASCII for ASCII strings
            else if (IsAscii(str))
            {
                writer.Write(TYPE_ASCII);
                WriteLong(writer, bytes.Length);
            }
            // CPython 3.12: TYPE_UNICODE for general Unicode
            else
            {
                writer.Write(TYPE_UNICODE);
                WriteLong(writer, bytes.Length);
            }

            writer.Write(bytes);
        }

        /// <summary>
        /// Check if a string is pure ASCII
        /// </summary>
        private static bool IsAscii(string str)
        {
            foreach (char c in str)
            {
                if (c > 127)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Write a tuple of PyObjects
        /// CPython 3.12: Python/marshal.c:492-510 (TYPE_TUPLE / TYPE_SMALL_TUPLE)
        /// </summary>
        private static void WriteTuple(BinaryWriter writer, List<PyObject> items)
        {
            // CPython 3.12: TYPE_SMALL_TUPLE for tuples with < 256 items
            if (items.Count < 256)
            {
                writer.Write(TYPE_SMALL_TUPLE);
                writer.Write((byte)items.Count);
            }
            else
            {
                writer.Write(TYPE_TUPLE);
                WriteLong(writer, items.Count);
            }

            foreach (var item in items)
            {
                WriteObject(writer, item);
            }
        }

        /// <summary>
        /// Write a tuple of strings
        /// </summary>
        private static void WriteStringTuple(BinaryWriter writer, List<string> items)
        {
            if (items.Count < 256)
            {
                writer.Write(TYPE_SMALL_TUPLE);
                writer.Write((byte)items.Count);
            }
            else
            {
                writer.Write(TYPE_TUPLE);
                WriteLong(writer, items.Count);
            }

            foreach (var item in items)
            {
                WriteString(writer, item);
            }
        }

        /// <summary>
        /// Write a general PyObject
        /// CPython 3.12: Python/marshal.c:281-596 (w_object)
        /// </summary>
        private static void WriteObject(BinaryWriter writer, PyObject obj)
        {
            // CPython 3.12: Python/marshal.c:289-596
            if (obj == null || obj == PyNone.Instance)
            {
                writer.Write(TYPE_NONE);
            }
            else if (obj is PyBool pyBool)
            {
                writer.Write(pyBool.Value ? TYPE_TRUE : TYPE_FALSE);
            }
            else if (obj is PyInt pyInt)
            {
                // CPython 3.12: TYPE_INT for small integers
                writer.Write(TYPE_INT);
                WriteLong(writer, (int)pyInt.Value);
            }
            else if (obj is PyFloat pyFloat)
            {
                // CPython 3.12: TYPE_BINARY_FLOAT (8 bytes, IEEE 754)
                writer.Write(TYPE_BINARY_FLOAT);
                writer.Write(BitConverter.GetBytes(pyFloat.Value));
            }
            else if (obj is PyStr pyStr)
            {
                WriteString(writer, pyStr.Value);
            }
            else if (obj is PyBytes pyBytes)
            {
                WriteBytes(writer, pyBytes.Value);
            }
            else if (obj is PyTuple pyTuple)
            {
                WriteTuple(writer, new List<PyObject>(pyTuple.Items));
            }
            else if (obj is PyCodeObject codeObj)
            {
                WriteCodeObject(writer, codeObj);
            }
            else
            {
                // Unknown type: write as None
                writer.Write(TYPE_NONE);
            }
        }
    }
}
