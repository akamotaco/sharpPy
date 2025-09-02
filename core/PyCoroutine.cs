using System;
using System.Collections.Generic;

namespace SharpPy.Core
{
    /// <summary>
    /// CPython 3.12 Native Coroutine - PEP 492 구현
    /// Generator와 구별되는 별도 타입으로 __await__ 메서드만 구현
    /// </summary>
    public class PyCoroutine : PyObject
    {
        public override string GetTypeName() => "coroutine";
        
        private readonly PyFrame _frame;
        private readonly PyVM _vm;
        private bool _started = false;
        private bool _finished = false;
        private PyObject? _result = null;
        private int _lastInstructionPointer = 0;
        
        public string Name { get; }
        public CoroutineState State { get; private set; } = CoroutineState.Created;
        
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