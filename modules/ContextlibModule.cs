using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// contextlib module implementation - Python 3.12 Context Manager utilities
    /// </summary>
    public static class ContextlibModule
    {
        public static PyModule Create()
        {
            var contextlibModule = new PyModule("contextlib", "Context manager utilities");
            
            // @contextmanager decorator
            contextlibModule.ModuleDict["contextmanager"] = new PyContextManagerDecorator();
            
            return contextlibModule;
        }
    }
    
    /// <summary>
    /// @contextmanager decorator implementation
    /// Converts a generator function into a context manager
    /// </summary>
    public class PyContextManagerDecorator : PyBuiltinFunction
    {
        public PyContextManagerDecorator() : base("contextmanager", (args) =>
        {
            if (args.Length != 1)
            {
                throw PyTypeError.Create("contextmanager() takes exactly one argument");
            }

            var func = args[0];
            if (func is not PyFunction pyFunc)
            {
                throw PyTypeError.Create("contextmanager() argument must be a function");
            }

            // Return a new context manager class that wraps the generator function
            return new PyGeneratorContextManagerWrapper(pyFunc);
        })
        {
        }
    }
    
    /// <summary>
    /// Context manager wrapper for generator-based context managers
    /// </summary>
    public class PyGeneratorContextManagerWrapper : PyObject
    {
        private readonly PyFunction _generatorFunc;
        
        public PyGeneratorContextManagerWrapper(PyFunction generatorFunc)
        {
            _generatorFunc = generatorFunc;
        }

        public override string GetTypeName() => "GeneratorContextManagerWrapper";

        public override PyObject Call(params PyObject[] args)
        {
            // Call the original generator function to get a generator
            var generator = _generatorFunc.Call(args);
            
            if (generator is not PyGenerator pyGen)
            {
                throw PyTypeError.Create("contextmanager function must be a generator");
            }
            
            return new PyGeneratorContextManager(pyGen);
        }

        public override bool IsCallable() => true;
    }
    
    /// <summary>
    /// Context manager implementation for generator-based context managers
    /// </summary>
    public class PyGeneratorContextManager : PyContextManager
    {
        private readonly PyGenerator _generator;
        private PyObject? _enteredValue;
        private bool _hasEntered = false;

        public PyGeneratorContextManager(PyGenerator generator)
        {
            _generator = generator;
        }

        public override string GetTypeName() => "GeneratorContextManager";

        /// <summary>
        /// __enter__ method - call next() on generator to get the yielded value
        /// </summary>
        public override PyObject Enter()
        {
            if (_hasEntered)
            {
                throw PyRuntimeError.Create("context manager already entered");
            }

            try
            {
                // Call next() on the generator - this should yield the value to return from __enter__
                _enteredValue = _generator.Next();
                _hasEntered = true;
                return _enteredValue ?? PyNone.Instance;
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // Generator exhausted without yielding - return None
                _hasEntered = true;
                return PyNone.Instance;
            }
            catch (PythonException)
            {
                // Re-throw Python exceptions
                throw;
            }
            catch (Exception ex)
            {
                // Wrap C# exceptions
                throw PyRuntimeError.Create($"Error in context manager: {ex.Message}");
            }
        }

        /// <summary>
        /// __exit__ method - send exception info to generator or call next()
        /// </summary>
        public override PyObject Exit(PyObject excType, PyObject excValue, PyObject traceback)
        {
            if (!_hasEntered)
            {
                throw PyRuntimeError.Create("context manager not entered");
            }

            try
            {
                if (excType != PyNone.Instance)
                {
                    // There was an exception - send it to the generator
                    // For now, just call next() - proper exception handling would use generator.throw()
                    _generator.Next();
                }
                else
                {
                    // No exception - just call next() to continue/finish the generator
                    _generator.Next();
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // Generator finished normally - exception not suppressed
                return PyBool.False;
            }
            catch (PythonException)
            {
                // Python exceptions from the generator should propagate
                throw;
            }
            catch (Exception ex)
            {
                // Wrap C# exceptions
                throw PyRuntimeError.Create($"Error in context manager exit: {ex.Message}");
            }

            // If we get here, the generator yielded again, which means it handled the exception
            return PyBool.True;
        }
    }
}