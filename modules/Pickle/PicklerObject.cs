using System;
using System.Collections.Generic;
using System.Numerics;
using static SharpPy.Modules.PickleModule;

namespace SharpPy.Modules
{
    /// <summary>
    /// PyMemoTable (Modules/_pickle.c): the pickler's memo, mapping an object (by identity) to its
    /// memo index. The C table is an open-addressing table keyed by the object's address; a CLR
    /// dictionary keyed by reference stands for it, and mt_allocated is kept by the same resize
    /// rule because __sizeof__ reports it.
    /// </summary>
    internal sealed class PyMemoTable
    {
        private const int MT_MINSIZE = 8;

        private readonly Dictionary<PyObject, long> _table = new(ReferenceEqualityComparer.Instance);
        // mt_allocated
        internal long Allocated = MT_MINSIZE;

        // PyMemoTable_New
        internal static PyMemoTable PyMemoTable_New() => new PyMemoTable();

        // PyMemoTable_Copy
        internal PyMemoTable PyMemoTable_Copy()
        {
            var copy = new PyMemoTable { Allocated = Allocated };
            foreach (var entry in _table)
                copy._table[entry.Key] = entry.Value;
            return copy;
        }

        // PyMemoTable_Size
        internal long PyMemoTable_Size() => _table.Count;

        // PyMemoTable_Clear: the entries go, the allocation stays.
        internal void PyMemoTable_Clear() => _table.Clear();

        // PyMemoTable_Get: false when the key is not in the table.
        internal bool PyMemoTable_Get(PyObject key, out long value) => _table.TryGetValue(key, out value);

        // PyMemoTable_Set
        internal void PyMemoTable_Set(PyObject key, long value)
        {
            if (_table.ContainsKey(key))
            {
                _table[key] = value;
                return;
            }
            _table[key] = value;
            long used = _table.Count;
            /* If we added a key, we can safely resize. Otherwise just return!
             * If used >= 2/3 size, adjust size. Normally, this quaduples the size.
             * Very large memo tables (over 50K items) use doubling instead. */
            if (used * 3 < Allocated * 2)
                return;
            _PyMemoTable_ResizeTable((used > 50000 ? 2 : 4) * used);
        }

        // _PyMemoTable_ResizeTable: the smallest power of two not below min_size.
        private void _PyMemoTable_ResizeTable(long minSize)
        {
            long newSize = MT_MINSIZE;
            while (newSize < minSize)
                newSize <<= 1;
            Allocated = newSize;
        }

        // The entries, as a walk over mt_table gives them.
        internal IEnumerable<KeyValuePair<PyObject, long>> Entries() => new List<KeyValuePair<PyObject, long>>(_table);
    }

    /// <summary>
    /// PicklerObject (Modules/_pickle.c): _pickle.Pickler, made from pickler_type_spec. Its tp_new is
    /// PyType_GenericNew, so a Pickler that __init__ has not run on has no memo, no output buffer and
    /// no write method.
    /// </summary>
    public sealed class PicklerObject : PyObject
    {
        internal static readonly PyType PicklerType = MakeType();

        internal PyMemoTable Memo;          // Memo table
        internal PyObject PersFunc;         // persistent_id() method, can be NULL
        internal PyObject PersFuncSelf;     // self if PersFunc is an unbound method, NULL otherwise
        internal PyObject DispatchTable;    // private dispatch_table, can be NULL
        internal PyObject ReducerOverride;  // hook for invoking user-defined callbacks
        internal PyObject Write;            // write() method of the output stream
        internal byte[] OutputBuffer;       // a local buffer written before flushing to the stream
        internal long OutputLen;            // Length of output_buffer
        internal long MaxOutputLen;         // Allocation size of output_buffer
        internal int Proto;                 // Pickle protocol number, >= 0
        internal int Bin;                   // Boolean, true if proto > 0
        internal int Framing;               // True when framing is enabled, proto >= 4
        internal long FrameStart;           // where the current frame begins, -1 if none
        internal long BufSize;
        internal int Fast;                  // fast mode: no memo
        internal int FastNesting;
        internal int FixImports;            // whether globals are renamed for Python 2.x
        internal PyDict FastMemo;
        internal PyObject BufferCallback;   // callback for out-of-band buffers, or NULL

        // PyType_GenericNew: every field zero.
        private PicklerObject(PyType type)
        {
            if (!ReferenceEquals(type, PicklerType))
                SetCustomType(type);
            FrameStart = 0;
        }

        // ob_type: Pickler, or the Python class that allocated the Pickler layout.
        public override PyType GetPyType() => _customType ?? PicklerType;

        private static PyType MakeType()
        {
            // pickler_type_spec: "_pickle.Pickler"
            var type = new PyType("Pickler", new[] { PyType.ObjectType }, "_pickle");
            // Py_tp_new: PyType_GenericNew, Py_tp_init: _pickle_Pickler___init__
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => new PicklerObject((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                ((PicklerObject)self)._pickle_Pickler___init__(args, kwargs);
                return PyNone.Instance;
            });
            // Pickler_methods
            PyArgs.Define(type, "dump", PyCall.Defining, (self, args) =>
                ((PicklerObject)self)._pickle_Pickler_dump_impl(args[0]), 1, 1);
            PyArgs.Define(type, "clear_memo", PyCall.NoArgs, (self, args) =>
                ((PicklerObject)self)._pickle_Pickler_clear_memo_impl());
            PyArgs.Define(type, "__sizeof__", PyCall.NoArgs, (self, args) =>
                PyInt.From(((PicklerObject)self)._pickle_Pickler___sizeof___impl()));
            // Pickler_members
            type.TypeDict["bin"] = new PyMemberDescriptor("bin", type, self => PyInt.From(((PicklerObject)self).Bin),
                (self, value) => ((PicklerObject)self).Bin = MemberInt(value),
                deleter: self => throw PyTypeError.Create("can't delete numeric/char attribute"));
            type.TypeDict["fast"] = new PyMemberDescriptor("fast", type, self => PyInt.From(((PicklerObject)self).Fast),
                (self, value) => ((PicklerObject)self).Fast = MemberInt(value),
                deleter: self => throw PyTypeError.Create("can't delete numeric/char attribute"));
            type.TypeDict["dispatch_table"] = new PyMemberDescriptor("dispatch_table", type,
                self => ((PicklerObject)self).DispatchTable
                    ?? throw PyAttributeError.Create($"'{self.GetTypeName()}' object has no attribute 'dispatch_table'"),
                (self, value) => ((PicklerObject)self).DispatchTable = value,
                deleter: self =>
                {
                    var pickler = (PicklerObject)self;
                    if (pickler.DispatchTable == null)
                        throw PyAttributeError.Create("dispatch_table");
                    pickler.DispatchTable = null;
                });
            // Pickler_getsets
            type.TypeDict["memo"] = new PyGetSetDescriptor("memo", type,
                self => ((PicklerObject)self).Pickler_get_memo(),
                (self, value) => ((PicklerObject)self).Pickler_set_memo(value),
                deleter: self => ((PicklerObject)self).Pickler_set_memo(null));
            type.TypeDict["persistent_id"] = new PyGetSetDescriptor("persistent_id", type,
                self => ((PicklerObject)self).Pickler_get_persid(),
                (self, value) => ((PicklerObject)self).Pickler_set_persid(value),
                deleter: self => ((PicklerObject)self).Pickler_set_persid(null));
            return type;
        }

        // PyMember_SetOne for a T_INT member: PyLong_AsLong, then the int it is cut to.
        internal static int MemberInt(PyObject value)
        {
            long longVal = PyArgs.CLong(value);
            if (longVal > int.MaxValue || longVal < int.MinValue)
                PyWarnings.Warn(PyType.RuntimeWarningType, "Truncation of value to int", 1);
            return unchecked((int)longVal);
        }

        // _Pickler_ClearBuffer
        internal void _Pickler_ClearBuffer()
        {
            OutputBuffer = NewBuffer(MaxOutputLen);
            OutputLen = 0;
            FrameStart = -1;
        }

        // PyBytes_FromStringAndSize(NULL, n): the CLR cannot hold an array past Array.MaxLength.
        private static byte[] NewBuffer(long size)
        {
            if (size > Array.MaxLength)
                throw PyMemoryError.Create();
            return new byte[size];
        }

        // _write_size64
        private static void _write_size64(Span<byte> output, ulong value)
        {
            for (int i = 0; i < 8; i++)
                output[i] = (byte)((value >> (8 * i)) & 0xff);
        }

        // _Pickler_CommitFrame
        internal void _Pickler_CommitFrame()
        {
            if (Framing == 0 || FrameStart == -1)
                return;
            long frameLen = OutputLen - FrameStart - FRAME_HEADER_SIZE;
            var qdata = OutputBuffer.AsSpan((int)FrameStart);
            if (frameLen >= FRAME_SIZE_MIN)
            {
                qdata[0] = FRAME;
                _write_size64(qdata.Slice(1), (ulong)frameLen);
            }
            else
            {
                qdata.Slice(FRAME_HEADER_SIZE, (int)frameLen).CopyTo(qdata);
                OutputLen -= FRAME_HEADER_SIZE;
            }
            FrameStart = -1;
        }

        // _Pickler_GetString
        internal PyBytes _Pickler_GetString()
        {
            _Pickler_CommitFrame();
            var output = OutputBuffer;
            OutputBuffer = null;
            // Resize down to exact size
            return PyBytes.From(output.AsSpan(0, (int)OutputLen).ToArray());
        }

        // _Pickler_FlushToFile
        internal void _Pickler_FlushToFile()
        {
            /* This will commit the frame first */
            var output = _Pickler_GetString();
            _Pickle_FastCall(Write, output);
        }

        // _Pickler_OpcodeBoundary
        private void _Pickler_OpcodeBoundary()
        {
            if (Framing == 0 || FrameStart == -1)
                return;
            long frameLen = OutputLen - FrameStart - FRAME_HEADER_SIZE;
            if (frameLen >= FRAME_SIZE_TARGET)
            {
                _Pickler_CommitFrame();
                /* Flush the content of the committed frame to the underlying
                 * file and reuse the pickler buffer for the next frame so as
                 * to limit memory usage when dumping large complex objects to
                 * a file.
                 *
                 * self->write is NULL when called via dumps. */
                if (Write != null)
                {
                    _Pickler_FlushToFile();
                    _Pickler_ClearBuffer();
                }
            }
        }

        // _Pickler_Write
        internal void _Pickler_Write(ReadOnlySpan<byte> s)
        {
            int dataLen = s.Length;
            bool needNewFrame = Framing != 0 && FrameStart == -1;
            long n = needNewFrame ? dataLen + FRAME_HEADER_SIZE : dataLen;
            long required = OutputLen + n;
            if (required > MaxOutputLen)
            {
                /* Make place in buffer for the pickle chunk */
                if (OutputLen >= long.MaxValue / 2 - n)
                    throw PyMemoryError.Create();
                MaxOutputLen = (OutputLen + n) / 2 * 3;
                var resized = NewBuffer(MaxOutputLen);
                OutputBuffer.AsSpan(0, (int)OutputLen).CopyTo(resized);
                OutputBuffer = resized;
            }
            var buffer = OutputBuffer;
            if (needNewFrame)
            {
                /* Setup new frame */
                long frameStart = OutputLen;
                FrameStart = frameStart;
                for (int i = 0; i < FRAME_HEADER_SIZE; i++)
                    /* Write an invalid value, for debugging */
                    buffer[frameStart + i] = 0xFE;
                OutputLen += FRAME_HEADER_SIZE;
            }
            s.CopyTo(buffer.AsSpan((int)OutputLen));
            OutputLen += dataLen;
        }

        private void _Pickler_Write(byte op) => _Pickler_Write(stackalloc byte[] { op });

        // _Pickler_New
        internal static PicklerObject _Pickler_New()
        {
            var self = new PicklerObject(PicklerType)
            {
                Memo = PyMemoTable.PyMemoTable_New(),
                MaxOutputLen = WRITE_BUF_SIZE,
                OutputLen = 0,
                FrameStart = -1,
            };
            self.OutputBuffer = new byte[WRITE_BUF_SIZE];
            return self;
        }

        // _Pickler_SetProtocol
        internal void _Pickler_SetProtocol(PyObject protocol, bool fixImports)
        {
            long proto;
            if (protocol is PyNone)
            {
                proto = DEFAULT_PROTOCOL;
            }
            else
            {
                proto = PyArgs.CLong(protocol);
                if (proto < 0)
                    proto = HIGHEST_PROTOCOL;
                else if (proto > HIGHEST_PROTOCOL)
                    throw PyValueError.Create($"pickle protocol must be <= {HIGHEST_PROTOCOL}");
            }
            Proto = (int)proto;
            Bin = proto > 0 ? 1 : 0;
            FixImports = fixImports && proto < 3 ? 1 : 0;
        }

        // _Pickler_SetOutputStream
        internal void _Pickler_SetOutputStream(PyObject file)
        {
            Write = LookupAttr(file, "write");
            if (Write == null)
                throw PyTypeError.Create("file must have a 'write' attribute");
        }

        // _Pickler_SetBufferCallback
        internal void _Pickler_SetBufferCallback(PyObject bufferCallback)
        {
            if (bufferCallback is PyNone)
                bufferCallback = null;
            if (bufferCallback != null && Proto < 5)
                throw PyValueError.Create("buffer_callback needs protocol >= 5");
            BufferCallback = bufferCallback;
        }

        // memo_get: generate a GET opcode for an object stored in the memo.
        private void memo_get(PyObject key)
        {
            if (!Memo.PyMemoTable_Get(key, out long value))
                throw PyKeyError.Create(key);
            Span<byte> pdata = stackalloc byte[30];
            int len;
            if (Bin == 0)
            {
                var text = "g" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
                len = Ascii(text, pdata);
            }
            else if (value < 256)
            {
                pdata[0] = BINGET;
                pdata[1] = (byte)(value & 0xff);
                len = 2;
            }
            else if ((ulong)value <= 0xffffffffUL)
            {
                pdata[0] = LONG_BINGET;
                pdata[1] = (byte)(value & 0xff);
                pdata[2] = (byte)((value >> 8) & 0xff);
                pdata[3] = (byte)((value >> 16) & 0xff);
                pdata[4] = (byte)((value >> 24) & 0xff);
                len = 5;
            }
            else
            { /* unlikely */
                throw Error(PicklingError, "memo id too large for LONG_BINGET");
            }
            _Pickler_Write(pdata.Slice(0, len));
        }

        // The bytes of an ASCII text, written into a buffer.
        private static int Ascii(string text, Span<byte> into)
        {
            for (int i = 0; i < text.Length; i++)
                into[i] = (byte)text[i];
            return text.Length;
        }

        private void WriteAscii(string text)
        {
            var bytes = new byte[text.Length];
            Ascii(text, bytes);
            _Pickler_Write(bytes);
        }

        /// <summary>
        /// memo_put: store an object in the memo, assign it a new unique ID based on the number of
        /// objects currently stored in the memo and generate a PUT opcode.
        /// </summary>
        private void memo_put(PyObject obj)
        {
            if (Fast != 0)
                return;
            long idx = Memo.PyMemoTable_Size();
            Memo.PyMemoTable_Set(obj, idx);
            if (Proto >= 4)
            {
                _Pickler_Write(MEMOIZE);
                return;
            }
            Span<byte> pdata = stackalloc byte[30];
            int len;
            if (Bin == 0)
            {
                len = Ascii("p" + idx.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n", pdata);
            }
            else if (idx < 256)
            {
                pdata[0] = BINPUT;
                pdata[1] = (byte)idx;
                len = 2;
            }
            else if ((ulong)idx <= 0xffffffffUL)
            {
                pdata[0] = LONG_BINPUT;
                pdata[1] = (byte)(idx & 0xff);
                pdata[2] = (byte)((idx >> 8) & 0xff);
                pdata[3] = (byte)((idx >> 16) & 0xff);
                pdata[4] = (byte)((idx >> 24) & 0xff);
                len = 5;
            }
            else
            { /* unlikely */
                throw Error(PicklingError, "memo id too large for LONG_BINPUT");
            }
            _Pickler_Write(pdata.Slice(0, len));
        }

        /* fast_save_enter() and fast_save_leave() are guards against recursive
           objects when Pickler is used with the "fast mode" (i.e., with object
           memoization disabled). If the nesting of a list or dict object exceed
           FAST_NESTING_LIMIT, these guards will start keeping an internal
           reference to the seen list or dict objects and check whether these objects
           are recursive. */
        // fast_save_enter
        private void fast_save_enter(PyObject obj)
        {
            /* if fast_nesting < 0, we're doing an error exit. */
            if (++FastNesting >= FAST_NESTING_LIMIT)
            {
                FastMemo ??= new PyDict();
                var key = PyInt.From(PyObjectId.Of(obj));
                if (FastMemo.ContainsKey(key))
                {
                    // The error exit: fast_nesting -1, so no fast_save_leave on the way out deletes.
                    FastNesting = -1;
                    throw PyValueError.Create($"fast mode: can't pickle cyclic objects including object type {obj.GetTypeName()} at {PyObjectId.Pointer(obj)}");
                }
                FastMemo.SetItem(key, PyNone.Instance);
            }
        }

        // fast_save_leave
        private void fast_save_leave(PyObject obj)
        {
            if (FastNesting-- >= FAST_NESTING_LIMIT)
            {
                var key = PyInt.From(PyObjectId.Of(obj));
                FastMemo.DelItem(key);
            }
        }

        // save_none
        private void save_none(PyObject obj) => _Pickler_Write(NONE);

        // save_bool
        private void save_bool(PyObject obj)
        {
            if (Proto >= 2)
            {
                _Pickler_Write(ReferenceEquals(obj, PyBool.True) ? NEWTRUE : NEWFALSE);
            }
            else
            {
                /* These aren't opcodes -- they're ways to pickle bools before protocol 2
                 * so that unpicklers written before bools were introduced unpickle them
                 * as ints, but unpicklers after can recognize that bools were intended. */
                WriteAscii(ReferenceEquals(obj, PyBool.True) ? "I01\n" : "I00\n");
            }
        }

        // save_long
        private void save_long(PyObject obj)
        {
            var value = ((PyInt)obj).Value;
            // PyLong_AsLongAndOverflow, then: the result fits in a signed 4-byte integer.
            if (value >= int.MinValue && value <= int.MaxValue)
            {
                long val = (long)value;
                Span<byte> pdata = stackalloc byte[32];
                int len;
                if (Bin != 0)
                {
                    pdata[1] = (byte)(val & 0xff);
                    pdata[2] = (byte)((val >> 8) & 0xff);
                    pdata[3] = (byte)((val >> 16) & 0xff);
                    pdata[4] = (byte)((val >> 24) & 0xff);
                    if (pdata[4] != 0 || pdata[3] != 0)
                    {
                        pdata[0] = BININT;
                        len = 5;
                    }
                    else if (pdata[2] != 0)
                    {
                        pdata[0] = BININT2;
                        len = 3;
                    }
                    else
                    {
                        pdata[0] = BININT1;
                        len = 2;
                    }
                }
                else
                {
                    len = Ascii("I" + val.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n", pdata);
                }
                _Pickler_Write(pdata.Slice(0, len));
                return;
            }

            if (Proto >= 2)
            {
                /* Linear-time pickling. */
                int sign = value.Sign;
                if (sign == 0)
                {
                    _Pickler_Write(stackalloc byte[] { LONG1, 0 });
                    return;
                }
                long nbits = BigInteger.Abs(value).GetBitLength();
                /* How many bytes do we need?  There are nbits >> 3 full bytes of data, and nbits & 7
                 * leftover bits; we always grab an extra byte at the start, and cut it back later
                 * if possible. */
                long nbytes = (nbits >> 3) + 1;
                if (nbytes > 0x7fffffffL)
                    throw PyOverflowError.Create("int too large to pickle");
                // _PyLong_AsByteArray(little endian, signed) into nbytes
                var minimal = value.ToByteArray(isUnsigned: false, isBigEndian: false);
                var pdata = new byte[nbytes];
                byte fill = sign < 0 ? (byte)0xff : (byte)0;
                for (long i = 0; i < nbytes; i++)
                    pdata[i] = i < minimal.Length ? minimal[i] : fill;
                /* If the int is negative, this may be a byte more than
                 * needed.  This is so iff the MSB is all redundant sign
                 * bits. */
                if (sign < 0 && nbytes > 1 && pdata[nbytes - 1] == 0xff && (pdata[nbytes - 2] & 0x80) != 0)
                    nbytes--;
                Span<byte> header = stackalloc byte[5];
                int size;
                if (nbytes < 256)
                {
                    header[0] = LONG1;
                    header[1] = (byte)nbytes;
                    size = 2;
                }
                else
                {
                    header[0] = LONG4;
                    long n = nbytes;
                    for (int i = 1; i < 5; i++)
                    {
                        header[i] = (byte)(n & 0xff);
                        n >>= 8;
                    }
                    size = 5;
                }
                _Pickler_Write(header.Slice(0, size));
                _Pickler_Write(pdata.AsSpan(0, (int)nbytes));
            }
            else
            {
                /* proto < 2: write the repr and newline.  This is quadratic-time (in
                   the number of digits), in both directions.  We add a trailing 'L'
                   to the repr, for compatibility with Python 2.x. */
                var repr = obj.ToRepr();
                var text = UnicodeEncoders.EncodeUtf8(repr, null);
                _Pickler_Write(LONG);
                _Pickler_Write(text);
                _Pickler_Write(stackalloc byte[] { (byte)'L', (byte)'\n' });
            }
        }

        // save_float
        private void save_float(PyObject obj)
        {
            double x = ((PyFloat)obj).Value;
            if (Bin != 0)
            {
                Span<byte> pdata = stackalloc byte[9];
                pdata[0] = BINFLOAT;
                PyFloatPack.Pack8(x, pdata.Slice(1), false);
                _Pickler_Write(pdata);
            }
            else
            {
                _Pickler_Write(FLOAT);
                // PyOS_double_to_string(x, 'r', 0, Py_DTSF_ADD_DOT_0, NULL): float's repr.
                WriteAscii(new PyFloat(x).ToRepr().Value);
                _Pickler_Write((byte)'\n');
            }
        }

        /// <summary>
        /// _Pickler_write_bytes: perform direct write of the header and payload of the binary
        /// object. The large contiguous data is written directly into the underlying file object,
        /// bypassing the output_buffer of the Pickler, without a protocol 4 frame.
        /// </summary>
        private void _Pickler_write_bytes(ReadOnlySpan<byte> header, ReadOnlySpan<byte> data, PyObject payload)
        {
            bool bypassBuffer = data.Length >= FRAME_SIZE_TARGET;
            int framing = Framing;
            if (bypassBuffer)
            {
                /* Commit the previous frame. */
                _Pickler_CommitFrame();
                /* Disable framing temporarily */
                Framing = 0;
            }
            _Pickler_Write(header);
            if (bypassBuffer && Write != null)
            {
                /* Bypass the in-memory buffer to directly stream large data
                   into the underlying file object. */
                /* Dump the output buffer to the file. */
                _Pickler_FlushToFile();
                /* Stream write the payload into the file without going through the
                   output buffer. */
                payload ??= PyBytes.From(data.ToArray());
                Write.Call(new[] { payload }, null);
                /* Reinitialize the buffer for subsequent calls to _Pickler_Write. */
                _Pickler_ClearBuffer();
            }
            else
            {
                _Pickler_Write(data);
            }
            /* Re-enable framing for subsequent calls to _Pickler_Write. */
            Framing = framing;
        }

        // _save_bytes_data
        private void _save_bytes_data(PyObject obj, ReadOnlySpan<byte> data)
        {
            long size = data.Length;
            Span<byte> header = stackalloc byte[9];
            int len;
            if (size <= 0xff)
            {
                header[0] = SHORT_BINBYTES;
                header[1] = (byte)size;
                len = 2;
            }
            else if ((ulong)size <= 0xffffffffUL)
            {
                header[0] = BINBYTES;
                header[1] = (byte)(size & 0xff);
                header[2] = (byte)((size >> 8) & 0xff);
                header[3] = (byte)((size >> 16) & 0xff);
                header[4] = (byte)((size >> 24) & 0xff);
                len = 5;
            }
            else if (Proto >= 4)
            {
                header[0] = BINBYTES8;
                _write_size64(header.Slice(1), (ulong)size);
                len = 9;
            }
            else
            {
                throw PyOverflowError.Create("serializing a bytes object larger than 4 GiB requires pickle protocol 4 or higher");
            }
            _Pickler_write_bytes(header.Slice(0, len), data, obj);
            memo_put(obj);
        }

        // save_bytes
        private void save_bytes(PyObject obj)
        {
            var bytes = ((PyBytes)obj).Value;
            if (Proto < 3)
            {
                /* Older pickle protocols do not have an opcode for pickling bytes
                   objects. Therefore, we need to fake the copy protocol (i.e.,
                   the __reduce__ method) to permit bytes object unpickling.
                   Here we use a hack to be compatible with Python 2: codecs.encode
                   makes the appropriate 'str' object when unpickled using Python 2
                   *and* the appropriate 'bytes' object when unpickled using Python 3. */
                PyObject reduceValue;
                if (bytes.Length == 0)
                {
                    reduceValue = PyTuple.From(PyType.BytesType, PyTuple.From());
                }
                else
                {
                    var unicodeStr = UnicodeDecoders.DecodeLatin1(bytes);
                    reduceValue = PyTuple.From(CodecsEncode, PyTuple.From(unicodeStr, PyString.InternFrom("latin1")));
                }
                /* save_reduce() will memoize the object automatically. */
                save_reduce(reduceValue, obj);
            }
            else
            {
                _save_bytes_data(obj, bytes);
            }
        }

        // _save_bytearray_data
        private void _save_bytearray_data(PyObject obj, ReadOnlySpan<byte> data)
        {
            Span<byte> header = stackalloc byte[9];
            header[0] = BYTEARRAY8;
            _write_size64(header.Slice(1), (ulong)data.Length);
            _Pickler_write_bytes(header, data, obj);
            memo_put(obj);
        }

        // save_bytearray
        private void save_bytearray(PyObject obj)
        {
            var array = (PyByteArray)obj;
            if (Proto < 5)
            {
                /* Older pickle protocols do not have an opcode for pickling bytearrays. */
                PyObject reduceValue;
                if (array.Length() == 0)
                    reduceValue = PyTuple.From(PyType.BytearrayType, PyTuple.From());
                else
                    reduceValue = PyTuple.From(PyType.BytearrayType, PyTuple.From(PyBytes.From(array.ToByteArrayCopy())));
                /* save_reduce() will memoize the object automatically. */
                save_reduce(reduceValue, obj);
            }
            else
            {
                _save_bytearray_data(obj, array.ToByteArrayCopy());
            }
        }

        // save_picklebuffer
        private void save_picklebuffer(PyObject obj)
        {
            if (Proto < 5)
                throw Error(PicklingError, "PickleBuffer can only be pickled with protocol >= 5");
            var view = PickleBufferObject.PyPickleBuffer_GetBuffer(obj);
            if (!view.IsCContiguous)
                throw Error(PicklingError, "PickleBuffer can not be pickled when pointing to a non-contiguous buffer");
            bool inBand = true;
            if (BufferCallback != null)
            {
                var ret = BufferCallback.Call(new[] { obj }, null);
                inBand = ret.IsTrue();
            }
            if (inBand)
            {
                /* Write data in-band */
                if (view.ReadOnly)
                    _save_bytes_data(obj, view.Data);
                else
                    _save_bytearray_data(obj, view.Data);
            }
            else
            {
                /* Write data out-of-band */
                _Pickler_Write(NEXT_BUFFER);
                if (view.ReadOnly)
                    _Pickler_Write(READONLY_BUFFER);
            }
        }

        private const string Hexdigits = "0123456789abcdef";

        /// <summary>
        /// raw_unicode_escape: a copy of PyUnicode_AsRawUnicodeEscapeString() that also translates
        /// backslash and newline characters to \uXXXX escapes.
        /// </summary>
        private static byte[] raw_unicode_escape(PyString obj)
        {
            var p = new List<byte>(obj.CodePointLength());
            foreach (int ch in obj.CodePoints())
            {
                /* Map 32-bit characters to '\Uxxxxxxxx' */
                if (ch >= 0x10000)
                {
                    p.Add((byte)'\\');
                    p.Add((byte)'U');
                    for (int shift = 28; shift >= 0; shift -= 4)
                        p.Add((byte)Hexdigits[(ch >> shift) & 0xf]);
                }
                /* Map 16-bit characters, '\\' and '\n' to '\uxxxx' */
                else if (ch >= 256 || ch == '\\' || ch == 0 || ch == '\n' || ch == '\r' || ch == 0x1a)
                {
                    p.Add((byte)'\\');
                    p.Add((byte)'u');
                    for (int shift = 12; shift >= 0; shift -= 4)
                        p.Add((byte)Hexdigits[(ch >> shift) & 0xf]);
                }
                /* Copy everything else as-is */
                else
                {
                    p.Add((byte)ch);
                }
            }
            return p.ToArray();
        }

        // Whether a str has a lone surrogate, which PyUnicode_AsUTF8AndSize refuses.
        private static bool HasSurrogate(PyString text)
        {
            foreach (int ch in text.CodePoints())
                if (ch >= 0xD800 && ch <= 0xDFFF)
                    return true;
            return false;
        }

        // write_unicode_binary
        private void write_unicode_binary(PyString obj)
        {
            PyObject encoded = null;
            byte[] data;
            if (!HasSurrogate(obj))
            {
                data = UnicodeEncoders.EncodeUtf8(obj, null);
            }
            else
            {
                /* Issue #8383: for strings with lone surrogates, fallback on the
                   "surrogatepass" error handler. */
                data = UnicodeEncoders.EncodeUtf8(obj, "surrogatepass");
                encoded = PyBytes.From(data);
            }
            long size = data.Length;
            Span<byte> header = stackalloc byte[9];
            int len;
            if (size <= 0xff && Proto >= 4)
            {
                header[0] = SHORT_BINUNICODE;
                header[1] = (byte)(size & 0xff);
                len = 2;
            }
            else if ((ulong)size <= 0xffffffffUL)
            {
                header[0] = BINUNICODE;
                header[1] = (byte)(size & 0xff);
                header[2] = (byte)((size >> 8) & 0xff);
                header[3] = (byte)((size >> 16) & 0xff);
                header[4] = (byte)((size >> 24) & 0xff);
                len = 5;
            }
            else if (Proto >= 4)
            {
                header[0] = BINUNICODE8;
                _write_size64(header.Slice(1), (ulong)size);
                len = 9;
            }
            else
            {
                throw PyOverflowError.Create("serializing a string larger than 4 GiB requires pickle protocol 4 or higher");
            }
            _Pickler_write_bytes(header.Slice(0, len), data, encoded);
        }

        // save_unicode
        private void save_unicode(PyObject obj)
        {
            if (Bin != 0)
            {
                write_unicode_binary((PyString)obj);
            }
            else
            {
                var encoded = raw_unicode_escape((PyString)obj);
                _Pickler_Write(UNICODE);
                _Pickler_Write(encoded);
                _Pickler_Write((byte)'\n');
            }
            memo_put(obj);
        }

        // store_tuple_elements: a helper for save_tuple, pushing the len elements of t on the stack.
        private void store_tuple_elements(PyTuple t, int len)
        {
            for (int i = 0; i < len; i++)
                save(t.Items[i], 0);
        }

        /* Tuples are ubiquitous in the pickle protocols, so many techniques are
         * used across protocols to minimize the space needed to pickle them.
         * Tuples are also the only builtin immutable type that can be recursive
         * (a tuple can be reached from itself), and that requires some subtle
         * magic so that it works in all cases.  IOW, this is a long routine. */
        // save_tuple
        private void save_tuple(PyObject obj)
        {
            var tuple = (PyTuple)obj;
            int len = tuple.Items.Length;
            byte[] len2opcode = { EMPTY_TUPLE, TUPLE1, TUPLE2, TUPLE3 };

            if (len == 0)
            {
                if (Proto != 0)
                    _Pickler_Write(EMPTY_TUPLE);
                else
                    _Pickler_Write(stackalloc byte[] { MARK, TUPLE });
                return;
            }

            /* The tuple isn't in the memo now.  If it shows up there after
             * saving the tuple elements, the tuple must be recursive, in
             * which case we'll pop everything we put on the stack, and fetch
             * its value from the memo. */
            if (len <= 3 && Proto >= 2)
            {
                /* Use TUPLE{1,2,3} opcodes. */
                store_tuple_elements(tuple, len);
                if (Memo.PyMemoTable_Get(obj, out _))
                {
                    /* pop the len elements */
                    for (int i = 0; i < len; i++)
                        _Pickler_Write(POP);
                    /* fetch from memo */
                    memo_get(obj);
                    return;
                }
                /* Not recursive. */
                _Pickler_Write(len2opcode[len]);
                memo_put(obj);
                return;
            }

            /* proto < 2 and len > 0, or proto >= 2 and len > 3.
             * Generate MARK e1 e2 ... TUPLE */
            _Pickler_Write(MARK);
            store_tuple_elements(tuple, len);
            if (Memo.PyMemoTable_Get(obj, out _))
            {
                /* pop the stack stuff we pushed */
                if (Bin != 0)
                {
                    _Pickler_Write(POP_MARK);
                }
                else
                {
                    /* Note that we pop one more than len, to remove the MARK too. */
                    for (int i = 0; i <= len; i++)
                        _Pickler_Write(POP);
                }
                /* fetch from memo */
                memo_get(obj);
                return;
            }
            /* Not recursive. */
            _Pickler_Write(TUPLE);
            memo_put(obj);
        }

        /* iter is an iterator giving items, and we batch up chunks of
         *     MARK item item ... item APPENDS
         * opcode sequences.  Calling code should have arranged to first create an
         * empty list, or list-like object, for the APPENDS to operate on. */
        // batch_list
        private void batch_list(PyObject iter)
        {
            PyObject obj;
            if (Proto == 0)
            {
                /* APPENDS isn't available; do one at a time. */
                for (; ; )
                {
                    obj = PyIter_Next(iter);
                    if (obj == null)
                        break;
                    save(obj, 0);
                    _Pickler_Write(APPEND);
                }
                return;
            }

            /* proto > 0:  write in batches of BATCHSIZE. */
            int n;
            do
            {
                /* Get first item */
                var firstitem = PyIter_Next(iter);
                if (firstitem == null)
                    /* nothing more to add */
                    break;

                /* Try to get a second item */
                obj = PyIter_Next(iter);
                if (obj == null)
                {
                    /* Only one item to write */
                    save(firstitem, 0);
                    _Pickler_Write(APPEND);
                    break;
                }

                /* More than one item to write */

                /* Pump out MARK, items, APPENDS. */
                _Pickler_Write(MARK);
                save(firstitem, 0);
                n = 1;

                /* Fetch and save up to BATCHSIZE items */
                while (obj != null)
                {
                    save(obj, 0);
                    n += 1;
                    if (n == BATCHSIZE)
                        break;
                    obj = PyIter_Next(iter);
                }
                _Pickler_Write(APPENDS);
            } while (n == BATCHSIZE);
        }

        /* This is a variant of batch_list() above, specialized for lists (with no
         * support for list subclasses). Like batch_list(), we batch up chunks of
         *     MARK item item ... item APPENDS
         * opcode sequences.  Note that this only works for protocols > 0. */
        // batch_list_exact
        private void batch_list_exact(PyList obj)
        {
            if (obj.Len() == 1)
            {
                var item = obj.RawGet(0);
                save(item, 0);
                _Pickler_Write(APPEND);
                return;
            }

            /* Write in batches of BATCHSIZE. */
            long total = 0;
            do
            {
                int thisBatch = 0;
                _Pickler_Write(MARK);
                while (total < obj.Len())
                {
                    var item = obj.RawGet((int)total);
                    save(item, 0);
                    total++;
                    if (++thisBatch == BATCHSIZE)
                        break;
                }
                _Pickler_Write(APPENDS);
            } while (total < obj.Len());
        }

        // save_list
        private void save_list(PyObject obj)
        {
            bool entered = false;
            try
            {
                if (Fast != 0)
                {
                    entered = true;
                    fast_save_enter(obj);
                }

                /* Create an empty list. */
                if (Bin != 0)
                    _Pickler_Write(EMPTY_LIST);
                else
                    _Pickler_Write(stackalloc byte[] { MARK, LIST });

                /* Get list length, and bow out early if empty. */
                long len = obj.Len();
                memo_put(obj);

                if (len != 0)
                {
                    /* Materialize the list elements. */
                    if (obj.IsExactly(PyType.ListType) && Proto > 0)
                    {
                        PyCRecursion.Enter(" while pickling an object");
                        try { batch_list_exact((PyList)obj); }
                        finally { PyCRecursion.Leave(); }
                    }
                    else
                    {
                        var iter = obj.GetIterator();
                        PyCRecursion.Enter(" while pickling an object");
                        try { batch_list(iter); }
                        finally { PyCRecursion.Leave(); }
                    }
                }
            }
            finally
            {
                if (entered && Fast != 0)
                    fast_save_leave(obj);
            }
        }

        // The 2-tuple check of batch_dict.
        private static PyTuple DictItem(PyObject obj) =>
            obj is PyTuple tuple && tuple.Items.Length == 2
                ? tuple
                : throw PyTypeError.Create("dict items iterator must return 2-tuples");

        /* iter is an iterator giving (key, value) pairs, and we batch up chunks of
         *     MARK key value ... key value SETITEMS
         * opcode sequences.  Calling code should have arranged to first create an
         * empty dict, or dict-like object, for the SETITEMS to operate on. */
        // batch_dict
        private void batch_dict(PyObject iter)
        {
            PyObject obj;
            if (Proto == 0)
            {
                /* SETITEMS isn't available; do one at a time. */
                for (; ; )
                {
                    obj = PyIter_Next(iter);
                    if (obj == null)
                        break;
                    var pair = DictItem(obj);
                    save(pair.Items[0], 0);
                    save(pair.Items[1], 0);
                    _Pickler_Write(SETITEM);
                }
                return;
            }

            /* proto > 0:  write in batches of BATCHSIZE. */
            int n;
            do
            {
                /* Get first item */
                var firstitem = PyIter_Next(iter);
                if (firstitem == null)
                    /* nothing more to add */
                    break;
                var first = DictItem(firstitem);

                /* Try to get a second item */
                obj = PyIter_Next(iter);
                if (obj == null)
                {
                    /* Only one item to write */
                    save(first.Items[0], 0);
                    save(first.Items[1], 0);
                    _Pickler_Write(SETITEM);
                    break;
                }

                /* More than one item to write */

                /* Pump out MARK, items, SETITEMS. */
                _Pickler_Write(MARK);
                save(first.Items[0], 0);
                save(first.Items[1], 0);
                n = 1;

                /* Fetch and save up to BATCHSIZE items */
                while (obj != null)
                {
                    var pair = DictItem(obj);
                    save(pair.Items[0], 0);
                    save(pair.Items[1], 0);
                    n += 1;
                    if (n == BATCHSIZE)
                        break;
                    obj = PyIter_Next(iter);
                }
                _Pickler_Write(SETITEMS);
            } while (n == BATCHSIZE);
        }

        /* This is a variant of batch_dict() above that specializes for dicts, with no
         * support for dict subclasses. Note that this currently doesn't work for protocol 0. */
        // batch_dict_exact
        private void batch_dict_exact(PyDict obj)
        {
            long dictSize = obj.Len();
            int ppos = 0;
            PyObject key, value;

            /* Special-case len(d) == 1 to save space. */
            if (dictSize == 1)
            {
                obj.Next(ref ppos, out key, out value);
                save(key, 0);
                save(value, 0);
                _Pickler_Write(SETITEM);
                return;
            }

            /* Write in batches of BATCHSIZE. */
            int i;
            do
            {
                i = 0;
                _Pickler_Write(MARK);
                while (obj.Next(ref ppos, out key, out value))
                {
                    save(key, 0);
                    save(value, 0);
                    if (++i == BATCHSIZE)
                        break;
                }
                _Pickler_Write(SETITEMS);
                if (obj.Len() != dictSize)
                    throw PyRuntimeError.Create("dictionary changed size during iteration");
            } while (i == BATCHSIZE);
        }

        // save_dict
        private void save_dict(PyObject obj)
        {
            bool entered = false;
            try
            {
                if (Fast != 0)
                {
                    entered = true;
                    fast_save_enter(obj);
                }

                /* Create an empty dict. */
                if (Bin != 0)
                    _Pickler_Write(EMPTY_DICT);
                else
                    _Pickler_Write(stackalloc byte[] { MARK, DICT });

                memo_put(obj);

                if (((PyDict)obj).Len() != 0)
                {
                    /* Save the dict items. */
                    if (obj.IsExactly(PyType.DictType) && Proto > 0)
                    {
                        /* We can take certain shortcuts if we know this is a dict and
                           not a dict subclass. */
                        PyCRecursion.Enter(" while pickling an object");
                        try { batch_dict_exact((PyDict)obj); }
                        finally { PyCRecursion.Leave(); }
                    }
                    else
                    {
                        // PyObject_CallMethodNoArgs(obj, &_Py_ID(items))
                        var items = obj.GetAttribute("items").Call(Array.Empty<PyObject>(), null);
                        var iter = items.GetIterator();
                        PyCRecursion.Enter(" while pickling an object");
                        try { batch_dict(iter); }
                        finally { PyCRecursion.Leave(); }
                    }
                }
            }
            finally
            {
                if (entered && Fast != 0)
                    fast_save_leave(obj);
            }
        }

        // save_set
        private void save_set(PyObject obj)
        {
            if (Proto < 4)
            {
                // PySequence_List(obj)
                var items = new PyList(IterateItems(obj));
                var reduceValue = PyTuple.From(PyType.SetType, PyTuple.From(items));
                /* save_reduce() will memoize the object automatically. */
                save_reduce(reduceValue, obj);
                return;
            }

            _Pickler_Write(EMPTY_SET);
            memo_put(obj);

            var table = ((PySet)obj).Table;
            long setSize = table.Count;
            if (setSize == 0)
                return; /* nothing to do */

            /* Write in batches of BATCHSIZE. */
            // _PySet_NextEntry over the live table.
            using var entries = table.Entries().GetEnumerator();
            int i;
            do
            {
                i = 0;
                _Pickler_Write(MARK);
                while (entries.MoveNext())
                {
                    save(entries.Current.Key, 0);
                    if (++i == BATCHSIZE)
                        break;
                }
                _Pickler_Write(ADDITEMS);
                if (table.Count != setSize)
                    throw PyRuntimeError.Create("set changed size during iteration");
            } while (i == BATCHSIZE);
        }

        // save_frozenset
        private void save_frozenset(PyObject obj)
        {
            // fast_save_enter, without a fast_save_leave on any path, as the C code has it.
            if (Fast != 0)
                fast_save_enter(obj);

            if (Proto < 4)
            {
                var items = new PyList(IterateItems(obj));
                var reduceValue = PyTuple.From(PyType.FrozenSetType, PyTuple.From(items));
                /* save_reduce() will memoize the object automatically. */
                save_reduce(reduceValue, obj);
                return;
            }

            _Pickler_Write(MARK);
            var iter = obj.GetIterator();
            for (; ; )
            {
                var item = PyIter_Next(iter);
                if (item == null)
                    break;
                save(item, 0);
            }

            /* If the object is already in the memo, this means it is
               recursive. In this case, throw away everything we put on the
               stack, and fetch the object back from the memo. */
            if (Memo.PyMemoTable_Get(obj, out _))
            {
                _Pickler_Write(POP_MARK);
                memo_get(obj);
                return;
            }
            _Pickler_Write(FROZENSET);
            memo_put(obj);
        }

        // fix_imports
        private static void fix_imports(ref PyObject moduleName, ref PyObject globalName)
        {
            var key = PyTuple.From(moduleName, globalName);
            if (NameMapping3to2.TryGetStoredValue(key, out var item))
            {
                if (item is not PyTuple pair || pair.Items.Length != 2)
                    throw PyRuntimeError.Create($"_compat_pickle.REVERSE_NAME_MAPPING values should be 2-tuples, not {item.GetTypeName()}");
                var fixedModuleName = pair.Items[0];
                var fixedGlobalName = pair.Items[1];
                if (fixedModuleName is not PyString || fixedGlobalName is not PyString)
                    throw PyRuntimeError.Create($"_compat_pickle.REVERSE_NAME_MAPPING values should be pairs of str, not ({fixedModuleName.GetTypeName()}, {fixedGlobalName.GetTypeName()})");
                moduleName = fixedModuleName;
                globalName = fixedGlobalName;
                return;
            }

            if (ImportMapping3to2.TryGetStoredValue(moduleName, out item))
            {
                if (item is not PyString)
                    throw PyRuntimeError.Create($"_compat_pickle.REVERSE_IMPORT_MAPPING values should be strings, not {item.GetTypeName()}");
                moduleName = item;
            }
        }

        // PyUnicode_AsUTF8String or PyUnicode_AsASCIIString of a global's names.
        private static byte[] EncodeIdentifier(PyObject name, bool utf8)
        {
            if (name is not PyString text)
                throw PyTypeError.Create("bad argument type for built-in operation");
            return utf8 ? UnicodeEncoders.EncodeUtf8(text, null) : UnicodeEncoders.EncodeUcs1(text, null, 128);
        }

        // save_global
        private void save_global(PyObject obj, PyObject name)
        {
            PyObject globalName;
            if (name != null)
            {
                globalName = name;
            }
            else
            {
                globalName = LookupAttr(obj, "__qualname__") ?? obj.GetAttribute("__name__");
            }

            var dottedPath = get_dotted_path(null, globalName);
            var moduleName = whichmodule(obj, dottedPath);

            /* XXX: Change to use the import C API directly with level=0 to disallow
               relative imports. PyImport_Import is __import__, so custom import functions
               are seen, unlike pickle.py's. */
            PyObject module;
            try
            {
                module = PyImportSystem.PyImportImport(moduleName);
            }
            catch (PythonException)
            {
                throw Error(PicklingError, $"Can't pickle {obj.ToRepr().Value}: import of module {moduleName.ToRepr().Value} failed");
            }
            PyObject cls, parent;
            try
            {
                cls = get_deep_attribute(module, dottedPath, out parent);
            }
            catch (PythonException)
            {
                cls = null;
                parent = null;
            }
            if (cls == null)
                throw Error(PicklingError, $"Can't pickle {obj.ToRepr().Value}: attribute lookup {globalName.AsString().Value} on {moduleName.AsString().Value} failed");
            if (!ReferenceEquals(cls, obj))
                throw Error(PicklingError, $"Can't pickle {obj.ToRepr().Value}: it's not the same object as {moduleName.AsString().Value}.{globalName.AsString().Value}");

            if (Proto >= 2)
            {
                /* See whether this is in the extension registry, and if
                 * so generate an EXT opcode. */
                var extensionKey = PyTuple.From(moduleName, globalName);
                if (ExtensionRegistry.TryGetStoredValue(extensionKey, out var codeObj))
                {
                    long code = PyArgs.CLong(codeObj);
                    if (code <= 0 || code > 0x7fffffffL)
                    {
                        /* Should never happen in normal circumstances, since the type and
                           the value of the code are checked in copyreg.add_extension(). */
                        throw PyRuntimeError.Create($"extension code {code} is out of range");
                    }
                    /* Generate an EXT opcode. */
                    Span<byte> pdata = stackalloc byte[5];
                    int n;
                    if (code <= 0xff)
                    {
                        pdata[0] = EXT1;
                        pdata[1] = (byte)code;
                        n = 2;
                    }
                    else if (code <= 0xffff)
                    {
                        pdata[0] = EXT2;
                        pdata[1] = (byte)(code & 0xff);
                        pdata[2] = (byte)((code >> 8) & 0xff);
                        n = 3;
                    }
                    else
                    {
                        pdata[0] = EXT4;
                        pdata[1] = (byte)(code & 0xff);
                        pdata[2] = (byte)((code >> 8) & 0xff);
                        pdata[3] = (byte)((code >> 16) & 0xff);
                        pdata[4] = (byte)((code >> 24) & 0xff);
                        n = 5;
                    }
                    _Pickler_Write(pdata.Slice(0, n));
                    return;
                }
                /* The object is not registered in the extension registry.
                   This is the most likely code path. */
            }

            // gen_global:
            if (ReferenceEquals(parent, module))
            {
                globalName = dottedPath[dottedPath.Count - 1];
                dottedPath = null;
            }
            if (Proto >= 4)
            {
                save(moduleName, 0);
                save(globalName, 0);
                _Pickler_Write(STACK_GLOBAL);
            }
            else
            {
                /* Generate a normal global opcode if we are using a pickle
                   protocol < 4, or if the object is not registered in the
                   extension registry.

                   Objects with multi-part __qualname__ are represented as
                   getattr(getattr(..., attrname1), attrname2). */
                byte tupletwoOp = Proto < 2 ? TUPLE : TUPLE2;
                if (dottedPath != null)
                {
                    if (dottedPath.Count > 1)
                        globalName = dottedPath[0];
                    for (int i = 1; i < dottedPath.Count; i++)
                    {
                        save(Getattr, 0);
                        if (Proto < 2)
                            _Pickler_Write(MARK);
                    }
                }

                _Pickler_Write(GLOBAL);

                /* For protocol < 3 and if the user didn't request against doing
                   so, we convert module names to the old 2.x module names. */
                if (Proto < 3 && FixImports != 0)
                    fix_imports(ref moduleName, ref globalName);

                /* Since Python 3.0 now supports non-ASCII identifiers, we encode
                   both the module name and the global name using UTF-8. We do so
                   only when we are using the pickle protocol newer than version
                   3. This is to ensure compatibility with older Unpickler running
                   on Python 2.x. */
                bool utf8 = Proto == 3;
                byte[] encoded;
                try
                {
                    encoded = EncodeIdentifier(moduleName, utf8);
                }
                catch (PythonException error) when (error.PyException is PyUnicodeEncodeError)
                {
                    throw Error(PicklingError, $"can't pickle module identifier '{moduleName.AsString().Value}' using pickle protocol {Proto}");
                }
                _Pickler_Write(encoded);
                _Pickler_Write((byte)'\n');

                /* Save the name of the module. */
                try
                {
                    encoded = EncodeIdentifier(globalName, utf8);
                }
                catch (PythonException error) when (error.PyException is PyUnicodeEncodeError)
                {
                    throw Error(PicklingError, $"can't pickle global identifier '{globalName.AsString().Value}' using pickle protocol {Proto}");
                }
                _Pickler_Write(encoded);
                _Pickler_Write((byte)'\n');

                if (dottedPath != null)
                {
                    for (int i = 1; i < dottedPath.Count; i++)
                    {
                        save(dottedPath[i], 0);
                        _Pickler_Write(tupletwoOp);
                        _Pickler_Write(REDUCE);
                    }
                }
            }
            /* Memoize the object. */
            memo_put(obj);
        }

        // save_singleton_type
        private void save_singleton_type(PyObject obj, PyObject singleton)
        {
            var reduceValue = PyTuple.From(PyType.TypeType, PyTuple.From(singleton));
            save_reduce(reduceValue, obj);
        }

        // save_type
        private void save_type(PyObject obj)
        {
            if (ReferenceEquals(obj, PyType.NoneType))
                save_singleton_type(obj, PyNone.Instance);
            else if (ReferenceEquals(obj, PyType.EllipsisType))
                save_singleton_type(obj, PyEllipsis.Instance);
            else if (ReferenceEquals(obj, PyType.NotImplementedType))
                save_singleton_type(obj, PyNotImplemented.Instance);
            else
                save_global(obj, null);
        }

        /// <summary>
        /// save_pers: false if it did nothing (the persistent id was None), true if a persistent id
        /// was saved.
        /// </summary>
        private bool save_pers(PyObject obj)
        {
            var pid = call_method(PersFunc, PersFuncSelf, obj);
            if (pid is PyNone)
                return false;
            if (Bin != 0)
            {
                save(pid, 1);
                _Pickler_Write(BINPERSID);
            }
            else
            {
                var pidStr = pid.AsString();
                /* XXX: Should it check whether the pid contains embedded newlines? */
                if (pidStr.MaxChar() >= 0x80)
                    throw Error(PicklingError, "persistent IDs in protocol 0 must be ASCII strings");
                _Pickler_Write(PERSID);
                WriteAscii(pidStr.Value);
                _Pickler_Write((byte)'\n');
            }
            return true;
        }

        // get_class
        private static PyObject get_class(PyObject obj) => LookupAttr(obj, "__class__") ?? obj.GetPyType();

        /* We're saving obj, and args is the 2-thru-5 tuple returned by the
         * appropriate __reduce__ method for obj. */
        // save_reduce
        private void save_reduce(PyObject args, PyObject obj)
        {
            if (args is not PyTuple argsTuple || argsTuple.Items.Length < 2 || argsTuple.Items.Length > 6)
                throw Error(PicklingError, "tuple returned by __reduce__ must contain 2 through 6 elements");

            var items = argsTuple.Items;
            var callable = items[0];
            var argtup = items[1];
            PyObject state = items.Length > 2 ? items[2] : null;
            PyObject listitems = items.Length > 3 ? items[3] : PyNone.Instance;
            PyObject dictitems = items.Length > 4 ? items[4] : PyNone.Instance;
            PyObject stateSetter = items.Length > 5 ? items[5] : PyNone.Instance;
            bool useNewobj = false, useNewobjEx = false;

            if (!callable.IsCallable())
                throw Error(PicklingError, "first item of the tuple returned by __reduce__ must be callable");
            if (argtup is not PyTuple)
                throw Error(PicklingError, "second item of the tuple returned by __reduce__ must be a tuple");

            if (state is PyNone)
                state = null;

            if (listitems is PyNone)
                listitems = null;
            else if (!listitems.IsIterator)
                throw Error(PicklingError, $"fourth element of the tuple returned by __reduce__ must be an iterator, not {listitems.GetTypeName()}");

            if (dictitems is PyNone)
                dictitems = null;
            else if (!dictitems.IsIterator)
                throw Error(PicklingError, $"fifth element of the tuple returned by __reduce__ must be an iterator, not {dictitems.GetTypeName()}");

            if (stateSetter is PyNone)
                stateSetter = null;
            else if (!stateSetter.IsCallable())
                throw Error(PicklingError, $"sixth element of the tuple returned by __reduce__ must be a function, not {stateSetter.GetTypeName()}");

            if (Proto >= 2)
            {
                var name = LookupAttr(callable, "__name__");
                if (name is PyString nameText)
                {
                    useNewobjEx = nameText.Value == "__newobj_ex__";
                    if (!useNewobjEx)
                        useNewobj = nameText.Value == "__newobj__";
                }
            }

            var argItems = ((PyTuple)argtup).Items;
            if (useNewobjEx)
            {
                if (argItems.Length != 3)
                    throw Error(PicklingError, $"length of the NEWOBJ_EX argument tuple must be exactly 3, not {argItems.Length}");
                var cls = argItems[0];
                if (cls is not PyType)
                    throw Error(PicklingError, $"first item from NEWOBJ_EX argument tuple must be a class, not {cls.GetTypeName()}");
                var newArgs = argItems[1];
                if (newArgs is not PyTuple newArgsTuple)
                    throw Error(PicklingError, $"second item from NEWOBJ_EX argument tuple must be a tuple, not {newArgs.GetTypeName()}");
                var kwargs = argItems[2];
                if (kwargs is not PyDict kwargsDict)
                    throw Error(PicklingError, $"third item from NEWOBJ_EX argument tuple must be a dict, not {kwargs.GetTypeName()}");

                if (Proto >= 4)
                {
                    save(cls, 0);
                    save(newArgs, 0);
                    save(kwargs, 0);
                    _Pickler_Write(NEWOBJ_EX);
                }
                else
                {
                    var newargs = new PyObject[newArgsTuple.Items.Length + 2];
                    newargs[0] = cls.GetAttribute("__new__");
                    newargs[1] = cls;
                    Array.Copy(newArgsTuple.Items, 0, newargs, 2, newArgsTuple.Items.Length);
                    var partialCallable = Partial.Call(newargs, kwargsDict);
                    save(partialCallable, 0);
                    save(PyTuple.From(), 0);
                    _Pickler_Write(REDUCE);
                }
            }
            else if (useNewobj)
            {
                /* Sanity checks. */
                if (argItems.Length < 1)
                    throw Error(PicklingError, "__newobj__ arglist is empty");
                var cls = argItems[0];
                if (cls is not PyType)
                    throw Error(PicklingError, "args[0] from __newobj__ args is not a type");
                if (obj != null)
                {
                    var objClass = get_class(obj);
                    if (!ReferenceEquals(objClass, cls))
                        throw Error(PicklingError, "args[0] from __newobj__ args has the wrong class");
                }
                /* XXX: These calls save() are prone to infinite recursion. Imagine
                   what happen if the value returned by the __reduce__() method of
                   some extension type contains another object of the same type. */

                /* Save the class and its __new__ arguments. */
                save(cls, 0);
                var newargtup = PyTuple.From(argItems[1..]);
                save(newargtup, 0);
                /* Add NEWOBJ opcode. */
                _Pickler_Write(NEWOBJ);
            }
            else
            { /* Not using NEWOBJ. */
                save(callable, 0);
                save(argtup, 0);
                _Pickler_Write(REDUCE);
            }

            /* obj can be NULL when save_reduce() is used directly. A NULL obj means
               the caller do not want to memoize the object. */
            if (obj != null)
            {
                /* If the object is already in the memo, this means it is
                   recursive. In this case, throw away everything we put on the
                   stack, and fetch the object back from the memo. */
                if (Memo.PyMemoTable_Get(obj, out _))
                {
                    _Pickler_Write(POP);
                    memo_get(obj);
                    return;
                }
                memo_put(obj);
            }

            if (listitems != null)
                batch_list(listitems);

            if (dictitems != null)
                batch_dict(dictitems);

            if (state != null)
            {
                if (stateSetter == null)
                {
                    save(state, 0);
                    _Pickler_Write(BUILD);
                }
                else
                {
                    /* If a state_setter is specified, call it instead of load_build to
                     * update obj's with its previous state.
                     * The first 4 save/write instructions push state_setter and its
                     * tuple of expected arguments (obj, state) onto the stack. The
                     * REDUCE opcode triggers the state_setter(obj, state) function
                     * call. Finally, because state-updating routines only do in-place
                     * modification, the whole operation has to be stack-transparent.
                     * Thus, we finally pop the call's output from the stack. */
                    save(stateSetter, 0);
                    save(obj, 0);
                    save(state, 0);
                    _Pickler_Write(TUPLE2);
                    _Pickler_Write(REDUCE);
                    _Pickler_Write(POP);
                }
            }
        }

        // save
        private void save(PyObject obj, int persSave)
        {
            _Pickler_OpcodeBoundary();

            /* The extra pers_save argument is necessary to avoid calling save_pers()
               on its returned object. */
            if (persSave == 0 && PersFunc != null)
            {
                if (save_pers(obj))
                    return;
            }

            var type = obj.GetPyType();

            /* Atom types; these aren't memoized, so don't check the memo. */
            if (obj is PyNone)
            {
                save_none(obj);
                return;
            }
            if (ReferenceEquals(obj, PyBool.False) || ReferenceEquals(obj, PyBool.True))
            {
                save_bool(obj);
                return;
            }
            if (ReferenceEquals(type, PyType.IntType) && obj is PyInt)
            {
                save_long(obj);
                return;
            }
            if (ReferenceEquals(type, PyType.FloatType) && obj is PyFloat)
            {
                save_float(obj);
                return;
            }

            /* Check the memo to see if it has the object. If so, generate
               a GET (or BINGET) opcode, instead of pickling the object
               once again. */
            if (Memo.PyMemoTable_Get(obj, out _))
            {
                memo_get(obj);
                return;
            }

            if (ReferenceEquals(type, PyType.BytesType) && obj is PyBytes)
            {
                save_bytes(obj);
                return;
            }
            if (ReferenceEquals(type, PyType.StrType) && obj is PyString)
            {
                save_unicode(obj);
                return;
            }

            /* We're only calling _Py_EnterRecursiveCall here so that atomic
               types above are pickled faster. */
            PyCRecursion.Enter(" while pickling an object");
            try
            {
                if (ReferenceEquals(type, PyType.DictType) && obj is PyDict)
                {
                    save_dict(obj);
                    return;
                }
                if (ReferenceEquals(type, PyType.SetType) && obj is PySet)
                {
                    save_set(obj);
                    return;
                }
                if (ReferenceEquals(type, PyType.FrozenSetType) && obj is PyFrozenSet)
                {
                    save_frozenset(obj);
                    return;
                }
                if (ReferenceEquals(type, PyType.ListType) && obj is PyList)
                {
                    save_list(obj);
                    return;
                }
                if (ReferenceEquals(type, PyType.TupleType) && obj is PyTuple)
                {
                    save_tuple(obj);
                    return;
                }
                if (ReferenceEquals(type, PyType.BytearrayType) && obj is PyByteArray)
                {
                    save_bytearray(obj);
                    return;
                }
                if (ReferenceEquals(type, PickleBufferObject.PickleBufferType))
                {
                    save_picklebuffer(obj);
                    return;
                }

                PyObject reduceValue = null;
                /* Now, check reducer_override.  If it returns NotImplemented,
                 * fallback to save_type or save_global, and then perhaps to the
                 * regular reduction mechanism. */
                if (ReducerOverride != null)
                {
                    reduceValue = ReducerOverride.Call(new[] { obj }, null);
                    if (reduceValue is PyNotImplemented)
                        reduceValue = null;
                }

                if (reduceValue == null)
                {
                    if (ReferenceEquals(type, PyType.TypeType))
                    {
                        save_type(obj);
                        return;
                    }
                    if (ReferenceEquals(type, PyType.FunctionType))
                    {
                        save_global(obj, null);
                        return;
                    }

                    /* Get a reduction callable, and call it.  This may come from
                     * self.dispatch_table, copyreg.dispatch_table, the object's
                     * __reduce_ex__ method, or the object's __reduce__ method. */
                    PyObject reduceFunc;
                    if (DispatchTable == null)
                    {
                        DispatchTable_GetItem(type, out reduceFunc);
                    }
                    else
                    {
                        try
                        {
                            reduceFunc = PyAbstract.GetItem(DispatchTable, type);
                        }
                        catch (PythonException error) when (error.PyException is PyKeyError)
                        {
                            reduceFunc = null;
                        }
                    }
                    if (reduceFunc != null)
                    {
                        reduceValue = _Pickle_FastCall(reduceFunc, obj);
                    }
                    else if (type.IsSubclassOf(PyType.TypeType))
                    {
                        save_global(obj, null);
                        return;
                    }
                    else
                    {
                        /* XXX: If the __reduce__ method is defined, __reduce_ex__ is
                           automatically defined as __reduce__. */

                        /* Check for a __reduce_ex__ method. */
                        reduceFunc = LookupAttr(obj, "__reduce_ex__");
                        if (reduceFunc != null)
                        {
                            reduceValue = _Pickle_FastCall(reduceFunc, PyInt.From(Proto));
                        }
                        else
                        {
                            /* Check for a __reduce__ method. */
                            reduceFunc = LookupAttr(obj, "__reduce__");
                            if (reduceFunc != null)
                                reduceValue = reduceFunc.Call(Array.Empty<PyObject>(), null);
                            else
                                throw Error(PicklingError, $"can't pickle '{type.FullName}' object: {obj.ToRepr().Value}");
                        }
                    }
                }

                // reduce:
                if (reduceValue is PyString)
                {
                    save_global(obj, reduceValue);
                    return;
                }
                if (reduceValue is not PyTuple)
                    throw Error(PicklingError, "__reduce__ must return a string or tuple");
                save_reduce(reduceValue, obj);
            }
            finally
            {
                PyCRecursion.Leave();
            }
        }

        // PyDict_GetItemWithError(st->dispatch_table, (PyObject *)type)
        private static void DispatchTable_GetItem(PyType type, out PyObject reduceFunc)
        {
            if (!PickleModule.DispatchTable.TryGetStoredValue(type, out reduceFunc))
                reduceFunc = null;
        }

        // dump
        internal void dump(PyObject obj)
        {
            /* Cache the reducer_override method, if it exists. */
            ReducerOverride = LookupAttr(this, "reducer_override");
            try
            {
                if (Proto >= 2)
                {
                    _Pickler_Write(stackalloc byte[] { PROTO, (byte)Proto });
                    if (Proto >= 4)
                        Framing = 1;
                }
                save(obj, 0);
                _Pickler_Write(STOP);
                _Pickler_CommitFrame();
            }
            finally
            {
                Framing = 0;
                /* Break the reference cycle we generated at the beginning this function
                 * call when setting the reducer_override attribute of the Pickler instance
                 * to a bound method of the same instance. */
                ReducerOverride = null;
            }
        }

        // _pickle_Pickler_clear_memo_impl
        private PyObject _pickle_Pickler_clear_memo_impl()
        {
            Memo?.PyMemoTable_Clear();
            return PyNone.Instance;
        }

        // _pickle_Pickler_dump_impl
        private PyObject _pickle_Pickler_dump_impl(PyObject obj)
        {
            /* Check whether the Pickler was initialized correctly (issue3664).
               Developers often forget to call __init__() in their subclasses, which
               would trigger a segfault without this check. */
            if (Write == null)
                throw Error(PicklingError, $"Pickler.__init__() was not called by {GetTypeName()}.__init__()");
            _Pickler_ClearBuffer();
            dump(obj);
            _Pickler_FlushToFile();
            return PyNone.Instance;
        }

        // _pickle_Pickler___sizeof___impl
        private long _pickle_Pickler___sizeof___impl()
        {
            long res = GetPyType().Layout.BasicSize;
            if (Memo != null)
            {
                // sizeof(PyMemoTable) and mt_allocated PyMemoEntry rows
                res += 32;
                res += Memo.Allocated * 16;
            }
            if (OutputBuffer != null)
                // _PySys_GetSizeOf of the bytes buffer
                res += 33 + OutputBuffer.LongLength;
            return res;
        }

        // Pickler_clear
        private void Pickler_clear()
        {
            OutputBuffer = null;
            Write = null;
            PersFunc = null;
            DispatchTable = null;
            FastMemo = null;
            ReducerOverride = null;
            BufferCallback = null;
            Memo = null;
        }

        /// <summary>
        /// _pickle.Pickler.__init__(file, protocol=None, fix_imports=True, buffer_callback=None)
        /// (the clinic's _pickle_Pickler___init__, then _pickle_Pickler___init___impl)
        /// </summary>
        private void _pickle_Pickler___init__(PyObject[] args, PyDict kwargs)
        {
            var a = PyArgs.Unpack("Pickler", args, kwargs, new[] { "file", "protocol", "fix_imports", "buffer_callback" }, 0, 1, 4);
            var file = a[0];
            var protocol = a[1] ?? PyNone.Instance;
            bool fixImports = a[2] == null || PyObject_IsTrue(a[2]);
            var bufferCallback = a[3] ?? PyNone.Instance;

            /* In case of multiple __init__() calls, clear previous content. */
            if (Write != null)
                Pickler_clear();

            _Pickler_SetProtocol(protocol, fixImports);
            _Pickler_SetOutputStream(file);
            _Pickler_SetBufferCallback(bufferCallback);

            /* memo and output_buffer may have already been created in _Pickler_New */
            Memo ??= PyMemoTable.PyMemoTable_New();
            OutputLen = 0;
            if (OutputBuffer == null)
            {
                MaxOutputLen = WRITE_BUF_SIZE;
                OutputBuffer = new byte[MaxOutputLen];
            }

            Fast = 0;
            FastNesting = 0;
            FastMemo = null;

            init_method_ref(this, "persistent_id", out PersFunc, out PersFuncSelf);
            if (DispatchTable != null)
                return;
            DispatchTable = LookupAttr(this, "dispatch_table");
        }

        // Pickler_get_memo
        private PyObject Pickler_get_memo() => PicklerMemoProxyObject.PicklerMemoProxy_New(this);

        // Pickler_set_memo
        private void Pickler_set_memo(PyObject obj)
        {
            if (obj == null)
                throw PyTypeError.Create("attribute deletion is not supported");
            PyMemoTable newMemo;
            if (obj.IsExactly(PicklerMemoProxyObject.PicklerMemoProxyType))
            {
                var pickler = ((PicklerMemoProxyObject)obj).Pickler;
                newMemo = (pickler.Memo ?? PyMemoTable.PyMemoTable_New()).PyMemoTable_Copy();
            }
            else if (obj is PyDict dict)
            {
                newMemo = PyMemoTable.PyMemoTable_New();
                int i = 0;
                while (dict.Next(ref i, out _, out var value))
                {
                    if (value is not PyTuple pair || pair.Items.Length != 2)
                        throw PyTypeError.Create("'memo' values must be 2-item tuples");
                    long memoId = PyArgs.LongAsSsize(pair.Items[0]);
                    var memoObj = pair.Items[1];
                    newMemo.PyMemoTable_Set(memoObj, memoId);
                }
            }
            else
            {
                throw PyTypeError.Create($"'memo' attribute must be a PicklerMemoProxy object or dict, not {obj.GetTypeName()}");
            }
            Memo = newMemo;
        }

        // Pickler_get_persid
        private PyObject Pickler_get_persid()
        {
            if (PersFunc == null)
                throw PyAttributeError.Create("persistent_id");
            return reconstruct_method(PersFunc, PersFuncSelf);
        }

        // Pickler_set_persid
        private void Pickler_set_persid(PyObject value)
        {
            if (value == null)
                throw PyTypeError.Create("attribute deletion is not supported");
            if (!value.IsCallable())
                throw PyTypeError.Create("persistent_id must be a callable taking one argument");
            PersFuncSelf = null;
            PersFunc = value;
        }
    }

    /// <summary>
    /// PicklerMemoProxyObject (Modules/_pickle.c): the proxy Pickler.memo answers, made from
    /// memoproxy_spec; it has no tp_new of its own and hashes as PyObject_HashNotImplemented.
    /// </summary>
    public sealed class PicklerMemoProxyObject : PyObject
    {
        internal static readonly PyType PicklerMemoProxyType = MakeType();

        internal PicklerObject Pickler;  // Pickler whose memo table we're proxying.

        private PicklerMemoProxyObject(PyType type)
        {
            if (!ReferenceEquals(type, PicklerMemoProxyType))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? PicklerMemoProxyType;

        private static PyType MakeType()
        {
            var type = new PyType("PicklerMemoProxy", new[] { PyType.ObjectType }, "_pickle");
            // picklerproxy_methods
            PyArgs.Define(type, "clear", PyCall.NoArgs, (self, args) => Self(self)._pickle_PicklerMemoProxy_clear_impl());
            PyArgs.Define(type, "copy", PyCall.NoArgs, (self, args) => Self(self)._pickle_PicklerMemoProxy_copy_impl());
            PyArgs.Define(type, "__reduce__", PyCall.NoArgs, (self, args) => Self(self)._pickle_PicklerMemoProxy___reduce___impl());
            // Py_tp_hash: PyObject_HashNotImplemented
            type.TypeDict["__hash__"] = PyNone.Instance;
            return type;
        }

        // A proxy object.__new__ made has no pickler; the C code would read through NULL.
        private static PicklerMemoProxyObject Self(PyObject self)
        {
            var proxy = self as PicklerMemoProxyObject
                ?? throw PyTypeError.Create($"descriptor requires a '_pickle.PicklerMemoProxy' object but received a '{self.GetTypeName()}'");
            if (proxy.Pickler == null)
                throw PyValueError.Create("PicklerMemoProxy has no Pickler");
            return proxy;
        }

        protected internal override long Hash() => throw PyTypeError.Create($"unhashable type: '{GetTypeName()}'");

        // PicklerMemoProxy_New
        internal static PicklerMemoProxyObject PicklerMemoProxy_New(PicklerObject pickler) =>
            new PicklerMemoProxyObject(PicklerMemoProxyType) { Pickler = pickler };

        // _pickle_PicklerMemoProxy_clear_impl
        private PyObject _pickle_PicklerMemoProxy_clear_impl()
        {
            Pickler.Memo?.PyMemoTable_Clear();
            return PyNone.Instance;
        }

        // _pickle_PicklerMemoProxy_copy_impl: {id(obj): (index, obj)}
        private PyObject _pickle_PicklerMemoProxy_copy_impl()
        {
            var newMemo = new PyDict();
            if (Pickler.Memo == null)
                return newMemo;
            foreach (var entry in Pickler.Memo.Entries())
            {
                var key = PyInt.From(PyObjectId.Of(entry.Key));
                var value = PyTuple.From(PyInt.From(entry.Value), entry.Key);
                newMemo.SetItem(key, value);
            }
            return newMemo;
        }

        // _pickle_PicklerMemoProxy___reduce___impl: (dict, (contents,))
        private PyObject _pickle_PicklerMemoProxy___reduce___impl()
        {
            var contents = _pickle_PicklerMemoProxy_copy_impl();
            return PyTuple.From(PyType.DictType, PyTuple.From(contents));
        }
    }
}
