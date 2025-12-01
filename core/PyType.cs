using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Global method cache for fast attribute lookup (CPython Objects/typeobject.c:_PyType_Lookup)
    /// </summary>
    internal class TypeMethodCache
    {
        /// <summary>
        /// Cache entry structure (version tag + name -> value)
        /// </summary>
        private struct CacheEntry
        {
            public ulong VersionTag;
            public string Name;
            public PyObject Value;
        }

        // CPython uses 2^12 = 4096 entries
        private const int CACHE_SIZE = 4096;
        private readonly CacheEntry[] _cache = new CacheEntry[CACHE_SIZE];

        /// <summary>
        /// Lookup method in cache with version tag validation
        /// CPython reference: Objects/typeobject.c:4650-4774 (_PyType_Lookup)
        /// </summary>
        public PyObject Lookup(PyType type, string name, out bool hit)
        {
            // CPython: MCACHE_HASH_METHOD(type, name)
            // Simple hash: type's version tag XOR name's hash code
            uint hash = (uint)((type.TypeVersionTag ^ (ulong)name.GetHashCode()) % CACHE_SIZE);
            int index = (int)hash;

            ref var entry = ref _cache[index];

            // Cache hit check: version tag + name match
            if (entry.VersionTag == type.TypeVersionTag && entry.Name == name)
            {
                hit = true;
                return entry.Value;
            }

            // Cache miss - need to search MRO
            hit = false;
            PyObject result = SearchMRO(type, name);

            // Update cache with new result
            // CPython: Only cache if MCACHE_CACHEABLE_NAME and version tag is valid
            if (type.TypeVersionTag != 0)
            {
                entry.VersionTag = type.TypeVersionTag;
                entry.Name = name;
                entry.Value = result;
            }

            return result;
        }

        /// <summary>
        /// Search through MRO for attribute (cache miss fallback)
        /// CPython reference: Objects/typeobject.c:4696-4733 (find_name_in_mro)
        /// </summary>
        private PyObject SearchMRO(PyType type, string name)
        {
            foreach (var mroType in type.MRO)
            {
                // Check PyClass.ClassDict first (user-defined classes)
                if (mroType is PyClass customType && customType.ClassDict.TryGetValue(name, out var value))
                {
                    return value;
                }

                // Check PyType.TypeDict (built-in types)
                if (mroType.TypeDict != null && mroType.TypeDict.TryGetValue(name, out var typeValue))
                {
                    return typeValue;
                }

                // Fallback: Check hardcoded descriptors via GetTypeAttribute
                // This handles cases like list.__delitem__ which are defined in PyClass but not in TypeDict
                // Note: Must be in SharpPy.PyClass (forward reference through dynamic lookup)
                try
                {
                    var getTypeAttrMethod = typeof(SharpPy.PyClass).GetMethod(
                        "GetTypeAttribute",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                    );
                    if (getTypeAttrMethod != null)
                    {
                        var result = (PyObject)getTypeAttrMethod.Invoke(null, new object[] { mroType, name });
                        if (result != null)
                            return result;
                    }
                }
                catch
                {
                    // Ignore reflection errors
                }
            }

            return null;
        }
    }

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
            Int,        // int 타입
            // 필요시 추가: Float, List, Dict, etc.
        }

        /// <summary>
        /// This type's kind (readonly for safety, set only in constructor)
        /// </summary>
        private readonly TypeKind _kind;

        #endregion

        #region Type Version Tag System (CPython tp_version_tag)

        /// <summary>
        /// Global method cache (CPython: type_cache global variable)
        /// </summary>
        internal static readonly TypeMethodCache GlobalMethodCache = new TypeMethodCache();

        /// <summary>
        /// Next available version tag (CPython: next_version_tag)
        /// </summary>
        private static ulong _nextVersionTag = 1;

        /// <summary>
        /// This type's version tag for cache invalidation (CPython: tp_version_tag)
        /// Version tag of 0 means type is not cacheable
        /// </summary>
        private ulong _typeVersionTag;

        /// <summary>
        /// Public accessor for version tag
        /// </summary>
        public ulong TypeVersionTag => _typeVersionTag;

        /// <summary>
        /// Invalidate type cache when ClassDict changes (CPython: type_modified)
        /// CPython reference: Objects/typeobject.c:420-522 (type_modified)
        /// </summary>
        public void InvalidateTypeCache()
        {
            // Assign new version tag to invalidate cache
            _typeVersionTag = _nextVersionTag++;

            // TODO: Invalidate subclasses recursively
            // CPython iterates through type_list and invalidates all subclasses
            // For now, we'll just invalidate this type
        }

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

        // CPython 3.12: type is its own metaclass (type.__class__ == type)
        // TypeType is actually PyTypeMetaclass.Instance
        public static PyType TypeType => PyTypeMetaclass.Instance;

        // 숫자 타입들
        public static readonly PyType IntType = new PyType("int", new[] { ObjectType }, null, TypeKind.Int);
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
        public static readonly PyType ClassMethodDescriptorType = new PyType("classmethod_descriptor", new[] { ObjectType });
        
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
        public static readonly PyType SystemErrorType = new PyType("SystemError", new[] { ExceptionType });
        public static readonly PyType NotImplementedErrorType = new PyType("NotImplementedError", new[] { RuntimeErrorType });
        public static readonly PyType RecursionErrorType = new PyType("RecursionError", new[] { RuntimeErrorType });

        public static readonly PyType OSErrorType = new PyType("OSError", new[] { ExceptionType });
        public static readonly PyType FileNotFoundErrorType = new PyType("FileNotFoundError", new[] { OSErrorType });
        public static readonly PyType EOFErrorType = new PyType("EOFError", new[] { ExceptionType });
        public static readonly PyType MemoryErrorType = new PyType("MemoryError", new[] { ExceptionType });
        public static readonly PyType UnicodeErrorType = new PyType("UnicodeError", new[] { ValueErrorType });
        public static readonly PyType UnicodeDecodeErrorType = new PyType("UnicodeDecodeError", new[] { UnicodeErrorType });
        public static readonly PyType UnicodeEncodeErrorType = new PyType("UnicodeEncodeError", new[] { UnicodeErrorType });
        public static readonly PyType BufferErrorType = new PyType("BufferError", new[] { ExceptionType });

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
        public static readonly PyType StopAsyncIterationType = new PyType("StopAsyncIteration", new[] { ExceptionType });
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

        // Typing system types (PEP 484, 585, 612, 646, 695)
        public static readonly PyType UnionType = new PyType("Union", new[] { ObjectType });
        public static readonly PyType TypeVarType = new PyType("TypeVar", new[] { ObjectType });
        public static readonly PyType ParamSpecType = new PyType("ParamSpec", new[] { ObjectType }); // PEP 612
        public static readonly PyType TypeVarTupleType = new PyType("TypeVarTuple", new[] { ObjectType }); // PEP 646
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

        // Cached tuple objects for __mro__ and __bases__ identity consistency
        // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
        internal PyTuple _cachedMroTuple;
        internal PyTuple _cachedBasesTuple;

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

            // CPython 3.12: Assign version tag for method cache
            _typeVersionTag = _nextVersionTag++;

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
                // Performance: Eliminated LINQ (Where + ToList) - manual removal
                for (int i = sequences.Count - 1; i >= 0; i--)
                {
                    if (sequences[i].Count == 0)
                    {
                        sequences.RemoveAt(i);
                    }
                }

                if (sequences.Count == 0)
                    break;

                PyType candidate = null;

                // 좋은 후보 찾기 (다른 시퀀스의 tail에 없는 head)
                foreach (var seq in sequences)
                {
                    var head = seq[0];

                    // Performance: Eliminated LINQ (Any + Skip + Contains) - nested loops
                    bool isTail = false;
                    for (int si = 0; si < sequences.Count; si++)
                    {
                        var s = sequences[si];
                        for (int i = 1; i < s.Count; i++)
                        {
                            if (s[i] == head)
                            {
                                isTail = true;
                                break;
                            }
                        }
                        if (isTail) break;
                    }

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
#if DEBUG
            Console.WriteLine($"[IsSubclassOf] Checking if {this.Name} (id={this.GetHashCode()}) is subclass of {other.Name} (id={other.GetHashCode()})");
            Console.WriteLine($"[IsSubclassOf] MRO.Count = {MRO.Count}");
            for (int i = 0; i < MRO.Count; i++)
            {
                var mroType = MRO[i];
                Console.WriteLine($"  MRO[{i}]: {mroType.Name} (id={mroType.GetHashCode()}), ReferenceEquals={ReferenceEquals(mroType, other)}");
            }
#endif

            // CPython 3.12: Check by reference first (fast path)
            // CPython equivalent: PyTuple_GET_ITEM(mro, i) == (PyObject *)b
            // This checks if the exact same type object is in the MRO
            for (int i = 0; i < MRO.Count; i++)
            {
                if (ReferenceEquals(MRO[i], other))
                {
#if DEBUG
                    Console.WriteLine($"[IsSubclassOf] Found match at MRO[{i}] by ReferenceEquals");
#endif
                    return true;
                }
            }

            // Fallback: Check by type name for built-in types
            // This handles cases where the same built-in type might have different PyType instances
            foreach (var mroType in MRO)
            {
                if (mroType.Name == other.Name &&
                    (mroType.Name == "tuple" || mroType.Name == "list" || mroType.Name == "dict" ||
                     mroType.Name == "str" || mroType.Name == "int" || mroType.Name == "float" ||
                     mroType.Name == "bool" || mroType.Name == "object" || mroType.Name == "type"))
                {
#if DEBUG
                    Console.WriteLine($"[IsSubclassOf] Found match by name: {mroType.Name}");
#endif
                    return true;
                }
            }

#if DEBUG
            Console.WriteLine($"[IsSubclassOf] No match found, returning false");
#endif
            return false;
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

            // type(name, bases, dict, **kwargs) - 새로운 타입 생성
            // CPython 3.12: Objects/typeobject.c:1627-1689 (type_call)
            if (this == TypeType && args.Length == 3)
            {
                if (args[0] is PyString name && args[1] is PyTuple bases && args[2] is PyDict classDict)
                {
                    // CPython 3.12: If kwargs are provided, we need to determine the metaclass
                    // and call its __new__ method with the kwargs
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                    {
                        // Determine metaclass from bases
                        PyType metaclass = TypeType;
                        if (bases.Items.Length > 0)
                        {
                            var firstBase = bases.Items[0];
                            if (firstBase is PyClass baseClass)
                            {
                                metaclass = baseClass.Metaclass ?? TypeType;
                            }
                            else if (firstBase is PyType baseType)
                            {
                                metaclass = TypeType;
                            }
                        }

                        // Call the metaclass's __new__ method with kwargs
                        // CPython reference: Objects/typeobject.c:1667
                        // obj = type->tp_new(type, args, kwds);
                        var newMethod = metaclass.GetAttribute("__new__");
                        if (newMethod != null && newMethod.IsCallable())
                        {
                            // Call metaclass.__new__(metaclass, name, bases, dict, **kwargs)
                            var newArgs = new PyObject[] { metaclass, name, bases, classDict };
                            return newMethod.Call(newArgs, kwargs);
                        }
                    }

                    // No kwargs - use simple class creation
                    // Performance: Eliminated LINQ - manual cast instead of Cast + ToArray
                    var baseTypes = new PyType[bases.Items.Length];
                    for (int i = 0; i < bases.Items.Length; i++)
                    {
                        baseTypes[i] = (PyType)bases.Items[i];
                    }
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

            // CPython 3.12: Objects/funcobject.c:1241-1256 (staticmethod)
            // Special handling for descriptor types
            if (this == StaticMethodType)
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"staticmethod expected 1 argument, got {args.Length}");

                var func = args[0];

                // If already a staticmethod, return as-is (idempotent)
                if (func is PyStaticmethod pyStaticmethod)
                    return pyStaticmethod;

                if (func == null || !func.IsCallable())
                    throw PyTypeError.Create($"staticmethod() argument must be callable (got {func?.GetTypeName()})");

                // CPython 3.12: Accept ANY callable
                return new PyStaticmethod(func);
            }

            // CPython 3.12: Objects/funcobject.c:1055-1068 (classmethod)
            if (this == ClassMethodType)
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"classmethod expected 1 argument, got {args.Length}");

                var func = args[0];

                // If already a classmethod, return as-is (idempotent)
                if (func is PyClassmethod pyClassmethod)
                    return pyClassmethod;

                if (func == null || !func.IsCallable())
                    throw PyTypeError.Create($"classmethod() argument must be callable (got {func?.GetTypeName()})");

                // CPython 3.12: Accept ANY callable
                return new PyClassmethod(func);
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
            return CreateInstance(args, kwargs);
        }

        // 인스턴스 생성 (기본 구현)
        public virtual PyObject CreateInstance(PyObject[] args, PyDict kwargs = null)
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
                case "SystemError":
                    return new PySystemError(message);
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
                case "bytearray":
                    // bytearray() constructor - create a mutable byte array
                    if (args.Length == 0)
                        return new PyByteArray();
                    if (args.Length == 1)
                    {
                        var arg = args[0];
                        // bytearray(int) - create zero-filled array
                        if (arg is PyInt pyInt)
                            return new PyByteArray((int)pyInt.Value);
                        // bytearray(bytes) or bytearray(bytearray) or bytearray(iterable)
                        return PyByteArray.FromIterable(arg);
                    }
                    throw PyTypeError.Create($"bytearray() takes at most 1 argument ({args.Length} given)");
            }

            // 내장 타입들에 대한 특별 처리 (타입 변환) - PyBuiltinFunction 위임
            var builtinFunc = new PyBuiltinFunction(Name);
            return builtinFunc.Call(args, null);
        }

        // Special method lookup (MRO 기반) with method cache
        // CPython reference: Objects/typeobject.c:_PyType_Lookup
        public PyObject LookupSpecial(string name)
        {
            // Use global method cache for fast lookup
            return GlobalMethodCache.Lookup(this, name, out _);
        }

        #endregion

        #region Descriptor Initialization

        /// <summary>
        /// 타입별 descriptor 초기화 (CPython의 타입 객체 초기화와 유사)
        /// CPython equivalent: fast type identification using enum instead of string comparison
        /// </summary>
        // CPython 3.12: Objects/typeobject.c:7163-7182 (type_ready_fill_dict)
        // Called from PyType_Ready to add tp_methods, tp_members, tp_getset to tp_dict
        // SharpPy: Called from constructor, can be overridden by subclasses
        protected virtual void InitializeDescriptors()
        {
            // CPython 3.12 호환: 각 타입의 tp_methods, tp_getset 초기화
            // readonly TypeKind를 사용하여 빠른 int 비교 (string 비교보다 훨씬 빠름)
            switch (_kind)
            {
                case TypeKind.Str:
                    InitializeStrTypeDescriptors();
                    break;

                case TypeKind.Int:
                    InitializeIntTypeDescriptors();
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
                    else if (Name == "mappingproxy")
                        InitializeMappingProxyTypeDescriptors();
                    else if (Name == "BaseException")
                        InitializeBaseExceptionTypeDescriptors();
                    break;
            }

            // CPython 3.12: Objects/typeobject.c:1410-1412
            // All types have __doc__ attribute (tp_doc)
            // If not already set by specific type initializer, default to None
            if (!TypeDict.ContainsKey("__doc__"))
            {
                TypeDict["__doc__"] = PyNone.Instance;
            }
        }

        /// <summary>
        /// str 타입의 descriptor 테이블 초기화 (CPython unicodeobject.c 참조)
        /// 실제 descriptor 등록은 PyString.InitializeStringDescriptors()에서 수행됨
        /// </summary>
        private void InitializeStrTypeDescriptors()
        {
            // str.__new__() - CPython Objects/unicodeobject.c:14871 (tp_new = unicode_new)
            // This is critical for enum._find_data_type_() to recognize str as a data type
            TypeDict["__new__"] = new PyBuiltinFunction(
                "__new__",
                (args, kwargs) => {
                    // str.__new__(cls, object='', encoding='utf-8', errors='strict')
                    if (args.Length == 0)
                        throw PyTypeError.Create("str.__new__(): not enough arguments");

                    var cls = args[0];
                    if (cls is not PyType)
                        throw PyTypeError.Create($"str.__new__(X): X is not a type object ({cls.GetTypeName()})");

                    // If called with just the class, return empty string
                    if (args.Length == 1)
                        return new PyString("");

                    var obj = args[1];

                    // Convert object to string
                    if (obj is PyString pyStr)
                        return pyStr;
                    else if (obj is PyInt pyInt)
                        return new PyString(pyInt.Value.ToString());
                    else if (obj is PyFloat pyFloat)
                        return new PyString(pyFloat.Value.ToString());
                    else if (obj is PyBool pyBool)
                        return new PyString(pyBool.Value ? "True" : "False");
                    else if (obj is PyNone)
                        return new PyString("None");
                    else
                        // Call __str__ method
                        return new PyString(obj.ToString());
                }
            );

            // PyString.InitializeStringDescriptors()에서 모든 str descriptor를 등록하므로
            // 여기서는 __new__ 외에는 아무것도 하지 않음 (중복 방지)
        }

        /// <summary>
        /// int 타입의 descriptor 테이블 초기화 (CPython longobject.c 참조)
        /// </summary>
        private void InitializeIntTypeDescriptors()
        {
            var intType = this;

            // int.__new__() - CPython Objects/longobject.c:5598-5642 (long_new_impl), line 6342 (tp_new = long_new)
            // This is critical for enum._find_data_type_() to recognize int as a data type
            TypeDict["__new__"] = new PyBuiltinFunction(
                "__new__",
                (args, kwargs) => {
                    // int.__new__(cls, x=0, base=10)
                    if (args.Length == 0)
                        throw PyTypeError.Create("int.__new__(): not enough arguments");

                    var cls = args[0];
                    if (cls is not PyType)
                        throw PyTypeError.Create($"int.__new__(X): X is not a type object ({cls.GetTypeName()})");

                    // If called with just the class, return 0
                    if (args.Length == 1)
                        return new PyInt(0);

                    var x = args[1];

                    // Handle base parameter if present
                    int baseValue = 10;
                    if (args.Length > 2)
                    {
                        if (args[2] is not PyInt baseArg)
                            throw PyTypeError.Create("int() base must be >= 2 and <= 36, or 0");
                        baseValue = (int)baseArg.Value;
                        if (baseValue != 0 && (baseValue < 2 || baseValue > 36))
                            throw PyTypeError.Create("int() base must be >= 2 and <= 36, or 0");
                    }

                    // Convert x to int
                    if (x is PyInt pyInt)
                        return pyInt;
                    else if (x is PyString pyStr)
                        return PyInt.FromString(pyStr.Value, baseValue);
                    else if (x is PyFloat pyFloat)
                        return new PyInt((long)pyFloat.Value);
                    else if (x is PyBool pyBool)
                        return new PyInt(pyBool.Value ? 1 : 0);
                    else
                        throw PyTypeError.Create($"int() argument must be a string or a number, not '{x.GetTypeName()}'");
                }
            );

            // int.__repr__() - CPython Objects/longobject.c:long_to_decimal_string
            TypeDict["__repr__"] = new PyMethodDescriptor(
                "__repr__",
                intType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"__repr__() takes no arguments ({args.Length} given)");
                    if (self is not PyInt pyInt)
                        throw PyTypeError.Create("descriptor '__repr__' for 'int' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return pyInt.ToRepr();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // int.bit_length() - CPython Objects/longobject.c:long_bit_length
            TypeDict["bit_length"] = new PyMethodDescriptor(
                "bit_length",
                intType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"bit_length() takes no arguments ({args.Length} given)");
                    if (self is not PyInt pyInt)
                        throw PyTypeError.Create("descriptor 'bit_length' for 'int' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return pyInt.BitLength();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // int.bit_count() - CPython Objects/longobject.c:long_bit_count
            TypeDict["bit_count"] = new PyMethodDescriptor(
                "bit_count",
                intType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"bit_count() takes no arguments ({args.Length} given)");
                    if (self is not PyInt pyInt)
                        throw PyTypeError.Create("descriptor 'bit_count' for 'int' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return pyInt.BitCount();
                },
                minArgs: 0,
                maxArgs: 0
            );
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

            // object.__new__(cls) - CPython Objects/typeobject.c:5444-5515 (object_new)
            // Note: PyMethodDescriptor extracts first arg as 'self', which is the 'cls' for __new__
            TypeDict["__new__"] = new PyMethodDescriptor(
                "__new__",
                objectType,
                (self, args, kwargs) => {
                    // CPython 3.12: __new__ is a static method, but exposed as method_descriptor
                    // 'self' parameter here is actually 'cls' (the class to instantiate)
                    // args contains additional arguments passed to __new__ (should be empty for object.__new__)

                    PyClass cls;
                    if (self is PyClass clsArg)
                    {
                        cls = clsArg;
                    }
                    else if (self is PyType typeArg)
                    {
                        // Handle PyType as well (for built-in types)
                        // Create PyClassInstance with a temporary PyClass wrapper
                        // For built-in types like 'int', 'str', etc., we need special handling
                        return new PyInstance();
                    }
                    else
                    {
                        throw PyTypeError.Create($"object.__new__(X): X is not a type object (got {self.GetTypeName()})");
                    }

                    // CPython 3.12: Check for excess args (lines 5446-5457)
                    // object.__new__() takes exactly one argument (the type to instantiate)
                    if (args.Length > 0 || (kwargs != null && kwargs.Length() > 0))
                    {
                        // Note: CPython has more complex logic here for subclasses
                        // For now, we allow extra args (will be passed to __init__)
                    }

                    // Create instance of the class
                    return new PyClassInstance(cls);
                },
                minArgs: 0,  // 'cls' is extracted as 'self' by PyMethodDescriptor
                maxArgs: int.MaxValue
            );

            // object.__getattribute__(self, name) - CPython PyObject_GenericGetAttr
            // Reference: Objects/object.c:1536, typeobject.c:6566 (tp_getattro slot)
            // CRITICAL: This must call the internal generic method to avoid infinite recursion
            // when custom __getattribute__ calls super().__getattribute__()
            TypeDict["__getattribute__"] = new PyMethodDescriptor(
                "__getattribute__",
                objectType,
                (self, args, kwargs) => {
                    if (args.Length < 1)
                        throw PyTypeError.Create("__getattribute__() missing 1 required positional argument: 'name'");

                    if (args[0] is not PyString nameStr)
                        throw PyTypeError.Create("attribute name must be string, not '" + args[0].GetTypeName() + "'");

                    // CPython: object's tp_getattro points directly to PyObject_GenericGetAttr (C function)
                    // SharpPy: Call GetAttributeGeneric which skips custom __getattribute__ lookup
                    if (self is PyClassInstance instance)
                    {
                        return instance.GetAttributeGeneric(nameStr.Value);
                    }

                    // Fallback for non-PyClassInstance objects
                    return self.GetAttribute(nameStr.Value);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // object.__setattr__(self, name, value) - CPython PyObject_GenericSetAttr
            // Reference: Objects/object.c:1565
            // IMPORTANT: This must call SetAttributeDefault, NOT SetAttribute,
            // to avoid infinite recursion when user defines __setattr__ that calls super().__setattr__()
            TypeDict["__setattr__"] = new PyMethodDescriptor(
                "__setattr__",
                objectType,
                (self, args, kwargs) => {
                    if (args.Length < 2)
                        throw PyTypeError.Create("__setattr__() missing required positional arguments");

                    if (args[0] is not PyString nameStr)
                        throw PyTypeError.Create("attribute name must be string, not '" + args[0].GetTypeName() + "'");

                    // CPython 3.12: object.__setattr__ uses _PyObject_GenericSetAttrWithDict
                    // which is the "default" behavior without __setattr__ lookup
                    if (self is SharpPy.PyClassInstance instance)
                    {
                        instance.SetAttributeDefault(nameStr.Value, args[1]);
                    }
                    else
                    {
                        // For other types (built-in types), use SetAttribute directly
                        // These don't have user-defined __setattr__ so no recursion issue
                        self.SetAttribute(nameStr.Value, args[1]);
                    }
                    return PyNone.Instance;
                },
                minArgs: 2,
                maxArgs: 2
            );

            // object.__delattr__(self, name) - CPython PyObject_GenericSetAttr with value=NULL
            // Reference: Objects/object.c:1565 (same function, value=NULL means delete)
            // IMPORTANT: This must call DelAttributeDefault, NOT DelAttribute,
            // to avoid infinite recursion when user defines __delattr__ that calls super().__delattr__()
            TypeDict["__delattr__"] = new PyMethodDescriptor(
                "__delattr__",
                objectType,
                (self, args, kwargs) => {
                    if (args.Length < 1)
                        throw PyTypeError.Create("__delattr__() missing 1 required positional argument: 'name'");

                    if (args[0] is not PyString nameStr)
                        throw PyTypeError.Create("attribute name must be string, not '" + args[0].GetTypeName() + "'");

                    // CPython 3.12: object.__delattr__ uses _PyObject_GenericSetAttrWithDict with value=NULL
                    if (self is SharpPy.PyClassInstance instance)
                    {
                        instance.DelAttributeDefault(nameStr.Value);
                    }
                    else
                    {
                        // For other types (built-in types), use DelAttribute directly
                        // These don't have user-defined __delattr__ so no recursion issue
                        self.DelAttribute(nameStr.Value);
                    }
                    return PyNone.Instance;
                },
                minArgs: 1,
                maxArgs: 1
            );

            // object.__init_subclass__() - CPython PEP 487
            // Reference: Objects/typeobject.c:6414 (object_init_subclass)
            // This is a classmethod (METH_CLASS | METH_NOARGS) that does nothing by default
            // It's called automatically when a class is subclassed
            TypeDict["__init_subclass__"] = new PyBuiltinFunction(
                "__init_subclass__",
                (args, kwargs) => {
                    // object.__init_subclass__() does nothing and returns None
                    // Subclasses can override this to customize subclass creation
                    // The first arg would be the class, but we don't need it here
                    return PyNone.Instance;
                }
            );

            // object.__format__(format_spec) - CPython 3.12: Objects/typeobject.c:6257 (object___format__)
            // Default implementation: if format_spec is empty, return str(self), else raise TypeError
            TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__",
                objectType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__format__() takes exactly 1 argument ({args.Length} given)");

                    if (args[0] is not PyString specStr)
                        throw PyTypeError.Create($"__format__() argument must be str, not {args[0].GetTypeName()}");

                    // CPython: empty format_spec returns str(self)
                    if (specStr.Value == "")
                    {
                        return self.ToStr();
                    }

                    // CPython: non-empty format_spec raises TypeError for base object
                    throw PyTypeError.Create($"unsupported format string passed to {self.GetTypeName()}.__format__");
                },
                minArgs: 1,
                maxArgs: 1
            );
        }

        /// <summary>
        /// type 타입의 descriptor 테이블 초기화 (CPython typeobject.c 참조)
        /// Phase 3: Directly populate TypeDict instead of Descriptors
        /// </summary>
        // CPython 3.12: Objects/typeobject.c:1577-1590 (type_getsets array)
        // Contains __dict__, __bases__, __mro__, __module__, etc.
        // SharpPy: Add these descriptors to TypeDict (equivalent to tp_dict)
        protected void InitializeTypeTypeDescriptors()
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

            // type.__qualname__ - CPython type_qualname (Objects/typeobject.c:250-275)
            // For now, same as __name__ for built-in types
            TypeDict["__qualname__"] = new PyGetSetDescriptor(
                "__qualname__",
                typeType,
                getter: self => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__qualname__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return new PyString(type.Name);  // For built-in types, qualname == name
                }
            );

            // type.__bases__ - CPython type_get_bases / type_set_bases
            TypeDict["__bases__"] = new PyGetSetDescriptor(
                "__bases__",
                typeType,
                getter: self => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__bases__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // CPython 3.12 requirement: type.__bases__ is type.__bases__ must be True
                    // Cache the tuple object to ensure identity consistency
                    if (type._cachedBasesTuple == null)
                    {
                        var basesArray = new PyObject[type.BaseTypes.Length];
                        for (int i = 0; i < type.BaseTypes.Length; i++)
                        {
                            basesArray[i] = type.BaseTypes[i];
                        }
                        type._cachedBasesTuple = new PyTuple(basesArray);
                    }
                    return type._cachedBasesTuple;
                }
            );

            // type.__mro__ - CPython type_mro (read-only)
            TypeDict["__mro__"] = new PyGetSetDescriptor(
                "__mro__",
                typeType,
                getter: self => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__mro__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
                    // Cache the tuple object to ensure identity consistency
                    if (type._cachedMroTuple == null)
                    {
                        var mroArray = new PyObject[type.MRO.Count];
                        for (int i = 0; i < type.MRO.Count; i++)
                        {
                            mroArray[i] = type.MRO[i];
                        }
                        type._cachedMroTuple = new PyTuple(mroArray);
                    }
                    return type._cachedMroTuple;
                }
            );

            // type.__dict__ - CPython type_dict (read-only, returns mappingproxy)
            TypeDict["__dict__"] = new PyGetSetDescriptor(
                "__dict__",
                typeType,
                getter: self => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__dict__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // CPython 3.12: Objects/typeobject.c:1397-1404 (type_dict)
                    // Simply return mappingproxy of tp_dict, DO NOT traverse MRO
                    // Line 1399: PyObject *dict = lookup_tp_dict(type);
                    // Line 1403: return PyDictProxy_New(dict);

                    // For PyClass: return mappingproxy of ClassDict
                    if (type is PyClass customType)
                    {
                        return new PyMappingProxy(customType.ClassDict);
                    }

                    // For built-in types: return mappingproxy of TypeDict
                    // Add required special attributes
                    var typeDict = new Dictionary<string, PyObject>(type.TypeDict);

                    // Add __name__
                    typeDict["__name__"] = StringCache.GetOrCreate(type.Name);

                    // CPython 3.12 requirement: Cache tuples for identity consistency
                    // type.__bases__ is type.__bases__ must be True
                    var basesArray = new PyObject[type.BaseTypes.Length];
                    for (int i = 0; i < type.BaseTypes.Length; i++)
                    {
                        basesArray[i] = type.BaseTypes[i];
                    }
                    if (type._cachedBasesTuple == null)
                    {
                        type._cachedBasesTuple = new PyTuple(basesArray);
                    }
                    typeDict["__bases__"] = type._cachedBasesTuple;

                    var mroArray = new PyObject[type.MRO.Count];
                    for (int i = 0; i < type.MRO.Count; i++)
                    {
                        mroArray[i] = type.MRO[i];
                    }
                    if (type._cachedMroTuple == null)
                    {
                        type._cachedMroTuple = new PyTuple(mroArray);
                    }
                    typeDict["__mro__"] = type._cachedMroTuple;

                    // Return as read-only mappingproxy
                    return new PyMappingProxy(typeDict);
                }
            );

            // CPython 3.12: Objects/typeobject.c:1063-1091 (type_module / type_set_module)
            // type.__module__ - returns module name for the type
            // For heap types (user-defined): lookup in tp_dict
            // For built-in types: "builtins" (or extract from tp_name if it contains '.')
            TypeDict["__module__"] = new PyGetSetDescriptor(
                "__module__",
                typeType,
                getter: self => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__module__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // CPython 3.12: Objects/typeobject.c:1065-1088
                    // For heap types (PyClass), check ClassDict
                    if (type is PyClass customType)
                    {
                        if (customType.ClassDict.TryGetValue("__module__", out var mod))
                        {
                            return mod;
                        }
                        throw PyAttributeError.Create("__module__");
                    }

                    // For built-in types: check if tp_name contains '.'
                    // CPython: const char *s = strrchr(type->tp_name, '.');
                    var dotIndex = type.Name.LastIndexOf('.');
                    if (dotIndex >= 0)
                    {
                        return new PyString(type.Name.Substring(0, dotIndex));
                    }

                    // Default: "builtins" for built-in types
                    // CPython: mod = Py_NewRef(&_Py_ID(builtins));
                    return new PyString("builtins");
                },
                setter: (self, value) => {
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__module__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // CPython 3.12: Objects/typeobject.c:1094-1105 (type_set_module)
                    // Only allow setting for heap types (user-defined classes)
                    if (type is PyClass customType)
                    {
                        customType.ClassDict["__module__"] = value;
                    }
                    else
                    {
                        throw PyTypeError.Create($"cannot set '__module__' attribute of immutable type '{type.Name}'");
                    }
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
                    // Performance: Eliminated LINQ - direct array use
                    var newClass = new PyClass(name.Value, baseTypes.ToArray(), stringDict);

                    // If metacls is not type, we might need to set a custom metaclass
                    // For now, PyClass always uses type as metaclass (standard behavior)
                    return newClass;
                },
                minArgs: 4,
                maxArgs: 4
            );

            // type.__setattr__(cls, name, value) - CPython type_setattro
            // Reference: Objects/typeobject.c:4815-4894 (type_setattro)
            TypeDict["__setattr__"] = new PyMethodDescriptor(
                "__setattr__",
                typeType,
                (self, args, kwargs) => {
                    if (args.Length < 2)
                        throw PyTypeError.Create("__setattr__() missing required positional arguments");

                    if (args[0] is not PyString nameStr)
                        throw PyTypeError.Create("attribute name must be string, not '" + args[0].GetTypeName() + "'");

                    // CPython 3.12: Objects/typeobject.c:4815-4894 (type_setattro)
                    // Set attribute directly on the type's dict
                    if (self is PyClass cls)
                    {
                        cls.ClassDict[nameStr.Value] = args[1];
                        cls.InvalidateTypeCache();
                    }
                    else if (self is PyType type)
                    {
                        type.TypeDict[nameStr.Value] = args[1];
                    }
                    return PyNone.Instance;
                },
                minArgs: 2,
                maxArgs: 2
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

            // CPython 3.12: Objects/typeobject.c:4482
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            typeType.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
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

            // CPython 3.12: Objects/setobject.c:2087-2094 (set_intersection_update)
            // set.intersection_update(other) - Update set, keeping only elements in both
            TypeDict["intersection_update"] = new PyMethodDescriptor(
                "intersection_update",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"intersection_update() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'intersection_update' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.IntersectionUpdate(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // CPython 3.12: Objects/setobject.c:2096-2103 (set_difference_update)
            // set.difference_update(other) - Remove elements found in other
            TypeDict["difference_update"] = new PyMethodDescriptor(
                "difference_update",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"difference_update() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'difference_update' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.DifferenceUpdate(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // CPython 3.12: Objects/setobject.c:2135-2142 (set_symmetric_difference_update)
            // set.symmetric_difference_update(other) - Update with symmetric difference
            TypeDict["symmetric_difference_update"] = new PyMethodDescriptor(
                "symmetric_difference_update",
                setType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"symmetric_difference_update() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PySet set)
                        throw PyTypeError.Create("descriptor 'symmetric_difference_update' for 'set' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    return set.SymmetricDifferenceUpdate(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // CPython 3.12: Objects/setobject.c:2343
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            setType.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
            );
        }

        /// <summary>
        /// frozenset 타입의 descriptor 테이블 초기화
        /// </summary>
        private void InitializeFrozenSetTypeDescriptors()
        {
            var frozensetType = this;

            // CPython 3.12: Objects/setobject.c:2304
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            frozensetType.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
            );

            // TODO: frozenset 메서드들 구현
        }

        /// <summary>
        /// list 타입의 descriptor 테이블 초기화
        /// </summary>
        private void InitializeListTypeDescriptors()
        {
            // CPython 3.12: Objects/listobject.c
            // Register essential list descriptors into TypeDict for proper method resolution
            // Note: The full implementations are in PyClass.GetTypeAttribute, we register them here to TypeDict

            // For now, we'll register the descriptors by marking them as present
            // The actual lookup will still go through PyClass.GetTypeAttribute via the fallback mechanism
            // TODO: Migrate full descriptor implementations from PyClass.GetTypeAttribute to here

            // Since we can't easily call PyClass.GetTypeAttribute from here due to circular dependencies,
            // we'll add a marker that tells the system these methods exist
            // The SearchMRO will be updated to use GetTypeAttribute as a fallback

            // Temporary: Do nothing here - we'll fix SearchMRO instead to call GetTypeAttribute
        }

        /// <summary>
        /// tuple 타입의 descriptor 테이블 초기화
        /// </summary>
        private void InitializeTupleTypeDescriptors()
        {
            var tupleType = this;

            // tuple.__new__(cls, iterable=()) - CPython Objects/tupleobject.c:tuple_new
            // This is called when creating tuple subclasses
            // Note: PyMethodDescriptor passes the class as 'self' when called on a type
            TypeDict["__new__"] = new PyMethodDescriptor(
                "__new__",
                tupleType,
                (self, args, kwargs) => {
                    // __new__ is a static method, 'self' is actually the class
                    var cls = self;

                    // Get the iterable argument (default to empty tuple)
                    PyObject iterable = null;
                    if (args.Length > 0)
                    {
                        iterable = args[0];
                    }

                    // CPython 3.12: If cls is tuple itself (not a subclass), use tuple() constructor
                    if (cls == PyType.TupleType)
                    {
                        if (iterable == null)
                            return new PyTuple();
                        return iterable.AsTuple();
                    }

                    // CPython 3.12: Creating a tuple subclass
                    // Convert iterable to tuple items
                    PyObject[] tupleItems;
                    if (iterable == null)
                    {
                        tupleItems = new PyObject[0];
                    }
                    else if (iterable is PyTuple tup)
                    {
                        tupleItems = tup.Items;
                    }
                    else if (iterable is PyList list)
                    {
                        tupleItems = list.Items;
                    }
                    else
                    {
                        // Generic iterable - use iterator
                        var items = new System.Collections.Generic.List<PyObject>();
                        var iterator = iterable.GetIterator();
                        while (true)
                        {
                            try
                            {
                                items.Add(iterator.Next());
                            }
                            catch (PythonException ex) when (ex.PyException is PyStopIteration)
                            {
                                break;
                            }
                        }
                        tupleItems = items.ToArray();
                    }

                    // Create PyTupleSubclass instance
                    if (cls is PyClass pyClass)
                    {
                        return new PyTupleSubclass(pyClass, tupleItems);
                    }
                    else
                    {
                        throw PyTypeError.Create($"tuple.__new__(X): X is not a type object ({cls.GetTypeName()})");
                    }
                },
                minArgs: 0,
                maxArgs: 1
            );
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
            // CPython: Objects/dictobject.c:3293-3378 (dict___init___impl)
            TypeDict["__init__"] = new PyMethodDescriptor(
                "__init__",
                dictType,
                (self, args, kwargs) => {
                    // Get the target dict storage
                    PyDict targetDict;
                    if (self is PyDict d)
                    {
                        targetDict = d;
                    }
                    else if (self is PyClassInstance ci && ci.IsDictSubclass())
                    {
                        targetDict = ci.GetDictStorage();
                    }
                    else
                    {
                        throw PyTypeError.Create($"descriptor '__init__' requires a 'dict' object but received a '{self.GetTypeName()}'");
                    }

                    // Process positional argument (mapping or iterable)
                    if (args.Length > 0)
                    {
                        var arg = args[0];
                        if (arg is PyDict srcDict)
                        {
                            // dict(mapping) - copy from dict using Keys() which returns PyList
                            var keysList = srcDict.Keys();
                            for (int i = 0; i < keysList.Length(); i++)
                            {
                                var key = keysList.GetItem(new PyInt(i));
                                targetDict.SetItem(key, srcDict.GetItem(key));
                            }
                        }
                        else
                        {
                            // Check if arg has keys method (mapping-like)
                            PyObject keysMethod = null;
                            try { keysMethod = arg.GetAttribute("keys"); } catch { }

                            if (keysMethod != null && keysMethod.IsCallable())
                            {
                                // dict(mapping) - copy from mapping-like object
                                var keysResult = keysMethod.Call(Array.Empty<PyObject>(), null);
                                var keysIter = keysResult.GetIterator();
                                while (true)
                                {
                                    try
                                    {
                                        var key = keysIter.Next();
                                        targetDict.SetItem(key, arg.GetItem(key));
                                    }
                                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                                    {
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                // dict(iterable) - copy from iterable of pairs
                                var iter = arg.GetIterator();
                                while (true)
                                {
                                    try
                                    {
                                        var item = iter.Next();
                                        // Each item should be a pair (key, value)
                                        if (item is PyTuple tuple && tuple.Length() == 2)
                                        {
                                            targetDict.SetItem(tuple.GetItem(0), tuple.GetItem(1));
                                        }
                                        else if (item is PyList list && list.Length() == 2)
                                        {
                                            targetDict.SetItem(list.GetItem(new PyInt(0)), list.GetItem(new PyInt(1)));
                                        }
                                        else
                                        {
                                            // Try to iterate the item to get key, value
                                            var pairList = new List<PyObject>();
                                            var pairIter = item.GetIterator();
                                            while (true)
                                            {
                                                try
                                                {
                                                    pairList.Add(pairIter.Next());
                                                }
                                                catch (PythonException ex2) when (ex2.PyException is PyStopIteration)
                                                {
                                                    break;
                                                }
                                            }
                                            if (pairList.Count == 2)
                                            {
                                                targetDict.SetItem(pairList[0], pairList[1]);
                                            }
                                            else
                                            {
                                                throw PyValueError.Create($"dictionary update sequence element has length {pairList.Count}; 2 is required");
                                            }
                                        }
                                    }
                                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // Process keyword arguments (kwargs is PyDict)
                    if (kwargs != null && kwargs.Length() > 0)
                    {
                        var kwargKeysList = kwargs.Keys();
                        for (int i = 0; i < kwargKeysList.Length(); i++)
                        {
                            var key = kwargKeysList.GetItem(new PyInt(i));
                            targetDict.SetItem(key, kwargs.GetItem(key));
                        }
                    }

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
                // Check ClassDict first
                bool foundInClassDict = metaclass.ClassDict.TryGetValue(name, out metaAttribute);
                if (foundInClassDict)
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

                // CPython 3.12: Objects/typeobject.c:4747 - find_name_in_mro(type, name, &error)
                // If not found in ClassDict, search through MRO (including base types' TypeDict)
                // This is needed for __dict__, __format__ and other type descriptors
                if (!foundInClassDict)
                {
                    // Search through metaclass MRO
                    foreach (var mroType in metaclass.MRO)
                    {
                        if (mroType is PyType mroTypeDict && mroTypeDict.TypeDict != null)
                        {
                            if (mroTypeDict.TypeDict.TryGetValue(name, out metaAttribute))
                            {
                                // Found in MRO - check if it's a descriptor
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
                                break; // Found attribute, stop searching
                            }
                        }
                    }
                }
            }
            else if (metatype is PyType metatypeType)
            {
                // CPython: For PyType instances, check TypeDict
                if (metatypeType.TypeDict != null && metatypeType.TypeDict.TryGetValue(name, out metaAttribute))
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
            //
            // CPython 3.12: Objects/typeobject.c:4844 - attribute = _PyType_Lookup(type, name)
            // Special case: If this == metatype (e.g., type accessing its own attributes),
            // skip Step 2 because we already searched in Step 1.
            // This prevents finding the same descriptor twice with different invocation contexts.
            bool skipSelfLookup = (this == metatype);

            // Phase 3: Check TypeDict (unified storage)
            if (!skipSelfLookup && TypeDict != null && TypeDict.TryGetValue(name, out var typeDictAttr))
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
            // Skip if this == metatype (already searched in Step 1)
            if (!skipSelfLookup)
            {
                var typeAttr = SharpPy.PyClass.GetTypeAttribute(this, name);
                if (typeAttr != null)
                {
                    if (typeAttr is IDescriptor localDescriptor)
                    {
                        return localDescriptor.Get(null, this);
                    }
                    return typeAttr;
                }
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
            // CPython 3.12: PEP 560 - Check for __class_getitem__ first
            // Reference: Objects/abstract.c:164-202 (PyObject_GetItem for types)

            // Try to lookup __class_getitem__ attribute
            // CPython uses _PyObject_LookupAttr which returns NULL if not found (no exception)
            try
            {
                var classGetItem = GetAttribute("__class_getitem__");

                if (classGetItem != null && classGetItem != PyNone.Instance && classGetItem.IsCallable())
                {
                    // Call __class_getitem__(key)
                    // Note: __class_getitem__ is converted to classmethod automatically,
                    // so it's already bound to the class
                    return classGetItem.Call(new PyObject[] { key }, null);
                }
            }
            catch (PythonException ex)
            {
                // No __class_getitem__, fall through to default behavior
                // CPython's _PyObject_LookupAttr returns NULL without exception for AttributeError
                if (!(ex.PyException is PyAttributeError))
                {
                    // Re-throw other exceptions
                    throw;
                }
            }

            // CPython 3.12: If no __class_getitem__ found, the type is not subscriptable
            // All subscriptable types define __class_getitem__ in their TypeDict
            // This error is only reached if the type doesn't define __class_getitem__
            throw PyTypeError.Create($"type '{Name}' is not subscriptable");
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
                // Performance: Eliminated LINQ - manual array construction instead of ToArray
                var newTypes = new PyObject[1 + unionType.Args.Length];
                newTypes[0] = this;
                for (int i = 0; i < unionType.Args.Length; i++)
                {
                    newTypes[i + 1] = unionType.Args[i];
                }
                return new PyUnionType(newTypes);
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
            // Performance: Eliminated LINQ - manual array construction instead of Select
            var mroNames = new string[MRO.Count];
            for (int i = 0; i < MRO.Count; i++)
            {
                mroNames[i] = MRO[i].Name;
            }
            Console.WriteLine($"{Name} MRO: [{string.Join(", ", mroNames)}]");
        }

        #endregion

        #region Type-specific Descriptor Initializers

        /// <summary>
        /// mappingproxy 타입의 descriptor 테이블 초기화
        /// CPython reference: Objects/descrobject.c:1043-1051 (mappingproxy_as_mapping)
        /// </summary>
        private void InitializeMappingProxyTypeDescriptors()
        {
            var mappingProxyType = this;

            // CPython 3.12: Objects/descrobject.c:1226-1246 (mappingproxy_new_impl)
            // mappingproxy.__new__(cls, mapping)
            TypeDict["__new__"] = new PyStaticBuiltinMethod("__new__", (args, kwargs) =>
            {
                if (args.Length < 2)
                    throw PyTypeError.Create($"mappingproxy() missing required argument: 'mapping' (pos 1)");

                // args[0] is the class (MappingProxyType)
                // args[1] is the mapping object
                var mappingObj = args[1];

                // CPython 3.12: Objects/descrobject.c:1237 - Check if mapping is a mapping
                // We accept PyDict or any object with dict-like interface
                if (mappingObj is PyDict pyDict)
                {
                    // Convert PyDict to Dictionary<string, PyObject>
                    var dict = new Dictionary<string, PyObject>();
                    foreach (var kvp in pyDict.InternalDict)
                    {
                        if (kvp.Key is PyString keyStr)
                        {
                            dict[keyStr.Value] = kvp.Value;
                        }
                    }
                    return new PyMappingProxy(dict);
                }
                else if (mappingObj is PyMappingProxy existingProxy)
                {
                    // If already a mappingproxy, return it as-is
                    return existingProxy;
                }
                else
                {
                    // For other mapping-like objects, we'd need to iterate and extract items
                    // For now, throw an error
                    throw PyTypeError.Create($"mappingproxy() argument must be a mapping, not '{mappingObj.GetTypeName()}'");
                }
            });

            // mappingproxy.__getitem__(key)
            // CPython 3.12: Objects/descrobject.c:1043-1046 (mappingproxy_getitem)
            // Line 1045: return PyObject_GetItem(pp->mapping, key);
            TypeDict["__getitem__"] = new PyMethodDescriptor(
                "__getitem__",
                mappingProxyType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__getitem__() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PyMappingProxy mappingProxy)
                        throw PyTypeError.Create($"descriptor '__getitem__' for 'mappingproxy' objects doesn't apply to a '{self.GetTypeName()}' object");

                    // Call GetItem which returns value as-is (no descriptor protocol)
                    return mappingProxy.GetItem(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // mappingproxy.__contains__(key)
            // CPython 3.12: Objects/descrobject.c:1079-1085 (mappingproxy_contains)
            // Line 1082: return PyDict_Contains(pp->mapping, key);
            TypeDict["__contains__"] = new PyMethodDescriptor(
                "__contains__",
                mappingProxyType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__contains__() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PyMappingProxy mappingProxy)
                        throw PyTypeError.Create($"descriptor '__contains__' for 'mappingproxy' objects doesn't apply to a '{self.GetTypeName()}' object");

                    // Call Contains which checks the underlying mapping
                    return mappingProxy.Contains(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // mappingproxy.__len__()
            // CPython 3.12: Objects/descrobject.c:1037-1040 (mappingproxy_len)
            // Line 1039: return PyObject_Size(pp->mapping);
            TypeDict["__len__"] = new PyMethodDescriptor(
                "__len__",
                mappingProxyType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"__len__() takes no arguments ({args.Length} given)");
                    if (self is not PyMappingProxy mappingProxy)
                        throw PyTypeError.Create($"descriptor '__len__' for 'mappingproxy' objects doesn't apply to a '{self.GetTypeName()}' object");

                    // Return the length of the underlying mapping
                    // Use InternalCount to avoid recursion (don't call Length() which looks up __len__)
                    return new PyInt(mappingProxy.InternalCount);
                },
                minArgs: 0,
                maxArgs: 0
            );
        }

        /// <summary>
        /// BaseException 타입의 descriptor 테이블 초기화
        /// CPython reference: Objects/exceptions.c:79-105 (BaseException_str)
        /// </summary>
        private void InitializeBaseExceptionTypeDescriptors()
        {
            // CPython 3.12: Objects/exceptions.c:42-71 (BaseException_init)
            // BaseException.__init__(self, *args) - stores args in self.args
            TypeDict["__init__"] = new PyBuiltinFunction("__init__", args =>
            {
                if (args.Length == 0)
                    throw PyTypeError.Create("descriptor '__init__' of 'BaseException' object needs an argument");

                var self = args[0];

                // Extract *args (skip self)
                var initArgs = args.Skip(1).ToArray();

                // Store args in instance
                if (self is PyClassInstance inst)
                {
                    inst.InstanceDict["args"] = new PyTuple(initArgs);
                }

                return PyNone.Instance;
            });

            // CPython 3.12: Objects/exceptions.c:79-105 (BaseException_str)
            // BaseException.__str__(self) - returns str(args[0]) if len(args) == 1, else str(args)
            TypeDict["__str__"] = new PyBuiltinFunction("__str__", args =>
            {
                if (args.Length == 0)
                    throw PyTypeError.Create("descriptor '__str__' of 'BaseException' object needs an argument");

                var self = args[0];

                // Get args attribute from exception instance
                PyObject argsAttr;
                if (self is PyBaseException exc)
                {
                    // C# exception object - directly access Args
                    var excArgs = exc.Args;
                    if (excArgs.Length == 0)
                        return new PyString("");
                    if (excArgs.Length == 1)
                        return excArgs[0].ToStr();
                    return new PyString($"({string.Join(", ", excArgs.Select(a => a.ToRepr().Value))})");
                }
                else if (self is PyClassInstance inst)
                {
                    // Python-level exception instance - get args attribute
                    if (inst.InstanceDict.TryGetValue("args", out argsAttr) ||
                        inst.InstanceType.ClassDict.TryGetValue("args", out argsAttr))
                    {
                        if (argsAttr is PyTuple tuple)
                        {
                            if (tuple.Items.Length == 0)
                                return new PyString("");
                            if (tuple.Items.Length == 1)
                                return tuple.Items[0].ToStr();
                            return new PyString($"({string.Join(", ", tuple.Items.Select(a => a.ToRepr().Value))})");
                        }
                    }
                }

                // Fallback: return empty string
                return new PyString("");
            });

            // CPython 3.12: Objects/exceptions.c:785-795 (BaseException_args member descriptor)
            // BaseException.args - data descriptor (writable, with both getter and setter)
            // CPython reference: Objects/exceptions.c:786 - PyMemberDef args_descriptor
            TypeDict["args"] = new PyGetSetDescriptor(
                name: "args",
                ownerType: this,
                getter: (PyObject self) =>
                {
                    if (self is PyClassInstance inst)
                    {
                        if (inst.InstanceDict.TryGetValue("args", out var argsValue))
                            return argsValue;
                    }
                    // If not found, return empty tuple
                    return new PyTuple(new PyObject[0]);
                },
                setter: (PyObject self, PyObject value) =>
                {
                    // CPython allows setting args attribute
                    if (self is PyClassInstance inst)
                    {
                        inst.InstanceDict["args"] = value;
                    }
                }
            );
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
                    // Performance: Eliminated LINQ - manual array construction instead of Concat + ToArray
                    var method = new PyBuiltinMethod(builtinFunction.Name, (self, args) => {
                        var newArgs = new PyObject[args.Length + 1];
                        newArgs[0] = self;
                        for (int i = 0; i < args.Length; i++)
                        {
                            newArgs[i + 1] = args[i];
                        }
                        return builtinFunction.Call(newArgs, null);
                    });
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