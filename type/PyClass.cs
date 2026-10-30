using System;

namespace SharpPy
{
    #region User-Defined Classes

    /// <summary>
    /// 사용자 정의 클래스
    /// </summary>
    public class PyClass : PyType
    {
        static PyClass()
        {
            InitializeTypeTypeDescriptors();
        }

        private static void InitializeTypeTypeDescriptors()
        {
            var typeType = PyType.TypeType;

            // mro() method descriptor - CPython 3.12 호환
            if (!typeType.TypeDict.ContainsKey("mro"))
            {
                typeType.TypeDict["mro"] = new PyMethodDescriptor(
                    "mro", typeType,
                    (self, args, kwargs) => {
                        if (args.Length != 0)
                            throw PyTypeError.Create("mro() takes no arguments");
                        if (self is not PyType type)
                            throw PyTypeError.Create($"descriptor 'mro' requires a 'type' object but received a '{self.GetTypeName()}'");
                        return new PyList(type.MRO.Cast<PyObject>().ToList());
                    },
                    minArgs: 0, maxArgs: 0
                );
            }

            // CPython 3.12: Objects/typeobject.c:4482
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            if (!typeType.TypeDict.ContainsKey("__class_getitem__"))
            {
                typeType.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                    (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
                );
            }
        }

        public Dictionary<string, PyObject> ClassDict { get; }

        private ComparisonMisses _comparisonMisses;

        private sealed class ComparisonMisses
        {
            internal ulong Epoch;
            internal PyType NativeBase;
            internal int Missing;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal PyObject LookupComparisonMethod(CompareOp op, PyType nativeBase)
        {
            var cache = _comparisonMisses;
            if (cache != null && cache.Epoch == ComparisonCacheEpoch &&
                ReferenceEquals(cache.NativeBase, nativeBase) && (cache.Missing & (1 << (int)op)) != 0)
                return null;
            return LookupUncachedComparison(op, nativeBase);
        }

        private PyObject LookupUncachedComparison(CompareOp op, PyType nativeBase)
        {
            var method = FindComparisonMethod(this, op, nativeBase);
            // Only cache misses: callbacks stay live and no removed function is retained.
            if (method == null)
            {
                var cache = _comparisonMisses ??= new ComparisonMisses();
                ulong epoch = ComparisonCacheEpoch;
                if (cache.Epoch != epoch || !ReferenceEquals(cache.NativeBase, nativeBase))
                {
                    cache.Epoch = epoch;
                    cache.NativeBase = nativeBase;
                    cache.Missing = 0;
                }
                cache.Missing |= 1 << (int)op;
            }
            return method;
        }
        public List<PyObject>? TypeParams { get; set; } // PEP 695 __type_params__
        public PyClass? Metaclass { get; set; } // Metaclass information for type() calls

        // Cached tuple objects for __mro__ and __bases__ identity consistency
        // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
        internal PyTuple _cachedMroTuple;
        internal PyTuple _cachedBasesTuple;

        // Whether this class' MRO declares __del__, remembered so that creating an instance of an
        // ordinary class costs one field read instead of an MRO walk. CPython answers the same
        // question once, at type creation: Objects/typeobject.c:9499 binds __del__ to tp_finalize
        // through the slotdef table, and subtype_dealloc (Objects/typeobject.c:1901) then tests
        // tp_finalize per object without searching. This field is the SharpPy equivalent of that
        // slot. See docs/lifetime_and_gc_policy.md section 3 for what does and does not follow.
        private bool _finalizerChecked;
        private bool _declaresFinalizer;

        internal bool DeclaresFinalizer()
        {
            if (!_finalizerChecked)
            {
                var hook = LookupInMRO("__del__");
                _declaresFinalizer = hook != null && !(hook is PyNone);
                _finalizerChecked = true;
            }
            return _declaresFinalizer;
        }

        // CPython 3.12: __slots__ semantics - an instance has a __dict__ only when
        // some class in the MRO omits __slots__ (or lists '__dict__' explicitly).
        private bool? _instancesHaveDictCache;

        public bool InstancesHaveDict()
        {
            if (_instancesHaveDictCache.HasValue)
            {
                return _instancesHaveDictCache.Value;
            }
            foreach (var t in MRO)
            {
                if (ReferenceEquals(t, PyType.ObjectType))
                {
                    continue; // object never contributes an instance dict
                }
                if (t is PyClass pc && pc.ClassDict.TryGetValue("__slots__", out var slots))
                {
                    foreach (var slotName in EnumerateSlotNames(slots))
                    {
                        if (slotName == "__dict__")
                        {
                            _instancesHaveDictCache = true;
                            return true;
                        }
                    }
                    continue;
                }
                // A class without __slots__ adds a __dict__ to instances
                _instancesHaveDictCache = true;
                return true;
            }
            _instancesHaveDictCache = false;
            return false;
        }

        public bool IsSlottedAttributeName(string name)
        {
            foreach (var t in MRO)
            {
                if (t is PyClass pc && pc.ClassDict.TryGetValue("__slots__", out var slots))
                {
                    foreach (var slotName in EnumerateSlotNames(slots))
                    {
                        if (slotName == name)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        internal static IEnumerable<string> EnumerateSlotNames(PyObject slots)
        {
            if (slots is PyString single)
            {
                yield return single.Value;
                yield break;
            }
            if (slots == null)
            {
                yield break;
            }
            var iter = slots.GetIterator();
            while (true)
            {
                PyObject item;
                try
                {
                    item = iter.Next();
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    yield break;
                }
                if (item is PyString ps)
                {
                    yield return ps.Value;
                }
            }
        }
        // Note: __dict__ does NOT cache in CPython 3.12 - each access returns new mappingproxy

        // Note: Method lookups use global TypeMethodCache (CPython-style array cache)
        // See PyType.GlobalMethodCache and LookupInMRO() below

        public PyClass(string name, PyType[] baseTypes, Dictionary<string, PyObject> classDict = null, List<PyObject>? typeParams = null)
            : this(name, baseTypes, classDict, typeParams, null)
        {
        }

        public PyClass(string name, PyType[] baseTypes, Dictionary<string, PyObject> classDict, List<PyObject>? typeParams, string module)
            : base(name, baseTypes, module)
        {
            // CPython 3.12: Objects/typeobject.c:3751 (type_new_init)
            // Line 3751: PyObject *dict = PyDict_Copy(ctx->orig_dict);
            // We must copy the classDict to prevent shared references
            // This is critical for enum classes where update() modifies the dict
            if (classDict != null)
            {
                ClassDict = new Dictionary<string, PyObject>(classDict);
            }
            else
            {
                ClassDict = new Dictionary<string, PyObject>();
            }
            // Defining equality without an explicit hash disables inherited hashing.
            // CPython type_ready_set_hash / inherit_slots; do this at creation,
            // not during lookup (later assignment of __eq__ has different semantics).
            if (ClassDict.ContainsKey("__eq__") && !ClassDict.ContainsKey("__hash__"))
                ClassDict["__hash__"] = PyNone.Instance;
            TypeParams = typeParams;
            
            // __type_params__ 속성 설정
            if (TypeParams != null && TypeParams.Count > 0)
            {
                var typeParamsTuple = new PyTuple(TypeParams.ToArray());
                ClassDict["__type_params__"] = typeParamsTuple;
            }
            else
            {
                ClassDict["__type_params__"] = new PyTuple(new PyObject[0]);
            }
            
            // PEP 698: @override 데코레이터 - CPython compatible (no runtime validation at class creation)
            // ValidateOverrideDecorators(); // Disabled for CPython compatibility

            // CPython 3.12: Objects/typeobject.c:6800-6996 (inherit_slots)
            // 부모 타입의 슬롯 메서드 상속
            InheritSlotMethods(baseTypes);
        }

        public new PyObject CreateInstance(params PyObject[] args)
        {
            return CreateInstance(args, null);
        }

        public new PyObject CreateInstance(PyObject[] args, PyDict kwargs)
        {
            var created = CreateInstanceCore(args, kwargs);
            // A class whose MRO declares __del__ has to be noticed at creation time, because the
            // only moment SharpPy can tell that an object is being let go is interpreter shutdown.
            PyFinalizer.RegisterIfDeclared(this, created);
            return created;
        }

        private PyObject CreateInstanceCore(PyObject[] args, PyDict kwargs)
        {
            // CPython 3.12: Objects/typeobject.c:1627-1689 (type_call)
            // The correct pattern is:
            // 1. Call type->tp_new(type, args, kwds) to create the object
            // 2. If returned object is not instance of type, return it (no __init__)
            // 3. Otherwise, call type->tp_init(obj, args, kwds)

            // Step 1: Call __new__ to create the object
            // CPython 3.12: Objects/typeobject.c:1667
            var newMethod = LookupInMRO("__new__");

            // CPython 3.12: Objects/floatobject.c float_new
            //   if (type == &PyFloat_Type) { ...plain float... }
            //   return float_subtype_new(type, args, kwds);
            // and float_subtype_new converts the argument with PyNumber_Float, allocates the
            // subclass, then copies ob_fval - so a float subclass instance carries the value.
            // Only when the class does not define its own __new__; a user __new__ is a real
            // slot and must run (it may call float.__new__(cls, ...) itself).
            if (DerivesFromType(FloatType) && (newMethod is PyStaticBuiltinMethod
                                            || newMethod is PyBuiltinMethod
                                            || newMethod is PyMethodDescriptor
                                            || newMethod is PyBuiltinFunction))
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create(
                        "float.__new__() takes exactly one argument (the type to instantiate)");
                var floatValue = args.Length == 0 ? new PyFloat(0.0) : args[0].AsFloat();
                return new PyFloatSubclass(this, floatValue);
            }

            // CPython bytes_new for a subclass builds the byte value first and then allocates the
            // subclass around that payload. SharpPy instead returned a plain bytes object, so
            // type(B(b"xy")).__name__ reported 'bytes' and a declared __len__/__add__/__contains__/
            // __iter__/__getitem__/__eq__/__hash__/__repr__ could never be reached.
            if (DerivesFromType(BytesType) && !DeclaresUserSpecial("__new__"))
            {
                return PyBytesSubclass.BytesSubtypeNew(this, args, kwargs);
            }

            // CPython runs tp_new first, and for a class with neither its own __new__ nor an
            // __init__ that consumes arguments, object_new refuses them. SharpPy passed the extras
            // to object.__new__ and built the instance silently, so A(1) and A(x=1) returned an
            // object where CPython raises TypeError: A() takes no arguments. When the class does
            // have an __init__, object_new falls through and the user __init__ reports its own
            // arity error; when __new__ comes from a builtin base (dict, int, tuple), that base's
            // tp_new handles the arguments and object_new is never reached.
            if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
            {
                if (PyType.TypeNewIsObjectNew(this) && PyType.TypeInitIsObjectInit(this))
                    throw PyTypeError.Create($"{Name}() takes no arguments");
            }

            PyObject instance;
            if (newMethod != null)
            {
                // Call __new__ with (cls, *args, **kwargs)
                var newArgs = new PyObject[args.Length + 1];
                newArgs[0] = this;  // cls parameter
                Array.Copy(args, 0, newArgs, 1, args.Length);

                // CPython 3.12: __new__ can be a static method, class method, or builtin
                if (newMethod is PyFunction func)
                {
                    // User-defined __new__ (should be staticmethod, but bound correctly)
                    instance = func.Call(newArgs, kwargs);
                }
                else if (newMethod is PyStaticBuiltinMethod staticBuiltin)
                {
                    // Builtin static __new__ (e.g., int.__new__)
                    instance = staticBuiltin.Call(newArgs, kwargs);
                }
                else if (newMethod is PyBuiltinMethod builtinMethod)
                {
                    // Builtin __new__ from base types (int.__new__, object.__new__, etc.)
                    instance = builtinMethod.Call(newArgs, kwargs);
                }
                else if (newMethod is PyMethodDescriptor descriptor)
                {
                    // Builtin type's __new__ descriptor
                    instance = descriptor.Call(newArgs, kwargs);
                }
                else if (newMethod.IsCallable())
                {
                    instance = newMethod.Call(newArgs, kwargs);
                }
                else
                {
                    throw PyTypeError.Create($"__new__ is not callable");
                }
            }
            else
            {
                // No __new__ found - this should not happen for valid Python classes
                // All classes inherit object.__new__ at minimum
                throw PyTypeError.Create($"cannot create '{Name}' instances: no __new__ method");
            }

            // Step 2: Check if returned object is an instance of this type
            // CPython 3.12: Objects/typeobject.c:1672-1675
            // If __new__ returned a different type, return it immediately (no __init__)
            if (instance.GetPyType() != this)
            {
                return instance;
            }

            // Store constructor arguments for toString() behavior
            if (instance is PyClassInstance classInstance)
            {
                classInstance.ConstructorArgs = args;
            }
            else if (instance is PyTupleSubclass tupleSubclass)
            {
                tupleSubclass.ConstructorArgs = args;
            }

            // Step 3: Call __init__ on the instance
            // CPython 3.12: Objects/typeobject.c:1677-1687
            // __init__ lookup bypasses __getattribute__ (uses _PyType_Lookup)
            // Reference: Objects/typeobject.c:9028 (slot_tp_init -> lookup_method -> _PyType_Lookup)
            var init = LookupInMRO("__init__");
            if (init != null)
            {
                // Apply descriptor protocol if needed
                if (init is IDescriptor desc)
                {
                    init = desc.Get(instance, this);
                }
                else if (init is PyFunction function)
                {
                    // Convert function to bound method
                    init = new PyMethod(instance, function);
                }

                // Call the bound init method
                if (init is PyMethod method)
                {
                    // PyMethod는 이미 self가 바인딩되어 있으므로 args만 전달
                    method.Call(args, kwargs);
                }
                else if (init is PyBuiltinMethod builtinMethod)
                {
                    // Builtin method도 이미 바인딩되어 있음
                    builtinMethod.Call(args, kwargs);
                }
                else
                {
                    // Fallback: callable object
                    init.Call(args, kwargs);
                }
            }

            return instance;
        }

        /// <summary>
        /// CPython 3.12: Objects/typeobject.c:6800-6996 (inherit_slots)
        /// 부모 builtin 타입의 슬롯 메서드 상속
        /// 슬롯 정의는 core/SlotDefs.cs에 테이블화됨
        /// </summary>
        private void InheritSlotMethods(PyType[] bases)
        {
            foreach (var baseType in bases)
            {
                // builtin 타입만 슬롯 메서드 상속 (Python 클래스는 일반 MRO 사용)
                if (!SlotDefs.IsBuiltinType(baseType))
                    continue;

                // 특수 메서드 슬롯 상속
                foreach (var slotMethod in SlotDefs.InheritableSlots)
                {
                    // Hash lookup must follow the MRO: copying int.__hash__ here
                    // would hide an earlier Python mixin's hash (or hash=None).
                    // Comparison descriptors also resolve through the MRO; do
                    // not duplicate six native wrappers in every subclass dict.
                    if (slotMethod is "__hash__" or "__lt__" or "__le__" or "__eq__" or "__ne__" or "__gt__" or "__ge__") continue;
                    // 현재 클래스에 없고 부모에 있으면 상속
                    if (!TypeDict.ContainsKey(slotMethod) &&
                        baseType.TypeDict.TryGetValue(slotMethod, out var method))
                    {
                        TypeDict[slotMethod] = method;
                    }
                }

                // dict 서브클래스인 경우 매핑 메서드도 상속
                if (baseType == PyType.DictType)
                {
                    foreach (var mappingMethod in SlotDefs.MappingMethods)
                    {
                        if (!TypeDict.ContainsKey(mappingMethod) &&
                            baseType.TypeDict.TryGetValue(mappingMethod, out var method))
                        {
                            TypeDict[mappingMethod] = method;
                        }
                    }
                }
            }
        }

        public bool HasMethod(string name)
        {
            return MRO.OfType<PyClass>().Any(t => t.ClassDict.ContainsKey(name));
        }

        /// <summary>
        /// CPython's _PyType_Lookup equivalent: Look up a name in the type's MRO
        /// This bypasses __getattribute__ and goes directly to tp_dict/__dict__
        /// Used for special method lookup (PEP 252: "Special method lookup bypasses __getattribute__()")
        /// Reference: Objects/typeobject.c:4725 (_PyType_Lookup)
        /// </summary>
        /// <summary>
        /// Whether this class - not its metaclass - declares the dunder in one of its own user frames.
        /// PyObject.LookupUserSpecial walks GetPyType().MRO, and for a class object GetPyType() is the
        /// metaclass, so asking it about __new__ answers "does type declare __new__" instead. Measured
        /// while wiring bytes subclasses: the gate reported a user __new__ for class B(bytes) and every
        /// subclass construction fell back to object's path.
        /// </summary>
        internal bool DeclaresUserSpecial(string name)
        {
            foreach (var mroType in MRO)
            {
                if (mroType is PyClass userClass && userClass.ClassDict.ContainsKey(name))
                    return true;
                if (!(mroType is PyClass))
                    break;
            }
            return false;
        }

        public PyObject LookupInMRO(string name)
        {
            // Phase 2: Use global TypeMethodCache (CPython-style array cache)
            // This is faster than Dictionary (15-25 cycles vs 30-100+ cycles)
            return PyType.GlobalMethodCache.Lookup(this, name, out _);
        }

        // 클래스 호출 시 인스턴스 생성
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // CPython: bytes subclasses inherit bytes_new, and bytes_new honours the type it was asked
            // for - Objects/bytesobject.c dispatches to bytes_subtype_new, which builds the value and
            // then allocates it as the subclass. This used to return the plain bytes object, which
            // erased the subclass: type(B(b"xy")).__name__ said 'bytes', and a declared __len__,
            // __add__, __contains__, __iter__, __getitem__, __eq__, __hash__ or __repr__ was
            // unreachable. A user-declared __new__ is a real slot and still runs first.
            foreach (var t in MRO)
            {
                if (ReferenceEquals(t, PyType.BytesType))
                {
                    if (!DeclaresUserSpecial("__new__"))
                        return PyBytesSubclass.BytesSubtypeNew(this, args, kwargs);
                    break;
                }
            }

            // Check for abstract methods before allowing instantiation
            CheckAbstractMethods();
            return CreateInstance(args, kwargs);
        }

        // 클래스는 항상 호출 가능 (인스턴스 생성)
        public override bool IsCallable() => true;

        // PEP 695: Generic class subscript support (Stack[int])
        public override PyObject GetItem(PyObject key)
        {
            // CPython 3.12: PEP 560 - Check for __class_getitem__ first
            // Delegate to PyType.GetItem() which handles the full protocol
            return base.GetItem(key);
        }

        // CPython 3.12: Override GetPyType to return metaclass if set
        public override PyType GetPyType()
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClass.GetPyType() called for {Name}");
            Console.WriteLine($"   Metaclass: {Metaclass}");
            Console.WriteLine($"   Metaclass != null: {Metaclass != null}");
            #endif

            // If this class was created with a metaclass, return the metaclass
            if (Metaclass != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → Returning Metaclass: {Metaclass}");
                #endif
                return Metaclass;
            }
            // Otherwise, return the default type (which is 'type')
            #if DEBUG_LOG
            Console.WriteLine($"   → Returning base.GetPyType()");
            #endif
            var baseType = base.GetPyType();
            #if DEBUG_LOG
            Console.WriteLine($"   → base.GetPyType() returned: {baseType}");
            #endif
            return baseType;
        }

        // Class objects use the iteration slots of their metaclass.
        public override PyObject GetIterator()
        {
            var method = LookupSpecialMethodUncached("__iter__");
            if (method != null) return InvokeIteratorMethod(method);
            if (LookupSpecialMethodUncached("__getitem__") != null)
                return new PyGenericIterator(this);
            throw PyTypeError.Create($"'{GetPyType().Name}' object is not iterable");
        }

        internal override bool IsIterator => LookupSpecialMethodUncached("__next__") != null;

        public override PyObject Next() => InvokeNextMethod();

        // 클래스 attribute 접근
        public override PyObject GetAttribute(string name)
        {
            // _Py_type_getattro_impl: metatype data descriptors precede the
            // class namespace, including descriptors inherited by the metatype.
            var metaAttribute = Metaclass == null ? null : MROUtils.LookupAttribute(Metaclass.MRO, name);
            if (metaAttribute != null && PyClassInstance.IsDescriptor(metaAttribute) &&
                PyClassInstance.IsDataDescriptor(metaAttribute))
                return PyClassInstance.CallDescriptorGet(metaAttribute, this, Metaclass);

            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClass.GetAttribute: {Name}.{name}");
            #endif
            
            // CPython 3.12: All type attributes (__name__, __bases__, __mro__, __dict__, __module__, etc.)
            // are now handled by descriptors in the metaclass, no need for hardcoded switch cases
            switch (name)
            {
                case "__call__":
                    // CPython 3.12: __call__ is special - class itself is callable
                    #if DEBUG_LOG
                    Console.WriteLine($"   → returning self for __call__");
                    #endif
                    return this;
                default:
                    #if DEBUG_LOG
                    Console.WriteLine($"   → searching for '{name}' in ClassDict ({ClassDict.Count} items)");
                    #endif

                    if (ClassDict.TryGetValue(name, out PyObject value))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ found '{name}' in ClassDict: {value?.GetType().Name}");
                        #endif
                        // Descriptor 처리 (CPython 3.12: Objects/descrobject.c:271-286, func_descr_get)
                        // CPython: When accessing attribute from CLASS (not instance), pass NULL as instance
                        // Example: MyClass.func → descriptor.__get__(NULL, MyClass)
                        //          instance.func → descriptor.__get__(instance, type(instance))
                        if (PyClassInstance.IsDescriptor(value))
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   🔧 calling descriptor.__get__(null, owner={Name}) for '{name}'");
                            #endif
                            // CPython 3.12: Objects/descrobject.c:271-286 (func_descr_get)
                            // Pass NULL as instance when accessing from CLASS object
                            // This makes function descriptors return unbound functions
                            var result = PyClassInstance.CallDescriptorGet(value, null, this);
                            #if DEBUG_LOG
                            Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                            #endif
                            return result;
                        }
                        #if DEBUG_LOG
                        Console.WriteLine($"   → returning direct value: {value}");
                        #endif
                        return value;
                    }
                    #if DEBUG_LOG
                    Console.WriteLine($"   ❌ '{name}' not found in ClassDict, checking MRO");
                    #endif

                    // Check MRO (Method Resolution Order) - skip self (index 0)
                    for (int i = 1; i < MRO.Count; i++)
                    {
                        var baseClass = MRO[i];

                        // Handle both PyClass and PyType in MRO
                        if (baseClass is PyClass pyClass)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Checking MRO class: {pyClass.Name}");
                            #endif
                            if (pyClass.ClassDict.TryGetValue(name, out PyObject baseValue))
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"   ✅ found '{name}' in MRO class {pyClass.Name}: {baseValue?.GetType().Name}");
                                #endif
                                // Descriptor 처리 (CPython 3.12: Objects/typeobject.c:4830)
                                if (PyClassInstance.IsDescriptor(baseValue))
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   🔧 calling descriptor.__get__(null, {Name}) for '{name}' from MRO");
                                    #endif
                                    // CPython 3.12: Class attribute access passes NULL as instance
                                    // Reference: Objects/funcobject.c:892 (if obj == NULL, return Py_NewRef(func))
                                    var result = PyClassInstance.CallDescriptorGet(baseValue, null, this);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                                    #endif
                                    return result;
                                }
                                return baseValue;
                            }
                        }
                        else if (baseClass is PyType pyType)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   → Checking MRO type: {pyType.Name}");
                            #endif
                            // CPython 3.12: PyType의 속성을 직접 체크
                            // 재귀 방지를 위해 PyType의 internal attribute lookup 사용
                            var typeAttribute = GetTypeAttribute(pyType, name);
                            if (typeAttribute != null)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"   ✅ found '{name}' in MRO type {pyType.Name}: {typeAttribute?.GetType().Name}");
                                #endif
                                // CPython 3.12: Objects/typeobject.c:4852-4859
                                // When descriptor is found in type's tp_dict (via MRO),
                                // call descriptor.__get__(NULL, owner) where owner is this class
                                if (PyClassInstance.IsDescriptor(typeAttribute))
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   🔧 calling descriptor.__get__(null, {Name}) for '{name}' from PyType MRO");
                                    #endif
                                    var result = PyClassInstance.CallDescriptorGet(typeAttribute, null, this);
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   → descriptor returned: {result?.GetType().Name}");
                                    #endif
                                    return result;
                                }
                                return typeAttribute;
                            }
                        }
                    }

                    // CPython 3.12: Check metaclass (type of this class) for attributes
                    #if DEBUG_LOG
                    Console.WriteLine($"   ❌ '{name}' not found in MRO, checking metaclass");
                    #endif
                    if (Metaclass != null)
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → checking metaclass '{Metaclass.Name}' for '{name}'");
                        #endif

                        // Check metaclass's ClassDict and MRO
                        if (Metaclass.ClassDict.TryGetValue(name, out PyObject metaclassValue))
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   ✅ found '{name}' in metaclass: {metaclassValue?.GetType().Name}");
                            #endif

                            // Descriptor 처리 - metaclass descriptor는 class 객체에 바인딩
                            if (metaclassValue is IDescriptor metaDesc)
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"   🔧 calling metaclass descriptor.Get({Name}, {Metaclass.Name})");
                                #endif
                                var result = metaDesc.Get(this, Metaclass);
                                #if DEBUG_LOG
                                Console.WriteLine($"   → metaclass descriptor returned: {result?.GetType().Name}");
                                #endif
                                return result;
                            }

                            return metaclassValue;
                        }

                        // Check metaclass MRO (e.g., type's __iter__)
                        for (int i = 1; i < Metaclass.MRO.Count; i++)
                        {
                            var metaBase = Metaclass.MRO[i];
                            if (metaBase is PyClass metaPyClass && metaPyClass.ClassDict.TryGetValue(name, out PyObject metaBaseValue))
                            {
                                #if DEBUG_LOG
                                Console.WriteLine($"   ✅ found '{name}' in metaclass MRO ({metaBase.Name}): {metaBaseValue?.GetType().Name}");
                                #endif

                                // Descriptor 처리
                                if (metaBaseValue is IDescriptor metaBaseDesc)
                                {
                                    #if DEBUG_LOG
                                    Console.WriteLine($"   🔧 calling metaclass MRO descriptor.Get({Name}, {Metaclass.Name})");
                                    #endif
                                    return metaBaseDesc.Get(this, Metaclass);
                                }

                                return metaBaseValue;
                            }
                        }
                    }

                    #if DEBUG_LOG
                    Console.WriteLine($"   ❌ '{name}' not found in metaclass, calling PyObject.GetAttribute");
                    #endif
                    var baseResult = base.GetAttribute(name);
                    #if DEBUG_LOG
                    Console.WriteLine($"   → base.GetAttribute returned: {baseResult?.GetType().Name}");
                    #endif
                    return baseResult;
            }
        }

        public override void SetAttribute(string name, PyObject value)
        {
#if DEBUG
            // Debug: Track _generate_next_value_ modifications
            if (name == "_generate_next_value_")
            {
                Console.WriteLine($"[PyClass.SetAttribute DEBUG] Setting '{name}' on class '{Name}'");
                var oldValue = ClassDict.ContainsKey(name) ? ClassDict[name].ToString() : "NOT SET";
                var oldType = ClassDict.ContainsKey(name) ? ClassDict[name].GetTypeName() : "N/A";
                Console.WriteLine($"[PyClass.SetAttribute DEBUG]   Old value: {oldValue}, type={oldType}");
                Console.WriteLine($"[PyClass.SetAttribute DEBUG]   New value: {value}, type={value.GetTypeName()}");
                // Print stack trace to see where this is being called from
                var stackTrace = new System.Diagnostics.StackTrace(1, true);
                Console.WriteLine($"[PyClass.SetAttribute DEBUG]   Stack trace:");
                for (int i = 0; i < Math.Min(15, stackTrace.FrameCount); i++)
                {
                    var frame = stackTrace.GetFrame(i);
                    var method = frame?.GetMethod();
                    Console.WriteLine($"[PyClass.SetAttribute DEBUG]     [{i}] {method?.DeclaringType?.Name}.{method?.Name}");
                }
            }
#endif
            // Note: Metaclass __setattr__ support is disabled due to super() compatibility issues.
            // This is a known limitation - metaclass __setattr__ won't be called.
            // TODO: Fix super() for metaclass contexts to enable this feature.

            ClassDict[name] = value;

            // CPython 3.12: Invalidate method cache when class dict changes
            // Reference: Objects/typeobject.c:420-522 (type_modified)
            InvalidateTypeCache();
        }

        // CPython 3.12: Override DelAttribute for type objects
        protected override void PyDelAttribute(string name)
        {
            // A heap class is writable, so type_setattro reaches object_set_class, which refuses
            // deletion of __class__ with TypeError (Objects/typeobject.c:5712-5719). Without this the
            // lookup below answered "type object 'C' has no attribute '__class__'".
            if (name == "__class__")
                throw PyTypeError.Create("can't delete __class__ attribute");

            if (ClassDict.ContainsKey(name))
            {
                ClassDict.Remove(name);

                // CPython 3.12: Invalidate method cache when class dict changes
                InvalidateTypeCache();
                return;
            }
            // CPython type_setattro delete of a missing class attribute:
            // "type object '<class>' has no attribute '<name>'".
            throw PyAttributeError.Create($"type object '{Name}' has no attribute '{name}'");
        }

        /// <summary>
        /// PEP 698: @override 데코레이터 런타임 검증
        /// 클래스가 생성될 때 @override가 적용된 메서드들이 실제로 부모 클래스의 메서드를 오버라이드하는지 확인
        /// </summary>
        private void ValidateOverrideDecorators()
        {
            foreach (var kvp in ClassDict)
            {
                string methodName = kvp.Key;
                PyObject methodValue = kvp.Value;

                // 함수이고 __override__ 속성이 True인 경우만 검증
                if (methodValue is PyFunction function && HasOverrideAttribute(function))
                {
                    // 부모 클래스들에서 이 메서드가 존재하는지 확인
                    bool foundInBase = false;
                    
                    foreach (var baseType in BaseTypes)
                    {
                        if (baseType != PyType.ObjectType && HasMethodInBase(baseType, methodName))
                        {
                            foundInBase = true;
                            break;
                        }
                    }
                    
                    if (!foundInBase)
                    {
                        throw PyTypeError.Create($"Method '{methodName}' is marked with @override, but does not override any method in base class");
                    }
                }
            }
        }

        /// <summary>
        /// 함수가 @override 데코레이터를 가지고 있는지 확인
        /// </summary>
        private bool HasOverrideAttribute(PyFunction function)
        {
            try
            {
                if (function.Attributes.TryGetValue("__override__", out PyObject overrideAttr))
                {
                    return overrideAttr.PyBoolValue();
                }
            }
            catch
            {
                // 속성 접근 실패시 false 반환
            }
            return false;
        }

        /// <summary>
        /// 기반 클래스에서 특정 메서드가 존재하는지 확인
        /// </summary>
        private bool HasMethodInBase(PyType baseType, string methodName)
        {
            try
            {
                // 직접 속성 확인
                if (baseType is PyClass baseClass)
                {
                    if (baseClass.ClassDict.ContainsKey(methodName))
                        return true;
                }
                
                // GetAttribute로 시도 (상속 체인을 통해)
                try
                {
                    var attr = baseType.GetAttribute(methodName);
                    return attr is PyFunction || attr is PyMethod;
                }
                catch (Exception)
                {
                    // 속성이 없거나 접근 실패시 false
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// CPython 3.12 compatible: Safe type attribute lookup without recursion
        /// </summary>
        public static PyObject? GetTypeAttribute(PyType pyType, string name)
        {
            // CPython 3.12: 내장 타입의 속성을 직접 조회
            // 재귀를 방지하기 위해 PyType의 내부 구조를 직접 사용

            try
            {
                // FIRST: Check TypeDict for descriptors (like __setattr__, __getattribute__, etc.)
                // This is critical for finding dynamically added descriptors
                if (pyType.TypeDict != null && pyType.TypeDict.TryGetValue(name, out PyObject descriptor))
                {
                    return descriptor;
                }

                // SECOND: Fall back to hardcoded switch for legacy/special attributes
                // object 타입의 기본 속성들
                if (pyType == PyType.ObjectType)
                {
                    return name switch
                    {
                        "__class__" => PyType.TypeType,
                        "__str__" => new PyBuiltinMethod("__str__", (self, args) => {
                            if (args.Length != 0)
                                throw PyTypeError.Create($"__str__() takes no arguments ({args.Length} given)");
                            return self.ToStr();
                        }, 1),
                        "__repr__" => new PyBuiltinMethod("__repr__", (self, args) => {
                            if (args.Length != 0)
                                throw PyTypeError.Create($"__repr__() takes no arguments ({args.Length} given)");
                            return self.ToRepr();
                        }, 1),
                        "__init__" => new PyBuiltinMethod("__init__", (self, args) => PyNone.Instance, 1),
                        "__hash__" => new PyBuiltinMethod("__hash__", (self, args) => {
                            if (args.Length != 0)
                                throw PyTypeError.Create($"__hash__() takes no arguments ({args.Length} given)");
                            return new PyInt(self.GetHashCode());
                        }, 1),
                        "__eq__" => new PyBuiltinMethod("__eq__", (self, args) => {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"__eq__() takes exactly 1 argument ({args.Length} given)");
                            return ReferenceEquals(self, args[0]) ? PyBool.True : PyNotImplemented.Instance;
                        }, 2),
                        "__ne__" => new PyBuiltinMethod("__ne__", (self, args) => {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"__ne__() takes exactly 1 argument ({args.Length} given)");
                            var result = self.RichCompare(args[0], PyObject.CompareOp.EQ);
                            return result == PyNotImplemented.Instance ? result : PyBool.FromBool(!result.IsTrue());
                        }, 2),
                        _ => null
                    };
                }

                // type 타입의 기본 속성들
                if (pyType == PyType.TypeType)
                {
                    return name switch
                    {
                        "__mro__" => null, // 이미 PyClass.GetAttribute에서 처리됨
                        "__bases__" => null, // 이미 PyClass.GetAttribute에서 처리됨
                        "__name__" => null, // 이미 PyClass.GetAttribute에서 처리됨
                        _ => null
                    };
                }

                // dict 타입의 메서드들 - CPython 3.12 compatible
                if (pyType == PyType.DictType)
                {
                    // Helper to get PyDict from self (works for both PyDict and dict subclasses)
                    static PyDict GetDictStorage(PyObject self)
                    {
                        if (self is PyDict dict)
                            return dict;

                        if (self is PyClassInstance instance && instance.InstanceType.BaseTypes.Any(bt => bt == PyType.DictType))
                        {
                            // Access the _dictStorage field via reflection or provide public accessor
                            var field = typeof(PyClassInstance).GetField("_dictStorage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            return (PyDict)field?.GetValue(instance);
                        }

                        throw PyTypeError.Create($"descriptor requires a 'dict' object but received a '{self.GetTypeName()}'");
                    }

                    return name switch
                    {
                        // Note: dict.__init__ is now implemented in PyType.cs DictType initialization
                        // The TypeDict lookup in GetTypeAttribute will find it there

                        // CPython 3.12: __getitem__ wrapper descriptor (mp_subscript slot)
                        "__getitem__" => new PyWrapperDescriptor(
                            "__getitem__",
                            PyType.DictType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 1)
                                    throw PyTypeError.Create($"__getitem__() takes exactly 1 argument ({args.Length} given)");

                                var key = args[0];
                                // CPython 3.12: Call self.GetItem() which allows subclass override
                                return self.GetItem(key);
                            }),

                        // CPython 3.12: __setitem__ wrapper descriptor (mp_ass_subscript slot)
                        "__setitem__" => new PyWrapperDescriptor(
                            "__setitem__",
                            PyType.DictType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 2)
                                    throw PyTypeError.Create($"__setitem__() takes exactly 2 arguments ({args.Length} given)");

                                var key = args[0];
                                var value = args[1];

                                // CPython 3.12: Direct storage access (dict_ass_sub in C)
                                // This wrapper descriptor represents the C-level slot, not Python method
                                PyDict dict;
                                if (self is PyDict d)
                                {
                                    dict = d;
                                }
                                else if (self is PyClassInstance ci && ci.IsDictSubclass())
                                {
                                    dict = ci.GetDictStorage();
                                }
                                else
                                {
                                    throw PyTypeError.Create($"descriptor '__setitem__' for 'dict' objects doesn't apply to a '{self.GetTypeName()}'  object");
                                }

                                dict.SetItem(key, value);
                                return PyNone.Instance;
                            }),

                        "get" => new PyBuiltinFunction("get", (args, kwargs) =>
                        {
                            if (args.Length < 2 || args.Length > 3)
                                throw PyTypeError.Create($"get() takes from 2 to 3 positional arguments but {args.Length} were given");

                            var self = args[0];
                            var key = args[1];
                            var defaultValue = args.Length > 2 ? args[2] : PyNone.Instance;

                            // CPython 3.12: Use GetItem with exception handling (works for both PyDict and dict subclasses)
                            try
                            {
                                return self.GetItem(key);
                            }
                            catch (PythonException ex) when (ex.PyException is PyKeyError)
                            {
                                return defaultValue;
                            }
                        }),

                        "keys" => new PyBuiltinFunction("keys", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"keys() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return new PyDictView(dict, DictViewKind.Keys);
                        }),

                        "values" => new PyBuiltinFunction("values", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"values() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return new PyDictView(dict, DictViewKind.Values);
                        }),

                        "items" => new PyBuiltinFunction("items", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"items() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return new PyDictView(dict, DictViewKind.Items);
                        }),

                        "pop" => new PyBuiltinFunction("pop", (args, kwargs) =>
                        {
                            if (args.Length < 2 || args.Length > 3)
                                throw PyTypeError.Create($"pop() takes from 2 to 3 positional arguments but {args.Length} were given");

                            var dict = GetDictStorage(args[0]);
                            var key = args[1];
                            var defaultValue = args.Length > 2 ? args[2] : null;
                            return dict.Pop(key, defaultValue);
                        }),

                        "clear" => new PyBuiltinFunction("clear", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"clear() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            dict.Clear();
                            return PyNone.Instance;
                        }),

                        "update" => new PyBuiltinFunction("update", (args, kwargs) =>
                        {
                            if (args.Length != 2)
                                throw PyTypeError.Create($"update() takes exactly 2 arguments ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);

                            if (args[1] is PyDict otherDict)
                            {
                                dict.Update(otherDict);
                            }
                            else if (args[1] is PyClassInstance otherInstance && otherInstance.InstanceType.BaseTypes.Any(bt => bt == PyType.DictType))
                            {
                                // Dict subclass - get its storage
                                var otherStorage = GetDictStorage(args[1]);
                                dict.Update(otherStorage);
                            }
                            else
                            {
                                throw PyTypeError.Create($"update() argument must be dict, not '{args[1].GetTypeName()}'");
                            }
                            return PyNone.Instance;
                        }),

                        "setdefault" => new PyBuiltinFunction("setdefault", (args, kwargs) =>
                        {
                            if (args.Length < 2 || args.Length > 3)
                                throw PyTypeError.Create($"setdefault() takes from 2 to 3 positional arguments but {args.Length} were given");

                            var dict = GetDictStorage(args[0]);
                            var key = args[1];
                            var defaultValue = args.Length > 2 ? args[2] : PyNone.Instance;
                            return dict.SetDefault(key, defaultValue);
                        }),

                        "copy" => new PyBuiltinFunction("copy", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"copy() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return dict.Copy();
                        }),

                        _ => null
                    };
                }

                // list item-protocol descriptors (__getitem__/__setitem__/__delitem__/__contains__)
                // are materialized in type/PyList.cs InitializeListDescriptors, so MRO lookup
                // finds them in TypeDict before reaching this reflection fallback.

                // 다른 내장 타입들은 기본적으로 object의 속성을 상속
                if (pyType.Name == "object")
                    return null;

                // 기본적으로는 null 반환 (속성 없음)
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if this class has any unimplemented abstract methods
        /// Throws TypeError if abstract methods are found (CPython 3.12 compatible)
        /// Reference: Objects/typeobject.c:5459-5505 (object_new)
        /// </summary>
        private void CheckAbstractMethods()
        {
            // CPython 3.12: Check __abstractmethods__ attribute directly
            // Reference: Objects/typeobject.c:5468 (type_abstractmethods)
            PyObject abstractMethodsAttr = null;
            try
            {
                // Get __abstractmethods__ from class (NOT instance)
                // This is set by AbcModule._abc_init or manually
                abstractMethodsAttr = this.GetAttribute("__abstractmethods__");
            }
            catch
            {
                // No __abstractmethods__ attribute - class is not abstract
                return;
            }

            // Check if __abstractmethods__ is None or empty
            if (abstractMethodsAttr == null || abstractMethodsAttr == PyNone.Instance)
            {
                return;
            }

            // __abstractmethods__ should be a frozenset or set
            // If it's non-empty, prevent instantiation
            if (abstractMethodsAttr is PyFrozenSet frozenSet)
            {
                if (frozenSet.Items.Count == 0)
                {
                    return; // Empty frozenset - all methods implemented
                }

                // CPython 3.12: Sort method names and format error message
                // Reference: Objects/typeobject.c:5471-5502
                var methodNames = frozenSet.Items
                    .Select(item => ((PyString)item).Value)
                    .OrderBy(name => name)
                    .ToList();

                // Format: "Can't instantiate abstract class X without an implementation for abstract method 'foo'"
                // or: "Can't instantiate abstract class X without an implementation for abstract methods 'bar', 'foo'"
                string joinedMethods = string.Join("', '", methodNames);
                string methodWord = methodNames.Count == 1 ? "method" : "methods";

                throw PyTypeError.Create(
                    $"Can't instantiate abstract class {Name} " +
                    $"without an implementation for abstract {methodWord} '{joinedMethods}'"
                );
            }
            else if (abstractMethodsAttr is PySet set)
            {
                if (set.Items.Count == 0)
                {
                    return; // Empty set - all methods implemented
                }

                // Same logic for PySet
                var methodNames = set.Items
                    .Select(item => ((PyString)item).Value)
                    .OrderBy(name => name)
                    .ToList();

                string joinedMethods = string.Join("', '", methodNames);
                string methodWord = methodNames.Count == 1 ? "method" : "methods";

                throw PyTypeError.Create(
                    $"Can't instantiate abstract class {Name} " +
                    $"without an implementation for abstract {methodWord} '{joinedMethods}'"
                );
            }
        }

    }

    #endregion

    #region Class Instances

    /// <summary>
    /// 사용자 정의 클래스의 인스턴스
    /// </summary>
    public class PyClassInstance : PyObject, IInstanceDictAccessor
    {
        public PyClass InstanceType { get; }
        public Dictionary<string, PyObject> InstanceDict { get; }
        public PyObject[] ConstructorArgs { get; set; } // Store constructor arguments
        private PyFunction _customGetAttr;

        // CPython 3.12: Dict subclasses have internal dict storage
        private PyDict _dictStorage;

        // CPython 3.12: List subclasses have internal list storage
        private PyList _listStorage;

        // CPython 3.12: set subclasses carry a mutable set value. tp_new (make_new_set,
        // Objects/setobject.c:949) allocates it empty and tp_init (setobject.c:1977 set_init)
        // clears and fills it from the one optional iterable argument.
        private PySet _setStorage;

        // CPython 3.12: frozenset has **no** tp_init (Objects/setobject.c:2262 "0, /* tp_init */"),
        // so the value exists only because frozenset_new (setobject.c:1003) built it out of the
        // construction arguments. SharpPy materializes it lazily from those arguments, the same way
        // _strStorage below does for str, and never re-reads them.
        private PyFrozenSet _frozenStorage;

        // CPython 3.12: str subclasses carry their immutable value in the object itself
        // (PyUnicode_Subclass). SharpPy materializes it lazily from the construction
        // arguments: str() semantics (no arg -> "", one arg -> str(x), more -> TypeError).
        // Contents come from the constructor call, never from a user __str__.
        private PyString _strStorage;

        // CPython 3.12: Cache the __dict__ wrapper for identity consistency
        private PyDict _dictCache;

        public PyClassInstance(PyClass instanceType)
        {
            InstanceType = instanceType;
            InstanceDict = new Dictionary<string, PyObject>();
            ConstructorArgs = new PyObject[0]; // Default empty args

            // __getattr__ 메서드가 있는지 확인
            if (instanceType.ClassDict.ContainsKey("__getattr__"))
            {
                _customGetAttr = instanceType.ClassDict["__getattr__"] as PyFunction;
            }

            // CPython 3.12: If this is a dict subclass, create internal dict storage
            if (IsDictSubclass())
            {
                _dictStorage = new PyDict();
            }

            // CPython 3.12: If this is a list subclass, create internal list storage
            if (IsListSubclass())
            {
                _listStorage = new PyList();
            }

            // CPython 3.12: make_new_set gives a set (sub)class an empty table before tp_init runs.
            if (IsSetSubclass())
            {
                _setStorage = new PySet();
            }
        }

        // "Is this instance a dict/list?" is a type question, so it is answered from the type
        // flags rather than by walking the MRO per call, exactly as CPython's PyDict_Check and
        // PyList_Check read Py_TPFLAGS_DICT_SUBCLASS / Py_TPFLAGS_LIST_SUBCLASS off ob_type
        // (Include/dictobject.h, Include/listobject.h:24). Multi-level inheritance comes for free
        // because inherit_special propagates the bit to every subtype
        // (Objects/typeobject.c:6726) - TracedOrderedDict -> OrderedDict -> dict carries it.
        public bool IsDictSubclass() => InstanceType.HasFeature(PyTypeFlags.DictSubclass);

        public bool IsListSubclass() => InstanceType.HasFeature(PyTypeFlags.ListSubclass);

        public bool IsStrSubclass() => InstanceType.HasFeature(PyTypeFlags.UnicodeSubclass);

        // set and frozenset have no family bit in CPython: PySet_Check (Include/setobject.h:36)
        // and PyAnySet_Check (:30) are PyType_IsSubtype tests, so the MRO walk is the faithful
        // answer for these two and is kept.
        public bool IsSetSubclass() => InstanceType.IsSubclassOf(PyType.SetType);

        public bool IsFrozenSetSubclass() => InstanceType.IsSubclassOf(PyType.FrozenSetType);

        /// <summary>
        /// The value a set subclass instance carries, or null when this is not one.
        /// </summary>
        public PySet GetSetStorage() => _setStorage;

        /// <summary>
        /// The value a frozenset subclass instance carries, built the way CPython's frozenset_new
        /// builds it: at most one positional iterable, taken from the construction arguments.
        /// </summary>
        public PyFrozenSet GetFrozenStorage()
        {
            if (_frozenStorage != null)
                return _frozenStorage;
            if (!IsFrozenSetSubclass())
                return null;

            var a = ConstructorArgs ?? new PyObject[0];
            // Objects/setobject.c:1013 PyArg_UnpackTuple(args, type->tp_name, 0, 1, &iterable)
            if (a.Length > 1)
                throw PyTypeError.Create($"{InstanceType.Name} expected at most 1 argument, got {a.Length}");
            _frozenStorage = a.Length == 0
                ? new PyFrozenSet()
                : new PyFrozenSet(PySet.IterableItems(a[0]));
            return _frozenStorage;
        }

        /// <summary>
        /// The builtin container value this instance carries, or null for a plain class. CPython
        /// answers this with slot inheritance (PyType_Ready installs the base's C slot on the
        /// subclass); here the value lives in a storage object, and a class can carry at most one
        /// variable-size builtin base - CPython refuses the combination with "multiple bases have
        /// instance lay-out conflict". Every protocol operation (length, containment, iteration,
        /// repr) asks this one question instead of repeating the per-type branch.
        /// </summary>
        internal PyObject NativeStorage()
        {
            if (_dictStorage != null) return _dictStorage;
            if (_listStorage != null) return _listStorage;
            if (_setStorage != null) return _setStorage;
            if (IsFrozenSetSubclass()) return GetFrozenStorage();
            return GetStringStorage();
        }

        // Value storage for str subclasses; null when this is not a str subclass.
        public PyString GetStringStorage()
        {
            if (_strStorage != null)
                return _strStorage;
            if (!IsStrSubclass())
                return null;

            var a = ConstructorArgs ?? new PyObject[0];
            if (a.Length == 0)
                _strStorage = new PyString("");
            else if (a.Length == 1)
                _strStorage = a[0] as PyString ?? a[0].ToStr();
            else
                throw PyTypeError.Create($"str() takes at most 1 argument ({a.Length} given)");
            return _strStorage;
        }

        // CPython 3.12: Provide access to internal dict storage for dict subclasses
        public PyDict GetDictStorage()
        {
            return _dictStorage;
        }

        // CPython 3.12: Provide access to internal list storage for list subclasses
        public PyList GetListStorage()
        {
            return _listStorage;
        }

        public override string ToString()
        {
            // Debugging only: Simple C# representation
            return $"<{InstanceType.Name} instance at 0x{GetHashCode():x}>";
        }

        private bool IsExceptionClass()
        {
            // Check if this class inherits from Exception
            return InstanceType.Name.EndsWith("Error") ||
                   InstanceType.Name == "Exception" ||
                   InstanceType.BaseTypes.Any(bt => bt.Name == "Exception" || bt.Name == "BaseException");
        }

        public override PyType GetPyType() => InstanceType;
        public override string GetTypeName() => InstanceType.Name;

        // CPython 3.12: Objects/dictobject.c:2490-2523 (dict_subscript)
        // CPython 3.12: Objects/abstract.c:171-201 (PyObject_GetItem)
        //
        // Same pattern as SetItem:
        // 1. Check ONLY user-defined __getitem__ (PyClass.ClassDict)
        // 2. If found, call it
        // 3. If NOT found, use direct storage (_dictStorage for dict subclasses)
        // 4. NEVER call built-in descriptors from PyType.TypeDict here
        public override PyObject GetItem(PyObject key)
        {
            #if DEBUG
            var keyStr = key is PyString ps ? ps.Value : key?.ToString() ?? "null";
            if (InstanceType.Name == "_EnumDict" && (keyStr == "STRICT" || keyStr == "CONFORM" || keyStr == "EJECT" || keyStr == "KEEP"))
            {
                Console.WriteLine($"[DEBUG-GETITEM] _EnumDict.GetItem('{keyStr}') called, _dictStorage != null: {_dictStorage != null}");
                if (_dictStorage != null && _dictStorage.InternalDict.TryGetValue(key, out var stored))
                {
                    Console.WriteLine($"[DEBUG-GETITEM]   _dictStorage contains '{keyStr}': {stored}, type={stored?.GetType().Name}");
                }
            }
            #endif

            // CPython 3.12: Check for user-defined __getitem__ in MRO
            // Only check PyClass.ClassDict, NOT PyType.TypeDict
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue("__getitem__", out PyObject getItemMethod))
                {
                    // Found user-defined __getitem__
                    if (getItemMethod is PyFunction func)
                    {
                        var boundMethod = new PyMethod(this, func);
                        return boundMethod.Call(new PyObject[] { key }, null);
                    }
                    else if (getItemMethod.IsCallable())
                    {
                        return getItemMethod.Call(new PyObject[] { this, key }, null);
                    }
                    break;
                }
            }

            // CPython 3.12: Fall back to direct storage access
            // For dict subclasses: read from _dictStorage (like PyDict_GetItem)
            if (_dictStorage != null)
            {
                var result = _dictStorage.GetItem(key);
                #if DEBUG
                if (InstanceType.Name == "_EnumDict" && (keyStr == "STRICT" || keyStr == "CONFORM" || keyStr == "EJECT" || keyStr == "KEEP"))
                {
                    Console.WriteLine($"[DEBUG-GETITEM]   Returning from _dictStorage: {result}, type={result?.GetType().Name}");
                }
                #endif
                return result;
            }

            // CPython 3.12: str subclasses inherit unicode_subscript (index/slice).
            var strStorage = GetStringStorage();
            if (strStorage != null)
                return strStorage.GetItem(key);

            // No __getitem__ and not a dict subclass
            return base.GetItem(key);
        }

        // CPython 3.12: Objects/dictobject.c:1879-1889 (PyDict_SetItem)
        // CPython 3.12: Objects/abstract.c:203-234 (PyObject_SetItem)
        // CPython 3.12: Lib/enum.py:509 (super().__setitem__)
        //
        // In CPython, PyObject_SetItem directly calls tp_as_mapping->mp_ass_subscript,
        // which for dict calls PyDict_SetItem (dictobject.c:2524-2530).
        // PyDict_SetItem checks PyDict_Check and writes to internal storage.
        //
        // For dict subclasses, CPython creates actual PyDictObject instances,
        // so mp_ass_subscript always gets the real dict's function pointer.
        // User-defined __setitem__ is only checked at VM level (STORE_SUBSCR),
        // NOT at the type slot level.
        //
        // SharpPy equivalent:
        // 1. Check ONLY user-defined __setitem__ (PyClass.ClassDict)
        // 2. If found, call it and return (user code must call super().__setitem__ to store)
        // 3. If NOT found, use direct storage (_dictStorage for dict subclasses)
        // 4. NEVER call built-in descriptors from PyType.TypeDict here
        //    (they are already handled at VM level in STORE_SUBSCR)
        public override void SetItem(PyObject key, PyObject value)
        {
            // CPython 3.12 Objects/dictobject.c:1879-1889 (PyDict_SetItem):
            //   For dict subclasses with user-defined __setitem__:
            //   - Call user-defined __setitem__ ONLY
            //   - Internal storage is updated ONLY if user calls super().__setitem__()
            //   - If user doesn't call super().__setitem__(), storage is NOT updated
            //
            // This is critical for Lib/enum.py:509 where _EnumDict.__setitem__
            // processes auto() values before calling super().__setitem__()

            // Check for user-defined __setitem__ in MRO
            // Only check PyClass.ClassDict, NOT PyType.TypeDict
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue("__setitem__", out var setitemMethod))
                {
                    // Found user-defined __setitem__ - call it and return
                    // Storage will be updated only if user code calls super().__setitem__()
                    if (setitemMethod is PyFunction func)
                    {
                        var boundMethod = new PyMethod(this, func);
                        boundMethod.Call(new PyObject[] { key, value }, null);
                    }
                    else if (setitemMethod.IsCallable())
                    {
                        setitemMethod.Call(new PyObject[] { this, key, value }, null);
                    }
                    return;  // User __setitem__ found and called, we're done
                }
            }

            // No user-defined __setitem__ found
            // For dict subclasses: write directly to internal storage
            if (_dictStorage != null)
            {
                _dictStorage.SetItem(key, value);
                return;
            }

            // For other types: use base implementation
            base.SetItem(key, value);
        }

        // CPython 3.12: Dict subclasses support item deletion (del container[key])
        // Same pattern as SetItem - check user-defined __delitem__ first, then fall back to storage
        public override void DelItem(PyObject key)
        {
            // Check for user-defined __delitem__ in MRO
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue("__delitem__", out var delitemMethod))
                {
                    // Found user-defined __delitem__ - call it and return
                    if (delitemMethod is PyFunction func)
                    {
                        var boundMethod = new PyMethod(this, func);
                        boundMethod.Call(new PyObject[] { key }, null);
                    }
                    else if (delitemMethod.IsCallable())
                    {
                        delitemMethod.Call(new PyObject[] { this, key }, null);
                    }
                    return;
                }
            }

            // No user-defined __delitem__ found
            // For dict subclasses: delete directly from internal storage
            if (_dictStorage != null)
            {
                _dictStorage.DelItem(key);
                return;
            }

            // For other types: use base implementation
            base.DelItem(key);
        }

        // CPython 3.12: Dict subclasses support 'in' operator
        public override PyBool Contains(PyObject item)
        {
            // A declared dunder comes first, because in CPython sq_contains is the slot wrapper
            // around __contains__; consulting the storage first skipped the class's own method.
            var declaredContains = CallUserSpecialOrNull("__contains__", new[] { item });
            if (declaredContains != null)
                return PyBool.FromBool(declaredContains.PyBoolValue());
            var container = NativeStorage();
            if (container != null)
                return container.Contains(item);
            return base.Contains(item);
        }

        // CPython 3.12: str subclasses inherit sq_length/unicode_length.
        public override int Length()
        {
            // The declared __len__ wins, otherwise the inherited base slot reads the storage.
            // Only str's storage was consulted here, so len() of a plain dict subclass raised
            // "object of type 'DPlain' has no len()" while its own __len__ happened to work.
            var declaredLength = CallUserSpecialOrNull("__len__", System.Array.Empty<PyObject>());
            if (declaredLength != null)
                return ToLengthResult(declaredLength);
            var container = NativeStorage();
            if (container != null)
                return container.Length();
            return base.Length();
        }

        // CPython 3.12: PyObject_GetIter / slot_tp_iter.
        public override PyObject GetIterator()
        {
            var method = LookupSpecialMethodUncached("__iter__");
            if (method != null) return InvokeIteratorMethod(method);

            // The value of the builtin base in the MRO (dict/list/set/frozenset/str) is iterated,
            // because CPython's slot for that base was inherited by the subclass.
            var containerIter = NativeStorage();
            if (containerIter != null) return containerIter.GetIterator();
            if (LookupSpecialMethodUncached("__getitem__") != null)
                return new PyGenericIterator(this);
            return base.GetIterator();
        }

        internal override bool IsIterator => LookupSpecialMethodUncached("__next__") != null;

        /// <summary>
        /// CPython 3.12: Get next item by calling __next__ method
        /// </summary>
        public override PyObject Next() => InvokeNextMethod();

        /// <summary>
        /// CPython 3.12: Helper to call a magic method if it exists
        /// Searches instance dict first, then class MRO
        /// </summary>
        private PyObject CallMagicMethod(string methodName, params PyObject[] args)
        {
            // Check instance dict first
            if (InstanceDict.ContainsKey(methodName))
            {
                var method = InstanceDict[methodName];
                return method.Call(args, null);
            }

            // Then check class hierarchy (MRO)
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(methodName, out PyObject method))
                {
                    // Bind method to this instance and call
                    if (method is PyFunction func)
                    {
                        var boundMethod = new PyMethod(this, func);
                        return boundMethod.Call(args, null);
                    }
                    else if (method.IsCallable())
                    {
                        // For non-function callables, pass self as first argument
                        var argsWithSelf = new PyObject[args.Length + 1];
                        argsWithSelf[0] = this;
                        Array.Copy(args, 0, argsWithSelf, 1, args.Length);
                        return method.Call(argsWithSelf, null);
                    }
                    break;
                }
            }

            return null; // Method not found
        }

        /// <summary>
        /// CPython 3.12: Binary operation with magic method support
        /// Implements the protocol: try __add__, then __radd__ on other
        /// </summary>
        private PyObject BinaryOpWithMagicMethod(PyObject other, string methodName, string reflectedMethodName)
        {
            // Try left operand's method first (e.g., self.__add__(other))
            var result = CallMagicMethod(methodName, other);
            if (result != null)
            {
                return result;
            }

            // Try right operand's reflected method (e.g., other.__radd__(self))
            if (other is PyClassInstance otherInstance)
            {
                result = otherInstance.CallMagicMethod(reflectedMethodName, this);
                if (result != null)
                {
                    return result;
                }
            }

            // No magic method found - fall back to base implementation (will throw TypeError)
            return null;
        }

        /// <summary>
        /// The operator slot a subclass inherits from its builtin base. CPython installs it through
        /// slot inheritance, so C([1]) + [2], C([1]) * 2, S([1]) | {2} and D({'a':1}) | {'b':2} work
        /// even though the class never mentions an operator (Objects/listobject.c list_add /
        /// list_repeat, Objects/setobject.c set_or/set_and/set_sub/set_xor, Objects/dictobject.c
        /// dict_or). The other operand is seen through as well, because those slots test with
        /// PyList_Check / PySet_Check / PyDict_Check, which accept subclasses.
        /// Null means "no such slot", which lets the caller produce the normal TypeError.
        /// </summary>
        private PyObject InheritedSlotBinary(PyObject other, Func<PyObject, PyObject, PyObject> slot)
        {
            var container = NativeStorage();
            if (container == null)
                return null;
            var value = slot(container, PyNativeStorage.Unwrap(other));
            return ReferenceEquals(value, PyNotImplemented.Instance) ? null : value;
        }

        // CPython 3.12: Magic method overrides for binary operations
        public override PyObject Add(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__add__", "__radd__");
            if (result != null) return result;
            var inherited = InheritedSlotBinary(other, (a, b) => a.Add(b));
            if (inherited != null) return inherited;
            return base.Add(other);
        }

        public override PyObject Subtract(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__sub__", "__rsub__");
            if (result != null) return result;
            return InheritedSlotBinary(other, (a, b) => a.Subtract(b)) ?? base.Subtract(other);
        }

        public override PyObject Multiply(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__mul__", "__rmul__");
            if (result != null) return result;
            return InheritedSlotBinary(other, (a, b) => a.Multiply(b)) ?? base.Multiply(other);
        }

        public override PyObject Divide(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__truediv__", "__rtruediv__");
            return result ?? base.Divide(other);
        }

        public override PyObject FloorDivide(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__floordiv__", "__rfloordiv__");
            return result ?? base.FloorDivide(other);
        }

        public override PyObject Modulo(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__mod__", "__rmod__");
            return result ?? base.Modulo(other);
        }

        public override PyObject Power(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__pow__", "__rpow__");
            return result ?? base.Power(other);
        }

        public override PyObject LeftShift(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__lshift__", "__rlshift__");
            return result ?? base.LeftShift(other);
        }

        public override PyObject RightShift(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__rshift__", "__rrshift__");
            return result ?? base.RightShift(other);
        }

        public override PyObject BitwiseAnd(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__and__", "__rand__");
            if (result != null) return result;
            return InheritedSlotBinary(other, (a, b) => a.BitwiseAnd(b)) ?? base.BitwiseAnd(other);
        }

        public override PyObject BitwiseOr(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__or__", "__ror__");
            if (result != null) return result;
            return InheritedSlotBinary(other, (a, b) => a.BitwiseOr(b)) ?? base.BitwiseOr(other);
        }

        public override PyObject BitwiseXor(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__xor__", "__rxor__");
            if (result != null) return result;
            return InheritedSlotBinary(other, (a, b) => a.BitwiseXor(b)) ?? base.BitwiseXor(other);
        }

        public override PyObject MatrixMultiply(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__matmul__", "__rmatmul__");
            return result ?? base.MatrixMultiply(other);
        }

        public override PyObject DivMod(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__divmod__", "__rdivmod__");
            return result ?? base.DivMod(other);
        }

        // CPython 3.12: nb_positive/nb_negative/nb_invert slots for user classes
        // (Objects/typeobject.c slot_nb_* -> __pos__/__neg__/__invert__).
        public override PyObject Positive()
        {
            var m = LookupSpecialMethodUncached("__pos__");
            if (m != null) return m.Call(new PyObject[] { this }, null);
            return base.Positive();
        }

        public override PyObject Negative()
        {
            var m = LookupSpecialMethodUncached("__neg__");
            if (m != null) return m.Call(new PyObject[] { this }, null);
            return base.Negative();
        }

        public override PyObject BitwiseNot()
        {
            var m = LookupSpecialMethodUncached("__invert__");
            if (m != null) return m.Call(new PyObject[] { this }, null);
            return base.BitwiseNot();
        }

        /// <summary>CPython nb_absolute: abs(inst) prefers __abs__.</summary>
        public PyObject AbsoluteOrNotImplemented()
        {
            var m = LookupSpecialMethodUncached("__abs__");
            if (m != null) return m.Call(new PyObject[] { this }, null);
            return PyNotImplemented.Instance;
        }

        protected override bool HasCustomGetAttr() => _customGetAttr != null;

        protected override PyObject CallGetAttr(string name)
        {
            if (_customGetAttr != null)
            {
                return _customGetAttr.Call(new PyObject[] { this, new PyString(name) }, null);
            }
            return null;
        }

        // Descriptor protocol helpers (CPython 3.12: Objects/descrobject.c:1021)

        /// <summary>
        /// Check if an object is a data descriptor
        /// CPython: PyDescr_IsData - checks if tp_descr_set != NULL
        /// Python level: has __set__ or __delete__ method
        /// </summary>
        internal static bool IsDataDescriptor(PyObject obj)
        {
            // C# descriptors implement IDescriptor
            if (obj is IDescriptor desc)
            {
                return desc.IsDataDescriptor();
            }

            // Python class instances: check for __set__ or __delete__ methods
            // CPython: Py_TYPE(ob)->tp_descr_set != NULL
            if (obj is PyClassInstance instance)
            {
                var objType = instance.InstanceType;
                // Use LookupInMRO to bypass __getattribute__ (like _PyType_Lookup)
                return objType.LookupInMRO("__set__") != null ||
                       objType.LookupInMRO("__delete__") != null;
            }

            return false;
        }

        /// <summary>
        /// Check if an object is a descriptor (has __get__)
        /// CPython: tp_descr_get != NULL
        /// </summary>
        internal static bool IsDescriptor(PyObject obj)
        {
            // C# descriptors implement IDescriptor
            if (obj is IDescriptor)
            {
                return true;
            }

            // Python class instances: check for __get__ method
            if (obj is PyClassInstance instance)
            {
                var objType = instance.InstanceType;
                return objType.LookupInMRO("__get__") != null;
            }

            return false;
        }

        /// <summary>
        /// Call descriptor's __get__ method
        /// CPython: tp_descr_get(descr, obj, (PyObject *)Py_TYPE(obj))
        /// </summary>
        internal static PyObject CallDescriptorGet(PyObject descriptor, PyObject instance, PyObject owner)
        {
            // C# descriptors implement IDescriptor
            if (descriptor is IDescriptor desc)
            {
                return desc.Get(instance, owner as PyType);
            }

            // Python class instances: call __get__ method
            if (descriptor is PyClassInstance descInstance)
            {
                var objType = descInstance.InstanceType;
                var getMethod = objType.LookupInMRO("__get__");

                if (getMethod != null)
                {
                    // CPython: When instance is NULL (class attribute access), pass None
                    var instanceArg = instance ?? PyNone.Instance;

                    // Apply descriptor protocol to __get__ itself (it might be a function)
                    if (getMethod is PyFunction func)
                    {
                        // Bind __get__ to the descriptor instance
                        var boundGet = new PyMethod(descriptor, func);
                        // Call: descriptor.__get__(instance, owner)
                        return boundGet.Call(new PyObject[] { instanceArg, owner }, null);
                    }
                    else if (getMethod.IsCallable())
                    {
                        // Call: __get__(descriptor, instance, owner)
                        return getMethod.Call(new PyObject[] { descriptor, instanceArg, owner }, null);
                    }
                }
            }

            // Not a descriptor, return as-is
            return descriptor;
        }

        /// <summary>
        /// Call descriptor's __set__ method
        /// CPython: tp_descr_set(descr, obj, value)
        /// Reference: Objects/object.c:1567-1570
        /// </summary>
        internal static void CallDescriptorSet(PyObject descriptor, PyObject instance, PyObject value)
        {
            // C# descriptors implement IDescriptor
            if (descriptor is IDescriptor desc)
            {
                desc.Set(instance, value);
                return;
            }

            // Python class instances: call __set__ method
            if (descriptor is PyClassInstance descInstance)
            {
                var objType = descInstance.InstanceType;
                var setMethod = objType.LookupInMRO("__set__");

                if (setMethod != null)
                {
                    // Apply descriptor protocol to __set__ itself (it might be a function)
                    if (setMethod is PyFunction func)
                    {
                        // Bind __set__ to the descriptor instance
                        var boundSet = new PyMethod(descriptor, func);
                        // Call: descriptor.__set__(instance, value)
                        boundSet.Call(new PyObject[] { instance, value }, null);
                        return;
                    }
                    else if (setMethod.IsCallable())
                    {
                        // Call: __set__(descriptor, instance, value)
                        setMethod.Call(new PyObject[] { descriptor, instance, value }, null);
                        return;
                    }
                }
            }

            // If we get here, it's a programming error (should have checked IsDataDescriptor first)
            throw PyAttributeError.Create("Descriptor does not have __set__ method");
        }

        // Special attributes

        /// <summary>
        /// Internal method: Generic attribute lookup WITHOUT checking for custom __getattribute__
        /// This is equivalent to CPython's PyObject_GenericGetAttr C function
        /// Used by object.__getattribute__ to avoid infinite recursion
        /// </summary>
        internal PyObject GetAttributeGeneric(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClassInstance.GetAttributeGeneric: {InstanceType.Name} instance.{name} (no custom __getattribute__)");
            #endif

            // 특별한 속성들 먼저 처리
            if (name == "__class__")
            {
                return InstanceType;
            }
            if (name == "__dict__")
            {
                // CPython 3.12: Return cached wrapper for identity consistency
                // obj.__dict__ is obj.__dict__ must be True
                if (InstanceType.InstancesHaveDict())
                {
                    if (_dictCache == null)
                    {
                        _dictCache = new PyInstanceAttrDict(InstanceDict);
                    }
                    return _dictCache;
                }
                // __slots__ class without '__dict__': fall through to the normal
                // MRO lookup, which ends in AttributeError like CPython.
            }

            // CPython 3.12 descriptor protocol (without custom __getattribute__ check):
            // 1. 클래스 MRO에서 data descriptor 찾기 → Get() 호출
            // 2. 인스턴스 __dict__ 검색
            // 3. 클래스 MRO에서 non-data descriptor 또는 일반 attribute 찾기
            // 4. __getattr__ 시도
            // 5. AttributeError

            PyObject classAttribute = null;
            PyClass foundInClass = null;

            // 1. Data descriptor 찾기
            // CPython 3.12: Objects/object.c:1440-1453
            // _PyType_Lookup(tp, name) searches MRO once for both PyClass and PyType
            foreach (var mroType in InstanceType.MRO)
            {
                PyObject foundValue = null;

                // Check PyClass first (user-defined classes)
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    foundValue = classValue;
                    #if DEBUG_LOG
                    Console.WriteLine($"   → Found '{name}' in PyClass {pyClass.Name}: {classValue?.GetType().Name}");
                    #endif
                }
                // Check PyType (built-in types like dict, list, etc.)
                else if (mroType is PyType pyType && !(mroType is PyClass))
                {
                    var typeAttr = SharpPy.PyClass.GetTypeAttribute(pyType, name);
                    if (typeAttr != null)
                    {
                        foundValue = typeAttr;
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Found '{name}' in PyType {pyType.Name}: {typeAttr?.GetType().Name}");
                        #endif
                    }
                }

                if (foundValue != null)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   → IsDataDescriptor: {IsDataDescriptor(foundValue)}");
                    Console.WriteLine($"   → IsDescriptor: {IsDescriptor(foundValue)}");
                    #endif

                    // Check if it's a data descriptor (has __set__ or __delete__)
                    if (IsDataDescriptor(foundValue))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → Calling __get__ for data descriptor");
                        #endif
                        return CallDescriptorGet(foundValue, this, InstanceType);
                    }

                    if (classAttribute == null)
                    {
                        classAttribute = foundValue;
                        foundInClass = mroType is PyClass pc ? pc : null;
                    }
                    break;
                }
            }

            // 2. 인스턴스 __dict__ 검색
            if (InstanceDict.TryGetValue(name, out PyObject instanceValue))
            {
                return instanceValue;
            }

            // 3. 클래스 attribute 처리 (non-data descriptor 포함)
            // CPython 3.12: Objects/object.c:1507-1520
            if (classAttribute != null)
            {
                // Check if it's a descriptor (has __get__)
                if (IsDescriptor(classAttribute))
                {
                    return CallDescriptorGet(classAttribute, this, InstanceType);
                }
                // PyBuiltinMethod: bind to instance (e.g., dict.items)
                else if (classAttribute is PyBuiltinMethod builtinMethod)
                {
                    return builtinMethod.Get(this, InstanceType);
                }
                // PyBuiltinFunction: bind to instance (e.g., dict methods from GetTypeAttribute)
                else if (classAttribute is PyBuiltinFunction builtinFunction)
                {
                    return new PyBuiltinBoundMethod(this, builtinFunction);
                }
                // PyFunction: bind to instance (user-defined methods)
                else if (classAttribute is PyFunction func)
                {
                    return new PyMethod(this, func);
                }
                // Regular class attribute (not a function/descriptor)
                return classAttribute;
            }

            // 5. __getattr__ 시도
            if (HasCustomGetAttr())
            {
                var customResult = CallGetAttr(name);
                if (customResult != null)
                {
                    return customResult;
                }
            }

            // 6. AttributeError
            throw PyAttributeError.Create($"'{InstanceType.Name}' object has no attribute '{name}'");
        }

        public override PyObject GetAttribute(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClassInstance.GetAttribute: {InstanceType.Name} instance.{name}");
            #endif

            // CPython 3.12: Check for custom __getattribute__ FIRST
            // (typeobject.c:8867 - _Py_slot_tp_getattr_hook)
            PyObject customGetAttr = null;
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue("__getattribute__", out customGetAttr))
                {
                    break;
                }
                if (mroType.TypeDict != null && mroType.TypeDict.TryGetValue("__getattribute__", out customGetAttr))
                {
                    // Check if it's NOT the default object.__getattribute__
                    // (CPython: check if d_wrapped == PyObject_GenericGetAttr)
                    if (mroType == PyType.ObjectType)
                    {
                        // This is the default object.__getattribute__, continue with normal logic
                        customGetAttr = null;
                    }
                    break;
                }
            }

            // If custom __getattribute__ found, call it directly
            if (customGetAttr != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   🔍 calling custom __getattribute__");
                #endif
                return customGetAttr.Call(new PyObject[] { this, new PyString(name) }, null);
            }

            // Use GetAttributeGeneric for standard attribute lookup
            // This handles the full CPython 3.12 descriptor protocol
            return GetAttributeGeneric(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyClassInstance.SetAttribute: {InstanceType.Name} instance.{name} = {value}");
            #endif

            // CPython 3.12: Objects/typeobject.c:8893-8910 (slot_tp_setattro)
            // First check for user-defined __setattr__ method
            var setattr = LookupSpecialMethod("__setattr__");
            if (setattr != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → found user __setattr__, calling it");
                #endif
                setattr.Call(new PyObject[] { new PyString(name), value }, null);
                return;
            }

            // No user __setattr__, use default behavior
            SetAttributeDefault(name, value);
        }

        /// <summary>
        /// CPython 3.12: _PyObject_GenericSetAttrWithDict (Objects/object.c:1563-1572)
        /// Default attribute setting behavior without __setattr__ lookup.
        /// Called by object.__setattr__ and directly when no user __setattr__ exists.
        /// </summary>
        public void SetAttributeDefault(string name, PyObject value)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyClassInstance.SetAttributeDefault: {InstanceType.Name} instance.{name} = {value}");
            #endif

            // CPython 3.12 descriptor protocol:
            // Reference: Objects/object.c:1563-1572 (_PyObject_GenericSetAttrWithDict)
            // 1. 클래스 MRO에서 attribute 찾기
            // 2. data descriptor라면 descriptor.__set__() 호출
            // 3. 아니라면 instance.__dict__[name] = value

            // 1. 클래스 MRO에서 descriptor 찾기
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    // 2. data descriptor 확인 및 __set__ 호출
                    if (IsDataDescriptor(classValue))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → found data descriptor in {mroType.Name}, calling __set__()");
                        #endif
                        CallDescriptorSet(classValue, this, value);
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ descriptor __set__() completed");
                        #endif
                        return;
                    }
                    // descriptor가 아니거나 non-data descriptor라면 계속 진행
                    break;
                }
            }

            // 3. 인스턴스 __dict__에 저장
            // CPython 3.12: a __slots__ class that never provides '__dict__' refuses
            // attributes outside the slot names (Objects/object.c generic setattr).
            if (!InstanceType.InstancesHaveDict() && !InstanceType.IsSlottedAttributeName(name))
            {
                throw PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '{name}'");
            }
            InstanceDict[name] = value;

            #if DEBUG_LOG
            Console.WriteLine($"   → stored in instance dict (now {InstanceDict.Count} items)");
            #endif
        }

        /// <summary>
        /// CPython 3.12: Lookup a special method in the class hierarchy, excluding object base class.
        /// Returns null if only object's version is found.
        /// </summary>
        private PyObject LookupSpecialMethod(string name)
        {
            foreach (var mroType in InstanceType.MRO)
            {
                // Skip object type - we don't want object.__setattr__/__delattr__
                if (mroType.Name == "object")
                    continue;

                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject method))
                {
                    // Found user-defined special method, bind it to self
                    if (method is PyFunction func)
                    {
                        return new PyMethod(this, func);
                    }
                    return method;
                }
            }
            return null;
        }

        /// <summary>
        /// CPython 3.12: Objects/typeobject.c:8893-8910 (slot_tp_setattro)
        /// Override DelAttribute to check for user-defined __delattr__ method.
        /// </summary>
        public override void DelAttribute(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyClassInstance.DelAttribute: {InstanceType.Name} instance.{name}");
            #endif

            // CPython 3.12: First check for user-defined __delattr__ method
            var delattr = LookupSpecialMethod("__delattr__");
            if (delattr != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → found user __delattr__, calling it");
                #endif
                delattr.Call(new PyObject[] { new PyString(name) }, null);
                return;
            }

            // No user __delattr__, use default behavior
            DelAttributeDefault(name);
        }

        /// <summary>
        /// CPython 3.12: _PyObject_GenericSetAttrWithDict with value=NULL (Objects/object.c:1563-1572)
        /// Default attribute deletion behavior without __delattr__ lookup.
        /// Called by object.__delattr__ and directly when no user __delattr__ exists.
        /// </summary>
        public void DelAttributeDefault(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔧 PyClassInstance.DelAttributeDefault: {InstanceType.Name} instance.{name}");
            #endif

            // __class__ is a getset on object whose setter rejects deletion with TypeError
            // (Objects/typeobject.c:5712-5719), so it is answered before the MRO search.
            if (name == "__class__")
                throw PyTypeError.Create("can't delete __class__ attribute");

            // CPython 3.12 descriptor protocol:
            // 1. 클래스 MRO에서 data descriptor 찾기
            // 2. data descriptor라면 descriptor.__delete__() 호출
            // 3. 아니라면 instance.__dict__에서 삭제

            // 1. 클래스 MRO에서 descriptor 찾기
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    // 2. data descriptor 확인 및 __delete__ 호출
                    if (IsDataDescriptor(classValue))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → found data descriptor in {mroType.Name}, calling __delete__()");
                        #endif
                        CallDescriptorDelete(classValue, this);
                        return;
                    }
                    break;
                }
            }

            // 3. 인스턴스 __dict__에서 삭제
            if (!InstanceDict.ContainsKey(name))
            {
                throw PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '{name}'");
            }
            InstanceDict.Remove(name);

            #if DEBUG_LOG
            Console.WriteLine($"   → removed from instance dict");
            #endif
        }

        /// <summary>
        /// CPython 3.12: Call descriptor's __delete__ method
        /// </summary>
        private static void CallDescriptorDelete(PyObject descriptor, PyObject instance)
        {
            // CPython 3.12: object.c calls tp_descr_set(self, obj, NULL). Built-in
            // descriptors (property, ...) implement deletion through the descriptor
            // interface itself and raise their own precise AttributeError.
            if (descriptor is IDescriptor nativeDescriptor)
            {
                nativeDescriptor.Delete(instance);
                return;
            }

            // Python-level descriptor with __delete__
            PyObject deleteMethod;
            try
            {
                deleteMethod = descriptor.GetAttribute("__delete__");
            }
            catch (PythonException) when (true)
            {
                throw PyAttributeError.Create("cannot delete attribute");
            }
            deleteMethod.Call(new PyObject[] { instance }, null);
        }

        // CPython Objects/abstract.c PyNumber_Long: the nb_int slot (a class __int__) is
        // consulted **before** nb_index, and its result must be an int. SharpPy implements
        // native nb_int/nb_float as CLR virtuals on the builtin types, so a user class's
        // numeric coercion belongs in this override.
        public override PyInt AsInt()
        {
            var asIntMethod = LookupSpecialMethodUncached("__int__");
            if (asIntMethod != null)
            {
                var result = BindSpecialMethod(asIntMethod).Call(Array.Empty<PyObject>(), null);
                // CPython accepts anything PyLong_Check covers, which includes bool and
                // int subclasses (it only warns for strict subclasses).
                if (result is PyInt or PyBool)
                    return result is PyInt exact ? exact : new PyInt(((PyBool)result).Value ? 1 : 0);
                throw PyTypeError.Create($"__int__ returned non-int (type {result.GetTypeName()})");
            }

            var viaIndex = TryGetIndex(this);
            if (viaIndex != null)
                return viaIndex;

            throw PyTypeError.Create(
                $"int() argument must be a string, a bytes-like object or a real number, not '{GetTypeName()}'");
        }

        // CPython Objects/abstract.c PyNumber_Float: nb_float, then nb_index. __int__ is
        // deliberately not consulted - measured float(Both()) == 30.0 while int(Both())
        // == 70 for the same object, and PyFloat.FromNumber keeps the arbitrary-precision
        // overflow rule (int too large to convert to float).
        public override PyFloat AsFloat()
        {
            var asFloatMethod = LookupSpecialMethodUncached("__float__");
            if (asFloatMethod != null)
            {
                var result = BindSpecialMethod(asFloatMethod).Call(Array.Empty<PyObject>(), null);
                if (result is PyFloat asFloat)
                    return asFloat;
                throw PyTypeError.Create(
                    $"{GetTypeName()}.__float__ returned non-float (type {result.GetTypeName()})");
            }

            var viaIndex = TryGetIndex(this);
            if (viaIndex != null)
                // Go through PyInt.AsFloat, which owns the arbitrary-precision overflow
                // rule; a plain (double) cast would return infinity instead of raising.
                return viaIndex.AsFloat();

            throw PyTypeError.Create(
                $"float() argument must be a string or a real number, not '{GetTypeName()}'");
        }

        public override PyString ToRepr()
        {
            // CPython PyObject_Repr: __repr__를 찾으면 호출하고 그 안에서 발생한 예외는
            // 전파된다. 반환이 str이 아니면 TypeError고, __repr__가 없을 때만 아래 기본
            // 표현(<module.Name object at 0x…>)을 쓴다.
            bool hasRepr = false;
            PyObject reprResult = null;

            if (InstanceDict.TryGetValue("__repr__", out var ownRepr))
            {
                hasRepr = true;
                reprResult = ownRepr.Call(new PyObject[0], null);
            }

            if (!hasRepr)
            {
                foreach (var mroType in InstanceType.MRO)
                {
                    if (mroType is PyClass customClass && customClass.ClassDict.ContainsKey("__repr__"))
                    {
                        if (customClass.ClassDict["__repr__"] is PyFunction func)
                        {
                            hasRepr = true;
                            reprResult = new PyMethod(this, func).Call(new PyObject[0], null);
                        }
                        break;
                    }
                    // Stop before reaching object type to avoid default __repr__
                    if (mroType.Name == "object")
                    {
                        break;
                    }
                }
            }

            if (hasRepr)
            {
                if (reprResult is PyString reprStr)
                    return reprStr;
                throw PyTypeError.Create($"__repr__ returned non-string (type {reprResult.GetTypeName()})");
            }

            // No Python-level __repr__ in the MRO does **not** mean object's repr. PyType_Ready
            // installs the builtin base's C slot on the subclass (update_one_slot: a list subclass
            // gets list_repr at Objects/listobject.c:379, a dict subclass dict_repr at
            // Objects/dictobject.c:2400, a set subclass set_repr at Objects/setobject.c:507), and
            // the storage object holds exactly the value those slots would have read.
            var inheritedRepr = NativeStorage();
            if (inheritedRepr != null)
            {
                // set_repr formats with Py_TYPE(so)->tp_name, so the subclass name has to travel
                // with it: C({1, 2}) and C(), never a bare {1, 2}.
                if (inheritedRepr is PySet mutableSet)
                    return mutableSet.Repr(GetTypeName(), exactType: false);
                if (inheritedRepr is PyFrozenSet frozenSet)
                    return frozenSet.Repr(GetTypeName());
                return inheritedRepr.ToRepr();
            }

            // Default representation: CPython prefixes the qualified name with the
            // class's module (except builtins): <__main__.O object at 0x...>.
            string modulePrefix = "";
            if (InstanceType is PyClass reprClass &&
                reprClass.ClassDict.TryGetValue("__module__", out var moduleObj) &&
                moduleObj is PyString moduleStr &&
                moduleStr.Value.Length > 0 && moduleStr.Value != "builtins" && moduleStr.Value != "None")
                modulePrefix = moduleStr.Value + ".";
            return new PyString($"<{modulePrefix}{GetTypeName()} object at 0x{GetHashCode():x}>");
        }

        // CPython PyObject_Str: __str__가 **없는 경우에만** __repr__로 폴백한다.
        // __str__ 호출 중 발생한 예외는 그대로 전파되고, 반환이 str이 아니면 TypeError다.
        public override PyString AsString()
        {
            bool hasStr = false;
            PyObject result = null;

            // Instance-level override wins in SharpPy's lookup order.
            if (InstanceDict.TryGetValue("__str__", out var ownStr))
            {
                hasStr = true;
                result = ownStr.Call(new PyObject[0], null);
            }

            if (!hasStr)
            {
                foreach (var mroType in InstanceType.MRO)
                {
                    // Stop before reaching object type
                    if (mroType.Name == "object")
                        break;

                    // Builtin types (BaseException, Exception, ...) expose __str__ in TypeDict
                    if (mroType is PyType pyType && pyType.TypeDict.ContainsKey("__str__"))
                    {
                        // CPython: __str__ = None is found and then *called* -> TypeError,
                        // it is not treated as "no __str__".
                        if (pyType.TypeDict["__str__"] is PyNone)
                        {
                            hasStr = true;
                            result = PyNone.Instance.Call(new PyObject[0], null);
                        }
                        else if (pyType.TypeDict["__str__"] is PyBuiltinFunction builtinFunc)
                        {
                            hasStr = true;
                            result = builtinFunc.Call(new PyObject[] { this }, null);
                        }
                        break;
                    }

                    // User-defined classes
                    if (mroType is PyClass customClass && customClass.ClassDict.ContainsKey("__str__"))
                    {
                        if (customClass.ClassDict["__str__"] is PyNone)
                        {
                            hasStr = true;
                            result = PyNone.Instance.Call(new PyObject[0], null);
                        }
                        else if (customClass.ClassDict["__str__"] is PyFunction func)
                        {
                            hasStr = true;
                            result = new PyMethod(this, func).Call(new PyObject[0], null);
                        }
                        break;
                    }
                }
            }

            if (!hasStr)
                return ToRepr();
            if (result is PyString pyStr)
                return pyStr;
            throw PyTypeError.Create($"__str__ returned non-string (type {result.GetTypeName()})");
        }

        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            var result = TryCompareSpecial(other, op);
            if (result != null) return result;

            // No Python-level comparison: the base's C slot is still there. unicode_richcompare,
            // list_richcompare, dict_equal and set_richcompare all compare contents and all accept
            // subclasses, so SS("x") == "x", C([1, 2]) == [1, 2] and D({'a': 1}) == {'a': 1} are
            // True for classes that never mention comparison. The storage is the value those slots
            // would have read, and the other side is seen through the same way.
            var mine = NativeStorage();
            if (mine != null)
            {
                var theirs = PyNativeStorage.Unwrap(other);
                if (theirs != null)
                    return mine.RichCompare(theirs, op);
            }
            return base.RichCompare(other, op);
        }

        // CPython 3.12: Objects/object.c:1675-1697 (PyObject_IsTrue) tries nb_bool, then
        // sq_length/mp_length. A container subclass inherits that length slot from its base, so an
        // empty C() is false without the class declaring __bool__ or __len__ - and a declared one
        // still wins, which is why the user lookup runs first.
        // CPython: a frozenset subclass inherits frozenset_hash (Objects/setobject.c), so
        // hash(C([1, 2])) equals hash(frozenset([1, 2])) and the two are the same dict key. SharpPy
        // found no __hash__ in the MRO and fell back to identity, which made an instance unequal-
        // by-hash to every equal value of its own base type. list/dict/set answer through the MRO as
        // __hash__ = None, so the unhashable refusal still comes from base.GetPythonHash().
        internal override long GetPythonHash()
        {
            if (LookupSpecialMethodUncached("__hash__") == null)
            {
                var container = NativeStorage();
                if (container is PyFrozenSet || container is PyString)
                    return container.GetPythonHash();
            }
            return base.GetPythonHash();
        }

        public override bool IsTrue()
        {
            if (LookupSpecialMethodUncached("__bool__") == null &&
                LookupSpecialMethodUncached("__len__") == null)
            {
                var container = NativeStorage();
                if (container != null)
                    return container.IsTrue();
            }
            return base.IsTrue();
        }

    }

    #endregion

        /// <summary>
    /// Tuple subclass instance for user-defined classes that inherit from tuple
    /// CPython 3.12: Used for namedtuple and other tuple subclasses
    /// </summary>
    public class PyTupleSubclass : PyTuple, IInstanceDictAccessor
    {
        public override PyObject RichCompare(PyObject other, CompareOp op) =>
            TryCompareSpecial(other, op, PyType.TupleType) ?? base.RichCompare(other, op);

        public PyClass InstanceType { get; }
        public Dictionary<string, PyObject> InstanceDict { get; }
        public PyObject[] ConstructorArgs { get; set; }

        public PyTupleSubclass(PyClass instanceType, PyObject[] items) : base(items)
        {
            InstanceType = instanceType;
            InstanceDict = new Dictionary<string, PyObject>();
        }

        public override PyType GetPyType() => InstanceType;

        public override string GetTypeName() => InstanceType.Name;

        // Override IsInstance to properly check MRO for tuple subclasses
        // CPython 3.12: PyObject_IsInstance checks tp_mro
        // NOTE: This override is no longer needed since isinstance() in BuiltinsModule.cs
        // now properly handles MRO checking for PyType (builtin types) at lines 286-293.
        // Keeping this as documentation of the fix.
        public override bool IsInstance(PyType type)
        {
            // Check if the type is in our MRO
            bool mroCheck = InstanceType.IsSubclassOf(type);
            if (mroCheck)
                return true;

            // Also check C# inheritance (PyTupleSubclass is a PyTuple in C#)
            return base.IsInstance(type);
        }

        // User-defined descriptors, methods and the instance __dict__ live in the Python class dict,
        // which CLR inheritance cannot see. Shared with PyBytesSubclass.
        public override PyObject GetAttribute(string name) =>
            SubclassInstanceSupport.GetAttribute(this, InstanceType, InstanceDict, name,
                n => base.GetAttribute(n));

        public override void SetAttribute(string name, PyObject value) =>
            SubclassInstanceSupport.SetAttribute(this, InstanceType, InstanceDict, name, value);
    }

#region Super Implementation

    /// <summary>
    /// Python의 super() 구현
    /// </summary>
    // PySuper moved to PyBuiltin.cs to follow CPython 3.12 structure

    #endregion
}
