using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// An iterator whose C tp_iternext ends by setting StopIteration rather than returning NULL
    /// alone (async_generator_asend and athrow, coroutine_wrapper, anext_awaitable): FOR_ITER and
    /// SEND see the exception and report it (monitor_raise).
    /// </summary>
    internal interface ISetsStopIteration
    {
    }

    /// <summary>
    /// _PyCoLineInstrumentationData (Include/cpython/code.h): whether the instruction starts a line
    /// (original_opcode != 0) and its line. CPython packs the line into an 8-bit delta and recomputes
    /// it (compute_line_delta, compute_line); the line itself is kept here, which is what
    /// compute_line(compute_line_delta(line)) gives back. NO_LINE is -1.
    /// </summary>
    internal struct CoLineInstrumentationData
    {
        internal bool OriginalOpcode;
        internal int Line;
    }

    /// <summary>
    /// _PyCoMonitoringData (Include/cpython/code.h). SharpPy does not rewrite bytecode, so what
    /// CPython keeps in the opcode itself - INSTRUMENTED_* in place of the opcode, INSTRUMENTED_LINE
    /// and INSTRUMENTED_INSTRUCTION in front of it - is the State of each instruction, which the
    /// dispatch loop reads instead of the opcode.
    /// </summary>
    internal sealed class CoMonitoringData
    {
        internal const byte InstrumentedEvent = 1, InstrumentedLine = 2, InstrumentedInstruction = 4;

        internal readonly byte[] LocalMonitors = new byte[PyInstrumentation.LocalEvents];
        internal byte[] ActiveMonitors = new byte[PyInstrumentation.LocalEvents];
        internal byte[] Tools;
        internal CoLineInstrumentationData[] Lines;
        internal byte[] LineTools;
        internal bool PerInstructionOpcodes;
        internal byte[] PerInstructionTools;
        internal byte[] State;
    }

    /// <summary>
    /// Python/instrumentation.c: PEP 669 monitoring. The interpreter state's monitors, callables,
    /// tool names and versions are the static fields; a code object's _co_monitoring and
    /// _co_instrumentation_version are PyCodeObject.Monitoring and InstrumentationVersion.
    /// </summary>
    internal static class PyInstrumentation
    {
        internal const int ToolIds = 8;
        internal const int LocalEvents = 10, UngroupedEvents = 15, Events = 17;

        internal const int PY_START = 0, PY_RESUME = 1, PY_RETURN = 2, PY_YIELD = 3, CALL = 4, LINE = 5,
            INSTRUCTION = 6, JUMP = 7, BRANCH = 8, STOP_ITERATION = 9, RAISE = 10, EXCEPTION_HANDLED = 11,
            PY_UNWIND = 12, PY_THROW = 13, RERAISE = 14, C_RETURN = 15, C_RAISE = 16;

        internal const int DEBUGGER_ID = 0, COVERAGE_ID = 1, PROFILER_ID = 2, OPTIMIZER_ID = 5;
        internal const int SYS_PROFILE_ID = 6, SYS_TRACE_ID = 7;

        // _PyInstrumentation_DISABLE and _PyInstrumentation_MISSING: bare objects of type object.
        internal static readonly PyObject DISABLE = new PyBaseObject();
        internal static readonly PyObject MISSING = new PyBaseObject();

        // interp->monitors, monitoring_callables, monitoring_tool_names, monitoring_version,
        // last_restart_version
        internal static readonly byte[] Monitors = new byte[UngroupedEvents];
        internal static readonly PyObject[,] Callables = new PyObject[ToolIds, Events];
        internal static readonly PyObject[] ToolNames = new PyObject[ToolIds];
        internal static ulong MonitoringVersion;
        internal static ulong LastRestartVersion;

        /// <summary>
        /// Whether any tool may see an event: some global event is set, or some code object has
        /// local events. The dispatch loop reads only this while nothing is monitored.
        /// </summary>
        internal static bool Active;

        // The code objects whose local_monitors are not empty, so Active can drop back to false.
        private static readonly List<WeakReference<PyCodeObject>> LocalCodes = new();

        private static void UpdateActive()
        {
            bool active = false;
            for (int e = 0; e < UngroupedEvents && !active; e++)
                active = Monitors[e] != 0;
            for (int i = LocalCodes.Count - 1; i >= 0; i--)
            {
                if (LocalCodes[i].TryGetTarget(out var code) && code.Monitoring != null && !MonitorsAreEmpty(code.Monitoring.LocalMonitors))
                    active = true;
                else
                    LocalCodes.RemoveAt(i);
            }
            Active = active;
        }

        internal static readonly string[] EventNames =
        {
            "PY_START", "PY_RESUME", "PY_RETURN", "PY_YIELD", "CALL", "LINE", "INSTRUCTION", "JUMP", "BRANCH",
            "STOP_ITERATION", "RAISE", "EXCEPTION_HANDLED", "PY_UNWIND", "PY_THROW", "RERAISE", "C_RETURN", "C_RAISE",
        };

        internal static bool IsInstrumentedEvent(int ev) => ev < LocalEvents;

        // EVENT_FOR_OPCODE; RESUME is -1 there and decided by its oparg.
        private static int EventForOpcode(ByteCodeOp opcode) => opcode switch
        {
            ByteCodeOp.RETURN_CONST or ByteCodeOp.RETURN_VALUE => PY_RETURN,
            ByteCodeOp.CALL or ByteCodeOp.CALL_FUNCTION_EX or ByteCodeOp.LOAD_SUPER_ATTR => CALL,
            ByteCodeOp.YIELD_VALUE => PY_YIELD,
            ByteCodeOp.JUMP_FORWARD or ByteCodeOp.JUMP_BACKWARD => JUMP,
            ByteCodeOp.POP_JUMP_IF_FALSE or ByteCodeOp.POP_JUMP_IF_TRUE or ByteCodeOp.POP_JUMP_IF_NONE
                or ByteCodeOp.POP_JUMP_IF_NOT_NONE or ByteCodeOp.FOR_ITER => BRANCH,
            ByteCodeOp.END_FOR or ByteCodeOp.END_SEND => STOP_ITERATION,
            _ => -1,
        };

        // opcode_has_event: INSTRUMENTED_OPCODES[opcode] > 0.
        private static bool OpcodeHasEvent(ByteCodeOp opcode) => opcode == ByteCodeOp.RESUME || EventForOpcode(opcode) >= 0;

        private static int EventOf(PyCodeObject code, int i)
        {
            var instr = code.Instructions[i];
            return instr.OpCode == ByteCodeOp.RESUME ? (instr.Argument > 0 ? PY_RESUME : PY_START) : EventForOpcode(instr.OpCode);
        }

        // monitors_sub, local_union, monitors_are_empty, multiple_tools, get_local_events, get_events
        private static byte[] MonitorsSub(byte[] a, byte[] b)
        {
            var res = new byte[LocalEvents];
            for (int i = 0; i < LocalEvents; i++)
                res[i] = (byte)(a[i] & ~b[i]);
            return res;
        }

        private static byte[] LocalUnion(byte[] global, byte[] local)
        {
            var res = new byte[LocalEvents];
            for (int i = 0; i < LocalEvents; i++)
                res[i] = (byte)(global[i] | local[i]);
            return res;
        }

        private static bool MonitorsAreEmpty(byte[] m)
        {
            for (int i = 0; i < LocalEvents; i++)
                if (m[i] != 0)
                    return false;
            return true;
        }

        private static bool MultipleTools(byte[] m)
        {
            for (int i = 0; i < LocalEvents; i++)
                if (System.Numerics.BitOperations.PopCount(m[i]) > 1)
                    return true;
            return false;
        }

        private static int GetLocalEvents(byte[] m, int toolId)
        {
            int result = 0;
            for (int e = 0; e < LocalEvents; e++)
                if (((m[e] >> toolId) & 1) != 0)
                    result |= 1 << e;
            return result;
        }

        private static int GetEvents(byte[] m, int toolId)
        {
            int result = 0;
            for (int e = 0; e < UngroupedEvents; e++)
                if (((m[e] >> toolId) & 1) != 0)
                    result |= 1 << e;
            return result;
        }

        // instruction_length: the instruction and its inline caches.
        private static int InstructionLength(PyCodeObject code, int offset) =>
            1 + PyAssemble.GetInlineCacheSize(code.Instructions[offset].OpCode);

        /// <summary>_co_firsttraceable (Objects/codeobject.c init_code): the index of the first RESUME.</summary>
        internal static int FirstTraceable(PyCodeObject code)
        {
            var instructions = code.Instructions;
            int entry = 0;
            while (entry < instructions.Count && instructions[entry].OpCode != ByteCodeOp.RESUME)
                entry++;
            return entry;
        }

        // de_instrument, de_instrument_line, de_instrument_per_instruction, instrument,
        // instrument_line, instrument_per_instruction: the opcode in place, which is State here.
        private static void DeInstrument(PyCodeObject code, int i) =>
            code.Monitoring.State[i] &= unchecked((byte)~CoMonitoringData.InstrumentedEvent);

        private static void DeInstrumentLine(PyCodeObject code, int i) =>
            code.Monitoring.State[i] &= unchecked((byte)~CoMonitoringData.InstrumentedLine);

        private static void DeInstrumentPerInstruction(PyCodeObject code, int i) =>
            code.Monitoring.State[i] &= unchecked((byte)~CoMonitoringData.InstrumentedInstruction);

        private static void Instrument(PyCodeObject code, int i) =>
            code.Monitoring.State[i] |= CoMonitoringData.InstrumentedEvent;

        private static void InstrumentLine(PyCodeObject code, int i) =>
            code.Monitoring.State[i] |= CoMonitoringData.InstrumentedLine;

        private static void InstrumentPerInstruction(PyCodeObject code, int i) =>
            code.Monitoring.State[i] |= CoMonitoringData.InstrumentedInstruction;

        // remove_tools
        private static void RemoveTools(PyCodeObject code, int offset, int ev, int tools)
        {
            var monitoring = code.Monitoring;
            if (monitoring.Tools != null)
            {
                monitoring.Tools[offset] &= (byte)~tools;
                if (monitoring.Tools[offset] == 0)
                    DeInstrument(code, offset);
            }
            else
            {
                // Single tool
                byte singleTool = monitoring.ActiveMonitors[ev];
                if ((singleTool & tools) == singleTool)
                    DeInstrument(code, offset);
            }
        }

        // remove_line_tools
        private static void RemoveLineTools(PyCodeObject code, int offset, int tools)
        {
            var monitoring = code.Monitoring;
            if (monitoring.LineTools != null)
            {
                monitoring.LineTools[offset] &= (byte)~tools;
                if (monitoring.LineTools[offset] == 0)
                    DeInstrumentLine(code, offset);
            }
            else
            {
                byte singleTool = monitoring.ActiveMonitors[LINE];
                if ((singleTool & tools) == singleTool)
                    DeInstrumentLine(code, offset);
            }
        }

        // add_tools
        private static void AddTools(PyCodeObject code, int offset, int tools)
        {
            if (code.Monitoring.Tools != null)
                code.Monitoring.Tools[offset] |= (byte)tools;
            Instrument(code, offset);
        }

        // add_line_tools
        private static void AddLineTools(PyCodeObject code, int offset, int tools)
        {
            if (code.Monitoring.LineTools != null)
                code.Monitoring.LineTools[offset] |= (byte)tools;
            InstrumentLine(code, offset);
        }

        // add_per_instruction_tools
        private static void AddPerInstructionTools(PyCodeObject code, int offset, int tools)
        {
            if (code.Monitoring.PerInstructionTools != null)
                code.Monitoring.PerInstructionTools[offset] |= (byte)tools;
            InstrumentPerInstruction(code, offset);
        }

        // remove_per_instruction_tools
        private static void RemovePerInstructionTools(PyCodeObject code, int offset, int tools)
        {
            var monitoring = code.Monitoring;
            if (monitoring.PerInstructionTools != null)
            {
                monitoring.PerInstructionTools[offset] &= (byte)~tools;
                if (monitoring.PerInstructionTools[offset] == 0)
                    DeInstrumentPerInstruction(code, offset);
            }
            else
            {
                byte singleTool = monitoring.ActiveMonitors[INSTRUCTION];
                if ((singleTool & tools) == singleTool)
                    DeInstrumentPerInstruction(code, offset);
            }
        }

        /// <summary>call_one_instrument: true when the callback answered DISABLE. tstate->tracing is on while it runs.</summary>
        private static bool CallOneInstrument(PyVM vm, PyObject[] args, int tool, int ev)
        {
            var instrument = Callables[tool, ev];
            if (instrument == null)
                return false;
            int oldWhat = vm.WhatEvent;
            vm.WhatEvent = ev;
            vm.Tracing++;
            PyObject res;
            try
            {
                res = instrument.Call(args, null);
            }
            finally
            {
                vm.Tracing--;
                vm.WhatEvent = oldWhat;
            }
            return ReferenceEquals(res, DISABLE);
        }

        // most_significant_bit
        private static int MostSignificantBit(int bits) => 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)bits);

        // get_tools_for_instruction
        private static byte GetToolsForInstruction(PyCodeObject code, int i, int ev)
        {
            if (ev >= UngroupedEvents)
                ev = CALL;
            if (IsInstrumentedEvent(ev))
            {
                var monitoring = code.Monitoring;
                if (monitoring == null)
                    return 0;
                return monitoring.Tools != null ? monitoring.Tools[i] : monitoring.ActiveMonitors[ev];
            }
            return Monitors[ev];
        }

        /// <summary>
        /// call_instrumentation_vector: each tool of the instruction, highest first, gets
        /// (code, offset in bytes, *args). A callback's error propagates; DISABLE removes the tool
        /// from the instruction, which a non-local event refuses.
        /// </summary>
        private static void CallInstrumentationVector(int ev, PyFrame frame, int offset, PyObject arg0, PyObject arg1, int nargs)
        {
            var vm = PyVM.Instance;
            if (vm.Tracing != 0)
                return;
            var code = frame.Code;
            byte tools = GetToolsForInstruction(code, offset, ev);
            if (tools == 0)
                return;
            var offsetObj = PyInt.From(offset * PyCodeObject.INSTRUCTION_WORD_SIZE);
            var callargs = nargs switch
            {
                2 => new PyObject[] { code, offsetObj },
                3 => new PyObject[] { code, offsetObj, arg0 },
                _ => new PyObject[] { code, offsetObj, arg0, arg1 },
            };
            int remaining = tools;
            while (remaining != 0)
            {
                int tool = MostSignificantBit(remaining);
                remaining ^= 1 << tool;
                if (!CallOneInstrument(vm, callargs, tool, ev))
                    continue;
                // DISABLE
                if (!IsInstrumentedEvent(ev))
                {
                    // Clear tool to prevent infinite loop
                    Callables[tool, ev] = null;
                    throw PyValueError.Create($"Cannot disable {EventNames[ev]} events. Callback removed.");
                }
                RemoveTools(code, offset, ev, 1 << tool);
            }
        }

        // _Py_call_instrumentation, _Py_call_instrumentation_arg, _Py_call_instrumentation_2args
        internal static void Call(int ev, PyFrame frame, int instr) => CallInstrumentationVector(ev, frame, instr, null, null, 2);

        internal static void CallArg(int ev, PyFrame frame, int instr, PyObject arg) => CallInstrumentationVector(ev, frame, instr, arg, null, 3);

        internal static void Call2Args(int ev, PyFrame frame, int instr, PyObject arg0, PyObject arg1) =>
            CallInstrumentationVector(ev, frame, instr, arg0, arg1, 4);

        /// <summary>
        /// _Py_call_instrumentation_jump: the event comes after the jump, so frame->prev_instr is the
        /// target while it runs; then prev_instr is the jump again for the line event that follows.
        /// Returns where execution goes on. When the callback raises, the error is at the target.
        /// </summary>
        internal static int CallJump(int ev, PyFrame frame, int instr, int target)
        {
            frame.LastInstruction = target;
            try
            {
                CallInstrumentationVector(ev, frame, instr, PyInt.From(target * PyCodeObject.INSTRUCTION_WORD_SIZE), null, 3);
            }
            catch (PythonException)
            {
                frame.InstructionPointer = target;
                throw;
            }
            if (frame.LastInstruction != target)
                // The callback has caused a jump (by setting the line number)
                return frame.LastInstruction;
            frame.LastInstruction = instr;
            return target;
        }

        /// <summary>
        /// _Py_call_instrumentation_exc2 (call_instrumentation_vector_protected): with the exception
        /// in flight. The caller rethrows it afterwards; an error of the callback replaces it.
        /// </summary>
        internal static void CallExc2(int ev, PyFrame frame, int instr, PyObject arg0, PyObject arg1) =>
            CallInstrumentationVector(ev, frame, instr, arg0, arg1, 4);

        // _Py_Instrumentation_GetLine
        internal static int GetLine(PyCodeObject code, int index)
        {
            var lines = code.Monitoring.Lines;
            if (index < 0)
                return code.GetFirstLineNo();
            return lines[index].Line;
        }

        /// <summary>
        /// _Py_call_instrumentation_line: the LINE event of an instruction that starts a line, unless
        /// the instruction before it (prev, frame->prev_instr) was on the same line and not a RESUME.
        /// sys.settrace's tool is called directly with the line set on the frame.
        /// </summary>
        internal static void CallLine(PyVM vm, PyFrame frame, int i, int prev)
        {
            var code = frame.Code;
            var monitoring = code.Monitoring;
            if (vm.Tracing != 0)
                return;
            int line = monitoring.Lines[i].Line;
            int prevLine = prev < 0 || prev >= monitoring.Lines.Length ? code.GetFirstLineNo() : GetLine(code, prev);
            if (prevLine == line)
            {
                // RESUME and INSTRUMENTED_RESUME are needed for the operation of instrumentation,
                // so must never be hidden by an INSTRUMENTED_LINE.
                if (prev < 0 || code.Instructions[prev].OpCode != ByteCodeOp.RESUME)
                    return;
            }
            byte tools = monitoring.LineTools != null
                ? monitoring.LineTools[i]
                : (byte)(Monitors[LINE] | monitoring.LocalMonitors[LINE]);
            // Special case sys.settrace to avoid boxing the line number, only to immediately unbox it.
            if ((tools & (1 << SYS_TRACE_ID)) != 0)
            {
                if (vm.TraceFunction != null && line >= 0 && frame.TraceLines)
                {
                    // Need to set tracing and what_event as if using the instrumentation call.
                    int oldWhat = vm.WhatEvent;
                    vm.WhatEvent = LINE;
                    vm.Tracing++;
                    frame.FLineno = line;
                    try
                    {
                        PyLegacyTracing.TraceTrampoline(vm.TraceFunction, frame, PyLegacyTracing.PyTrace_LINE, PyNone.Instance);
                    }
                    finally
                    {
                        frame.FLineno = 0;
                        vm.Tracing--;
                        vm.WhatEvent = oldWhat;
                    }
                }
                tools &= 255 - (1 << SYS_TRACE_ID);
            }
            if (tools == 0)
                return;
            var args = new PyObject[] { code, PyInt.From(line) };
            do
            {
                int tool = MostSignificantBit(tools);
                tools &= (byte)~(1 << tool);
                if (CallOneInstrument(vm, args, tool, LINE))
                    // DISABLE
                    RemoveLineTools(code, i, 1 << tool);
            } while (tools != 0);
        }

        /// <summary>_Py_call_instrumentation_instruction: the INSTRUCTION event before the instruction runs.</summary>
        internal static void CallInstruction(PyVM vm, PyFrame frame, int offset)
        {
            if (vm.Tracing != 0)
                return;
            var code = frame.Code;
            var monitoring = code.Monitoring;
            byte tools = monitoring.PerInstructionTools != null
                ? monitoring.PerInstructionTools[offset]
                : (byte)(Monitors[INSTRUCTION] | monitoring.LocalMonitors[INSTRUCTION]);
            var args = new PyObject[] { code, PyInt.From(offset * PyCodeObject.INSTRUCTION_WORD_SIZE) };
            while (tools != 0)
            {
                int tool = MostSignificantBit(tools);
                tools &= (byte)~(1 << tool);
                if (CallOneInstrument(vm, args, tool, INSTRUCTION))
                    // DISABLE
                    RemovePerInstructionTools(code, offset, 1 << tool);
            }
        }

        // _PyMonitoring_RegisterCallback
        internal static PyObject RegisterCallback(int toolId, int eventId, PyObject obj)
        {
            var callback = Callables[toolId, eventId];
            Callables[toolId, eventId] = obj;
            return callback;
        }

        // initialize_tools
        private static void InitializeTools(PyCodeObject code)
        {
            var monitoring = code.Monitoring;
            var tools = monitoring.Tools;
            int codeLen = code.Instructions.Count;
            for (int i = 0; i < codeLen; i++)
            {
                var opcode = code.Instructions[i].OpCode;
                if (OpcodeHasEvent(opcode))
                {
                    if ((monitoring.State[i] & CoMonitoringData.InstrumentedEvent) != 0)
                        tools[i] = monitoring.ActiveMonitors[EventOf(code, i)];
                    else
                        tools[i] = 0;
                }
                i += PyAssemble.GetInlineCacheSize(opcode);
            }
        }

        // initialize_lines
        private static void InitializeLines(PyCodeObject code)
        {
            var lineData = code.Monitoring.Lines;
            var instructions = code.Instructions;
            int codeLen = instructions.Count;
            int firstTraceable = FirstTraceable(code);
            for (int i = 0; i < firstTraceable && i < codeLen; i++)
            {
                lineData[i].OriginalOpcode = false;
                lineData[i].Line = PyFrame.LineAt(code, i);
            }
            int currentLine = -1;
            for (int i = firstTraceable; i < codeLen;)
            {
                var opcode = instructions[i].OpCode;
                int line = instructions[i].LineNumber;
                if (line < 0)
                    line = -1;
                lineData[i].Line = line;
                int length = InstructionLength(code, i);
                switch (opcode)
                {
                    case ByteCodeOp.END_ASYNC_FOR:
                    case ByteCodeOp.END_FOR:
                    case ByteCodeOp.END_SEND:
                    case ByteCodeOp.RESUME:
                        // END_FOR cannot start a line, as it is skipped by FOR_ITER
                        // END_SEND cannot start a line, as it is skipped by SEND
                        // RESUME must not be instrumented with INSTRUMENT_LINE
                        lineData[i].OriginalOpcode = false;
                        break;
                    default:
                        // Set original_opcode to the opcode iff the instruction starts a line, and
                        // thus should be instrumented.
                        lineData[i].OriginalOpcode = line != currentLine && line >= 0;
                        currentLine = line;
                        break;
                }
                for (int j = 1; j < length && i + j < codeLen; j++)
                {
                    lineData[i + j].OriginalOpcode = false;
                    lineData[i + j].Line = -1;
                }
                i += length;
            }
            for (int i = firstTraceable; i < codeLen;)
            {
                var opcode = instructions[i].OpCode;
                int oparg = 0;
                while (opcode == ByteCodeOp.EXTENDED_ARG && i + 1 < codeLen)
                {
                    oparg = (oparg << 8) | instructions[i].Argument;
                    i++;
                    opcode = instructions[i].OpCode;
                }
                oparg = (oparg << 8) | instructions[i].Argument;
                i += InstructionLength(code, i);
                int target;
                switch (opcode)
                {
                    case ByteCodeOp.POP_JUMP_IF_FALSE:
                    case ByteCodeOp.POP_JUMP_IF_TRUE:
                    case ByteCodeOp.POP_JUMP_IF_NONE:
                    case ByteCodeOp.POP_JUMP_IF_NOT_NONE:
                    case ByteCodeOp.JUMP_FORWARD:
                        target = i + oparg;
                        break;
                    case ByteCodeOp.FOR_ITER:
                    case ByteCodeOp.SEND:
                        // Skip over END_FOR/END_SEND
                        target = i + oparg + 1;
                        break;
                    case ByteCodeOp.JUMP_BACKWARD:
                    case ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT:
                        target = i - oparg;
                        break;
                    default:
                        continue;
                }
                if (target >= 0 && target < codeLen && lineData[target].Line != -1)
                    lineData[target].OriginalOpcode = true;
            }
            // Scan exception table
            foreach (var entry in code.ExceptionTable)
            {
                int handler = code.ByteOffsetToInstructionIndex(entry.HandlerOffset);
                if (handler < 0 || handler >= codeLen)
                    continue;
                // Skip if not the start of a line. END_ASYNC_FOR is a bit special as it marks the
                // end of an `async for` loop, which should not generate its own line event.
                if (lineData[handler].Line != -1 && instructions[handler].OpCode != ByteCodeOp.END_ASYNC_FOR)
                    lineData[handler].OriginalOpcode = true;
            }
        }

        // allocate_instrumentation_data
        private static void AllocateInstrumentationData(PyCodeObject code)
        {
            if (code.Monitoring == null)
                code.Monitoring = new CoMonitoringData { State = new byte[code.Instructions.Count] };
        }

        // update_instrumentation_data
        private static void UpdateInstrumentationData(PyCodeObject code)
        {
            int codeLen = code.Instructions.Count;
            AllocateInstrumentationData(code);
            var monitoring = code.Monitoring;
            var allEvents = LocalUnion(Monitors, monitoring.LocalMonitors);
            bool multitools = MultipleTools(allEvents);
            if (monitoring.Tools == null && multitools)
            {
                monitoring.Tools = new byte[codeLen];
                InitializeTools(code);
            }
            if (allEvents[LINE] != 0)
            {
                if (monitoring.Lines == null)
                {
                    monitoring.Lines = new CoLineInstrumentationData[codeLen];
                    InitializeLines(code);
                }
                if (multitools && monitoring.LineTools == null)
                {
                    monitoring.LineTools = new byte[codeLen];
                    // initialize_line_tools
                    for (int i = 0; i < codeLen; i++)
                        monitoring.LineTools[i] = allEvents[LINE];
                }
            }
            if (allEvents[INSTRUCTION] != 0)
            {
                monitoring.PerInstructionOpcodes = true;
                if (multitools && monitoring.PerInstructionTools == null)
                    monitoring.PerInstructionTools = new byte[codeLen];
            }
        }

        /// <summary>
        /// _Py_Instrument: brings the code's instrumentation up to the interpreter's monitors and its
        /// own local monitors, adding what is new and removing what went (everything after a restart).
        /// </summary>
        internal static void InstrumentCode(PyCodeObject code)
        {
            if (code.InstrumentationVersion == MonitoringVersion)
                return;
            int codeLen = code.Instructions.Count;
            // code->_co_firsttraceable >= code_len indicates that no instrumentation can be inserted.
            int firstTraceable = FirstTraceable(code);
            if (firstTraceable >= codeLen)
                return;
            UpdateInstrumentationData(code);
            var monitoring = code.Monitoring;
            var activeEvents = LocalUnion(Monitors, monitoring.LocalMonitors);
            byte[] newEvents, removedEvents;
            bool restarted = LastRestartVersion > code.InstrumentationVersion;
            if (restarted)
            {
                removedEvents = monitoring.ActiveMonitors;
                newEvents = activeEvents;
            }
            else
            {
                removedEvents = MonitorsSub(monitoring.ActiveMonitors, activeEvents);
                newEvents = MonitorsSub(activeEvents, monitoring.ActiveMonitors);
            }
            monitoring.ActiveMonitors = activeEvents;
            code.InstrumentationVersion = MonitoringVersion;
            if (MonitorsAreEmpty(newEvents) && MonitorsAreEmpty(removedEvents))
                return;
            var instructions = code.Instructions;
            // Insert instrumentation
            for (int i = 0; i < codeLen; i += InstructionLength(code, i))
            {
                if (!OpcodeHasEvent(instructions[i].OpCode))
                    continue;
                int ev = EventOf(code, i);
                byte removedTools = removedEvents[ev];
                if (removedTools != 0)
                    RemoveTools(code, i, ev, removedTools);
                byte newTools = newEvents[ev];
                if (newTools != 0)
                    AddTools(code, i, newTools);
            }

            // GH-103845: We need to remove both the line and instruction instrumentation before
            // adding new ones, otherwise we may remove the newly added instrumentation.
            byte removedLineTools = removedEvents[LINE];
            byte removedPerInstructionTools = removedEvents[INSTRUCTION];
            if (removedLineTools != 0)
            {
                var lineData = monitoring.Lines;
                for (int i = firstTraceable; i < codeLen; i += InstructionLength(code, i))
                    if (lineData[i].OriginalOpcode)
                        RemoveLineTools(code, i, removedLineTools);
            }
            if (removedPerInstructionTools != 0)
            {
                for (int i = firstTraceable; i < codeLen; i += InstructionLength(code, i))
                {
                    var opcode = instructions[i].OpCode;
                    if (opcode == ByteCodeOp.RESUME || opcode == ByteCodeOp.END_FOR)
                        continue;
                    RemovePerInstructionTools(code, i, removedPerInstructionTools);
                }
            }
            byte newLineTools = newEvents[LINE];
            byte newPerInstructionTools = newEvents[INSTRUCTION];
            if (newLineTools != 0)
            {
                var lineData = monitoring.Lines;
                for (int i = firstTraceable; i < codeLen; i += InstructionLength(code, i))
                    if (lineData[i].OriginalOpcode)
                        AddLineTools(code, i, newLineTools);
            }
            if (newPerInstructionTools != 0)
            {
                for (int i = firstTraceable; i < codeLen; i += InstructionLength(code, i))
                {
                    var opcode = instructions[i].OpCode;
                    if (opcode == ByteCodeOp.RESUME || opcode == ByteCodeOp.END_FOR)
                        continue;
                    AddPerInstructionTools(code, i, newPerInstructionTools);
                }
            }
        }

        private const int C_RETURN_EVENTS = (1 << C_RETURN) | (1 << C_RAISE);
        private const int C_CALL_EVENTS = C_RETURN_EVENTS | (1 << CALL);

        /// <summary>
        /// instrument_all_executing_code_objects. A frame that is not running yet, or a suspended
        /// generator, is instrumented when it next runs: the dispatch loop compares the versions at
        /// each instruction, where CPython compares them at RESUME and after a throw.
        /// </summary>
        private static void InstrumentAllExecutingCodeObjects()
        {
            UpdateActive();
            PyGil.ForEachState(state =>
            {
                foreach (var frame in state.Frames)
                    InstrumentCode(frame.Code);
            });
        }

        // set_events
        private static void SetEvents(byte[] m, int toolId, int events)
        {
            for (int e = 0; e < UngroupedEvents; e++)
            {
                int active = (events >> e) & 1;
                m[e] = (byte)((m[e] & ~(1 << toolId)) | (active << toolId));
            }
        }

        // set_local_events
        private static void SetLocalEvents(byte[] m, int toolId, int events)
        {
            for (int e = 0; e < LocalEvents; e++)
            {
                int val = (events >> e) & 1;
                m[e] = (byte)((m[e] & ~(1 << toolId)) | (val << toolId));
            }
        }

        // check_tool
        private static void CheckTool(int toolId)
        {
            if (toolId < SYS_PROFILE_ID && ToolNames[toolId] == null)
                throw PyValueError.Create($"tool {toolId} is not in use");
        }

        // _PyMonitoring_SetEvents
        internal static void MonitoringSetEvents(int toolId, int events)
        {
            CheckTool(toolId);
            int existingEvents = GetEvents(Monitors, toolId);
            if (existingEvents == events)
                return;
            SetEvents(Monitors, toolId, events);
            MonitoringVersion++;
            InstrumentAllExecutingCodeObjects();
        }

        // _PyMonitoring_SetLocalEvents
        private static void MonitoringSetLocalEvents(PyCodeObject code, int toolId, int events)
        {
            if (FirstTraceable(code) >= code.Instructions.Count)
                throw PyErrFormat.Format(PyType.SystemErrorType, "cannot instrument shim code object '%U'", code.CoName);
            CheckTool(toolId);
            AllocateInstrumentationData(code);
            var local = code.Monitoring.LocalMonitors;
            int existingEvents = GetLocalEvents(local, toolId);
            if (existingEvents == events)
                return;
            SetLocalEvents(local, toolId, events);
            if (code.InstrumentationVersion == MonitoringVersion)
                // Force instrumentation update
                code.InstrumentationVersion = ulong.MaxValue;
            if (events != 0)
                LocalCodes.Add(new WeakReference<PyCodeObject>(code));
            UpdateActive();
            InstrumentCode(code);
        }

        // check_valid_tool
        private static void CheckValidTool(int toolId)
        {
            if (toolId < 0 || toolId >= SYS_PROFILE_ID)
                throw PyValueError.Create($"invalid tool {toolId} (must be between 0 and 5)");
        }

        // monitoring_use_tool_id_impl
        private static PyObject UseToolId(int toolId, PyObject name)
        {
            CheckValidTool(toolId);
            if (name is not PyString)
                throw PyValueError.Create("tool name must be a str");
            if (ToolNames[toolId] != null)
                throw PyValueError.Create($"tool {toolId} is already in use");
            ToolNames[toolId] = name;
            return PyNone.Instance;
        }

        // monitoring_free_tool_id_impl
        private static PyObject FreeToolId(int toolId)
        {
            CheckValidTool(toolId);
            ToolNames[toolId] = null;
            return PyNone.Instance;
        }

        // monitoring_get_tool_impl
        private static PyObject GetTool(int toolId)
        {
            CheckValidTool(toolId);
            return ToolNames[toolId] ?? PyNone.Instance;
        }

        // monitoring_register_callback_impl
        private static PyObject MonitoringRegisterCallback(int toolId, int ev, PyObject func)
        {
            CheckValidTool(toolId);
            if (System.Numerics.BitOperations.PopCount((uint)ev) != 1)
                throw PyValueError.Create("The callback can only be set for one event at a time");
            int eventId = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)ev) - 1;
            if (eventId < 0 || eventId >= Events)
                throw PyValueError.Create($"invalid event {ev}");
            if (func is PyNone)
                func = null;
            return RegisterCallback(toolId, eventId, func) ?? PyNone.Instance;
        }

        // monitoring_get_events_impl
        private static PyObject MonitoringGetEvents(int toolId)
        {
            CheckValidTool(toolId);
            return PyInt.From(GetEvents(Monitors, toolId));
        }

        // monitoring_set_events_impl
        private static PyObject MonitoringSetEventsImpl(int toolId, int eventSet)
        {
            CheckValidTool(toolId);
            if (eventSet < 0 || eventSet >= (1 << Events))
                throw PyValueError.Create($"invalid event set 0x{eventSet:x}");
            if ((eventSet & C_RETURN_EVENTS) != 0 && (eventSet & C_CALL_EVENTS) != C_CALL_EVENTS)
                throw PyValueError.Create("cannot set C_RETURN or C_RAISE events independently");
            eventSet &= ~C_RETURN_EVENTS;
            MonitoringSetEvents(toolId, eventSet);
            return PyNone.Instance;
        }

        // monitoring_get_local_events_impl
        private static PyObject MonitoringGetLocalEvents(int toolId, PyObject code)
        {
            if (code is not PyCodeObject codeObject)
                throw PyTypeError.Create("code must be a code object");
            CheckValidTool(toolId);
            int eventSet = 0;
            var data = codeObject.Monitoring;
            if (data != null)
                for (int e = 0; e < LocalEvents; e++)
                    if (((data.LocalMonitors[e] >> toolId) & 1) != 0)
                        eventSet |= 1 << e;
            return PyInt.From(eventSet);
        }

        // monitoring_set_local_events_impl
        private static PyObject MonitoringSetLocalEventsImpl(int toolId, PyObject code, int eventSet)
        {
            if (code is not PyCodeObject codeObject)
                throw PyTypeError.Create("code must be a code object");
            CheckValidTool(toolId);
            if ((eventSet & C_RETURN_EVENTS) != 0 && (eventSet & C_CALL_EVENTS) != C_CALL_EVENTS)
                throw PyValueError.Create("cannot set C_RETURN or C_RAISE events independently");
            eventSet &= ~C_RETURN_EVENTS;
            if (eventSet < 0 || eventSet >= (1 << LocalEvents))
                throw PyValueError.Create($"invalid local event set 0x{eventSet:x}");
            MonitoringSetLocalEvents(codeObject, toolId, eventSet);
            return PyNone.Instance;
        }

        // monitoring_restart_events_impl
        private static PyObject RestartEvents()
        {
            // We want to ensure that:
            //   last restart version > instrumented version for all code objects
            //   last restart version < current version
            LastRestartVersion = MonitoringVersion + 1;
            MonitoringVersion = LastRestartVersion + 1;
            InstrumentAllExecutingCodeObjects();
            return PyNone.Instance;
        }

        // monitoring__all_events_impl
        private static PyObject AllEvents()
        {
            var res = new PyDict();
            for (int e = 0; e < UngroupedEvents; e++)
            {
                byte tools = Monitors[e];
                if (tools == 0)
                    continue;
                res.SetItem(PyString.From(EventNames[e]), PyInt.From(tools));
            }
            return res;
        }

        private static PyBuiltinFunction NoArgs(string name, Func<PyObject> body) =>
            new(name, (args, kwargs) => body())
            {
                OwnerModule = "sys.monitoring",
                DeclaredConvention = new PyArity.Convention(PyCall.NoArgs, 0, 0),
            };

        private static PyBuiltinFunction Fast(string name, int count, Func<PyObject[], PyObject> body) =>
            PyArgs.FastCall("sys.monitoring", name, count, count, body);

        /// <summary>_Py_CreateMonitoringObject: the sys.monitoring namespace.</summary>
        internal static PyModule CreateMonitoringObject()
        {
            var mod = new PyModule("sys.monitoring");
            var d = mod.ModuleDict;
            // methods
            d["use_tool_id"] = Fast("use_tool_id", 2, args => UseToolId(PyArgs.Int(args[0]), args[1]));
            d["free_tool_id"] = Fast("free_tool_id", 1, args => FreeToolId(PyArgs.Int(args[0])));
            d["get_tool"] = Fast("get_tool", 1, args => GetTool(PyArgs.Int(args[0])));
            d["register_callback"] = Fast("register_callback", 3,
                args => MonitoringRegisterCallback(PyArgs.Int(args[0]), PyArgs.Int(args[1]), args[2]));
            d["get_events"] = Fast("get_events", 1, args => MonitoringGetEvents(PyArgs.Int(args[0])));
            d["set_events"] = Fast("set_events", 2, args => MonitoringSetEventsImpl(PyArgs.Int(args[0]), PyArgs.Int(args[1])));
            d["get_local_events"] = Fast("get_local_events", 2, args => MonitoringGetLocalEvents(PyArgs.Int(args[0]), args[1]));
            d["set_local_events"] = Fast("set_local_events", 3,
                args => MonitoringSetLocalEventsImpl(PyArgs.Int(args[0]), args[1], PyArgs.Int(args[2])));
            d["restart_events"] = NoArgs("restart_events", RestartEvents);
            d["_all_events"] = NoArgs("_all_events", AllEvents);

            d["DISABLE"] = DISABLE;
            d["MISSING"] = MISSING;
            var events = new Modules.PySimpleNamespace();
            for (int i = 0; i < Events; i++)
                events.SetAttribute(EventNames[i], PyInt.From(1 << i));
            events.SetAttribute("NO_EVENTS", PyInt.From(0));
            d["events"] = events;
            d["DEBUGGER_ID"] = PyInt.From(DEBUGGER_ID);
            d["COVERAGE_ID"] = PyInt.From(COVERAGE_ID);
            d["PROFILER_ID"] = PyInt.From(PROFILER_ID);
            d["OPTIMIZER_ID"] = PyInt.From(OPTIMIZER_ID);
            // PyCFunction_NewEx(ml, self=mod, module=name): each function's m_self is the namespace.
            foreach (var value in d.Values)
                if (value is PyBuiltinFunction function)
                    function.OwnerModuleObject = mod;
            return mod;
        }

        // ---- The monitoring calls of Python/ceval.c ----

        // no_tools_for_global_event, no_tools_for_local_event
        private static bool NoToolsForGlobalEvent(int ev) => Monitors[ev] == 0;

        private static bool NoToolsForLocalEvent(PyFrame frame, int ev)
        {
            var data = frame.Code.Monitoring;
            return data != null ? data.ActiveMonitors[ev] == 0 : NoToolsForGlobalEvent(ev);
        }

        /// <summary>
        /// do_monitor_exc: the event with the exception in flight; a callback that raises replaces
        /// it, which is the exception this returns.
        /// </summary>
        private static PythonException DoMonitorExc(PyFrame frame, int instr, int ev, PythonException exc)
        {
            try
            {
                CallArg(ev, frame, instr, exc.PyException);
                return exc;
            }
            catch (PythonException error)
            {
                return error;
            }
        }

        // monitor_raise
        internal static PythonException MonitorRaise(PyFrame frame, int instr, PythonException exc) =>
            !Active || NoToolsForGlobalEvent(RAISE) ? exc : DoMonitorExc(frame, instr, RAISE, exc);

        // monitor_reraise
        internal static PythonException MonitorReraise(PyFrame frame, int instr, PythonException exc) =>
            !Active || NoToolsForGlobalEvent(RERAISE) ? exc : DoMonitorExc(frame, instr, RERAISE, exc);

        /// <summary>monitor_stop_iteration: a callback's error is raised.</summary>
        internal static void MonitorStopIteration(PyFrame frame, int instr, PyObject value)
        {
            if (NoToolsForLocalEvent(frame, STOP_ITERATION))
                return;
            // PyErr_SetObject(PyExc_StopIteration, value): _PyErr_CreateException, so a tuple is the
            // arguments and None is none.
            var args = value is PyTuple { IsExact: true } tuple ? tuple.Items : value is PyNone ? Array.Empty<PyObject>() : new[] { value };
            CallArg(STOP_ITERATION, frame, instr, new PyStopIteration(args));
        }

        // monitor_unwind
        internal static PythonException MonitorUnwind(PyFrame frame, int instr, PythonException exc) =>
            !Active || NoToolsForGlobalEvent(PY_UNWIND) ? exc : DoMonitorExc(frame, instr, PY_UNWIND, exc);

        /// <summary>monitor_handled: a callback's error is raised.</summary>
        internal static void MonitorHandled(PyFrame frame, int instr, PyObject exc)
        {
            if (!Active || NoToolsForGlobalEvent(EXCEPTION_HANDLED))
                return;
            CallArg(EXCEPTION_HANDLED, frame, instr, exc);
        }

        // monitor_throw
        internal static PythonException MonitorThrow(PyFrame frame, int instr, PythonException exc) =>
            !Active || NoToolsForGlobalEvent(PY_THROW) ? exc : DoMonitorExc(frame, instr, PY_THROW, exc);
    }
}
