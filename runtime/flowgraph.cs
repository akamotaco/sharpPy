using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12: Python/flowgraph.c - Build CFG from InstructionSequence
    /// Converts label-based InstructionSequence to BasicBlock-based CFG
    /// </summary>
    public static class PyFlowGraph
    {
        /// <summary>
        /// CPython 3.12: flowgraph_from_instr_sequence()
        /// Convert InstructionSequence (labels) to ControlFlowGraph (basic blocks)
        /// </summary>
        public static ControlFlowGraph Build(InstructionSequence instrSeq)
        {
            var cfg = new ControlFlowGraph();

            if (instrSeq.Count == 0)
            {
                return cfg;
            }

            // Phase 1: Find all block boundaries (where labels are placed + jump targets)
            var blockStarts = FindBlockBoundaries(instrSeq);

            // Phase 2: Create basic blocks from instruction ranges
            var indexToBlock = CreateBasicBlocks(cfg, instrSeq, blockStarts);

            // Phase 3: Link blocks together (set successors and next)
            LinkBlocks(cfg, instrSeq, indexToBlock);

            return cfg;
        }

        /// <summary>
        /// Find all positions where a new basic block should start
        /// - Position 0 (entry point)
        /// - Positions where labels are used (UseLabel was called)
        /// - Positions after jump instructions (for fallthrough)
        /// </summary>
        private static HashSet<int> FindBlockBoundaries(InstructionSequence instrSeq)
        {
            var boundaries = new HashSet<int>();

            // First instruction is always a block start
            boundaries.Add(0);

            var instructions = instrSeq.Instructions;

            for (int i = 0; i < instructions.Count; i++)
            {
                var instr = instructions[i];

                // If this instruction is a jump, the target label position is a block start
                if (instr.IsJump && instr.Target.HasValue)
                {
                    int targetIndex = instrSeq.GetLabelTarget(instr.Target.Value);
                    if (targetIndex >= 0)
                    {
                        boundaries.Add(targetIndex);
                    }

                    // Instruction after conditional jump is a block start (fallthrough path)
                    if (IsConditionalJump(instr.OpCode) && i + 1 < instructions.Count)
                    {
                        boundaries.Add(i + 1);
                    }
                }

                // Instruction after unconditional jump is a block start (unreachable code or new entry)
                if (IsUnconditionalJump(instr.OpCode) && i + 1 < instructions.Count)
                {
                    boundaries.Add(i + 1);
                }
            }

            return boundaries;
        }

        /// <summary>
        /// Create BasicBlocks from instruction ranges
        /// Returns mapping: instruction index → BasicBlock (for jump target resolution)
        /// </summary>
        private static Dictionary<int, BasicBlock> CreateBasicBlocks(
            ControlFlowGraph cfg,
            InstructionSequence instrSeq,
            HashSet<int> blockStarts)
        {
            var sortedStarts = blockStarts.OrderBy(x => x).ToList();
            var instructions = instrSeq.Instructions;

            // Create instruction index → block mapping
            var indexToBlock = new Dictionary<int, BasicBlock>();

            for (int i = 0; i < sortedStarts.Count; i++)
            {
                int start = sortedStarts[i];
                int end = (i + 1 < sortedStarts.Count) ? sortedStarts[i + 1] : instructions.Count;

                var block = cfg.CreateBlock();
                block.Offset = start;

                // Add instructions to this block
                for (int j = start; j < end; j++)
                {
                    var instr = instructions[j];

                    // Convert Instruction to ByteCodeInstruction
                    ByteCodeInstruction bcInstr;

                    if (instr.IsJump && instr.Target.HasValue)
                    {
                        // For jump instructions, keep target as label reference for now
                        // We'll resolve to actual offset later in LinkBlocks
                        int targetIndex = instrSeq.GetLabelTarget(instr.Target.Value);
                        bcInstr = new ByteCodeInstruction(
                            instr.OpCode,
                            targetIndex,  // Temporary: instruction index (will be updated later)
                            instr.LineNumber,
                            instr.ColumnOffset,
                            instr.FileName,
                            instr.ExceptHandler
                        );
                    }
                    else
                    {
                        // Regular instruction with integer argument (or no argument)
                        bcInstr = new ByteCodeInstruction(
                            instr.OpCode,
                            instr.Arg ?? 0,
                            instr.LineNumber,
                            instr.ColumnOffset,
                            instr.FileName,
                            instr.ExceptHandler
                        );
                    }

                    block.Instructions.Add(bcInstr);
                }

                indexToBlock[start] = block;
            }

            // Set entry block
            if (indexToBlock.TryGetValue(0, out var entryBlock))
            {
                cfg.EntryBlock = entryBlock;
            }

            return indexToBlock;
        }

        /// <summary>
        /// Link blocks together: set successors and next pointers
        /// Also resolve jump targets from instruction indices to actual block references
        /// </summary>
        private static void LinkBlocks(
            ControlFlowGraph cfg,
            InstructionSequence instrSeq,
            Dictionary<int, BasicBlock> indexToBlock)
        {
            // For each block, link to successors
            for (int i = 0; i < cfg.AllBlocks.Count; i++)
            {
                var block = cfg.AllBlocks[i];

                if (block.Instructions.Count == 0)
                {
                    continue;
                }

                var lastInstr = block.Instructions[block.Instructions.Count - 1];

                // Set fallthrough (next block)
                if (i + 1 < cfg.AllBlocks.Count && !IsUnconditionalJump(lastInstr.OpCode))
                {
                    block.Next = cfg.AllBlocks[i + 1];
                }

                // Set jump successors
                if (IsJumpInstruction(lastInstr.OpCode))
                {
                    int targetIndex = lastInstr.Argument;
                    if (indexToBlock.TryGetValue(targetIndex, out var targetBlock))
                    {
                        block.Successors.Add(targetBlock);
                    }
                }

                // Conditional jumps also have fallthrough as successor
                if (IsConditionalJump(lastInstr.OpCode) && block.Next != null)
                {
                    block.Successors.Add(block.Next);
                }
            }
        }

        private static bool IsJumpInstruction(ByteCodeOp op)
        {
            return op == ByteCodeOp.JUMP_FORWARD ||
                   op == ByteCodeOp.JUMP_BACKWARD ||
                   op == ByteCodeOp.POP_JUMP_IF_FALSE ||
                   op == ByteCodeOp.POP_JUMP_IF_TRUE ||
                   op == ByteCodeOp.POP_JUMP_IF_NONE ||
                   op == ByteCodeOp.POP_JUMP_IF_NOT_NONE ||
                   op == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT ||
                   op == ByteCodeOp.FOR_ITER;
        }

        private static bool IsConditionalJump(ByteCodeOp op)
        {
            return op == ByteCodeOp.POP_JUMP_IF_FALSE ||
                   op == ByteCodeOp.POP_JUMP_IF_TRUE ||
                   op == ByteCodeOp.POP_JUMP_IF_NONE ||
                   op == ByteCodeOp.POP_JUMP_IF_NOT_NONE ||
                   op == ByteCodeOp.FOR_ITER;
        }

        private static bool IsUnconditionalJump(ByteCodeOp op)
        {
            return op == ByteCodeOp.JUMP_FORWARD ||
                   op == ByteCodeOp.JUMP_BACKWARD ||
                   op == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT ||
                   op == ByteCodeOp.RETURN_VALUE;
        }
    }
}
