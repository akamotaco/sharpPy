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
        /// <summary>
        /// CPython has exactly one open object: _io.open is what builtins.open is set to, so
        /// `io.open is builtins.open` is True (measured). SharpPy used to construct two separate
        /// PyBuiltinFunction instances, one per module, which made that identity check False.
        /// </summary>
        public static readonly PyBuiltinFunction OpenFunction = new("open");

        // Bare type objects for name/repr parity (CPython Modules/_io). SharpPy's
        // open() never returns FileIO / BufferedRWPair instances directly, but the
        // classes must exist in the io namespace; matching their full C-level MRO
        // (_RawIOBase/_IOBase) is out of scope.
        public static readonly PyType FileIOType =
            new PyType("FileIO", new[] { PyType.ObjectType }, "_io");
        public static readonly PyType BufferedRWPairType =
            new PyType("BufferedRWPair", new[] { PyType.ObjectType }, "_io");

        // Native PyTypes resolve __module__ from their own TypeDict (see _sre), so
        // bind the literal alongside the Module field used by type_repr.
        static IoModule()
        {
            // The four abstract io bases live in this native module (Modules/_io/iobase.c);
            // registering them here means the types are complete even when _io itself is
            // never imported.
            PyIOBaseSlots.Register();
            PyStringIO.Register();
            PyBytesIO.Register();

            foreach (var t in new[] { FileIOType, BufferedRWPairType })
                t.TypeDict["__module__"] = PyString.From("_io");
            // The file types and UnsupportedOperation set their own __module__ at their
            // definition sites so they are correct even when _io is never imported.
        }

        public static PyModule CreateIoModule()
        {
            var module = new PyModule("_io");

            module.ModuleDict["TextIOWrapper"] = PyTextIOWrapper.WrapperType;
            module.ModuleDict["BufferedReader"] = PyBuffered.ReaderType;
            module.ModuleDict["BufferedWriter"] = PyBuffered.WriterType;
            module.ModuleDict["BufferedRandom"] = PyBuffered.RandomType;
            module.ModuleDict["FileIO"] = PyFileIO.FileIOType;
            module.ModuleDict["BufferedRWPair"] = PyBufferedRWPair.PairType;
            module.ModuleDict["IncrementalNewlineDecoder"] = PyNewlineDecoder.DecoderType;
            module.ModuleDict["UnsupportedOperation"] = PyUnsupportedOperation.UnsupportedOperationPyType;

            // CPython's abstract bases. Lib/io.py only adds abc.ABCMeta to these and virtually
            // registers the concrete classes, so every default implementation belongs here.
            module.ModuleDict["StringIO"] = PyStringIO.StringType;
            module.ModuleDict["BytesIO"] = PyBytesIO.BytesType;
            module.ModuleDict["_IOBase"] = PyIOBaseSlots.IOBaseType;
            module.ModuleDict["_RawIOBase"] = PyIOBaseSlots.RawIOBaseType;
            module.ModuleDict["_BufferedIOBase"] = PyIOBaseSlots.BufferedIOBaseType;
            module.ModuleDict["_TextIOBase"] = PyIOBaseSlots.TextIOBaseType;

            // Names Lib/io.py re-exports from _io; measured absent from _io before this.
            module.ModuleDict["DEFAULT_BUFFER_SIZE"] = PyInt.From(8192);
            module.ModuleDict["open"] = OpenFunction;
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
                return PyString.From("locale");
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
                return Open(new PyObject[] { pathStr, PyString.From("rb") }, null);
            });

            return module;
        }

        private static readonly string[] OpenNames = { "file", "mode", "buffering", "encoding", "errors", "newline", "closefd", "opener" };

        // Modules/_io/_iomodule.c _io_open_impl: FileIO, then a Buffered* over it, then a TextIOWrapper
        // over that; the descriptor table under FileIO is where Godot's files come in.
        internal static PyObject Open(PyObject[] args, PyDict kwargs)
        {
            if (args.Length > OpenNames.Length)
                throw PyTypeError.Create($"open() takes at most 8 arguments ({args.Length} given)");
            var values = new PyObject[OpenNames.Length];
            Array.Copy(args, values, args.Length);
            if (kwargs != null)
                foreach (var pair in kwargs.InternalDict)
                {
                    string name = (pair.Key as PyString)?.Value;
                    int position = Array.IndexOf(OpenNames, name);
                    if (position < 0)
                        throw PyTypeError.Create($"'{name}' is an invalid keyword argument for open()");
                    if (values[position] != null)
                        throw PyTypeError.Create($"argument for open() given by name ('{name}') and position ({position + 1})");
                    values[position] = pair.Value;
                }
            var file = values[0] ?? throw PyTypeError.Create("open() missing required argument 'file' (pos 1)");

            // The clinic converters, in order.
            string Text(int position, bool noneAllowed)
            {
                var value = values[position];
                if (value == null || (noneAllowed && value is PyNone))
                    return null;
                if (value is not PyString text)
                    throw PyTypeError.Create($"open() argument '{OpenNames[position]}' must be str{(noneAllowed ? " or None" : "")}, not {value.GetTypeName()}");
                if (text.Value.Contains('\0'))
                    throw PyValueError.Create("embedded null character");
                return text.Value;
            }
            string mode = Text(1, false) ?? "r";
            int buffering = -1;
            if (values[2] != null)
            {
                if (values[2] is PyFloat)
                    throw PyTypeError.Create("'float' object cannot be interpreted as an integer");
                var index = PyObject.TryGetIndex(values[2])
                    ?? throw PyTypeError.Create($"'{values[2].GetTypeName()}' object cannot be interpreted as an integer");
                if (index.Value < int.MinValue || index.Value > int.MaxValue)
                    throw PyOverflowError.Create(index.Value < 0 ? "signed integer is less than minimum" : "signed integer is greater than maximum");
                buffering = (int)index.Value;
            }
            string encoding = Text(3, true), errors = Text(4, true), newline = Text(5, true);
            bool closefd = values[6] == null || values[6].IsTrue();
            var opener = values[7] ?? PyNone.Instance;

            bool isNumber = file is PyFloat || PyObject.IndexCheck(file);
            PyObject pathOrFd = isNumber ? file : NtModule.FsPathObject(file);
            if (!isNumber && pathOrFd is not PyString && pathOrFd is not PyBytes)
                throw PyTypeError.Create($"invalid file: {file.ToRepr().Value}");

            bool creating = false, reading = false, writing = false, appending = false, updating = false, text = false, binary = false;
            for (int i = 0; i < mode.Length; i++)
            {
                char m = mode[i];
                switch (m)
                {
                    case 'x': creating = true; break;
                    case 'r': reading = true; break;
                    case 'w': writing = true; break;
                    case 'a': appending = true; break;
                    case '+': updating = true; break;
                    case 't': text = true; break;
                    case 'b': binary = true; break;
                    default: throw PyValueError.Create($"invalid mode: '{mode}'");
                }
                if (mode.IndexOf(m, i + 1) >= 0)
                    throw PyValueError.Create($"invalid mode: '{mode}'");
            }
            string rawmode = (creating ? "x" : "") + (reading ? "r" : "") + (writing ? "w" : "") + (appending ? "a" : "") + (updating ? "+" : "");
            if (text && binary)
                throw PyValueError.Create("can't have text and binary mode at once");
            if ((creating ? 1 : 0) + (reading ? 1 : 0) + (writing ? 1 : 0) + (appending ? 1 : 0) > 1)
                throw PyValueError.Create("must have exactly one of create/read/write/append mode");
            if (binary && encoding != null)
                throw PyValueError.Create("binary mode doesn't take an encoding argument");
            if (binary && errors != null)
                throw PyValueError.Create("binary mode doesn't take an errors argument");
            if (binary && newline != null)
                throw PyValueError.Create("binary mode doesn't take a newline argument");
            if (binary && buffering == 1)
                PyWarnings.Warn(PyType.RuntimeWarningType,
                    "line buffering (buffering=1) isn't supported in binary mode, the default buffer size will be used", 1);

            var raw = PyFileIO.FileIOType.Call(new[] { pathOrFd, PyString.From(rawmode), PyBool.FromBool(closefd), opener }, null);
            PyObject result = raw;
            try
            {
                bool isatty = false;
                if (buffering < 0)
                    isatty = raw.GetAttribute("isatty").Call(Array.Empty<PyObject>(), null).IsTrue();
                bool lineBuffering = false;
                if (buffering == 1 || isatty)
                {
                    buffering = -1;
                    lineBuffering = true;
                }
                if (buffering < 0)
                    buffering = (int)BytesMethods.AsSsize(raw.GetAttribute("_blksize"));
                if (buffering < 0)
                    throw PyValueError.Create("invalid buffering size");
                if (buffering == 0)
                {
                    if (!binary)
                        throw PyValueError.Create("can't have unbuffered text I/O");
                    return result;
                }
                PyType bufferedClass = updating ? PyBuffered.RandomType
                    : creating || writing || appending ? PyBuffered.WriterType
                    : reading ? PyBuffered.ReaderType
                    : throw PyValueError.Create($"unknown mode: '{mode}'");
                var buffer = bufferedClass.Call(new[] { raw, PyInt.From(buffering) }, null);
                result = buffer;
                if (binary)
                    return result;
                PyObject Optional(string value) => value == null ? PyNone.Instance : PyString.From(value);
                var wrapper = PyTextIOWrapper.WrapperType.Call(new[]
                {
                    buffer, Optional(encoding), Optional(errors), Optional(newline), PyBool.FromBool(lineBuffering),
                }, null);
                result = wrapper;
                wrapper.SetAttribute("mode", PyString.From(mode));
                return result;
            }
            catch (PythonException error)
            {
                try
                {
                    result.GetAttribute("close").Call(Array.Empty<PyObject>(), null);
                }
                catch (PythonException closing)
                {
                    closing.PyException.SetAttribute("__context__", error.PyException);
                    throw closing;
                }
                throw;
            }
        }
    }
}
