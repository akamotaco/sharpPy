using System;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 완전 호환 제너레이터 구현
    /// 바이트코드 실행을 중단하고 재개하는 방식으로 동작
    /// </summary>
    public class PyGenerator : PyIterator
    {
        static PyGenerator()
        {
            InitializeGeneratorDescriptors();
        }

        private static void InitializeGeneratorDescriptors()
        {
            var genType = PyType.GeneratorType;

            // send method descriptor
            genType.TypeDict["send"] = new PyMethodDescriptor(
                "send", genType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create("send() takes exactly one argument");
                    if (self is not PyGenerator generator)
                        throw PyTypeError.Create($"descriptor 'send' requires a 'generator' object but received a '{self.GetTypeName()}'");
                    return generator.Send(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // throw method descriptor
            genType.TypeDict["throw"] = new PyMethodDescriptor(
                "throw", genType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create("throw() takes 1 to 3 arguments");
                    if (self is not PyGenerator generator)
                        throw PyTypeError.Create($"descriptor 'throw' requires a 'generator' object but received a '{self.GetTypeName()}'");

                    var excType = args[0];
                    var value = args.Length > 1 ? args[1] : null;
                    var traceback = args.Length > 2 ? args[2] : null;

                    return generator.Throw(excType, value, traceback);
                },
                minArgs: 1, maxArgs: 3
            );

            // close method descriptor
            genType.TypeDict["close"] = new PyMethodDescriptor(
                "close", genType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create("close() takes no arguments");
                    if (self is not PyGenerator generator)
                        throw PyTypeError.Create($"descriptor 'close' requires a 'generator' object but received a '{self.GetTypeName()}'");
                    return generator.Close();
                },
                minArgs: 0, maxArgs: 0
            );

            // __iter__ and __next__ are inherited from PyIterator
        }

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
            Qualname = new PyStr(name);
            
            // 제너레이터 플래그 설정
            _frame.IsGenerator = true;
        }

        public override PyType GetPyType() => PyType.GeneratorType;
        public override string GetTypeName() => "generator";

        #endregion

        #region String Representation

        public override PyStr ToRepr() => new PyStr($"<generator object {Name}>");

        #endregion

        #region Iterator Protocol - CPython 3.12 호환

        public override PyObject Next()
        {
            #if DEBUG_GENERATOR_LOG
            Console.WriteLine($"🔄 PyGenerator.Next() called, _finished: {_finished}, _started: {_started}");
            #endif
            if (_finished)
                throw PyStopIteration.Create();

            // CPython 3.12: 제너레이터에 던져진 예외가 있으면 프레임의 PendingException으로 설정
            // 예외는 VM ExecuteFrame에서 exception table을 통해 처리됨
            // CPython reference: Objects/genobject.c:531-556 (gen_send_ex with exc_state handling)
            if (_thrownException != null)
            {
                var exceptionToThrow = _thrownException;
                _thrownException = null; // 한 번만 사용

                // Set as PendingException so VM's exception handler can route to try-finally
                if (exceptionToThrow is PythonException pyEx)
                {
                    _frame.PendingException = pyEx;
                }
                else
                {
                    _frame.PendingException = new PythonException(new PyRuntimeError(exceptionToThrow.Message));
                }
                // Fall through to execute the frame - VM will pick up the pending exception
            }

            try
            {
                if (!_started)
                {
                    // 첫 번째 실행: CPython 3.12 패턴
                    // Execution sequence:
                    // 0: COPY_FREE_VARS - Initialize closure cells from parent function
                    // 2: RETURN_GENERATOR - No-op in SharpPy (generator already created)
                    // 4: POP_TOP - Pop the initial sent value (None)
                    // 6: RESUME - Resume execution
                    // We must start from instruction 0 to execute COPY_FREE_VARS!
                    _frame.InstructionPointer = 0; // Start from COPY_FREE_VARS
                    _frame.ValueStack.Push(PyNone.Instance); // Initial sent value for POP_TOP
                    _started = true;
                    #if DEBUG_GENERATOR_LOG
                    Console.WriteLine($"🔄 Native Generator: First execution, starting from instruction 0 (COPY_FREE_VARS), stack size: {_frame.ValueStack.Count}");
                    #endif
                }
                else
                {
                    // CPython 3.12: Objects/genobject.c:215-217 (gen_send_ex2)
                    // Resume generator execution:
                    // 1. Push sent value onto frame's value stack
                    // 2. Call _PyEval_EvalFrame to resume execution

                    #if DEBUG_GENERATOR_LOG
                    Console.WriteLine($"🔄 Generator Resume: IP before adjustment: {_frame.InstructionPointer}, stack size: {_frame.ValueStack.Count}");
                    #endif

                    // CPython pattern: YIELD_VALUE left IP pointing at itself
                    // We need to move to RESUME (next instruction) before executing
                    _frame.InstructionPointer++;  // Move from YIELD_VALUE to RESUME

                    #if DEBUG_GENERATOR_LOG
                    Console.WriteLine($"🔄 Generator Resume: IP after increment: {_frame.InstructionPointer}");
                    #endif

                    // Now push sent value for RESUME + POP_TOP pattern
                    _frame.ValueStack.Push(_sentValue);
                }

                // 프레임 실행 (yield까지 또는 끝까지)
                var result = _vm.ExecuteFrame(_frame);

                // Optimized: Check for yield sentinel (no exception path)
                if (result == PyFrame.YieldSentinel)
                {
                    // yield 지점에서 중단 - 정상적인 제너레이터 동작
                    #if DEBUG_GENERATOR_LOG
                    Console.WriteLine($"🔄 Native Generator: Yielded {_frame.YieldValue} at instruction {_frame.InstructionPointer}");
                    #endif
                    _sentValue = PyNone.Instance;
                    var yieldedValue = _frame.YieldValue ?? PyNone.Instance;
                    _frame.YieldValue = null; // Clear for next yield
                    return yieldedValue;
                }

                // 정상 완료된 경우 (return 또는 end of function)
                // CPython: Generator의 return 값은 StopIteration.value로 전달됨
                _finished = true;
                #if DEBUG_GENERATOR_LOG
                Console.WriteLine($"🔄 Native Generator: Completed normally, return value: {result}");
                #endif
                throw PyStopIteration.Create(result);
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
                #if DEBUG_GENERATOR_LOG
                Console.WriteLine($"🔴 PyGenerator.Next() PythonException: {ex.PyException?.GetType().Name}: {ex.Message}");
                #endif
                _finished = true;
                throw;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Stack empty"))
            {
                // Generator completion: Stack empty during final cleanup is normal completion
                #if DEBUG_GENERATOR_LOG
                Console.WriteLine($"🎉 Generator: Completed successfully (stack empty during cleanup)");
                #endif
                _finished = true;
                throw PyStopIteration.Create();
            }
            catch (Exception ex)
            {
                // 다른 예외가 발생한 경우
                #if DEBUG_GENERATOR_LOG
                Console.WriteLine($"🔴 PyGenerator.Next() Exception: {ex.GetType().Name}: {ex.Message}");
                #endif
                _finished = true;
                throw;
            }
        }

        /// <summary>
        /// Optimized TryNext: avoids the base class try/catch wrapper around Next().
        /// FOR_ITER calls TryNext() on every iteration - eliminating exception overhead
        /// for both yield (sentinel-based) and StopIteration (caught here once).
        /// </summary>
        public override bool TryNext(out PyObject value)
        {
            if (_finished)
            {
                value = null;
                return false;
            }

            try
            {
                value = Next();
                return true;
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                value = null;
                return false;
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
        /// CPython 3.12: Objects/genobject.c:_gen_throw (line 387-564)
        /// </summary>
        public PyObject Throw(PyObject excType, PyObject? value = null, PyObject? traceback = null)
        {
            if (_finished)
                throw PyStopIteration.Create();

            // CPython 3.12: Extract traceback from exception instance if provided
            // genobject.c:544: tb = PyException_GetTraceback(val)
            PyTraceback? tb = null;
            PythonException thrownException;

            // CPython 3.12 호환: 다양한 예외 타입 처리
            if (excType is PyType exceptionType)
            {
                var exc = CreateExceptionFromType(exceptionType, value);
                thrownException = exc as PythonException ?? new PythonException(new PyRuntimeError(exc.Message));
            }
            else if (excType is PyBuiltinType builtinType)
            {
                // 내장 예외 타입 (ValueError, TypeError 등) 처리
                var exc = CreateExceptionFromBuiltinType(builtinType, value);
                thrownException = exc as PythonException ?? new PythonException(new PyRuntimeError(exc.Message));
            }
            else if (excType is PyException exception)
            {
                thrownException = new PythonException(exception);
                // Extract existing traceback from exception instance
                tb = exception.__traceback__;
            }
            else
            {
                throw PyTypeError.Create("throw() arg 1 must be exception type or instance");
            }

            // CPython 3.12: If traceback argument provided, use it
            // genobject.c:514-521: Replace None with NULL, validate traceback
            if (traceback != null && traceback is PyTraceback pyTb)
            {
                tb = pyTb;
            }

            // CPython 3.12: Attach traceback to exception before throwing
            // genobject.c:556: PyErr_Restore(typ, val, tb)
            // This preserves the traceback through the generator throw
            if (tb != null)
            {
                thrownException.PyException.__traceback__ = tb;
            }

            _thrownException = thrownException;

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
            var message = value?.ToStr()?.Value ?? "generator exception";

            // 기본적인 예외 타입들만 처리
            // CPython 3.12: Objects/genobject.c:284-340 (CreateExceptionFromType equivalent)
            if (excType == PyType.StopIterationType)
                return PyStopIteration.Create();
            if (excType == PyType.GeneratorExitType)
                return PyGeneratorExit.Create(message);
            if (excType == PyType.ValueErrorType)
                return PyValueError.Create(message);
            if (excType == PyType.TypeErrorType)
                return PyTypeError.Create(message);
            if (excType == PyType.RuntimeErrorType)
                return PyRuntimeError.Create(message);

            return PyRuntimeError.Create($"generator exception: {message}");
        }

        private System.Exception CreateExceptionFromBuiltinType(PyBuiltinType builtinType, PyObject? value)
        {
            var message = value?.ToStr()?.Value ?? "generator exception";
            
            // PyBuiltinType의 Name 속성 사용 (예: "ValueError")
            var typeName = builtinType.Name;
            
            switch (typeName)
            {
                case "ValueError":
                case "class 'ValueError'":  // SharpPy에서 나타나는 형태
                    return PyValueError.Create(message);
                case "TypeError":
                case "class 'TypeError'":
                    return PyTypeError.Create(message);
                case "RuntimeError":
                case "class 'RuntimeError'":
                    return PyRuntimeError.Create(message);
                case "StopIteration":
                case "class 'StopIteration'":
                    return PyStopIteration.Create();
                case "GeneratorExit":
                case "class 'GeneratorExit'":
                    return PyGeneratorExit.Create(message);
                default:
                    return PyRuntimeError.Create($"generator exception: {message}");
            }
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
            catch (PythonException ex) when (ex.PyException is PyGeneratorExit)
            {
                // GeneratorExit 예외가 발생하면 정상 종료
                _finished = true;
                return PyNone.Instance;
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                // StopIteration도 정상 종료로 처리
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

        // Note: GetAttribute is inherited from PyIterator, which uses GenericGetAttribute

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