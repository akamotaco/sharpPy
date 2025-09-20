using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python type 시스템 - C3 선형화 MRO 구현
    /// </summary>
    public class PyType : PyObject
    {
        #region Built-in Type Constants

        // 핵심 기본 타입들 (실제 구현된 것들만)
        public static readonly PyType ObjectType = new PyType("object", new PyType[0]);
        public static readonly PyType TypeType = new PyType("type", new[] { ObjectType });
        
        // 숫자 타입들
        public static readonly PyType IntType = new PyType("int", new[] { ObjectType });
        public static readonly PyType FloatType = new PyType("float", new[] { ObjectType });
        public static readonly PyType BoolType = new PyType("bool", new[] { IntType });
        public static readonly PyType ComplexType = new PyType("complex", new[] { ObjectType });
        
        // 컬렉션 타입들
        public static readonly PyType StrType = new PyType("str", new[] { ObjectType });
        public static readonly PyType BytesType = new PyType("bytes", new[] { ObjectType });
        public static readonly PyType ListType = new PyType("list", new[] { ObjectType });
        public static readonly PyType TupleType = new PyType("tuple", new[] { ObjectType });
        public static readonly PyType DictType = new PyType("dict", new[] { ObjectType });
        public static readonly PyType SetType = new PyType("set", new[] { ObjectType });
        public static readonly PyType FrozenSetType = new PyType("frozenset", new[] { ObjectType });
        public static readonly PyType RangeType = new PyType("range", new[] { ObjectType });
        public static readonly PyType SliceType = new PyType("slice", new[] { ObjectType });
        
        // 함수 타입들
        public static readonly PyType FunctionType = new PyType("function", new[] { ObjectType });
        public static readonly PyType MethodType = new PyType("method", new[] { ObjectType });
        public static readonly PyType IteratorType = new PyType("iterator", new[] { ObjectType });
        public static readonly PyType GeneratorType = new PyType("generator", new[] { IteratorType });
        public static readonly PyType CoroutineType = new PyType("coroutine", new[] { ObjectType });
        public static readonly PyType AsyncGeneratorType = new PyType("async_generator", new[] { ObjectType });
        
        // Descriptor 타입들 (데모에서 필요)
        public static readonly PyType PropertyType = new PyType("property", new[] { ObjectType });
        
        // 기타 핵심 타입들
        public static readonly PyType ModuleType = new PyType("module", new[] { ObjectType });
        public static readonly PyType NoneType = new PyType("NoneType", new[] { ObjectType });
        public static readonly PyType NullType = new PyType("NullType", new[] { ObjectType }); // CPython 내부 NULL
        public static readonly PyType GenericAliasType = new PyType("GenericAlias", new[] { ObjectType });

        // 예외 타입 계층 (PyException.cs와 연동)
        public static readonly PyType BaseExceptionType = new PyType("BaseException", new[] { ObjectType });
        public static readonly PyType ExceptionType = new PyType("Exception", new[] { BaseExceptionType });
        public static readonly PyType SystemExitType = new PyType("SystemExit", new[] { BaseExceptionType });
        public static readonly PyType KeyboardInterruptType = new PyType("KeyboardInterrupt", new[] { BaseExceptionType });
        
        public static readonly PyType ArithmeticErrorType = new PyType("ArithmeticError", new[] { ExceptionType });
        public static readonly PyType ZeroDivisionErrorType = new PyType("ZeroDivisionError", new[] { ArithmeticErrorType });
        public static readonly PyType OverflowErrorType = new PyType("OverflowError", new[] { ArithmeticErrorType });
        
        public static readonly PyType LookupErrorType = new PyType("LookupError", new[] { ExceptionType });
        public static readonly PyType IndexErrorType = new PyType("IndexError", new[] { LookupErrorType });
        public static readonly PyType KeyErrorType = new PyType("KeyError", new[] { LookupErrorType });
        
        public static readonly PyType AttributeErrorType = new PyType("AttributeError", new[] { ExceptionType });
        public static readonly PyType TypeErrorType = new PyType("TypeError", new[] { ExceptionType });
        public static readonly PyType ValueErrorType = new PyType("ValueError", new[] { ExceptionType });
        public static readonly PyType NameErrorType = new PyType("NameError", new[] { ExceptionType });
        public static readonly PyType UnboundLocalErrorType = new PyType("UnboundLocalError", new[] { NameErrorType });
        
        public static readonly PyType RuntimeErrorType = new PyType("RuntimeError", new[] { ExceptionType });
        public static readonly PyType NotImplementedErrorType = new PyType("NotImplementedError", new[] { RuntimeErrorType });
        public static readonly PyType RecursionErrorType = new PyType("RecursionError", new[] { RuntimeErrorType });

        public static readonly PyType OSErrorType = new PyType("OSError", new[] { ExceptionType });
        public static readonly PyType FileNotFoundErrorType = new PyType("FileNotFoundError", new[] { OSErrorType });
        
        // Exception Groups (PEP 654)
        public static readonly PyType BaseExceptionGroupType = new PyType("BaseExceptionGroup", new[] { BaseExceptionType });
        public static readonly PyType ExceptionGroupType = new PyType("ExceptionGroup", new[] { BaseExceptionGroupType, ExceptionType });
        
        // Buffer Protocol (PEP 688)
        public static readonly PyType BufferType = new PyType("buffer", new[] { ObjectType });
        public static readonly PyType MemoryViewType = new PyType("memoryview", new[] { ObjectType });
        
        // Closure Support
        public static readonly PyType CellType = new PyType("cell", new[] { ObjectType });
        
        public static readonly PyType ImportErrorType = new PyType("ImportError", new[] { ExceptionType });
        public static readonly PyType ModuleNotFoundErrorType = new PyType("ModuleNotFoundError", new[] { ImportErrorType });
        
        public static readonly PyType SyntaxErrorType = new PyType("SyntaxError", new[] { ExceptionType });
        public static readonly PyType IndentationErrorType = new PyType("IndentationError", new[] { SyntaxErrorType });
        
        public static readonly PyType StopIterationType = new PyType("StopIteration", new[] { ExceptionType });
        public static readonly PyType AssertionErrorType = new PyType("AssertionError", new[] { ExceptionType });
        public static readonly PyType GeneratorExitType = new PyType("GeneratorExit", new[] { BaseExceptionType });


        #endregion

        #region Core Properties

        public string Name { get; }
        public PyType[] BaseTypes { get; }
        public List<PyType> MRO { get; private set; }
        public string Module { get; }

        #endregion

        #region Constructor

        public PyType(string name, PyType[] baseTypes) : this(name, baseTypes, null)
        {
        }

        public PyType(string name, PyType[] baseTypes, string module)
        {
            Name = name;
            BaseTypes = baseTypes ?? new PyType[0];
            Module = module;
            MRO = CalculateC3MRO();
        }

        #endregion

        #region Type Identity

        public override PyType GetPyType() => TypeType;
        public override string GetTypeName() => "type";

        #endregion

        #region C3 Linearization Algorithm

        private List<PyType> CalculateC3MRO()
        {
            if (Name == "object")
            {
                return new List<PyType> { this };
            }

            if (Name == "type")
            {
                return new List<PyType> { this, ObjectType };
            }

            try
            {
                return C3Linearize(this);
            }
            catch (Exception)
            {
                throw new ArgumentException($"Cannot create a consistent method resolution order (MRO) for class {Name}");
            }
        }

        private List<PyType> C3Linearize(PyType cls)
        {
            var result = new List<PyType> { cls };

            if (cls.BaseTypes.Length == 0)
            {
                result.Add(ObjectType);
                return result;
            }

            // 부모 클래스들의 MRO 수집
            var basesMROs = new List<List<PyType>>();
            foreach (var baseType in cls.BaseTypes)
            {
                basesMROs.Add(new List<PyType>(baseType.MRO));
            }

            // 부모 클래스 리스트도 추가
            basesMROs.Add(new List<PyType>(cls.BaseTypes));

            // C3 merge 수행
            var merged = C3Merge(basesMROs);
            result.AddRange(merged);

            return result;
        }

        private List<PyType> C3Merge(List<List<PyType>> sequences)
        {
            var result = new List<PyType>();

            while (true)
            {
                // 빈 시퀀스들 제거
                sequences = sequences.Where(seq => seq.Count > 0).ToList();

                if (sequences.Count == 0)
                    break;

                PyType candidate = null;

                // 좋은 후보 찾기 (다른 시퀀스의 tail에 없는 head)
                foreach (var seq in sequences)
                {
                    var head = seq[0];
                    var isTail = sequences.Any(s => s.Skip(1).Contains(head));

                    if (!isTail)
                    {
                        candidate = head;
                        break;
                    }
                }

                if (candidate == null)
                {
                    throw new ArgumentException("Inconsistent MRO");
                }

                result.Add(candidate);

                // 모든 시퀀스에서 candidate 제거
                foreach (var seq in sequences)
                {
                    if (seq.Count > 0 && seq[0] == candidate)
                    {
                        seq.RemoveAt(0);
                    }
                }
            }

            return result;
        }

        #endregion

        #region Type Methods

        // isinstance/issubclass 지원
        public bool IsSubclassOf(PyType other)
        {
            return MRO.Contains(other);
        }

        // type은 항상 호출 가능 (인스턴스 생성)
        public override bool IsCallable() => true;

        // type 호출 - 인스턴스 생성 또는 타입 조회
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // type(obj) - 객체의 타입 반환
            if (this == TypeType && args.Length == 1)
            {
                return args[0].GetPyType();
            }
            
            // type(name, bases, dict) - 새로운 타입 생성
            if (this == TypeType && args.Length == 3)
            {
                if (args[0] is PyString name && args[1] is PyTuple bases && args[2] is PyDict classDict)
                {
                    var baseTypes = bases.Items.Cast<PyType>().ToArray();
                    // CPython 호환: PyDict의 PyObject 키를 string 키로 변환
                    var stringDict = new Dictionary<string, PyObject>();
                    foreach (var kv in classDict.InternalDict)
                    {
                        if (kv.Key is PyString keyStr)
                        {
                            stringDict[keyStr.Value] = kv.Value;
                        }
                        // 문자열이 아닌 키는 무시 (CPython과 동일한 동작)
                    }
                    return new PyClass(name.Value, baseTypes, stringDict);
                }
                throw PyTypeError.Create("type() arguments must be (name, bases, dict)");
            }
            
            // 일반 타입 호출 - 인스턴스 생성
            return CreateInstance(args);
        }

        // 인스턴스 생성 (기본 구현)
        public virtual PyObject CreateInstance(params PyObject[] args)
        {
            // 예외 타입들에 대한 특별 처리
            string message = args.Length > 0 && args[0] is PyString pyStr ? pyStr.Value : "";

            switch (Name)
            {
                case "BaseException":
                    return new PyBaseException(message);
                case "Exception":
                    return new PyException(message);
                case "ValueError":
                    return new PyValueError(message);
                case "TypeError":
                    return new PyTypeError(message);
                case "AttributeError":
                    return new PyAttributeError(message);
                case "NameError":
                    return new PyNameError(message);
                case "UnboundLocalError":
                    return new PyUnboundLocalError(message);
                case "ArithmeticError":
                    return new PyArithmeticError(message);
                case "ZeroDivisionError":
                    return new PyZeroDivisionError(message);
                case "OverflowError":
                    return new PyOverflowError(message);
                case "LookupError":
                    return new PyLookupError(message);
                case "IndexError":
                    return new PyIndexError(message);
                case "KeyError":
                    return new PyKeyError(message);
                case "RuntimeError":
                    return new PyRuntimeError(message);
                case "NotImplementedError":
                    return new PyNotImplementedError(message);
                case "RecursionError":
                    return new PyRecursionError(message);
                case "ImportError":
                    return new PyImportError(message);
                case "ModuleNotFoundError":
                    return new PyModuleNotFoundError(message);
                case "SyntaxError":
                    return new PySyntaxError(message);
                case "IndentationError":
                    return new PyIndentationError(message);
                case "SystemExit":
                    return new PySystemExit(0);
                case "KeyboardInterrupt":
                    return new PyKeyboardInterrupt();
                case "GeneratorExit":
                    return new PyGeneratorExit();
                case "StopIteration":
                    return new PyStopIteration();
                case "AssertionError":
                    return new PyAssertionError(message);
                case "OSError":
                    return new PyOSError(message);
                case "FileNotFoundError":
                    return new PyFileNotFoundError(message);
                case "BaseExceptionGroup":
                    {
                        // BaseExceptionGroup(message, exceptions) - handle the special constructor
                        if (args.Length >= 2)
                        {
                            string msg = args[0] is PyString msgStr ? msgStr.Value : "";
                            var exceptions = new List<PyException>();

                            if (args[1] is PyList exceptionList)
                            {
                                foreach (var item in exceptionList.Items)
                                {
                                    if (item is PyException exc)
                                        exceptions.Add(exc);
                                }
                            }
                            else if (args[1] is PyTuple exceptionTuple)
                            {
                                foreach (var item in exceptionTuple.Items)
                                {
                                    if (item is PyException exc)
                                        exceptions.Add(exc);
                                }
                            }

                            return new PyBaseExceptionGroup(msg, exceptions);
                        }
                        return new PyBaseExceptionGroup(message, new List<PyException>());
                    }
                case "ExceptionGroup":
                    {
                        // ExceptionGroup(message, exceptions) - handle the special constructor
                        if (args.Length >= 2)
                        {
                            string msg = args[0] is PyString msgStr ? msgStr.Value : "";
                            var exceptions = new List<PyException>();

                            if (args[1] is PyList exceptionList)
                            {
                                foreach (var item in exceptionList.Items)
                                {
                                    if (item is PyException exc)
                                        exceptions.Add(exc);
                                }
                            }
                            else if (args[1] is PyTuple exceptionTuple)
                            {
                                foreach (var item in exceptionTuple.Items)
                                {
                                    if (item is PyException exc)
                                        exceptions.Add(exc);
                                }
                            }

                            return new PyExceptionGroup(msg, exceptions);
                        }
                        return new PyExceptionGroup(message, new List<PyException>());
                    }
            }

            // 내장 타입들에 대한 특별 처리 (타입 변환) - PyBuiltinFunction 위임
            var builtinFunc = new PyBuiltinFunction(Name);
            return builtinFunc.Call(args, null);
        }

        // Special method lookup (MRO 기반)
        public PyObject LookupSpecial(string name)
        {
            foreach (var mroType in MRO)
            {
                if (mroType is PyClass customType && customType.ClassDict.ContainsKey(name))
                {
                    return customType.ClassDict[name];
                }
            }
            return null;
        }

        #endregion

        #region Type Attributes

        public override PyObject GetAttribute(string name)
        {
            // Special handling for str type methods
            if (this == StrType)
            {
                switch (name)
                {
                    case "upper":
                        return new PyBuiltinFunction("str.upper", args => {
                            if (args.Length != 1) throw PyTypeError.Create($"upper() takes exactly one argument ({args.Length} given)");
                            if (args[0] is PyString str) return new PyString(str.Value.ToUpperInvariant());
                            throw PyTypeError.Create("descriptor 'upper' for 'str' objects doesn't apply to a '" + args[0].GetTypeName() + "' object");
                        });
                    case "lower":
                        return new PyBuiltinFunction("str.lower", args => {
                            if (args.Length != 1) throw PyTypeError.Create($"lower() takes exactly one argument ({args.Length} given)");
                            if (args[0] is PyString str) return new PyString(str.Value.ToLowerInvariant());
                            throw PyTypeError.Create("descriptor 'lower' for 'str' objects doesn't apply to a '" + args[0].GetTypeName() + "' object");
                        });
                    case "title":
                        return new PyBuiltinFunction("str.title", args => {
                            if (args.Length != 1) throw PyTypeError.Create($"title() takes exactly one argument ({args.Length} given)");
                            if (args[0] is PyString str) return str.Title();
                            throw PyTypeError.Create("descriptor 'title' for 'str' objects doesn't apply to a '" + args[0].GetTypeName() + "' object");
                        });
                }
            }

            // CPython 3.12: object type의 기본 메서드들
            if (this == ObjectType)
            {
                switch (name)
                {
                    case "__init__":
                        return new PyBuiltinMethod("__init__", (self, args) =>
                        {
                            // object.__init__() does nothing and returns None
                            return PyNone.Instance;
                        });
                }
            }

            switch (name)
            {
                case "__name__":
                    return new PyString(Name);
                case "__bases__":
                    return new PyTuple(BaseTypes.Cast<PyObject>().ToArray());
                case "__mro__":
                    return new PyTuple(MRO.Cast<PyObject>().ToArray());
                case "__new__":
                    // Return the built-in type.__new__ method
                    return new PyBuiltinFunction("type.__new__");
                default:
                    return base.GetAttribute(name);
            }
        }

        /// <summary>
        /// PEP 698: 클래스의 모든 속성 이름 반환 (@override 검증용)
        /// </summary>
        public virtual IEnumerable<string> GetAttributeNames()
        {
            var names = new HashSet<string>();
            
            // 기본 속성들
            names.Add("__name__");
            names.Add("__bases__");
            names.Add("__mro__");
            
            // MRO를 통해 모든 속성 수집
            foreach (var mroType in MRO)
            {
                if (mroType is PyClass customType)
                {
                    foreach (var key in customType.ClassDict.Keys)
                    {
                        names.Add(key);
                    }
                }
            }
            
            return names;
        }

        #endregion

        #region Generic Type Support

        /// <summary>
        /// CPython 3.12 compatible generic type subscripting: list[int], tuple[str, int], etc.
        /// </summary>
        public override PyObject GetItem(PyObject key)
        {
            // For built-in generic types like list, tuple, dict, etc.
            if (Name == "list" || Name == "tuple" || Name == "dict" || Name == "set" || Name == "frozenset")
            {
                // Create a generic alias representation - for now just return the type itself
                // In a full implementation, this would return types.GenericAlias(this, key)
                return new PyGenericAlias(this, key);
            }
            
            // Not a generic type
            throw PyTypeError.Create($"'{Name}' object is not subscriptable");
        }

        #endregion

        #region String Representation

        public override string ToRepr() => !string.IsNullOrEmpty(Module) ? $"<class '{Module}.{Name}'>" : $"<class '{Name}'>";
        public override string ToStr() => !string.IsNullOrEmpty(Module) ? $"<class '{Module}.{Name}'>" : $"<class '{Name}'>";

        #endregion

        #region Hash and Comparison

        public override bool PyBoolValue() => true;

        protected override int GetDefaultHash()
        {
            return Name.GetHashCode();
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return PyBool.FromBool(ReferenceEquals(this, other));
        }

        #endregion

        #region Debug Support

        public void PrintMRO()
        {
            Console.WriteLine($"{Name} MRO: [{string.Join(", ", MRO.Select(t => t.Name))}]");
        }

        #endregion
    }
}