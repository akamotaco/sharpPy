using System;
using System.IO;

#if GODOT
using Godot_IO;
#endif

namespace SharpPy
{
    /// <summary>
    /// Base class for Python context managers (__enter__/__exit__ protocol)
    /// </summary>
    public abstract class PyContextManager : PyObject
    {
        public override PyType GetPyType() => PyType.ObjectType;

        /// <summary>
        /// Python __enter__ method - called when entering with block
        /// </summary>
        public abstract PyObject Enter();

        /// <summary>
        /// Python __exit__ method - called when exiting with block
        /// </summary>
        /// <param name="excType">Exception type (None if no exception)</param>
        /// <param name="excValue">Exception value (None if no exception)</param>
        /// <param name="traceback">Traceback (None if no exception)</param>
        /// <returns>True to suppress exception, False to propagate</returns>
        public abstract PyObject Exit(PyObject excType, PyObject excValue, PyObject traceback);

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__enter__" => new PyBuiltinFunction("__enter__", args =>
                {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__enter__() takes no arguments");
                    return Enter();
                }),
                "__exit__" => new PyBuiltinFunction("__exit__", args =>
                {
                    if (args.Length != 3)
                        throw PyTypeError.Create("__exit__() takes exactly 3 arguments");
                    return Exit(args[0], args[1], args[2]);
                }),
                _ => base.GetAttribute(name)
            };
        }
    }

    /// <summary>
    /// Simple test context manager for demonstration
    /// </summary>
    public class PyTestContextManager : PyContextManager
    {
        private readonly string _name;

        public PyTestContextManager(string name)
        {
            _name = name;
        }

        public override string GetTypeName() => "test_context_manager";
        public override PyString ToRepr() => new PyString($"<TestContextManager '{_name}'>");

        public override PyObject Enter()
        {
            Console.WriteLine($"Entering context: {_name}");
            return this;
        }

        public override PyObject Exit(PyObject excType, PyObject excValue, PyObject traceback)
        {
            Console.WriteLine($"Exiting context: {_name}");

            // Return True to suppress ValueError, False otherwise
            if (excValue is PyValueError)
            {
                Console.WriteLine($"Suppressing ValueError in context: {_name}");
                return PyBool.True;
            }

            return PyBool.False;
        }
    }
}
