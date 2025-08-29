using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// functools 모듈 - 함수형 프로그래밍 도구들
    /// </summary>
    public class FunctoolsModule : PyModule
    {
        public static FunctoolsModule Instance { get; } = new FunctoolsModule();

        private FunctoolsModule() : base("functools")
        {
            // 주요 functools 함수들 등록
            AddFunction("reduce", ReduceFunction);
            AddFunction("partial", PartialFunction);
            AddFunction("wraps", WrapsFunction);
            AddFunction("lru_cache", LruCacheFunction);
            AddFunction("cached_property", CachedPropertyFunction);
            AddFunction("singledispatch", SingleDispatchFunction);
            AddFunction("cmp_to_key", CmpToKeyFunction);
        }

        /// <summary>
        /// functools.reduce(function, iterable, initial=None) - 누적 적용
        /// </summary>
        private PyObject ReduceFunction(PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"reduce expected 2 or 3 arguments ({args.Length} given)");

            var function = args[0];
            var iterable = args[1];
            var hasInitial = args.Length == 3;
            var initialValue = hasInitial ? args[2] : null;

            var iterator = iterable.GetIterator();
            PyObject result = null;

            try
            {
                if (hasInitial)
                {
                    result = initialValue;
                }
                else
                {
                    result = iterator.Next(); // 첫 번째 값을 초기값으로 사용
                }

                while (true)
                {
                    var nextValue = iterator.Next();
                    result = function.Call(result, nextValue);
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 빈 시퀀스인 경우 처리
            }
            
            if (result == null)
            {
                if (hasInitial)
                    return initialValue;
                else
                    throw PyTypeError.Create("reduce() of empty sequence with no initial value");
            }
            
            return result;
        }

        /// <summary>
        /// functools.partial(func, *args, **kwargs) - 부분 적용 함수
        /// </summary>
        private PyObject PartialFunction(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("partial expected at least 1 argument");

            var func = args[0];
            var partialArgs = args.Skip(1).ToArray();

            return new PartialFunction(func, partialArgs);
        }

        /// <summary>
        /// functools.wraps(wrapped) - 데코레이터 래퍼 (단순 구현)
        /// </summary>
        private PyObject WrapsFunction(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"wraps expected 1 argument ({args.Length} given)");

            var wrapped = args[0];
            
            // 데코레이터를 반환하는 함수를 반환
            return new PyBuiltinFunction("wraps_decorator", (decoratorArgs) =>
            {
                if (decoratorArgs.Length != 1)
                    throw PyTypeError.Create("wraps decorator expected 1 argument");

                var decorator = decoratorArgs[0];
                // 실제로는 메타데이터를 복사해야 하지만 단순화
                return decorator;
            });
        }

        /// <summary>
        /// functools.lru_cache(maxsize=128, typed=False) - LRU 캐시 데코레이터
        /// </summary>
        private PyObject LruCacheFunction(PyObject[] args)
        {
            int maxsize = 128;
            bool typed = false;

            // 매개변수 파싱 (단순화)
            if (args.Length >= 1 && args[0] is PyInt maxsizeInt)
                maxsize = (int)maxsizeInt.Value;

            // 실제 데코레이터를 반환
            return new PyBuiltinFunction("lru_cache_decorator", (funcArgs) =>
            {
                if (funcArgs.Length != 1)
                    throw PyTypeError.Create("lru_cache decorator expected 1 argument");

                var func = funcArgs[0];
                return new LruCacheWrapper(func, maxsize);
            });
        }

        /// <summary>
        /// functools.cached_property - 캐시된 속성 (단순 구현)
        /// </summary>
        private PyObject CachedPropertyFunction(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"cached_property expected 1 argument ({args.Length} given)");

            var func = args[0];
            return new CachedProperty(func);
        }

        /// <summary>
        /// functools.singledispatch(func) - 단일 디스패치 제네릭 함수
        /// </summary>
        private PyObject SingleDispatchFunction(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"singledispatch expected 1 argument ({args.Length} given)");

            var func = args[0];
            return new SingleDispatchFunction(func);
        }

        /// <summary>
        /// functools.cmp_to_key(func) - 비교 함수를 키 함수로 변환
        /// </summary>
        private PyObject CmpToKeyFunction(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"cmp_to_key expected 1 argument ({args.Length} given)");

            var cmpFunc = args[0];
            return new CmpToKeyWrapper(cmpFunc);
        }
    }

    #region Functools Implementation Classes

    /// <summary>
    /// partial 함수 구현
    /// </summary>
    public class PartialFunction : PyObject
    {
        private readonly PyObject _func;
        private readonly PyObject[] _partialArgs;

        public PartialFunction(PyObject func, PyObject[] partialArgs)
        {
            _func = func;
            _partialArgs = partialArgs;
        }

        public override PyObject Call(params PyObject[] args)
        {
            // 부분 적용된 인수와 새 인수를 결합
            var combinedArgs = _partialArgs.Concat(args).ToArray();
            return _func.Call(combinedArgs);
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "functools.partial";
        public override string ToString() => $"functools.partial({_func}, {string.Join(", ", _partialArgs.Select(arg => arg.ToString()))})";
    }

    /// <summary>
    /// LRU 캐시 래퍼 (단순 구현)
    /// </summary>
    public class LruCacheWrapper : PyObject
    {
        private readonly PyObject _func;
        private readonly int _maxsize;
        private readonly Dictionary<string, PyObject> _cache = new Dictionary<string, PyObject>();
        private readonly Queue<string> _accessOrder = new Queue<string>();

        public LruCacheWrapper(PyObject func, int maxsize)
        {
            _func = func;
            _maxsize = maxsize;
        }

        public override PyObject Call(params PyObject[] args)
        {
            // 간단한 캐시 키 생성 (실제로는 더 복잡해야 함)
            var key = string.Join(",", args.Select(arg => arg.ToString()));

            if (_cache.ContainsKey(key))
            {
                // 캐시 히트
                return _cache[key];
            }

            // 캐시 미스 - 함수 실행
            var result = _func.Call(args);

            // 캐시에 저장
            if (_cache.Count >= _maxsize)
            {
                // LRU 제거
                var oldestKey = _accessOrder.Dequeue();
                _cache.Remove(oldestKey);
            }

            _cache[key] = result;
            _accessOrder.Enqueue(key);

            return result;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "functools.lru_cache";
        public override string ToString() => $"<functools.lru_cache({_func})>";
    }

    /// <summary>
    /// cached_property 구현 (단순 버전)
    /// </summary>
    public class CachedProperty : PyObject
    {
        private readonly PyObject _func;
        private PyObject _cachedValue;
        private bool _hasCache = false;

        public CachedProperty(PyObject func)
        {
            _func = func;
        }

        public override PyObject Call(params PyObject[] args)
        {
            if (!_hasCache)
            {
                _cachedValue = _func.Call(args);
                _hasCache = true;
            }
            return _cachedValue;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "functools.cached_property";
        public override string ToString() => $"<functools.cached_property({_func})>";
    }

    /// <summary>
    /// singledispatch 함수 구현 (단순 버전)
    /// </summary>
    public class SingleDispatchFunction : PyObject
    {
        private readonly PyObject _defaultFunc;
        private readonly Dictionary<PyType, PyObject> _registry = new Dictionary<PyType, PyObject>();

        public SingleDispatchFunction(PyObject defaultFunc)
        {
            _defaultFunc = defaultFunc;
        }

        public override PyObject Call(params PyObject[] args)
        {
            if (args.Length == 0)
                return _defaultFunc.Call(args);

            var firstArgType = args[0].GetPyType();
            
            if (_registry.ContainsKey(firstArgType))
            {
                return _registry[firstArgType].Call(args);
            }

            return _defaultFunc.Call(args);
        }

        public void Register(PyType type, PyObject func)
        {
            _registry[type] = func;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "functools.singledispatch";
        public override string ToString() => $"<functools.singledispatch({_defaultFunc})>";
    }

    /// <summary>
    /// cmp_to_key 래퍼 구현
    /// </summary>
    public class CmpToKeyWrapper : PyObject
    {
        private readonly PyObject _cmpFunc;

        public CmpToKeyWrapper(PyObject cmpFunc)
        {
            _cmpFunc = cmpFunc;
        }

        public override PyObject Call(params PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("cmp_to_key function expected 1 argument");

            return new CmpToKeyObject(args[0], _cmpFunc);
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "functools.cmp_to_key";
        public override string ToString() => $"<functools.cmp_to_key({_cmpFunc})>";
    }

    /// <summary>
    /// cmp_to_key로 생성된 키 객체
    /// </summary>
    public class CmpToKeyObject : PyObject
    {
        private readonly PyObject _obj;
        private readonly PyObject _cmpFunc;

        public CmpToKeyObject(PyObject obj, PyObject cmpFunc)
        {
            _obj = obj;
            _cmpFunc = cmpFunc;
        }

        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            if (other is CmpToKeyObject otherKey)
            {
                var result = _cmpFunc.Call(_obj, otherKey._obj);
                if (result is PyInt intResult)
                {
                    var comparison = intResult.Value;
                    return op switch
                    {
                        CompareOp.LT => PyBool.FromBool(comparison < 0),
                        CompareOp.LE => PyBool.FromBool(comparison <= 0),
                        CompareOp.EQ => PyBool.FromBool(comparison == 0),
                        CompareOp.NE => PyBool.FromBool(comparison != 0),
                        CompareOp.GT => PyBool.FromBool(comparison > 0),
                        CompareOp.GE => PyBool.FromBool(comparison >= 0),
                        _ => base.RichCompare(other, op)
                    };
                }
            }
            return base.RichCompare(other, op);
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "functools.KeyWrapper";
        public override string ToString() => $"<functools.KeyWrapper({_obj})>";
    }

    #endregion
}