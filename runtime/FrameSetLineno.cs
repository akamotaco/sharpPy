using System;

namespace SharpPy
{
    /// <summary>
    /// frame_setlineno and the evaluation-stack model it decides with (Objects/frameobject.c):
    /// mark_stacks, compatible_stack, marklines. The stack of each instruction is a 64-bit integer
    /// of 3-bit kinds, as in CPython.
    /// </summary>
    internal static class PyFrameSetLineno
    {
        // enum kind
        private const int Iterator = 1, Except = 2, Object = 3, Null = 4, Lasti = 5;

        // compatible_kind
        private static bool CompatibleKind(int from, int to)
        {
            if (to == 0)
                return false;
            if (to == Object)
                return from != Null;
            if (to == Null)
                return true;
            return from == to;
        }

        private const int BITS_PER_BLOCK = 3;
        private const long UNINITIALIZED = -2;
        private const long OVERFLOWED = -1;
        private const int MAX_STACK_ENTRIES = 63 / BITS_PER_BLOCK;
        private const ulong WILL_OVERFLOW = 1UL << ((MAX_STACK_ENTRIES - 1) * BITS_PER_BLOCK);
        private const long EMPTY_STACK = 0;
        private const long MASK = (1 << BITS_PER_BLOCK) - 1;

        // push_value
        private static long PushValue(long stack, int kind) =>
            (ulong)stack >= WILL_OVERFLOW ? OVERFLOWED : (stack << BITS_PER_BLOCK) | (long)kind;

        // pop_value: Py_ARITHMETIC_RIGHT_SHIFT
        private static long PopValue(long stack) => stack >> BITS_PER_BLOCK;

        // top_of_stack
        private static int TopOfStack(long stack) => (int)(stack & MASK);

        // peek
        private static int Peek(long stack, int n) => (int)((stack >> (BITS_PER_BLOCK * (n - 1))) & MASK);

        // stack_swap
        private static long StackSwap(long stack, int n)
        {
            long toSwap = Peek(stack, n);
            long top = TopOfStack(stack);
            int shift = BITS_PER_BLOCK * (n - 1);
            long replacedLow = (stack & ~(MASK << shift)) | (top << shift);
            long replacedTop = (replacedLow & ~MASK) | toSwap;
            return replacedTop;
        }

        // pop_to_level
        private static long PopToLevel(long stack, int level)
        {
            if (level == 0)
                return EMPTY_STACK;
            long maxItem = (1 << BITS_PER_BLOCK) - 1;
            long levelMaxStack = maxItem << ((level - 1) * BITS_PER_BLOCK);
            while (stack > levelMaxStack)
                stack = PopValue(stack);
            return stack;
        }

        // PyCompile_OpcodeStackEffect: stack_effect with jump -1, the larger of the two.
        private static int OpcodeStackEffect(ByteCodeOp opcode, int oparg) =>
            Math.Max(CFGOptimizer.StackEffect(opcode, oparg, true), CFGOptimizer.StackEffect(opcode, oparg, false));

        // mark_stacks
        private static long[] MarkStacks(PyCodeObject codeObj, int len)
        {
            var code = codeObj.Instructions;
            var stacks = new long[len + 1];
            for (int i = 1; i <= len; i++)
                stacks[i] = UNINITIALIZED;
            stacks[0] = EMPTY_STACK;
            if ((codeObj.Flags & (PyCodeObject.CO_GENERATOR | PyCodeObject.CO_COROUTINE | PyCodeObject.CO_ASYNC_GENERATOR)) != 0)
                // Generators get sent None while starting:
                stacks[0] = PushValue(stacks[0], Object);
            bool todo = true;
            while (todo)
            {
                todo = false;
                // Scan instructions
                for (int i = 0; i < len;)
                {
                    long nextStack = stacks[i];
                    var opcode = code[i].OpCode;
                    int oparg = 0;
                    while (opcode == ByteCodeOp.EXTENDED_ARG && i + 1 < len)
                    {
                        oparg = (oparg << 8) | code[i].Argument;
                        i++;
                        opcode = code[i].OpCode;
                        stacks[i] = nextStack;
                    }
                    int nextI = i + PyAssemble.GetInlineCacheSize(opcode) + 1;
                    if (nextStack == UNINITIALIZED)
                    {
                        i = nextI;
                        continue;
                    }
                    oparg = (oparg << 8) | code[i].Argument;
                    int j;
                    switch (opcode)
                    {
                        case ByteCodeOp.POP_JUMP_IF_FALSE:
                        case ByteCodeOp.POP_JUMP_IF_TRUE:
                        case ByteCodeOp.POP_JUMP_IF_NONE:
                        case ByteCodeOp.POP_JUMP_IF_NOT_NONE:
                        {
                            j = nextI + oparg;
                            nextStack = PopValue(nextStack);
                            long targetStack = nextStack;
                            Set(stacks, j, targetStack);
                            Set(stacks, nextI, nextStack);
                            break;
                        }
                        case ByteCodeOp.SEND:
                            j = oparg + i + PyAssemble.GetInlineCacheSize(ByteCodeOp.SEND) + 1;
                            Set(stacks, j, nextStack);
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.JUMP_FORWARD:
                            j = oparg + i + 1;
                            Set(stacks, j, nextStack);
                            break;
                        case ByteCodeOp.JUMP_BACKWARD:
                        case ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT:
                            j = i + 1 - oparg;
                            if (j >= 0 && j < len && stacks[j] == UNINITIALIZED && j < i)
                                todo = true;
                            Set(stacks, j, nextStack);
                            break;
                        case ByteCodeOp.GET_ITER:
                        case ByteCodeOp.GET_AITER:
                            nextStack = PushValue(PopValue(nextStack), Iterator);
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.FOR_ITER:
                        {
                            long targetStack = PushValue(nextStack, Object);
                            Set(stacks, nextI, targetStack);
                            j = oparg + 1 + PyAssemble.GetInlineCacheSize(ByteCodeOp.FOR_ITER) + i;
                            Set(stacks, j, targetStack);
                            break;
                        }
                        case ByteCodeOp.END_ASYNC_FOR:
                            nextStack = PopValue(PopValue(nextStack));
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.PUSH_EXC_INFO:
                            nextStack = PushValue(nextStack, Except);
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.POP_EXCEPT:
                            nextStack = PopValue(nextStack);
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.RETURN_VALUE:
                        case ByteCodeOp.RETURN_CONST:
                        case ByteCodeOp.RAISE_VARARGS:
                        case ByteCodeOp.RERAISE:
                            // End of block
                            break;
                        case ByteCodeOp.PUSH_NULL:
                            nextStack = PushValue(nextStack, Null);
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.LOAD_GLOBAL:
                            if ((oparg & 1) != 0)
                                nextStack = PushValue(nextStack, Null);
                            nextStack = PushValue(nextStack, Object);
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.LOAD_ATTR:
                            if ((oparg & 1) != 0)
                            {
                                nextStack = PopValue(nextStack);
                                nextStack = PushValue(nextStack, Null);
                                nextStack = PushValue(nextStack, Object);
                            }
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.CALL:
                            for (int k = 0; k < oparg + 2; k++)
                                nextStack = PopValue(nextStack);
                            nextStack = PushValue(nextStack, Object);
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.SWAP:
                            nextStack = StackSwap(nextStack, oparg);
                            Set(stacks, nextI, nextStack);
                            break;
                        case ByteCodeOp.COPY:
                            nextStack = PushValue(nextStack, Peek(nextStack, oparg));
                            Set(stacks, nextI, nextStack);
                            break;
                        default:
                        {
                            int delta = OpcodeStackEffect(opcode, oparg);
                            while (delta < 0)
                            {
                                nextStack = PopValue(nextStack);
                                delta++;
                            }
                            while (delta > 0)
                            {
                                nextStack = PushValue(nextStack, Object);
                                delta--;
                            }
                            Set(stacks, nextI, nextStack);
                            break;
                        }
                    }
                    i = nextI;
                }
                // Scan exception table
                foreach (var entry in codeObj.ExceptionTable)
                {
                    int startOffset = codeObj.ByteOffsetToInstructionIndex(entry.StartOffset);
                    int handler = codeObj.ByteOffsetToInstructionIndex(entry.HandlerOffset);
                    if (startOffset < 0 || startOffset >= len || handler < 0 || handler >= len)
                        continue;
                    if (stacks[startOffset] != UNINITIALIZED && stacks[handler] == UNINITIALIZED)
                    {
                        todo = true;
                        long targetStack = PopToLevel(stacks[startOffset], entry.Depth);
                        if (entry.Lasti)
                            targetStack = PushValue(targetStack, Lasti);
                        targetStack = PushValue(targetStack, Except);
                        stacks[handler] = targetStack;
                    }
                }
            }
            return stacks;
        }

        private static void Set(long[] stacks, int j, long value)
        {
            if (j >= 0 && j < stacks.Length)
                stacks[j] = value;
        }

        // compatible_stack
        private static bool CompatibleStack(long fromStack, long toStack)
        {
            if (fromStack < 0 || toStack < 0)
                return false;
            while (fromStack > toStack)
                fromStack = PopValue(fromStack);
            while (fromStack != 0)
            {
                int fromTop = TopOfStack(fromStack);
                int toTop = TopOfStack(toStack);
                if (!CompatibleKind(fromTop, toTop))
                    return false;
                fromStack = PopValue(fromStack);
                toStack = PopValue(toStack);
            }
            return toStack == 0;
        }

        // explain_incompatible_stack
        private static string ExplainIncompatibleStack(long toStack)
        {
            if (toStack == OVERFLOWED)
                return "stack is too deep to analyze";
            if (toStack == UNINITIALIZED)
                return "can't jump into an exception handler, or code may be unreachable";
            return TopOfStack(toStack) switch
            {
                Except => "can't jump into an 'except' block as there's no exception",
                Lasti => "can't jump into a re-raising block as there's no location",
                Iterator => "can't jump into the body of a for loop",
                _ => "incompatible stacks",
            };
        }

        // marklines: the line each address range starts, where it differs from the one before.
        private static int[] Marklines(PyCodeObject code, int len)
        {
            int lastLine = -1;
            var linestarts = new int[len];
            for (int i = 0; i < len; i++)
            {
                linestarts[i] = -1;
                int line = code.Instructions[i].LineNumber;
                if (code.Instructions[i].OpCode == ByteCodeOp.CACHE && i > 0)
                    continue;
                if (line != lastLine && line >= 0)
                {
                    linestarts[i] = line;
                    lastLine = line;
                }
            }
            return linestarts;
        }

        // first_line_not_before
        private static int FirstLineNotBefore(int[] lines, int len, int line)
        {
            int result = int.MaxValue;
            for (int i = 0; i < len; i++)
                if (lines[i] < result && lines[i] >= line)
                    result = lines[i];
            return result == int.MaxValue ? -1 : result;
        }

        /// <summary>
        /// frame_setlineno: from within a trace function, the frame goes on at the first line not
        /// before the one given, when the evaluation stack there is compatible with the one here;
        /// what is popped on the way is let go.
        /// </summary>
        internal static void SetLineno(PyFrame f, PyObject pNewLineno)
        {
            if (pNewLineno == null)
                throw PyAttributeError.Create("cannot delete attribute");
            // f_lineno must be an integer.
            if (pNewLineno is not PyInt newLine || !pNewLineno.IsExactly(PyType.IntType))
                throw PyValueError.Create("lineno must be an integer");

            bool suspended = f.OwnerGenerator != null && f.State == PyFrame.FrameState.Suspended;
            // This code preserves the historical restrictions on setting the line number of a frame.
            int whatEvent = PyVM.Instance.WhatEvent;
            if (whatEvent < 0)
                throw PyValueError.Create("f_lineno can only be set in a trace function");
            switch (whatEvent)
            {
                case PyInstrumentation.PY_RESUME:
                case PyInstrumentation.JUMP:
                case PyInstrumentation.BRANCH:
                case PyInstrumentation.LINE:
                case PyInstrumentation.PY_YIELD:
                    // Setting f_lineno is allowed for the above events
                    break;
                case PyInstrumentation.PY_START:
                    throw PyValueError.Create("can't jump from the 'call' trace event of a new frame");
                case PyInstrumentation.CALL:
                case PyInstrumentation.C_RETURN:
                    throw PyValueError.Create("can't jump during a call");
                case PyInstrumentation.PY_RETURN:
                case PyInstrumentation.PY_UNWIND:
                case PyInstrumentation.PY_THROW:
                case PyInstrumentation.RAISE:
                case PyInstrumentation.C_RAISE:
                case PyInstrumentation.INSTRUCTION:
                case PyInstrumentation.EXCEPTION_HANDLED:
                    throw PyValueError.Create("can only jump from a 'line' trace event");
                default:
                    throw PySystemError.Create("unexpected event type");
            }

            // Fail if the line falls outside the code block and select first line with actual code.
            var value = newLine.Value;
            if (value > PyArgs.CLongMax || value < PyArgs.CLongMin || value > int.MaxValue || value < int.MinValue)
                throw PyValueError.Create("lineno out of range");
            int lNewLineno = (int)value;
            int newLineno = lNewLineno;
            var code = f.Code;
            if (newLineno < code.GetFirstLineNo())
                throw PyValueError.Create($"line {newLineno} comes before the current code block");

            int len = code.Instructions.Count;
            var lines = Marklines(code, len);
            newLineno = FirstLineNotBefore(lines, len, newLineno);
            if (newLineno < 0)
                throw PyValueError.Create($"line {lNewLineno} comes after the current code block");

            var stacks = MarkStacks(code, len);
            long bestStack = OVERFLOWED;
            int bestAddr = -1;
            int lasti = f.HasRun ? f.LastInstruction : -1;
            long startStack = lasti >= 0 && lasti <= len ? stacks[lasti] : stacks[0];
            int err = -1;
            string msg = "cannot find bytecode for specified line";
            for (int i = 0; i < len; i++)
            {
                if (lines[i] != newLineno)
                    continue;
                long targetStack = stacks[i];
                if (CompatibleStack(startStack, targetStack))
                {
                    err = 0;
                    if (targetStack > bestStack)
                    {
                        bestStack = targetStack;
                        bestAddr = i;
                    }
                }
                else if (err < 0)
                {
                    if (startStack == OVERFLOWED)
                        msg = "stack to deep to analyze";
                    else if (startStack == UNINITIALIZED)
                        msg = "can't jump from unreachable code";
                    else
                    {
                        msg = ExplainIncompatibleStack(targetStack);
                        err = 1;
                    }
                }
            }
            if (err != 0)
                throw PyValueError.Create(msg);
            // Populate any NULL locals that the compiler might have "proven" to exist in the new
            // location. Rather than crashing or changing co_code, just bind None instead:
            int unbound = 0;
            for (int i = 0; i < code.NLocalsPlus; i++)
                // Counting every unbound local is overly-cautious, but a full flow analysis (like we
                // do in the compiler) is probably too expensive:
                if (PyNull.IsNull(f.LocalsPlus[i]) || f.LocalsPlus[i] == null)
                    unbound++;
            if (unbound != 0)
            {
                PyWarnings.Warn(PyType.RuntimeWarningType, $"assigning None to {unbound} unbound local{(unbound == 1 ? "" : "s")}", 0);
                // Do this in a second pass to avoid writing a bunch of Nones when warnings are being
                // treated as errors and the previous bit raises:
                for (int i = 0; i < code.NLocalsPlus; i++)
                    if (PyNull.IsNull(f.LocalsPlus[i]) || f.LocalsPlus[i] == null)
                        f.LocalsPlus[i] = PyNone.Instance;
            }
            if (suspended)
                // Account for value popped by yield
                startStack = PopValue(startStack);
            while (startStack > bestStack)
            {
                var popped = f.ValueStack.Count > 0 ? f.ValueStack.Pop() : PyNone.Instance;
                if (TopOfStack(startStack) == Except)
                    // Pop exception stack as well as the evaluation stack
                    f.CurrentException = popped as PyBaseException;
                startStack = PopValue(startStack);
            }
            // Finally set the new lasti and return OK.
            f.FLineno = 0;
            f.LastInstruction = bestAddr;
            if (suspended)
                f.InstructionPointer = bestAddr;
        }
    }
}
