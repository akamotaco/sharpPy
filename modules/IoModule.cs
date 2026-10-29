using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// Native _io module.
    /// CPython 3.12: the concrete stream classes live in Modules/_io and Lib/io.py
    /// re-exports them (Lib/io.py: `from _io import ...`). SharpPy implements file
    /// streams as PyFileContextManager, whose concrete types (TextIOWrapper /
    /// BufferedReader / BufferedWriter / BufferedRandom) are bound here so that
    /// Lib/io.py can import the exact type objects. That keeps
    /// isinstance(open(f), io.TextIOWrapper) true and gives _io.__module__ repr parity.
    /// </summary>
    public static class IoModule
    {
        // Bare type objects for name/repr parity (CPython Modules/_io). SharpPy's
        // open() never returns FileIO / BufferedRWPair instances directly, but the
        // classes must exist in the io namespace; matching their full C-level MRO
        // (_RawIOBase/_IOBase) is out of scope.
        public static readonly PyType FileIOType =
            new PyType("FileIO", new[] { PyType.ObjectType }, "_io");
        public static readonly PyType BufferedRWPairType =
            new PyType("BufferedRWPair", new[] { PyType.ObjectType }, "_io");
        public static readonly PyType IncrementalNewlineDecoderType =
            new PyType("IncrementalNewlineDecoder", new[] { PyType.ObjectType }, "_io");

        // Native PyTypes resolve __module__ from their own TypeDict (see _sre), so
        // bind the literal alongside the Module field used by type_repr.
        static IoModule()
        {
            // The four abstract io bases live in this native module (Modules/_io/iobase.c);
            // registering them here means the types are complete even when _io itself is
            // never imported.
            PyIOBaseSlots.Register();
            PyStringIO.Register();

            foreach (var t in new[] { FileIOType, BufferedRWPairType, IncrementalNewlineDecoderType })
                t.TypeDict["__module__"] = new PyString("_io");
            // The file types and UnsupportedOperation set their own __module__ at their
            // definition sites so they are correct even when _io is never imported.
        }

        public static PyModule CreateIoModule()
        {
            var module = new PyModule("_io");

            module.ModuleDict["TextIOWrapper"] = PyFileContextManager.TextType;
            module.ModuleDict["BufferedReader"] = PyFileContextManager.ReaderType;
            module.ModuleDict["BufferedWriter"] = PyFileContextManager.WriterType;
            module.ModuleDict["BufferedRandom"] = PyFileContextManager.RandomType;
            module.ModuleDict["FileIO"] = FileIOType;
            module.ModuleDict["BufferedRWPair"] = BufferedRWPairType;
            module.ModuleDict["IncrementalNewlineDecoder"] = IncrementalNewlineDecoderType;
            module.ModuleDict["UnsupportedOperation"] = PyUnsupportedOperation.UnsupportedOperationPyType;

            // CPython's abstract bases. Lib/io.py only adds abc.ABCMeta to these and virtually
            // registers the concrete classes, so every default implementation belongs here.
            module.ModuleDict["StringIO"] = PyStringIO.StringType;
            module.ModuleDict["_IOBase"] = PyIOBaseSlots.IOBaseType;
            module.ModuleDict["_RawIOBase"] = PyIOBaseSlots.RawIOBaseType;
            module.ModuleDict["_BufferedIOBase"] = PyIOBaseSlots.BufferedIOBaseType;
            module.ModuleDict["_TextIOBase"] = PyIOBaseSlots.TextIOBaseType;

            // Names Lib/io.py re-exports from _io; measured absent from _io before this.
            module.ModuleDict["DEFAULT_BUFFER_SIZE"] = new PyInt(8192);
            module.ModuleDict["open"] = new PyBuiltinFunction("open");
            module.ModuleDict["BlockingIOError"] = PyType.BlockingIOErrorType;

            // CPython _io.text_encoding(encoding, /): positional-only.
            // Non-None encoding is returned unchanged; None resolves to the locale /
            // UTF-8-mode default. SharpPy ships no locale module, so it returns the
            // same marker the pinned reference interpreter reports for this host.
            module.ModuleDict["text_encoding"] = new PyBuiltinFunction("text_encoding", (args, kwargs) =>
            {
                if (args.Length < 1)
                    throw PyTypeError.Create($"text_encoding expected at least 1 argument, got {args.Length}");
                if (args.Length > 2)
                    throw PyTypeError.Create($"text_encoding expected at most 2 arguments, got {args.Length}");
                if (args[0] is not PyNone)
                    return args[0];
                return new PyString("locale");
            });

            // CPython _io.open_code(path, /): open a source file for the import
            // machinery; returns a binary stream.
            module.ModuleDict["open_code"] = new PyBuiltinFunction("open_code", (args, kwargs) =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"open_code() takes exactly one argument ({args.Length} given)");
                if (args[0] is not PyString pathStr)
                    throw PyTypeError.Create($"open_code() argument must be str, not {args[0].GetTypeName()}");
                if (pathStr.Value.Contains('\0'))
                    throw PyValueError.Create("embedded null character");
                return new PyFileContextManager(pathStr.Value, "rb");
            });

            return module;
        }
    }
}
