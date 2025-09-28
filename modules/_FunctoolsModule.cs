using System;
using System.Collections.Generic;
using System.Linq;

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

        // reduce - 단순한 스텁
        public static PyObject PyReduce(PyObject[] args, PyDict? kwargs)
        {
            if (args.Length < 2)
                throw PyTypeError.Create($"reduce expected at least 2 arguments, got {args.Length}");

            // 단순한 구현
            var function = args[0];
            var iterable = args[1];

            return new PyInt(42); // 스텁
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
    /// partial 타입 - 스텁 구현
    /// </summary>
    public class PyPartialType : PyObject
    {
        public PyPartialType() { }

        public override string GetTypeName() => "partial";

        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("partial expected at least 1 argument, got 0");

            return new PyPartialStub(args[0]);
        }
    }

    /// <summary>
    /// partial 인스턴스 스텁
    /// </summary>
    public class PyPartialStub : PyObject
    {
        private readonly PyObject _func;

        public PyPartialStub(PyObject func)
        {
            _func = func;
        }

        public override string GetTypeName() => "partial";

        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            return _func.Call(args, kwargs);
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "func":
                    return _func;
                case "args":
                    return new PyTuple(new PyObject[0]);
                case "keywords":
                    return new PyDict();
                default:
                    return base.GetAttribute(name);
            }
        }

        public override string ToRepr()
        {
            return $"functools.partial({_func.ToRepr()})";
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

        public override string ToRepr()
        {
            return $"<functools.KeyWrapper object at 0x{GetHashCode():x}>";
        }
    }
}