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
            if (IsBytes)
            {
                var data = new byte[end - start];
                for (int i = start; i < end; i++) data[i - start] = (byte)Items[i];
                return new PyBytes(data);
            }
            var builder = new StringBuilder(end - start);
            for (int i = start; i < end; i++)
            {
                int item = Items[i];
                if (item < 0x10000) builder.Append((char)item);
                else builder.Append(char.ConvertFromUtf32(item));
            }
            return new PyString(builder.ToString());
        }

        internal PyObject Empty() => IsBytes ? new PyBytes(Array.Empty<byte>()) : new PyString("");
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
        public override string GetTypeName() => "re.Pattern";

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
            return kwargs != null && kwargs.TryGetStoredValue(new PyString(name), out var value) ? value : null;
        }

        private static int Position(PyObject value, int fallback)
        {
            if (value == null) return fallback;
            var number = value is PyBool truth ? (truth.Value ? BigInteger.One : BigInteger.Zero)
                : value is PyIntSubclass subclass ? subclass.GetIntValue().Value
                : value is PyInt plain ? plain.Value
                : throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
            return (int)BigInteger.Max(int.MinValue, BigInteger.Min(int.MaxValue, number));
        }

        private PyObject Run(PyObject[] args, PyDict kwargs, string name, Func<SreState, bool> engine)
        {
            var subject = Argument(args, kwargs, 0, "string")
                ?? throw PyTypeError.Create($"{name}() missing required argument 'string' (pos 1)");
            var text = SreText.Of(subject);
            var state = NewState(text, Position(Argument(args, kwargs, 1, "pos"), 0),
                Position(Argument(args, kwargs, 2, "endpos"), int.MaxValue));
            state.Ptr = state.Start;
            return engine(state) ? NewMatch(text, state) : PyNone.Instance;
        }

        // pattern_new_match.
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
                    result.Append(new PyTuple(items));
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
                bool hasEscape = replacement is PyString or PyStrSubclass
                    ? replacement.ToStr().Value.IndexOf('\\') >= 0
                    : replacement is PyBytes bytes ? Array.IndexOf(bytes.Value, (byte)'\\') >= 0 : true;
                if (hasEscape)
                {
                    var compiled = PyImportSystem.Import("re").GetAttribute("_compile_template")
                        .Call(new PyObject[] { this, replacement }, null);
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

        private static PyObject Join(List<PyObject> pieces, SreText text)
        {
            if (text.IsBytes)
            {
                var output = new List<byte>();
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (pieces[i] is PyBytes bytes) output.AddRange(bytes.Value);
                    else if (pieces[i] is PyByteArray array) output.AddRange(array.ToByteArrayCopy());
                    else throw PyTypeError.Create(
                        $"sequence item {i}: expected a bytes-like object, {pieces[i].GetTypeName()} found");
                }
                return new PyBytes(output.ToArray());
            }
            var builder = new StringBuilder();
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] is PyString or PyStrSubclass) builder.Append(pieces[i].ToStr().Value);
                else throw PyTypeError.Create($"sequence item {i}: expected str instance, {pieces[i].GetTypeName()} found");
            }
            return new PyString(builder.ToString());
        }

        private PyObject Scanner(PyObject[] args, PyDict kwargs)
        {
            var text = SreText.Of(Argument(args, kwargs, 0, "string"));
            return new PySreScanner(this, text, Position(Argument(args, kwargs, 1, "pos"), 0),
                Position(Argument(args, kwargs, 2, "endpos"), int.MaxValue));
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "match":
                    return new PyBuiltinFunction("match", (a, k) => Run(a, k, name, state => state.Match(0, true)));
                case "fullmatch":
                    return new PyBuiltinFunction("fullmatch", (a, k) => Run(a, k, name, state =>
                    {
                        state.MatchAll = true;
                        return state.Match(0, true);
                    }));
                case "search":
                    return new PyBuiltinFunction("search", (a, k) => Run(a, k, name, state => state.Search(0)));
                case "findall":
                    return new PyBuiltinFunction("findall", FindAll);
                case "finditer":
                    return new PyBuiltinFunction("finditer", (a, k) => new PySreIterator((PySreScanner)Scanner(a, k)));
                case "split":
                    return new PyBuiltinFunction("split", Split);
                case "sub":
                    return new PyBuiltinFunction("sub", (a, k) => Substitute(a, k).result);
                case "subn":
                    return new PyBuiltinFunction("subn", (a, k) =>
                    {
                        var (result, count) = Substitute(a, k);
                        return new PyTuple(new[] { result, new PyInt(count) });
                    });
                case "scanner":
                    return new PyBuiltinFunction("scanner", Scanner);
                case "__copy__":
                case "__deepcopy__":
                    return new PyBuiltinFunction(name, _ => this);
                case "pattern":
                    return Pattern;
                case "flags":
                    return new PyInt(Flags);
                case "groups":
                    return new PyInt(Groups);
                case "groupindex":
                {
                    var names = new Dictionary<string, PyObject>();
                    foreach (var key in GroupIndex.Keys().Items) names[key.ToStr().Value] = GroupIndex.GetItem(key);
                    return new PyMappingProxy(names);
                }
            }
            return base.GetAttribute(name);
        }

        // pattern_repr.
        public override PyString ToRepr()
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
            return new PyString(flags.Count == 0 ? $"re.compile({text})" : $"re.compile({text}, {string.Join("|", flags)})");
        }

        public override PyString AsString() => ToRepr();

        public override int ToHash() => HashCode.Combine(Flags, Code.Length, Pattern.ToHash());

        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            if (op != CompareOp.EQ && op != CompareOp.NE || other is not PySrePattern pattern)
                return PyNotImplemented.Instance;
            bool same = ReferenceEquals(this, other) || (Flags == pattern.Flags && Code.AsSpan().SequenceEqual(pattern.Code)
                && _isBytes == pattern._isBytes && Pattern.RichCompare(pattern.Pattern, CompareOp.EQ).IsTrue());
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
        public override string GetTypeName() => "re.Match";

        // match_getslice_by_index and match_getindex.
        private int GroupNumber(PyObject group)
        {
            long number = -1;
            if (group is PyInt or PyBool or PyIntSubclass)
            {
                number = group.ToInt();
            }
            else if (_pattern.GroupIndex.TryGetStoredValue(group, out var found))
            {
                number = found.ToInt();
            }
            else if (group.LookupSpecialMethodUncached("__index__") != null)
            {
                number = group.GetAttribute("__index__").Call(Array.Empty<PyObject>(), null).ToInt();
            }
            if (number < 0 || number > _pattern.Groups)
                throw PyIndexError.Create("no such group");
            return (int)number;
        }

        private PyObject GroupValue(int group, PyObject missing)
            => _marks[group * 2] < 0 || _marks[group * 2 + 1] < 0
                ? missing
                : _text.Slice(_marks[group * 2], _marks[group * 2 + 1]);

        internal PyObject GroupOrEmpty(int group) => GroupValue(group, _text.Empty());

        public override PyObject GetItem(PyObject index) => GroupValue(GroupNumber(index), PyNone.Instance);

        private PyObject Group(PyObject[] args)
        {
            if (args.Length == 0) return GroupValue(0, PyNone.Instance);
            if (args.Length == 1) return GetItem(args[0]);
            var items = new PyObject[args.Length];
            for (int i = 0; i < args.Length; i++) items[i] = GetItem(args[i]);
            return new PyTuple(items);
        }

        private static PyObject Default(PyObject[] args, PyDict kwargs)
        {
            if (args.Length > 0) return args[0];
            return kwargs != null && kwargs.TryGetStoredValue(new PyString("default"), out var value) ? value : PyNone.Instance;
        }

        private PyObject Groups(PyObject[] args, PyDict kwargs)
        {
            var fallback = Default(args, kwargs);
            var items = new PyObject[_pattern.Groups];
            for (int i = 0; i < items.Length; i++) items[i] = GroupValue(i + 1, fallback);
            return new PyTuple(items);
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

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "group":
                    return new PyBuiltinFunction("group", Group);
                case "groups":
                    return new PyBuiltinFunction("groups", Groups);
                case "groupdict":
                    return new PyBuiltinFunction("groupdict", GroupDict);
                case "start":
                    return new PyBuiltinFunction("start", a => new PyInt(Boundary(a, 0)));
                case "end":
                    return new PyBuiltinFunction("end", a => new PyInt(Boundary(a, 1)));
                case "span":
                    return new PyBuiltinFunction("span", a =>
                        new PyTuple(new PyObject[] { new PyInt(Boundary(a, 0)), new PyInt(Boundary(a, 1)) }));
                case "expand":
                    return new PyBuiltinFunction("expand", (a, k) =>
                    {
                        var compiled = PyImportSystem.Import("re").GetAttribute("_compile_template")
                            .Call(new PyObject[] { _pattern, a[0] }, null);
                        return compiled is PySreTemplate template ? template.Expand(this) : compiled;
                    });
                case "__copy__":
                case "__deepcopy__":
                    return new PyBuiltinFunction(name, _ => this);
                case "re":
                    return _pattern;
                case "string":
                    return _text.Source;
                case "pos":
                    return new PyInt(_pos);
                case "endpos":
                    return new PyInt(_endPos);
                case "lastindex":
                    return _lastIndex > 0 ? new PyInt(_lastIndex) : PyNone.Instance;
                case "lastgroup":
                    return _lastIndex > 0 && _lastIndex < _pattern.IndexGroup.Length
                        ? _pattern.IndexGroup[_lastIndex] : PyNone.Instance;
                case "regs":
                {
                    var spans = new PyObject[_pattern.Groups + 1];
                    for (int i = 0; i < spans.Length; i++)
                        spans[i] = new PyTuple(new PyObject[] { new PyInt(_marks[i * 2]), new PyInt(_marks[i * 2 + 1]) });
                    return new PyTuple(spans);
                }
            }
            return base.GetAttribute(name);
        }

        // match_repr: at most fifty characters of repr.
        public override PyString ToRepr()
        {
            string text = GroupValue(0, PyNone.Instance).ToRepr().Value;
            if (text.Length > 50) text = text.Substring(0, 50);
            return new PyString($"<re.Match object; span=({_marks[0]}, {_marks[1]}), match={text}>");
        }

        public override PyString AsString() => ToRepr();
        public override bool IsTrue() => true;
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

        public override string GetTypeName() => "re.template";

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
            return isBytes ? new PyBytes(bytes.ToArray()) : new PyString(builder.ToString());
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

        public override string GetTypeName() => "_sre.SRE_Scanner";

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

        public override PyObject GetAttribute(string name) => name switch
        {
            "match" => new PyBuiltinFunction("match", _ => Step(false)),
            "search" => new PyBuiltinFunction("search", _ => Step(true)),
            "pattern" => _pattern,
            _ => base.GetAttribute(name),
        };
    }

    public sealed class PySreIterator : PyIterator
    {
        private readonly PySreScanner _scanner;

        internal PySreIterator(PySreScanner scanner) { _scanner = scanner; }

        protected override string IteratorName => "callable_iterator";

        public override PyObject Next()
        {
            var match = _scanner.Step(true);
            if (match is PyNone) throw PyStopIteration.Create();
            return match;
        }
    }
}
