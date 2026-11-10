using System;
using System.IO;
using System.IO.Compression;

namespace SharpPy.Modules
{
    /// <summary>
    /// zlib - the one-shot functions of Modules/zlibmodule.c, on the base library's ZLibStream and DeflateStream.
    /// compressobj and decompressobj are not provided. Compressed bytes are valid but need not equal zlib's own.
    /// </summary>
    internal static class ZlibModule
    {
        private static readonly PyType ErrorType = PyExceptionSlots.NewException("zlib", "error", PyType.ExceptionType);

        internal static PyModule CreateZlibModule()
        {
            var module = new PyModule("zlib");
            module.ModuleDict["error"] = ErrorType;
            module.ModuleDict["MAX_WBITS"] = PyInt.From(15);
            module.ModuleDict["DEFLATED"] = PyInt.From(8);
            module.ModuleDict["DEF_MEM_LEVEL"] = PyInt.From(8);
            module.ModuleDict["Z_DEFAULT_COMPRESSION"] = PyInt.From(-1);
            module.ModuleDict["Z_BEST_SPEED"] = PyInt.From(1);
            module.ModuleDict["Z_BEST_COMPRESSION"] = PyInt.From(9);
            module.ModuleDict["compress"] = new PyBuiltinFunction("compress", (args, kwargs) =>
            {
                // zlib_compress's clinic: compress(data, /, level=-1, wbits=15), converted in order.
                var parsed = PyArgs.Unpack("compress", args, kwargs, new[] { "", "level", "wbits" }, 1, 1, 3);
                var data = Bytes(parsed[0]);
                int level = parsed[1] == null ? -1 : PyArgs.Int(parsed[1]);
                if (parsed[2] != null)
                    PyArgs.Int(parsed[2]);
                var output = new MemoryStream();
                using (var stream = new ZLibStream(output, level is 0 ? CompressionLevel.NoCompression
                           : level is 1 or 2 or 3 ? CompressionLevel.Fastest
                           : level == 9 ? CompressionLevel.SmallestSize : CompressionLevel.Optimal, leaveOpen: true))
                {
                    stream.Write(data, 0, data.Length);
                }
                return PyBytes.From(output.ToArray());
            });
            module.ModuleDict["decompress"] = new PyBuiltinFunction("decompress", (args, kwargs) =>
            {
                try
                {
                    using var input = new MemoryStream(Bytes(args[0]));
                    using var stream = new ZLibStream(input, CompressionMode.Decompress);
                    var output = new MemoryStream();
                    stream.CopyTo(output);
                    return PyBytes.From(output.ToArray());
                }
                catch (InvalidDataException)
                {
                    throw PyExceptionSlots.Raise(ErrorType, "Error -3 while decompressing data: incorrect header check");
                }
            });
            module.ModuleDict["adler32"] = new PyBuiltinFunction("adler32", args =>
            {
                // zlib_adler32: data a Py_buffer, then value unsigned_int(bitwise=True).
                var data = Bytes(args[0]);
                uint value = args.Length > 1 ? PyArgs.UnsignedIntMask(args[1]) : 1;
                uint low = value & 0xFFFF, high = value >> 16;
                foreach (byte b in data)
                {
                    low = (low + b) % 65521;
                    high = (high + low) % 65521;
                }
                return PyInt.From((long)((high << 16) | low));
            });
            // zlib_crc32: its own function, the same CRC as binascii.
            module.ModuleDict["crc32"] = new PyBuiltinFunction("crc32", args =>
            {
                var data = Bytes(args[0]);
                return PyInt.From((long)PyBinascii.Crc32(data, args.Length > 1 ? PyArgs.UnsignedIntMask(args[1]) : 0));
            });
            return module;
        }

        // Py_buffer
        private static byte[] Bytes(PyObject value) => BytesMethods.RequireBuffer(value);
    }
}
