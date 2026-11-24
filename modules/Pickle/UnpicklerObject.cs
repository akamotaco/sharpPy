using System;
using System.Collections.Generic;
using System.Numerics;
using static SharpPy.Modules.PickleModule;

namespace SharpPy.Modules
{
    /// <summary>
    /// Pdata (Modules/_pickle.c): the unpickling stack, with the fence of the top MARK. Not a type
    /// Python code can reach, so it is a plain CLR object here.
    /// </summary>
    internal sealed class Pdata
    {
        internal PyObject[] Data;
        internal int Size;          // Py_SIZE
        internal bool MarkSet;      // is MARK set?
        internal long Fence;        // position of top MARK or 0
        internal int Allocated;     // number of slots in data allocated

        // Pdata_New
        internal static Pdata Pdata_New() => new Pdata { Size = 0, MarkSet = false, Fence = 0, Allocated = 8, Data = new PyObject[8] };

        /* Retain only the initial clearto items.  If clearto >= the current
         * number of items, this is a (non-erroneous) NOP. */
        // Pdata_clear
        internal void Pdata_clear(long clearto)
        {
            int i = Size;
            if (clearto >= i)
                return;
            while (--i >= clearto)
                Data[i] = null;
            Size = (int)clearto;
        }

        // Pdata_grow
        private void Pdata_grow()
        {
            long allocated = Allocated;
            long newAllocated = (allocated >> 3) + 6;
            newAllocated += allocated;
            if (newAllocated > Array.MaxLength)
                throw PyMemoryError.Create();
            Array.Resize(ref Data, (int)newAllocated);
            Allocated = (int)newAllocated;
        }

        // Pdata_stack_underflow
        internal PythonException Pdata_stack_underflow() =>
            Error(UnpicklingError, MarkSet ? "unexpected MARK found" : "unpickling stack underflow");

        /* Pop the topmost element. On stack underflow, UnpicklingError is raised. */
        // Pdata_pop
        internal PyObject Pdata_pop()
        {
            if (Size <= Fence)
                throw Pdata_stack_underflow();
            Size--;
            var value = Data[Size];
            Data[Size] = null;
            return value;
        }

        // Pdata_push
        internal void Pdata_push(PyObject obj)
        {
            if (Size == Allocated)
                Pdata_grow();
            Data[Size] = obj;
            Size++;
        }

        // Pdata_poptuple
        internal PyTuple Pdata_poptuple(long start)
        {
            if (start < Fence)
                throw Pdata_stack_underflow();
            int len = Size - (int)start;
            var items = new PyObject[len];
            for (int i = (int)start, j = 0; j < len; i++, j++)
            {
                items[j] = Data[i];
                Data[i] = null;
            }
            Size = (int)start;
            return PyTuple.From(items);
        }

        // Pdata_poplist
        internal PyList Pdata_poplist(long start)
        {
            int len = Size - (int)start;
            var items = new PyObject[len];
            for (int i = (int)start, j = 0; j < len; i++, j++)
            {
                items[j] = Data[i];
                Data[i] = null;
            }
            Size = (int)start;
            return new PyList(items);
        }
    }

    /// <summary>
    /// UnpicklerObject (Modules/_pickle.c): _pickle.Unpickler, made from unpickler_type_spec. The
    /// input is a byte array: the whole pickle for loads(), or what the file's read, readline or
    /// peek gave last.
    /// </summary>
    public sealed class UnpicklerObject : PyObject
    {
        internal static readonly PyType UnpicklerType = MakeType();

        private const long READ_WHOLE_LINE = -1;

        internal Pdata Stack;               // Pickle data stack, store unpickled objects.
        /* The unpickler memo is just an array of PyObject *s. Using a dict
           is unnecessary, since the keys are contiguous ints. */
        internal PyObject[] Memo;
        internal long MemoSize;             // Capacity of the memo array
        internal long MemoLen;              // Number of objects in the memo
        internal PyObject PersFunc;         // persistent_load() method, can be NULL.
        internal PyObject PersFuncSelf;     // self if PersFunc is an unbound method, NULL otherwise
        private bool _hasBuffer;            // buffer.buf != NULL
        private byte[] _inputBuffer;
        private byte[] _inputLine;
        private long _inputLen;
        private long _nextReadIdx;
        private long _prefetchedIdx;        // index of first prefetched byte
        internal PyObject Read;             // read() method of the input stream.
        internal PyObject Readinto;         // readinto() method of the input stream.
        internal PyObject Readline;         // readline() method of the input stream.
        internal PyObject Peek;             // peek() method of the input stream, or NULL
        internal PyObject Buffers;          // iterable of out-of-band buffers, or NULL
        internal string Encoding;           // decoding of Python 2.x str, "ASCII" by default
        internal string Errors;             // its errors handler, "strict" by default
        private long[] _marks;              // Mark stack, used for unpickling container objects.
        private long _numMarks;             // Number of marks in the mark stack.
        private long _marksSize;            // Current allocated size of the mark stack.
        internal int Proto;                 // Protocol of the pickle loaded.
        internal bool FixImports;           // whether globals pickled by Python 2.x are renamed

        // PyType_GenericNew: every field zero.
        private UnpicklerObject(PyType type)
        {
            if (!ReferenceEquals(type, UnpicklerType))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? UnpicklerType;

        private static PyType MakeType()
        {
            // unpickler_type_spec: "_pickle.Unpickler"
            var type = new PyType("Unpickler", new[] { PyType.ObjectType }, "_pickle");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => new UnpicklerObject((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                ((UnpicklerObject)self)._pickle_Unpickler___init__(args, kwargs);
                return PyNone.Instance;
            });
            // Unpickler_methods
            PyArgs.Define(type, "load", PyCall.NoArgs, (self, args) => ((UnpicklerObject)self)._pickle_Unpickler_load_impl());
            PyArgs.Define(type, "find_class", PyCall.Defining, (self, args) =>
                ((UnpicklerObject)self)._pickle_Unpickler_find_class_impl(args[0], args[1]), 2, 2);
            PyArgs.Define(type, "__sizeof__", PyCall.NoArgs, (self, args) =>
                PyInt.From(((UnpicklerObject)self)._pickle_Unpickler___sizeof___impl()));
            // Unpickler_getsets
            type.TypeDict["memo"] = new PyGetSetDescriptor("memo", type,
                self => ((UnpicklerObject)self).Unpickler_get_memo(),
                (self, value) => ((UnpicklerObject)self).Unpickler_set_memo(value),
                deleter: self => ((UnpicklerObject)self).Unpickler_set_memo(null));
            type.TypeDict["persistent_load"] = new PyGetSetDescriptor("persistent_load", type,
                self => ((UnpicklerObject)self).Unpickler_get_persload(),
                (self, value) => ((UnpicklerObject)self).Unpickler_set_persload(value),
                deleter: self => ((UnpicklerObject)self).Unpickler_set_persload(null));
            return type;
        }

        // _Unpickler_SetStringInput: the bytes of a buffer (PyObject_GetBuffer, PyBUF_CONTIG_RO).
        internal long _Unpickler_SetStringInput(PyObject input)
        {
            var data = BytesMethods.RequireBuffer(input);
            _hasBuffer = true;
            _inputBuffer = data;
            _inputLen = data.Length;
            _nextReadIdx = 0;
            _prefetchedIdx = _inputLen;
            return _inputLen;
        }

        // bad_readline
        private static PythonException bad_readline() => Error(UnpicklingError, "pickle data was truncated");

        // _Unpickler_SkipConsumed: skip any consumed data that was only prefetched using peek()
        private void _Unpickler_SkipConsumed()
        {
            long consumed = _nextReadIdx - _prefetchedIdx;
            if (consumed <= 0)
                return;
            /* This makes a useless copy... */
            Read.Call(new PyObject[] { PyInt.From(consumed) }, null);
            _prefetchedIdx = _nextReadIdx;
        }

        /* If reading from a file, we need to only pull the bytes we need, since there
           may be multiple pickle objects arranged contiguously in the same input
           buffer.

           If `n` is READ_WHOLE_LINE, read a whole line. Otherwise, read up to `n`
           bytes from the input stream/buffer.

           Update the unpickler's input buffer with the newly-read data. On success,
           returns the number of bytes read from the file. */
        // _Unpickler_ReadFromFile
        private long _Unpickler_ReadFromFile(long n)
        {
            _Unpickler_SkipConsumed();

            PyObject data;
            long readSize;
            if (n == READ_WHOLE_LINE)
            {
                data = Readline.Call(Array.Empty<PyObject>(), null);
            }
            else
            {
                /* Prefetch some data without advancing the file pointer, if possible */
                if (Peek != null && n < PREFETCH)
                {
                    PyObject peeked = null;
                    try
                    {
                        peeked = _Pickle_FastCall(Peek, PyInt.From(PREFETCH));
                    }
                    catch (PythonException error) when (error.PyException is PyNotImplementedError)
                    {
                        /* peek() is probably not supported by the given file object */
                        Peek = null;
                    }
                    if (peeked != null)
                    {
                        readSize = _Unpickler_SetStringInput(peeked);
                        _prefetchedIdx = 0;
                        if (n <= readSize)
                            return n;
                    }
                }
                data = _Pickle_FastCall(Read, PyInt.From(n));
            }
            readSize = _Unpickler_SetStringInput(data);
            return readSize;
        }

        // _Unpickler_ReadImpl: don't call it directly, use _Unpickler_Read().
        private long _Unpickler_ReadImpl(long n)
        {
            if (_nextReadIdx > long.MaxValue - n)
                throw Error(UnpicklingError, "read would overflow (invalid bytecode)");

            /* This case is handled by the _Unpickler_Read() macro for efficiency */
            if (Read == null)
                throw bad_readline();

            /* Extend the buffer to satisfy desired size */
            long numRead = _Unpickler_ReadFromFile(n);
            if (numRead < n)
                throw bad_readline();
            _nextReadIdx = n;
            return 0;
        }

        /* Read `n` bytes from the unpickler's data source, storing the result in `buf`.
         *
         * This should only be used for non-small data reads where potentially
         * avoiding a copy is beneficial.  This method does not try to prefetch
         * more data into the input buffer. */
        // _Unpickler_ReadInto
        private void _Unpickler_ReadInto(byte[] buf, long n)
        {
            int offset = 0;
            /* Read from available buffer data, if any */
            long inBuffer = _inputLen - _nextReadIdx;
            if (inBuffer > 0)
            {
                long toRead = Math.Min(inBuffer, n);
                Array.Copy(_inputBuffer, _nextReadIdx, buf, 0, toRead);
                _nextReadIdx += toRead;
                offset += (int)toRead;
                n -= toRead;
                if (n == 0)
                    /* Entire read was satisfied from buffer */
                    return;
            }

            /* Read from file */
            if (Read == null)
                /* We're unpickling memory, this means the input is truncated */
                throw bad_readline();
            _Unpickler_SkipConsumed();

            if (Readinto == null)
            {
                /* readinto() not supported on file-like object, fall back to read()
                 * and copy into destination buffer (bpo-39681) */
                var data = _Pickle_FastCall(Read, PyInt.From(n));
                if (data is not PyBytes bytes)
                    throw PyValueError.Create($"read() returned non-bytes object ({data.GetPyType().ToRepr().Value})");
                long readSize = bytes.Value.Length;
                if (readSize < n)
                    throw bad_readline();
                Array.Copy(bytes.Value, 0, buf, offset, n);
                return;
            }

            /* Call readinto() into user buffer */
            var target = new byte[n];
            var bufObj = new PyMemoryView(target);
            var readSizeObj = _Pickle_FastCall(Readinto, bufObj);
            long size = PyArgs.LongAsSsize(readSizeObj);
            if (size < 0)
                throw PyValueError.Create("readinto() returned negative size");
            if (size < n)
                throw bad_readline();
            Array.Copy(target, 0, buf, offset, n);
        }

        /* Read `n` bytes from the unpickler's data source: the index in the input buffer they
           start at. This should be used for all data reads, rather than accessing the
           unpickler's input buffer directly. */
        // _Unpickler_Read (a macro in C)
        private long _Unpickler_Read(long n)
        {
            if (n <= _inputLen - _nextReadIdx)
            {
                long start = _nextReadIdx;
                _nextReadIdx += n;
                return start;
            }
            return _Unpickler_ReadImpl(n);
        }

        // The n bytes _Unpickler_Read made available.
        private byte[] ReadBytes(long n)
        {
            long start = _Unpickler_Read(n);
            var result = new byte[n];
            Array.Copy(_inputBuffer, start, result, 0, n);
            return result;
        }

        // _Unpickler_CopyLine
        private byte[] _Unpickler_CopyLine(long start, long len)
        {
            var line = new byte[len];
            Array.Copy(_inputBuffer, start, line, 0, len);
            _inputLine = line;
            return line;
        }

        /* Read a line from the input stream/buffer. If we run off the end of the input
           before hitting \n, raise an error. The line answered ends with its newline. */
        // _Unpickler_Readline
        private byte[] _Unpickler_Readline()
        {
            for (long i = _nextReadIdx; i < _inputLen; i++)
            {
                if (_inputBuffer[i] == (byte)'\n')
                {
                    long lineStart = _nextReadIdx;
                    long numRead = i - _nextReadIdx + 1;
                    _nextReadIdx = i + 1;
                    return _Unpickler_CopyLine(lineStart, numRead);
                }
            }
            if (Read == null)
                throw bad_readline();

            long read = _Unpickler_ReadFromFile(READ_WHOLE_LINE);
            if (read == 0 || _inputBuffer[read - 1] != (byte)'\n')
                throw bad_readline();
            _nextReadIdx = read;
            return _Unpickler_CopyLine(0, read);
        }

        // _Unpickler_ResizeMemoList
        private void _Unpickler_ResizeMemoList(long newSize)
        {
            if (newSize > Array.MaxLength)
                throw PyMemoryError.Create();
            var memoNew = Memo;
            Array.Resize(ref memoNew, (int)newSize);
            Memo = memoNew;
            MemoSize = newSize;
        }

        // _Unpickler_MemoGet: null if idx is out of bounds.
        private PyObject _Unpickler_MemoGet(long idx)
        {
            if (idx < 0 || idx >= MemoSize)
                return null;
            return Memo[idx];
        }

        // _Unpickler_MemoPut
        private void _Unpickler_MemoPut(long idx, PyObject value)
        {
            if (idx >= MemoSize)
                // idx * 2; an empty memo (a memo set from {}) grows to hold idx too.
                _Unpickler_ResizeMemoList(Math.Max(idx * 2, idx + 1));
            var oldItem = Memo[idx];
            Memo[idx] = value;
            if (oldItem == null)
                MemoLen++;
        }

        // _Unpickler_NewMemo
        private static PyObject[] _Unpickler_NewMemo(long newSize)
        {
            if (newSize > Array.MaxLength)
                throw PyMemoryError.Create();
            return new PyObject[newSize];
        }

        // _Unpickler_MemoCleanup: free the unpickler's memo.
        private void _Unpickler_MemoCleanup()
        {
            Memo = null;
        }

        // _Unpickler_New
        internal static UnpicklerObject _Unpickler_New()
        {
            const int MEMO_SIZE = 32;
            return new UnpicklerObject(UnpicklerType)
            {
                Stack = Pdata.Pdata_New(),
                Memo = _Unpickler_NewMemo(MEMO_SIZE),
                MemoSize = MEMO_SIZE,
                MemoLen = 0,
            };
        }

        // _Unpickler_SetInputStream
        internal void _Unpickler_SetInputStream(PyObject file)
        {
            try
            {
                /* Optional file methods */
                Peek = LookupAttr(file, "peek");
                Readinto = LookupAttr(file, "readinto");
                Read = LookupAttr(file, "read");
                Readline = LookupAttr(file, "readline");
                if (Readline == null || Read == null)
                    throw PyTypeError.Create("file must have 'read' and 'readline' attributes");
            }
            catch
            {
                Read = null;
                Readinto = null;
                Readline = null;
                Peek = null;
                throw;
            }
        }

        // _Unpickler_SetInputEncoding
        internal void _Unpickler_SetInputEncoding(string encoding, string errors)
        {
            Encoding = encoding ?? "ASCII";
            Errors = errors ?? "strict";
        }

        // _Unpickler_SetBuffers
        internal void _Unpickler_SetBuffers(PyObject buffers)
        {
            if (buffers == null || buffers is PyNone)
                Buffers = null;
            else
                Buffers = buffers.GetIterator();
        }

        // find_class: self.find_class(module_name, global_name), overridable by a subclass.
        private PyObject find_class(PyObject moduleName, PyObject globalName) =>
            GetAttribute("find_class").Call(new[] { moduleName, globalName }, null);

        // marker
        private long marker()
        {
            if (_numMarks < 1)
                throw Error(UnpicklingError, "could not find MARK");
            long mark = _marks[--_numMarks];
            Stack.MarkSet = _numMarks != 0;
            Stack.Fence = _numMarks != 0 ? _marks[_numMarks - 1] : 0;
            return mark;
        }

        // load_none
        private void load_none() => Stack.Pdata_push(PyNone.Instance);

        // The C string a line is read as: up to its first NUL.
        private static byte[] CString(byte[] s)
        {
            int nul = Array.IndexOf(s, (byte)0);
            return nul < 0 ? s : s[..nul];
        }

        // The text of a C string, for PyLong_FromString's and PyOS_string_to_double's parsing and
        // messages (PyUnicode_FromStringAndSize: UTF-8).
        private static string CText(byte[] s) => UnicodeDecoders.DecodeUtf8(CString(s), "strict", null).Value;

        private static bool IsSpace(byte c) => c == ' ' || (c >= '\t' && c <= '\r');

        private static int DigitValue(byte c) =>
            c >= '0' && c <= '9' ? c - '0'
            : c >= 'a' && c <= 'z' ? c - 'a' + 10
            : c >= 'A' && c <= 'Z' ? c - 'A' + 10
            : 99;

        /// <summary>
        /// strtol(s, &amp;endptr, 0) of the C library over a C long (32 bits on Windows): the
        /// value, where parsing stopped, and errno's ERANGE.
        /// </summary>
        private static long Strtol(byte[] s, out int end, out bool overflow)
        {
            overflow = false;
            int i = 0;
            while (i < s.Length && s[i] != 0 && IsSpace(s[i]))
                i++;
            bool negative = false;
            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                negative = s[i++] == '-';
            int numberBase = 10;
            if (i < s.Length && s[i] == '0')
            {
                if (i + 2 < s.Length + 1 && i + 1 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'X')
                    && i + 2 < s.Length && DigitValue(s[i + 2]) < 16)
                {
                    numberBase = 16;
                    i += 2;
                }
                else
                {
                    numberBase = 8;
                }
            }
            int digitsStart = i;
            BigInteger value = 0;
            while (i < s.Length && DigitValue(s[i]) < numberBase)
            {
                value = value * numberBase + DigitValue(s[i]);
                i++;
            }
            if (i == digitsStart)
            {
                // No conversion: endptr is s.
                end = 0;
                return 0;
            }
            end = i;
            if (negative)
                value = -value;
            if (value > PyArgs.CLongMax)
            {
                overflow = true;
                return PyArgs.CLongMax;
            }
            if (value < PyArgs.CLongMin)
            {
                overflow = true;
                return PyArgs.CLongMin;
            }
            return (long)value;
        }

        /// <summary>
        /// PyLong_FromString(s, NULL, base) over the C string of a line: leading and trailing
        /// whitespace allowed, and a syntax error reported with the base the prefix resolved and
        /// the first 200 bytes of the text.
        /// </summary>
        private static PyObject PyLong_FromString(byte[] s, int numberBase)
        {
            var cs = CString(s);
            string text = UnicodeDecoders.DecodeLatin1(cs).Value;
            try
            {
                return PyInt.FromString(text, numberBase, unicode: false);
            }
            catch (PythonException error) when (error.PyException is PyValueError
                && error.Message.StartsWith("invalid literal for int()", StringComparison.Ordinal))
            {
                int resolved = ResolvedBase(text, numberBase);
                var strobj = UnicodeDecoders.DecodeUtf8(cs.Length > 200 ? cs[..200] : cs, "strict", null);
                throw PyValueError.Create($"invalid literal for int() with base {resolved}: {Truncate200(strobj.ToRepr().Value)}");
            }
        }

        // The base PyLong_FromString's message names: what a base 0 resolved to from the prefix,
        // or 0 again for a nonzero old-style octal literal.
        private static int ResolvedBase(string text, int numberBase)
        {
            if (numberBase != 0)
                return numberBase;
            int i = 0;
            while (i < text.Length && IsSpace((byte)text[i]))
                i++;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
                i++;
            if (i >= text.Length || text[i] != '0')
                return 10;
            char next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (next == 'x' || next == 'X')
                return 16;
            if (next == 'o' || next == 'O')
                return 8;
            if (next == 'b' || next == 'B')
                return 2;
            try
            {
                return PyInt.FromString(text, 10, unicode: false).Value.IsZero ? 10 : 0;
            }
            catch (PythonException)
            {
                return 10;
            }
        }

        // load_int
        private void load_int()
        {
            var s = _Unpickler_Readline();
            long len = s.Length;
            if (len < 2)
                throw bad_readline();

            var cs = CString(s);
            /* XXX: Should the base argument of strtol() be explicitly set to 10?
               XXX(avassalotti): Should this uses PyOS_strtol()? */
            long x = Strtol(cs, out int end, out bool overflow);
            byte endChar = end < cs.Length ? cs[end] : (byte)0;
            PyObject value;
            if (overflow || (endChar != '\n' && endChar != 0))
            {
                /* Hm, maybe we've got something long.  Let's try reading
                 * it as a Python int object. */
                try
                {
                    value = PyLong_FromString(s, 0);
                }
                catch (PythonException)
                {
                    throw PyValueError.Create("could not convert string to int");
                }
            }
            else
            {
                if (len == 3 && (x == 0 || x == 1))
                    value = PyBool.FromBool(x != 0);
                else
                    value = PyInt.From(x);
            }
            Stack.Pdata_push(value);
        }

        // load_bool
        private void load_bool(PyObject boolean) => Stack.Pdata_push(boolean);

        /* s contains x bytes of an unsigned little-endian integer.  Return its value
         * as a C Py_ssize_t, or -1 if it's higher than PY_SSIZE_T_MAX. */
        // calc_binsize
        private static long calc_binsize(byte[] s, int nbytes)
        {
            ulong x = 0;
            for (int i = 0; i < nbytes; i++)
                x |= (ulong)s[i] << (8 * i);
            if (x > long.MaxValue)
                return -1;
            return (long)x;
        }

        /* s contains x bytes of a little-endian integer.  Return its value as a
         * C int.  Obscure:  when x is 1 or 2, this is an unsigned little-endian
         * int, but when x is 4 it's a signed one. */
        // calc_binint
        private static long calc_binint(byte[] s, int nbytes)
        {
            long x = 0;
            for (int i = 0; i < nbytes; i++)
                x |= (long)s[i] << (8 * i);
            /* Unlike BININT1 and BININT2, BININT (more accurately BININT4)
             * is signed, so on a box with longs bigger than 4 bytes we need
             * to extend a BININT's sign bit to the full width. */
            if (nbytes == 4)
                x |= -(x & (1L << 31));
            return x;
        }

        // load_binintx
        private void load_binintx(byte[] s, int size) => Stack.Pdata_push(PyInt.From(calc_binint(s, size)));

        // load_binint
        private void load_binint() => load_binintx(ReadBytes(4), 4);

        // load_binint1
        private void load_binint1() => load_binintx(ReadBytes(1), 1);

        // load_binint2
        private void load_binint2() => load_binintx(ReadBytes(2), 2);

        // load_long
        private void load_long()
        {
            var s = _Unpickler_Readline();
            int len = s.Length;
            if (len < 2)
                throw bad_readline();

            /* s[len-2] will usually be 'L' (and s[len-1] is '\n'); we need to remove
               the 'L' before calling PyLong_FromString.  In order to maintain
               compatibility with Python 3.0.0, we don't actually *require*
               the 'L' to be present. */
            if (s[len - 2] == (byte)'L')
                s[len - 2] = 0;
            /* XXX: Should the base argument explicitly set to 10? */
            var value = PyLong_FromString(s, 0);
            Stack.Pdata_push(value);
        }

        /* 'size' bytes contain the # of bytes of little-endian 256's-complement
         * data following. */
        // load_counted_long
        private void load_counted_long(int size)
        {
            var nbytes = ReadBytes(size);
            long count = calc_binint(nbytes, size);
            if (count < 0)
            {
                /* Corrupt or hostile pickle -- we never write one like this */
                throw Error(UnpicklingError, "LONG pickle has negative byte count");
            }
            PyObject value;
            if (count == 0)
            {
                value = PyInt.From(0);
            }
            else
            {
                /* Read the raw little-endian bytes and convert. */
                var pdata = ReadBytes(count);
                value = PyInt.From(new BigInteger(pdata, isUnsigned: false, isBigEndian: false));
            }
            Stack.Pdata_push(value);
        }

        // load_float
        private void load_float()
        {
            var s = _Unpickler_Readline();
            if (s.Length < 2)
                throw bad_readline();

            // PyOS_string_to_double(s, &endptr, PyExc_OverflowError)
            string text = CText(s);
            int position = 0;
            if (!PyFloat.ScanDouble(text, ref position, out double d))
                throw PyValueError.Create($"could not convert string to float: '{Truncate200(text)}'");
            if (double.IsInfinity(d) && FiniteSpelling(text))
                throw PyOverflowError.Create($"value too large to convert to float: '{Truncate200(text)}'");
            char endChar = position < text.Length ? text[position] : '\0';
            if (endChar != '\n' && endChar != '\0')
                throw PyValueError.Create("could not convert string to float");
            Stack.Pdata_push(new PyFloat(d));
        }

        // '%.200s'
        private static string Truncate200(string text) => text.Length > 200 ? text.Substring(0, 200) : text;

        // A number written in digits, which can overflow, unlike inf or nan.
        private static bool FiniteSpelling(string text)
        {
            int i = 0;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
                i++;
            return i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.');
        }

        // load_binfloat
        private void load_binfloat()
        {
            var s = ReadBytes(8);
            double x = PyFloatPack.Unpack8(s, false);
            Stack.Pdata_push(new PyFloat(x));
        }

        // load_string
        private void load_string()
        {
            var s = _Unpickler_Readline();
            /* Strip the newline */
            int len = s.Length - 1;
            /* Strip outermost quotes */
            int p;
            if (len >= 2 && s[0] == s[len - 1] && (s[0] == (byte)'\'' || s[0] == (byte)'"'))
            {
                p = 1;
                len -= 2;
            }
            else
            {
                throw Error(UnpicklingError, "the STRING opcode argument must be quoted");
            }

            /* Use the PyBytes API to decode the string, since that is what is used
               to encode, and then coerce the result to Unicode. */
            var bytes = EscapeCodecs.DecodeEscape(s[p..(p + len)], null);

            /* Leave the Python 2.x strings as bytes if the *encoding* given to the
               Unpickler was 'bytes'. Otherwise, convert them to unicode. */
            PyObject obj;
            if (Encoding == "bytes")
                obj = PyBytes.From(bytes);
            else
                // PyUnicode_FromEncodedObject(bytes, self->encoding, self->errors)
                obj = PyStringNewArguments.DecodeBytes(bytes, Encoding, Errors);
            Stack.Pdata_push(obj);
        }

        // load_counted_binstring
        private void load_counted_binstring(int nbytes)
        {
            var s = ReadBytes(nbytes);
            long size = calc_binsize(s, nbytes);
            if (size < 0)
                throw Error(UnpicklingError, $"BINSTRING exceeds system's maximum size of {long.MaxValue} bytes");

            var data = ReadBytes(size);
            /* Convert Python 2.x strings to bytes if the *encoding* given to the
               Unpickler was 'bytes'. Otherwise, convert them to unicode. */
            PyObject obj;
            if (Encoding == "bytes")
                obj = PyBytes.From(data);
            else
                // PyUnicode_Decode(s, size, self->encoding, self->errors)
                obj = PyStringNewArguments.DecodeBytes(data, Encoding, Errors);
            Stack.Pdata_push(obj);
        }

        // A buffer of size bytes for _Unpickler_ReadInto: a pickle held in memory cannot hold
        // more than it has, so a size past it is the truncation it will be found to be.
        // PyBytes_FromStringAndSize(NULL, size) refuses first a size no bytes object can have; a
        // bytearray's refuses only PY_SSIZE_T_MAX.
        private byte[] ReadIntoBuffer(long size, bool bytes)
        {
            if (bytes && size > long.MaxValue - 33)
                throw PyOverflowError.Create("byte string is too large");
            if (!bytes && size == long.MaxValue)
                throw PyMemoryError.Create();
            if (Read == null && size > _inputLen - _nextReadIdx)
                throw bad_readline();
            if (size > Array.MaxLength)
                throw PyMemoryError.Create();
            return new byte[size];
        }

        // load_counted_binbytes
        private void load_counted_binbytes(int nbytes)
        {
            var s = ReadBytes(nbytes);
            long size = calc_binsize(s, nbytes);
            if (size < 0)
                throw PyOverflowError.Create($"BINBYTES exceeds system's maximum size of {long.MaxValue} bytes");
            var bytes = ReadIntoBuffer(size, true);
            _Unpickler_ReadInto(bytes, size);
            Stack.Pdata_push(PyBytes.From(bytes));
        }

        // load_counted_bytearray
        private void load_counted_bytearray()
        {
            var s = ReadBytes(8);
            long size = calc_binsize(s, 8);
            if (size < 0)
                throw PyOverflowError.Create($"BYTEARRAY8 exceeds system's maximum size of {long.MaxValue} bytes");
            var str = ReadIntoBuffer(size, false);
            _Unpickler_ReadInto(str, size);
            Stack.Pdata_push(new PyByteArray(str));
        }

        // load_next_buffer
        private void load_next_buffer()
        {
            if (Buffers == null)
                throw Error(UnpicklingError, "pickle stream refers to out-of-band data but no *buffers* argument was given");
            var buf = PyIter_Next(Buffers);
            if (buf == null)
                throw Error(UnpicklingError, "not enough out-of-band buffers");
            Stack.Pdata_push(buf);
        }

        // load_readonly_buffer
        private void load_readonly_buffer()
        {
            int len = Stack.Size;
            if (len <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();

            var obj = Stack.Data[len - 1];
            var view = PickleBufferObject.PyMemoryView_FromObject(obj);
            if (!view.ReadOnly)
            {
                /* Original object is writable */
                var readOnly = view.GetAttribute("toreadonly").Call(Array.Empty<PyObject>(), null);
                view.Release();
                Stack.Data[len - 1] = readOnly;
            }
            else
            {
                /* Original object is read-only, no need to replace it */
                view.Release();
            }
        }

        // load_unicode
        private void load_unicode()
        {
            var s = _Unpickler_Readline();
            if (s.Length < 1)
                throw bad_readline();
            var str = EscapeCodecs.DecodeRawUnicodeEscapeStateful(s[..^1], null, null);
            Stack.Pdata_push(str);
        }

        // load_counted_binunicode
        private void load_counted_binunicode(int nbytes)
        {
            var s = ReadBytes(nbytes);
            long size = calc_binsize(s, nbytes);
            if (size < 0)
                throw PyOverflowError.Create($"BINUNICODE exceeds system's maximum size of {long.MaxValue} bytes");
            var data = ReadBytes(size);
            var str = UnicodeDecoders.DecodeUtf8(data, "surrogatepass", null);
            Stack.Pdata_push(str);
        }

        // load_counted_tuple
        private void load_counted_tuple(long len)
        {
            if (Stack.Size < len)
                throw Stack.Pdata_stack_underflow();
            var tuple = Stack.Pdata_poptuple(Stack.Size - len);
            Stack.Pdata_push(tuple);
        }

        // load_tuple
        private void load_tuple()
        {
            long i = marker();
            load_counted_tuple(Stack.Size - i);
        }

        // load_empty_list
        private void load_empty_list() => Stack.Pdata_push(new PyList());

        // load_empty_dict
        private void load_empty_dict() => Stack.Pdata_push(new PyDict());

        // load_empty_set
        private void load_empty_set() => Stack.Pdata_push(new PySet());

        // load_list
        private void load_list()
        {
            long i = marker();
            var list = Stack.Pdata_poplist(i);
            Stack.Pdata_push(list);
        }

        // load_dict
        private void load_dict()
        {
            long i = marker();
            long j = Stack.Size;
            var dict = new PyDict();
            if ((j - i) % 2 != 0)
                throw Error(UnpicklingError, "odd number of items for DICT");
            for (long k = i + 1; k < j; k += 2)
            {
                var key = Stack.Data[k - 1];
                var value = Stack.Data[k];
                dict.SetItem(key, value);
            }
            Stack.Pdata_clear(i);
            Stack.Pdata_push(dict);
        }

        // load_frozenset
        private void load_frozenset()
        {
            long i = marker();
            var items = Stack.Pdata_poptuple(i);
            var frozenset = new PyFrozenSet(items.Items);
            Stack.Pdata_push(frozenset);
        }

        // instantiate
        private static PyObject instantiate(PyObject cls, PyTuple args)
        {
            /* Caller must assure args are a tuple.  Normally, args come from
               Pdata_poptuple which packs objects from the top of the stack
               into a newly created tuple. */
            if (args.Items.Length == 0 && cls is PyType)
            {
                var func = LookupAttr(cls, "__getinitargs__");
                if (func == null)
                    // PyObject_CallMethodOneArg(cls, &_Py_ID(__new__), cls)
                    return cls.GetAttribute("__new__").Call(new[] { cls }, null);
            }
            return cls.Call(args.Items, null);
        }

        // load_obj
        private void load_obj()
        {
            long i = marker();
            if (Stack.Size - i < 1)
                throw Stack.Pdata_stack_underflow();
            var args = Stack.Pdata_poptuple(i + 1);
            var cls = Stack.Pdata_pop();
            var obj = instantiate(cls, args);
            Stack.Pdata_push(obj);
        }

        // load_inst
        private void load_inst()
        {
            long i = marker();
            var s = _Unpickler_Readline();
            if (s.Length < 2)
                throw bad_readline();

            /* Here it is safe to use PyUnicode_DecodeASCII(), even though non-ASCII
               identifiers are permitted in Python 3.0, since the INST opcode is only
               supported by older protocols on Python 2.x. */
            var moduleName = UnicodeDecoders.DecodeAscii(s[..^1], "strict");

            s = _Unpickler_Readline();
            if (s.Length < 2)
                throw bad_readline();
            var className = UnicodeDecoders.DecodeAscii(s[..^1], "strict");
            var cls = find_class(moduleName, className);

            var args = Stack.Pdata_poptuple(i);
            var obj = instantiate(cls, args);
            Stack.Pdata_push(obj);
        }

        // newobj_unpickling_error
        private static PythonException newobj_unpickling_error(string msg, bool useKwargs, PyObject arg) =>
            Error(UnpicklingError, string.Format(msg, useKwargs ? "NEWOBJ_EX" : "NEWOBJ", arg.GetTypeName()));

        // load_newobj
        private void load_newobj(bool useKwargs)
        {
            PyObject kwargs = null;
            /* Stack is ... cls args [kwargs], and we want to call
             * cls.__new__(cls, *args, **kwargs). */
            if (useKwargs)
                kwargs = Stack.Pdata_pop();
            var args = Stack.Pdata_pop();
            var cls = Stack.Pdata_pop();

            if (cls is not PyType clsType)
                throw newobj_unpickling_error("{0} class argument must be a type, not {1}", useKwargs, cls);
            // tp_new == NULL: a type that disallows instantiation.
            if (clsType.Layout.NoInstances || clsType.FindNameInMro("__new__") == null)
                throw Error(UnpicklingError, $"{(useKwargs ? "NEWOBJ_EX" : "NEWOBJ")} class argument '{clsType.FullName}' doesn't have __new__");
            if (args is not PyTuple argsTuple)
                throw newobj_unpickling_error("{0} args argument must be a tuple, not {1}", useKwargs, args);
            if (useKwargs && kwargs is not PyDict)
                throw newobj_unpickling_error("{0} kwargs argument must be a dict, not {1}", useKwargs, kwargs);

            // ((PyTypeObject *)cls)->tp_new((PyTypeObject *)cls, args, kwargs)
            var newArgs = new PyObject[argsTuple.Items.Length + 1];
            newArgs[0] = cls;
            Array.Copy(argsTuple.Items, 0, newArgs, 1, argsTuple.Items.Length);
            var obj = cls.GetAttribute("__new__").Call(newArgs, (PyDict)kwargs);
            Stack.Pdata_push(obj);
        }

        // load_global
        private void load_global()
        {
            var s = _Unpickler_Readline();
            if (s.Length < 2)
                throw bad_readline();
            var moduleName = UnicodeDecoders.DecodeUtf8(s[..^1], "strict", null);

            s = _Unpickler_Readline();
            if (s.Length < 2)
                throw bad_readline();
            var globalName = UnicodeDecoders.DecodeUtf8(s[..^1], "strict", null);
            var global = find_class(moduleName, globalName);
            Stack.Pdata_push(global);
        }

        // load_stack_global
        private void load_stack_global()
        {
            var globalName = Stack.Pdata_pop();
            var moduleName = Stack.Pdata_pop();
            if (!moduleName.IsExactly(PyType.StrType) || !globalName.IsExactly(PyType.StrType))
                throw Error(UnpicklingError, "STACK_GLOBAL requires str");
            var global = find_class(moduleName, globalName);
            Stack.Pdata_push(global);
        }

        // load_persid
        private void load_persid()
        {
            if (PersFunc != null)
            {
                var s = _Unpickler_Readline();
                if (s.Length < 1)
                    throw bad_readline();
                PyString pid;
                try
                {
                    pid = UnicodeDecoders.DecodeAscii(s[..^1], "strict");
                }
                catch (PythonException error) when (error.PyException is PyUnicodeDecodeError)
                {
                    throw Error(UnpicklingError, "persistent IDs in protocol 0 must be ASCII strings");
                }
                var obj = call_method(PersFunc, PersFuncSelf, pid);
                Stack.Pdata_push(obj);
            }
            else
            {
                throw Error(UnpicklingError, "A load persistent id instruction was encountered, but no persistent_load function was specified.");
            }
        }

        // load_binpersid
        private void load_binpersid()
        {
            if (PersFunc != null)
            {
                var pid = Stack.Pdata_pop();
                var obj = call_method(PersFunc, PersFuncSelf, pid);
                Stack.Pdata_push(obj);
            }
            else
            {
                throw Error(UnpicklingError, "A load persistent id instruction was encountered, but no persistent_load function was specified.");
            }
        }

        // load_pop
        private void load_pop()
        {
            long len = Stack.Size;
            /* Note that we split the (pickle.py) stack into two stacks,
             * an object stack and a mark stack. We have to be clever and
             * pop the right one. We do this by looking at the top of the
             * mark stack first, and only signalling a stack underflow if
             * the object stack is empty and the mark stack doesn't match
             * our expectations. */
            if (_numMarks > 0 && _marks[_numMarks - 1] == len)
            {
                _numMarks--;
                Stack.MarkSet = _numMarks != 0;
                Stack.Fence = _numMarks != 0 ? _marks[_numMarks - 1] : 0;
            }
            else if (len <= Stack.Fence)
            {
                throw Stack.Pdata_stack_underflow();
            }
            else
            {
                len--;
                Stack.Data[len] = null;
                Stack.Size = (int)len;
            }
        }

        // load_pop_mark
        private void load_pop_mark()
        {
            long i = marker();
            Stack.Pdata_clear(i);
        }

        // load_dup
        private void load_dup()
        {
            int len = Stack.Size;
            if (len <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();
            var last = Stack.Data[len - 1];
            Stack.Pdata_push(last);
        }

        private PythonException MemoNotFound(long idx) => Error(UnpicklingError, $"Memo value not found at index {idx}");

        // load_get
        private void load_get()
        {
            var s = _Unpickler_Readline();
            if (s.Length < 2)
                throw bad_readline();
            var key = PyLong_FromString(s, 10);
            long idx = PyArgs.LongAsSsize(key);
            var value = _Unpickler_MemoGet(idx);
            if (value == null)
                throw MemoNotFound(idx);
            Stack.Pdata_push(value);
        }

        // load_binget
        private void load_binget()
        {
            var s = ReadBytes(1);
            long idx = s[0];
            var value = _Unpickler_MemoGet(idx);
            if (value == null)
                throw MemoNotFound(idx);
            Stack.Pdata_push(value);
        }

        // load_long_binget
        private void load_long_binget()
        {
            var s = ReadBytes(4);
            long idx = calc_binsize(s, 4);
            var value = _Unpickler_MemoGet(idx);
            if (value == null)
                throw MemoNotFound(idx);
            Stack.Pdata_push(value);
        }

        /* Push an object from the extension registry (EXT[124]).  nbytes is
         * the number of bytes following the opcode, holding the index (code) value. */
        // load_extension
        private void load_extension(int nbytes)
        {
            var codebytes = ReadBytes(nbytes);
            long code = calc_binint(codebytes, nbytes);
            if (code <= 0)
            { /* note that 0 is forbidden */
                /* Corrupt or hostile pickle. */
                throw Error(UnpicklingError, "EXT specifies code <= 0");
            }

            /* Look for the code in the cache. */
            var pyCode = PyInt.From(code);
            if (ExtensionCache.TryGetStoredValue(pyCode, out var obj))
            {
                /* Bingo. */
                Stack.Pdata_push(obj);
                return;
            }

            /* Look up the (module_name, class_name) pair. */
            if (!InvertedRegistry.TryGetStoredValue(pyCode, out var pair))
                throw PyValueError.Create($"unregistered extension code {code}");
            /* Since the extension registry is manipulable via Python code,
             * confirm that pair is really a 2-tuple of strings. */
            if (pair is not PyTuple tuple || tuple.Items.Length != 2
                || tuple.Items[0] is not PyString || tuple.Items[1] is not PyString)
                throw PyValueError.Create($"_inverted_registry[{code}] isn't a 2-tuple of strings");

            /* Load the object. */
            obj = find_class(tuple.Items[0], tuple.Items[1]);
            /* Cache code -> obj. */
            ExtensionCache.SetItem(pyCode, obj);
            Stack.Pdata_push(obj);
        }

        // load_put
        private void load_put()
        {
            var s = _Unpickler_Readline();
            if (s.Length < 2)
                throw bad_readline();
            if (Stack.Size <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();
            var value = Stack.Data[Stack.Size - 1];

            var key = PyLong_FromString(s, 10);
            long idx = PyArgs.LongAsSsize(key);
            if (idx < 0)
                throw PyValueError.Create("negative PUT argument");
            _Unpickler_MemoPut(idx, value);
        }

        // load_binput
        private void load_binput()
        {
            var s = ReadBytes(1);
            if (Stack.Size <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();
            var value = Stack.Data[Stack.Size - 1];
            long idx = s[0];
            _Unpickler_MemoPut(idx, value);
        }

        // load_long_binput
        private void load_long_binput()
        {
            var s = ReadBytes(4);
            if (Stack.Size <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();
            var value = Stack.Data[Stack.Size - 1];
            long idx = calc_binsize(s, 4);
            if (idx < 0)
                throw PyValueError.Create("negative LONG_BINPUT argument");
            _Unpickler_MemoPut(idx, value);
        }

        // load_memoize
        private void load_memoize()
        {
            if (Stack.Size <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();
            var value = Stack.Data[Stack.Size - 1];
            _Unpickler_MemoPut(MemoLen, value);
        }

        // do_append
        private void do_append(long x)
        {
            long len = Stack.Size;
            if (x > len || x <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();
            if (len == x) /* nothing to do */
                return;

            var list = Stack.Data[x - 1];
            if (list.IsExactly(PyType.ListType) && list is PyList exact)
            {
                var slice = Stack.Pdata_poplist(x);
                // PyList_SetSlice(list, list_len, list_len, slice)
                foreach (var item in slice.Items)
                    exact.Append(item);
                return;
            }

            var extendFunc = LookupAttr(list, "extend");
            if (extendFunc != null)
            {
                var slice = Stack.Pdata_poplist(x);
                _Pickle_FastCall(extendFunc, slice);
            }
            else
            {
                /* Even if the PEP 307 requires extend() and append() methods,
                   fall back on append() if the object has no extend() method
                   for backward compatibility. */
                var appendFunc = list.GetAttribute("append");
                for (long i = x; i < len; i++)
                {
                    var value = Stack.Data[i];
                    try
                    {
                        _Pickle_FastCall(appendFunc, value);
                    }
                    catch (PythonException)
                    {
                        Stack.Pdata_clear(i + 1);
                        Stack.Size = (int)x;
                        throw;
                    }
                }
                Stack.Pdata_clear(x);
            }
        }

        // load_append
        private void load_append()
        {
            if (Stack.Size - 1 <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();
            do_append(Stack.Size - 1);
        }

        // load_appends
        private void load_appends()
        {
            long i = marker();
            do_append(i);
        }

        // do_setitems
        private void do_setitems(long x)
        {
            long len = Stack.Size;
            if (x > len || x <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();
            if (len == x) /* nothing to do */
                return;
            if ((len - x) % 2 != 0)
            {
                /* Corrupt or hostile pickle -- we never write one like this. */
                throw Error(UnpicklingError, "odd number of items for SETITEMS");
            }

            /* Here, dict does not actually need to be a PyDict; it could be anything
               that supports the __setitem__ attribute. */
            var dict = Stack.Data[x - 1];
            try
            {
                for (long i = x + 1; i < len; i += 2)
                {
                    var key = Stack.Data[i - 1];
                    var value = Stack.Data[i];
                    PyAbstract.SetItem(dict, key, value);
                }
            }
            finally
            {
                Stack.Pdata_clear(x);
            }
        }

        // load_setitem
        private void load_setitem() => do_setitems(Stack.Size - 2);

        // load_setitems
        private void load_setitems()
        {
            long i = marker();
            do_setitems(i);
        }

        // load_additems
        private void load_additems()
        {
            long mark = marker();
            long len = Stack.Size;
            if (mark > len || mark <= Stack.Fence)
                throw Stack.Pdata_stack_underflow();
            if (len == mark) /* nothing to do */
                return;

            var set = Stack.Data[mark - 1];
            if (set is PySet pySet && set.GetPyType().IsSubclassOf(PyType.SetType))
            {
                var items = Stack.Pdata_poptuple(mark);
                // _PySet_Update(set, items)
                pySet.Update(items);
                return;
            }

            var addFunc = set.GetAttribute("add");
            for (long i = mark; i < len; i++)
            {
                var item = Stack.Data[i];
                try
                {
                    _Pickle_FastCall(addFunc, item);
                }
                catch (PythonException)
                {
                    Stack.Pdata_clear(i + 1);
                    Stack.Size = (int)mark;
                    throw;
                }
            }
            Stack.Pdata_clear(mark);
        }

        // load_build
        private void load_build()
        {
            /* Stack is ... instance, state.  We want to leave instance at
             * the stack top, possibly mutated via instance.__setstate__(state). */
            if (Stack.Size - 2 < Stack.Fence)
                throw Stack.Pdata_stack_underflow();

            var state = Stack.Pdata_pop();
            var inst = Stack.Data[Stack.Size - 1];

            var setstate = LookupAttr(inst, "__setstate__");
            if (setstate != null)
            {
                /* The explicit __setstate__ is responsible for everything. */
                _Pickle_FastCall(setstate, state);
                return;
            }

            /* A default __setstate__.  First see whether state embeds a
             * slot state dict too (a proto 2 addition). */
            PyObject slotstate;
            if (state is PyTuple tuple && tuple.Items.Length == 2)
            {
                state = tuple.Items[0];
                slotstate = tuple.Items[1];
            }
            else
            {
                slotstate = null;
            }

            /* Set inst.__dict__ from the state dict (if any). */
            if (state is not PyNone)
            {
                if (state is not PyDict stateDict)
                    throw Error(UnpicklingError, "state is not a dictionary");
                var dict = inst.GetAttribute("__dict__");
                int i = 0;
                while (stateDict.Next(ref i, out var dKey, out var dValue))
                {
                    /* normally the keys for instance attributes are
                       interned.  we should try to do that here. */
                    if (dKey.IsExactly(PyType.StrType) && dKey is PyString text)
                        dKey = PyString.Intern(text);
                    PyAbstract.SetItem(dict, dKey, dValue);
                }
            }

            /* Also set instance attributes from the slotstate dict (if any). */
            if (slotstate != null)
            {
                if (slotstate is not PyDict slotDict)
                    throw Error(UnpicklingError, "slot state is not a dictionary");
                int i = 0;
                while (slotDict.Next(ref i, out var dKey, out var dValue))
                {
                    PyObject.AttributeName(dKey);
                    inst.SetAttribute((PyString)dKey, dValue);
                }
            }
        }

        // load_mark
        private void load_mark()
        {
            /* Note that we split the (pickle.py) stack into two stacks, an
             * object stack and a mark stack. Here we push a mark onto the
             * mark stack. */
            if (_numMarks >= _marksSize)
            {
                long alloc = (_numMarks << 1) + 20;
                var marksNew = _marks ?? Array.Empty<long>();
                Array.Resize(ref marksNew, (int)alloc);
                _marks = marksNew;
                _marksSize = alloc;
            }
            Stack.MarkSet = true;
            Stack.Fence = Stack.Size;
            _marks[_numMarks++] = Stack.Size;
        }

        // load_reduce
        private void load_reduce()
        {
            var argtup = Stack.Pdata_pop();
            var callable = Stack.Pdata_pop();
            // PyObject_CallObject(callable, argtup)
            if (argtup is not PyTuple args)
                throw PyTypeError.Create("argument list must be a tuple");
            var obj = callable.Call(args.Items, null);
            Stack.Pdata_push(obj);
        }

        /* Just raises an error if we don't know the protocol specified.  PROTO
         * is the first opcode for protocols >= 2. */
        // load_proto
        private void load_proto()
        {
            var s = ReadBytes(1);
            int i = s[0];
            if (i <= HIGHEST_PROTOCOL)
            {
                Proto = i;
                return;
            }
            throw PyValueError.Create($"unsupported pickle protocol: {i}");
        }

        // load_frame
        private void load_frame()
        {
            var s = ReadBytes(8);
            long frameLen = calc_binsize(s, 8);
            if (frameLen < 0)
                throw PyOverflowError.Create($"FRAME length exceeds system's maximum of {long.MaxValue} bytes");
            _Unpickler_Read(frameLen);
            /* Rewind to start of frame */
            _nextReadIdx -= frameLen;
        }

        // load
        internal PyObject load()
        {
            _numMarks = 0;
            Stack.MarkSet = false;
            Stack.Fence = 0;
            Proto = 0;
            if (Stack.Size != 0)
                Stack.Pdata_clear(0);

            while (true)
            {
                long at;
                try
                {
                    at = _Unpickler_Read(1);
                }
                catch (PythonException error) when (Matches(error, UnpicklingError))
                {
                    throw PyEOFError.Create("Ran out of input");
                }
                byte s = _inputBuffer[at];

                switch (s)
                {
                    case NONE: load_none(); continue;
                    case BININT: load_binint(); continue;
                    case BININT1: load_binint1(); continue;
                    case BININT2: load_binint2(); continue;
                    case INT: load_int(); continue;
                    case LONG: load_long(); continue;
                    case LONG1: load_counted_long(1); continue;
                    case LONG4: load_counted_long(4); continue;
                    case FLOAT: load_float(); continue;
                    case BINFLOAT: load_binfloat(); continue;
                    case SHORT_BINBYTES: load_counted_binbytes(1); continue;
                    case BINBYTES: load_counted_binbytes(4); continue;
                    case BINBYTES8: load_counted_binbytes(8); continue;
                    case BYTEARRAY8: load_counted_bytearray(); continue;
                    case NEXT_BUFFER: load_next_buffer(); continue;
                    case READONLY_BUFFER: load_readonly_buffer(); continue;
                    case SHORT_BINSTRING: load_counted_binstring(1); continue;
                    case BINSTRING: load_counted_binstring(4); continue;
                    case STRING: load_string(); continue;
                    case UNICODE: load_unicode(); continue;
                    case SHORT_BINUNICODE: load_counted_binunicode(1); continue;
                    case BINUNICODE: load_counted_binunicode(4); continue;
                    case BINUNICODE8: load_counted_binunicode(8); continue;
                    case EMPTY_TUPLE: load_counted_tuple(0); continue;
                    case TUPLE1: load_counted_tuple(1); continue;
                    case TUPLE2: load_counted_tuple(2); continue;
                    case TUPLE3: load_counted_tuple(3); continue;
                    case TUPLE: load_tuple(); continue;
                    case EMPTY_LIST: load_empty_list(); continue;
                    case LIST: load_list(); continue;
                    case EMPTY_DICT: load_empty_dict(); continue;
                    case DICT: load_dict(); continue;
                    case EMPTY_SET: load_empty_set(); continue;
                    case ADDITEMS: load_additems(); continue;
                    case FROZENSET: load_frozenset(); continue;
                    case OBJ: load_obj(); continue;
                    case INST: load_inst(); continue;
                    case NEWOBJ: load_newobj(false); continue;
                    case NEWOBJ_EX: load_newobj(true); continue;
                    case GLOBAL: load_global(); continue;
                    case STACK_GLOBAL: load_stack_global(); continue;
                    case APPEND: load_append(); continue;
                    case APPENDS: load_appends(); continue;
                    case BUILD: load_build(); continue;
                    case DUP: load_dup(); continue;
                    case BINGET: load_binget(); continue;
                    case LONG_BINGET: load_long_binget(); continue;
                    case GET: load_get(); continue;
                    case MARK: load_mark(); continue;
                    case BINPUT: load_binput(); continue;
                    case LONG_BINPUT: load_long_binput(); continue;
                    case PUT: load_put(); continue;
                    case MEMOIZE: load_memoize(); continue;
                    case POP: load_pop(); continue;
                    case POP_MARK: load_pop_mark(); continue;
                    case SETITEM: load_setitem(); continue;
                    case SETITEMS: load_setitems(); continue;
                    case PERSID: load_persid(); continue;
                    case BINPERSID: load_binpersid(); continue;
                    case REDUCE: load_reduce(); continue;
                    case PROTO: load_proto(); continue;
                    case FRAME: load_frame(); continue;
                    case EXT1: load_extension(1); continue;
                    case EXT2: load_extension(2); continue;
                    case EXT4: load_extension(4); continue;
                    case NEWTRUE: load_bool(PyBool.True); continue;
                    case NEWFALSE: load_bool(PyBool.False); continue;

                    case STOP:
                        break;

                    default:
                        {
                            byte c = s;
                            if (0x20 <= c && c <= 0x7e && c != '\'' && c != '\\')
                                throw Error(UnpicklingError, $"invalid load key, '{(char)c}'.");
                            throw Error(UnpicklingError, $"invalid load key, '\\x{c:x2}'.");
                        }
                }
                break; /* and we are done! */
            }

            _Unpickler_SkipConsumed();
            return Stack.Pdata_pop();
        }

        // _pickle_Unpickler_load_impl
        private PyObject _pickle_Unpickler_load_impl()
        {
            /* Check whether the Unpickler was initialized correctly. This prevents
               segfaulting if a subclass overridden __init__ with a function that does
               not call Unpickler.__init__(). Here, we simply ensure that self->read
               is not NULL. */
            if (Read == null)
                throw Error(UnpicklingError, $"Unpickler.__init__() was not called by {GetTypeName()}.__init__()");
            return load();
        }

        /* The name of find_class() is misleading. In newer pickle protocols, this
           function is used for loading any global (i.e., functions), not just
           classes. The name is kept only for backward compatibility. */
        // _pickle_Unpickler_find_class_impl
        private PyObject _pickle_Unpickler_find_class_impl(PyObject moduleName, PyObject globalName)
        {
            // PySys_Audit("pickle.find_class", "OO", module_name, global_name): sys has no audit hooks.

            /* Try to map the old names used in Python 2.x to the new ones used in
               Python 3.x.  We do this only with old pickle protocols and when the
               user has not disabled the feature. */
            if (Proto < 3 && FixImports)
            {
                /* Check if the global (i.e., a function or a class) was renamed
                   or moved to another module. */
                var key = PyTuple.From(moduleName, globalName);
                if (NameMapping2to3.TryGetStoredValue(key, out var item))
                {
                    if (item is not PyTuple pair || pair.Items.Length != 2)
                        throw PyRuntimeError.Create($"_compat_pickle.NAME_MAPPING values should be 2-tuples, not {item.GetTypeName()}");
                    moduleName = pair.Items[0];
                    globalName = pair.Items[1];
                    if (moduleName is not PyString || globalName is not PyString)
                        throw PyRuntimeError.Create($"_compat_pickle.NAME_MAPPING values should be pairs of str, not ({moduleName.GetTypeName()}, {globalName.GetTypeName()})");
                }
                else if (ImportMapping2to3.TryGetStoredValue(moduleName, out item))
                {
                    /* Check if the module was renamed. */
                    if (item is not PyString)
                        throw PyRuntimeError.Create($"_compat_pickle.IMPORT_MAPPING values should be strings, not {item.GetTypeName()}");
                    moduleName = item;
                }
            }

            /*
             * we don't use PyImport_GetModule here, because it can return partially-
             * initialised modules, which then cause the getattribute to fail.
             */
            var module = PyImportSystem.PyImportImport(moduleName);
            return getattribute(module, globalName, Proto >= 4);
        }

        // _pickle_Unpickler___sizeof___impl
        private long _pickle_Unpickler___sizeof___impl()
        {
            long res = GetPyType().Layout.BasicSize;
            if (Memo != null)
                res += MemoSize * 8;
            if (_marks != null)
                res += _marksSize * 8;
            if (_inputLine != null)
                res += CString(_inputLine).Length + 1;
            if (Encoding != null)
                res += System.Text.Encoding.UTF8.GetByteCount(Encoding) + 1;
            if (Errors != null)
                res += System.Text.Encoding.UTF8.GetByteCount(Errors) + 1;
            return res;
        }

        // Unpickler_clear
        private void Unpickler_clear()
        {
            Readline = null;
            Readinto = null;
            Read = null;
            Peek = null;
            Stack = null;
            PersFunc = null;
            Buffers = null;
            if (_hasBuffer)
            {
                _hasBuffer = false;
                _inputBuffer = null;
            }
            _Unpickler_MemoCleanup();
            _marks = null;
            _inputLine = null;
            Encoding = null;
            Errors = null;
        }

        /// <summary>
        /// _pickle.Unpickler.__init__(file, *, fix_imports=True, encoding='ASCII', errors='strict',
        /// buffers=()) (the clinic's _pickle_Unpickler___init__, then _pickle_Unpickler___init___impl)
        /// </summary>
        private void _pickle_Unpickler___init__(PyObject[] args, PyDict kwargs)
        {
            var a = PyArgs.Unpack("Unpickler", args, kwargs, new[] { "file", "fix_imports", "encoding", "errors", "buffers" }, 0, 1, 1);
            var file = a[0];
            bool fixImports = a[1] == null || PyObject_IsTrue(a[1]);
            string encoding = a[2] == null ? "ASCII" : PyArgs.ClinicStr("Unpickler", "argument 'encoding'", "str", a[2]);
            string errors = a[3] == null ? "strict" : PyArgs.ClinicStr("Unpickler", "argument 'errors'", "str", a[3]);
            var buffers = a[4];

            /* In case of multiple __init__() calls, clear previous content. */
            if (Read != null)
                Unpickler_clear();

            _Unpickler_SetInputStream(file);
            _Unpickler_SetInputEncoding(encoding, errors);
            _Unpickler_SetBuffers(buffers);
            FixImports = fixImports;

            init_method_ref(this, "persistent_load", out PersFunc, out PersFuncSelf);

            Stack = Pdata.Pdata_New();
            MemoSize = 32;
            Memo = _Unpickler_NewMemo(MemoSize);
            Proto = 0;
        }

        // Unpickler_get_memo
        private PyObject Unpickler_get_memo() => UnpicklerMemoProxyObject.UnpicklerMemoProxy_New(this);

        // Unpickler_set_memo
        private void Unpickler_set_memo(PyObject obj)
        {
            if (obj == null)
                throw PyTypeError.Create("attribute deletion is not supported");
            PyObject[] newMemo;
            long newMemoSize;
            if (obj.IsExactly(UnpicklerMemoProxyObject.UnpicklerMemoProxyType))
            {
                var unpickler = ((UnpicklerMemoProxyObject)obj).Unpickler;
                newMemoSize = unpickler.MemoSize;
                newMemo = _Unpickler_NewMemo(newMemoSize);
                for (long i = 0; i < newMemoSize && unpickler.Memo != null; i++)
                    newMemo[i] = unpickler.Memo[i];
            }
            else if (obj is PyDict dict)
            {
                newMemoSize = dict.Len();
                newMemo = _Unpickler_NewMemo(newMemoSize);
                int i = 0;
                while (dict.Next(ref i, out var key, out var value))
                {
                    if (key is not PyInt && key is not PyBool)
                        throw PyTypeError.Create("memo key must be integers");
                    long idx = PyArgs.LongAsSsize(key);
                    if (idx < 0)
                        throw PyValueError.Create("memo key must be positive integers.");
                    // As in the C code: the entry goes to the memo being replaced.
                    _Unpickler_MemoPut(idx, value);
                }
            }
            else
            {
                throw PyTypeError.Create($"'memo' attribute must be an UnpicklerMemoProxy object or dict, not {obj.GetTypeName()}");
            }
            _Unpickler_MemoCleanup();
            MemoSize = newMemoSize;
            Memo = newMemo;
        }

        // Unpickler_get_persload
        private PyObject Unpickler_get_persload()
        {
            if (PersFunc == null)
                throw PyAttributeError.Create("persistent_load");
            return reconstruct_method(PersFunc, PersFuncSelf);
        }

        // Unpickler_set_persload
        private void Unpickler_set_persload(PyObject value)
        {
            if (value == null)
                throw PyTypeError.Create("attribute deletion is not supported");
            if (!value.IsCallable())
                throw PyTypeError.Create("persistent_load must be a callable taking one argument");
            PersFuncSelf = null;
            PersFunc = value;
        }
    }

    /// <summary>
    /// UnpicklerMemoProxyObject (Modules/_pickle.c): the proxy Unpickler.memo answers, made from
    /// unpickler_memoproxy_spec.
    /// </summary>
    public sealed class UnpicklerMemoProxyObject : PyObject
    {
        internal static readonly PyType UnpicklerMemoProxyType = MakeType();

        internal UnpicklerObject Unpickler;

        private UnpicklerMemoProxyObject(PyType type)
        {
            if (!ReferenceEquals(type, UnpicklerMemoProxyType))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? UnpicklerMemoProxyType;

        private static PyType MakeType()
        {
            var type = new PyType("UnpicklerMemoProxy", new[] { PyType.ObjectType }, "_pickle");
            // unpicklerproxy_methods
            PyArgs.Define(type, "clear", PyCall.NoArgs, (self, args) => Self(self)._pickle_UnpicklerMemoProxy_clear_impl());
            PyArgs.Define(type, "copy", PyCall.NoArgs, (self, args) => Self(self)._pickle_UnpicklerMemoProxy_copy_impl());
            PyArgs.Define(type, "__reduce__", PyCall.NoArgs, (self, args) => Self(self)._pickle_UnpicklerMemoProxy___reduce___impl());
            // Py_tp_hash: PyObject_HashNotImplemented
            type.TypeDict["__hash__"] = PyNone.Instance;
            return type;
        }

        private static UnpicklerMemoProxyObject Self(PyObject self)
        {
            var proxy = self as UnpicklerMemoProxyObject
                ?? throw PyTypeError.Create($"descriptor requires a '_pickle.UnpicklerMemoProxy' object but received a '{self.GetTypeName()}'");
            if (proxy.Unpickler == null)
                throw PyValueError.Create("UnpicklerMemoProxy has no Unpickler");
            return proxy;
        }

        protected internal override long Hash() => throw PyTypeError.Create($"unhashable type: '{GetTypeName()}'");

        // UnpicklerMemoProxy_New
        internal static UnpicklerMemoProxyObject UnpicklerMemoProxy_New(UnpicklerObject unpickler) =>
            new UnpicklerMemoProxyObject(UnpicklerMemoProxyType) { Unpickler = unpickler };

        // _pickle_UnpicklerMemoProxy_clear_impl
        private PyObject _pickle_UnpicklerMemoProxy_clear_impl()
        {
            Unpickler.Memo = new PyObject[Unpickler.MemoSize];
            return PyNone.Instance;
        }

        // _pickle_UnpicklerMemoProxy_copy_impl: {index: obj}
        private PyObject _pickle_UnpicklerMemoProxy_copy_impl()
        {
            var newMemo = new PyDict();
            var memo = Unpickler.Memo;
            if (memo == null)
                return newMemo;
            for (long i = 0; i < Unpickler.MemoSize && i < memo.Length; i++)
            {
                var value = memo[i];
                if (value == null)
                    continue;
                newMemo.SetItem(PyInt.From(i), value);
            }
            return newMemo;
        }

        // _pickle_UnpicklerMemoProxy___reduce___impl: (dict, (contents,))
        private PyObject _pickle_UnpicklerMemoProxy___reduce___impl()
        {
            var contents = _pickle_UnpicklerMemoProxy_copy_impl();
            return PyTuple.From(PyType.DictType, PyTuple.From(contents));
        }
    }
}
