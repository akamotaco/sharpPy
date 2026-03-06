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
            var agType = PyType.AsyncGeneratorType;

            // __anext__ method descriptor
            agType.TypeDict["__anext__"] = new PyMethodDescriptor(
                "__anext__", agType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__anext__() takes no arguments");
                    if (self is not PyAsyncGenerator asyncGen)
                        throw PyTypeError.Create($"descriptor '__anext__' requires an 'async_generator' object but received a '{self.GetTypeName()}'");
                    return asyncGen.ANext();
                },
                minArgs: 0, maxArgs: 0
            );

            // asend method descriptor
            agType.TypeDict["asend"] = new PyMethodDescriptor(
                "asend", agType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create("asend() takes exactly one argument");
                    if (self is not PyAsyncGenerator asyncGen)
                        throw PyTypeError.Create($"descriptor 'asend' requires an 'async_generator' object but received a '{self.GetTypeName()}'");
                    return asyncGen.ASend(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // athrow method descriptor
            agType.TypeDict["athrow"] = new PyMethodDescriptor(
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
            );

            // aclose method descriptor
            agType.TypeDict["aclose"] = new PyMethodDescriptor(
                "aclose", agType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("aclose() takes no arguments");
                    if (self is not PyAsyncGenerator asyncGen)
                        throw PyTypeError.Create($"descriptor 'aclose' requires an 'async_generator' object but received a '{self.GetTypeName()}'");
                    return asyncGen.AClose();
                },
                minArgs: 0, maxArgs: 0
            );
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
            Qualname = new PyStr(name);
        }

        #region String Representation

        public override PyStr ToRepr() => new PyStr($"<async_generator object {Name}>");

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

    /// <summary>
    /// CPython 3.12: _PyAsyncGenWrappedValue
    /// Wrapper for values yielded from async generators
    /// (Objects/genobject.c: _PyAsyncGenWrappedValue)
    ///
    /// Performance: Uses object pooling (freelist) to reduce allocations.
    /// CPython reports 6-10% performance improvement from freelists.
    /// </summary>
    public class PyAsyncGenWrappedValue : PyObject
    {
        #region CPython 3.12: Freelist for object pooling (pycore_genobject.h:28)

        /// <summary>
        /// CPython 3.12: _PyAsyncGen_MAXFREELIST = 80
        /// Maximum number of cached objects in the freelist
        /// </summary>
        private const int MAXFREELIST = 80;

        /// <summary>
        /// CPython 3.12: value_freelist[]
        /// Array of reusable wrapper objects
        /// </summary>
        private static readonly PyAsyncGenWrappedValue[] _freelist = new PyAsyncGenWrappedValue[MAXFREELIST];

        /// <summary>
        /// CPython 3.12: value_numfree
        /// Number of objects currently in the freelist
        /// </summary>
        private static int _numFree = 0;

        /// <summary>
        /// Thread safety lock for freelist operations
        /// CPython uses per-interpreter state; we use a global lock for simplicity
        /// </summary>
        private static readonly object _freelistLock = new object();

        #endregion

        public PyObject Value { get; private set; }

        /// <summary>
        /// Private constructor - use Create() factory method instead
        /// </summary>
        private PyAsyncGenWrappedValue()
        {
        }

        /// <summary>
        /// CPython 3.12: _PyAsyncGenValueWrapperNew (Objects/genobject.c:2028)
        /// Factory method that uses freelist for object pooling
        /// </summary>
        public static PyAsyncGenWrappedValue Create(PyObject value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            PyAsyncGenWrappedValue wrapper;
            bool reusedFromFreelist = false;

            lock (_freelistLock)
            {
                // CPython: if (state->value_numfree)
                if (_numFree > 0)
                {
                    // Reuse object from freelist
                    _numFree--;
                    wrapper = _freelist[_numFree];
                    _freelist[_numFree] = null; // Clear reference
                    reusedFromFreelist = true;
                }
                else
                {
                    // Allocate new object
                    wrapper = new PyAsyncGenWrappedValue();
                }
            }

#if DEBUG_FREELIST
            if (reusedFromFreelist)
                Console.WriteLine($"[FREELIST] Reused wrapper from freelist (free: {_numFree})");
            else
                Console.WriteLine($"[FREELIST] Allocated new wrapper (free: {_numFree})");
#endif

            // Set the value (CPython: o->agw_val = Py_NewRef(val))
            wrapper.Value = value;
            return wrapper;
        }

        /// <summary>
        /// CPython 3.12: Return object to freelist when no longer needed
        /// Called during garbage collection or when wrapper is discarded
        /// (Objects/genobject.c:1962)
        /// </summary>
        public void Release()
        {
            lock (_freelistLock)
            {
                // CPython: if (state->value_numfree < _PyAsyncGen_MAXFREELIST)
                if (_numFree < MAXFREELIST)
                {
                    // Clear the value reference
                    Value = null;

                    // Add to freelist
                    _freelist[_numFree] = this;
                    _numFree++;

#if DEBUG_FREELIST
                    Console.WriteLine($"[FREELIST] Returned wrapper to freelist (free: {_numFree}/{MAXFREELIST})");
#endif
                }
                else
                {
#if DEBUG_FREELIST
                    Console.WriteLine($"[FREELIST] Freelist full, wrapper will be GC'd (free: {_numFree}/{MAXFREELIST})");
#endif
                }
                // else: freelist is full, object will be garbage collected
            }
        }

        /// <summary>
        /// CPython 3.12: _PyAsyncGen_ClearFreeLists (Objects/genobject.c:1665)
        /// Clear the freelist (useful for testing or shutdown)
        /// </summary>
        public static void ClearFreelist()
        {
            lock (_freelistLock)
            {
                Array.Clear(_freelist, 0, _numFree);
                _numFree = 0;
            }
        }

        /// <summary>
        /// Get current freelist statistics (for debugging/profiling)
        /// </summary>
        public static (int numFree, int maxSize) GetFreelistStats()
        {
            lock (_freelistLock)
            {
                return (_numFree, MAXFREELIST);
            }
        }

        public override string GetTypeName() => "async_generator_wrapped_value";
        public override PyType GetPyType() => PyType.ObjectType; // Internal type, no public PyType

        public override PyStr ToRepr() => new PyStr($"<async_generator_wrapped_value {Value.ToRepr().Value}>");
        public override PyStr ToStr() => Value.ToStr();

        public override bool Equals(object obj)
        {
            if (obj is PyAsyncGenWrappedValue other)
                return Value.Equals(other.Value);
            return false;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}