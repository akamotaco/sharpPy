using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpPy.Core
{
    /// <summary>
    /// CPython 3.12 Async Generator - PEP 525 구현
    /// async def 함수에서 yield를 사용할 때 생성되는 객체
    /// </summary>
    public class PyAsyncGenerator : PyObject
    {
        static PyAsyncGenerator()
        {
            InitializeAsyncGeneratorDescriptors();
        }

        private static void InitializeAsyncGeneratorDescriptors()
        {
            if (PyType.AsyncGeneratorType.Descriptors.Methods.Count > 0) return;

            var agType = PyType.AsyncGeneratorType;

            // __anext__ method descriptor
            agType.Descriptors.AddMethod("__anext__", new PyMethodDescriptor(
                "__anext__", agType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__anext__() takes no arguments");
                    if (self is not PyAsyncGenerator asyncGen)
                        throw PyTypeError.Create($"descriptor '__anext__' requires an 'async_generator' object but received a '{self.GetTypeName()}'");
                    return asyncGen.ANext();
                },
                minArgs: 0, maxArgs: 0
            ));

            // asend method descriptor
            agType.Descriptors.AddMethod("asend", new PyMethodDescriptor(
                "asend", agType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create("asend() takes exactly one argument");
                    if (self is not PyAsyncGenerator asyncGen)
                        throw PyTypeError.Create($"descriptor 'asend' requires an 'async_generator' object but received a '{self.GetTypeName()}'");
                    return asyncGen.ASend(args[0]);
                },
                minArgs: 1, maxArgs: 1
            ));

            // athrow method descriptor
            agType.Descriptors.AddMethod("athrow", new PyMethodDescriptor(
                "athrow", agType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create("athrow() takes 1 to 3 arguments");
                    if (self is not PyAsyncGenerator asyncGen)
                        throw PyTypeError.Create($"descriptor 'athrow' requires an 'async_generator' object but received a '{self.GetTypeName()}'");
                    var value = args.Length > 1 ? args[1] : null;
                    var tb = args.Length > 2 ? args[2] : null;
                    return asyncGen.AThrow(args[0], value, tb);
                },
                minArgs: 1, maxArgs: 3
            ));

            // aclose method descriptor
            agType.Descriptors.AddMethod("aclose", new PyMethodDescriptor(
                "aclose", agType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("aclose() takes no arguments");
                    if (self is not PyAsyncGenerator asyncGen)
                        throw PyTypeError.Create($"descriptor 'aclose' requires an 'async_generator' object but received a '{self.GetTypeName()}'");
                    return asyncGen.AClose();
                },
                minArgs: 0, maxArgs: 0
            ));
        }

        public override string GetTypeName() => "async_generator";
        public override PyType GetPyType() => PyType.AsyncGeneratorType;
        
        private readonly IEnumerator<PyObject> _enumerator;
        private bool _finished;
        private PyObject _sentValue;
        private Exception _thrownException;

        public string Name { get; }
        public PyObject Qualname { get; }

        public PyAsyncGenerator(IEnumerator<PyObject> enumerator, string name = "<async_generator>")
        {
            _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
            _finished = false;
            Name = name;
            Qualname = new PyString(name);
        }

        #region String Representation

        public override PyString ToRepr() => new PyString($"<async_generator object {Name}>");

        #endregion

        #region Async Generator Protocol

        /// <summary>
        /// async_generator.__anext__() 메서드 - PEP 525
        /// </summary>
        public PyObject ANext()
        {
            if (_finished)
            {
                var stopAsync = PyStopAsyncIteration.Create();
                throw new PythonException(stopAsync);
            }

            try
            {
                if (!_enumerator.MoveNext())
                {
                    _finished = true;
                    var stopAsync = PyStopAsyncIteration.Create();
                    throw new PythonException(stopAsync);
                }

                return _enumerator.Current;
            }
            catch (PythonException pyEx) when (pyEx.PyException is PyStopIteration stopIter)
            {
                _finished = true;
                var stopAsync = PyStopAsyncIteration.Create(stopIter.Value);
                throw new PythonException(stopAsync);
            }
            catch (Exception ex)
            {
                _finished = true;
                throw PyRuntimeError.Create(ex.Message);
            }
        }

        /// <summary>
        /// async_generator.asend(value) 메서드 - PEP 525
        /// </summary>
        public PyObject ASend(PyObject value)
        {
            _sentValue = value;
            return ANext();
        }

        /// <summary>
        /// async_generator.athrow(type, value=None, tb=None) 메서드 - PEP 525
        /// </summary>
        public PyObject AThrow(PyObject type, PyObject value = null, PyObject tb = null)
        {
            // 예외를 generator에 전달
            if (type is PyType pyType)
            {
                _thrownException = PyException.Create(pyType.Name);
            }
            else
            {
                _thrownException = PyException.Create(type.AsString());
            }

            try
            {
                return ANext();
            }
            catch (PythonException)
            {
                _finished = true;
                throw;
            }
        }

        /// <summary>
        /// async_generator.aclose() 메서드 - PEP 525
        /// </summary>
        public PyObject AClose()
        {
            if (!_finished)
            {
                try
                {
                    // GeneratorExit 예외를 생성하여 전달
                    var genExit = PyType.GeneratorExitType;
                    AThrow(genExit);
                }
                catch (PythonException ex) when (ex.PyException.GetTypeName() == "GeneratorExit")
                {
                    // 정상적인 종료
                }
                catch (PythonException ex) when (ex.PyException.GetTypeName() == "StopAsyncIteration")
                {
                    // 이미 종료됨
                }
                finally
                {
                    _finished = true;
                    _enumerator?.Dispose();
                }
            }
            return PyNone.Instance;
        }

        #endregion

        #region String and Equality

        public override string ToString()
        {
            return ToRepr().Value;
        }

        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj);
        }

        public override int GetHashCode()
        {
            return RuntimeHelpers.GetHashCode(this);
        }

        #endregion
    }
    
    /// <summary>
    /// StopAsyncIteration 예외 - PEP 525
    /// CPython 3.12의 StopAsyncIteration과 정확히 호환
    /// </summary>
    public class PyStopAsyncIteration : PyStopIteration
    {
        public PyStopAsyncIteration(PyObject value = null) : base(value)
        {
        }

        public static new PyStopAsyncIteration Create(PyObject value = null)
        {
            return new PyStopAsyncIteration(value);
        }

        public override string GetTypeName() => "StopAsyncIteration";
    }
}