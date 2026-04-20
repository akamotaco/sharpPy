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
        public List<PyObject>? TypeParams { get; set; } // PEP 695 __type_params__
        public PyClass? Metaclass { get; set; } // Metaclass information for type() calls

        // Cached tuple objects for __mro__ and __bases__ identity consistency
        // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
        internal PyTuple _cachedMroTuple;
        internal PyTuple _cachedBasesTuple;
        // Note: __dict__ does NOT cache in CPython 3.12 - each access returns new mappingproxy

        // Note: Method lookups use global TypeMethodCache (CPython-style array cache)
        // See PyType.GlobalMethodCache and LookupInMRO() below

        // Magic method cache: ClassDict-only MRO lookup cached per TypeVersionTag
        // CPython: Objects/typeobject.c — type_modified() invalidates caches
        // Lazy initialized, invalidated when TypeVersionTag changes
        private Dictionary<string, PyObject> _magicMethodCache;
        private ulong _magicMethodCacheVersion;

        // Cache for CheckAbstractMethods: avoid GetAttribute("__abstractmethods__") on every instantiation.
        // -1 = unchecked, 0 = not abstract, 1 = abstract (needs full check)
        private int _abstractCheckResult = -1;
        private ulong _abstractCheckVersion;

        // Cache for __new__: if class inherits object.__new__, skip lookup+call and create PyClassInstance directly.
        // CPython 3.12: Objects/typeobject.c:1627 (type_call) — most classes use object.__new__
        // -1 = unchecked, 0 = uses custom __new__, 1 = uses default object.__new__
        private int _usesDefaultNew = -1;
        private ulong _usesDefaultNewVersion;

        // Cache: true if no data descriptors exist in MRO ClassDicts
        // Eliminates MRO walk in STORE_ATTR for common classes (property-less)
        // CPython 3.12: Objects/object.c:1563 _PyObject_GenericSetAttrWithDict
        private bool _mroHasNoDataDescriptors;
        private ulong _mroNoDataDescVersion;

        /// <summary>
        /// True if no class in this type's MRO defines a data descriptor in its ClassDict.
        /// Cached per TypeVersionTag. Eliminates MRO walk in STORE_ATTR fast path.
        /// </summary>
        internal bool MroHasNoDataDescriptors
        {
            get
            {
                if (_mroNoDataDescVersion == TypeVersionTag)
                    return _mroHasNoDataDescriptors;
                _mroHasNoDataDescriptors = ComputeNoDataDescriptors();
                _mroNoDataDescVersion = TypeVersionTag;
                return _mroHasNoDataDescriptors;
            }
        }

        private bool ComputeNoDataDescriptors()
        {
            foreach (var mroType in MRO)
            {
                if (mroType is PyClass cls)
                {
                    foreach (var val in cls.ClassDict.Values)
                    {
                        if (val is IDescriptor desc && desc.IsDataDescriptor())
                            return false;
                    }
                }
            }
            return true;
        }

        // Cached subclass flags — avoid MRO.Any() LINQ per instance creation
        // CPython 3.12: Objects/typeobject.c — tp_flags (Py_TPFLAGS_DICT_SUBCLASS, etc.)
        private int _isDictSubclass = -1; // -1=unchecked, 0=no, 1=yes
        private int _isListSubclass = -1;
        private int _hasGetattr = -1;     // -1=unchecked, 0=no, 1=yes

        // ThreadStatic buffers for __init__ args (self + args) to avoid per-call allocation
        // CPython 3.12: Objects/typeobject.c:1677 (slot_tp_init) — init args include self
        [ThreadStatic] private static PyObject[] _initBuf1; // [self]
        [ThreadStatic] private static PyObject[] _initBuf2; // [self, arg1]
        [ThreadStatic] private static PyObject[] _initBuf3; // [self, arg1, arg2]
        [ThreadStatic] private static PyObject[] _initBuf4; // [self, arg1, arg2, arg3]

        // FastInit: bypass frame creation for simple __init__ methods
        // Pattern: __init__(self, arg1, ...) that only does self.attr = arg assignments + return None
        // -1 = unchecked, 0 = not fast-initable, 1 = fast-initable
        private int _fastInitChecked = -1;
        private string[] _fastInitAttrNames;   // attribute names in assignment order
        private int[] _fastInitArgIndices;     // LocalsPlus index of each arg (1-based, 0=self)

        // Slot-based attribute storage for FastInit classes: eliminates Dictionary<> allocation
        // Slot names/indices are shared across all instances of this class.
        // CPython 3.12: tp_dictoffset + cached key version for LOAD_ATTR_INSTANCE_VALUE
        internal string[] SlotNames;                          // attribute names in slot order (null if not slotted)
        internal int SlotCount;                               // number of slots (0 if not slotted)

        // Fast constructor eligibility: SlotCount > 0 && !DictSubclass && !ListSubclass && !HasGetAttr
        // Checked once on first CreateInstance, cached for all subsequent calls.
        private int _fastConstructor = -1; // -1=unchecked, 0=no, 1=yes

        /// <summary>
        /// Cached magic method lookup: ClassDict-only MRO search with TypeVersionTag invalidation.
        /// O(1) on cache hit, O(MRO depth) on cache miss.
        /// </summary>
        internal PyObject GetCachedMagicMethod(string name)
        {
            // Version check: invalidate entire cache on type change
            if (_magicMethodCache != null && _magicMethodCacheVersion == TypeVersionTag)
            {
                // Cache hit (including cached null = "method not found")
                if (_magicMethodCache.TryGetValue(name, out var cached))
                    return cached;
            }
            else
            {
                // Version mismatch: rebuild cache
                _magicMethodCache = new Dictionary<string, PyObject>();
                _magicMethodCacheVersion = TypeVersionTag;
            }

            // Cache miss: MRO search — ClassDict(사용자 정의) + TypeDict(built-in descriptor) 둘 다 확인.
            // CPython 은 단일 __dict__ 를 갖지만 SharpPy 는 분리 — built-in base class
            // (BaseException/Exception 등) 의 __init__/__str__/... 는 TypeDict 에 저장되므로
            // ClassDict 만 탐색하면 user-defined exception subclass 가 base __init__ 을 못 찾음.
            PyObject found = null;
            foreach (var mroType in MRO)
            {
                // PyClass (사용자 정의) 우선 — user override
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out var clsMethod))
                {
                    found = clsMethod;
                    break;
                }
                // 그 다음 TypeDict — built-in PyType 의 descriptor
                if (mroType != null && mroType.TypeDict != null
                    && mroType.TypeDict.TryGetValue(name, out var typeMethod))
                {
                    found = typeMethod;
                    break;
                }
            }

            // Cache the result (null means "not found", also cached)
            _magicMethodCache[name] = found;
            return found;
        }

        /// <summary>
        /// Cached check: is this class a dict subclass?
        /// CPython 3.12: tp_flags & Py_TPFLAGS_DICT_SUBCLASS
        /// </summary>
        internal bool IsDictSubclassType()
        {
            if (_isDictSubclass == -1)
                _isDictSubclass = MRO.Any(bt => bt == PyType.DictType) ? 1 : 0;
            return _isDictSubclass == 1;
        }

        /// <summary>
        /// Cached check: is this class a list subclass?
        /// CPython 3.12: tp_flags & Py_TPFLAGS_LIST_SUBCLASS
        /// </summary>
        internal bool IsListSubclassType()
        {
            if (_isListSubclass == -1)
                _isListSubclass = MRO.Any(bt => bt == PyType.ListType) ? 1 : 0;
            return _isListSubclass == 1;
        }

        /// <summary>
        /// Cached check: does this class have __getattr__?
        /// CPython 3.12: Objects/typeobject.c:8855
        /// </summary>
        internal bool HasGetAttrMethod()
        {
            if (_hasGetattr == -1)
            {
                var m = GetCachedMagicMethod("__getattr__");
                _hasGetattr = (m is PyFunction) ? 1 : 0;
            }
            return _hasGetattr == 1;
        }

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
            // CPython 3.12: Objects/typeobject.c:1627-1689 (type_call)
            // The correct pattern is:
            // 1. Call type->tp_new(type, args, kwds) to create the object
            // 2. If returned object is not instance of type, return it (no __init__)
            // 3. Otherwise, call type->tp_init(obj, args, kwds)

            // Step 1: Call __new__ to create the object
            // CPython 3.12: Objects/typeobject.c:1667
            PyObject instance;

            // Fast path: if class inherits object.__new__ (no custom __new__),
            // skip lookup + args allocation + descriptor call — just create PyClassInstance directly.
            // CPython 3.12: Objects/typeobject.c:1642-1665 — tp_new == object_new fast path
            if (_usesDefaultNew == -1 || _usesDefaultNewVersion != TypeVersionTag)
            {
                var newMethod = LookupInMRO("__new__");
                // Check if __new__ is object.__new__ (PyMethodDescriptor on ObjectType)
                if (newMethod is PyMethodDescriptor md && md.OwnerType == PyType.ObjectType)
                    _usesDefaultNew = 1;
                else
                    _usesDefaultNew = 0;
                _usesDefaultNewVersion = TypeVersionTag;
            }

            if (_usesDefaultNew == 1 && (kwargs == null || kwargs.InternalDict.Count == 0))
            {
                // Default object.__new__: directly create instance
                // Fast constructor: skip Dict/List subclass + __getattr__ checks for simple classes
                if (_fastConstructor == -1)
                    _fastConstructor = (SlotCount > 0 && !IsDictSubclassType() && !IsListSubclassType() && !HasGetAttrMethod()) ? 1 : 0;
                instance = _fastConstructor == 1
                    ? new PyClassInstance(this, SlotCount)
                    : new PyClassInstance(this);
            }
            else
            {
                // Custom __new__: full path
                var newMethod = LookupInMRO("__new__");
                if (newMethod != null)
                {
                    // Call __new__ with (cls, *args, **kwargs)
                    var newArgs = new PyObject[args.Length + 1];
                    newArgs[0] = this;  // cls parameter
                    Array.Copy(args, 0, newArgs, 1, args.Length);

                    // CPython 3.12: __new__ can be a static method, class method, or builtin
                    if (newMethod is PyFunction func)
                        instance = func.Call(newArgs, kwargs);
                    else if (newMethod is PyStaticBuiltinMethod staticBuiltin)
                        instance = staticBuiltin.Call(newArgs, kwargs);
                    else if (newMethod is PyBuiltinMethod builtinMethod)
                        instance = builtinMethod.Call(newArgs, kwargs);
                    else if (newMethod is PyMethodDescriptor descriptor)
                        instance = descriptor.Call(newArgs, kwargs);
                    else if (newMethod.IsCallable())
                        instance = newMethod.Call(newArgs, kwargs);
                    else
                        throw PyTypeError.Create($"__new__ is not callable");
                }
                else
                {
                    throw PyTypeError.Create($"cannot create '{Name}' instances: no __new__ method");
                }

                // Step 2: Check if returned object is an instance of this type
                // CPython 3.12: Objects/typeobject.c:1672-1675
                if (instance.GetPyType() != this)
                    return instance;
            }

            // Step 3: Call __init__ on the instance
            // CPython 3.12: Objects/typeobject.c:1677-1687
            var init = GetCachedMagicMethod("__init__");
            if (init != null)
            {
                // FastInit: bypass frame creation for simple __init__ (self.x = arg patterns)
                // Saves ~2 frame allocations + ~8 instruction dispatches per instance creation
                if (init is PyFunction function)
                {
                    if (_fastInitChecked == -1)
                        AnalyzeFastInit(function);

                    if (_fastInitChecked == 1 && (kwargs == null || kwargs.InternalDict.Count == 0)
                        && instance is PyClassInstance fastInst && args.Length == function.CodeObject.ArgCount - 1)
                    {
                        // Direct attribute assignment without frame creation
                        // Slot path: write directly to slot array (avoids Dictionary allocation)
                        if (fastInst._slotValues != null)
                        {
                            for (int i = 0; i < _fastInitAttrNames.Length; i++)
                                fastInst._slotValues[i] = args[_fastInitArgIndices[i] - 1];
                        }
                        else
                        {
                            for (int i = 0; i < _fastInitAttrNames.Length; i++)
                                fastInst.InstanceDict[_fastInitAttrNames[i]] = args[_fastInitArgIndices[i] - 1];
                        }
                    }
                    else
                    {
                        // Standard __init__ call path
                        int totalArgs = args.Length + 1;
                        PyObject[] initArgs;
                        if (totalArgs == 1) { initArgs = _initBuf1 ??= new PyObject[1]; }
                        else if (totalArgs == 2) { initArgs = _initBuf2 ??= new PyObject[2]; }
                        else if (totalArgs == 3) { initArgs = _initBuf3 ??= new PyObject[3]; }
                        else if (totalArgs == 4) { initArgs = _initBuf4 ??= new PyObject[4]; }
                        else { initArgs = new PyObject[totalArgs]; }
                        initArgs[0] = instance;
                        for (int i = 0; i < args.Length; i++) initArgs[i + 1] = args[i];

                        if ((kwargs == null || kwargs.InternalDict.Count == 0) && function.CodeObject != null)
                            function.CallSimple(initArgs);
                        else
                            function.Call(initArgs, kwargs);
                    }
                }
                else if (init is IDescriptor desc)
                {
                    var boundInit = desc.Get(instance, this);
                    boundInit.Call(args, kwargs);
                }
                else if (init is PyMethod method)
                {
                    method.Call(args, kwargs);
                }
                else if (init is PyBuiltinMethod builtinMethod)
                {
                    builtinMethod.Call(args, kwargs);
                }
                else
                {
                    // PyBuiltinFunction (e.g. BaseException.__init__ at TypeDict) 등 unbound callable —
                    // CPython type.__call__ 처럼 instance 를 self 로 앞에 prepend 해서 호출해야 함.
                    // (prepend 누락 시 첫 사용자 arg 가 self 로 잘못 해석됨 — args 저장 실패 등 증상)
                    var wrappedArgs = new PyObject[args.Length + 1];
                    wrappedArgs[0] = instance;
                    for (int i = 0; i < args.Length; i++) wrappedArgs[i + 1] = args[i];
                    init.Call(wrappedArgs, kwargs);
                }
            }

            return instance;
        }

        /// <summary>
        /// Analyze __init__ bytecode to detect simple self.attr = arg patterns.
        /// If the __init__ only does LOAD_FAST + STORE_ATTR pairs (no other logic),
        /// we can skip frame creation and do direct dict assignment.
        /// </summary>
        private void AnalyzeFastInit(PyFunction initFunc)
        {
            _fastInitChecked = 0; // Default: not fast-initable

            var code = initFunc.CodeObject;
            if (code == null) return;

            // Must have no closures, no generators, be CO_OPTIMIZED
            if ((code.CellVars?.Count ?? 0) != 0 || (code.FreeVars?.Count ?? 0) != 0) return;
            if (code.IsGenerator() || code.IsCoroutine()) return;

            var instrs = code.InstructionsArray;
            if (instrs == null || instrs.Length < 2) return;

            // Pattern: RESUME, (LOAD_FAST argN, LOAD_FAST 0 (self), STORE_ATTR name, CACHE*)*, RETURN_CONST None
            // CPython 3.12 bytecode for `self.x = val`:
            //   LOAD_FAST 1 (val)   -- push value
            //   LOAD_FAST 0 (self)  -- push self
            //   STORE_ATTR 0 (x)    -- pop self, pop value, self.x = value
            //   CACHE * 4           -- inline cache entries
            int ip = 0;

            // Skip RESUME
            if (instrs[ip].OpCode == ByteCodeOp.RESUME) ip++;
            if (ip >= instrs.Length) return;

            var attrNames = new System.Collections.Generic.List<string>();
            var argIndices = new System.Collections.Generic.List<int>();

            while (ip + 2 < instrs.Length)
            {
                var loadVal = instrs[ip];
                var loadSelf = instrs[ip + 1];
                var storeAttr = instrs[ip + 2];

                if (loadVal.OpCode != ByteCodeOp.LOAD_FAST) break;
                if (loadSelf.OpCode != ByteCodeOp.LOAD_FAST || loadSelf.Argument != 0) break;
                if (storeAttr.OpCode != ByteCodeOp.STORE_ATTR) break;

                int valIdx = loadVal.Argument;
                if (valIdx == 0) break; // Can't assign self to self

                string attrName = code.Names[storeAttr.Argument];
                attrNames.Add(attrName);
                argIndices.Add(valIdx);
                ip += 3;
                // Skip CACHE entries after STORE_ATTR (4 inline cache slots in CPython 3.12)
                while (ip < instrs.Length && instrs[ip].OpCode == ByteCodeOp.CACHE)
                    ip++;
            }

            // Must end with RETURN_CONST (None)
            if (ip < instrs.Length && instrs[ip].OpCode == ByteCodeOp.RETURN_CONST
                && attrNames.Count > 0)
            {
                _fastInitChecked = 1;
                _fastInitAttrNames = attrNames.ToArray();
                _fastInitArgIndices = argIndices.ToArray();

                // Build slot infrastructure for inline attribute storage
                // Use SlotNames array for linear scan (SlotCount is typically 2-5,
                // linear scan is faster than Dictionary hash for small N)
                SlotNames = _fastInitAttrNames;
                SlotCount = SlotNames.Length;
            }
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
        public PyObject LookupInMRO(string name)
        {
            // Phase 2: Use global TypeMethodCache (CPython-style array cache)
            // This is faster than Dictionary (15-25 cycles vs 30-100+ cycles)
            return PyType.GlobalMethodCache.Lookup(this, name, out _);
        }

        // 클래스 호출 시 인스턴스 생성
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
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

        // CPython 3.12: Override GetIterator to check metaclass __iter__
        public override PyObject GetIterator()
        {
            // Check if metaclass has __iter__ method
            if (Metaclass != null)
            {
                // Check metaclass's ClassDict directly
                if (Metaclass is PyClass metaClass && metaClass.ClassDict.TryGetValue("__iter__", out PyObject iterMethod))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ found __iter__ in metaclass ClassDict");
                    #endif

                    if (iterMethod.IsCallable())
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   → calling __iter__ with this class as argument");
                        #endif
                        // Call metaclass's __iter__ with this class as argument
                        var iterResult = iterMethod.Call(new PyObject[] { this }, null);
                        #if DEBUG_LOG
                        Console.WriteLine($"   → __iter__ returned: {iterResult?.GetTypeName()}");
                        #endif
                        return iterResult;
                    }
                }

                // Also check metaclass MRO (e.g., if EnumType inherits from type)
                for (int i = 1; i < Metaclass.MRO.Count; i++)
                {
                    var metaBase = Metaclass.MRO[i];
                    if (metaBase is PyClass metaPyClass && metaPyClass.ClassDict.TryGetValue("__iter__", out PyObject metaIterMethod))
                    {
                        #if DEBUG_LOG
                        Console.WriteLine($"   ✅ found __iter__ in metaclass MRO ({metaBase.Name})");
                        #endif

                        if (metaIterMethod.IsCallable())
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"   → calling metaclass MRO __iter__ with this class as argument");
                            #endif
                            var iterResult = metaIterMethod.Call(new PyObject[] { this }, null);
                            #if DEBUG_LOG
                            Console.WriteLine($"   → __iter__ returned: {iterResult?.GetTypeName()}");
                            #endif
                            return iterResult;
                        }
                    }
                }

                #if DEBUG_LOG
                Console.WriteLine($"   ❌ No __iter__ found in metaclass or its MRO");
                #endif
            }

            // Fall back to base behavior (which throws "not iterable" error)
            return base.GetIterator();
        }

        // 클래스 attribute 접근
        public override PyObject GetAttribute(string name)
        {
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
            if (ClassDict.ContainsKey(name))
            {
                ClassDict.Remove(name);

                // CPython 3.12: Invalidate method cache when class dict changes
                InvalidateTypeCache();
                return;
            }
            throw PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '{name}'");
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
                            return self.RichCompare(args[0], PyObject.CompareOp.EQ);
                        }, 2),
                        "__ne__" => new PyBuiltinMethod("__ne__", (self, args) => {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"__ne__() takes exactly 1 argument ({args.Length} given)");
                            return self.RichCompare(args[0], PyObject.CompareOp.NE);
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
                            return dict.Keys();
                        }),

                        "values" => new PyBuiltinFunction("values", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"values() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return dict.Values();
                        }),

                        "items" => new PyBuiltinFunction("items", (args, kwargs) =>
                        {
                            if (args.Length != 1)
                                throw PyTypeError.Create($"items() takes exactly 1 argument ({args.Length} given)");

                            var dict = GetDictStorage(args[0]);
                            return dict.Items();
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

                // list 타입의 메서드들 - CPython 3.12 compatible
                if (pyType == PyType.ListType)
                {
                    // CPython 3.12: Objects/listobject.c
                    // Helper to get PyList from self (works for both PyList and list subclasses)
                    static PyList GetListStorage(PyObject self)
                    {
                        if (self is PyList list)
                            return list;

                        if (self is PyClassInstance instance && instance.IsListSubclass())
                        {
                            // List subclass: return internal list storage
                            return instance.GetListStorage();
                        }

                        throw PyTypeError.Create($"descriptor requires a 'list' object but received a '{self.GetTypeName()}'");
                    }

                    return name switch
                    {
                        // CPython 3.12: __getitem__ wrapper descriptor (sq_item slot)
                        // CPython 3.12: Objects/listobject.c:420-449 (list_subscript)
                        "__getitem__" => new PyWrapperDescriptor(
                            "__getitem__",
                            PyType.ListType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 1)
                                    throw PyTypeError.Create($"__getitem__() takes exactly 1 argument ({args.Length} given)");

                                var key = args[0];
                                // CPython 3.12: Direct storage access to avoid infinite loop
                                // Use GetListStorage() to support both PyList and list subclasses
                                var list = GetListStorage(self);
                                return list.GetItem(key);
                            }),

                        // CPython 3.12: __setitem__ wrapper descriptor (sq_ass_item slot)
                        "__setitem__" => new PyWrapperDescriptor(
                            "__setitem__",
                            PyType.ListType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 2)
                                    throw PyTypeError.Create($"__setitem__() takes exactly 2 arguments ({args.Length} given)");

                                var key = args[0];
                                var value = args[1];

                                // CPython 3.12: Direct storage access (list_ass_item in C)
                                // Use GetListStorage() to support both PyList and list subclasses
                                var list = GetListStorage(self);
                                list.SetItem(key, value);
                                return PyNone.Instance;
                            }),

                        // CPython 3.12: __delitem__ wrapper descriptor (sq_ass_item slot with NULL value)
                        "__delitem__" => new PyWrapperDescriptor(
                            "__delitem__",
                            PyType.ListType,
                            (self, args, kwargs) =>
                            {
                                if (args.Length != 1)
                                    throw PyTypeError.Create($"__delitem__() takes exactly 1 argument ({args.Length} given)");

                                var key = args[0];

                                // CPython 3.12: Direct storage access
                                PyList list;
                                if (self is PyList l)
                                {
                                    list = l;
                                }
                                else
                                {
                                    throw PyTypeError.Create($"descriptor '__delitem__' for 'list' objects doesn't apply to a '{self.GetTypeName()}' object");
                                }

                                // Delete item at index
                                if (key is PyInt index)
                                {
                                    int idx = (int)index.Value;
                                    int count = list.Length();
                                    if (idx < 0) idx += count;
                                    if (idx < 0 || idx >= count)
                                        throw PyIndexError.Create("list index out of range");
                                    // Use list.Pop() which handles deletion correctly
                                    list.Pop(idx);
                                }
                                else if (key is PySlice slice)
                                {
                                    // CPython 3.12: Objects/listobject.c:623-737 (list_ass_slice)
                                    // Handle slice deletion: del list[start:stop:step]
                                    var length = list.Length();
                                    var (start, stop, step) = slice.Indices(length);
                                    var sliceLength = slice.GetLength(length);

                                    if (step == 1)
                                    {
                                        // Simple case: contiguous deletion
                                        // del list[start:stop] removes items from start to stop-1
                                        // CPython: list_ass_slice(a, ilow, ihigh, NULL) with v=NULL means delete
                                        for (int i = 0; i < sliceLength; i++)
                                        {
                                            list.Pop(start);
                                        }
                                    }
                                    else if (step > 0)
                                    {
                                        // Extended slice deletion with positive step
                                        // Delete from end to beginning to maintain indices
                                        var indicesToDelete = new List<int>();
                                        for (int i = start; i < stop; i += step)
                                        {
                                            if (i >= 0 && i < length)
                                                indicesToDelete.Add(i);
                                        }
                                        // Remove in reverse order
                                        for (int i = indicesToDelete.Count - 1; i >= 0; i--)
                                        {
                                            list.Pop(indicesToDelete[i]);
                                        }
                                    }
                                    else // step < 0
                                    {
                                        // Extended slice deletion with negative step
                                        var indicesToDelete = new List<int>();
                                        for (int i = start; i > stop; i += step)
                                        {
                                            if (i >= 0 && i < length)
                                                indicesToDelete.Add(i);
                                        }
                                        // Remove in reverse order
                                        indicesToDelete.Sort();
                                        indicesToDelete.Reverse();
                                        foreach (var idx in indicesToDelete)
                                        {
                                            list.Pop(idx);
                                        }
                                    }
                                }
                                else
                                {
                                    throw PyTypeError.Create("list indices must be integers or slices, not " + key.GetTypeName());
                                }

                                return PyNone.Instance;
                            }),

                        _ => null
                    };
                }

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
            // Fast path: if we already checked and class is not abstract, skip entirely.
            // Invalidated when TypeVersionTag changes (class modified).
            if (_abstractCheckResult == 0 && _abstractCheckVersion == TypeVersionTag)
                return;

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
                _abstractCheckResult = 0;
                _abstractCheckVersion = TypeVersionTag;
                return;
            }

            // Check if __abstractmethods__ is None or empty
            if (abstractMethodsAttr == null || abstractMethodsAttr == PyNone.Instance)
            {
                _abstractCheckResult = 0;
                _abstractCheckVersion = TypeVersionTag;
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
                    .Select(item => ((PyStr)item).Value)
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
                    .Select(item => ((PyStr)item).Value)
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
        private Dictionary<string, PyObject> _instanceDict;
        // ConstructorArgs removed — was write-only, never read
        private PyFunction _customGetAttr;

        // Slot-based inline attribute storage: eliminates Dictionary allocation for FastInit classes.
        // Slot names/indices are shared on PyClass; per-instance only stores the values array.
        // CPython 3.12: tp_dictoffset + LOAD_ATTR_INSTANCE_VALUE inline cache.
        internal PyObject[] _slotValues;

        /// <summary>
        /// InstanceDict property: lazy creation for slotted instances.
        /// On first access, copies slot values to dict and switches to dict mode.
        /// Cold paths use this; hot paths use TryGetInstanceAttr/SetInstanceAttr.
        /// </summary>
        public Dictionary<string, PyObject> InstanceDict
        {
            get
            {
                if (_instanceDict == null)
                {
                    _instanceDict = new Dictionary<string, PyObject>();
                    // Copy slot values to dict, then switch to dict mode
                    if (_slotValues != null)
                    {
                        var names = InstanceType.SlotNames;
                        if (names != null)
                        {
                            for (int i = 0; i < names.Length; i++)
                            {
                                if (_slotValues[i] != null)
                                    _instanceDict[names[i]] = _slotValues[i];
                            }
                        }
                        _slotValues = null; // switch to dict mode permanently
                    }
                }
                return _instanceDict;
            }
        }

        /// <summary>
        /// Fast attribute read: check slot storage first, then overflow dict.
        /// Linear scan on SlotNames (typically 2-5 entries, faster than Dictionary hash).
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal bool TryGetInstanceAttr(string name, out PyObject value)
        {
            if (_slotValues != null)
            {
                var names = InstanceType.SlotNames;
                if (names != null)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (ReferenceEquals(names[i], name) || names[i] == name)
                        {
                            value = _slotValues[i];
                            return value != null;
                        }
                    }
                }
            }
            if (_instanceDict != null) return _instanceDict.TryGetValue(name, out value);
            value = null;
            return false;
        }

        /// <summary>
        /// Fast attribute write: check slot storage first, then overflow dict.
        /// Linear scan on SlotNames (typically 2-5 entries, faster than Dictionary hash).
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal void SetInstanceAttr(string name, PyObject value)
        {
            if (_slotValues != null)
            {
                var names = InstanceType.SlotNames;
                if (names != null)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (ReferenceEquals(names[i], name) || names[i] == name)
                        {
                            _slotValues[i] = value;
                            return;
                        }
                    }
                }
            }
            // Non-slot attribute or no slots: fall to dict (triggers lazy creation)
            InstanceDict[name] = value;
        }

        // ThreadStatic buffers for magic method dispatch — avoids per-call array allocation.
        // Safe because args are consumed by BindArgumentsToParametersCPython312 (copied to LocalsPlus)
        // before any user code (which could re-enter) executes.
        [ThreadStatic] private static PyObject[] _unaryBuf;
        [ThreadStatic] private static PyObject[] _binaryBuf;

        // CPython 3.12: Dict subclasses have internal dict storage
        private PyDict _dictStorage;

        // CPython 3.12: List subclasses have internal list storage
        private PyList _listStorage;

        // CPython 3.12: Cache the __dict__ wrapper for identity consistency
        private PyDict _dictCache;

        public PyClassInstance(PyClass instanceType)
        {
            InstanceType = instanceType;
            // Slot-based storage for FastInit classes: PyObject[] instead of Dictionary
            if (instanceType.SlotCount > 0)
            {
                _slotValues = new PyObject[instanceType.SlotCount];
                _instanceDict = null; // lazy, created on first InstanceDict access
            }
            else
            {
                _slotValues = null;
                _instanceDict = new Dictionary<string, PyObject>();
            }
            // CPython 3.12: _PyType_Lookup(tp, &_Py_ID(__getattr__))
            // Use cached flag on PyClass (O(1)) instead of per-instance MRO search
            if (instanceType.HasGetAttrMethod())
            {
                _customGetAttr = (PyFunction)instanceType.GetCachedMagicMethod("__getattr__");
            }

            // CPython 3.12: Subclass storage — use cached flags on PyClass (O(1))
            if (instanceType.IsDictSubclassType())
            {
                _dictStorage = new PyDict();
            }

            if (instanceType.IsListSubclassType())
            {
                _listStorage = new PyList();
            }
        }

        /// <summary>
        /// Fast constructor for FastInit classes: skip Dict/List subclass checks, skip __getattr__ lookup.
        /// Caller guarantees: slotCount > 0, not dict/list subclass, _customGetAttr cached on PyClass.
        /// CPython 3.12: tp_new fast path for simple user classes
        /// </summary>
        internal PyClassInstance(PyClass instanceType, int slotCount)
        {
            InstanceType = instanceType;
            _slotValues = new PyObject[slotCount];
            // _instanceDict = null (default), _customGetAttr = null (default)
            // Dict/List subclass checks skipped — caller guarantees not applicable
        }

        public bool IsDictSubclass()
        {
            return InstanceType.IsDictSubclassType();
        }

        public bool IsListSubclass()
        {
            return InstanceType.IsListSubclassType();
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
            var keyStr = key is PyStr ps ? ps.Value : key?.ToString() ?? "null";
            if (InstanceType.Name == "_EnumDict" && (keyStr == "STRICT" || keyStr == "CONFORM" || keyStr == "EJECT" || keyStr == "KEEP"))
            {
                Console.WriteLine($"[DEBUG-GETITEM] _EnumDict.GetItem('{keyStr}') called, _dictStorage != null: {_dictStorage != null}");
                if (_dictStorage != null && _dictStorage.InternalDict.TryGetValue(key, out var stored))
                {
                    Console.WriteLine($"[DEBUG-GETITEM]   _dictStorage contains '{keyStr}': {stored}, type={stored?.GetType().Name}");
                }
            }
            #endif

            // CPython 3.12: Check for user-defined __getitem__ in MRO (cached)
            // Only check PyClass.ClassDict, NOT PyType.TypeDict
            // CPython: Objects/abstract.c PyObject_GetItem → slot_mp_subscript
            var getItemMethod = InstanceType.GetCachedMagicMethod("__getitem__");
            if (getItemMethod != null)
            {
                var result = InvokeMagicMethod(getItemMethod, new PyObject[] { key });
                if (result != null) return result;
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

            // Check for user-defined __setitem__ in MRO (cached)
            // Only check PyClass.ClassDict, NOT PyType.TypeDict
            var setitemMethod = InstanceType.GetCachedMagicMethod("__setitem__");
            if (setitemMethod != null)
            {
                // Found user-defined __setitem__ - call it and return
                // Storage will be updated only if user code calls super().__setitem__()
                InvokeMagicMethod(setitemMethod, new PyObject[] { key, value });
                return;  // User __setitem__ found and called, we're done
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
            // Check for user-defined __delitem__ in MRO (cached)
            var delitemMethod = InstanceType.GetCachedMagicMethod("__delitem__");
            if (delitemMethod != null)
            {
                // Found user-defined __delitem__ - call it and return
                InvokeMagicMethod(delitemMethod, new PyObject[] { key });
                return;
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
            if (_dictStorage != null)
            {
                return _dictStorage.Contains(item);
            }
            return base.Contains(item);
        }

        // CPython 3.12: Objects/abstract.c:2859-2864 (PyObject_GetIter)
        // Support iteration via __iter__ method or dict storage
        public override PyObject GetIterator()
        {
            // Try to call __iter__ method if it exists (takes precedence)
            try
            {
                // Instance dict first (monkey-patching)
                if (TryGetInstanceAttr("__iter__", out var instIter))
                    return instIter.Call(new PyObject[0], null);

                // ClassDict + TypeDict MRO (기존 동작 보존: GetIterator는 TypeDict도 검색)
                // CPython: Objects/abstract.c PyObject_GetIter → slot_tp_iter
                foreach (var mroType in InstanceType.MRO)
                {
                    // PyClass.ClassDict (user-defined classes)
                    if (mroType is PyClass customClass && customClass.ClassDict.TryGetValue("__iter__", out var classMethod))
                    {
                        var result = InvokeMagicMethod(classMethod, new PyObject[0]);
                        if (result != null) return result;
                        break;
                    }
                    // PyType.TypeDict (builtin types like list, dict)
                    // Critical for list subclasses that inherit __iter__ from list
                    else if (mroType.TypeDict.TryGetValue("__iter__", out var typeMethod))
                    {
                        if (typeMethod is PyMethodDescriptor descriptor)
                            return descriptor.Call(new PyObject[] { this }, null);
                        var result = InvokeMagicMethod(typeMethod, new PyObject[0]);
                        if (result != null) return result;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                throw PyTypeError.Create($"iter() returned non-iterator of type '{GetTypeName()}': {ex.Message}");
            }

            // Fall back to dict storage if this is a dict subclass
            if (_dictStorage != null)
            {
                return _dictStorage.GetIterator();
            }

            // CPython 3.12: Objects/abstract.c - PyObject_GetIter fallback to PySeqIter_New
            // If no __iter__ method, check for __getitem__ (sequence protocol)
            // Try to check if __getitem__ exists
            try
            {
                var getitemAttr = GetAttribute("__getitem__");
                if (getitemAttr != null && getitemAttr.IsCallable())
                {
                    // Object has __getitem__, create a generic iterator
                    return new PyGenericIterator(this);
                }
            }
            catch (PythonException)
            {
                // __getitem__ doesn't exist, fall through to error
            }

            // No __iter__ or __getitem__ method found
            return base.GetIterator();
        }

        /// <summary>
        /// CPython 3.12: Get next item by calling __next__ method
        /// </summary>
        public override PyObject Next()
        {
            // Try to call __next__ method if it exists
            // Check instance dict first
            if (TryGetInstanceAttr("__next__", out var nextMethod))
            {
                return nextMethod.Call(new PyObject[0], null);
            }

            // Then check class hierarchy
            // CPython 3.12: slot_tp_iternext calls __next__(self) directly, no PyMethod
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass customClass && customClass.ClassDict.TryGetValue("__next__", out var method))
                {
                    if (method is PyFunction func)
                    {
                        // Direct call: func(self) — skip PyMethod allocation
                        return func.Call(new PyObject[] { this }, null);
                    }
                    break;
                }
            }

            // No __next__ method found
            return base.Next();
        }

        /// <summary>
        /// CPython 3.12: Find a magic method in ClassDict-only MRO (not TypeDict).
        /// Returns the raw unbound method (PyFunction, callable, etc.) or null.
        /// Searches instance dict first, then class hierarchy (ClassDict only).
        /// CPython: Objects/typeobject.c — _PyType_Lookup for special methods
        /// </summary>
        private PyObject FindMagicMethod(string methodName)
        {
            // 1. Instance dict first (monkey-patching support: obj.__add__ = ...)
            if (TryGetInstanceAttr(methodName, out var instMethod))
                return instMethod;

            // 2. ClassDict-only MRO search (기존 동작 보존: TypeDict 미검색)
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(methodName, out var method))
                    return method;
            }

            return null;
        }

        /// <summary>
        /// CPython 3.12: Call a magic method bound to this instance.
        /// Avoids PyMethod allocation by calling func directly with self prepended.
        /// CPython: Objects/abstract.c — call_unbound_noarg, call_method
        /// </summary>
        private PyObject InvokeMagicMethod(PyObject method, PyObject[] args)
        {
            if (method is PyFunction func)
            {
                // PyMethod 할당 제거: func(self, *args) 직접 호출
                if (args.Length == 0)
                    return func.Call(new PyObject[] { this }, null);
                if (args.Length == 1)
                    return func.Call(new PyObject[] { this, args[0] }, null);
                var fullArgs = new PyObject[args.Length + 1];
                fullArgs[0] = this;
                System.Array.Copy(args, 0, fullArgs, 1, args.Length);
                return func.Call(fullArgs, null);
            }
            else if (method is IDescriptor desc)
            {
                var bound = desc.Get(this, InstanceType);
                return bound.Call(args, null);
            }
            else if (method.IsCallable())
            {
                var fullArgs = new PyObject[args.Length + 1];
                fullArgs[0] = this;
                System.Array.Copy(args, 0, fullArgs, 1, args.Length);
                return method.Call(fullArgs, null);
            }
            return null;
        }

        /// <summary>
        /// CPython 3.12: Helper to find and call a magic method.
        /// Combines FindMagicMethod + InvokeMagicMethod.
        /// Instance dict methods are called directly (no self prepend).
        /// </summary>
        private PyObject CallMagicMethod(string methodName, params PyObject[] args)
        {
            // 1. Instance dict (monkey-patching: already bound, no self prepend)
            if (TryGetInstanceAttr(methodName, out var instMethod))
                return instMethod.Call(args, null);

            // 2. Cached ClassDict MRO search (O(1) on hit)
            var method = InstanceType.GetCachedMagicMethod(methodName);
            if (method != null)
                return InvokeMagicMethod(method, args);

            return null;
        }

        /// <summary>
        /// Zero-alloc unary magic method call (no args, e.g., __repr__, __str__, __iter__)
        /// Avoids params array + inner array allocation.
        /// </summary>
        private PyObject CallMagicMethodUnary(string methodName)
        {
            // CPython 3.12: special/dunder methods are looked up on the TYPE, not instance dict.
            var method = InstanceType.GetCachedMagicMethod(methodName);
            if (method == null) return null;

            if (method is PyFunction func)
            {
                var buf = _unaryBuf ??= new PyObject[1];
                buf[0] = this;
                // Fast path: skip generator/coroutine/kwargs checks for dunder methods
                if (func.CodeObject != null)
                    return func.CallSimple(buf);
                return func.Call(buf, null);
            }
            if (method is IDescriptor desc)
                return desc.Get(this, InstanceType).Call(System.Array.Empty<PyObject>(), null);
            if (method.IsCallable())
            {
                var buf = _unaryBuf ??= new PyObject[1];
                buf[0] = this;
                return method.Call(buf, null);
            }
            return null;
        }

        /// <summary>
        /// Zero-alloc binary magic method call (1 arg, e.g., __add__, __eq__, __getitem__)
        /// Avoids params array + inner array allocation.
        /// </summary>
        internal PyObject CallMagicMethodBinary(string methodName, PyObject arg)
        {
            // CPython 3.12: special/dunder methods are looked up on the TYPE, not instance dict.
            // Skip TryGetInstanceAttr — matches CPython's slot_nb_add / lookup_in_type() behavior.
            var method = InstanceType.GetCachedMagicMethod(methodName);
            if (method == null) return null;

            if (method is PyFunction func)
            {
                var buf = _binaryBuf ??= new PyObject[2];
                buf[0] = this;
                buf[1] = arg;
                // Fast path: skip generator/coroutine/kwargs checks for dunder methods
                if (func.CodeObject != null)
                    return func.CallSimple(buf);
                return func.Call(buf, null);
            }
            if (method is IDescriptor desc)
            {
                var buf1 = _unaryBuf ??= new PyObject[1];
                buf1[0] = arg;
                return desc.Get(this, InstanceType).Call(buf1, null);
            }
            if (method.IsCallable())
            {
                var buf = _binaryBuf ??= new PyObject[2];
                buf[0] = this;
                buf[1] = arg;
                return method.Call(buf, null);
            }
            return null;
        }

        /// <summary>
        /// CPython 3.12: Binary operation with magic method support
        /// Implements the protocol: try __add__, then __radd__ on other
        /// </summary>
        private PyObject BinaryOpWithMagicMethod(PyObject other, string methodName, string reflectedMethodName)
        {
            // Try left operand's method first (e.g., self.__add__(other))
            var result = CallMagicMethodBinary(methodName, other);
            if (result != null)
            {
                return result;
            }

            // Try right operand's reflected method (e.g., other.__radd__(self))
            if (other is PyClassInstance otherInstance)
            {
                result = otherInstance.CallMagicMethodBinary(reflectedMethodName, this);
                if (result != null)
                {
                    return result;
                }
            }

            // No magic method found - fall back to base implementation (will throw TypeError)
            return null;
        }

        // CPython 3.12: Magic method overrides for binary operations
        public override PyObject Add(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__add__", "__radd__");
            return result ?? base.Add(other);
        }

        public override PyObject Subtract(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__sub__", "__rsub__");
            return result ?? base.Subtract(other);
        }

        public override PyObject Multiply(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__mul__", "__rmul__");
            return result ?? base.Multiply(other);
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
            return result ?? base.BitwiseAnd(other);
        }

        public override PyObject BitwiseOr(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__or__", "__ror__");
            return result ?? base.BitwiseOr(other);
        }

        public override PyObject BitwiseXor(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__xor__", "__rxor__");
            return result ?? base.BitwiseXor(other);
        }

        protected override bool HasCustomGetAttr() => _customGetAttr != null;

        protected override PyObject CallGetAttr(string name)
        {
            if (_customGetAttr != null)
            {
                return _customGetAttr.Call(new PyObject[] { this, new PyStr(name) }, null);
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
                        // CPython 3.12: Direct call — func(descriptor, instance, owner)
                        // Skip PyMethod allocation
                        return func.Call(new PyObject[] { descriptor, instanceArg, owner }, null);
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
                        // CPython 3.12: Direct call — func(descriptor, instance, value)
                        // Skip PyMethod allocation
                        func.Call(new PyObject[] { descriptor, instance, value }, null);
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
                if (_dictCache == null)
                {
                    _dictCache = new PyInstanceAttrDict(InstanceDict);
                }
                return _dictCache;
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

            // 2. 인스턴스 __dict__ 검색 (slot-aware)
            if (TryGetInstanceAttr(name, out PyObject instanceValue))
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

            // CPython 3.12: Check for custom __getattribute__ FIRST (cached)
            // (typeobject.c:8867 - _Py_slot_tp_getattr_hook)
            var customGetAttr = InstanceType.GetCachedMagicMethod("__getattribute__");
            if (customGetAttr != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   🔍 calling custom __getattribute__");
                #endif
                return InvokeMagicMethod(customGetAttr, new PyObject[] { new PyStr(name) });
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
            // Check for user-defined __setattr__ method (cached, no PyMethod allocation)
            var setattr = InstanceType.GetCachedMagicMethod("__setattr__");
            if (setattr != null)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → found user __setattr__, calling it");
                #endif
                InvokeMagicMethod(setattr, new PyObject[] { new PyStr(name), value });
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
                // CPython 3.12: slot_tp_setattro calls __delattr__(self, name) directly
                if (delattr is PyFunction delattrFunc)
                    delattrFunc.Call(new PyObject[] { this, new PyStr(name) }, null);
                else
                    delattr.Call(new PyObject[] { new PyStr(name) }, null);
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
            // Try to call __delete__
            try
            {
                var deleteMethod = descriptor.GetAttribute("__delete__");
                deleteMethod.Call(new PyObject[] { instance }, null);
            }
            catch
            {
                throw PyAttributeError.Create($"cannot delete attribute");
            }
        }

        public override PyStr ToRepr()
        {
            // CPython 3.12: Try to call __repr__ method if user defined it
            // Check instance dict and class hierarchy (not object's default)
            // CPython: Objects/typeobject.c slot_tp_repr
            try
            {
                // Zero-alloc unary call: InstanceDict → cached ClassDict MRO
                var result = CallMagicMethodUnary("__repr__");
                if (result is PyStr pyStr) return pyStr;
            }
            catch
            {
                // If __repr__ fails, fall back to default
            }

            return new PyStr($"<{GetTypeName()} object at 0x{GetHashCode():x}>");
        }

        public override PyStr ToStr()
        {
            // CPython 3.12: Try to call __str__ method if user defined it
            // CPython: Objects/typeobject.c slot_tp_str
            try
            {
                // Instance dict first (monkey-patching)
                if (TryGetInstanceAttr("__str__", out var instStr))
                {
                    var result = instStr.Call(new PyObject[0], null);
                    if (result is PyStr pyStr) return pyStr;
                }

                // ClassDict + TypeDict MRO (기존 동작 보존: ToStr은 TypeDict도 검색)
                foreach (var mroType in InstanceType.MRO)
                {
                    if (mroType.Name == "object") break;

                    // Check PyType.TypeDict (builtin types like BaseException, Exception)
                    if (mroType is PyType pyType && pyType.TypeDict.TryGetValue("__str__", out var typeMethod))
                    {
                        if (typeMethod is PyBuiltinFunction builtinFunc)
                        {
                            var result = builtinFunc.Call(new PyObject[] { this }, null);
                            if (result is PyStr pyStr) return pyStr;
                        }
                        break;
                    }
                    // Check PyClass.ClassDict (user-defined classes)
                    else if (mroType is PyClass customClass && customClass.ClassDict.TryGetValue("__str__", out var classMethod))
                    {
                        var result = InvokeMagicMethod(classMethod, new PyObject[0]);
                        if (result is PyStr pyStr) return pyStr;
                        break;
                    }
                }
            }
            catch
            {
                // If __str__ fails, fall back to __repr__
            }

            return ToRepr();
        }

        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            // CPython 3.12: Try to call __eq__, __ne__, __lt__, __le__, __gt__, __ge__ methods
            string methodName = op switch
            {
                CompareOp.EQ => "__eq__",
                CompareOp.NE => "__ne__",
                CompareOp.LT => "__lt__",
                CompareOp.LE => "__le__",
                CompareOp.GT => "__gt__",
                CompareOp.GE => "__ge__",
                _ => null
            };

            if (methodName != null)
            {
                try
                {
                    // Zero-alloc binary call: InstanceDict → cached ClassDict MRO
                    // CPython: Objects/typeobject.c slot_tp_richcompare
                    var result = CallMagicMethodBinary(methodName, other);
                    if (result != null && result != PyNotImplemented.Instance)
                        return result;
                }
                catch (PythonException)
                {
                    throw; // Re-throw Python exceptions
                }
            }

            // Fall back to base comparison (identity-based for EQ/NE)
            return base.RichCompare(other, op);
        }
    }

    #endregion

        /// <summary>
    /// Tuple subclass instance for user-defined classes that inherit from tuple
    /// CPython 3.12: Used for namedtuple and other tuple subclasses
    /// </summary>
    public class PyTupleSubclass : PyTuple, IInstanceDictAccessor
    {
        public PyClass InstanceType { get; }
        public Dictionary<string, PyObject> InstanceDict { get; }
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

        // Override GetAttribute to support user-defined descriptors and __dict__
        public override PyObject GetAttribute(string name)
        {
            // First check if there's a descriptor in the class
            PyObject classAttribute = null;
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    classAttribute = classValue;
                    break;
                }
            }

            // If it's a descriptor, use it
            if (classAttribute is IDescriptor desc)
            {
                return desc.Get(this, InstanceType);
            }
            else if (classAttribute is PyFunction function)
            {
                return new PyMethod(this, function);
            }

            // Check instance __dict__
            if (InstanceDict.TryGetValue(name, out PyObject instanceValue))
            {
                return instanceValue;
            }

            // Return class attribute if found
            if (classAttribute != null)
            {
                return classAttribute;
            }

            // Fall back to base PyTuple behavior
            return base.GetAttribute(name);
        }

        // Override SetAttribute to support instance __dict__
        public override void SetAttribute(string name, PyObject value)
        {
            // Check if there's a data descriptor in the class
            foreach (var mroType in InstanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject classValue))
                {
                    if (classValue is IDescriptor desc && desc.IsDataDescriptor())
                    {
                        desc.Set(this, value);
                        return;
                    }
                    break;
                }
            }

            // Set in instance __dict__
            InstanceDict[name] = value;
        }
    }

#region Super Implementation

    /// <summary>
    /// Python의 super() 구현
    /// </summary>
    // PySuper moved to PyBuiltin.cs to follow CPython 3.12 structure

    #endregion
}