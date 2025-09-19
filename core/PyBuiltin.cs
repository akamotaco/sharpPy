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
            
            return Name switch
            {
                "print" => CallPrint(args, kwargs),
                "len" => CallLen(args, kwargs),
                "abs" => CallAbs(args, kwargs),
                "callable" => CallCallable(args, kwargs),
                "range" => CallRange(args, kwargs),
                "enumerate" => CallEnumerate(args, kwargs),
                "zip" => CallZip(args, kwargs),
                "map" => CallMap(args, kwargs),
                "filter" => CallFilter(args, kwargs),
                "sorted" => CallSorted(args, kwargs),
                "reversed" => CallReversed(args, kwargs),
                "sum" => CallSum(args, kwargs),
                "min" => CallMin(args, kwargs),
                "max" => CallMax(args, kwargs),
                "any" => CallAny(args, kwargs),
                "all" => CallAll(args, kwargs),
                "isinstance" => CallIsInstance(args, kwargs),
                "issubclass" => CallIsSubclass(args, kwargs),
                "hasattr" => CallHasAttr(args, kwargs),
                "getattr" => CallGetAttr(args, kwargs),
                "setattr" => CallSetAttr(args, kwargs),
                "delattr" => CallDelAttr(args, kwargs),
                "dir" => CallDir(args, kwargs),
                "__import__" => CallImport(args, kwargs),
                "type" => CallType(args, kwargs),
                "id" => CallId(args, kwargs),
                "hash" => CallHash(args, kwargs),
                "super" => CallSuper(args, kwargs),
                "property" => CallProperty(args, kwargs),
                "classmethod" => CallClassmethod(args, kwargs),
                "staticmethod" => CallStaticmethod(args, kwargs),
                "type.__new__" => CallTypeNew(args, kwargs),
                "str" => CallStr(args, kwargs),
                "repr" => CallRepr(args, kwargs),
                "int" => CallInt(args, kwargs),
                "float" => CallFloat(args, kwargs),
                "bool" => CallBool(args, kwargs),
                "list" => CallList(args, kwargs),
                "tuple" => CallTuple(args, kwargs),
                "dict" => CallDict(args, kwargs),
                "set" => CallSet(args, kwargs),
                "iter" => CallIter(args, kwargs),
                "next" => CallNext(args, kwargs),
                "round" => CallRound(args, kwargs),
                "pow" => CallPow(args, kwargs),
                "divmod" => CallDivmod(args, kwargs),
                "ord" => CallOrd(args, kwargs),
                "chr" => CallChr(args, kwargs),
                "open" => CallOpen(args, kwargs),
                "__build_class__" => CallBuildClass(args, kwargs),
                "globals" => CallGlobals(args, kwargs),
                "locals" => CallLocals(args, kwargs),
                // Buffer Protocol functions
                "bytes" => CallBytes(args, kwargs),
                "bytearray" => CallBytearray(args, kwargs),
                "memoryview" => CallMemoryview(args, kwargs),
                _ => throw PyNotImplementedError.Create($"Built-in function '{Name}' not implemented")
            };
        }

        // 내장 함수들의 구현
        private PyObject CallPrint(PyObject[] args, PyDict kwargs = null)
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
                    sep = sepValue as PyString ?? new PyString(sepValue.ToString());
                }
                catch { }

                try
                {
                    var endValue = kwargs.GetItem(new PyString("end"));
                    end = endValue as PyString ?? new PyString(endValue.ToString());
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

            // 출력 생성
            var output = string.Join(sep.Value, args.Select(arg => arg.ToString()));

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

        private PyObject CallLen(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallAbs(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"abs() takes exactly one argument ({args.Length} given)");

            if (args[0] is PyInt intVal)
                return new PyInt(Math.Abs(intVal.Value));
            else
                throw PyTypeError.Create($"bad operand type for abs(): '{args[0].GetTypeName()}'");
        }

        private PyObject CallCallable(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"callable() takes exactly one argument ({args.Length} given)");

            return PyBool.FromBool(args[0].IsCallable());
        }

        // === 핵심 내장 함수들 ===

        private PyObject CallRange(PyObject[] args, PyDict kwargs = null)
        {
            return args.Length switch
            {
                1 => args[0] switch
                {
                    PyInt stop => PyRange.Create(stop.Value),
                    _ => throw PyTypeError.Create("'int' object cannot be interpreted as an integer")
                },
                2 => (args[0], args[1]) switch
                {
                    (PyInt start, PyInt stop) => PyRange.Create(start.Value, stop.Value),
                    _ => throw PyTypeError.Create("'int' object cannot be interpreted as an integer")
                },
                3 => (args[0], args[1], args[2]) switch
                {
                    (PyInt start, PyInt stop, PyInt step) => PyRange.Create(start.Value, stop.Value, step.Value),
                    _ => throw PyTypeError.Create("'int' object cannot be interpreted as an integer")
                },
                _ => throw PyTypeError.Create($"range expected at most 3 arguments, got {args.Length}")
            };
        }

        private PyObject CallEnumerate(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallZip(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallMap(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallFilter(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallSorted(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallReversed(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallSum(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallMin(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallMax(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallAny(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallAll(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallIsInstance(PyObject[] args, PyDict kwargs = null)
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
        private bool IsInstanceExtended(PyObject obj, PyType type)
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

        private PyObject CallIsSubclass(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallHasAttr(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallGetAttr(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallSetAttr(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallDelAttr(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallType(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"type expected exactly 1 arguments ({args.Length} given)");

            return args[0].GetPyType();
        }


        private PyObject CallId(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"id expected exactly 1 arguments ({args.Length} given)");

            return new PyInt(args[0].GetHashCode());
        }

        private PyObject CallHash(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"hash expected exactly 1 arguments ({args.Length} given)");

            return new PyInt(args[0].ToHash());
        }

        // === 타입 변환 함수들 ===

        private PyObject CallStr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"str expected exactly 1 arguments ({args.Length} given)");

            return args[0].AsString();
        }

        private PyObject CallInt(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"int expected exactly 1 arguments ({args.Length} given)");

            return args[0].AsInt();
        }

        private PyObject CallFloat(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"float expected exactly 1 arguments ({args.Length} given)");

            return args[0].AsFloat();
        }

        private PyObject CallBool(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"bool expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return PyBool.False;

            return args[0].AsBool();
        }

        private PyObject CallList(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"list expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyList(new PyObject[0]);

            return args[0].AsList();
        }

        private PyObject CallTuple(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"tuple expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyTuple();

            return args[0].AsTuple();
        }

        private PyObject CallDict(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"dict expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyDict();

            return args[0].AsDict();
        }

        private PyObject CallSet(PyObject[] args, PyDict kwargs = null)
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

        // === 이터레이터 함수들 ===

        private PyObject CallIter(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"iter expected exactly 1 arguments ({args.Length} given)");

            return args[0].GetIterator();
        }

        private PyObject CallNext(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallRound(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"round expected 1 or 2 arguments ({args.Length} given)");

            var number = args[0];
            var ndigits = args.Length > 1 ? ((PyInt)args[1]).Value : 0;

            if (number is PyFloat f)
            {
                var rounded = Math.Round(f.Value, ndigits);
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

        private PyObject CallPow(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallDivmod(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallOrd(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallChr(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallDir(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallBuildClass(PyObject[] args, PyDict kwargs = null)
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
            
            // CPython 3.12: Handle metaclass and base classes
            // SharpPy compiler pattern: 
            // - Inheritance: __build_class__(func, name, *bases)
            // - Explicit metaclass: __build_class__(func, name, *bases, "__metaclass__", metaclass)
            
            // Check for explicit metaclass marker
            bool hasExplicitMetaclass = false;
            if (args.Length >= 4)
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
                // Traditional logic: all args from index 2 onwards are bases
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
            
            // CPython 3.12: Determine metaclass from base classes if not explicitly provided
            if (!hasMetaclass && bases.Count > 0)
            {
                // Find metaclass from base classes (most derived metaclass)
                foreach (var baseClass in bases)
                {
                    if (baseClass is PyClass baseAsClass && baseAsClass.Metaclass != null)
                    {
                        metaclass = baseAsClass.Metaclass;
                        #if DEBUG_LOG
                        Console.WriteLine($"Inherited metaclass from base {baseClass.Name}: {metaclass}");
                        #endif
                        hasMetaclass = true;
                        break;
                    }
                }
            }
            
            var className = name.ToString();
            
            // CPython 3.12: First execute class body to build class namespace
            Dictionary<string, PyObject> classNamespace = new Dictionary<string, PyObject>();
            
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
                        var vm = PyVM.Instance;
                        classNamespace = vm.ExecuteClassBody(classBodyFunc.CodeObject, classBodyFunc.Closure);

                        #if DEBUG_LOG
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
                    var namespaceDict = new PyDict();
                    foreach (var kvp in classNamespace)
                    {
                        namespaceDict.SetItem(new PyString(kvp.Key), kvp.Value);
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
                    var newMethod = metaclass.GetAttribute("__new__");
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
                                }
                            }
                        }
                        
                        var newArgs = new PyObject[] {
                            metaclass,                      // cls  
                            new PyString(className),        // name
                            new PyTuple(bases.Cast<PyObject>().ToArray()), // bases
                            namespaceDict                   // namespace - this will be modified by metaclass
                        };
                        
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
                            result = newMethod.Call(newArgs, null);
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
                            var items = namespaceDict.Items();
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
                                        namespaceDict              // namespace
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
                            var typeResult = CallTypeNew(new PyObject[] { metaclass, new PyString(className), new PyTuple(bases.Cast<PyObject>().ToArray()), namespaceDict });
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
                        var typeResult = CallTypeNew(new PyObject[] { metaclass, new PyString(className), new PyTuple(bases.Cast<PyObject>().ToArray()), namespaceDict });
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
                    #if DEBUG_LOG
                    Console.WriteLine($"Error calling metaclass.__new__: {ex.Message}");
                    #endif

                    // Extract __module__ from class namespace
                    string moduleInfo = null;
                    if (classNamespace.TryGetValue("__module__", out var moduleObj) && moduleObj is PyString moduleStr)
                    {
                        moduleInfo = moduleStr.Value;
                    }

                    pyClass = new PyClass(className, bases.ToArray(), classNamespace, null, moduleInfo);
                    pyClass.SetAttribute("__metaclass__", metaclass);
                    
                    // CPython 3.12: Update __classcell__ even in exception case
                    if (classcell != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"🎯 CPython 3.12: Updating __classcell__ in exception case");
                        #endif
                        classcell.Value = pyClass;
                        #if DEBUG_LOG
                        Console.WriteLine($"✅ __classcell__ updated in exception case");
                        #endif
                    }
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
        private PyObject CallImport(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 5)
                throw PyTypeError.Create($"__import__ expected 1 to 5 arguments ({args.Length} given)");

            var name = args[0].ToString();
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

        private PyObject CallOpen(PyObject[] args, PyDict kwargs = null)
        {
            // CPython 3.12: open(file, mode='r', buffering=-1, encoding=None, errors=None, newline=None, closefd=True, opener=None)
            if (args.Length < 1)
                throw PyTypeError.Create("open() missing required argument: 'file'");

            // Extract file argument
            var filename = args[0].ToStr();
            var mode = "r";
            var buffering = -1;
            string encoding = null;
            string errors = null;
            string newline = null;
            var closefd = true;
            PyObject opener = null;

            // Process positional arguments
            if (args.Length > 1) mode = args[1].ToStr();
            if (args.Length > 2) buffering = ((PyInt)args[2]).Value;
            if (args.Length > 3) encoding = args[3] != PyNone.Instance ? args[3].ToStr() : null;
            if (args.Length > 4) errors = args[4] != PyNone.Instance ? args[4].ToStr() : null;
            if (args.Length > 5) newline = args[5] != PyNone.Instance ? args[5].ToStr() : null;
            if (args.Length > 6) closefd = args[6].PyBoolValue();
            if (args.Length > 7) opener = args[7] != PyNone.Instance ? args[7] : null;

            // Process kwargs
            if (kwargs != null)
            {
                try
                {
                    var modeValue = kwargs.GetItem(new PyString("mode"));
                    mode = modeValue.ToStr();
                }
                catch { }

                try
                {
                    var bufferingValue = kwargs.GetItem(new PyString("buffering"));
                    buffering = ((PyInt)bufferingValue).Value;
                }
                catch { }

                try
                {
                    var encodingValue = kwargs.GetItem(new PyString("encoding"));
                    encoding = encodingValue != PyNone.Instance ? encodingValue.ToStr() : null;
                }
                catch { }

                try
                {
                    var errorsValue = kwargs.GetItem(new PyString("errors"));
                    errors = errorsValue != PyNone.Instance ? errorsValue.ToStr() : null;
                }
                catch { }

                try
                {
                    var newlineValue = kwargs.GetItem(new PyString("newline"));
                    newline = newlineValue != PyNone.Instance ? newlineValue.ToStr() : null;
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
        private PyObject CallSuper(PyObject[] args, PyDict kwargs = null)
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
                                
                                // CPython 3.12: zero-argument super() needs __class__ and self/cls
                                // Check if we have a 'self' parameter in the current frame (instance method)
                                PyObject instance = null;
                                if (currentFrame.Code.VarNames.Count > 0)
                                {
                                    var firstParam = currentFrame.Code.VarNames[0];
                                    if (firstParam == "self" && currentFrame.FastLocals.TryGetValue("self", out var selfValue))
                                    {
                                        instance = selfValue;
                                        #if DEBUG_LOG
                                        Console.WriteLine($"🔍 Found 'self' parameter: {instance}");
                                        #endif
                                    }
                                    else if (firstParam == "cls" && currentFrame.FastLocals.TryGetValue("cls", out var clsValue))
                                    {
                                        instance = clsValue;
                                        #if DEBUG_LOG
                                        Console.WriteLine($"🔍 Found 'cls' parameter: {instance}");
                                        #endif
                                    }
                                }
                                
                                if (classValue is PyType pyType)
                                {
                                    // Create a proper super proxy object for PyType
                                    return new PySuperProxy(pyType, instance);
                                }
                                else if (classValue is PyClass pyClass)
                                {
                                    // Create a proper super proxy object for PyClass 
                                    // PyClass inherits from PyType, so we can use it directly
                                    return new PySuperProxy(pyClass, instance);
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
                return new PySuperProxy(pyType, obj);
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
        private PyObject CallProperty(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length < 1 || args.Length > 4)
            {
                throw PyTypeError.Create($"property expected 1 to 4 arguments ({args.Length} given)");
            }

            PyFunction getter = args[0] as PyFunction;
            PyFunction setter = args.Length > 1 ? args[1] as PyFunction : null;
            PyFunction deleter = args.Length > 2 ? args[2] as PyFunction : null;
            // args[3] would be doc string, but we'll ignore it for now

            if (getter == null)
            {
                throw PyTypeError.Create("property() argument 1 must be callable");
            }

            return new PyProperty(getter, setter, deleter);
        }

        private PyObject CallClassmethod(PyObject[] args, PyDict kwargs = null)
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

        private PyObject CallStaticmethod(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
            {
                throw PyTypeError.Create($"staticmethod expected 1 argument ({args.Length} given)");
            }

            if (!(args[0] is PyFunction function))
            {
                throw PyTypeError.Create("staticmethod() argument must be callable");
            }

            return new PyStaticmethod(function);
        }

        /// <summary>
        /// type.__new__ builtin method implementation
        /// Creates a new type instance (class creation)
        /// </summary>
        private PyObject CallTypeNew(PyObject[] args, PyDict kwargs = null)
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
            
            if (!(attrs is PyDict attrsDict))
            {
                throw PyTypeError.Create("type.__new__() argument 4 must be dict");
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
            
            // Set class attributes from the attrs dict
            var items = attrsDict.Items();
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
        private PyObject CallGlobals(PyObject[] args, PyDict kwargs = null)
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
        private PyObject CallLocals(PyObject[] args, PyDict kwargs = null)
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
        private PyObject ResolveActualClass(PyObject cellClassValue, PyFrame currentFrame)
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
        private PyObject CallBytes(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length == 0)
            {
                // bytes() with no arguments creates empty bytes
                return new PyBytes(new byte[0]);
            }
            else if (args.Length == 1)
            {
                var arg = args[0];
                
                // bytes(string, encoding) - convert string to bytes
                if (arg is PyString str)
                {
                    // Default encoding is utf-8
                    var bytes = System.Text.Encoding.UTF8.GetBytes(str.Value);
                    return new PyBytes(bytes);
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
        private PyObject CallBytearray(PyObject[] args, PyDict kwargs = null)
        {
            // Temporary implementation: return PyBytes for now
            // TODO: Implement full mutable PyBytearray class
            if (args.Length == 0)
            {
                return new PyBytes(new byte[0]);
            }
            else if (args.Length == 1)
            {
                var arg = args[0];
                if (arg is PyString str)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(str.Value);
                    return new PyBytes(bytes);
                }
                else if (arg is PyBytes bytesObj)
                {
                    return new PyBytes((byte[])bytesObj.Value.Clone());
                }
                else if (arg is PyInt size)
                {
                    if (size.Value < 0)
                        throw PyValueError.Create("negative count");
                    return new PyBytes(new byte[size.Value]);
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
        private PyObject CallMemoryview(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"memoryview() takes exactly one argument ({args.Length} given)");

            var obj = args[0];

            // Check if object supports buffer protocol
            if (obj is PyBytes bytes)
            {
                return new PyMemoryView(bytes.Value, true); // bytes are read-only
            }
            else if (obj.SupportsBuffer())
            {
                var buffer = obj.GetBuffer(0);
                return buffer;
            }
            else
            {
                throw PyTypeError.Create($"a bytes-like object is required, not '{obj.GetTypeName()}'");
            }
        }

        /// <summary>
        /// repr() built-in function - returns a printable representation of an object
        /// </summary>
        private PyObject CallRepr(PyObject[] args, PyDict kwargs = null)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"repr() takes exactly one argument ({args.Length} given)");

            var obj = args[0];
            
            // Use the object's ToRepr() method, which should provide the canonical string representation
            return new PyString(obj.ToRepr());
        }

        #endregion

        public override string ToString() => $"<built-in function {Name}>";
    }
    
    /// <summary>
    /// Super proxy object that handles method resolution
    /// </summary>
    public class PySuperProxy : PyObject
    {
        public PyType Type { get; }
        public PyObject Object { get; }
        
        public PySuperProxy(PyType type, PyObject obj)
        {
            Type = type;
            Object = obj;
        }
        
        public override PyObject GetAttribute(string name)
        {
            Console.WriteLine($"🔍 PySuperProxy.GetAttribute: Looking for '{name}' in super({Type.Name})");
            #if DEBUG_LOG
            Console.WriteLine($"   Type.BaseTypes: {(Type.BaseTypes != null ? $"[{string.Join(", ", Type.BaseTypes.Select(t => t.Name))}]" : "null")}");
            #endif

            // CPython 3.12: Use MRO to find the method in parent classes
            // Skip the current class and look in its parents
            if (Type.MRO != null && Type.MRO.Count > 1)
            {
                // Check each base type in order (MRO), skipping the current type
                for (int i = 1; i < Type.MRO.Count; i++)
                {
                    var baseType = Type.MRO[i];
                    #if DEBUG_LOG
                    Console.WriteLine($"   → Checking base type: {baseType.Name}");
                    #endif

                    try
                    {
                        var attr = baseType.GetAttribute(name);
                        if (attr != null)
                        {
                            Console.WriteLine($"   ✅ Found '{name}' in {baseType.Name}: {attr.GetType().Name}");

                            // Special handling for methods that need binding
                            if (Object != null)
                            {
                                if (attr is PyFunction function)
                                {
                                    // Check if we're in a metaclass context (Object is a class)
                                    bool isMetaclassContext = Object is PyClass || Object is PyTypeMetaclass;

                                    if (isMetaclassContext && (name == "__new__" || name == "__init__" || name == "__init_subclass__"))
                                    {
                                        // Return unbound function for class methods in metaclass context
                                        #if DEBUG_LOG
                                        Console.WriteLine($"   🔧 Metaclass context: returning unbound {name}");
                                        #endif
                                        return function;
                                    }
                                    else
                                    {
                                        // Regular instance method binding
                                        #if DEBUG_LOG
                                        Console.WriteLine($"   🔧 Instance context: binding {name} to {Object.GetType().Name}");
                                        #endif
                                        return new PyMethod(Object, function);
                                    }
                                }
                                else if (attr is PyBuiltinMethod builtin)
                                {
                                    // Handle builtin methods like object.__init__
                                    Console.WriteLine($"   🔧 Binding builtin method {name} to {Object.GetType().Name}");
                                    // Convert PyBuiltinMethod to PyFunction for proper binding
                                    var func = new PyFunction(builtin.Name, args => builtin.Call(args, null));
                                    return new PyMethod(Object, func);
                                }
                                else if (attr is PyBuiltinFunction builtinFunc)
                                {
                                    // Handle builtin functions like object.__init__
                                    Console.WriteLine($"   🔧 Binding builtin function {name} to {Object.GetType().Name}");
                                    // Convert PyBuiltinFunction to PyFunction for proper binding
                                    var func = new PyFunction(builtinFunc.Name, args => builtinFunc.Call(args, null));
                                    return new PyMethod(Object, func);
                                }
                            }

                            Console.WriteLine($"🔧 PySuperProxy returning final attr: {attr?.GetType().Name ?? "null"}");
                            return attr;
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
            else
            {
                // CPython 3.12: If no explicit base classes, implicitly inherit from object
                #if DEBUG_LOG
                Console.WriteLine($"   → No base types, checking implicit 'object' base class");
                #endif

                // For object.__init__, return a no-op function (object's __init__ does nothing)
                if (name == "__init__" && Object != null)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ Found implicit object.__init__: returning bound method");
                    #endif

                    // Create a no-op __init__ function that matches object.__init__
                    // We need to wrap PyBuiltinFunction as PyFunction for binding
                    var objectInitBuiltin = new PyBuiltinFunction("__init__", args => PyNone.Instance);

                    // Convert PyBuiltinFunction to PyFunction-compatible form
                    var objectInitFunction = new PyFunction("__init__", args => PyNone.Instance);

                    return new PyMethod(Object, objectInitFunction);
                }

                // For other object methods, we could add them here if needed
                // For now, let's see if __init__ is enough
            }

            #if DEBUG_LOG
            Console.WriteLine($"   ❌ '{name}' not found in any parent class");
            #endif
            throw PyAttributeError.Create($"'super' object has no attribute '{name}'");
        }
        
        public override string ToString() => $"<super: {Type.Name}, {Object}>";
    }
}