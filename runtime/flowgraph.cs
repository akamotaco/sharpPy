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

            // Phase 1: Build label name → instruction offset mapping
            // This is needed to resolve ExceptHandlerInfo.HandlerLabel → HandlerOffset
            var labelToOffset = BuildLabelMapping(instrSeq);

            // Phase 2: Find all block boundaries (where labels are placed + jump targets)
            var blockStarts = FindBlockBoundaries(instrSeq);

            // Phase 3: Create basic blocks from instruction ranges
            var indexToBlock = CreateBasicBlocks(cfg, instrSeq, blockStarts, labelToOffset);

            // Phase 4: Link blocks together (set successors and next)
            LinkBlocks(cfg, instrSeq, indexToBlock);

            return cfg;
        }

        /// <summary>
        /// Build mapping from label names to instruction offsets
        /// This is needed for resolving ExceptHandlerInfo.HandlerLabel
        /// </summary>
        private static Dictionary<string, int> BuildLabelMapping(InstructionSequence instrSeq)
        {
            var mapping = new Dictionary<string, int>();

            // Iterate through all labels and find their target offsets
            // InstructionSequence doesn't expose its label map directly,
            // so we need to scan for all possible label IDs
            for (int labelId = 0; labelId < 1000; labelId++)  // Reasonable upper bound
            {
                var label = new Label(labelId);
                int targetOffset = instrSeq.GetLabelTarget(label);

                if (targetOffset >= 0)
                {
                    // This label exists and points to targetOffset
                    // Store multiple name patterns that might be used
                    mapping[$"Label({labelId})"] = targetOffset;
                    mapping[$"with_cleanup_{labelId}"] = targetOffset;
                    mapping[$"label_{labelId}"] = targetOffset;
                }
            }

            return mapping;
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
            HashSet<int> blockStarts,
            Dictionary<string, int> labelToOffset)
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

                    // Resolve ExceptHandlerInfo: convert HandlerLabel → HandlerOffset
                    var resolvedHandler = ResolveExceptHandler(instr.ExceptHandler, labelToOffset);

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
                            resolvedHandler,  // Use resolved handler
                            resolvedHandler.HandlerOffset  // Set ExceptionHandlerOffset
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
                            resolvedHandler,  // Use resolved handler
                            resolvedHandler.HandlerOffset  // Set ExceptionHandlerOffset
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
        /// Resolve ExceptHandlerInfo: convert HandlerLabel (string) to HandlerOffset (int)
        /// </summary>
        private static ExceptHandlerInfo ResolveExceptHandler(
            ExceptHandlerInfo handler,
            Dictionary<string, int> labelToOffset)
        {
            // If no handler or handler label, return as-is
            if (handler.HandlerLabel == null)
            {
                return handler;
            }

            // Try to resolve label to offset
            if (labelToOffset.TryGetValue(handler.HandlerLabel, out int offset))
            {
                return new ExceptHandlerInfo(
                    handlerOffset: offset,
                    stackDepth: handler.StackDepth,
                    preserveLasti: handler.PreserveLasti,
                    handlerLabel: null  // Clear label after resolution
                );
            }
            else
            {
                return handler;  // Return unresolved
            }
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

                // Empty blocks always fall through to next block
                if (block.Instructions.Count == 0)
                {
                    if (i + 1 < cfg.AllBlocks.Count)
                    {
                        block.Next = cfg.AllBlocks[i + 1];
                    }
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
