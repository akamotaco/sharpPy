using System;
using System.Collections.Generic;
using System.Numerics;
using SharpPy.Modules;
using SharpPy.Generated;

namespace SharpPy
{
    /// <summary>
    /// 내장 함수의 매개변수 정의
    /// </summary>
    public class BuiltinParameter
    {
        public string Name { get; }
        public PyObject DefaultValue { get; }
        public bool IsKeywordOnly { get; }
        public bool IsPositionalOnly { get; }

        public BuiltinParameter(string name, PyObject defaultValue = null, bool isKeywordOnly = false, bool isPositionalOnly = false)
        {
            Name = name;
            DefaultValue = defaultValue;
            IsKeywordOnly = isKeywordOnly;
            IsPositionalOnly = isPositionalOnly;
        }
    }

    /// <summary>
    /// 내장 함수의 시그니처 정의
    /// </summary>
    public class BuiltinSignature
    {
        public List<BuiltinParameter> Parameters { get; }
        public bool AcceptsVarArgs { get; }
        public bool AcceptsKwArgs { get; }

        public BuiltinSignature(bool acceptsVarArgs = false, bool acceptsKwArgs = false)
        {
            Parameters = new List<BuiltinParameter>();
            AcceptsVarArgs = acceptsVarArgs;
            AcceptsKwArgs = acceptsKwArgs;
        }

        public BuiltinSignature AddParameter(string name, PyObject defaultValue = null, bool isKeywordOnly = false, bool isPositionalOnly = false)
        {
            Parameters.Add(new BuiltinParameter(name, defaultValue, isKeywordOnly, isPositionalOnly));
            return this;
        }

        /// <summary>
        /// args와 kwargs를 처리하여 최종 인수 배열을 생성
        /// </summary>
        public PyObject[] ProcessArguments(PyObject[] args, PyDict kwargs)
        {
            var result = new PyObject[Parameters.Count];
            var kwDict = kwargs?.InternalDict ?? new PyDictionaryStorage();

            // 위치 인수 처리
            for (int i = 0; i < Math.Min(args.Length, Parameters.Count); i++)
            {
                var param = Parameters[i];
                if (param.IsKeywordOnly)
                {
                    throw PyTypeError.Create($"'{param.Name}' parameter is keyword-only");
                }
                result[i] = args[i];
            }

            // 키워드 인수 처리
            foreach (var kvp in kwDict)
            {
                if (kvp.Key is PyString keyStr)
                {
                    var paramIndex = Parameters.FindIndex(p => p.Name == keyStr.Value);
                    if (paramIndex >= 0)
                    {
                        if (paramIndex < args.Length)
                        {
                            throw PyTypeError.Create($"got multiple values for argument '{keyStr.Value}'");
                        }
                        result[paramIndex] = kvp.Value;
                    }
                    else if (!AcceptsKwArgs)
                    {
                        throw PyTypeError.Create($"got an unexpected keyword argument '{keyStr.Value}'");
                    }
                }
            }

            // 기본값 적용
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (result[i] == null)
                {
                    if (Parameters[i].DefaultValue != null)
                    {
                        result[i] = Parameters[i].DefaultValue;
                    }
                    else
                    {
                        throw PyTypeError.Create($"missing required argument: '{Parameters[i].Name}'");
                    }
                }
            }

            return result;
        }
    }

    public class PyBuiltinFunction : PyObject, ICFunction
    {
        public string Name { get; }
        private readonly Func<PyObject[], PyObject>? _implementation;
        private readonly Func<PyObject[], PyDict, PyObject>? _kwargsImplementation;
        private readonly BuiltinSignature? _signature;

        /// <summary>
        /// CPython 호환: 내장 함수 구현 테이블 (이름으로 직접 lookup하는 fallback용)
        /// </summary>
        private static readonly Dictionary<string, Func<PyObject[], PyDict, PyObject>> _builtinImplementations
            = new Dictionary<string, Func<PyObject[], PyDict, PyObject>>();

        /// <summary>
        /// The module-level builtins whose CPython signature is entirely positional-only, read off
        /// CPython 3.12.15's __text_signature__ values. All of them answer a keyword with the same
        /// text, "name() takes no keyword arguments".
        ///
        /// Before this guard SharpPy dropped the keyword on the floor and then complained about the
        /// argument that keyword was meant to supply: abs(x=-5) said "abs() takes exactly one argument
        /// (0 given)" and hasattr(obj=1, ...) said "hasattr() takes exactly 2 arguments (0 given)".
        /// The call being refused was not even the call being reported.
        ///
        /// Deliberately absent, because these do take keywords: round(number, ndigits),
        /// pow(base, exp, mod), print(*args, sep, end, file, flush), sorted(*, key, reverse),
        /// sum(iterable, start), exec(*, closure), compile(...), __import__(name, ...) and open(file,
        /// mode, ...). min and max take key/default too and report a handwritten text for anything
        /// else ("min expected at least 1 argument, got 0"); that family is tracked separately.
        /// </summary>
        internal static readonly HashSet<string> PositionalOnlyBuiltins = new()
        {
            "abs", "aiter", "all", "anext", "any", "ascii", "bin", "callable", "chr", "delattr",
            "dir", "divmod", "eval", "format", "getattr", "globals", "hasattr", "hash", "hex",
            "id", "input", "isinstance", "issubclass", "iter", "len", "locals", "next", "oct",
            "ord", "repr", "setattr", "vars",
        };

        static PyBuiltinFunction()
        {
            InitializeBuiltinImplementations();
        }

        public PyBuiltinFunction(string name)
        {
            Name = name;
        }

        public PyBuiltinFunction(string name, Func<PyObject[], PyObject> implementation)
        {
            Name = name;
            _implementation = implementation;
        }

        public PyBuiltinFunction(string name, Func<PyObject[], PyDict, PyObject> kwargsImplementation)
        {
            Name = name;
            _kwargsImplementation = kwargsImplementation;
        }

        public PyBuiltinFunction(string name, BuiltinSignature signature, Func<PyObject[], PyObject> implementation)
        {
            Name = name;
            _signature = signature;
            _implementation = implementation;
        }

        public override string GetTypeName() => "builtin_function_or_method";
        public override PyType GetPyType() => PyType.BuiltinFunctionOrMethodType;

        // m_module: the module that holds this function
        internal string OwnerModule = "builtins";

        // A method of a class: its name goes before this one in __qualname__.
        internal string OwnerClass;
        // The type a METH_CLASS function belongs to, which its doc is filed under.
        internal PyType OwnerType;

        // CPython builtin_function_repr for an unbound (module-level) builtin:
        // <built-in function NAME>. repr()/str() route through ToRepr/ToStr, not the
        // C# ToString(), so these must be overridden explicitly.
        protected internal override PyString Repr() => new PyString($"<built-in function {Name}>");
        protected internal override PyString Str() => new PyString($"<built-in function {Name}>");

        /// <summary>
        /// CPython 호환: 내장 함수 구현 테이블 초기화
        /// </summary>
        private static void InitializeBuiltinImplementations()
        {
            // 이미 초기화되었으면 스킵
            if (_builtinImplementations.Count > 0)
                return;

            // 각 builtin 함수를 테이블에 등록
            _builtinImplementations["print"] = (args, kwargs) => CallPrint(args, kwargs);
            _builtinImplementations["input"] = (args, kwargs) => CallInput(args, kwargs);
            _builtinImplementations["callable"] = (args, kwargs) => CallCallable(args, kwargs);
            _builtinImplementations["range"] = (args, kwargs) => CallRange(args, kwargs);
            _builtinImplementations["enumerate"] = (args, kwargs) => CallEnumerate(args, kwargs);
            _builtinImplementations["zip"] = (args, kwargs) => CallZip(args, kwargs);
            _builtinImplementations["map"] = (args, kwargs) => CallMap(args, kwargs);
            _builtinImplementations["filter"] = (args, kwargs) => CallFilter(args, kwargs);
            _builtinImplementations["sorted"] = (args, kwargs) => CallSorted(args, kwargs);
            _builtinImplementations["reversed"] = (args, kwargs) => ReversedNew(PyType.ReversedType, args, kwargs);
            _builtinImplementations["sum"] = (args, kwargs) => CallSum(args, kwargs);
            _builtinImplementations["min"] = (args, kwargs) => CallMin(args, kwargs);
            _builtinImplementations["max"] = (args, kwargs) => CallMax(args, kwargs);
            _builtinImplementations["any"] = (args, kwargs) => CallAny(args, kwargs);
            _builtinImplementations["all"] = (args, kwargs) => CallAll(args, kwargs);
            _builtinImplementations["hasattr"] = (args, kwargs) => CallHasAttr(args, kwargs);
            _builtinImplementations["getattr"] = (args, kwargs) => CallGetAttr(args, kwargs);
            _builtinImplementations["setattr"] = (args, kwargs) => CallSetAttr(args, kwargs);
            _builtinImplementations["delattr"] = (args, kwargs) => CallDelAttr(args, kwargs);
            _builtinImplementations["dir"] = (args, kwargs) => CallDir(args, kwargs);
            _builtinImplementations["__import__"] = (args, kwargs) => CallImport(args, kwargs);
            _builtinImplementations["type"] = (args, kwargs) => CallType(args, kwargs);
            _builtinImplementations["id"] = (args, kwargs) => CallId(args, kwargs);
            _builtinImplementations["hash"] = (args, kwargs) => CallHash(args, kwargs);
            _builtinImplementations["super"] = (args, kwargs) => CallSuper(args, kwargs);
            _builtinImplementations["property"] = (args, kwargs) => CallProperty(args, kwargs);
            _builtinImplementations["classmethod"] = (args, kwargs) => CallClassmethod(args, kwargs);
            _builtinImplementations["staticmethod"] = (args, kwargs) => CallStaticmethod(args, kwargs);
            _builtinImplementations["type.__new__"] = (args, kwargs) => PyTypeMetaclass.TypeNew(args, kwargs);
            _builtinImplementations["str"] = (args, kwargs) => CallStr(args, kwargs);
            _builtinImplementations["repr"] = (args, kwargs) => CallRepr(args, kwargs);
            _builtinImplementations["int"] = (args, kwargs) => CallInt(args, kwargs);
            _builtinImplementations["float"] = (args, kwargs) => CallFloat(args, kwargs);
            _builtinImplementations["complex"] = (args, kwargs) => CallComplex(args, kwargs);
            _builtinImplementations["bool"] = (args, kwargs) => CallBool(args, kwargs);
            _builtinImplementations["eval"] = (args, kwargs) => CallEval(args, kwargs);
            _builtinImplementations["compile"] = (args, kwargs) => CallCompile(args, kwargs);
            _builtinImplementations["exec"] = (args, kwargs) => CallExec(args, kwargs);
            _builtinImplementations["list"] = (args, kwargs) => CallList(args, kwargs);
            _builtinImplementations["tuple"] = (args, kwargs) => CallTuple(args, kwargs);
            _builtinImplementations["dict"] = (args, kwargs) => CallDict(args, kwargs);
            _builtinImplementations["set"] = (args, kwargs) => CallSet(args, kwargs);
            _builtinImplementations["frozenset"] = (args, kwargs) => CallFrozenSet(args, kwargs);
            _builtinImplementations["iter"] = (args, kwargs) => CallIter(args, kwargs);
            _builtinImplementations["next"] = (args, kwargs) => CallNext(args, kwargs);
            _builtinImplementations["round"] = (args, kwargs) => CallRound(args, kwargs);
            _builtinImplementations["pow"] = (args, kwargs) => CallPow(args, kwargs);
            _builtinImplementations["divmod"] = (args, kwargs) => CallDivmod(args, kwargs);
            _builtinImplementations["ord"] = (args, kwargs) => CallOrd(args, kwargs);
            _builtinImplementations["chr"] = (args, kwargs) => CallChr(args, kwargs);
            _builtinImplementations["open"] = (args, kwargs) => CallOpen(args, kwargs);
            _builtinImplementations["__build_class__"] = (args, kwargs) => CallBuildClass(args, kwargs);
            _builtinImplementations["globals"] = (args, kwargs) => CallGlobals(args, kwargs);
            _builtinImplementations["locals"] = (args, kwargs) => CallLocals(args, kwargs);
            _builtinImplementations["bytes"] = (args, kwargs) => CallBytes(args, kwargs);
            _builtinImplementations["bytearray"] = (args, kwargs) => CallBytearray(args, kwargs);
            _builtinImplementations["slice"] = (args, kwargs) => CallSlice(args, kwargs);
            _builtinImplementations["object"] = (args, kwargs) => CallObject(args, kwargs);
            _builtinImplementations["help"] = (args, kwargs) => CallHelp(args, kwargs);
            _builtinImplementations["breakpoint"] = (args, kwargs) => CallBreakpoint(args, kwargs);
        }

        // 내장 함수 호출 - CPython 3.12 호환: kwargs 지원
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
#if DEBUG_LOG
            Console.WriteLine($"[PyBuiltinFunction.Call] Name={Name}, args.Length={args.Length}");
#endif

            // CPython: a positional-only signature has no way to receive a keyword, and the generic
            // parser refuses it before looking at anything else.
            if (kwargs != null && kwargs.Length() != 0 && PositionalOnlyBuiltins.Contains(Name))
                throw PyTypeError.Create($"{Name}() takes no keyword arguments");

            // kwargs 지원 구현이 있으면 우선 사용
            if (_kwargsImplementation != null)
            {
#if DEBUG_LOG
                Console.WriteLine($"[PyBuiltinFunction.Call] Using _kwargsImplementation for {Name}");
#endif
                return _kwargsImplementation(args, kwargs);
            }

            // 시그니처가 있으면 인수 처리 후 기존 구현 호출
            if (_signature != null && _implementation != null)
            {
#if DEBUG_LOG
                Console.WriteLine($"[PyBuiltinFunction.Call] Using _signature + _implementation for {Name}");
#endif
                var processedArgs = _signature.ProcessArguments(args, kwargs);
                return _implementation(processedArgs);
            }

            // METH_FASTCALL and METH_O: a function that takes no keywords says so.
            if (_implementation != null)
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{Name}() takes no keyword arguments");
#if DEBUG_LOG
                Console.WriteLine($"[PyBuiltinFunction.Call] Using _implementation for {Name}");
#endif
                return _implementation(args);
            }

            // CPython 호환: 딕셔너리 기반 lookup (switch 문 제거)
            if (_builtinImplementations.TryGetValue(Name, out var implementation))
            {
#if DEBUG_LOG
                Console.WriteLine($"[PyBuiltinFunction.Call] Using _builtinImplementations lookup for {Name}");
#endif
                return implementation(args, kwargs);
            }

            throw PyNotImplementedError.Create($"Built-in function '{Name}' not implemented");
        }

        // 내장 함수들의 구현 (static으로 변경하여 테이블에서 호출 가능하게)
        private static PyObject CallPrint(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12 print(*values, sep=' ', end='\n', file=sys.stdout, flush=False)
            var sep = new PyString(" ");
            var end = new PyString("\n");
            PyObject file = null; // sys.stdout는 추후 구현
            var flush = PyBool.False;

            // CPython prints *args and accepts exactly sep/end/file/flush; anything else is named in
            // the error rather than ignored.
            RejectUnknownKeywords(kwargs, "print", "sep", "end", "file", "flush");

            // kwargs 처리
            if (kwargs != null)
            {
                try
                {
                    var sepValue = kwargs.GetItem(new PyString("sep"));
                    sep = sepValue as PyString ?? new PyString(sepValue.ToClrString());
                }
                catch { }

                try
                {
                    var endValue = kwargs.GetItem(new PyString("end"));
                    end = endValue as PyString ?? new PyString(endValue.ToClrString());
                }
                catch { }

                try
                {
                    var fileValue = kwargs.GetItem(new PyString("file"));
                    file = fileValue;
                }
                catch { }

                try
                {
                    var flushValue = kwargs.GetItem(new PyString("flush"));
                    flush = flushValue as PyBool ?? PyBool.FromBool(flushValue.IsTrue());
                }
                catch { }
            }

            // 출력 생성 - CPython 3.12: print는 str()을 사용, repr()이 아님
            // Performance: Eliminated LINQ - manual iteration instead of Select

            // builtin_print_impl: file=None means sys.stdout, whatever it is now.
            if (file == null || file is PyNone)
            {
                PyObject stream = null;
                if (SharpPy.Modules.SysModule.Current?.ModuleDict.TryGetValue("stdout", out stream) != true)
                    throw PyRuntimeError.Create("lost sys.stdout");
                if (stream is PyNone) return PyNone.Instance;
                file = stream;
            }
            {
                // CPython print.c: print delegates to file.write(str) - errors
                // (missing attribute, closed file) propagate to the caller.
                var write = file.GetAttribute("write");
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) write.Call(new PyObject[] { sep }, null);
                    write.Call(new PyObject[] { args[i].ToStr() }, null);
                }
                write.Call(new PyObject[] { end }, null);
                if (flush.Value)
                {
                    var flushMethod = file.GetAttribute("flush");
                    flushMethod.Call(System.Array.Empty<PyObject>(), null);
                }
            }

            return PyNone.Instance;
        }

        private static PyObject CallInput(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: input(prompt='', /)
            // Read a string from standard input. The trailing newline is stripped.
            // If the user hits EOF, raise EOFError.

            // input()은 kwargs를 받지 않음 (positional-only parameter)
            if (kwargs != null && kwargs.InternalDict.Count > 0)
                throw PyTypeError.Create("input() takes no keyword arguments");

            // 최대 1개 인수 (prompt)
            if (args.Length > 1)
                throw PyTypeError.Create($"input() takes at most 1 argument ({args.Length} given)");

            // builtin_input: through sys.stdin and sys.stdout, whatever they are now.
            var sys = PyImportSystem.Import("sys");
            var input = sys.ModuleDict.TryGetValue("stdin", out var fin) ? fin : PyNone.Instance;
            var output = sys.ModuleDict.TryGetValue("stdout", out var fout) ? fout : PyNone.Instance;
            if (input is PyNone)
                throw PyRuntimeError.Create("input(): lost sys.stdin");
            if (output is PyNone)
                throw PyRuntimeError.Create("input(): lost sys.stdout");
            // builtin_input_impl: stderr is flushed first, and a failing flush is cleared.
            if (sys.ModuleDict.TryGetValue("stderr", out var ferr) && ferr is not PyNone)
            {
                try { ferr.GetAttribute("flush").Call(new PyObject[0], null); }
                catch (PythonException) { }
            }
            if (args.Length == 1)
            {
                output.GetAttribute("write").Call(new PyObject[] { new PyString(args[0].ToStr().Value) }, null);
                // builtin_input_impl: a failing flush is cleared.
                try { output.GetAttribute("flush").Call(new PyObject[0], null); }
                catch (PythonException) { }
            }
            {
                var answer = input.GetAttribute("readline").Call(new PyObject[0], null);
                if (answer is not PyString text)
                    throw PyTypeError.Create("object.readline() returned non-string");
                if (text.Value.Length == 0)
                    throw PyEOFError.Create("EOF when reading a line");
                return new PyString(text.Value.EndsWith("\n", StringComparison.Ordinal) ? text.Value.Substring(0, text.Value.Length - 1) : text.Value);
            }

            // prompt가 주어지면 출력 (줄바꿈 없이)
            // stdin에서 한 줄 읽기
            try
            {
                var line = Console.ReadLine();

                // CPython 3.12: EOF (Ctrl+D on Unix, Ctrl+Z on Windows) raises EOFError
                if (line == null)
                {
                    throw PyEOFError.Create("EOF when reading a line");
                }

                // CPython 3.12: 줄바꿈은 자동으로 제거됨 (ReadLine이 이미 제거함)
                return new PyString(line);
            }
            catch (PythonException)
            {
                // Python 예외는 그대로 전파
                throw;
            }
            catch (Exception ex)
            {
                // I/O error 등 다른 예외는 EOFError로 변환
                throw PyEOFError.Create($"Error reading input: {ex.Message}");
            }
        }


        /// <summary>
        /// One implementation of abs for every caller. CPython has a single nb_absolute meaning,
        /// while SharpPy had four: builtins.abs, operator.abs, math.abs and this one, each with its
        /// own type tests, so abs(FSub(2.5)) failed even though float.__abs__ existed and worked.
        /// builtins.abs and operator.abs now delegate here.
        /// </summary>
        internal static PyObject Absolute(PyObject value)
        {
            // CPython 3.12: Python/bltinmodule.c:2340-2355 - builtin_abs
            // CPython builtin_abs goes through the nb_absolute slot: for an exact int/float that is
            // the C routine, for a heap type it is the __abs__ special method, and for a float or int
            // subclass it is the inherited base slot reading the value payload. CLR type tests express
            // none of that: they matched only exact PyInt/PyFloat/PyComplex plus hand-coded
            // PyClassInstance, so abs(FSub(2.5)) raised even though float.__abs__ now exists.
            // Keep the exact-type fast paths and dispatch everything else through __abs__.
            switch (value)
            {
                case PyInt intVal:
                    return new PyInt(BigInteger.Abs(intVal.Value));
                case PyBool boolVal:
                    return new PyInt(boolVal.Value ? BigInteger.One : BigInteger.Zero);
                case PyFloat floatVal:
                    return new PyFloat(Math.Abs(floatVal.Value));
                case PyComplex complexVal:
                    return complexVal.Absolute();
            }

            // Measured: builtin_abs returns whatever __abs__ produced, NotImplemented included.
            // class A: __abs__ returns NotImplemented  =>  abs(A()) IS NotImplemented, no TypeError.
            // The "bad operand type" error only comes from the nb_absolute path, which runs when
            // the type has no __abs__ at all. Filtering NotImplemented here turned that observable
            // into an error, so the result is returned unconditionally.
            var absMethod = value.LookupSpecialMethodUncached("__abs__");
            if (absMethod != null)
                return value.BindSpecialMethod(absMethod).Call(Array.Empty<PyObject>(), null);

            throw PyTypeError.Create($"bad operand type for abs(): '{value.GetTypeName()}'");
        }


        private static PyObject CallCallable(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"callable() takes exactly one argument ({args.Length} given)");

            return PyBool.FromBool(args[0].IsCallable());
        }

        // === 핵심 내장 함수들 ===

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static PyObject CallRange(PyObject[] args, PyDict kwargs = null)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("range() takes no keyword arguments");
            if (args.Length == 0)
                throw PyTypeError.Create("range expected at least 1 argument, got 0");
            if (args.Length > 3)
                throw PyTypeError.Create($"range expected at most 3 arguments, got {args.Length}");

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            static BigInteger Index(PyObject value)
            {
                var integer = PyObject.TryGetIndex(value);
                if (integer == null)
                    throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
                return integer.Value;
            }
            if (args.Length == 1) return PyRange.Create(BigInteger.Zero, Index(args[0]), BigInteger.One);
            var start = Index(args[0]);
            var stop = Index(args[1]);
            var step = args.Length == 3 ? Index(args[2]) : BigInteger.One;
            return PyRange.Create(start, stop, step);
        }

        private static PyObject CallEnumerate(PyObject[] args, PyDict kwargs = null)
        {
            // enumerate's CPython signature is (iterable, start=0) with no positional-only marker, so
            // both names are legal keywords: list(enumerate(iterable=[1])) is [(0, 1)]. Too many
            // positionals and a missing iterable have different texts, and an unknown name is named.
            if (args.Length > 2)
                throw PyTypeError.Create($"enumerate() takes at most 2 arguments ({args.Length} given)");

            // Order matters and is measured: enumerate(zzz=1) reports the invalid keyword, while
            // min(zzz=1) reports the missing argument. enumerate parses keywords first.
            RejectUnknownKeywords(kwargs, "enumerate", "iterable", "start");
            var iterable = args.Length > 0 ? args[0] : PyObject.KwValue(kwargs, "iterable");
            if (iterable == null)
                throw PyTypeError.Create("enumerate() missing required argument 'iterable'");

            // CPython 3.12: Objects/enumobject.c:246-292 - enum_new, PyNumber_Index on start
            var startValue = args.Length > 1 ? args[1] : PyObject.KwValue(kwargs, "start");
            var start = System.Numerics.BigInteger.Zero;
            if (startValue != null)
            {
                var index = PyObject.TryGetIndex(startValue);
                if (index == null) throw PyTypeError.Create($"'{startValue.GetTypeName()}' object cannot be interpreted as an integer");
                start = index.Value;
            }

            // CPython 3.12 호환: enumerate iterator 객체 반환
            return new PyEnumerateIterator(iterable, start);
        }

        private static PyObject CallZip(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: zip is a lazy iterator; strict=True is keyword-only.
            bool strict = false;
            if (kwargs != null && kwargs.Length() > 0)
            {
                foreach (var key in kwargs.Keys().Items)
                {
                    if (!(key is PyString ks && ks.Value == "strict"))
                        throw PyTypeError.Create($"zip() has got an unexpected keyword argument '{key.ToStr().Value}'");
                }
                if (kwargs.Contains(new PyString("strict")).ToBool())
                    strict = kwargs.GetItem(new PyString("strict")).IsTrue();
            }

            var iterators = new PyObject[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                iterators[i] = args[i].GetIterator();
            }
            return new PyZipIterator(iterators, strict);
        }

        private static PyObject CallMap(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: map is lazy and takes any number of iterables
            // (Objects/mapobject.c); it stops at the shortest iterable. map()
            // accepts NO keyword arguments (no strict=), even though zip() does.
            if (kwargs != null && kwargs.Length() > 0)
                throw PyTypeError.Create("map() takes no keyword arguments");
            if (args.Length < 2)
                throw PyTypeError.Create($"map() must have at least two arguments.");

            var iterators = new PyObject[args.Length - 1];
            for (int i = 1; i < args.Length; i++)
                iterators[i - 1] = args[i].GetIterator();
            return new PyMapIterator(args[0], iterators);
        }

        private static PyObject CallFilter(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: filter() accepts no keyword arguments.
            if (kwargs != null && kwargs.Length() > 0)
                throw PyTypeError.Create("filter() takes no keyword arguments");
            if (args.Length != 2)
                throw PyTypeError.Create($"filter expected exactly 2 arguments ({args.Length} given)");

            // CPython 3.12: filter is lazy (Objects/mapobject.c filter_new).
            var func = args[0];
            var iterable = args[1];
            return new PyFilterIterator(func, iterable.GetIterator());
        }

        /// <summary>
        /// CPython parses keywords per function, so an unrecognised name is reported against that
        /// function's own label. Two labels are surprises worth naming: sorted() says sort(), because it
        /// shares list.sort()'s keyword parser, and a function that is missing a required positional
        /// reports the arity first - min(zzz=1) says "min expected at least 1 argument, got 0", not
        /// anything about the keyword. All of this is measured against CPython 3.12.15.
        /// </summary>
        private static void RejectUnknownKeywords(PyDict kwargs, string label, params string[] allowed)
        {
            if (kwargs == null)
                return;
            foreach (var entry in kwargs.GetInternalDict())
            {
                string key = entry.Key.ToClrString();
                bool known = false;
                foreach (var name in allowed)
                {
                    if (name == key)
                    {
                        known = true;
                        break;
                    }
                }
                if (!known)
                    throw PyTypeError.Create($"'{key}' is an invalid keyword argument for {label}()");
            }
        }

        private static PyObject CallSorted(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: sorted(iterable, /, *, key=None, reverse=False). The iterable is
            // positional-only, so sorted(iterable=[1]) is an arity error, not a keyword error.
            if (args.Length != 1)
                throw PyTypeError.Create($"sorted expected 1 argument, got {args.Length}");
            RejectUnknownKeywords(kwargs, "sort", "key", "reverse");

            // CPython materializes the iterable before list.sort parses keywords.
            var result = PyList.FromIterable(args[0]);
            PyList.ParseSortKeywords(kwargs, out var key, out bool reverse);
            result.Sort(key, reverse);
            return result;
        }

        /// <summary>
        /// reversed_new and reversed_new_impl (Objects/enumobject.c): what __reversed__ answers is
        /// returned as it is, whatever type was asked for; only the sequence protocol makes an
        /// object of the requested type.
        /// </summary>
        internal static PyObject ReversedNew(PyType type, PyObject[] args, PyDict kwargs)
        {
            // The clinic: "(type == &PyReversed_Type || type->tp_init == PyReversed_Type.tp_init) &&
            // !_PyArg_NoKeywords", then "reversed expected 1 argument, got N".
            if ((ReferenceEquals(type, PyType.ReversedType) || PyType.TypeInitIsObjectInit(type)) &&
                kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("reversed() takes no keyword arguments");
            if (args.Length != 1)
                throw PyTypeError.Create($"reversed expected 1 argument, got {args.Length}");

            var seq = args[0];

            // CPython: dict is not a sequence but provides dict.__reversed__ (keys).
            if (seq is PyDict dict)
            {
                // CPython: dict.__reversed__ yields keys in reverse as a dict_keyiterator.
                var keys = dict.Keys().Items;
                var keyList = new PyList((PyObject[])keys.Clone());
                return new PyReversedIterator(keyList, keys.Length, "dict_reversekeyiterator");
            }

            var revDesc = seq.GetPyType()?.LookupSpecial("__reversed__");
            if (revDesc != null)
            {
                return seq.CallSpecial(revDesc);
            }

            if (!HasGetItem(seq))
                throw PyTypeError.Create($"'{seq.GetTypeName()}' object is not reversible");

            if (!TrySequenceLength(seq, out long length))
                throw PyTypeError.Create($"object of type '{seq.GetTypeName()}' has no len()");

            // CPython names reversed() by source: list_reverseiterator, range_iterator,
            // else the generic reversed object.
            string revName = seq is PyList ? "list_reverseiterator"
                          : seq is PyRange ? "range_iterator"
                          : "reversed";
            var reversed = new PyReversedIterator(seq, length, revName);
            if (!ReferenceEquals(type, PyType.ReversedType))
                reversed.SetCustomType(type);
            return reversed;
        }

        private static bool HasGetItem(PyObject obj)
        {
            if (obj is PyList or PyTuple or PyString or PyRange or PyBytes or PyByteArray)
                return true;
            return obj.GetPyType()?.LookupSpecial("__getitem__") != null;
        }

        private static bool TrySequenceLength(PyObject obj, out long length)
        {
            length = 0;
            switch (obj)
            {
                case PyList pl: length = pl.Length(); return true;
                case PyTuple pt: length = pt.Items.Length; return true;
                case PyString ps: length = ps.Length(); return true;
                case PyRange pr:
                    unchecked { length = (long)pr.LengthUnsigned(); }
                    return true;
                case PyBytes pb: length = pb.Length(); return true;
                case PyByteArray pba: length = pba.Length(); return true;
            }
            if (!obj.HasTypeSlot("__len__"))
                return false;
            length = obj.Size();
            return true;
        }

        private static PyObject CallSum(PyObject[] args, PyDict kwargs = null)
        {
            // Measured: sum(iterable, start=0) takes the iterable positionally only, and reports too
            // few and too many positionals with two different texts.
            if (args.Length < 1)
                throw PyTypeError.Create($"sum() takes at least 1 positional argument ({args.Length} given)");
            if (args.Length > 2)
                throw PyTypeError.Create($"sum() takes at most 2 arguments ({args.Length} given)");
            RejectUnknownKeywords(kwargs, "sum", "start");

            var iterable = args[0];
            var start = args.Length > 1
                ? args[1]
                : PyObject.KwValue(kwargs, "start") ?? new PyInt(0);
            // builtin_sum_impl
            if (start is PyString or PyStrSubclass)
                throw PyTypeError.Create("sum() can't sum strings [use ''.join(seq) instead]");
            if (start.GetPyType().IsSubclassOf(PyType.BytesType) || ReferenceEquals(start.GetPyType(), PyType.BytesType))
                throw PyTypeError.Create("sum() can't sum bytes [use b''.join(seq) instead]");
            if (start.GetPyType().IsSubclassOf(PyType.BytearrayType) || ReferenceEquals(start.GetPyType(), PyType.BytearrayType))
                throw PyTypeError.Create("sum() can't sum bytearray [use b''.join(seq) instead]");
            var result = start;
            var iterator = iterable.GetIterator();
            double compensation = 0;

            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    // builtin_sum: exact floats accumulate with Neumaier compensation (3.12).
                    if (result.GetType() == typeof(PyFloat) && (item.GetType() == typeof(PyFloat) || item.GetType() == typeof(PyInt)))
                    {
                        double total = ((PyFloat)result).Value, value = item.ToFloat(), sumNow = total + value;
                        compensation += Math.Abs(total) >= Math.Abs(value) ? (total - sumNow) + value : (value - sumNow) + total;
                        result = new PyFloat(sumNow);
                        continue;
                    }
                    if (compensation != 0 && double.IsFinite(compensation) && result is PyFloat partial)
                    {
                        result = new PyFloat(partial.Value + compensation);
                        compensation = 0;
                    }
                    result = PyVM.Instance.ExecuteBinaryOpType(result, item, BinaryOpType.ADD);
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
            }

            if (compensation != 0 && double.IsFinite(compensation) && result is PyFloat sum)
                return new PyFloat(sum.Value + compensation);
            return result;
        }

        private static PyObject CallMin(PyObject[] args, PyDict kwargs = null)
        {
            return MinMaxCore(true, args, kwargs);
        }

        private static PyObject CallMax(PyObject[] args, PyDict kwargs = null)
        {
            return MinMaxCore(false, args, kwargs);
        }

        /// <summary>
        /// CPython 3.12: Python/bltinmodule.c builtin_min_max.
        /// Supports key= and default= keyword arguments; ties keep the first best.
        /// </summary>
        private static PyObject MinMaxCore(bool isMin, PyObject[] args, PyDict kwargs)
        {
            string fname = isMin ? "min" : "max";
            var better = isMin ? PyObject.CompareOp.LT : PyObject.CompareOp.GT;

            // Arity is checked before the keywords: min(zzz=1) and min(key=5) both report the missing
            // argument in CPython, because the parser counts positionals first.
            if (args.Length == 0)
                throw PyTypeError.Create($"{fname} expected at least 1 argument, got 0");
            RejectUnknownKeywords(kwargs, fname, "key", "default");

            PyObject keyFn = null;
            bool hasDefault = false;
            PyObject defaultVal = null;
            if (kwargs != null)
            {
                if (kwargs.InternalDict.TryGetValue(new PyString("key"), out var k)
                    && !ReferenceEquals(k, PyNone.Instance))
                {
                    keyFn = k;
                    if (!keyFn.IsCallable())
                    {
                        throw PyTypeError.Create($"'{keyFn.GetTypeName()}' object is not callable");
                    }
                }
                if (kwargs.InternalDict.TryGetValue(new PyString("default"), out var dv))
                {
                    hasDefault = true;
                    defaultVal = dv;
                }
            }

            if (hasDefault && args.Length > 1)
                throw PyTypeError.Create($"Cannot specify a default for {fname}() with multiple positional arguments");

            PyObject KeyOf(PyObject x)
            {
                return keyFn == null ? x : keyFn.Call(new PyObject[] { x }, null);
            }

            PyObject best = null;
            PyObject bestKey = null;

            void Consider(PyObject item)
            {
                var itemKey = KeyOf(item);
                if (best == null || PyObject.CompareBool(itemKey, bestKey, better))
                {
                    best = item;
                    bestKey = itemKey;
                }
            }

            if (args.Length == 1)
            {
                var iterator = args[0].GetIterator();
                while (true)
                {
                    PyObject item;
                    try
                    {
                        item = iterator.Next();
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        break;
                    }
                    Consider(item);
                }

                if (best == null)
                {
                    if (hasDefault)
                    {
                        return defaultVal;
                    }
                    throw PyValueError.Create($"{fname}() iterable argument is empty");
                }
                return best;
            }

            foreach (var a in args)
            {
                Consider(a);
            }
            return best;
        }

        private static PyObject CallAny(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"any expected exactly 1 arguments ({args.Length} given)");

            var iterable = args[0];
            var iterator = iterable.GetIterator();

            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    if (item.IsTrue())
                        return PyBool.True;
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 모두 False였음
            }

            return PyBool.False;
        }

        private static PyObject CallAll(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"all expected exactly 1 arguments ({args.Length} given)");

            var iterable = args[0];
            var iterator = iterable.GetIterator();

            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    if (!item.IsTrue())
                        return PyBool.False;
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 모두 True였음
            }

            return PyBool.True;
        }

        // === 타입 및 리플렉션 함수들 ===

        
        /// <summary>
        /// 제네릭 타입을 지원하는 확장된 isinstance 검사
        /// </summary>
        private static bool IsInstanceExtended(PyObject obj, PyType type)
        {
            // 기본 isinstance 검사
            bool result = obj.IsInstance(type);
            if (result)
                return true;

            // 제네릭 타입 검사
            if (type is PyGenericType genericType)
            {
                // 객체가 원본 타입의 인스턴스인지 확인
                if (obj.IsInstance(genericType.OriginType))
                {
                    // 런타임에서는 타입 인자는 체크하지 않고 구조만 확인
                    return true;
                }
            }

            return false;
        }


        // CPython 3.12: Python/bltinmodule.c builtin_getattr / builtin_hasattr / builtin_setattr /
        // builtin_delattr. Two separate defects lived here. The duplicates in modules/BuiltinsModule.cs
        // caught every exception, so a ValueError from a user __getattr__ turned into the default value
        // (or into hasattr returning False); CPython's default suppresses AttributeError only. And the
        // name-type check invented "getattr(): attribute name must be string", while CPython says
        // "attribute name must be string, not 'int'" for all four builtins.
        private static string AttributeNameOf(PyObject name)
        {
            if (name is PyString strName)
                return strName.Value;
            throw PyTypeError.Create($"attribute name must be string, not '{name.GetTypeName()}'");
        }

        private static PyObject CallHasAttr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"hasattr expected 2 arguments, got {args.Length}");

            var obj = args[0];
            var name = AttributeNameOf(args[1]);

            // CPython's builtin_hasattr is _PyObject_LookupAttr plus "if the error was AttributeError,
            // clear it and answer False". LookupAttribute is that primitive: a user __getattr__ that
            // raises ValueError still propagates.
            return obj.LookupAttribute(name) != null ? PyBool.True : PyBool.False;
        }

        private static PyObject CallGetAttr(PyObject[] args, PyDict kwargs = null)
        {
            // Measured: getattr's arity texts use "at least" / "at most", unlike hasattr's plain form.
            if (args.Length < 2)
                throw PyTypeError.Create($"getattr expected at least 2 arguments, got {args.Length}");
            if (args.Length > 3)
                throw PyTypeError.Create($"getattr expected at most 3 arguments, got {args.Length}");

            var obj = args[0];
            var name = AttributeNameOf(args[1]);
            var defaultValue = args.Length > 2 ? args[2] : null;

            if (defaultValue == null)
                return obj.GetAttribute(name);

            // The two-argument form raises; the three-argument form suppresses AttributeError only.
            // LookupAttribute returns CLR null exactly when the lookup failed with AttributeError, and
            // propagates anything else. A Python None here is a real attribute value, returned as
            // itself - an earlier version treated it as missing, which turned x = None into a default.
            return obj.LookupAttribute(name) ?? defaultValue;
        }

        private static PyObject CallSetAttr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 3)
                throw PyTypeError.Create($"setattr expected 3 arguments, got {args.Length}");

            var obj = args[0];
            var name = AttributeNameOf(args[1]);

            obj.SetAttribute(name, args[2]);
            return PyNone.Instance;
        }

        private static PyObject CallDelAttr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"delattr expected 2 arguments, got {args.Length}");

            var obj = args[0];
            var name = AttributeNameOf(args[1]);

            obj.DelAttribute(name);
            return PyNone.Instance;
        }

        private static PyObject CallType(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"type expected exactly 1 arguments ({args.Length} given)");

            return args[0].GetPyType();
        }


        private static PyObject CallId(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"id expected exactly 1 arguments ({args.Length} given)");

            // Not GetHashCode: a value-based hash makes two distinct objects share an id, which is
            // the identity observable (core/PyObjectId.cs).
            return new PyInt(PyObjectId.Of(args[0]));
        }

        private static PyObject CallHash(PyObject[] args, PyDict kwargs = null)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("hash() takes no keyword arguments");
            if (args.Length != 1)
                throw PyTypeError.Create($"hash() takes exactly one argument ({args.Length} given)");

            long hash = args[0].GetPythonHash();
            return SmallIntCache.GetOrCreate(hash);
        }

        // === 타입 변환 함수들 ===

        // CPython 3.12: Objects/unicodeobject.c:14702-14730 (unicode_new_impl)
        private static PyObject CallStr(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: str(object='', encoding=None, errors='strict')
            // x == NULL: return empty string
            if (args.Length == 0)
                return new PyString("");

            if (args.Length > 3)
                throw PyTypeError.Create($"str() takes at most 3 arguments ({args.Length} given)");

            var x = args[0];

            // CPython 3.12: encoding == NULL and errors == NULL → PyObject_Str(x)
            if (args.Length == 1)
                return new PyString(x.ToClrString());

            // CPython 3.12: str(bytes, encoding, errors='strict') → PyUnicode_FromEncodedObject
            var encoding = args.Length > 1 ? args[1] : PyNone.Instance;
            var errors = args.Length > 2 ? args[2] : new PyString("strict");

            if (encoding == PyNone.Instance && errors is PyString errStr && errStr.Value == "strict")
            {
                // No encoding specified, just convert to string
                return new PyString(x.ToClrString());
            }

            // Decode bytes with specified encoding
            if (!(x is PyBytes pyBytes))
            {
                throw PyTypeError.Create($"decoding to str: need a bytes-like object, {x.GetTypeName()} found");
            }

            if (!(encoding is PyString encStr))
            {
                throw PyTypeError.Create($"str() argument 2 must be str, not {encoding.GetTypeName()}");
            }

            if (!(errors is PyString))
            {
                throw PyTypeError.Create($"str() argument 3 must be str, not {errors.GetTypeName()}");
            }

            // One decoder. str() kept a three-name table of its own while PyStringNewArguments
            // already had the decoder the str(bytes, enc, errors) constructor path uses, so
            // str(b'ab', 'utf-16-le') reported an unknown encoding that str() itself accepts.
            return new PyString(PyStringNewArguments.DecodeBytes(
                pyBytes.Value, encStr.Value, ((PyString)errors).Value));
        }

        // CPython 3.12: Objects/longobject.c:5598-5641 (long_new_impl)
        private static PyObject CallInt(PyObject[] args, PyDict kwargs = null) => PyInt.FromArguments(args, kwargs);

        private static PyObject CallFloat(PyObject[] args, PyDict kwargs = null) =>
            PyFloat.FloatNew(PyType.FloatType, args, kwargs);

        // try_complex_special_method
        private static PyComplex ComplexSpecial(PyObject value)
        {
            var method = value.LookupSpecialMethodUncached("__complex__");
            if (method == null)
                return null;
            var result = value.CallSpecial(method);
            if (result is not PyComplex complex)
                throw PyTypeError.Create("__complex__ should return a complex object");
            if (result is PyComplexSubclass)
                PyWarnings.Deprecated($"__complex__ returned non-complex (type {result.GetPyType().Name}).  " +
                    "The ability to return an instance of a strict subclass of complex is deprecated, and may be removed in a future version of Python.");
            return complex;
        }

        private static bool IsNumberLike(PyObject value) =>
            value is PyComplex or PyFloat or PyFloatSubclass or PyInt or PyBool or PyIntSubclass ||
            value.HasTypeSlot("__float__") || value.HasTypeSlot("__index__");

        // complex_new_impl
        internal static PyObject CallComplex(PyObject[] args, PyDict kwargs = null)
        {
            int total = args.Length + (kwargs?.Length() ?? 0);
            if (total > 2)
                throw PyTypeError.Create($"complex() takes at most 2 arguments ({total} given)");
            PyObject r = args.Length > 0 ? args[0] : null;
            PyObject i = args.Length > 1 ? args[1] : null;
            if (kwargs != null)
                foreach (var entry in kwargs.GetInternalDict())
                {
                    if (entry.Key is PyString { Value: "real" } && r == null) r = entry.Value;
                    else if (entry.Key is PyString { Value: "imag" } && i == null) i = entry.Value;
                    else if (entry.Key is PyString { Value: "real" or "imag" } duplicate)
                        throw PyTypeError.Create($"argument for complex() given by name ('{duplicate.Value}') and position ({(duplicate.Value == "real" ? 1 : 2)})");
                    else
                        throw PyTypeError.Create($"{entry.Key.ToRepr().Value} is an invalid keyword argument for complex()");
                }
            r ??= new PyInt(0);

            if (r.GetType() == typeof(PyComplex) && i == null)
                return r;
            if (r is PyString text)
            {
                if (i != null)
                    throw PyTypeError.Create("complex() can't take second arg if first is a string");
                return PyComplex.FromObject(text);
            }
            if (i is PyString)
                throw PyTypeError.Create("complex() second arg can't be a string");

            r = ComplexSpecial(r) ?? r;
            if (!IsNumberLike(r))
                throw PyTypeError.Create($"complex() first argument must be a string or a number, not '{r.GetPyType().Name}'");
            if (i != null && !IsNumberLike(i))
                throw PyTypeError.Create($"complex() second argument must be a number, not '{i.GetPyType().Name}'");

            double realReal, realImag = 0.0, imagReal, imagImag = 0.0;
            if (r is PyComplex first)
            {
                realReal = first.Real;
                realImag = first.Imag;
            }
            else
            {
                realReal = PyFloat.NumberFloat(r).Value;
            }
            if (i == null)
                imagReal = realImag;
            else if (i is PyComplex second)
            {
                imagReal = second.Real;
                imagImag = second.Imag;
            }
            else
            {
                imagReal = PyFloat.NumberFloat(i).Value;
            }
            if (i is PyComplex)
                realReal -= imagImag;
            if (r is PyComplex && i != null)
                imagReal += realImag;
            return new PyComplex(realReal, imagReal);
        }

        // PyDict_Check
        private static bool IsDict(PyObject value) =>
            value is PyDict || value.GetPyType().IsSubclassOf(PyType.DictType);

        // PyMapping_Check
        private static bool IsMapping(PyObject value) =>
            value is PyDict || value.HasTypeSlot("__getitem__");

        // What eval and exec share. Names resolve in a dict's storage. A dict subclass given as
        // globals, or any other mapping as locals, also takes LOAD_NAME and STORE_NAME through
        // its own __getitem__ and __setitem__, as PyObject_GetItem does.
        private static (PyDict Globals, PyObject Namespace) GlobalsAndNamespace(PyObject globals, PyObject locals)
        {
            if (globals == null)
                return (null, locals != null && locals.GetType() != typeof(PyDict) ? locals : null);
            bool plain = globals is PyDict;
            var storage = plain ? (PyDict)globals : PyNativeStorage.ForDict(globals);
            if (locals != null)
                return (storage, locals is PyDict ? null : locals);
            return (storage, plain ? null : globals);
        }

        private static PyObject CallEval(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Python/bltinmodule.c:builtin_eval_impl
            // eval(source, globals=None, locals=None)

            if (args.Length == 0)
                throw PyTypeError.Create("eval expected at least 1 argument, got 0");

            if (args.Length > 3)
                throw PyTypeError.Create($"eval expected at most 3 arguments, got {args.Length}");

            var source = args[0];
            var globalsArgument = args.Length > 1 && args[1] != PyNone.Instance ? args[1] : null;
            var localsArgument = args.Length > 2 && args[2] != PyNone.Instance ? args[2] : null;
            if (localsArgument != null && !IsMapping(localsArgument))
                throw PyTypeError.Create("locals must be a mapping");
            if (globalsArgument != null && !IsDict(globalsArgument))
                throw PyTypeError.Create(IsMapping(globalsArgument)
                    ? "globals must be a real dict; try eval(expr, {}, mapping)" : "globals must be a dict");
            var (globals, localsMapping) = GlobalsAndNamespace(globalsArgument, localsArgument);
            PyDict locals = localsMapping == null ? localsArgument as PyDict : null;

            // CPython 3.12: If globals is not provided, use current frame's globals/locals
            if (globals == null)
            {
                // Get current executing frame
                var currentFrame = PyVM.CurrentFrame;
                if (currentFrame != null)
                {
                    // Convert frame's globals to PyDict
                    globals = PyGlobalsDict.For(currentFrame.Globals);

                    // Convert frame's local scope to PyDict
                    if (locals == null && localsMapping == null)
                    {
                        bool optimized = (currentFrame.Code.Flags & PyCodeObject.CO_OPTIMIZED) != 0;
                        locals = optimized ? currentFrame.GetOptimizedLocals()
                            : currentFrame.ClassLocalsDict as PyDict ?? globals;
                    }
                }
                else
                {
                    // No current frame - create empty dicts
                    globals = new PyDict();
                    if (locals == null)
                        locals = globals;
                }
            }
            else if (locals == null)
            {
                // If globals is provided but locals is not, locals = globals
                locals = globals;
            }

            // CPython 3.12: Add __builtins__ to globals if not present
            var builtinsKey = new PyString("__builtins__");
            if (!globals.Contains(builtinsKey).Value)
            {
                // Get builtins module
                var builtinsModule = PyBuiltinsModule.Instance;
                globals.SetItem(builtinsKey, builtinsModule);  // Use SetItem to update _keys
            }

            PyCodeObject codeObject;

            // If source is already a code object, use it directly
            if (source is PyCodeObject pyCode)
            {
                codeObject = pyCode;
                if (pyCode.FreeVars.Count != 0)
                    throw PyTypeError.Create("code object passed to eval() may not contain free variables");
            }
            // If source is a string, parse and compile it
            else
            {
                string filename = "<string>";
                // builtin_eval_impl: the leading spaces and tabs are no indentation.
                string sourceCode = SourceAsString(source, "eval", "string, bytes or code", false, filename).TrimStart(' ', '\t');

                try
                {
                    codeObject = Compiler.CompileSource(sourceCode, filename, CompileMode.Eval, 0, InheritedCompilerFlags());
                }
                catch (Exception ex)
                {
                    throw CompilerError(ex, filename);
                }
            }

            // 4. Execute with provided globals/locals
            if (FrameOfGenerator(codeObject, globals) is { } suspended)
                return suspended;
            var vm = PyVM.Instance;
            if (localsMapping != null)
                return vm.ExecuteWithNamespace(codeObject, PyVM.ScopeOver(globals), localsMapping);
            return vm.ExecuteExpression(codeObject, globals, locals);
        }

        private static PyObject CallCompile(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Python/bltinmodule.c:builtin_compile_impl
            // compile(source, filename, mode, flags=0, dont_inherit=False, optimize=-1)

            if (args.Length > 6)
                throw PyTypeError.Create($"compile() takes at most 7 arguments ({args.Length} given)");
            // The clinic parser: the first three by position or by name.
            string[] required = { "source", "filename", "mode" };
            for (int i = 0; i < Math.Min(3, args.Length); i++)
                if (KwValue(kwargs, required[i]) != null)
                    throw PyTypeError.Create($"argument for compile() given by name ('{required[i]}') and position ({i + 1})");
            if (args.Length < 3)
            {
                var filled = new PyObject[3];
                Array.Copy(args, filled, args.Length);
                for (int i = args.Length; i < 3; i++)
                    filled[i] = KwValue(kwargs, required[i]) ??
                        throw PyTypeError.Create($"compile() missing required argument '{required[i]}' (pos {i + 1})");
                args = filled;
            }

            var source = args[0];
            var filenameArg = args[1];
            var modeArg = args[2];
            // builtin_compile_impl: PyCF_MASK, PyCF_MASK_OBSOLETE and PyCF_COMPILE_MASK.
            var flagsArgument = args.Length > 3 ? args[3] : KwValue(kwargs, "flags");
            // The clinic's int converter: _PyLong_AsInt over __index__.
            int compileFlags = flagsArgument == null ? 0 : PyArgs.Int(flagsArgument);
            if ((compileFlags & ~(0x1FE0000 | 0x10 | 0x400 | 0x200 | 0x1000 | 0x2000 | 0x4000)) != 0)
                throw PyValueError.Create("compile(): unrecognised flags");
            // int flags = args.Length > 3 ? ... : 0;  // Future: compilation flags
            // bool dont_inherit = args.Length > 4 ? ... : false;
            // int optimize = args.Length > 5 ? ... : -1;

            // Extract filename
            string filename;
            if (filenameArg is PyString pyFilename)
                filename = pyFilename.Value;
            else
                throw PyTypeError.Create($"compile() arg 2 must be str, not '{filenameArg.GetTypeName()}'");

            // Extract mode
            string mode;
            if (modeArg is PyString pyMode)
                mode = pyMode.Value;
            else
                throw PyTypeError.Create($"compile() arg 3 must be str, not '{modeArg.GetTypeName()}'");

            // Validate mode
            if (mode != "exec" && mode != "eval" && mode != "single")
                throw PyValueError.Create($"compile() mode must be 'exec', 'eval' or 'single', not '{mode}'");

            // If source is already a code object, return it as-is (CPython behavior)
            if (source is PyCodeObject pyCode)
                return pyCode;

            // builtin_compile_impl: flags, dont_inherit, optimize, and a tree in place of text.
            PyObject Option(int position, string name) => args.Length > position ? args[position]
                : kwargs != null ? PyObject.KwValue(kwargs, name) : null;
            int optimizeLevel = Option(5, "optimize") is PyObject optimizeValue ? PyArgs.Int(optimizeValue) : -1;
            if (optimizeLevel < 0)
                optimizeLevel = 0;
            var compileMode = mode switch
            {
                "eval" => CompileMode.Eval,
                "single" => CompileMode.Single,
                _ => CompileMode.File,
            };
            if (PythonAst.IsAst(source))
            {
                if ((compileFlags & PythonAst.PyCF_ONLY_AST) != 0)
                    return source;
                return Compiler.Compile(PythonAst.ObjectToMod(source, compileMode), filename, optimizeLevel, null);
            }

            string sourceCode = SourceAsString(source, "compile", "string, bytes or AST", compileMode == CompileMode.File, filename);
            bool dontInherit = Option(4, "dont_inherit") is PyObject dontInheritValue && dontInheritValue.IsTrue();
            if (!dontInherit)
                compileFlags |= InheritedCompilerFlags();

            PyCodeObject codeObject;

            try
            {
                if ((compileFlags & PythonAst.PyCF_ONLY_AST) != 0)
                    return PythonAst.ModToObject(Compiler.Parse(sourceCode, filename, compileMode, compileFlags));
                codeObject = Compiler.CompileSource(sourceCode, filename, compileMode, optimizeLevel, compileFlags);
            }
            catch (Exception ex)
            {
                if (ex is PythonException pythonError && pythonError.PyException is PySyntaxError) throw;
                throw CompilerError(ex, filename);
            }

            return codeObject;
        }

        // _Py_SourceAsString
        private static string SourceAsString(PyObject cmd, string funcname, string what, bool execInput, string filename)
        {
            if (cmd is PyString text)
            {
                if (text.Value.IndexOf('\0') >= 0)
                    throw new PythonException(new PySyntaxError("source code string cannot contain null bytes"));
                return text.Value;
            }
            byte[] bytes = cmd switch
            {
                PyBytes value => value.Value,
                PyByteArray array => array.ToByteArrayCopy(),
                PyMemoryView view => ((PyBytes)view.GetAttribute("tobytes").Call(new PyObject[0], null)).Value,
                _ => throw PyTypeError.Create($"{funcname}() arg 1 must be a {what} object"),
            };
            if (Array.IndexOf(bytes, (byte)0) >= 0)
                throw new PythonException(new PySyntaxError("source code string cannot contain null bytes"));
            return Tokenizer.DecodeStr(bytes, execInput, filename);
        }

        // PyEval_MergeCompilerFlags: the __future__ statements of the code that calls
        private static int InheritedCompilerFlags()
        {
            const int PyCF_MASK = 0x20000 | 0x40000 | 0x80000 | 0x100000 | 0x200000 | 0x400000 | 0x800000 | 0x1000000;
            return PyVM.CurrentFrame?.Code is PyCodeObject code ? code.Flags & PyCF_MASK : 0;
        }

        /// <summary>What compile, exec and eval raise for source that does not compile.</summary>
        private static Exception CompilerError(Exception error, string filename)
        {
            if (error is PythonException { PyException: PySyntaxError })
                return error;
            if (error is SyntaxErrorException compiler)
                return PySyntaxError.Create(compiler.Text, filename, Math.Max(compiler.LineNumber, 1));
            if (error is PySyntaxErrorException parser)
            {
                var lines = parser.Message.Split('\n');
                var text = lines[lines.Length - 1].Trim();
                if (text.StartsWith("SyntaxError: ", StringComparison.Ordinal)) text = text.Substring("SyntaxError: ".Length);
                return PySyntaxError.Create(text, filename, Math.Max(parser.LineNumber, 1));
            }
            return PySyntaxError.Create("invalid syntax", filename, 1);
        }

        private static PyObject CallExec(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Python/bltinmodule.c:builtin_exec_impl
            // exec(object, globals=None, locals=None)

            if (args.Length == 0)
                throw PyTypeError.Create("exec expected at least 1 argument, got 0");

            if (args.Length > 3)
                throw PyTypeError.Create($"exec expected at most 3 arguments, got {args.Length}");

            var source = args[0];
            var globalsArgument = args.Length > 1 && args[1] != PyNone.Instance ? args[1] : null;
            var localsArgument = args.Length > 2 && args[2] != PyNone.Instance ? args[2] : null;
            PyObject closure = null;
            if (kwargs != null)
                foreach (var key in kwargs.Keys().Items)
                {
                    if (key is not PyString { Value: "closure" })
                        throw PyTypeError.Create($"exec() got an unexpected keyword argument '{key.AsString().Value}'");
                    closure = kwargs.GetItem(key);
                }
            if (closure is PyNone)
                closure = null;
            if (globalsArgument != null && !IsDict(globalsArgument))
                throw PyTypeError.Create($"exec() globals must be a dict, not {globalsArgument.GetTypeName()}");
            if (localsArgument != null && !IsMapping(localsArgument))
                throw PyTypeError.Create($"locals must be a mapping or None, not {localsArgument.GetTypeName()}");
            var (globals, localsMapping) = GlobalsAndNamespace(globalsArgument, localsArgument);
            PyDict locals = localsMapping == null ? localsArgument as PyDict : null;

            // CPython 3.12: If globals is not provided, use current frame's globals/locals
            if (globals == null)
            {
                // Get current executing frame
                var currentFrame = PyVM.CurrentFrame;
                if (currentFrame != null)
                {
                    // Convert frame's globals to PyDict
                    globals = PyGlobalsDict.For(currentFrame.Globals);

                    // Convert frame's local scope to PyDict
                    if (locals == null && localsMapping == null)
                    {
                        bool optimized = (currentFrame.Code.Flags & PyCodeObject.CO_OPTIMIZED) != 0;
                        locals = optimized ? currentFrame.GetOptimizedLocals()
                            : currentFrame.ClassLocalsDict as PyDict ?? globals;
                    }
                }
                else
                {
                    // No current frame - create empty dicts
                    globals = new PyDict();
                    if (locals == null)
                        locals = globals;
                }
            }
            else if (locals == null)
            {
                // If globals is provided but locals is not, locals = globals
                locals = globals;
            }

            // CPython 3.12: Add __builtins__ to globals if not present
            var builtinsKey = new PyString("__builtins__");
            if (!globals.Contains(builtinsKey).Value)
            {
                // Get builtins module
                var builtinsModule = PyBuiltinsModule.Instance;
                globals.SetItem(builtinsKey, builtinsModule);  // Use SetItem to update _keys
            }

            PyCodeObject codeObject;

            // If source is already a code object, use it directly
            if (source is PyCodeObject pyCode)
            {
                codeObject = pyCode;
                if (pyCode.FreeVars.Count == 0)
                {
                    if (closure != null)
                        throw PyTypeError.Create("cannot use a closure with this code object");
                }
                else if (closure is not PyTuple { } cells || cells.Items.Length != pyCode.FreeVars.Count)
                    throw PyTypeError.Create($"code object requires a closure of exactly length {pyCode.FreeVars.Count}");
            }
            // If source is a string, compile it first
            else
            {
                if (closure != null)
                    throw PyTypeError.Create("closure can only be used when source is a code object");
                string filename = "<string>";
                string sourceCode = SourceAsString(source, "exec", "string, bytes or code", true, filename);

                try
                {
                    codeObject = Compiler.CompileSource(sourceCode, filename, CompileMode.File, 0, InheritedCompilerFlags());
                }
                catch (Exception ex)
                {
                    throw CompilerError(ex, filename);
                }
            }

            // 4. Execute with provided globals/locals (exec returns None)
            // CPython pattern: PyEval_EvalCode(source, globals, locals)
            // Create a scope chain from the provided globals/locals
            var scopeChain = PyVM.ScopeOver(globals);
            if (closure != null)
            {
                var given = ((PyTuple)closure).Items;
                var cellArray = new PyCell[given.Length];
                for (int i = 0; i < given.Length; i++)
                    cellArray[i] = given[i] as PyCell ?? throw PyTypeError.Create($"arg 5 (closure) expected cell, found {given[i].GetTypeName()}");
                PyVM.Instance.ExecuteWithClosure(codeObject, scopeChain, cellArray);
                return PyNone.Instance;
            }
            if (localsMapping != null)
            {
                PyVM.Instance.ExecuteWithNamespace(codeObject, scopeChain, localsMapping);
                return PyNone.Instance;
            }
            var localScope = PyVM.PushLocals(scopeChain, globals, locals);
            PyVM.Instance.ExecuteModule(codeObject, scopeChain);
            PyVM.StoreLocals(localScope, locals);

            return PyNone.Instance;
        }

        private static PyObject CallBool(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"bool expected at most 1 argument, got {args.Length}");

            if (args.Length == 0)
                return PyBool.False;

            // CPython 3.12: bool(x) is PyObject_IsTrue - it must honour __bool__
            // and then __len__ (Objects/object.c PyObject_IsTrue), not the CLR
            // fast-path ToBool/PyBoolValue.
            return PyBool.FromBool(args[0].IsTrue());
        }

        private static PyObject CallList(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"list expected at most 1 argument, got {args.Length}");

            if (args.Length == 0)
                return new PyList(new PyObject[0]);

            return args[0].AsList();
        }

        private static PyObject CallTuple(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"tuple expected at most 1 argument, got {args.Length}");

            if (args.Length == 0)
                return new PyTuple();

            // tuple_new_impl: an exact tuple is its own answer.
            if (args[0].GetType() == typeof(PyTuple) && ReferenceEquals(args[0].GetPyType(), PyType.TupleType))
                return args[0];
            return args[0].AsTuple();
        }

        private static PyObject CallDict(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"dict expected at most 1 argument, got {args.Length}");

            if (args.Length == 0)
                return new PyDict();

            return args[0].AsDict();
        }

        private static PyObject CallSet(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"set expected at most 1 argument, got {args.Length}");

            if (args.Length == 0)
                return PySet.Empty;

            return new PySet(PySet.SourceItems(args[0]));
        }

        private static PyObject CallFrozenSet(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"frozenset expected at most 1 argument, got {args.Length}");

            if (args.Length == 0)
                return new PyFrozenSet();

            if (args[0] is PyFrozenSet frozen) return frozen;
            return new PyFrozenSet(PySet.IterableItems(args[0]));
        }

        // === 이터레이터 함수들 ===

        private static PyObject CallIter(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: iter(obj) or iter(callable, sentinel)
            // (Python/bltinmodule.c builtin_iter).
            if (args.Length < 1)
                throw PyTypeError.Create($"iter expected at least 1 argument, got {args.Length}");
            if (args.Length > 2)
                throw PyTypeError.Create($"iter expected at most 2 arguments, got {args.Length}");

            if (args.Length == 2)
            {
                if (!args[0].IsCallable())
                    throw PyTypeError.Create($"iter(v, w): v must be callable");
                return new PyCallableIterator(args[0], args[1]);
            }
            return args[0].GetIterator();
        }

        private static PyObject CallNext(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"next expected 1 or 2 arguments ({args.Length} given)");

            var iterator = args[0];
            var defaultValue = args.Length > 1 ? args[1] : null;
            // builtin_next: PyIter_Check before tp_iternext.
            if (!iterator.IsIterator)
                throw PyTypeError.Create($"'{iterator.GetPyType().FullName}' object is not an iterator");

            try
            {
                return iterator.Next();
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                if (defaultValue != null)
                    return defaultValue;
                throw;
            }
        }

        // === 수학 함수들 ===

        // CPython 3.12: Python/bltinmodule.c:builtin_round (lines 2876-2920)
        // Delegates to __round__ special method
        private static PyObject CallRound(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Python/bltinmodule.c builtin_round is Clinic `round(number,
            // ndigits=None)`, and its implementation only dispatches to type(number).__round__.
            // The builtin rounding rules therefore live in the int/float __round__ slots, which is
            // also why a user class defining __round__ (or a float subclass overriding it) has to
            // be reached here rather than through a type test.
            // CPython's clinic parser fills the positional parameters first and then matches keywords
            // by name, so round(number=2.567, ndigits=2) is a legal call. A name matching neither is
            // invalid, and a missing required parameter is reported before that: round(zzz=1) says
            // "round() missing required argument 'number' (pos 1)".
            if (args.Length > 2)
                throw PyTypeError.Create($"round() takes at most 2 arguments ({args.Length} given)");

            var number = args.Length > 0 ? args[0] : PyObject.KwValue(kwargs, "number");
            PyObject ndigits = args.Length > 1 ? args[1] : PyObject.KwValue(kwargs, "ndigits");
            if (number == null)
                throw PyTypeError.Create("round() missing required argument 'number' (pos 1)");
            RejectUnknownKeywords(kwargs, "round", "number", "ndigits");

            var rounder = number.LookupSpecialMethodUncached("__round__");
            if (rounder == null)
                throw PyTypeError.Create(
                    $"type {number.GetTypeName()} doesn't define __round__ method");
            return number.BindSpecialMethod(rounder).Call(
                ndigits == null ? Array.Empty<PyObject>() : new PyObject[] { ndigits }, null);
        }


        // CPython 3.12: Python/bltinmodule.c:builtin_pow (lines 2633-2671)
        // Two-argument form: pow(x, y) computes x ** y
        // Three-argument form: pow(x, y, z) computes (x ** y) % z efficiently
        private static PyObject CallPow(PyObject[] args, PyDict kwargs = null)
        {
            // Measured clinic behaviour: base/exp/mod are positional-or-keyword, a missing required
            // parameter names its position, and too many positionals gets its own text.
            if (args.Length > 3)
                throw PyTypeError.Create($"pow() takes at most 3 arguments ({args.Length} given)");

            var base_ = args.Length > 0 ? args[0] : PyObject.KwValue(kwargs, "base");
            var exp = args.Length > 1 ? args[1] : PyObject.KwValue(kwargs, "exp");
            var mod = args.Length > 2 ? args[2] : PyObject.KwValue(kwargs, "mod");
            if (base_ == null)
                throw PyTypeError.Create("pow() missing required argument 'base' (pos 1)");
            if (exp == null)
                throw PyTypeError.Create("pow() missing required argument 'exp' (pos 2)");
            RejectUnknownKeywords(kwargs, "pow", "base", "exp", "mod");

            if (mod != null && mod is not PyNone)
                return PyAbstract.Power(base_, exp, mod);
            else
            {
                // CPython: Python/bltinmodule.c:2653 - Two-argument power
                return PyVM.Instance.ExecuteBinaryOpType(base_, exp, BinaryOpType.POWER);
            }
        }

        /// <summary>
        /// CPython 3.12: builtin_divmod() - divmod(a, b)
        /// CPython: Python/bltinmodule.c:builtin_divmod (lines 922-932)
        /// Returns tuple (a // b, a % b)
        /// </summary>
        private static PyObject CallDivmod(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"divmod expected exactly 2 arguments ({args.Length} given)");

            var a = args[0];
            var b = args[1];

            // CPython: Python/bltinmodule.c:929 - Call PyNumber_Divmod
            // Delegate to PyObject.DivMod which implements Python floor division semantics
            var result = a.DivMod(b);
            if (result == PyNotImplemented.Instance)
            {
                // CPython binary_op1: try the reflected __rdivmod__ before failing.
                var reflected = b.LookupSpecialMethodUncached("__rdivmod__");
                if (reflected != null)
                {
                    result = b.CallSpecial(reflected, a);
                    if (result != PyNotImplemented.Instance)
                        return result;
                }
                throw PyTypeError.Create($"unsupported operand type(s) for divmod(): '{a.GetTypeName()}' and '{b.GetTypeName()}'");
            }
            return result;
        }

        private static PyObject CallOrd(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"ord expected exactly 1 arguments ({args.Length} given)");

            // PyUnicode_GET_LENGTH: the text, whatever __len__ a subclass declares.
            var text = args[0] is PyStrSubclass subclass ? subclass.GetStrValue() : args[0] as PyString;
            if (text != null && text.Length() == 1)
            {
                return new PyInt(text.CodePointAt(0));
            }
            // builtin_ord
            if (args[0] is PyBytes or PyByteArray)
            {
                var data = BytesMethods.Buffer(args[0]);
                if (data.Length == 1)
                    return new PyInt(data[0]);
                throw PyTypeError.Create($"ord() expected a character, but string of length {data.Length} found");
            }
            if (text == null)
                throw PyTypeError.Create($"ord() expected string of length 1, but {args[0].GetTypeName()} found");
            throw PyTypeError.Create($"ord() expected a character, but string of length {text.Length()} found");
        }

        private static PyObject CallChr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"chr expected exactly 1 arguments ({args.Length} given)");

            if (args[0] is PyInt i)
            {
                if (i.Value < 0 || i.Value > 0x10FFFF)
                    throw PyValueError.Create("chr() arg not in range(0x110000)");
                
                int codepoint = (int)i.Value;
                return new PyString(codepoint <= 0xffff ? new string((char)codepoint, 1) : char.ConvertFromUtf32(codepoint));
            }
            else
            {
                throw PyTypeError.Create("an integer is required");
            }
        }

        // Built-in function은 항상 호출 가능
        public override bool IsCallable() => true;

        // PyCFunctionObject of a module's function: m_self is the module, m_module its name.
        string ICFunction.MethodName => Name;
        PyObject ICFunction.MethodSelf => PyImportSystem.Import(OwnerModule);
        PyObject ICFunction.MethodModule => new PyString(OwnerModule);

        private static PyObject CallDir(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"dir expected at most 1 argument, got {args.Length}");

            // CPython 3.12: Python/bltinmodule.c:844-851 - builtin_dir
            // CPython 3.12: Objects/object.c:1754-1781 - _dir_object
            // CPython 3.12: Objects/object.c:1788-1791 - PyObject_Dir

            if (args.Length == 0)
            {
                // _dir_locals: the sorted names of the caller's locals
                var names = new List<PyObject>();
                // PyMapping_Keys: a mapping as locals is asked for its keys.
                var locals = PyVM.CurrentFrame.GetAttribute("f_locals");
                var keys = locals is PyDict ? locals : locals.GetAttribute("keys").Call(new PyObject[0], null);
                foreach (var name in PySet.IterableItems(keys)) names.Add(name);
                var sortedLocals = new PyList(names);
                sortedLocals.GetAttribute("sort").Call(new PyObject[0], null);
                return sortedLocals;
            }

            var obj = args[0];
            // _dir_object
            var dirMethod = obj.LookupSpecialMethodUncached("__dir__") ??
                throw PyTypeError.Create("object does not provide __dir__");
            var listing = PyList.FromIterable(obj.BindSpecialMethod(dirMethod).Call(Array.Empty<PyObject>(), null));
            listing.GetAttribute("sort").Call(Array.Empty<PyObject>(), null);
            return listing;
        }

        /// <summary>
        /// C# 타입 시스템을 활용한 정확한 메타클래스 검출 (재귀 방지 헬퍼)
        /// </summary>
        private static bool IsMetaclassRecursive(PyClass pyClass)
        {
            if (pyClass.BaseTypes == null) return false;

            // 직접 type을 상속하면 메타클래스
            // Performance: Eliminated LINQ - manual loop instead of Any()
            for (int i = 0; i < pyClass.BaseTypes.Length; i++)
            {
                var t = pyClass.BaseTypes[i];
                if (t == PyType.TypeType || t.Name == "type")
                    return true;
            }
            return false;
        }

        /// <summary>
        /// C# 타입 시스템을 활용한 정확한 메타클래스 검출
        /// </summary>
        private static bool IsMetaclass(PyClass pyClass)
        {
            // 1. PyType 계층 구조를 사용한 정확한 메타클래스 검출 - 직접 'type'을 상속하는 경우만
            if (pyClass.BaseTypes != null)
            {
                // 메타클래스는 직접 type을 상속받거나, 다른 메타클래스를 상속받아야 함
                // Performance: Eliminated LINQ - manual loop instead of Any()
                bool directlyInheritsFromType = false;
                for (int i = 0; i < pyClass.BaseTypes.Length; i++)
                {
                    var t = pyClass.BaseTypes[i];
                    if (t == PyType.TypeType || t.Name == "type")
                    {
                        directlyInheritsFromType = true;
                        break;
                    }
                }
                if (directlyInheritsFromType)
                {
                    #if DEBUG_LOG
                    // Performance: Eliminated LINQ - manual loop instead of Select()
                    var baseTypeNames = new string[pyClass.BaseTypes.Length];
                    for (int i = 0; i < pyClass.BaseTypes.Length; i++)
                    {
                        baseTypeNames[i] = pyClass.BaseTypes[i].Name;
                    }
                    Console.WriteLine($"   ✅ BaseTypes에서 직접 'type' 상속 확인: [{string.Join(", ", baseTypeNames)}]");
                    #endif
                    return true;
                }

                // 다른 메타클래스를 상속하는 경우 (재귀 검사)
                // Performance: Eliminated LINQ - manual loop instead of Any()
                bool inheritsFromMetaclass = false;
                for (int i = 0; i < pyClass.BaseTypes.Length; i++)
                {
                    var t = pyClass.BaseTypes[i];
                    if (t is PyClass baseClass && IsMetaclassRecursive(baseClass))
                    {
                        inheritsFromMetaclass = true;
                        break;
                    }
                }
                if (inheritsFromMetaclass)
                {
                    #if DEBUG_LOG
                    // Performance: Eliminated LINQ - manual loop instead of Select()
                    var baseTypeNames = new string[pyClass.BaseTypes.Length];
                    for (int i = 0; i < pyClass.BaseTypes.Length; i++)
                    {
                        baseTypeNames[i] = pyClass.BaseTypes[i].Name;
                    }
                    Console.WriteLine($"   ✅ BaseTypes에서 메타클래스 상속 확인: [{string.Join(", ", baseTypeNames)}]");
                    #endif
                    return true;
                }
            }

            #if DEBUG_LOG
            // Performance: Eliminated LINQ - manual loop instead of Select()
            if (pyClass.BaseTypes != null)
            {
                var baseTypeNames = new string[pyClass.BaseTypes.Length];
                for (int i = 0; i < pyClass.BaseTypes.Length; i++)
                {
                    baseTypeNames[i] = pyClass.BaseTypes[i].Name;
                }
                Console.WriteLine($"   ❌ 메타클래스가 아님: Name={pyClass.Name}, BaseTypes=[{string.Join(", ", baseTypeNames)}]");
            }
            else
            {
                Console.WriteLine($"   ❌ 메타클래스가 아님: Name={pyClass.Name}, BaseTypes=[]");
            }
            #endif
            return false;
        }

        /// <summary>
        /// update_bases (Python/bltinmodule.c): a base that is not a type may replace itself through
        /// __mro_entries__. This is what makes class C(Generic[T]) and class N(NamedTuple) work. The
        /// originals are kept for __orig_bases__.
        /// </summary>
        private static PyObject[] UpdateBases(PyObject[] args, out PyTuple original)
        {
            original = null;
            List<PyObject> updated = null;
            var given = new PyTuple(args[2..]);
            for (int i = 2; i < args.Length; i++)
            {
                var entry = args[i];
                PyObject method = null;
                if (!(entry is PyType))
                {
                    try { method = entry.GetAttribute("__mro_entries__"); }
                    catch (PythonException error) when (error.PyException is PyAttributeError) { }
                }
                if (method == null)
                {
                    updated?.Add(entry);
                    continue;
                }
                if (!(method.Call(new PyObject[] { given }, null) is PyTuple replacement))
                    throw PyTypeError.Create("__mro_entries__ must return a tuple");
                if (updated == null)
                {
                    updated = new List<PyObject>(args[..i]);
                    original = given;
                }
                updated.AddRange(replacement.Items);
            }
            return updated == null ? args : updated.ToArray();
        }

        /// <summary>
        /// builtin___build_class__ (Python/bltinmodule.c:97). Everything that makes a class happens
        /// in the call of the metaclass; this only prepares the namespace and runs the body in it.
        /// </summary>
        private static PyObject CallBuildClass(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("__build_class__: not enough arguments");
            if (args[0] is not PyFunction body)
                throw PyTypeError.Create("__build_class__: func must be a function");
            if (args[1] is not PyString)
                throw PyTypeError.Create("__build_class__: name is not a string");
            args = UpdateBases(args, out var originalBases);
            var name = args[1];
            var bases = new PyTuple(args[2..]);

            PyDict classKeywords = null;
            PyObject metaclass = null;
            if (kwargs != null && kwargs.InternalDict.Count > 0)
            {
                classKeywords = new PyDict();
                foreach (var entry in kwargs.InternalDict)
                {
                    if (entry.Key is PyString { Value: "metaclass" }) metaclass = entry.Value;
                    else classKeywords.InternalDict[entry.Key] = entry.Value;
                }
            }
            if (metaclass == null)
                metaclass = bases.Items.Length == 0 ? PyTypeMetaclass.Instance : bases.Items[0].GetPyType();
            if (metaclass is PyType declared)
                metaclass = PyTypeMetaclass.CalculateMetaclass(declared, bases.Items);

            PyObject prepare = null;
            try { prepare = metaclass.GetAttribute("__prepare__"); }
            catch (PythonException error) when (error.PyException is PyAttributeError) { }
            var ns = prepare == null ? new PyDict() : prepare.Call(new[] { name, bases }, classKeywords);
            if (ns is not PyDict && !ns.HasTypeSlot("__getitem__"))
                throw PyTypeError.Create(
                    $"{(metaclass is PyType named ? named.Name : "<metaclass>")}.__prepare__() must return a mapping, not {ns.GetTypeName()}");

            var cell = PyVM.Instance.ExecuteClassBody(body.CodeObject, body.Closure, ns);
            if (originalBases != null) ns.SetItem(new PyString("__orig_bases__"), originalBases);
            var created = metaclass.Call(new[] { name, bases, ns }, classKeywords);
            if (created is PyType && cell is PyCell classCell && !ReferenceEquals(classCell.Value, created))
            {
                if (!classCell.HasValue)
                    throw PyRuntimeError.Create(
                        $"__class__ not set defining {name.ToRepr().Value} as {created.ToRepr().Value}. Was __classcell__ propagated to type.__new__?");
                throw PyTypeError.Create(
                    $"__class__ set to {classCell.Value.ToRepr().Value} defining {name.ToRepr().Value} as {created.ToRepr().Value}");
            }
            return created;
        }

        /// <summary>
        /// CPython 3.12: _PyObject_FunctionStr reports method owners by __qualname__.
        /// </summary>
        public static string QualifiedClassLabel(PyObject cls)
        {
            if (cls is PyClass pc)
            {
                // _PyObject_FunctionStr reports the owner by __qualname__, which is ht_qualname and
                // no longer a dict entry (Objects/typeobject.c:3416).
                return pc.QualNameText;
            }
            if (cls is PyType pt)
            {
                return pt.Name;
            }
            return cls?.GetTypeName() ?? "object";
        }

        /// <summary>
        /// __import__(name, globals=None, locals=None, fromlist=(), level=0)
        /// CPython 3.12: Python/bltinmodule.c:246-278
        /// </summary>
        private static PyObject CallImport(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: __import__(name, globals=None, locals=None, fromlist=(), level=0)
            // Signature has 5 parameters: name (required), globals, locals, fromlist, level

            // PyArg_ParseTupleAndKeywords "U|OOOi:__import__"
            if (args.Length > 5)
                throw PyTypeError.Create($"__import__() takes at most 5 arguments ({args.Length} given)");
            if (args.Length >= 1 && kwargs != null && kwargs.Contains(new PyString("name")).Value)
                throw PyTypeError.Create("argument for __import__() given by name ('name') and position (1)");
            var nameArgument = args.Length >= 1 ? args[0] : KwValue(kwargs, "name");
            if (nameArgument == null)
                throw PyTypeError.Create("__import__() missing required argument 'name' (pos 1)");
            if (nameArgument is not PyString and not PyStrSubclass)
                throw PyTypeError.Create($"__import__() argument 1 must be str, not {nameArgument.GetTypeName()}");
            var levelArgument = args.Length >= 5 ? args[4] : KwValue(kwargs, "level");
            if (nameArgument.Length() == 0 && (levelArgument == null || !levelArgument.IsTrue()))
                throw PyValueError.Create("Empty module name");

            // Extract parameters from positional args and kwargs
            string name = null;
            Dictionary<string, PyObject> globals = null;
            // locals is unused in CPython (kept for backwards compatibility)
            string[] fromlist = null;
            int level = 0;

            // Process positional arguments
            if (args.Length >= 1)
            {
                name = args[0].ToClrString();
            }
            if (args.Length >= 2 && args[1] != null && args[1] is not PyNone)
            {
                // globals parameter - convert to Dictionary for PyImportSystem
                if (args[1] is PyDict globalsDict)
                {
                    globals = new Dictionary<string, PyObject>();
                    foreach (var kvp in globalsDict.InternalDict)
                    {
                        if (kvp.Key is PyString keyStr)
                            globals[keyStr.Value] = kvp.Value;
                    }
                }
            }
            // args[2] = locals (unused)
            if (args.Length >= 4 && args[3] != null && args[3] is not PyNone)
            {
                fromlist = ExtractFromlist(args[3]);
            }
            if (args.Length >= 5 && args[4] != null && args[4] is not PyNone)
            {
                if (args[4] is PyInt levelInt)
                    level = (int)levelInt.Value;
            }

            // Process keyword arguments (override positional if present)
            if (kwargs != null)
            {
                foreach (var kvp in kwargs.InternalDict)
                {
                    if (kvp.Key is PyString keyStr)
                    {
                        switch (keyStr.Value)
                        {
                            case "name":
                                name = kvp.Value.ToClrString();
                                break;
                            case "globals":
                                if (kvp.Value is PyDict gDict && kvp.Value is not PyNone)
                                {
                                    globals = new Dictionary<string, PyObject>();
                                    foreach (var g in gDict.InternalDict)
                                    {
                                        if (g.Key is PyString gKeyStr)
                                            globals[gKeyStr.Value] = g.Value;
                                    }
                                }
                                break;
                            case "locals":
                                // Unused in CPython
                                break;
                            case "fromlist":
                                if (kvp.Value is not PyNone)
                                    fromlist = ExtractFromlist(kvp.Value);
                                break;
                            case "level":
                                if (kvp.Value is PyInt lvl)
                                    level = (int)lvl.Value;
                                break;
                        }
                    }
                }
            }

            if (name == null)
                throw PyTypeError.Create("__import__() missing required argument: 'name' (pos 1)");

            // CPython 3.12: Python/bltinmodule.c:276-277
            // _calc___package__: neither says where a relative import starts.
            if (level > 0 && globals != null)
            {
                globals.TryGetValue("__package__", out var package);
                globals.TryGetValue("__spec__", out var spec);
                if ((package == null || package is PyNone) && (spec == null || spec is PyNone))
                    PyImportSystem.Import("warnings").GetAttribute("warn").Call(new PyObject[]
                    {
                        new PyString("can't resolve package from __spec__ or __package__, falling back on __name__ and __path__"),
                        PyType.ImportWarningType,
                    }, null);
            }

            // return PyImport_ImportModuleLevelObject(name, globals, locals, fromlist, level);
            return PyImportSystem.Import(name, level, fromlist, globals);
        }

        /// <summary>
        /// Extract fromlist from PyObject (tuple or list)
        /// </summary>
        private static string[] ExtractFromlist(PyObject fromlistObj)
        {
            if (fromlistObj is PyTuple tuple)
            {
                if (tuple.Items.Length == 0)
                    return null; // Empty tuple = no fromlist
                var result = new string[tuple.Items.Length];
                for (int i = 0; i < tuple.Items.Length; i++)
                {
                    result[i] = tuple.Items[i] is PyString s ? s.Value : tuple.Items[i].ToClrString();
                }
                return result;
            }
            else if (fromlistObj is PyList list)
            {
                var items = list.Items;
                if (items.Length == 0)
                    return null; // Empty list = no fromlist
                var result = new string[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    result[i] = items[i] is PyString s ? s.Value : items[i].ToClrString();
                }
                return result;
            }
            return null;
        }

        // PEP 695: Support generic type subscripts for builtin types like tuple[T, T], list[T]
        public override PyObject GetItem(PyObject key)
        {
            // Handle generic type subscripts for builtin collection types
            return Name switch
            {
                "tuple" => CreateGenericTuple(key),
                "list" => CreateGenericList(key),
                "dict" => CreateGenericDict(key),
                "set" => CreateGenericSet(key),
                _ => base.GetItem(key) // Default behavior (throws error)
            };
        }

        private PyObject CreateGenericTuple(PyObject key)
        {
            var typeArgs = new List<PyObject>();
            if (key is PyTuple tuple)
                typeArgs.AddRange(tuple.Items);
            else
                typeArgs.Add(key);
            
            return new PyGenericType($"tuple[{key}]", PyType.TupleType, typeArgs);
        }

        private PyObject CreateGenericList(PyObject key)
        {
            return PyGenericList.Create(key);
        }

        private PyObject CreateGenericDict(PyObject key)
        {
            if (key is PyTuple tuple && tuple.Items.Length == 2)
            {
                return PyGenericDict.Create(tuple.Items[0], tuple.Items[1]);
            }
            else
            {
                throw PyTypeError.Create("dict requires exactly 2 type arguments");
            }
        }

        private PyObject CreateGenericSet(PyObject key)
        {
            var typeArgs = new List<PyObject> { key };
            return new PyGenericType($"set[{key}]", PyType.SetType, typeArgs);
        }

        // builtins.open is _io.open.
        private static PyObject CallOpen(PyObject[] args, PyDict kwargs = null) => Modules.IoModule.Open(args, kwargs);

        /// <summary>
        /// super() builtin function implementation
        /// Returns a proxy object that delegates method calls to parent or sibling class
        /// </summary>
        internal static PyObject CallSuper(PyObject[] args, PyDict kwargs = null)
        {
            // CPython super has no keyword parameters (super_vectorcall / _PyArg_NoKwnames).
            if (kwargs != null && kwargs.Length() > 0)
                throw PyTypeError.Create("super() takes no keyword arguments");

            if (args.Length == 0)
            {
                // super_init_without_args (Objects/typeobject.c): localsplus[0], through its cell
                // once MAKE_CELL has made one, and the __class__ among the free variables.
                var frame = PyVM.CurrentFrame;
                if (frame == null || frame.Code.ArgCount - frame.Code.KwonlyArgCount == 0)
                    throw PyRuntimeError.Create("super(): no arguments");

                var code = frame.Code;
                PyObject instance = frame.LocalsPlus[0];
                if ((code.LocalsPlusKinds[0] & PyCodeObject.FastCell) != 0 && instance is PyCell firstCell)
                    instance = firstCell.Value;
                if (instance == null || PyNull.IsNull(instance))
                    throw PyRuntimeError.Create("super(): arg[0] deleted");

                PyType type = null;
                for (int i = code.FirstFree; i < code.NLocalsPlus; i++)
                {
                    if (code.LocalsPlusNames[i] != "__class__")
                        continue;
                    if (frame.LocalsPlus[i] is not PyCell classCell)
                        throw PyRuntimeError.Create("super(): bad __class__ cell");
                    if (!classCell.HasValue)
                        throw PyRuntimeError.Create("super(): empty __class__ cell");
                    type = classCell.Value as PyType
                        ?? throw PyRuntimeError.Create($"super(): __class__ is not a type ({classCell.Value.GetPyType().Name})");
                    break;
                }
                if (type == null)
                    throw PyRuntimeError.Create("super(): __class__ cell not found");
                return new PySuper(type, instance);
            }
            else if (args.Length == 1)
            {
                // super(type) -> unbound super object (obj_type == NULL).
                if (args[0] is not PyType single)
                    throw PyTypeError.Create($"super() argument 1 must be a type, not {args[0].GetTypeName()}");
                return new PySuper(single, null);
            }
            else if (args.Length == 2)
            {
                // super(type, obj) - standard form
                var type = args[0];
                var obj = args[1];

                if (type is not PyType pyType)
                    throw PyTypeError.Create($"super() argument 1 must be a type, not {type.GetTypeName()}");

                // CPython: obj == None is treated as an unbound super.
                return new PySuper(pyType, obj == PyNone.Instance ? null : obj);
            }
            else
            {
                throw PyTypeError.Create($"super() expected at most 2 arguments, got {args.Length}");
            }
        }

        /// <summary>
        /// property builtin function implementation
        /// Creates a property descriptor
        /// </summary>
        private static PyObject CallProperty(PyObject[] args, PyDict kwargs = null)
        {
            // property([fget[, fset[, fdel[, doc]]]])
            if (args.Length > 4)
            {
                throw PyTypeError.Create($"property expected at most 4 arguments ({args.Length} given)");
            }

            return PyType.PropertyType.Call(args, kwargs);
        }

        // CPython 3.12: Objects/funcobject.c:1055-1068 (cm_init)
        private static PyObject CallClassmethod(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
            {
                throw PyTypeError.Create($"classmethod expected 1 argument ({args.Length} given)");
            }

            var func = args[0];
            return new PyClassmethod(func);
        }

        private static PyObject CallStaticmethod(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
            {
                throw PyTypeError.Create($"staticmethod expected 1 argument ({args.Length} given)");
            }

            var func = args[0];
            return new PyStaticmethod(func);
        }

        /// <summary>
        /// globals() builtin function - returns a dictionary of the current global symbol table
        /// </summary>
        private static PyObject CallGlobals(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Python/bltinmodule.c:1177-1184 (builtin_globals_impl)
            // Returns current_frame->f_globals (actual reference, not a copy)
            // CPython 3.12: Python/ceval.c:2385-2393 (PyEval_GetGlobals)
            // Returns current_frame->f_globals which is the module's md_dict

            if (args.Length != 0)
            {
                throw PyTypeError.Create($"globals() takes no arguments ({args.Length} given)");
            }

            // Get the current global scope from the VM
            var currentFrame = PyVM.CurrentFrame;
            if (currentFrame == null)
            {
                return new PyDict(); // Return empty dict if no frame
            }

            // Get the global scope from the current frame's scope chain
            var globalScope = currentFrame.ScopeChain?.GlobalScope;
            if (globalScope == null)
            {
                return new PyDict(); // Return empty dict if no global scope
            }

            // CPython 3.12: Python/ceval.c:2392 - return current_frame->f_globals
            // f_globals is the same as module->md_dict
            // In SharpPy: GlobalScope.Module references the PyModule, return its __dict__
            if (globalScope.Module != null)
            {
                // Return the module's __dict__ - same object every time
                // This ensures globals() is module.__dict__ (same object)
                return globalScope.Module.GetAttribute("__dict__");
            }

            // Fallback: Use PyGlobalsDict which synchronizes with globalScope.Variables
            return PyGlobalsDict.DictOf(globalScope.Variables);
        }

        /// <summary>
        /// locals() builtin function - returns a dictionary of the current local symbol table
        /// </summary>
        internal static PyObject CallLocals(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 0)
            {
                throw PyTypeError.Create($"locals() takes no arguments ({args.Length} given)");
            }

            // Get the current frame from the VM
            var currentFrame = PyVM.CurrentFrame;
            if (currentFrame == null)
            {
                return new PyDict(); // Return empty dict if no frame
            }

            if (currentFrame.ClassLocalsDict != null)
                return currentFrame.ClassLocalsDict;

            if ((currentFrame.Code.Flags & PyCodeObject.CO_OPTIMIZED) != 0)
                return currentFrame.GetOptimizedLocals();

            // Get the local scope from the current frame's scope chain
            // At module level or if no local scope, return globals
            var localScope = currentFrame.ScopeChain?.CurrentScope;
            if (localScope == null || localScope.Type == ScopeType.Global)
            {
                // If no local scope or at global level, return globals
                return CallGlobals(new PyObject[0]);
            }

            // Convert the local scope variables to a Python dictionary
            var result = new PyDict();
            foreach (var variable in localScope.Variables)
            {
                result.SetItem(new PyString(variable.Key), variable.Value);
            }

            return result;
        }
        

        #region Buffer Protocol Functions

        /// <summary>
        /// bytes() constructor - creates immutable byte sequences
        /// </summary>
        /// <summary>
        /// CPython 3.12: Objects/bytesobject.c bytes_new. A str source requires an
        /// explicit encoding; other sources are ints or iterables of ints.
        /// </summary>
        public static PyObject CallBytes(PyObject[] args, PyDict kwargs = null) => BytesMethods.BytesNew(args, kwargs);

        private static PyObject[] AppendOne(PyObject[] arr, PyObject extra)
        {
            var result = new PyObject[arr.Length + 1];
            arr.CopyTo(result, 0);
            result[arr.Length] = extra;
            return result;
        }

        /// <summary>
        /// bytearray() constructor - creates mutable byte sequences
        /// </summary>
        private static PyObject CallBytearray(PyObject[] args, PyDict kwargs = null) => new PyByteArray(BytesMethods.ByteArrayInit(args, kwargs));


        /// <summary>
        /// repr() built-in function - returns a printable representation of an object
        /// </summary>
        private static PyObject CallRepr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"repr() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // Use the object's ToRepr() method, which already returns a PyString
            // Don't wrap it again, as that would add extra quotes
            return obj.ToRepr();
        }





        /// <summary>
        /// slice(stop) or slice(start, stop[, step]) - CPython 3.12: Python/bltinmodule.c:2379
        /// Return a slice object representing the set of indices
        /// </summary>
        private static PyObject CallSlice(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0 || args.Length > 3)
                throw PyTypeError.Create($"slice expected at most 3 arguments, got {args.Length}");

            PyObject start, stop, step;

            if (args.Length == 1)
            {
                // slice(stop)
                start = PyNone.Instance;
                stop = args[0];
                step = PyNone.Instance;
            }
            else if (args.Length == 2)
            {
                // slice(start, stop)
                start = args[0];
                stop = args[1];
                step = PyNone.Instance;
            }
            else
            {
                // slice(start, stop, step)
                start = args[0];
                stop = args[1];
                step = args[2];
            }

            return new PySlice(start, stop, step);
        }

        /// <summary>
        /// object() - CPython 3.12: Objects/typeobject.c (object_new)
        /// Return a featureless object that is a base for all classes
        /// </summary>
        private static PyObject CallObject(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 0)
                throw PyTypeError.Create("object() takes no arguments");

            // Return a new base object instance
            return new PyBaseObject();
        }



        /// <summary>
        /// help([object]) - Invoke the built-in help system.
        /// CPython 3.12: Python/bltinmodule.c:1580-1590 (builtin_help)
        /// Note: Simplified implementation - full interactive help requires pydoc module.
        /// </summary>
        private static PyObject CallHelp(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
            {
                // Interactive help (simplified)
                Console.WriteLine("Type help(object) for help about that object.");
                return PyNone.Instance;
            }

            if (args.Length > 1)
                throw PyTypeError.Create($"help expected at most 1 argument, got {args.Length}");

            var obj = args[0];

            // Determine the type of object and format accordingly
            // CPython 3.12: Lib/pydoc.py (Helper.help)
            string objName = GetObjectName(obj);
            string typeName = obj.GetTypeName();
            string doc = GetDocString(obj);

            // Format output based on object type
            // Check PyClass before PyType since PyClass extends PyType
            if (obj is PyClass cls)
            {
                Console.WriteLine($"Help on class {cls.Name}:");
                Console.WriteLine();
                Console.WriteLine($"class {cls.Name}");
            }
            else if (obj is PyType typeObj)
            {
                Console.WriteLine($"Help on class {objName} in module builtins:");
                Console.WriteLine();
                Console.WriteLine($"class {objName}(object)");
            }
            else if (obj is PyBuiltinFunction)
            {
                Console.WriteLine($"Help on built-in function {objName} in module builtins:");
                Console.WriteLine();
                Console.WriteLine($"{objName}(...)");
            }
            else if (obj is PyFunction func)
            {
                Console.WriteLine($"Help on function {func.Name}:");
                Console.WriteLine();
                Console.WriteLine($"{func.Name}(...)");
            }
            else
            {
                Console.WriteLine($"Help on {typeName} object:");
            }

            // Print docstring
            Console.WriteLine();
            if (!string.IsNullOrEmpty(doc))
            {
                // Indent docstring lines
                foreach (var line in doc.Split('\n'))
                {
                    Console.WriteLine($" |  {line.TrimEnd()}");
                }
            }
            else
            {
                Console.WriteLine($"No documentation available for {objName}.");
            }

            return PyNone.Instance;
        }

        /// <summary>
        /// Get object name for help display
        /// </summary>
        private static string GetObjectName(PyObject obj)
        {
            return obj switch
            {
                PyClass c => c.Name,  // PyClass extends PyType, so check first
                PyType t => t.Name,
                PyBuiltinFunction b => b.Name,
                PyFunction f => f.Name,
                _ => obj.GetTypeName()
            };
        }

        /// <summary>
        /// Get __doc__ string from object
        /// CPython 3.12: Objects/object.c (PyObject_GetAttr for __doc__)
        /// </summary>
        private static string GetDocString(PyObject obj)
        {
            try
            {
                // For types, check the type's __dict__
                if (obj is PyType typeObj && typeObj.TypeDict.TryGetValue("__doc__", out var typeDoc))
                {
                    if (typeDoc is PyString docStr)
                        return docStr.Value;
                }

                // For classes, check __doc__ in __dict__
                if (obj is PyClass cls)
                {
                    if (cls.TypeDict.TryGetValue("__doc__", out var clsDoc) && clsDoc is PyString docStr)
                        return docStr.Value;
                }

                // For functions
                if (obj is PyFunction func)
                {
                    var docAttr = func.GetAttribute("__doc__");
                    if (docAttr != PyNone.Instance && docAttr is PyString docStr)
                        return docStr.Value;
                }

                // Generic attribute access
                var docObj = obj.GetAttribute("__doc__");
                if (docObj != PyNone.Instance && docObj is PyString str)
                    return str.Value;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// breakpoint(*args, **kws) - Drop into the debugger.
        /// CPython 3.12: Python/bltinmodule.c - builtin_breakpoint
        /// </summary>
        // PyEval_EvalCode: code that compiled to a coroutine answers with one, as a call of it would.
        private static PyObject FrameOfGenerator(PyCodeObject code, PyObject globals) =>
            (code.Flags & (PyCodeObject.CO_GENERATOR | PyCodeObject.CO_COROUTINE | 0x200)) == 0 ? null :
            PyType.FunctionType.Call(new PyObject[] { code, globals }, null).Call(Array.Empty<PyObject>(), null);

        private static PyObject CallBreakpoint(PyObject[] args, PyDict kwargs = null)
        {
            // builtin_breakpoint
            var sys = PyImportSystem.Import("sys");
            if (!sys.ModuleDict.TryGetValue("breakpointhook", out var hook))
                throw PyRuntimeError.Create("lost sys.breakpointhook");
            return hook.Call(args, kwargs);
        }

        #endregion

        public override string ToString() => $"<built-in function {Name}>";
    }
    
    /// <summary>
    /// CPython 3.12 compatible super() implementation
    /// Based on CPython's superobject (Objects/typeobject.c)
    /// </summary>
    public class PySuper : PyObject, IDescriptor
    {
        // super_descr_get: an unbound super in a class binds to the instance it is reached from.
        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance is PyNone || Object != null)
                return this;
            if (!ReferenceEquals(GetPyType(), PyType.SuperType))
                return GetPyType().Call(new PyObject[] { Type, instance }, null);
            return new PySuper(Type, instance);
        }

        public void Set(PyObject instance, PyObject value) =>
            throw PyAttributeError.Create("'super' object has no attribute '__set__'");

        public void Delete(PyObject instance) =>
            throw PyAttributeError.Create("'super' object has no attribute '__delete__'");

        public bool IsDataDescriptor() => false;

        // CPython 3.12: superobject struct
        public PyType Type { get; private set; }           // __thisclass__: the class invoking super()
        public PyObject Object { get; private set; }       // __self__: the instance (or None)
        public PyType ObjectType { get; private set; }     // obj_type: type of Object (or Object itself if it's a type)

        // super_init on an object PyType_GenericNew made.
        internal static void Install()
        {
            var type = PyType.SuperType;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                if (ReferenceEquals(cls, type))
                    return PyBuiltinFunction.CallSuper(args, kwargs);
                var created = new PySuper(null, null);
                created.SetCustomType((PyType)cls);
                return created;
            }, 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                var target = (PySuper)self;
                var made = (PySuper)PyBuiltinFunction.CallSuper(args, kwargs);
                target.Type = made.Type;
                target.Object = made.Object;
                target.ObjectType = made.ObjectType;
                return PyNone.Instance;
            });
        }

        public PySuper(PyType type, PyObject obj)
        {
            Type = type;
            Object = obj;

            // CPython 3.12: supercheck() logic (Objects/typeobject.c:10431-10481)
            // Determine obj_type based on obj, following CPython's exact validation
            if (obj != null)
            {
                ObjectType = SuperCheck(type, obj);
            }
        }

        /// <summary>
        /// CPython 3.12: supercheck() (Objects/typeobject.c:10431-10481)
        /// Check that a super() call makes sense and return the appropriate type object.
        ///
        /// obj can be a class, or an instance of one:
        /// - If it is a class, it must be a subclass of 'type'. Return obj.
        /// - If it is an instance, it must be an instance of 'type'. Return obj.__class__.
        ///
        /// But when obj is an instance, we also allow Py_TYPE(obj) != subclass of type,
        /// as long as obj.__class__ is! This allows using super() with a proxy for obj.
        /// </summary>
        private static PyType SuperCheck(PyType type, PyObject obj)
        {
            // Case 1: obj is a type and subtype of 'type' (class method case)
            // CPython: if (PyType_Check(obj) && PyType_IsSubtype((PyTypeObject *)obj, type))
            if (obj is PyType objAsType)
            {
                if (objAsType.IsSubclassOf(type))
                {
                    return objAsType;
                }
                // If obj is a type but NOT a subtype of 'type', try Case 2
                // This is critical for metaclass methods where obj is a class
                // and type is the metaclass
            }

            // Case 2: Normal case - obj is an instance of 'type'
            // CPython: if (PyType_IsSubtype(Py_TYPE(obj), type))
            PyType objType = obj.GetPyType() as PyType;
            if (objType != null && objType.IsSubclassOf(type))
            {
                return objType;
            }

            // Case 3: Try the slow way - check obj.__class__
            // CPython: _PyObject_LookupAttr(obj, &_Py_ID(__class__), &class_attr)
            try
            {
                PyObject classAttr = obj.GetAttribute("__class__");
                if (classAttr is PyType classType && classType != objType)
                {
                    if (classType.IsSubclassOf(type))
                    {
                        return classType;
                    }
                }
            }
            catch
            {
                // __class__ lookup failed, continue to error
            }

            throw PyTypeError.Create("super(type, obj): obj must be an instance or subtype of type");
        }

        /// <summary>
        /// CPython 3.12: super_getattro() -> do_super_lookup()
        /// </summary>
        // CPython PySuper_Type: name "super", base object.
        public override PyType GetPyType() => _customType ?? PyType.SuperType;

        public override string GetTypeName() => "super";

        // CPython super_repr:
        //   obj_type != NULL: <super: <class 'TYPE'>, <OBJTYPE object>>
        //   obj_type == NULL: <super: <class 'TYPE'>, NULL>
        protected internal override PyString Repr()
            => ObjectType != null
                ? new PyString($"<super: <class '{Type.Name}'>, <{ObjectType.Name} object>>")
                : new PyString($"<super: <class '{Type.Name}'>, NULL>");

        // super_getattro (Objects/typeobject.c): __class__ is the super object's own; every other
        // name goes through do_super_lookup.
        public override PyObject GetAttribute(string name)
        {
            if (name == "__class__")
                return base.GetAttribute(name);
            return DoSuperLookup(name);
        }

        // super_members
        internal PyObject Own(string name)
        {
            switch (name)
            {
                case "__thisclass__": return Type;
                case "__self__": return Object ?? PyNone.Instance;
                case "__self_class__": return (PyObject)ObjectType ?? PyNone.Instance;
            }
            return null;
        }

        // do_super_lookup: the bases after __thisclass__ answer first, with tp_descr_get applied;
        // an unbound super or a miss falls to PyObject_GenericGetAttr on the super object.
        private PyObject DoSuperLookup(string name)
        {
            if (ObjectType != null)
            {
                var found = SuperLookupDescr(name);
                if (found != null)
                {
                    var descriptor = found as IDescriptor
                        ?? (IsPythonDescriptor(found) ? new PyDescriptorWrapper(found) : null);
                    if (descriptor == null)
                        return found;
                    // Only pass obj if this is instance-mode super (SF ID #743627).
                    return descriptor.Get(ReferenceEquals(Object, ObjectType) ? null : Object, ObjectType);
                }
            }
            return base.GetAttribute(name);
        }

        // _super_lookup_descr: each base's own dict, starting after __thisclass__ in obj_type's MRO.
        private PyObject SuperLookupDescr(string name)
        {
            var mro = ObjectType.MRO;
            if (mro == null)
                return null;
            int n = mro.Count;
            int i = 0;
            for (; i + 1 < n; i++)
            {
                if (ReferenceEquals(mro[i], Type))
                    break;
            }
            i++;
            for (; i < n; i++)
            {
                var found = mro[i].LookupOwnDict(name, out _);
                if (found != null)
                    return found;
            }
            return null;
        }

        public override string ToString() => ToRepr().Value;
    }

    /// <summary>
    /// CPython 3.12: _sitebuiltins.Quitter - exit/quit objects for REPL
    /// CPython: Lib/_sitebuiltins.py:21-42
    /// </summary>
    public class PyQuitter : PyObject
    {
        private readonly string _name;
        private readonly string _message;

        public PyQuitter(string name)
        {
            _name = name;
            // CPython: Lib/_sitebuiltins.py:24
            _message = $"Use {name}() or Ctrl-Z plus Return to exit";
        }

        // CPython: Lib/_sitebuiltins.py:30-36
        protected internal override PyString Repr()
        {
            return new PyString(_message);
        }

        // CPython: Lib/_sitebuiltins.py:38-42
        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            // When called as exit() or quit(), raise SystemExit
            // CPython 3.12: SystemExit is a BaseException, not Exception
            // This allows REPL to catch it separately from normal exceptions.
            // Lib/_sitebuiltins.py:38 def __call__(self, code=None): raise SystemExit(code).
            // measured: exit().args is (None,), not (); exit(2).args is (2,). The parameter is
            // positional-or-keyword, so exit(code=2) exits with 2, and the refusals are the standard
            // ones a Python function gives, naming Quitter.__call__() (self counts as an argument):
            //   exit(1, 2)      -> takes from 1 to 2 positional arguments but 3 were given
            //   exit(1, code=2) -> got multiple values for argument 'code'
            //   exit(x=1)       -> got an unexpected keyword argument 'x'
            // Measured on the pinned interpreter in probes/gap_l22_quit_msgs.py.
            if (kwargs != null && kwargs.Length() != 0)
            {
                foreach (var key in kwargs.Keys().Items)
                {
                    var name = key is PyString text ? text.Value : key.ToStr().Value;
                    if (name != "code")
                        throw PyTypeError.Create(
                            $"Quitter.__call__() got an unexpected keyword argument '{name}'");
                }
                if (args.Length != 0)
                    throw PyTypeError.Create("Quitter.__call__() got multiple values for argument 'code'");
                var given = kwargs.GetItem(new PyString("code")) ?? PyNone.Instance;
                throw new PythonException(new PySystemExit(new PyObject[] { given }));
            }
            if (args.Length > 1)
                throw PyTypeError.Create(
                    $"Quitter.__call__() takes from 1 to 2 positional arguments but {args.Length + 1} were given");
            var carried = args.Length == 0 ? new PyObject[] { PyNone.Instance } : args;
            throw new PythonException(new PySystemExit(carried));
        }

        public override string ToString() => _message;
    }

    /// <summary>
    /// PyBaseObject - CPython 3.12: The most basic object instance
    /// Returned by object() builtin function
    /// </summary>
    public class PyBaseObject : PyObject
    {
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "object";
        protected internal override PyString Repr() => new PyString($"<object object at 0x{GetHashCode():x}>");
        protected internal override PyString Str() => ToRepr();
    }
}
