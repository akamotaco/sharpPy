using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python type 시스템 - C3 선형화 MRO 구현
    /// </summary>
    public class PyType : PyObject
    {
        #region Type Kind (CPython equivalent: fast type identification)

        /// <summary>
        /// Built-in type identification for fast comparison (CPython uses pointer comparison)
        /// </summary>
        internal enum TypeKind
        {
            Generic,    // 일반 타입 (사용자 정의 클래스 등)
            Object,     // object 타입
            Type,       // type 타입
            Str,        // str 타입
            // 필요시 추가: Int, Float, List, Dict, etc.
        }

        /// <summary>
        /// This type's kind (readonly for safety, set only in constructor)
        /// </summary>
        private readonly TypeKind _kind;

        #endregion

        #region Descriptor Tables (CPython tp_methods, tp_getset, tp_members)

        /// <summary>
        /// CPython 3.12 호환: 타입의 딕셔너리 (tp_dict)
        /// All types (builtin and user-defined) use this unified storage
        /// </summary>
        public Dictionary<string, PyObject> TypeDict { get; protected set; }

        #endregion

        #region Built-in Type Constants

        // 핵심 기본 타입들 (실제 구현된 것들만)
        public static readonly PyType ObjectType = new PyType("object", new PyType[0], null, TypeKind.Object);
        public static readonly PyType TypeType = new PyType("type", new[] { ObjectType }, null, TypeKind.Type);

        // 숫자 타입들
        public static readonly PyType IntType = new PyType("int", new[] { ObjectType });
        public static readonly PyType FloatType = new PyType("float", new[] { ObjectType });
        public static readonly PyType BoolType = new PyType("bool", new[] { IntType });
        public static readonly PyType ComplexType = new PyType("complex", new[] { ObjectType });

        // 컬렉션 타입들
        public static readonly PyType StrType = new PyType("str", new[] { ObjectType }, null, TypeKind.Str);
        public static readonly PyType BytesType = new PyType("bytes", new[] { ObjectType });
        public static readonly PyType BytearrayType = new PyType("bytearray", new[] { ObjectType });
        public static readonly PyType MemoryViewType = new PyType("memoryview", new[] { ObjectType });
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
        public static readonly PyType StaticMethodType = new PyType("staticmethod", new[] { ObjectType });
        public static readonly PyType ClassMethodType = new PyType("classmethod", new[] { ObjectType });
        
        // 기타 핵심 타입들
        public static readonly PyType ModuleType = new PyType("module", new[] { ObjectType });
        public static readonly PyType NoneType = new PyType("NoneType", new[] { ObjectType });
        public static readonly PyType EllipsisType = new PyType("ellipsis", new[] { ObjectType }); // CPython Objects/sliceobject.c
        public static readonly PyType NullType = new PyType("NullType", new[] { ObjectType }); // CPython 내부 NULL
        public static readonly PyType GenericAliasType = new PyType("GenericAlias", new[] { ObjectType });
        public static readonly PyType MappingProxyType = new PyType("mappingproxy", new[] { ObjectType }); // CPython Objects/descrobject.c

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
        public static readonly PyType EOFErrorType = new PyType("EOFError", new[] { ExceptionType });

        // Exception Groups (PEP 654)
        public static readonly PyType BaseExceptionGroupType = new PyType("BaseExceptionGroup", new[] { BaseExceptionType });
        public static readonly PyType ExceptionGroupType = new PyType("ExceptionGroup", new[] { BaseExceptionGroupType, ExceptionType });
        
        // Buffer Protocol (PEP 688)
        public static readonly PyType BufferType = new PyType("buffer", new[] { ObjectType });
        
        // Closure Support
        public static readonly PyType CellType = new PyType("cell", new[] { ObjectType });
        
        public static readonly PyType ImportErrorType = new PyType("ImportError", new[] { ExceptionType });
        public static readonly PyType ModuleNotFoundErrorType = new PyType("ModuleNotFoundError", new[] { ImportErrorType });
        
        public static readonly PyType SyntaxErrorType = new PyType("SyntaxError", new[] { ExceptionType });
        public static readonly PyType IndentationErrorType = new PyType("IndentationError", new[] { SyntaxErrorType });
        
        public static readonly PyType StopIterationType = new PyType("StopIteration", new[] { ExceptionType });
        public static readonly PyType AssertionErrorType = new PyType("AssertionError", new[] { ExceptionType });
        public static readonly PyType GeneratorExitType = new PyType("GeneratorExit", new[] { BaseExceptionType });

        // Warning hierarchy (CPython 3.12)
        public static readonly PyType WarningType = new PyType("Warning", new[] { ExceptionType });
        public static readonly PyType UserWarningType = new PyType("UserWarning", new[] { WarningType });
        public static readonly PyType DeprecationWarningType = new PyType("DeprecationWarning", new[] { WarningType });
        public static readonly PyType PendingDeprecationWarningType = new PyType("PendingDeprecationWarning", new[] { WarningType });
        public static readonly PyType SyntaxWarningType = new PyType("SyntaxWarning", new[] { WarningType });
        public static readonly PyType RuntimeWarningType = new PyType("RuntimeWarning", new[] { WarningType });
        public static readonly PyType FutureWarningType = new PyType("FutureWarning", new[] { WarningType });
        public static readonly PyType ImportWarningType = new PyType("ImportWarning", new[] { WarningType });
        public static readonly PyType UnicodeWarningType = new PyType("UnicodeWarning", new[] { WarningType });
        public static readonly PyType BytesWarningType = new PyType("BytesWarning", new[] { WarningType });
        public static readonly PyType ResourceWarningType = new PyType("ResourceWarning", new[] { WarningType });

        // Typing system types (PEP 484, 585, 695)
        public static readonly PyType UnionType = new PyType("Union", new[] { ObjectType });
        public static readonly PyType TypeVarType = new PyType("TypeVar", new[] { ObjectType });
        public static readonly PyType OptionalType = new PyType("Optional", new[] { ObjectType });
        public static readonly PyType CallableType = new PyType("Callable", new[] { ObjectType });
        public static readonly PyType AnyType = new PyType("Any", new[] { ObjectType });
        public static readonly PyType NoReturnType = new PyType("NoReturn", new[] { ObjectType });
        public static readonly PyType ProtocolType = new PyType("Protocol", new[] { ObjectType });

        #endregion

        #region Core Properties

        public string Name { get; }
        public PyType[] BaseTypes { get; }
        public List<PyType> MRO { get; private set; }
        public string Module { get; }

        #endregion

        #region Constructor

        public PyType(string name, PyType[] baseTypes) : this(name, baseTypes, null, TypeKind.Generic)
        {
        }

        public PyType(string name, PyType[] baseTypes, string module) : this(name, baseTypes, module, TypeKind.Generic)
        {
        }

        // Internal constructor: TypeKind는 내부에서만 사용 (CPython equivalent: tp_flags 설정)
        internal PyType(string name, PyType[] baseTypes, string module, TypeKind kind)
        {
            Name = name;
            BaseTypes = baseTypes ?? new PyType[0];
            Module = module;
            _kind = kind;  // readonly 필드는 생성자에서만 설정 가능
            MRO = CalculateC3MRO();

            // CPython 3.12: Initialize tp_dict (unified type dictionary)
            TypeDict = new Dictionary<string, PyObject>();

            // 타입별 descriptor 초기화 (Phase 3: Directly populates TypeDict)
            InitializeDescriptors();
        }

        #endregion

        #region Type Identity

        public override PyType GetPyType()
        {
            // CPython 3.12: type(int) is type → True
            // All types (PyType instances) return the type metaclass
            // Use PyTypeMetaclass.Instance instead of TypeType to ensure singleton
            return PyTypeMetaclass.Instance;
        }

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

            // CPython 3.12: Special handling for descriptor types
            if (this == StaticMethodType)
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"staticmethod expected 1 argument, got {args.Length}");
                if (args[0] == null || !args[0].IsCallable())
                    throw PyTypeError.Create("staticmethod() argument must be callable");
                if (args[0] is PyFunction func)
                    return new PyStaticmethod(func);
                throw PyTypeError.Create("staticmethod() currently only supports PyFunction objects");
            }

            if (this == ClassMethodType)
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"classmethod expected 1 argument, got {args.Length}");
                if (args[0] == null || !args[0].IsCallable())
                    throw PyTypeError.Create("classmethod() argument must be callable");
                if (args[0] is PyFunction func)
                    return new PyClassmethod(func);
                throw PyTypeError.Create("classmethod() currently only supports PyFunction objects");
            }

            if (this == PropertyType)
            {
                // property([fget[, fset[, fdel[, doc]]]])
                PyObject getter = args.Length > 0 ? args[0] : null;
                PyObject setter = args.Length > 1 ? args[1] : null;
                PyObject deleter = args.Length > 2 ? args[2] : null;
                PyObject doc = args.Length > 3 ? args[3] : null;
                return new PyProperty(getter, setter, deleter, doc);
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
                case "object":
                    // object() creates a new basic object instance
                    if (args.Length > 0)
                    {
                        throw PyTypeError.Create("object() takes no arguments");
                    }
                    return new PyInstance(); // Create basic object instance
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
                case "mappingproxy":
                    // CPython 3.12: mappingproxy(dict) - create read-only dict proxy
                    if (args.Length != 1)
                        throw PyTypeError.Create($"mappingproxy expected 1 argument, got {args.Length}");
                    if (args[0] is PyDict dict)
                    {
                        // Convert PyDict to Dictionary<string, PyObject>
                        var stringDict = new Dictionary<string, PyObject>();
                        foreach (var kv in dict.InternalDict)
                        {
                            if (kv.Key is PyString keyStr)
                                stringDict[keyStr.Value] = kv.Value;
                        }
                        return new PyMappingProxy(stringDict);
                    }
                    throw PyTypeError.Create($"mappingproxy() argument must be dict, not '{args[0].GetTypeName()}'");
                case "set":
                    // set() constructor - create a new set
                    if (args.Length == 0)
                        return PySet.Empty;
                    if (args.Length == 1)
                    {
                        var iterable = args[0];
                        var items = new List<PyObject>();
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
                            // Normal termination
                        }
                        return new PySet(items);
                    }
                    throw PyTypeError.Create($"set expected at most 1 arguments ({args.Length} given)");
                case "frozenset":
                    // frozenset() constructor - create a new frozenset
                    if (args.Length == 0)
                        return new PyFrozenSet();
                    if (args.Length == 1)
                    {
                        var iterable = args[0];
                        var items = new List<PyObject>();
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
                            // Normal termination
                        }
                        return new PyFrozenSet(items);
                    }
                    throw PyTypeError.Create($"frozenset expected at most 1 arguments ({args.Length} given)");
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

        #region Descriptor Initialization

        /// <summary>
        /// 타입별 descriptor 초기화 (CPython의 타입 객체 초기화와 유사)
        /// CPython equivalent: fast type identification using enum instead of string comparison
        /// </summary>
        private void InitializeDescriptors()
        {
            // CPython 3.12 호환: 각 타입의 tp_methods, tp_getset 초기화
            // readonly TypeKind를 사용하여 빠른 int 비교 (string 비교보다 훨씬 빠름)
            switch (_kind)
            {
                case TypeKind.Str:
                    InitializeStrTypeDescriptors();
                    break;

                case TypeKind.Object:
                    InitializeObjectTypeDescriptors();
                    break;

                case TypeKind.Type:
                    InitializeTypeTypeDescriptors();
                    break;

                case TypeKind.Generic:
                    // 특정 내장 타입들에 대한 descriptor 초기화
                    if (Name == "set")
                        InitializeSetTypeDescriptors();
                    else if (Name == "frozenset")
                        InitializeFrozenSetTypeDescriptors();
                    else if (Name == "list")
                        InitializeListTypeDescriptors();
                    else if (Name == "tuple")
                        InitializeTupleTypeDescriptors();
                    else if (Name == "dict")
                        InitializeDictTypeDescriptors();
                    break;
            }
        }

        /// <summary>
        /// str 타입의 descriptor 테이블 초기화 (CPython unicodeobject.c 참조)
        /// 실제 descriptor 등록은 PyString.InitializeStringDescriptors()에서 수행됨
        /// </summary>
        private void InitializeStrTypeDescriptors()
        {
            // PyString.InitializeStringDescriptors()에서 모든 str descriptor를 등록하므로
            // 여기서는 아무것도 하지 않음 (중복 방지)
        }

        /// <summary>
        /// object 타입의 descriptor 테이블 초기화 (CPython typeobject.c 참조)
        /// Phase 3: Directly populate TypeDict instead of Descriptors
        /// </summary>
        private void InitializeObjectTypeDescriptors()
        {
            var objectType = this;

            // object.__init__() - CPython object_init
            TypeDict["__init__"] = new PyMethodDescriptor(
                "__init__",
                objectType,
                (self, args, kwargs) => {
                    // object.__init__() does nothing and returns None
                    return PyNone.Instance;
                },
                minArgs: 0,
                maxArgs: int.MaxValue
            );

            // object.__new__(cls) - CPython object_new
            TypeDict["__new__"] = new PyMethodDescriptor(
                "__new__",
                objectType,
                (self, args, kwargs) => {
                    // __new__ is a static method, first arg is the class
                    if (args.Length < 1)
                        throw PyTypeError.Create("__new__() missing 1 required positional argument: 'cls'");

                    // Create a basic PyInstance
                    return new PyInstance();
                },
                minArgs: 1,
                maxArgs: int.MaxValue
            );
        }

        /// <summary>
        /// type 타입의 descriptor 테이블 초기화 (CPython typeobject.c 참조)
        /// Phase 3: Directly populate TypeDict instead of Descriptors
        /// </summary>
        private void InitializeTypeTypeDescriptors()
        {
            var typeType = this;

            // type.__name__ - CPython type_name / type_set_name
            TypeDict["__name__"] = new PyGetSetDescriptor(
                "__name__",
                typeType,
                getter: self => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__name__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return new PyString(type.Name);
                }
            );

            // type.__bases__ - CPython type_get_bases / type_set_bases
            TypeDict["__bases__"] = new PyGetSetDescriptor(
                "__bases__",
                typeType,
                getter: self => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__bases__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return new PyTuple(type.BaseTypes.Cast<PyObject>().ToArray());
                }
            );

            // type.__mro__ - CPython type_mro (read-only)
            TypeDict["__mro__"] = new PyGetSetDescriptor(
                "__mro__",
                typeType,
                getter: self => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__mro__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return new PyTuple(type.MRO.Cast<PyObject>().ToArray());
                }
            );

            // type.__dict__ - CPython type_dict (read-only, returns mappingproxy)
            TypeDict["__dict__"] = new PyGetSetDescriptor(
                "__dict__",
                typeType,
                getter: self => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__dict__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // CPython 3.12: Build type's namespace dictionary
                    var typeDict = new Dictionary<string, PyObject>();

                    // Add __name__, __bases__, __mro__
                    typeDict["__name__"] = new PyString(type.Name);
                    typeDict["__bases__"] = new PyTuple(type.BaseTypes.Cast<PyObject>().ToArray());
                    typeDict["__mro__"] = new PyTuple(type.MRO.Cast<PyObject>().ToArray());

                    // CPython 3.12: Add descriptors from MRO (inherited descriptors)
                    // This ensures int.__dict__ includes __new__ from object
                    foreach (var mroType in type.MRO)
                    {
                        // Phase 3: Use TypeDict instead of Descriptors
                        if (mroType.TypeDict != null)
                        {
                            foreach (var kv in mroType.TypeDict)
                            {
                                if (!typeDict.ContainsKey(kv.Key))
                                    typeDict[kv.Key] = kv.Value;
                            }
                        }
                    }

                    // For PyClass, add attributes from ClassDict
                    if (type is PyClass customType)
                    {
                        foreach (var kv in customType.ClassDict)
                        {
                            if (!typeDict.ContainsKey(kv.Key))
                                typeDict[kv.Key] = kv.Value;
                        }
                    }

                    // Return as read-only mappingproxy
                    return new PyMappingProxy(typeDict);
                }
            );

            // type.__new__ - CPython type_new
            TypeDict["__new__"] = new PyMethodDescriptor(
                "__new__",
                typeType,
                (self, args, kwargs) => {
                    // CPython 3.12: type.__new__(metacls, name, bases, namespace)
                    // args[0] = metacls (the class to create, usually type or a subclass)
                    // args[1] = name (str)
                    // args[2] = bases (tuple)
                    // args[3] = namespace (dict)

                    if (args.Length < 4)
                        throw PyTypeError.Create($"type.__new__() takes exactly 4 arguments ({args.Length} given)");

                    var metacls = args[0];
                    var nameArg = args[1];
                    var basesArg = args[2];
                    var namespaceArg = args[3];

                    // Validate arguments
                    if (nameArg is not PyString name)
                        throw PyTypeError.Create($"type.__new__() argument 2 must be str, not {nameArg.GetTypeName()}");

                    if (basesArg is not PyTuple bases)
                        throw PyTypeError.Create($"type.__new__() argument 3 must be tuple, not {basesArg.GetTypeName()}");

                    if (namespaceArg is not PyDict classDict)
                        throw PyTypeError.Create($"type.__new__() argument 4 must be dict, not {namespaceArg.GetTypeName()}");

                    // Convert bases tuple to PyType array
                    var baseTypes = new List<PyType>();
                    foreach (var baseItem in bases.Items)
                    {
                        if (baseItem is PyType baseType)
                            baseTypes.Add(baseType);
                        else
                            throw PyTypeError.Create($"bases must be types, not {baseItem.GetTypeName()}");
                    }

                    // If no bases specified, default to object
                    if (baseTypes.Count == 0)
                        baseTypes.Add(ObjectType);

                    // Convert namespace dict to string dictionary
                    var stringDict = new Dictionary<string, PyObject>();
                    foreach (var kv in classDict.InternalDict)
                    {
                        if (kv.Key is PyString keyStr)
                        {
                            stringDict[keyStr.Value] = kv.Value;
                        }
                        // Non-string keys are ignored (same as CPython)
                    }

                    // Create new class (CPython equivalent: type_new in typeobject.c)
                    var newClass = new PyClass(name.Value, baseTypes.ToArray(), stringDict);

                    // If metacls is not type, we might need to set a custom metaclass
                    // For now, PyClass always uses type as metaclass (standard behavior)
                    return newClass;
                },
                minArgs: 4,
                maxArgs: 4
            );

            // type.__repr__ - CPython type_repr (from PyTypeMetaclass)
            TypeDict["__repr__"] = new PyMethodDescriptor(
                "__repr__",
                typeType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"__repr__() takes no arguments ({args.Length} given)");
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__repr__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return new PyString($"<class '{type.Name}'>");
                },
                minArgs: 0,
                maxArgs: 0
            );

            // type.__str__ - CPython type_repr (same as __repr__ for type)
            TypeDict["__str__"] = new PyMethodDescriptor(
                "__str__",
                typeType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"__str__() takes no arguments ({args.Length} given)");
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__str__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return new PyString($"<class '{type.Name}'>");
                },
                minArgs: 0,
                maxArgs: 0
            );

            // type.__format__ - CPython 3.12 (defaults to __str__)
            TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__",
                typeType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__format__() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__format__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    // format_spec is args[0], but for type objects we just return str()
                    return new PyString($"<class '{type.Name}'>");
                },
                minArgs: 1,
                maxArgs: 1
            );

            // type.__reduce_ex__ - CPython 3.12 (pickle support)
            TypeDict["__reduce_ex__"] = new PyMethodDescriptor(
                "__reduce_ex__",
                typeType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__reduce_ex__() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__reduce_ex__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    // Return (type, (type.__name__,))
                    return new PyTuple(new PyObject[] {
                        TypeType,
                        new PyTuple(new PyObject[] { new PyString(type.Name) })
                    });
                },
                minArgs: 1,
                maxArgs: 1
            );
        }

        /// <summary>
        /// set 타입의 descriptor 테이블 초기화 (CPython Objects/setobject.c 참조)
        /// Phase 3: Directly populate TypeDict instead of Descriptors
        /// </summary>
        private void InitializeSetTypeDescriptors()
        {
            var setType = this;

            // set.add(elem)
            TypeDict["add"] = new PyMethodDescriptor(
                "add",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"add() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'add' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Add(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.remove(elem)
            TypeDict["remove"] = new PyMethodDescriptor(
                "remove",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"remove() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'remove' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Remove(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.discard(elem)
            TypeDict["discard"] = new PyMethodDescriptor(
                "discard",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"discard() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'discard' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Discard(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.pop()
            TypeDict["pop"] = new PyMethodDescriptor(
                "pop",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"pop() takes no arguments ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'pop' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Pop();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // set.clear()
            TypeDict["clear"] = new PyMethodDescriptor(
                "clear",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"clear() takes no arguments ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'clear' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Clear();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // set.copy()
            TypeDict["copy"] = new PyMethodDescriptor(
                "copy",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"copy() takes no arguments ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'copy' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Copy();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // set.update(other)
            TypeDict["update"] = new PyMethodDescriptor(
                "update",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"update() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'update' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Update(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.union(other)
            TypeDict["union"] = new PyMethodDescriptor(
                "union",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"union() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'union' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Union(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.intersection(other)
            TypeDict["intersection"] = new PyMethodDescriptor(
                "intersection",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"intersection() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'intersection' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Intersection(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.difference(other)
            TypeDict["difference"] = new PyMethodDescriptor(
                "difference",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"difference() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'difference' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.Difference(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.symmetric_difference(other)
            TypeDict["symmetric_difference"] = new PyMethodDescriptor(
                "symmetric_difference",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"symmetric_difference() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'symmetric_difference' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.SymmetricDifference(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.issubset(other)
            TypeDict["issubset"] = new PyMethodDescriptor(
                "issubset",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"issubset() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'issubset' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.IsSubset(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.issuperset(other)
            TypeDict["issuperset"] = new PyMethodDescriptor(
                "issuperset",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"issuperset() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'issuperset' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.IsSuperset(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // set.isdisjoint(other)
            TypeDict["isdisjoint"] = new PyMethodDescriptor(
                "isdisjoint",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"isdisjoint() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'isdisjoint' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.IsDisjoint(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );
        }

        /// <summary>
        /// frozenset 타입의 descriptor 테이블 초기화
        /// </summary>
        private void InitializeFrozenSetTypeDescriptors()
        {
            // TODO: frozenset 메서드들 구현
        }

        /// <summary>
        /// list 타입의 descriptor 테이블 초기화
        /// </summary>
        private void InitializeListTypeDescriptors()
        {
            // TODO: list 메서드들 구현
        }

        /// <summary>
        /// tuple 타입의 descriptor 테이블 초기화
        /// </summary>
        private void InitializeTupleTypeDescriptors()
        {
            // TODO: tuple 메서드들 구현
        }

        /// <summary>
        /// dict 타입의 descriptor 테이블 초기화 (CPython Objects/dictobject.c 참조)
        /// Phase 3: Directly populate TypeDict instead of Descriptors
        /// </summary>
        private void InitializeDictTypeDescriptors()
        {
            var dictType = this;

            // dict.keys()
            TypeDict["keys"] = new PyMethodDescriptor(
                "keys",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"keys() takes no arguments ({args.Length} given)");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'keys' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    return dict.Keys();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // dict.values()
            TypeDict["values"] = new PyMethodDescriptor(
                "values",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"values() takes no arguments ({args.Length} given)");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'values' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    return dict.Values();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // dict.items()
            TypeDict["items"] = new PyMethodDescriptor(
                "items",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"items() takes no arguments ({args.Length} given)");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'items' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    return dict.Items();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // dict.get(key, default=None)
            TypeDict["get"] = new PyMethodDescriptor(
                "get",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"get() takes from 1 to 2 positional arguments but {args.Length} were given");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'get' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    var key = args[0];
                    var defaultValue = args.Length > 1 ? args[1] : PyNone.Instance;
                    return dict.Get(key, defaultValue);
                },
                minArgs: 1,
                maxArgs: 2
            );

            // dict.pop(key, default)
            TypeDict["pop"] = new PyMethodDescriptor(
                "pop",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"pop() takes from 1 to 2 positional arguments but {args.Length} were given");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'pop' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    var key = args[0];
                    var defaultValue = args.Length > 1 ? args[1] : null;
                    return dict.Pop(key, defaultValue);
                },
                minArgs: 1,
                maxArgs: 2
            );

            // dict.popitem()
            TypeDict["popitem"] = new PyMethodDescriptor(
                "popitem",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"popitem() takes no arguments ({args.Length} given)");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'popitem' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    return dict.PopItem();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // dict.clear()
            TypeDict["clear"] = new PyMethodDescriptor(
                "clear",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"clear() takes no arguments ({args.Length} given)");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'clear' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    return dict.Clear();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // dict.copy()
            TypeDict["copy"] = new PyMethodDescriptor(
                "copy",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"copy() takes no arguments ({args.Length} given)");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'copy' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    return dict.Copy();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // dict.update(other)
            TypeDict["update"] = new PyMethodDescriptor(
                "update",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"update() takes exactly one argument ({args.Length} given)");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'update' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    return dict.Update(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // dict.setdefault(key, default=None)
            TypeDict["setdefault"] = new PyMethodDescriptor(
                "setdefault",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"setdefault() takes from 1 to 2 positional arguments but {args.Length} were given");

                    // CPython 3.12: Accept dict subclasses (isinstance check)
                    PyDict dict;
                    if (self is PyDict d)
                    {
                        dict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsInstance(PyType.DictType))
                    {
                        dict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create("descriptor 'setdefault' for 'dict' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    }

                    var key = args[0];
                    var defaultValue = args.Length > 1 ? args[1] : PyNone.Instance;
                    return dict.SetDefault(key, defaultValue);
                },
                minArgs: 1,
                maxArgs: 2
            );

            // dict.fromkeys(seq, value=None) - static method
            TypeDict["fromkeys"] = new PyMethodDescriptor(
                "fromkeys",
                dictType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"fromkeys() takes from 1 to 2 positional arguments but {args.Length} were given");
                    var keys = args[0];
                    var value = args.Length > 1 ? args[1] : PyNone.Instance;
                    return PyDict.FromKeys(keys, value);
                },
                minArgs: 1,
                maxArgs: 2
            );

            // dict.__init__ - CPython 3.12: dict.__init__ can accept optional args and kwargs
            TypeDict["__init__"] = new PyMethodDescriptor(
                "__init__",
                dictType,
                (self, args, kwargs) => {
                    // For dict subclasses (like _EnumDict), just return None
                    return PyNone.Instance;
                },
                minArgs: 0,
                maxArgs: int.MaxValue,
                acceptsKwargs: true
            );
        }

        #endregion

        #region Type Attributes

        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12 호환: type_getattro() 구현
            // Equivalent to _Py_type_getattro_impl in Objects/typeobject.c:4800

            // Step 1: Look for attribute in metatype (Py_TYPE(type))
            var metatype = GetPyType(); // For PyType, this is PyTypeMetaclass.Instance
            PyObject metaAttribute = null;
            IDescriptor metaGet = null;

            // CPython: meta_attribute = _PyType_Lookup(metatype, name)
            if (metatype is PyClass metaclass)
            {
                if (metaclass.ClassDict.TryGetValue(name, out metaAttribute))
                {
                    // Check if it's a descriptor
                    if (metaAttribute is IDescriptor descriptor)
                    {
                        metaGet = descriptor;

                        // CPython: if (meta_get != NULL && PyDescr_IsData(meta_attribute))
                        if (metaGet.IsDataDescriptor())
                        {
                            // Data descriptors on metatype have highest priority
                            // Call descriptor.__get__(self, type(self))
                            return metaGet.Get(this, metatype);
                        }
                    }
                }
            }

            // Step 2: Look in tp_dict of this type (and its bases via MRO)
            // CPython: attribute = _PyType_Lookup(type, name)

            // Phase 3: Check TypeDict (unified storage)
            if (TypeDict != null && TypeDict.TryGetValue(name, out var typeDictAttr))
            {
                // Found in TypeDict - check if it's a descriptor
                if (typeDictAttr is IDescriptor localDescriptor)
                {
                    // CPython 3.12: NULL 2nd argument indicates the descriptor was
                    // found on the target object itself (or a base)
                    return localDescriptor.Get(null, this);
                }
                return typeDictAttr;
            }

            // Check PyClass.GetTypeAttribute() for wrapper descriptors
            var typeAttr = SharpPy.PyClass.GetTypeAttribute(this, name);
            if (typeAttr != null)
            {
                if (typeAttr is IDescriptor localDescriptor)
                {
                    return localDescriptor.Get(null, this);
                }
                return typeAttr;
            }

            // Step 3: Use non-data descriptor from metatype (if found in step 1)
            // CPython: if (meta_get != NULL) { res = meta_get(...); }
            if (metaGet != null)
            {
                return metaGet.Get(this, metatype);
            }

            // Step 4: Return ordinary attribute from metatype
            // CPython: if (meta_attribute != NULL) { return meta_attribute; }
            if (metaAttribute != null)
            {
                return metaAttribute;
            }

            // Step 5: Give up - attribute not found
            // CPython: PyErr_Format(PyExc_AttributeError, ...)
            return GenericGetAttribute(name);
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

        public override PyString ToRepr() => new PyString(!string.IsNullOrEmpty(Module) ? $"<class '{Module}.{Name}'>" : $"<class '{Name}'>");
        public override PyString ToStr() => new PyString(!string.IsNullOrEmpty(Module) ? $"<class '{Module}.{Name}'>" : $"<class '{Name}'>");

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

        #region Union Type Support (PEP 585)

        /// <summary>
        /// Union type operator support: int | str -> Union[int, str]
        /// </summary>
        public override PyObject BitwiseOr(PyObject other)
        {
            // Both operands should be types for Union
            if (other is PyType otherType)
            {
                return new PyUnionType(new PyObject[] { this, otherType });
            }
            else if (other is PyUnionType unionType)
            {
                // Type | Union -> extend Union
                var newTypes = new List<PyObject> { this };
                newTypes.AddRange(unionType.Args);
                return new PyUnionType(newTypes.ToArray());
            }
            else if (other is PyBuiltinType builtinType)
            {
                return new PyUnionType(new PyObject[] { this, builtinType });
            }

            // Fall back to base implementation for non-type objects
            return base.BitwiseOr(other);
        }

        #endregion

        #region Debug Support

        public void PrintMRO()
        {
            Console.WriteLine($"{Name} MRO: [{string.Join(", ", MRO.Select(t => t.Name))}]");
        }

        #endregion
    }

    /// <summary>
    /// Simple object instance for object() constructor
    /// </summary>
    public class PyInstance : PyObject
    {
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "object";

        public override string ToString() => "<object>";
        public override PyString ToRepr() => new PyString("<object>");

        // CPython 3.12: Handle builtin methods for object instances
        public override PyObject GetAttribute(string name)
        {
            // Check if this is a builtin method from object type
            var objectType = PyType.ObjectType;
            var typeAttr = SharpPy.PyClass.GetTypeAttribute(objectType, name);

            if (typeAttr != null)
            {
                // CPython 3.12: PyBuiltinMethod는 descriptor이므로 Get() 호출
                if (typeAttr is PyBuiltinMethod builtinMethod)
                {
                    return builtinMethod.Get(this, objectType);
                }
                // CPython 3.12: PyBuiltinFunction도 bound method로 변환
                if (typeAttr is PyBuiltinFunction builtinFunction)
                {
                    // Convert PyBuiltinFunction to PyBuiltinMethod
                    var method = new PyBuiltinMethod(builtinFunction.Name, (self, args) => builtinFunction.Call(new[] { self }.Concat(args).ToArray(), null));
                    return new PyBoundBuiltinMethod(this, method);
                }
                if (typeAttr is PyFunction func)
                {
                    return new PyMethod(this, func);
                }
                return typeAttr;
            }

            // Fall back to base implementation
            return base.GetAttribute(name);
        }
    }
}