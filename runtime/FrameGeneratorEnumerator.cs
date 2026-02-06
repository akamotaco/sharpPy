using System;
using System.Collections;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Frame을 실행하는 제너레이터 Enumerator
    /// </summary>
    public class FrameGeneratorEnumerator : IEnumerator<PyObject>
    {
        private readonly PyFrame _frame;
        private readonly PyVM _vm;
        private PyObject _current = PyNone.Instance;
        private bool _finished;
        private bool _started = false;
        private int _lastInstructionPointer = 0;
        private PyStack _savedStack = null;

        public FrameGeneratorEnumerator(PyFrame frame, PyVM vm)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        }

        public PyObject Current => _current;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_finished)
                return false;

            try
            {
                // CPython 3.12 방식: frame state 관리
                if (!_started)
                {
                    // 첫 번째 실행: FRAME_CREATED → FRAME_EXECUTING
                    _frame.InstructionPointer = 0;
                    _frame.State = PyFrame.FrameState.Executing;
                    _started = true;
#if DEBUG_VM_LOG
                    Console.WriteLine("🔄 Generator: First execution, starting from instruction 0");
#endif
                }
                else
                {
                    // 재개: FRAME_SUSPENDED → FRAME_EXECUTING with stack restoration
                    _frame.InstructionPointer = _lastInstructionPointer;
                    _frame.State = PyFrame.FrameState.Executing;
                    
                    // Restore stack state from previous yield
                    if (_savedStack != null)
                    {
                        _frame.ValueStack.Clear();
                        // Optimized: Direct iteration via GetInternalList() avoids intermediate Stack<T> allocation.
                        // PyStack stores items bottom-to-top internally (TOS at end).
                        // Iterating forward and Push()ing restores the original stack order.
                        var items = _savedStack.GetInternalList();
                        for (int i = 0; i < items.Count; i++)
                        {
                            _frame.ValueStack.Push(items[i]);
                        }
#if DEBUG_VM_LOG
                        Console.WriteLine($"🔄 Generator: Resumed with stack size {_frame.ValueStack.Count} at instruction {_lastInstructionPointer}");
#endif
                    }
                }

                // Frame을 부분적으로 실행 (yield까지 또는 끝까지)
                var result = _vm.ExecuteFrame(_frame);
                
                // 정상 완료된 경우 (return 또는 end of function)
                _frame.State = PyFrame.FrameState.Completed;
                _finished = true;
                _current = result ?? PyNone.Instance;
                return false;
            }
            catch (PyYieldException yieldEx)
            {
                // yield 지점에서 중단: FRAME_EXECUTING → FRAME_SUSPENDED with stack preservation
                _frame.State = PyFrame.FrameState.Suspended;
                _lastInstructionPointer = _frame.InstructionPointer;
                
                // Save current stack state for restoration on resume
                // O(n) optimized clone using List.AddRange
                _savedStack = _frame.ValueStack.Clone();

#if DEBUG_VM_LOG
                Console.WriteLine($"🔄 Generator: Yielded {yieldEx.Value} at instruction {_lastInstructionPointer}, saved stack size {_savedStack.Count}");
#endif
                _current = yieldEx.Value ?? PyNone.Instance;
                return true;
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // generator가 완료된 경우
                _frame.State = PyFrame.FrameState.Completed;
                _finished = true;
                return false;
            }
            catch (PythonException ex)
            {
                // 다른 Python 예외가 발생한 경우 전파
                _frame.State = PyFrame.FrameState.Completed;
                _finished = true;
                throw;
            }
        }

        public void Reset()
        {
            throw new NotSupportedException("Generator enumerator cannot be reset");
        }

        public void Dispose()
        {
            _finished = true;
        }
    }
}