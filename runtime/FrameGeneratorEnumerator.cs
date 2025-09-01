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
        private PyObject _current;
        private bool _finished;

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

            // 간단한 구현: 한 번만 실행하고 종료
            _finished = true;
            _current = PyNone.Instance;
            return false;
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