namespace SharpPy
{
    /// <summary>
    /// Python object의 핵심 구현 - 데모 동작에 필요한 기능 포함
    /// CPython 3.12: All PyObjects have ob_type field (Include/object.h:164-195)
    /// </summary>
    public abstract class PyObject
    {
        internal PyString Format(PyString spec)
        {
            // Exact builtins have immutable format slots. Subclasses still bind
            // the type descriptor; instance __getattribute__ must not run here.
            if (IsExactly(PyType.FloatType)) return PyString.From(PyFloat.FormatFloat(((PyFloat)this).Value, spec.Value));
            if (IsExactly(PyType.IntType)) return PyString.From(PyInt.FormatInt(((PyInt)this).Value, spec.Value));
            if (IsExactly(PyType.StrType)) return spec.Value.Length == 0 ? (PyString)this
                : PyString.From(PyString.FormatString(((PyString)this).Value, spec.Value));
            var method = LookupSpecialMethodUncached("__format__");
            if (method == null) throw PyTypeError.Create($"Type {GetTypeName()} doesn't define __format__");
            var result = BindSpecialMethod(method).Call(new PyObject[] { spec }, null);
            if (result is not PyString text)
                throw PyTypeError.Create($"__format__ must return a str, not {result.GetTypeName()}");
            return text;
        }

        #region Core Identity

        // CPython 3.12: Include/object.h:195 - PyTypeObject *ob_type;
        // For subclasses of builtin types (IntEnum, StrEnum, etc.), this stores the actual subclass type
        // If null, GetPyType() returns the default type for this object
        protected PyType? _customType;
        internal bool HasCustomType => _customType != null;

        /// <summary>
        /// Py_IS_TYPE(o, type), which every PyX_CheckExact is: the object's type is that one, not a
        /// class derived from it and not a builtin that shares its layout (a struct sequence is no
        /// exact tuple). Neither the CLR class nor the presence of a heap type alone says this.
        /// </summary>
        internal bool IsExactly(PyType type) => ReferenceEquals(GetPyType(), type);

        // CPython 3.12: Include/object.h:350 - Py_TYPE(op) macro
        // Returns ob_type if set, otherwise returns default type
        public virtual PyType GetPyType() => _customType ?? PyType.ObjectType;

        // Py_TYPE(o)->tp_name: "module.name" for a C module's type, the bare name for builtins' and
        // for a class statement's.
        public virtual string GetTypeName() => GetPyType().FullName;
        public virtual PyType PyClass => GetPyType();

        /// <summary>
        /// Set custom type for this object (used by subclasses of builtin types)
        /// CPython 3.12: This corresponds to setting ob_type field
        /// </summary>
        public void SetCustomType(PyType customType)
        {
            _customType = customType;
        }

        /// <summary>
        /// ob_type at allocation (type->tp_alloc of the type asked for): a class written in Python
        /// is kept in _customType, where every heap type is; the answer is the builtin whose layout
        /// this object is.
        /// </summary>
        protected PyType Allocated(PyType type)
        {
            if (type is not SharpPy.PyClass heap)
                return type;
            SetCustomType(heap);
            foreach (var entry in heap.MRO)
                if (entry is not SharpPy.PyClass)
                    return entry;
            return PyType.ObjectType;
        }

        protected virtual int GetDefaultHash()
        {
            return GetTypeName().GetHashCode();
        }

        #endregion

        #region String Representation

        // Python repr() - returns PyString
        /// <summary>
        /// The tp_repr function of this object's CLR implementation, what its __repr__ slot wrapper
        /// calls; object_repr (Objects/typeobject.c) unless a builtin type has its own.
        /// </summary>
        protected internal virtual PyObject Repr() => SharpPy.PyClass.ObjectRepr(this);

        /// <summary>
        /// PyObject_Repr (Objects/object.c): Py_TYPE(v)->tp_repr(v) inside a recursive call, and
        /// what it answers must be a str. A class written in Python has the slot update_one_slot
        /// gave it.
        /// </summary>
        public PyString ToRepr()
        {
            PyObject result;
            var recursion = PyCRecursion.EnterState(" while getting the repr of an object");
            try
            {
                result = TpRepr();
            }
            finally
            {
                recursion.Depth--;
            }
            // PyUnicode_Check: a str subclass instance answers too.
            return result as PyString ??
                throw PyTypeError.Create($"__repr__ returned non-string (type {result.GetPyType().FullName})");
        }

        // Py_TYPE(v)->tp_repr(v): the type's repr slot, without PyObject_Repr's checks.
        internal PyObject TpRepr() => GetPyType() is SharpPy.PyClass heap ? heap.ReprSlot(this) : Repr();

        /// <summary>
        /// The tp_str function of this object's CLR implementation, what its __str__ slot wrapper
        /// calls; object_str (Objects/typeobject.c), the type's tp_repr, unless a builtin has its own.
        /// </summary>
        protected internal virtual PyObject Str() => TpRepr();

        /// <summary>
        /// PyObject_Str (Objects/object.c): an exact str is itself; otherwise Py_TYPE(v)->tp_str(v)
        /// inside a recursive call, and what it answers must be a str.
        /// </summary>
        public PyString AsString()
        {
            if (IsExactly(PyType.StrType))
                return (PyString)this;
            PyObject result;
            var recursion = PyCRecursion.EnterState(" while getting the str of an object");
            try
            {
                result = GetPyType() is SharpPy.PyClass heap ? heap.StrSlot(this) : Str();
            }
            finally
            {
                recursion.Depth--;
            }
            return result as PyString ??
                throw PyTypeError.Create($"__str__ returned non-string (type {result.GetPyType().FullName})");
        }

        // CPython-internal 호환 이름. **비가상**이므로 서브클래스는 반드시 AsString()을
        // override 해야 하고(누락은 컴파일 오류로 드러난다), ToStr/AsString 이중 경로가
        // 생기지 않는다.  신규 코드는 AsString()을 호출한다.
        public PyString ToStr() => AsString();

        // ❌ 절대 금지: ToString() → AsString() 위임
        // ToString()은 CLR 디버깅 전용 identity(<type object at 0x…>)다.
        public override string ToString() => $"<{GetTypeName()} object at {PyObjectId.Pointer(this)}>";

        #endregion

        #region Hash Protocol

        public virtual int ToHash()
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        public override int GetHashCode() => ToHash();

        /// <summary>
        /// The tp_hash function of this object's CLR implementation: what ToHash gives for a
        /// builtin that keeps its hash there, object's identity hash otherwise. A builtin whose
        /// tp_hash is PyObject_HashNotImplemented says so with __hash__ = None in its dict.
        /// </summary>
        protected internal virtual long Hash()
        {
            if (GetPyType().FindNameInMro("__hash__") is PyNone)
                return HashNotImplemented(this);
            return ToHash();
        }

        /// <summary>PyObject_HashNotImplemented (Objects/object.c), the tp_hash of an unhashable type.</summary>
        internal static long HashNotImplemented(PyObject v) =>
            throw PyTypeError.Create($"unhashable type: '{v.GetPyType().FullName}'");

        /// <summary>
        /// PyObject_Hash (Objects/object.c): Py_TYPE(v)->tp_hash(v); a class written in Python has
        /// the slot update_one_slot gave it.
        /// </summary>
        internal long GetPythonHash() => GetPyType() is SharpPy.PyClass heap ? heap.HashSlot(this) : Hash();

        #endregion

        #region Index Protocol

        /// <summary>
        /// Convert an object to an integer index by calling __index__().
        /// CPython 3.12: Objects/abstract.c:1324-1355 (PyNumber_Index)
        /// </summary>
        /// <param name="obj">The object to convert</param>
        /// <returns>PyInt if successful, null if __index__ not found</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        /// <summary>PyIndex_Check: the type fills nb_index (int's own included).</summary>
        internal static bool IndexCheck(PyObject value) =>
            PyInt.LongCheck(value) || value.HasTypeSlot("__index__");

        /// <summary>PyNumber_Check: complex has neither __int__ nor __float__.</summary>
        internal static bool NumberCheck(PyObject value) =>
            value is PyFloat or PyComplex || IndexCheck(value) ||
            value.HasTypeSlot("__int__") ||
            value.HasTypeSlot("__float__");

        public static PyInt? TryGetIndex(PyObject obj)
        {
            // PyNumber_Index: an int (bool and a Python subclass included) is its own value.
            return PyInt.LongValue(obj) ?? TryGetIndexSlow(obj);
        }

        /// <summary>
        /// CPython PyNumber_Index as used by bin(), hex(), oct() and operator.index().
        /// TryGetIndex already carries the measured rule that an int subclass keeps long's C slot:
        ///   class IO(int):  def __index__(self): return 3
        ///   operator.index(IO(5)) -> 5, bin(IO(5)) -> '0b101', IO(5).__index__() -> 3
        /// so the value is used and a Python-level override is not consulted for int subclasses,
        /// while a plain user class still answers through __index__.
        /// Failure text is CPython's "'T' object cannot be interpreted as an integer".
        /// </summary>
        internal static PyInt IndexOrThrow(PyObject obj)
        {
            var index = TryGetIndex(obj);
            if (index != null)
                return index;
            throw PyTypeError.Create(
                $"'{obj.GetTypeName()}' object cannot be interpreted as an integer");
        }

        private static PyInt TryGetIndexSlow(PyObject obj)
        {
            // Special methods come from the type; exceptions are not "missing".
            var method = obj.LookupSpecialMethodUncached("__index__");
            if (method == null) return null;
            var result = obj.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
            if (result.IsExactly(PyType.IntType)) return (PyInt)result;
            // _PyNumber_Index: a strict subclass of int still answers, with a warning.
            if (result is PyBool or PyIntSubclass)
            {
                PyWarnings.Deprecated($"__index__ returned non-int (type {result.GetTypeName()}).  The ability to return an instance of a strict subclass of int is deprecated, and may be removed in a future version of Python.");
                return PyInt.LongValue(result);
            }
            throw PyTypeError.Create($"__index__ returned non-int (type {result.GetTypeName()})");
        }

        /// <summary>
        /// Convert object to int index or throw TypeError.
        /// CPython 3.12: Used for list/tuple/string indexing
        /// </summary>
        /// <summary>
        /// A sequence subscript: _PyIndex_Check, then PyNumber_AsSsize_t(item, PyExc_IndexError). A
        /// Py_ssize_t that no CLR sequence can reach is clamped, so the caller's range check reports it.
        /// </summary>
        public static int GetIndex(PyObject obj, string typeName) =>
            TrySequenceIndex(obj, out int index) ? index
                : throw PyTypeError.Create($"{typeName} indices must be integers or slices, not {obj.GetTypeName()}");

        /// <summary>
        /// The integer key of a sequence subscript (tuplesubscript, bytes_subscript, unicode_subscript,
        /// array_subscr, ...): _PyIndex_Check, then PyNumber_AsSsize_t(item, PyExc_IndexError). False
        /// when the key is no index, the type's own TypeError then.
        /// </summary>
        internal static bool TrySequenceIndex(PyObject item, out int index)
        {
            var value = TryGetIndex(item);
            if (value == null)
            {
                index = 0;
                return false;
            }
            long position = PyArgs.FitSsize(item, value.Value, PyArgs.SsizeError.Index);
            index = position > int.MaxValue ? int.MaxValue : position < int.MinValue ? int.MinValue : (int)position;
            return true;
        }

        #endregion

        #region Comparison Protocol

        public enum CompareOp { LT, LE, EQ, NE, GT, GE }

        protected virtual PyObject PyEquals(PyObject other)
        {
            return ReferenceEquals(this, other) ? PyBool.True : PyNotImplemented.Instance;
        }

        protected virtual PyObject PyNotEquals(PyObject other)
        {
            var result = RichCompare(other, CompareOp.EQ);
            return result == PyNotImplemented.Instance ? result : PyBool.FromBool(!result.IsTrue());
        }

        protected virtual PyObject PyLess(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        protected virtual PyObject PyLessEqual(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        protected virtual PyObject PyGreater(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        protected virtual PyObject PyGreaterEqual(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// Py_TYPE(self)->tp_richcompare(self, other, op): a builtin's slot is its function, a class
        /// written in Python has the slot update_one_slot gave it. do_richcompare (Compare) asks both
        /// operands in the order CPython does.
        /// </summary>
        public PyObject RichCompare(PyObject other, CompareOp op)
        {
            if ((uint)op >= 6) ThrowInvalidComparison(op);
            return GetPyType() is SharpPy.PyClass heap ? heap.RichCompareSlot(this, other, op) : TpRichCompare(other, op);
        }

        /// <summary>The tp_richcompare function of this object's CLR implementation (object_richcompare).</summary>
        protected internal virtual PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            return op switch
            {
                CompareOp.EQ => PyEquals(other),
                CompareOp.NE => PyNotEquals(other),
                CompareOp.LT => PyLess(other),
                CompareOp.LE => PyLessEqual(other),
                CompareOp.GT => PyGreater(other),
                CompareOp.GE => PyGreaterEqual(other),
                _ => throw PyValueError.Create($"Invalid comparison operator: {op}")
            };
        }

        // RichCompare is a single type's slot; Compare resolves both operands.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static PyObject Compare(PyObject left, PyObject right, CompareOp op)
        {
            // Exact builtin numbers always handle each other without Python callbacks.
            if (left._customType == null && right._customType == null && IsExactNumber(right) && IsExactNumber(left))
                return left.RichCompare(right, op);
            // PyObject_RichCompare: _Py_EnterRecursiveCallTstate(" in comparison") around do_richcompare.
            PyCRecursion.Enter(" in comparison");
            try
            {
                return ResolveComparison(left, right, op);
            }
            finally
            {
                PyCRecursion.Leave();
            }
        }

        private static PyObject ResolveComparison(PyObject left, PyObject right, CompareOp op)
        {
            var leftType = left.GetPyType();
            var rightType = right.GetPyType();
            bool reverseFirst = !ReferenceEquals(leftType, rightType) && rightType.MRO.Contains(leftType);
            PyObject result;
            if (reverseFirst)
            {
                result = right.RichCompare(left, ReverseComparison(op));
                if (result != PyNotImplemented.Instance) return result;
            }
            result = left.RichCompare(right, op);
            if (result != PyNotImplemented.Instance) return result;
            if (!reverseFirst)
            {
                result = right.RichCompare(left, ReverseComparison(op));
                if (result != PyNotImplemented.Instance) return result;
            }
            if (op == CompareOp.EQ) return PyBool.FromBool(ReferenceEquals(left, right));
            if (op == CompareOp.NE) return PyBool.FromBool(!ReferenceEquals(left, right));
            string symbol = op switch
            {
                CompareOp.LT => "<", CompareOp.LE => "<=",
                CompareOp.GT => ">", CompareOp.GE => ">=",
                _ => throw PyValueError.Create($"Invalid comparison operator: {op}")
            };
            throw PyTypeError.Create($"'{symbol}' not supported between instances of '{left.GetTypeName()}' and '{right.GetTypeName()}'");
        }

        private static bool IsExactNumber(PyObject value)
        {
            var type = value.GetType();
            return type == typeof(PyInt) || type == typeof(PyFloat) || type == typeof(PyBool);
        }

        private static CompareOp ReverseComparison(CompareOp op) => op switch
        {
            CompareOp.LT => CompareOp.GT, CompareOp.LE => CompareOp.GE,
            CompareOp.GT => CompareOp.LT, CompareOp.GE => CompareOp.LE,
            _ => op
        };

        public static bool CompareBool(PyObject left, PyObject right, CompareOp op)
        {
            if (ReferenceEquals(left, right))
            {
                if (op == CompareOp.EQ) return true;
                if (op == CompareOp.NE) return false;
            }
            return Compare(left, right, op).IsTrue();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ThrowInvalidComparison(CompareOp op)
        {
            throw PyValueError.Create($"Invalid comparison operator: {op}");
        }

        /// <summary>
        /// Keyword lookup for builtins that are METH_FASTCALL|METH_KEYWORDS in CPython but whose
        /// SharpPy delegates read the positional array. PyDict.GetItem raises KeyError on a missing
        /// key, so presence is checked first; a missing parameter must stay "absent" so the delegate's
        /// own default and required-argument handling applies.
        /// </summary>
        internal static PyObject KwValue(PyDict kwargs, string name)
        {
            if (kwargs == null)
                return null;
            var key = PyString.From(name);
            return kwargs.ContainsKey(key) ? kwargs.GetItem(key) : null;
        }


        #endregion

        #region Truth Value Protocol

        /// <summary>
        /// The nb_bool function of this object's CLR implementation. A builtin without one is
        /// true unless it has a length (PyObject_IsTrue reads mp_length or sq_length then); its
        /// slots may be only the functions of the wrappers in its dict.
        /// </summary>
        protected internal virtual bool Bool()
        {
            var type = GetPyType();
            if (type.FindNameInMro("__bool__") is PyWrapperDescriptor { CallsVirtual: false } wrapper)
                return wrapper._implementation(this, Array.Empty<PyObject>(), null).IsTrue();
            return type.FindNameInMro("__len__") == null || Len() != 0;
        }

        /// <summary>
        /// CPython 3.12: Objects/object.c:1675-1697 (PyObject_IsTrue)
        /// Returns the truth value of an object.
        /// Returns true if the object is considered true, false if considered false.
        ///
        /// Truth value testing algorithm:
        /// 1. True → true
        /// 2. False → false
        /// 3. None → false
        /// 4. If __bool__ exists, call it and use its result
        /// 5. If __len__ exists (mapping or sequence), call it and return length > 0
        /// 6. Otherwise → true (default for all objects)
        /// </summary>
        public bool IsTrue()
        {
            // PyObject_IsTrue (Objects/object.c): Py_True, Py_False and Py_None first, then nb_bool,
            // and a class written in Python has the slot update_one_slot gave it.
            if (this is PyBool boolean) return boolean.Value;
            if (ReferenceEquals(this, PyNone.Instance)) return false;
            return GetPyType() is SharpPy.PyClass heap ? heap.BoolSlot(this) : Bool();
        }

        #endregion

        #region Attribute Access Protocol (MRO-based with Descriptor Support)

        /// <summary>
        /// PyObject_GetAttr: the type's tp_getattro. A class written in Python has the slot
        /// update_one_slot gave it (slot_tp_getattr_hook when it writes __getattribute__ or has a
        /// __getattr__), a metaclass for its classes as any class for its instances; type's is
        /// type_getattro, the class object's own TpGetattro.
        /// </summary>
        public PyObject GetAttribute(string name) =>
            HeapType() is { } heap ? heap.GetattroSlot(this, name) : TpGetattro(name);

        // Py_TYPE(o) when it is a heap type, a class written in Python; type itself is static.
        private SharpPy.PyClass HeapType() => GetPyType() is SharpPy.PyClass heap && heap.IsHeapType ? heap : null;

        /// <summary>tp_getattro of the CLR type: the builtin's own, PyObject_GenericGetAttr by default.</summary>
        protected internal virtual PyObject TpGetattro(string name) => GenericGetattro(name, false, out _);

        /// <summary>
        /// Whether tp_getattro is PyObject_GenericGetAttr: false for a CLR type that overrides
        /// TpGetattro (type, module, function, weakref proxy and the like) - every such override
        /// says so here too.
        /// </summary>
        internal virtual bool TpGetattroIsGeneric => true;

        // PyObject_GenericGetAttr, and with method _PyObject_GetMethod's lookup.
        private PyObject GenericGetattro(string name, bool method, out bool unbound)
        {
            // A Python subclass of a builtin with no storage of its own for attributes.
            if (_customType is PyClass heap && !HasOwnDict)
            {
                var attributes = HeapAttributes.GetOrCreateValue(this);
                if (name == "__dict__" && heap.InstancesHaveDict())
                {
                    unbound = false;
                    return PyGlobalsDict.For(attributes);
                }
                return SubclassInstanceSupport.GenericGetAttrWithDict(this, heap, name, attributes, null, method, out unbound) ?? PyGetAttribute(name);
            }
            if (method)
                return SubclassInstanceSupport.GenericGetAttrWithDict(this, GetPyType(), name, null, this as IInstanceDictAccessor, true, out unbound)
                    ?? throw AttributeNotFound(name);
            unbound = false;
            return PyGetAttribute(name);
        }

        /// <summary>
        /// _PyObject_GetMethod (Objects/object.c): with a generic tp_getattro, a method descriptor
        /// found on the type and not shadowed comes back unbound (unbound set), for CALL to pass
        /// the object as its first argument; anything else is PyObject_GetAttr's answer.
        /// </summary>
        internal PyObject GetMethod(string name, out bool unbound)
        {
            unbound = false;
            if (HeapType() is { } heap)
                return heap.GetattroIsInherited() && TpGetattroIsGeneric ? GenericGetMethod(name, out unbound) : heap.GetattroSlot(this, name);
            return TpGetattroIsGeneric ? GenericGetMethod(name, out unbound) : TpGetattro(name);
        }

        // The generic lookup of GetMethod: the CLR type's PyObject_GenericGetAttr.
        internal virtual PyObject GenericGetMethod(string name, out bool unbound) => GenericGetattro(name, true, out unbound);

        /// <summary>
        /// PyObject_GenericGetAttr for this object, which object.__getattribute__ is: tp_getattro
        /// without the __getattribute__/__getattr__ hook of a Python class. A builtin's own
        /// GetAttribute is both.
        /// </summary>
        /// <summary>
        /// The tp_getattro a Python class inherits from its builtin base when it writes neither
        /// __getattribute__ nor __getattr__: PyObject_GenericGetAttr unless the base has its own.
        /// </summary>
        internal virtual PyObject InheritedGetattro(string name) => GenericGetAttr(name);

        internal virtual PyObject GenericGetAttr(string name)
        {
            return TpGetattro(name);
        }

        /// <summary>
        /// PyObject_SetAttr: the type's tp_setattro. A class written in Python has slot_tp_setattro,
        /// its __setattr__, else PyObject_GenericSetAttr (GenericSetAttr); a class object's is
        /// type_setattro, its own TpSetattro.
        /// </summary>
        public void SetAttribute(string name, PyObject value)
        {
            if (HeapType() is { } heap)
            {
                if (!SubclassInstanceSupport.SetattroHook(this, heap, name, value))
                    GenericSetAttr(name, value);
            }
            else
                TpSetattro(name, value);
        }

        /// <summary>PyObject_SetAttr with no value: tp_setattro asked to delete.</summary>
        public void DelAttribute(string name)
        {
            if (HeapType() is { } heap)
            {
                if (!SubclassInstanceSupport.SetattroHook(this, heap, name, null))
                    GenericDelAttr(name);
            }
            else
                TpDelattro(name);
        }

        /// <summary>
        /// PyObject_GenericSetAttr, object.__setattr__: for a Python class over a builtin, the
        /// generic set over the instance dict kept beside the value; else the CLR type's own.
        /// </summary>
        internal virtual void GenericSetAttr(string name, PyObject value)
        {
            TpSetattro(name, value);
        }

        /// <summary>PyObject_GenericSetAttr with no value, object.__delattr__.</summary>
        internal virtual void GenericDelAttr(string name)
        {
            if (this is not PyType && GetPyType() is PyClass heap)
            {
            }
            TpDelattro(name);
        }

        /// <summary>The instance dict of a Python class over a builtin with no store of its own.</summary>
        internal Dictionary<string, PyObject> HeapDict => HeapAttributes.GetOrCreateValue(this);

        // tp_dictoffset of the builtin itself (function, BaseException, classmethod, the io objects,
        // SimpleNamespace): a heap subclass uses that dict, there is no second one beside it.
        // The CLR object keeps its attributes itself (a class instance, or one with tp_dictoffset).
        private bool HasOwnDict => this is IInstanceDictStore;

        // The __slots__ of a Python class over a builtin, beside its __dict__ (PyMemberDescriptor of
        // type_new_descriptors); a PyClassInstance keeps its own.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PyObject, Dictionary<string, PyObject>> SlotValues = new();
        internal Dictionary<string, PyObject> SlotStore => SlotValues.GetOrCreateValue(this);

        /// <summary>tp_setattro of the CLR type: PyObject_GenericSetAttr by default.</summary>
        protected internal virtual void TpSetattro(string name, PyObject value)
        {
            if (_customType is PyClass heap && !HasOwnDict)
                SubclassInstanceSupport.GenericSetAttribute(this, heap, HeapAttributes.GetOrCreateValue(this), name, value);
            else
                PySetAttribute(name, value);
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PyObject, Dictionary<string, PyObject>> HeapAttributes = new();

        /// <summary>tp_setattro of the CLR type asked to delete.</summary>
        protected internal virtual void TpDelattro(string name)
        {
            if (!HasOwnDict && _customType is SharpPy.PyClass &&
                HeapAttributes.TryGetValue(this, out var attributes) && PyGlobalsDict.Delete(attributes, name)) return;
            PyDelAttribute(name);
        }


        // 내부 attribute 처리 메서드들 (MRO 기반)
        protected virtual PyObject PyGetAttribute(string name)
        {
            // CPython 3.12 호환: GenericGetAttribute 사용
            return GenericGetAttribute(name);
        }

        /// <summary>
        /// CPython 3.12: Objects/object.c:1082-1147 (_PyObject_LookupAttr)
        /// Lookup attribute without raising AttributeError.
        /// Returns:
        ///   - attribute value if found
        ///   - null if not found (AttributeError is suppressed)
        ///   - throws other exceptions (not AttributeError)
        ///
        /// This is the key difference from GetAttribute():
        /// - GetAttribute() throws AttributeError if not found
        /// - LookupAttribute() returns null if not found
        ///
        /// Used in isinstance/issubclass to avoid infinite recursion when
        /// checking __bases__ or __class__ attributes.
        /// </summary>
        public virtual PyObject LookupAttribute(string name)
        {
            // CPython's _PyObject_LookupAttr runs the object's NORMAL tp_getattro and suppresses only
            // AttributeError. It must not be narrowed to GenericGetAttribute: in this runtime several
            // types implement their attributes in a GetAttribute override instead of TypeDict
            // descriptors (complex.real is one), so the generic path reports "no attribute" for
            // attributes that getattr finds. Measured: hasattr(1 + 2j, "real") went False while
            // (1 + 2j).real kept working, which is two disagreeing answers about one attribute.
            try
            {
                return GetAttribute(name);
            }
            catch (PythonException ex) when (ex.PyException is PyAttributeError)
            {
                return null;
            }
            // Anything a user __getattr__ raises still propagates.
        }

        /// <summary>
        /// Type-only special method lookup without invoking instance attribute hooks.
        /// CPython v3.12.15, Objects/typeobject.c: lookup_maybe_method / slot_nb_inplace_*.
        /// Deliberately uncached: the global cache does not yet invalidate descendants.
        /// </summary>
        internal PyObject LookupSpecialMethodUncached(string name)
        {
            var found = GetPyType().FindNameInMro(name);
            // The wrapper of a builtin's slot is the virtual this dispatch runs in: its C slot.
            return found is PyWrapperDescriptor { CallsVirtual: true } ? null : found;
        }

        /// <summary>
        /// Whether the type fills the slot (tp_iter != NULL and the like): _PyType_Lookup found
        /// something, a builtin's own slot wrapper included.
        /// </summary>
        /// <summary>PyObject_GetAttr, SetAttr and DelAttr: the name must be a str.</summary>
        internal static string AttributeName(PyObject name) =>
            name is PyString text ? text.Value
            : throw PyTypeError.Create($"attribute name must be string, not '{name.GetTypeName()}'");

        internal bool HasTypeSlot(string name) => GetPyType().FindNameInMro(name) != null;

        /// <summary>
        /// PyMapping_Check: the type has mp_subscript. A __getitem__ that is wrap_sq_item is sq_item
        /// alone; a class statement's type gets slot_mp_subscript for any __getitem__ it finds.
        /// </summary>
        internal static bool MappingCheck(PyObject value)
        {
            var type = value.GetPyType();
            var item = type.FindNameInMro("__getitem__");
            return item != null && (type.IsHeapType || item is not PyWrapperDescriptor { SequenceItem: true });
        }

        /// <summary>
        /// Looks up a special method declared by user classes only, stopping where the C slots take
        /// over. The value-carrying wrappers (PyIntSubclass, PyFloatSubclass) need this narrowing:
        /// their arithmetic is implemented as CLR calls into the wrapped value, so consulting the
        /// full MRO would hand the call straight back to the same virtual (int.__divmod__ reads the
        /// value through FloorDivide) and recurse. What has to win is a dunder the user declared in
        /// their own class; anything else falls through to the inherited base slot, which is exactly
        /// what CPython does with an inherited slot.
        /// </summary>
        internal PyObject LookupUserSpecial(string name)
        {
            var found = GetPyType().FindNameInMro(name, out bool written);
            return written ? found : null;
        }

        /// <summary>Call a user-declared special method, or null when the user declared none.</summary>
        internal PyObject CallUserSpecialOrNull(string name, PyObject[] args)
        {
            var method = LookupUserSpecial(name);
            return method == null ? null : BindSpecialMethod(method).Call(args, null);
        }

        /// <summary>
        /// Turns the result of __len__ into a length the way PyObject_Size does, through
        /// _PyNumber_Index. Measured: a __len__ returning 'short' raises
        /// TypeError: 'str' object cannot be interpreted as an integer.
        /// </summary>
        internal static int ToLengthResult(PyObject value)
        {
            if (PyInt.LongValue(value) is { } integer)
                return (int)integer.Value;
            throw PyTypeError.Create(
                $"'{value.GetTypeName()}' object cannot be interpreted as an integer");
        }

        /// <summary>
        /// Bind a type-level special method, including Python descriptor __get__.
        /// CPython v3.12.15, Objects/typeobject.c: lookup_maybe_method.
        /// Descriptor exceptions must propagate, rather than mean "method missing".
        /// </summary>
        // lookup_maybe_method: what the type holds binds as a descriptor, whatever it is.
        internal PyObject CallSpecial(PyObject method, params PyObject[] args)
        {
            if (method is PyBuiltinFunction)
            {
                var withSelf = new PyObject[args.Length + 1];
                withSelf[0] = this;
                Array.Copy(args, 0, withSelf, 1, args.Length);
                return method.Call(withSelf, null);
            }
            return BindSpecialMethod(method).Call(args, null);
        }

        internal PyObject BindSpecialMethod(PyObject method)
        {
            // lookup_maybe_method: a method descriptor (Py_TPFLAGS_METHOD_DESCRIPTOR) is not bound but
            // called with self first, so one taken from another class fails in wrapperdescr_call or
            // method_vectorcall's self check, not in binding.
            if (method is PyWrapperDescriptor { Name: not "__new__" } wrapper && !PyMethodDescriptor.AppliesTo(wrapper.OwnerType, this)
                || method is PyMethodDescriptor { Name: not "__new__" } methodDescriptor && !PyMethodDescriptor.AppliesTo(methodDescriptor.OwnerType, this))
                return new PyMethod(this, method);
            if (method is IDescriptor descriptor)
                return descriptor.Get(this, GetPyType());

            var getMethod = method.LookupSpecialMethodUncached("__get__");
            if (getMethod != null)
            {
                var boundGet = method.BindSpecialMethod(getMethod);
                return boundGet.Call(new PyObject[] { this, GetPyType() }, null);
            }
            return method;
        }

        /// <summary>
        /// CPython PyObject_GenericGetAttr 호환: 제네릭 속성 접근
        /// MRO 기반 descriptor 프로토콜 구현
        /// </summary>
        protected virtual PyObject GenericGetAttribute(string name)
        {
            // _PyObject_GenericGetAttrWithDict over the object's own store; the type's dict holds
            // the slot wrappers too, as tp_dict does.
            return SubclassInstanceSupport.GenericGetAttrWithDict(this, GetPyType(), name, null, this as IInstanceDictAccessor)
                ?? throw AttributeNotFound(name);
        }

        // CPython distinguishes attribute errors on a class object
        // (type_getattro: "type object 'X' has no attribute 'Y'") from those on
        // an instance ("'X' object has no attribute 'Y'"). PyType overrides this.
        protected virtual System.Exception AttributeNotFound(string name)
            => PyAttributeError.CreateFor(this, name, $"'{GetTypeName()}' object has no attribute '{name}'");

        protected virtual void PySetAttribute(string name, PyObject value) => GenericSetOrDelete(name, value);

        /// <summary>PyObject_GenericSetAttr with value=NULL.</summary>
        protected virtual void PyDelAttribute(string name) => GenericSetOrDelete(name, null);

        // _PyObject_GenericSetAttrWithDict over the instance dict the CLR type keeps, if any (a type
        // has its own slot, PyType.TypeSetAttribute).
        private void GenericSetOrDelete(string name, PyObject value)
        {
            SubclassInstanceSupport.GenericSetAttrWithDict(this, GetPyType(), name, value, null, this as IInstanceDictStore);
        }

        internal System.Exception NoSuchAttribute(string name) => AttributeNotFound(name);

        #endregion


        // Python의 __call__ 메서드 - 모든 객체가 잠재적으로 호출 가능
        #region Call Protocol

        // PyCallable_Check: tp_call, the type's __call__; an instance attribute does not fill it.
        public virtual bool IsCallable() => GetPyType().LookupSpecial("__call__") != null;

        // CPython 3.12 호환: kwargs 지원 버전
        /// <summary>Py_EnterRecursiveCall for native recursion: what no Python frame counts.</summary>
        internal static void CheckNativeStack(string where)
        {
            if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
                throw new PythonException(new PyRecursionError("maximum recursion depth exceeded" + where));
        }

        /// <summary>
        /// object_is_not_callable (Objects/call.c): a module whose attribute of its own name is
        /// callable is offered that ("Did you mean: 'pprint.pprint(...)'?").
        /// </summary>
        internal static Exception ObjectIsNotCallable(PyObject callable)
        {
            var type = callable.GetPyType();
            if (ReferenceEquals(type, PyType.ModuleType) && callable is PyModule module
                && module.ModuleDict.TryGetValue("__name__", out var nameObject) && nameObject is PyString name)
            {
                PyObject attribute = null;
                try { attribute = callable.GetAttribute(name.Value); }
                catch (PythonException) { }
                if (attribute != null && attribute.IsCallable())
                    return PyTypeError.Create($"'{type.FullName}' object is not callable. Did you mean: '{name.Value}.{name.Value}(...)'?");
            }
            return PyTypeError.Create($"'{type.FullName}' object is not callable");
        }

        public virtual PyObject Call(PyObject[] args, PyDict kwargs)
        {
            CheckNativeStack(" while calling a Python object");
            // slot_tp_call (Objects/typeobject.c): __call__ from the type, never the instance, for a
            // class written in Python and a builtin alike. lookup_maybe_method: a function is called
            // with self prepended, anything else is bound through its __get__ (a staticmethod drops
            // self). What the call raises propagates.
            var declared = GetPyType().LookupSpecial("__call__");
            if (declared == null)
                throw ObjectIsNotCallable(this);
            if (declared is PyFunction function)
            {
                var withSelf = new PyObject[args.Length + 1];
                withSelf[0] = this;
                Array.Copy(args, 0, withSelf, 1, args.Length);
                return function.Call(withSelf, kwargs);
            }
            return BindSpecialMethod(declared).Call(args, kwargs);
        }

        #endregion

        #region Basic Type Conversion (CPython Compatible)

        // === To* Methods: PyObject → C# Basic Types (Value Extraction) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyObject에서 C# int 값 추출
        /// </summary>
        public virtual int ToInt()
        {
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a real number, not '{GetTypeName()}'");
        }

        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyObject에서 C# double 값 추출
        /// </summary>
        public virtual double ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a real number, not '{GetTypeName()}'");
        }

        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyObject에서 C# bool 값 추출
        /// </summary>
        public virtual bool ToBool() => IsTrue();

        // === As* Methods: PyObject → PyObject Types (Type Conversion/Casting) ===
        
        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyInt로 변환/캐스팅
        /// </summary>
        /// <summary>The nb_int function of this object's CLR implementation; null when it has none.</summary>
        protected internal virtual PyObject NbInt() => null;

        /// <summary>The nb_float function of this object's CLR implementation; null when it has none.</summary>
        protected internal virtual PyObject NbFloat() => null;

        // Py_TYPE(o)->tp_as_number->nb_int and nb_float, a class written in Python's from update_one_slot.
        internal PyObject TpNbInt() => GetPyType() is SharpPy.PyClass heap ? heap.NbIntSlot(this) : NbInt();
        internal PyObject TpNbFloat() => GetPyType() is SharpPy.PyClass heap ? heap.NbFloatSlot(this) : NbFloat();

        /// <summary>PyNumber_Long (Objects/abstract.c).</summary>
        public PyInt AsInt() => PyInt.NumberLong(this);

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyFloat로 변환/캐스팅
        /// </summary>
        /// <summary>PyNumber_Float (Objects/abstract.c).</summary>
        public PyFloat AsFloat() => PyFloat.NumberFloat(this);

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyBool로 변환/캐스팅
        /// </summary>
        public virtual PyBool AsBool()
        {
            return PyBool.FromBool(ToBool());
        }

        /// <summary>
        /// To~ = C# (CLR) 값 / As~ = Python 타입. 이 메서드는 Python str()
        /// 내용을 **C# string**으로 뽑는다(그러므로 To~).
        /// Python 타입(PyString)이 필요하면 AsString()(호환 별명 ToStr()).
        /// ToString()은 별개: CLR 디버깅 전용 identity(&lt;type object at 0x…&gt;).
        /// </summary>
        public virtual string ToClrString() => AsString().Value;

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyList로 변환/캐스팅
        /// </summary>
        public virtual PyList AsList()
        {
            return PyList.FromIterable(this);
        }

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyDict로 변환/캐스팅
        /// CPython 3.12: Objects/dictobject.c:2637-2672 - dict_init
        /// </summary>
        public virtual PyDict AsDict()
        {
            var created = new PyDict();
            PyDict.UpdateCommon(created, this);
            return created;
        }

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyTuple로 변환/캐스팅
        /// </summary>
        public virtual PyTuple AsTuple()
        {
            // PySequence_Tuple: errors raised while consuming an iterator propagate.
            var items = new List<PyObject>();
            var iter = GetIterator();
            LengthHint(10);
            while (true)
            {
                try
                {
                    items.Add(iter.Next());
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    break;
                }
            }
            return PyTuple.From(items.ToArray());
        }

        // CPython PyObject_LengthHint: TypeError from len/call is a fallback,
        // while invalid hint return values and other exceptions propagate.
        internal long LengthHint(long defaultValue)
        {
            if (this is PyRange range) return range.PythonLength();
            // _PyObject_HasLen: no iterator has one, and asking would raise for each list(iterator).
            if (this is not PyIterator)
            try
            {
                // PyObject_Length; a TypeError is a type without a length.
                return Size();
            }
            catch (PythonException ex) when (ex.PyException is PyTypeError) { }

            var hintMethod = LookupSpecialMethodUncached("__length_hint__");
            if (hintMethod == null) return defaultValue;
            var boundHint = BindSpecialMethod(hintMethod);
            PyObject hint;
            try { hint = boundHint.Call(Array.Empty<PyObject>(), null); }
            catch (PythonException ex) when (ex.PyException is PyTypeError) { return defaultValue; }
            if (hint == PyNotImplemented.Instance) return defaultValue;
            if (!PyInt.LongCheck(hint))
                throw PyTypeError.Create($"__length_hint__ must be an integer, not {hint.GetTypeName()}");
            long value;
            try { value = (long)nint.CreateChecked(TryGetIndex(hint).Value); }
            catch (OverflowException)
            {
                throw PyArgs.SsizeOverflow();
            }
            if (value < 0) throw PyValueError.Create("__length_hint__() should return >= 0");
            return value;
        }

        // PyObject_Size: sq_length or mp_length, which a class fills with slot_sq_length.
        /// <summary>
        /// The sq_length or mp_length function of this object's CLR implementation. A builtin's
        /// slot may be only the function of the wrapper in its dict.
        /// </summary>
        protected internal virtual long Len()
        {
            switch (GetPyType().FindNameInMro("__len__"))
            {
                case PyWrapperDescriptor { CallsVirtual: false } wrapper:
                    return ToLengthResult(wrapper._implementation(this, Array.Empty<PyObject>(), null));
                case PyMethodDescriptor method:
                    return ToLengthResult(method._implementation(this, Array.Empty<PyObject>(), null));
            }
            throw PyTypeError.Create($"object of type '{GetTypeName()}' has no len()");
        }

        /// <summary>
        /// PyObject_Size (Objects/abstract.c): sq_length, then mp_length, of the type; a class
        /// written in Python has the slot update_one_slot gave it.
        /// </summary>
        public long Size() => GetPyType() is SharpPy.PyClass heap ? heap.LenSlot(this) : Len();

        /// <summary>PyObject_Size where SharpPy needs an int: a CLR collection is never longer.</summary>
        public int Length()
        {
            long size = Size();
            if (size > int.MaxValue)
                throw PyOverflowError.Create("cannot fit 'int' into an index-sized integer");
            return (int)size;
        }

        #endregion

        #region Arithmetic Operations

        /// <summary>
        /// 더하기 연산 (__add__)
        /// </summary>
        public virtual PyObject Add(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 빼기 연산 (__sub__)
        /// </summary>
        public virtual PyObject Subtract(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 곱하기 연산 (__mul__)
        /// </summary>
        public virtual PyObject Multiply(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 나누기 연산 (__truediv__)
        /// </summary>
        public virtual PyObject Divide(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 정수 나누기 연산 (__floordiv__)
        /// </summary>
        public virtual PyObject FloorDivide(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 나머지 연산 (__mod__)
        /// </summary>
        public virtual PyObject Modulo(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// divmod() 연산 (__divmod__)
        /// CPython: Objects/longobject.c:long_divmod (lines 4509-4526)
        /// Returns tuple of (quotient, remainder)
        /// </summary>
        public virtual PyObject DivMod(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 거듭제곱 연산 (__pow__)
        /// </summary>
        public virtual PyObject Power(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 비트 AND 연산 (__and__)
        /// </summary>
        public virtual PyObject BitwiseAnd(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 비트 OR 연산 (__or__)
        /// </summary>
        public virtual PyObject BitwiseOr(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 비트 XOR 연산 (__xor__)
        /// </summary>
        public virtual PyObject BitwiseXor(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 왼쪽 시프트 연산 (__lshift__)
        /// </summary>
        public virtual PyObject LeftShift(PyObject other)
        {
            // CPython slots return NotImplemented so reflected operands get a turn.
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 오른쪽 시프트 연산 (__rshift__)
        /// </summary>
        public virtual PyObject RightShift(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        /// <summary>
        /// 단항 플러스 연산 (__pos__)
        /// </summary>
        // nb_positive, nb_negative, nb_invert and nb_absolute of this object's CLR implementation.
        protected internal virtual PyObject NbPositive() =>
            throw PyTypeError.Create($"bad operand type for unary +: '{GetTypeName()}'");
        protected internal virtual PyObject NbNegative() =>
            throw PyTypeError.Create($"bad operand type for unary -: '{GetTypeName()}'");
        protected internal virtual PyObject NbInvert() =>
            throw PyTypeError.Create($"bad operand type for unary ~: '{GetTypeName()}'");
        protected internal virtual PyObject NbAbsolute() =>
            throw PyTypeError.Create($"bad operand type for abs(): '{GetTypeName()}'");

        /// <summary>PyNumber_Positive, PyNumber_Negative, PyNumber_Invert and PyNumber_Absolute: the type's slot.</summary>
        public PyObject Positive() => GetPyType() is SharpPy.PyClass heap ? heap.NbPositiveSlot(this) : NbPositive();
        public PyObject Negative() => GetPyType() is SharpPy.PyClass heap ? heap.NbNegativeSlot(this) : NbNegative();
        public PyObject BitwiseNot() => GetPyType() is SharpPy.PyClass heap ? heap.NbInvertSlot(this) : NbInvert();
        public PyObject Absolute() => GetPyType() is SharpPy.PyClass heap ? heap.NbAbsoluteSlot(this) : NbAbsolute();



        /// <summary>
        /// 논리 NOT 연산
        /// </summary>
        public virtual PyBool Not()
        {
            return PyBool.FromBool(!IsTrue());
        }

        /// <summary>
        /// 행렬 곱셈 연산 (__matmul__) - Python 3.5+
        /// </summary>
        public virtual PyObject MatrixMultiply(PyObject other)
        {
            return PyNotImplemented.Instance;
        }

        #endregion

        #region In-Place Operations (CPython 3.12: Objects/abstract.c)

        // CPython 3.12: In-place operations return result or null if not supported
        // Returning null signals to fall back to regular binary operation

        /// <summary>In-place addition (__iadd__)</summary>
        public virtual PyObject? InplaceAdd(PyObject other) => null;

        /// <summary>In-place subtraction (__isub__)</summary>
        public virtual PyObject? InplaceSubtract(PyObject other) => null;

        /// <summary>In-place multiplication (__imul__)</summary>
        public virtual PyObject? InplaceMultiply(PyObject other) => null;

        /// <summary>In-place true division (__itruediv__)</summary>
        public virtual PyObject? InplaceDivide(PyObject other) => null;

        /// <summary>In-place floor division (__ifloordiv__)</summary>
        public virtual PyObject? InplaceFloorDivide(PyObject other) => null;

        /// <summary>In-place modulo (__imod__)</summary>
        public virtual PyObject? InplaceModulo(PyObject other) => null;

        /// <summary>In-place power (__ipow__)</summary>
        public virtual PyObject? InplacePower(PyObject other) => null;

        /// <summary>In-place left shift (__ilshift__)</summary>
        public virtual PyObject? InplaceLeftShift(PyObject other) => null;

        /// <summary>In-place right shift (__irshift__)</summary>
        public virtual PyObject? InplaceRightShift(PyObject other) => null;

        /// <summary>In-place bitwise AND (__iand__)</summary>
        public virtual PyObject? InplaceBitwiseAnd(PyObject other) => null;

        /// <summary>In-place bitwise OR (__ior__)</summary>
        public virtual PyObject? InplaceBitwiseOr(PyObject other) => null;

        /// <summary>In-place bitwise XOR (__ixor__)</summary>
        public virtual PyObject? InplaceBitwiseXor(PyObject other) => null;

        /// <summary>In-place matrix multiplication (__imatmul__)</summary>
        public virtual PyObject? InplaceMatrixMultiply(PyObject other) => null;

        #endregion

        #region Iterator Protocol

        // Shared by Python instances and class objects (metaclass slots).
        internal PyObject InvokeIteratorMethod(PyObject method)
        {
            PyObject iterator;
            // Python functions have fixed binding: avoid a temporary PyMethod.
            if (method is PyFunction function)
                iterator = function.Call(new PyObject[] { this }, null);
            else
            {
                PyObject bound;
                try { bound = BindSpecialMethod(method); }
                catch (PythonException)
                {
                    // slot_tp_iter clears lookup errors before trying __getitem__.
                    // Errors from calling the resolved __iter__ still propagate.
                    try
                    {
                        var getitem = LookupSpecialMethodUncached("__getitem__");
                        if (getitem != null)
                        {
                            BindSpecialMethod(getitem);
                            return new PyGenericIterator(this);
                        }
                    }
                    catch (PythonException) { }
                    throw PyTypeError.Create($"'{GetPyType().Name}' object is not iterable");
                }
                if (bound is PyNone) throw PyTypeError.Create($"'{GetPyType().Name}' object is not iterable");
                iterator = bound.Call(Array.Empty<PyObject>(), null);
            }
            return iterator;
        }

        internal PyObject InvokeNextMethod()
        {
            var method = LookupSpecialMethodUncached("__next__");
            if (method == null)
                throw PyTypeError.Create($"'{GetPyType().Name}' object is not an iterator");
            if (method is PyFunction function)
                return function.Call(new PyObject[] { this }, null);
            return BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
        }

        /// <summary>
        /// PyIter_Check (Objects/abstract.c): tp_iternext is set and is not
        /// _PyObject_NextNotImplemented. A builtin iterator's slot is its CLR function; any other
        /// type has one when __next__ is found in its MRO, as update_one_slot decides.
        /// </summary>
        internal bool IsIterator => (this is PyIterator && GetPyType() is not SharpPy.PyClass) || HasTypeSlot("__next__");

        /// <summary>
        /// The tp_iter function of this object's CLR implementation: what a builtin type puts in its
        /// slot, and what its __iter__ slot wrapper calls. Without one, tp_iter is NULL.
        /// </summary>
        protected internal virtual PyObject Iter()
        {
            throw PyTypeError.Create($"'{GetTypeName()}' object is not iterable");
        }

        /// <summary>
        /// PyObject_GetIter (Objects/abstract.c): Py_TYPE(o)->tp_iter(o), and what it answers must
        /// be an iterator. A class written in Python has the slot update_one_slot gave it.
        /// </summary>
        public PyObject GetIterator()
        {
            var iterator = GetPyType() is SharpPy.PyClass heap ? heap.IterSlot(this) : Iter();
            if (!iterator.IsIterator)
                throw PyTypeError.Create($"iter() returned non-iterator of type '{iterator.GetPyType().FullName}'");
            return iterator;
        }

        /// <summary>
        /// The tp_iternext function of this object's CLR implementation: what a builtin type puts
        /// in its slot, and what its __next__ slot wrapper calls (wrap_next).
        /// </summary>
        protected internal virtual PyObject IterNext()
        {
            throw PyStopIteration.Create();
        }

        /// <summary>
        /// Py_TYPE(o)->tp_iternext(o). A builtin type's slot is its own function; a class written in
        /// Python has the slot update_one_slot gave it (Objects/typeobject.c).
        /// </summary>
        public PyObject Next() => GetPyType() is SharpPy.PyClass heap ? heap.IterNextSlot(this) : IterNext();

        /// <summary>
        /// PyObject_GetIter, then PyIter_Next until it is exhausted: the items of an iterable, read
        /// lazily. What the iterator raises, other than the StopIteration that ends it, propagates.
        /// </summary>
        internal static IEnumerable<PyObject> IterateItems(PyObject iterable)
        {
            var iterator = iterable.GetIterator();
            while (true)
            {
                PyObject item;
                try { item = iterator.Next(); }
                catch (PythonException error) when (error.PyException is PyStopIteration) { yield break; }
                yield return item;
            }
        }

        #endregion

        #region Buffer Protocol (PEP 688)

        /// <summary>
        /// PEP 688: __buffer__ method
        /// Returns a memoryview object representing this buffer
        /// </summary>
        public virtual PyMemoryView GetBuffer(int flags)
        {
            throw PyTypeError.Create($"a bytes-like object is required, not '{GetTypeName()}'");
        }

        /// <summary>
        /// PEP 688: __release_buffer__ method (optional)
        /// Releases resources associated with the buffer
        /// </summary>
        public virtual void ReleaseBuffer(PyMemoryView buffer)
        {
            // Default implementation - no cleanup needed for most objects
        }

        /// <summary>
        /// Check if this object supports the buffer protocol
        /// </summary>
        public virtual bool SupportsBuffer()
        {
            try
            {
                GetBuffer(0);
                return true;
            }
            catch (PythonException pe) when (pe.PyException is PyTypeError)
            {
                return false;
            }
        }

        #endregion

        #region Container Protocol

        /// <summary>
        /// 요소 포함 여부 확인 (__contains__)
        /// CPython 3.12: PySequence_Contains (Objects/abstract.c:2260-2297)
        /// </summary>
        /// <summary>
        /// The sq_contains function of this object's CLR implementation. A builtin's slot may be
        /// only the function of the wrapper in its dict; without one, _PySequence_IterSearch.
        /// </summary>
        protected internal virtual PyBool ContainsItem(PyObject item)
        {
            switch (GetPyType().FindNameInMro("__contains__"))
            {
                case PyWrapperDescriptor { CallsVirtual: false } wrapper:
                    return PyBool.FromBool(wrapper._implementation(this, new[] { item }, null).IsTrue());
                case PyMethodDescriptor method:
                    return PyBool.FromBool(method._implementation(this, new[] { item }, null).IsTrue());
            }
            return IterSearchContains(this, item);
        }

        /// <summary>
        /// PySequence_Contains (Objects/abstract.c): the type's sq_contains, else iteration; a class
        /// written in Python has the slot update_one_slot gave it.
        /// </summary>
        public PyBool Contains(PyObject item) =>
            GetPyType() is SharpPy.PyClass heap ? heap.ContainsSlot(this, item) : ContainsItem(item);

        /// <summary>_PySequence_IterSearch with PY_ITERSEARCH_CONTAINS.</summary>
        internal static PyBool IterSearchContains(PyObject seq, PyObject item)
        {
            PyObject iterator;
            try
            {
                iterator = seq.GetIterator();
            }
            catch (PythonException ex) when (ex.PyException is PyTypeError)
            {
                throw PyTypeError.Create($"argument of type '{seq.GetPyType().FullName}' is not iterable");
            }
            while (true)
            {
                PyObject current;
                try { current = iterator.Next(); }
                catch (PythonException ex) when (ex.PyException is PyStopIteration) { return PyBool.False; }
                if (CompareBool(current, item, CompareOp.EQ))
                    return PyBool.True;
            }
        }

        /// <summary>
        /// 인덱스로 요소 가져오기 (__getitem__)
        /// </summary>
        /// <summary>
        /// The mp_subscript function of this object's CLR implementation; a builtin's slot may be
        /// only the function of the wrapper in its dict.
        /// </summary>
        protected internal virtual PyObject MpSubscript(PyObject key)
        {
            switch (GetPyType().FindNameInMro("__getitem__"))
            {
                case PyWrapperDescriptor { CallsVirtual: false } wrapper: return wrapper._implementation(this, new[] { key }, null);
                case PyMethodDescriptor method: return method._implementation(this, new[] { key }, null);
            }
            throw PyTypeError.Create($"'{GetTypeName()}' object is not subscriptable");
        }

        /// <summary>
        /// PyObject_GetItem (Objects/abstract.c): the type's mp_subscript; a class written in
        /// Python has the slot update_one_slot gave it.
        /// </summary>
        public PyObject GetItem(PyObject key) =>
            GetPyType() is SharpPy.PyClass heap ? heap.SubscriptSlot(this, key) : MpSubscript(key);

        /// <summary>
        /// 인덱스로 요소 설정하기 (__setitem__)
        /// </summary>
        /// <summary>The mp_ass_subscript function of this object's CLR implementation, storing.</summary>
        protected internal virtual void MpAssSubscript(PyObject key, PyObject value)
        {
            switch (GetPyType().FindNameInMro("__setitem__"))
            {
                case PyWrapperDescriptor { CallsVirtual: false } wrapper: wrapper._implementation(this, new[] { key, value }, null); return;
                case PyMethodDescriptor method: method._implementation(this, new[] { key, value }, null); return;
            }
            throw PyTypeError.Create($"'{GetTypeName()}' object does not support item assignment");
        }

        /// <summary>PyObject_SetItem: the type's mp_ass_subscript with a value.</summary>
        public void SetItem(PyObject key, PyObject value)
        {
            if (GetPyType() is SharpPy.PyClass heap) heap.AssSubscriptSlot(this, key, value);
            else MpAssSubscript(key, value);
        }

        /// <summary>
        /// 인덱스로 요소 삭제하기 (__delitem__)
        /// </summary>
        /// <summary>The mp_ass_subscript function of this object's CLR implementation, deleting.</summary>
        protected internal virtual void MpDelSubscript(PyObject key)
        {
            switch (GetPyType().FindNameInMro("__delitem__"))
            {
                case PyWrapperDescriptor { CallsVirtual: false } wrapper: wrapper._implementation(this, new[] { key }, null); return;
                case PyMethodDescriptor method: method._implementation(this, new[] { key }, null); return;
            }
            throw PyTypeError.Create($"'{GetTypeName()}' object does not support item deletion");
        }

        /// <summary>PyObject_DelItem: the type's mp_ass_subscript without a value.</summary>
        public void DelItem(PyObject key)
        {
            if (GetPyType() is SharpPy.PyClass heap) heap.AssSubscriptSlot(this, key, null);
            else MpDelSubscript(key);
        }

        #endregion

        #region Type Checking

        public bool IsInstance<T>() where T : PyObject => this is T;
        public virtual bool IsInstance(PyType type) => GetPyType().IsSubclassOf(type);

        #endregion

        #region Descriptor Protocol Support

        /// <summary>
        /// CPython 호환: 객체가 Python 디스크립터인지 확인 (__get__, __set__, __delete__ 메서드 존재)
        /// </summary>
        // Py_TYPE(attr)->tp_descr_get: __get__ on the attribute's type, not on the attribute.
        protected virtual bool IsPythonDescriptor(PyObject obj) => obj.LookupSpecialMethodUncached("__get__") != null;

        #endregion
    }

    #region Support Classes

    public sealed class PyNotImplemented : PyObject
    {
        public static readonly PyNotImplemented Instance = new PyNotImplemented();
        private PyNotImplemented() { }

        public override PyType GetPyType() => PyType.NotImplementedType;
        protected internal override PyString Str() => PyString.From("NotImplemented");
        protected internal override PyString Repr() => PyString.From("NotImplemented");

        // NotImplemented_bool
        protected internal override bool Bool()
        {
            PyWarnings.Deprecated("NotImplemented should not be used in a boolean context");
            return true;
        }
    }

    /// <summary>
    /// Python 디스크립터 객체(__get__, __set__, __delete__ 메서드를 가진 객체)를 IDescriptor로 래핑
    /// </summary>
    public class PyDescriptorWrapper : IDescriptor
    {
        private readonly PyObject _descriptor;

        public PyDescriptorWrapper(PyObject descriptor)
        {
            _descriptor = descriptor;
        }

        // slot_tp_descr_get (Objects/typeobject.c): __get__ from the descriptor's type, called with
        // None for a missing instance or owner. What it raises propagates.
        public PyObject Get(PyObject instance, PyType owner)
        {
            var get = _descriptor.LookupSpecialMethodUncached("__get__");
            if (get == null)
                return _descriptor;
            return _descriptor.BindSpecialMethod(get).Call(new PyObject[]
            {
                instance ?? PyNone.Instance, (PyObject)owner ?? PyNone.Instance,
            }, null);
        }

        // slot_tp_descr_set: __set__, or __delete__ for a deletion, from the descriptor's type.
        public void Set(PyObject instance, PyObject value) => CallSlot("__set__", instance, value);

        public void Delete(PyObject instance) => CallSlot("__delete__", instance, null);

        private void CallSlot(string name, PyObject instance, PyObject value)
        {
            var method = _descriptor.LookupSpecialMethodUncached(name)
                ?? throw PyAttributeError.Create($"'{_descriptor.GetTypeName()}' object has no attribute '{name}'");
            var bound = _descriptor.BindSpecialMethod(method);
            bound.Call(value == null ? new[] { instance } : new[] { instance, value }, null);
        }

        // PyDescr_IsData: tp_descr_set, which a type has for __set__ or __delete__.
        public bool IsDataDescriptor() =>
            _descriptor.LookupSpecialMethodUncached("__set__") != null || _descriptor.LookupSpecialMethodUncached("__delete__") != null;

    }

    #endregion
}
