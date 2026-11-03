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
        public static PyObject PyReduce(PyObject[] args, PyDict? kwargs)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"reduce expected 2 or 3 arguments, got {args.Length}");

            var function = args[0];
            var sequence = args[1];

            // Get iterator from sequence
            var iterator = sequence.GetIterator();
            PyObject? accumulator = null;

            // If initial value provided, use it
            if (args.Length == 3)
            {
                accumulator = args[2];
            }
            else
            {
                // Otherwise, use first element of sequence
                try
                {
                    accumulator = iterator.Next();
                }
                catch
                {
                    throw PyTypeError.Create("reduce() of empty iterable with no initial value");
                }
            }

            // Apply function to each element
            while (true)
            {
                try
                {
                    var item = iterator.Next();
                    accumulator = function.Call(new[] { accumulator, item }, null);
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    break;
                }
            }

            return accumulator;
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
            return new PyCmpToKeyWrapper(compare);
        }
    }

    /// <summary>
    /// cmp_to_key wrapper - CPython 3.12: Modules/_functoolsmodule.c:100-150 (keyobject)
    /// This is callable and produces KeyWrapper objects when called with a value
    /// </summary>
    public class PyCmpToKeyWrapper : PyObject
    {
        private readonly PyObject _cmpFunc;  // The comparison function

        public PyCmpToKeyWrapper(PyObject cmpFunc)
        {
            _cmpFunc = cmpFunc;
        }

        public override string GetTypeName() => "cmp_to_key";

        /// <summary>
        /// When called with a value, produce a KeyWrapper that wraps that value
        /// CPython 3.12: Modules/_functoolsmodule.c:138-158 (keyobject_call)
        /// </summary>
        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("cmp_to_key wrapper expected exactly 1 argument");

            return new PyKeyWrapper(_cmpFunc, args[0]);
        }

        public override PyString ToRepr()
        {
            return new PyString("<functools.cmp_to_key object>");
        }
    }

    /// <summary>
    /// KeyWrapper - wraps a value and uses the comparison function for ordering
    /// CPython 3.12: Modules/_functoolsmodule.c:54-100 (keyobject)
    /// </summary>
    public class PyKeyWrapper : PyObject
    {
        private readonly PyObject _cmpFunc;  // CPython: cmp
        private readonly PyObject _object;   // CPython: object (the wrapped value)

        public PyKeyWrapper(PyObject cmpFunc, PyObject obj)
        {
            _cmpFunc = cmpFunc;
            _object = obj;
        }

        public override string GetTypeName() => "KeyWrapper";

        public override PyString ToRepr()
        {
            return new PyString($"<functools.KeyWrapper object at 0x{GetHashCode():x}>");
        }

        /// <summary>
        /// Rich comparison - CPython 3.12: Modules/_functoolsmodule.c:60-98 (keyobject_richcompare)
        /// </summary>
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            if (other is PyKeyWrapper otherKey)
            {
                // Call the comparison function: cmp(self.object, other.object)
                var cmpResult = _cmpFunc.Call(new[] { _object, otherKey._object }, null);

                // Convert result to int
                int cmp;
                if (cmpResult is PyInt pyInt)
                {
                    cmp = (int)pyInt.Value;
                }
                else if (cmpResult is PyBool pyBool)
                {
                    cmp = pyBool.Value ? 1 : 0;
                }
                else
                {
                    throw PyTypeError.Create("comparison function must return int");
                }

                // Apply the comparison operator
                bool result;
                switch (op)
                {
                    case CompareOp.LT:
                        result = cmp < 0;
                        break;
                    case CompareOp.LE:
                        result = cmp <= 0;
                        break;
                    case CompareOp.EQ:
                        result = cmp == 0;
                        break;
                    case CompareOp.NE:
                        result = cmp != 0;
                        break;
                    case CompareOp.GT:
                        result = cmp > 0;
                        break;
                    case CompareOp.GE:
                        result = cmp >= 0;
                        break;
                    default:
                        return PyNotImplemented.Instance;
                }

                return PyBool.FromBool(result);
            }

            return PyNotImplemented.Instance;
        }
    }
}