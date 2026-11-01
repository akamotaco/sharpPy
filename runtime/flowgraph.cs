using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 Python/flowgraph.c: instr_sequence_to_cfg and the preprocessing of
    /// _PyCfg_OptimizeCodeUnit. SETUP_* and POP_BLOCK stay in the graph until
    /// CFGOptimizer.ConvertPseudoOps, so handlers stay reachable through their setup.
    /// </summary>
    public static class PyFlowGraph
    {
        public static ControlFlowGraph Build(InstructionSequence instrSeq)
        {
            var cfg = new ControlFlowGraph();
            if (instrSeq.Count == 0)
                return cfg;

            var handlerDepths = ExceptionStackDepth.Analyze(instrSeq);
            var instructions = instrSeq.Instructions;
            var starts = FindBlockBoundaries(instrSeq);

            var blocks = new Dictionary<int, BasicBlock>();
            BasicBlock? previous = null;
            foreach (int start in starts)
            {
                var block = cfg.CreateBlock();
                blocks[start] = block;
                if (previous != null)
                    previous.Next = block;
                previous = block;
            }
            cfg.EntryBlock = blocks[0];
            foreach (int position in instrSeq.LabelPositions)
                if (position < instructions.Count)
                    blocks[position].Label = 1;

            for (int s = 0; s < starts.Count; s++)
            {
                var block = blocks[starts[s]];
                int end = s + 1 < starts.Count ? starts[s + 1] : instructions.Count;
                for (int j = starts[s]; j < end; j++)
                {
                    var instr = instructions[j];
                    BasicBlock? target = null;
                    if (instr.Target.HasValue)
                    {
                        int position = instrSeq.GetLabelTarget(instr.Target.Value);
                        // A label past the last instruction: this jump is dead code after a return.
                        if (position < 0 || position >= instructions.Count)
                            continue;
                        target = blocks[position];
                        if (CFGOptimizer.IsBlockPush(instr.OpCode))
                        {
                            // mark_except_handlers, and push_except_block's b_preserve_lasti
                            target.IsExceptionHandler = true;
                            target.PreserveLasti = instr.OpCode != ByteCodeOp.SETUP_FINALLY;
                            target.ExceptionDepth = handlerDepths.GetValueOrDefault(j);
                        }
                    }
                    block.Instructions.Add(new ByteCodeInstruction(instr.OpCode, instr.Arg ?? 0, instr.LineNumber,
                        instr.ColumnOffset, instr.FileName, target, null));
                }
            }

            LabelExceptionTargets(cfg);
            return cfg;
        }

        // cfg_builder_maybe_start_new_block: a label, or what follows a jump or a scope exit.
        private static List<int> FindBlockBoundaries(InstructionSequence instrSeq)
        {
            var instructions = instrSeq.Instructions;
            var boundaries = new HashSet<int> { 0 };
            foreach (int position in instrSeq.LabelPositions)
                if (position < instructions.Count)
                    boundaries.Add(position);
            for (int i = 0; i + 1 < instructions.Count; i++)
            {
                var instr = instructions[i];
                bool jump = instr.Target.HasValue && !CFGOptimizer.IsBlockPush(instr.OpCode);
                if (jump || CFGOptimizer.IsScopeExit(instr.OpCode))
                    boundaries.Add(i + 1);
            }
            var sorted = new List<int>(boundaries);
            sorted.Sort();
            return sorted;
        }

        // label_exception_targets: walk the graph with the stack of active handlers.
        private static void LabelExceptionTargets(ControlFlowGraph cfg)
        {
            var visited = new HashSet<BasicBlock>();
            var stacks = new Dictionary<BasicBlock, ExceptStack>();
            var todo = new Stack<BasicBlock>();
            todo.Push(cfg.EntryBlock);
            visited.Add(cfg.EntryBlock);
            stacks[cfg.EntryBlock] = new ExceptStack();
            while (todo.Count > 0)
            {
                var b = todo.Pop();
                ExceptStack? exceptStack = stacks[b];
                stacks.Remove(b);
                var handler = exceptStack.Top();
                var code = b.Instructions;
                for (int i = 0; i < code.Count; i++)
                {
                    var instr = code[i];
                    if (CFGOptimizer.IsBlockPush(instr.OpCode))
                    {
                        if (visited.Add(instr.TargetBlock!))
                        {
                            stacks[instr.TargetBlock!] = exceptStack!.Copy();
                            todo.Push(instr.TargetBlock!);
                        }
                        exceptStack!.Push(instr.TargetBlock!);
                        handler = instr.TargetBlock;
                    }
                    else if (instr.OpCode == ByteCodeOp.POP_BLOCK)
                    {
                        if (exceptStack!.Depth > 0)
                            exceptStack.Pop();
                        handler = exceptStack.Top();
                    }
                    else if (CFGOptimizer.IsJump(instr))
                    {
                        code[i] = new ByteCodeInstruction(instr.OpCode, instr.Argument, instr.LineNumber, instr.ColumnOffset,
                            instr.FileName, instr.TargetBlock, handler);
                        if (visited.Add(instr.TargetBlock!))
                        {
                            if (CFGOptimizer.HasFallthrough(b))
                            {
                                stacks[instr.TargetBlock!] = exceptStack!.Copy();
                            }
                            else
                            {
                                stacks[instr.TargetBlock!] = exceptStack!;
                                exceptStack = null;
                            }
                            todo.Push(instr.TargetBlock!);
                        }
                    }
                    else
                    {
                        int arg = instr.OpCode == ByteCodeOp.YIELD_VALUE ? exceptStack!.Depth : instr.Argument;
                        code[i] = new ByteCodeInstruction(instr.OpCode, arg, instr.LineNumber, instr.ColumnOffset,
                            instr.FileName, null, handler);
                    }
                }
                if (CFGOptimizer.HasFallthrough(b) && b.Next != null && visited.Add(b.Next))
                {
                    stacks[b.Next] = exceptStack!;
                    todo.Push(b.Next);
                }
            }
        }
    }

    /// <summary>CPython 3.12 flowgraph.c ExceptStack.</summary>
    internal class ExceptStack
    {
        private const int MaxBlocks = 20; // CO_MAXBLOCKS
        private readonly BasicBlock?[] _handlers = new BasicBlock?[MaxBlocks + 2];
        private int _depth = 0;

        public int Depth => _depth;

        public void Push(BasicBlock handler)
        {
            if (_depth > MaxBlocks)
                throw new Exception($"ExceptStack overflow: depth={_depth}, max={MaxBlocks}");
            _handlers[_depth++] = handler;
        }

        public void Pop() => _depth--;

        public BasicBlock? Top() => _depth > 0 ? _handlers[_depth - 1] : null;

        public ExceptStack Copy()
        {
            var copy = new ExceptStack();
            copy._depth = _depth;
            Array.Copy(_handlers, copy._handlers, _depth);
            return copy;
        }
    }
}
