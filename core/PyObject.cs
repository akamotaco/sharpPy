namespace SharpPy
{
    /// <summary>
    /// Python object의 핵심 구현 - 데모 동작에 필요한 기능 포함
    /// </summary>
    public abstract class PyObject
    {
        #region Core Identity

        public virtual PyType GetPyType() => PyType.ObjectType;
        public virtual string GetTypeName() => this.GetType().Name;
        public virtual PyType PyClass => GetPyType();

        protected virtual int GetDefaultHash()
        {
            return GetTypeName().GetHashCode();
        }

        #endregion

        #region String Representation

        public virtual string ToRepr()
        {
            // CPython 3.12: Try to call __repr__ method if it exists
            try
            {
                var reprAttr = PyGetAttribute("__repr__");
                if (reprAttr != null && reprAttr != PyNone.Instance)
                {
                    // Call __repr__() method
                    var result = reprAttr.Call(new PyObject[0], null);
                    if (result is PyString pyStr)
                    {
                        return pyStr.Value;
                    }
                }
            }
            catch
            {
                // If __repr__ fails, fall back to default
            }

            // Default representation
            return $"<{GetTypeName()} object at 0x{GetHashCode():x}>";
        }

        public virtual string ToStr()
        {
            // CPython 3.12: Try to call __str__ method if it exists
            try
            {
                var strAttr = PyGetAttribute("__str__");
                if (strAttr != null && strAttr != PyNone.Instance)
                {
                    // Call __str__() method
                    var result = strAttr.Call(new PyObject[0], null);
                    if (result is PyString pyStr)
                    {
                        return pyStr.Value;
                    }
                }
            }
            catch
            {
                // If __str__ fails, fall back to __repr__
            }

            return ToRepr();
        }

        public override string ToString() => ToStr();

        #endregion

        #region Hash Protocol

        public virtual int ToHash()
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        public override int GetHashCode() => ToHash();

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

        #endregion

        #region Attribute Access Protocol (MRO-based with Descriptor Support)

        public virtual PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "__class__":
                    return PyClass;
                case "__dict__":
                    if (this is PyClassInstance instance)
                        return new PyDict(instance.InstanceDict);
                    return PyNone.Instance;
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
            var type = GetPyType();
            Console.WriteLine($"🔍 PyObject.PyGetAttribute: looking for '{name}' on {type.Name}");

            // 1. 타입의 MRO에서 descriptor 찾기
            IDescriptor descriptor = null;
            PyObject attr = null;

            Console.WriteLine($"   → checking MRO ({type.MRO.Count} types):");
            foreach (var mroType in type.MRO)
            {
                Console.WriteLine($"     - checking {mroType.Name}");
                if (mroType is PyClass customType && customType.ClassDict.ContainsKey(name))
                {
                    attr = customType.ClassDict[name];
                    Console.WriteLine($"   ✅ found '{name}' in {mroType.Name}: {attr?.GetType().Name}");
                    if (attr is IDescriptor desc)
                    {
                        descriptor = desc;
                        Console.WriteLine($"   🔧 '{name}' is a descriptor: {desc.GetType().Name}");
                    }
                    // CPython 호환: __get__, __set__, __delete__ 메서드가 있는 객체는 디스크립터로 취급
                    else if (IsPythonDescriptor(attr))
                    {
                        descriptor = new PyDescriptorWrapper(attr);
                        Console.WriteLine($"   🔧 '{name}' is a Python descriptor: {attr.GetType().Name}");
                    }
                    break;
                }
                // Check for builtin object methods when checking 'object' type
                // else if (mroType.Name == "object")
                // {
                //     attr = this.GetBuiltinObjectMethod(name);
                //     if (attr != null)
                //     {
                //         Console.WriteLine($"   ✅ found builtin '{name}' in object: {attr.GetType().Name}");
                //         break;
                //     }
                // }
            }

            // 2. data descriptor라면 우선권
            if (descriptor != null && descriptor.IsDataDescriptor())
            {
                Console.WriteLine($"   → calling data descriptor.Get({this}, {type}) for '{name}'");
                return descriptor.Get(this, type);
            }

            // 3. instance dictionary 확인
            if (this is PyClassInstance instance && instance.InstanceDict.TryGetValue(name, out PyObject value))
            {
                return value;
            }

            // 4. non-data descriptor 또는 일반 attribute
            if (descriptor != null)
            {
                return descriptor.Get(this, type);
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
                instance.InstanceDict[name] = value;
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

        protected virtual void PyDelAttribute(string name)
        {
            if (this is PyClassInstance instance)
            {
                if (instance.InstanceDict.ContainsKey(name))
                {
                    instance.InstanceDict.Remove(name);
                    return;
                }
            }
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
            catch (PythonException pe)
            {
                var pae = (PyAttributeError)pe.PyException;
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
        /// CPython PyFloat_AsDouble 호환: PyObject에서 C# double 값 추출
        /// </summary>
        public virtual double ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a number, not '{GetTypeName()}'");
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
        /// CPython 호환: 현재 PyObject를 PyString으로 변환/캐스팅
        /// </summary>
        public virtual PyString AsString()
        {
            return new PyString(ToStr());
        }

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
            try
            {
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
            catch
            {
                throw PyTypeError.Create($"'{GetTypeName()}' object is not iterable");
            }
        }

        /// <summary>
        /// CPython 호환: 현재 PyObject를 PyDict로 변환/캐스팅
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
            
            throw PyTypeError.Create($"cannot convert '{GetTypeName()}' to dict");
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
            try
            {
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
            catch
            {
                throw PyTypeError.Create($"'{GetTypeName()}' object is not iterable");
            }
        }

        public virtual int Length()
        {
            try
            {
                var lenMethod = GetAttribute("__len__");
                var result = lenMethod.Call(new PyObject[] {  }, null);
                if (result is PyInt pyInt)
                    return (int)pyInt.Value;
                throw PyTypeError.Create("__len__ should return an integer");
            }
            catch (PythonException pe)
            {
                var pae = (PyAttributeError)pe.PyException;
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
        /// </summary>
        public virtual PyBool Contains(PyObject item)
        {
            throw PyTypeError.Create($"argument of type '{GetTypeName()}' is not iterable");
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

        #endregion

        #region Type Checking

        public bool IsInstance<T>() where T : PyObject => this is T;
        public bool IsInstance(PyType type) => GetPyType().IsSubclassOf(type);

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
        public override string ToStr() => "NotImplemented";
        public override string ToRepr() => "NotImplemented";
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
                    // object.__new__() creates a new instance
                    if (args.Length > 0 && args[0] is PyClass cls)
                    {
                        return new PyClassInstance(cls);
                    }
                    throw PyTypeError.Create("object.__new__() missing 1 required positional argument: 'cls'");
                }),
                "__str__" => new PyBuiltinFunction("__str__", args => {
                    // object.__str__() delegates to __repr__
                    if (args.Length > 0)
                    {
                        return new PyString(args[0].ToStr());
                    }
                    throw PyTypeError.Create("__str__() missing 1 required positional argument: 'self'");
                }),
                "__repr__" => new PyBuiltinFunction("__repr__", args => {
                    // object.__repr__() returns default representation
                    if (args.Length > 0)
                    {
                        return new PyString(args[0].ToRepr());
                    }
                    throw PyTypeError.Create("__repr__() missing 1 required positional argument: 'self'");
                }),
                _ => null
            };
        }
    }

    #endregion
}