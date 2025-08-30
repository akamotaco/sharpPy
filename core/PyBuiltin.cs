namespace SharpPy
{
    public class PyBuiltinFunction : PyObject
    {
        public string Name { get; }
        private readonly Func<PyObject[], PyObject>? _implementation;

        public PyBuiltinFunction(string name)
        {
            Name = name;
        }
        
        public PyBuiltinFunction(string name, Func<PyObject[], PyObject> implementation)
        {
            Name = name;
            _implementation = implementation;
        }

        public override string GetTypeName() => "builtin_function_or_method";

        // 내장 함수 호출
        public override PyObject Call(params PyObject[] args)
        {
            // 사용자 정의 구현이 있으면 우선 사용
            if (_implementation != null)
            {
                return _implementation(args);
            }
            
            return Name switch
            {
                "print" => CallPrint(args),
                "len" => CallLen(args),
                "abs" => CallAbs(args),
                "callable" => CallCallable(args),
                "range" => CallRange(args),
                "enumerate" => CallEnumerate(args),
                "zip" => CallZip(args),
                "map" => CallMap(args),
                "filter" => CallFilter(args),
                "sorted" => CallSorted(args),
                "reversed" => CallReversed(args),
                "sum" => CallSum(args),
                "min" => CallMin(args),
                "max" => CallMax(args),
                "any" => CallAny(args),
                "all" => CallAll(args),
                "isinstance" => CallIsInstance(args),
                "issubclass" => CallIsSubclass(args),
                "hasattr" => CallHasAttr(args),
                "getattr" => CallGetAttr(args),
                "setattr" => CallSetAttr(args),
                "delattr" => CallDelAttr(args),
                "type" => CallType(args),
                "id" => CallId(args),
                "hash" => CallHash(args),
                "str" => CallStr(args),
                "int" => CallInt(args),
                "float" => CallFloat(args),
                "bool" => CallBool(args),
                "list" => CallList(args),
                "tuple" => CallTuple(args),
                "dict" => CallDict(args),
                "set" => CallSet(args),
                "iter" => CallIter(args),
                "next" => CallNext(args),
                "round" => CallRound(args),
                "pow" => CallPow(args),
                "divmod" => CallDivmod(args),
                "ord" => CallOrd(args),
                "chr" => CallChr(args),
                "__build_class__" => CallBuildClass(args),
                _ => throw PyNotImplementedError.Create($"Built-in function '{Name}' not implemented")
            };
        }

        // 내장 함수들의 구현
        private PyObject CallPrint(PyObject[] args)
        {
            var output = string.Join(" ", args.Select(arg => arg.ToString()));
            Console.WriteLine(output);
            return PyNone.Instance;
        }

        private PyObject CallLen(PyObject[] args)
        {
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

        private PyObject CallAbs(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"abs() takes exactly one argument ({args.Length} given)");

            if (args[0] is PyInt intVal)
                return new PyInt(Math.Abs(intVal.Value));
            else
                throw PyTypeError.Create($"bad operand type for abs(): '{args[0].GetTypeName()}'");
        }

        private PyObject CallCallable(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"callable() takes exactly one argument ({args.Length} given)");

            return PyBool.FromBool(args[0].IsCallable());
        }

        // === 핵심 내장 함수들 ===

        private PyObject CallRange(PyObject[] args)
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

        private PyObject CallEnumerate(PyObject[] args)
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

        private PyObject CallZip(PyObject[] args)
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

        private PyObject CallMap(PyObject[] args)
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
                    var mappedItem = func.Call(item);
                    result.Add(mappedItem);
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
            }

            return new PyList(result.ToArray());
        }

        private PyObject CallFilter(PyObject[] args)
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
                        : func.Call(item).PyBoolValue();
                    
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

        private PyObject CallSorted(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"sorted expected exactly 1 arguments ({args.Length} given)");

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

            // 간단한 정렬 (비교 가능한 객체만)
            try
            {
                items.Sort((a, b) => 
                {
                    var cmpResult = a.RichCompare(b, PyObject.CompareOp.LT);
                    return ((PyBool)cmpResult).Value ? -1 : 
                           ((PyBool)a.RichCompare(b, PyObject.CompareOp.GT)).Value ? 1 : 0;
                });
            }
            catch
            {
                throw PyTypeError.Create("'<' not supported between instances");
            }

            return new PyList(items.ToArray());
        }

        private PyObject CallReversed(PyObject[] args)
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

        private PyObject CallSum(PyObject[] args)
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

        private PyObject CallMin(PyObject[] args)
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

        private PyObject CallMax(PyObject[] args)
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

        private PyObject CallAny(PyObject[] args)
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

        private PyObject CallAll(PyObject[] args)
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

        private PyObject CallIsInstance(PyObject[] args)
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

        private PyObject CallIsSubclass(PyObject[] args)
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

        private PyObject CallHasAttr(PyObject[] args)
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

        private PyObject CallGetAttr(PyObject[] args)
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

        private PyObject CallSetAttr(PyObject[] args)
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

        private PyObject CallDelAttr(PyObject[] args)
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

        private PyObject CallType(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"type expected exactly 1 arguments ({args.Length} given)");

            return args[0].GetPyType();
        }

        private PyObject CallId(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"id expected exactly 1 arguments ({args.Length} given)");

            return new PyInt(args[0].GetHashCode());
        }

        private PyObject CallHash(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"hash expected exactly 1 arguments ({args.Length} given)");

            return new PyInt(args[0].ToHash());
        }

        // === 타입 변환 함수들 ===

        private PyObject CallStr(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"str expected exactly 1 arguments ({args.Length} given)");

            return new PyString(args[0].ToStr());
        }

        private PyObject CallInt(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"int expected exactly 1 arguments ({args.Length} given)");

            return new PyInt(args[0].ToInt());
        }

        private PyObject CallFloat(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"float expected exactly 1 arguments ({args.Length} given)");

            return new PyFloat(args[0].ToFloat());
        }

        private PyObject CallBool(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"bool expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return PyBool.False;

            return PyBool.FromBool(args[0].PyBoolValue());
        }

        private PyObject CallList(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"list expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyList(new PyObject[0]);

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

            return new PyList(items.ToArray());
        }

        private PyObject CallTuple(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"tuple expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyTuple();

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

            return new PyTuple(items.ToArray());
        }

        private PyObject CallDict(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"dict expected at most 1 arguments ({args.Length} given)");

            if (args.Length == 0)
                return new PyDict();

            // 간단한 구현 - 이터러블에서 키-값 쌍 생성
            var iterable = args[0];
            var result = new PyDict();
            var iterator = iterable.GetIterator();

            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    if (item is PyTuple tuple && tuple.Items.Length == 2)
                    {
                        result.SetItem(tuple.Items[0], tuple.Items[1]);
                    }
                    else
                    {
                        throw PyTypeError.Create("dictionary update sequence element must contain exactly 2 elements");
                    }
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상 종료
            }

            return result;
        }

        private PyObject CallSet(PyObject[] args)
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

        private PyObject CallIter(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"iter expected exactly 1 arguments ({args.Length} given)");

            return args[0].GetIterator();
        }

        private PyObject CallNext(PyObject[] args)
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

        private PyObject CallRound(PyObject[] args)
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

        private PyObject CallPow(PyObject[] args)
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

        private PyObject CallDivmod(PyObject[] args)
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

        private PyObject CallOrd(PyObject[] args)
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

        private PyObject CallChr(PyObject[] args)
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
                _ => throw PyAttributeError.Create($"'builtin_function_or_method' object has no attribute '{name}'")
            };
        }

        private PyObject CallBuildClass(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create($"__build_class__() missing required arguments");
            var func = args[0];
            var name = args[1];
            var bases = new PyType[args.Length - 2];
            for (int i = 2; i < args.Length; i++)
            {
                // Convert PyObject to PyType - simplified approach
                if (args[i] is PyType pyType)
                    bases[i - 2] = pyType;
                else
                    bases[i - 2] = PyType.ObjectType; // Default to object type
            }
            var className = name.ToString();
            
            // CPython처럼 단순하게 클래스 생성만 함 (복잡한 검증 제거)
            return new PyClass(className, bases);
        }

        public override string ToString() => $"<built-in function {Name}>";
    }
}