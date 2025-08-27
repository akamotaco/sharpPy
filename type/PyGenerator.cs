using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python generator 타입 구현 - yield를 사용한 제너레이터
    /// </summary>
    public class PyGenerator : PyIterator
    {
        #region Core Properties

        private readonly IEnumerator<PyObject> _enumerator;
        private bool _finished;
        private PyObject _sentValue;
        private Exception _thrownException;

        public string Name { get; }
        public PyObject Qualname { get; }

        public PyGenerator(IEnumerator<PyObject> enumerator, string name = "<generator>")
        {
            _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
            _finished = false;
            Name = name;
            Qualname = new PyString(name);
        }

        public override PyType GetPyType() => PyType.GeneratorType;
        public override string GetTypeName() => "generator";

        #endregion

        #region String Representation

        public override string ToRepr() => $"<generator object {Name}>";

        #endregion

        #region Iterator Protocol

        public override PyObject Next()
        {
            if (_finished)
                throw PyStopIteration.Create();

            try
            {
                if (!_enumerator.MoveNext())
                {
                    _finished = true;
                    throw PyStopIteration.Create();
                }
                
                return _enumerator.Current;
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                _finished = true;
                throw;
            }
            catch (Exception ex)
            {
                _finished = true;
                throw PyRuntimeError.Create($"generator raised {ex.GetType().Name}: {ex.Message}");
            }
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

            _sentValue = value ?? PyNone.Instance;
            return Next();
        }

        /// <summary>
        /// generator.throw(type, value=None, traceback=None) - 제너레이터에 예외를 보냄
        /// </summary>
        public PyObject Throw(PyObject excType, PyObject value = null, PyObject traceback = null)
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

        private System.Exception CreateExceptionFromType(PyType excType, PyObject value)
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
            catch (PythonException ex) when (ex.PyException is PyGeneratorExit)
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

        /// <summary>
        /// 제너레이터에 보낸 값 가져오기 (yield 표현식의 값)
        /// </summary>
        protected PyObject GetSentValue()
        {
            var value = _sentValue ?? PyNone.Instance;
            _sentValue = null; // 한 번만 사용
            return value;
        }

        /// <summary>
        /// 제너레이터에 던진 예외 가져오기
        /// </summary>
        protected Exception GetThrownException()
        {
            var exception = _thrownException;
            _thrownException = null; // 한 번만 사용
            return exception;
        }

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
                
                _enumerator?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion

        #region Generator Helpers

        /// <summary>
        /// 간단한 값 시퀀스로부터 제너레이터 생성
        /// </summary>
        public static PyGenerator FromSequence(IEnumerable<PyObject> sequence, string name = "<generator>")
        {
            return new PyGenerator(sequence.GetEnumerator(), name);
        }

        /// <summary>
        /// 함수로부터 제너레이터 생성 (간단한 구현)
        /// </summary>
        public static PyGenerator FromFunction(Func<IEnumerable<PyObject>> generatorFunction, string name = "<generator>")
        {
            return new PyGenerator(generatorFunction().GetEnumerator(), name);
        }

        #endregion
    }

    /// <summary>
    /// GeneratorExit 예외 - generator.close()에서 사용
    /// </summary>
    public class PyGeneratorExit : PyException
    {
        public PyGeneratorExit(string message = "") : base(message) { }
        
        public static PyGeneratorExit Create(string message = "generator exit")
        {
            return new PyGeneratorExit(message);
        }

        public override PyType GetPyType() => PyType.GeneratorExitType;
        public override string GetTypeName() => "GeneratorExit";
    }

    /// <summary>
    /// 제너레이터 표현식 구현을 위한 헬퍼
    /// </summary>
    public static class GeneratorHelpers
    {
        /// <summary>
        /// 리스트 컴프리헨션을 제너레이터 표현식으로 변환
        /// </summary>
        public static PyGenerator ListToGenerator<T>(IEnumerable<T> source, Func<T, PyObject> selector)
        {
            return PyGenerator.FromSequence(source.Select(selector));
        }

        /// <summary>
        /// 조건부 제너레이터 생성
        /// </summary>
        public static PyGenerator ConditionalGenerator<T>(IEnumerable<T> source, Func<T, bool> predicate, Func<T, PyObject> selector)
        {
            return PyGenerator.FromSequence(source.Where(predicate).Select(selector));
        }

        /// <summary>
        /// range() 함수의 제너레이터 버전
        /// </summary>
        public static PyGenerator RangeGenerator(int start, int stop, int step = 1)
        {
            return PyGenerator.FromFunction(() => GenerateRange(start, stop, step));
        }

        private static IEnumerable<PyObject> GenerateRange(int start, int stop, int step)
        {
            if (step > 0)
            {
                for (int i = start; i < stop; i += step)
                    yield return new PyInt(i);
            }
            else if (step < 0)
            {
                for (int i = start; i > stop; i += step)
                    yield return new PyInt(i);
            }
            else
            {
                throw PyValueError.Create("range() step argument must not be zero");
            }
        }
    }
}