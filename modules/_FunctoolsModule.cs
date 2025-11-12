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
            module.ModuleDict["partial"] = new PyPartialType();
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
                    throw PyTypeError.Create("reduce() of empty sequence with no initial value");
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

        // cmp_to_key - 단순한 스텁
        public static PyObject PyCmpToKey(PyObject[] args, PyDict? kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"cmp_to_key expected exactly 1 argument, got {args.Length}");

            return new PyCmpToKeyStub();
        }
    }

    /// <summary>
    /// CPython 3.12: partial type - functools.partial implementation
    /// Modules/_functoolsmodule.c: partialobject
    /// </summary>
    public class PyPartialType : PyObject
    {
        public PyPartialType() { }

        public override string GetTypeName() => "partial";

        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            // CPython: partial_new - requires at least one argument (the function)
            if (args.Length == 0)
                throw PyTypeError.Create("type 'partial' takes at least one argument");

            var func = args[0];
            var partialArgs = new PyObject[args.Length - 1];
            Array.Copy(args, 1, partialArgs, 0, args.Length - 1);

            return new PyPartial(func, partialArgs, kwargs);
        }
    }

    /// <summary>
    /// CPython 3.12: partial object instance
    /// Stores a function and pre-filled arguments
    /// </summary>
    public class PyPartial : PyObject
    {
        private readonly PyObject _func;
        private readonly PyObject[] _args;
        private readonly PyDict? _kwargs;

        public PyPartial(PyObject func, PyObject[] args, PyDict? kwargs)
        {
            _func = func;
            _args = args ?? new PyObject[0];
            _kwargs = kwargs;
        }

        public override string GetTypeName() => "partial";

        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            // CPython 3.12: partial_call - merge stored args with call-time args
            // Combine pre-filled args with new args
            var combinedArgs = new PyObject[_args.Length + args.Length];
            Array.Copy(_args, 0, combinedArgs, 0, _args.Length);
            Array.Copy(args, 0, combinedArgs, _args.Length, args.Length);

            // Merge kwargs (call-time kwargs override stored kwargs)
            PyDict? combinedKwargs = null;
            if (_kwargs != null || kwargs != null)
            {
                combinedKwargs = new PyDict();

                // Add stored kwargs first
                if (_kwargs != null)
                {
                    foreach (var kv in _kwargs.InternalDict)
                        combinedKwargs.SetItem(kv.Key, kv.Value);
                }

                // Call-time kwargs override stored kwargs
                if (kwargs != null)
                {
                    foreach (var kv in kwargs.InternalDict)
                        combinedKwargs.SetItem(kv.Key, kv.Value);
                }
            }

            return _func.Call(combinedArgs, combinedKwargs);
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "func":
                    return _func;
                case "args":
                    return new PyTuple(_args);
                case "keywords":
                    return _kwargs ?? new PyDict();
                default:
                    return base.GetAttribute(name);
            }
        }

        public override PyString ToRepr()
        {
            return new PyString($"functools.partial({_func.ToRepr().Value})");
        }
    }

    /// <summary>
    /// cmp_to_key 스텁
    /// </summary>
    public class PyCmpToKeyStub : PyObject
    {
        public override string GetTypeName() => "cmp_to_key";

        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("cmp_to_key wrapper expected exactly 1 argument");

            return new PyKeyWrapperStub();
        }
    }

    /// <summary>
    /// KeyWrapper 스텁
    /// </summary>
    public class PyKeyWrapperStub : PyObject
    {
        public override string GetTypeName() => "KeyWrapper";

        public override PyString ToRepr()
        {
            return new PyString($"<functools.KeyWrapper object at 0x{GetHashCode():x}>");
        }
    }
}