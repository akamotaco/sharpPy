namespace SharpPy
{
    /// <summary>
    /// Python object의 핵심 구현 - 데모 동작에 필요한 기능 포함
    /// CPython 3.12: All PyObjects have ob_type field (Include/object.h:164-195)
    /// </summary>
    public abstract class PyObject
    {
        #region Core Identity

        // CPython 3.12: Include/object.h:195 - PyTypeObject *ob_type;
        // For subclasses of builtin types (IntEnum, StrEnum, etc.), this stores the actual subclass type
        // If null, GetPyType() returns the default type for this object
        protected PyType? _customType;

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

        // Python repr() - returns PyStr
        public virtual PyStr ToRepr()
        {
            // CPython 3.12: Try to call __repr__ method if it exists
            try
            {
                var reprAttr = PyGetAttribute("__repr__");
                if (reprAttr != null && reprAttr != PyNone.Instance)
                {
                    var result = reprAttr.Call(new PyObject[0], null);
                    if (result is PyStr pyStr)
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
            return new PyStr($"<{GetTypeName()} object at 0x{GetHashCode():x}>");
        }

        // Python str() - returns PyStr
        public virtual PyStr ToStr()
        {
            // CPython 3.12: Try to call __str__ method if it exists
            try
            {
                var strAttr = PyGetAttribute("__str__");
                if (strAttr != null && strAttr != PyNone.Instance)
                {
                    var result = strAttr.Call(new PyObject[0], null);
                    if (result is PyStr pyStr)
                    {
                        return pyStr;
                    }
                }
            }
            catch
            {
                // If __str__ fails, fall back to __repr__
            }

            return ToRepr();
        }

        // ❌ 절대 금지: ToString() → ToStr() 위임
        // ToString()은 디버깅 전용입니다
        public override string ToString() => $"<{GetTypeName()} object at 0x{GetHashCode():x}>";

        #endregion

        #region Hash Protocol

        public virtual int ToHash()
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        public override int GetHashCode() => ToHash();

        #endregion

        #region Index Protocol

        /// <summary>
        /// Convert an object to an integer index by calling __index__().
        /// CPython 3.12: Objects/abstract.c:1324-1355 (PyNumber_Index)
        /// </summary>
        /// <param name="obj">The object to convert</param>
        /// <returns>PyInt if successful, null if __index__ not found</returns>
        public static PyInt? TryGetIndex(PyObject obj)
        {
            // Fast path for int and bool
            if (obj is PyInt pyInt)
                return pyInt;
            if (obj is PyBool pyBool)
                return new PyInt(pyBool.IsTrue() ? 1 : 0);

            // Try __index__ method
            try
            {
                var indexMethod = obj.GetAttribute("__index__");
                if (indexMethod != null)
                {
                    PyObject result;
                    if (indexMethod is PyMethod boundMethod)
                        result = boundMethod.Call(new PyObject[0], null);
                    else if (indexMethod is PyFunction func)
                        result = func.Call(new PyObject[] { obj }, null);
                    else if (indexMethod is PyBuiltinFunction builtinFunc)
                        result = builtinFunc.Call(new PyObject[0]);
                    else
                        result = indexMethod.Call(new PyObject[0], null);

                    if (result is PyInt intResult)
                        return intResult;

                    throw PyTypeError.Create($"__index__ returned non-int (type {result.GetTypeName()})");
                }
            }
            catch
            {
                // No __index__ method or any other error
            }

            return null;
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
            return PyBool.FromBool(ReferenceEquals(this, other));
        }

        protected virtual PyObject PyNotEquals(PyObject other)
        {
            var result = PyEquals(other);
            return result is PyBool b ? PyBool.FromBool(!b.Value) : PyBool.True;
        }

        protected virtual PyObject PyLess(PyObject other)
        {
            throw PyTypeError.Create($"'<' not supported between instances of '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        protected virtual PyObject PyLessEqual(PyObject other)
        {
            throw PyTypeError.Create($"'<=' not supported between instances of '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        protected virtual PyObject PyGreater(PyObject other)
        {
            throw PyTypeError.Create($"'>' not supported between instances of '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        protected virtual PyObject PyGreaterEqual(PyObject other)
        {
            throw PyTypeError.Create($"'>=' not supported between instances of '{GetTypeName()}' and '{other.GetTypeName()}'");
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
            // CPython 3.12: Objects/object.c:1678-1683
            // Fast path for True/False/None
            if (this == PyBool.True)
                return true;
            if (this == PyBool.False)
                return false;
            if (this == PyNone.Instance)
                return false;

            // CPython 3.12: Objects/object.c:1684-1686
            // Try __bool__ method (tp_as_number->nb_bool)
            try
            {
                var boolAttr = PyGetAttribute("__bool__");
                if (boolAttr != null && boolAttr != PyNone.Instance)
                {
                    var result = boolAttr.Call(new PyObject[0], null);
                    if (result is PyBool boolResult)
                        return boolResult.Value;
                    if (result is PyInt intResult)
                        return intResult.Value != 0;
                    // Invalid __bool__ return type
                    throw PyTypeError.Create("__bool__ should return bool or int");
                }
            }
            catch (Exception ex) when (ex is PyAttributeError || ex.GetType().Name == "PyAttributeError")
            {
                // __bool__ not found, try __len__
            }

            // CPython 3.12: Objects/object.c:1687-1692
            // Try __len__ method (tp_as_mapping->mp_length or tp_as_sequence->sq_length)
            try
            {
                var lenAttr = PyGetAttribute("__len__");
                if (lenAttr != null && lenAttr != PyNone.Instance)
                {
                    var result = lenAttr.Call(new PyObject[0], null);
                    if (result is PyInt intResult)
                    {
                        // CPython 3.12: Objects/object.c:1696
                        // Return true if length > 0
                        return intResult.Value > 0;
                    }
                    // Invalid __len__ return type
                    throw PyTypeError.Create("__len__ should return an integer");
                }
            }
            catch (Exception ex) when (ex is PyAttributeError || ex.GetType().Name == "PyAttributeError")
            {
                // __len__ not found
            }

            // CPython 3.12: Objects/object.c:1694
            // Default: all objects are true
            return true;
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
                    // CPython 3.12: object.__str__ bound method
                    return new PyBuiltinMethod("__str__", (self, args) => {
                        if (args.Length != 0) // self is separate
                            throw PyTypeError.Create($"__str__() takes no arguments ({args.Length} given)");
                        return self.ToStr();
                    }, 1);
                case "__repr__":
                    // CPython 3.12: Check if type has its own __repr__ descriptor first
                    // This allows int, str, etc. to define their own __repr__
                    var type = GetPyType();
                    foreach (var mroType in type.MRO)
                    {
                        if (mroType.TypeDict != null && mroType.TypeDict.TryGetValue("__repr__", out var typeAttr))
                        {
                            // Found a type-level __repr__, use descriptor protocol
                            if (typeAttr is IDescriptor desc)
                            {
                                return desc.Get(this, type);
                            }
                            // If it's not a descriptor but callable, wrap it as a bound method
                            if (typeAttr.IsCallable())
                            {
                                // Found type's __repr__, delegate to full attribute lookup
                                return PyGetAttribute(name);
                            }
                        }
                    }
                    // Fallback to object.__repr__ bound method
                    return new PyBuiltinMethod("__repr__", (self, args) => {
                        if (args.Length != 0) // self is separate
                            throw PyTypeError.Create($"__repr__() takes no arguments ({args.Length} given)");
                        return self.ToRepr();
                    }, 1);
                case "__init__":
                    // CPython 3.12: object.__init__ bound method
                    return new PyBuiltinMethod("__init__", (self, args) => PyNone.Instance, 1);
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
            try
            {
                // Use GenericGetAttribute but catch AttributeError
                return GenericGetAttribute(name);
            }
            catch (PythonException ex) when (ex.PyException is PyAttributeError)
            {
                // CPython: AttributeError is suppressed, return null
                return null;
            }
            // Other exceptions are propagated
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

                // CPython 호환: TypeDict에서 descriptor 확인
                if (mroType.TypeDict != null && mroType.TypeDict.TryGetValue(name, out var typeAttr))
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

            throw PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '{name}'");
        }

        protected virtual void PySetAttribute(string name, PyObject value)
        {
            var type = GetPyType();

            // 1. 타입의 MRO에서 data descriptor 찾기
            foreach (var mroType in type.MRO)
            {
                if (mroType is PyClass customType && customType.ClassDict.ContainsKey(name))
                {
                    var attr = customType.ClassDict[name];
                    if (attr is IDescriptor desc && desc.IsDataDescriptor())
                    {
                        desc.Set(this, value);
                        return;
                    }
                    break;
                }
            }

            // 2. instance dictionary에 저장
            if (this is PyClassInstance instance)
            {
                instance.SetInstanceAttr(name, value);
            }
            else if (this is PyClass customType)
            {
                customType.ClassDict[name] = value;
            }
            else
            {
                throw PyAttributeError.Create($"'{GetTypeName()}' object attribute '{name}' is read-only");
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

            // 4. Attribute not found - raise AttributeError (CPython behavior)
            #if DEBUG_LOG
            Console.WriteLine($"   ❌ attribute not found anywhere, raising AttributeError");
            #endif
            throw PyAttributeError.Create($"'{GetTypeName()}' object has no attribute '{name}'");
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
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a number, not '{GetTypeName()}'");
        }

        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyObject에서 C# float (32-bit) 값 추출
        /// </summary>
        public virtual float ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a number, not '{GetTypeName()}'");
        }

        /// <summary>
        /// PyObject에서 C# double 값 추출 (64-bit)
        /// </summary>
        public virtual double ToDouble()
        {
            throw PyTypeError.Create($"double() argument must be a string or a number, not '{GetTypeName()}'");
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
        /// C# 네이티브 타입 변환: PyObject → C# string
        /// </summary>
        public virtual string AsString() => ToStr().Value;

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyList로 변환/캐스팅
        /// </summary>
        public virtual PyList AsList()
        {
            // CPython list() 생성자 동작 모방
            if (this is PyList list)
                return new PyList(list.Items.ToList()); // 복사본 생성

            // CPython: iter() 단계 실패만 "not iterable" — 반복 중 예외는 그대로 전파되어야 함.
            // (기존 try/catch 전체를 감싸면 generator 내부 예외가 TypeError 로 오염됨)
            PyObject iter;
            try
            {
                iter = GetIterator();
            }
            catch
            {
                throw PyTypeError.Create($"'{GetTypeName()}' object is not iterable");
            }

            var items = new List<PyObject>();
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
                // 다른 예외는 catch 안 함 → CPython 처럼 호출자로 전파
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

            // CPython: iter() 단계 실패만 "not iterable" — 반복 중 예외는 그대로 전파
            PyObject iter;
            try
            {
                iter = GetIterator();
            }
            catch
            {
                throw PyTypeError.Create($"'{GetTypeName()}' object is not iterable");
            }

            var items = new List<PyObject>();
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
                // 다른 예외는 catch 안 함 → 호출자로 전파
            }
            return new PyTuple(items.ToArray());
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
            throw PyTypeError.Create($"unsupported operand type(s) for divmod(): '{GetTypeName()}' and '{other.GetTypeName()}'");
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
            throw PyTypeError.Create($"unsupported operand type(s) for &: '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 비트 OR 연산 (__or__)
        /// </summary>
        public virtual PyObject BitwiseOr(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for |: '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 비트 XOR 연산 (__xor__)
        /// </summary>
        public virtual PyObject BitwiseXor(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for ^: '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 왼쪽 시프트 연산 (__lshift__)
        /// </summary>
        public virtual PyObject LeftShift(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for <<: '{GetTypeName()}' and '{other.GetTypeName()}'");
        }

        /// <summary>
        /// 오른쪽 시프트 연산 (__rshift__)
        /// </summary>
        public virtual PyObject RightShift(PyObject other)
        {
            throw PyTypeError.Create($"unsupported operand type(s) for >>: '{GetTypeName()}' and '{other.GetTypeName()}'");
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
            throw PyTypeError.Create($"unsupported operand type(s) for @: '{GetTypeName()}' and '{other.GetTypeName()}'");
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

        /// <summary>
        /// 예외 없이 다음 값을 시도하는 최적화된 메서드.
        /// FOR_ITER에서 StopIteration 예외 대신 이 메서드를 사용하여 성능 향상.
        /// 파생 클래스에서 override하면 예외 없이 O(1)로 종료 감지 가능.
        /// </summary>
        public virtual bool TryNext(out PyObject value)
        {
            try
            {
                value = Next();
                return true;
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                value = null;
                return false;
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

            // CPython 3.12: Fallback to iteration (Objects/abstract.c:2279-2291)
            try
            {
                var iterator = GetIterator();
                while (true)
                {
                    try
                    {
                        var current = iterator.Next();
                        // CPython: Use rich comparison for equality check
                        if (((PyBool)current.RichCompare(item, CompareOp.EQ)).Value)
                        {
                            return PyBool.True;
                        }
                    }
                    catch (PythonException ex) when (ex is PyStopIteration)
                    {
                        return PyBool.False;
                    }
                }
            }
            catch (PythonException)
            {
                // Not iterable either
                throw PyTypeError.Create($"argument of type '{GetTypeName()}' is not iterable");
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
        public override PyStr ToStr() => new PyStr("NotImplemented");
        public override PyStr ToRepr() => new PyStr("NotImplemented");
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

                    return new PyClassInstance(cls);
                }),
                "__str__" => new PyBuiltinFunction("__str__", args => {
                    // object.__str__() delegates to __repr__
                    if (args.Length > 0)
                    {
                        return new PyStr(args[0].ToStr().Value);
                    }
                    throw PyTypeError.Create("__str__() missing 1 required positional argument: 'self'");
                }),
                "__repr__" => new PyBuiltinFunction("__repr__", args => {
                    // object.__repr__() returns default representation
                    if (args.Length > 0)
                    {
                        return new PyStr(args[0].ToRepr().Value);
                    }
                    throw PyTypeError.Create("__repr__() missing 1 required positional argument: 'self'");
                }),
                _ => null
            };
        }
    }

    #endregion
}