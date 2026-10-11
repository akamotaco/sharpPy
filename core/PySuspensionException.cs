using System;

namespace SharpPy
{
    // Argument normalization shared by generator and coroutine throw().
    internal static class PySuspensionException
    {
        internal static PythonException Normalize(PyObject exception, PyObject value = null, PyObject traceback = null)
        {
            traceback ??= PyNone.Instance;
            value ??= PyNone.Instance;
            var original = exception;
            if (traceback != PyNone.Instance && traceback is not PyTraceback)
                throw PyTypeError.Create("throw() third argument must be a traceback object");
            if (exception is PyType type && type.IsSubclassOf(PyType.BaseExceptionType))
            {
                if (value != PyNone.Instance && value.GetPyType().IsSubclassOf(type))
                    exception = value;
                else
                {
                    var constructorArgs = value == PyNone.Instance ? Array.Empty<PyObject>() :
                        value is PyTuple tuple ? tuple.Items : new[] { value };
                    try { exception = type.Call(constructorArgs, null); }
                    catch (PythonException error) { return error; }
                }
            }
            else if (exception.GetPyType().IsSubclassOf(PyType.BaseExceptionType))
            {
                if (value != PyNone.Instance)
                    throw PyTypeError.Create("instance exception may not have a separate value");
            }
            else
                throw PyTypeError.Create($"exceptions must be classes or instances deriving from BaseException, not {exception.GetTypeName()}");

            PyBaseException normalized = exception as PyBaseException;
            if (normalized == null && exception is PyClassInstance instance &&
                instance.InstanceType.IsSubclassOf(PyType.BaseExceptionType))
                normalized = new PyException(instance.ToString(), instance.InstanceType, instance);
            if (normalized == null)
                return new PythonException(new PyTypeError($"calling {original.ToStr().Value} should have returned an instance of BaseException, not {exception.GetTypeName()}"));
            if (traceback is PyTraceback tb) normalized.__traceback__ = tb;
            return new PythonException(normalized);
        }

    }
}
