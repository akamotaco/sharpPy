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
            _builtinImplementations["bool"] = (args, kwargs) => CallBool(args, kwargs);
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
        }

        // 내장 함수 호출 - CPython 3.12 호환: kwargs 지원
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // kwargs 지원 구현이 있으면 우선 사용
            if (_kwargsImplementation != null)
            {
                return _kwargsImplementation(args, kwargs);
            }

            // 시그니처가 있으면 인수 처리 후 기존 구현 호출
            if (_signature != null && _implementation != null)
            {
                var processedArgs = _signature.ProcessArguments(args, kwargs);
                return _implementation(processedArgs);
            }

            // 기존 구현이 있으면 사용 (kwargs 무시)
            if (_implementation != null)
            {
                return _implementation(args);
            }

            // CPython 호환: 딕셔너리 기반 lookup (switch 문 제거)
            if (_builtinImplementations.TryGetValue(Name, out var implementation))
            {
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

            // kwargs 처리
            if (kwargs != null)
            {
                try
                {
                    var sepValue = kwargs.GetItem(new PyString("sep"));
                    sep = sepValue as PyString ?? new PyString(sepValue.AsString());
                }
                catch { }

                try
                {
                    var endValue = kwargs.GetItem(new PyString("end"));
                    end = endValue as PyString ?? new PyString(endValue.AsString());
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
                    flush = flushValue as PyBool ?? PyBool.FromBool(flushValue.PyBoolValue());
                }
                catch { }
            }

            // 출력 생성 - CPython 3.12: print는 str()을 사용, repr()이 아님
            var output = string.Join(sep.Value, args.Select(arg => arg.ToStr().Value));

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

            if (args[0] is PyInt intVal)
                return new PyInt(Math.Abs(intVal.Value));
            else
                throw PyTypeError.Create($"bad operand type for abs(): '{args[0].GetTypeName()}'");
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
                    PyInt stop => PyRange.Create((int)stop.Value),
                    _ => throw PyTypeError.Create("'int' object cannot be interpreted as an integer")
                },
                2 => (args[0], args[1]) switch
                {
                    (PyInt start, PyInt stop) => PyRange.Create((int)start.Value, (int)stop.Value),
                    _ => throw PyTypeError.Create("'int' object cannot be interpreted as an integer")
                },
                3 => (args[0], args[1], args[2]) switch
                {
                    (PyInt start, PyInt stop, PyInt step) => PyRange.Create((int)start.Value, (int)stop.Value, (int)step.Value),
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
            var start = args.Length > 1 ? ((PyInt)args[1]).Value : 0;

            // 간단한 enumerate 구현 - PyList 반환
            var result = new System.Collections.Generic.List<PyObject>();
            var iterator = iterable.GetIterator();
            var index = start;

            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    result.Add(new PyTuple(new PyInt(index), item));
                    index++;
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
            }

            return new PyList(result.ToArray());
        }

        private static PyObject CallZip(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
                return new PyList(new PyObject[0]);

            var iterators = args.Select(arg => arg.GetIterator()).ToArray();
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

            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    var mappedItem = func.Call(new PyObject[] { item }, null);
                    result.Add(mappedItem);
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
            }

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

            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    var shouldInclude = func == PyNone.Instance 
                        ? item.PyBoolValue() 
                        : func.Call(new PyObject[] { item }, null).PyBoolValue();
                    
                    if (shouldInclude)
                        result.Add(item);
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
            }

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
                    var keyValue = kwargs.GetItem(new PyString("key"));
                    keyFunc = keyValue != PyNone.Instance ? keyValue : null;
                }
                catch { }

                try
                {
                    var reverseValue = kwargs.GetItem(new PyString("reverse"));
                    reverse = reverseValue.PyBoolValue();
                }
                catch { }

                // 예상치 못한 키워드 인수 체크
                foreach (var kvp in kwargs.InternalDict)
                {
                    if (kvp.Key is PyString keyStr)
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

            try
            {
                while (true)
                {
                    items.Add(iterator.Next());
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
            }

            // key 함수가 있으면 키 값과 함께 정렬
            if (keyFunc != null)
            {
                var keysAndItems = items.Select(item => new { Item = item, Key = keyFunc.Call(new PyObject[] { item }, null) }).ToList();

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

                items = keysAndItems.Select(x => x.Item).ToList();
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

            return new PyList(items.ToArray());
        }

        private static PyObject CallReversed(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"reversed expected exactly 1 arguments ({args.Length} given)");

            var iterable = args[0];
            var items = new System.Collections.Generic.List<PyObject>();
            var iterator = iterable.GetIterator();

            try
            {
                while (true)
                {
                    items.Add(iterator.Next());
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
            }

            items.Reverse();
            return new PyList(items.ToArray());
        }

        private static PyObject CallSum(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"sum expected at most 2 arguments ({args.Length} given)");

            var iterable = args[0];
            var start = args.Length > 1 ? args[1] : new PyInt(0);
            var result = start;
            var iterator = iterable.GetIterator();

            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    result = result.Add(item);
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
            }

            return result;
        }

        private static PyObject CallMin(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("min expected at least 1 argument (0 given)");

            if (args.Length == 1)
            {
                // 이터러블에서 최소값 찾기
                var iterable = args[0];
                var iterator = iterable.GetIterator();
                PyObject min = null;

                try
                {
                    min = iterator.Next();
                    while (true)
                    {
                        var item = iterator.Next();
                        if (((PyBool)item.RichCompare(min, PyObject.CompareOp.LT)).Value)
                            min = item;
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    // 정상 종료
                }

                if (min == null)
                    throw PyValueError.Create("min() arg is an empty sequence");
                return min;
            }
            else
            {
                // 인수들 중 최소값
                var min = args[0];
                for (int i = 1; i < args.Length; i++)
                {
                    if (((PyBool)args[i].RichCompare(min, PyObject.CompareOp.LT)).Value)
                        min = args[i];
                }
                return min;
            }
        }

        private static PyObject CallMax(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("max expected at least 1 argument (0 given)");

            if (args.Length == 1)
            {
                // 이터러블에서 최대값 찾기
                var iterable = args[0];
                var iterator = iterable.GetIterator();
                PyObject max = null;

                try
                {
                    max = iterator.Next();
                    while (true)
                    {
                        var item = iterator.Next();
                        if (((PyBool)item.RichCompare(max, PyObject.CompareOp.GT)).Value)
                            max = item;
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    // 정상 종료
                }

                if (max == null)
                    throw PyValueError.Create("max() arg is an empty sequence");
                return max;
            }
            else
            {
                // 인수들 중 최대값
                var max = args[0];
                for (int i = 1; i < args.Length; i++)
                {
                    if (((PyBool)args[i].RichCompare(max, PyObject.CompareOp.GT)).Value)
                        max = args[i];
                }
                return max;
            }
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
                    if (item.PyBoolValue())
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
                    if (!item.PyBoolValue())
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
                throw PyTypeError.Create("isinstance() arg 2 must be a type or tuple of types");
            }
        }
        
        /// <summary>
        /// 제네릭 타입을 지원하는 확장된 isinstance 검사
        /// </summary>
        private static bool IsInstanceExtended(PyObject obj, PyType type)
        {
            // 기본 isinstance 검사
            if (obj.IsInstance(type))
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
            if (args.Length != 2)
                throw PyTypeError.Create($"issubclass expected exactly 2 arguments ({args.Length} given)");

            if (!(args[0] is PyType subclass))
                throw PyTypeError.Create("issubclass() arg 1 must be a class");

            if (args[1] is PyType superclass)
            {
                return PyBool.FromBool(subclass.IsSubclassOf(superclass));
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
                throw PyTypeError.Create("issubclass() arg 2 must be a class or tuple of classes");
            }
        }

        private static PyObject CallHasAttr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"hasattr expected exactly 2 arguments ({args.Length} given)");

            var obj = args[0];
            var name = args[1];

            if (!(name is PyString strName))
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

            if (!(name is PyString strName))
                throw PyTypeError.Create("getattr expected str object, not '" + name.GetTypeName() + "'");

            try
            {
                return obj.GetAttribute(strName.Value);
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

            if (!(name is PyString strName))
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

            if (!(name is PyString strName))
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

        private static PyObject CallStr(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: str() with no args returns empty string
            if (args.Length == 0)
                return new PyString("");

            if (args.Length != 1)
                throw PyTypeError.Create($"str expected at most 1 argument ({args.Length} given)");

            return new PyString(args[0].AsString());
        }

        private static PyObject CallInt(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: int() with no args returns 0
            if (args.Length == 0)
                return new PyInt(0);

            if (args.Length > 2)
                throw PyTypeError.Create($"int() takes at most 2 arguments ({args.Length} given)");

            // int(x, base=10) - base parameter not fully implemented yet
            return args[0].AsInt();
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

            try
            {
                while (true)
                {
                    items.Add(iterator.Next());
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
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

            try
            {
                while (true)
                {
                    items.Add(iterator.Next());
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
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

        private static PyObject CallRound(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"round expected 1 or 2 arguments ({args.Length} given)");

            var number = args[0];
            var ndigits = args.Length > 1 ? ((PyInt)args[1]).Value : 0;

            if (number is PyFloat f)
            {
                var rounded = Math.Round(f.Value, (int)ndigits);
                return ndigits == 0 ? new PyInt((int)rounded) : new PyFloat(rounded);
            }
            else if (number is PyInt i)
            {
                return i; // 정수는 그대로
            }
            else
            {
                throw PyTypeError.Create("a float is required");
            }
        }

        private static PyObject CallPow(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"pow expected 2 or 3 arguments ({args.Length} given)");

            var base_ = args[0];
            var exp = args[1];
            var mod = args.Length > 2 ? args[2] : null;

            if (mod != null)
            {
                // pow(x, y, z) = (x**y) % z
                var result = base_.Power(exp);
                return result.Modulo(mod);
            }
            else
            {
                return base_.Power(exp);
            }
        }

        private static PyObject CallDivmod(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"divmod expected exactly 2 arguments ({args.Length} given)");

            var a = args[0];
            var b = args[1];

            if (a is PyInt ai && b is PyInt bi)
            {
                var quotient = ai.Value / bi.Value;
                var remainder = ai.Value % bi.Value;
                return new PyTuple(new PyInt(quotient), new PyInt(remainder));
            }
            else if (a is PyFloat af || b is PyFloat bf)
            {
                var aVal = a is PyFloat ? ((PyFloat)a).Value : ((PyInt)a).Value;
                var bVal = b is PyFloat ? ((PyFloat)b).Value : ((PyInt)b).Value;
                var quotient = Math.Floor(aVal / bVal);
                var remainder = aVal - quotient * bVal;
                return new PyTuple(new PyFloat(quotient), new PyFloat(remainder));
            }
            else
            {
                throw PyTypeError.Create("unsupported operand types for divmod()");
            }
        }

        private static PyObject CallOrd(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"ord expected exactly 1 arguments ({args.Length} given)");

            if (args[0] is PyString str && str.Value.Length == 1)
            {
                return new PyInt((int)str.Value[0]);
            }
            else
            {
                throw PyTypeError.Create("ord() expected a character, but string of length " + 
                    (args[0] is PyString s ? s.Value.Length.ToString() : "?") + " found");
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
                
                return new PyString(((char)i.Value).ToString());
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
                "__name__" => new PyString(Name),
                "__call__" => this,
                "__new__" when Name == "type" => new PyBuiltinFunction("type.__new__"),
                _ => throw PyAttributeError.Create($"'builtin_function_or_method' object has no attribute '{name}'")
            };
        }

        private static PyObject CallDir(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"dir expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
            {
                // dir() with no arguments - return local variables
                // For now, return empty list
                return new PyList();
            }

            var obj = args[0];
            var attributes = new List<PyObject>();

            // Get object's __dict__ if available
            try
            {
                var dict = obj.GetAttribute("__dict__");
                if (dict is PyDict pyDict)
                {
                    foreach (var key in pyDict.Keys().Items)
                    {
                        attributes.Add(key);
                    }
                }
            }
            catch
            {
                // No __dict__, continue with other attributes
            }

            // Add common attributes based on object type
            if (obj is PyString)
            {
                attributes.AddRange(new[] { 
                    new PyString("capitalize"), new PyString("center"), new PyString("count"),
                    new PyString("endswith"), new PyString("find"), new PyString("format"),
                    new PyString("join"), new PyString("lower"), new PyString("replace"),
                    new PyString("split"), new PyString("startswith"), new PyString("strip"),
                    new PyString("upper")
                });
            }
            else if (obj is PyList)
            {
                attributes.AddRange(new[] {
                    new PyString("append"), new PyString("count"), new PyString("extend"),
                    new PyString("index"), new PyString("insert"), new PyString("pop"),
                    new PyString("remove"), new PyString("reverse"), new PyString("sort")
                });
            }
            else if (obj is PyDict)
            {
                attributes.AddRange(new[] {
                    new PyString("clear"), new PyString("copy"), new PyString("get"),
                    new PyString("items"), new PyString("keys"), new PyString("pop"),
                    new PyString("update"), new PyString("values")
                });
            }

            // Add standard object attributes
            attributes.AddRange(new[] {
                new PyString("__class__"), new PyString("__doc__"), 
                new PyString("__module__"), new PyString("__dict__")
            });

            // Sort alphabetically (like CPython)
            attributes.Sort((a, b) => a.ToString().CompareTo(b.ToString()));

            return new PyList(attributes);
        }

        /// <summary>
        /// C# 타입 시스템을 활용한 정확한 메타클래스 검출 (재귀 방지 헬퍼)
        /// </summary>
        private static bool IsMetaclassRecursive(PyClass pyClass)
        {
            if (pyClass.BaseTypes == null) return false;
            
            // 직접 type을 상속하면 메타클래스
            return pyClass.BaseTypes.Any(t => t == PyType.TypeType || t.Name == "type");
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
                bool directlyInheritsFromType = pyClass.BaseTypes.Any(t => t == PyType.TypeType || t.Name == "type");
                if (directlyInheritsFromType)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ BaseTypes에서 직접 'type' 상속 확인: [{string.Join(", ", pyClass.BaseTypes.Select(t => t.Name))}]");
                    #endif
                    return true;
                }
                
                // 다른 메타클래스를 상속하는 경우 (재귀 검사)
                bool inheritsFromMetaclass = pyClass.BaseTypes.Any(t => 
                    t is PyClass baseClass && IsMetaclassRecursive(baseClass));
                if (inheritsFromMetaclass)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ BaseTypes에서 메타클래스 상속 확인: [{string.Join(", ", pyClass.BaseTypes.Select(t => t.Name))}]");
                    #endif
                    return true;
                }
            }

            #if DEBUG_LOG
            Console.WriteLine($"   ❌ 메타클래스가 아님: Name={pyClass.Name}, BaseTypes=[{string.Join(", ", pyClass.BaseTypes?.Select(t => t.Name) ?? new string[0])}]");
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
            if (kwargs != null && kwargs.InternalDict.ContainsKey(new PyString("metaclass")))
            {
                metaclass = kwargs.InternalDict[new PyString("metaclass")];
                hasMetaclass = true;
                #if DEBUG_LOG
                Console.WriteLine($"   ✅ Metaclass from kwargs: {metaclass}");
                #endif
            }

            // Check for explicit metaclass marker (old way, for backward compatibility)
            bool hasExplicitMetaclass = false;
            if (!hasMetaclass && args.Length >= 4)
            {
                // Look for "__metaclass__" marker in second-to-last position
                var markerIndex = args.Length - 2;
                if (args[markerIndex] is PyString marker && marker.Value == "__metaclass__")
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
                metaclass = PyTypeMetaclass.CallCalculateMetaclass(metaclass, bases.ToArray());
                #if DEBUG_LOG
                Console.WriteLine($"Winner metaclass after CalculateMetaclass: {metaclass}");
                #endif
            }

            var className = name.AsString();

            // CPython 3.12: Call __prepare__ if metaclass has it
            Dictionary<string, PyObject> classNamespace = new Dictionary<string, PyObject>();
            PyDict prepareDict = null;  // Store the original __prepare__ result to preserve special attributes
            PyObject originalPrepareResult = null;  // Track the original __prepare__ result (PyClassInstance for _EnumDict)

            Console.WriteLine($"🔧 __build_class__ for {className}: hasMetaclass={hasMetaclass}, metaclass={metaclass?.GetType().Name}");
            if (hasMetaclass && metaclass != null)
            {
                try
                {
                    Console.WriteLine($"  🔎 Looking for __prepare__ on {metaclass}");
                    var prepareMethod = metaclass.GetAttribute("__prepare__");
                    Console.WriteLine($"  🔎 Got prepareMethod: {prepareMethod?.GetType().Name} (callable={prepareMethod?.IsCallable()})");
                    if (prepareMethod != null && prepareMethod.IsCallable())
                    {
                        Console.WriteLine($"🔧 Calling __prepare__ on metaclass {metaclass}");
                        Console.WriteLine($"   prepareMethod type: {prepareMethod.GetType().Name}");
                        Console.WriteLine($"   prepareMethod is PyMethod: {prepareMethod is PyMethod}");

                        // __prepare__(metacls, name, bases, **kwds)
                        // If prepareMethod is a bound method (PyMethod), the first argument is already bound
                        // So we should NOT pass metaclass again
                        PyObject[] prepareArgs;
                        if (prepareMethod is PyMethod)
                        {
                            // Bound method - don't pass metaclass
                            prepareArgs = new PyObject[] {
                                new PyString(className),
                                new PyTuple(bases.Cast<PyObject>().ToArray())
                            };
                            Console.WriteLine($"   Using bound method call with {prepareArgs.Length} args (cls, bases)");
                        }
                        else
                        {
                            // Unbound method - pass metaclass
                            prepareArgs = new PyObject[] {
                                metaclass,
                                new PyString(className),
                                new PyTuple(bases.Cast<PyObject>().ToArray())
                            };
                            Console.WriteLine($"   Using unbound method call with {prepareArgs.Length} args (metacls, cls, bases)");
                        }

                        Console.WriteLine($"  📞 About to call __prepare__ with {prepareArgs.Length} args");
                        var prepareResult = prepareMethod.Call(prepareArgs, null);
                        Console.WriteLine($"  ✅ __prepare__ returned: {prepareResult?.GetType().Name} (Type: {prepareResult?.GetTypeName()})");
                        Console.WriteLine($"     is PyDict: {prepareResult is PyDict}, is PyClassInstance: {prepareResult is PyClassInstance}");

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
                                    if (tuple.Items[0] is PyString keyStr)
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
                                                if (tuple.Items[0] is PyString keyStr)
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
                            prepareDict.InternalDict[new PyString("__prepare_result__")] = prepareResult;

                            Console.WriteLine($"  🔑 Stored original object in __prepare_result__ marker");
                            Console.WriteLine($"     prepareDict.InternalDict.Count = {prepareDict.InternalDict.Count}");
                            Console.WriteLine($"     prepareResult type: {prepareResult.GetType().Name}");
                        }
                    }
                }
                catch (PythonException pex) when (pex.PyException is PyAttributeError)
                {
                    // __prepare__ not found, that's okay
                    Console.WriteLine($"❌ AttributeError during __prepare__: {pex.Message}");
                    Console.WriteLine("   Using empty namespace");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Exception while calling __prepare__: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"   Stack trace: {ex.StackTrace}");
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
                        foreach (var kvp in classNamespace.Take(10))
                        {
                            Console.WriteLine($"   - {kvp.Key}: {kvp.Value?.GetTypeName()}");
                        }
                        #endif

                        var vm = PyVM.Instance;
                        classNamespace = vm.ExecuteClassBody(classBodyFunc.CodeObject, classBodyFunc.Closure, classNamespace);

                        #if DEBUG_LOG
                        Console.WriteLine($"📦 classNamespace after ExecuteClassBody: {classNamespace.Count} items");
                        foreach (var kvp in classNamespace.Take(10))
                        {
                            Console.WriteLine($"   - {kvp.Key}: {kvp.Value?.GetTypeName()}");
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
                    Console.WriteLine($"Error executing class body for {className}: {ex.Message}");
                    #if DEBUG_LOG
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    #endif
                    // Don't re-throw to allow class creation to continue
                    // The real issue is that method bodies should not execute during class definition
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
                    if (originalPrepareResult != null)
                    {
                        // Use the original __prepare__ result directly (PyClassInstance for _EnumDict)
                        Console.WriteLine($"  ✅ Using original __prepare__ result! Type: {originalPrepareResult.GetType().Name}");

                        // Update the original _EnumDict-like object with class body variables
                        foreach (var kvp in classNamespace)
                        {
                            // Use __setitem__ to set values on the dict-like object
                            try
                            {
                                originalPrepareResult.SetItem(new PyString(kvp.Key), kvp.Value);
                            }
                            catch (PythonException)
                            {
                                // Fallback: try setAttribute
                                originalPrepareResult.SetAttribute(kvp.Key, kvp.Value);
                            }
                        }
                        namespaceObj = originalPrepareResult;  // Use the original _EnumDict instance
                        Console.WriteLine($"📦 Using original __prepare__ result (_EnumDict instance)");
                        Console.WriteLine($"   namespaceObj type after assignment: {namespaceObj?.GetType().Name}, PyType: {namespaceObj?.GetTypeName()}");
                    }
                    else if (prepareDict != null)
                    {
                        // Use prepareDict (regular PyDict)
                        namespaceObj = prepareDict;
                        foreach (var kvp in classNamespace)
                        {
                            ((PyDict)namespaceObj).SetItem(new PyString(kvp.Key), kvp.Value);
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
                            namespaceDict.SetItem(new PyString(kvp.Key), kvp.Value);
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
                    Console.WriteLine($"🔍 Getting __new__ from metaclass: {metaclass}");
                    var newMethod = metaclass.GetAttribute("__new__");
                    Console.WriteLine($"🔍 Got newMethod: {newMethod?.GetType().Name ?? "null"}");
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
                        if (newMethod is PyFunction pyFunc && pyFunc.CodeObject?.FreeVars?.Contains("__class__") == true)
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
                                
                                var classIndex = pyFunc.CodeObject.FreeVars.IndexOf("__class__");
                                if (classIndex >= 0 && classIndex < adjustedClosure.Length)
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   Original __class__ cell: {adjustedClosure[classIndex]?.Value}");
                                    #endif
                                    adjustedClosure[classIndex] = new PyCell(metaclass);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   ✅ Updated __class__ cell[{classIndex}] to {metaclass}");
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
                        
                        Console.WriteLine($"🔍 Before creating newArgs: namespaceObj type={namespaceObj?.GetType().Name}, PyType={namespaceObj?.GetTypeName()}");

                        var newArgs = new PyObject[] {
                            metaclass,                      // cls
                            new PyString(className),        // name
                            new PyTuple(bases.Cast<PyObject>().ToArray()), // bases
                            namespaceObj                    // namespace - PyDict or dict-like object (e.g., _EnumDict)
                        };

                        Console.WriteLine($"🔍 After creating newArgs: newArgs[3] type={newArgs[3]?.GetType().Name}, PyType={newArgs[3]?.GetTypeName()}");

                        // CPython 3.12: Pass keyword arguments (like boundary, **kwds) to metaclass.__new__
                        // Metaclasses may have keyword-only parameters after *
                        // Note: We pass empty kwargs which should use the default values from the function signature
                        PyDict newKwargs = new PyDict();

                        // CPython 3.12: Make metaclass name available during metaclass.__new__ execution
                        // This enables explicit super(MetaclassName, cls) calls
                        PyObject? previousValue = null;
                        bool hadPreviousValue = false;
                        var globalScope = PyVM.CurrentFrame?.ScopeChain?.GlobalScope;
                        if (globalScope != null && metaclass is PyClass metaclassForScope)
                        {
                            string metaclassName = metaclassForScope.Name;
                            #if DEBUG_LOG
                            Console.WriteLine($"🔧 CPython 3.12: Temporarily adding {metaclassName} to global scope for explicit super()");
                            #endif
                            
                            // Save any existing value
                            if (globalScope.Variables.ContainsKey(metaclassName))
                            {
                                previousValue = globalScope.GetVariable(metaclassName);
                                hadPreviousValue = true;
                            }
                            
                            // Set the metaclass in global scope
                            globalScope.SetVariable(metaclassName, metaclass);
                        }
                        
                        PyObject? result = null;
                        try
                        {
                            // Execute metaclass.__new__ - this should modify namespaceDict and call type.__new__
                            Console.WriteLine($"🚀 Calling metaclass.__new__ for class: {className}");
                            Console.WriteLine($"  newArgs[3] type: {newArgs[3]?.GetType().Name}, PyType: {newArgs[3]?.GetTypeName()}");
                            result = newMethod.Call(newArgs, newKwargs);
                            Console.WriteLine($"🚀 metaclass.__new__ returned: {result?.GetType().Name ?? "null"}");
                        }
                        finally
                        {
                            // CPython 3.12: Restore previous global scope state
                            if (globalScope != null && metaclass is PyClass metaclassForRestore)
                            {
                                string metaclassName = metaclassForRestore.Name;
                                if (hadPreviousValue)
                                {
                                    globalScope.SetVariable(metaclassName, previousValue!);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔧 Restored {metaclassName} to previous value in global scope");
                                    #endif
                                }
                                else
                                {
                                    globalScope.Variables.Remove(metaclassName);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🔧 Removed {metaclassName} from global scope");
                                    #endif
                                }
                            }
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
                            
                            // CPython 3.12: The metaclass.__new__ should have already set all attributes
                            // But let's ensure any additional attributes from the modified namespace are set
                            #if DEBUG_LOG
                            Console.WriteLine("Ensuring all namespace attributes are set on metaclass-created class");
                            #endif
                            // Get items from namespace (dict or dict-like object)
                            PyList items;
                            if (namespaceObj is PyDict pyDictObj)
                            {
                                items = pyDictObj.Items();
                            }
                            else
                            {
                                // Try calling items() method on dict-like object
                                var itemsMethod = namespaceObj.GetAttribute("items");
                                items = itemsMethod.Call(new PyObject[0], null) as PyList;
                            }
                            for (int i = 0; i < items.Items.Length; i++)
                            {
                                if (items.Items[i] is PyTuple kvp && kvp.Items.Length == 2)
                                {
                                    if (kvp.Items[0] is PyString keyStr)
                                    {
                                        pyClass.SetAttribute(keyStr.Value, kvp.Items[1]);
                                        #if DEBUG_LOG
                                        Console.WriteLine($"  Set attribute from namespace: {keyStr.Value} = {kvp.Items[1].GetType().Name}");
                                        #endif
                                    }
                                }
                            }
                            
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
                                    var initArgs = new PyObject[] {
                                        pyClass,                    // cls (the created class)
                                        new PyString(className),    // name
                                        new PyTuple(bases.Cast<PyObject>().ToArray()), // bases
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
                            var typeResult = CallTypeNew(new PyObject[] { metaclass, new PyString(className), new PyTuple(bases.Cast<PyObject>().ToArray()), namespaceObj });
                            // If type.__new__ didn't return a PyClass, create one with module info
                            if (typeResult is PyClass existingClass)
                            {
                                pyClass = existingClass;
                            }
                            else
                            {
                                string moduleInfo = null;
                                if (classNamespace.TryGetValue("__module__", out var moduleObj) && moduleObj is PyString moduleStr)
                                {
                                    moduleInfo = moduleStr.Value;
                                }
                                pyClass = new PyClass(className, bases.ToArray(), classNamespace, null, moduleInfo);
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
                        var typeResult = CallTypeNew(new PyObject[] { metaclass, new PyString(className), new PyTuple(bases.Cast<PyObject>().ToArray()), namespaceObj });
                        // If type.__new__ didn't return a PyClass, create one with module info
                        if (typeResult is PyClass existingClass)
                        {
                            pyClass = existingClass;
                        }
                        else
                        {
                            string moduleInfo = null;
                            if (classNamespace.TryGetValue("__module__", out var moduleObj) && moduleObj is PyString moduleStr)
                            {
                                moduleInfo = moduleStr.Value;
                            }
                            pyClass = new PyClass(className, bases.ToArray(), classNamespace, null, moduleInfo);
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
                if (classNamespace.TryGetValue("__module__", out var moduleObj) && moduleObj is PyString moduleStr)
                {
                    moduleInfo = moduleStr.Value;
                }

                pyClass = new PyClass(className, bases.ToArray(), classNamespace, null, moduleInfo);

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
                    namespaceDict.SetItem(new PyString(kvp.Key), kvp.Value);
                }
                
                // After metaclass execution, the namespaceDict should contain any additions
                // But we need to get the updated dict from the metaclass result
                // For now, let's try to get the attributes from the created class itself
                #if DEBUG_LOG
                Console.WriteLine($"Setting attributes for metaclass-created class");
                #endif

                // CPython 3.12: Ensure all namespace attributes are set on metaclass-created class
                #if DEBUG_LOG
                Console.WriteLine($"Ensuring all namespace attributes are set on metaclass-created class");
                #endif
                foreach (var kvp in classNamespace)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  Set attribute from namespace: {kvp.Key} = {kvp.Value.GetType().Name}");
                    #endif
                    pyClass.SetAttribute(kvp.Key, kvp.Value);
                }
            }
            else
            {
                foreach (var kvp in classNamespace)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"  Setting class attribute: {kvp.Key} = {kvp.Value.GetType().Name}");
                    #endif
                    pyClass.SetAttribute(kvp.Key, kvp.Value);
                }
            }
            
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
            
            return pyClass;
        }

        /// <summary>
        /// __import__(name, globals=None, locals=None, fromlist=(), level=0)
        /// 동적 import 기능
        /// </summary>
        private static PyObject CallImport(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 5)
                throw PyTypeError.Create($"__import__ expected 1 to 5 arguments ({args.Length} given)");

            var name = args[0].AsString();
            // globals, locals, fromlist, level 매개변수는 일단 무시하고 기본 동작만 구현
            
            try
            {
                var module = PyImportSystem.Import(name);
                return module;
            }
            catch (System.Exception ex)
            {
                throw PyImportError.Create($"No module named '{name}': {ex.Message}");
            }
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
                    var modeValue = kwargs.GetItem(new PyString("mode"));
                    mode = modeValue.ToStr().Value;
                }
                catch { }

                try
                {
                    var bufferingValue = kwargs.GetItem(new PyString("buffering"));
                    buffering = (int)((PyInt)bufferingValue).Value;
                }
                catch { }

                try
                {
                    var encodingValue = kwargs.GetItem(new PyString("encoding"));
                    encoding = encodingValue != PyNone.Instance ? encodingValue.ToStr().Value : null;
                }
                catch { }

                try
                {
                    var errorsValue = kwargs.GetItem(new PyString("errors"));
                    errors = errorsValue != PyNone.Instance ? errorsValue.ToStr().Value : null;
                }
                catch { }

                try
                {
                    var newlineValue = kwargs.GetItem(new PyString("newline"));
                    newline = newlineValue != PyNone.Instance ? newlineValue.ToStr().Value : null;
                }
                catch { }

                try
                {
                    var closefdValue = kwargs.GetItem(new PyString("closefd"));
                    closefd = closefdValue.PyBoolValue();
                }
                catch { }

                try
                {
                    var openerValue = kwargs.GetItem(new PyString("opener"));
                    opener = openerValue != PyNone.Instance ? openerValue : null;
                }
                catch { }

                // 예상치 못한 키워드 인수 체크
                var validKeys = new[] { "mode", "buffering", "encoding", "errors", "newline", "closefd", "opener" };
                foreach (var kvp in kwargs.InternalDict)
                {
                    if (kvp.Key is PyString keyStr && !validKeys.Contains(keyStr.Value))
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
                    int classIndex = -1;
                    
                    // Check FreeVars first (most common for class methods)
                    var freeVarIndex = currentFrame.Code.FreeVars.IndexOf("__class__");
                    if (freeVarIndex >= 0)
                    {
                        classIndex = freeVarIndex; // FreeVars start at index 0
                        #if DEBUG_LOG
                        Console.WriteLine($"🔍 Found __class__ in FreeVars at index {classIndex}");
                        #endif
                    }
                    else
                    {
                        // Check CellVars (local cell variables come after FreeVars)
                        var cellVarIndex = currentFrame.Code.CellVars.IndexOf("__class__");
                        if (cellVarIndex >= 0)
                        {
                            classIndex = (currentFrame.Code.FreeVars?.Count ?? 0) + cellVarIndex;
                            #if DEBUG_LOG
                            Console.WriteLine($"🔍 Found __class__ in CellVars at combined index {classIndex}");
                            #endif
                        }
                    }
                    
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
                                
                                // CPython 3.12: Dynamic __class__ resolution for metaclass inheritance
                                // When TopMeta class is being created, its inherited __new__ method should use TopMeta, not MiddleMeta
                                var actualClassValue = ResolveActualClass(classValue, currentFrame);
                                if (actualClassValue != classValue)
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"🎯 CPython 3.12: Dynamic __class__ resolution: {classValue} → {actualClassValue}");
                                    #endif
                                    classValue = actualClassValue;
                                }
                                
                                // CPython 3.12: zero-argument super() needs __class__ and first parameter
                                // CPython uses LOAD_FAST 0 - always the first parameter, regardless of name
                                // (could be 'self', 'cls', 'metacls', or any other name)
                                PyObject instance = null;
                                if (currentFrame.Code.VarNames.Count > 0)
                                {
                                    var firstParam = currentFrame.Code.VarNames[0];
                                    // CPython bytecode: LOAD_FAST 0 - get first parameter by index, not by name
                                    if (currentFrame.FastLocals.TryGetValue(firstParam, out var firstValue))
                                    {
                                        instance = firstValue;
                                        #if DEBUG_LOG
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

        private static PyObject CallClassmethod(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
            {
                throw PyTypeError.Create($"classmethod expected 1 argument ({args.Length} given)");
            }

            if (!(args[0] is PyFunction function))
            {
                throw PyTypeError.Create("classmethod() argument must be callable");
            }

            return new PyClassmethod(function);
        }

        private static PyObject CallStaticmethod(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
            {
                throw PyTypeError.Create($"staticmethod expected 1 argument ({args.Length} given)");
            }

            var func = args[0];
            Console.WriteLine($"[STATICMETHOD] Received: {func?.GetType().Name} / IsCallable={func?.IsCallable()} / value={func}");

            // CPython 3.12: If already a staticmethod, return as-is (idempotent)
            if (func is PyStaticmethod pyStaticmethod)
            {
                Console.WriteLine($"[STATICMETHOD] Already a staticmethod, returning as-is");
                return pyStaticmethod;
            }

            if (func == null || !func.IsCallable())
            {
                throw PyTypeError.Create($"staticmethod() argument must be callable (got {func?.GetTypeName()})");
            }

            // CPython 3.12: Accept any callable, but PyStaticmethod only stores PyFunction
            if (func is PyFunction pyFunc)
            {
                return new PyStaticmethod(pyFunc);
            }

            // For non-PyFunction callables, we still need to wrap them
            // Create a wrapper PyFunction
            throw PyTypeError.Create("staticmethod() currently only supports PyFunction objects");
        }

        /// <summary>
        /// type.__new__ builtin method implementation
        /// Creates a new type instance (class creation)
        /// </summary>
        private static PyObject CallTypeNew(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 4)
            {
                throw PyTypeError.Create($"type.__new__() takes exactly 4 arguments ({args.Length} given)");
            }
            
            var cls = args[0];        // The metaclass (e.g., MyMeta)
            var name = args[1];       // Class name (e.g., "MyClass")  
            var bases = args[2];      // Base classes tuple (e.g., ())
            var attrs = args[3];      // Class attributes dict (e.g., {"method": <function>})
            
            // Convert arguments to proper types
            if (!(name is PyString nameStr))
            {
                throw PyTypeError.Create("type.__new__() argument 2 must be string");
            }
            
            if (!(bases is PyTuple basesTuple))
            {
                throw PyTypeError.Create("type.__new__() argument 3 must be tuple");
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
            var newClass = new PyClass(nameStr.Value, baseTypes.ToArray());

            // Set class attributes from the attrs dict or dict-like object
            var items = attrItems;
            for (int i = 0; i < items.Items.Length; i++)
            {
                if (items.Items[i] is PyTuple kvp && kvp.Items.Length == 2)
                {
                    if (kvp.Items[0] is PyString keyStr)
                    {
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

            // Convert the global scope variables to a Python dictionary
            var result = new PyDict();
            foreach (var variable in globalScope.Variables)
            {
                result.SetItem(new PyString(variable.Key), variable.Value);
            }

            return result;
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
                result.SetItem(new PyString(variable.Key), variable.Value);
            }

            return result;
        }
        
        /// <summary>
        /// CPython 3.12: Each metaclass method should see its own class as __class__
        /// </summary>
        private static PyObject ResolveActualClass(PyObject cellClassValue, PyFrame currentFrame)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 ResolveActualClass: cellClassValue = {cellClassValue}");
            #endif
            
            // CPython 3.12 correct behavior: Each method should see its own class as __class__
            // The __class__ cell value is already correct - don't try to resolve to a different class
            
            if (cellClassValue is PyClass cellClass)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   ✅ Using cell class as-is: {cellClass.Name}");
                #endif
                #if DEBUG_LOG
                Console.WriteLine($"   🎯 CPython 3.12: Each metaclass method sees its own class in __class__");
                #endif
            }
            
            return cellClassValue; // Use the original cell value - it's already correct
        }
        
        /// <summary>
        /// CPython 3.12: Find or create a target class reference for dynamic __class__ resolution
        /// </summary>
        private PyObject FindOrCreateTargetClass(string targetClassName, PyClass basedOnClass)
        {
            try
            {
                #if DEBUG_LOG
                Console.WriteLine($"🔍 FindOrCreateTargetClass: target='{targetClassName}', basedOn='{basedOnClass.Name}'");
                #endif
                
                // For metaclass inheritance, the target class should have the same base types as the cell class
                // but with the target name. This creates a "future reference" to the class being created.
                
                // Create a temporary class that inherits from the same base as the cell class
                var targetBaseTypes = basedOnClass.BaseTypes ?? new PyType[] { PyType.TypeType };
                #if DEBUG_LOG
                Console.WriteLine($"   Creating target class with base types: [{string.Join(", ", targetBaseTypes.Select(t => t.Name))}]");
                #endif
                
                // Create the target class with the correct name and base types
                var targetClass = new PyClass(targetClassName, targetBaseTypes);
                
                // Copy essential attributes from the base class to maintain metaclass behavior
                foreach (var attr in basedOnClass.ClassDict)
                {
                    if (attr.Key != "__name__" && attr.Key != "__qualname__")
                    {
                        targetClass.SetAttribute(attr.Key, attr.Value);
                        #if DEBUG_LOG
                        Console.WriteLine($"   Copied attribute: {attr.Key}");
                        #endif
                    }
                }
                
                #if DEBUG_LOG
                Console.WriteLine($"✅ Created target class reference: {targetClass}");
                #endif
                return targetClass;
            }
            catch (Exception ex)
            {
                #if DEBUG_LOG
                Console.WriteLine($"❌ Error in FindOrCreateTargetClass: {ex.Message}");
                #endif
                // Fallback to original class
                return basedOnClass;
            }
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
                if (arg is PyString str)
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
                    return new PyBytes(byteList.ToArray());
                }
                // bytes(int) - create bytes of specified size filled with zeros
                else if (arg is PyInt size)
                {
                    if (size.Value < 0)
                        throw PyValueError.Create("negative count");
                    return new PyBytes(new byte[size.Value]);
                }
                else
                {
                    throw PyTypeError.Create($"cannot convert '{arg.GetTypeName()}' object to bytes");
                }
            }
            else if (args.Length == 2)
            {
                // bytes(string, encoding)
                if (args[0] is PyString str && args[1] is PyString encoding)
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
                if (arg is PyString str)
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
                else if (arg is PyInt size)
                {
                    if (size.Value < 0)
                        throw PyValueError.Create("negative count");
                    return new PyBytearrayObject(new byte[size.Value]);
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

            // Use the object's ToRepr() method, which should provide the canonical string representation
            return new PyString(obj.ToRepr().Value);
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

            // CPython 3.12: supercheck() logic
            // Determine obj_type based on obj
            if (obj != null)
            {
                // If obj is a type itself, obj_type = obj
                if (obj is PyType objAsType)
                {
                    ObjectType = objAsType;
                }
                // Otherwise, obj_type = type(obj)
                else
                {
                    ObjectType = obj.GetPyType() as PyType;
                }
            }
        }

        /// <summary>
        /// CPython 3.12: super_getattro() -> do_super_lookup()
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            Console.WriteLine($"🔍 PySuper.GetAttribute: Looking for '{name}' in super({Type.Name})");

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
        /// </summary>
        private PyObject LookupInMRO(string name, bool bindDescriptor)
        {
            // CPython 3.12: Use MRO to find the method in parent classes
            // Skip the current class and look in its parents
            if (Type.MRO != null && Type.MRO.Count > 1)
            {
                // Check each base type in order (MRO), skipping the current type
                for (int i = 1; i < Type.MRO.Count; i++)
                {
                    var baseType = Type.MRO[i];

                    try
                    {
                        var attr = baseType.GetAttribute(name);
                        if (attr != null)
                        {
                            Console.WriteLine($"   ✅ Found '{name}' in {baseType.Name}: {attr.GetType().Name}");

                            if (!bindDescriptor || Object == null)
                            {
                                // Unbound super or no binding needed
                                Console.WriteLine($"🔧 PySuper returning unbound attr: {attr.GetType().Name}");
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

                Console.WriteLine($"   🔧 Applying descriptor protocol: instance={(instance != null ? instance.GetType().Name : "null")}, owner={ObjectType.Name}");

                var result = descriptor.Get(instance, ObjectType);
                Console.WriteLine($"🔧 PySuper returning bound attr: {result?.GetType().Name ?? "null"}");
                return result;
            }
            else if (attr is PyFunction function)
            {
                // PyFunction implements descriptor protocol
                // CPython 3.12: If Object == ObjectType, don't bind (class-mode)
                Console.WriteLine($"   🔧 PyFunction check: Object={Object?.GetType().Name ?? "null"}, ObjectType={ObjectType?.Name ?? "null"}");
                Console.WriteLine($"   🔧 Object == ObjectType: {Object == ObjectType}, ReferenceEquals: {ReferenceEquals(Object, ObjectType)}");

                if (Object == ObjectType)
                {
                    Console.WriteLine($"   🔧 Class-mode super: returning unbound function");
                    Console.WriteLine($"🔧 PySuper returning unbound function");
                    return function;
                }
                else
                {
                    Console.WriteLine($"   🔧 Instance-mode super: binding function to {Object.GetType().Name}");
                    var result = new PyMethod(Object, function);
                    Console.WriteLine($"🔧 PySuper returning bound method");
                    return result;
                }
            }
            else if (attr is PyStaticBuiltinMethod || attr is PyBuiltinMethod)
            {
                // StaticBuiltinMethod and BuiltinMethod already implement IDescriptor
                // This case is already handled above
                Console.WriteLine($"🔧 PySuper returning builtin method as-is: {attr.GetType().Name}");
                return attr;
            }

            // Not a descriptor - return as-is
            Console.WriteLine($"🔧 PySuper returning non-descriptor attr: {attr.GetType().Name}");
            return attr;
        }

        public override string ToString() => $"<super: {Type.Name}, {Object}>";
    }
}