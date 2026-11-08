using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// Python/marshal.c, format version 4, CPython v3.12.15. CPython leaves FLAG_REF off an object
    /// whose reference count is 1. C# has no such count, so every object that can be shared gets the
    /// flag: what is written means the same and reads back the same, but the bytes may differ.
    /// </summary>
    internal static class MarshalModule
    {
        internal const int Version = 4;
        // MAX_MARSHAL_STACK_DEPTH
        private static readonly int MaxDepth =
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? 1000 : 2000;
        private const byte FlagRef = 0x80;
        // Include/internal/pycore_code.h: _PyLocals_Kind
        private const byte FastLocal = PyCodeObject.FastLocal, FastCell = PyCodeObject.FastCell, FastFree = PyCodeObject.FastFree;
        private const int FloatRepr = 17;

        internal static PyModule CreateMarshalModule()
        {
            var module = new PyModule("marshal");
            module.ModuleDict["version"] = PyInt.From(Version);
            module.ModuleDict["dumps"] = new PyBuiltinFunction("dumps", Dumps);
            module.ModuleDict["loads"] = new PyBuiltinFunction("loads", Loads);
            module.ModuleDict["dump"] = new PyBuiltinFunction("dump", Dump);
            module.ModuleDict["load"] = new PyBuiltinFunction("load", Load);
            return module;
        }

        private static int VersionArgument(PyObject[] args, int position, string name)
        {
            if (args.Length > position + 1)
                throw PyTypeError.Create($"{name} expected at most {position + 1} arguments, got {args.Length}");
            if (args.Length <= position)
                return Version;
            var index = PyObject.IndexOrThrow(args[position]);
            return (int)index.Value;
        }

        internal static byte[] Write(PyObject value, int version)
        {
            var writer = new Writer(version);
            writer.Object(value);
            return writer.ToArray();
        }

        /// <summary>One object out of part of a buffer, with no source lookup: a bundle's module.</summary>
        internal static PyObject Read(byte[] data, int offset, int length)
        {
            var part = new byte[length];
            Array.Copy(data, offset, part, 0, length);
            return new Reader(part) { FindSources = false }.ReadObject();
        }

        // marshal_dumps_impl
        private static PyObject Dumps(PyObject[] args)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("dumps expected at least 1 argument, got 0");
            return PyBytes.From(Write(args[0], VersionArgument(args, 1, "dumps")));
        }

        // marshal_dump_impl
        private static PyObject Dump(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create($"dump expected at least 2 arguments, got {args.Length}");
            var data = PyBytes.From(Write(args[0], VersionArgument(args, 2, "dump")));
            return args[1].GetAttribute("write").Call(new PyObject[] { data }, null);
        }

        // marshal_loads_impl
        private static PyObject Loads(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"loads() takes exactly one argument ({args.Length} given)");
            var data = BytesMethods.Buffer(args[0]) ??
                throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");
            return new Reader(data).ReadObject();
        }

        // marshal_load: read(0) proves the file gives bytes.
        private static PyObject Load(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"load() takes exactly one argument ({args.Length} given)");
            var data = args[0].GetAttribute("read").Call(new PyObject[] { PyInt.From(0) }, null);
            if (data is not PyBytes)
                throw PyTypeError.Create($"file.read() returned not bytes but {data.GetTypeName()}");
            return new Reader(args[0]).ReadObject();
        }


        private static byte[] Utf8(string text) =>
            ((PyBytes)PyString.From(text).GetAttribute("encode")
                .Call(new PyObject[] { PyString.From("utf8"), PyString.From("surrogatepass") }, null)).Value;

        private static string FromUtf8(byte[] data) =>
            ((PyString)PyBytes.From(data).GetAttribute("decode")
                .Call(new PyObject[] { PyString.From("utf8"), PyString.From("surrogatepass") }, null)).Value;

        private static bool IsAscii(string text)
        {
            foreach (char c in text)
                if (c >= 128)
                    return false;
            return true;
        }

        private sealed class Writer
        {
            private readonly List<byte> _output = new();
            private readonly Dictionary<PyObject, int> _refs = new(ReferenceEqualityComparer.Instance);
            private readonly int _version;
            private int _depth;

            internal Writer(int version) => _version = version;

            internal byte[] ToArray() => _output.ToArray();

            private void Byte(int value) => _output.Add((byte)value);
            private void Bytes(byte[] value) => _output.AddRange(value);

            private void Short(int value)
            {
                Byte(value & 0xff);
                Byte((value >> 8) & 0xff);
            }

            private void Long(int value)
            {
                for (int shift = 0; shift < 32; shift += 8)
                    Byte((value >> shift) & 0xff);
            }

            private void Double(double value) => Bytes(BitConverter.GetBytes(value));

            private void PString(byte[] value)
            {
                Long(value.Length);
                Bytes(value);
            }

            private void ShortPString(byte[] value)
            {
                Byte(value.Length);
                Bytes(value);
            }

            // w_type with W_TYPE's flag.
            private void Type(char code, byte flag) => Byte((byte)code | flag);

            // w_ref: true when a reference was written in place of the object.
            private bool Ref(PyObject value, ref byte flag)
            {
                if (_version < 3)
                    return false;
                if (_refs.TryGetValue(value, out int index))
                {
                    Byte('r');
                    Long(index);
                    return true;
                }
                _refs[value] = _refs.Count;
                flag = FlagRef;
                return false;
            }

            // w_object
            internal void Object(PyObject value)
            {
                if (_depth >= MaxDepth)
                    throw PyValueError.Create("object too deeply nested to marshal");
                _depth++;
                try
                {
                    switch (value)
                    {
                        case PyNone: Byte('N'); return;
                        case PyBool truth: Byte(truth.Value ? 'T' : 'F'); return;
                        case PyEllipsis: Byte('.'); return;
                    }
                    if (ReferenceEquals(value, PyType.StopIterationType))
                    {
                        Byte('S');
                        return;
                    }
                    byte flag = 0;
                    if (!Ref(value, ref flag))
                        Complex(value, flag);
                }
                finally
                {
                    _depth--;
                }
            }

            // w_complex_object
            private void Complex(PyObject value, byte flag)
            {
                if (value.IsExactly(PyType.IntType))
                    Integer(((PyInt)value).Value, flag);
                else if (value.IsExactly(PyType.FloatType))
                    Real(((PyFloat)value).Value, flag);
                else if (value.IsExactly(PyType.ComplexType))
                    ComplexNumber((PyComplex)value, flag);
                else if (value.IsExactly(PyType.BytesType))
                {
                    Type('s', flag);
                    PString(((PyBytes)value).Value);
                }
                else if (value.IsExactly(PyType.StrType))
                    Text(((PyString)value), flag);
                else if (value.IsExactly(PyType.TupleType))
                    Tuple((PyTuple)value, flag);
                else if (value.IsExactly(PyType.ListType))
                {
                    var items = ((PyList)value).Items;
                    Type('[', flag);
                    Long(items.Length);
                    foreach (var item in items)
                        Object(item);
                }
                else if (value.IsExactly(PyType.DictType))
                {
                    var dict = (PyDict)value;
                    Type('{', flag);
                    foreach (var key in dict.Keys().Items)
                    {
                        Object(key);
                        Object(dict.GetItem(key));
                    }
                    Byte('0');
                }
                else if (value.IsExactly(PyType.SetType) || value.IsExactly(PyType.FrozenSetType))
                    Set(value, flag);
                else if (value is PyCodeObject code)
                    Code(code, flag);
                else if (value.SupportsBuffer())
                {
                    Type('s', flag);
                    PString(BytesMethods.Buffer(value));
                }
                else
                    throw PyValueError.Create("unmarshallable object");
            }

            private static PyTuple InternedNames(IEnumerable<string> names)
            {
                var items = new List<PyObject>();
                foreach (var name in names)
                    items.Add(PySysFunction.Intern(PyString.From(name)));
                return PyTuple.From(items.ToArray());
            }

            private void Code(PyCodeObject code, byte flag)
            {
                // co_localsplusnames and co_localspluskinds. The CO_FAST_HIDDEN bit of inlined
                // comprehensions is not kept after compiling, so it is not written.
                var names = code.LocalsPlusNames;
                var kinds = code.LocalsPlusKinds;
                Type('c', flag);
                Long(code.ArgCount - code.KwonlyArgCount);
                Long(code.PosonlyArgCount);
                Long(code.KwonlyArgCount);
                Long(code.StackSize);
                Long(code.Flags);
                Object(PyBytes.From(code.Tables.Code));
                Object(PyTuple.From(code.Constants.ToArray()));
                Object(InternedNames(code.Names));
                Object(InternedNames(names));
                Object(PyBytes.From((byte[])kinds.Clone()));
                Object(PySysFunction.Intern((PyString)code.GetAttribute("co_filename")));
                Object(PySysFunction.Intern((PyString)code.GetAttribute("co_name")));
                Object(PySysFunction.Intern((PyString)code.GetAttribute("co_qualname")));
                Long(code.GetFirstLineNo());
                Object(PyBytes.From(code.Tables.LineTable));
                Object(PyBytes.From(code.Tables.ExceptionTable));
            }

            private void Integer(BigInteger number, byte flag)
            {
                if (number >= int.MinValue && number <= int.MaxValue)
                {
                    Type('i', flag);
                    Long((int)number);
                    return;
                }
                // w_PyLong: digits of 15 bits, the count carrying the sign.
                var digits = new List<int>();
                var rest = BigInteger.Abs(number);
                while (!rest.IsZero)
                {
                    digits.Add((int)(rest & 0x7fff));
                    rest >>= 15;
                }
                Type('l', flag);
                Long(number.Sign < 0 ? -digits.Count : digits.Count);
                foreach (int digit in digits)
                    Short(digit);
            }

            private static byte[] FloatText(double value)
            {
                string text = double.IsNaN(value) ? "nan"
                    : double.IsInfinity(value) ? "inf"
                    : PyFloatFormatter.DoubleToString(Math.Abs(value), 'g', FloatRepr, false);
                if (double.IsNegative(value) && !double.IsNaN(value))
                    text = "-" + text;
                return Encoding.ASCII.GetBytes(text);
            }

            private void Real(double value, byte flag)
            {
                if (_version > 1)
                {
                    Type('g', flag);
                    Double(value);
                }
                else
                {
                    Type('f', flag);
                    ShortPString(FloatText(value));
                }
            }

            private void ComplexNumber(PyComplex value, byte flag)
            {
                if (_version > 1)
                {
                    Type('y', flag);
                    Double(value.Real);
                    Double(value.Imag);
                }
                else
                {
                    Type('x', flag);
                    ShortPString(FloatText(value.Real));
                    ShortPString(FloatText(value.Imag));
                }
            }

            private void Text(PyString value, byte flag)
            {
                string text = value.Value;
                bool interned = _version >= 3 && PySysFunction.IsInterned(value);
                if (_version >= 4 && IsAscii(text))
                {
                    byte[] ascii = Encoding.ASCII.GetBytes(text);
                    if (text.Length < 256)
                    {
                        Type(interned ? 'Z' : 'z', flag);
                        ShortPString(ascii);
                    }
                    else
                    {
                        Type(interned ? 'A' : 'a', flag);
                        PString(ascii);
                    }
                }
                else
                {
                    Type(interned ? 't' : 'u', flag);
                    PString(Utf8(text));
                }
            }

            private void Tuple(PyTuple value, byte flag)
            {
                var items = value.Items;
                if (_version >= 4 && items.Length < 256)
                {
                    Type(')', flag);
                    Byte(items.Length);
                }
                else
                {
                    Type('(', flag);
                    Long(items.Length);
                }
                foreach (var item in items)
                    Object(item);
            }

            // w_complex_object: a set is written in the order of its marshalled items, so that
            // equal sets give equal bytes whatever their iteration order.
            private void Set(PyObject value, byte flag)
            {
                var pairs = new List<(byte[] Key, PyObject Item)>();
                foreach (var item in PySet.IterableItems(value))
                    pairs.Add((Write(item, _version), item));
                pairs.Sort((a, b) => a.Key.AsSpan().SequenceCompareTo(b.Key));
                Type(value is PyFrozenSet ? '>' : '<', flag);
                Long(pairs.Count);
                foreach (var pair in pairs)
                    Object(pair.Item);
            }
        }

        private sealed class Reader
        {
            private readonly byte[] _data;
            private readonly PyObject _file;
            private readonly List<PyObject> _refs = new();
            private int _position;
            private int _depth;

            internal Reader(byte[] data) => _data = data;

            internal Reader(PyObject file) => _file = file;

            // r_string
            private byte[] Take(int count)
            {
                if (_file == null)
                {
                    if (_data.Length - _position < count)
                        throw PyEOFError.Create("marshal data too short");
                    var part = new byte[count];
                    Array.Copy(_data, _position, part, 0, count);
                    _position += count;
                    return part;
                }
                var buffer = new PyByteArray(new byte[count]);
                var answer = _file.GetAttribute("readinto").Call(new PyObject[] { buffer }, null);
                var read = PyObject.TryGetIndex(answer) ??
                    throw PyValueError.Create($"'{answer.GetTypeName()}' object cannot be interpreted as an integer");
                if (read.Value > count)
                    throw PyValueError.Create($"read() returned too much data: {count} bytes requested, {read.Value} returned");
                if (read.Value < count)
                    throw PyEOFError.Create("EOF read where not expected");
                return BytesMethods.Buffer(buffer);
            }

            private int Byte()
            {
                if (_file != null)
                    return Take(1)[0];
                if (_position >= _data.Length)
                    throw PyEOFError.Create("EOF read where not expected");
                return _data[_position++];
            }

            private int Short()
            {
                var part = Take(2);
                return part[0] | part[1] << 8;
            }

            private int Long() => BitConverter.ToInt32(Take(4), 0);

            private long Long64() => BitConverter.ToInt64(Take(8), 0);

            private double Double() => BitConverter.ToDouble(Take(8), 0);

            private int Size(string kind)
            {
                int size = Long();
                if (size < 0)
                    throw PyValueError.Create($"bad marshal data ({kind} size out of range)");
                return size;
            }

            // r_PyLong
            private BigInteger LongInteger()
            {
                int count = Long();
                if (count == int.MinValue)
                    throw PyValueError.Create("bad marshal data (long size out of range)");
                int digits = Math.Abs(count);
                var value = BigInteger.Zero;
                for (int i = 0; i < digits; i++)
                {
                    int digit = Short();
                    if (digit >= 0x8000)
                        throw PyValueError.Create("bad marshal data (digit out of range in long)");
                    if (digit == 0 && i == digits - 1)
                        throw PyValueError.Create("bad marshal data (unnormalized long data)");
                    value |= (BigInteger)digit << (15 * i);
                }
                return count < 0 ? -value : value;
            }

            private double FloatText() =>
                ((PyFloat)PyType.FloatType.Call(new PyObject[] { PyString.From(Encoding.ASCII.GetString(Take(Byte()))) }, null)).Value;

            // r_ref_reserve: a container takes its index before its contents.
            private int Reserve(bool flagged)
            {
                if (!flagged)
                    return -1;
                _refs.Add(null);
                return _refs.Count - 1;
            }

            private PyObject Keep(PyObject value, bool flagged)
            {
                if (flagged)
                    _refs.Add(value);
                return value;
            }

            private PyObject Insert(PyObject value, int index)
            {
                if (index >= 0)
                    _refs[index] = value;
                return value;
            }

            private PyObject Required(string container) =>
                Item() ?? throw PyTypeError.Create($"NULL object in marshal data for {container}");

            private static PyString Intern(string text) => PySysFunction.Intern(PyString.From(text));

            private PyObject Text(int size, bool ascii, bool interned, bool flagged)
            {
                var data = Take(size);
                string text = ascii ? Encoding.Latin1.GetString(data) : FromUtf8(data);
                return Keep(interned ? Intern(text) : PyString.From(text), flagged);
            }

            // read_object
            internal PyObject ReadObject() =>
                Item() ?? throw PyTypeError.Create("NULL object in marshal data for object");

            // r_object: null is TYPE_NULL.
            private PyObject Item()
            {
                int code;
                try
                {
                    code = Byte();
                }
                catch (PythonException error) when (error.PyException is PyEOFError)
                {
                    throw PyEOFError.Create("EOF read where object expected");
                }
                if (_depth >= MaxDepth)
                    throw PyValueError.Create("recursion limit exceeded");
                _depth++;
                try
                {
                    return Decode((char)(code & 0x7f), (code & FlagRef) != 0);
                }
                finally
                {
                    _depth--;
                }
            }

            private PyObject Decode(char type, bool flagged)
            {
                switch (type)
                {
                    case '0': return null;
                    case 'N': return PyNone.Instance;
                    case 'S': return PyType.StopIterationType;
                    case '.': return PyEllipsis.Instance;
                    case 'F': return PyBool.False;
                    case 'T': return PyBool.True;
                    case 'i': return Keep(PyInt.From(Long()), flagged);
                    case 'I': return Keep(PyInt.From(Long64()), flagged);
                    case 'l': return Keep(PyInt.From(LongInteger()), flagged);
                    case 'f': return Keep(new PyFloat(FloatText()), flagged);
                    case 'g': return Keep(new PyFloat(Double()), flagged);
                    case 'x':
                    {
                        double real = FloatText();
                        return Keep(new PyComplex(real, FloatText()), flagged);
                    }
                    case 'y':
                    {
                        double real = Double();
                        return Keep(new PyComplex(real, Double()), flagged);
                    }
                    case 's': return Keep(PyBytes.From(Take(Size("bytes object"))), flagged);
                    case 'a': return Text(Size("string"), true, false, flagged);
                    case 'A': return Text(Size("string"), true, true, flagged);
                    case 'z': return Text(Byte(), true, false, flagged);
                    case 'Z': return Text(Byte(), true, true, flagged);
                    case 'u': return Text(Size("string"), false, false, flagged);
                    case 't': return Text(Size("string"), false, true, flagged);
                    case ')': return Tuple(Byte(), flagged);
                    case '(': return Tuple(Size("tuple"), flagged);
                    case '[':
                    {
                        int size = Size("list");
                        var list = new PyList(new List<PyObject>());
                        Keep(list, flagged);
                        for (int i = 0; i < size; i++)
                            list.Append(Required("list"));
                        return list;
                    }
                    case '{':
                    {
                        var dict = new PyDict();
                        Keep(dict, flagged);
                        while (true)
                        {
                            var key = Item();
                            if (key == null)
                                return dict;
                            var value = Item();
                            if (value == null)
                                return dict;
                            dict.SetItem(key, value);
                        }
                    }
                    case '<':
                    case '>':
                        return Set(type == '>', flagged);
                    case 'c':
                        return Code(flagged);
                    case 'r':
                    {
                        int index = Long();
                        if (index < 0 || index >= _refs.Count || _refs[index] == null)
                            throw PyValueError.Create("bad marshal data (invalid reference)");
                        return _refs[index];
                    }
                    default:
                        throw PyValueError.Create("bad marshal data (unknown type code)");
                }
            }

            private PyObject Field(Type wanted)
            {
                var value = Required("code object");
                if (!wanted.IsInstanceOfType(value))
                    throw PyTypeError.Create("bad marshal data (code object field)");
                return value;
            }

            private static List<string> Names(PyObject tuple)
            {
                var names = new List<string>();
                foreach (var item in ((PyTuple)tuple).Items)
                    names.Add(((PyString)item).Value);
                return names;
            }

            private readonly Dictionary<string, List<string>> _sources = new();

            internal bool FindSources { get; init; } = true;

            // A .pyc keeps no source: the lines come from the file, as linecache finds them.
            private List<string> SourceOf(string fileName)
            {
                if (!FindSources)
                    return null;
                if (_sources.TryGetValue(fileName, out var lines))
                    return lines;
                try
                {
                    lines = new List<string>(System.IO.File.ReadAllLines(fileName));
                }
                catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    lines = null;
                }
                return _sources[fileName] = lines;
            }

            // r_object, TYPE_CODE
            private PyObject Code(bool flagged)
            {
                int index = Reserve(flagged);
                int argCount = Long(), posonly = Long(), kwonly = Long(), stackSize = Long(), flags = Long();
                var code = (PyBytes)Field(typeof(PyBytes));
                var constants = (PyTuple)Field(typeof(PyTuple));
                var names = Names(Field(typeof(PyTuple)));
                var localNames = Names(Field(typeof(PyTuple)));
                var kinds = ((PyBytes)Field(typeof(PyBytes))).Value;
                string fileName = ((PyString)Field(typeof(PyString))).Value;
                string name = ((PyString)Field(typeof(PyString))).Value;
                string qualName = ((PyString)Field(typeof(PyString))).Value;
                int firstLineNo = Long();
                var lineTable = (PyBytes)Field(typeof(PyBytes));
                var exceptionTable = (PyBytes)Field(typeof(PyBytes));
                if (kinds.Length != localNames.Count)
                    throw PyValueError.Create("bad marshal data (co_localspluskinds size)");
                var varNames = new List<string>();
                var cellVars = new List<string>();
                var freeVars = new List<string>();
                for (int i = 0; i < kinds.Length; i++)
                {
                    if ((kinds[i] & FastLocal) != 0) varNames.Add(localNames[i]);
                    if ((kinds[i] & FastCell) != 0) cellVars.Add(localNames[i]);
                    if ((kinds[i] & FastFree) != 0) freeVars.Add(localNames[i]);
                }
                return Insert(PyCodeObject.FromTables(argCount, posonly, kwonly, stackSize, flags, code.Value,
                    new List<PyObject>(constants.Items), names, varNames, freeVars, cellVars, fileName, name, qualName,
                    firstLineNo, lineTable.Value, exceptionTable.Value, SourceOf(fileName)), index);
            }

            private PyObject Tuple(int size, bool flagged)
            {
                // R_REF before the items: an item may refer back to the tuple.
                var items = new PyObject[size];
                Array.Fill(items, PyNone.Instance);
                var tuple = PyTuple.From(items);
                Keep(tuple, flagged);
                for (int i = 0; i < size; i++)
                    items[i] = Required("tuple");
                return tuple;
            }

            private PyObject Set(bool frozen, bool flagged)
            {
                int size = Size("set");
                if (!frozen)
                {
                    var set = new PySet();
                    Keep(set, flagged);
                    for (int i = 0; i < size; i++)
                        set.Add(Required("set"));
                    return set;
                }
                int index = Reserve(flagged);
                var items = new List<PyObject>(size);
                for (int i = 0; i < size; i++)
                    items.Add(Required("set"));
                return Insert(new PyFrozenSet(items), index);
            }
        }
    }
}
