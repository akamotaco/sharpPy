using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace SharpPy.Core
{
    /// <summary>
    /// Global coroutine tracking for CPython 3.12 compatibility warnings
    /// </summary>
    public static class CoroutineTracker
    {
        private static readonly ConcurrentBag<WeakReference<PyCoroutine>> _activeCoroutines = new();

        public static void RegisterCoroutine(PyCoroutine coroutine)
        {
            _activeCoroutines.Add(new WeakReference<PyCoroutine>(coroutine));
        }

        public static void CheckForUnawaitedCoroutines()
        {
            foreach (var weakRef in _activeCoroutines)
            {
                if (weakRef.TryGetTarget(out var coroutine) && !coroutine.IsFinished)
                {
                    Console.Error.WriteLine($"sys:1: RuntimeWarning: coroutine '{coroutine.Name}' was never awaited");
                }
            }
        }
    }
    /// <summary>
    /// CPython 3.12 Native Coroutine - PEP 492 구현
    /// Generator와 구별되는 별도 타입으로 __await__ 메서드만 구현
    /// </summary>
    public class PyCoroutine : PyObject
    {
        static PyCoroutine()
        {
            InitializeCoroutineDescriptors();
        }

        private static void InitializeCoroutineDescriptors()
        {
            var corType = PyType.CoroutineType;

            // send method descriptor
            corType.TypeDict["send"] = new PyMethodDescriptor(
                "send", corType,
                (self, args, kwargs) => {
                    if (self is not PyCoroutine coroutine)
                        throw PyTypeError.Create($"descriptor 'send' requires a 'coroutine' object but received a '{self.GetTypeName()}'");
                    CheckSendArguments("coroutine", args, kwargs);
                    return coroutine.Send(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            corType.TypeDict["throw"] = new PyMethodDescriptor("throw", corType,
                (self, args, kwargs) => {
                    if (self is not PyCoroutine coroutine)
                        throw PyTypeError.Create($"descriptor 'throw' requires a 'coroutine' object but received a '{self.GetTypeName()}'");
                    CheckThrowArguments("coroutine", args, kwargs);
                    return coroutine.Throw(args);
                }, minArgs: 1, maxArgs: 3);

            // close method descriptor
            corType.TypeDict["close"] = new PyMethodDescriptor(
                "close", corType,
                (self, args, kwargs) => {
                    if (self is not PyCoroutine coroutine)
                        throw PyTypeError.Create($"descriptor 'close' requires a 'coroutine' object but received a '{self.GetTypeName()}'");
                    CheckCloseArguments("coroutine", args, kwargs);
                    coroutine.Close();
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 0
            );

            // __await__ method descriptor
            corType.TypeDict["__await__"] = new PyMethodDescriptor(
                "__await__", corType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__await__() takes no arguments");
                    if (self is not PyCoroutine coroutine)
                        throw PyTypeError.Create($"descriptor '__await__' requires a 'coroutine' object but received a '{self.GetTypeName()}'");
                    return coroutine.GetAwaiter();
                },
                minArgs: 0, maxArgs: 0
            );
        }

        public override string GetTypeName() => "coroutine";
        public override PyType GetPyType() => PyType.CoroutineType;

        internal static void CheckSendArguments(string typeName, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{typeName}.send() takes no keyword arguments");
            if (args.Length != 1)
                throw PyTypeError.Create($"{typeName}.send() takes exactly one argument ({args.Length} given)");
        }

        internal static void CheckCloseArguments(string typeName, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{typeName}.close() takes no keyword arguments");
            if (args.Length != 0)
                throw PyTypeError.Create($"{typeName}.close() takes no arguments ({args.Length} given)");
        }

        internal static void CheckThrowArguments(string typeName, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{typeName}.throw() takes no keyword arguments");
            if (args.Length < 1)
                throw PyTypeError.Create("throw expected at least 1 argument, got 0");
            if (args.Length > 3)
                throw PyTypeError.Create($"throw expected at most 3 arguments, got {args.Length}");
        }

        private readonly PyFrame _frame;
        private readonly PyVM _vm;
        private bool _started = false;
        private bool _finished = false;
        private bool _warningShown = false;

        public string Name { get; }
        public CoroutineState State { get; private set; } = CoroutineState.Created;
        public bool IsFinished => _finished;
        
        /// <summary>
        /// CPython 3.12 스타일 Coroutine 상태
        /// </summary>
        public enum CoroutineState
        {
            Created,     // CR_CREATED
            Running,     // CR_RUNNING  
            Suspended,   // CR_SUSPENDED
            Closed       // CR_CLOSED
        }
        
        public PyCoroutine(PyFrame frame, PyVM vm, string name)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            Name = name;

            // Coroutine frame은 특별한 플래그를 가짐
            _frame.IsGenerator = true; // 내부적으로는 generator 메커니즘 사용
            _frame.IsCoroutine = true; // 하지만 coroutine으로 마킹

            // Register for CPython 3.12 compatibility warnings
            CoroutineTracker.RegisterCoroutine(this);
        }

        /// <summary>
        /// CPython 3.12 style finalizer - warn if coroutine was never awaited
        /// </summary>
        ~PyCoroutine()
        {
            if (!_finished && !_warningShown)
            {
                _warningShown = true;
                // CPython 3.12에서와 동일한 경고 메시지
                Console.Error.WriteLine($"sys:1: RuntimeWarning: coroutine '{Name}' was never awaited");
            }
        }
        
        /// <summary>
        /// PEP 492: Native coroutine은 __await__ 메서드만 구현
        /// __iter__, __next__ 메서드는 구현하지 않음
        /// </summary>
        public PyIterator GetAwaiter()
        {
            return new PyCoroutineAwaiter(this);
        }
        
        /// <summary>
        /// CPython send() 메서드 구현
        /// </summary>
        public PyObject Send(PyObject? value = null)
        {
            if (State == CoroutineState.Running)
                throw PyValueError.Create("coroutine already executing");
            if (_finished)
                throw PyRuntimeError.Create("cannot reuse already awaited coroutine");
            if (!_started && value != null && value != PyNone.Instance)
                throw PyTypeError.Create("can't send non-None value to a just-started coroutine");

            // gen_send_ex2 supplies a value on both initial entry and resume.
            // YIELD_VALUE leaves SharpPy's instruction pointer at the yield.
            if (!_started)
                _frame.InstructionPointer = 0;
            else if (_frame.PendingException == null)
                _frame.InstructionPointer++;
            _frame.ValueStack.Push(value ?? PyNone.Instance);
            _started = true;
            _frame.State = PyFrame.FrameState.Executing;
            State = CoroutineState.Running;
            PyObject result;
            try
            {
                result = _vm.ExecuteFrame(_frame) ?? PyNone.Instance;
            }
            catch (PyYieldException yieldEx)
            {
                _frame.State = PyFrame.FrameState.Suspended;
                State = CoroutineState.Suspended;
                return yieldEx.Value ?? PyNone.Instance;
            }
            catch (PythonException)
            {
                Complete();
                throw;
            }

            Complete();
            throw PyStopIteration.Create(result);
        }
        
        /// <summary>
        /// CPython close() 메서드 구현
        /// </summary>
        public void Close()
        {
            if (_finished) return;
            if (!_started)
            {
                Complete();
                return;
            }

            var closeError = CloseReceiver(GetAwaitReceiver());

            // Keep the yield instruction as the exception-table lookup site.
            if (State == CoroutineState.Running)
                throw PyValueError.Create("coroutine already executing");
            _frame.PendingException = closeError ?? new PythonException(new PyGeneratorExit());
            try
            {
                Send(PyNone.Instance);
            }
            catch (PythonException error) when (error.PyException is PyGeneratorExit or PyStopIteration)
            {
                return;
            }
            // A coroutine which yields during close remains suspended.
            throw PyRuntimeError.Create("coroutine ignored GeneratorExit");
        }

        private PyObject GetAwaitReceiver()
        {
            if (!_started || _finished) return null;
            int resumeIndex = _frame.InstructionPointer + 1;
            return resumeIndex < _frame.Code.Instructions.Count &&
                _frame.Code.Instructions[resumeIndex].OpCode == ByteCodeOp.RESUME &&
                _frame.Code.Instructions[resumeIndex].Argument >= 2
                ? _frame.ValueStack.Peek() : null;
        }

        private PythonException CloseReceiver(PyObject receiver)
        {
            if (receiver != null)
            {
                var savedState = State;
                var savedFrameState = _frame.State;
                State = CoroutineState.Running;
                _frame.State = PyFrame.FrameState.Executing;
                try
                {
                    if (receiver is PyGenerator generator)
                        generator.Close();
                    else if (receiver is PyCoroutineAwaiter wrapper &&
                             receiver.GetType() == typeof(PyCoroutineAwaiter))
                        wrapper.Close();
                    else
                    {
                        PyObject close = null;
                        try { close = receiver.GetAttribute("close"); }
                        catch (PythonException error) when (error.PyException is PyAttributeError) { }
                        catch (PythonException error) { PyUnraisable.Report(error, receiver); }
                        close?.Call(Array.Empty<PyObject>(), null);
                    }
                }
                catch (PythonException error)
                {
                    return error;
                }
                finally
                {
                    State = savedState;
                    _frame.State = savedFrameState;
                }
            }

            return null;
        }

        internal PyObject Throw(PyObject[] args)
        {
            var receiver = GetAwaitReceiver();
            if (receiver != null)
            {
                var type = args[0] as PyType ?? args[0].GetPyType();
                if (type.IsSubclassOf(PyType.GeneratorExitType))
                {
                    var closeError = CloseReceiver(receiver);
                    if (closeError != null) return ResumeException(closeError);
                }
                else
                {
                    // Lookup failures leave the outer coroutine suspended; only errors
                    // from calling the delegate are injected at its await instruction.
                    bool nativeGenerator = receiver.GetType() == typeof(PyGenerator);
                    bool nativeWrapper = receiver.GetType() == typeof(PyCoroutineAwaiter);
                    PyObject method = null;
                    if (!nativeGenerator && !nativeWrapper)
                    {
                        try { method = receiver.GetAttribute("throw"); }
                        catch (PythonException error) when (error.PyException is PyAttributeError) { }
                    }
                    if (nativeGenerator || nativeWrapper || method != null)
                    {
                        var savedState = State;
                        var savedFrameState = _frame.State;
                        State = CoroutineState.Running;
                        _frame.State = PyFrame.FrameState.Executing;
                        PythonException delegateError;
                        try
                        {
                            if (nativeGenerator)
                                return ((PyGenerator)receiver).Throw(args[0],
                                    args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
                            if (nativeWrapper) return ((PyCoroutineAwaiter)receiver).Throw(args);
                            return method.Call(args, null);
                        }
                        catch (PythonException error) { delegateError = error; }
                        finally
                        {
                            State = savedState;
                            _frame.State = savedFrameState;
                        }
                        return ResumeException(delegateError);
                    }
                }
            }
            return ResumeException(PySuspensionException.Normalize(args[0],
                args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null));
        }

        private PyObject ResumeException(PythonException error)
        {
            if (State == CoroutineState.Running)
                throw PyValueError.Create("coroutine already executing");
            if (_finished)
                throw PyRuntimeError.Create("cannot reuse already awaited coroutine");
            _frame.PendingException = error;
            return Send(PyNone.Instance);
        }

        private void Complete()
        {
            State = CoroutineState.Closed;
            _finished = true;
            _frame.State = PyFrame.FrameState.Completed;
        }

        // Note: GetAttribute is inherited from PyObject, which uses GenericGetAttribute
        // All coroutine methods (send, throw, close, __await__) are registered as descriptors in PyType.CoroutineType

        public override string ToString()
        {
            var stateStr = State switch
            {
                CoroutineState.Created => "created",
                CoroutineState.Running => "running",
                CoroutineState.Suspended => "suspended",
                CoroutineState.Closed => "closed",
                _ => "unknown"
            };

            return $"<coroutine object {Name} at 0x{GetHashCode():x8} [{stateStr}]>";
        }
    }
    
    /// <summary>
    /// Coroutine의 __await__ 메서드가 반환하는 Awaiter
    /// </summary>
    public class PyCoroutineAwaiter : PyIterator
    {
        private static readonly PyType WrapperType = new PyType("coroutine_wrapper", new[] { PyType.ObjectType });

        static PyCoroutineAwaiter()
        {
            WrapperType.TypeDict["throw"] = new PyMethodDescriptor("throw", WrapperType,
                (self, args, kwargs) => {
                    if (self is not PyCoroutineAwaiter wrapper)
                        throw PyTypeError.Create($"descriptor 'throw' for 'coroutine_wrapper' objects doesn't apply to a '{self.GetTypeName()}' object");
                    PyCoroutine.CheckThrowArguments("coroutine_wrapper", args, kwargs);
                    return wrapper._coroutine.Throw(args);
                }, minArgs: 1, maxArgs: 3);
            WrapperType.TypeDict["close"] = new PyMethodDescriptor("close", WrapperType,
                (self, args, kwargs) => {
                    if (self is not PyCoroutineAwaiter wrapper)
                        throw PyTypeError.Create($"descriptor 'close' for 'coroutine_wrapper' objects doesn't apply to a '{self.GetTypeName()}' object");
                    PyCoroutine.CheckCloseArguments("coroutine_wrapper", args, kwargs);
                    wrapper.Close();
                    return PyNone.Instance;
                }, minArgs: 0, maxArgs: 0);
            WrapperType.TypeDict["send"] = new PyMethodDescriptor("send", WrapperType,
                (self, args, kwargs) => {
                    if (self is not PyCoroutineAwaiter wrapper)
                        throw PyTypeError.Create($"descriptor 'send' for 'coroutine_wrapper' objects doesn't apply to a '{self.GetTypeName()}' object");
                    PyCoroutine.CheckSendArguments("coroutine_wrapper", args, kwargs);
                    return wrapper.Send(args[0]);
                }, minArgs: 1, maxArgs: 1);
            WrapperType.TypeDict["__iter__"] = new PyMethodDescriptor("__iter__", WrapperType,
                (self, args, kwargs) => {
                    CheckIteratorArguments(self, args, kwargs, "__iter__");
                    return self;
                }, minArgs: 0, maxArgs: 0);
            WrapperType.TypeDict["__next__"] = new PyMethodDescriptor("__next__", WrapperType,
                (self, args, kwargs) => {
                    CheckIteratorArguments(self, args, kwargs, "__next__");
                    return ((PyCoroutineAwaiter)self).Next();
                }, minArgs: 0, maxArgs: 0);
        }

        private static void CheckIteratorArguments(PyObject self, PyObject[] args, PyDict kwargs, string name)
        {
            if (self is not PyCoroutineAwaiter)
                throw PyTypeError.Create($"descriptor '{name}' requires a 'coroutine_wrapper' object but received a '{self.GetTypeName()}'");
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"wrapper {name}() takes no keyword arguments");
            if (args.Length != 0)
                throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
        }

        private readonly PyCoroutine _coroutine;

        public override string GetTypeName() => "coroutine_wrapper";
        public override PyType GetPyType() => WrapperType;
        public override bool IsCallable() => false;
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
            => throw PyTypeError.Create("'coroutine_wrapper' object is not callable");

        public PyObject Send(PyObject value) => _coroutine.Send(value);
        public void Close() => _coroutine.Close();
        internal PyObject Throw(PyObject[] args) => _coroutine.Throw(args);
        
        public PyCoroutineAwaiter(PyCoroutine coroutine)
        {
            _coroutine = coroutine;
        }
        
        public override PyObject Next()
        {
            return _coroutine.Send(null);
        }
        
        public override string ToString()
        {
            return $"<coroutine_wrapper at 0x{GetHashCode():x8}>";
        }
    }
}
