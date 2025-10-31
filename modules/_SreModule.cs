using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython 3.12 _sre 모듈 - re.py의 C# 백엔드
    /// System.Text.RegularExpressions를 활용한 고성능 구현
    /// </summary>
    public static class _SreModule
    {
        public static PyModule CreateSreModule()
        {
            var module = new PyModule("_sre", "<_sre C module>");

            // CPython _sre.c의 핵심 함수들
            module.ModuleDict["compile"] = new PyBuiltinFunction("compile", Compile);
            module.ModuleDict["match"] = new PyBuiltinFunction("match", Match);
            module.ModuleDict["search"] = new PyBuiltinFunction("search", Search);

            // SRE_Pattern, SRE_Match 타입은 런타임에서 생성됨

            return module;
        }

        #region 핵심 함수들

        public static PyObject Compile(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("compile() missing 1 required positional argument: 'pattern'");

            var pattern = args[0].ToStr();
            var flags = args.Length > 1 ? GetFlags(args[1]) : RegexOptions.None;

            try
            {
                return new PySrePattern(pattern.Value, flags);
            }
            catch (ArgumentException ex)
            {
                throw PyRegexError.Create($"Invalid regular expression: {ex.Message}");
            }
        }

        public static PyObject Match(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("match() missing required arguments");

            var pattern = args[0].ToStr();
            var text = args[1].ToStr();
            var flags = args.Length > 2 ? GetFlags(args[2]) : RegexOptions.None;

            try
            {
                var regex = new Regex(pattern.Value, flags);
                var match = regex.Match(text.Value);

                return match.Success ? new PySreMatch(match, text.Value) : PyNone.Instance;
            }
            catch (ArgumentException ex)
            {
                throw PyRegexError.Create($"Invalid regular expression: {ex.Message}");
            }
        }

        public static PyObject Search(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("search() missing required arguments");

            var pattern = args[0].ToStr();
            var text = args[1].ToStr();
            var flags = args.Length > 2 ? GetFlags(args[2]) : RegexOptions.None;

            try
            {
                var regex = new Regex(pattern.Value, flags);
                var match = regex.Match(text.Value);

                return match.Success ? new PySreMatch(match, text.Value) : PyNone.Instance;
            }
            catch (ArgumentException ex)
            {
                throw PyRegexError.Create($"Invalid regular expression: {ex.Message}");
            }
        }

        #endregion

        #region 헬퍼 메서드들

        private static RegexOptions GetFlags(PyObject flagsObj)
        {
            if (flagsObj is PyInt flagsInt)
                return (RegexOptions)flagsInt.Value;
            return RegexOptions.None;
        }

        #endregion
    }

    #region SRE_Pattern 클래스

    /// <summary>
    /// CPython SRE_Pattern - 컴파일된 정규표현식 패턴 객체
    /// </summary>
    public class PySrePattern : PyObject
    {
        private readonly Regex _regex;
        private readonly string _pattern;
        private readonly RegexOptions _flags;

        public PySrePattern(string pattern, RegexOptions flags)
        {
            _pattern = pattern;
            _flags = flags;
            _regex = new Regex(pattern, flags);
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "SRE_Pattern";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "pattern":
                    return new PyString(_pattern);
                case "flags":
                    return new PyInt((int)_flags);
                case "match":
                    return new PyBuiltinFunction("match", Match);
                case "search":
                    return new PyBuiltinFunction("search", Search);
                case "findall":
                    return new PyBuiltinFunction("findall", FindAll);
                case "finditer":
                    return new PyBuiltinFunction("finditer", FindIter);
                case "sub":
                    return new PyBuiltinFunction("sub", Sub);
                case "subn":
                    return new PyBuiltinFunction("subn", SubN);
                case "split":
                    return new PyBuiltinFunction("split", Split);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject Match(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("match() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            var match = _regex.Match(text.Value);
            return match.Success ? new PySreMatch(match, text.Value) : PyNone.Instance;
        }

        private PyObject Search(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("search() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            var match = _regex.Match(text.Value);
            return match.Success ? new PySreMatch(match, text.Value) : PyNone.Instance;
        }

        private PyObject FindAll(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("findall() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            var matches = _regex.Matches(text.Value);
            var result = new List<PyObject>();

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var groups = new List<PyObject>();
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        groups.Add(new PyString(match.Groups[i].Value));
                    }
                    result.Add(new PyTuple(groups.ToArray()));
                }
                else
                {
                    result.Add(new PyString(match.Value));
                }
            }

            return new PyList(result.ToArray());
        }

        private PyObject FindIter(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("finditer() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            var matches = _regex.Matches(text.Value);
            var matchObjects = new List<PyObject>();

            foreach (Match match in matches)
            {
                matchObjects.Add(new PySreMatch(match, text.Value));
            }

            return new PySreMatchIterator(matchObjects);
        }

        private PyObject Sub(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("sub() missing required arguments");

            var repl = args[0].ToStr();
            var text = args[1].ToStr();
            var count = args.Length > 2 && args[2] is PyInt countInt ? (int)countInt.Value : int.MaxValue;

            var result = count == int.MaxValue ?
                _regex.Replace(text.Value, repl.Value) :
                _regex.Replace(text.Value, repl.Value, count);

            return new PyString(result);
        }

        private PyObject SubN(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("subn() missing required arguments");

            var repl = args[0].ToStr();
            var text = args[1].ToStr();
            var count = args.Length > 2 && args[2] is PyInt countInt ? (int)countInt.Value : int.MaxValue;

            var matches = _regex.Matches(text.Value).Count;
            var result = count == int.MaxValue ?
                _regex.Replace(text.Value, repl.Value) :
                _regex.Replace(text.Value, repl.Value, count);

            var actualSubstitutions = Math.Min(matches, count == int.MaxValue ? matches : count);
            return new PyTuple(new PyString(result), new PyInt(actualSubstitutions));
        }

        private PyObject Split(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("split() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            var maxsplit = args.Length > 1 && args[1] is PyInt maxInt ? (int)maxInt.Value + 1 : 0;

            var parts = maxsplit > 0 ?
                _regex.Split(text.Value, maxsplit) :
                _regex.Split(text.Value);

            var result = new List<PyObject>();
            foreach (var part in parts)
            {
                result.Add(new PyString(part));
            }

            return new PyList(result.ToArray());
        }

        public override string ToString() => $"re.compile('{_pattern}')";
    }

    #endregion

    #region SRE_Match 클래스

    /// <summary>
    /// CPython SRE_Match - 정규표현식 매치 결과 객체
    /// </summary>
    public class PySreMatch : PyObject
    {
        private readonly Match _match;
        private readonly string _string;

        public PySreMatch(Match match, string text)
        {
            _match = match;
            _string = text;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "SRE_Match";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "string":
                    return new PyString(_string);
                case "group":
                    return new PyBuiltinFunction("group", Group);
                case "groups":
                    return new PyBuiltinFunction("groups", Groups);
                case "groupdict":
                    return new PyBuiltinFunction("groupdict", GroupDict);
                case "start":
                    return new PyBuiltinFunction("start", Start);
                case "end":
                    return new PyBuiltinFunction("end", End);
                case "span":
                    return new PyBuiltinFunction("span", Span);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject Group(PyObject[] args)
        {
            if (args.Length == 0)
            {
                return new PyString(_match.Value);
            }

            if (args.Length == 1)
            {
                if (args[0] is PyInt groupNum)
                {
                    if (groupNum.Value < 0 || groupNum.Value >= _match.Groups.Count)
                        throw PyIndexError.Create("no such group");
                    return new PyString(_match.Groups[(int)groupNum.Value].Value);
                }
                else if (args[0] is PyString groupName)
                {
                    try
                    {
                        var group = _match.Groups[groupName.Value];
                        return new PyString(group.Value);
                    }
                    catch
                    {
                        throw PyIndexError.Create($"no such group: '{groupName.Value}'");
                    }
                }
            }

            var result = new List<PyObject>();
            foreach (var arg in args)
            {
                if (arg is PyInt groupNum)
                {
                    if (groupNum.Value < 0 || groupNum.Value >= _match.Groups.Count)
                        throw PyIndexError.Create("no such group");
                    result.Add(new PyString(_match.Groups[(int)groupNum.Value].Value));
                }
                else if (arg is PyString groupName)
                {
                    try
                    {
                        var group = _match.Groups[groupName.Value];
                        result.Add(new PyString(group.Value));
                    }
                    catch
                    {
                        throw PyIndexError.Create($"no such group: '{groupName.Value}'");
                    }
                }
            }

            return result.Count == 1 ? result[0] : new PyTuple(result.ToArray());
        }

        private PyObject Groups(PyObject[] args)
        {
            var defaultValue = args.Length > 0 ? args[0] : PyNone.Instance;
            var result = new List<PyObject>();

            for (int i = 1; i < _match.Groups.Count; i++)
            {
                var group = _match.Groups[i];
                if (group.Success)
                    result.Add(new PyString(group.Value));
                else
                    result.Add(defaultValue);
            }

            return new PyTuple(result.ToArray());
        }

        private PyObject GroupDict(PyObject[] args)
        {
            var defaultValue = args.Length > 0 ? args[0] : PyNone.Instance;
            var result = new PyDict();

            foreach (string groupName in _match.Groups.Keys)
            {
                if (groupName != "0")
                {
                    var group = _match.Groups[groupName];
                    var value = group.Success ? new PyString(group.Value) : defaultValue;
                    result.SetItem(new PyString(groupName), value);
                }
            }

            return result;
        }

        private PyObject Start(PyObject[] args)
        {
            var groupNum = args.Length > 0 && args[0] is PyInt groupInt ? (int)groupInt.Value : 0;

            if (groupNum < 0 || groupNum >= _match.Groups.Count)
                throw PyIndexError.Create("no such group");

            return new PyInt(_match.Groups[groupNum].Index);
        }

        private PyObject End(PyObject[] args)
        {
            var groupNum = args.Length > 0 && args[0] is PyInt groupInt ? (int)groupInt.Value : 0;

            if (groupNum < 0 || groupNum >= _match.Groups.Count)
                throw PyIndexError.Create("no such group");

            var group = _match.Groups[groupNum];
            return new PyInt(group.Index + group.Length);
        }

        private PyObject Span(PyObject[] args)
        {
            var groupNum = args.Length > 0 && args[0] is PyInt groupInt ? (int)groupInt.Value : 0;

            if (groupNum < 0 || groupNum >= _match.Groups.Count)
                throw PyIndexError.Create("no such group");

            var group = _match.Groups[groupNum];
            return new PyTuple(new PyInt(group.Index), new PyInt(group.Index + group.Length));
        }

        public override string ToString() => $"<re.Match object; span=({_match.Index}, {_match.Index + _match.Length}), match='{_match.Value}'>";
    }

    #endregion

    #region SRE Match Iterator

    public class PySreMatchIterator : PyObject
    {
        private readonly List<PyObject> _matches;

        public PySreMatchIterator(List<PyObject> matches)
        {
            _matches = matches;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "callable_iterator";

        public override PyObject GetIterator()
        {
            return new PySreListIterator(_matches);
        }

        private class PySreListIterator : PyIterator
        {
            private readonly List<PyObject> _items;
            private int _index = 0;

            public PySreListIterator(List<PyObject> items)
            {
                _items = items;
            }

            public override PyObject Next()
            {
                if (_index >= _items.Count)
                    throw PyStopIteration.Create();

                return _items[_index++];
            }

            public override string GetTypeName() => "list_iterator";
        }
    }

    #endregion

    #region 예외 클래스

    public class PyRegexError : PyValueError
    {
        public PyRegexError(string message) : base(message) { }
        public override string GetTypeName() => "error";

        public new static PythonException Create(string message)
        {
            return new PythonException(new PyRegexError(message));
        }
    }

    #endregion
}
