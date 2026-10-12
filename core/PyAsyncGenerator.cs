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
        private readonly PyFrame _frame;
        private readonly PyGenerator _generator;
        internal bool Running;
        private bool _finished;
        public string Name { get; }
        public PyObject Qualname { get; }

        static PyAsyncGenerator()
        {
            var type = PyType.AsyncGeneratorType;
            foreach (var name in new[] { "__aiter__", "__anext__", "asend", "athrow", "aclose" })
                type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) => {
                    if (self is not PyAsyncGenerator generator)
                        throw PyTypeError.Create($"descriptor '{name}' requires a 'async_generator' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.Length() != 0) throw PyTypeError.Create($"{name}() takes no keyword arguments");
                    int minimum = name is "asend" or "athrow" ? 1 : 0;
                    int maximum = name == "athrow" ? 3 : minimum;
                    if (args.Length < minimum || args.Length > maximum)
                        throw PyTypeError.Create($"{name}() takes {minimum} argument{(minimum == 1 ? "" : "s")} ({args.Length} given)");
                    return name switch {
                        "__aiter__" => generator,
                        "__anext__" => generator.ANext(),
                        "asend" => generator.ASend(args[0]),
                        "athrow" => generator.AThrow(args[0], args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null),
                        _ => generator.AClose()
                    };
                });
        }

        public PyAsyncGenerator(PyFrame frame, PyVM vm, string name = "<async_generator>")
        {
            _frame = frame;
            _generator = new PyGenerator(frame, vm, name);
            Name = name;
            Qualname = new PyString(name);
        }
        public override PyType GetPyType() => PyType.AsyncGeneratorType;
        public override string GetTypeName() => "async_generator";
        public override PyString ToRepr() => new PyString($"<async_generator object {Name}>");
        public PyObject ANext() => ASend(PyNone.Instance);
        public PyObject ASend(PyObject value) => new Awaitable(this, value, null, false);
        public PyObject AThrow(PyObject type, PyObject value = null, PyObject tb = null) =>
            new Awaitable(this, null, tb != null ? new[] { type, value ?? PyNone.Instance, tb } : value != null ? new[] { type, value } : new[] { type }, false);
        public PyObject AClose() => new Awaitable(this, null, null, true);

        private sealed class Awaitable : PyIterator
        {
            private static readonly PyType SendType = MakeType("async_generator_asend");
            private static readonly PyType ThrowType = MakeType("async_generator_athrow");
            private readonly PyAsyncGenerator _owner;
            private readonly PyObject _value;
            private readonly PyObject[] _throwArgs;
            private readonly bool _closing;
            private bool _started, _closed;
            private bool IsThrow => _closing || _throwArgs != null;
            internal Awaitable(PyAsyncGenerator owner, PyObject value, PyObject[] throwArgs, bool closing)
            { _owner = owner; _value = value; _throwArgs = throwArgs; _closing = closing; }
            private static PyType MakeType(string name)
            {
                var type = new PyType(name, new[] { PyType.ObjectType });
                foreach (var method in new[] { "__await__", "__iter__", "__next__", "send", "throw", "close" })
                    type.TypeDict[method] = new PyMethodDescriptor(method, type, (self, args, kwargs) => {
                        if (self is not Awaitable request || request.GetPyType() != type)
                            throw PyTypeError.Create($"descriptor '{method}' requires a '{name}' object but received a '{self.GetTypeName()}'");
                        if (method == "send")
                        {
                            PyCoroutine.CheckSendArguments(name, args, kwargs);
                            return request.Drive(args[0]);
                        }
                        if (method == "throw")
                        {
                            PyCoroutine.CheckThrowArguments(name, args, kwargs);
                            return request.Drive(PyNone.Instance, args);
                        }
                        PyCoroutine.CheckCloseArguments(name + "." + method, args, kwargs);
                        if (method == "close") { request._closed = true; return PyNone.Instance; }
                        return method == "__next__" ? request.Next() : request;
                    });
                return type;
            }
            public override PyType GetPyType() => IsThrow ? ThrowType : SendType;
            public override string GetTypeName() => GetPyType().Name;
            public override bool IsCallable() => false;
            public override PyObject Call(PyObject[] args, PyDict kwargs = null) => throw PyTypeError.Create($"'{GetTypeName()}' object is not callable");
            public override PyObject Next() => Drive(PyNone.Instance);
            private PyObject Drive(PyObject sent, PyObject[] injected = null)
            {
                if (_closed) throw PyRuntimeError.Create(IsThrow ? "cannot reuse already awaited aclose()/athrow()" : "cannot reuse already awaited __anext__()/asend()");
                bool initial = !_started;
                if (initial)
                {
                    if (_owner.Running)
                    {
                        _closed = true;
                        throw PyRuntimeError.Create($"{(_closing ? "aclose" : IsThrow ? "athrow" : "anext")}(): asynchronous generator is already running");
                    }
                    if (_owner._finished)
                    {
                        _closed = true;
                        if (IsThrow) throw PyStopIteration.Create();
                        throw PyStopAsyncIteration.Create();
                    }
                    if (IsThrow && sent != PyNone.Instance && injected == null)
                        throw PyRuntimeError.Create("can't send non-None value to a just-started coroutine");
                    if (_closing && _owner._frame.State == PyFrame.FrameState.Created)
                    {
                        _owner._generator.Close(); _owner._finished = _closed = true;
                        throw PyStopIteration.Create();
                    }
                    _started = true;
                    if (!IsThrow && sent == PyNone.Instance) sent = _value;
                }
                _owner.Running = true;
                PyObject result;
                try
                {
                    var arguments = injected ?? (initial ? _throwArgs : null);
                    if (arguments != null)
                        result = _owner._generator.Throw(arguments[0], arguments.Length > 1 ? arguments[1] : null, arguments.Length > 2 ? arguments[2] : null);
                    else if (initial && _closing)
                        result = _owner._generator.Throw(PyType.GeneratorExitType);
                    else
                    {
                        if (_owner._frame.State == PyFrame.FrameState.Created && sent != PyNone.Instance)
                            throw PyTypeError.Create("can't send non-None value to a just-started async generator");
                        result = _owner._generator.Send(sent);
                    }
                }
                catch (PythonException error)
                {
                    _closed = true; _owner.Running = false;
                    _owner._finished = _owner._generator.IsFinished;
                    if (error.PyException is PyStopIteration)
                    {
                        _owner._finished = true;
                        if (_closing) throw PyStopIteration.Create();
                        throw PyStopAsyncIteration.Create();
                    }
                    if (_closing && (error.PyException is PyGeneratorExit || error.PyException is PyStopAsyncIteration))
                    { _owner._finished = true; throw PyStopIteration.Create(); }
                    throw;
                }
                if (result is PyAsyncGenWrappedValue wrapped)
                {
                    var value = wrapped.Value;
                    wrapped.Release();
                    _closed = true; _owner.Running = false;
                    if (_closing) throw PyRuntimeError.Create("async generator ignored GeneratorExit");
                    throw PyStopIteration.Create(value);
                }
                return result; // An inner await suspended; this request still owns execution.
            }
        }
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

        public override PyString ToRepr() => new PyString($"<async_generator_wrapped_value {Value.ToRepr().Value}>");
        public override PyString ToStr() => Value.ToStr();

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
