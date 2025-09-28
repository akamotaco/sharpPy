using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.Modules
{
    /// <summary>
    /// Python _collections 모듈 구현 - CPython 3.12 호환
    /// C 확장 모듈을 C#으로 구현하여 고성능 컬렉션 제공
    /// </summary>
    public static class _CollectionsModule
    {
        public static PyModule CreateCollectionsModule()
        {
            var module = new PyModule("_collections", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\_collections.py");

            // CPython 3.12 _collections 모듈의 C 확장 타입들
            module.ModuleDict["deque"] = new PyDequeType();
            module.ModuleDict["defaultdict"] = new PyDefaultDictType();
            module.ModuleDict["OrderedDict"] = new PyOrderedDictType();

            return module;
        }
    }

    /// <summary>
    /// deque 타입 팩토리
    /// </summary>
    public class PyDequeType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;
        public override string GetTypeName() => "type";

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // deque([iterable[, maxlen]])
            PyObject iterable = null;
            PyObject maxlen = null;

            if (args.Length > 0) iterable = args[0];
            if (args.Length > 1) maxlen = args[1];

            return new PyDeque(iterable, maxlen);
        }

        public override bool IsCallable() => true;
    }

    /// <summary>
    /// defaultdict 타입 팩토리
    /// </summary>
    public class PyDefaultDictType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;
        public override string GetTypeName() => "type";

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // defaultdict([default_factory[, ...]])
            PyObject defaultFactory = null;
            if (args.Length > 0) defaultFactory = args[0];

            return new PyDefaultDict(defaultFactory);
        }

        public override bool IsCallable() => true;
    }

    /// <summary>
    /// OrderedDict 타입 팩토리
    /// </summary>
    public class PyOrderedDictType : PyObject
    {
        public override PyType GetPyType() => PyType.TypeType;
        public override string GetTypeName() => "type";

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return new PyOrderedDict();
        }

        public override bool IsCallable() => true;
    }

    /// <summary>
    /// Python deque 클래스의 C# 구현
    /// CPython의 _collectionsmodule.c 호환
    /// </summary>
    public class PyDeque : PyObject
    {
        private readonly LinkedList<PyObject> _items;
        private readonly int? _maxlen;

        public PyDeque(PyObject iterable = null, PyObject maxlen = null)
        {
            _items = new LinkedList<PyObject>();

            // maxlen 처리
            if (maxlen != null && maxlen != PyNone.Instance)
            {
                if (maxlen is PyInt maxInt && maxInt.Value >= 0)
                {
                    _maxlen = (int)maxInt.Value;
                }
                else
                {
                    throw new PythonException(new PyTypeError("an integer is required"));
                }
            }

            // iterable 처리
            if (iterable != null && iterable != PyNone.Instance)
            {
                var iterator = iterable.GetIterator();
                try
                {
                    while (true)
                    {
                        var item = iterator.Next();
                        Append(item);
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    // 정상적인 반복 종료
                }
                catch (Exception)
                {
                    throw new PythonException(new PyTypeError($"'{iterable.GetTypeName()}' object is not iterable"));
                }
            }
        }

        /// <summary>
        /// deque.append(x) - Add x to the right side of the deque
        /// </summary>
        public PyObject Append(PyObject item)
        {
            _items.AddLast(item);

            // maxlen 제한 적용
            if (_maxlen.HasValue && _items.Count > _maxlen.Value)
            {
                _items.RemoveFirst();
            }

            return PyNone.Instance;
        }

        /// <summary>
        /// deque.appendleft(x) - Add x to the left side of the deque
        /// </summary>
        public PyObject AppendLeft(PyObject item)
        {
            _items.AddFirst(item);

            // maxlen 제한 적용
            if (_maxlen.HasValue && _items.Count > _maxlen.Value)
            {
                _items.RemoveLast();
            }

            return PyNone.Instance;
        }

        /// <summary>
        /// deque.pop() - Remove and return an element from the right side
        /// </summary>
        public PyObject Pop()
        {
            if (_items.Count == 0)
            {
                throw new PythonException(new PyIndexError("pop from empty deque"));
            }

            var item = _items.Last.Value;
            _items.RemoveLast();
            return item;
        }

        /// <summary>
        /// deque.popleft() - Remove and return an element from the left side
        /// </summary>
        public PyObject PopLeft()
        {
            if (_items.Count == 0)
            {
                throw new PythonException(new PyIndexError("pop from empty deque"));
            }

            var item = _items.First.Value;
            _items.RemoveFirst();
            return item;
        }

        /// <summary>
        /// deque.clear() - Remove all elements from the deque
        /// </summary>
        public PyObject Clear()
        {
            _items.Clear();
            return PyNone.Instance;
        }

        /// <summary>
        /// deque.extend(iterable) - Extend the right side by appending elements
        /// </summary>
        public PyObject Extend(PyObject iterable)
        {
            var iterator = iterable.GetIterator();
            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    Append(item);
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상적인 반복 종료
            }
            catch (Exception)
            {
                throw new PythonException(new PyTypeError($"'{iterable.GetTypeName()}' object is not iterable"));
            }

            return PyNone.Instance;
        }

        /// <summary>
        /// deque.extendleft(iterable) - Extend the left side by appending elements
        /// </summary>
        public PyObject ExtendLeft(PyObject iterable)
        {
            var iterator = iterable.GetIterator();
            try
            {
                while (true)
                {
                    var item = iterator.Next();
                    AppendLeft(item);
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 정상적인 반복 종료
            }
            catch (Exception)
            {
                throw new PythonException(new PyTypeError($"'{iterable.GetTypeName()}' object is not iterable"));
            }

            return PyNone.Instance;
        }

        /// <summary>
        /// deque.rotate(n=1) - Rotate the deque n steps to the right
        /// </summary>
        public PyObject Rotate(PyObject n = null)
        {
            if (_items.Count == 0) return PyNone.Instance;

            int steps = 1;
            if (n != null && n != PyNone.Instance)
            {
                if (n is PyInt stepInt)
                {
                    steps = (int)stepInt.Value;
                }
                else
                {
                    throw new PythonException(new PyTypeError("an integer is required"));
                }
            }

            // 정규화 (음수 처리)
            steps = steps % _items.Count;
            if (steps < 0) steps += _items.Count;

            // 회전 수행
            for (int i = 0; i < steps; i++)
            {
                var last = _items.Last.Value;
                _items.RemoveLast();
                _items.AddFirst(last);
            }

            return PyNone.Instance;
        }

        // Python 특수 메서드들
        public override int Length()
        {
            return _items.Count;
        }

        public override PyObject GetIterator()
        {
            return new PyListIterator(new PyList(_items.ToArray()));
        }

        public override string ToString()
        {
            var items = string.Join(", ", _items.Select(item => item.ToString()));
            if (_maxlen.HasValue)
            {
                return $"deque([{items}], maxlen={_maxlen})";
            }
            return $"deque([{items}])";
        }

        public override string GetTypeName()
        {
            return "deque";
        }

        // 동적 메서드 호출 지원
        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "append":
                    return new PyFunction("append", (args) => {
                        if (args.Length != 1) throw new PythonException(new PyTypeError("append() takes exactly one argument"));
                        return Append(args[0]);
                    });
                case "appendleft":
                    return new PyFunction("appendleft", (args) => {
                        if (args.Length != 1) throw new PythonException(new PyTypeError("appendleft() takes exactly one argument"));
                        return AppendLeft(args[0]);
                    });
                case "pop":
                    return new PyFunction("pop", (args) => {
                        if (args.Length != 0) throw new PythonException(new PyTypeError("pop() takes no arguments"));
                        return Pop();
                    });
                case "popleft":
                    return new PyFunction("popleft", (args) => {
                        if (args.Length != 0) throw new PythonException(new PyTypeError("popleft() takes no arguments"));
                        return PopLeft();
                    });
                case "clear":
                    return new PyFunction("clear", (args) => {
                        if (args.Length != 0) throw new PythonException(new PyTypeError("clear() takes no arguments"));
                        return Clear();
                    });
                case "extend":
                    return new PyFunction("extend", (args) => {
                        if (args.Length != 1) throw new PythonException(new PyTypeError("extend() takes exactly one argument"));
                        return Extend(args[0]);
                    });
                case "extendleft":
                    return new PyFunction("extendleft", (args) => {
                        if (args.Length != 1) throw new PythonException(new PyTypeError("extendleft() takes exactly one argument"));
                        return ExtendLeft(args[0]);
                    });
                case "rotate":
                    return new PyFunction("rotate", (args) => {
                        if (args.Length > 1) throw new PythonException(new PyTypeError("rotate() takes at most 1 argument"));
                        var nParam = args.Length == 1 ? args[0] : null;
                        return Rotate(nParam);
                    });
                default:
                    return base.GetAttribute(name);
            }
        }
    }

    /// <summary>
    /// Python defaultdict 클래스의 C# 구현
    /// CPython의 _collectionsmodule.c 호환
    /// </summary>
    public class PyDefaultDict : PyDict
    {
        private readonly PyObject _defaultFactory;

        public PyDefaultDict(PyObject defaultFactory = null)
        {
            _defaultFactory = defaultFactory;
        }

        public override PyObject GetItem(PyObject key)
        {
            try
            {
                return base.GetItem(key);
            }
            catch (PythonException ex) when (ex.PyException is PyKeyError)
            {
                if (_defaultFactory == null)
                {
                    throw PyKeyError.Create($"'{key}'");
                }

                // Call default_factory() to get default value
                var defaultValue = _defaultFactory.IsCallable()
                    ? _defaultFactory.Call(new PyObject[0], null)
                    : throw new PythonException(new PyTypeError("default_factory must be callable"));

                SetItem(key, defaultValue);
                return defaultValue;
            }
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "default_factory":
                    return _defaultFactory ?? PyNone.Instance;
                default:
                    return base.GetAttribute(name);
            }
        }

        public override string GetTypeName()
        {
            return "defaultdict";
        }

        public override string ToString()
        {
            var items = string.Join(", ", _dict.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
            return $"defaultdict({_defaultFactory}, {{{items}}})";
        }
    }

    /// <summary>
    /// Python OrderedDict 클래스의 C# 구현
    /// CPython의 _collectionsmodule.c 호환
    /// </summary>
    public class PyOrderedDict : PyDict
    {
        private readonly LinkedList<PyObject> _insertionOrder;

        public PyOrderedDict()
        {
            _insertionOrder = new LinkedList<PyObject>();
        }

        public override void SetItem(PyObject key, PyObject value)
        {
            bool isNewKey = !_dict.ContainsKey(key);
            base.SetItem(key, value);

            if (isNewKey)
            {
                _insertionOrder.AddLast(key);
            }
        }

        public override PyObject DelItem(PyObject key)
        {
            if (_dict.ContainsKey(key))
            {
                var result = base.DelItem(key);
                _insertionOrder.Remove(key);
                return result;
            }
            else
            {
                throw PyKeyError.Create($"'{key}'");
            }
        }

        /// <summary>
        /// od.move_to_end(key, last=True) - Move key to end (or beginning if last=False)
        /// </summary>
        public PyObject MoveToEnd(PyObject key, PyObject last = null)
        {
            if (!_dict.ContainsKey(key))
            {
                throw PyKeyError.Create($"'{key}'");
            }

            bool moveToLast = true;
            if (last != null && last != PyNone.Instance)
            {
                moveToLast = last.PyBoolValue();
            }

            _insertionOrder.Remove(key);
            if (moveToLast)
            {
                _insertionOrder.AddLast(key);
            }
            else
            {
                _insertionOrder.AddFirst(key);
            }

            return PyNone.Instance;
        }

        public override PyObject GetIterator()
        {
            var keyArray = _insertionOrder.ToArray();
            return new PyListIterator(new PyList(keyArray));
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "move_to_end":
                    return new PyFunction("move_to_end", (args) => {
                        if (args.Length < 1 || args.Length > 2)
                            throw new PythonException(new PyTypeError("move_to_end() takes 1 or 2 arguments"));
                        var last = args.Length == 2 ? args[1] : null;
                        return MoveToEnd(args[0], last);
                    });
                default:
                    return base.GetAttribute(name);
            }
        }

        public override string GetTypeName()
        {
            return "OrderedDict";
        }

        protected virtual PyObject[] GetKeys()
        {
            return _insertionOrder.ToArray();
        }
    }
}