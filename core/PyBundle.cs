using System;
using System.Collections.Generic;
using System.Text;

#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy
{
    /// <summary>
    /// A file of marshalled modules, put on sys.path as zipimport puts an archive there. Only the
    /// wrapping is SharpPy's: a module's code is marshal data, and FromTables rebuilds it as it does
    /// any other. The layout, all little-endian, is
    ///   "SPKB", format version, Python magic, flags, index length (five 32-bit words, the first text)
    ///   index: marshal of { name: (offset, length, is_package, source_hash) }
    ///   modules: marshal of each code object, offsets counted from the end of the index
    /// </summary>
    internal sealed class PyBundle
    {
        internal const string Extension = ".spk";
        private const string Mark = "SPKB";
        private const int FormatVersion = 1;
        private const int HeaderSize = 20;
        // PYC_MAGIC_NUMBER of 3.12: what the bytecode inside was made for.
        private const int PythonMagic = 3531;

        private static readonly Dictionary<string, PyBundle> Opened = new();

        private readonly struct Entry
        {
            internal readonly int Offset;
            internal readonly int Length;
            internal readonly bool IsPackage;

            internal Entry(int offset, int length, bool isPackage)
            {
                Offset = offset;
                Length = length;
                IsPackage = isPackage;
            }
        }

        private readonly Dictionary<string, Entry> _entries = new();
        private readonly byte[] _data;
        private readonly int _modulesStart;

        internal string Path { get; }

        internal static bool IsBundlePath(string path) => path.EndsWith(Extension, StringComparison.Ordinal);

        /// <summary>The bundle at this path, or null if there is no such file.</summary>
        internal static PyBundle Open(string path)
        {
            lock (Opened)
            {
                if (Opened.TryGetValue(path, out var known))
                    return known;
                if (!IOHelper.FileExists(path))
                    return null;
                return Opened[path] = new PyBundle(path, IOHelper.ReadAllBytes(path));
            }
        }

        private PyBundle(string path, byte[] data)
        {
            Path = path;
            _data = data;
            if (data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, Mark.Length) != Mark)
                throw PyImportError.Create($"not a SharpPy bundle: {path}");
            int format = BitConverter.ToInt32(data, 4);
            int magic = BitConverter.ToInt32(data, 8);
            if (format != FormatVersion)
                throw PyImportError.Create($"bundle format {format} is not {FormatVersion}: {path}");
            if (magic != PythonMagic)
                throw PyImportError.Create($"bundle was built for Python magic {magic}, not {PythonMagic}: {path}");
            int indexLength = BitConverter.ToInt32(data, 16);
            _modulesStart = HeaderSize + indexLength;
            var index = Modules.MarshalModule.Read(data, HeaderSize, indexLength) as PyDict;
            if (index == null)
                throw PyImportError.Create($"bad bundle index: {path}");
            foreach (var key in index.Keys().Items)
            {
                var entry = ((PyTuple)index.GetItem(key)).Items;
                _entries[((PyString)key).Value] = new Entry(entry[0].ToInt(), entry[1].ToInt(), entry[2].IsTrue());
            }
        }

        internal bool Contains(string moduleName) => _entries.ContainsKey(moduleName);

        internal bool IsPackage(string moduleName) => _entries[moduleName].IsPackage;

        internal PyCodeObject Code(string moduleName)
        {
            var entry = _entries[moduleName];
            return Modules.MarshalModule.Read(_data, _modulesStart + entry.Offset, entry.Length) as PyCodeObject
                ?? throw PyImportError.Create($"bad bundle entry for {moduleName}: {Path}");
        }
    }
}
