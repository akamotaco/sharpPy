using System;

namespace SharpPy
{
    /// <summary>
    /// Python/legacy_tracing.c: sys.settrace and sys.setprofile on top of PEP 669, as the tools
    /// PY_MONITORING_SYS_TRACE_ID and PY_MONITORING_SYS_PROFILE_ID with _PyLegacyEventHandler
    /// callbacks; and the trampolines of Python/sysmodule.c that call the Python functions.
    /// The thread state's c_traceobj and c_profileobj are PyVM.TraceFunction and ProfileFunction
    /// (c_tracefunc is trace_trampoline exactly when c_traceobj is set).
    /// </summary>
    internal static class PyLegacyTracing
    {
        // Include/cpython/pystate.h
        internal const int PyTrace_CALL = 0, PyTrace_EXCEPTION = 1, PyTrace_LINE = 2, PyTrace_RETURN = 3,
            PyTrace_C_CALL = 4, PyTrace_C_EXCEPTION = 5, PyTrace_C_RETURN = 6, PyTrace_OPCODE = 7;

        // whatstrings (Python/sysmodule.c)
        private static readonly PyString[] WhatStrings =
        {
            PyString.From("call"), PyString.From("exception"), PyString.From("line"), PyString.From("return"),
            PyString.From("c_call"), PyString.From("c_exception"), PyString.From("c_return"), PyString.From("opcode"),
        };

        // interp->sys_profile_initialized, sys_trace_initialized, sys_profiling_threads,
        // sys_tracing_threads, f_opcode_trace_set
        private static bool _sysProfileInitialized, _sysTraceInitialized;
        private static int _sysProfilingThreads, _sysTracingThreads;
        internal static bool FOpcodeTraceSet;

        /// <summary>_PyLegacyEventHandler_Type, "sys.legacy_event_handler": not instantiable from Python.</summary>
        internal static readonly PyType LegacyEventHandlerType = MakeType();

        private static PyType MakeType()
        {
            var type = new PyType("legacy_event_handler", new[] { PyType.ObjectType }, "sys");
            type.TypeDict["__call__"] = new PyWrapperDescriptor("__call__", type, (self, args, kwargs) => ((LegacyEventHandler)self).Call(args, kwargs));
            return type;
        }

        /// <summary>_PyLegacyEventHandler: a vectorcall function and the legacy event it reports.</summary>
        private sealed class LegacyEventHandler : PyObject
        {
            private readonly Func<LegacyEventHandler, PyObject[], PyObject> _vectorcall;
            internal readonly int Event;

            internal LegacyEventHandler(Func<LegacyEventHandler, PyObject[], PyObject> vectorcall, int legacyEvent)
            {
                _vectorcall = vectorcall;
                Event = legacyEvent;
            }

            public override PyType GetPyType() => LegacyEventHandlerType;
            public override bool IsCallable() => true;
            public override PyObject Call(PyObject[] args, PyDict kwargs) => _vectorcall(this, args);
        }

        // PyEval_GetFrame
        private static PyFrame GetFrame(string what)
        {
            var frame = PyVM.CurrentFrame;
            if (frame == null)
                throw PySystemError.Create($"Missing frame when calling {what} function.");
            return frame;
        }

        // call_profile_func
        private static PyObject CallProfileFunc(LegacyEventHandler self, PyObject arg)
        {
            var vm = PyVM.Instance;
            if (vm.ProfileFunction == null)
                return PyNone.Instance;
            var frame = GetFrame("profile");
            ProfileTrampoline(vm.ProfileFunction, frame, self.Event, arg);
            return PyNone.Instance;
        }

        // sys_profile_func2
        private static PyObject SysProfileFunc2(LegacyEventHandler self, PyObject[] args) => CallProfileFunc(self, PyNone.Instance);

        // sys_profile_func3
        private static PyObject SysProfileFunc3(LegacyEventHandler self, PyObject[] args) => CallProfileFunc(self, args[2]);

        // sys_profile_unwind
        private static PyObject SysProfileUnwind(LegacyEventHandler self, PyObject[] args) => CallProfileFunc(self, PyNone.Instance);

        // PyCFunction_Check: builtin_function_or_method or a subclass of it.
        internal static bool IsCFunction(PyObject callable) => callable.GetPyType().IsSubclassOf(PyType.BuiltinFunctionOrMethodType);

        // sys_profile_call_or_return
        private static PyObject SysProfileCallOrReturn(LegacyEventHandler self, PyObject[] args)
        {
            var callable = args[2];
            if (IsCFunction(callable))
                return CallProfileFunc(self, callable);
            var type = callable.GetPyType();
            if (ReferenceEquals(type, PyType.MethodDescriptorType))
            {
                var selfArg = args[3];
                // For backwards compatibility need to convert to builtin method. If no arg, skip.
                if (ReferenceEquals(selfArg, PyInstrumentation.MISSING))
                    return PyNone.Instance;
                var meth = ((IDescriptor)callable).Get(selfArg, selfArg.GetPyType());
                return CallProfileFunc(self, meth);
            }
            else if (ReferenceEquals(type, PyType.MethodType))
            {
                // CALL instruction will grab the function from the method, so if the function is a C
                // function, the return event will be emitted. However, CALL event happens before
                // CALL instruction, so we need to handle this case here.
                var func = ((PyMethod)callable).Function;
                if (IsCFunction(func))
                    return CallProfileFunc(self, func);
            }
            return PyNone.Instance;
        }

        // call_trace_func
        private static PyObject CallTraceFunc(LegacyEventHandler self, PyObject arg)
        {
            var vm = PyVM.Instance;
            if (vm.TraceFunction == null)
                return PyNone.Instance;
            var frame = GetFrame("trace");
            TraceTrampoline(vm.TraceFunction, frame, self.Event, arg);
            return PyNone.Instance;
        }

        // sys_trace_exception_func
        private static PyObject SysTraceExceptionFunc(LegacyEventHandler self, PyObject[] args)
        {
            var exc = args[2];
            var type = exc.GetPyType();
            PyObject tb = (exc as PyBaseException)?.__traceback__ ?? (PyObject)PyNone.Instance;
            return CallTraceFunc(self, PyTuple.From(new[] { type, exc, tb }));
        }

        // sys_trace_func2, sys_trace_func3
        private static PyObject SysTraceFunc2(LegacyEventHandler self, PyObject[] args) => CallTraceFunc(self, PyNone.Instance);

        private static PyObject SysTraceFunc3(LegacyEventHandler self, PyObject[] args) => CallTraceFunc(self, PyNone.Instance);

        // sys_trace_return
        private static PyObject SysTraceReturn(LegacyEventHandler self, PyObject[] args) => CallTraceFunc(self, args[2]);

        // sys_trace_yield
        private static PyObject SysTraceYield(LegacyEventHandler self, PyObject[] args) => CallTraceFunc(self, args[2]);

        // sys_trace_instruction_func
        private static PyObject SysTraceInstructionFunc(LegacyEventHandler self, PyObject[] args)
        {
            var frame = GetFrame("trace");
            if (!frame.TraceOpcodes)
                return PyNone.Instance;
            var vm = PyVM.Instance;
            try
            {
                if (vm.TraceFunction != null)
                    TraceTrampoline(vm.TraceFunction, frame, self.Event, PyNone.Instance);
            }
            finally
            {
                frame.FLineno = 0;
            }
            return PyNone.Instance;
        }

        // trace_line
        private static PyObject TraceLine(PyVM vm, LegacyEventHandler self, PyFrame frame, int line)
        {
            if (!frame.TraceLines)
                return PyNone.Instance;
            if (line < 0)
                return PyNone.Instance;
            frame.FLineno = line;
            try
            {
                TraceTrampoline(vm.TraceFunction, frame, self.Event, PyNone.Instance);
            }
            finally
            {
                frame.FLineno = 0;
            }
            return PyNone.Instance;
        }

        // sys_trace_line_func
        private static PyObject SysTraceLineFunc(LegacyEventHandler self, PyObject[] args)
        {
            var vm = PyVM.Instance;
            if (vm.TraceFunction == null)
                return PyNone.Instance;
            int line = PyArgs.Int(args[1]);
            var frame = GetFrame("trace");
            return TraceLine(vm, self, frame, line);
        }

        /// <summary>
        /// sys_trace_jump_func: sys.settrace generates line events for all backward edges, even if
        /// on the same line. Handle that case here.
        /// </summary>
        private static PyObject SysTraceJumpFunc(LegacyEventHandler self, PyObject[] args)
        {
            var vm = PyVM.Instance;
            if (vm.TraceFunction == null)
                return PyNone.Instance;
            int from = PyArgs.Int(args[1]) / PyCodeObject.INSTRUCTION_WORD_SIZE;
            int to = PyArgs.Int(args[2]) / PyCodeObject.INSTRUCTION_WORD_SIZE;
            if (to > from)
                // Forward jump
                return PyInstrumentation.DISABLE;
            var code = (PyCodeObject)args[0];
            // We can call _Py_Instrumentation_GetLine because we always set line events for tracing
            int toLine = PyInstrumentation.GetLine(code, to);
            int fromLine = PyInstrumentation.GetLine(code, from);
            if (toLine != fromLine)
                // Will be handled by target INSTRUMENTED_LINE
                return PyInstrumentation.DISABLE;
            var frame = GetFrame("trace");
            if (!frame.TraceLines)
                return PyNone.Instance;
            return TraceLine(vm, self, frame, toLine);
        }

        // set_callbacks
        private static void SetCallbacks(int tool, Func<LegacyEventHandler, PyObject[], PyObject> vectorcall, int legacyEvent, int event1, int event2)
        {
            var callback = new LegacyEventHandler(vectorcall, legacyEvent);
            PyInstrumentation.RegisterCallback(tool, event1, callback);
            if (event2 >= 0)
                PyInstrumentation.RegisterCallback(tool, event2, callback);
        }

        /// <summary>
        /// _PyEval_SetProfile: the callbacks of the profile tool once, then the thread's function,
        /// and the tool's events while any thread has one.
        /// </summary>
        /// <param name="unbound">Within PyGil.ForAllThreads, where every state holds its own fields.</param>
        internal static void SetProfile(PyThreadState tstate, PyObject func, bool unbound = false)
        {
            const int P = PyInstrumentation.SYS_PROFILE_ID;
            if (!_sysProfileInitialized)
            {
                _sysProfileInitialized = true;
                SetCallbacks(P, SysProfileFunc2, PyTrace_CALL, PyInstrumentation.PY_START, PyInstrumentation.PY_RESUME);
                SetCallbacks(P, SysProfileFunc3, PyTrace_CALL, PyInstrumentation.PY_THROW, -1);
                SetCallbacks(P, SysProfileFunc3, PyTrace_RETURN, PyInstrumentation.PY_RETURN, PyInstrumentation.PY_YIELD);
                SetCallbacks(P, SysProfileUnwind, PyTrace_RETURN, PyInstrumentation.PY_UNWIND, -1);
                SetCallbacks(P, SysProfileCallOrReturn, PyTrace_C_CALL, PyInstrumentation.CALL, -1);
                SetCallbacks(P, SysProfileCallOrReturn, PyTrace_C_RETURN, PyInstrumentation.C_RETURN, -1);
                SetCallbacks(P, SysProfileCallOrReturn, PyTrace_C_EXCEPTION, PyInstrumentation.C_RAISE, -1);
            }

            var vm = PyVM.Instance;
            bool bound = !unbound && ReferenceEquals(tstate, vm.BoundState);
            var old = bound ? vm.ProfileFunction : tstate.ProfileFunction;
            int delta = (func != null ? 1 : 0) - (old != null ? 1 : 0);
            if (bound)
                vm.ProfileFunction = func;
            else
                tstate.ProfileFunction = func;
            _sysProfilingThreads += delta;

            int events = 0;
            if (_sysProfilingThreads != 0)
                events = (1 << PyInstrumentation.PY_START) | (1 << PyInstrumentation.PY_RESUME) |
                    (1 << PyInstrumentation.PY_RETURN) | (1 << PyInstrumentation.PY_YIELD) |
                    (1 << PyInstrumentation.CALL) | (1 << PyInstrumentation.PY_UNWIND) |
                    (1 << PyInstrumentation.PY_THROW);
            PyInstrumentation.MonitoringSetEvents(P, events);
        }

        /// <summary>_PyEval_SetTrace: as SetProfile, for the trace tool.</summary>
        /// <param name="unbound">Within PyGil.ForAllThreads, where every state holds its own fields.</param>
        internal static void SetTrace(PyThreadState tstate, PyObject func, bool unbound = false)
        {
            const int T = PyInstrumentation.SYS_TRACE_ID;
            if (!_sysTraceInitialized)
            {
                _sysTraceInitialized = true;
                SetCallbacks(T, SysTraceFunc2, PyTrace_CALL, PyInstrumentation.PY_START, PyInstrumentation.PY_RESUME);
                SetCallbacks(T, SysTraceFunc3, PyTrace_CALL, PyInstrumentation.PY_THROW, -1);
                SetCallbacks(T, SysTraceReturn, PyTrace_RETURN, PyInstrumentation.PY_RETURN, -1);
                SetCallbacks(T, SysTraceYield, PyTrace_RETURN, PyInstrumentation.PY_YIELD, -1);
                SetCallbacks(T, SysTraceExceptionFunc, PyTrace_EXCEPTION, PyInstrumentation.RAISE, PyInstrumentation.STOP_ITERATION);
                SetCallbacks(T, SysTraceLineFunc, PyTrace_LINE, PyInstrumentation.LINE, -1);
                SetCallbacks(T, SysTraceFunc3, PyTrace_RETURN, PyInstrumentation.PY_UNWIND, -1);
                SetCallbacks(T, SysTraceJumpFunc, PyTrace_LINE, PyInstrumentation.JUMP, -1);
                SetCallbacks(T, SysTraceInstructionFunc, PyTrace_OPCODE, PyInstrumentation.INSTRUCTION, -1);
            }

            var vm = PyVM.Instance;
            bool bound = !unbound && ReferenceEquals(tstate, vm.BoundState);
            var old = bound ? vm.TraceFunction : tstate.TraceFunction;
            int delta = (func != null ? 1 : 0) - (old != null ? 1 : 0);
            if (bound)
                vm.TraceFunction = func;
            else
                tstate.TraceFunction = func;
            _sysTracingThreads += delta;

            int events = 0;
            if (_sysTracingThreads != 0)
            {
                events = (1 << PyInstrumentation.PY_START) | (1 << PyInstrumentation.PY_RESUME) |
                    (1 << PyInstrumentation.PY_RETURN) | (1 << PyInstrumentation.PY_YIELD) |
                    (1 << PyInstrumentation.RAISE) | (1 << PyInstrumentation.LINE) |
                    (1 << PyInstrumentation.JUMP) | (1 << PyInstrumentation.BRANCH) |
                    (1 << PyInstrumentation.PY_UNWIND) | (1 << PyInstrumentation.PY_THROW) |
                    (1 << PyInstrumentation.STOP_ITERATION) |
                    (1 << PyInstrumentation.EXCEPTION_HANDLED);
                if (FOpcodeTraceSet)
                    events |= 1 << PyInstrumentation.INSTRUCTION;
            }
            PyInstrumentation.MonitoringSetEvents(T, events);
        }

        // ---- Python/sysmodule.c ----

        /// <summary>
        /// call_trampoline: the Python function with (frame, what, arg). A frame whose f_locals was
        /// read has its fast locals refreshed first and written back afterwards (PyFrame_LocalsToFast).
        /// </summary>
        private static PyObject CallTrampoline(PyObject callback, PyFrame frame, int what, PyObject arg)
        {
            // Discard any previous modifications the frame's fast locals
            if (frame.FastAsLocals)
                frame.GetOptimizedLocals();
            var result = callback.Call(new PyObject[] { frame, WhatStrings[what], arg ?? PyNone.Instance }, null);
            frame.LocalsToFast(true);
            return result;
        }

        // profile_trampoline: an error removes the profile function.
        internal static void ProfileTrampoline(PyObject self, PyFrame frame, int what, PyObject arg)
        {
            try
            {
                CallTrampoline(self, frame, what, arg);
            }
            catch (PythonException)
            {
                SetProfile(PyVM.Instance.BoundState, null);
                throw;
            }
        }

        // trace_trampoline: 'call' goes to the global function, the rest to the frame's f_trace,
        // whose answer replaces it unless None. An error removes both.
        internal static void TraceTrampoline(PyObject self, PyFrame frame, int what, PyObject arg)
        {
            var callback = what == PyTrace_CALL ? self : frame.Trace;
            if (callback == null)
                return;
            PyObject result;
            try
            {
                result = CallTrampoline(callback, frame, what, arg);
            }
            catch (PythonException)
            {
                SetTrace(PyVM.Instance.BoundState, null);
                frame.Trace = null;
                throw;
            }
            if (result is not PyNone)
                frame.Trace = result;
        }

        // sys_settrace
        internal static PyObject SysSettrace(PyObject args)
        {
            SetTrace(PyVM.Instance.BoundState, args is PyNone ? null : args);
            return PyNone.Instance;
        }

        // sys__settraceallthreads (PyEval_SetTraceAllThreads)
        internal static PyObject SysSettraceAllThreads(PyObject arg)
        {
            var func = arg is PyNone ? null : arg;
            PyGil.ForAllThreads(state => SetTrace(state, func, unbound: true));
            return PyNone.Instance;
        }

        // sys_gettrace
        internal static PyObject SysGettrace() => PyVM.Instance.TraceFunction ?? PyNone.Instance;

        // sys_setprofile
        internal static PyObject SysSetprofile(PyObject args)
        {
            SetProfile(PyVM.Instance.BoundState, args is PyNone ? null : args);
            return PyNone.Instance;
        }

        // sys__setprofileallthreads (PyEval_SetProfileAllThreads)
        internal static PyObject SysSetprofileAllThreads(PyObject arg)
        {
            var func = arg is PyNone ? null : arg;
            PyGil.ForAllThreads(state => SetProfile(state, func, unbound: true));
            return PyNone.Instance;
        }

        // sys_getprofile
        internal static PyObject SysGetprofile() => PyVM.Instance.ProfileFunction ?? PyNone.Instance;

        /// <summary>sys_call_tracing (_PyEval_CallTracing): func(*args) with tracing on again, restored after.</summary>
        internal static PyObject CallTracing(PyObject func, PyTuple args)
        {
            var vm = PyVM.Instance;
            int saveTracing = vm.Tracing;
            vm.Tracing = 0;
            try
            {
                return func.Call(args.Items, null);
            }
            finally
            {
                vm.Tracing = saveTracing;
            }
        }
    }
}
