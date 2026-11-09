using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// The objects of Modules/_sre/sre.c over the engine in SreEngine.cs. Lib/re compiles a pattern to opcodes, and
    /// these run them, so a pattern means what it means in CPython. Text is an int per character or byte, which
    /// keeps positions in code points as Python counts them.
    /// </summary>
    internal sealed class SreText
    {
        internal readonly PyObject Source;
        internal readonly int[] Items;
        internal readonly bool IsBytes;

        [ThreadStatic] private static string _lastString;
        [ThreadStatic] private static int[] _lastItems;

        private SreText(PyObject source, int[] items, bool isBytes)
        {
            Source = source;
            Items = items;
            IsBytes = isBytes;
        }

        internal static SreText Of(PyObject source)
        {
            switch (source)
            {
                case PyString text:
                    return new SreText(source, CodePoints(text.Value), false);
                case PyBytes bytes:
                    return new SreText(source, Widen(bytes.Value), true);
                case PyByteArray array:
                    return new SreText(source, Widen(array.ToByteArrayCopy()), true);
                default:
                    if (BytesMethods.Buffer(source) is { } exported)
                        return new SreText(source, Widen(exported), true);
                    throw PyTypeError.Create($"expected string or bytes-like object, got '{source.GetTypeName()}'");
            }
        }

        private static int[] Widen(byte[] data)
        {
            var items = new int[data.Length];
            for (int i = 0; i < data.Length; i++) items[i] = data[i];
            return items;
        }

        private static int[] CodePoints(string text)
        {
            // A tokenizer calls match on one string many times.
            if (ReferenceEquals(text, _lastString)) return _lastItems;
            var items = new int[text.Length];
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    items[count++] = char.ConvertToUtf32(c, text[++i]);
                else
                    items[count++] = c;
            }
            if (count != items.Length) Array.Resize(ref items, count);
            _lastString = text;
            _lastItems = items;
            return items;
        }

        /// <summary>getslice: a run of the subject, as the subject's own type.</summary>
        internal PyObject Slice(int start, int end)
        {
            if (start < 0) start = 0;
            if (end > Items.Length) end = Items.Length;
            if (end < start) end = start;
            if (start == 0 && end == Items.Length && Source is PyString or PyBytes) return Source;
            if (IsBytes && Source is not PyBytes)
            {
                // match_getslice_by_index's getstring: a mutable buffer is read as it is now, and the
                // bounds clipped to its length.
                var current = BytesMethods.Buffer(Source) ?? Array.Empty<byte>();
                int from = Math.Min(start, current.Length), to = Math.Min(end, current.Length);
                return PyBytes.From(current.AsSpan(from, to - from).ToArray());
            }
            if (IsBytes)
            {
                var data = new byte[end - start];
                for (int i = start; i < end; i++) data[i - start] = (byte)Items[i];
                return PyBytes.From(data);
            }
            var builder = new StringBuilder(end - start);
            for (int i = start; i < end; i++)
            {
                int item = Items[i];
                if (item < 0x10000) builder.Append((char)item);
                else builder.Append(char.ConvertFromUtf32(item));
            }
            return PyString.From(builder.ToString());
        }

        internal PyObject Empty() => IsBytes ? PyBytes.From(Array.Empty<byte>()) : PyString.From("");
    }

    public sealed class PySrePattern : PyObject
    {
        internal readonly PyObject Pattern;
        internal readonly int Flags;
        internal readonly uint[] Code;
        internal readonly int Groups;
        internal readonly PyDict GroupIndex;
        internal readonly PyObject[] IndexGroup;
        private readonly int _isBytes;

        internal PySrePattern(PyObject pattern, int flags, uint[] code, int groups, PyDict groupIndex, PyObject[] indexGroup)
        {
            Pattern = pattern;
            Flags = flags;
            Code = code;
            Groups = groups;
            GroupIndex = groupIndex;
            IndexGroup = indexGroup;
            _isBytes = pattern is PyNone ? -1 : pattern is PyBytes or PyByteArray ? 1 : 0;
        }

        public override PyType GetPyType() => _SreModule.PyPatternType;

        // state_init: pos and endpos clamp to the subject, and the two kinds may not mix.
        internal SreState NewState(SreText text, int pos, int endpos)
        {
            if (_isBytes == 0 && text.IsBytes)
                throw PyTypeError.Create("cannot use a string pattern on a bytes-like object");
            if (_isBytes == 1 && !text.IsBytes)
                throw PyTypeError.Create("cannot use a bytes pattern on a string-like object");
            int length = text.Items.Length;
            pos = Math.Clamp(pos, 0, length);
            endpos = Math.Clamp(endpos, 0, length);
            var state = new SreState(text.Items, pos, endpos, Code, Groups, (Flags & 256) != 0, (Flags & 32) != 0);
            return state;
        }

        private static PyObject Argument(PyObject[] args, PyDict kwargs, int position, string name)
        {
            if (args.Length > position) return args[position];
            return kwargs != null && kwargs.TryGetStoredValue(PyString.From(name), out var value) ? value : null;
        }

        private static int Position(PyObject value, int fallback)
        {
            if (value == null) return fallback;
            // pos and endpos: Py_ssize_t, clamped to what a CLR string can hold
            return PyArgs.SsizeCount(value);
        }

        private PyObject Run(PyObject[] args, PyDict kwargs, string name, Func<SreState, bool> engine)
        {
            var subject = Argument(args, kwargs, 0, "string")
                ?? throw PyArgs.MissingArgument(name, "string", 1);
            var text = SreText.Of(subject);
            var state = NewState(text, Position(Argument(args, kwargs, 1, "pos"), 0),
                Position(Argument(args, kwargs, 2, "endpos"), int.MaxValue));
            state.Ptr = state.Start;
            return engine(state) ? NewMatch(text, state) : PyNone.Instance;
        }

        // pattern_new_match.
        // getstring and pattern_subx's literal test: a str, or any buffer, with no backslash.
        private static bool IsLiteralTemplate(PyObject template)
        {
            if (template is PyString text)
                return text.Value.IndexOf('\\') < 0;
            var data = BytesMethods.Buffer(template);
            return data != null && Array.IndexOf(data, (byte)'\\') < 0;
        }

        /// <summary>
        /// compile_template (Modules/_sre/sre.c): re._compile_template, and once more with the
        /// template as its basic type (str or bytes) when it was unhashable (a bytearray).
        /// </summary>
        internal static PyObject CompileTemplate(PyObject pattern, PyObject template)
        {
            var compile = PyImportSystem.Import("re").GetAttribute("_compile_template");
            try
            {
                return compile.Call(new PyObject[] { pattern, template }, null);
            }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                PyObject basic;
                if (template is PyString text && !template.IsExactly(PyType.StrType))
                    basic = PyString.From(text.Value);
                else if (template.SupportsBuffer() && !template.IsExactly(PyType.BytesType))
                    basic = PyBytes.From(BytesMethods.RequireBuffer(template));
                else
                    throw;
                return compile.Call(new PyObject[] { pattern, basic }, null);
            }
        }

        internal PySreMatch NewMatch(SreText text, SreState state)
        {
            var marks = new int[2 * (Groups + 1)];
            marks[0] = state.Start;
            marks[1] = state.Ptr;
            for (int i = 0, j = 0; i < Groups; i++, j += 2)
            {
                bool set = j + 1 <= state.LastMark && state.Mark[j] >= 0 && state.Mark[j + 1] >= 0;
                marks[j + 2] = set ? state.Mark[j] : -1;
                marks[j + 3] = set ? state.Mark[j + 1] : -1;
            }
            return new PySreMatch(this, text, marks, state.LastIndex, state.Pos, state.EndPos);
        }

        // state_getslice.
        private static PyObject GroupSlice(SreText text, SreState state, int group, bool emptyForMissing)
        {
            int index = (group - 1) * 2;
            if (index >= state.LastMark || state.Mark[index] < 0 || state.Mark[index + 1] < 0)
                return emptyForMissing ? text.Empty() : PyNone.Instance;
            return text.Slice(state.Mark[index], state.Mark[index + 1]);
        }

        private PyObject FindAll(PyObject[] args, PyDict kwargs)
        {
            var text = SreText.Of(Argument(args, kwargs, 0, "string"));
            var state = NewState(text, Position(Argument(args, kwargs, 1, "pos"), 0),
                Position(Argument(args, kwargs, 2, "endpos"), int.MaxValue));
            var result = new PyList();
            while (state.Start <= state.End)
            {
                state.Reset();
                state.Ptr = state.Start;
                if (!state.Search(0)) break;
                if (Groups == 0)
                {
                    result.Append(text.Slice(state.Start, state.Ptr));
                }
                else if (Groups == 1)
                {
                    result.Append(GroupSlice(text, state, 1, true));
                }
                else
                {
                    var items = new PyObject[Groups];
                    for (int i = 0; i < Groups; i++) items[i] = GroupSlice(text, state, i + 1, true);
                    result.Append(PyTuple.From(items));
                }
                state.MustAdvance = state.Ptr == state.Start;
                state.Start = state.Ptr;
            }
            return result;
        }

        private PyObject Split(PyObject[] args, PyDict kwargs)
        {
            var text = SreText.Of(Argument(args, kwargs, 0, "string"));
            int maxSplit = Position(Argument(args, kwargs, 1, "maxsplit"), 0);
            var state = NewState(text, 0, int.MaxValue);
            var result = new PyList();
            int count = 0, last = 0;
            while (maxSplit == 0 || count < maxSplit)
            {
                state.Reset();
                state.Ptr = state.Start;
                if (!state.Search(0)) break;
                result.Append(text.Slice(last, state.Start));
                for (int i = 1; i <= Groups; i++) result.Append(GroupSlice(text, state, i, false));
                count++;
                state.MustAdvance = state.Ptr == state.Start;
                last = state.Start = state.Ptr;
            }
            result.Append(text.Slice(last, text.Items.Length));
            return result;
        }

        private (PyObject result, int count) Substitute(PyObject[] args, PyDict kwargs)
        {
            var replacement = Argument(args, kwargs, 0, "repl");
            var text = SreText.Of(Argument(args, kwargs, 1, "string"));
            int limit = Position(Argument(args, kwargs, 2, "count"), 0);

            // pattern_subx: a function, a literal, or a template made by re._compile_template.
            bool callable = replacement.IsCallable();
            PySreTemplate template = null;
            PyObject literal = null;
            if (!callable)
            {
                if (!IsLiteralTemplate(replacement))
                {
                    var compiled = CompileTemplate(this, replacement);
                    template = compiled as PySreTemplate;
                    if (template == null) literal = compiled;
                }
                else
                {
                    literal = replacement;
                }
            }

            var state = NewState(text, 0, int.MaxValue);
            var pieces = new List<PyObject>();
            int count = 0, last = 0;
            while (limit == 0 || count < limit)
            {
                state.Reset();
                state.Ptr = state.Start;
                if (!state.Search(0)) break;
                int start = state.Start, end = state.Ptr;
                if (last < start) pieces.Add(text.Slice(last, start));
                if (literal != null)
                {
                    pieces.Add(literal);
                }
                else
                {
                    var match = NewMatch(text, state);
                    var item = template != null ? template.Expand(match) : replacement.Call(new PyObject[] { match }, null);
                    if (item is not PyNone) pieces.Add(item);
                }
                last = end;
                count++;
                state.MustAdvance = state.Ptr == state.Start;
                state.Start = state.Ptr;
            }
            if (last < text.Items.Length) pieces.Add(text.Slice(last, text.Items.Length));
            return (Join(pieces, text), count);
        }

        // pattern_subx's join: _PyBytes_Join or PyUnicode_Join with an empty separator, the joins
        // bytes.join and str.join are.
        private static PyObject Join(List<PyObject> pieces, SreText text) => text.IsBytes
            ? BytesMethods.Join(PyBytes.From(System.Array.Empty<byte>()), new PyList(pieces))
            : PyString.From("").Join(new PyList(pieces));

        private PyObject Scanner(PyObject[] args, PyDict kwargs)
        {
            var text = SreText.Of(Argument(args, kwargs, 0, "string"));
            return new PySreScanner(this, text, Position(Argument(args, kwargs, 1, "pos"), 0),
                Position(Argument(args, kwargs, 2, "endpos"), int.MaxValue));
        }

        // pattern_methods (Modules/_sre/sre.c): what the type's method descriptors call; the
        // argument conventions are the clinic's, checked by the descriptor (PyArity).
        internal PyObject MatchMethod(PyObject[] a, PyDict k) => Run(a, k, "match", state => state.Match(0, true));
        internal PyObject FullMatchMethod(PyObject[] a, PyDict k) => Run(a, k, "fullmatch", state =>
        {
            state.MatchAll = true;
            return state.Match(0, true);
        });
        internal PyObject SearchMethod(PyObject[] a, PyDict k) => Run(a, k, "search", state => state.Search(0));
        internal PyObject FindAllMethod(PyObject[] a, PyDict k) => FindAll(a, k);
        internal PyObject FindIterMethod(PyObject[] a, PyDict k) => new PySreIterator((PySreScanner)Scanner(a, k));
        internal PyObject SplitMethod(PyObject[] a, PyDict k) => Split(a, k);
        internal PyObject SubMethod(PyObject[] a, PyDict k) => Substitute(a, k).result;
        internal PyObject SubnMethod(PyObject[] a, PyDict k)
        {
            var (result, count) = Substitute(a, k);
            return PyTuple.From(new[] { result, PyInt.From(count) });
        }
        internal PyObject ScannerMethod(PyObject[] a, PyDict k) => Scanner(a, k);

        // pattern_members and pattern_getset.
        internal PyObject Read(string name)
        {
            switch (name)
            {
                case "pattern":
                    return Pattern;
                case "flags":
                    return PyInt.From(Flags);
                case "groups":
                    return PyInt.From(Groups);
                case "groupindex":
                {
                    var names = new Dictionary<string, PyObject>();
                    foreach (var key in GroupIndex.Keys().Items) names[key.ToStr().Value] = GroupIndex.GetItem(key);
                    return new PyMappingProxy(names);
                }
            }
            return base.TpGetattro(name);
        }

        // pattern_repr.
        protected internal override PyString Repr()
        {
            var flags = new List<string>();
            int remaining = Flags;
            if (_isBytes == 0 && (remaining & (4 | 32 | 256)) == 32) remaining &= ~32;
            foreach (var (mask, label) in new[] { (2, "re.IGNORECASE"), (4, "re.LOCALE"), (8, "re.MULTILINE"), (16, "re.DOTALL"),
                         (32, "re.UNICODE"), (64, "re.VERBOSE"), (128, "re.DEBUG"), (256, "re.ASCII") })
            {
                if ((remaining & mask) == 0) continue;
                flags.Add(label);
                remaining &= ~mask;
            }
            if (remaining != 0) flags.Add($"0x{remaining:x}");
            string text = Pattern.ToRepr().Value;
            if (text.Length > 200) text = text.Substring(0, 200);
            return PyString.From(flags.Count == 0 ? $"re.compile({text})" : $"re.compile({text}, {string.Join("|", flags)})");
        }

        protected internal override PyString Str() => ToRepr();

        public override int ToHash() => HashCode.Combine(Flags, Code.Length, Pattern.ToHash());

        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (op != CompareOp.EQ && op != CompareOp.NE || other is not PySrePattern pattern)
                return PyNotImplemented.Instance;
            bool same = ReferenceEquals(this, other) || (Flags == pattern.Flags && Code.AsSpan().SequenceEqual(pattern.Code)
                && _isBytes == pattern._isBytes && PyObject.CompareBool(Pattern, pattern.Pattern, PyObject.CompareOp.EQ));
            return PyBool.FromBool(op == CompareOp.EQ ? same : !same);
        }
    }

    public sealed class PySreMatch : PyObject
    {
        private readonly PySrePattern _pattern;
        private readonly SreText _text;
        private readonly int[] _marks;
        private readonly int _lastIndex;
        private readonly int _pos;
        private readonly int _endPos;

        internal PySreMatch(PySrePattern pattern, SreText text, int[] marks, int lastIndex, int pos, int endPos)
        {
            _pattern = pattern;
            _text = text;
            _marks = marks;
            _lastIndex = lastIndex;
            _pos = pos;
            _endPos = endPos;
        }

        public override PyType GetPyType() => _SreModule.PyMatchType;

        // match_getslice_by_index and match_getindex.
        private int GroupNumber(PyObject group)
        {
            // match_getindex: an index is PyNumber_AsSsize_t(index, NULL), clipped; anything else is a
            // name of groupindex, whose value must be an int.
            long number = -1;
            if (PyObject.IndexCheck(group))
                number = PyArgs.AsSsize(group, PyArgs.SsizeError.Clip);
            else if (_pattern.GroupIndex.TryGetStoredValue(group, out var found) && PyInt.LongCheck(found))
                number = PyArgs.Ssize(found);
            if (number < 0 || number > _pattern.Groups)
                throw PyIndexError.Create("no such group");
            return (int)number;
        }

        private PyObject GroupValue(int group, PyObject missing)
            => _marks[group * 2] < 0 || _marks[group * 2 + 1] < 0
                ? missing
                : _text.Slice(_marks[group * 2], _marks[group * 2 + 1]);

        internal PyObject GroupOrEmpty(int group) => GroupValue(group, _text.Empty());

        protected internal override PyObject MpSubscript(PyObject index) => GroupValue(GroupNumber(index), PyNone.Instance);

        private PyObject Group(PyObject[] args)
        {
            if (args.Length == 0) return GroupValue(0, PyNone.Instance);
            if (args.Length == 1) return GetItem(args[0]);
            var items = new PyObject[args.Length];
            for (int i = 0; i < args.Length; i++) items[i] = GetItem(args[i]);
            return PyTuple.From(items);
        }

        private static PyObject Default(PyObject[] args, PyDict kwargs)
        {
            if (args.Length > 0) return args[0];
            return kwargs != null && kwargs.TryGetStoredValue(PyString.From("default"), out var value) ? value : PyNone.Instance;
        }

        private PyObject Groups(PyObject[] args, PyDict kwargs)
        {
            var fallback = Default(args, kwargs);
            var items = new PyObject[_pattern.Groups];
            for (int i = 0; i < items.Length; i++) items[i] = GroupValue(i + 1, fallback);
            return PyTuple.From(items);
        }

        private PyObject GroupDict(PyObject[] args, PyDict kwargs)
        {
            var fallback = Default(args, kwargs);
            var result = new PyDict();
            foreach (var key in _pattern.GroupIndex.Keys().Items)
                result.SetItem(key, GroupValue(_pattern.GroupIndex.GetItem(key).ToInt(), fallback));
            return result;
        }

        private int Boundary(PyObject[] args, int offset)
            => _marks[(args.Length > 0 ? GroupNumber(args[0]) : 0) * 2 + offset];

        // match_methods: what the type's method descriptors call.
        internal PyObject GroupMethod(PyObject[] a) => Group(a);
        internal PyObject GroupsMethod(PyObject[] a, PyDict k) => Groups(a, k);
        internal PyObject GroupDictMethod(PyObject[] a, PyDict k) => GroupDict(a, k);
        internal PyObject StartMethod(PyObject[] a) => PyInt.From(Boundary(a, 0));
        internal PyObject EndMethod(PyObject[] a) => PyInt.From(Boundary(a, 1));
        internal PyObject SpanMethod(PyObject[] a) =>
            PyTuple.From(new PyObject[] { PyInt.From(Boundary(a, 0)), PyInt.From(Boundary(a, 1)) });
        internal PyObject ExpandMethod(PyObject[] a, PyDict k)
        {
            var compiled = PySrePattern.CompileTemplate(_pattern, k != null && k.Length() > 0 ? k.GetItem(PyString.From("template")) : a[0]);
            return compiled is PySreTemplate template ? template.Expand(this) : compiled;
        }

        // match_members and match_getset.
        internal PyObject Read(string name)
        {
            switch (name)
            {
                case "re":
                    return _pattern;
                case "string":
                    return _text.Source;
                case "pos":
                    return PyInt.From(_pos);
                case "endpos":
                    return PyInt.From(_endPos);
                case "lastindex":
                    return _lastIndex > 0 ? PyInt.From(_lastIndex) : PyNone.Instance;
                case "lastgroup":
                    return _lastIndex > 0 && _lastIndex < _pattern.IndexGroup.Length
                        ? _pattern.IndexGroup[_lastIndex] : PyNone.Instance;
                case "regs":
                {
                    var spans = new PyObject[_pattern.Groups + 1];
                    for (int i = 0; i < spans.Length; i++)
                        spans[i] = PyTuple.From(new PyObject[] { PyInt.From(_marks[i * 2]), PyInt.From(_marks[i * 2 + 1]) });
                    return PyTuple.From(spans);
                }
            }
            return base.TpGetattro(name);
        }

        // match_repr: at most fifty characters of repr.
        protected internal override PyString Repr()
        {
            string text = GroupValue(0, PyNone.Instance).ToRepr().Value;
            if (text.Length > 50) text = text.Substring(0, 50);
            return PyString.From($"<re.Match object; span=({_marks[0]}, {_marks[1]}), match={text}>");
        }

        protected internal override PyString Str() => ToRepr();
        protected internal override bool Bool() => true;
    }

    /// <summary>The compiled form of a replacement: literal, then (group, literal) pairs.</summary>
    public sealed class PySreTemplate : PyObject
    {
        private readonly PySrePattern _pattern;
        private readonly PyObject[] _items;

        internal PySreTemplate(PySrePattern pattern, PyObject[] items)
        {
            _pattern = pattern;
            _items = items;
        }


        // expand_template.
        internal PyObject Expand(PySreMatch match)
        {
            bool isBytes = _pattern.Pattern is PyBytes or PyByteArray;
            var builder = new StringBuilder();
            var bytes = new List<byte>();
            void Add(PyObject piece)
            {
                if (piece is PyNone) return;
                if (isBytes) bytes.AddRange(((PyBytes)piece).Value);
                else builder.Append(piece.ToStr().Value);
            }

            Add(_items[0]);
            for (int i = 1; i + 1 < _items.Length; i += 2)
            {
                Add(match.GroupOrEmpty(_items[i].ToInt()));
                Add(_items[i + 1]);
            }
            return isBytes ? PyBytes.From(bytes.ToArray()) : PyString.From(builder.ToString());
        }
    }

    /// <summary>scanner_search and scanner_match: one state that moves on after each result.</summary>
    public sealed class PySreScanner : PyObject
    {
        private readonly PySrePattern _pattern;
        private readonly SreText _text;
        private readonly SreState _state;
        private bool _done;

        internal PySreScanner(PySrePattern pattern, SreText text, int pos, int endpos)
        {
            _pattern = pattern;
            _text = text;
            _state = pattern.NewState(text, pos, endpos);
        }

        public override PyType GetPyType() => _SreModule.ScannerType;

        internal PyObject Step(bool search)
        {
            if (_done) return PyNone.Instance;
            _state.Reset();
            _state.Ptr = _state.Start;
            bool found = search ? _state.Search(0) : _state.Match(0, true);
            PyObject result = found ? _pattern.NewMatch(_text, _state) : PyNone.Instance;
            if (!found)
            {
                _done = true;
            }
            else
            {
                _state.MustAdvance = _state.Ptr == _state.Start;
                _state.Start = _state.Ptr;
            }
            return result;
        }

        // scanner_members: pattern, READONLY. scanner_methods are Step's (match, search: no arguments).
        internal PyObject Pattern => _pattern;
    }

    public sealed class PySreIterator : PyIterator
    {
        private readonly PySreScanner _scanner;

        internal PySreIterator(PySreScanner scanner) { _scanner = scanner; }

        protected override string IteratorName => "callable_iterator";

        protected internal override PyObject IterNext()
        {
            var match = _scanner.Step(true);
            if (match is PyNone) throw PyStopIteration.Create();
            return match;
        }
    }
}
