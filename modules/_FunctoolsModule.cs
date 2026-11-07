using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython _functools C 확장 모듈 - 단순한 스텁 구현
    /// 향후 완전한 CPython 3.12 호환 구현으로 대체 예정
    /// </summary>
    public static class _FunctoolsModule
    {
        public static PyModule CreateFunctoolsModule()
        {
            var module = new PyModule("_functools", "Fast C implementation of functools functions");

            // 기본적인 스텁 구현만 제공
            module.ModuleDict["reduce"] = new PyBuiltinFunction("reduce", PyReduce);
            // partial is the class in functools.py: a type, as isinstance and asyncio need it to be.
            module.ModuleDict["cmp_to_key"] = new PyBuiltinFunction("cmp_to_key", PyCmpToKey);

            return module;
        }

        // reduce - CPython 3.12 compatible implementation
        // functools_reduce: PyArg_UnpackTuple("reduce", 2, 3), PyObject_GetIter, then PyIter_Next
        // until it ends; only the end of the iterator stops the loop, every other error propagates.
        public static PyObject PyReduce(PyObject[] args, PyDict? kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("reduce() takes no keyword arguments");
            PyArgs.Positional("reduce", args, null, 2, 3);
            var function = args[0];
            PyObject? result = args.Length == 3 ? args[2] : null;
            PyObject iterator;
            try
            {
                iterator = args[1].GetIterator();
            }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                throw PyTypeError.Create("reduce() arg 2 must support iteration");
            }
            while (true)
            {
                PyObject item;
                try
                {
                    item = iterator.Next();
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    break;
                }
                result = result == null ? item : function.Call(new[] { result, item }, null);
            }
            return result ?? throw PyTypeError.Create("reduce() of empty iterable with no initial value");
        }

        /// <summary>
        /// cmp_to_key - CPython 3.12: Modules/_functoolsmodule.c:202-280 (cmp_to_key)
        /// Converts a comparison function (cmp(a, b) -> int) to a key function
        /// </summary>
        public static PyObject PyCmpToKey(PyObject[] args, PyDict? kwargs)
        {
            var compare = args.Length > 0 ? args[0] : null;
            if (kwargs != null && kwargs.TryGetStoredValue(new PyString("mycmp"), out var named))
            {
                if (compare != null)
                    throw PyTypeError.Create("argument for cmp_to_key() given by name ('mycmp') and position (1)");
                compare = named;
            }
            if (compare == null)
                throw PyTypeError.Create("cmp_to_key() missing required argument 'mycmp' (pos 1)");
            if (args.Length + (kwargs?.Length() ?? 0) > 1)
                throw PyTypeError.Create($"cmp_to_key() takes at most 1 argument ({args.Length + kwargs.Length()} given)");
            // functools_cmp_to_key_impl: a keyobject with no object yet.
            return new PyKeyWrapper(compare, null);
        }
    }

    /// <summary>
    /// keyobject (Modules/_functoolsmodule.c): functools.KeyWrapper, what cmp_to_key returns and
    /// what calling it makes. The first holds no object; each call wraps one in a new keyobject.
    /// </summary>
    public class PyKeyWrapper : PyObject
    {
        private static readonly PyType KeyWrapperType = MakeType();

        private readonly PyObject _cmpFunc;
        // keyobject.object, the T_OBJECT member obj; null is NULL.
        private PyObject _object;

        public PyKeyWrapper(PyObject cmpFunc, PyObject obj)
        {
            _cmpFunc = cmpFunc;
            _object = obj;
        }

        private static PyType MakeType()
        {
            var type = new PyType("KeyWrapper", new[] { PyType.ObjectType }, "functools");
            type.TypeDict["obj"] = new PyMemberDescriptor("obj", type,
                self => ((PyKeyWrapper)self)._object ?? PyNone.Instance,
                (self, value) => ((PyKeyWrapper)self)._object = value,
                deleter: self => ((PyKeyWrapper)self)._object = null);
            type.TypeDict["__call__"] = new PyWrapperDescriptor("__call__", type, (self, args, kwargs) => self.Call(args, kwargs));
            // tp_richcompare without tp_hash: unhashable.
            type.TypeDict["__hash__"] = PyNone.Instance;
            foreach (var (name, op) in new[] { ("__lt__", CompareOp.LT), ("__le__", CompareOp.LE), ("__eq__", CompareOp.EQ),
                         ("__ne__", CompareOp.NE), ("__gt__", CompareOp.GT), ("__ge__", CompareOp.GE) })
                type.TypeDict[name] = new PyWrapperDescriptor(name, type, (self, args, kwargs) => self.TpRichCompare(args[0], op));
            return type;
        }

        public override PyType GetPyType() => KeyWrapperType;
        public override string GetTypeName() => "functools.KeyWrapper";

        protected internal override long Hash() => throw PyTypeError.Create("unhashable type: 'functools.KeyWrapper'");

        // keyobject_call: PyArg_ParseTupleAndKeywords(args, kwds, "O:K", {"obj"}).
        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            var parsed = PyArgs.Unpack("K", args, kwargs, new[] { "obj" }, 0, 1, 1);
            return new PyKeyWrapper(_cmpFunc, parsed[0]);
        }

        protected internal override PyString Repr() => new PyString($"<functools.KeyWrapper object at {PyObjectId.Pointer(this)}>");

        // keyobject_richcompare: both keyobjects of the same type, the user's three-way result
        // compared with zero by PyObject_RichCompare.
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (other is not PyKeyWrapper otherKey)
                throw PyTypeError.Create("other argument must be K instance");
            if (_object == null || otherKey._object == null)
                throw PyAttributeError.Create("object");
            var result = _cmpFunc.Call(new[] { _object, otherKey._object }, null);
            return Compare(result, new PyInt(0), op);
        }
    }
}
