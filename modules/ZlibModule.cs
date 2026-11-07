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
                int level = args.Length > 1 ? args[1].ToInt()
                    : kwargs != null && kwargs.TryGetStoredValue(PyString.From("level"), out var given) ? given.ToInt() : -1;
                var output = new MemoryStream();
                using (var stream = new ZLibStream(output, level is 0 ? CompressionLevel.NoCompression
                           : level is 1 or 2 or 3 ? CompressionLevel.Fastest
                           : level == 9 ? CompressionLevel.SmallestSize : CompressionLevel.Optimal, leaveOpen: true))
                {
                    var data = Bytes(args[0]);
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
                uint value = args.Length > 1 ? (uint)(args[1].ToFloat()) : 1;
                uint low = value & 0xFFFF, high = value >> 16;
                foreach (byte b in Bytes(args[0]))
                {
                    low = (low + b) % 65521;
                    high = (high + low) % 65521;
                }
                return PyInt.From((long)((high << 16) | low));
            });
            module.ModuleDict["crc32"] = PyImportSystem.Import("binascii").GetAttribute("crc32");
            return module;
        }

        private static byte[] Bytes(PyObject value) => value switch
        {
            PyBytes bytes => bytes.Value,
            PyByteArray array => array.ToByteArrayCopy(),
            _ => throw PyTypeError.Create($"a bytes-like object is required, not '{value.GetTypeName()}'"),
        };
    }
}
