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
            var kwDict = kwargs?.InternalDict ?? new Dictionary<PyObject, PyObject>();

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
                if (kvp.Key is PyStr keyStr)
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

    public class PyBuiltinFunction : PyObject
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

        public override PyType GetPyType() => PyType.FunctionType;

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
            _builtinImplementations["len"] = (args, kwargs) => CallLen(args, kwargs);
            _builtinImplementations["abs"] = (args, kwargs) => CallAbs(args, kwargs);
            _builtinImplementations["callable"] = (args, kwargs) => CallCallable(args, kwargs);
            _builtinImplementations["range"] = (args, kwargs) => CallRange(args, kwargs);
            _builtinImplementations["enumerate"] = (args, kwargs) => CallEnumerate(args, kwargs);
            _builtinImplementations["zip"] = (args, kwargs) => CallZip(args, kwargs);
            _builtinImplementations["map"] = (args, kwargs) => CallMap(args, kwargs);
            _builtinImplementations["filter"] = (args, kwargs) => CallFilter(args, kwargs);
            _builtinImplementations["sorted"] = (args, kwargs) => CallSorted(args, kwargs);
            _builtinImplementations["reversed"] = (args, kwargs) => CallReversed(args, kwargs);
            _builtinImplementations["sum"] = (args, kwargs) => CallSum(args, kwargs);
            _builtinImplementations["min"] = (args, kwargs) => CallMin(args, kwargs);
            _builtinImplementations["max"] = (args, kwargs) => CallMax(args, kwargs);
            _builtinImplementations["any"] = (args, kwargs) => CallAny(args, kwargs);
            _builtinImplementations["all"] = (args, kwargs) => CallAll(args, kwargs);
            _builtinImplementations["isinstance"] = (args, kwargs) => CallIsInstance(args, kwargs);
            _builtinImplementations["issubclass"] = (args, kwargs) => CallIsSubclass(args, kwargs);
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
            _builtinImplementations["type.__new__"] = (args, kwargs) => CallTypeNew(args, kwargs);
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
            _builtinImplementations["memoryview"] = (args, kwargs) => CallMemoryview(args, kwargs);
            _builtinImplementations["hex"] = (args, kwargs) => CallHex(args, kwargs);
            _builtinImplementations["oct"] = (args, kwargs) => CallOct(args, kwargs);
            _builtinImplementations["bin"] = (args, kwargs) => CallBin(args, kwargs);
            _builtinImplementations["ascii"] = (args, kwargs) => CallAscii(args, kwargs);
            _builtinImplementations["slice"] = (args, kwargs) => CallSlice(args, kwargs);
            _builtinImplementations["object"] = (args, kwargs) => CallObject(args, kwargs);
            _builtinImplementations["vars"] = (args, kwargs) => CallVars(args, kwargs);
            _builtinImplementations["format"] = (args, kwargs) => CallFormat(args, kwargs);
            _builtinImplementations["help"] = (args, kwargs) => CallHelp(args, kwargs);
            _builtinImplementations["breakpoint"] = (args, kwargs) => CallBreakpoint(args, kwargs);
        }

        // 내장 함수 호출 - CPython 3.12 호환: kwargs 지원
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
#if DEBUG_LOG
            Console.WriteLine($"[PyBuiltinFunction.Call] Name={Name}, args.Length={args.Length}");
#endif

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

            // 기존 구현이 있으면 사용 (kwargs 무시)
            if (_implementation != null)
            {
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
            var sep = new PyStr(" ");
            var end = new PyStr("\n");
            PyObject file = null; // sys.stdout는 추후 구현
            var flush = PyBool.False;

            // kwargs 처리
            if (kwargs != null)
            {
                try
                {
                    var sepValue = kwargs.GetItem(new PyStr("sep"));
                    sep = sepValue as PyStr ?? new PyStr(sepValue.AsString());
                }
                catch { }

                try
                {
                    var endValue = kwargs.GetItem(new PyStr("end"));
                    end = endValue as PyStr ?? new PyStr(endValue.AsString());
                }
                catch { }

                try
                {
                    var fileValue = kwargs.GetItem(new PyStr("file"));
                    file = fileValue;
                }
                catch { }

                try
                {
                    var flushValue = kwargs.GetItem(new PyStr("flush"));
                    flush = flushValue as PyBool ?? PyBool.FromBool(flushValue.PyBoolValue());
                }
                catch { }
            }

            // 출력 생성 - CPython 3.12: print는 str()을 사용, repr()이 아님
            // Performance: Eliminated LINQ - manual iteration instead of Select
            var outputParts = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                outputParts[i] = args[i].ToStr().Value;
            }
            var output = string.Join(sep.Value, outputParts);

            // file이 지정되지 않았으면 Console에 출력 (기본값)
            if (file == null)
            {
                Console.Write(output + end.Value);
                if (flush.Value)
                {
                    Console.Out.Flush();
                }
            }
            else
            {
                // file 객체에 쓰기 (추후 구현 가능)
                Console.Write(output + end.Value);
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

            // prompt가 주어지면 출력 (줄바꿈 없이)
            if (args.Length == 1)
            {
                var prompt = args[0].ToStr().Value;
                Console.Write(prompt);
            }

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
                return new PyStr(line);
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

        private static PyObject CallLen(PyObject[] args, PyDict kwargs = null)
        {
            // len()은 kwargs를 받지 않음
            if (kwargs != null && kwargs.InternalDict.Count > 0)
                throw PyTypeError.Create("len() takes no keyword arguments");

            if (args.Length != 1)
                throw PyTypeError.Create($"len() takes exactly one argument ({args.Length} given)");

            try
            {
                return new PyInt(args[0].Length());
            }
            catch (System.Exception)
            {
                throw PyTypeError.Create($"object of type '{args[0].GetTypeName()}' has no len()");
            }
        }

        private static PyObject CallAbs(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"abs() takes exactly one argument ({args.Length} given)");

            // CPython 3.12: Python/bltinmodule.c:2340-2355 - builtin_abs
            return args[0] switch
            {
                PyInt intVal => new PyInt(BigInteger.Abs(intVal.Value)),
                PyFloat floatVal => new PyFloat(Math.Abs(floatVal.Value)),
                PyComplex complexVal => complexVal.Absolute(),
                _ => throw PyTypeError.Create($"bad operand type for abs(): '{args[0].GetTypeName()}'")
            };
        }

        private static PyObject CallCallable(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"callable() takes exactly one argument ({args.Length} given)");

            return PyBool.FromBool(args[0].IsCallable());
        }

        // === 핵심 내장 함수들 ===

        private static PyObject CallRange(PyObject[] args, PyDict kwargs = null)
        {
            return args.Length switch
            {
                1 => args[0] switch
                {
                    PyInt stop => PyRange.Create((long)stop.Value),
                    _ => throw PyTypeError.Create("'int' object cannot be interpreted as an integer")
                },
                2 => (args[0], args[1]) switch
                {
                    (PyInt start, PyInt stop) => PyRange.Create((long)start.Value, (long)stop.Value),
                    _ => throw PyTypeError.Create("'int' object cannot be interpreted as an integer")
                },
                3 => (args[0], args[1], args[2]) switch
                {
                    (PyInt start, PyInt stop, PyInt step) => PyRange.Create((long)start.Value, (long)stop.Value, (long)step.Value),
                    _ => throw PyTypeError.Create("'int' object cannot be interpreted as an integer")
                },
                _ => throw PyTypeError.Create($"range expected at most 3 arguments, got {args.Length}")
            };
        }

        private static PyObject CallEnumerate(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"enumerate expected at most 2 arguments, got {args.Length}");

            var iterable = args[0];

            // CPython 3.12: Objects/enumobject.c:246-292 - enum_new
            // Check kwargs for 'start' parameter first, then positional arg
            long start = 0;
            if (kwargs != null && kwargs.Contains(new PyStr("start")).ToBool())
            {
                start = (long)((PyInt)kwargs.GetItem(new PyStr("start"))).Value;
            }
            else if (args.Length > 1)
            {
                start = (long)((PyInt)args[1]).Value;
            }

            // CPython 3.12 호환: enumerate iterator 객체 반환
            return new PyEnumerateIterator(iterable, start);
        }

        private static PyObject CallZip(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
                return new PyList(new PyObject[0]);

            // Performance: Eliminated LINQ - manual iteration instead of Select().ToArray()
            var iterators = new PyObject[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                iterators[i] = args[i].GetIterator();
            }
            var result = new System.Collections.Generic.List<PyObject>();

            try
            {
                while (true)
                {
                    var items = new PyObject[iterators.Length];
                    for (int i = 0; i < iterators.Length; i++)
                    {
                        items[i] = iterators[i].Next();
                    }
                    result.Add(new PyTuple(items));
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 하나라도 끝나면 종료
            }

            // Performance: Eliminated LINQ - ToArray() replaced with direct conversion
            return new PyList(result.ToArray());
        }

        private static PyObject CallMap(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 2)
                throw PyTypeError.Create($"map() must have at least two arguments.");

            var func = args[0];
            var iterable = args[1];
            var iterator = iterable.GetIterator();
            var result = new System.Collections.Generic.List<PyObject>();

            while (iterator.TryNext(out var item))
            {
                var mappedItem = func.Call(new PyObject[] { item }, null);
                result.Add(mappedItem);
            }
            {
                // 정상 종료
            }

            // Performance: Eliminated LINQ - ToArray() is a List method, not LINQ
            return new PyList(result.ToArray());
        }

        private static PyObject CallFilter(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"filter expected exactly 2 arguments ({args.Length} given)");

            var func = args[0];
            var iterable = args[1];
            var iterator = iterable.GetIterator();
            var result = new System.Collections.Generic.List<PyObject>();

            while (iterator.TryNext(out var item))
            {
                var shouldInclude = func == PyNone.Instance
                    ? item.PyBoolValue()
                    : func.Call(new PyObject[] { item }, null).PyBoolValue();

                if (shouldInclude)
                    result.Add(item);
            }

            // Performance: Eliminated LINQ - ToArray() is a List method, not LINQ
            return new PyList(result.ToArray());
        }

        private static PyObject CallSorted(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: sorted(iterable, *, key=None, reverse=False)
            if (args.Length != 1)
                throw PyTypeError.Create($"sorted expected exactly 1 arguments ({args.Length} given)");

            var iterable = args[0];
            PyObject keyFunc = null;
            var reverse = false;

            // kwargs 처리
            if (kwargs != null)
            {
                try
                {
                    var keyValue = kwargs.GetItem(new PyStr("key"));
                    keyFunc = keyValue != PyNone.Instance ? keyValue : null;
                }
                catch { }

                try
                {
                    var reverseValue = kwargs.GetItem(new PyStr("reverse"));
                    reverse = reverseValue.PyBoolValue();
                }
                catch { }

                // 예상치 못한 키워드 인수 체크
                foreach (var kvp in kwargs.InternalDict)
                {
                    if (kvp.Key is PyStr keyStr)
                    {
                        if (keyStr.Value != "key" && keyStr.Value != "reverse")
                        {
                            throw PyTypeError.Create($"'{keyStr.Value}' is an invalid keyword argument for sorted()");
                        }
                    }
                }
            }

            var items = new System.Collections.Generic.List<PyObject>();
            var iterator = iterable.GetIterator();

            while (iterator.TryNext(out var sortedItem))
            {
                items.Add(sortedItem);
            }

            // key 함수가 있으면 키 값과 함께 정렬
            if (keyFunc != null)
            {
                // Performance: Eliminated LINQ - manual loop instead of Select().ToList()
                var keysAndItems = new System.Collections.Generic.List<(PyObject Item, PyObject Key)>();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var key = keyFunc.Call(new PyObject[] { item }, null);
                    keysAndItems.Add((item, key));
                }

                try
                {
                    keysAndItems.Sort((a, b) =>
                    {
                        var cmpResult = a.Key.RichCompare(b.Key, PyObject.CompareOp.LT);
                        var result = ((PyBool)cmpResult).Value ? -1 :
                                   ((PyBool)a.Key.RichCompare(b.Key, PyObject.CompareOp.GT)).Value ? 1 : 0;
                        return reverse ? -result : result;
                    });
                }
                catch
                {
                    throw PyTypeError.Create("'<' not supported between instances");
                }

                // Performance: Eliminated LINQ - manual loop instead of Select().ToList()
                items.Clear();
                for (int i = 0; i < keysAndItems.Count; i++)
                {
                    items.Add(keysAndItems[i].Item);
                }
            }
            else
            {
                // 간단한 정렬 (비교 가능한 객체만)
                try
                {
                    items.Sort((a, b) =>
                    {
                        var cmpResult = a.RichCompare(b, PyObject.CompareOp.LT);
                        var result = ((PyBool)cmpResult).Value ? -1 :
                                   ((PyBool)a.RichCompare(b, PyObject.CompareOp.GT)).Value ? 1 : 0;
                        return reverse ? -result : result;
                    });
                }
                catch
                {
                    throw PyTypeError.Create("'<' not supported between instances");
                }
            }

            // Performance: Eliminated LINQ - ToArray() is a List method, not LINQ
            return new PyList(items.ToArray());
        }

        private static PyObject CallReversed(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Objects/enumobject.c:281-352 (reversed_new)
            if (args.Length != 1)
                throw PyTypeError.Create($"reversed expected exactly 1 arguments ({args.Length} given)");

            var seq = args[0];

            // CPython 3.12: First try __reversed__ method
            try
            {
                var reversedMethod = seq.GetAttribute("__reversed__");
                if (reversedMethod != null)
                {
                    PyObject result;
                    if (reversedMethod is PyMethod boundMethod)
                        result = boundMethod.Call(new PyObject[0], null);
                    else if (reversedMethod is PyFunction func)
                        result = func.Call(new PyObject[] { seq }, null);
                    else if (reversedMethod is PyBuiltinFunction builtinFunc)
                        result = builtinFunc.Call(new PyObject[0]);
                    else
                        result = reversedMethod.Call(new PyObject[0], null);
                    return result;
                }
            }
            catch
            {
                // No __reversed__ method, try fallback
            }

            // CPython 3.12: Fallback to using __len__ and __getitem__
            // For sequences (list, tuple, string), get length and iterate backwards
            if (seq is PyList pyList)
            {
                var items = new System.Collections.Generic.List<PyObject>();
                for (int i = pyList.Length() - 1; i >= 0; i--)
                    items.Add(pyList.GetItem(i));
                return new PyListIterator(new PyList(items.ToArray()));
            }
            else if (seq is PyTuple pyTuple)
            {
                var items = new System.Collections.Generic.List<PyObject>();
                for (int i = pyTuple.Length() - 1; i >= 0; i--)
                    items.Add(pyTuple.GetItem(new PyInt(i)));
                return new PyListIterator(new PyList(items.ToArray()));
            }
            else if (seq is PyStr pyStr)
            {
                var chars = pyStr.Value.ToCharArray();
                System.Array.Reverse(chars);
                var items = new System.Collections.Generic.List<PyObject>();
                foreach (var c in chars)
                    items.Add(new PyStr(c.ToString()));
                return new PyListIterator(new PyList(items.ToArray()));
            }

            // General fallback: use __len__ and __getitem__
            try
            {
                var lenAttr = seq.GetAttribute("__len__");
                var getItemAttr = seq.GetAttribute("__getitem__");
                if (lenAttr != null && getItemAttr != null)
                {
                    PyObject lenResult;
                    if (lenAttr is PyMethod lenMethod)
                        lenResult = lenMethod.Call(new PyObject[0], null);
                    else if (lenAttr is PyFunction lenFunc)
                        lenResult = lenFunc.Call(new PyObject[] { seq }, null);
                    else if (lenAttr is PyBuiltinFunction lenBuiltin)
                        lenResult = lenBuiltin.Call(new PyObject[0]);
                    else
                        lenResult = lenAttr.Call(new PyObject[0], null);

                    if (lenResult is PyInt lenInt)
                    {
                        var items = new System.Collections.Generic.List<PyObject>();
                        for (int i = (int)lenInt.Value - 1; i >= 0; i--)
                        {
                            PyObject item;
                            if (getItemAttr is PyMethod getItemMethod)
                                item = getItemMethod.Call(new PyObject[] { new PyInt(i) }, null);
                            else if (getItemAttr is PyFunction getItemFunc)
                                item = getItemFunc.Call(new PyObject[] { seq, new PyInt(i) }, null);
                            else if (getItemAttr is PyBuiltinFunction getItemBuiltin)
                                item = getItemBuiltin.Call(new PyObject[] { new PyInt(i) });
                            else
                                item = getItemAttr.Call(new PyObject[] { new PyInt(i) }, null);
                            items.Add(item);
                        }
                        return new PyListIterator(new PyList(items.ToArray()));
                    }
                }
            }
            catch
            {
                // Fallback failed
            }

            throw PyTypeError.Create($"argument to reversed() must be a sequence");
        }

        private static PyObject CallSum(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"sum expected at most 2 arguments ({args.Length} given)");

            var iterable = args[0];
            var start = args.Length > 1 ? args[1] : SmallIntCache.Zero;

            // Fast path: PyList of PyInt with int start → long accumulator
            // CPython: bltinmodule.c:2614 (builtin_sum_impl) has _PyLong_Add fast path
            if (iterable is PyList sumList && (start is PyInt startInt))
            {
                long acc = (long)startInt.Value;
                bool overflow = false;
                int listLen = sumList.Length();
                for (int i = 0; i < listLen; i++)
                {
                    var elem = sumList.GetItem(i);
                    if (elem is PyInt elemInt && elemInt.Value >= long.MinValue && elemInt.Value <= long.MaxValue)
                    {
                        long prev = acc;
                        acc = unchecked(acc + (long)elemInt.Value);
                        if (((prev ^ acc) & ((long)elemInt.Value ^ acc)) < 0)
                        {
                            overflow = true;
                            break;
                        }
                    }
                    else
                    {
                        // Non-int element or BigInteger, fall back to generic path
                        var result = (PyObject)SmallIntCache.GetOrCreate(acc);
                        for (int j = i; j < listLen; j++)
                            result = result.Add(sumList.GetItem(j));
                        return result;
                    }
                }
                if (!overflow)
                {
                    if (acc >= -5 && acc <= 256)
                        return SmallIntCache.GetOrCreate((int)acc);
                    return new PyInt(acc);
                }
                // overflow: fall through to generic
            }

            // Generic path: use long accumulator when possible
            // CPython: bltinmodule.c:2614 (builtin_sum_impl) uses _PyLong_Add fast path
            var iterator = iterable.GetIterator();
            if (start is PyInt genStartInt)
            {
                long acc = (long)genStartInt.Value;
                bool useGenericFallback = false;
                PyObject fallbackResult = null;
                while (iterator.TryNext(out var item))
                {
                    if (item is PyInt itemInt && itemInt.Value >= long.MinValue && itemInt.Value <= long.MaxValue)
                    {
                        long prev = acc;
                        acc = unchecked(acc + (long)itemInt.Value);
                        if (((prev ^ acc) & ((long)itemInt.Value ^ acc)) < 0)
                        {
                            // Overflow: switch to PyObject path for remainder
                            useGenericFallback = true;
                            fallbackResult = new PyInt(new System.Numerics.BigInteger(prev) + itemInt.Value);
                            break;
                        }
                    }
                    else
                    {
                        // Non-int item: switch to PyObject path
                        useGenericFallback = true;
                        fallbackResult = (acc >= -5 && acc <= 256)
                            ? ((PyObject)SmallIntCache.GetOrCreate((int)acc)).Add(item)
                            : new PyInt(acc).Add(item);
                        break;
                    }
                }
                if (useGenericFallback)
                {
                    while (iterator.TryNext(out var item))
                        fallbackResult = fallbackResult.Add(item);
                    return fallbackResult;
                }
                if (acc >= -5 && acc <= 256)
                    return SmallIntCache.GetOrCreate((int)acc);
                return new PyInt(acc);
            }

            // Fully generic path
            var genericResult = start;
            while (iterator.TryNext(out var item))
            {
                genericResult = genericResult.Add(item);
            }
            return genericResult;
        }

        // CPython 3.12 bltinmodule.c:1745 min_max()
        // min() = MinMax(args, kwargs, CompareOp.LT, "min")
        // max() = MinMax(args, kwargs, CompareOp.GT, "max")
        private static PyObject MinMax(PyObject[] args, PyDict kwargs, PyObject.CompareOp op, string name)
        {
            if (args.Length == 0)
                throw PyTypeError.Create($"{name} expected at least 1 argument, got 0");

            // kwargs에서 key, default 추출
            // CPython bltinmodule.c:1749 kwlist[] = {"key", "default", NULL}
            PyObject keyfunc = null;
            PyObject defaultval = null;
            if (kwargs != null)
            {
                try { keyfunc = kwargs.GetItem(new PyStr("key")); } catch { }
                try { defaultval = kwargs.GetItem(new PyStr("default")); } catch { }
            }

            // CPython: key=None이면 key 없는 것과 동일
            if (keyfunc is PyNone)
                keyfunc = null;

            bool positional = args.Length > 1;

            // CPython: multi-arg에서는 default 사용 불가
            if (positional && defaultval != null)
                throw PyTypeError.Create($"Cannot specify a default for {name}() with multiple positional arguments");

            // CPython: positional이면 args 자체를 iterable로 사용, 아니면 args[0]
            PyObject v;
            if (positional)
            {
                v = new PyList(args);  // args tuple → iterable
            }
            else
            {
                v = args[0];
            }

            var iterator = v.GetIterator();

            // CPython: maxitem = 반환할 원본, maxval = 비교용 key 결과
            PyObject maxitem = null;
            PyObject maxval = null;

            while (iterator.TryNext(out var item))
            {
                // CPython bltinmodule.c:1794-1802
                PyObject val;
                if (keyfunc != null)
                    val = keyfunc.Call(new PyObject[] { item }, null);
                else
                    val = item;

                if (maxval == null)
                {
                    // 첫 번째 아이템 — 초기값 설정
                    maxitem = item;
                    maxval = val;
                }
                else
                {
                    // CPython bltinmodule.c:1811 — val끼리 비교 (원본 객체 비교 아님)
                    if (((PyBool)val.RichCompare(maxval, op)).Value)
                    {
                        maxval = val;
                        maxitem = item;
                    }
                }
            }

            // CPython bltinmodule.c:1828-1835
            if (maxitem == null)
            {
                if (defaultval != null)
                    return defaultval;
                throw PyValueError.Create($"{name}() iterable argument is empty");
            }

            return maxitem;
        }

        private static PyObject CallMin(PyObject[] args, PyDict kwargs = null)
        {
            return MinMax(args, kwargs, PyObject.CompareOp.LT, "min");
        }

        private static PyObject CallMax(PyObject[] args, PyDict kwargs = null)
        {
            return MinMax(args, kwargs, PyObject.CompareOp.GT, "max");
        }

        private static PyObject CallAny(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"any expected exactly 1 arguments ({args.Length} given)");

            var iterable = args[0];
            var iterator = iterable.GetIterator();

            while (iterator.TryNext(out var item))
            {
                if (item.PyBoolValue())
                    return PyBool.True;
            }

            return PyBool.False;
        }

        private static PyObject CallAll(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"all expected exactly 1 arguments ({args.Length} given)");

            var iterable = args[0];
            var iterator = iterable.GetIterator();

            while (iterator.TryNext(out var item))
            {
                if (!item.PyBoolValue())
                    return PyBool.False;
            }

            return PyBool.True;
        }

        // === 타입 및 리플렉션 함수들 ===

        private static PyObject CallIsInstance(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"isinstance expected exactly 2 arguments ({args.Length} given)");

            var obj = args[0];
            var classinfo = args[1];

            if (classinfo is PyType type)
            {
                return PyBool.FromBool(IsInstanceExtended(obj, type));
            }
            else if (classinfo is PyTuple tuple)
            {
                // 여러 타입 중 하나인지 확인
                foreach (var item in tuple.Items)
                {
                    if (item is PyType t && IsInstanceExtended(obj, t))
                        return PyBool.True;
                }
                return PyBool.False;
            }
            else
            {
                // CPython 3.12: More helpful error message showing what was passed
                string typename = classinfo?.GetTypeName() ?? "None";
                Console.WriteLine($"[isinstance ERROR] classinfo C# type: {classinfo?.GetType().FullName}");
                throw PyTypeError.Create($"isinstance() arg 2 must be a type or tuple of types, got '{typename}'");
            }
        }
        
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

        private static PyObject CallIsSubclass(PyObject[] args, PyDict kwargs = null)
        {
#if DEBUG
            Console.WriteLine($"[CallIsSubclass] Called with {args.Length} arguments");
            Console.WriteLine($"  args[0] = {args[0]}, type = {args[0].GetType().Name}");
            Console.WriteLine($"  args[1] = {args[1]}, type = {args[1].GetType().Name}");
#endif

            if (args.Length != 2)
                throw PyTypeError.Create($"issubclass expected exactly 2 arguments ({args.Length} given)");

            if (!(args[0] is PyType subclass))
            {
#if DEBUG
                Console.WriteLine($"[CallIsSubclass] args[0] is not PyType!");
#endif
                throw PyTypeError.Create("issubclass() arg 1 must be a class");
            }

#if DEBUG
            Console.WriteLine($"[CallIsSubclass] args[0] IS PyType: {subclass.Name}");
#endif

            if (args[1] is PyType superclass)
            {
#if DEBUG
                Console.WriteLine($"[CallIsSubclass] args[1] IS PyType: {superclass.Name}");
                Console.WriteLine($"[CallIsSubclass] Calling {subclass.Name}.IsSubclassOf({superclass.Name})");
#endif
                var result = subclass.IsSubclassOf(superclass);
#if DEBUG
                Console.WriteLine($"[CallIsSubclass] IsSubclassOf returned: {result}");
#endif
                return PyBool.FromBool(result);
            }
            else if (args[1] is PyTuple tuple)
            {
                // 여러 클래스 중 하나의 서브클래스인지 확인
                foreach (var item in tuple.Items)
                {
                    if (item is PyType t && subclass.IsSubclassOf(t))
                        return PyBool.True;
                }
                return PyBool.False;
            }
            else
            {
#if DEBUG
                Console.WriteLine($"[CallIsSubclass] args[1] is NOT PyType or PyTuple!");
#endif
                throw PyTypeError.Create("issubclass() arg 2 must be a class or tuple of classes");
            }
        }

        private static PyObject CallHasAttr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"hasattr expected exactly 2 arguments ({args.Length} given)");

            var obj = args[0];
            var name = args[1];

            if (!(name is PyStr strName))
                throw PyTypeError.Create("hasattr expected str object, not '" + name.GetTypeName() + "'");

            try
            {
                obj.GetAttribute(strName.Value);
                return PyBool.True;
            }
            catch (PythonException ex) when (ex.PyException is PyAttributeError)
            {
                return PyBool.False;
            }
        }

        private static PyObject CallGetAttr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"getattr expected 2 or 3 arguments ({args.Length} given)");

            var obj = args[0];
            var name = args[1];
            var defaultValue = args.Length > 2 ? args[2] : null;

            if (!(name is PyStr strName))
                throw PyTypeError.Create("getattr expected str object, not '" + name.GetTypeName() + "'");

            try
            {
                var result = obj.GetAttribute(strName.Value);

                // CPython 3.12: If GetAttribute returns None, treat it as AttributeError for default value handling
                // Some attributes (like __qualname__) may return None instead of raising AttributeError
                if (result is PyNone && defaultValue != null)
                {
                    return defaultValue;
                }

                return result;
            }
            catch (PythonException ex) when (ex.PyException is PyAttributeError)
            {
                if (defaultValue != null)
                    return defaultValue;
                throw;
            }
        }

        private static PyObject CallSetAttr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 3)
                throw PyTypeError.Create($"setattr expected exactly 3 arguments ({args.Length} given)");

            var obj = args[0];
            var name = args[1];
            var value = args[2];

            if (!(name is PyStr strName))
                throw PyTypeError.Create("setattr expected str object, not '" + name.GetTypeName() + "'");

            obj.SetAttribute(strName.Value, value);
            return PyNone.Instance;
        }

        private static PyObject CallDelAttr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"delattr expected exactly 2 arguments ({args.Length} given)");

            var obj = args[0];
            var name = args[1];

            if (!(name is PyStr strName))
                throw PyTypeError.Create("delattr expected str object, not '" + name.GetTypeName() + "'");

            obj.DelAttribute(strName.Value);
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

            return new PyInt(args[0].GetHashCode());
        }

        private static PyObject CallHash(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"hash expected exactly 1 arguments ({args.Length} given)");

            return new PyInt(args[0].ToHash());
        }

        // === 타입 변환 함수들 ===

        // CPython 3.12: Objects/unicodeobject.c:14702-14730 (unicode_new_impl)
        private static PyObject CallStr(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: str(object='', encoding=None, errors='strict')
            // x == NULL: return empty string
            if (args.Length == 0)
                return new PyStr("");

            if (args.Length > 3)
                throw PyTypeError.Create($"str() takes at most 3 arguments ({args.Length} given)");

            var x = args[0];

            // CPython 3.12: encoding == NULL and errors == NULL → PyObject_Str(x)
            // Fast path: avoid double wrapping (AsString → new PyStr) for built-in types
            if (args.Length == 1)
            {
                if (x is PyStr xStr) return xStr;
                return new PyStr(x.AsString());
            }

            // CPython 3.12: str(bytes, encoding, errors='strict') → PyUnicode_FromEncodedObject
            var encoding = args.Length > 1 ? args[1] : PyNone.Instance;
            var errors = args.Length > 2 ? args[2] : new PyStr("strict");

            if (encoding == PyNone.Instance && errors is PyStr errStr && errStr.Value == "strict")
            {
                // No encoding specified, just convert to string
                return new PyStr(x.AsString());
            }

            // Decode bytes with specified encoding
            if (!(x is PyBytes pyBytes))
            {
                throw PyTypeError.Create($"decoding to str: need a bytes-like object, {x.GetTypeName()} found");
            }

            if (!(encoding is PyStr encStr))
            {
                throw PyTypeError.Create($"str() argument 2 must be str, not {encoding.GetTypeName()}");
            }

            if (!(errors is PyStr))
            {
                throw PyTypeError.Create($"str() argument 3 must be str, not {errors.GetTypeName()}");
            }

            // Decode bytes using specified encoding
            // For now, only support 'utf-8' and 'latin-1' encodings
            string encodingName = encStr.Value.ToLower();
            try
            {
                System.Text.Encoding enc = encodingName switch
                {
                    "utf-8" or "utf8" => System.Text.Encoding.UTF8,
                    "latin-1" or "latin1" or "iso-8859-1" => System.Text.Encoding.Latin1,
                    "ascii" => System.Text.Encoding.ASCII,
                    _ => throw PyLookupError.Create($"unknown encoding: {encStr.Value}")
                };
                return new PyStr(enc.GetString(pyBytes.Value));
            }
            catch (Exception ex)
            {
                throw PyUnicodeDecodeError.Create($"'{encStr.Value}' codec can't decode bytes: {ex.Message}");
            }
        }

        // CPython 3.12: Objects/longobject.c:5598-5641 (long_new_impl)
        private static PyObject CallInt(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: int() with no args returns 0
            if (args.Length == 0)
                return new PyInt(0);

            if (args.Length > 2)
                throw PyTypeError.Create($"int() takes at most 2 arguments ({args.Length} given)");

            var x = args[0];

            // CPython 3.12: int(x) without base - default base and limit, forward to standard implementation
            if (args.Length == 1)
                return x.AsInt();

            // CPython 3.12: int(x, base) - convert string with explicit base
            var obase = args[1];

            if (x == PyNone.Instance)
            {
                throw PyTypeError.Create("int() missing string argument");
            }

            // CPython 3.12: base validation (lines 5617-5624)
            if (!(obase is PyInt baseInt))
            {
                throw PyTypeError.Create($"int() argument 2 must be int, not {obase.GetTypeName()}");
            }

            int baseValue = (int)baseInt.Value;

            if ((baseValue != 0 && baseValue < 2) || baseValue > 36)
            {
                throw PyValueError.Create("int() base must be >= 2 and <= 36, or 0");
            }

            // CPython 3.12: Only strings (and bytes) can be converted with explicit base (lines 5626-5639)
            if (x is PyStr pyStr)
            {
                return PyInt.FromString(pyStr.Value, baseValue);
            }
            else if (x is PyBytes pyBytes)
            {
                // TODO: Implement bytes-to-int conversion with base
                throw PyTypeError.Create("int() can't convert bytes with explicit base (not implemented yet)");
            }
            else
            {
                throw PyTypeError.Create("int() can't convert non-string with explicit base");
            }
        }

        private static PyObject CallFloat(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: float() with no args returns 0.0
            if (args.Length == 0)
                return new PyFloat(0.0);

            if (args.Length != 1)
                throw PyTypeError.Create($"float expected at most 1 argument ({args.Length} given)");

            return args[0].AsFloat();
        }

        private static PyObject CallComplex(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Objects/complexobject.c:complex_new
            // complex() → 0+0j
            // complex(real) → real+0j
            // complex(real, imag) → real+imag*j
            // complex("1+2j") → parse string

            if (args.Length == 0)
                return new PyComplex(0, 0);

            if (args.Length > 2)
                throw PyTypeError.Create($"complex() takes at most 2 arguments ({args.Length} given)");

            var firstArg = args[0];

            // complex("1+2j") - string parsing
            if (firstArg is PyStr pyStr)
            {
                if (args.Length > 1)
                    throw PyTypeError.Create("complex() can't take second arg if first is a string");

                return PyComplex.FromString(pyStr.Value);
            }

            // Get real part - try __complex__ protocol first
            // CPython 3.12: Objects/complexobject.c:102-127 (try_complex_special_method)
            double real = 0;
            double imag = 0;
            bool gotFromComplex = false;

            if (firstArg is PyInt pyInt)
                real = (double)pyInt.Value;
            else if (firstArg is PyFloat pyFloat)
                real = pyFloat.Value;
            else if (firstArg is PyComplex pyComplex)
            {
                if (args.Length > 1)
                    throw PyTypeError.Create("complex() second arg can't be used when first arg is complex");
                return pyComplex;  // Return as-is
            }
            else if (firstArg is PyBool pyBool)
                real = pyBool.Value ? 1 : 0;
            else
            {
                // Try __complex__ protocol
                try
                {
                    var complexMethod = firstArg.GetAttribute("__complex__");
                    if (complexMethod != null)
                    {
                        PyObject result;
                        if (complexMethod is PyMethod boundMethod)
                            result = boundMethod.Call(new PyObject[0], null);
                        else if (complexMethod is PyFunction func)
                            result = func.Call(new PyObject[] { firstArg }, null);
                        else if (complexMethod is PyBuiltinFunction builtinFunc)
                            result = builtinFunc.Call(new PyObject[0]);
                        else
                            result = complexMethod.Call(new PyObject[0], null);

                        if (result is PyComplex complexResult)
                        {
                            if (args.Length > 1)
                                throw PyTypeError.Create("complex() second arg can't be used when first arg is complex (from __complex__)");
                            return complexResult;
                        }
                        else
                        {
                            throw PyTypeError.Create($"__complex__ returned non-complex (type {result.GetTypeName()})");
                        }
                    }
                }
                catch
                {
                    // No __complex__ method, fall through to error
                }

                throw PyTypeError.Create($"complex() argument must be a string or a number, not '{firstArg.GetTypeName()}'");
            }

            // Get imaginary part if provided
            if (args.Length == 2)
            {
                var secondArg = args[1];
                if (secondArg is PyInt pyInt2)
                    imag = (double)pyInt2.Value;
                else if (secondArg is PyFloat pyFloat2)
                    imag = pyFloat2.Value;
                else if (secondArg is PyBool pyBool2)
                    imag = pyBool2.Value ? 1 : 0;
                else if (secondArg is PyComplex)
                    throw PyTypeError.Create("complex() second arg can't be complex");
                else
                    throw PyTypeError.Create($"complex() second argument must be a number, not '{secondArg.GetTypeName()}'");
            }

            return new PyComplex(real, imag);
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
            PyDict globals = args.Length > 1 && args[1] != PyNone.Instance ? args[1] as PyDict : null;
            PyDict locals = args.Length > 2 && args[2] != PyNone.Instance ? args[2] as PyDict : null;

            // Validate arguments
            if (args.Length > 1 && args[1] != PyNone.Instance && !(args[1] is PyDict))
                throw PyTypeError.Create("globals must be a dict");
            if (args.Length > 2 && args[2] != PyNone.Instance && !(args[2] is PyDict))
                throw PyTypeError.Create("locals must be a mapping");

            // CPython 3.12: If globals is not provided, use current frame's globals/locals
            if (globals == null)
            {
                // Get current executing frame
                var currentFrame = PyVM.CurrentFrame;
                if (currentFrame != null)
                {
                    // Convert frame's globals to PyDict
                    globals = new PyDict();
                    foreach (var kvp in currentFrame.Globals)
                    {
                        globals.InternalDict[new PyStr(kvp.Key)] = kvp.Value;
                    }

                    // Convert frame's local scope to PyDict
                    if (locals == null)
                    {
                        locals = new PyDict();
                        if (currentFrame.LocalScope != null)
                        {
                            foreach (var kvp in currentFrame.LocalScope.Variables)
                            {
                                locals.InternalDict[new PyStr(kvp.Key)] = kvp.Value;
                            }
                        }

                        // If no local variables, use globals for locals (like CPython)
                        if (locals.InternalDict.Count == 0)
                            locals = globals;
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
            var builtinsKey = new PyStr("__builtins__");
            if (!globals.Contains(builtinsKey).Value)
            {
                // Get builtins module
                var builtinsModule = BuiltinsModule.CreateBuiltinsModule();
                globals.SetItem(builtinsKey, builtinsModule);  // Use SetItem to update _keys
            }

            PyCodeObject codeObject;

            // If source is already a code object, use it directly
            if (source is PyCodeObject pyCode)
            {
                codeObject = pyCode;
            }
            // If source is a string, parse and compile it
            else if (source is PyStr pyString)
            {
                string sourceCode = pyString.Value;
                string filename = "<string>";

                try
                {
                    // 1. Tokenize
                    var tokens = PyParserRuntime.LexerSource(sourceCode);

                    // 2. Parse as expression (eval mode)
                    var expression = PyParserRuntime.ParseExpression(tokens, sourceCode, filename);

                    // 3. Compile to bytecode
                    // For eval(), we need to wrap the expression in a Return statement
                    var returnStmt = new ReturnStatement(expression);
                    var statements = new List<Statement> { returnStmt };

                    var compiler = new PythonCompiler();
                    codeObject = compiler.Compile(statements, filename, new List<string>(), null);
                }
                catch (Exception ex)
                {
                    throw PySyntaxError.Create($"invalid syntax: {ex.Message}", filename, 0);
                }
            }
            else
            {
                throw PyTypeError.Create($"eval() arg 1 must be a string, bytes or code object, not '{source.GetTypeName()}'");
            }

            // 4. Execute with provided globals/locals
            var vm = PyVM.Instance;
            return vm.ExecuteExpression(codeObject, globals, locals);
        }

        private static PyObject CallCompile(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Python/bltinmodule.c:builtin_compile_impl
            // compile(source, filename, mode, flags=0, dont_inherit=False, optimize=-1)

            if (args.Length < 3)
                throw PyTypeError.Create($"compile expected at least 3 arguments, got {args.Length}");

            if (args.Length > 6)
                throw PyTypeError.Create($"compile expected at most 6 arguments, got {args.Length}");

            var source = args[0];
            var filenameArg = args[1];
            var modeArg = args[2];
            // int flags = args.Length > 3 ? ... : 0;  // Future: compilation flags
            // bool dont_inherit = args.Length > 4 ? ... : false;
            // int optimize = args.Length > 5 ? ... : -1;

            // Extract filename
            string filename;
            if (filenameArg is PyStr pyFilename)
                filename = pyFilename.Value;
            else
                throw PyTypeError.Create($"compile() arg 2 must be str, not '{filenameArg.GetTypeName()}'");

            // Extract mode
            string mode;
            if (modeArg is PyStr pyMode)
                mode = pyMode.Value;
            else
                throw PyTypeError.Create($"compile() arg 3 must be str, not '{modeArg.GetTypeName()}'");

            // Validate mode
            if (mode != "exec" && mode != "eval" && mode != "single")
                throw PyValueError.Create($"compile() mode must be 'exec', 'eval' or 'single', not '{mode}'");

            // If source is already a code object, return it as-is (CPython behavior)
            if (source is PyCodeObject pyCode)
                return pyCode;

            // Extract source code string
            string sourceCode;
            if (source is PyStr pyString)
                sourceCode = pyString.Value;
            else
                throw PyTypeError.Create($"compile() arg 1 must be a string, bytes or code object, not '{source.GetTypeName()}'");

            PyCodeObject codeObject;

            try
            {
                // 1. Tokenize
                var tokens = PyParserRuntime.LexerSource(sourceCode);

                // 2. Parse based on mode
                if (mode == "eval")
                {
                    // Parse as expression
                    var expression = PyParserRuntime.ParseExpression(tokens, sourceCode, filename);

                    // Wrap in Return statement for eval mode
                    var returnStmt = new ReturnStatement(expression);
                    var statements = new List<Statement> { returnStmt };

                    var compiler = new PythonCompiler();
                    // CPython: code object name is "<module>" for eval mode
                    codeObject = compiler.Compile(statements, "<module>", new List<string>(), filename);
                }
                else if (mode == "exec")
                {
                    // Parse as module (statements)
                    var statements = PyParserRuntime.ParseSource(tokens, sourceCode, filename);

                    var compiler = new PythonCompiler();
                    // CPython: code object name is "<module>" for exec mode
                    codeObject = compiler.Compile(statements, "<module>", new List<string>(), filename);
                }
                else // mode == "single"
                {
                    // Parse as single interactive statement
                    // CPython: single mode prints expression results automatically
                    var statements = PyParserRuntime.ParseSource(tokens, sourceCode, filename);

                    var compiler = new PythonCompiler();
                    // CPython: code object name is "<module>" for single mode
                    // CPython 3.12: Python/bltinmodule.c:780 - mode "single" for interactive
                    // Include/compile.h:8 - Py_single_input = 256
                    codeObject = compiler.Compile(statements, "<module>", new List<string>(), filename, CompileMode.Single);
                }
            }
            catch (Exception ex)
            {
                throw PySyntaxError.Create($"invalid syntax: {ex.Message}", filename, 0);
            }

            return codeObject;
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
            PyDict globals = args.Length > 1 && args[1] != PyNone.Instance ? args[1] as PyDict : null;
            PyDict locals = args.Length > 2 && args[2] != PyNone.Instance ? args[2] as PyDict : null;

            // Validate arguments
            if (args.Length > 1 && args[1] != PyNone.Instance && !(args[1] is PyDict))
                throw PyTypeError.Create("exec() globals must be a dict, not '" + args[1].GetTypeName() + "'");
            if (args.Length > 2 && args[2] != PyNone.Instance && !(args[2] is PyDict))
                throw PyTypeError.Create("exec() locals must be a mapping");

            // CPython 3.12: If globals is not provided, use current frame's globals/locals
            if (globals == null)
            {
                // Get current executing frame
                var currentFrame = PyVM.CurrentFrame;
                if (currentFrame != null)
                {
                    // Convert frame's globals to PyDict
                    globals = new PyDict();
                    foreach (var kvp in currentFrame.Globals)
                    {
                        globals.InternalDict[new PyStr(kvp.Key)] = kvp.Value;
                    }

                    // Convert frame's local scope to PyDict
                    if (locals == null)
                    {
                        locals = new PyDict();
                        if (currentFrame.LocalScope != null)
                        {
                            foreach (var kvp in currentFrame.LocalScope.Variables)
                            {
                                locals.InternalDict[new PyStr(kvp.Key)] = kvp.Value;
                            }
                        }

                        // If no local variables, use globals for locals (like CPython)
                        if (locals.InternalDict.Count == 0)
                            locals = globals;
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
            var builtinsKey = new PyStr("__builtins__");
            if (!globals.Contains(builtinsKey).Value)
            {
                // Get builtins module
                var builtinsModule = BuiltinsModule.CreateBuiltinsModule();
                globals.SetItem(builtinsKey, builtinsModule);  // Use SetItem to update _keys
            }

            PyCodeObject codeObject;

            // If source is already a code object, use it directly
            if (source is PyCodeObject pyCode)
            {
                codeObject = pyCode;
            }
            // If source is a string, compile it first
            else if (source is PyStr pyString)
            {
                string sourceCode = pyString.Value;
                string filename = "<string>";

                try
                {
                    // 1. Tokenize
                    var tokens = PyParserRuntime.LexerSource(sourceCode);

                    // 2. Parse as statements (exec mode)
                    var statements = PyParserRuntime.ParseSource(tokens, sourceCode, filename);

                    // 3. Compile to bytecode
                    // CPython: code object name is "<module>" for exec mode, filename is "<string>"
                    var compiler = new PythonCompiler();
                    codeObject = compiler.Compile(statements, "<module>", new List<string>(), filename);
                }
                catch (Exception ex)
                {
                    throw PySyntaxError.Create($"invalid syntax: {ex.Message}", filename, 0);
                }
            }
            else
            {
                throw PyTypeError.Create($"exec() arg 1 must be a string, bytes or code object, not '{source.GetTypeName()}'");
            }

            // 4. Execute with provided globals/locals (exec returns None)
            // CPython pattern: PyEval_EvalCode(source, globals, locals)
            // Create a scope chain from the provided globals/locals
            var scopeChain = new PyScopeChain();

            // Convert globals dict to global scope variables
            // IMPORTANT: Use scopeChain.GlobalScope.Variables directly (like ExecuteExpression)
            if (globals != null)
            {
                foreach (var kvp in globals.InternalDict)
                {
                    if (kvp.Key is PyStr keyStr)
                    {
                        scopeChain.GlobalScope.Variables[keyStr.Value] = kvp.Value;
                    }
                }
            }

            // If locals is different from globals, push a new local scope
            // CPython 3.12: exec() creates a new local scope if locals dict is provided
            PyScope localScope = null;
            if (locals != null && locals != globals)
            {
                localScope = scopeChain.PushScope(ScopeType.Local, "exec_locals");

                foreach (var kvp in locals.InternalDict)
                {
                    if (kvp.Key is PyStr keyStr)
                    {
                        localScope.Variables[keyStr.Value] = kvp.Value;
                    }
                }
            }

            // Execute the code object
            var vm = PyVM.Instance;
            vm.ExecuteModule(codeObject, scopeChain);

            // Copy modified scope variables back to the dicts
            // IMPORTANT: Use PyDict.Clear() and SetItem() to update both _dict and _keys
            if (localScope != null)
            {
                // If locals dict was provided separately, copy local scope back
                locals.Clear();  // Clears both _dict and _keys
                foreach (var kvp in localScope.Variables)
                {
                    locals.SetItem(new PyStr(kvp.Key), kvp.Value);  // Updates both _dict and _keys
                }
            }
            else
            {
                // If no separate locals, copy global scope back to globals dict
                globals.Clear();
                foreach (var kvp in scopeChain.GlobalScope.Variables)
                {
                    globals.SetItem(new PyStr(kvp.Key), kvp.Value);
                }
            }

            // CPython: exec() always returns None
            return PyNone.Instance;
        }

        private static PyObject CallBool(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"bool expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return PyBool.False;

            return args[0].AsBool();
        }

        private static PyObject CallList(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"list expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyList(new PyObject[0]);

            return args[0].AsList();
        }

        private static PyObject CallTuple(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"tuple expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyTuple();

            return args[0].AsTuple();
        }

        private static PyObject CallDict(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"dict expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyDict();

            // CPython 3.12: dict(obj) supports mapping protocol
            var obj = args[0];

            // Fast path: if obj is already a dict, copy it
            if (obj is PyDict existingDict)
            {
                return existingDict.Copy();
            }

            // CPython 3.12: Mapping protocol - check if obj has keys() method
            // This allows custom mapping types like _EnumDict to be converted to dict
            try
            {
                var keysMethod = obj.GetAttribute("keys");
                if (keysMethod != null && keysMethod.IsCallable())
                {
                    // Call keys() to get all keys
                    var keysResult = keysMethod.Call(new PyObject[0], null);
                    var keysIterator = keysResult.GetIterator();

                    // Create new dict and populate it
                    var newDict = new PyDict();
                    try
                    {
                        while (true)
                        {
                            var key = keysIterator.Next();
                            // Get value using obj[key]
                            var value = obj.GetItem(key);
                            newDict.SetItem(key, value);
                        }
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        // Normal termination of iteration
                    }

                    return newDict;
                }
            }
            catch (PythonException)
            {
                // obj doesn't have keys() method or not callable, fall through
            }

            // Fallback: try AsDict() conversion
            return obj.AsDict();
        }

        private static PyObject CallSet(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"set expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return PySet.Empty;

            var iterable = args[0];
            var items = new System.Collections.Generic.List<PyObject>();
            var iterator = iterable.GetIterator();

            while (iterator.TryNext(out var setItem))
            {
                items.Add(setItem);
            }

            return new PySet(items);
        }

        private static PyObject CallFrozenSet(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"frozenset expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyFrozenSet();

            var iterable = args[0];
            var items = new System.Collections.Generic.List<PyObject>();
            var iterator = iterable.GetIterator();

            while (iterator.TryNext(out var frozenItem))
            {
                items.Add(frozenItem);
            }

            return new PyFrozenSet(items);
        }

        // === 이터레이터 함수들 ===

        private static PyObject CallIter(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"iter expected exactly 1 arguments ({args.Length} given)");

            return args[0].GetIterator();
        }

        private static PyObject CallNext(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"next expected 1 or 2 arguments ({args.Length} given)");

            var iterator = args[0];
            var defaultValue = args.Length > 1 ? args[1] : null;

            if (iterator.TryNext(out var nextVal))
                return nextVal;

            if (defaultValue != null)
                return defaultValue;
            throw PyStopIteration.Create();
        }

        // === 수학 함수들 ===

        // CPython 3.12: Python/bltinmodule.c:builtin_round (lines 2876-2920)
        // Delegates to __round__ special method
        private static PyObject CallRound(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"round expected 1 or 2 arguments ({args.Length} given)");

            var number = args[0];
            PyObject ndigits = args.Length > 1 ? args[1] : null;

            // CPython: builtin_round delegates to __round__ method
            if (number is PyFloat f)
            {
                return f.Round(ndigits);
            }
            else if (number is PyInt i)
            {
                // CPython: Integer returns itself when ndigits is None
                if (ndigits == null || ndigits is PyNone)
                    return i;

                // With ndigits, round to that many decimal places (no-op for ints, but validates ndigits)
                if (ndigits is not PyInt)
                    throw PyTypeError.Create("'int' object cannot be interpreted as an integer");

                return i; // For integers, rounding to any ndigits is identity
            }
            else
            {
                throw PyTypeError.Create($"type '{number.GetTypeName()}' doesn't define __round__ method");
            }
        }

        // CPython 3.12: Python/bltinmodule.c:builtin_pow (lines 2633-2671)
        // Two-argument form: pow(x, y) computes x ** y
        // Three-argument form: pow(x, y, z) computes (x ** y) % z efficiently
        private static PyObject CallPow(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"pow expected 2 or 3 arguments ({args.Length} given)");

            var base_ = args[0];
            var exp = args[1];
            var mod = args.Length > 2 ? args[2] : null;

            if (mod != null)
            {
                // CPython: Python/bltinmodule.c:2656-2668 - Three-argument modular exponentiation
                // Only integers are supported for 3-argument pow()
                if (base_ is PyInt baseInt)
                {
                    return baseInt.PowerMod(exp, mod);
                }
                else
                {
                    throw PyTypeError.Create("pow() 3rd argument not allowed unless all arguments are integers");
                }
            }
            else
            {
                // CPython: Python/bltinmodule.c:2653 - Two-argument power
                return base_.Power(exp);
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
            return a.DivMod(b);
        }

        private static PyObject CallOrd(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"ord expected exactly 1 arguments ({args.Length} given)");

            if (args[0] is PyStr str && str.Value.Length == 1)
            {
                return new PyInt((int)str.Value[0]);
            }
            else
            {
                throw PyTypeError.Create("ord() expected a character, but string of length " + 
                    (args[0] is PyStr s ? s.Value.Length.ToString() : "?") + " found");
            }
        }

        private static PyObject CallChr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"chr expected exactly 1 arguments ({args.Length} given)");

            if (args[0] is PyInt i)
            {
                if (i.Value < 0 || i.Value > 0x10FFFF)
                    throw PyValueError.Create("chr() arg not in range(0x110000)");
                
                return new PyStr(((char)i.Value).ToString());
            }
            else
            {
                throw PyTypeError.Create("an integer is required");
            }
        }

        // Built-in function은 항상 호출 가능
        public override bool IsCallable() => true;

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__name__" => new PyStr(Name),
                "__call__" => this,
                "__new__" when Name == "type" => new PyBuiltinFunction("type.__new__"),
                _ => throw PyAttributeError.Create($"'builtin_function_or_method' object has no attribute '{name}'")
            };
        }

        private static PyObject CallDir(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"dir expected at most 1 arguments ({args.Length} given)");

            // CPython 3.12: Python/bltinmodule.c:844-851 - builtin_dir
            // CPython 3.12: Objects/object.c:1754-1781 - _dir_object
            // CPython 3.12: Objects/object.c:1788-1791 - PyObject_Dir

            if (args.Length == 0)
            {
                // dir() with no arguments - return local variables
                // For now, return empty list (TODO: implement _dir_locals)
                return new PyList();
            }

            var obj = args[0];
            var attributes = new HashSet<string>(); // Use HashSet to avoid duplicates

            // Step 1: Try to call __dir__() method if available
            try
            {
                var dirMethod = obj.GetAttribute("__dir__");
                if (dirMethod != null)
                {
                    // Call __dir__() and return sorted result
                    var result = dirMethod.Call(Array.Empty<PyObject>(), null);
                    if (result is PyList list)
                    {
                        // Sort and return
                        var sortedItems = new List<PyObject>(list.Items);
                        sortedItems.Sort((a, b) => a.ToString().CompareTo(b.ToString()));
                        return new PyList(sortedItems);
                    }
                }
            }
            catch
            {
                // __dir__() not available or failed, continue with default behavior
            }

            // Step 2: Default behavior - collect attributes from type's TypeDict
            var pyType = obj.GetPyType();
            if (pyType != null && pyType.TypeDict != null)
            {
                // Add all attributes from the type's TypeDict
                foreach (var key in pyType.TypeDict.Keys)
                {
                    attributes.Add(key);
                }
            }

            // Step 3: Get object's __dict__ if available (instance attributes)
            try
            {
                var dict = obj.GetAttribute("__dict__");
                if (dict is PyDict pyDict)
                {
                    foreach (var key in pyDict.Keys().Items)
                    {
                        if (key is PyStr keyStr)
                        {
                            attributes.Add(keyStr.Value);
                        }
                    }
                }
            }
            catch
            {
                // No __dict__, that's fine
            }

            // Step 4: Add standard object attributes
            attributes.Add("__class__");
            attributes.Add("__doc__");
            attributes.Add("__module__");

            // Step 5: Convert to sorted list and return
            var sortedAttributes = new List<PyObject>();
            var sortedNames = new List<string>(attributes);
            sortedNames.Sort();
            foreach (var name in sortedNames)
            {
                sortedAttributes.Add(new PyStr(name));
            }

            return new PyList(sortedAttributes);
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

        private static PyObject CallBuildClass(PyObject[] args, PyDict kwargs = null)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🚀 === __build_class__ called with {args.Length} args ===");
            #endif
            if (args.Length < 2)
                throw PyTypeError.Create($"__build_class__() missing required arguments");
            var func = args[0];
            var name = args[1];
            PyObject? metaclass = null;
            
            try
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 func: {func?.GetType().Name}, name: {name?.ToString()}");
                #endif
                for (int i = 2; i < args.Length; i++)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 arg[{i}]: {args[i]?.GetType().Name} = {args[i]}");
                    #endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG_LOG
                Console.WriteLine($"❌ Error printing func/name: {ex.Message}");
                #endif
            }
            
            // Parse args to separate bases and metaclass
            var bases = new List<PyType>();
            bool hasMetaclass = false;

            // CPython 3.12: Handle metaclass from kwargs (new way) or marker (old way)
            // - New way: __build_class__(func, name, *bases, metaclass=Meta) with KW_NAMES
            // - Old way: __build_class__(func, name, *bases, "__metaclass__", metaclass)

            // CPython 3.12: Check kwargs for 'metaclass' keyword argument first
            // CPython reference: Python/bltinmodule.c:137-142
            // Line 140: PyDict_DelItem(mkw, &_Py_ID(metaclass))
            // The 'metaclass' key is removed from kwargs before passing to __prepare__ and __new__
            if (kwargs != null && kwargs.InternalDict.ContainsKey(new PyStr("metaclass")))
            {
                metaclass = kwargs.InternalDict[new PyStr("metaclass")];
                hasMetaclass = true;
                #if DEBUG_LOG
                Console.WriteLine($"   ✅ Metaclass from kwargs: {metaclass}");
                #endif

                // Remove 'metaclass' from kwargs so it doesn't get passed to __prepare__ or __new__
                kwargs.InternalDict.Remove(new PyStr("metaclass"));
            }

            // Check for explicit metaclass marker (old way, for backward compatibility)
            bool hasExplicitMetaclass = false;
            if (!hasMetaclass && args.Length >= 4)
            {
                // Look for "__metaclass__" marker in second-to-last position
                var markerIndex = args.Length - 2;
                if (args[markerIndex] is PyStr marker && marker.Value == "__metaclass__")
                {
                    hasExplicitMetaclass = true;
                    metaclass = args[args.Length - 1];  // Last arg is metaclass
                    hasMetaclass = true;
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ Explicit metaclass detected: {metaclass}");
                    #endif
                    
                    // Process all args before marker as bases
                    for (int i = 2; i < markerIndex; i++)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   Processing arg[{i}] as base class: {args[i]}");
                        #endif
                        if (args[i] is PyType pyType)
                            bases.Add(pyType);
                        else if (args[i] is PyClass baseClass)
                            bases.Add(baseClass);
                        else
                            bases.Add(PyType.ObjectType);
                    }
                }
            }
            
            if (!hasExplicitMetaclass)
            {
                // CPython 3.12 / Traditional logic: all args from index 2 onwards are bases
                // This handles both: kwargs metaclass (all args are bases) and no metaclass cases
                for (int i = 2; i < args.Length; i++)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   Processing arg[{i}] as base class: {args[i]}");
                    #endif
                    if (args[i] is PyType pyType)
                        bases.Add(pyType);
                    else if (args[i] is PyClass baseClass)
                        bases.Add(baseClass);
                    else
                        bases.Add(PyType.ObjectType);
                }
            }

            // CPython 3.12: Do NOT add object to bases here!
            // CPython passes bases as-is to __prepare__ and __new__
            // object is added later by type.__new__ if bases is empty

            // CPython 3.12: Determine metaclass if not explicitly provided
            if (!hasMetaclass)
            {
                if (bases.Count == 0)
                {
                    // No bases: use type
                    metaclass = PyTypeMetaclass.Instance;
                    #if DEBUG_LOG
                    Console.WriteLine($"No bases: using type as metaclass");
                    #endif
                    hasMetaclass = true;
                }
                else
                {
                    // Get initial metaclass from first base (like CPython's line 157-158)
                    var firstBase = bases[0];
                    if (firstBase is PyClass firstBaseAsClass)
                    {
                        metaclass = firstBaseAsClass.Metaclass ?? PyTypeMetaclass.Instance;
                        #if DEBUG_LOG
                        Console.WriteLine($"Initial metaclass from first base {firstBase.Name}: {metaclass}");
                        #endif
                        hasMetaclass = true;
                    }
                    else if (firstBase is PyType)
                    {
                        // Built-in type (like int, str, dict)
                        metaclass = PyTypeMetaclass.Instance;
                        #if DEBUG_LOG
                        Console.WriteLine($"First base is built-in type: using type as metaclass");
                        #endif
                        hasMetaclass = true;
                    }
                }
            }

            // CPython 3.12: Calculate the winner metaclass using _PyType_CalculateMetaclass logic
            // This ensures we pick the most derived metaclass among all bases
            if (hasMetaclass && metaclass != null && bases.Count > 0)
            {
                // Performance: Eliminated LINQ - manual conversion instead of ToArray()
                var basesArray = new PyType[bases.Count];
                for (int i = 0; i < bases.Count; i++)
                {
                    basesArray[i] = bases[i];
                }
                metaclass = PyTypeMetaclass.CallCalculateMetaclass(metaclass, basesArray);
                #if DEBUG_LOG
                Console.WriteLine($"Winner metaclass after CalculateMetaclass: {metaclass}");
                #endif
            }

            var className = name.AsString();

            // CPython 3.12: Call __prepare__ if metaclass has it
            Dictionary<string, PyObject> classNamespace = new Dictionary<string, PyObject>();
            PyDict prepareDict = null;  // Store the original __prepare__ result to preserve special attributes
            PyObject originalPrepareResult = null;  // Track the original __prepare__ result (PyClassInstance for _EnumDict)

            #if DEBUG_LOG
            Console.WriteLine($"🔧 __build_class__ for {className}: hasMetaclass={hasMetaclass}, metaclass={metaclass?.GetType().Name}");
            #endif
            if (hasMetaclass && metaclass != null)
            {
                try
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  🔎 Looking for __prepare__ on {metaclass}");
                    #endif
                    var prepareMethod = metaclass.GetAttribute("__prepare__");
                    #if DEBUG_LOG
                    Console.WriteLine($"  🔎 Got prepareMethod: {prepareMethod?.GetType().Name} (callable={prepareMethod?.IsCallable()})");
                    #endif
                    if (prepareMethod != null && prepareMethod.IsCallable())
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 Calling __prepare__ on metaclass {metaclass}");
                        Console.WriteLine($"   prepareMethod type: {prepareMethod.GetType().Name}");
                        Console.WriteLine($"   prepareMethod is PyMethod: {prepareMethod is PyMethod}");
                        #endif

                        // __prepare__(metacls, name, bases, **kwds)
                        // If prepareMethod is a bound method (PyMethod), the first argument is already bound
                        // So we should NOT pass metaclass again
                        PyObject[] prepareArgs;
                        if (prepareMethod is PyMethod)
                        {
                            // Bound method - don't pass metaclass
                            // Performance: Eliminated LINQ - manual conversion instead of Cast().ToArray()
                            var basesArray = new PyObject[bases.Count];
                            for (int i = 0; i < bases.Count; i++)
                            {
                                basesArray[i] = bases[i];
                            }
                            prepareArgs = new PyObject[] {
                                new PyStr(className),
                                new PyTuple(basesArray)
                            };
                            #if DEBUG_LOG
                            Console.WriteLine($"   Using bound method call with {prepareArgs.Length} args (cls, bases)");
                            #endif
                        }
                        else
                        {
                            // Unbound method - pass metaclass
                            // Performance: Eliminated LINQ - manual conversion instead of Cast().ToArray()
                            var basesArray = new PyObject[bases.Count];
                            for (int i = 0; i < bases.Count; i++)
                            {
                                basesArray[i] = bases[i];
                            }
                            prepareArgs = new PyObject[] {
                                metaclass,
                                new PyStr(className),
                                new PyTuple(basesArray)
                            };
                            #if DEBUG_LOG
                            Console.WriteLine($"   Using unbound method call with {prepareArgs.Length} args (metacls, cls, bases)");
                            #endif
                        }

                        #if DEBUG_LOG
                        Console.WriteLine($"  📞 About to call __prepare__ with {prepareArgs.Length} args");
                        #endif
                        // CPython 3.12: Python/bltinmodule.c:186
                        // ns = PyObject_VectorcallDict(prep, pargs, 2, mkw);
                        // Pass kwargs to __prepare__ (after 'metaclass' key has been removed)
                        var prepareResult = prepareMethod.Call(prepareArgs, kwargs);
                        #if DEBUG_LOG
                        Console.WriteLine($"  ✅ __prepare__ returned: {prepareResult?.GetType().Name} (Type: {prepareResult?.GetTypeName()})");
                        Console.WriteLine($"     is PyDict: {prepareResult is PyDict}, is PyClassInstance: {prepareResult is PyClassInstance}");
                        #endif

                        // Store the original dict object to preserve special attributes (e.g., _member_names_ for _EnumDict)
                        // CPython 3.12: __prepare__ can return dict subclasses like _EnumDict (PyClassInstance)
                        if (prepareResult is PyDict originalPrepareDict)
                        {
                            prepareDict = originalPrepareDict;

                            // Convert the dict contents to Dictionary<string, PyObject> for class body execution
                            var items = originalPrepareDict.Items();
                            foreach (var item in items.Items)
                            {
                                if (item is PyTuple tuple && tuple.Items.Length == 2)
                                {
                                    if (tuple.Items[0] is PyStr keyStr)
                                    {
                                        classNamespace[keyStr.Value] = tuple.Items[1];
                                        #if DEBUG_LOG
                                        Console.WriteLine($"  Added from __prepare__: {keyStr.Value} = {tuple.Items[1]?.GetType().Name}");
                                        #endif
                                    }
                                }
                            }
                        }
                        else if (prepareResult != null)
                        {
                            // Handle dict-like objects (e.g., _EnumDict instance - PyClassInstance)
                            // IMPORTANT: Keep the original object and pass it to metaclass.__new__
                            // This preserves special attributes like _member_names in _EnumDict

                            originalPrepareResult = prepareResult;  // Save the original PyClassInstance

                            #if DEBUG_LOG
                            Console.WriteLine($"__prepare__ returned PyClassInstance or dict-like object: {prepareResult.GetType().Name}");
                            Console.WriteLine($"  Preserving original object for metaclass.__new__");
                            #endif

                            // Try to get initial items from the dict-like object
                            try
                            {
                                var itemsMethod = prepareResult.GetAttribute("items");
                                if (itemsMethod != null && itemsMethod.IsCallable())
                                {
                                    var itemsResult = itemsMethod.Call(new PyObject[0], null);
                                    if (itemsResult is PyList itemsList)
                                    {
                                        foreach (var item in itemsList.Items)
                                        {
                                            if (item is PyTuple tuple && tuple.Items.Length == 2)
                                            {
                                                if (tuple.Items[0] is PyStr keyStr)
                                                {
                                                    classNamespace[keyStr.Value] = tuple.Items[1];
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (PythonException)
                            {
                                // If items() doesn't work, that's okay - namespace is empty
                            }

                            // CRITICAL: Use a wrapper PyDict that stores the original object
                            // This allows us to pass the original _EnumDict instance to metaclass.__new__
                            prepareDict = new PyDict();
                            // Use InternalDict to bypass equality comparator issues
                            prepareDict.InternalDict[new PyStr("__prepare_result__")] = prepareResult;

                            // Also add to classNamespace so ExecuteClassBody can access it
                            classNamespace["__prepare_result__"] = prepareResult;

                            #if DEBUG_LOG
                            Console.WriteLine($"  🔑 Stored original object in __prepare_result__ marker");
                            Console.WriteLine($"     prepareDict.InternalDict.Count = {prepareDict.InternalDict.Count}");
                            Console.WriteLine($"     classNamespace has __prepare_result__: {classNamespace.ContainsKey("__prepare_result__")}");
                            Console.WriteLine($"     prepareResult type: {prepareResult.GetType().Name}");
                            #endif
                        }
                    }
                }
                catch (PythonException pex) when (pex.PyException is PyAttributeError)
                {
                    // __prepare__ not found, that's okay
                    #if DEBUG_LOG
                    Console.WriteLine($"❌ AttributeError during __prepare__: {pex.Message}");
                    Console.WriteLine("   Using empty namespace");
                    #endif
                }
                catch (Exception ex)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"❌ Exception while calling __prepare__: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"   Stack trace: {ex.StackTrace}");
                    #endif
                    // CPython 3.12: If __prepare__ exists but fails, the exception should propagate
                    throw;
                }
            }

            // Execute the class body function to populate the class namespace
            if (func is PyFunction classBodyFunc)
            {
                #if DEBUG_LOG
                Console.WriteLine($"Found class body function for {className}: {classBodyFunc.Name}");
                #endif
                try
                {
                    // CPython 3.12: Execute class body with namespace capture
                    if (classBodyFunc.CodeObject != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"Executing class body with namespace capture...");
                        if (classBodyFunc.Closure != null && classBodyFunc.Closure.Length > 0)
                        {
                            Console.WriteLine($"Class body function has {classBodyFunc.Closure.Length} closure cells");
                            for (int i = 0; i < classBodyFunc.Closure.Length; i++)
                            {
                                Console.WriteLine($"  Closure[{i}]: {classBodyFunc.Closure[i]} (HasValue: {classBodyFunc.Closure[i].HasValue})");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Class body function has no closure");
                        }
                        #endif

                        #if DEBUG_LOG
                        Console.WriteLine($"📦 Initial classNamespace before ExecuteClassBody: {classNamespace.Count} items");
                        // Performance: Eliminated LINQ - manual iteration instead of Take()
                        int count = 0;
                        foreach (var kvp in classNamespace)
                        {
                            Console.WriteLine($"   - {kvp.Key}: {kvp.Value?.GetTypeName()}");
                            if (++count >= 10) break;
                        }
                        #endif

                        var vm = PyVM.Instance;
                        classNamespace = vm.ExecuteClassBody(classBodyFunc.CodeObject, classBodyFunc.Closure, classNamespace);

                        #if DEBUG_LOG
                        Console.WriteLine($"📦 classNamespace after ExecuteClassBody: {classNamespace.Count} items");
                        // Performance: Eliminated LINQ - manual iteration instead of Take()
                        count = 0;
                        foreach (var kvp in classNamespace)
                        {
                            Console.WriteLine($"   - {kvp.Key}: {kvp.Value?.GetTypeName()}");
                            if (++count >= 10) break;
                        }
                        Console.WriteLine($"Class body executed for {className}, captured {classNamespace.Count} variables");
                        #endif
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine("Class body function has no CodeObject, falling back to direct call");
                        #endif
                        var result = classBodyFunc.Call(new PyObject[] {  }, null);
                        #if DEBUG_LOG
                        Console.WriteLine($"Class body executed for {className}, result: {result}");
                        #endif
                    }
                }
                catch (Exception ex)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"Error executing class body for {className}: {ex.Message}");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    #endif
                    // CPython 3.12: If class body execution fails, class creation must fail
                    // Reference: Python/bltinmodule.c:201 - cell = _PyEval_Vector(...)
                    // If cell is NULL (execution failed), class creation is aborted
                    throw;
                }
            }
            else
            {
                #if DEBUG_LOG
                Console.WriteLine($"Class body function is not PyFunction: {func?.GetType().Name}");
                #endif
            }
            
            // CPython 3.12: Handle __classcell__ mechanism
            PyCell classcell = null;
            if (classNamespace.ContainsKey("__classcell__"))
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 Found __classcell__ in class namespace for {className}");
                #endif
                if (classNamespace["__classcell__"] is PyCell cell)
                {
                    classcell = cell;
                    #if DEBUG_LOG
                    Console.WriteLine($"✅ Extracted __classcell__ for later update");
                    #endif
                    // Remove __classcell__ from namespace as it's not a class attribute
                    classNamespace.Remove("__classcell__");
                }
                else
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"⚠️  __classcell__ is not a PyCell: {classNamespace["__classcell__"]?.GetType().Name}");
                    #endif
                }
            }
            
            // Now create class using metaclass if available
            PyClass pyClass;
            
            if (hasMetaclass)
            {
                #if DEBUG_LOG
                Console.WriteLine($"Creating class with metaclass: {metaclass}");
                #endif
                
                // CPython 3.12: Execute the custom metaclass to create the class
                try
                {
                    #if DEBUG_LOG
                    Console.WriteLine("Executing custom metaclass to create class");
                    #endif
                    
                    // Create a PyDict from the class namespace for the metaclass call
                    // CPython 3.12: If __prepare__ returned a custom dict, use it to preserve special attributes
                    PyObject namespaceObj;

                    // Check if prepareDict contains the __prepare_result__ marker
                    PyObject storedPrepareResult = null;
                    if (prepareDict != null && prepareDict.InternalDict.ContainsKey(new PyStr("__prepare_result__")))
                    {
                        storedPrepareResult = prepareDict.InternalDict[new PyStr("__prepare_result__")];
                        #if DEBUG_LOG
                        Console.WriteLine($"  🔍 Found __prepare_result__ marker: {storedPrepareResult?.GetType().Name}");
                        #endif
                    }

                    if (originalPrepareResult != null)
                    {
                        // CPython 3.12: Use the original __prepare__ result directly (PyClassInstance for _EnumDict)
                        // CPython Python/bltinmodule.c:186-209:
                        //   - __prepare__ returns ns dict (line 186)
                        //   - Class body executes with ns as locals (line 201)
                        //   - STORE_NAME in class body calls __setitem__ on ns
                        //   - Pass SAME ns to metaclass.__new__ (line 208) - NO re-processing!

                        Console.WriteLine($"\n🔍 DEBUG: originalPrepareResult check");
                        Console.WriteLine($"  Type: {originalPrepareResult.GetType().Name}");

                        // Check what's in originalPrepareResult now
                        try
                        {
                            var itemsMethod = originalPrepareResult.GetAttribute("items");
                            if (itemsMethod != null && itemsMethod.IsCallable())
                            {
                                var itemsResult = itemsMethod.Call(new PyObject[0], null);
                                if (itemsResult is PyList itemsList)
                                {
                                    Console.WriteLine($"  Items in originalPrepareResult ({itemsList.Items.Length} total):");
                                    int count = 0;
                                    foreach (var item in itemsList.Items)
                                    {
                                        if (item is PyTuple tuple && tuple.Items.Length == 2 && tuple.Items[0] is PyStr keyStr)
                                        {
                                            if (keyStr.Value == "func" || keyStr.Value == "_generate_next_value_")
                                            {
                                                Console.WriteLine($"    {keyStr.Value} = {tuple.Items[1]}, type={tuple.Items[1]?.GetTypeName()}");
                                            }
                                            if (++count > 15) break;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  Error checking items: {ex.Message}");
                        }

                        #if DEBUG_LOG
                        Console.WriteLine($"  ✅ Using original __prepare__ result from variable! Type: {originalPrepareResult.GetType().Name}");
                        #endif

                        // IMPORTANT: STORE_NAME bytecode handler (PyVM.cs:1491-1509) already called
                        // __setitem__ on ClassLocalsDict during class body execution.
                        // We must NOT call __setitem__ again for those items!
                        // Only items from initialNamespace (like _generate_next_value_ inherited from base)
                        // were added by __prepare__ before class body execution.

                        // CPython Python/bltinmodule.c:208-209:
                        //   - Pass the SAME ns dict from __prepare__ to metaclass.__new__
                        //   - Class body execution has already populated this dict via STORE_NAME
                        //   - When custom dict has __setitem__, it was already called for each STORE_NAME
                        //
                        // SharpPy: The classNamespace dict IS the originalPrepareResult after class body execution.
                        // All STORE_NAME operations already called __setitem__ on it.
                        // We should NOT call __setitem__ again here - the dict is already complete!
                        //
                        // Note: This code block is actually unnecessary now because:
                        // 1. When prepareResult != null, ExecuteClassBody uses it as ClassLocalsDict
                        // 2. STORE_NAME calls __setitem__ on ClassLocalsDict directly
                        // 3. The returned classNamespace IS the modified originalPrepareResult
                        // 4. No need to manually call __setitem__ again
                        //
                        // However, keeping this as NO-OP for clarity and future reference.

                        var setitemMethod = originalPrepareResult.GetAttribute("__setitem__");
                        if (setitemMethod != null && setitemMethod.IsCallable())
                        {
                            // NO-OP: All items were already added via STORE_NAME during class body execution
                            // The originalPrepareResult dict is already complete.
                            #if DEBUG_LOG
                            Console.WriteLine($"  ✓ __setitem__ found, but NOT calling it (already called during STORE_NAME)");
                            Console.WriteLine($"     originalPrepareResult now has {classNamespace.Count} items after class body");
                            #endif
                        }
                        else
                        {
                            // Fallback: use SetItem if __setitem__ not available
                            #if DEBUG_LOG
                            Console.WriteLine($"  ⚠️ No __setitem__ found, using SetItem fallback");
                            #endif
                            foreach (var kvp in classNamespace)
                            {
                                try
                                {
                                    originalPrepareResult.SetItem(new PyStr(kvp.Key), kvp.Value);
                                }
                                catch (PythonException)
                                {
                                    originalPrepareResult.SetAttribute(kvp.Key, kvp.Value);
                                }
                            }
                        }
                        namespaceObj = originalPrepareResult;  // Use the original _EnumDict instance
                        #if DEBUG_LOG
                        Console.WriteLine($"📦 Using original __prepare__ result (_EnumDict instance)");
                        Console.WriteLine($"   namespaceObj type after assignment: {namespaceObj?.GetType().Name}, PyType: {namespaceObj?.GetTypeName()}");
                        #endif
                    }
                    else if (storedPrepareResult != null)
                    {
                        // CPython 3.12: Restore the original __prepare__ result from the marker
                        // CRITICAL: Same as above - call __setitem__ to trigger custom dict behavior
                        #if DEBUG_LOG
                        Console.WriteLine($"  ✅ Restoring original __prepare__ result from marker! Type: {storedPrepareResult.GetType().Name}");
                        #endif

                        var setitemMethod = storedPrepareResult.GetAttribute("__setitem__");
                        if (setitemMethod != null && setitemMethod.IsCallable())
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"  🔧 Found __setitem__ method, calling it for each class member");
                            #endif
                            foreach (var kvp in classNamespace)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"    Calling __setitem__('{kvp.Key}', {kvp.Value?.GetTypeName()})");
                                #endif
                                setitemMethod.Call(new PyObject[] { new PyStr(kvp.Key), kvp.Value }, null);
                            }
                        }
                        else
                        {
                            // Fallback
                            #if DEBUG_LOG
                            Console.WriteLine($"  ⚠️ No __setitem__ found, using SetItem fallback");
                            #endif
                            foreach (var kvp in classNamespace)
                            {
                                try
                                {
                                    storedPrepareResult.SetItem(new PyStr(kvp.Key), kvp.Value);
                                }
                                catch (PythonException)
                                {
                                    storedPrepareResult.SetAttribute(kvp.Key, kvp.Value);
                                }
                            }
                        }
                        namespaceObj = storedPrepareResult;  // Use the original _EnumDict instance
                        #if DEBUG_LOG
                        Console.WriteLine($"📦 Using restored __prepare__ result (_EnumDict instance)");
                        Console.WriteLine($"   namespaceObj type after assignment: {namespaceObj?.GetType().Name}, PyType: {namespaceObj?.GetTypeName()}");
                        #endif
                    }
                    else if (prepareDict != null)
                    {
                        // Use prepareDict (regular PyDict)
                        namespaceObj = prepareDict;
                        foreach (var kvp in classNamespace)
                        {
                            ((PyDict)namespaceObj).SetItem(new PyStr(kvp.Key), kvp.Value);
                        }
                        #if DEBUG_LOG
                        Console.WriteLine($"📦 Using __prepare__ dict (PyDict)");
                        #endif
                    }
                    else
                    {
                        // No __prepare__, create a regular PyDict
                        var namespaceDict = new PyDict();
                        foreach (var kvp in classNamespace)
                        {
                            namespaceDict.SetItem(new PyStr(kvp.Key), kvp.Value);
                        }
                        namespaceObj = namespaceDict;
                    }
                    
                    // CPython 3.12: Execute metaclass.__new__ which modifies namespace and calls type.__new__ 
                    #if DEBUG_LOG
                    Console.WriteLine("Executing metaclass.__new__ with namespace modification support");
                    #endif
                    
                    // CPython 3.12: Do NOT pre-update __classcell__ here!
                    // The __class__ cell should point to the class being created (TopMeta), not the metaclass (MiddleMeta)
                    // We'll update it AFTER the class is created
                    if (classcell != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🎯 CPython 3.12: __classcell__ found, will update AFTER class creation");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   Current __classcell__.Value: {classcell.Value}");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   Target class name: {className}");
                        #endif
                    }
                    
                    // Get the __new__ method from the metaclass
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Getting __new__ from metaclass: {metaclass}");
                    #endif
                    var newMethod = metaclass.GetAttribute("__new__");
                    #if DEBUG_LOG
                    Console.WriteLine($"🔍 Got newMethod: {newMethod?.GetType().Name ?? "null"}");
                    #endif
                    if (newMethod != null && newMethod.IsCallable())
                    {
                        #if DEBUG_LOG
                        Console.WriteLine("Found metaclass.__new__ method, executing it");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   newMethod type: {newMethod.GetType().Name}");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   newMethod is PyFunction: {newMethod is PyFunction}");
                        #endif
                        
                        // CPython 3.12: Dynamic __class__ cell binding for inherited metaclass methods
                        // Optimized: Use cached HasClassCell instead of FreeVars.Contains("__class__")
                        if (newMethod is PyFunction pyFunc && pyFunc.CodeObject?.HasClassCell == true)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 CPython 3.12: Adjusting __class__ cell for inherited metaclass method");
                            #endif
                            #if DEBUG_LOG
                            Console.WriteLine($"   Method: {pyFunc.Name}");
                            #endif
                            #if DEBUG_LOG
                            Console.WriteLine($"   Target metaclass: {metaclass}");
                            #endif

                            // Create a copy of the closure and update the __class__ cell
                            if (pyFunc.Closure != null && pyFunc.Closure.Length > 0)
                            {
                                var adjustedClosure = new PyCell[pyFunc.Closure.Length];
                                Array.Copy(pyFunc.Closure, adjustedClosure, pyFunc.Closure.Length);

                                // Optimized: Use cached ClassCellIndex instead of FreeVars.IndexOf("__class__")
                                var classIndex = pyFunc.CodeObject.ClassCellIndex;
                                // Note: ClassCellIndex is combined index (CellVars.Count + freeIndex)
                                // For closure array, we need the index relative to FreeVars
                                var closureIndex = classIndex - pyFunc.CodeObject.CellVars.Count;
                                if (closureIndex >= 0 && closureIndex < adjustedClosure.Length)
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   Original __class__ cell: {adjustedClosure[closureIndex]?.Value}");
                                    #endif
                                    adjustedClosure[closureIndex] = new PyCell(metaclass);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   ✅ Updated __class__ cell[{closureIndex}] to {metaclass}");
                                    #endif
                                    
                                    // Create a new function with the adjusted closure
                                    newMethod = new PyFunction(pyFunc.Name, pyFunc.Implementation,
                                        pyFunc.DefiningModule, pyFunc.TypeParams, adjustedClosure, pyFunc.CodeObject);
                                    // CPython 3.12: Preserve GlobalsDict and ParentScope
                                    ((PyFunction)newMethod).GlobalsDict = pyFunc.GlobalsDict;
                                    ((PyFunction)newMethod).ParentScope = pyFunc.ParentScope;
                                }
                            }
                        }
                        
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 Before creating newArgs: namespaceObj type={namespaceObj?.GetType().Name}, PyType={namespaceObj?.GetTypeName()}");
                        #endif

                        // Performance: Eliminated LINQ - manual conversion instead of Cast().ToArray()
                        var basesArray = new PyObject[bases.Count];
                        for (int i = 0; i < bases.Count; i++)
                        {
                            basesArray[i] = bases[i];
                        }
                        var newArgs = new PyObject[] {
                            metaclass,                      // cls
                            new PyStr(className),        // name
                            new PyTuple(basesArray),        // bases
                            namespaceObj                    // namespace - PyDict or dict-like object (e.g., _EnumDict)
                        };

                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 After creating newArgs: newArgs[3] type={newArgs[3]?.GetType().Name}, PyType={newArgs[3]?.GetTypeName()}");
                        #endif

                        // CPython 3.12: Pass keyword arguments (like boundary, **kwds) to metaclass.__new__
                        // Metaclasses may have keyword-only parameters after *
                        // CPython reference: Python/bltinmodule.c:205-209
                        // Line 208: cls = PyObject_VectorcallDict(meta, margs, 3, mkw);
                        // mkw contains the keyword arguments passed to __build_class__
                        PyDict newKwargs = kwargs ?? new PyDict();

                        // CPython 3.12: Metaclass methods use __class__ cell variable (compile-time generated)
                        // No need to manipulate global scope - __class__ cell is set during class creation

                        PyObject? result = null;
                        try
                        {
                            // CPython 3.12: slot_tp_new uses _PyObject_Call_Prepend to explicitly prepend the type argument
                            // This ensures __new__ always receives (cls, name, bases, namespace) regardless of binding
                            #if DEBUG_LOG
                            Console.WriteLine($"🚀 Calling metaclass.__new__ for class: {className}");
                            Console.WriteLine($"  newMethod type: {newMethod.GetType().Name}");
                            Console.WriteLine($"  newArgs[3] type: {newArgs[3]?.GetType().Name}, PyType: {newArgs[3]?.GetTypeName()}");
                            #endif

                            // CPython 3.12: If newMethod is a bound method (PyMethod), we need to call the underlying function
                            // directly with all args including cls, because _PyObject_Call_Prepend always adds cls
                            if (newMethod is PyMethod boundMethod)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"  ⚠️ newMethod is PyMethod (bound), calling underlying function directly");
                                #endif
                                // Get the underlying function and call it with all 4 args
                                var underlyingFunc = boundMethod.Function;
                                result = underlyingFunc.Call(newArgs, newKwargs);
                            }
                            else
                            {
                                // For unbound functions or staticmethods, call normally
                                result = newMethod.Call(newArgs, newKwargs);
                            }
                            #if DEBUG_LOG
                            Console.WriteLine($"🚀 metaclass.__new__ returned: {result?.GetType().Name ?? "null"}");
                            #endif
                        }
                        catch (Exception ex)
                        {
                            // Re-throw exception - no cleanup needed
                            throw;
                        }
                        #if DEBUG_LOG
                        Console.WriteLine($"Metaclass.__new__ returned: {result?.GetType().Name}");
                        #endif
                        
                        if (result is PyClass createdClass)
                        {
                            pyClass = createdClass;
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 Setting Metaclass: {metaclass?.GetType().Name}, is PyClass: {metaclass is PyClass}");
                            #endif
                            pyClass.Metaclass = metaclass as PyClass;
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 After setting: pyClass.Metaclass = {pyClass.Metaclass}");
                            #endif

                            // CPython 3.12 Python/bltinmodule.c:208-209:
                            //   PyObject *margs[3] = {name, bases, ns};
                            //   cls = PyObject_VectorcallDict(meta, margs, 3, mkw);
                            //
                            // The metaclass.__new__ receives the SAME ns dict that was used for class body execution.
                            // type.__new__ (or custom metaclass.__new__) is responsible for creating the class
                            // and setting all attributes from ns.
                            //
                            // IMPORTANT: CPython does NOT iterate through ns again after metaclass.__new__ returns.
                            // The returned class already has all attributes set by metaclass.__new__.
                            //
                            // Previous code here was redundantly calling SetAttribute for each item in ns,
                            // which caused attributes to be set twice. This is incorrect and causes bugs
                            // when __prepare__ returns a custom dict that pre-populates items (like _EnumDict).
                            //
                            // Reference: Python/bltinmodule.c:201-209 - no additional attribute setting after line 209
                            #if DEBUG_LOG
                            Console.WriteLine($"✅ Metaclass.__new__ created class with all attributes from namespace");
                            #endif
                            
                            // CPython 3.12: NOW update __classcell__ to point to the created class
                            if (classcell != null)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"🎯 CPython 3.12: Updating __classcell__ to point to created class");
                                #endif
                                #if DEBUG_LOG
                                Console.WriteLine($"   Before: __classcell__.Value = {classcell.Value}");
                                #endif
                                classcell.Value = pyClass;  // Set to the newly created class
                                #if DEBUG_LOG
                                Console.WriteLine($"   After: __classcell__.Value = {classcell.Value}");
                                #endif
                                #if DEBUG_LOG
                                Console.WriteLine($"✅ __classcell__ correctly updated to created class");
                                #endif
                            }
                            
                            #if DEBUG_LOG
                            Console.WriteLine($"✅ Metaclass created class successfully: {createdClass}");
                            #endif
                            
                            // CPython 3.12: Call metaclass.__init__ after __new__
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 Calling metaclass.__init__: metaclass={metaclass}, type={metaclass?.GetType().Name}");
                            #endif
                            try
                            {
                                var initMethod = metaclass.GetAttribute("__init__");
                                if (initMethod != null && initMethod.IsCallable())
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine("Found metaclass.__init__ method, executing it");
                                    #endif
                                try
                                {
                                    // Performance: Eliminated LINQ - manual conversion instead of Cast().ToArray()
                                    var initBasesArray = new PyObject[bases.Count];
                                    for (int i = 0; i < bases.Count; i++)
                                    {
                                        initBasesArray[i] = bases[i];
                                    }
                                    var initArgs = new PyObject[] {
                                        pyClass,                    // cls (the created class)
                                        new PyStr(className),    // name
                                        new PyTuple(initBasesArray),// bases
                                        namespaceObj                // namespace (dict or dict-like object)
                                    };
                                    initMethod.Call(initArgs, null);
                                    #if DEBUG_LOG
                                    Console.WriteLine("Metaclass.__init__ executed successfully");
                                    #endif
                                }
                                catch (Exception initEx)
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"Error calling metaclass.__init__: {initEx.Message}");
                                    #endif
                                }
                                }
                            }
                            catch (Exception ex) when (ex.Message.Contains("has no attribute"))
                            {
                                #if DEBUG_LOG
                                Console.WriteLine("No __init__ method found on metaclass, skipping initialization");
                                #endif
                            }
                        }
                        else
                        {
                            // If metaclass.__new__ didn't return a class, fall back to direct type.__new__ call
                            #if DEBUG_LOG
                            Console.WriteLine("Metaclass.__new__ didn't return a class, falling back to type.__new__");
                            #endif
                            // Performance: Eliminated LINQ - manual conversion instead of Cast().ToArray()
                            var fallbackBasesArray = new PyObject[bases.Count];
                            for (int i = 0; i < bases.Count; i++)
                            {
                                fallbackBasesArray[i] = bases[i];
                            }
                            var typeResult = CallTypeNew(new PyObject[] { metaclass, new PyStr(className), new PyTuple(fallbackBasesArray), namespaceObj });
                            // If type.__new__ didn't return a PyClass, create one with module info
                            if (typeResult is PyClass existingClass)
                            {
                                pyClass = existingClass;
                            }
                            else
                            {
                                string moduleInfo = null;
                                if (classNamespace.TryGetValue("__module__", out var moduleObj) && moduleObj is PyStr moduleStr)
                                {
                                    moduleInfo = moduleStr.Value;
                                }
                                // Performance: Eliminated LINQ - manual conversion instead of ToArray()
                                var fallbackBasesArr = new PyType[bases.Count];
                                for (int i = 0; i < bases.Count; i++)
                                {
                                    fallbackBasesArr[i] = bases[i];
                                }
                                pyClass = new PyClass(className, fallbackBasesArr, classNamespace, null, moduleInfo);
                            }
                            pyClass.Metaclass = metaclass as PyClass;
                            
                            // CPython 3.12: Update __classcell__ for fallback case too
                            if (classcell != null)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"🎯 CPython 3.12: Updating __classcell__ in fallback case");
                                #endif
                                classcell.Value = pyClass;
                                #if DEBUG_LOG
                                Console.WriteLine($"✅ __classcell__ updated in fallback case");
                                #endif
                            }
                        }
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine("No callable __new__ method found on metaclass, using type.__new__ directly");
                        #endif
                        // Performance: Eliminated LINQ - manual conversion instead of Cast().ToArray()
                        var noCallableBasesArray = new PyObject[bases.Count];
                        for (int i = 0; i < bases.Count; i++)
                        {
                            noCallableBasesArray[i] = bases[i];
                        }
                        var typeResult = CallTypeNew(new PyObject[] { metaclass, new PyStr(className), new PyTuple(noCallableBasesArray), namespaceObj });
                        // If type.__new__ didn't return a PyClass, create one with module info
                        if (typeResult is PyClass existingClass)
                        {
                            pyClass = existingClass;
                        }
                        else
                        {
                            string moduleInfo = null;
                            if (classNamespace.TryGetValue("__module__", out var moduleObj) && moduleObj is PyStr moduleStr)
                            {
                                moduleInfo = moduleStr.Value;
                            }
                            // Performance: Eliminated LINQ - manual conversion instead of ToArray()
                            var noCallableBasesArr = new PyType[bases.Count];
                            for (int i = 0; i < bases.Count; i++)
                            {
                                noCallableBasesArr[i] = bases[i];
                            }
                            pyClass = new PyClass(className, noCallableBasesArr, classNamespace, null, moduleInfo);
                        }
                        pyClass.Metaclass = metaclass as PyClass;
                        
                        // CPython 3.12: Update __classcell__ for no callable __new__ case
                        if (classcell != null)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🎯 CPython 3.12: Updating __classcell__ in no callable __new__ case");
                            #endif
                            classcell.Value = pyClass;
                            #if DEBUG_LOG
                            Console.WriteLine($"✅ __classcell__ updated in no callable __new__ case");
                            #endif
                        }
                    }
                }
                catch (Exception ex)
                {
                    // CPython 3.12: __build_class__ does NOT catch exceptions from metaclass call
                    // Exceptions from metaclass.__new__ should propagate to the caller
                    #if DEBUG_LOG
                    Console.WriteLine($"❌ ERROR calling metaclass.__new__ for {className}: {ex.Message}");
                    Console.WriteLine($"   Exception type: {ex.GetType().Name}");
                    Console.WriteLine($"   CPython 3.12: Re-throwing exception to propagate to caller");
                    #endif

                    // Re-throw to propagate to Python level (for try/except handling)
                    throw;
                }
            }
            else
            {
                // Extract __module__ from class namespace
                string moduleInfo = null;
                if (classNamespace.TryGetValue("__module__", out var moduleObj) && moduleObj is PyStr moduleStr)
                {
                    moduleInfo = moduleStr.Value;
                }

                // Performance: Eliminated LINQ - manual conversion instead of ToArray()
                var basesArray = new PyType[bases.Count];
                for (int i = 0; i < bases.Count; i++)
                {
                    basesArray[i] = bases[i];
                }
                pyClass = new PyClass(className, basesArray, classNamespace, null, moduleInfo);

                // CPython 3.12: Even without explicit metaclass, all classes have 'type' as metaclass
                pyClass.Metaclass = PyTypeMetaclass.Instance;

                // CPython 3.12: Update __classcell__ for non-metaclass case too
                if (classcell != null)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"🎯 CPython 3.12: Updating __classcell__ in non-metaclass case");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"   Before: __classcell__.Value = {classcell.Value}");
                    #endif
                    classcell.Value = pyClass;
                    #if DEBUG_LOG
                    Console.WriteLine($"   After: __classcell__.Value = {classcell.Value}");
                    #endif
                    #if DEBUG_LOG
                    Console.WriteLine($"✅ __classcell__ updated in non-metaclass case");
                    #endif
                }
            }
            
            // CPython 3.12: Copy namespace attributes to class
            // For metaclass: use the potentially modified namespace dict
            // For non-metaclass: use the original classNamespace
            if (hasMetaclass)
            {
                // Extract attributes from the namespace dict that was passed to metaclass.__new__
                // This dict may have been modified by the metaclass
                var namespaceDict = new PyDict();
                foreach (var kvp in classNamespace)
                {
                    namespaceDict.SetItem(new PyStr(kvp.Key), kvp.Value);
                }
                
                // After metaclass execution, the namespaceDict should contain any additions
                // But we need to get the updated dict from the metaclass result
                // For now, let's try to get the attributes from the created class itself
                #if DEBUG_LOG
                Console.WriteLine($"Setting attributes for metaclass-created class");
                #endif

                // CPython 3.12: Python/bltinmodule.c:201-209
                // After metaclass.__new__ returns, CPython does NOT iterate through namespace again!
                // Line 208-209: cls = PyObject_VectorcallDict(meta, margs, 3, mkw);
                // The metaclass.__new__ (or type.__new__) is responsible for setting all attributes.
                //
                // IMPORTANT: DO NOT call SetAttribute here!
                // This causes duplicate attribute setting and breaks enum.py behavior:
                // - EnumType.__new__ processes _generate_next_value_ (unwraps staticmethod to function)
                // - If we call SetAttribute again here, we overwrite the staticmethod with unwrapped function
                // - This breaks StrEnum._generate_next_value_ which should remain as staticmethod
                //
                // Reference: Objects/typeobject.c:3770 - set_tp_dict(type, dict)
                // CPython sets the dict directly to type->tp_dict, preserving all values as-is
                #if DEBUG_LOG
                Console.WriteLine($"✅ Metaclass.__new__ already set all attributes - skipping duplicate SetAttribute calls");
                #endif
            }
            else
            {
                // CPython 3.12: Same logic applies for non-metaclass case
                // The namespace dict was already passed to PyClass constructor
                // Reference: core/PyType.cs:579-589 - stringDict is copied to ClassDict in PyClass constructor
                #if DEBUG_LOG
                Console.WriteLine($"✅ PyClass constructor already set all attributes from namespace");
                #endif
            }

            // PEP 560 & PEP 487: Special-case __class_getitem__ and __init_subclass__
            // CPython 3.12: Objects/typeobject.c:3692-3699
            // If they are plain functions, make them classmethods
            // This MUST happen AFTER namespace attributes are set
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PEP 560/487: Converting __class_getitem__ and __init_subclass__ to classmethods");
            #endif
            PyTypeMetaclass.ConvertToClassmethod(pyClass, "__init_subclass__");
            PyTypeMetaclass.ConvertToClassmethod(pyClass, "__class_getitem__");

            // CPython 3.12: Update __classcell__ with created class
            if (classcell != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"🎯 CPython 3.12: Updating __classcell__ with created class {pyClass}");
                #endif
                classcell.Value = pyClass;
                #if DEBUG_LOG
                Console.WriteLine($"✅ __classcell__ updated successfully");
                #endif
            }
            
            
            // Debug: Check final class attributes
            #if DEBUG_LOG
            Console.WriteLine($"🔍 Final class attributes: {pyClass?.Name}");
            #endif
            #if DEBUG_LOG
            Console.WriteLine($"   Metaclass: {pyClass?.Metaclass}");
            #endif
            if (pyClass?.ClassDict != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ClassDict has {pyClass.ClassDict.Count} items:");
                #endif
                foreach (var attr in pyClass.ClassDict)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"     {attr.Key}: {attr.Value?.GetType().Name}");
                    #endif
                }
            }

            // CPython 3.12: Call __set_name__ on all class attributes (descriptors)
            // Reference: Objects/typeobject.c:3485-3517 (type_new_set_names)
            CallSetNameOnClassAttributes(pyClass);

            return pyClass;
        }

        /// <summary>
        /// CPython 3.12: Call __set_name__ on all class attributes
        /// Reference: Objects/typeobject.c:3485-3517 (type_new_set_names)
        /// </summary>
        private static void CallSetNameOnClassAttributes(PyClass pyClass)
        {
            // Iterate through all class attributes
            foreach (var kvp in pyClass.ClassDict)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                // Skip None values
                if (value == null || value == PyNone.Instance)
                    continue;

                // Look for __set_name__ method on the attribute
                // CPython: _PyObject_LookupSpecial(value, &_Py_ID(__set_name__))
                try
                {
                    var setNameMethod = value.LookupAttribute("__set_name__");
                    if (setNameMethod != null && setNameMethod != PyNone.Instance)
                    {
                        // Call __set_name__(owner, name)
                        // CPython 3.12: PyObject_CallFunctionObjArgs(set_name, type, key, NULL)
                        #if DEBUG_LOG
                        Console.WriteLine($"🔧 Calling __set_name__ for attribute '{key}' on {value.GetTypeName()}");
                        #endif
                        setNameMethod.Call(new PyObject[] { pyClass, new PyStr(key) }, null);
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyAttributeError)
                {
                    // __set_name__ not found - this is normal, not all attributes have it
                }
            }
        }

        /// <summary>
        /// __import__(name, globals=None, locals=None, fromlist=(), level=0)
        /// CPython 3.12: Python/bltinmodule.c:246-278
        /// </summary>
        private static PyObject CallImport(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: __import__(name, globals=None, locals=None, fromlist=(), level=0)
            // Signature has 5 parameters: name (required), globals, locals, fromlist, level

            // Extract parameters from positional args and kwargs
            string name = null;
            Dictionary<string, PyObject> globals = null;
            // locals is unused in CPython (kept for backwards compatibility)
            string[] fromlist = null;
            int level = 0;

            // Process positional arguments
            if (args.Length >= 1)
            {
                name = args[0].AsString();
            }
            if (args.Length >= 2 && args[1] != null && args[1] is not PyNone)
            {
                // globals parameter - convert to Dictionary for PyImportSystem
                if (args[1] is PyDict globalsDict)
                {
                    globals = new Dictionary<string, PyObject>();
                    foreach (var kvp in globalsDict.InternalDict)
                    {
                        if (kvp.Key is PyStr keyStr)
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
                    if (kvp.Key is PyStr keyStr)
                    {
                        switch (keyStr.Value)
                        {
                            case "name":
                                name = kvp.Value.AsString();
                                break;
                            case "globals":
                                if (kvp.Value is PyDict gDict && kvp.Value is not PyNone)
                                {
                                    globals = new Dictionary<string, PyObject>();
                                    foreach (var g in gDict.InternalDict)
                                    {
                                        if (g.Key is PyStr gKeyStr)
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
                    result[i] = tuple.Items[i] is PyStr s ? s.Value : tuple.Items[i].AsString();
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
                    result[i] = items[i] is PyStr s ? s.Value : items[i].AsString();
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

        private static PyObject CallOpen(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: open(file, mode='r', buffering=-1, encoding=None, errors=None, newline=None, closefd=True, opener=None)
            if (args.Length < 1)
                throw PyTypeError.Create("open() missing required argument: 'file'");

            // Extract file argument
            var filename = args[0].ToStr().Value;
            var mode = "r";
            var buffering = -1;
            string encoding = null;
            string errors = null;
            string newline = null;
            var closefd = true;
            PyObject opener = null;

            // Process positional arguments
            if (args.Length > 1) mode = args[1].ToStr().Value;
            if (args.Length > 2) buffering = (int)((PyInt)args[2]).Value;
            if (args.Length > 3) encoding = args[3] != PyNone.Instance ? args[3].ToStr().Value : null;
            if (args.Length > 4) errors = args[4] != PyNone.Instance ? args[4].ToStr().Value : null;
            if (args.Length > 5) newline = args[5] != PyNone.Instance ? args[5].ToStr().Value : null;
            if (args.Length > 6) closefd = args[6].PyBoolValue();
            if (args.Length > 7) opener = args[7] != PyNone.Instance ? args[7] : null;

            // Process kwargs
            if (kwargs != null)
            {
                try
                {
                    var modeValue = kwargs.GetItem(new PyStr("mode"));
                    mode = modeValue.ToStr().Value;
                }
                catch { }

                try
                {
                    var bufferingValue = kwargs.GetItem(new PyStr("buffering"));
                    buffering = (int)((PyInt)bufferingValue).Value;
                }
                catch { }

                try
                {
                    var encodingValue = kwargs.GetItem(new PyStr("encoding"));
                    encoding = encodingValue != PyNone.Instance ? encodingValue.ToStr().Value : null;
                }
                catch { }

                try
                {
                    var errorsValue = kwargs.GetItem(new PyStr("errors"));
                    errors = errorsValue != PyNone.Instance ? errorsValue.ToStr().Value : null;
                }
                catch { }

                try
                {
                    var newlineValue = kwargs.GetItem(new PyStr("newline"));
                    newline = newlineValue != PyNone.Instance ? newlineValue.ToStr().Value : null;
                }
                catch { }

                try
                {
                    var closefdValue = kwargs.GetItem(new PyStr("closefd"));
                    closefd = closefdValue.PyBoolValue();
                }
                catch { }

                try
                {
                    var openerValue = kwargs.GetItem(new PyStr("opener"));
                    opener = openerValue != PyNone.Instance ? openerValue : null;
                }
                catch { }

                // 예상치 못한 키워드 인수 체크
                var validKeys = new[] { "mode", "buffering", "encoding", "errors", "newline", "closefd", "opener" };
                foreach (var kvp in kwargs.InternalDict)
                {
                    if (kvp.Key is PyStr keyStr && !validKeys.Contains(keyStr.Value))
                    {
                        throw PyTypeError.Create($"'{keyStr.Value}' is an invalid keyword argument for open()");
                    }
                }
            }

            // encoding 기본값 설정
            if (encoding == null && mode.Contains("t"))
            {
                encoding = "utf-8"; // 기본 텍스트 인코딩
            }

            // Create file context manager
            return new PyFileContextManager(filename, mode);
        }

        /// <summary>
        /// super() builtin function implementation
        /// Returns a proxy object that delegates method calls to parent or sibling class
        /// </summary>
        private static PyObject CallSuper(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
            {
                // super() with no arguments - try to use __class__ cell variable
                // This is a simplified implementation - in CPython, this requires frame introspection
                #if DEBUG_LOG
                Console.WriteLine("🔍 super() called with no arguments, attempting __class__ cell lookup");
                #endif
                
                // CPython 3.12: Look for __class__ cell variable in current frame
                var currentFrame = PyVM.CurrentFrame;
                if (currentFrame != null)
                {
                    // CPython 3.12: __class__ can be in FreeVars (from parent) or CellVars (local)
                    // Optimized: Use cached ClassCellIndex instead of IndexOf calls
                    int classIndex = currentFrame.Code.ClassCellIndex;
                    #if DEBUG_LOG
                    if (classIndex >= 0)
                        Console.WriteLine($"🔍 Found __class__ at cached ClassCellIndex={classIndex}");
                    #endif

                    if (classIndex >= 0)
                    {
                        // Try to get the __class__ value from cell variables
                        if (currentFrame.Cells != null && classIndex < currentFrame.Cells.Length)
                        {
                            var classCell = currentFrame.Cells[classIndex];
                            if (classCell != null && classCell.Value != null)
                            {
                                var classValue = classCell.Value;
                                #if DEBUG_LOG
                                Console.WriteLine($"🔍 Retrieved __class__ from cell[{classIndex}]: {classValue}");
                                #endif
                                
                                // CPython 3.12: __class__ cell value is already correct
                                // Each metaclass method sees its own class in __class__
                                
                                // CPython 3.12: zero-argument super() needs __class__ and first parameter
                                // CPython uses LOAD_FAST 0 - always the first parameter, regardless of name
                                // (could be 'self', 'cls', 'metacls', or any other name)
                                PyObject instance = null;
                                if (currentFrame.LocalsPlus.Length > 0)
                                {
                                    // CPython bytecode: LOAD_FAST 0 - get first parameter by index
                                    var firstValue = currentFrame.LocalsPlus[0];
                                    if (!firstValue.IsNull)
                                    {
                                        instance = firstValue.ToObject();
                                        #if DEBUG_LOG
                                        var firstParam = currentFrame.Code.VarNames[0];
                                        Console.WriteLine($"🔍 Found first parameter '{firstParam}': {instance}");
                                        #endif
                                    }
                                }

                                // CPython 3.12: super() with no arguments requires both __class__ and self/cls
                                if (instance == null)
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔍 No self/cls parameter found in current frame");
                                    #endif
                                    throw PyRuntimeError.Create("super(): no arguments");
                                }

                                if (classValue is PyType pyType)
                                {
                                    // Create a proper super proxy object for PyType
                                    return new PySuper(pyType, instance);
                                }
                                else if (classValue is PyClass pyClass)
                                {
                                    // Create a proper super proxy object for PyClass
                                    // PyClass inherits from PyType, so we can use it directly
                                    return new PySuper(pyClass, instance);
                                }
                                else
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔍 __class__ cell contains non-type value: {classValue.GetType().Name}");
                                    #endif
                                }
                            }
                        }
                        
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 __class__ cell found but not initialized (cell[{classIndex}])");
                        #endif
                    }
                    else
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 No __class__ found in current frame");
                        #endif
                        #if DEBUG_LOG
                        // Performance: Eliminated LINQ - direct string.Join works with collections
                        Console.WriteLine($"   FreeVars: [{string.Join(", ", currentFrame.Code.FreeVars)}]");
                        #endif
                        #if DEBUG_LOG
                        Console.WriteLine($"   CellVars: [{string.Join(", ", currentFrame.Code.CellVars)}]");
                        #endif
                    }
                }
                
                throw PyTypeError.Create("super(): __class__ cell not found - zero-argument super() requires compile-time __class__ cell generation");
            }
            else if (args.Length == 2)
            {
                // super(type, obj) - standard form
                var type = args[0];
                var obj = args[1];
                
                if (!(type is PyType pyType))
                {
                    throw PyTypeError.Create("super() argument 1 must be type");
                }
                
                // Create a super proxy object
                return new PySuper(pyType, obj);
            }
            else
            {
                throw PyTypeError.Create($"super expected at most 2 arguments ({args.Length} given)");
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

            PyObject? fget = args.Length > 0 ? args[0] : null;
            PyObject? fset = args.Length > 1 ? args[1] : null;
            PyObject? fdel = args.Length > 2 ? args[2] : null;
            PyObject? doc = args.Length > 3 ? args[3] : null;

            // Allow None for any argument
            if (fget == PyNone.Instance) fget = null;
            if (fset == PyNone.Instance) fset = null;
            if (fdel == PyNone.Instance) fdel = null;
            if (doc == PyNone.Instance) doc = null;

            return new PyProperty(fget, fset, fdel, doc);
        }

        // CPython 3.12: Objects/funcobject.c:1055-1068 (cm_init)
        private static PyObject CallClassmethod(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
            {
                throw PyTypeError.Create($"classmethod expected 1 argument ({args.Length} given)");
            }

            var func = args[0];

            // CPython 3.12: If already a classmethod, return as-is (idempotent)
            if (func is PyClassmethod pyClassmethod)
            {
                return pyClassmethod;
            }

            if (func == null || !func.IsCallable())
            {
                throw PyTypeError.Create($"classmethod() argument must be callable (got {func?.GetTypeName()})");
            }

            // CPython 3.12: Objects/funcobject.c:1062 - Accept ANY callable
            // cm_callable can be PyFunction, PyBuiltinFunction, PyMethodDescriptor, etc.
            return new PyClassmethod(func);
        }

        private static PyObject CallStaticmethod(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
            {
                throw PyTypeError.Create($"staticmethod expected 1 argument ({args.Length} given)");
            }

            var func = args[0];
            #if DEBUG_LOG
            Console.WriteLine($"[STATICMETHOD] Received: {func?.GetType().Name} / IsCallable={func?.IsCallable()} / value={func}");
            #endif

            // CPython 3.12: Objects/funcobject.c:1241-1256 (sm_init)
            // If already a staticmethod, return as-is (idempotent)
            if (func is PyStaticmethod pyStaticmethod)
            {
                #if DEBUG_LOG
                Console.WriteLine($"[STATICMETHOD] Already a staticmethod, returning as-is");
                #endif
                return pyStaticmethod;
            }

            if (func == null || !func.IsCallable())
            {
                throw PyTypeError.Create($"staticmethod() argument must be callable (got {func?.GetTypeName()})");
            }

            // CPython 3.12: Objects/funcobject.c:1250 - Accept ANY callable
            // sm_callable can be PyFunction, PyBuiltinFunction, PyMethodDescriptor, etc.
            return new PyStaticmethod(func);
        }

        /// <summary>
        /// type.__new__ builtin method implementation
        /// Creates a new type instance (class creation)
        /// </summary>
        private static PyObject CallTypeNew(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: Objects/typeobject.c:1627-1689 (type_call)
            // This function implements type.__new__() which is called when creating a class
            // It receives: (metaclass, name, bases, dict) as args and potential kwargs
            // If the metaclass is not 'type', we need to call its __new__ method with kwargs

            if (args.Length < 4)
            {
                throw PyTypeError.Create($"type.__new__() takes exactly 4 arguments ({args.Length} given)");
            }

            var cls = args[0];        // The metaclass (e.g., MyMeta, EnumType)
            var name = args[1];       // Class name (e.g., "MyClass")
            var bases = args[2];      // Base classes tuple (e.g., ())
            var attrs = args[3];      // Class attributes dict (e.g., {"method": <function>})

            #if DEBUG_LOG
            Console.WriteLine($"\n🔍 CallTypeNew called for class: {(name as PyStr)?.Value}");
            Console.WriteLine($"  metaclass: {cls?.GetTypeName()}");
            Console.WriteLine($"  attrs type: {attrs?.GetType().Name}, PyType: {attrs?.GetTypeName()}");
            if (kwargs != null && kwargs.InternalDict.Count > 0)
            {
                Console.WriteLine($"  kwargs: {string.Join(", ", kwargs.InternalDict.Keys.Select(k => (k as PyStr)?.Value))}");
            }
            #endif

            // Convert arguments to proper types
            if (!(name is PyStr nameStr))
            {
                throw PyTypeError.Create("type.__new__() argument 2 must be string");
            }

            if (!(bases is PyTuple basesTuple))
            {
                throw PyTypeError.Create("type.__new__() argument 3 must be tuple");
            }

            // CPython 3.12: Objects/typeobject.c:1667
            // If cls is a custom metaclass (not type), call its __new__ method with kwargs
            if (cls is PyType metaclassType && metaclassType != PyType.TypeType)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  🔧 Custom metaclass detected: {metaclassType.Name}, calling its __new__ with kwargs");
                #endif

                // Get the metaclass's __new__ method
                var newMethod = metaclassType.GetAttribute("__new__");
                if (newMethod != null && newMethod.IsCallable())
                {
                    // Call metaclass.__new__(metaclass, name, bases, dict, **kwargs)
                    // args[0] is already the metaclass, so we pass all args
                    var result = newMethod.Call(args, kwargs);
                    return result;
                }

                // Fallback: if __new__ is not found, use the default implementation below
                #if DEBUG_LOG
                Console.WriteLine($"  ⚠️ Metaclass {metaclassType.Name} has no __new__, using default");
                #endif
            }
            
            // CPython 3.12: Accept both PyDict and dict-like objects (e.g., _EnumDict which is PyClassInstance)
            PyDict attrsDict = null;
            PyList attrItems = null;

            if (attrs is PyDict dict)
            {
                attrsDict = dict;
                attrItems = attrsDict.Items();
            }
            else if (attrs is PyClassInstance instance && instance.IsDictSubclass())
            {
                // For dict subclasses like _EnumDict, get items from the dict-like object
                // Try to call items() method on the dict-like object
                try
                {
                    var itemsMethod = instance.GetAttribute("items");
                    if (itemsMethod != null && itemsMethod.IsCallable())
                    {
                        var itemsResult = itemsMethod.Call(new PyObject[0], null);
                        if (itemsResult is PyList list)
                        {
                            attrItems = list;
                        }
                        else
                        {
                            // items() might return an iterator or other iterable
                            var itemsList = new List<PyObject>();
                            var iter = itemsResult.GetIterator();
                            PyObject item;
                            while ((item = iter.Next()) != null)
                            {
                                itemsList.Add(item);
                            }
                            attrItems = new PyList(itemsList);
                        }
                    }
                    else
                    {
                        // Fallback: get from internal dict storage
                        attrsDict = instance.GetDictStorage();
                        if (attrsDict != null)
                        {
                            attrItems = attrsDict.Items();
                        }
                    }
                }
                catch
                {
                    // If items() fails, try to get from internal storage
                    attrsDict = instance.GetDictStorage();
                    if (attrsDict != null)
                    {
                        attrItems = attrsDict.Items();
                    }
                }
            }
            else
            {
                throw PyTypeError.Create("type.__new__() argument 4 must be dict or dict subclass");
            }

            // Create the new class using the standard class creation mechanism
            var baseTypes = new List<PyType>();
            foreach (var baseObj in basesTuple.Items)
            {
                if (baseObj is PyType baseType)
                {
                    baseTypes.Add(baseType);
                }
                else
                {
                    throw PyTypeError.Create("bases must be types");
                }
            }

            // Create new class
            // Performance: Eliminated LINQ - manual conversion instead of ToArray()
            var baseTypesArray = new PyType[baseTypes.Count];
            for (int i = 0; i < baseTypes.Count; i++)
            {
                baseTypesArray[i] = baseTypes[i];
            }
            var newClass = new PyClass(nameStr.Value, baseTypesArray);

            // Set class attributes from the attrs dict or dict-like object
            var items = attrItems;
            for (int i = 0; i < items.Items.Length; i++)
            {
                if (items.Items[i] is PyTuple kvp && kvp.Items.Length == 2)
                {
                    if (kvp.Items[0] is PyStr keyStr)
                    {
                        #if DEBUG_LOG
                        if (keyStr.Value == "func" || keyStr.Value == "_generate_next_value_")
                        {
                            Console.WriteLine($"  🔧 Setting attribute: {keyStr.Value} = {kvp.Items[1]}, type={kvp.Items[1]?.GetTypeName()}");
                        }
                        #endif
                        newClass.SetAttribute(keyStr.Value, kvp.Items[1]);
                    }
                }
            }

            return newClass;
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
            return new PyGlobalsDict(globalScope.Variables);
        }

        /// <summary>
        /// locals() builtin function - returns a dictionary of the current local symbol table
        /// </summary>
        private static PyObject CallLocals(PyObject[] args, PyDict kwargs = null)
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
                result.SetItem(new PyStr(variable.Key), variable.Value);
            }

            return result;
        }
        

        #region Buffer Protocol Functions

        /// <summary>
        /// bytes() constructor - creates immutable byte sequences
        /// </summary>
        private static PyObject CallBytes(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
            {
                // bytes() with no arguments creates empty bytes
                return new PyBytesObject(new byte[0]);
            }
            else if (args.Length == 1)
            {
                var arg = args[0];

                // bytes(string, encoding) - convert string to bytes
                if (arg is PyStr str)
                {
                    // Default encoding is utf-8
                    var bytes = System.Text.Encoding.UTF8.GetBytes(str.Value);
                    return new PyBytesObject(bytes);
                }
                // bytes(iterable) - create from iterable of integers
                else if (arg is PyList list)
                {
                    var byteList = new List<byte>();
                    foreach (var item in list.Items)
                    {
                        if (item is PyInt pyInt)
                        {
                            if (pyInt.Value < 0 || pyInt.Value > 255)
                                throw PyValueError.Create("byte must be in range(0, 256)");
                            byteList.Add((byte)pyInt.Value);
                        }
                        else
                        {
                            throw PyTypeError.Create("an integer is required");
                        }
                    }
                    // Performance: Eliminated LINQ - ToArray() is a List method, not LINQ
                    return new PyBytes(byteList.ToArray());
                }
                // bytes(range) - create from range object
                else if (arg is PyRange range)
                {
                    var byteList = new List<byte>();
                    var items = range.GetValues();
                    foreach (var item in items)
                    {
                        if (item is PyInt pyInt)
                        {
                            if (pyInt.Value < 0 || pyInt.Value > 255)
                                throw PyValueError.Create("byte must be in range(0, 256)");
                            byteList.Add((byte)pyInt.Value);
                        }
                    }
                    // Performance: Eliminated LINQ - ToArray() is a List method, not LINQ
                    return new PyBytes(byteList.ToArray());
                }
                // CPython 3.12: Objects/bytesobject.c:2571-2585 - bytes_new
                // bytes(int) - create bytes of specified size filled with zeros
                else if (arg is PyInt size)
                {
                    if (size.Value < 0)
                        throw PyValueError.Create("negative count");
                    return new PyBytes(new byte[(int)size.Value]);
                }
                else
                {
                    throw PyTypeError.Create($"cannot convert '{arg.GetTypeName()}' object to bytes");
                }
            }
            else if (args.Length == 2)
            {
                // bytes(string, encoding)
                if (args[0] is PyStr str && args[1] is PyStr encoding)
                {
                    var enc = encoding.Value.ToLowerInvariant() switch
                    {
                        "utf-8" or "utf8" => System.Text.Encoding.UTF8,
                        "ascii" => System.Text.Encoding.ASCII,
                        "unicode" or "utf-16" => System.Text.Encoding.Unicode,
                        _ => throw PyLookupError.Create($"unknown encoding: {encoding.Value}")
                    };
                    var bytes = enc.GetBytes(str.Value);
                    return new PyBytes(bytes);
                }
                else
                {
                    throw PyTypeError.Create("bytes() argument 2 must be a string");
                }
            }
            else
            {
                throw PyTypeError.Create($"bytes() takes at most 2 arguments ({args.Length} given)");
            }
        }

        /// <summary>
        /// bytearray() constructor - creates mutable byte sequences
        /// </summary>
        private static PyObject CallBytearray(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
            {
                return new PyBytearrayObject();
            }
            else if (args.Length == 1)
            {
                var arg = args[0];
                if (arg is PyStr str)
                {
                    return new PyBytearrayObject(str.Value);
                }
                else if (arg is PyBytesObject bytesObj)
                {
                    return new PyBytearrayObject(bytesObj.Data);
                }
                else if (arg is PyBytearrayObject bytearrayObj)
                {
                    return new PyBytearrayObject(bytearrayObj.Data);
                }
                // CPython 3.12: Objects/bytearrayobject.c:773-788 - bytearray_init
                else if (arg is PyInt size)
                {
                    if (size.Value < 0)
                        throw PyValueError.Create("negative count");
                    return new PyBytearrayObject(new byte[(int)size.Value]);
                }
                else if (arg is PyList list)
                {
                    var bytes = new List<byte>();
                    foreach (var item in list.Items)
                    {
                        if (item is PyInt itemInt)
                        {
                            if (itemInt.Value < 0 || itemInt.Value > 255)
                                throw PyValueError.Create("byte must be in range(0, 256)");
                            bytes.Add((byte)itemInt.Value);
                        }
                        else
                        {
                            throw PyTypeError.Create($"'{item.GetTypeName()}' object cannot be interpreted as an integer");
                        }
                    }
                    // Performance: Eliminated LINQ - ToArray() is a List method, not LINQ
                    return new PyBytes(bytes.ToArray());
                }
            }
            throw PyTypeError.Create($"bytearray() argument must be bytes-like, not '{args[0]?.GetTypeName() ?? "None"}'");
        }

        /// <summary>
        /// memoryview() constructor - creates memory view objects
        /// </summary>
        private static PyObject CallMemoryview(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"memoryview() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // Check if object supports buffer protocol
            if (obj is PyBytesObject bytes)
            {
                return new PyMemoryViewObject(bytes);
            }
            else if (obj is PyBytearrayObject bytearray)
            {
                return new PyMemoryViewObject(bytearray);
            }
            else
            {
                throw PyTypeError.Create($"a bytes-like object is required, not '{obj.GetTypeName()}'");
            }
        }

        /// <summary>
        /// repr() built-in function - returns a printable representation of an object
        /// </summary>
        private static PyObject CallRepr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"repr() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // Use the object's ToRepr() method, which already returns a PyStr
            // Don't wrap it again, as that would add extra quotes
            return obj.ToRepr();
        }

        /// <summary>
        /// hex(x) - CPython 3.12: Python/bltinmodule.c:1439 (builtin_hex)
        /// Convert an integer number to a lowercase hexadecimal string prefixed with "0x"
        /// </summary>
        private static PyObject CallHex(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"hex() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // CPython 3.12: Python/bltinmodule.c:1803-1825 - builtin_hex
            // CPython: PyNumber_Index() is called first
            PyInt intValue;
            if (obj is PyInt pyInt)
            {
                intValue = pyInt;
            }
            else if (obj is PyBool pyBool)
            {
                intValue = new PyInt(pyBool.IsTrue() ? 1 : 0);
            }
            else
            {
                // Try __index__ method
                try
                {
                    var indexMethod = obj.GetAttribute("__index__");
                    var result = indexMethod.Call(Array.Empty<PyObject>(), null);
                    if (result is PyInt indexResult)
                    {
                        intValue = indexResult;
                    }
                    else
                    {
                        throw PyTypeError.Create($"__index__ returned non-int (type {result.GetTypeName()})");
                    }
                }
                catch
                {
                    throw PyTypeError.Create($"'{obj.GetTypeName()}' object cannot be interpreted as an integer");
                }
            }

            // Delegate to PyInt.Hex() for proper BigInteger formatting
            return intValue.Hex();
        }

        /// <summary>
        /// oct(x) - CPython 3.12: Python/bltinmodule.c:1855 (builtin_oct)
        /// Convert an integer number to an octal string prefixed with "0o"
        /// </summary>
        private static PyObject CallOct(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"oct() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // CPython 3.12: Python/bltinmodule.c:2069-2091 - builtin_oct
            PyInt intValue;
            if (obj is PyInt pyInt)
            {
                intValue = pyInt;
            }
            else if (obj is PyBool pyBool)
            {
                intValue = new PyInt(pyBool.IsTrue() ? 1 : 0);
            }
            else
            {
                try
                {
                    var indexMethod = obj.GetAttribute("__index__");
                    var result = indexMethod.Call(Array.Empty<PyObject>(), null);
                    if (result is PyInt indexResult)
                    {
                        intValue = indexResult;
                    }
                    else
                    {
                        throw PyTypeError.Create($"__index__ returned non-int (type {result.GetTypeName()})");
                    }
                }
                catch
                {
                    throw PyTypeError.Create($"'{obj.GetTypeName()}' object cannot be interpreted as an integer");
                }
            }

            // Delegate to PyInt.Oct() for proper BigInteger formatting
            return intValue.Oct();
        }

        /// <summary>
        /// bin(x) - CPython 3.12: Python/bltinmodule.c:541 (builtin_bin)
        /// Convert an integer number to a binary string prefixed with "0b"
        /// </summary>
        private static PyObject CallBin(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"bin() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // CPython 3.12: Python/bltinmodule.c:541-563 - builtin_bin
            PyInt intValue;
            if (obj is PyInt pyInt)
            {
                intValue = pyInt;
            }
            else if (obj is PyBool pyBool)
            {
                intValue = new PyInt(pyBool.IsTrue() ? 1 : 0);
            }
            else
            {
                try
                {
                    var indexMethod = obj.GetAttribute("__index__");
                    var result = indexMethod.Call(Array.Empty<PyObject>(), null);
                    if (result is PyInt indexResult)
                    {
                        intValue = indexResult;
                    }
                    else
                    {
                        throw PyTypeError.Create($"__index__ returned non-int (type {result.GetTypeName()})");
                    }
                }
                catch
                {
                    throw PyTypeError.Create($"'{obj.GetTypeName()}' object cannot be interpreted as an integer");
                }
            }

            // Delegate to PyInt.Bin() for proper BigInteger formatting
            return intValue.Bin();
        }

        /// <summary>
        /// ascii(obj) - CPython 3.12: Python/bltinmodule.c:414 (builtin_ascii)
        /// Return string containing a printable representation of an object,
        /// but escape non-ASCII characters using \x, \u, or \U escapes
        /// </summary>
        private static PyObject CallAscii(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"ascii() takes exactly one argument ({args.Length} given)");

            var obj = args[0];
            var repr = obj.ToRepr().Value;

            // Escape non-ASCII characters
            var sb = new System.Text.StringBuilder();
            foreach (char c in repr)
            {
                if (c < 128)
                {
                    sb.Append(c);
                }
                else if (c <= 0xFF)
                {
                    sb.Append($"\\x{(int)c:x2}");
                }
                else if (c <= 0xFFFF)
                {
                    sb.Append($"\\u{(int)c:x4}");
                }
                else
                {
                    sb.Append($"\\U{(int)c:x8}");
                }
            }
            return new PyStr(sb.ToString());
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
        /// vars([object]) - CPython 3.12: Python/bltinmodule.c:2802 (builtin_vars)
        /// Return the __dict__ attribute for a module, class, instance, or any other object
        /// </summary>
        private static PyObject CallVars(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"vars() takes at most 1 argument ({args.Length} given)");

            if (args.Length == 0)
            {
                // vars() without argument returns locals()
                return CallLocals(args, kwargs);
            }

            var obj = args[0];

            // Try to get __dict__ attribute
            try
            {
                var dict = obj.GetAttribute("__dict__");
                return dict;
            }
            catch
            {
                throw PyTypeError.Create($"vars() argument must have __dict__ attribute");
            }
        }

        /// <summary>
        /// format(value[, format_spec]) - CPython 3.12: Python/bltinmodule.c:946 (builtin_format)
        /// Return value.__format__(format_spec)
        /// </summary>
        private static PyObject CallFormat(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"format() takes 1 or 2 arguments ({args.Length} given)");

            var value = args[0];
            var formatSpec = args.Length > 1 ? args[1] : new PyStr("");

            if (formatSpec is not PyStr specStr)
                throw PyTypeError.Create($"format() argument 2 must be str, not {formatSpec.GetTypeName()}");

            // Try to call __format__ method
            try
            {
                var formatMethod = value.GetAttribute("__format__");
                return formatMethod.Call(new PyObject[] { specStr }, null);
            }
            catch
            {
                // Fallback: use str() for empty format spec, raise error otherwise
                if (specStr.Value == "")
                {
                    return value.ToStr();
                }
                throw PyTypeError.Create($"unsupported format string passed to {value.GetTypeName()}.__format__");
            }
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
            else if (obj is PyBuiltinFunction || obj is PyBuiltinMethod)
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
                PyBuiltinMethod bm => bm.Name,
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
                    if (typeDoc is PyStr docStr)
                        return docStr.Value;
                }

                // For classes, check __doc__ in __dict__
                if (obj is PyClass cls)
                {
                    if (cls.TypeDict.TryGetValue("__doc__", out var clsDoc) && clsDoc is PyStr docStr)
                        return docStr.Value;
                }

                // For functions
                if (obj is PyFunction func)
                {
                    var docAttr = func.GetAttribute("__doc__");
                    if (docAttr != PyNone.Instance && docAttr is PyStr docStr)
                        return docStr.Value;
                }

                // Generic attribute access
                var docObj = obj.GetAttribute("__doc__");
                if (docObj != PyNone.Instance && docObj is PyStr str)
                    return str.Value;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// breakpoint(*args, **kws) - Drop into the debugger.
        /// CPython 3.12: Python/bltinmodule.c - builtin_breakpoint
        /// Note: This is a simplified implementation that just prints a message.
        /// Full debugger integration requires sys.breakpointhook.
        /// </summary>
        private static PyObject CallBreakpoint(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: calls sys.breakpointhook(*args, **kws)
            // Default hook calls pdb.set_trace()
            // Simplified: just print a message
            Console.WriteLine("*** Breakpoint ***");
            Console.WriteLine("(breakpoint() is simplified - full debugger not implemented)");
            return PyNone.Instance;
        }

        #endregion

        public override string ToString() => $"<built-in function {Name}>";
    }
    
    /// <summary>
    /// CPython 3.12 compatible super() implementation
    /// Based on CPython's superobject (Objects/typeobject.c)
    /// </summary>
    public class PySuper : PyObject
    {
        // CPython 3.12: superobject struct
        public PyType Type { get; }           // __thisclass__: the class invoking super()
        public PyObject Object { get; }       // __self__: the instance (or None)
        public PyType ObjectType { get; }     // obj_type: type of Object (or Object itself if it's a type)

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
                if (IsSubtype(objAsType, type))
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
            if (objType != null && IsSubtype(objType, type))
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
                    if (IsSubtype(classType, type))
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
        /// CPython 3.12: PyType_IsSubtype() check
        /// Returns true if 'a' is a subtype of 'b' (or same type)
        /// </summary>
        private static bool IsSubtype(PyType a, PyType b)
        {
            if (a == b) return true;

            // Check MRO of 'a' for 'b'
            if (a.MRO != null)
            {
                foreach (var mroType in a.MRO)
                {
                    if (mroType == b || mroType.Name == b.Name)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// CPython 3.12: super_getattro() -> do_super_lookup()
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PySuper.GetAttribute: Looking for '{name}' in super({Type.Name})");
            #endif

            // Special case: __class__ returns the super class itself
            if (name == "__class__")
            {
                return this.GetPyType();
            }

            // CPython 3.12: do_super_lookup()
            if (ObjectType == null)
            {
                // Unbound super - only return class attributes without binding
                return LookupInMRO(name, bindDescriptor: false);
            }

            return LookupInMRO(name, bindDescriptor: true);
        }

        /// <summary>
        /// CPython 3.12: _super_lookup_descr() + descriptor protocol
        /// Key insight: super() uses the MRO of the *instance's actual type*, not the type that called super()
        /// See CPython Objects/typeobject.c: super_getattro() uses su->obj_type->tp_mro
        /// </summary>
        private PyObject LookupInMRO(string name, bool bindDescriptor)
        {
            // CPython 3.12: Use the MRO of the actual instance's type (ObjectType), NOT Type!
            // This is crucial for cooperative multiple inheritance
            PyType startType = ObjectType ?? Type;

            if (startType.MRO == null || startType.MRO.Count <= 1)
            {
                throw PyAttributeError.Create($"'super' object has no attribute '{name}'");
            }

            // Find the position of Type in the MRO of startType
            int skipUntil = -1;
            for (int i = 0; i < startType.MRO.Count; i++)
            {
                if (startType.MRO[i] == Type || startType.MRO[i].Name == Type.Name)
                {
                    skipUntil = i;
                    break;
                }
            }

            if (skipUntil == -1)
            {
                throw PyAttributeError.Create($"super(type, obj): obj must be an instance or subtype of type");
            }

            // Look for the attribute starting from the class AFTER Type in the MRO
            for (int i = skipUntil + 1; i < startType.MRO.Count; i++)
            {
                var baseType = startType.MRO[i];

                    try
                    {
                        PyObject attr = null;

                        // CPython 3.12: Look ONLY in the class's __dict__, NOT its MRO!
                        // This is critical - we are already iterating through the MRO manually,
                        // so we must NOT use GetAttribute which would follow the MRO again!
                        // See CPython Objects/typeobject.c:10345: PyDict_GetItemWithError(dict, name)
                        if (baseType is PyClass pyClass)
                        {
                            // For user-defined classes, look only in ClassDict
                            pyClass.ClassDict.TryGetValue(name, out attr);
                        }
                        else if (baseType is PyType pyType)
                        {
                            // For built-in types, use GetTypeAttribute
                            // (built-in types don't have the double-MRO issue because they don't call GetAttribute recursively)
                            attr = SharpPy.PyClass.GetTypeAttribute(pyType, name);
                        }

                        if (attr != null)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   ✅ Found '{name}' in {baseType.Name}: {attr.GetType().Name}");
                            #endif

                            if (!bindDescriptor || Object == null)
                            {
                                // Unbound super or no binding needed
                                #if DEBUG_LOG
                                Console.WriteLine($"🔧 PySuper returning unbound attr: {attr.GetType().Name}");
                                #endif
                                return attr;
                            }

                            // CPython 3.12: Apply descriptor protocol if needed
                            return ApplyDescriptorProtocol(attr, name);
                        }
                    }
                    catch (Exception ex)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ⚠️ Error checking {baseType.Name}: {ex.Message}");
                        #endif
                    }
            }

            throw PyAttributeError.Create($"'super' object has no attribute '{name}'");
        }

        /// <summary>
        /// CPython 3.12: Apply tp_descr_get if the attribute is a descriptor
        /// Key logic from do_super_lookup():
        ///   f(res, (su_obj == su_obj_type) ? NULL : su_obj, (PyObject *)su_obj_type)
        /// </summary>
        private PyObject ApplyDescriptorProtocol(PyObject attr, string name)
        {
            // CPython 3.12: Check if descriptor protocol applies
            if (attr is IDescriptor descriptor)
            {
                // CPython 3.12 key logic:
                // If Object == ObjectType (class-mode super), pass NULL for instance
                // Otherwise pass Object (instance-mode super)
                PyObject instance = (Object == ObjectType) ? null : Object;

                #if DEBUG_LOG
                Console.WriteLine($"   🔧 Applying descriptor protocol: instance={(instance != null ? instance.GetType().Name : "null")}, owner={ObjectType.Name}");
                #endif

                var result = descriptor.Get(instance, ObjectType);
                #if DEBUG_LOG
                Console.WriteLine($"🔧 PySuper returning bound attr: {result?.GetType().Name ?? "null"}");
                #endif
                return result;
            }
            else if (attr is PyFunction function)
            {
                // PyFunction implements descriptor protocol
                // CPython 3.12: If Object == ObjectType, don't bind (class-mode)
                #if DEBUG_LOG
                Console.WriteLine($"   🔧 PyFunction check: Object={Object?.GetType().Name ?? "null"}, ObjectType={ObjectType?.Name ?? "null"}");
                Console.WriteLine($"   🔧 Object == ObjectType: {Object == ObjectType}, ReferenceEquals: {ReferenceEquals(Object, ObjectType)}");
                #endif

                if (Object == ObjectType)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   🔧 Class-mode super: returning unbound function");
                    Console.WriteLine($"🔧 PySuper returning unbound function");
                    #endif
                    return function;
                }
                else
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   🔧 Instance-mode super: binding function to {Object.GetType().Name}");
                    #endif
                    var result = new PyMethod(Object, function);
                    #if DEBUG_LOG
                    Console.WriteLine($"🔧 PySuper returning bound method");
                    #endif
                    return result;
                }
            }
            else if (attr is PyStaticBuiltinMethod || attr is PyBuiltinMethod)
            {
                // StaticBuiltinMethod and BuiltinMethod already implement IDescriptor
                // This case is already handled above
                #if DEBUG_LOG
                Console.WriteLine($"🔧 PySuper returning builtin method as-is: {attr.GetType().Name}");
                #endif
                return attr;
            }

            // Not a descriptor - return as-is
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PySuper returning non-descriptor attr: {attr.GetType().Name}");
            #endif
            return attr;
        }

        public override string ToString() => $"<super: {Type.Name}, {Object}>";
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
        public override PyStr ToRepr()
        {
            return new PyStr(_message);
        }

        // CPython: Lib/_sitebuiltins.py:38-42
        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            // When called as exit() or quit(), raise SystemExit
            // CPython 3.12: SystemExit is a BaseException, not Exception
            // This allows REPL to catch it separately from normal exceptions
            throw PySystemExit.Create(0);
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
        public override PyStr ToRepr() => new PyStr($"<object object at 0x{GetHashCode():x}>");
        public override PyStr ToStr() => ToRepr();
    }
}