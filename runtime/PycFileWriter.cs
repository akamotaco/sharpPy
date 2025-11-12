using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible .pyc file writer
    /// Reference: Lib/importlib/_bootstrap_external.py
    /// </summary>
    public static class PycFileWriter
    {
        // CPython 3.12: Lib/importlib/_bootstrap_external.py:469
        // MAGIC_NUMBER = (3531).to_bytes(2, 'little') + b'\r\n'
        // Python 3.12 magic number (3531 = 0x0DCB)
        private static readonly byte[] MAGIC_NUMBER = new byte[] { 0xCB, 0x0D, 0x0D, 0x0A };

        // CPython 3.12: Lib/importlib/_bootstrap_external.py:473
        private const string PYCACHE = "__pycache__";

        // CPython 3.12: sys.implementation.cache_tag for Python 3.12
        private const string CACHE_TAG = "cpython-312";

        /// <summary>
        /// Get the .pyc file path for a given .py source file
        /// CPython 3.12: Lib/importlib/_bootstrap_external.py:486-553 (cache_from_source)
        /// </summary>
        /// <param name="sourcePath">.py file path</param>
        /// <returns>.pyc file path in __pycache__</returns>
        public static string GetPycPath(string sourcePath)
        {
            // CPython 3.12: Lib/importlib/_bootstrap_external.py:511-528
            var directory = Path.GetDirectoryName(sourcePath) ?? ".";
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);

            // CPython 3.12: Lib/importlib/_bootstrap_external.py:517-528
            // Format: <name>.<cache_tag>.pyc
            var pycFileName = $"{fileNameWithoutExt}.{CACHE_TAG}.pyc";

            // CPython 3.12: Lib/importlib/_bootstrap_external.py:553
            // Return path in __pycache__ subdirectory
            return Path.Combine(directory, PYCACHE, pycFileName);
        }

        /// <summary>
        /// Write a .pyc file (timestamp-based invalidation mode)
        /// CPython 3.12: Lib/importlib/_bootstrap_external.py:768-775 (_code_to_timestamp_pyc)
        ///
        /// .pyc file format:
        ///   - Magic number (4 bytes): Python version identifier
        ///   - Flags (4 bytes): PEP 552 invalidation mode (0 = timestamp)
        ///   - Timestamp (4 bytes): Source file modification time (unix timestamp)
        ///   - Source size (4 bytes): Source file size in bytes
        ///   - Marshalled code object (variable length)
        /// </summary>
        public static void WritePycFile(string pycPath, PyCodeObject codeObject, string sourcePath)
        {
            // CPython 3.12: Lib/importlib/_bootstrap_external.py:155-158
            // Create __pycache__ directory if it doesn't exist
            var directory = Path.GetDirectoryName(pycPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Get source file stats
            var sourceInfo = new FileInfo(sourcePath);
            var mtime = (uint)((DateTimeOffset)sourceInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
            var sourceSize = (uint)sourceInfo.Length;

            using (var fs = new FileStream(pycPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                // CPython 3.12: Lib/importlib/_bootstrap_external.py:770-774

                // 1. Magic number (4 bytes)
                writer.Write(MAGIC_NUMBER);

                // 2. Flags (4 bytes) - 0 for timestamp-based invalidation
                WriteUInt32(writer, 0);

                // 3. Timestamp (4 bytes)
                WriteUInt32(writer, mtime);

                // 4. Source size (4 bytes)
                WriteUInt32(writer, sourceSize);

                // 5. Marshalled code object
                var marshalledCode = PyMarshal.DumpCodeObject(codeObject);
                writer.Write(marshalledCode);
            }
        }

        /// <summary>
        /// Write a .pyc file (hash-based invalidation mode)
        /// CPython 3.12: Lib/importlib/_bootstrap_external.py:778-786 (_code_to_hash_pyc)
        ///
        /// .pyc file format (hash-based):
        ///   - Magic number (4 bytes): Python version identifier
        ///   - Flags (4 bytes): PEP 552 invalidation mode (0b1 = hash-based, 0b11 = checked hash)
        ///   - Source hash (8 bytes): First 8 bytes of SipHash-1-3 of source
        ///   - Marshalled code object (variable length)
        /// </summary>
        public static void WritePycFileWithHash(string pycPath, PyCodeObject codeObject, string sourcePath, bool checked_ = true)
        {
            // Create __pycache__ directory if it doesn't exist
            var directory = Path.GetDirectoryName(pycPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Compute source hash (simplified - using SHA256 instead of SipHash)
            var sourceBytes = File.ReadAllBytes(sourcePath);
            var sourceHash = ComputeSourceHash(sourceBytes);

            using (var fs = new FileStream(pycPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                // CPython 3.12: Lib/importlib/_bootstrap_external.py:780-785

                // 1. Magic number (4 bytes)
                writer.Write(MAGIC_NUMBER);

                // 2. Flags (4 bytes)
                // CPython 3.12: flags = 0b1 | checked << 1
                uint flags = 0b1 | (checked_ ? 0b10u : 0u);
                WriteUInt32(writer, flags);

                // 3. Source hash (8 bytes)
                writer.Write(sourceHash, 0, 8);

                // 4. Marshalled code object
                var marshalledCode = PyMarshal.DumpCodeObject(codeObject);
                writer.Write(marshalledCode);
            }
        }

        /// <summary>
        /// Compute source hash (first 8 bytes)
        /// CPython 3.12: Uses SipHash-1-3, but we'll use SHA256 for simplicity
        /// Reference: Lib/importlib/_bootstrap_external.py:165
        /// </summary>
        private static byte[] ComputeSourceHash(byte[] sourceBytes)
        {
            using (var sha256 = SHA256.Create())
            {
                var fullHash = sha256.ComputeHash(sourceBytes);
                var hash8 = new byte[8];
                Array.Copy(fullHash, hash8, 8);
                return hash8;
            }
        }

        /// <summary>
        /// Write a 32-bit unsigned integer in little-endian format
        /// CPython 3.12: Lib/importlib/_bootstrap_external.py:100-105 (_pack_uint32)
        /// </summary>
        private static void WriteUInt32(BinaryWriter writer, uint value)
        {
            writer.Write((byte)(value & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 24) & 0xFF));
        }
    }
}
