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
            if (!HasCustomType)
            {
                if (GetType() == typeof(PyFloat)) return new PyString(PyFloat.FormatFloat(((PyFloat)this).Value, spec.Value));
                if (GetType() == typeof(PyInt)) return new PyString(PyInt.FormatInt(((PyInt)this).Value, spec.Value));
                if (GetType() == typeof(PyString)) return spec.Value.Length == 0 ? (PyString)this
                    : new PyString(PyString.FormatString(((PyString)this).Value, spec.Value));
            }
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

        // CPython 3.12: Include/object.h:350 - Py_TYPE(op) macro
        // Returns ob_type if set, otherwise returns default type
        public virtual PyType GetPyType() => _customType ?? PyType.ObjectType;

        public virtual string GetTypeName() => GetPyType().Name;
        public virtual PyType PyClass => GetPyType();

        /// <summary>
        /// Set custom type for this object (used by subclasses of builtin types)
        /// CPython 3.12: This corresponds to setting ob_type field
        /// </summary>
        public void SetCustomType(PyType customType)
        {
            _customType = customType;
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
            return result as PyString ?? PyNativeStorage.Unwrap(result) as PyString ??
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
            if (GetType() == typeof(PyString))
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
            return result as PyString ?? PyNativeStorage.Unwrap(result) as PyString ??
                throw PyTypeError.Create($"__str__ returned non-string (type {result.GetPyType().FullName})");
        }

        // CPython-internal 호환 이름. **비가상**이므로 서브클래스는 반드시 AsString()을
        // override 해야 하고(누락은 컴파일 오류로 드러난다), ToStr/AsString 이중 경로가
        // 생기지 않는다.  신규 코드는 AsString()을 호출한다.
        public PyString ToStr() => AsString();

        // ❌ 절대 금지: ToString() → AsString() 위임
        // ToString()은 CLR 디버깅 전용 identity(<type object at 0x…>)다.
        public override string ToString() => $"<{GetTypeName()} object at 0x{GetHashCode():x}>";

        #endregion

        #region Hash Protocol

        public virtual int ToHash()
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        public override int GetHashCode() => ToHash();

        internal virtual long GetPythonHash()
        {
            if (_customType == null)
            {
                if (this is PyInt number) return PyInt.PythonHash(number.Value);
                if (this is PyBool boolean) return boolean.Value ? 1 : 0;
                if (this is PyFloat real) return real.PythonHash();
                if (this is PyNone) return ToHash();
            }
            // unicode_hash and bytes_hash, unless the class says otherwise.
            if (this is PyString text && (text.GetType() == typeof(PyString) || LookupUserSpecial("__hash__") == null))
                return PyHash.Text(text.Value);
            if (this is PyStrSubclass derived && LookupUserSpecial("__hash__") == null)
                return PyHash.Text(derived.GetStrValue().Value);
            if (this is PyBytes bytes && (bytes.GetType() == typeof(PyBytes) || LookupUserSpecial("__hash__") == null))
                return PyHash.Bytes(bytes.Value);
            var method = LookupSpecialMethodUncached("__hash__");
            if (method != null)
            {
                if (method == PyNone.Instance)
                    throw PyTypeError.Create($"unhashable type: '{GetTypeName()}'");
                PyObject bound;
                try { bound = BindSpecialMethod(method); }
                catch (PythonException) { throw PyTypeError.Create($"unhashable type: '{GetTypeName()}'"); }
                var result = bound.Call(Array.Empty<PyObject>(), null);
                var integer = result as PyInt ?? (result as PyIntSubclass)?.GetIntValue();
                System.Numerics.BigInteger value;
                if (integer != null) value = integer.Value;
                else if (result is PyBool flag) value = flag.Value ? 1 : 0;
                else throw PyTypeError.Create("__hash__ method should return an integer");
                long hash = value < nint.MinValue || value > nint.MaxValue ? PyInt.PythonHash(value) : (long)value;
                return hash == -1 ? -2 : hash;
            }
            if (this is PyTuple tuple)
            {
                // CPython tuplehash, native-width xxHash mixing.
                if (IntPtr.Size == 8)
                {
                    ulong acc = 2870177450012600261UL;
                    foreach (var item in tuple.Items)
                    {
                        acc = unchecked(acc + (ulong)item.GetPythonHash() * 14029467366897019727UL);
                        acc = (acc << 31) | (acc >> 33);
                        acc = unchecked(acc * 11400714785074694791UL);
                    }
                    acc = unchecked(acc + ((ulong)tuple.Items.Length ^ (2870177450012600261UL ^ 3527539UL)));
                    return acc == ulong.MaxValue ? 1546275796 : unchecked((long)acc);
                }
                uint small = 374761393U;
                foreach (var item in tuple.Items)
                {
                    small = unchecked(small + (uint)item.GetPythonHash() * 2246822519U);
                    small = (small << 13) | (small >> 19);
                    small = unchecked(small * 2654435761U);
                }
                small = unchecked(small + ((uint)tuple.Items.Length ^ (374761393U ^ 3527539U)));
                return small == uint.MaxValue ? 1546275796 : unchecked((int)small);
            }
            return ToHash();
        }

        #endregion

        #region Index Protocol

        /// <summary>
        /// Convert an object to an integer index by calling __index__().
        /// CPython 3.12: Objects/abstract.c:1324-1355 (PyNumber_Index)
        /// </summary>
        /// <param name="obj">The object to convert</param>
        /// <returns>PyInt if successful, null if __index__ not found</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        /// <summary>PyNumber_Check: complex has neither __int__ nor __float__.</summary>
        internal static bool NumberCheck(PyObject value) =>
            value is PyInt or PyBool or PyIntSubclass or PyFloat or PyComplex ||
            value.HasTypeSlot("__index__") ||
            value.HasTypeSlot("__int__") ||
            value.HasTypeSlot("__float__");

        public static PyInt? TryGetIndex(PyObject obj)
        {
            // Fast path for int and bool
            if (obj is PyInt pyInt)
                return pyInt;
            if (obj is PyBool pyBool)
                return pyBool.Value ? SmallIntCache.One : SmallIntCache.Zero;
            if (obj is PyIntSubclass subclass)
                return subclass.GetIntValue();
            return TryGetIndexSlow(obj);
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
            if (result is PyInt intResult) return intResult;
            // _PyNumber_Index: a strict subclass of int still answers, with a warning.
            if (result is PyBool or PyIntSubclass)
            {
                PyWarnings.Deprecated($"__index__ returned non-int (type {result.GetTypeName()}).  The ability to return an instance of a strict subclass of int is deprecated, and may be removed in a future version of Python.");
                return result is PyBool truth ? new PyInt(truth.Value ? 1 : 0) : ((PyIntSubclass)result).GetIntValue();
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
        public static int GetIndex(PyObject obj, string typeName)
        {
            var index = TryGetIndex(obj)
                ?? throw PyTypeError.Create($"{typeName} indices must be integers or slices, not {obj.GetTypeName()}");
            var value = index.Value;
            if (value < long.MinValue || value > long.MaxValue)
                throw PyIndexError.Create($"cannot fit '{obj.GetTypeName()}' into an index-sized integer");
            return value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;
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

        public virtual PyObject RichCompare(PyObject other, CompareOp op)
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
            if (left._customType == null && right._customType == null && IsExactNumber(right))
            {
                if (IsExactNumber(left)) return left.RichCompare(right, op);
                if (left.GetType() == typeof(PyIntSubclass))
                {
                    // The builtin right operand cannot be a strict subtype of
                    // left. Its numeric slot also accepts the integer payload
                    // if the Python override declines, so no MRO pass is needed.
                    var result = left.RichCompare(right, op);
                    return result == PyNotImplemented.Instance
                        ? right.RichCompare(left, ReverseComparison(op)) : result;
                }
            }
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
            bool reverseFirst = false;
            // A builtin number cannot be a strict subtype of a Python int subclass.
            // Still call both slots below: the left callback may return NotImplemented.
            if (!(left.GetType() == typeof(PyIntSubclass) && left._customType == null &&
                  right._customType == null && IsExactNumber(right)))
            {
                var leftType = left.GetPyType();
                var rightType = right.GetPyType();
                reverseFirst = !ReferenceEquals(leftType, rightType) && rightType.MRO.Contains(leftType);
            }
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

        // A null result means no method was found, not Python NotImplemented.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal PyObject TryCompareSpecial(PyObject other, CompareOp op, PyType nativeBase = null)
        {
            if ((uint)op >= 6) ThrowInvalidComparison(op);
            var type = GetPyType();
            var method = type is PyClass cls
                ? cls.LookupComparisonMethod(op, nativeBase)
                : FindComparisonMethod(type, op, nativeBase);
            return method == null ? null : CallComparisonMethod(method, other);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ThrowInvalidComparison(CompareOp op)
        {
            throw PyValueError.Create($"Invalid comparison operator: {op}");
        }

        private PyObject CallComparisonMethod(PyObject method, PyObject other)
        {
            // Exact Python functions bind by prepending self. Avoid two temporary objects.
            if (method.GetType() == typeof(PyFunction))
                return method.Call(new PyObject[] { this, other }, null);
            PyObject bound;
            try { bound = BindSpecialMethod(method); }
            catch (PythonException) { return PyNotImplemented.Instance; }
            // CPython slot_tp_richcompare clears binding errors, but not call errors.
            return bound.Call(new PyObject[] { other }, null);
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
            var key = new PyString(name);
            return kwargs.ContainsKey(key) ? kwargs.GetItem(key) : null;
        }

        internal static PyObject FindComparisonMethod(PyType owner, CompareOp op, PyType nativeBase)
        {
            string name = op switch
            {
                CompareOp.LT => "__lt__", CompareOp.LE => "__le__",
                CompareOp.EQ => "__eq__", CompareOp.NE => "__ne__",
                CompareOp.GT => "__gt__", CompareOp.GE => "__ge__",
                _ => throw PyValueError.Create($"Invalid comparison operator: {op}")
            };
            PyObject method = null;
            foreach (var type in owner.MRO)
            {
                // The native slot wins over mixins later in the MRO.
                if (ReferenceEquals(type, nativeBase)) return null;
                if (type is PyClass cls)
                {
                    // ClassDict is the class's own namespace; copied native slots
                    // in TypeDict must not shadow later MRO entries.
                    if (cls.ClassDict.TryGetValue(name, out method)) break;
                }
                else if (type.TypeDict.TryGetValue(name, out method)) break;
            }
            return method;
        }

        #endregion

        #region Truth Value Protocol

        public virtual bool PyBoolValue()
        {
            return true; // 기본값: 모든 객체는 truthy
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
        public virtual bool IsTrue()
        {
            if (this is PyBool boolean) return boolean.Value;
            if (this == PyNone.Instance) return false;
            // NotImplemented_bool
            if (ReferenceEquals(this, PyNotImplemented.Instance))
            {
                PyWarnings.Deprecated("NotImplemented should not be used in a boolean context");
                return true;
            }
            var method = LookupSpecialMethodUncached("__bool__");
            if (method != null)
            {
                var result = BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
                if (result is PyBool truth) return truth.Value;
                throw PyTypeError.Create($"__bool__ should return bool, returned {result.GetTypeName()}");
            }
            method = LookupSpecialMethodUncached("__len__");
            if (method != null)
            {
                var result = BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
                var length = TryGetIndex(result);
                if (length == null)
                    throw PyTypeError.Create($"'{result.GetTypeName()}' object cannot be interpreted as an integer");
                if (length.Value.Sign < 0) throw PyValueError.Create("__len__() should return >= 0");
                if (length.Value > nint.MaxValue)
                    throw PyOverflowError.Create($"cannot fit '{result.GetTypeName()}' into an index-sized integer");
                return !length.Value.IsZero;
            }
            return PyBoolValue();
        }

        #endregion

        #region Attribute Access Protocol (MRO-based with Descriptor Support)

        public virtual PyObject GetAttribute(string name)
        {
            // CPython 3.12: For type-level access (accessing attributes ON a type),
            // always use descriptor protocol via PyGetAttribute
            if (this is PyType || this is PyClass)
            {
                return PyGetAttribute(name);
            }

            // A Python subclass of a builtin with no storage of its own for attributes.
            if (_customType is PyClass heap && this is not IDictBackedInstance)
                return SubclassInstanceSupport.GetAttribute(this, heap, name);

            // An instance dict kept beside a native value (IDictBackedInstance) is its __dict__.
            if (name == "__dict__" && this is IDictBackedInstance dictAccessor && (GetPyType() is not PyClass heapOwner || heapOwner.InstancesHaveDict()))
                return PyGlobalsDict.OfInstance(dictAccessor.InstanceDict);
            return PyGetAttribute(name);
        }

        /// <summary>
        /// PyObject_GenericGetAttr for this object, which object.__getattribute__ is: tp_getattro
        /// without the __getattribute__/__getattr__ hook of a Python class. A builtin's own
        /// GetAttribute is both.
        /// </summary>
        internal virtual PyObject GenericGetAttr(string name)
        {
            if (_customType is PyClass heap && this is not IDictBackedInstance)
                return SubclassInstanceSupport.GenericGetAttribute(this, heap, HeapAttributes.GetOrCreateValue(this), name, PyGetAttribute);
            return GetAttribute(name);
        }

        public virtual void SetAttribute(string name, PyObject value)
        {
            if (name == "__class__")
                throw PyTypeError.Create(value is PyType ? "__class__ assignment only supported for mutable types or ModuleType subclasses" : $"__class__ must be set to a class, not '{value.GetTypeName()}' object");

            if (_customType is PyClass heap && this is not IDictBackedInstance)
                SubclassInstanceSupport.SetAttribute(this, heap, HeapAttributes.GetOrCreateValue(this), name, value);
            else
                PySetAttribute(name, value);
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PyObject, Dictionary<string, PyObject>> HeapAttributes = new();

        public virtual void DelAttribute(string name)
        {
            if (this is IDictBackedInstance owner && PyGlobalsDict.Delete(owner.InstanceDict, name)) return;
            PyDelAttribute(name);
        }

        // 내부 attribute 처리 메서드들 (MRO 기반)
        protected virtual PyObject PyGetAttribute(string name)
        {
            // CPython 3.12 호환: GenericGetAttribute 사용
            // A heap subclass of a value type keeps its instance dict beside the value; __dict__ is the live view.
            if (name == "__dict__" && this is IDictBackedInstance owner &&
                GetPyType() is PyClass { } heapClass && heapClass.InstancesHaveDict())
                return PyGlobalsDict.OfInstance(owner.InstanceDict);
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
        internal bool HasTypeSlot(string name) => GetPyType().FindNameInMro(name) != null;

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
            if (value is PyInt exact)
                return (int)exact.Value;
            if (value is PyBool asBool)
                return asBool.Value ? 1 : 0;
            if (value is PyIntSubclass intSub)
                return (int)intSub.AsInt().Value;
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
            var type = GetPyType();

            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyObject.GenericGetAttribute: looking for '{name}' on {type.Name}");
            #endif

            // CPython 3.12: GenericGetAttr does NOT check for __getattribute__!
            // __getattribute__ is handled at a higher level (tp_getattro slot).
            // GenericGetAttr is the DEFAULT implementation of tp_getattro.
            //
            // Removing the __getattribute__ check here prevents infinite recursion:
            // - PyIntSubclass.GetAttribute() calls base.GetAttribute()
            // - base.GetAttribute() calls GenericGetAttribute()
            // - GenericGetAttribute() should NOT call __getattribute__ again!
            //
            // Custom __getattribute__ methods should be handled by overriding
            // GetAttribute() at the Python class level, not here.

            // _PyType_Lookup, and tp_descr_get of what it found.
            PyObject attr = type.FindNameInMro(name);
            IDescriptor descriptor = attr as IDescriptor ?? (attr != null && IsPythonDescriptor(attr) ? new PyDescriptorWrapper(attr) : null);

            // CPython 3.12: For type-level access to METHOD descriptors,
            // pass null as the instance to get an unbound method.
            // For getset/member descriptors, pass the type itself to call the getter.
            PyObject instanceForDescriptor;
            if ((this is PyType || this is PyClass) && descriptor is PyMethodDescriptor or PyWrapperDescriptor)
            {
                instanceForDescriptor = null; // Unbound method for type-level access
            }
            else
            {
                instanceForDescriptor = this; // Normal descriptor binding
            }

            // 2. data descriptor라면 우선권
            if (descriptor != null && descriptor.IsDataDescriptor())
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → calling data descriptor.Get({instanceForDescriptor?.GetType().Name ?? "null"}, {type}) for '{name}'");
                #endif
                return descriptor.Get(instanceForDescriptor, type);
            }

            // 3. instance dictionary 확인
            // CPython 3.12: Objects/object.c:1255-1259 - check instance __dict__
            if (this is IInstanceDictAccessor dictAccessor && dictAccessor.TryGetInstanceAttribute(name, out PyObject value))
            {
                return value;
            }
            if (this is IObjectDict holder && holder.ObjectDict(false) is { } ownDict
                && ownDict.TryGetStoredValue(new PyString(name), out var held))
            {
                return held;
            }

            // 4. non-data descriptor 또는 일반 attribute
            if (descriptor != null)
            {
                return descriptor.Get(instanceForDescriptor, type);
            }

            if (attr != null)
            {
                // 함수는 method로 바인딩
                if (attr is PyFunction func && this is PyClassInstance)
                {
                    return new PyMethod(this, func);
                }
                return attr;
            }

            // 5. __getattr__ 호출 (있다면)
            if (HasCustomGetAttr())
            {
                return CallGetAttr(name);
            }

            // object.__repr__ and object.__str__ are answered by the type, see PyType.GetAttribute.
            if (name is "__repr__" or "__str__" && this is not PyType && PyType.ObjectType.GetAttribute(name) is IDescriptor objectSlot)
                return objectSlot.Get(this, GetPyType());
            return PySlotWrappers.TryBind(this, name) ?? throw AttributeNotFound(name);
        }

        // CPython distinguishes attribute errors on a class object
        // (type_getattro: "type object 'X' has no attribute 'Y'") from those on
        // an instance ("'X' object has no attribute 'Y'"). PyType overrides this.
        protected virtual System.Exception AttributeNotFound(string name)
            => PyAttributeError.CreateFor(this, name, $"'{GetTypeName()}' object has no attribute '{name}'");

        protected virtual void PySetAttribute(string name, PyObject value)
        {
            // CPython type_setattro rejects ALL writes on a static (non-heap) type
            // BEFORE descriptor lookup: TypeError "cannot set '<n>' attribute of
            // immutable type '<T>'". Native builtins are non-heap; PyClass is heap.
            if (this is PyType nativeType && !(nativeType is PyClass))
            {
                // One narrow exception, measured: CPython's io.UnsupportedOperation is built as a
                // heap type, so Lib/io.py may end with UnsupportedOperation.__module__ = "io",
                // while the same write on ValueError, OSError, _io._IOBase or _io.StringIO is
                // rejected. type_set_module stores the value in the type dict, which is what this
                // does for the opted-in types (core/PyType.AllowsModuleAssignment).
                if (nativeType.AllowsModuleAssignment && name == "__module__")
                {
                    nativeType.TypeDict["__module__"] = value;
                    return;
                }
                throw PyTypeError.Create($"cannot set '{name}' attribute of immutable type '{nativeType.Name}'");
            }

            var type = GetPyType();

            // 1. CPython resolves the name with _PyType_Lookup, i.e. the MERGED type dict: a user class
            //    frame and a native type frame are the same tp_dict there. The loop used to look only at
            //    PyClass frames, so a getset published in float's TypeDict was invisible to assignment
            //    and setattr(2.5, "real", 1) answered "'float' object has no attribute 'real'" instead of
            //    the getset's own "attribute 'real' of 'float' objects is not writable".
            PyObject resolved = type.FindNameInMro(name);

            if (resolved is IDescriptor descriptor && descriptor.IsDataDescriptor())
            {
                descriptor.Set(this, value);
                return;
            }

            // 2. instance dictionary에 저장
            if (this is PyClassInstance instance)
            {
                instance.SetInstanceAttribute(name, value);
            }
            else if (this is PyClass customType)
            {
                customType.ClassDict[name] = value;
                customType.InvalidateTypeCache();
            }
            else if (this is IObjectDict holder)
            {
                holder.ObjectDict(true).SetItem(new PyString(name), value);
            }
            else
            {
                // No instance dict to write into. CPython splits this by whether the name resolved at all
                // (Objects/object.c:1589-1599): something without a setter is read-only, nothing is
                // "has no attribute".
                if (resolved != null)
                    throw PyAttributeError.Create(
                        $"'{GetTypeName()}' object attribute '{name}' is read-only");
                throw PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '{name}'");
            }
        }

        /// <summary>
        /// CPython PyObject_GenericSetAttr 호환: 속성 삭제 (value=NULL로 SetAttr 호출)
        /// Descriptor 프로토콜 지원 (data descriptor의 Delete 메서드 호출)
        /// </summary>
        protected virtual void PyDelAttribute(string name)
        {
            // CPython 3.12 compatible: PyObject_GenericSetAttr with value=NULL
            // References: Objects/object.c _PyObject_GenericSetAttrWithDict()
            // Static (non-heap) types reject every deletion: TypeError immutable.
            if (this is PyType nativeType && !(nativeType is PyClass))
                throw PyTypeError.Create($"cannot set '{name}' attribute of immutable type '{nativeType.Name}'");

            // Objects/typeobject.c:5712-5719: __class__ is a getset whose setter rejects deletion with
            // TypeError before any MRO search, so delattr(obj, "__class__") is
            // "can't delete __class__ attribute" for every object whose type is writable.
            if (name == "__class__")
                throw PyTypeError.Create("can't delete __class__ attribute");

            var type = GetPyType();

            #if DEBUG_LOG
            Console.WriteLine($"🔍 PyDelAttribute: deleting '{name}' from {GetTypeName()}");
            Console.WriteLine($"   → searching class MRO for descriptors");
            #endif

            // 1. _PyType_Lookup
            PyObject attr = type.FindNameInMro(name);
            IDescriptor descriptor = attr as IDescriptor;

            // 2. If data descriptor found with Delete capability, use it (CPython: tp_descr_set)
            if (descriptor != null && descriptor.IsDataDescriptor())
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → data descriptor found, calling Delete()");
                #endif
                try
                {
                    descriptor.Delete(this);
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ descriptor.Delete() succeeded");
                    #endif
                    return;
                }
                catch (Exception ex)
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   ❌ descriptor.Delete() failed: {ex.Message}");
                    #endif
                    throw;
                }
            }

            // 3. Try to delete from instance dictionary (CPython: instance __dict__ handling)
            #if DEBUG_LOG
            Console.WriteLine($"   → no data descriptor, trying instance dict");
            #endif

            if (this is PyClassInstance instance)
            {
                if (instance.RemoveInstanceAttribute(name))
                {
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ deleted from instance dict");
                    #endif
                    return;
                }
            }
            if (this is IObjectDict holder && holder.ObjectDict(false) is { } ownDict)
            {
                var key = new PyString(name);
                if (ownDict.ContainsKey(key))
                {
                    ownDict.DelItem(key);
                    return;
                }
            }

            // 4. CPython's _PyObject_GenericSetAttrWithDict(..., value=NULL) splits at
            //    Objects/object.c:1589-1599.  When the object has no instance dict, a name that DID
            //    resolve to something without tp_descr_set reports
            //    "'int' object attribute '__add__' is read-only", while an unresolved name reports
            //    "'str' object has no attribute 'real'".  When the object DOES have an instance dict the
            //    delete falls into that dict, so even a class attribute that resolved answers
            //    "'C' object has no attribute 'cls_attr'" - measured, and the reason the dict-carrying
            //    cases are excluded from the read-only text.  (Writable-but-read-only descriptors such as
            //    int.real never get here: their own Delete slot raises, see core/PyDescriptor.cs.)
            #if DEBUG_LOG
            Console.WriteLine($"   ❌ attribute not deletable, raising AttributeError");
            #endif
            if (attr != null && !(this is PyClassInstance) && !(this is IInstanceDictAccessor) && !(this is IObjectDict))
                throw PyAttributeError.Create(
                    $"'{GetTypeName()}' object attribute '{name}' is read-only");
            throw AttributeNotFound(name);
        }

        #endregion

        protected virtual bool HasCustomGetAttr() => false;
        protected virtual PyObject CallGetAttr(string name) => null;

        // Python의 __call__ 메서드 - 모든 객체가 잠재적으로 호출 가능
        #region Call Protocol

        public virtual bool IsCallable()
        {
            // PyCallable_Check: tp_call, which an instance attribute does not fill.
            if (GetPyType() is PyClass type)
                return type.LookupInMRO("__call__") != null;
            try
            {
                GetAttribute("__call__");
                return true;
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return false;
            }
        }

        // CPython 3.12 호환: kwargs 지원 버전
        /// <summary>Py_EnterRecursiveCall for native recursion: what no Python frame counts.</summary>
        internal static void CheckNativeStack(string where)
        {
            if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
                throw new PythonException(new PyRecursionError("maximum recursion depth exceeded" + where));
        }

        public virtual PyObject Call(PyObject[] args, PyDict kwargs)
        {
            CheckNativeStack(" while calling a Python object");
            // slot_tp_call (Objects/typeobject.c): __call__ from the type, never the instance.
            // lookup_maybe_method: a function is called with self prepended, anything else is
            // bound through its __get__ (a staticmethod drops self). What the call raises propagates.
            if (GetPyType() is PyClass heap)
            {
                var declared = heap.FindNameInMro("__call__");
                if (declared != null)
                {
                    if (declared is PyFunction function)
                    {
                        var withSelf = new PyObject[args.Length + 1];
                        withSelf[0] = this;
                        Array.Copy(args, 0, withSelf, 1, args.Length);
                        return function.Call(withSelf, kwargs);
                    }
                    return BindSpecialMethod(declared).Call(args, kwargs);
                }
                throw PyTypeError.Create($"'{GetTypeName()}' object is not callable");
            }

            // A builtin whose __call__ is only reachable as an attribute.
            PyObject callMethod;
            try
            {
                callMethod = GetAttribute("__call__");
            }
            catch (PythonException pe) when (pe.PyException is PyAttributeError)
            {
                throw PyTypeError.Create($"'{GetTypeName()}' object is not callable");
            }
            if (callMethod is PyFunction func)
            {
                var newArgs = new PyObject[args.Length + 1];
                newArgs[0] = this;
                Array.Copy(args, 0, newArgs, 1, args.Length);
                return func.Call(newArgs, kwargs);
            }
            return callMethod.Call(args, kwargs);
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
        public virtual bool ToBool() => PyBoolValue();

        // === As* Methods: PyObject → PyObject Types (Type Conversion/Casting) ===
        
        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyInt로 변환/캐스팅
        /// </summary>
        public virtual PyInt AsInt()
        {
            return new PyInt(ToInt());
        }

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyFloat로 변환/캐스팅
        /// </summary>
        public virtual PyFloat AsFloat()
        {
            return new PyFloat(ToFloat());
        }

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
            return new PyTuple(items.ToArray());
        }

        // CPython PyObject_LengthHint: TypeError from len/call is a fallback,
        // PyObject_Size: __len__ must give a non-negative index-sized int.
        internal long SequenceLength()
        {
            if (this is PyRange range)
                return range.PythonLength();
            var method = LookupUserSpecial("__len__");
            if (method == null)
                return Length();
            var result = CallSpecial(method);
            var length = TryGetIndex(result) ??
                throw PyTypeError.Create($"'{result.GetTypeName()}' object cannot be interpreted as an integer");
            if (length.Value.Sign < 0)
                throw PyValueError.Create("__len__() should return >= 0");
            if (length.Value > long.MaxValue)
                throw PyOverflowError.Create($"cannot fit '{result.GetTypeName()}' into an index-sized integer");
            return (long)length.Value;
        }

        // while invalid hint return values and other exceptions propagate.
        internal long LengthHint(long defaultValue)
        {
            if (this is PyRange range) return range.PythonLength();
            // _PyObject_HasLen: no iterator has one, and asking would raise for each list(iterator).
            if (this is not PyIterator)
            try
            {
                var lengthMethod = LookupSpecialMethodUncached("__len__");
                if (lengthMethod == null) return Length();
                var result = BindSpecialMethod(lengthMethod).Call(Array.Empty<PyObject>(), null);
                var length = TryGetIndex(result);
                if (length == null)
                    throw PyTypeError.Create($"'{result.GetTypeName()}' object cannot be interpreted as an integer");
                if (length.Value.Sign < 0)
                    throw PyValueError.Create("__len__() should return >= 0");
                try { return (long)nint.CreateChecked(length.Value); }
                catch (OverflowException)
                {
                    throw PyOverflowError.Create($"cannot fit '{result.GetTypeName()}' into an index-sized integer");
                }
            }
            catch (PythonException ex) when (ex.PyException is PyTypeError) { }

            var hintMethod = LookupSpecialMethodUncached("__length_hint__");
            if (hintMethod == null) return defaultValue;
            var boundHint = BindSpecialMethod(hintMethod);
            PyObject hint;
            try { hint = boundHint.Call(Array.Empty<PyObject>(), null); }
            catch (PythonException ex) when (ex.PyException is PyTypeError) { return defaultValue; }
            if (hint == PyNotImplemented.Instance) return defaultValue;
            if (hint is not PyInt && hint is not PyBool && hint is not PyIntSubclass)
                throw PyTypeError.Create($"__length_hint__ must be an integer, not {hint.GetTypeName()}");
            long value;
            try { value = (long)nint.CreateChecked(TryGetIndex(hint).Value); }
            catch (OverflowException)
            {
                throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
            }
            if (value < 0) throw PyValueError.Create("__length_hint__() should return >= 0");
            return value;
        }

        // PyObject_Size: sq_length or mp_length, which a class fills with slot_sq_length.
        public virtual int Length()
        {
            var lenMethod = LookupSpecialMethodUncached("__len__")
                ?? throw PyTypeError.Create($"object of type '{GetTypeName()}' has no len()");
            return ToLengthResult(BindSpecialMethod(lenMethod).Call(System.Array.Empty<PyObject>(), null));
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
        public virtual PyObject Positive()
        {
            throw PyTypeError.Create($"bad operand type for unary +: '{GetTypeName()}'");
        }

        /// <summary>
        /// 단항 마이너스 연산 (__neg__)
        /// </summary>
        public virtual PyObject Negative()
        {
            throw PyTypeError.Create($"bad operand type for unary -: '{GetTypeName()}'");
        }

        /// <summary>
        /// 비트 NOT 연산 (__invert__)
        /// </summary>
        public virtual PyObject BitwiseNot()
        {
            throw PyTypeError.Create($"bad operand type for unary ~: '{GetTypeName()}'");
        }

        /// <summary>
        /// 논리 NOT 연산
        /// </summary>
        public virtual PyBool Not()
        {
            return PyBool.FromBool(!PyBoolValue());
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
        public virtual PyBool Contains(PyObject item)
        {
            // CPython 3.12: Try __contains__ first (Objects/abstract.c:2266-2278)
            try
            {
                var containsMethod = PyGetAttribute("__contains__");
                if (containsMethod != null && containsMethod != PyNone.Instance)
                {
                    var result = containsMethod.Call(new PyObject[] { item }, null);
                    return PyBool.FromBool(result.PyBoolValue());
                }
            }
            catch (PythonException ex) when (ex.PyException is PyAttributeError)
            {
                // __contains__ not found, try iteration fallback
            }

            // CPython 3.12: PySequence_Contains (Objects/abstract.c:2279-2297).
            // Only a missing iteration protocol yields "argument of type ... is not
            // iterable"; exceptions raised while iterating propagate unchanged.
            PyObject iter;
            try
            {
                iter = GetIterator();
            }
            catch (PythonException ex) when (ex.PyException is PyTypeError)
            {
                throw PyTypeError.Create($"argument of type '{GetTypeName()}' is not iterable");
            }
            while (true)
            {
                try
                {
                    var current = iter.Next();
                    // CPython: Use rich comparison for equality check
                    if (CompareBool(current, item, CompareOp.EQ))
                    {
                        return PyBool.True;
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    return PyBool.False;
                }
            }
        }

        /// <summary>
        /// 인덱스로 요소 가져오기 (__getitem__)
        /// </summary>
        public virtual PyObject GetItem(PyObject index)
        {
            throw PyTypeError.Create($"'{GetTypeName()}' object is not subscriptable");
        }

        /// <summary>
        /// 인덱스로 요소 설정하기 (__setitem__)
        /// </summary>
        public virtual void SetItem(PyObject index, PyObject value)
        {
            throw PyTypeError.Create($"'{GetTypeName()}' object does not support item assignment");
        }

        /// <summary>
        /// 인덱스로 요소 삭제하기 (__delitem__)
        /// </summary>
        public virtual void DelItem(PyObject index)
        {
            throw PyTypeError.Create($"'{GetTypeName()}' object does not support item deletion");
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

        public override string GetTypeName() => "NotImplementedType";
        public override PyType GetPyType() => PyType.NotImplementedType;
        protected internal override PyString Str() => new PyString("NotImplemented");
        protected internal override PyString Repr() => new PyString("NotImplemented");
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

        private bool HasAttribute(string attrName)
        {
            try
            {
                var attr = _descriptor.GetAttribute(attrName);
                return attr != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns builtin methods for the 'object' type
        /// </summary>
        protected PyObject GetBuiltinObjectMethod(string name)
        {
            return name switch
            {
                "__init__" => new PyBuiltinFunction("__init__", args => {
                    // object.__init__() does nothing and returns None
                    // args[0] is self, which is already bound
                    return PyNone.Instance;
                }),
                "__new__" => new PyBuiltinFunction("__new__", args => {
                    // CPython 3.12: Objects/typeobject.c:5444-5515 (object_new)
                    // object.__new__() creates a new instance
                    // When called as object.__new__(SomeClass), args[0] is the class
                    // When called through descriptor protocol, first arg is already extracted

                    PyClass cls;
                    if (args.Length == 0)
                    {
                        // Called through method descriptor: object.__new__(cls) where descriptor
                        // already extracted 'cls' as self, leaving args empty
                        throw PyTypeError.Create("object.__new__() missing 1 required positional argument: 'cls'");
                    }
                    else if (args[0] is PyClass clsArg)
                    {
                        // Direct call with class as first argument
                        cls = clsArg;
                    }
                    else
                    {
                        throw PyTypeError.Create("object.__new__(X): X is not a type object");
                    }

                    // object.__new__ does not pass through PyClass.CreateInstance, so it files its own
                    // result: object.__new__(C) still owes C's __del__ (probe H).
                    var created = new PyClassInstance(cls);
                    PyFinalizer.RegisterIfDeclared(created);
                    return created;
                }),
                "__str__" => new PyBuiltinFunction("__str__", args => {
                    // object_str: the type's tp_repr, whatever __str__ the type declares.
                    if (args.Length > 0)
                    {
                        return args[0].ToRepr();
                    }
                    throw PyTypeError.Create("__str__() missing 1 required positional argument: 'self'");
                }),
                "__repr__" => new PyBuiltinFunction("__repr__", args => {
                    // object.__repr__ is object_repr, whatever the type declares.
                    if (args.Length > 0)
                    {
                        return SharpPy.PyClass.ObjectRepr(args[0]);
                    }
                    throw PyTypeError.Create("__repr__() missing 1 required positional argument: 'self'");
                }),
                _ => null
            };
        }
    }

    #endregion
}
