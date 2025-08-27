using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python type 시스템 - C3 선형화 MRO 구현
    /// </summary>
    public class PyType : PyObject
    {
        #region Built-in Type Constants

        // 핵심 기본 타입들 (실제 구현된 것들만)
        public static readonly PyType ObjectType = new PyType("object", new PyType[0]);
        public static readonly PyType TypeType = new PyType("type", new[] { ObjectType });
        
        // 숫자 타입들
        public static readonly PyType IntType = new PyType("int", new[] { ObjectType });
        public static readonly PyType FloatType = new PyType("float", new[] { ObjectType });
        public static readonly PyType BoolType = new PyType("bool", new[] { IntType });
        
        // 컬렉션 타입들
        public static readonly PyType StrType = new PyType("str", new[] { ObjectType });
        public static readonly PyType ListType = new PyType("list", new[] { ObjectType });
        public static readonly PyType TupleType = new PyType("tuple", new[] { ObjectType });
        public static readonly PyType DictType = new PyType("dict", new[] { ObjectType });
        
        // 함수 타입들
        public static readonly PyType FunctionType = new PyType("function", new[] { ObjectType });
        public static readonly PyType MethodType = new PyType("method", new[] { ObjectType });
        
        // Descriptor 타입들 (데모에서 필요)
        public static readonly PyType PropertyType = new PyType("property", new[] { ObjectType });
        
        // 기타 핵심 타입들
        public static readonly PyType ModuleType = new PyType("module", new[] { ObjectType });
        public static readonly PyType NoneType = new PyType("NoneType", new[] { ObjectType });

        // 예외 타입 계층 (PyException.cs와 연동)
        public static readonly PyType BaseExceptionType = new PyType("BaseException", new[] { ObjectType });
        public static readonly PyType ExceptionType = new PyType("Exception", new[] { BaseExceptionType });
        public static readonly PyType SystemExitType = new PyType("SystemExit", new[] { BaseExceptionType });
        public static readonly PyType KeyboardInterruptType = new PyType("KeyboardInterrupt", new[] { BaseExceptionType });
        
        public static readonly PyType ArithmeticErrorType = new PyType("ArithmeticError", new[] { ExceptionType });
        public static readonly PyType ZeroDivisionErrorType = new PyType("ZeroDivisionError", new[] { ArithmeticErrorType });
        public static readonly PyType OverflowErrorType = new PyType("OverflowError", new[] { ArithmeticErrorType });
        
        public static readonly PyType LookupErrorType = new PyType("LookupError", new[] { ExceptionType });
        public static readonly PyType IndexErrorType = new PyType("IndexError", new[] { LookupErrorType });
        public static readonly PyType KeyErrorType = new PyType("KeyError", new[] { LookupErrorType });
        
        public static readonly PyType AttributeErrorType = new PyType("AttributeError", new[] { ExceptionType });
        public static readonly PyType TypeErrorType = new PyType("TypeError", new[] { ExceptionType });
        public static readonly PyType ValueErrorType = new PyType("ValueError", new[] { ExceptionType });
        public static readonly PyType NameErrorType = new PyType("NameError", new[] { ExceptionType });
        public static readonly PyType UnboundLocalErrorType = new PyType("UnboundLocalError", new[] { NameErrorType });
        
        public static readonly PyType RuntimeErrorType = new PyType("RuntimeError", new[] { ExceptionType });
        public static readonly PyType NotImplementedErrorType = new PyType("NotImplementedError", new[] { RuntimeErrorType });
        public static readonly PyType RecursionErrorType = new PyType("RecursionError", new[] { RuntimeErrorType });
        
        public static readonly PyType ImportErrorType = new PyType("ImportError", new[] { ExceptionType });
        public static readonly PyType ModuleNotFoundErrorType = new PyType("ModuleNotFoundError", new[] { ImportErrorType });
        
        public static readonly PyType SyntaxErrorType = new PyType("SyntaxError", new[] { ExceptionType });
        public static readonly PyType IndentationErrorType = new PyType("IndentationError", new[] { SyntaxErrorType });
        
        public static readonly PyType StopIterationType = new PyType("StopIteration", new[] { ExceptionType });

        #endregion

        #region Core Properties

        public string Name { get; }
        public PyType[] BaseTypes { get; }
        public List<PyType> MRO { get; private set; }

        #endregion

        #region Constructor

        public PyType(string name, PyType[] baseTypes)
        {
            Name = name;
            BaseTypes = baseTypes ?? new PyType[0];
            MRO = CalculateC3MRO();
        }

        #endregion

        #region Type Identity

        public override PyType GetPyType() => TypeType;
        public override string GetTypeName() => "type";

        #endregion

        #region C3 Linearization Algorithm

        private List<PyType> CalculateC3MRO()
        {
            if (Name == "object")
            {
                return new List<PyType> { this };
            }

            if (Name == "type")
            {
                return new List<PyType> { this, ObjectType };
            }

            try
            {
                return C3Linearize(this);
            }
            catch (Exception)
            {
                throw new ArgumentException($"Cannot create a consistent method resolution order (MRO) for class {Name}");
            }
        }

        private List<PyType> C3Linearize(PyType cls)
        {
            var result = new List<PyType> { cls };

            if (cls.BaseTypes.Length == 0)
            {
                result.Add(ObjectType);
                return result;
            }

            // 부모 클래스들의 MRO 수집
            var basesMROs = new List<List<PyType>>();
            foreach (var baseType in cls.BaseTypes)
            {
                basesMROs.Add(new List<PyType>(baseType.MRO));
            }

            // 부모 클래스 리스트도 추가
            basesMROs.Add(new List<PyType>(cls.BaseTypes));

            // C3 merge 수행
            var merged = C3Merge(basesMROs);
            result.AddRange(merged);

            return result;
        }

        private List<PyType> C3Merge(List<List<PyType>> sequences)
        {
            var result = new List<PyType>();

            while (true)
            {
                // 빈 시퀀스들 제거
                sequences = sequences.Where(seq => seq.Count > 0).ToList();

                if (sequences.Count == 0)
                    break;

                PyType candidate = null;

                // 좋은 후보 찾기 (다른 시퀀스의 tail에 없는 head)
                foreach (var seq in sequences)
                {
                    var head = seq[0];
                    var isTail = sequences.Any(s => s.Skip(1).Contains(head));

                    if (!isTail)
                    {
                        candidate = head;
                        break;
                    }
                }

                if (candidate == null)
                {
                    throw new ArgumentException("Inconsistent MRO");
                }

                result.Add(candidate);

                // 모든 시퀀스에서 candidate 제거
                foreach (var seq in sequences)
                {
                    if (seq.Count > 0 && seq[0] == candidate)
                    {
                        seq.RemoveAt(0);
                    }
                }
            }

            return result;
        }

        #endregion

        #region Type Methods

        // isinstance/issubclass 지원
        public bool IsSubclassOf(PyType other)
        {
            return MRO.Contains(other);
        }

        // type은 항상 호출 가능 (인스턴스 생성)
        public override bool IsCallable() => true;

        // type 호출 - 인스턴스 생성 또는 타입 조회
        public override PyObject Call(params PyObject[] args)
        {
            // type(obj) - 객체의 타입 반환
            if (this == TypeType && args.Length == 1)
            {
                return args[0].GetPyType();
            }
            
            // type(name, bases, dict) - 새로운 타입 생성
            if (this == TypeType && args.Length == 3)
            {
                if (args[0] is PyString name && args[1] is PyTuple bases && args[2] is PyDict classDict)
                {
                    var baseTypes = bases.Items.Cast<PyType>().ToArray();
                    // CPython 호환: PyDict의 PyObject 키를 string 키로 변환
                    var stringDict = new Dictionary<string, PyObject>();
                    foreach (var kv in classDict.InternalDict)
                    {
                        if (kv.Key is PyString keyStr)
                        {
                            stringDict[keyStr.Value] = kv.Value;
                        }
                        // 문자열이 아닌 키는 무시 (CPython과 동일한 동작)
                    }
                    return new PyClass(name.Value, baseTypes, stringDict);
                }
                throw PyTypeError.Create("type() arguments must be (name, bases, dict)");
            }
            
            // 일반 타입 호출 - 인스턴스 생성
            return CreateInstance(args);
        }

        // 인스턴스 생성 (기본 구현)
        public virtual PyObject CreateInstance(params PyObject[] args)
        {
            // 내장 타입은 직접 인스턴스 생성 불가
            throw PyTypeError.Create($"Cannot create instance of built-in type '{Name}'");
        }

        // Special method lookup (MRO 기반)
        public PyObject LookupSpecial(string name)
        {
            foreach (var mroType in MRO)
            {
                if (mroType is PyClass customType && customType.ClassDict.ContainsKey(name))
                {
                    return customType.ClassDict[name];
                }
            }
            return null;
        }

        #endregion

        #region Type Attributes

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "__name__":
                    return new PyString(Name);
                case "__bases__":
                    return new PyTuple(BaseTypes.Cast<PyObject>().ToArray());
                case "__mro__":
                    return new PyTuple(MRO.Cast<PyObject>().ToArray());
                default:
                    return base.GetAttribute(name);
            }
        }

        #endregion

        #region String Representation

        public override string ToRepr() => $"<class '{Name}'>";
        public override string ToStr() => $"<class '{Name}'>";

        #endregion

        #region Hash and Comparison

        public override bool PyBoolValue() => true;

        protected override int GetDefaultHash()
        {
            return Name.GetHashCode();
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return PyBool.FromBool(ReferenceEquals(this, other));
        }

        #endregion

        #region Debug Support

        public void PrintMRO()
        {
            Console.WriteLine($"{Name} MRO: [{string.Join(", ", MRO.Select(t => t.Name))}]");
        }

        #endregion
    }
}