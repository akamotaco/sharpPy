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
            Int,        // int 타입
            // 필요시 추가: Float, List, Dict, etc.
        }

        /// <summary>
        /// This type's kind (readonly for safety, set only in constructor)
        /// </summary>
        private readonly TypeKind _kind;

        #endregion

        #region Type flags (CPython tp_flags)

        /// <summary>
        /// CPython's <c>tp_flags</c>. The whole point of this field is that "what kind of object
        /// is this" is answered from the type, once, instead of being discovered per operation:
        /// <c>PyList_Check(op)</c> is a feature test on <c>ob_type</c>
        /// (Include/listobject.h:24) and the test itself is <c>PyType_HasFeature</c>
        /// (Include/object.h:959; internal spelling <c>_PyType_HasFeature</c>, used at
        /// Objects/typeobject.c:2789).
        /// </summary>
        private PyTypeFlags _flags;

        public PyTypeFlags Flags => _flags;

        /// <summary>
        /// Include/object.h:959 <c>PyType_HasFeature</c>. Not <c>(flags &amp; f) == f</c>: a
        /// feature test asks whether any of the named bits are set.
        /// </summary>
        internal bool HasFeature(PyTypeFlags feature) => (_flags & feature) != 0;

        /// <summary>
        /// The bits a subtype takes from its base. CPython's heap types copy the base flags and
        /// then replace a few, which is measurable: a user class deriving from list has both
        /// <c>Py_TPFLAGS_LIST_SUBCLASS</c> and <c>Py_TPFLAGS_SEQUENCE</c> (measured with
        /// <c>class C(list): C.__flags__</c>). <c>inherit_special</c>
        /// (Objects/typeobject.c:6726) is what propagates the family bits, and it also propagates
        /// <c>_Py_TPFLAGS_MATCH_SELF</c> and <c>Py_TPFLAGS_ITEMS_AT_END</c>.
        /// </summary>
        /// <summary>
        /// What inherit_special propagates (Objects/typeobject.c:6755-6790): the family bits from
        /// the best base, plus _Py_TPFLAGS_MATCH_SELF and Py_TPFLAGS_ITEMS_AT_END. Notably **not**
        /// BaseType - measured, PyBool_Type has no BaseType although its base int does, because a
        /// static spec's tp_flags is the literal and the propagation list omits BaseType.
        /// </summary>
        private const PyTypeFlags InheritSpecialFlags =
            PyTypeFlags.MatchSelf | PyTypeFlags.ItemsAtEnd |
            PyTypeFlags.LongSubclass | PyTypeFlags.ListSubclass | PyTypeFlags.TupleSubclass |
            PyTypeFlags.BytesSubclass | PyTypeFlags.UnicodeSubclass |
            PyTypeFlags.DictSubclass | PyTypeFlags.BaseExceptionSubclass |
            PyTypeFlags.TypeSubclass;

        private const PyTypeFlags InheritableFlags =
            PyTypeFlags.BaseType | PyTypeFlags.HaveGC | PyTypeFlags.MatchSelf |
            PyTypeFlags.ItemsAtEnd |
            PyTypeFlags.LongSubclass | PyTypeFlags.ListSubclass | PyTypeFlags.TupleSubclass |
            PyTypeFlags.BytesSubclass | PyTypeFlags.UnicodeSubclass |
            PyTypeFlags.DictSubclass | PyTypeFlags.BaseExceptionSubclass |
            PyTypeFlags.TypeSubclass | PyTypeFlags.ManagedWeakRef;

        /// <summary>
        /// The bits of a compiled-in static spec that SharpPy models by hand: the family bits,
        /// MATCH_SELF and the collection bits, with the line they come from. Whether the type
        /// takes subclasses (Py_TPFLAGS_BASETYPE), is collected (HAVE_GC) and has a weakref list
        /// (tp_weaklistoffset) is measured on the reference interpreter into the layout table
        /// (tools/gen_type_layout.py); a type CPython does not have takes the DEFAULT|BASETYPE
        /// shape most static specs use.
        /// </summary>
        private static PyTypeFlags BuiltInSpecFlags(string name)
        {
            var flags = name switch
            {
                // Objects/longobject.c:6322
                "int" => PyTypeFlags.LongSubclass | PyTypeFlags.MatchSelf,
                // Objects/unicodeobject.c:14851
                "str" => PyTypeFlags.UnicodeSubclass | PyTypeFlags.MatchSelf,
                // Objects/bytesobject.c:2970
                "bytes" => PyTypeFlags.BytesSubclass | PyTypeFlags.MatchSelf,
                // Objects/tupleobject.c:864
                "tuple" => PyTypeFlags.TupleSubclass | PyTypeFlags.MatchSelf | PyTypeFlags.Sequence,
                // Objects/listobject.c:3147
                "list" => PyTypeFlags.ListSubclass | PyTypeFlags.MatchSelf | PyTypeFlags.Sequence,
                // Objects/dictobject.c:3863 - MAPPING, never SEQUENCE (Objects/typeobject.c:7021
                // asserts the two are exclusive)
                "dict" => PyTypeFlags.DictSubclass | PyTypeFlags.MatchSelf | PyTypeFlags.Mapping,
                // Objects/setobject.c:2143 and :2246 - no family bit: PySet_Check is a subtype test
                // (Include/setobject.h:36), so SharpPy keeps testing the MRO for these two
                "set" or "frozenset" => PyTypeFlags.MatchSelf,
                // Objects/memoryobject.c:3390, Objects/rangeobject.c:785
                "memoryview" or "range" => PyTypeFlags.Sequence,
                _ => PyTypeFlags.None,
            };
            if (!TypeLayouts.Measured(name, out var layout))
                return flags | PyTypeFlags.BaseType;
            if (!layout.Final) flags |= PyTypeFlags.BaseType;
            if (layout.Gc) flags |= PyTypeFlags.HaveGC;
            if (layout.WeakOffset != 0) flags |= PyTypeFlags.ManagedWeakRef;
            return flags;
        }

        private PyTypeFlags ComputeFlags()
        {
            // A class statement or type() call makes a heap type; a compiled builtin carries its
            // spec flags (Include/object.h:497 Py_TPFLAGS_HEAPTYPE).
            // inherit_special reads tp_base, best_base of a class, which need not be the first
            // base: class D(Mixin, dict) is a dict.
            var baseType = BaseTypes.Length > 0 ? TpBase : null;
            PyTypeFlags flags;
            // type itself is PyType_Type, a static type, although SharpPy builds it as a class.
            if (MadeByTypeNew)
            {
                // type_new copies the base's flags into a heap type, which is measurable: a user
                // class deriving from list carries LIST_SUBCLASS, and one deriving from bool
                // carries nothing, which is what makes the base check fire.
                flags = PyTypeFlags.HeapType |
                        (baseType != null ? baseType._flags & InheritableFlags : PyTypeFlags.None);
            }
            else
            {
                flags = BuiltInSpecFlags(Name) |
                        (baseType != null ? baseType._flags & InheritSpecialFlags : PyTypeFlags.None);
            }
            // type_ready_inherit -> inherit_patma_flags over mro[1:]: a type without SEQUENCE or
            // MAPPING of its own takes those of the first in its MRO that has any.
            if ((flags & CollectionBits) == 0 && MRO != null)
            {
                foreach (var level in MRO)
                {
                    if (ReferenceEquals(level, this) || level == null)
                        continue;
                    if ((level._flags & CollectionBits) != 0)
                    {
                        flags |= level._flags & CollectionBits;
                        break;
                    }
                }
            }
            // type_ready_set_type: historically all static types are immutable (bpo-43908).
            if ((flags & PyTypeFlags.HeapType) == 0)
                flags |= PyTypeFlags.ImmutableType;
            return flags;
        }

        private const PyTypeFlags CollectionBits = PyTypeFlags.Sequence | PyTypeFlags.Mapping;

        /// <summary>
        /// Objects/typeobject.c:2789 - a base without Py_TPFLAGS_BASETYPE is refused before any
        /// best_base/MRO work. Measured: <c>class C(bool)</c> raises, and PyBool_Type is the case
        /// because its spec omits the bit.
        /// </summary>
        private void CheckBaseTypeAcceptable()
        {
            foreach (var baseType in BaseTypes)
            {
                if (baseType == null) continue;
                if (!baseType.HasFeature(PyTypeFlags.BaseType))
                    throw PyTypeError.Create(
                        $"type '{baseType.FullName}' is not an acceptable base type");
            }
        }

        #endregion

        #region Type Version Tag System (CPython tp_version_tag)

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

            // type_modified: what a subclass cached from here is stale too.
            foreach (var subclass in LiveSubclasses())
                subclass.InvalidateTypeCache();
        }

        #endregion

        #region Descriptor Tables (CPython tp_methods, tp_getset, tp_members)

        /// <summary>
        /// CPython 3.12 호환: 타입의 딕셔너리 (tp_dict)
        /// All types (builtin and user-defined) use this unified storage
        /// </summary>
        public PyTypeDictionary TypeDict { get; protected set; }

        // The wrappers of add_operators for what this type does through CLR virtuals: see PySlotWrappers.

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
        public static readonly PyType DictKeysType = new PyType("dict_keys", new[] { ObjectType });
        public static readonly PyType DictValuesType = new PyType("dict_values", new[] { ObjectType });
        public static readonly PyType DictItemsType = new PyType("dict_items", new[] { ObjectType });
        public static readonly PyType SetType = new PyType("set", new[] { ObjectType });
        public static readonly PyType FrozenSetType = new PyType("frozenset", new[] { ObjectType });
        public static readonly PyType RangeType = new PyType("range", new[] { ObjectType });
        public static readonly PyType SliceType = new PyType("slice", new[] { ObjectType });
        
        // 함수 타입들
        public static readonly PyType FunctionType = new PyType("function", new[] { ObjectType });
        public static readonly PyType MethodType = new PyType("method", new[] { ObjectType });
        public static readonly PyType GeneratorType = new PyType("generator", new[] { ObjectType });
        public static readonly PyType MapType = new PyType("map", new[] { ObjectType });
        // tp_name "weakref.ReferenceType"
        public static readonly PyType WeakRefType = new PyType("ReferenceType", new[] { ObjectType }, "weakref");
        public static readonly PyType ProxyType = new PyType("ProxyType", new[] { ObjectType }, "weakref");
        public static readonly PyType CallableProxyType = new PyType("CallableProxyType", new[] { ObjectType }, "weakref");
        public static readonly PyType ZipType = new PyType("zip", new[] { ObjectType });
        public static readonly PyType FilterType = new PyType("filter", new[] { ObjectType });
        public static readonly PyType CallableIteratorType = new PyType("callable_iterator", new[] { ObjectType });
        public static readonly PyType ReversedType = new PyType("reversed", new[] { ObjectType });
        public static readonly PyType CoroutineType = new PyType("coroutine", new[] { ObjectType });
        public static readonly PyType AsyncGeneratorType = new PyType("async_generator", new[] { ObjectType });
        
        // Descriptor 타입들 (데모에서 필요)
        public static readonly PyType PropertyType = new PyType("property", new[] { ObjectType });
        public static readonly PyType StaticMethodType = new PyType("staticmethod", new[] { ObjectType });
        public static readonly PyType ClassMethodType = new PyType("classmethod", new[] { ObjectType });
        public static readonly PyType ClassMethodDescriptorType = new PyType("classmethod_descriptor", new[] { ObjectType });
        // CPython 3.12: Objects/descrobject.c descriptor/type names (type(x).__name__).
        public static readonly PyType MethodDescriptorType = new PyType("method_descriptor", new[] { ObjectType });
        public static readonly PyType WrapperDescriptorType = new PyType("wrapper_descriptor", new[] { ObjectType });
        public static readonly PyType MethodWrapperType = new PyType("method-wrapper", new[] { ObjectType });
        public static readonly PyType BuiltinFunctionOrMethodType = new PyType("builtin_function_or_method", new[] { ObjectType });
        public static readonly PyType SuperType = new PyType("super", new[] { ObjectType }); // CPython Objects/typeobject.c PySuper_Type

        // CPython gives each iterator family its own type (list_iterator,
        // tuple_iterator, str_ascii_iterator/str_iterator, set_iterator,
        // dict_keyiterator/valueiterator/itemiterator, range_iterator,
        // bytes_iterator, list_reverseiterator, reversed, enumerate,
        // empty_iterator). type(it).__name__ and repr both read this, so build a
        // cached PyType per name (base object) to keep type identity stable.
        private static readonly System.Collections.Generic.Dictionary<string, PyType> _iteratorTypeCache = new();
        public static PyType GetIteratorType(string name)
        {
            // The static iterator types of builtins are the objects themselves, not a second type
            // of the same name.
            switch (name)
            {
                case "reversed": return ReversedType;
                case "callable_iterator": return CallableIteratorType;
                case "map": return MapType;
                case "zip": return ZipType;
                case "filter": return FilterType;
            }
            lock (_iteratorTypeCache)
            {
                if (!_iteratorTypeCache.TryGetValue(name, out var t))
                {
                    // A static iterator type: its base is object, and its own tp_iter and
                    // tp_iternext are in its dict (InstallByName). A dotted name is tp_name: the
                    // module and then the class.
                    int dot = name.LastIndexOf('.');
                    t = dot < 0
                        ? new PyType(name, new[] { ObjectType })
                        : new PyType(name.Substring(dot + 1), new[] { ObjectType }, name.Substring(0, dot));
                    _iteratorTypeCache[name] = t;
                    PyIterator.InstallByName(t);
                }
                return t;
            }
        }
        
        // 기타 핵심 타입들
        public static readonly PyType ModuleType = new PyType("module", new[] { ObjectType });
        public static readonly PyType NoneType = new PyType("NoneType", new[] { ObjectType });
        public static readonly PyType NotImplementedType = new PyType("NotImplementedType", new[] { ObjectType });
        public static readonly PyType EllipsisType = new PyType("ellipsis", new[] { ObjectType }); // CPython Objects/sliceobject.c
        // The C NULL SharpPy needs as an object (PyNull): no Python type, so object's tp_subclasses
        // does not list it.
        public static readonly PyType NullType = Unlisted(new PyType("NullType", new[] { ObjectType }));

        private static PyType Unlisted(PyType type)
        {
            foreach (var baseType in type.BaseTypes) baseType.RemoveSubclass(type);
            return type;
        }
        public static readonly PyType GenericAliasType = new PyType("GenericAlias", new[] { ObjectType }, "types");
        public static readonly PyType MappingProxyType = new PyType("mappingproxy", new[] { ObjectType }); // CPython Objects/descrobject.c

        // 예외 타입 계층 (PyException.cs와 연동)
        public static readonly PyType BaseExceptionType = new PyType("BaseException", new[] { ObjectType });
        public static readonly PyType ExceptionType = new PyType("Exception", new[] { BaseExceptionType });
        public static readonly PyType SystemExitType = new PyType("SystemExit", new[] { BaseExceptionType });
        public static readonly PyType KeyboardInterruptType = new PyType("KeyboardInterrupt", new[] { BaseExceptionType });
        
        public static readonly PyType ArithmeticErrorType = new PyType("ArithmeticError", new[] { ExceptionType });
        public static readonly PyType ZeroDivisionErrorType = new PyType("ZeroDivisionError", new[] { ArithmeticErrorType });
        public static readonly PyType OverflowErrorType = new PyType("OverflowError", new[] { ArithmeticErrorType });
        public static readonly PyType FloatingPointErrorType = new PyType("FloatingPointError", new[] { ArithmeticErrorType });
        
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
        public static readonly PyType ConnectionErrorType = new PyType("ConnectionError", new[] { OSErrorType });
        public static readonly PyType BrokenPipeErrorType = new PyType("BrokenPipeError", new[] { ConnectionErrorType });
        public static readonly PyType ConnectionAbortedErrorType = new PyType("ConnectionAbortedError", new[] { ConnectionErrorType });
        public static readonly PyType ConnectionRefusedErrorType = new PyType("ConnectionRefusedError", new[] { ConnectionErrorType });
        public static readonly PyType ConnectionResetErrorType = new PyType("ConnectionResetError", new[] { ConnectionErrorType });
        public static readonly PyType ChildProcessErrorType = new PyType("ChildProcessError", new[] { OSErrorType });
        public static readonly PyType InterruptedErrorType = new PyType("InterruptedError", new[] { OSErrorType });
        public static readonly PyType ProcessLookupErrorType = new PyType("ProcessLookupError", new[] { OSErrorType });
        public static readonly PyType TimeoutErrorType = new PyType("TimeoutError", new[] { OSErrorType });
        public static readonly PyType BlockingIOErrorType = new PyType("BlockingIOError", new[] { OSErrorType });
        public static readonly PyType FileNotFoundErrorType = new PyType("FileNotFoundError", new[] { OSErrorType });
        public static readonly PyType PermissionErrorType = new PyType("PermissionError", new[] { OSErrorType });
        public static readonly PyType FileExistsErrorType = new PyType("FileExistsError", new[] { OSErrorType });
        public static readonly PyType NotADirectoryErrorType = new PyType("NotADirectoryError", new[] { OSErrorType });
        public static readonly PyType IsADirectoryErrorType = new PyType("IsADirectoryError", new[] { OSErrorType });

        public static readonly PyType EOFErrorType = new PyType("EOFError", new[] { ExceptionType });
        public static readonly PyType MemoryErrorType = new PyType("MemoryError", new[] { ExceptionType });
        public static readonly PyType UnicodeErrorType = new PyType("UnicodeError", new[] { ValueErrorType });
        public static readonly PyType UnicodeDecodeErrorType = new PyType("UnicodeDecodeError", new[] { UnicodeErrorType });
        public static readonly PyType UnicodeEncodeErrorType = new PyType("UnicodeEncodeError", new[] { UnicodeErrorType });
        public static readonly PyType UnicodeTranslateErrorType = new PyType("UnicodeTranslateError", new[] { UnicodeErrorType });
        /// <summary>
        /// Objects/exceptions.c PyExc_ReferenceError. The only thing that raises it is a weak reference
        /// being asked for an object that is gone (Objects/weakrefobject.c), so it belongs to the
        /// lifetime work. MRO measured: ReferenceError, Exception, BaseException, object.
        /// </summary>
        public static readonly PyType ReferenceErrorType =
            new PyType("ReferenceError", new[] { ExceptionType });

        public static readonly PyType BufferErrorType = new PyType("BufferError", new[] { ExceptionType });

        // Exception Groups (PEP 654)
        public static readonly PyType BaseExceptionGroupType = new PyType("BaseExceptionGroup", new[] { BaseExceptionType });
        public static readonly PyType ExceptionGroupType = new PyType("ExceptionGroup", new[] { BaseExceptionGroupType, ExceptionType });
        
        // Buffer Protocol (PEP 688)
        
        // Closure Support
        public static readonly PyType CellType = new PyType("cell", new[] { ObjectType });
        
        public static readonly PyType ImportErrorType = new PyType("ImportError", new[] { ExceptionType });
        public static readonly PyType ModuleNotFoundErrorType = new PyType("ModuleNotFoundError", new[] { ImportErrorType });
        
        public static readonly PyType SyntaxErrorType = new PyType("SyntaxError", new[] { ExceptionType });
        public static readonly PyType IndentationErrorType = new PyType("IndentationError", new[] { SyntaxErrorType });
        public static readonly PyType TabErrorType = new PyType("TabError", new[] { IndentationErrorType });
        
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
        public static readonly PyType EncodingWarningType = new PyType("EncodingWarning", new[] { WarningType });
        public static readonly PyType ResourceWarningType = new PyType("ResourceWarning", new[] { WarningType });

        // Typing system types (PEP 484, 585, 612, 646, 695)
        public static readonly PyType UnionType = new PyType("UnionType", new[] { ObjectType }, "types");
        public static readonly PyType MemberDescriptorType = new PyType("member_descriptor", new[] { ObjectType });
        public static readonly PyType GetSetDescriptorType = new PyType("getset_descriptor", new[] { ObjectType });

        /// <summary>
        /// Every builtin type adds its observable surface - TypeDict entries, slot wrappers, method
        /// descriptors - from a CLR static constructor, which runs the first time that CLR class is
        /// touched rather than the first time the Python type is used. Measured before this existed, a
        /// script that named `bytes` without ever creating a bytes object saw bytes.TypeDict holding one
        /// entry: dir(bytes) returned 24 names instead of 78, hasattr(bytes, "upper") was False and
        /// getattr(bytes, "fromhex") raised AttributeError, while b"a".upper() worked normally. The
        /// visible surface of a type therefore depended on which objects a program happened to build
        /// first. Forcing the class constructors here, in dependency order, removes that order
        /// dependence: str and the numeric types first because codecs and error texts use them, bytes
        /// before bytearray because bytearray's methods are delegated out of bytes' TypeDict, and
        /// PyClass last because user classes are built on top of all of it.
        /// </summary>
        static PyType()
        {
            // _PyExc_InitTypes makes ExceptionGroup with PyErr_NewException: a heap type, mutable.
            ExceptionGroupType.MarkHeapType();
            static void Init<T>() =>
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(T).TypeHandle);

            Init<PyString>();
            Init<PyInt>();
            Init<PyFloat>();
            Init<PyBytes>();
            Init<PyByteArray>();
            Init<PyList>();
            Init<PyTuple>();
            Init<PyDict>();
            Init<PyDictView>();
            Init<PyFrozenSet>();
            Init<PyComplex>();
            Init<PyIterator>();
            Init<PyGenerator>();
            Init<SharpPy.Core.PyAsyncGenerator>();
            Init<SharpPy.Core.PyCoroutine>();
            Init<PyMemoryView>();
            Init<PyClass>();
        }

        #endregion

        #region Core Properties

        public string Name { get; private set; }
        public PyType[] BaseTypes { get; private set; }

        private PyType _tpBase;
        private TypeLayout _builtinLayout;

        // tp_new of a type, as the __new__ object its own dict or a tp_base's holds; slot is true for
        // slot_tp_new, which a class that writes __new__ has. Builtins that share one C tp_new
        // (BaseException_new) share the object.
        private static PyObject TpNew(PyType type, out bool slot)
        {
            for (var level = type; level != null; level = level.TpBase)
            {
                if (level is PyClass written && level.IsHeapType)
                {
                    if (written.ClassDict.TryGetValue("__new__", out var declared))
                    {
                        slot = true;
                        return declared;
                    }
                    continue;
                }
                if (level.TypeDict != null && level.TypeDict.TryGetValue("__new__", out var builtin))
                {
                    slot = false;
                    return builtin is PyNewMethodDescriptor { TpNew: { } shared } ? shared : builtin;
                }
            }
            slot = false;
            return null;
        }

        // A builtin type's __new__, the tp_new_wrapper method of tp_new_methoddef.
        internal static bool IsBuiltinNew(PyObject value) =>
            value is PyNewMethodDescriptor || value is PyStaticBuiltinMethod { Name: "__new__", HasOwner: true };

        /// <summary>
        /// update_one_slot (Objects/typeobject.c) for __new__: a class whose dict holds a builtin
        /// type's __new__ keeps the tp_new it inherited, and type_call runs that directly, without
        /// tp_new_wrapper's checks. Null when the inherited tp_new is a Python __new__.
        /// </summary>
        internal static PyObject InheritedBuiltinNew(PyClass type)
        {
            bool slot = false;
            var inherited = type.TpBase == null ? null : TpNew(type.TpBase, out slot);
            return inherited != null && !slot ? inherited : null;
        }

        // type->tp_new(subtype, args, kwds) from C, which tp_new_wrapper does not stand in front of.
        internal static PyObject CallTpNew(PyObject tpNew, PyObject[] args, PyDict kwargs) => tpNew switch
        {
            PyNewMethodDescriptor descriptor => descriptor.CallTpNew(args, kwargs),
            PyStaticBuiltinMethod method => method.Method(args, kwargs),
            _ => tpNew.Call(args, kwargs),
        };

        /// <summary>
        /// tp_new_wrapper (Objects/typeobject.c): the checks of type.__new__(subtype, ...) before the
        /// type's tp_new runs. The subtype must be a type and a subtype, and its most derived base
        /// whose tp_new is not slot_tp_new must have this tp_new, so object.__new__(dict) is refused.
        /// </summary>
        internal static void CheckNewArguments(PyType type, PyObject[] args)
        {
            string name = TpName(type);
            if (args.Length < 1)
                throw PyTypeError.Create($"{name}.__new__(): not enough arguments");
            if (args[0] is not PyType subtype)
                throw PyTypeError.Create($"{name}.__new__(X): X is not a type object ({args[0].GetTypeName()})");
            if (!subtype.IsSubclassOf(type))
                throw PyTypeError.Create($"{name}.__new__({TpName(subtype)}): {TpName(subtype)} is not a subtype of {name}");
            var staticBase = subtype;
            while (staticBase != null && TpNew(staticBase, out bool slot) != null && slot)
                staticBase = staticBase.TpBase;
            if (staticBase != null && !ReferenceEquals(TpNew(staticBase, out _), TpNew(type, out _)))
                throw PyTypeError.Create($"{name}.__new__({TpName(subtype)}) is not safe, use {TpName(staticBase)}.__new__()");
        }

        /// <summary>
        /// Py_TPFLAGS_HEAPTYPE, the one answer to "is this a heap type": a class statement or type()
        /// call, or PyStructSequence_NewType (type itself is not one).
        /// </summary>
        internal bool IsHeapType => HasFeature(PyTypeFlags.HeapType);

        // PyType_FromSpec of PyStructSequence_NewType: a heap type made from a spec.
        // A heap type is mutable unless its spec says otherwise: the static default is undone.
        internal void MarkHeapType() => _flags = (_flags | PyTypeFlags.HeapType) & ~PyTypeFlags.ImmutableType;

        // type_new made it: a class statement or a type() call, whose tp_base is best_base.
        private bool MadeByTypeNew => this is PyClass && this is not PyTypeMetaclass;

        /// <summary>tp_base: best_base of a class, the base of a builtin.</summary>
        internal PyType TpBase => _tpBase ??= MadeByTypeNew
            ? PyTypeMetaclass.BestBase(BaseTypes)
            : BaseTypes.Length > 0 ? BaseTypes[0] : null;

        internal virtual TypeLayout Layout => _builtinLayout ??= TypeLayouts.Of(this);

        // PyType_FromSpec: the basicsize and flags of a module's spec, which the generated table of
        // static types does not hold.
        internal void SetSpecLayout(TypeLayout layout) => _builtinLayout = layout;

        /// <summary>tp_name of a type that keeps its module in it, as a compiled one does.</summary>
        // tp_name: a heap type's is its name; a static type of a C module's is "module.name".
        internal virtual string FullName => this is PyClass || IsHeapType || string.IsNullOrEmpty(Module) || Module == "builtins" ? Name : Module + "." + Name;

        /// <summary>type_set_bases (Objects/typeobject.c): new bases, then a new MRO, here and in
        /// every subclass, as mro_hierarchy does.</summary>
        // A metaclass's mro() may set __bases__ again while this runs, so at each point, as
        // type_set_bases does, only what is still this call's own is touched.
        internal void SetBases(PyType[] bases)
        {
            var oldBases = BaseTypes;
            var oldTpBase = TpBase;
            var undo = new List<(PyType Type, List<PyType> Old, List<PyType> New)>();
            BaseTypes = bases;
            _tpBase = null;
            _cachedBasesTuple = null;
            try
            {
                Relinearize(undo);
            }
            catch
            {
                for (int i = undo.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(undo[i].Type.MRO, undo[i].New))
                        undo[i].Type.SetMro(undo[i].Old);
                if (ReferenceEquals(BaseTypes, bases))
                {
                    BaseTypes = oldBases;
                    _tpBase = oldTpBase;
                    _cachedBasesTuple = null;
                }
                throw;
            }
            if (ReferenceEquals(BaseTypes, bases))
            {
                foreach (var oldBase in oldBases) oldBase.RemoveSubclass(this);
                foreach (var newBase in bases) newBase.AddSubclass(this);
            }
        }

        // mro_hierarchy and mro_internal: reentrance has already done this type and those below it.
        private void Relinearize(List<(PyType, List<PyType>, List<PyType>)> undo)
        {
            var old = MRO;
            var order = ComputeMro();
            if (!ReferenceEquals(MRO, old))
                return;
            SetMro(order);
            undo.Add((this, old, order));
            foreach (var subclass in LiveSubclasses()) subclass.Relinearize(undo);
        }

        /// <summary>
        /// tp_mro is NULL until the first mro() has answered. The MRO itself has to be there
        /// sooner, for what a class does while it is built, so only __mro__ says so.
        /// </summary>
        internal bool MroPending { get; set; }

        /// <summary>mro_invoke</summary>
        protected virtual List<PyType> ComputeMro() => CalculateC3MRO();

        /// <summary>mro_internal when the metaclass overrides mro(): what it answered is the MRO.</summary>
        internal void SetMro(List<PyType> mro)
        {
            MRO = mro;
            _cachedMroTuple = null;
            InvalidateTypeCache();
        }

        // Py_TPFLAGS_SEQUENCE and Py_TPFLAGS_MAPPING, which MATCH_SEQUENCE and MATCH_MAPPING read
        internal const int TPFLAGS_SEQUENCE = 1 << 5;
        internal const int TPFLAGS_MAPPING = 1 << 6;
        internal const int COLLECTION_FLAGS = TPFLAGS_SEQUENCE | TPFLAGS_MAPPING;

        // The SEQUENCE and MAPPING bits of tp_flags (set by PyType_Ready, by _abc_init and by
        // set_collection_flag_recursive), as the int MATCH_SEQUENCE and MATCH_MAPPING test.
        internal int CollectionFlags
        {
            get => (int)(_flags & CollectionBits);
            set => _flags = (_flags & ~CollectionBits) | ((PyTypeFlags)(ulong)(uint)value & CollectionBits);
        }

        // set_collection_flag_recursive
        internal void SetCollectionFlagRecursive(int flag)
        {
            if (GetType() == typeof(PyType) || CollectionFlags == flag)
                return;
            CollectionFlags = flag;
            foreach (var subclass in LiveSubclasses())
                subclass.SetCollectionFlagRecursive(flag);
        }

        // tp_subclasses: weak, a class that is gone leaves no entry.
        private readonly List<WeakReference<PyType>> _subclasses = new List<WeakReference<PyType>>();

        private void AddSubclass(PyType subclass)
        {
            lock (_subclasses) _subclasses.Add(new WeakReference<PyType>(subclass));
        }

        private void RemoveSubclass(PyType subclass)
        {
            lock (_subclasses)
                _subclasses.RemoveAll(entry => !entry.TryGetTarget(out var live) || ReferenceEquals(live, subclass));
        }

        internal List<PyType> LiveSubclasses()
        {
            var live = new List<PyType>();
            lock (_subclasses)
            {
                _subclasses.RemoveAll(entry => !entry.TryGetTarget(out _));
                foreach (var entry in _subclasses)
                    if (entry.TryGetTarget(out var subclass)) live.Add(subclass);
            }
            return live;
        }

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
            // CPython: Objects/typeobject.c:2789, after check_duplicates and before best_base.
            // type_new's check: a static builtin is wired to its base by its spec, never refused.
            if (MadeByTypeNew)
                CheckBaseTypeAcceptable();
            MRO = CalculateC3MRO();
            foreach (var baseType in BaseTypes) baseType.AddSubclass(this);
            // tp_flags: static spec bits plus what inherit_special propagates from the base
            // (Objects/typeobject.c:6726).
            _flags = ComputeFlags();

            // CPython 3.12: Assign version tag for method cache
            _typeVersionTag = _nextVersionTag++;

            // CPython 3.12: Initialize tp_dict (unified type dictionary)
            TypeDict = new PyTypeDictionary();

            // type_ready_fill_dict of a static type: its tp_methods, tp_members and tp_getset. A type
            // that type_new made starts from its class body alone, whatever its name.
            if (!MadeByTypeNew)
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


        #endregion

        #region C3 Linearization Algorithm

        internal List<PyType> CalculateC3MRO()
        {
            // object is the one type without a base; type is the metaclass itself. A class that
            // type_new made under either name is linearized like any other.
            if (BaseTypes.Length == 0 && !MadeByTypeNew)
            {
                return new List<PyType> { this };
            }

            if (this is PyTypeMetaclass)
            {
                return new List<PyType> { this, ObjectType };
            }

            // mro_implementation
            foreach (var parent in BaseTypes)
                if (parent.MroPending)
                    throw PyTypeError.Create($"Cannot extend an incomplete type '{parent.Name}'");

            // mro_implementation: check_duplicates, then C3Merge, which raises its own TypeError.
            CheckDuplicateBases();
            return C3Linearize(this);
        }

        /// <summary>
        /// CPython 3.12: typeobject.c check_duplicates(). Reports the first repeated base
        /// as "duplicate base class X" using object identity (CPython pointer comparison).
        /// </summary>
        private void CheckDuplicateBases()
        {
            for (int i = 0; i < BaseTypes.Length; i++)
            {
                for (int j = i + 1; j < BaseTypes.Length; j++)
                {
                    if (ReferenceEquals(BaseTypes[i], BaseTypes[j]))
                    {
                        throw PyTypeError.Create($"duplicate base class {BaseTypes[i].Name}");
                    }
                }
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
                    // CPython 3.12: typeobject.c set_mro_error() - report the unique heads of
                    // the remaining sequences as "... for bases A, B". Must be a Python
                    // TypeError so user code can catch it, never a raw CLR exception.
                    var names = new List<string>();
                    var seenHeads = new List<PyType>();
                    for (int s = 0; s < sequences.Count; s++)
                    {
                        var seq = sequences[s];
                        if (seq.Count > 0 && !seenHeads.Contains(seq[0]))
                        {
                            seenHeads.Add(seq[0]);
                            names.Add(seq[0].Name);
                        }
                    }
                    throw PyTypeError.Create($"Cannot create a consistent method resolution\norder (MRO) for bases {string.Join(", ", names)}");
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
        /// <summary>
        /// True for native types that CPython creates as heap types, and therefore lets Python
        /// reassign __module__ on. io.UnsupportedOperation is the measured case: Lib/io.py ends with
        /// UnsupportedOperation.__module__ = "io", which CPython accepts while it rejects the same
        /// assignment on ValueError, OSError, _io._IOBase and _io.StringIO.
        /// </summary>

        // type_setattro is the slot of every type; a metaclass's __setattr__/__delattr__ is its
        // own slot in PyObject_SetAttr.
        // type_setattro copies a str subclass to an exact str before it touches tp_dict.
        protected internal override void TpSetattro(PyString name, PyObject value) => TypeSetAttribute(name.Value, value);

        protected internal override void TpDelattro(PyString name) => TypeSetAttribute(name.Value, null);

        protected override void PySetAttribute(PyString name, PyObject value) => TypeSetAttribute(name.Value, value);

        protected override void PyDelAttribute(PyString name) => TypeSetAttribute(name.Value, null);

        internal void TypeDeleteAttribute(string name) => TypeSetAttribute(name, null);

        /// <summary>
        /// type_setattro (Objects/typeobject.c): Py_TPFLAGS_IMMUTABLETYPE refuses writes and
        /// deletions alike; then _PyObject_GenericSetAttrWithDict(type, name, value, NULL), a data
        /// descriptor of the metatype before tp_dict; then PyType_Modified. value null deletes.
        /// </summary>
        internal void TypeSetAttribute(string name, PyObject value)
        {
            if (HasFeature(PyTypeFlags.ImmutableType))
            {
                throw PyTypeError.Create($"cannot set '{name}' attribute of immutable type '{FullName}'");
            }
            // _PyType_Lookup(Py_TYPE(type), name): a data descriptor of the metatype sets.
            if (PyClassInstance.FindDataDescriptorInMro(GetPyType(), name) is { } descriptor)
            {
                if (value == null)
                    PyClassInstance.CallDescriptorDelete(descriptor, this);
                else
                    PyClassInstance.CallDescriptorSet(descriptor, this, value);
                return;
            }
            if (value == null)
            {
                if (!TypeDict.ContainsKey(name))
                    throw PyAttributeError.Create($"type object '{FullName}' has no attribute '{name}'");
                TypeDict.Remove(name);
            }
            else
            {
                TypeDict[name] = value;
            }
            // __del__ may be assigned to a class after instances of it already exist. C decides
            // tp_finalize at type creation (Objects/typeobject.c:9499); SharpPy's cached answer is
            // versioned by the global epoch so subclasses pick it up as well.
            if (name == "__del__")
                SharpPy.PyClass.FinalizerEpoch++;
            // PyType_Modified
            InvalidateTypeCache();
        }

        /// <summary>
        /// Whether instances of this type can be the target of a weak reference.
        /// CPython: tp_weaklistoffset != 0 (Objects/typeobject.c:2960).
        /// </summary>
        internal virtual bool SupportsWeakReference()
        {
            return HasFeature(PyTypeFlags.ManagedWeakRef);
        }

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

#if DEBUG
            Console.WriteLine($"[IsSubclassOf] No match found, returning false");
#endif
            return false;
        }

        // type은 항상 호출 가능 (인스턴스 생성)
        public override bool IsCallable() => true;

        // type 호출 - 인스턴스 생성 또는 타입 조회
        public override PyObject Call(PyObject[] args, PyDict kwargs = null) => TypeCall(args, kwargs);

        /// <summary>type_call, the tp_call of type: what calling any type does after a metaclass's own __call__.</summary>
        internal PyObject TypeCall(PyObject[] args, PyDict kwargs)
        {
            if (Layout.NoInstances)
                throw PyTypeError.Create($"cannot create '{FullName}' instances");
            // type_vectorcall / type_call: type(x) is Py_TYPE(x); otherwise type() takes three arguments.
            if (this is PyTypeMetaclass)
            {
                if (args.Length == 1)
                    return kwargs == null || kwargs.Length() == 0
                        ? args[0].GetPyType()
                        : throw PyTypeError.Create("type() takes no keyword arguments");
                if (args.Length != 3)
                    throw PyTypeError.Create("type() takes 1 or 3 arguments");
            }
            var newSlot = FindNameInMro("__new__", out bool newWritten);
            if (this is PyClass ||
                (newSlot != null && (!ReferenceEquals(newSlot, ObjectNewDescriptor()) || ReferenceEquals(this, ObjectType))))
                return TypeCall(newSlot, newWritten, args, kwargs);

            // 일반 타입 호출 - 인스턴스 생성
            return CreateInstance(args, kwargs);
        }

        /// <summary>
        /// type_call (Objects/typeobject.c:1627): tp_new, then, when what came back is an instance of
        /// the type, the tp_init of Py_TYPE(obj). One path for builtin and heap types.
        /// </summary>
        private PyObject TypeCall(PyObject newSlot, bool newWritten, PyObject[] args, PyDict kwargs)
        {
            if (newSlot == null)
                throw PyTypeError.Create($"cannot create '{Name}' instances");
            var slotArgs = new PyObject[args.Length + 1];
            slotArgs[0] = this;
            Array.Copy(args, 0, slotArgs, 1, args.Length);
            PyObject obj;
            // update_one_slot: a builtin __new__ written into a class body leaves the inherited
            // tp_new in place.
            if (newWritten && this is PyClass heap && IsBuiltinNew(newSlot) && InheritedBuiltinNew(heap) is { } inheritedNew)
                obj = CallTpNew(inheritedNew, slotArgs, kwargs);
            else
                obj = SlotNew(newSlot).Call(slotArgs, kwargs);
            // Filed on the object that came out of __new__, not on the class that was called: a
            // __new__ can return a foreign object, and that object has no hook of the called class.
            PyFinalizer.RegisterIfDeclared(obj);

            var objType = obj.GetPyType();
            if (!ReferenceEquals(objType, this) && !objType.IsSubclassOf(this))
                return obj;
            var init = objType.FindNameInMro("__init__");
            if (init == null || ReferenceEquals(init, ObjectInitDescriptor()))
                return obj;
            PyObject result;
            if (init is PyFunction or PyWrapperDescriptor or PyMethodDescriptor)
            {
                slotArgs[0] = obj;
                result = init.Call(slotArgs, kwargs);
            }
            else
                result = obj.BindSpecialMethod(init).Call(args, kwargs);
            // slot_tp_init
            if (result != null && result is not PyNone)
                throw PyTypeError.Create($"__init__() should return None, not '{result.GetTypeName()}'");
            return obj;
        }

        /// <summary>slot_tp_new reads __new__ off the type, so a descriptor there binds.</summary>
        private PyObject SlotNew(PyObject newMethod)
        {
            if (newMethod is PyFunction or PyStaticBuiltinMethod or PyMethodDescriptor)
                return newMethod;
            if (newMethod is IDescriptor newDescriptor)
                return newDescriptor.Get(null, this);
            var newGet = newMethod.LookupSpecialMethodUncached("__get__");
            return newGet != null
                ? newMethod.BindSpecialMethod(newGet).Call(new PyObject[] { PyNone.Instance, this }, null)
                : newMethod;
        }

        internal bool DerivesFromType(PyType wanted)
        {
            if (ReferenceEquals(this, wanted))
            {
                return true;
            }
            var mro = MRO;
            if (mro != null)
            {
                foreach (var t in mro)
                {
                    if (ReferenceEquals(t, wanted))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>tp_alloc of a builtin type allocated through CreateInstance, null for others.</summary>
        internal Func<PyObject[], PyDict, PyObject> Allocator;

        /// <summary>
        /// tp_alloc and the C initialiser of each builtin exception type (Objects/exceptions.c): what
        /// BaseException_new allocates for the type, found on the type itself rather than by name.
        /// </summary>
        internal static void InstallExceptionAllocators()
        {
            BaseExceptionType.Allocator = (args, kwargs) => new PyBaseException(args, kwargs);
            ExceptionType.Allocator = (args, kwargs) => new PyException(args, kwargs);
            WarningType.Allocator = (args, kwargs) => new PyWarning(args, kwargs);
            UserWarningType.Allocator = (args, kwargs) => new PyUserWarning(args, kwargs);
            DeprecationWarningType.Allocator = (args, kwargs) => new PyDeprecationWarning(args, kwargs);
            PendingDeprecationWarningType.Allocator = (args, kwargs) => new PyPendingDeprecationWarning(args, kwargs);
            SyntaxWarningType.Allocator = (args, kwargs) => new PySyntaxWarning(args, kwargs);
            RuntimeWarningType.Allocator = (args, kwargs) => new PyRuntimeWarning(args, kwargs);
            FutureWarningType.Allocator = (args, kwargs) => new PyFutureWarning(args, kwargs);
            ImportWarningType.Allocator = (args, kwargs) => new PyImportWarning(args, kwargs);
            UnicodeWarningType.Allocator = (args, kwargs) => new PyUnicodeWarning(args, kwargs);
            BytesWarningType.Allocator = (args, kwargs) => new PyBytesWarning(args, kwargs);
            ResourceWarningType.Allocator = (args, kwargs) => new PyResourceWarning(args, kwargs);
            EncodingWarningType.Allocator = (args, kwargs) => new PyEncodingWarning(args, kwargs);
            StopAsyncIterationType.Allocator = (args, kwargs) => new PyStopAsyncIteration(args, kwargs);
            EOFErrorType.Allocator = (args, kwargs) => new PyEOFError(args, kwargs);
            ValueErrorType.Allocator = (args, kwargs) => new PyValueError(args, kwargs);
            TypeErrorType.Allocator = (args, kwargs) => new PyTypeError(args, kwargs);
            AttributeErrorType.Allocator = (args, kwargs) => new PyAttributeError(args, kwargs);
            NameErrorType.Allocator = (args, kwargs) => new PyNameError(args, kwargs);
            UnboundLocalErrorType.Allocator = (args, kwargs) => new PyUnboundLocalError(args, kwargs);
            ArithmeticErrorType.Allocator = (args, kwargs) => new PyArithmeticError(args, kwargs);
            FloatingPointErrorType.Allocator = (args, kwargs) => new PyFloatingPointError(args, kwargs);
            TabErrorType.Allocator = (args, kwargs) => new PyTabError(args, kwargs);
            MemoryErrorType.Allocator = (args, kwargs) => new PyMemoryError(args, kwargs);
            UnicodeErrorType.Allocator = (args, kwargs) => new PyUnicodeError(args, kwargs);
            UnicodeEncodeErrorType.Allocator = (args, kwargs) => new PyUnicodeEncodeError(args, kwargs);
            UnicodeDecodeErrorType.Allocator = (args, kwargs) => new PyUnicodeDecodeError(args, kwargs);
            UnicodeTranslateErrorType.Allocator = (args, kwargs) => new PyUnicodeTranslateError(args, kwargs);
            // Objects/exceptions.c ReferenceError/BufferError are builtins names, so calling them
            // has to build the exception like every other one. BufferError was registered in
            // builtins but missing here, so BufferError('x') raised NotImplementedError instead.
            ZeroDivisionErrorType.Allocator = (args, kwargs) => new PyZeroDivisionError(args, kwargs);
            ReferenceErrorType.Allocator = (args, kwargs) => new PyReferenceError(args, kwargs);
            BufferErrorType.Allocator = (args, kwargs) => new PyBufferError(args, kwargs);
            OverflowErrorType.Allocator = (args, kwargs) => new PyOverflowError(args, kwargs);
            LookupErrorType.Allocator = (args, kwargs) => new PyLookupError(args, kwargs);
            IndexErrorType.Allocator = (args, kwargs) => new PyIndexError(args, kwargs);
            KeyErrorType.Allocator = (args, kwargs) => new PyKeyError(args, kwargs);
            RuntimeErrorType.Allocator = (args, kwargs) => new PyRuntimeError(args, kwargs);
            SystemErrorType.Allocator = (args, kwargs) => new PySystemError(args, kwargs);
            NotImplementedErrorType.Allocator = (args, kwargs) => new PyNotImplementedError(args, kwargs);
            RecursionErrorType.Allocator = (args, kwargs) => new PyRecursionError(args, kwargs);
            ImportErrorType.Allocator = (args, kwargs) => new PyImportError(args, kwargs);
            ModuleNotFoundErrorType.Allocator = (args, kwargs) => new PyModuleNotFoundError(args, kwargs);
            SyntaxErrorType.Allocator = (args, kwargs) => new PySyntaxError(args, kwargs);
            IndentationErrorType.Allocator = (args, kwargs) => new PyIndentationError(args, kwargs);
            // The argument survives: SystemExit(3).args is (3,) and .code is 3
            // (Objects/exceptions.c SystemExit_init), which is what decides the status later.
            SystemExitType.Allocator = (args, kwargs) => new PySystemExit(args, kwargs);
            // No constructor of its own: BaseException_init keeps the arguments, so
            // KeyboardInterrupt().args is () and KeyboardInterrupt('x', 1).args is ('x', 1).
            KeyboardInterruptType.Allocator = (args, kwargs) => new PyKeyboardInterrupt(args, kwargs);
            GeneratorExitType.Allocator = (args, kwargs) => new PyGeneratorExit(args, kwargs);
            StopIterationType.Allocator = (args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("StopIteration() takes no keyword arguments");
                return new PyStopIteration(args);
            };
            AssertionErrorType.Allocator = (args, kwargs) => new PyAssertionError(args, kwargs);
            ConnectionErrorType.Allocator = (args, kwargs) => new PyConnectionError(args, kwargs);
            BrokenPipeErrorType.Allocator = (args, kwargs) => new PyBrokenPipeError(args, kwargs);
            ConnectionAbortedErrorType.Allocator = (args, kwargs) => new PyConnectionAbortedError(args, kwargs);
            ConnectionRefusedErrorType.Allocator = (args, kwargs) => new PyConnectionRefusedError(args, kwargs);
            ConnectionResetErrorType.Allocator = (args, kwargs) => new PyConnectionResetError(args, kwargs);
            ChildProcessErrorType.Allocator = (args, kwargs) => new PyChildProcessError(args, kwargs);
            InterruptedErrorType.Allocator = (args, kwargs) => new PyInterruptedError(args, kwargs);
            ProcessLookupErrorType.Allocator = (args, kwargs) => new PyProcessLookupError(args, kwargs);
            TimeoutErrorType.Allocator = (args, kwargs) => new PyTimeoutError(args, kwargs);
            BlockingIOErrorType.Allocator = (args, kwargs) => new PyBlockingIOError(args, kwargs);
            OSErrorType.Allocator = (args, kwargs) => PyOSError.FromArguments(args, kwargs);
            PyUnsupportedOperation.UnsupportedOperationPyType.Allocator = (args, kwargs) => new PyUnsupportedOperation(args, kwargs);
            FileNotFoundErrorType.Allocator = (args, kwargs) => new PyFileNotFoundError(args, kwargs);
            PermissionErrorType.Allocator = (args, kwargs) => new PyPermissionError(args, kwargs);
            FileExistsErrorType.Allocator = (args, kwargs) => new PyFileExistsError(args, kwargs);
            NotADirectoryErrorType.Allocator = (args, kwargs) => new PyNotADirectoryError(args, kwargs);
            // Both names go to the one constructor transliteration
            // (Objects/exceptions.c BaseExceptionGroup_new). It used to have a copy here and
            // another one in PyScope, both of which quietly dropped members that were not
            // PyException, so BaseExceptionGroup('m', [KeyboardInterrupt()]) reported a group
            // with no members instead of refusing or keeping the member.
            IsADirectoryErrorType.Allocator = (args, kwargs) => new PyIsADirectoryError(args, kwargs);
            // BaseExceptionGroup_new, then BaseExceptionGroup_init, which refuses keywords
            // under the name of the type the group became.
            BaseExceptionGroupType.Allocator = (args, kwargs) =>
            {
                var group = PyBaseExceptionGroup.NewGroup(args, BaseExceptionGroupType);
                PyBaseException.BaseExceptionInit(group, args, kwargs);
                return group;
            };
            ExceptionGroupType.Allocator = (args, kwargs) =>
            {
                var group = PyBaseExceptionGroup.NewGroup(args, ExceptionGroupType);
                PyBaseException.BaseExceptionInit(group, args, kwargs);
                return group;
            };
        }

        // 인스턴스 생성 (기본 구현)
        public virtual PyObject CreateInstance(PyObject[] args, PyDict kwargs = null)
        {
            // The type's own tp_alloc: an exception type's struct (InstallExceptionAllocators).
            if (Allocator != null)
                return Allocator(args, kwargs);


            // bytes_new for bytes itself, which bytes.__new__ hands here.
            if (ReferenceEquals(this, BytesType))
                return PyBuiltinFunction.CallBytes(args, kwargs);
            throw PyTypeError.Create($"cannot create '{FullName}' instances");
        }

        /// <summary>
        /// find_name_in_mro (Objects/typeobject.c): each base's own dict in MRO order. A class's
        /// namespace is its ClassDict, then the TypeDict that carries what a builtin base installs.
        /// </summary>
        internal PyObject FindNameInMro(string name, out bool written)
        {
            foreach (var mroType in MRO)
            {
                var found = mroType.LookupOwnDict(name, out written);
                if (found != null)
                    return found;
            }
            written = false;
            return null;
        }

        /// <summary>
        /// This type's tp_dict alone, as find_name_in_mro and _super_lookup_descr read each base.
        /// </summary>
        internal PyObject LookupOwnDict(string name, out bool written)
        {
            // written: the dict of a class that type_new made, which a class body filled.
            written = false;
            if (TypeDict == null || !TypeDict.TryGetValue(name, out var value))
                return null;
            written = MadeByTypeNew;
            return value;
        }

        /// <summary>_PyType_Lookup. written tells whether a Python class body holds the name.</summary>
        internal PyObject FindNameInMro(string name) => FindNameInMro(name, out _);

        // _PyType_Lookup
        public PyObject LookupSpecial(string name) => FindNameInMro(name);

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
            InstallBuiltinRepr();
            switch (_kind)
            {
                case TypeKind.Str:
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
                    else if (Name == "tuple")
                        InitializeTupleTypeDescriptors();
                    else if (Name == "dict")
                        InitializeDictTypeDescriptors();
                    else if (Name == "mappingproxy")
                        InitializeMappingProxyTypeDescriptors();
                    else if (Name == "BaseException")
                        InitializeBaseExceptionTypeDescriptors();
                    else if (Name == "SystemExit")
                        InitializeSystemExitTypeDescriptors();
                    else if (PyExceptionSlots.FieldsOfType(Name) is string[] exceptionFields)
                        PyExceptionSlots.InstallFields(this, exceptionFields);
                    else if (Name == "slice")
                        TypeDict["indices"] = new PyMethodDescriptor("indices", this, PySlice.IndicesMethod);
                    break;
            }

            // CPython 3.12: Objects/typeobject.c:1410-1412
            // All types have __doc__ attribute (tp_doc)
            // If not already set by specific type initializer, default to None
            // The builtins fill in their tp_doc once the builtins module exists (PyDocs.SetTypeDocs).
            if (!TypeDict.ContainsKey("__doc__"))
            {
                TypeDict["__doc__"] = PyNone.Instance;
            }
        }

        /// <summary>
        /// int 타입의 descriptor 테이블 초기화 (CPython longobject.c 참조)
        /// </summary>
        private void InitializeIntTypeDescriptors()
        {
            var intType = this;

            // int.__repr__() - CPython Objects/longobject.c:long_to_decimal_string
            TypeDict["__repr__"] = new PyWrapperDescriptor(
                "__repr__",
                intType,
                (self, args, kwargs) => {
                    // long_to_decimal_string: the digits, whatever a subclass declares.
                    if (self is PyIntSubclass subclass) self = subclass.GetIntValue();
                    if (self is PyBool truth) return PyString.From(truth.Value ? "1" : "0");
                    var pyInt = (PyInt)self;
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
                    var pyInt = (PyInt)self;
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
                    var pyInt = (PyInt)self;
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
        /// <summary>
        /// CPython picks these messages with two predicates: whether tp_new is object_new and whether
        /// tp_init is object_init (Objects/typeobject.c:5416-5457). The SharpPy equivalent asks
        /// whether the MRO resolves the dunder to object's own descriptor, rather than to a user
        /// function or to a builtin base's implementation.
        /// </summary>
        private static PyObject ObjectNewDescriptor() =>
            ObjectType != null && ObjectType.TypeDict.TryGetValue("__new__", out var d) ? d : null;

        private static PyObject ObjectInitDescriptor() =>
            ObjectType != null && ObjectType.TypeDict.TryGetValue("__init__", out var d) ? d : null;

        /// <summary>
        /// CPython builds constructor and descriptor messages from type-&gt;tp_name, which is the
        /// class's own name. PyObject.GetTypeName() answers the type *of* the object, so for a class
        /// value it says 'type' and produces messages like "set.__new__(type): type is not a subtype
        /// of set" where CPython says int. Classes are asked for their own name here.
        /// </summary>
        internal static string TpName(PyObject cls) =>
            cls is PyType typeObject ? typeObject.FullName : cls.GetTypeName();

        /// <summary>
        /// set_reduce / frozenset_reduce (Objects/setobject.c): (type(self), (list(self),), state),
        /// state being the instance dict or None. Measured on the pinned interpreter:
        /// set([1, 2]).__reduce__() == (set, ([1, 2],), None).
        /// </summary>
        internal static PyObject BuildSetReduce(PyObject self, IEnumerable<PyObject> items)
        {
            var itemList = new PyList(items);
            var state = GetState(self);
            return PyTuple.From(new PyObject[]
            {
                self.GetPyType(),
                PyTuple.From(new PyObject[] { itemList }),
                state
            });
        }

        /// <summary>
        /// METH_NOARGS refusal. CPython has two shapes for this and they do not agree: the
        /// vectorcall path a direct call takes names the class that *defines* the method
        /// (measured S().__sizeof__(1) -> "set.__sizeof__() takes no arguments (1 given)"), while
        /// the generic call path used when a test unpacks a bound method names the receiving type
        /// ("S.__sizeof__() ..."). SharpPy follows the direct-call shape, which is what ordinary
        /// code hits; the other is recorded in docs/known_gaps.md P0-45.
        /// </summary>
        internal static void RefuseExtra(PyType defining, string name, PyObject[] args)
        {
            if (args.Length == 0) return;
            throw PyTypeError.Create(
                $"{defining.Name}.{name}() takes no arguments ({args.Length} given)");
        }


        private PyString _nameString;
        private PyString _qualNameString;

        /// <summary>
        /// ht_name (Objects/typeobject.c:994): the name string a heap type was created with, kept so
        /// that repeated type.__name__ reads return the same object. Static builtin types do not use
        /// this - CPython allocates for those on every read (:998).
        /// </summary>
        internal PyString NameString => _nameString ??= PyString.From(Name);

        /// <summary>type_set_name replaces tp_name and ht_name, not a dict entry. ht_qualname is a
        /// separate field and stays what it was, so it is fixed before the name moves.</summary>
        internal void SetName(PyString value)
        {
            _ = QualNameString;
            Name = value.Value;
            _nameString = value;
        }

        /// <summary>ht_qualname, held separately because CPython holds it separately. type_new_set_ht_name
        /// fills it from the class body and removes that entry from the dict (Objects/typeobject.c:3416),
        /// type_set_qualname (:1044) replaces it on assignment.</summary>
        internal PyString QualNameString => _qualNameString ??= PyString.From(Name);
        internal string QualNameText => QualNameString.Value;
        internal void SetQualName(string value) => _qualNameString = PyString.From(value);

        internal static int CountItems(IEnumerable<PyObject> items)
        {
            int n = 0;
            foreach (var _ in items) n++;
            return n;
        }

        /// <summary>
        /// set_sizeof (Objects/setobject.c). The pinned interpreter reports 200 for an empty set and
        /// still 200 with two items, because the initial table is part of the object. Past that,
        /// SharpPy's storage is a CLR hash set rather than a C table, so the growth term is an
        /// estimate and not the allocator's figure (docs/known_gaps.md P0-45).
        /// </summary>
        internal static long SetSizeInBytes(int count)
        {
            if (count <= 4) return 200;
            long table = 8;
            while (count * 5 >= table * 3) table *= 4;
            return 200 + table * 16;
        }

        // tp_new is object_new unless a level below object has another __new__: update_one_slot
        // leaves the inherited slot when a class's __new__ is a builtin's tp_new_wrapper.
        internal static bool TypeNewIsObjectNew(PyType type)
        {
            var objectNew = ObjectNewDescriptor();
            foreach (var level in type.MRO)
            {
                if (ReferenceEquals(level, ObjectType))
                    break;
                if (level is PyClass python)
                {
                    if (python.ClassDict.TryGetValue("__new__", out var found) && !ReferenceEquals(found, objectNew))
                        return false;
                }
                else if (level.TypeDict.ContainsKey("__new__"))
                {
                    return false;
                }
            }
            return true;
        }

        internal static bool TypeInitIsObjectInit(PyType type) =>
            ReferenceEquals(type is PyClass cls ? cls.FindNameInMro("__init__") : type.LookupSpecial("__init__"), ObjectInitDescriptor());

        private static PyObject AttributeOrNull(PyObject self, string name)
        {
            try
            {
                return self.GetAttribute(name);
            }
            catch (PythonException e) when (e.PyException is PyAttributeError)
            {
                return null;
            }
        }

        /// <summary>
        /// object_getstate_default (Objects/typeobject.c): __dict__, with the slots beside it. When the
        /// state is required (no new arguments, not a list or dict), an object whose struct holds more
        /// than its dict, weakref and slots cannot be rebuilt from it. The struct sizes are CPython's
        /// for the builtin types (TypeLayout.g.cs); a class is laid out by SharpPy and is not judged.
        /// </summary>
        /// <summary>
        /// object_getstate, _PyObject_GetState (Objects/typeobject.c): the object's __getstate__;
        /// object's own also gets the required flag.
        /// </summary>
        internal static PyObject GetState(PyObject self, bool required = false)
        {
            var getState = self.GetAttribute("__getstate__");
            return getState is PyBoundMethodDescriptor { MethodDescriptor: var defining } bound
                && ReferenceEquals(bound.Instance, self) && ReferenceEquals(defining, ObjectType.TypeDict["__getstate__"])
                ? ObjectGetState(self, required)
                : getState.Call(Array.Empty<PyObject>(), null);
        }

        private static PyObject ObjectGetState(PyObject self, bool required = false)
        {
            var type = self.GetPyType();
            if (required && type is not SharpPy.PyClass)
            {
                var layout = type.Layout;
                if (layout.ItemSize != 0)
                    throw PyTypeError.Create($"cannot pickle {type.FullName} objects");
                int basicSize = ObjectType.Layout.BasicSize;
                if (layout.DictOffset > 0) basicSize += 8;
                if (layout.WeakOffset > 0) basicSize += 8;
                basicSize += 8 * (layout.Slots?.Length ?? 0);
                if (layout.BasicSize > basicSize)
                    throw PyTypeError.Create($"cannot pickle '{type.FullName}' object");
            }
            var state = AttributeOrNull(self, "__dict__") ?? PyNone.Instance;
            if (state is PyDict { } instanceDict && instanceDict.Length() == 0) state = PyNone.Instance;
            var names = (PyList)PyImportSystem.Import("copyreg").GetAttribute("_slotnames")
                .Call(new PyObject[] { self.GetPyType() }, null);
            var slots = new PyDict();
            foreach (var name in names.Items)
            {
                var value = AttributeOrNull(self, ((PyString)name).Value);
                if (value != null) slots.SetItem(name, value);
            }
            return slots.Length() == 0 ? state : PyTuple.From(new[] { state, slots });
        }

        /// <summary>_common_reduce: protocol 2 and up is reduce_newobj; below that, copyreg._reduce_ex.</summary>
        private static PyObject CommonReduce(PyObject self, int protocol)
        {
            // reduce_newobj: a type without tp_new has nothing to rebuild with.
            if (self.GetPyType().Layout.NoInstances)
                throw PyTypeError.Create($"cannot pickle '{self.GetPyType().FullName}' object");
            var copyreg = PyImportSystem.Import("copyreg");
            if (protocol < 2)
                return copyreg.GetAttribute("_reduce_ex").Call(new[] { self, PyInt.From(protocol) }, null);

            var type = self.GetPyType();
            var arguments = new List<PyObject> { type };
            PyObject factory = copyreg.GetAttribute("__newobj__");
            // _PyObject_GetNewArguments: looked up on the type, and checked.
            var newArgsEx = type.LookupSpecial("__getnewargs_ex__");
            var newArgs = type.LookupSpecial("__getnewargs__");
            if (newArgsEx != null)
            {
                var answer = self.CallSpecial(newArgsEx);
                if (answer is not PyTuple pair)
                    throw PyTypeError.Create($"__getnewargs_ex__ should return a tuple, not '{answer.GetTypeName()}'");
                if (pair.Items.Length != 2)
                    throw PyValueError.Create($"__getnewargs_ex__ should return a tuple of length 2, not {pair.Items.Length}");
                if (pair.Items[0] is not PyTuple)
                    throw PyTypeError.Create($"first item of the tuple returned by __getnewargs_ex__ must be a tuple, not '{pair.Items[0].GetTypeName()}'");
                if (pair.Items[1] is not PyDict)
                    throw PyTypeError.Create($"second item of the tuple returned by __getnewargs_ex__ must be a dict, not '{pair.Items[1].GetTypeName()}'");
                // reduce_newobj: keywords are what needs __newobj_ex__.
                if (pair.Items[1].Length() == 0)
                {
                    arguments.AddRange(((PyTuple)pair.Items[0]).Items);
                }
                else
                {
                    factory = copyreg.GetAttribute("__newobj_ex__");
                    arguments.Add(pair.Items[0]);
                    arguments.Add(pair.Items[1]);
                }
            }
            else if (newArgs != null)
            {
                var answer = self.CallSpecial(newArgs);
                if (answer is not PyTuple values)
                    throw PyTypeError.Create($"__getnewargs__ should return a tuple, not '{answer.GetTypeName()}'");
                arguments.AddRange(values.Items);
            }

            bool required = newArgsEx == null && newArgs == null && !type.IsSubclassOf(ListType) && !type.IsSubclassOf(DictType)
                && !ReferenceEquals(type, ListType) && !ReferenceEquals(type, DictType);
            var state = GetState(self, required);
            PyObject listItems = type.IsSubclassOf(ListType) ? self.GetIterator() : PyNone.Instance;
            PyObject dictItems = type.IsSubclassOf(DictType)
                ? self.GetAttribute("items").Call(Array.Empty<PyObject>(), null).GetIterator() : PyNone.Instance;
            return PyTuple.From(new[] { factory, PyTuple.From(arguments.ToArray()), state, listItems, dictItems });
        }

        /// <summary>tp_repr of the containers as a slot wrapper: dict.__repr__(self) is the base's text whatever
        /// a subclass declares, so it reads the stored contents and not the virtual ToRepr.</summary>
        private void InstallBuiltinRepr()
        {
            Func<PyObject, PyObject> repr = Name switch
            {
                // The value's own repr function: Py_ReprEnter marks that object, not a copy of it.
                "dict" => self => (self as PyDict).Repr(),
                "list" => self => (self as PyList).Repr(),
                "tuple" => self => ((PyTuple)self).Repr(),
                "set" => self => (self as PySet).Repr(),
                "frozenset" => self => (self as PyFrozenSet).Repr(),
                "bool" => self => self.Repr(),
                _ => null,
            };
            if (repr != null)
                TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", this, (self, args, kwargs) => repr(self));
        }

        // hackcheck: object's slot may not jump over type_setattro.
        internal static void Hackcheck(PyObject self, string what)
        {
            if (self is PyType)
                throw PyTypeError.Create($"can't apply this {what} to {self.GetPyType().Name} object");
        }

        private void InitializeObjectTypeDescriptors()
        {
            var objectType = this;

            TypeDict["__getstate__"] = new PyMethodDescriptor("__getstate__", objectType,
                (self, args, kwargs) => ObjectGetState(self), 0, 0);
            TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", objectType,
                (self, args, kwargs) =>
                {
                    return CommonReduce(self, 0);
                }, 0, 0);
            TypeDict["__reduce_ex__"] = new PyMethodDescriptor("__reduce_ex__", objectType,
                (self, args, kwargs) =>
                {
                    // object.__reduce_ex__(protocol: int): the int converter
                    int protocol = PyArgs.Int(args[0]);
                    // object___reduce_ex___impl: a class that overrides __reduce__ decides.
                    foreach (var level in self.GetPyType().MRO)
                    {
                        if (ReferenceEquals(level, objectType)) break;
                        if ((level as PyClass)?.ClassDict.ContainsKey("__reduce__") == true ||
                            level.TypeDict.ContainsKey("__reduce__"))
                            return self.GetAttribute("__reduce__").Call(Array.Empty<PyObject>(), null);
                    }
                    return CommonReduce(self, protocol);
                }, 1, 1);

            // object.__init__() - CPython object_init
            TypeDict["__init__"] = new PyWrapperDescriptor(
                "__init__",
                objectType,
                (self, args, kwargs) => {
                    // CPython object_init: object.__init__(A(), 1) is
                    // "A.__init__() takes exactly one argument (the instance to initialize)", while
                    // a type that has its own __init__ gets "object.__init__() takes exactly one
                    // argument (the instance to initialize)". SharpPy accepted the extras and
                    // returned None.
                    if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                    {
                        var selfType = self.GetPyType();
                        if (!TypeInitIsObjectInit(selfType))
                            throw PyTypeError.Create(
                                "object.__init__() takes exactly one argument (the instance to initialize)");
                        if (TypeNewIsObjectNew(selfType))
                            throw PyTypeError.Create(
                                $"{selfType.Name}.__init__() takes exactly one argument (the instance to initialize)");
                    }
                    return PyNone.Instance;
                },
                minArgs: 0,
                maxArgs: int.MaxValue
            );

            // object.__new__(cls) - CPython Objects/typeobject.c:5444-5515 (object_new)
            // Note: PyMethodDescriptor extracts first arg as 'self', which is the 'cls' for __new__
            TypeDict["__new__"] = new PyNewMethodDescriptor(
                "__new__",
                objectType,
                (self, args, kwargs) => {
                    // CPython 3.12: __new__ is a static method, but exposed as method_descriptor
                    // 'self' parameter here is actually 'cls' (the class to instantiate)
                    // args contains additional arguments passed to __new__ (should be empty for object.__new__)

                    // CPython object_new is reached here for object.__new__(A, 1) as well, and it
                    // refuses the arguments: measured "A() takes no arguments" when A's tp_new is
                    // object_new and A has no __init__ of its own, and "object.__new__() takes
                    // exactly one argument (the type to instantiate)" when the class's tp_new comes
                    // from somewhere else.
                    if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                    {
                        if (self is PyType newTarget && TypeNewIsObjectNew(newTarget))
                        {
                            if (TypeInitIsObjectInit(newTarget))
                                throw PyTypeError.Create($"{newTarget.Name}() takes no arguments");
                        }
                        else
                        {
                            throw PyTypeError.Create(
                                "object.__new__() takes exactly one argument (the type to instantiate)");
                        }
                    }
                    if (self is PyClass abstractCandidate)
                        abstractCandidate.CheckAbstractMethods();

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

                    // Create instance of the class. This is the live object.__new__ (PyMethodDescriptor
                    // pulls cls out of self), so it files its own result: object.__new__(C) and
                    // C.__new__(C) both owe C's __del__ (probe H).
                    var created = new PyClassInstance(cls);
                    PyFinalizer.RegisterIfDeclared(created);
                    return created;
                },
                minArgs: 0,  // 'cls' is extracted as 'self' by PyMethodDescriptor
                maxArgs: int.MaxValue
            );

            // object.__getattribute__(self, name) - CPython PyObject_GenericGetAttr
            // Reference: Objects/object.c:1536, typeobject.c:6566 (tp_getattro slot)
            // CRITICAL: This must call the internal generic method to avoid infinite recursion
            // when custom __getattribute__ calls super().__getattribute__()
            TypeDict["__getattribute__"] = new PyWrapperDescriptor(
                "__getattribute__",
                objectType,
                (self, args, kwargs) => {
                    if (args.Length < 1)
                        throw PyTypeError.Create("__getattribute__() missing 1 required positional argument: 'name'");

                    PyObject.AttributeName(args[0]);
                    var nameStr = (PyString)args[0];

                    // PyObject_GenericGetAttr, without the hook of the instance's Python class; its
                    // AttributeError carries the name object (set_attribute_error_context).
                    try { return self.GenericGetAttr(nameStr.Value); }
                    catch (PythonException error) when (error.PyException is PyAttributeError missing)
                    {
                        missing.SetContextIfUnset(nameStr, self);
                        throw;
                    }
                },
                minArgs: 1,
                maxArgs: 1
            );

            // object.__setattr__(self, name, value) - CPython PyObject_GenericSetAttr
            // Reference: Objects/object.c:1565
            // IMPORTANT: This must call SetAttributeDefault, NOT SetAttribute,
            // to avoid infinite recursion when user defines __setattr__ that calls super().__setattr__()
            TypeDict["__setattr__"] = new PyWrapperDescriptor(
                "__setattr__",
                objectType,
                (self, args, kwargs) => {
                    if (args.Length < 2)
                        throw PyTypeError.Create("__setattr__() missing required positional arguments");
                    Hackcheck(self, "__setattr__");

                    PyObject.AttributeName(args[0]);
                    var nameStr = (PyString)args[0];

                    // CPython 3.12: object.__setattr__ uses _PyObject_GenericSetAttrWithDict
                    // which is the "default" behavior without __setattr__ lookup
                    // A heap instance's tp_setattro may be slot_tp_setattro; this is the generic set.
                    self.GenericSetAttr(nameStr, args[1]);
                    return PyNone.Instance;
                },
                minArgs: 2,
                maxArgs: 2
            );

            // object.__delattr__(self, name) - CPython PyObject_GenericSetAttr with value=NULL
            // Reference: Objects/object.c:1565 (same function, value=NULL means delete)
            // IMPORTANT: This must call DelAttributeDefault, NOT DelAttribute,
            // to avoid infinite recursion when user defines __delattr__ that calls super().__delattr__()
            TypeDict["__delattr__"] = new PyWrapperDescriptor(
                "__delattr__",
                objectType,
                (self, args, kwargs) => {
                    if (args.Length < 1)
                        throw PyTypeError.Create("__delattr__() missing 1 required positional argument: 'name'");
                    Hackcheck(self, "__delattr__");

                    PyObject.AttributeName(args[0]);
                    var nameStr = (PyString)args[0];

                    // CPython 3.12: object.__delattr__ uses _PyObject_GenericSetAttrWithDict with value=NULL
                    self.GenericDelAttr(nameStr);
                    return PyNone.Instance;
                },
                minArgs: 1,
                maxArgs: 1
            );

            // object.__init_subclass__() - CPython PEP 487
            // Reference: Objects/typeobject.c:6414 (object_init_subclass)
            // METH_CLASS|METH_NOARGS: a classmethod_descriptor, whose convention (PyArity) refuses
            // arguments in the name of the class it is bound to.
            TypeDict["__init_subclass__"] = new PyClassMethodDescriptor("__init_subclass__", objectType,
                new PyBuiltinFunction("__init_subclass__", (args, kwargs) => PyNone.Instance));

            // object.__class__ getset (CPython object.c get_class): makes __class__
            // visible through GenericGetAttribute/MRO for builtin instances too.
            objectType.TypeDict["__class__"] = new PyGetSetDescriptor("__class__", objectType,
                self => self.GetPyType(),
                SubclassInstanceSupport.ObjectSetClass,
                // object_set_class with NULL
                deleter: self => throw PyTypeError.Create("can't delete __class__ attribute"));

            // object.__format__(format_spec) - CPython 3.12: Objects/typeobject.c:6257 (object___format__)
            // Default implementation: if format_spec is empty, return str(self), else raise TypeError
            TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__",
                objectType,
                (self, args, kwargs) => {
                    if (args[0] is not PyString specStr)
                        throw PyArgs.BadArgument("__format__", "argument", "str", args[0]);

                    // CPython: empty format_spec returns str(self)
                    if (specStr.Value == "")
                    {
                        return self.ToStr();
                    }

                    // CPython: non-empty format_spec raises TypeError for base object, and
                    // names type(obj) - for a class object that is its metaclass.
                    throw PyTypeError.Create($"unsupported format string passed to {self.GetPyType()?.Name ?? self.GetTypeName()}.__format__");
                },
                minArgs: 1,
                maxArgs: 1
            );

            // __dir__ dispatches on whether self is a type: classes use the type.__dir__
            // algorithm (own+base dicts, no metaclass), instances use object.__dir__
            // (instance __dict__ + class/base dicts). See DefaultDirNames. This one is
            // safe to expose functionally: no operator/dispatch path resolves __dir__.
            TypeDict["__dir__"] = new PyMethodDescriptor("__dir__", objectType,
                (self, args, kwargs) => new PyList(DefaultDirNames(self).Select(n => (PyObject)PyString.From(n)).ToList()),
                minArgs: 0, maxArgs: 0);
            // NOTE on __eq__/__ne__/__lt__/__le__/__gt__/__ge__/__hash__/__str__/__repr__/
            // __sizeof__/__getstate__/__reduce__/__reduce_ex__/__subclasshook__:
            // In CPython these are slot wrappers in object's tp_dict, so dir() lists them
            // on every object. SharpPy implements the SAME behaviour as CLR virtuals and
            // operator dispatch (BinaryOp/CompareOp slots, the ToStr/ToRepr fallback, the
            // hash cache, ABC checks). Exposing them as real TypeDict descriptors makes
            // attribute/operator lookup find object's generic version and SHORT-CIRCUIT
            // those CLR paths - measured: object.__str__ hijacked PyList/PyDict repr and
            // object.__eq__/__hash__ interfered with value comparison. So dir() reports
            // the standard object protocol surface via DefaultDirNames' name set instead;
            // operator/repr/hash dispatch is left untouched. (Making these attributes
            // genuinely getattr-accessible without breaking dispatch is a larger, separate
            // change and is recorded as a residual gap.)
        }

        /// <summary>
        // CPython _dir_object/type.__dir__/object.__dir__ name computation.
        /// Classes: union of tp_dict over the class and its bases (metaclass EXCLUDED).
        /// Heap (user) types contribute __dict__ and __weakref__. Instances add their
        /// own instance __dict__. Returned in arbitrary order; builtins.dir() sorts it.
        /// </summary>
        public static List<string> DefaultDirNames(PyObject obj)
        {
            var names = new HashSet<string>();
            if (obj is PyType classObj)
            {
                foreach (var t in classObj.MRO) AddClassDictKeys(names, t);
            }
            else if (obj is PyModule mod)
            {
                // module.__dir__ == its __dict__ keys (CPython module_dir)
                foreach (var k in mod.ModuleDict.Keys) names.Add(k);
            }
            else
            {
                // object___dir___impl: _PyObject_LookupAttr(__dict__), only AttributeError is "none".
                try
                {
                    var d = obj.GetAttribute("__dict__");
                    if (d is PyDict pd)
                        foreach (var k in pd.Keys().Items)
                            if (k is PyString ks) names.Add(ks.Value);
                }
                catch (PythonException error) when (error.PyException is PyAttributeError) { }
                // object___dir___impl: the class the object names, and it may name none.
                PyObject itsClass = null;
                try
                {
                    itsClass = obj.GetAttribute("__class__");
                }
                catch (PythonException error) when (error.PyException is PyAttributeError)
                {
                }
                if (itsClass is PyType t2)
                {
                    var imro = t2.MRO != null && t2.MRO.Count > 0 ? t2.MRO : new List<PyType> { t2 };
                    foreach (var t in imro) AddClassDictKeys(names, t);
                }
            }
            return new List<string>(names);
        }

        private static void AddClassDictKeys(HashSet<string> names, PyType t)
        {
            if (t is PyClass pc)
            {
                foreach (var k in pc.ClassDict.Keys)
                {
                    // __qualname__ and __type_params__ live on the metaclass `type`
                    // in CPython, not in the class tp_dict, so type.__dir__ (which
                    // excludes the metaclass) must not report them even though the
                    // CLR ClassDict keeps them internally.
                    if (k == "__qualname__" || k == "__type_params__") continue;
                    names.Add(k);
                }
                // heap types get a __dict__ / __weakref__ member (CPython adds them to tp_dict)
                if (pc.InstancesHaveDict())
                    names.Add("__dict__");
                if (pc.SupportsWeakReference())
                    names.Add("__weakref__");
            }
            else if (t.TypeDict != null)
            {
                foreach (var k in t.TypeDict.Keys) names.Add(k);
                // object contributes the standard protocol surface that CPython keeps as
                // tp_dict slot wrappers. SharpPy implements these as CLR virtuals/operator
                // slots rather than getattr descriptors (see InitializeObjectTypeDescriptors),
                // so they are reported here for dir() parity without changing dispatch.
                if (ReferenceEquals(t, ObjectType))
                {
                    names.Add("__eq__");
                    names.Add("__ne__");
                    names.Add("__lt__");
                    names.Add("__le__");
                    names.Add("__gt__");
                    names.Add("__ge__");
                    names.Add("__hash__");
                    names.Add("__str__");
                    names.Add("__repr__");
                    names.Add("__sizeof__");
                    names.Add("__getstate__");
                    names.Add("__reduce__");
                    names.Add("__reduce_ex__");
                    names.Add("__subclasshook__");
                }
            }
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
                    var type = (PyType)self;
                    // Objects/typeobject.c:988 type_name: a heap type returns the one name object it
                    // was created with (Py_NewRef(et->ht_name)), so reading __name__ twice gives the
                    // same object; a static spec has no stored str and CPython builds one per read.
                    // The branch is kept because it is observable: C.__name__ is C.__name__ is True,
                    // and that has to come from a cache, not from interning every string.
                    return type.HasFeature(PyTypeFlags.HeapType) ? type.NameString : PyString.From(type.Name);
                },
                setter: (self, value) => {
                    var type = (PyType)self;
                    if (type.IsHeapType)
                    {
                        // type_set_name
                        if (value is PyString { } newName && newName.Value.Contains('\0'))
                            throw PyValueError.Create("type name must not contain null characters");
                        if (value is PyString)
                            value.GetAttribute("encode").Call(Array.Empty<PyObject>(), null);
                        if (value is not PyString)
                            throw PyTypeError.Create($"can only assign string to {type.Name}.__name__, not '{value.GetTypeName()}'");
                        type.SetName((PyString)value);
                        type.InvalidateTypeCache();
                    }
                    else
                    {
                        throw PyTypeError.Create($"cannot set '__name__' attribute of immutable type '{type.FullName}'");
                    }
                },
                deleter: self => {
                    var type = (PyType)self;
                    var name = type.FullName;
                    throw PyTypeError.Create($"cannot delete '__name__' attribute of immutable type '{name}'");
                }
            );

            // type.__qualname__ - CPython type_qualname (Objects/typeobject.c:1002)
            // For now, same as __name__ for built-in types
            TypeDict["__qualname__"] = new PyGetSetDescriptor(
                "__qualname__",
                typeType,
                getter: self => {
                    var type = (PyType)self;
                    // type_qualname reads ht_qualname for a heap type; SharpPy keeps qualname equal to
                    // name today, but still as one stored object per type rather than a fresh str.
                    return type.HasFeature(PyTypeFlags.HeapType)
                        ? type.QualNameString
                        : PyString.From(type.Name);  // For built-in types, qualname == name
                },
                setter: (self, value) => {
                    var type = (PyType)self;
                    if (type.IsHeapType)
                    {
                        if (value is not PyString)
                            throw PyTypeError.Create($"can only assign string to {type.Name}.__qualname__, not '{value.GetTypeName()}'");
                        type.SetQualName(((PyString)value).Value);
                    }
                    else
                    {
                        throw PyTypeError.Create($"cannot set '__qualname__' attribute of immutable type '{type.FullName}'");
                    }
                },
                deleter: self => {
                    var type = (PyType)self;
                    var name = type.FullName;
                    throw PyTypeError.Create($"cannot delete '__qualname__' attribute of immutable type '{name}'");
                }
            );

            // type.__bases__ - CPython type_get_bases / type_set_bases
            TypeDict["__bases__"] = new PyGetSetDescriptor(
                "__bases__",
                typeType,
                getter: self => {
                    var type = (PyType)self;

                    // CPython 3.12 requirement: type.__bases__ is type.__bases__ must be True
                    // Cache the tuple object to ensure identity consistency
                    if (type._cachedBasesTuple == null)
                    {
                        var basesArray = new PyObject[type.BaseTypes.Length];
                        for (int i = 0; i < type.BaseTypes.Length; i++)
                        {
                            basesArray[i] = type.BaseTypes[i];
                        }
                        type._cachedBasesTuple = PyTuple.From(basesArray);
                    }
                    return type._cachedBasesTuple;
                },
                setter: (self, value) => {
                    if (self is not PyClass heap)
                        throw PyTypeError.Create($"cannot set '__bases__' attribute of immutable type '{((PyType)self).FullName}'");
                    if (value is not PyTuple basesTuple)
                        throw PyTypeError.Create($"can only assign tuple to {heap.Name}.__bases__, not {value.GetTypeName()}");
                    if (basesTuple.Items.Length == 0)
                        throw PyTypeError.Create($"can only assign non-empty tuple to {heap.Name}.__bases__, not ()");
                    var newBases = new PyType[basesTuple.Items.Length];
                    for (int i = 0; i < newBases.Length; i++)
                        newBases[i] = basesTuple.Items[i] as PyType ?? throw PyTypeError.Create(
                            $"{heap.Name}.__bases__ must be tuple of classes, not '{basesTuple.Items[i].GetTypeName()}'");
                    PyTypeMetaclass.CheckBases(heap, newBases);
                    heap.SetBases(newBases);
                    heap.InvalidateTypeCache();
                },
                deleter: self => throw PyTypeError.Create($"cannot delete '__bases__' attribute of immutable type '{((PyType)self).FullName}'")
            );

            // type.__mro__ - CPython type_mro (read-only)
            TypeDict["__mro__"] = new PyGetSetDescriptor(
                "__mro__",
                typeType,
                getter: self => {
                    var type = (PyType)self;
                    if (type.MroPending)
                        return PyNone.Instance;

                    // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
                    // Cache the tuple object to ensure identity consistency
                    if (type._cachedMroTuple == null)
                    {
                        var mroArray = new PyObject[type.MRO.Count];
                        for (int i = 0; i < type.MRO.Count; i++)
                        {
                            mroArray[i] = type.MRO[i];
                        }
                        type._cachedMroTuple = PyTuple.From(mroArray);
                    }
                    return type._cachedMroTuple;
                }
            );

            // type_getsets: __dict__ is type_dict, a mappingproxy of tp_dict alone.
            TypeDict["__dict__"] = new PyGetSetDescriptor("__dict__", typeType, self =>
            {
                var type = (PyType)self;
                return new PyMappingProxy(type.TypeDict);
            });

            // type_members: {"__base__", T_OBJECT, offsetof(PyTypeObject, tp_base), READONLY}.
            TypeDict["__base__"] = new PyMemberDescriptor("__base__", typeType,
                self => (PyObject)((PyType)self).TpBase ?? PyNone.Instance, readOnly: true);

            // type_get_doc / type_set_doc: a static type cuts its tp_doc; a heap type's dict entry is
            // asked as a descriptor with no instance; check_set_special_type_attr guards the writes.
            TypeDict["__doc__"] = new PyGetSetDescriptor("__doc__", typeType, self =>
            {
                if (self is PyType withDoc && PyDocs.Of(withDoc) is { Doc: not null } internalDoc)
                    return PyDocs.DocFromInternal(internalDoc.Name, internalDoc.Doc);
                var dict = ((PyType)self).TypeDict;
                return dict.TryGetValue("__doc__", out var doc) ? PyClassInstance.CallDescriptorGet(doc, null, self) : PyNone.Instance;
            },
            (self, value) =>
            {
                var type = (PyType)self;
                if (!type.IsHeapType)
                    throw PyTypeError.Create($"cannot set '__doc__' attribute of immutable type '{type.FullName}'");
                type.TypeDict["__doc__"] = value;
                type.InvalidateTypeCache();
            },
            deleter: self => throw PyTypeError.Create($"cannot delete '__doc__' attribute of immutable type '{((PyType)self).FullName}'"));

            // CPython 3.12: Objects/typeobject.c:1063-1091 (type_module / type_set_module)
            // type.__module__ - returns module name for the type
            // For heap types (user-defined): lookup in tp_dict
            // For built-in types: "builtins" (or extract from tp_name if it contains '.')
            TypeDict["__module__"] = new PyGetSetDescriptor(
                "__module__",
                typeType,
                getter: self => {
                    var type = (PyType)self;

                    // CPython 3.12: Objects/typeobject.c:1065-1088
                    // For heap types (PyClass), check ClassDict
                    if (type is PyClass customType)
                    {
                        // A user/heap class stores its module string directly in the class dict.
                        // `type` is a PyTypeMetaclass whose ClassDict holds this very descriptor.
                        if (customType.ClassDict.TryGetValue("__module__", out var mod) && mod is not IDescriptor)
                        {
                            return mod;
                        }
                    }

                    // For built-in/native types SharpPy stores the module in the
                    // Module field (CPython encodes it in tp_name as "module.Name").
                    if (!string.IsNullOrEmpty(type.Module))
                        return PyString.From(type.Module);

                    // For built-in types: check if tp_name contains '.'
                    // CPython: const char *s = strrchr(type->tp_name, '.');
                    var dotIndex = type.Name.LastIndexOf('.');
                    if (dotIndex >= 0)
                    {
                        return PyString.From(type.Name.Substring(0, dotIndex));
                    }

                    // Default: "builtins" for built-in types
                    // CPython: mod = Py_NewRef(&_Py_ID(builtins));
                    return PyString.From("builtins");
                },
                setter: (self, value) => {
                    var type = (PyType)self;
                    if (type.IsHeapType)
                    {
                        type.TypeDict["__module__"] = value;
                        type.InvalidateTypeCache();
                    }
                    else
                    {
                        throw PyTypeError.Create($"cannot set '__module__' attribute of immutable type '{type.FullName}'");
                    }
                },
                deleter: self => {
                    var type = (PyType)self;
                    throw PyTypeError.Create($"cannot delete '__module__' attribute of immutable type '{type.FullName}'");
                }
            );

            TypeDict["__new__"] = PyStaticBuiltinMethod.ForNew(this, PyTypeMetaclass.TypeNew);

            // type.__setattr__(cls, name, value) - CPython type_setattro
            // Reference: Objects/typeobject.c:4815-4894 (type_setattro)
            TypeDict["__setattr__"] = new PyWrapperDescriptor(
                "__setattr__",
                typeType,
                (self, args, kwargs) => {
                    if (args.Length < 2)
                        throw PyTypeError.Create("__setattr__() missing required positional arguments");

                    PyObject.AttributeName(args[0]);
                    var nameStr = (PyString)args[0];

                    ((PyType)self).TypeSetAttribute(nameStr.Value, args[1]);
                    return PyNone.Instance;
                },
                minArgs: 2,
                maxArgs: 2
            );

            // type_setattro with NULL
            TypeDict["__delattr__"] = new PyWrapperDescriptor(
                "__delattr__",
                typeType,
                (self, args, kwargs) => {
                    PyObject.AttributeName(args[0]);
                    var nameStr = (PyString)args[0];
                    ((PyType)self).TypeDeleteAttribute(nameStr.Value);
                    return PyNone.Instance;
                },
                minArgs: 1,
                maxArgs: 1
            );

            // type.__repr__ - CPython type_repr (from PyTypeMetaclass)
            TypeDict["__repr__"] = new PyWrapperDescriptor(
                "__repr__",
                typeType,
                (self, args, kwargs) => {
                    var type = (PyType)self;
                    return type.TypeRepr();
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython has no type.__str__ of its own: type.__str__ resolves to object.__str__,
            // object_str, the metaclass's tp_repr (measured: type.__str__(C) == 'META-repr:C').

            // type___dir___impl: METH_NOARGS, the names of the class and its bases.
            TypeDict["__dir__"] = new PyMethodDescriptor("__dir__", typeType,
                (self, args, kwargs) => new PyList(DefaultDirNames(self).ConvertAll(n => (PyObject)PyString.From(n))), minArgs: 0, maxArgs: 0);
        }

        /// <summary>
        /// set 타입의 descriptor 테이블 초기화 (CPython Objects/setobject.c 참조)
        /// Phase 3: Directly populate TypeDict instead of Descriptors
        /// </summary>
        private void InitializeSetTypeDescriptors()
        {
            TypeDict["__len__"] = new PyWrapperDescriptor("__len__", this, (self, args, kwargs) =>
                PyInt.From((self as PySet).Items.Count()));
            TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", this, (self, args, kwargs) =>
                (self as PySet).Iter());
            TypeDict["__contains__"] = new PyMethodDescriptor("__contains__", this, (self, args, kwargs) =>
            {
                return (self as PySet).ContainsItem(args[0]);
            });

            var setType = this;

            // CPython 3.12: set.__hash__ is None (mutable set is unhashable; frozenset keeps
            // its hash). See the same mechanism noted in InitializeDictDescriptors.
            TypeDict["__hash__"] = PyNone.Instance;

            // set.__new__(cls, /) - Objects/setobject.c:1038 set_new, which is literally
            //   return make_new_set(type, NULL);
            // The iterable a caller passes is **not** read here; set_init reads it. Registering the
            // descriptor also matters for the constructor guard: PyType.TypeNewIsObjectNew compares
            // the MRO answer against object.__new__, and while set's tp_new existed only as C code
            // SharpPy saw object.__new__ and refused every argument with "C() takes no arguments".
            TypeDict["__new__"] = new PyNewMethodDescriptor(
                "__new__",
                setType,
                (self, args, kwargs) => {
                    var cls = self;
                    if (cls == PyType.SetType)
                        return new PySet();
                    if (!(cls is PyClass setClass)) {
                        string foreign = TpName(cls);
                        throw PyTypeError.Create(
                            "set.__new__(" + foreign + "): " + foreign + " is not a subtype of set");
                    }
                    return new PySetSubclass(setClass);
                },
                minArgs: 0,
                maxArgs: int.MaxValue
            );

            // set.__init__(iterable=(), /) - Objects/setobject.c:1977 set_init:
            //   if (!_PyArg_NoKeywords("set", kwds))                     name is hardcoded "set"
            //   PyArg_UnpackTuple(args, Py_TYPE(self)->tp_name, 0, 1, &iterable)   name is the type's own
            //   if (self->fill) set_clear_internal(self);                 re-initialising empties it
            //   self->hash = -1;
            //   if (iterable == NULL) return 0;
            //   return set_update_internal(self, iterable);
            // The two names differing is observable: C([1],[2]) says "C expected at most 1 argument,
            // got 2" while C(x=[1]) says "set() takes no keyword arguments" (measured).
            TypeDict["__init__"] = new PyWrapperDescriptor(
                "__init__",
                setType,
                (self, args, kwargs) => {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("set() takes no keyword arguments");
                    if (args.Length > 1)
                        throw PyTypeError.Create(
                            $"{self.GetTypeName()} expected at most 1 argument, got {args.Length}");
                    var target = (self as PySet) ?? throw PyTypeError.Create(
                        "object.__init__() takes exactly one argument (the instance to initialize)");
                    target.Clear();
                    if (args.Length == 1)
                        target.Update(args[0]);
                    return PyNone.Instance;
                },
                minArgs: 0,
                maxArgs: int.MaxValue
            );

            // set.add(elem)
            TypeDict["add"] = new PyMethodDescriptor(
                "add",
                setType,
                (self, args, kwargs) => {
                    var set = (PySet)self;
                    return set.Add(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // nb_inplace_or / nb_inplace_and / nb_inplace_subtract / nb_inplace_xor are visible in
            // dir(set) as __ior__ / __iand__ / __isub__ / __ixor__ (Objects/setobject.c
            // set_inplace_union and the *_update helpers). They mutate the object and return the
            // same object, so s |= {2} keeps a subclass instance and keeps aliases live; without
            // these entries SharpPy fell back to the plain operator and rebound a new exact set
            // (and reported the failure with '|' where CPython says '|=').
            RegisterSetInplace("__ior__", "|=", (target, other) => target.Update(other));
            RegisterSetInplace("__iand__", "&=", (target, other) => target.IntersectionUpdate(other));
            RegisterSetInplace("__isub__", "-=", (target, other) => target.DifferenceUpdate(other));
            RegisterSetInplace("__ixor__", "^=", (target, other) => target.SymmetricDifferenceUpdate(other));

            // set.remove(elem)
            TypeDict["remove"] = new PyMethodDescriptor(
                "remove",
                setType,
                (self, args, kwargs) => {
                    var set = (PySet)self;
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
                    var set = (PySet)self;
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
                    var set = (PySet)self;
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
                    var set = (PySet)self;
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
                    var set = (PySet)self;
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
                    var set = (PySet)self;
                    // CPython set_update(set, *others)
                    foreach (var other in args)
                        set.Update(other);
                    return PyNone.Instance;
                },
                minArgs: 0
            );

            // set.union(other)
            TypeDict["union"] = new PyMethodDescriptor(
                "union",
                setType,
                (self, args, kwargs) => {
                    var set = (PySet)self;
                    return PySet.UnionOf(set.Copy(), set, args);
                },
                minArgs: 0
            );

            // set.intersection(other)
            TypeDict["intersection"] = new PyMethodDescriptor(
                "intersection",
                setType,
                (self, args, kwargs) => {
                    var set = (PySet)self;
                    return PySet.IntersectionOf(set, args);
                },
                minArgs: 0
            );

            // set.difference(other)
            TypeDict["difference"] = new PyMethodDescriptor(
                "difference",
                setType,
                (self, args, kwargs) => {
                    var set = (PySet)self;
                    return PySet.DifferenceOf(set, args);
                },
                minArgs: 0
            );

            // set.symmetric_difference(other)
            TypeDict["symmetric_difference"] = new PyMethodDescriptor(
                "symmetric_difference",
                setType,
                (self, args, kwargs) => {
                    var set = (PySet)self;
                    // CPython set_symmetric_difference takes exactly one other argument.
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
                    var set = (PySet)self;
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
                    var set = (PySet)self;
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
                    var set = (PySet)self;
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
                    var set = (PySet)self;
                    foreach (var other in args)
                        set.IntersectionUpdate(other);
                    return PyNone.Instance;
                },
                minArgs: 0
            );

            // CPython 3.12: Objects/setobject.c:2096-2103 (set_difference_update)
            // set.difference_update(other) - Remove elements found in other
            TypeDict["difference_update"] = new PyMethodDescriptor(
                "difference_update",
                setType,
                (self, args, kwargs) => {
                    var set = (PySet)self;
                    foreach (var other in args)
                        set.DifferenceUpdate(other);
                    return PyNone.Instance;
                },
                minArgs: 0
            );

            // CPython 3.12: Objects/setobject.c:2135-2142 (set_symmetric_difference_update)
            // set.symmetric_difference_update(other) - Update with symmetric difference
            TypeDict["symmetric_difference_update"] = new PyMethodDescriptor(
                "symmetric_difference_update",
                setType,
                (self, args, kwargs) => {
                    var set = (PySet)self;
                    return set.SymmetricDifferenceUpdate(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // CPython 3.12: Objects/setobject.c:2343
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            setType.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(setType);
        }

        /// <summary>
        /// frozenset 타입의 descriptor 테이블 초기화
        /// </summary>
        /// <summary>
        /// One of set's four in-place operators. The operand test is PyAnySet_Check - which is why
        /// s |= [1] is an error even though s.update([1]) is not, and why the message has to carry
        /// the in-place symbol. Returning self rather than the storage is what preserves identity and
        /// the subclass.
        /// </summary>
        private void RegisterSetInplace(string name, string symbol, Action<PySet, PyObject> mutate)
        {
            TypeDict[name] = new PyWrapperDescriptor(
                name,
                this,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create(
                            $"{name}() takes exactly one argument ({args.Length} given)");
                    var target = (PySet)self;
                    if (!PySet.IsAnySetOperand(args[0])) return PyNotImplemented.Instance;
                    mutate(target, args[0]);
                    return self;
                },
                minArgs: 0,
                maxArgs: int.MaxValue
            );
        }

        private void InitializeFrozenSetTypeDescriptors()
        {
            var frozensetType = this;

            // CPython 3.12: Objects/setobject.c:2304
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            frozensetType.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(frozensetType);

            // frozenset.__new__(cls, iterable=(), /) - Objects/setobject.c:1003 frozenset_new:
            //   if ((type == &PyFrozenSet_Type || type->tp_init == PyFrozenSet_Type.tp_init)
            //       && !_PyArg_NoKeywords("frozenset", kwds)) return NULL;
            //   PyArg_UnpackTuple(args, type->tp_name, 0, 1, &iterable)
            // frozenset declares **no** tp_init (Objects/setobject.c:2262 "0, /* tp_init */"), so
            // this is the only place the value is built, and the keyword test's condition is false
            // for a subclass: the subclass inherits object_init, not frozenset's absent one. The
            // observable result is that frozenset(x=1) is an error while C(x=1) quietly builds C().
            TypeDict["__new__"] = new PyNewMethodDescriptor(
                "__new__",
                frozensetType,
                (self, args, kwargs) => {
                    var cls = self;
                    bool exact = cls == PyType.FrozenSetType;
                    // The condition in frozenset_new compares tp_init against frozenset's own.
                    // A subclass that declares no __init__ ends up with the very same object_init
                    // frozenset itself has (Objects/setobject.c:2262 leaves tp_init 0), so the
                    // refusal applies there too: F(x=1) raises, measured on 3.12.15. Only a class
                    // that defines its own __init__ escapes this check.
                    bool initIsFrozensetInit = exact || PyType.TypeInitIsObjectInit(cls as PyType);
                    if (initIsFrozensetInit && kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("frozenset() takes no keyword arguments");
                    if (args.Length > 1)
                        throw PyTypeError.Create(
                            $"{TpName(cls)} expected at most 1 argument, got {args.Length}");
                    // make_new_frozenset: an exact frozenset is returned as it is, a dict gives its keys
                    // with the hashes it holds.
                    if (exact)
                        return args.Length == 0 ? new PyFrozenSet()
                            : args[0] is PyFrozenSet same && same.GetPyType() == PyType.FrozenSetType ? same
                            : new PyFrozenSet(PySet.SourceItems(args[0]));
                    if (cls is PyClass frozenClass) {
                        // make_new_set: the frozenset layout, ob_type the subclass.
                        return new PyFrozenSetSubclass(frozenClass, args.Length == 0
                            ? Array.Empty<PyObject>()
                            : PySet.SourceItems(args[0]));
                    }
                    string foreign = TpName(cls);
                    throw PyTypeError.Create(
                        "frozenset.__new__(" + foreign + "): " + foreign +
                        " is not a subtype of frozenset");
                },
                minArgs: 0,
                maxArgs: int.MaxValue
            );

            // frozenset's methods live in Objects/setobject.c frozenset_methods as METH_NOARGS /
            // METH_O / METH_VARARGS entries, so a subclass inherits them as builtins bound to the
            // instance. SharpPy had them only as CLR methods on PyFrozenSet, and a subclass instance
            // is not a PyFrozenSet: C([1]).union([2]) raised AttributeError while the same call on a
            // plain frozenset worked. Arity refusals keep the measured wording, which names the
            // defining type rather than the subclass:
            //   frozenset.copy() takes no arguments (1 given)
            //   frozenset.issubset() takes exactly one argument (0 given)
            // set_reduce / frozenset_reduce and set_sizeof / frozenset_sizeof
            // (Objects/setobject.c). A subclass inherits them as builtins, and __reduce__ names the
            // subclass so pickle can rebuild it: S([1]).__reduce__() is (S, ([1],), None).
            // Both are METH_NOARGS, and CPython's refusal names the receiving class - measured
            // S().__reduce__(1) gives "S.__reduce__() takes no arguments (1 given)", while calling
            // the descriptor with a foreign receiver gives
            // "descriptor '__sizeof__' for 'set' objects doesn't apply to a 'int' object".
            PyType.SetType.TypeDict["__reduce__"] = new PyMethodDescriptor(
                "__reduce__", PyType.SetType,
                (self, args, kwargs) => {
                    RefuseExtra(PyType.SetType, "__reduce__", args);
                    return BuildSetReduce(self, PySet.IterableItems((PySet)self));
                },
                minArgs: 0, maxArgs: 0);
            PyType.SetType.TypeDict["__sizeof__"] = new PyMethodDescriptor(
                "__sizeof__", PyType.SetType,
                (self, args, kwargs) => {
                    RefuseExtra(PyType.SetType, "__sizeof__", args);
                    return PyInt.From(SetSizeInBytes(CountItems(PySet.IterableItems(
                        (PySet)self))));
                },
                minArgs: 0, maxArgs: 0);
            frozensetType.TypeDict["__reduce__"] = new PyMethodDescriptor(
                "__reduce__", frozensetType,
                (self, args, kwargs) => {
                    RefuseExtra(PyType.FrozenSetType, "__reduce__", args);
                    return BuildSetReduce(self, PySet.IterableItems(
                        (PyFrozenSet)self));
                },
                minArgs: 0, maxArgs: 0);
            frozensetType.TypeDict["__sizeof__"] = new PyMethodDescriptor(
                "__sizeof__", frozensetType,
                (self, args, kwargs) => {
                    RefuseExtra(PyType.FrozenSetType, "__sizeof__", args);
                    return PyInt.From(SetSizeInBytes(CountItems(PySet.IterableItems(
                        (PyFrozenSet)self))));
                },
                minArgs: 0, maxArgs: 0);

            RegisterFrozenSetMethod("copy", 0, 0, (frozen, a) => frozen.Copy());
            // The set algorithms, the result made a frozenset (make_new_set_basetype): with no others
            // still a new object, unlike frozenset.copy, and an exact frozenset for a subclass.
            RegisterFrozenSetMethod("union", 0, int.MaxValue,
                (frozen, a) => PyFrozenSet.Adopting(PySet.UnionOf(new PySet(frozen.Items), frozen, a)));
            RegisterFrozenSetMethod("intersection", 0, int.MaxValue,
                (frozen, a) => PyFrozenSet.Adopting(PySet.IntersectionOf(PySet.Over(frozen.Table), a)));
            RegisterFrozenSetMethod("difference", 0, int.MaxValue,
                (frozen, a) => PyFrozenSet.Adopting(PySet.DifferenceOf(PySet.Over(frozen.Table), a)));
            RegisterFrozenSetMethod("symmetric_difference", 1, 1,
                (frozen, a) => frozen.SymmetricDifference(a[0]));
            RegisterFrozenSetMethod("isdisjoint", 1, 1, (frozen, a) => frozen.IsDisjoint(a[0]));
            RegisterFrozenSetMethod("issubset", 1, 1, (frozen, a) => frozen.IsSubset(a[0]));
            RegisterFrozenSetMethod("issuperset", 1, 1, (frozen, a) => frozen.IsSuperset(a[0]));
        }

        /// <summary>
        /// One frozenset method table entry. The self check is PyFrozenSet_Check, which a subclass
        /// instance passes because it is a PyFrozenSet. The refusal wording is Objects/descrobject.c's
        /// method_descriptor form: "descriptor 'union' for 'frozenset' objects doesn't apply to a
        /// 'C' object".
        /// </summary>
        private void RegisterFrozenSetMethod(string name, int minCount, int maxCount,
            Func<PyFrozenSet, PyObject[], PyObject> body)
        {
            TypeDict[name] = new PyMethodDescriptor(
                name,
                this,
                (self, args, kwargs) => {
                    if (args.Length < minCount || args.Length > maxCount)
                    {
                        string phrase = maxCount == 0
                            ? "takes no arguments"
                            : minCount == 1 && maxCount == 1
                                ? "takes exactly one argument"
                                : "takes at least " + minCount + " arguments";
                        throw PyTypeError.Create(
                            "frozenset." + name + "() " + phrase + " (" + args.Length + " given)");
                    }
                    var frozen = (PyFrozenSet)self;
                    return body(frozen, args);
                },
                minArgs: 0,
                maxArgs: int.MaxValue
            );
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
            TypeDict["__new__"] = new PyNewMethodDescriptor(
                "__new__",
                tupleType,
                (self, args, kwargs) => {
                    // __new__ is a static method, 'self' is actually the class
                    var cls = self;

                    // CPython tuple_new takes exactly one positional argument, no keywords, and only
                    // for a tuple subtype. All three refusals were missing: keywords were dropped, a
                    // second positional was read as the size argument Python 2 used to have, and a
                    // foreign class happily produced a tuple.
                    // tuple_new: only a subclass with an __init__ of its own may take keywords.
                    if (kwargs != null && kwargs.Length() != 0 &&
                        (ReferenceEquals(cls, TupleType) || TypeInitIsObjectInit(cls as PyType)))
                        throw PyTypeError.Create("tuple() takes no keyword arguments");
                    if (args.Length > 1)
                        throw PyTypeError.Create(
                            $"tuple expected at most 1 argument, got {args.Length}");

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
                            return PyTuple.From();
                        // PySequence_Tuple: an exact tuple is its own result.
                        return iterable is PyTuple { IsExact: true } same ? same : iterable.AsTuple();
                    }

                    // CPython 3.12: Creating a tuple subclass
                    // Convert iterable to tuple items
                    PyObject[] tupleItems;
                    if (iterable == null)
                    {
                        tupleItems = new PyObject[0];
                    }
                    else if (iterable is PyTuple { IsExact: true } tup)
                    {
                        tupleItems = tup.Items;
                    }
                    else if (iterable is PyList { IsExact: true } list)
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

            // dict.__new__(cls, /) - Objects/dictobject.c dict_new builds an empty dict while
            // dict_init consumes the mapping argument. Same reason as list.__new__: the class's
            // tp_new is dict_new, not object_new.
            TypeDict["__new__"] = new PyNewMethodDescriptor(
                "__new__",
                dictType,
                (self, args, kwargs) => {
                    var cls = self;
                    if (cls == PyType.DictType)
                        return new PyDict();
                    if (!(cls is PyClass dictClass)) {
                        string foreign = TpName(cls);
                        throw PyTypeError.Create(
                            "dict.__new__(" + foreign + "): " + foreign + " is not a subtype of dict");
                    }
                    return new PyDictSubclass(dictClass);
                },
                minArgs: 0,
                maxArgs: int.MaxValue
            );

            // dict.__init__ - CPython 3.12: dict.__init__ can accept optional args and kwargs
            // CPython: Objects/dictobject.c:3293-3378 (dict___init___impl)
            // dict_init: dict_update_common(self, args, kwds, "dict"), what dict.update does.
            TypeDict["__init__"] = new PyWrapperDescriptor("__init__", dictType, (self, args, kwargs) =>
            {
                PyDict.UpdateCommon((PyDict)self, args, kwargs, "dict");
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
        }

        #endregion

        #region Type Attributes

        // CPython type_getattro reports a missing attribute on a class as
        // "type object '<class>' has no attribute '<name>'" (uses the class name,
        // not the metaclass name).
        protected override System.Exception AttributeNotFound(string name)
            => PyAttributeError.Create($"type object '{Name}' has no attribute '{name}'");

        internal override bool TpGetattroIsGeneric => false;
        protected internal override PyObject TpGetattro(PyString name) => TypeGetattro(name.Value);

        /// <summary>
        /// _Py_type_getattro_impl (Objects/typeobject.c): a data descriptor of the metatype; then
        /// _PyType_Lookup of the type itself, through tp_descr_get(attribute, NULL, type); then what
        /// the metatype holds, a non-data descriptor bound to the type or a plain value.
        /// </summary>
        internal PyObject TypeGetattro(string name)
        {
            var metatype = GetPyType();
            var metaAttribute = metatype.FindNameInMro(name);
            bool metaDescriptor = metaAttribute != null && SharpPy.PyClassInstance.IsDescriptor(metaAttribute);
            if (metaDescriptor && SharpPy.PyClassInstance.IsDataDescriptor(metaAttribute))
                return SharpPy.PyClassInstance.CallDescriptorGet(metaAttribute, this, metatype);
            var attribute = FindNameInMro(name);
            if (attribute != null)
                return SharpPy.PyClassInstance.IsDescriptor(attribute)
                    ? SharpPy.PyClassInstance.CallDescriptorGet(attribute, null, this) : attribute;
            if (metaAttribute != null)
                return metaDescriptor ? SharpPy.PyClassInstance.CallDescriptorGet(metaAttribute, this, metatype) : metaAttribute;
            throw AttributeNotFound(name);
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
        protected internal override PyObject MpSubscript(PyObject key)
        {
            // PyObject_GetItem (Objects/abstract.c) for a type without a metatype mp_subscript:
            // type[int] is special-cased, since type has no __class_getitem__ that str[int] could
            // inherit; any other type answers through getattr(o, "__class_getitem__").
            if (ReferenceEquals(this, TypeType))
                return new PyGenericAlias(this, key);
            var method = LookupAttribute("__class_getitem__");
            if (method != null && method is not PyNone)
                return method.Call(new PyObject[] { key }, null);
            throw PyTypeError.Create($"type '{Name}' is not subscriptable");
        }

        #endregion

        #region String Representation

        internal PyString TypeRepr()
        {
            // type_repr reads type metadata directly, bypassing metaclass hooks.
            string module = Module;
            string qualname = Name;
            if (this is PyClass cls)
            {
                module = cls.ClassDict.GetValueOrDefault("__module__") is PyString mod ? mod.Value : null;
                // type_repr reads ht_qualname, not a dict entry.
                qualname = cls.QualNameText;
            }
            return PyString.From(module != null && module != "builtins"
                ? $"<class '{module}.{qualname}'>" : $"<class '{Name}'>");
        }

        // CPython PyObject_Repr / PyObject_Str dispatch on type(obj), and for a class
        // object type(obj) is the metaclass, so a metaclass __repr__/__str__ wins over
        // type_repr. Slots at or above `type` are the default behaviour that TypeRepr()
        // already implements, so the walk stops there instead of re-entering them.

        // type_repr: a metaclass's __repr__ is its tp_repr, which PyObject_Repr reaches first.
        protected internal override PyString Repr() => TypeRepr();


        #endregion

        #region Hash and Comparison

        protected internal override bool Bool() => true;

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
        public override PyObject BitwiseOr(PyObject other) => PyUnionType.Or(this, other);

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
                    throw PyArgs.MissingArgument("mappingproxy", "mapping", 1);

                // args[0] is the class (MappingProxyType)
                // args[1] is the mapping object
                var mappingObj = args[1];

                return PyMappingProxy.New(mappingObj);
            });

            // mappingproxy.__getitem__(key)
            // CPython 3.12: Objects/descrobject.c:1043-1046 (mappingproxy_getitem)
            // Line 1045: return PyObject_GetItem(pp->mapping, key);
            TypeDict["__getitem__"] = new PyWrapperDescriptor(
                "__getitem__",
                mappingProxyType,
                (self, args, kwargs) => {
                    var mappingProxy = (PyMappingProxy)self;

                    // Call GetItem which returns value as-is (no descriptor protocol)
                    return mappingProxy.MpSubscript(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // mappingproxy.__contains__(key)
            // CPython 3.12: Objects/descrobject.c:1079-1085 (mappingproxy_contains)
            // Line 1082: return PyDict_Contains(pp->mapping, key);
            TypeDict["__contains__"] = new PyWrapperDescriptor(
                "__contains__",
                mappingProxyType,
                (self, args, kwargs) => {
                    var mappingProxy = (PyMappingProxy)self;

                    // Call Contains which checks the underlying mapping
                    return mappingProxy.ContainsItem(args[0]);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // mappingproxy.__len__()
            // CPython 3.12: Objects/descrobject.c:1037-1040 (mappingproxy_len)
            // Line 1039: return PyObject_Size(pp->mapping);
            TypeDict["__len__"] = new PyWrapperDescriptor(
                "__len__",
                mappingProxyType,
                (self, args, kwargs) => {
                    var mappingProxy = (PyMappingProxy)self;

                    // Return the length of the underlying mapping
                    // Use InternalCount to avoid recursion (don't call Length() which looks up __len__)
                    return PyInt.From(mappingProxy.InternalCount);
                },
                minArgs: 0,
                maxArgs: 0
            );
        }

        /// <summary>
        /// BaseException_args_repr (Objects/exceptions.c:120): nothing for no arguments, the bare repr
        /// for one argument, and a parenthesised list for more.
        /// </summary>
        internal static string ArgsReprForException(PyObject[] args)
        {
            if (args == null || args.Length == 0) return "()";
            if (args.Length == 1) return $"({args[0].ToRepr().Value})";
            var parts = new string[args.Length];
            for (int i = 0; i < args.Length; i++) parts[i] = args[i].ToRepr().Value;
            return "(" + string.Join(", ", parts) + ")";
        }

        /// <summary>
        /// BaseException 타입의 descriptor 테이블 초기화
        /// CPython reference: Objects/exceptions.c:79-105 (BaseException_str)
        /// </summary>
        private void InitializeBaseExceptionTypeDescriptors()
        {
            // CPython 3.12: Objects/exceptions.c:56 BaseException_new. The slot is filled while the
            // object is allocated, before __init__ runs, so an __init__ that never calls super() still
            // has its arguments: class X(Exception): def __init__(self, a): self.a = a gives
            // X(1).args == (1,). Keywords are not arguments and are not refused (:59-:62). Registering
            // the slot is also what makes super().__new__(cls, "n") work in a subclass - with no slot
            // the lookup fell through to object.__new__, which refused the extra value.
            TypeDict["__new__"] = new PyNewMethodDescriptor(
                "__new__", this,
                (cls, args, kwargs) =>
                {
                    if (!(cls is PyType wanted) || !wanted.DerivesFromType(BaseExceptionType))
                    {
                        string foreign = cls == null ? "NoneType" : cls.GetTypeName();
                        throw PyTypeError.Create(
                            $"BaseException.__new__({foreign}): {foreign} is not an exception class");
                    }
                    // type->tp_alloc(type, 0): the struct of the type. A class written in Python has
                    // the layout of its nearest builtin exception, with ob_type the class.
                    var created = wanted is PyClass heap
                        ? PyExceptionSlots.Allocate(heap, args)
                        : wanted.CreateInstance(new PyObject[0], null);
                    PyExceptionSlots.StoreArgs(created, args);
                    return created;
                },
                minArgs: 0,
                maxArgs: int.MaxValue);

            // BaseException_getset: one table for builtin exceptions and Python subclasses.
            static PyBaseException Head(PyObject self) => (PyBaseException)self;
            TypeDict["__traceback__"] = new PyGetSetDescriptor("__traceback__", this, self => Head(self).TracebackGet(),
                (self, value) => Head(self).TracebackSet(value), deleter: self => Head(self).TracebackSet(null));
            TypeDict["__context__"] = new PyGetSetDescriptor("__context__", this, self => Head(self).ContextGet(),
                (self, value) => Head(self).ContextSet(value), doc: "exception context", deleter: self => Head(self).ContextSet(null));
            TypeDict["__cause__"] = new PyGetSetDescriptor("__cause__", this, self => Head(self).CauseGet(),
                (self, value) => Head(self).CauseSet(value), doc: "exception cause", deleter: self => Head(self).CauseSet(null));
            TypeDict["__suppress_context__"] = new PyMemberDescriptor("__suppress_context__", this, self => Head(self).SuppressContextGet(),
                (self, value) => Head(self).SuppressContextSet(value), deleter: self => Head(self).SuppressContextSet(null));

            // BaseException_add_note: __notes__ is an ordinary attribute of the exception.
            TypeDict["add_note"] = new PyMethodDescriptor("add_note", this, (self, args, kwargs) =>
            {
                if (!(args[0] is PyString))
                    throw PyTypeError.Create($"note must be a str, not '{args[0].GetTypeName()}'");
                PyObject notes = null;
                try { notes = self.GetAttribute("__notes__"); }
                catch (PythonException error) when (error.PyException is PyAttributeError) { }
                if (notes == null)
                {
                    notes = new PyList();
                    self.SetAttribute("__notes__", notes);
                }
                if (!(notes is PyList list))
                    throw PyTypeError.Create("Cannot add note: __notes__ is not a list");
                list.Append(args[0]);
                return PyNone.Instance;
            }, 1, 1);

            TypeDict["with_traceback"] = new PyMethodDescriptor("with_traceback", this, (self, args, kwargs) =>
            {
                // BaseException_with_traceback: PyException_SetTraceback.
                ((PyBaseException)self).TracebackSet(args[0]);
                return self;
            }, 1, 1);

            // BaseException_reduce and BaseException_setstate, which copy and pickle use.
            TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", this, (self, args, kwargs) =>
            {
                var state = self.GetAttribute("__dict__");
                var parts = state is PyDict dict && dict.Length() > 0
                    ? new PyObject[] { self.GetPyType(), PyExceptionSlots.ArgsTuple(self), state }
                    : new PyObject[] { self.GetPyType(), PyExceptionSlots.ArgsTuple(self) };
                return PyTuple.From(parts);
            }, 0, 0);
            TypeDict["__setstate__"] = new PyMethodDescriptor("__setstate__", this, (self, args, kwargs) =>
            {
                if (args[0] is PyNone) return PyNone.Instance;
                if (!(args[0] is PyDict state))
                    throw PyTypeError.Create("state is not a dictionary");
                foreach (var item in state.InternalDict)
                    self.SetAttribute(item.Key.ToStr().Value, item.Value);
                return PyNone.Instance;
            }, 1, 1);

            // CPython 3.12: Objects/exceptions.c:42-71 (BaseException_init)
            // BaseException.__init__(self, *args) - stores args in self.args
            TypeDict["__init__"] = new PyWrapperDescriptor("__init__", this, (self, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{self.GetTypeName()}() takes no keyword arguments");
                StoreExceptionArgs(self, args);
                return PyNone.Instance;
            });

            // __str__ (BaseException_str) is installed with the other exceptions' tp_str (PyExceptionTable).

            // CPython 3.12: Objects/exceptions.c:785-795 (BaseException_args member descriptor)
            // BaseException.args - data descriptor (writable, with both getter and setter)
            // CPython 3.12: Objects/exceptions.c:131 BaseException_repr. The name of the type and the
            // arguments, never "<module.Name object at 0x...>"; a Python subclass inherits it, which
            // is what repr(SubK(1)) shows in probes/l23_exit_fields.py.
            TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", this, (self, args, kwargs) =>
            {
                return PyString.From(self.GetTypeName()
                    + ArgsReprForException(PyExceptionSlots.ArgsOf(self)));
            });

            // CPython reference: Objects/exceptions.c:786 - PyMemberDef args_descriptor
            TypeDict["args"] = new PyGetSetDescriptor(
                name: "args",
                ownerType: this,
                getter: (PyObject self) => PyExceptionSlots.ArgsTuple(self),
                setter: (PyObject self, PyObject value) =>
                {
                    PyExceptionSlots.AssignArgs(self, value);
                },
                deleter: (PyObject self) =>
                {
                    throw PyTypeError.Create("args may not be deleted");
                }
            );
        }

        /// <summary>
        /// BaseException_init (Objects/exceptions.c:42) writes the args slot; SystemExit_init calls it
        /// (:637) and then sets the code, so both __init__ descriptors share this implementation.
        /// It writes the slot through PyExceptionSlots rather than SetAttribute: CPython writes the C
        /// slot, so a subclass defining __setattr__ sees no call during construction and a read-only
        /// property named code cannot block it (probes/l24_exit_code_slot.py).
        /// </summary>
        internal static void StoreExceptionArgs(PyObject self, PyObject[] args)
            => PyExceptionSlots.StoreArgs(self, args);

        /// <summary>
        /// SystemExit's own surface, kept next to the type the way Objects/exceptions.c keeps
        /// SystemExit_init (:633) and SystemExit_members (:673) beside PyExc_SystemExit. A subclass
        /// inherits both, which is why class Sub(SystemExit) gets a working .code and a status.
        /// </summary>
        private void InitializeSystemExitTypeDescriptors()
        {
            TypeDict["__init__"] = new PyWrapperDescriptor("__init__", this, (self, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{self.GetTypeName()}() takes no keyword arguments");
                StoreExceptionArgs(self, args);
                // SystemExit_init (:640): with no arguments it only calls BaseException_init and
                // leaves code as it was. code was never a view of args, and None here is just the
                // initial value of the field, which is why e.args = (9,) does not move e.code.
                if (args.Length != 0)
                    PyExceptionSlots.StoreCode(self, PySystemExit.CodeForArgs(args));
                return PyNone.Instance;
            });

            // {"code", T_OBJECT, offsetof(PySystemExitObject, code), 0, "exception code"} (:674).
            TypeDict["code"] = new PyMemberDescriptor(
                "code", this,
                getter: self => PyExceptionSlots.CodeOf(self) ?? (PyObject)PyNone.Instance,
                setter: (self, value) => PyExceptionSlots.StoreCode(self, value));
        }

        #endregion
    }

    /// <summary>
    /// Simple object instance for object() constructor
    /// </summary>
    public class PyInstance : PyObject
    {
        public override PyType GetPyType() => PyType.ObjectType;

        // CPython: str(object()) == repr(object()) == '<object object at 0x...>'
        public override string ToString() => ToRepr().Value;
        protected internal override PyString Repr() => PyString.From($"<object object at {PyObjectId.Pointer(this)}>");
    }
}
