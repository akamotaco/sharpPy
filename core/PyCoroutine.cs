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
            if (PyType.CoroutineType.Descriptors.Methods.Count > 0) return;

            var corType = PyType.CoroutineType;

            // send method descriptor
            corType.Descriptors.AddMethod("send", new PyMethodDescriptor(
                "send", corType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create("send() takes at most 1 argument");
                    if (self is not PyCoroutine coroutine)
                        throw PyTypeError.Create($"descriptor 'send' requires a 'coroutine' object but received a '{self.GetTypeName()}'");
                    return coroutine.Send(args.Length == 0 ? null : args[0]);
                },
                minArgs: 0, maxArgs: 1
            ));

            // close method descriptor
            corType.Descriptors.AddMethod("close", new PyMethodDescriptor(
                "close", corType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("close() takes no arguments");
                    if (self is not PyCoroutine coroutine)
                        throw PyTypeError.Create($"descriptor 'close' requires a 'coroutine' object but received a '{self.GetTypeName()}'");
                    coroutine.Close();
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 0
            ));

            // __await__ method descriptor
            corType.Descriptors.AddMethod("__await__", new PyMethodDescriptor(
                "__await__", corType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("__await__() takes no arguments");
                    if (self is not PyCoroutine coroutine)
                        throw PyTypeError.Create($"descriptor '__await__' requires a 'coroutine' object but received a '{self.GetTypeName()}'");
                    return coroutine.GetAwaiter();
                },
                minArgs: 0, maxArgs: 0
            ));
        }

        public override string GetTypeName() => "coroutine";
        public override PyType GetPyType() => PyType.CoroutineType;

        private readonly PyFrame _frame;
        private readonly PyVM _vm;
        private bool _started = false;
        private bool _finished = false;
        private PyObject? _result = null;
        private int _lastInstructionPointer = 0;
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
            if (_finished)
            {
                throw PyStopIteration.Create(new PyString("coroutine already finished"));
            }
            
            try
            {
                if (!_started)
                {
                    // 첫 번째 실행
                    _frame.InstructionPointer = 0;
                    _frame.State = PyFrame.FrameState.Executing;
                    State = CoroutineState.Running;
                    _started = true;
                }
                else
                {
                    // 재개 실행
                    _frame.InstructionPointer = _lastInstructionPointer;
                    _frame.State = PyFrame.FrameState.Executing;
                    State = CoroutineState.Running;
                    
                    // yield가 있었다면 값을 스택에 푸시
                    if (value != null)
                    {
                        _frame.ValueStack.Push(value);
                    }
                }
                
                var result = _vm.ExecuteFrame(_frame);
                
                // 정상 완료
                _frame.State = PyFrame.FrameState.Completed;
                State = CoroutineState.Closed;
                _finished = true;
                _result = result ?? PyNone.Instance;
                
                throw PyStopIteration.Create(_result);
            }
            catch (PyYieldException yieldEx)
            {
                // yield로 인한 일시 중단
                _frame.State = PyFrame.FrameState.Suspended;
                State = CoroutineState.Suspended;
                _lastInstructionPointer = _frame.InstructionPointer;
                
                return yieldEx.Value ?? PyNone.Instance;
            }
        }
        
        /// <summary>
        /// CPython close() 메서드 구현
        /// </summary>
        public void Close()
        {
            if (_finished) return;
            
            State = CoroutineState.Closed;
            _finished = true;
            _frame.State = PyFrame.FrameState.Completed;
        }

        // Note: GetAttribute is inherited from PyObject, which uses GenericGetAttribute
        // All coroutine methods (send, close, __await__) are registered as descriptors in PyType.CoroutineType

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
        private readonly PyCoroutine _coroutine;
        
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