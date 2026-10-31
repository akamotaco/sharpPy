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
        public virtual PyString ToRepr()
        {
            // CPython 3.12: Try to call __repr__ method if it exists
            try
            {
                var reprAttr = PyGetAttribute("__repr__");
                if (reprAttr != null && reprAttr != PyNone.Instance)
                {
                    var result = reprAttr.Call(new PyObject[0], null);
                    if (result is PyString pyStr)
                    {
                        return pyStr;
                    }
                }
            }
            catch
            {
                // If __repr__ fails, fall back to default
            }

            // Default representation
            return new PyString($"<{GetTypeName()} object at 0x{GetHashCode():x}>");
        }

        // Python str() - returns PyString.  As~ = Python 타입 변환 (AsInt/AsFloat/AsBool과 통일).
        //
        // CPython PyObject_Str: __str__가 **없는 경우에만** __repr__로 폴백한다.
        // __str__ 호출 중 발생한 예외는 그대로 전파되고, 반환이 str이 아니면 TypeError다.
        public virtual PyString AsString()
        {
            PyObject strAttr;
            try
            {
                strAttr = PyGetAttribute("__str__");
            }
            catch
            {
                // 속성 조회 자체가 실패하면 없는 것으로 취급한다.
                return ToRepr();
            }

            if (strAttr == null || strAttr == PyNone.Instance)
                return ToRepr();

            var result = strAttr.Call(new PyObject[0], null);
            if (result is PyString ok)
                return ok;
            throw PyTypeError.Create($"__str__ returned non-string (type {result.GetTypeName()})");
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
                if (this is PyString || this is PyNone) return ToHash();
            }
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
            throw PyTypeError.Create($"__index__ returned non-int (type {result.GetTypeName()})");
        }

        /// <summary>
        /// Convert object to int index or throw TypeError.
        /// CPython 3.12: Used for list/tuple/string indexing
        /// </summary>
        public static int GetIndex(PyObject obj, string typeName)
        {
            var index = TryGetIndex(obj);
            if (index != null)
                return (int)index.Value;

            throw PyTypeError.Create($"{typeName} indices must be integers or slices, not {obj.GetTypeName()}");
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
            return ResolveComparison(left, right, op);
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

            // Instance-level access: handle special cases
            switch (name)
            {
                case "__class__":
                    return PyClass;
                case "__dict__":
                    // CPython 3.12: Objects/object.c:4200-4250 (PyObject_GenericGetDict)
                    if (this is IInstanceDictAccessor dictAccessor)
                        return new PyDict(dictAccessor.InstanceDict);
                    // CPython 3.12: Non-instance objects should delegate to type's __dict__ descriptor
                    // For objects like None, functions, etc., __dict__ access should go through the type
                    return PyGetAttribute(name);
                case "__str__":
                case "__repr__":
                {
                    // CPython 3.12: __str__/__repr__ resolve through the type's MRO and bind
                    // via the descriptor protocol. The previous code returned an *unbound*
                    // PyBuiltinMethod, so x.__str__() / x.__repr__() raised
                    // "missing required 'self' argument" for every exact builtin type.
                    var selfType = GetPyType();
                    foreach (var mroType in selfType.MRO)
                    {
                        // ClassDict (user-defined, this MRO frame) wins before any
                        // inherited/merged TypeDict descriptor view.
                        if (mroType is PyClass userClass &&
                            userClass.ClassDict.TryGetValue(name, out var classAttr))
                        {
                            // User-defined: bind directly (CPython method object).
                            if (classAttr is PyFunction userFunc)
                                return new PyMethod(this, userFunc);
                            if (classAttr.IsCallable())
                                return PyGetAttribute(name);
                        }
                        if (mroType.TypeDict != null &&
                            mroType.TypeDict.TryGetValue(name, out var typeAttr))
                        {
                            if (typeAttr is IDescriptor descriptor)
                                return descriptor.Get(this, selfType);
                            if (typeAttr.IsCallable())
                                return PyGetAttribute(name);
                        }
                    }
                    // Fall back to object-level behavior, bound to this instance.
                    string dunder = name;
                    var fallback = new PyBuiltinMethod(dunder, (self, args) => {
                        if (args.Length != 0) // self is separate
                            throw PyTypeError.Create($"{dunder}() takes no arguments ({args.Length} given)");
                        return dunder == "__str__" ? self.ToStr() : self.ToRepr();
                    }, 1);
                    return fallback.Get(this, selfType);
                }
                case "__init__":
                {
                    // CPython 3.12: __init__ resolves through the MRO like __str__/__repr__;
                    // the previous hardcoded unbound PyBuiltinMethod misreported its type
                    // and skipped type-level overrides.
                    var initType = GetPyType();
                    foreach (var mroType in initType.MRO)
                    {
                        if (mroType is PyClass initClass &&
                            initClass.ClassDict.TryGetValue("__init__", out var initFunc))
                        {
                            if (initFunc is PyFunction userInit)
                                return new PyMethod(this, userInit);
                            if (initFunc.IsCallable())
                                return PyGetAttribute(name);
                        }
                        if (mroType.TypeDict != null &&
                            mroType.TypeDict.TryGetValue("__init__", out var initAttr))
                        {
                            if (initAttr is IDescriptor initDesc)
                                return initDesc.Get(this, initType);
                            if (initAttr.IsCallable())
                                return PyGetAttribute(name);
                        }
                    }
                    var initFallback = new PyBuiltinMethod("__init__", (self, args) => PyNone.Instance, 1);
                    return initFallback.Get(this, initType);
                }
                default:
                    return PyGetAttribute(name);
            }
        }

        public virtual void SetAttribute(string name, PyObject value)
        {
            if (name == "__class__")
                throw PyAttributeError.Create($"can't set attribute '{name}'");

            PySetAttribute(name, value);
        }

        public virtual void DelAttribute(string name)
        {
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
        /// Lookup special method bypassing __getattribute__
        /// CPython: Special method lookup uses _PyType_Lookup which bypasses tp_getattro
        /// Reference: Objects/typeobject.c:2188 (lookup_maybe_method)
        /// </summary>
        protected PyObject LookupSpecialMethod(string name)
        {
            var type = GetPyType();

            // For PyClass instances, use LookupInMRO
            if (type is PyClass pyClass)
            {
                return pyClass.LookupInMRO(name);
            }

            // For PyType instances, search TypeDict
            foreach (var mroType in type.MRO)
            {
                if (mroType.TypeDict != null && mroType.TypeDict.TryGetValue(name, out PyObject value))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// Type-only special method lookup without invoking instance attribute hooks.
        /// CPython v3.12.15, Objects/typeobject.c: lookup_maybe_method / slot_nb_inplace_*.
        /// Deliberately uncached: the global cache does not yet invalidate descendants.
        /// </summary>
        internal PyObject LookupSpecialMethodUncached(string name)
        {
            foreach (var type in GetPyType().MRO)
            {
                if (type is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out var classValue))
                    return classValue;
                if (type.TypeDict != null && type.TypeDict.TryGetValue(name, out var typeValue))
                    return typeValue;
            }
            return null;
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
            foreach (var mroType in GetPyType().MRO)
            {
                if (mroType is PyClass userClass &&
                    userClass.ClassDict.TryGetValue(name, out var declared))
                    return declared;
                if (!(mroType is PyClass))
                    break;
            }
            return null;
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

            // 1. 타입의 MRO에서 descriptor 찾기
            IDescriptor descriptor = null;
            PyObject attr = null;

            #if DEBUG_LOG
            Console.WriteLine($"   → checking MRO ({type.MRO.Count} types):");
            #endif
            foreach (var mroType in type.MRO)
            {
                #if DEBUG_LOG
                Console.WriteLine($"     - checking {mroType.Name}");
                #endif

                // CPython 3.12: a single MRO frame resolves in that type's own namespace first.
                // SharpPy models one frame as ClassDict (the class's own namespace) plus TypeDict
                // (the inherited / native merged slot view), so ClassDict has to win. TypeDict used
                // to be consulted first, which let an inherited builtin slot wrapper shadow a user
                // definition declared in the same frame: as soon as int published __index__, the
                // user's IO.__index__ stopped being reachable through i.__index__().
                if (mroType.TypeDict != null &&
                    !(mroType is PyClass ownFrame && ownFrame.ClassDict.ContainsKey(name)) &&
                    mroType.TypeDict.TryGetValue(name, out var typeAttr))
                {
                    attr = typeAttr;

                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ found '{name}' in {mroType.Name} TypeDict: {typeAttr?.GetType().Name}");
                    #endif
                    if (attr is IDescriptor desc)
                    {
                        descriptor = desc;
                        #if DEBUG_LOG
                        Console.WriteLine($"   🔧 '{name}' is a descriptor: {desc.GetType().Name}");
                        #endif
                    }
                    // CPython 호환: __get__, __set__, __delete__ 메서드가 있는 객체는 디스크립터로 취급
                    else if (IsPythonDescriptor(attr))
                    {
                        descriptor = new PyDescriptorWrapper(attr);
                        #if DEBUG_LOG
                        Console.WriteLine($"   🔧 '{name}' is a Python descriptor: {attr.GetType().Name}");
                        #endif
                    }
                    break;
                }

                // 사용자 정의 클래스의 ClassDict 확인
                if (mroType is PyClass customType && customType.ClassDict.ContainsKey(name))
                {
                    attr = customType.ClassDict[name];
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ found '{name}' in {mroType.Name}: {attr?.GetType().Name}");
                    #endif
                    if (attr is IDescriptor desc)
                    {
                        descriptor = desc;
                        #if DEBUG_LOG
                        Console.WriteLine($"   🔧 '{name}' is a descriptor: {desc.GetType().Name}");
                        #endif
                    }
                    // CPython 호환: __get__, __set__, __delete__ 메서드가 있는 객체는 디스크립터로 취급
                    else if (IsPythonDescriptor(attr))
                    {
                        descriptor = new PyDescriptorWrapper(attr);
                        #if DEBUG_LOG
                        Console.WriteLine($"   🔧 '{name}' is a Python descriptor: {attr.GetType().Name}");
                        #endif
                    }
                    break;
                }
            }

            // CPython 3.12: For type-level access to METHOD descriptors,
            // pass null as the instance to get an unbound method.
            // For getset/member descriptors, pass the type itself to call the getter.
            PyObject instanceForDescriptor;
            if ((this is PyType || this is PyClass) && descriptor is PyMethodDescriptor)
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
            if (this is IInstanceDictAccessor dictAccessor && dictAccessor.InstanceDict.TryGetValue(name, out PyObject value))
            {
                return value;
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

            throw AttributeNotFound(name);
        }

        // CPython distinguishes attribute errors on a class object
        // (type_getattro: "type object 'X' has no attribute 'Y'") from those on
        // an instance ("'X' object has no attribute 'Y'"). PyType overrides this.
        protected virtual System.Exception AttributeNotFound(string name)
            => PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '{name}'");

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
            PyObject resolved = null;
            foreach (var mroType in type.MRO)
            {
                if (mroType is PyClass frameClass && frameClass.ClassDict.TryGetValue(name, out var classAttr))
                {
                    resolved = classAttr;
                    break;
                }
                if (mroType.TypeDict.TryGetValue(name, out var typeAttr))
                {
                    resolved = typeAttr;
                    break;
                }
            }

            if (resolved is IDescriptor descriptor && descriptor.IsDataDescriptor())
            {
                descriptor.Set(this, value);
                return;
            }

            // 2. instance dictionary에 저장
            if (this is PyClassInstance instance)
            {
                instance.InstanceDict[name] = value;
            }
            else if (this is PyClass customType)
            {
                customType.ClassDict[name] = value;
                customType.InvalidateTypeCache();
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

            // 1. Look for descriptor in class MRO (CPython: _PyType_Lookup)
            IDescriptor descriptor = null;
            PyObject attr = null;

            foreach (var mroType in type.MRO)
            {
                #if DEBUG_LOG
                Console.WriteLine($"   → checking {mroType.Name}");
                #endif

                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out attr))
                {
                    descriptor = attr as IDescriptor;
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ found in PyClass.ClassDict: {attr.GetTypeName()}, IsDescriptor={descriptor != null}");
                    #endif
                    break;
                }
                else if (mroType.TypeDict.TryGetValue(name, out attr))
                {
                    descriptor = attr as IDescriptor;
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ found in TypeDict: {attr.GetTypeName()}, IsDescriptor={descriptor != null}");
                    #endif
                    break;
                }
            }

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
                if (instance.InstanceDict.ContainsKey(name))
                {
                    instance.InstanceDict.Remove(name);
                    #if DEBUG_LOG
                    Console.WriteLine($"   ✅ deleted from instance dict");
                    #endif
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
            if (attr != null && !(this is PyClassInstance) && !(this is IInstanceDictAccessor))
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
            try
            {
                GetAttribute("__call__");
                return true;
            }
            catch (PythonException pe)
            {
                PyAttributeError pae = (PyAttributeError)pe.PyException;
                return false;
            }
        }

        // CPython 3.12 호환: kwargs 지원 버전
        public virtual PyObject Call(PyObject[] args, PyDict kwargs)
        {
            try
            {
                var callMethod = GetAttribute("__call__");
                if (callMethod is PyMethod method)
                    return method.Call(args, kwargs);
                else if (callMethod is PyFunction func)
                {
                    // unbound function이면 self를 첫 번째 인자로 추가
                    var newArgs = new PyObject[args.Length + 1];
                    newArgs[0] = this;
                    Array.Copy(args, 0, newArgs, 1, args.Length);
                    return func.Call(newArgs, kwargs);
                }
                else if (callMethod.IsCallable())
                    return callMethod.Call(args, kwargs);
            }
            catch (PythonException pe) when (pe.PyException is PyAttributeError)
            {
                // If __call__ attribute doesn't exist, fall through to TypeError below
            }

            throw PyTypeError.Create($"'{GetTypeName()}' object is not callable");
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
            // CPython list() 생성자 동작 모방
            if (this is PyList list)
                return new PyList(list.Items.ToList()); // 복사본 생성
            
            // 이터러블 객체인 경우 변환
            var items = new List<PyObject>();
            // CPython: errors raised while consuming an iterator propagate; only
            // the missing-__iter__ case reports "not iterable" (from GetIterator).
            var iter = GetIterator();
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
            return new PyList(items);
        }

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyDict로 변환/캐스팅
        /// CPython 3.12: Objects/dictobject.c:2637-2672 - dict_init
        /// </summary>
        public virtual PyDict AsDict()
        {
            if (this is PyDict dict)
            {
                // PyDict 복사본 생성 (CPython dict() 생성자 동작)
                var newDict = new PyDict();
                var items = dict.Items(); // PyList of PyTuple pairs
                foreach (PyTuple pair in items.Items)
                {
                    newDict.SetItem(pair.Items[0], pair.Items[1]);
                }
                return newDict;
            }

            // CPython 3.12: dict() can accept an iterable of (key, value) pairs
            // Objects/dictobject.c:2649-2672
            try
            {
                var newDict = new PyDict();
                var iter = GetIterator();

                while (true)
                {
                    PyObject item;
                    try
                    {
                        item = iter.Next();
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        break;
                    }

                    // Each item should be a (key, value) pair
                    if (item is not PyTuple itemPair || itemPair.Items.Length != 2)
                    {
                        throw PyTypeError.Create($"dictionary update sequence element must be a sequence of length 2");
                    }

                    newDict.SetItem(itemPair.Items[0], itemPair.Items[1]);
                }

                return newDict;
            }
            catch (PythonException ex) when (ex.PyException is PyTypeError te && te.Message.Contains("not iterable"))
            {
                throw PyTypeError.Create($"cannot convert '{GetTypeName()}' to dict");
            }
        }

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyTuple로 변환/캐스팅
        /// </summary>
        public virtual PyTuple AsTuple()
        {
            // CPython tuple() 생성자 동작 모방
            if (this is PyTuple tuple)
                return tuple; // 튜플은 immutable이므로 동일 객체 반환
            
            // 이터러블 객체인 경우 변환
            var items = new List<PyObject>();
            // CPython: errors raised while consuming an iterator propagate.
            var iter = GetIterator();
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
        // while invalid hint return values and other exceptions propagate.
        internal long LengthHint(long defaultValue)
        {
            if (this is PyRange range) return range.PythonLength();
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

        public virtual int Length()
        {
            try
            {
                // CPython: Special method lookup bypasses __getattribute__
                // Uses _PyType_Lookup directly
                var lenMethod = LookupSpecialMethod("__len__");
                if (lenMethod == null)
                {
                    throw PyTypeError.Create($"object of type '{GetTypeName()}' has no len()");
                }

                // Apply descriptor protocol if needed
                PyObject boundMethod = lenMethod;
                if (lenMethod is IDescriptor desc)
                {
                    boundMethod = desc.Get(this, GetPyType());
                }
                else if (lenMethod is PyFunction func)
                {
                    boundMethod = new PyMethod(this, func);
                }

                var result = boundMethod.Call(new PyObject[] {  }, null);
                if (result is PyInt pyInt)
                    return (int)pyInt.Value;
                throw PyTypeError.Create("__len__ should return an integer");
            }
            catch (PythonException pe) when (pe.PyException is PyAttributeError)
            {
                throw PyTypeError.Create($"object of type '{GetTypeName()}' has no len()");
            }
        }

        #endregion

        #region Arithmetic Operations

        /// <summary>
        /// 더하기 연산 (__add__)
        /// </summary>
        public virtual PyObject Add(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for +: '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 빼기 연산 (__sub__)
        /// </summary>
        public virtual PyObject Subtract(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for -: '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 곱하기 연산 (__mul__)
        /// </summary>
        public virtual PyObject Multiply(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for *: '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 나누기 연산 (__truediv__)
        /// </summary>
        public virtual PyObject Divide(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for /: '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 정수 나누기 연산 (__floordiv__)
        /// </summary>
        public virtual PyObject FloorDivide(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for //: '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 나머지 연산 (__mod__)
        /// </summary>
        public virtual PyObject Modulo(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for %: '{GetTypeName()}' and '{other.GetTypeName()}'");
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
            throw PyTypeError.Create($"unsupported operand type(s) for ** or pow(): '{GetTypeName()}' and '{other.GetTypeName()}'");
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
            if (!iterator.IsIterator)
                throw PyTypeError.Create($"iter() returned non-iterator of type '{iterator.GetPyType().Name}'");
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

        // Equivalent to a non-null tp_iternext slot; do not invoke descriptors.
        internal virtual bool IsIterator => false;

        /// <summary>
        /// 이터레이터 객체 반환 (__iter__)
        /// </summary>
        public virtual PyObject GetIterator()
        {
            throw PyTypeError.Create($"'{GetTypeName()}' object is not iterable");
        }

        /// <summary>
        /// 다음 이터레이션 값 반환 (__next__)
        /// </summary>
        public virtual PyObject Next()
        {
            throw PyStopIteration.Create();
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
        protected virtual bool IsPythonDescriptor(PyObject obj)
        {
            try
            {
                var hasGet = HasAttribute(obj, "__get__");
                return hasGet;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 객체가 특정 속성을 가지고 있는지 확인
        /// </summary>
        private bool HasAttribute(PyObject obj, string attrName)
        {
            try
            {
                var attr = obj.GetAttribute(attrName);
                return attr != null;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }

    #region Support Classes

    public sealed class PyNotImplemented : PyObject
    {
        public static readonly PyNotImplemented Instance = new PyNotImplemented();
        private PyNotImplemented() { }

        public override string GetTypeName() => "NotImplementedType";
        public override PyString AsString() => new PyString("NotImplemented");
        public override PyString ToRepr() => new PyString("NotImplemented");
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

        public PyObject Get(PyObject instance, PyType owner)
        {
            try
            {
                var getMethod = _descriptor.GetAttribute("__get__");
                if (getMethod != null && getMethod.IsCallable())
                {
                    return getMethod.Call(new PyObject[] { instance, owner }, null);
                }
                return _descriptor;
            }
            catch
            {
                return _descriptor;
            }
        }

        public void Set(PyObject instance, PyObject value)
        {
            try
            {
                var setMethod = _descriptor.GetAttribute("__set__");
                if (setMethod != null && setMethod.IsCallable())
                {
                    setMethod.Call(new PyObject[] { instance, value }, null);
                    return;
                }
            }
            catch
            {
                // Fall through to AttributeError
            }
            throw PyAttributeError.Create($"'{_descriptor.GetTypeName()}' object has no attribute '__set__'");
        }

        public void Delete(PyObject instance)
        {
            try
            {
                var delMethod = _descriptor.GetAttribute("__delete__");
                if (delMethod != null && delMethod.IsCallable())
                {
                    delMethod.Call(new PyObject[] { instance }, null);
                    return;
                }
            }
            catch
            {
                // Fall through to AttributeError
            }
            throw PyAttributeError.Create($"'{_descriptor.GetTypeName()}' object has no attribute '__delete__'");
        }

        public bool IsDataDescriptor()
        {
            try
            {
                var hasSet = HasAttribute("__set__");
                var hasDelete = HasAttribute("__delete__");
                return hasSet || hasDelete;
            }
            catch
            {
                return false;
            }
        }

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
                    // object.__str__() delegates to __repr__
                    if (args.Length > 0)
                    {
                        return new PyString(args[0].ToStr().Value);
                    }
                    throw PyTypeError.Create("__str__() missing 1 required positional argument: 'self'");
                }),
                "__repr__" => new PyBuiltinFunction("__repr__", args => {
                    // object.__repr__() returns default representation
                    if (args.Length > 0)
                    {
                        return new PyString(args[0].ToRepr().Value);
                    }
                    throw PyTypeError.Create("__repr__() missing 1 required positional argument: 'self'");
                }),
                _ => null
            };
        }
    }

    #endregion
}
