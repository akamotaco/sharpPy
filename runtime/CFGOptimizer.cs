using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 Python/flowgraph.c: _PyCfg_OptimizeCodeUnit and the passes after it.
    /// Function by function the same order and the same conditions, so the bytecode is CPython's.
    /// </summary>
    public class CFGOptimizer
    {
        // Maximum size of basic block that should be copied in optimizer (MAX_COPY_SIZE)
        private const int MaxCopySize = 4;
        private const int Visited = -1;

        private readonly ControlFlowGraph _cfg;
        private readonly List<PyObject> _constants;
        private readonly Func<PyObject, int> _addConstant;
        private readonly int _nlocals;
        private readonly int _nparams;
        private readonly int _firstLineNo;

        public CFGOptimizer(ControlFlowGraph cfg, List<PyObject> constants, Func<PyObject, int> addConstant, int nlocals, int nparams, int firstLineNo)
        {
            _cfg = cfg;
            _constants = constants;
            _addConstant = addConstant;
            _nlocals = nlocals;
            _nparams = nparams;
            _firstLineNo = firstLineNo;
        }

        /// <summary>_PyCfg_OptimizeCodeUnit, after the preprocessing PyFlowGraph.Build has done.</summary>
        public void Optimize()
        {
            OptimizeCfg();
            RemoveUnusedConsts();
            AddChecksForLoadsOfUninitializedVariables();
            PushColdBlocksToEnd();
            ResolveLineNumbers();
        }

        private BasicBlock Entry => _cfg.EntryBlock;

        private static ByteCodeInstruction SetOp(ByteCodeInstruction i, ByteCodeOp op, int arg = 0) =>
            new ByteCodeInstruction(op, arg, i.LineNumber, i.ColumnOffset, i.End, null, i.ExceptBlock);

        private static ByteCodeInstruction Retarget(ByteCodeInstruction i, ByteCodeOp op, BasicBlock target) =>
            new ByteCodeInstruction(op, i.Argument, i.LineNumber, i.ColumnOffset, i.End, target, i.ExceptBlock);

        private static ByteCodeInstruction WithLine(ByteCodeInstruction i, ByteCodeInstruction from) =>
            new ByteCodeInstruction(i.OpCode, i.Argument, from.LineNumber, from.ColumnOffset, from.End, i.TargetBlock, i.ExceptBlock);

        private static ByteCodeInstruction WithLine(ByteCodeInstruction i, int line) =>
            new ByteCodeInstruction(i.OpCode, i.Argument, line, i.ColumnOffset, i.End, i.TargetBlock, i.ExceptBlock);

        internal static bool IsBlockPush(ByteCodeOp op) =>
            op == ByteCodeOp.SETUP_FINALLY || op == ByteCodeOp.SETUP_CLEANUP || op == ByteCodeOp.SETUP_WITH;

        // is_jump: has a target and is not a block push.
        internal static bool IsJump(ByteCodeInstruction i) => i.TargetBlock != null && !IsBlockPush(i.OpCode);

        private static bool IsAnyJumpOp(ByteCodeOp op) =>
            op == ByteCodeOp.JUMP || op == ByteCodeOp.JUMP_FORWARD || op == ByteCodeOp.JUMP_BACKWARD;

        internal static bool IsUnconditionalJump(ByteCodeOp op) =>
            IsAnyJumpOp(op) || op == ByteCodeOp.JUMP_NO_INTERRUPT || op == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT;

        internal static bool IsScopeExit(ByteCodeOp op) =>
            op == ByteCodeOp.RETURN_VALUE || op == ByteCodeOp.RETURN_CONST ||
            op == ByteCodeOp.RAISE_VARARGS || op == ByteCodeOp.RERAISE;

        private static bool HasConst(ByteCodeOp op) =>
            op == ByteCodeOp.LOAD_CONST || op == ByteCodeOp.RETURN_CONST || op == ByteCodeOp.KW_NAMES;

        // BB_HAS_FALLTHROUGH
        internal static bool HasFallthrough(BasicBlock b)
        {
            if (b.Instructions.Count == 0)
                return true;
            var last = b.Instructions[b.Instructions.Count - 1].OpCode;
            return !IsScopeExit(last) && !IsUnconditionalJump(last);
        }

        private static bool ExitsScope(BasicBlock b) =>
            b.Instructions.Count > 0 && IsScopeExit(b.Instructions[b.Instructions.Count - 1].OpCode);

        // optimize_cfg
        private void OptimizeCfg()
        {
            EliminateEmptyBasicBlocks();
            for (var b = Entry; b != null; b = b.Next)
                InlineSmallExitBlocks(b);
            for (var b = Entry; b != null; b = b.Next)
                OptimizeBasicBlock(b);
            RemoveRedundantNopsAndPairs();
            for (var b = Entry; b != null; b = b.Next)
                InlineSmallExitBlocks(b);
            MarkReachable();

            // Delete unreachable instructions
            for (var b = Entry; b != null; b = b.Next)
                if (b.Predecessors == 0)
                    b.Instructions.Clear();
            for (var b = Entry; b != null; b = b.Next)
                RemoveRedundantNops(b);
            EliminateEmptyBasicBlocks();
            RemoveRedundantJumps();
        }

        private void MarkReachable()
        {
            for (var b = Entry; b != null; b = b.Next)
                b.Predecessors = 0;
            var visited = new HashSet<BasicBlock>();
            var stack = new Stack<BasicBlock>();
            Entry.Predecessors = 1;
            stack.Push(Entry);
            while (stack.Count > 0)
            {
                var b = stack.Pop();
                visited.Add(b);
                if (b.Next != null && HasFallthrough(b))
                {
                    if (!visited.Contains(b.Next))
                        stack.Push(b.Next);
                    b.Next.Predecessors++;
                }
                foreach (var instr in b.Instructions)
                {
                    if (instr.TargetBlock != null)
                    {
                        if (!visited.Contains(instr.TargetBlock))
                            stack.Push(instr.TargetBlock);
                        instr.TargetBlock.Predecessors++;
                    }
                }
            }
        }

        private void EliminateEmptyBasicBlocks()
        {
            for (var b = Entry; b != null; b = b.Next)
            {
                var next = b.Next;
                while (next != null && next.Instructions.Count == 0)
                    next = next.Next;
                b.Next = next;
            }
            while (_cfg.EntryBlock != null && _cfg.EntryBlock.Instructions.Count == 0)
                _cfg.EntryBlock = _cfg.EntryBlock.Next;
            for (var b = Entry; b != null; b = b.Next)
            {
                for (int i = 0; i < b.Instructions.Count; i++)
                {
                    var instr = b.Instructions[i];
                    if (instr.TargetBlock == null)
                        continue;
                    var target = instr.TargetBlock;
                    while (target.Instructions.Count == 0)
                        target = target.Next;
                    if (target != instr.TargetBlock)
                    {
                        target.Label = 1;
                        b.Instructions[i] = Retarget(instr, instr.OpCode, target);
                    }
                }
            }
        }

        private static void RemoveRedundantNops(BasicBlock bb)
        {
            var code = bb.Instructions;
            int dest = 0;
            int prevLineno = -1;
            for (int src = 0; src < code.Count; src++)
            {
                int lineno = code[src].LineNumber;
                if (code[src].OpCode == ByteCodeOp.NOP)
                {
                    // Eliminate no-op if it doesn't have a line number
                    if (lineno < 0)
                        continue;
                    // or, if the previous instruction had the same line number.
                    if (prevLineno == lineno)
                        continue;
                    // or, if the next instruction has same line number or no line number
                    if (src < code.Count - 1)
                    {
                        int nextLineno = code[src + 1].LineNumber;
                        if (nextLineno == lineno)
                            continue;
                        if (nextLineno < 0)
                        {
                            code[src + 1] = WithLine(code[src + 1], code[src]);
                            continue;
                        }
                    }
                    else
                    {
                        var next = bb.Next;
                        while (next != null && next.Instructions.Count == 0)
                            next = next.Next;
                        // or if last instruction in BB and next BB has same line number
                        if (next != null)
                        {
                            int nextLine = -1;
                            foreach (var instr in next.Instructions)
                            {
                                // Skip over NOPs without location, they will be removed
                                if (instr.OpCode == ByteCodeOp.NOP && instr.LineNumber == -1)
                                    continue;
                                nextLine = instr.LineNumber;
                                break;
                            }
                            if (lineno == nextLine)
                                continue;
                        }
                    }
                }
                if (dest != src)
                    code[dest] = code[src];
                dest++;
                prevLineno = lineno;
            }
            code.RemoveRange(dest, code.Count - dest);
        }

        private void RemoveRedundantNopsAndPairs()
        {
            bool done = false;
            while (!done)
            {
                done = true;
                BasicBlock? prevBlock = null, block = null;
                int prevIndex = -1, index = -1;
                for (var b = Entry; b != null; b = b.Next)
                {
                    RemoveRedundantNops(b);
                    // this block is a jump target, forget instr
                    if (b.Label >= 0)
                    {
                        block = null;
                        index = -1;
                    }
                    for (int i = 0; i < b.Instructions.Count; i++)
                    {
                        prevBlock = block;
                        prevIndex = index;
                        block = b;
                        index = i;
                        var prevOp = prevBlock != null ? prevBlock.Instructions[prevIndex].OpCode : (ByteCodeOp)0;
                        int prevArg = prevBlock != null ? prevBlock.Instructions[prevIndex].Argument : 0;
                        if (b.Instructions[i].OpCode == ByteCodeOp.POP_TOP &&
                            (prevOp == ByteCodeOp.LOAD_CONST || (prevOp == ByteCodeOp.COPY && prevArg == 1)))
                        {
                            prevBlock!.Instructions[prevIndex] = SetOp(prevBlock.Instructions[prevIndex], ByteCodeOp.NOP);
                            b.Instructions[i] = SetOp(b.Instructions[i], ByteCodeOp.NOP);
                            done = false;
                        }
                    }
                    if ((block != null && IsJump(block.Instructions[index])) || !HasFallthrough(b))
                    {
                        block = null;
                        index = -1;
                    }
                }
            }
        }

        private void RemoveRedundantJumps()
        {
            for (var b = Entry; b != null; b = b.Next)
            {
                if (b.Instructions.Count == 0)
                    continue;
                int lastIndex = b.Instructions.Count - 1;
                var last = b.Instructions[lastIndex];
                if (IsUnconditionalJump(last.OpCode) && last.TargetBlock == b.Next)
                    b.Instructions[lastIndex] = SetOp(last, ByteCodeOp.NOP);
            }
        }

        private static void InlineSmallExitBlocks(BasicBlock bb)
        {
            if (bb.Instructions.Count == 0)
                return;
            int lastIndex = bb.Instructions.Count - 1;
            var last = bb.Instructions[lastIndex];
            if (!IsUnconditionalJump(last.OpCode))
                return;
            var target = last.TargetBlock!;
            if (ExitsScope(target) && target.Instructions.Count <= MaxCopySize)
            {
                bb.Instructions[lastIndex] = SetOp(last, ByteCodeOp.NOP);
                bb.Instructions.AddRange(target.Instructions.ToArray());
            }
        }

        // jump_thread: jump straight to where the jump at the target goes.
        private static bool JumpThread(BasicBlock bb, int index, ByteCodeInstruction target, ByteCodeOp opcode)
        {
            var inst = bb.Instructions[index];
            if ((inst.LineNumber == target.LineNumber || target.LineNumber == -1) && inst.TargetBlock != target.TargetBlock)
            {
                bb.Instructions[index] = Retarget(inst, opcode, target.TargetBlock!);
                return true;
            }
            return false;
        }

        private void FoldTupleOnConstants(BasicBlock bb, int start, int n)
        {
            var code = bb.Instructions;
            for (int i = 0; i < n; i++)
                if (code[start + i].OpCode != ByteCodeOp.LOAD_CONST)
                    return;
            var items = new PyObject[n];
            for (int i = 0; i < n; i++)
                items[i] = _constants[code[start + i].Argument];
            int index = _addConstant(PyTuple.From(items));
            for (int i = 0; i < n; i++)
                code[start + i] = SetOp(code[start + i], ByteCodeOp.NOP);
            code[start + n] = SetOp(code[start + n], ByteCodeOp.LOAD_CONST, index);
        }

        // swaptimize: replace a run of SWAPs and NOPs with an optimal one that has the same effect.
        private static void Swaptimize(BasicBlock block, ref int ix)
        {
            var code = block.Instructions;
            int start = ix;
            int depth = code[start].Argument;
            int len = 0;
            bool more = false;
            int limit = code.Count - start;
            while (++len < limit)
            {
                var opcode = code[start + len].OpCode;
                if (opcode == ByteCodeOp.SWAP)
                {
                    depth = Math.Max(depth, code[start + len].Argument);
                    more = true;
                }
                else if (opcode != ByteCodeOp.NOP)
                {
                    break;
                }
            }
            // It's already optimal if there's only one SWAP:
            if (!more)
                return;
            var stack = new int[depth];
            for (int i = 0; i < depth; i++)
                stack[i] = i;
            // Simulate the combined effect of these instructions on our "stack":
            for (int i = 0; i < len; i++)
            {
                if (code[start + i].OpCode == ByteCodeOp.SWAP)
                {
                    int oparg = code[start + i].Argument;
                    int top = stack[0];
                    stack[0] = stack[oparg - 1];
                    stack[oparg - 1] = top;
                }
            }
            // Un-shuffle by cycles, replacing instructions from the end of the run.
            int current = len - 1;
            for (int i = 0; i < depth; i++)
            {
                if (stack[i] == Visited || stack[i] == i)
                    continue;
                int j = i;
                while (true)
                {
                    if (j != 0)
                    {
                        code[start + current] = SetOp(code[start + current], ByteCodeOp.SWAP, j + 1);
                        current--;
                    }
                    if (stack[j] == Visited)
                        break;
                    int nextJ = stack[j];
                    stack[j] = Visited;
                    j = nextJ;
                }
            }
            // NOP out any unused instructions:
            while (0 <= current)
            {
                code[start + current] = SetOp(code[start + current], ByteCodeOp.NOP);
                current--;
            }
            ix += len - 1;
        }

        private static bool Swappable(ByteCodeOp op) => op == ByteCodeOp.STORE_FAST || op == ByteCodeOp.STORE_FAST_MAYBE_NULL || op == ByteCodeOp.POP_TOP;

        private static int StoresTo(ByteCodeInstruction i) =>
            i.OpCode == ByteCodeOp.STORE_FAST || i.OpCode == ByteCodeOp.STORE_FAST_MAYBE_NULL ? i.Argument : -1;

        private static int NextSwappableInstruction(BasicBlock block, int i, int lineno)
        {
            var code = block.Instructions;
            while (++i < code.Count)
            {
                // Optimizing across this instruction could change the names bound between line events.
                if (0 <= lineno && code[i].LineNumber != lineno)
                    return -1;
                if (code[i].OpCode == ByteCodeOp.NOP)
                    continue;
                return Swappable(code[i].OpCode) ? i : -1;
            }
            return -1;
        }

        // apply_static_swaps: swap *instructions* rather than stack items.
        private static void ApplyStaticSwaps(BasicBlock block, int i)
        {
            var code = block.Instructions;
            for (; 0 <= i; i--)
            {
                var swap = code[i];
                if (swap.OpCode != ByteCodeOp.SWAP)
                {
                    if (swap.OpCode == ByteCodeOp.NOP || Swappable(swap.OpCode))
                        continue;
                    return;
                }
                int j = NextSwappableInstruction(block, i, -1);
                if (j < 0)
                    return;
                int k = j;
                int lineno = code[j].LineNumber;
                for (int count = swap.Argument - 1; 0 < count; count--)
                {
                    k = NextSwappableInstruction(block, k, lineno);
                    if (k < 0)
                        return;
                }
                // Not safe if the two store to the same place, or anything between does.
                int storeJ = StoresTo(code[j]);
                int storeK = StoresTo(code[k]);
                if (storeJ >= 0 || storeK >= 0)
                {
                    if (storeJ == storeK)
                        return;
                    for (int idx = j + 1; idx < k; idx++)
                    {
                        int storeIdx = StoresTo(code[idx]);
                        if (storeIdx >= 0 && (storeIdx == storeJ || storeIdx == storeK))
                            return;
                    }
                }
                code[i] = SetOp(swap, ByteCodeOp.NOP);
                var temp = code[j];
                code[j] = code[k];
                code[k] = temp;
            }
        }

        private void OptimizeBasicBlock(BasicBlock bb)
        {
            var code = bb.Instructions;
            ByteCodeOp target = ByteCodeOp.NOP;
            ByteCodeInstruction targetInstr = default;
            ByteCodeOp opcode = 0;
            int oparg = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var inst = code[i];
                bool isCopyOfLoadConst = opcode == ByteCodeOp.LOAD_CONST && inst.OpCode == ByteCodeOp.COPY && inst.Argument == 1;
                if (!isCopyOfLoadConst)
                {
                    opcode = inst.OpCode;
                    oparg = inst.Argument;
                    if (inst.TargetBlock != null)
                    {
                        targetInstr = inst.TargetBlock.Instructions[0];
                        target = targetInstr.OpCode;
                    }
                    else
                    {
                        target = ByteCodeOp.NOP;
                    }
                }
                var nextop = i + 1 < code.Count ? code[i + 1].OpCode : (ByteCodeOp)0;
                switch (opcode)
                {
                    // Remove LOAD_CONST const; conditional jump
                    case ByteCodeOp.LOAD_CONST:
                        switch (nextop)
                        {
                            case ByteCodeOp.POP_JUMP_IF_FALSE:
                            case ByteCodeOp.POP_JUMP_IF_TRUE:
                                bool isTrue = _constants[oparg].IsTrue();
                                code[i] = SetOp(code[i], ByteCodeOp.NOP);
                                bool jumpIfTrue = nextop == ByteCodeOp.POP_JUMP_IF_TRUE;
                                if (isTrue == jumpIfTrue)
                                    code[i + 1] = Retarget(code[i + 1], ByteCodeOp.JUMP, code[i + 1].TargetBlock!);
                                else
                                    code[i + 1] = SetOp(code[i + 1], ByteCodeOp.NOP);
                                break;
                            case ByteCodeOp.IS_OP:
                                var jumpOp = i + 2 < code.Count ? code[i + 2].OpCode : (ByteCodeOp)0;
                                if (_constants[oparg] is PyNone && (jumpOp == ByteCodeOp.POP_JUMP_IF_FALSE || jumpOp == ByteCodeOp.POP_JUMP_IF_TRUE))
                                {
                                    int nextarg = code[i + 1].Argument;
                                    code[i] = SetOp(code[i], ByteCodeOp.NOP);
                                    code[i + 1] = SetOp(code[i + 1], ByteCodeOp.NOP);
                                    bool notNone = (nextarg ^ (jumpOp == ByteCodeOp.POP_JUMP_IF_FALSE ? 1 : 0)) != 0;
                                    code[i + 2] = Retarget(code[i + 2], notNone ? ByteCodeOp.POP_JUMP_IF_NOT_NONE : ByteCodeOp.POP_JUMP_IF_NONE, code[i + 2].TargetBlock!);
                                }
                                break;
                            case ByteCodeOp.RETURN_VALUE:
                                code[i] = SetOp(code[i], ByteCodeOp.NOP);
                                i++;
                                code[i] = SetOp(code[i], ByteCodeOp.RETURN_CONST, oparg);
                                break;
                        }
                        break;

                    // Try to fold tuples of constants.
                    case ByteCodeOp.BUILD_TUPLE:
                        if (nextop == ByteCodeOp.UNPACK_SEQUENCE && oparg == code[i + 1].Argument)
                        {
                            if (oparg == 1)
                            {
                                code[i] = SetOp(code[i], ByteCodeOp.NOP);
                                code[i + 1] = SetOp(code[i + 1], ByteCodeOp.NOP);
                                continue;
                            }
                            if (oparg == 2 || oparg == 3)
                            {
                                code[i] = SetOp(code[i], ByteCodeOp.NOP);
                                code[i + 1] = SetOp(code[i + 1], ByteCodeOp.SWAP, code[i + 1].Argument);
                                continue;
                            }
                        }
                        if (i >= oparg)
                            FoldTupleOnConstants(bb, i - oparg, oparg);
                        break;

                    case ByteCodeOp.POP_JUMP_IF_NOT_NONE:
                    case ByteCodeOp.POP_JUMP_IF_NONE:
                    case ByteCodeOp.POP_JUMP_IF_FALSE:
                    case ByteCodeOp.POP_JUMP_IF_TRUE:
                        if (IsAnyJumpOp(target) && JumpThread(bb, i, targetInstr, inst.OpCode))
                            i--;
                        break;

                    case ByteCodeOp.JUMP:
                    case ByteCodeOp.JUMP_FORWARD:
                    case ByteCodeOp.JUMP_BACKWARD:
                        if (IsAnyJumpOp(target) && JumpThread(bb, i, targetInstr, ByteCodeOp.JUMP))
                            i--;
                        break;

                    case ByteCodeOp.SWAP:
                        if (oparg == 1)
                        {
                            code[i] = SetOp(code[i], ByteCodeOp.NOP);
                            break;
                        }
                        Swaptimize(bb, ref i);
                        ApplyStaticSwaps(bb, i);
                        break;

                    case ByteCodeOp.PUSH_NULL:
                        if (nextop == ByteCodeOp.LOAD_GLOBAL && (code[i + 1].Argument & 1) == 0)
                        {
                            code[i] = SetOp(code[i], ByteCodeOp.NOP);
                            code[i + 1] = SetOp(code[i + 1], ByteCodeOp.LOAD_GLOBAL, code[i + 1].Argument | 1);
                        }
                        break;
                }
            }
        }

        private void RemoveUnusedConsts()
        {
            int nconsts = _constants.Count;
            if (nconsts == 0)
                return;
            var indexMap = new int[nconsts];
            for (int i = 1; i < nconsts; i++)
                indexMap[i] = -1;
            // The first constant may be docstring; keep it always.
            indexMap[0] = 0;
            for (var b = Entry; b != null; b = b.Next)
                foreach (var instr in b.Instructions)
                    if (HasConst(instr.OpCode))
                        indexMap[instr.Argument] = instr.Argument;
            int used = 0;
            for (int i = 0; i < nconsts; i++)
                if (indexMap[i] != -1)
                    indexMap[used++] = indexMap[i];
            if (used == nconsts)
                return;
            var reverse = new int[nconsts];
            for (int i = 0; i < used; i++)
            {
                reverse[indexMap[i]] = i;
                _constants[i] = _constants[indexMap[i]];
            }
            _constants.RemoveRange(used, nconsts - used);
            for (var b = Entry; b != null; b = b.Next)
            {
                var code = b.Instructions;
                for (int i = 0; i < code.Count; i++)
                    if (HasConst(code[i].OpCode))
                        code[i] = new ByteCodeInstruction(code[i].OpCode, reverse[code[i].Argument], code[i].LineNumber, code[i].ColumnOffset, code[i].End, code[i].TargetBlock, code[i].ExceptBlock);
            }
        }

        private static void MaybePush(BasicBlock b, ulong unsafeMask, Stack<BasicBlock> stack)
        {
            // Push b if the unsafe mask is giving us any new information.
            ulong both = b.UnsafeLocalsMask | unsafeMask;
            if (b.UnsafeLocalsMask != both)
            {
                b.UnsafeLocalsMask = both;
                if (!b.Visited)
                {
                    stack.Push(b);
                    b.Visited = true;
                }
            }
        }

        private static void ScanBlockForLocals(BasicBlock b, Stack<BasicBlock> stack)
        {
            // bit i is set if local i is potentially uninitialized
            ulong unsafeMask = b.UnsafeLocalsMask;
            var code = b.Instructions;
            for (int i = 0; i < code.Count; i++)
            {
                var instr = code[i];
                if (instr.ExceptBlock != null)
                    MaybePush(instr.ExceptBlock, unsafeMask, stack);
                if (instr.Argument >= 64 || instr.Argument < 0)
                    continue;
                ulong bit = 1UL << instr.Argument;
                switch (instr.OpCode)
                {
                    case ByteCodeOp.DELETE_FAST:
                    case ByteCodeOp.LOAD_FAST_AND_CLEAR:
                    case ByteCodeOp.STORE_FAST_MAYBE_NULL:
                        unsafeMask |= bit;
                        break;
                    case ByteCodeOp.STORE_FAST:
                    case ByteCodeOp.LOAD_FAST_CHECK:
                        unsafeMask &= ~bit;
                        break;
                    case ByteCodeOp.LOAD_FAST:
                        if ((unsafeMask & bit) != 0)
                            code[i] = new ByteCodeInstruction(ByteCodeOp.LOAD_FAST_CHECK, instr.Argument, instr.LineNumber, instr.ColumnOffset, instr.End, null, instr.ExceptBlock);
                        unsafeMask &= ~bit;
                        break;
                }
            }
            if (b.Next != null && HasFallthrough(b))
                MaybePush(b.Next, unsafeMask, stack);
            if (code.Count > 0 && IsJump(code[code.Count - 1]))
                MaybePush(code[code.Count - 1].TargetBlock!, unsafeMask, stack);
        }

        private void FastScanManyLocals(int nlocals)
        {
            var states = new long[nlocals - 64];
            long blocknum = 0;
            for (var b = Entry; b != null; b = b.Next)
            {
                blocknum++;
                var code = b.Instructions;
                for (int i = 0; i < code.Count; i++)
                {
                    var instr = code[i];
                    int arg = instr.Argument;
                    if (arg < 64 || arg >= nlocals)
                        continue;
                    switch (instr.OpCode)
                    {
                        case ByteCodeOp.DELETE_FAST:
                        case ByteCodeOp.LOAD_FAST_AND_CLEAR:
                        case ByteCodeOp.STORE_FAST_MAYBE_NULL:
                            states[arg - 64] = blocknum - 1;
                            break;
                        case ByteCodeOp.STORE_FAST:
                            states[arg - 64] = blocknum;
                            break;
                        case ByteCodeOp.LOAD_FAST:
                            if (states[arg - 64] != blocknum)
                                code[i] = new ByteCodeInstruction(ByteCodeOp.LOAD_FAST_CHECK, arg, instr.LineNumber, instr.ColumnOffset, instr.End, null, instr.ExceptBlock);
                            states[arg - 64] = blocknum;
                            break;
                    }
                }
            }
        }

        private void AddChecksForLoadsOfUninitializedVariables()
        {
            int nlocals = _nlocals;
            if (nlocals == 0)
                return;
            if (nlocals > 64)
            {
                // Locals beyond the first 64 are only analyzed one basicblock at a time.
                FastScanManyLocals(nlocals);
                nlocals = 64;
            }
            var stack = new Stack<BasicBlock>();
            for (var b = Entry; b != null; b = b.Next)
            {
                b.Visited = false;
                b.UnsafeLocalsMask = 0;
            }

            // First origin of being uninitialized: the non-parameter locals in the entry block.
            ulong startMask = 0;
            for (int i = _nparams; i < nlocals; i++)
                startMask |= 1UL << i;
            MaybePush(Entry, startMask, stack);

            // Second origin: there could be DELETE_FAST somewhere, so scan each block at least once.
            for (var b = Entry; b != null; b = b.Next)
                ScanBlockForLocals(b, stack);

            // Now propagate the uncertainty from the origins we found.
            while (stack.Count > 0)
            {
                var b = stack.Pop();
                b.Visited = false;
                ScanBlockForLocals(b, stack);
            }
        }

        private void MarkWarm()
        {
            for (var b = Entry; b != null; b = b.Next)
                b.Visited = false;
            var stack = new Stack<BasicBlock>();
            stack.Push(Entry);
            Entry.Visited = true;
            while (stack.Count > 0)
            {
                var b = stack.Pop();
                b.Warm = true;
                var next = b.Next;
                if (next != null && HasFallthrough(b) && !next.Visited)
                {
                    stack.Push(next);
                    next.Visited = true;
                }
                foreach (var instr in b.Instructions)
                {
                    if (IsJump(instr) && !instr.TargetBlock!.Visited)
                    {
                        stack.Push(instr.TargetBlock);
                        instr.TargetBlock.Visited = true;
                    }
                }
            }
        }

        private void MarkCold()
        {
            MarkWarm();
            for (var b = Entry; b != null; b = b.Next)
                b.Visited = false;
            var stack = new Stack<BasicBlock>();
            for (var b = Entry; b != null; b = b.Next)
            {
                if (b.IsExceptionHandler)
                {
                    stack.Push(b);
                    b.Visited = true;
                }
            }
            while (stack.Count > 0)
            {
                var b = stack.Pop();
                b.Cold = true;
                var next = b.Next;
                if (next != null && HasFallthrough(b) && !next.Warm && !next.Visited)
                {
                    stack.Push(next);
                    next.Visited = true;
                }
                foreach (var instr in b.Instructions)
                {
                    if (IsJump(instr) && !instr.TargetBlock!.Warm && !instr.TargetBlock.Visited)
                    {
                        stack.Push(instr.TargetBlock);
                        instr.TargetBlock.Visited = true;
                    }
                }
            }
        }

        private void PushColdBlocksToEnd()
        {
            // single basicblock, no need to reorder
            if (Entry.Next == null)
                return;
            MarkCold();

            // A cold block with fallthrough to a warm block gets an explicit jump.
            for (var b = Entry; b != null; b = b.Next)
            {
                if (b.Cold && HasFallthrough(b) && b.Next != null && b.Next.Warm)
                {
                    var explicitJump = _cfg.CreateBlock();
                    b.Next.Label = 1;
                    explicitJump.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.JUMP, 0, -1, -1, default, b.Next, null));
                    explicitJump.Cold = true;
                    explicitJump.Next = b.Next;
                    b.Next = explicitJump;
                }
            }

            BasicBlock? coldBlocks = null, coldBlocksTail = null;
            var block = Entry;
            while (block.Next != null)
            {
                while (block.Next != null && !block.Next.Cold)
                    block = block.Next;
                if (block.Next == null)
                    break;

                // block.Next is the beginning of a cold streak
                var end = block.Next;
                while (end.Next != null && end.Next.Cold)
                    end = end.Next;
                if (coldBlocks == null)
                    coldBlocks = block.Next;
                else
                    coldBlocksTail!.Next = block.Next;
                coldBlocksTail = end;
                block.Next = end.Next;
                end.Next = null;
            }
            block.Next = coldBlocks;
            if (coldBlocks != null)
                RemoveRedundantJumps();
        }

        /// <summary>insert_prefix_instructions: after the optimizer, so they have no neighbours' lines.</summary>
        public void InsertPrefixInstructions(int codeFlags, List<int> cells, int freeCount, int firstLineNo)
        {
            var prefix = new List<ByteCodeInstruction>();
            if (freeCount > 0)
                prefix.Add(new ByteCodeInstruction(ByteCodeOp.COPY_FREE_VARS, freeCount, -1));
            foreach (int cell in cells)
                prefix.Add(new ByteCodeInstruction(ByteCodeOp.MAKE_CELL, cell, -1));
            if ((codeFlags & (PyCodeObject.CO_GENERATOR | PyCodeObject.CO_COROUTINE | PyCodeObject.CO_ASYNC_GENERATOR)) != 0)
            {
                prefix.Add(new ByteCodeInstruction(ByteCodeOp.RETURN_GENERATOR, 0, firstLineNo));
                prefix.Add(new ByteCodeInstruction(ByteCodeOp.POP_TOP, 0, -1));
            }
            Entry.Instructions.InsertRange(0, prefix);
        }

        /// <summary>_PyCfg_ConvertPseudoOps</summary>
        // stack_effect (Python/compile.c)
        internal static int StackEffect(ByteCodeOp opcode, int oparg, bool jump)
        {
            switch (opcode)
            {
                case ByteCodeOp.POP_BLOCK:
                case ByteCodeOp.JUMP:
                case ByteCodeOp.JUMP_NO_INTERRUPT:
                    return 0;
                case ByteCodeOp.SETUP_FINALLY:
                case ByteCodeOp.SETUP_WITH:
                    return jump ? 1 : 0;
                case ByteCodeOp.SETUP_CLEANUP:
                    return jump ? 2 : 0;
                case ByteCodeOp.STORE_FAST_MAYBE_NULL:
                    return -1;
            }
            return OpcodeMetadata.NumPushed(opcode, oparg, jump) - OpcodeMetadata.NumPopped(opcode, oparg, jump);
        }

        // _PyCfg_Stackdepth
        public int Stackdepth(int codeFlags)
        {
            const int NOT_SEEN = int.MinValue;
            var startDepth = new Dictionary<BasicBlock, int>();
            var stack = new Stack<BasicBlock>();

            void Push(BasicBlock b, int depth)
            {
                if (!startDepth.ContainsKey(b))
                {
                    startDepth[b] = depth;
                    stack.Push(b);
                }
            }

            int maxDepth = 0;
            bool generator = (codeFlags & (0x20 | 0x80 | 0x200)) != 0;
            Push(Entry, generator ? 1 : 0);
            while (stack.Count != 0)
            {
                var b = stack.Pop();
                int depth = startDepth.GetValueOrDefault(b, NOT_SEEN);
                var next = b.Next;
                foreach (var instr in b.Instructions)
                {
                    int newDepth = depth + StackEffect(instr.OpCode, instr.Argument, false);
                    if (newDepth > maxDepth)
                        maxDepth = newDepth;
                    if (instr.TargetBlock != null)
                    {
                        int targetDepth = depth + StackEffect(instr.OpCode, instr.Argument, true);
                        if (targetDepth > maxDepth)
                            maxDepth = targetDepth;
                        Push(instr.TargetBlock, targetDepth);
                    }
                    depth = newDepth;
                    if (IsUnconditionalJump(instr.OpCode) || IsScopeExit(instr.OpCode))
                    {
                        next = null;
                        break;
                    }
                }
                if (next != null)
                    Push(next, depth);
            }
            return maxDepth;
        }

        public void ConvertPseudoOps()
        {
            for (var b = Entry; b != null; b = b.Next)
            {
                var code = b.Instructions;
                for (int i = 0; i < code.Count; i++)
                {
                    if (IsBlockPush(code[i].OpCode) || code[i].OpCode == ByteCodeOp.POP_BLOCK)
                        code[i] = SetOp(code[i], ByteCodeOp.NOP);
                    else if (code[i].OpCode == ByteCodeOp.STORE_FAST_MAYBE_NULL)
                        code[i] = SetOp(code[i], ByteCodeOp.STORE_FAST, code[i].Argument);
                }
            }
            for (var b = Entry; b != null; b = b.Next)
                RemoveRedundantNops(b);
        }

        /// <summary>normalize_jumps: a conditional jump only goes forward.</summary>
        public void NormalizeJumps()
        {
            for (var b = Entry; b != null; b = b.Next)
                b.Visited = false;
            for (var b = Entry; b != null; b = b.Next)
            {
                b.Visited = true;
                NormalizeJumpsInBlock(b);
            }
        }

        private void NormalizeJumpsInBlock(BasicBlock b)
        {
            if (b.Instructions.Count == 0)
                return;
            int lastIndex = b.Instructions.Count - 1;
            var last = b.Instructions[lastIndex];
            if (!IsJump(last) || !last.TargetBlock!.Visited)
                return;
            ByteCodeOp reversed;
            switch (last.OpCode)
            {
                case ByteCodeOp.POP_JUMP_IF_NOT_NONE: reversed = ByteCodeOp.POP_JUMP_IF_NONE; break;
                case ByteCodeOp.POP_JUMP_IF_NONE: reversed = ByteCodeOp.POP_JUMP_IF_NOT_NONE; break;
                case ByteCodeOp.POP_JUMP_IF_FALSE: reversed = ByteCodeOp.POP_JUMP_IF_TRUE; break;
                case ByteCodeOp.POP_JUMP_IF_TRUE: reversed = ByteCodeOp.POP_JUMP_IF_FALSE; break;
                default: return;
            }
            // 'conditional jump T' becomes 'reversed_jump b_next' followed by 'jump_backwards T'
            var backwardsJump = _cfg.CreateBlock();
            backwardsJump.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.JUMP, 0, last.LineNumber, last.ColumnOffset, last.End, last.TargetBlock, null));
            b.Instructions[lastIndex] = Retarget(last, reversed, b.Next!);
            backwardsJump.Cold = b.Cold;
            backwardsJump.Next = b.Next;
            b.Next = backwardsJump;
        }

        private static bool IsExitWithoutLineno(BasicBlock b)
        {
            if (!ExitsScope(b))
                return false;
            foreach (var instr in b.Instructions)
                if (instr.LineNumber >= 0)
                    return false;
            return true;
        }

        private void DuplicateExitsWithoutLineno()
        {
            // Copy all exit blocks without line number that are targets of a jump.
            for (var b = Entry; b != null; b = b.Next)
            {
                if (b.Instructions.Count == 0)
                    continue;
                int lastIndex = b.Instructions.Count - 1;
                var last = b.Instructions[lastIndex];
                if (IsJump(last))
                {
                    var target = last.TargetBlock!;
                    if (IsExitWithoutLineno(target) && target.Predecessors > 1)
                    {
                        var copy = _cfg.CreateBlock();
                        copy.Instructions.AddRange(target.Instructions);
                        copy.Instructions[0] = WithLine(copy.Instructions[0], last);
                        b.Instructions[lastIndex] = Retarget(last, last.OpCode, copy);
                        target.Predecessors--;
                        copy.Predecessors = 1;
                        copy.Next = target.Next;
                        copy.Label = 1;
                        copy.Cold = target.Cold;
                        target.Next = copy;
                    }
                }
            }

            // Any remaining reachable exit blocks without line number have a single predecessor.
            for (var b = Entry; b != null; b = b.Next)
            {
                if (HasFallthrough(b) && b.Next != null && b.Instructions.Count > 0 && IsExitWithoutLineno(b.Next))
                    b.Next.Instructions[0] = WithLine(b.Next.Instructions[0], b.Instructions[b.Instructions.Count - 1]);
            }
        }

        private void PropagateLineNumbers()
        {
            for (var b = Entry; b != null; b = b.Next)
            {
                var code = b.Instructions;
                if (code.Count == 0)
                    continue;
                ByteCodeInstruction? prev = null;
                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].LineNumber < 0)
                    {
                        if (prev != null)
                            code[i] = WithLine(code[i], prev.Value);
                    }
                    else
                    {
                        prev = code[i];
                    }
                }
                if (prev == null)
                    continue;
                if (HasFallthrough(b) && b.Next != null && b.Next.Predecessors == 1 && b.Next.Instructions[0].LineNumber < 0)
                    b.Next.Instructions[0] = WithLine(b.Next.Instructions[0], prev.Value);
                var last = code[code.Count - 1];
                if (IsJump(last) && last.TargetBlock!.Predecessors == 1 && last.TargetBlock.Instructions[0].LineNumber < 0)
                    last.TargetBlock.Instructions[0] = WithLine(last.TargetBlock.Instructions[0], prev.Value);
            }
        }

        private void GuaranteeLinenoForExits()
        {
            int lineno = _firstLineNo;
            for (var b = Entry; b != null; b = b.Next)
            {
                var code = b.Instructions;
                if (code.Count == 0)
                    continue;
                var last = code[code.Count - 1];
                if (last.LineNumber < 0)
                {
                    if (last.OpCode == ByteCodeOp.RETURN_VALUE)
                        for (int i = 0; i < code.Count; i++)
                            code[i] = WithLine(code[i], lineno);
                }
                else
                {
                    lineno = last.LineNumber;
                }
            }
        }

        private void ResolveLineNumbers()
        {
            DuplicateExitsWithoutLineno();
            PropagateLineNumbers();
            GuaranteeLinenoForExits();
        }
    }
}
