using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// typing 모듈 - Python 타입 힌트 시스템
    /// </summary>
    public class TypingModule : PyModule
    {
        public static TypingModule Instance { get; } = new TypingModule();

        private TypingModule() : base("typing")
        {
            // 핵심 타입 구성자들
            AddClass("TypeVar", () => new PyTypeVarType());
            AddClass("Generic", () => PyGeneric.Instance);
            AddClass("Union", () => new PyUnionType());
            AddClass("Optional", () => new PyOptionalType());
            AddClass("Any", () => new PyAnyType());
            
            // 제네릭 컬렉션 타입들
            AddClass("List", () => new PyGenericListType());
            AddClass("Dict", () => new PyGenericDictType());
            AddClass("Tuple", () => new PyGenericTupleType());
            AddClass("Set", () => new PyGenericSetType());
            
            // 함수 타입
            AddClass("Callable", () => new PyCallableType());
            
            // 이터레이션 타입들
            AddClass("Iterator", () => new PyTypingIteratorType());
            AddClass("Iterable", () => new PyTypingIterableType());
            
            // 타입 유틸리티 함수들
            AddFunction("get_origin", GetOriginFunction);
            AddFunction("get_args", GetArgsFunction);
            AddFunction("is_generic", IsGenericFunction);
        }

        /// <summary>
        /// typing.get_origin() - 제네릭 타입의 기본 타입 반환
        /// </summary>
        private PyObject GetOriginFunction(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"get_origin expected 1 argument ({args.Length} given)");

            var obj = args[0];
            
            if (obj is PyGenericType genericType)
                return genericType.OriginType;
                
            // 제네릭이 아닌 경우 None 반환
            return PyNone.Instance;
        }

        /// <summary>
        /// typing.get_args() - 제네릭 타입의 타입 인자 반환
        /// </summary>
        private PyObject GetArgsFunction(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"get_args expected 1 argument ({args.Length} given)");

            var obj = args[0];
            
            if (obj is PyGenericType genericType && genericType.TypeArgs.Count > 0)
                return new PyTuple(genericType.TypeArgs.ToArray());
                
            // 타입 인자가 없는 경우 빈 튜플 반환
            return new PyTuple(new PyObject[0]);
        }

        /// <summary>
        /// typing.is_generic() - 제네릭 타입인지 확인
        /// </summary>
        private PyObject IsGenericFunction(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"is_generic expected 1 argument ({args.Length} given)");

            var obj = args[0];
            return PyBool.FromBool(obj is PyGenericType);
        }
    }

    #region TypeVar 구현

    /// <summary>
    /// typing.TypeVar 타입
    /// </summary>
    public class PyTypeVarType : PyType
    {
        public PyTypeVarType() : base("TypeVar", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject CreateInstance(params PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("TypeVar expected at least 1 argument");

            var name = args[0].ToStr();
            
            // bound 매개변수 추출
            PyObject bound = null;
            var constraints = new List<PyObject>();
            
            // 키워드 인자 파싱은 단순화 (실제로는 더 복잡)
            for (int i = 1; i < args.Length; i++)
            {
                constraints.Add(args[i]);
            }

            return new PyTypeVar(name, bound, constraints);
        }
    }

    /// <summary>
    /// TypeVar 인스턴스
    /// </summary>
    public class PyTypeVar : PyObject
    {
        public string Name { get; }
        public PyObject Bound { get; }
        public List<PyObject> Constraints { get; }

        public PyTypeVar(string name, PyObject bound = null, List<PyObject> constraints = null)
        {
            Name = name;
            Bound = bound;
            Constraints = constraints ?? new List<PyObject>();
        }

        /// <summary>
        /// 타입이 이 TypeVar의 제약을 만족하는지 확인
        /// </summary>
        public bool IsValidType(PyObject type)
        {
            // bound 제약 확인
            if (Bound != null)
            {
                // 실제로는 type이 bound의 서브타입인지 확인해야 함
                // 단순화된 구현
                return true;
            }
            
            // constraints 제약 확인
            if (Constraints.Count > 0)
            {
                // type이 constraints 중 하나와 일치해야 함
                return Constraints.Any(constraint => 
                    type.GetPyType().IsSubclassOf(constraint.GetPyType()));
            }
            
            // 제약이 없으면 모든 타입 허용
            return true;
        }

        public override PyType GetPyType() => new PyTypeVarType();
        public override string GetTypeName() => "TypeVar";
        public override string ToString() => $"~{Name}";
    }

    #endregion

    #region Union 구현

    /// <summary>
    /// typing.Union 타입
    /// </summary>
    public class PyUnionType : PyType
    {
        public PyUnionType() : base("Union", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject GetItem(PyObject key)
        {
            var args = new List<PyObject>();
            
            if (key is PyTuple tuple)
            {
                args.AddRange(tuple.Items);
            }
            else
            {
                args.Add(key);
            }

            return new PyUnion(args);
        }
    }

    /// <summary>
    /// Union[X, Y, ...] 타입
    /// </summary>
    public class PyUnion : PyObject
    {
        public List<PyObject> Types { get; }

        public PyUnion(List<PyObject> types)
        {
            Types = types ?? new List<PyObject>();
        }

        /// <summary>
        /// 객체가 Union의 타입 중 하나와 일치하는지 확인
        /// </summary>
        public bool IsInstance(PyObject obj)
        {
            return Types.Any(type => 
            {
                // 실제로는 더 복잡한 타입 체킹이 필요
                if (type is PyType pyType)
                    return obj.GetPyType().IsSubclassOf(pyType);
                return false;
            });
        }

        public override PyType GetPyType() => new PyUnionType();
        public override string GetTypeName() => "Union";
        public override string ToString() => $"Union[{string.Join(", ", Types)}]";
    }

    #endregion

    #region Optional 구현

    /// <summary>
    /// typing.Optional 타입 (Union[X, None]의 축약형)
    /// </summary>
    public class PyOptionalType : PyType
    {
        public PyOptionalType() : base("Optional", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject GetItem(PyObject key)
        {
            // Optional[X] = Union[X, None]
            var types = new List<PyObject> { key, PyNone.Instance.GetPyType() };
            return new PyUnion(types);
        }
    }

    #endregion

    #region Any 구현

    /// <summary>
    /// typing.Any 타입 - 모든 타입과 호환
    /// </summary>
    public class PyAnyType : PyType
    {
        public PyAnyType() : base("Any", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject CreateInstance(params PyObject[] args)
        {
            return new PyAny();
        }
    }

    /// <summary>
    /// Any 타입 인스턴스
    /// </summary>
    public class PyAny : PyObject
    {
        public override PyType GetPyType() => new PyAnyType();
        public override string GetTypeName() => "Any";
        public override string ToString() => "Any";
    }

    #endregion

    #region 제네릭 컬렉션 타입들

    /// <summary>
    /// typing.List 타입
    /// </summary>
    public class PyGenericListType : PyType
    {
        public PyGenericListType() : base("List", new PyType[] { PyType.ListType })
        {
        }

        public override PyObject GetItem(PyObject key)
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

            return new PyGenericList(typeArgs);
        }
    }

    /// <summary>
    /// typing.Dict 타입
    /// </summary>
    public class PyGenericDictType : PyType
    {
        public PyGenericDictType() : base("Dict", new PyType[] { PyType.DictType })
        {
        }

        public override PyObject GetItem(PyObject key)
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

            if (typeArgs.Count != 2)
                throw PyTypeError.Create("Dict requires exactly 2 type arguments");

            return new PyGenericDict(typeArgs);
        }
    }

    /// <summary>
    /// typing.Tuple 타입
    /// </summary>
    public class PyGenericTupleType : PyType
    {
        public PyGenericTupleType() : base("Tuple", new PyType[] { PyType.TupleType })
        {
        }

        public override PyObject GetItem(PyObject key)
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

            return new PyGenericTuple(typeArgs);
        }
    }

    /// <summary>
    /// typing.Set 타입
    /// </summary>
    public class PyGenericSetType : PyType
    {
        public PyGenericSetType() : base("Set", new PyType[] { PyType.SetType })
        {
        }

        public override PyObject GetItem(PyObject key)
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

            return new PyGenericSet(typeArgs);
        }
    }

    #endregion

    #region 제네릭 컬렉션 구현체들

    /// <summary>
    /// 제네릭 튜플 타입
    /// </summary>
    public class PyGenericTuple : PyGenericType
    {
        public PyGenericTuple(List<PyObject> typeArgs) 
            : base($"tuple[{string.Join(", ", typeArgs)}]", PyType.TupleType, typeArgs)
        {
        }
    }

    /// <summary>
    /// 제네릭 셋 타입
    /// </summary>
    public class PyGenericSet : PyGenericType
    {
        public PyGenericSet(List<PyObject> typeArgs) 
            : base($"set[{string.Join(", ", typeArgs)}]", PyType.SetType, typeArgs)
        {
        }
    }

    #endregion

    #region Callable과 Iterator 타입들

    /// <summary>
    /// typing.Callable 타입
    /// </summary>
    public class PyCallableType : PyType
    {
        public PyCallableType() : base("Callable", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject GetItem(PyObject key)
        {
            // Callable[[args...], return_type] 형태
            if (key is PyTuple keyTuple && keyTuple.Items.Length == 2)
            {
                var argsTypes = keyTuple.Items[0];
                var returnType = keyTuple.Items[1];
                return new PyCallable(argsTypes, returnType);
            }

            throw PyTypeError.Create("Callable requires [args, return_type] format");
        }
    }

    /// <summary>
    /// Callable 타입 인스턴스
    /// </summary>
    public class PyCallable : PyObject
    {
        public PyObject ArgsTypes { get; }
        public PyObject ReturnType { get; }

        public PyCallable(PyObject argsTypes, PyObject returnType)
        {
            ArgsTypes = argsTypes;
            ReturnType = returnType;
        }

        public override PyType GetPyType() => new PyCallableType();
        public override string GetTypeName() => "Callable";
        public override string ToString() => $"Callable[[{ArgsTypes}], {ReturnType}]";
    }

    /// <summary>
    /// typing.Iterator 타입
    /// </summary>
    public class PyTypingIteratorType : PyType
    {
        public PyTypingIteratorType() : base("Iterator", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject GetItem(PyObject key)
        {
            var typeArgs = new List<PyObject> { key };
            return new PyTypingIterator(typeArgs);
        }
    }

    /// <summary>
    /// typing 제네릭 이터레이터 타입
    /// </summary>
    public class PyTypingIterator : PyGenericType
    {
        public PyTypingIterator(List<PyObject> typeArgs) 
            : base($"Iterator[{string.Join(", ", typeArgs)}]", PyType.ObjectType, typeArgs)
        {
        }
    }

    /// <summary>
    /// typing.Iterable 타입
    /// </summary>
    public class PyTypingIterableType : PyType
    {
        public PyTypingIterableType() : base("Iterable", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject GetItem(PyObject key)
        {
            var typeArgs = new List<PyObject> { key };
            return new PyTypingIterable(typeArgs);
        }
    }

    /// <summary>
    /// typing 제네릭 이터러블 타입
    /// </summary>
    public class PyTypingIterable : PyGenericType
    {
        public PyTypingIterable(List<PyObject> typeArgs) 
            : base($"Iterable[{string.Join(", ", typeArgs)}]", PyType.ObjectType, typeArgs)
        {
        }
    }

    #endregion
}