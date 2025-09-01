namespace SharpPy
{
    /// <summary>
    /// Python Generic 타입 지원 (typing.Generic 기능)
    /// </summary>
    public class PyGenericType : PyType
    {
        public List<PyObject> TypeArgs { get; }
        public PyType OriginType { get; }

        public PyGenericType(string name, PyType originType, List<PyObject> typeArgs) 
            : base(name, originType.BaseTypes)
        {
            OriginType = originType;
            TypeArgs = typeArgs;
        }

        public new bool IsSubclassOf(PyType other)
        {
            // Generic[T] 타입 매칭
            if (other is PyGenericType otherGeneric)
            {
                // 기본 타입이 같고 타입 인자가 호환되는지 확인
                return OriginType.IsSubclassOf(otherGeneric.OriginType);
            }
            
            // 일반 타입과 비교 시 origin type으로 비교
            return OriginType.IsSubclassOf(other);
        }

        // Forward calls to origin type (Stack[int]() -> Stack())
        public override PyObject Call(params PyObject[] args)
        {
            return OriginType.Call(args);
        }

        public override bool IsCallable() => OriginType.IsCallable();

        public override string ToString()
        {
            if (TypeArgs.Count > 0)
            {
                var typeArgStr = string.Join(", ", TypeArgs.Select(arg => arg.ToString()));
                return $"{OriginType.Name}[{typeArgStr}]";
            }
            return OriginType.Name;
        }
    }

    /// <summary>
    /// typing 모듈의 Generic 클래스
    /// </summary>
    public class PyGeneric : PyType
    {
        public static PyGeneric Instance { get; } = new PyGeneric();

        private PyGeneric() : base("Generic", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject GetItem(PyObject key)
        {
            // Generic[T] 같은 문법 지원
            if (key is PyObject typeParam)
            {
                var typeArgs = new List<PyObject>();
                
                if (key is PyTuple tuple)
                {
                    typeArgs.AddRange(tuple.Items);
                }
                else
                {
                    typeArgs.Add(key);
                }

                return new PyGenericType($"Generic[{key}]", this, typeArgs);
            }

            return base.GetItem(key);
        }
    }

    /// <summary>
    /// List[T] 같은 제네릭 리스트 타입
    /// </summary>
    public class PyGenericList : PyGenericType
    {
        public PyGenericList(List<PyObject> typeArgs) 
            : base($"list[{string.Join(", ", typeArgs)}]", PyType.ListType, typeArgs)
        {
        }

        public static PyGenericList Create(PyObject typeArg)
        {
            return new PyGenericList(new List<PyObject> { typeArg });
        }
    }

    /// <summary>
    /// Dict[K, V] 같은 제네릭 딕셔너리 타입
    /// </summary>
    public class PyGenericDict : PyGenericType
    {
        public PyGenericDict(List<PyObject> typeArgs) 
            : base($"dict[{string.Join(", ", typeArgs)}]", PyType.DictType, typeArgs)
        {
            if (typeArgs.Count != 2)
                throw new ArgumentException("Dict requires exactly 2 type arguments");
        }

        public static PyGenericDict Create(PyObject keyType, PyObject valueType)
        {
            return new PyGenericDict(new List<PyObject> { keyType, valueType });
        }
    }
}