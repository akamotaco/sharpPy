using System;

namespace SharpPy
{
    /// <summary>
    /// Python 문자열 메서드를 나타내는 바인드된 메서드 객체
    /// </summary>
    public class PyStringMethod : PyObject
    {
        // CPython: bound str methods are builtin_function_or_method (PyCFunction).
        public override PyType GetPyType() => PyType.BuiltinFunctionOrMethodType;
        public override string GetTypeName() => "builtin_function_or_method";

        private readonly PyString _instance;
        private readonly string _methodName;
        private readonly Func<PyObject[], PyObject> _method;

        public PyStringMethod(PyString instance, string methodName, Func<PyObject[], PyObject> method)
        {
            _instance = instance;
            _methodName = methodName;
            _method = method;
        }

        // CPython: bound str method repr uses the bound str object's identity and the
        // 0x prefix: <built-in method NAME of str object at 0xID>.
        public override string ToString() => $"<built-in method {_methodName} of str object at 0x{_instance.GetHashCode():x}>";

        public override PyString ToRepr() => new PyString(ToString());

        // CPython: a bound builtin method exposes __self__ (the bound object),
        // __name__/__qualname__ (the method name) and __objclass__ (str).
        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "__self__": return _instance;
                case "__name__": return new PyString(_methodName);
                case "__qualname__": return new PyString(_methodName);
                case "__objclass__": return PyType.StrType;
                case "__call__": return this;
                default: return base.GetAttribute(name);
            }
        }

        /// <summary>
        /// 메서드 호출 구현
        /// </summary>
        // CPython str methods are METH_VARARGS/METH_FASTCALL without METH_KEYWORDS, so keywords are
        // rejected using the defining type's qualname: 'a'.upper(x=1) ->
        // TypeError: str.upper() takes no keyword arguments. Measured on CPython 3.12.15, only
        // str.format, str.format_map and str.splitlines are METH_KEYWORDS.
        private static readonly System.Collections.Generic.HashSet<string> AcceptsKeywords =
            new() { "format", "format_map", "splitlines" };

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            if (kwargs != null && kwargs.Length() != 0 && !AcceptsKeywords.Contains(_methodName))
                throw PyTypeError.Create($"str.{_methodName}() takes no keyword arguments");

            args ??= Array.Empty<PyObject>();
            return _method(args);
        }

        /// <summary>
        /// VM에서 CALL_FUNCTION 바이트코드로 호출될 때 사용
        /// </summary>
        public PyObject Invoke(params PyObject[] args)
        {
            return Call(args);
        }
    }
}