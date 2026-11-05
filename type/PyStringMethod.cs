using System;

namespace SharpPy
{
    /// <summary>
    /// Python 문자열 메서드를 나타내는 바인드된 메서드 객체
    /// </summary>
    public class PyStringMethod : PyObject, ICFunction
    {
        string ICFunction.MethodName => _methodName;
        PyObject ICFunction.MethodSelf => _instance;
        PyObject ICFunction.MethodModule => null;


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

        protected internal override PyString Repr() => new PyString(ToString());

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