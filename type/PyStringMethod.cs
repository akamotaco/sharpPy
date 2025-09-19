using System;

namespace SharpPy
{
    /// <summary>
    /// Python 문자열 메서드를 나타내는 바인드된 메서드 객체
    /// </summary>
    public class PyStringMethod : PyObject
    {
        private readonly PyString _instance;
        private readonly string _methodName;
        private readonly Func<PyObject[], PyObject> _method;

        public PyStringMethod(PyString instance, string methodName, Func<PyObject[], PyObject> method)
        {
            _instance = instance;
            _methodName = methodName;
            _method = method;
        }

        public override string GetTypeName() => "builtin_function_or_method";

        public override string ToString() => $"<built-in method {_methodName} of str object at {GetHashCode():x8}>";

        public override string ToRepr() => ToString();

        /// <summary>
        /// 메서드 호출 구현
        /// </summary>
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
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