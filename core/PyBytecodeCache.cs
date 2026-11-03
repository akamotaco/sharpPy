using System;
using System.IO;

namespace SharpPy
{
    /// <summary>
    /// __pycache__ (Lib/importlib/_bootstrap_external.py SourceLoader.get_code). The file is what
    /// CPython writes: magic, flags, the source's mtime and size, then the marshalled code. SharpPy's
    /// bytecode and marshal are CPython 3.12's, so the format is the same; the tag in the name is
    /// SharpPy's own, so neither interpreter ever reads a file the other wrote.
    /// </summary>
    internal static class PyBytecodeCache
    {
        private const string Tag = "sharppy-312";
        // MAGIC_NUMBER of 3.12: (3531).to_bytes(2, 'little') + b'\r\n'
        private static readonly byte[] Magic = { 0xcb, 0x0d, 0x0d, 0x0a };
        private const int HeaderSize = 16;

        // cache_from_source
        private static string PathFor(string source)
        {
            string directory = Path.GetDirectoryName(source) ?? "";
            string name = Path.GetFileNameWithoutExtension(source);
            return Path.Combine(directory, "__pycache__", $"{name}.{Tag}.pyc");
        }

        private static bool Applies(string source) =>
            !Godot_IO_Path(source) && source.EndsWith(".py", StringComparison.Ordinal) && File.Exists(source);

        private static bool Godot_IO_Path(string source) =>
            source.StartsWith("res://", StringComparison.Ordinal) || source.StartsWith("user://", StringComparison.Ordinal);

        private static (uint MTime, uint Size) Stamp(string source)
        {
            var info = new FileInfo(source);
            long seconds = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
            return ((uint)seconds, (uint)info.Length);
        }

        // _validate_timestamp_pyc: the code of the cache when it was written from this source.
        internal static PyCodeObject TryLoad(string source)
        {
            try
            {
                if (!Applies(source))
                    return null;
                string cached = PathFor(source);
                if (!File.Exists(cached))
                    return null;
                var data = File.ReadAllBytes(cached);
                if (data.Length < HeaderSize || data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2] || data[3] != Magic[3])
                    return null;
                if (BitConverter.ToUInt32(data, 4) != 0)
                    return null;
                var (mtime, size) = Stamp(source);
                if (BitConverter.ToUInt32(data, 8) != mtime || BitConverter.ToUInt32(data, 12) != size)
                    return null;
                if (Modules.MarshalModule.Read(data, HeaderSize, data.Length - HeaderSize) is not PyCodeObject code)
                    return null;
                // _imp._fix_co_filename: the code names the file it was imported from now.
                code.FixFileName(source);
                return code;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or PythonException)
            {
                return null;
            }
        }

        // _code_to_timestamp_pyc and _write_atomic. A failure to write is not the import's failure.
        internal static void Store(string source, PyCodeObject code)
        {
            try
            {
                if (!Applies(source) || DontWrite())
                    return;
                string cached = PathFor(source);
                Directory.CreateDirectory(Path.GetDirectoryName(cached));
                var (mtime, size) = Stamp(source);
                var body = Modules.MarshalModule.Write(code, Modules.MarshalModule.Version);
                var data = new byte[HeaderSize + body.Length];
                Array.Copy(Magic, data, 4);
                BitConverter.GetBytes(mtime).CopyTo(data, 8);
                BitConverter.GetBytes(size).CopyTo(data, 12);
                Array.Copy(body, 0, data, HeaderSize, body.Length);
                string temporary = $"{cached}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.tmp";
                File.WriteAllBytes(temporary, data);
                File.Move(temporary, cached, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or PythonException)
            {
            }
        }

        // sys.dont_write_bytecode, which -B and PYTHONDONTWRITEBYTECODE set.
        private static bool DontWrite()
        {
            if (PyImportSystem.TryGetModule("sys", out var sys) && sys.ModuleDict.TryGetValue("dont_write_bytecode", out var flag))
                return flag.IsTrue();
            return false;
        }
    }
}
