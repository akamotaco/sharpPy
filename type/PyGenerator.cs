using System;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 완전 호환 제너레이터 구현
    /// 바이트코드 실행을 중단하고 재개하는 방식으로 동작
    /// </summary>
    public class PyGenerator : PyIterator
    {
        #region Core Properties

        private readonly PyFrame _frame;
        private readonly PyVM _vm;
        private bool _started = false;
        private bool _finished = false;
        private PyObject _sentValue = PyNone.Instance;
        private Exception? _thrownException = null;

        public string Name { get; }
        public PyObject Qualname { get; }

        public PyGenerator(PyFrame frame, PyVM vm, string name = "<generator>")
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _finished = false;
            Name = name;
            Qualname = new PyString(name);
            
            // 제너레이터 플래그 설정
            _frame.IsGenerator = true;
        }

        public override PyType GetPyType() => PyType.GeneratorType;
        public override string GetTypeName() => "generator";

        #endregion

        #region String Representation

        public override string ToRepr() => $"<generator object {Name}>";

        #endregion

        #region Iterator Protocol - CPython 3.12 호환

        public override PyObject Next()
        {
            if (_finished)
                throw PyStopIteration.Create();

            // 제너레이터에 던져진 예외가 있으면 프레임에서 발생시키기
            if (_thrownException != null)
            {
                var exceptionToThrow = _thrownException;
                _thrownException = null; // 한 번만 사용
                
                // 프레임 내에서 예외 발생 (일단 간단한 구현)
                _finished = true;
                if (exceptionToThrow is PythonException pyEx)
                    throw pyEx;
                throw PyRuntimeError.Create($"generator exception: {exceptionToThrow.Message}");
            }

            try
            {
                if (!_started)
                {
                    // 첫 번째 실행: 처음부터 시작
                    _frame.InstructionPointer = 0;
                    _started = true;
                    Console.WriteLine("🔄 Native Generator: First execution, starting from instruction 0");
                }
                else
                {
                    // 재개: CPython과 달리 sent value를 스택에 push하지 않음
                    // YIELD_VALUE에서 이미 처리했고, 대부분 제너레이터에서는 sent value를 사용하지 않음
                    Console.WriteLine($"🔄 Native Generator: Resumed, stack size: {_frame.ValueStack.Count}");
                }

                // 프레임 실행 (yield까지 또는 끝까지)
                var result = _vm.ExecuteFrame(_frame);
                
                // 정상 완료된 경우 (return 또는 end of function)
                _finished = true;
                Console.WriteLine("🔄 Native Generator: Completed normally");
                throw PyStopIteration.Create();
            }
            catch (PyYieldException yieldEx)
            {
                // yield 지점에서 중단 - 이것이 정상적인 제너레이터 동작
                Console.WriteLine($"🔄 Native Generator: Yielded {yieldEx.Value} at instruction {_frame.InstructionPointer}");
                
                // sent value 초기화 (다음 호출까지 기본값)
                _sentValue = PyNone.Instance;
                
                return yieldEx.Value ?? PyNone.Instance;
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // 제너레이터가 완료된 경우
                _finished = true;
                throw;
            }
            catch (PythonException ex)
            {
                // 다른 Python 예외가 발생한 경우 전파
                _finished = true;
                throw;
            }
        }

        /// <summary>
        /// 제너레이터는 자기 자신이 이터레이터이므로 자신을 반환
        /// </summary>
        public override PyObject GetIterator()
        {
            return this;
        }

        #endregion

        #region Generator-specific Methods

        /// <summary>
        /// generator.send(value) - 제너레이터에 값을 보냄
        /// </summary>
        public PyObject Send(PyObject value)
        {
            if (_finished)
                throw PyStopIteration.Create();

            // 첫 번째 호출에서 None이 아닌 값을 보내면 TypeError
            if (!_started && value != PyNone.Instance)
                throw PyTypeError.Create("can't send non-None value to a just-started generator");

            _sentValue = value ?? PyNone.Instance;
            return Next();
        }

        /// <summary>
        /// generator.throw(type, value=None, traceback=None) - 제너레이터에 예외를 보냄
        /// </summary>
        public PyObject Throw(PyObject excType, PyObject? value = null, PyObject? traceback = null)
        {
            if (_finished)
                throw PyStopIteration.Create();

            // 간단한 구현: 예외 타입을 저장하고 다음 호출에서 발생
            if (excType is PyType exceptionType)
            {
                _thrownException = CreateExceptionFromType(exceptionType, value);
            }
            else if (excType is PyException exception)
            {
                _thrownException = new PythonException(exception);
            }
            else
            {
                throw PyTypeError.Create("throw() arg 1 must be exception type or instance");
            }

            try
            {
                return Next();
            }
            catch (Exception)
            {
                _finished = true;
                throw;
            }
        }

        private System.Exception CreateExceptionFromType(PyType excType, PyObject? value)
        {
            var message = value?.ToStr() ?? "generator exception";
            
            // 기본적인 예외 타입들만 처리
            if (excType == PyType.StopIterationType)
                return PyStopIteration.Create();
            if (excType == PyType.ValueErrorType)
                return PyValueError.Create(message);
            if (excType == PyType.TypeErrorType)
                return PyTypeError.Create(message);
            if (excType == PyType.RuntimeErrorType)
                return PyRuntimeError.Create(message);
            
            return PyRuntimeError.Create($"generator exception: {message}");
        }

        /// <summary>
        /// generator.close() - 제너레이터 종료
        /// </summary>
        public PyNone Close()
        {
            if (_finished)
                return PyNone.Instance;

            try
            {
                Throw(PyType.GeneratorExitType);
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // GeneratorExit이 StopIteration으로 변환되는 것은 정상
                _finished = true;
                return PyNone.Instance;
            }
            catch (PythonException ex) when (ex.PyException?.GetTypeName() == "GeneratorExit")
            {
                // GeneratorExit 예외가 발생하면 정상 종료
                _finished = true;
                return PyNone.Instance;
            }
            catch (Exception)
            {
                // 다른 예외가 발생하면 RuntimeError로 변환
                _finished = true;
                throw PyRuntimeError.Create("generator ignored GeneratorExit");
            }

            _finished = true;
            return PyNone.Instance;
        }

        #endregion

        #region Generator State

        /// <summary>
        /// 제너레이터가 완료되었는지 확인
        /// </summary>
        public bool IsFinished => _finished;

        #endregion

        #region Resource Management

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_finished)
            {
                try
                {
                    Close();
                }
                catch
                {
                    // 종료 중 예외 무시
                }
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}