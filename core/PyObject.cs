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
            return $"<{GetTypeName()} object at 0x{GetHashCode():x}>";
        }

        public virtual string ToStr()
        {
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

            // 1. 타입의 MRO에서 descriptor 찾기
            IDescriptor descriptor = null;
            PyObject attr = null;

            foreach (var mroType in type.MRO)
            {
                if (mroType is PyClass customType && customType.ClassDict.ContainsKey(name))
                {
                    attr = customType.ClassDict[name];
                    if (attr is IDescriptor desc)
                        descriptor = desc;
                    break;
                }
            }

            // 2. data descriptor라면 우선권
            if (descriptor != null && descriptor.IsDataDescriptor())
            {
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

        public virtual PyObject Call(params PyObject[] args)
        {
            try
            {
                var callMethod = GetAttribute("__call__");
                if (callMethod is PyMethod method)
                    return method.Call(args);
                else if (callMethod is PyFunction func)
                {
                    // unbound function이면 self를 첫 번째 인자로 추가
                    var newArgs = new PyObject[args.Length + 1];
                    newArgs[0] = this;
                    Array.Copy(args, 0, newArgs, 1, args.Length);
                    return func.Call(newArgs);
                }
                else if (callMethod.IsCallable())
                    return callMethod.Call(args);
            }
            catch (PythonException pe)
            {
                var pae = (PyAttributeError)pe.PyException;
            }

            throw PyTypeError.Create($"'{GetTypeName()}' object is not callable");
        }

        #endregion

        #region Basic Type Conversion

        public virtual int ToInt()
        {
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a number, not '{GetTypeName()}'");
        }

        public virtual double ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a number, not '{GetTypeName()}'");
        }

        public virtual bool ToBool() => PyBoolValue();

        public virtual int Length()
        {
            try
            {
                var lenMethod = GetAttribute("__len__");
                var result = lenMethod.Call();
                if (result is PyInt pyInt)
                    return pyInt.Value;
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

        #region Type Checking

        public bool IsInstance<T>() where T : PyObject => this is T;
        public bool IsInstance(PyType type) => GetPyType().IsSubclassOf(type);

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

    #endregion
}