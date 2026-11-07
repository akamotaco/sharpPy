using System;

namespace SharpPy
{
    #region User-Defined Classes

    /// <summary>
    /// 사용자 정의 클래스
    /// </summary>
    public class PyClass : PyType
    {
        public Dictionary<string, PyObject> ClassDict { get; }

        /// <summary>
        /// One slot of the type as update_one_slot (Objects/typeobject.c) leaves it, for the version
        /// of the type it was decided at: update_slot reruns it when the name changes here or in a
        /// base, and type_modified moves the version tag of this type and its subclasses then.
        /// Specific is a base's slot wrapper whose d_wrapped is the slot; Generic is any other
        /// object the MRO names, for which the slot is the slot_* function; neither is NULL.
        /// </summary>
        private struct Slot
        {
            internal ulong Version;
            internal PyWrapperDescriptor Specific;
            internal PyObject Generic;
        }

        private ref Slot UpdateOneSlot(ref Slot slot, string name)
        {
            if (slot.Version != TypeVersionTag)
            {
                var descr = FindNameInMro(name);
                slot.Specific = descr is PyWrapperDescriptor wrapper && wrapper.Name == name &&
                    wrapper.OwnerType != null && IsSubclassOf(wrapper.OwnerType) ? wrapper : null;
                slot.Generic = slot.Specific == null ? descr : null;
                slot.Version = TypeVersionTag;
            }
            return ref slot;
        }

        // tp_getattro: what the class writes as __getattribute__, and __getattr__ wherever it is.
        private ulong _getattroVersion;
        private PyObject _getattribute, _getattr;

        /// <summary>
        /// tp_getattro of a class written in Python. update_one_slot leaves the base's tp_getattro
        /// when the class has neither name of its own, else slot_tp_getattr_hook: __getattribute__
        /// (PyObject_GenericGetAttr when it is object's), and __getattr__ when that raises
        /// AttributeError (Objects/typeobject.c).
        /// </summary>
        internal PyObject GetattroSlot(PyObject self, string name)
        {
            if (_getattroVersion != TypeVersionTag)
            {
                var getattribute = FindNameInMro("__getattribute__", out bool written);
                _getattribute = written ? getattribute : null;
                _getattr = FindNameInMro("__getattr__");
                _getattroVersion = TypeVersionTag;
            }
            var hook = _getattr;
            var method = _getattribute;
            if (hook == null)
                return method == null ? self.InheritedGetattro(name) : CallGetattrMethod(self, method, name);
            try
            {
                return method == null ? self.InheritedGetattro(name) : CallGetattrMethod(self, method, name);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return CallGetattrMethod(self, hook, name);
            }
        }

        private static PyObject CallGetattrMethod(PyObject self, PyObject method, string name) =>
            method.GetType() == typeof(PyFunction)
                ? method.Call(new PyObject[] { self, new PyString(name) }, null)
                : self.BindSpecialMethod(method).Call(new PyObject[] { new PyString(name) }, null);

        // Version 0 is never a tag, so every slot starts undecided.
        private Slot _tpIter, _tpIterNext, _tpRepr, _tpStr, _sqLength, _nbBool, _sqContains, _tpHash, _mpSubscript,
            _mpSetItem, _mpDelItem, _nbInt, _nbFloat, _nbPositive, _nbNegative, _nbInvert, _nbAbsolute;
        private readonly Slot[] _tpRichCompare = new Slot[6];
        private static readonly string[] RichCompareNames = { "__lt__", "__le__", "__eq__", "__ne__", "__gt__", "__ge__" };

        /// <summary>
        /// tp_richcompare, one slot for the six names: a base's function through the wrapper of the
        /// operation, or slot_tp_richcompare, lookup_maybe_method of the operation's name, whose
        /// failure to bind is NotImplemented (Objects/typeobject.c).
        /// </summary>
        internal PyObject RichCompareSlot(PyObject self, PyObject other, CompareOp op)
        {
            ref var slot = ref UpdateOneSlot(ref _tpRichCompare[(int)op], RichCompareNames[(int)op]);
            if (slot.Specific != null)
                return slot.Specific._implementation(self, new[] { other }, null);
            if (slot.Generic == null)
                return PyNotImplemented.Instance;
            if (slot.Generic.GetType() == typeof(PyFunction))
                return slot.Generic.Call(new[] { self, other }, null);
            PyObject bound;
            try { bound = self.BindSpecialMethod(slot.Generic); }
            catch (PythonException) { return PyNotImplemented.Instance; }
            return bound.Call(new[] { other }, null);
        }

        // slot_nb_positive and the like (SLOT0); without the method, the type's CLR function refuses.
        internal PyObject NbPositiveSlot(PyObject self) => UnarySlot(ref _nbPositive, "__pos__", self) ?? self.NbPositive();
        internal PyObject NbNegativeSlot(PyObject self) => UnarySlot(ref _nbNegative, "__neg__", self) ?? self.NbNegative();
        internal PyObject NbInvertSlot(PyObject self) => UnarySlot(ref _nbInvert, "__invert__", self) ?? self.NbInvert();
        internal PyObject NbAbsoluteSlot(PyObject self) => UnarySlot(ref _nbAbsolute, "__abs__", self) ?? self.NbAbsolute();

        // nb_int and nb_float: slot_nb_int and slot_nb_float call the method; null is NULL.
        internal PyObject NbIntSlot(PyObject self) => UnarySlot(ref _nbInt, "__int__", self);
        internal PyObject NbFloatSlot(PyObject self) => UnarySlot(ref _nbFloat, "__float__", self);

        private PyObject UnarySlot(ref Slot slot, string name, PyObject self)
        {
            ref var decided = ref UpdateOneSlot(ref slot, name);
            if (decided.Specific != null)
                return decided.Specific._implementation(self, Array.Empty<PyObject>(), null);
            return decided.Generic == null ? null : self.BindSpecialMethod(decided.Generic).Call(Array.Empty<PyObject>(), null);
        }

        internal PyObject SubscriptSlot(PyObject self, PyObject key)
        {
            ref var slot = ref UpdateOneSlot(ref _mpSubscript, "__getitem__");
            if (slot.Specific != null)
                return slot.Specific._implementation(self, new[] { key }, null);
            // slot_mp_subscript: vectorcall_method(&_Py_ID(__getitem__), stack, 2)
            if (slot.Generic != null)
                return self.BindSpecialMethod(slot.Generic).Call(new[] { key }, null);
            return self.MpSubscript(key);
        }

        /// <summary>
        /// mp_ass_subscript, one slot for __setitem__ and __delitem__: update_one_slot makes it
        /// slot_mp_ass_subscript when either is a Python-level function, which then calls the one
        /// the operation names, and an AttributeError of that name when the class has not it.
        /// </summary>
        internal void AssSubscriptSlot(PyObject self, PyObject key, PyObject value)
        {
            ref var set = ref UpdateOneSlot(ref _mpSetItem, "__setitem__");
            ref var del = ref UpdateOneSlot(ref _mpDelItem, "__delitem__");
            bool generic = set.Generic != null || del.Generic != null;
            ref var slot = ref value == null ? ref del : ref set;
            string name = value == null ? "__delitem__" : "__setitem__";
            var args = value == null ? new[] { key } : new[] { key, value };
            if (generic)
            {
                var method = slot.Generic ?? (PyObject)slot.Specific ?? throw PyAttributeError.Create(name);
                self.BindSpecialMethod(method).Call(args, null);
                return;
            }
            if (slot.Specific != null)
                slot.Specific._implementation(self, args, null);
            else if (value == null)
                self.MpDelSubscript(key);
            else
                self.MpAssSubscript(key, value);
        }

        internal long HashSlot(PyObject self)
        {
            ref var slot = ref UpdateOneSlot(ref _tpHash, "__hash__");
            if (slot.Specific != null)
                return ((PyInt)slot.Specific._implementation(self, Array.Empty<PyObject>(), null)).ToLong();
            // update_one_slot: __hash__ = None is PyObject_HashNotImplemented.
            if (slot.Generic == null || slot.Generic is PyNone)
                throw PyTypeError.Create($"unhashable type: '{Name}'");
            // slot_tp_hash: a lookup that fails is unhashable, the answer must be an int, a value
            // outside Py_hash_t is long_hash of it, and -1 is reserved.
            // lookup_maybe_method binds, and what it answers is tested for None.
            PyObject bound;
            try { bound = self.BindSpecialMethod(slot.Generic); }
            catch (PythonException) { bound = null; }
            if (bound == null || bound is PyNone)
                throw PyTypeError.Create($"unhashable type: '{Name}'");
            var result = bound.Call(Array.Empty<PyObject>(), null);
            System.Numerics.BigInteger value = (PyInt.LongValue(result)
                ?? throw PyTypeError.Create("__hash__ method should return an integer")).Value;
            long hash = value < nint.MinValue || value > nint.MaxValue ? PyInt.PythonHash(value) : (long)value;
            return hash == -1 ? -2 : hash;
        }

        internal PyBool ContainsSlot(PyObject self, PyObject item)
        {
            ref var slot = ref UpdateOneSlot(ref _sqContains, "__contains__");
            if (slot.Specific != null)
                return PyBool.FromBool(slot.Specific._implementation(self, new[] { item }, null).IsTrue());
            if (slot.Generic == null)
                return IterSearchContains(self, item);
            // slot_sq_contains: __contains__ = None refuses; the answer is PyObject_IsTrue.
            if (slot.Generic is PyNone)
                throw PyTypeError.Create($"'{Name}' object is not a container");
            return PyBool.FromBool(self.BindSpecialMethod(slot.Generic).Call(new[] { item }, null).IsTrue());
        }

        internal long LenSlot(PyObject self)
        {
            ref var slot = ref UpdateOneSlot(ref _sqLength, "__len__");
            if (slot.Specific != null)
                return ToLengthResult(slot.Specific._implementation(self, Array.Empty<PyObject>(), null));
            if (slot.Generic == null)
                throw PyTypeError.Create($"object of type '{Name}' has no len()");
            // slot_sq_length: _PyNumber_Index of what __len__ answers, never negative, and
            // PyNumber_AsSsize_t(res, PyExc_OverflowError).
            var result = self.BindSpecialMethod(slot.Generic).Call(Array.Empty<PyObject>(), null);
            var length = PyObject.IndexOrThrow(result);
            if (length.Value.Sign < 0)
                throw PyValueError.Create("__len__() should return >= 0");
            if (length.Value > long.MaxValue)
                throw PyOverflowError.Create($"cannot fit '{length.GetPyType().FullName}' into an index-sized integer");
            return (long)length.Value;
        }

        internal bool BoolSlot(PyObject self)
        {
            ref var slot = ref UpdateOneSlot(ref _nbBool, "__bool__");
            if (slot.Specific != null)
                return slot.Specific._implementation(self, Array.Empty<PyObject>(), null).IsTrue();
            if (slot.Generic != null)
            {
                // slot_nb_bool: what __bool__ answers must be a bool.
                var value = self.BindSpecialMethod(slot.Generic).Call(Array.Empty<PyObject>(), null);
                if (value is PyBool truth)
                    return truth.Value;
                throw PyTypeError.Create($"__bool__ should return bool, returned {value.GetPyType().FullName}");
            }
            // PyObject_IsTrue without nb_bool: mp_length or sq_length, else true.
            ref var length = ref UpdateOneSlot(ref _sqLength, "__len__");
            return length.Specific == null && length.Generic == null || LenSlot(self) != 0;
        }

        internal PyObject StrSlot(PyObject self)
        {
            ref var slot = ref UpdateOneSlot(ref _tpStr, "__str__");
            if (slot.Specific != null)
                return slot.Specific._implementation(self, Array.Empty<PyObject>(), null);
            // slot_tp_str: vectorcall_method(&_Py_ID(__str__), stack, 1)
            if (slot.Generic != null)
                return self.BindSpecialMethod(slot.Generic).Call(Array.Empty<PyObject>(), null);
            return self.TpRepr();
        }

        internal PyObject ReprSlot(PyObject self)
        {
            ref var slot = ref UpdateOneSlot(ref _tpRepr, "__repr__");
            if (slot.Specific != null)
                return slot.Specific._implementation(self, Array.Empty<PyObject>(), null);
            // slot_tp_repr: lookup_maybe_method, and a lookup that fails is cleared for the
            // "<%s object at %p>" of tp_name.
            if (slot.Generic != null)
            {
                PyObject bound;
                try { bound = self.BindSpecialMethod(slot.Generic); }
                catch (PythonException) { bound = null; }
                if (bound != null)
                    return bound.Call(Array.Empty<PyObject>(), null);
            }
            return new PyString($"<{Name} object at {PyObjectId.Pointer(self)}>");
        }

        internal PyObject IterNextSlot(PyObject self)
        {
            ref var slot = ref UpdateOneSlot(ref _tpIterNext, "__next__");
            if (slot.Specific != null)
                return slot.Specific._implementation(self, Array.Empty<PyObject>(), null);
            // slot_tp_iternext: vectorcall_method(&_Py_ID(__next__), stack, 1)
            if (slot.Generic != null)
                return self.InvokeNextMethod();
            // _PyObject_NextNotImplemented (Objects/object.c)
            throw PyTypeError.Create($"'{Name}' object is not iterable");
        }

        /// <summary>tp_iter is the function of owner's slot wrapper, as inherited from that base.</summary>
        internal bool IterSlotIs(PyType owner)
        {
            ref var slot = ref UpdateOneSlot(ref _tpIter, "__iter__");
            return slot.Specific != null && ReferenceEquals(slot.Specific.OwnerType, owner);
        }

        internal PyObject IterSlot(PyObject self)
        {
            ref var slot = ref UpdateOneSlot(ref _tpIter, "__iter__");
            if (slot.Specific != null)
                return slot.Specific._implementation(self, Array.Empty<PyObject>(), null);
            // slot_tp_iter: __iter__ = None refuses; a lookup that fails falls back to __getitem__.
            if (slot.Generic != null)
                return self.InvokeIteratorMethod(slot.Generic);
            // PyObject_GetIter with tp_iter NULL: PySequence_Check, which a dict subclass fails.
            if (FindNameInMro("__getitem__") != null && !IsSubclassOf(DictType))
                return new PyGenericIterator(self);
            throw PyTypeError.Create($"'{Name}' object is not iterable");
        }

        public List<PyObject>? TypeParams { get; set; } // PEP 695 __type_params__
        public PyClass? Metaclass { get; set; } // Metaclass information for type() calls

        // Cached tuple objects for __mro__ and __bases__ identity consistency
        // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
        internal PyTuple _cachedMroTuple;

        // Whether this class' MRO declares __del__, remembered so that creating an instance of an
        // ordinary class costs one field read instead of an MRO walk. CPython answers the same
        // question once, at type creation: Objects/typeobject.c:9499 binds __del__ to tp_finalize
        // through the slotdef table, and subtype_dealloc (Objects/typeobject.c:1901) then tests
        // tp_finalize per object without searching. This field is the SharpPy equivalent of that
        // slot. See docs/lifetime_and_gc_policy.md section 3 for what does and does not follow.
        // A global epoch rather than a bool. InvalidateTypeCache (core/PyType.cs) does not spread to
        // subclasses, so a per-class version tag could not be trusted: assigning
        // LateParent.__del__ after a subclass already asked the question has to invalidate that
        // subclass' cached answer too (probes G). The cost is one increment in the two paths that
        // write or delete a class attribute.
        internal static ulong FinalizerEpoch = 1;
        private ulong _finalizerCheckedAt;
        private bool _declaresFinalizer;
        private PyObject _finalizerHook;

        internal bool DeclaresFinalizer() => LookupFinalizerHook() != null;

        /// <summary>
        /// The declared __del__ for this type, or null. Answered once per epoch, then cached, so an
        /// ordinary instance costs one field comparison (this is SharpPy's stand-in for the
        /// tp_finalize slot C fills at type creation, Objects/typeobject.c:9499).
        ///
        /// The re-check walks the class dictionaries instead of LookupInMRO: the global method cache
        /// still holds the negative answer recorded before a late `P.__del__ = ...`, and
        /// InvalidateTypeCache does not spread to subclasses, so a subclass would keep answering
        /// "no finalizer" from a stale cache entry (probe G, parent assigned after subclass used).
        /// </summary>
        internal PyObject LookupFinalizerHook()
        {
            if (_finalizerCheckedAt != FinalizerEpoch) return FindFinalizerHook();
            return _declaresFinalizer ? _finalizerHook : null;
        }

        private PyObject FindFinalizerHook()
        {
            PyObject hook = null;
            foreach (var t in MRO)
            {
                // A native base's tp_finalize, exposed as its __del__ (iobase_finalize), is inherited.
                if (t is not PyClass owner)
                {
                    if (t.TypeDict.TryGetValue("__del__", out var native))
                    {
                        hook = native;
                        break;
                    }
                    continue;
                }
                // The first class in the MRO that has the name wins, whatever the value is. Skipping a
                // None went on to find the parent and call it, where CPython stops at the None and
                // reports TypeError: 'NoneType' object is not callable on stderr
                // (Objects/typeobject.c:9071 slot_tp_finalize looks the name up once, on the type).
                // Measured: docs/plans/lifetime_foundation/probes/l1_del_hook.py - the subclass with
                // __del__ = None leaves the log empty.
                if (!owner.ClassDict.TryGetValue("__del__", out var own)) continue;
                hook = own;
                break;
            }
            _finalizerHook = hook;
            _declaresFinalizer = hook != null;
            _finalizerCheckedAt = FinalizerEpoch;
            return hook;
        }

        protected override List<PyType> ComputeMro() => PyTypeMetaclass.MroInvoke(this) ?? base.ComputeMro();

        private TypeLayout _layout;

        internal override TypeLayout Layout =>
            this is PyTypeMetaclass ? base.Layout : _layout ??= PyTypeMetaclass.NewLayout(this);

        public bool InstancesHaveDict() => Layout.DictOffset != 0;

        internal override bool SupportsWeakReference() => Layout.WeakOffset != 0;

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
            // type_new_set_ht_name (Objects/typeobject.c:3416): the qualname the class body wrote is
            // taken out of the dict and kept on the type, so '__qualname__' in C.__dict__ is False and
            // an instance never sees the name. The metaclass installs the getset descriptor of the
            // same name into type's own dict, and that one is not a body entry, so it stays.
            if (ClassDict.TryGetValue("__qualname__", out var qualEntry))
            {
                if (qualEntry is PyString qualText)
                {
                    SetQualName(qualText.Value);
                    ClassDict.Remove("__qualname__");
                }
                else
                {
                    throw PyTypeError.Create(
                        $"type __qualname__ must be a str, not {qualEntry.GetTypeName()}");
                }
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
            
            // PEP 698: @override 데코레이터 - CPython compatible (no runtime validation at class creation)
            // ValidateOverrideDecorators(); // Disabled for CPython compatibility

        }

        // _PyType_Lookup
        public PyObject LookupInMRO(string name) => FindNameInMro(name);

        // 클래스 호출 시 인스턴스 생성
        // Py_TYPE(cls)->tp_call: a metaclass __call__ comes before type_call.
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            if (Metaclass != null && !ReferenceEquals(Metaclass, PyTypeMetaclass.Instance))
            {
                var call = Metaclass.LookupInMRO("__call__");
                if (call != null && !ReferenceEquals(call, PyTypeMetaclass.Instance.LookupInMRO("__call__")))
                    return BindSpecialMethod(call).Call(args, kwargs);
            }
            return TypeCall(args, kwargs);
        }

        internal virtual PyObject TypeCall(PyObject[] args, PyDict kwargs) => base.Call(args, kwargs);

        // 클래스는 항상 호출 가능 (인스턴스 생성)
        public override bool IsCallable() => true;

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
        protected internal override PyObject Iter()
        {
            var method = LookupSpecialMethodUncached("__iter__");
            if (method != null) return InvokeIteratorMethod(method);
            if (LookupSpecialMethodUncached("__getitem__") != null)
                return new PyGenericIterator(this);
            throw PyTypeError.Create($"'{GetPyType().Name}' object is not iterable");
        }

        protected internal override PyObject IterNext() => InvokeNextMethod();


        internal PyObject TypeGetAttribute(string name) => TypeGetattro(name);

        // type_setattro; a metaclass's __setattr__/__delattr__ is its slot in PyObject_SetAttr.
        protected internal override void TpSetattro(string name, PyObject value) => TypeSetAttribute(name, value);

        protected internal override void TpDelattro(string name) => PyDelAttribute(name);

        internal void TypeDeleteAttribute(string name) => PyDelAttribute(name);

        /// <summary>type_setattro</summary>
        internal void TypeSetAttribute(string name, PyObject value)
        {
            if (this is PyTypeMetaclass)
                throw PyTypeError.Create($"cannot set '{name}' attribute of immutable type 'type'");
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
            // CPython type_setattro -> _PyObject_GenericSetAttrWithDict: a data descriptor on the
            // metatype wins over the class dict (Objects/typeobject.c type_setattro).
            var metaDesc = MetatypeAttribute(name);
            if (metaDesc != null && PyClassInstance.IsDataDescriptor(metaDesc))
            {
                PyClassInstance.CallDescriptorSet(metaDesc, this, value);
                return;
            }

            ClassDict[name] = value;
            // __del__ may be assigned to a class after instances of it already exist. C decides
            // tp_finalize at type creation (Objects/typeobject.c:9499); SharpPy's cached answer is
            // versioned by the global epoch so subclasses pick it up as well.
            if (name == "__del__") FinalizerEpoch++;

            // CPython 3.12: Invalidate method cache when class dict changes
            // Reference: Objects/typeobject.c:420-522 (type_modified)
            InvalidateTypeCache();
        }

        // _PyType_Lookup on the metatype: a user metaclass has an MRO, a builtin one only its table.
        private PyObject MetatypeAttribute(string name)
        {
            var metatype = GetPyType();
            return (metatype as PyClass)?.LookupInMRO(name) ?? GetTypeAttribute(metatype, name);
        }

        // CPython 3.12: Override DelAttribute for type objects
        protected override void PyDelAttribute(string name)
        {
            // A heap class is writable, so type_setattro reaches object_set_class, which refuses
            // deletion of __class__ with TypeError (Objects/typeobject.c:5712-5719). Without this the
            // lookup below answered "type object 'C' has no attribute '__class__'".
            if (name == "__class__")
                throw PyTypeError.Create("can't delete __class__ attribute");

            // CPython type_setattro delete: metatype data descriptor first (Objects/typeobject.c).
            var metaDesc = MetatypeAttribute(name);
            if (metaDesc != null && PyClassInstance.IsDataDescriptor(metaDesc))
            {
                PyClassInstance.CallDescriptorDelete(metaDesc, this);
                return;
            }

            if (ClassDict.ContainsKey(name))
            {
                ClassDict.Remove(name);
                if (name == "__del__") FinalizerEpoch++;

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
                if (function.ObjectDict(false) is { } attributes && attributes.TryGetStoredValue(new PyString("__override__"), out PyObject overrideAttr))
                {
                    return overrideAttr.IsTrue();
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
        private static readonly Dictionary<string, PyObject> ObjectSlots = new Dictionary<string, PyObject>();

        /// <summary>
        /// A slot of object is one object in CPython, so getattr(cls, "__lt__") can be compared with
        /// getattr(object, "__lt__"), as functools.total_ordering does. The lookup below builds them.
        /// </summary>
        public static PyObject? GetTypeAttribute(PyType pyType, string name)
        {
            if (!ReferenceEquals(pyType, PyType.ObjectType)) return BuildTypeAttribute(pyType, name);
            lock (ObjectSlots)
            {
                if (ObjectSlots.TryGetValue(name, out var slot)) return slot;
                slot = BuildTypeAttribute(pyType, name);
                if (slot != null) ObjectSlots[name] = slot;
                return slot;
            }
        }

        // object_repr: the default, whatever the object's own type declares.
        internal static PyString ObjectRepr(PyObject self)
        {
            var type = self.GetPyType();
            string modulePrefix = type is PyClass cls && cls.ClassDict.TryGetValue("__module__", out var module) &&
                module is PyString { Value: { Length: > 0 } and not "builtins" } text ? text.Value + "." : "";
            // object_repr: a static type's __module__ and __qualname__ are its tp_name's parts.
            string name = type is PyClass named ? named.QualNameText : type.FullName;
            return new PyString($"<{modulePrefix}{name} object at {PyObjectId.Pointer(self)}>");
        }

        // A builtin type's own dict, then the slot wrappers SharpPy keeps beside it (PyType.SlotDict).
        private static PyObject? BuildTypeAttribute(PyType pyType, string name)
        {
            if (pyType.TypeDict != null && pyType.TypeDict.TryGetValue(name, out PyObject descriptor))
                return descriptor;
            if (pyType.SlotDict.TryGetValue(name, out var slotWrapper))
                return slotWrapper;
            return ReferenceEquals(pyType, PyType.ObjectType) && name == "__class__" ? PyType.TypeType : null;
        }

        /// <summary>
        /// object_new's Py_TPFLAGS_IS_ABSTRACT refusal (Objects/typeobject.c:5459). The flag follows
        /// the truth of the type's own __abstractmethods__ (type_set_abstractmethods), and the message
        /// lists those names sorted (type_abstractmethods reads the type's own dict only).
        /// </summary>
        internal void CheckAbstractMethods()
        {
            if (!ClassDict.TryGetValue("__abstractmethods__", out var abstracts) || !abstracts.IsTrue())
                return;
            var sorted = new PyList(PySet.SourceItems(abstracts));
            sorted.Sort();
            var names = new List<string>();
            foreach (var item in sorted.Items)
                names.Add(item is PyString text ? text.Value : throw PyTypeError.Create(
                    $"sequence item {names.Count}: expected str instance, {item.GetTypeName()} found"));
            throw PyTypeError.Create(
                $"Can't instantiate abstract class {Name} without an implementation for abstract " +
                $"method{(names.Count > 1 ? "s" : "")} '{string.Join("', '", names)}'");
        }

    }

    #endregion

    #region Class Instances

    /// <summary>
    /// 사용자 정의 클래스의 인스턴스
    /// </summary>
    public class PyClassInstance : PyObject, IInstanceDictStore
    {
        public PyClass InstanceType { get; private set; }

        // --- Attribute-store API (CPython 3.12: _PyObject_MaterializeManagedDict pattern) ---

        public bool TryGetInstanceAttribute(string name, out PyObject value)
        {
            if (_materialized != null)
                return _materialized.TryGetStoredValue(new PyString(name), out value);
            return _attrs.TryGetValue(name, out value);
        }

        // The slots of __slots__ are storage of the instance beside its __dict__.
        private Dictionary<string, PyObject> _slots;

        internal bool TryGetSlot(string name, out PyObject value)
        {
            value = null;
            return _slots != null && _slots.TryGetValue(name, out value);
        }

        internal void SetSlot(string name, PyObject value) => (_slots ??= new Dictionary<string, PyObject>())[name] = value;

        internal bool RemoveSlot(string name) => _slots != null && _slots.Remove(name);

        public void SetInstanceAttribute(string name, PyObject value)
        {
            if (_materialized != null)
                _materialized.SetItem(new PyString(name), value);
            else
                _attrs[name] = value;
        }

        public bool RemoveInstanceAttribute(string name)
        {
            // Dictionary reuses a freed slot, which would reorder __dict__; the dict keeps insertion order.
            if (_materialized == null && !_attrs.ContainsKey(name)) return false;
            var dict = Materialize();
            var key = new PyString(name);
            if (!dict.ContainsKey(key)) return false;
            dict.DelItem(key);
            return true;
        }

        // What was assigned to __dict__ when it is more than a dict: x.__dict__ is x.
        private PyObject _assignedDict;

        // PyObject_GenericSetDict: the dict replaces the store itself, a subclass's table included.
        internal void SetDict(PyObject value)
        {
            _materialized = (value as PyDict) ?? throw PyTypeError.Create(
                $"__dict__ must be set to a dictionary, not a '{value.GetPyType().Name}'");
            _assignedDict = value;
            _attrs = null;
        }

        // subtype_setdict with NULL: the reference goes, a new dict comes on the next use.
        internal void DropDict()
        {
            _materialized = null;
            _assignedDict = null;
            _attrs = new Dictionary<string, PyObject>();
        }

        // _PyObject_MaterializeManagedDict: the compact store moves into the dict once and for all.
        private PyDict Materialize()
        {
            if (_materialized == null)
            {
                var dict = new PyDict();
                foreach (var entry in _attrs)
                    dict.SetItem(new PyString(entry.Key), entry.Value);
                _materialized = dict;
                _attrs = null;
            }
            return _materialized;
        }

        public IEnumerable<KeyValuePair<string, PyObject>> InstanceAttributes
        {
            get
            {
                if (_materialized != null)
                {
                    var keys = _materialized.GetStorageKeyEnumerator();
                    while (keys.MoveNext())
                    {
                        if (keys.Current is PyString ps && _materialized.TryGetStoredValue(ps, out var stored))
                            yield return new KeyValuePair<string, PyObject>(ps.Value, stored);
                    }
                }
                else
                {
                    foreach (var entry in _attrs)
                        yield return new KeyValuePair<string, PyObject>(entry.Key, entry.Value);
                }
            }
        }

        // CPython 3.12: _PyObject_MaterializeManagedDict — exactly one of _attrs / _materialized is live.
        private Dictionary<string, PyObject> _attrs;
        private PyDict _materialized;

        public PyClassInstance(PyClass instanceType)
        {
            InstanceType = instanceType;
            _attrs = new Dictionary<string, PyObject>();
        }

        public override string ToString()
        {
            // Debugging only: Simple C# representation
            return $"<{InstanceType.Name} instance at {PyObjectId.Pointer(this)}>";
        }

        private bool IsExceptionClass()
        {
            // Check if this class inherits from Exception
            return InstanceType.Name.EndsWith("Error", StringComparison.Ordinal) ||
                   InstanceType.Name == "Exception" ||
                   InstanceType.BaseTypes.Any(bt => bt.Name == "Exception" || bt.Name == "BaseException");
        }

        public override PyType GetPyType() => InstanceType;
        public override string GetTypeName() => InstanceType.Name;

        /// <summary>
        /// CPython 3.12: Get next item by calling __next__ method
        /// </summary>
        protected internal override PyObject IterNext() => InvokeNextMethod();

        /// <summary>
        /// CPython 3.12: Helper to call a magic method if it exists
        /// Searches instance dict first, then class MRO
        /// </summary>
        private PyObject CallMagicMethod(string methodName, params PyObject[] args)
        {
            // Check instance dict first
            if (TryGetInstanceAttribute(methodName, out var ownMethod))
            {
                return ownMethod.Call(args, null);
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

        public override PyObject DivMod(PyObject other)
        {
            var result = BinaryOpWithMagicMethod(other, "__divmod__", "__rdivmod__");
            return result ?? base.DivMod(other);
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
        // type_new adds a __dict__ getset to each class; anything else in that place is the class's own.
        private bool DictIsReplaced()
        {
            foreach (var type in InstanceType.MRO)
                if (type is PyClass cls && cls.ClassDict.TryGetValue("__dict__", out var found))
                    return found is not PyGetSetDescriptor;
            return false;
        }

        private bool IsShadowedInClass(string name)
        {
            foreach (var type in InstanceType.MRO)
                if (type is PyClass cls && cls.ClassDict.ContainsKey(name))
                    return true;
            return false;
        }

        internal override PyObject GenericGetAttr(string name) => GetAttributeGeneric(name);

        internal PyObject GetAttributeGeneric(string name)
        {
            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyClassInstance.GetAttributeGeneric: {InstanceType.Name} instance.{name} (no custom __getattribute__)");
            #endif

            // 특별한 속성들 먼저 처리
            // object.__class__ is a getset on object: a class of the MRO may shadow it.
            if (name == "__class__" && !IsShadowedInClass(name))
            {
                return InstanceType;
            }
            if (name == "__dict__" && !DictIsReplaced())
            {
                // CPython 3.12: Return cached wrapper for identity consistency
                // obj.__dict__ is obj.__dict__ must be True
                if (InstanceType.InstancesHaveDict())
                {
                    return _assignedDict ?? Materialize();
                }
                // __slots__ class without '__dict__': fall through to the normal
                // MRO lookup, which ends in AttributeError like CPython.
            }

            // _PyObject_GenericGetAttrWithDict over the instance's own store; what a builtin base
            // holds, and the AttributeError, are object's generic lookup.
            return SubclassInstanceSupport.GenericGetAttrWithDict(this, InstanceType, name, null, this) ?? base.TpGetattro(name);
        }

        // PyObject_GenericSetAttr; object_set_class answers __class__.
        internal override void GenericSetAttr(string name, PyObject value)
        {
            if (name == "__class__")
                InstanceType = SubclassInstanceSupport.ClassAssignment(InstanceType, value);
            else
                SetAttributeDefault(name, value);
        }

        // _PyObject_GenericSetAttrWithDict over the instance's own store; also object.__delattr__.
        public void SetAttributeDefault(string name, PyObject value) =>
            SubclassInstanceSupport.GenericSetAttrWithDict(this, InstanceType, name, value, null, this);

        internal override void GenericDelAttr(string name) => DelAttributeDefault(name);

        public void DelAttributeDefault(string name) =>
            SubclassInstanceSupport.GenericSetAttrWithDict(this, InstanceType, name, null, null, this);

        /// <summary>
        /// _PyType_Lookup (Objects/typeobject.c) searches the MRO once; first hit decides.
        /// Returns the data descriptor or null (non-data descriptor also returns null, stopping search).
        /// </summary>
        private PyObject FindDataDescriptorInMro(string name) => FindDataDescriptorInMro(InstanceType, name);

        // _PyObject_GenericSetAttrWithDict's _PyType_Lookup: the first level of the MRO that names it,
        // which counts when it is a data descriptor.
        // _PyType_Lookup, and whether what it found has tp_descr_set.
        internal static PyObject FindDataDescriptorInMro(PyType type, string name) =>
            type.FindNameInMro(name) is { } found && IsDataDescriptor(found) ? found : null;

        /// <summary>
        /// CPython 3.12: Call descriptor's __delete__ method
        /// </summary>
        internal static void CallDescriptorDelete(PyObject descriptor, PyObject instance)
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



        // CPython 3.12: Objects/object.c:1675-1697 (PyObject_IsTrue) tries nb_bool, then
        // sq_length/mp_length. A container subclass inherits that length slot from its base, so an
        // empty C() is false without the class declaring __bool__ or __len__ - and a declared one
        // still wins, which is why the user lookup runs first.


    }

    #endregion

        /// <summary>
    /// Tuple subclass instance for user-defined classes that inherit from tuple
    /// CPython 3.12: Used for namedtuple and other tuple subclasses
    /// </summary>
    public class PyTupleSubclass : PyTuple
    {


        // update_one_slot: a declared __repr__ or __str__ replaces the slot, otherwise tuple's stays.





        public PyTupleSubclass(PyClass instanceType, PyObject[] items) : base(items)
        {
            SetCustomType(instanceType);
        }

        // User-defined descriptors, methods and the instance __dict__ live in the Python class dict,
        // which CLR inheritance cannot see. Shared with PyBytesSubclass.
    }

#region Super Implementation

    /// <summary>
    /// Python의 super() 구현
    /// </summary>
    // PySuper moved to PyBuiltin.cs to follow CPython 3.12 structure

    #endregion
}
