using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// collections 모듈 - 특화된 컨테이너 데이터 타입들
    /// </summary>
    public class CollectionsModule : PyModule
    {
        public static CollectionsModule Instance { get; } = new CollectionsModule();

        private CollectionsModule() : base("collections")
        {
            // 주요 collections 클래스들 등록
            AddClass("deque", () => new PyDequeType());
            AddClass("Counter", () => new PyCounterType());
            AddClass("defaultdict", () => new PyDefaultDictType());
            AddClass("OrderedDict", () => new PyOrderedDictType());
            AddClass("ChainMap", () => new PyChainMapType());
            AddFunction("namedtuple", NamedTupleFunction);
        }

        /// <summary>
        /// collections.namedtuple(typename, field_names) - 명명된 튜플 생성
        /// </summary>
        private PyObject NamedTupleFunction(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create($"namedtuple expected at least 2 arguments ({args.Length} given)");

            var typename = args[0].ToStr();
            var fieldNames = new List<string>();

            // 필드명 파싱 (문자열 또는 리스트)
            if (args[1] is PyString fieldStr)
            {
                // 공백으로 구분된 문자열
                fieldNames.AddRange(fieldStr.Value.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
            }
            else if (args[1] is PyList fieldList)
            {
                fieldNames.AddRange(fieldList.Items.Select(item => item.ToStr()));
            }
            else
            {
                throw PyTypeError.Create("namedtuple field_names must be a string or list");
            }

            return new PyNamedTupleType(typename, fieldNames.ToArray());
        }
    }

    #region Collections Data Types

    /// <summary>
    /// collections.deque - 양방향 큐
    /// </summary>
    public class PyDequeType : PyType
    {
        public PyDequeType() : base("deque", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject CreateInstance(params PyObject[] args)
        {
            return new PyDeque(args.Length > 0 ? args[0] : null);
        }
    }

    public class PyDeque : PyObject
    {
        private readonly LinkedList<PyObject> _items = new LinkedList<PyObject>();

        public PyDeque(PyObject iterable = null)
        {
            if (iterable != null)
            {
                var iterator = iterable.GetIterator();
                try
                {
                    while (true)
                    {
                        _items.AddLast(iterator.Next());
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration) { }
            }
        }

        public void Append(PyObject item)
        {
            _items.AddLast(item);
        }

        public void AppendLeft(PyObject item)
        {
            _items.AddFirst(item);
        }

        public PyObject Pop()
        {
            if (_items.Count == 0)
                throw PyIndexError.Create("pop from empty deque");
            
            var item = _items.Last.Value;
            _items.RemoveLast();
            return item;
        }

        public PyObject PopLeft()
        {
            if (_items.Count == 0)
                throw PyIndexError.Create("pop from empty deque");
                
            var item = _items.First.Value;
            _items.RemoveFirst();
            return item;
        }

        public override PyType GetPyType() => new PyDequeType();
        public override string GetTypeName() => "deque";
        public override string ToString() => $"deque([{string.Join(", ", _items)}])";

        public override PyIterator GetIterator()
        {
            return new DequeIterator(_items.ToList());
        }
    }

    public class DequeIterator : PyIterator
    {
        private readonly List<PyObject> _items;
        private int _index = 0;

        public DequeIterator(List<PyObject> items)
        {
            _items = items;
        }

        public override PyObject Next()
        {
            if (_index >= _items.Count)
                throw PyStopIteration.Create();

            return _items[_index++];
        }
    }

    /// <summary>
    /// collections.Counter - 카운터 딕셔너리
    /// </summary>
    public class PyCounterType : PyType
    {
        public PyCounterType() : base("Counter", new PyType[] { PyType.DictType })
        {
        }

        public override PyObject CreateInstance(params PyObject[] args)
        {
            return new PyCounter(args.Length > 0 ? args[0] : null);
        }
    }

    public class PyCounter : PyDict
    {
        public PyCounter(PyObject iterable = null) : base()
        {
            if (iterable != null)
            {
                var iterator = iterable.GetIterator();
                try
                {
                    while (true)
                    {
                        var item = iterator.Next();
                        var currentCount = GetItem(item);
                        
                        if (currentCount is PyInt intCount)
                        {
                            SetItem(item, new PyInt(intCount.Value + 1));
                        }
                        else
                        {
                            SetItem(item, new PyInt(1));
                        }
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration) { }
            }
        }

        public PyList MostCommon(int n = -1)
        {
            var sorted = InternalDict.OrderByDescending(kvp => 
            {
                if (kvp.Value is PyInt intVal) return intVal.Value;
                return 0;
            }).ToList();

            if (n > 0)
                sorted = sorted.Take(n).ToList();

            var result = sorted.Select(kvp => new PyTuple(new PyObject[] { kvp.Key, kvp.Value }))
                              .Cast<PyObject>().ToList();

            return new PyList(result.ToArray());
        }

        public override PyType GetPyType() => new PyCounterType();
        public override string GetTypeName() => "Counter";
        public override string ToString() => $"Counter({base.ToString()})";
    }

    /// <summary>
    /// collections.defaultdict - 기본값이 있는 딕셔너리
    /// </summary>
    public class PyDefaultDictType : PyType
    {
        public PyDefaultDictType() : base("defaultdict", new PyType[] { PyType.DictType })
        {
        }

        public override PyObject CreateInstance(params PyObject[] args)
        {
            var defaultFactory = args.Length > 0 ? args[0] : null;
            return new PyDefaultDict(defaultFactory);
        }
    }

    public class PyDefaultDict : PyDict
    {
        private readonly PyObject _defaultFactory;

        public PyDefaultDict(PyObject defaultFactory) : base()
        {
            _defaultFactory = defaultFactory;
        }

        public new PyObject GetItem(PyObject key)
        {
            try
            {
                return base.GetItem(key);
            }
            catch (PythonException ex) when (ex.PyException is PyKeyError)
            {
                if (_defaultFactory != null)
                {
                    var defaultValue = _defaultFactory.Call();
                    SetItem(key, defaultValue);
                    return defaultValue;
                }
                throw;
            }
        }

        public override PyType GetPyType() => new PyDefaultDictType();
        public override string GetTypeName() => "defaultdict";
        public override string ToString() => $"defaultdict({_defaultFactory}, {base.ToString()})";
    }

    /// <summary>
    /// collections.OrderedDict - 순서를 유지하는 딕셔너리 (Python 3.7+에서는 일반 dict도 순서 유지)
    /// </summary>
    public class PyOrderedDictType : PyType
    {
        public PyOrderedDictType() : base("OrderedDict", new PyType[] { PyType.DictType })
        {
        }

        public override PyObject CreateInstance(params PyObject[] args)
        {
            return new PyOrderedDict();
        }
    }

    public class PyOrderedDict : PyDict
    {
        // Python 3.7+에서는 일반 dict도 순서를 유지하므로 특별한 구현 불필요
        public PyOrderedDict() : base() { }

        public override PyType GetPyType() => new PyOrderedDictType();
        public override string GetTypeName() => "OrderedDict";
        public override string ToString() => $"OrderedDict({base.ToString()})";
    }

    /// <summary>
    /// collections.ChainMap - 여러 매핑을 체인으로 연결
    /// </summary>
    public class PyChainMapType : PyType
    {
        public PyChainMapType() : base("ChainMap", new PyType[] { PyType.ObjectType })
        {
        }

        public override PyObject CreateInstance(params PyObject[] args)
        {
            return new PyChainMap(args);
        }
    }

    public class PyChainMap : PyObject
    {
        private readonly List<PyDict> _maps;

        public PyChainMap(PyObject[] maps)
        {
            _maps = maps.Cast<PyDict>().ToList();
            if (_maps.Count == 0)
            {
                _maps.Add(new PyDict());
            }
        }

        public new PyObject GetItem(PyObject key)
        {
            foreach (var map in _maps)
            {
                try
                {
                    return map.GetItem(key);
                }
                catch (PythonException ex) when (ex.PyException is PyKeyError)
                {
                    continue;
                }
            }
            throw PyKeyError.Create(key.ToString());
        }

        public override void SetItem(PyObject key, PyObject value)
        {
            // 첫 번째 맵에만 설정
            _maps[0].SetItem(key, value);
        }

        public override PyType GetPyType() => new PyChainMapType();
        public override string GetTypeName() => "ChainMap";
        public override string ToString() => $"ChainMap({string.Join(", ", _maps)})";
    }

    /// <summary>
    /// namedtuple로 생성되는 타입
    /// </summary>
    public class PyNamedTupleType : PyType
    {
        private readonly string[] _fieldNames;

        public PyNamedTupleType(string name, string[] fieldNames) : base(name, new PyType[] { PyType.TupleType })
        {
            _fieldNames = fieldNames;
        }

        public override PyObject CreateInstance(params PyObject[] args)
        {
            return new PyNamedTuple(Name, _fieldNames, args);
        }
    }

    public class PyNamedTuple : PyTuple
    {
        private readonly string _typeName;
        private readonly string[] _fieldNames;

        public PyNamedTuple(string typeName, string[] fieldNames, PyObject[] values) : base(values)
        {
            _typeName = typeName;
            _fieldNames = fieldNames;

            if (values.Length != fieldNames.Length)
                throw PyTypeError.Create($"{typeName} takes {fieldNames.Length} arguments ({values.Length} given)");
        }

        public new PyObject GetAttr(string name)
        {
            var index = Array.IndexOf(_fieldNames, name);
            if (index >= 0)
            {
                return Items[index];
            }

            return base.GetAttribute(name);
        }

        public override PyType GetPyType() => new PyNamedTupleType(_typeName, _fieldNames);
        public override string GetTypeName() => _typeName;
        public override string ToString() => $"{_typeName}({string.Join(", ", _fieldNames.Zip(Items, (name, value) => $"{name}={value}"))})";
    }

    #endregion
}