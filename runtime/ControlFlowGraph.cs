using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 Control Flow Graph (Python/flowgraph.c)
    /// Manages basic blocks for bytecode optimization
    /// </summary>
    public class ControlFlowGraph
    {
        public BasicBlock EntryBlock { get; set; }
        public List<BasicBlock> AllBlocks { get; set; }
        private int _nextBlockId = 0;

        public ControlFlowGraph()
        {
            AllBlocks = new List<BasicBlock>();
            EntryBlock = CreateBlock();
        }

        public BasicBlock CreateBlock()
        {
            var block = new BasicBlock(_nextBlockId++);
            AllBlocks.Add(block);
            return block;
        }

        /// <summary>
        /// CPython's assemble.c:push_instr_sequence() concept
        /// Convert linear instructions + exception table to CFG
        /// </summary>
        public static ControlFlowGraph FromInstructionsAndExceptionTable(
            List<ByteCodeInstruction> instructions,
            List<ExceptionTableEntry> exceptionTable)
        {
            var cfg = new ControlFlowGraph();

            if (instructions.Count == 0)
            {
                return cfg;
            }

            // Phase 1: Find block boundaries
            var blockStarts = FindBlockBoundaries(instructions, exceptionTable);

            // Phase 2: Build blocks
            var blocks = BuildBlocks(instructions, blockStarts);

            // Phase 3: Set exception handlers (CPython's i_except)
            SetExceptionHandlers(blocks, exceptionTable, instructions);

            // Phase 4: Link blocks (successors, next)
            LinkBlocks(blocks, instructions);

            cfg.AllBlocks = blocks;
            cfg.EntryBlock = blocks.Count > 0 ? blocks[0] : cfg.CreateBlock();

            return cfg;
        }

        /// <summary>
        /// Find all positions where a new block should start
        /// </summary>
        private static HashSet<int> FindBlockBoundaries(
            List<ByteCodeInstruction> instructions,
            List<ExceptionTableEntry> exceptionTable)
        {
            var boundaries = new HashSet<int>();

            // First instruction is always a block start
            boundaries.Add(0);

            // Jump targets are block starts
            for (int i = 0; i < instructions.Count; i++)
            {
                var instr = instructions[i];
                if (IsJumpInstruction(instr.OpCode))
                {
                    // Target of jump is a block start
                    int target = instr.Argument;
                    if (target >= 0 && target < instructions.Count)
                    {
                        boundaries.Add(target);
                    }

                    // Instruction after jump is a block start (for conditional jumps)
                    if (i + 1 < instructions.Count && IsConditionalJump(instr.OpCode))
                    {
                        boundaries.Add(i + 1);
                    }
                }
            }

            // Exception handler entry points are block starts
            foreach (var entry in exceptionTable)
            {
                if (entry.HandlerOffset >= 0 && entry.HandlerOffset < instructions.Count)
                {
                    boundaries.Add(entry.HandlerOffset);
                }

                // Start and end of protected regions are block boundaries
                if (entry.StartOffset >= 0 && entry.StartOffset < instructions.Count)
                {
                    boundaries.Add(entry.StartOffset);
                }
                if (entry.EndOffset > 0 && entry.EndOffset < instructions.Count)
                {
                    boundaries.Add(entry.EndOffset);
                }
            }

            return boundaries;
        }

        /// <summary>
        /// Split instructions into basic blocks
        /// </summary>
        private static List<BasicBlock> BuildBlocks(
            List<ByteCodeInstruction> instructions,
            HashSet<int> blockStarts)
        {
            var blocks = new List<BasicBlock>();
            var sortedStarts = blockStarts.OrderBy(x => x).ToList();

            for (int i = 0; i < sortedStarts.Count; i++)
            {
                int start = sortedStarts[i];
                int end = (i + 1 < sortedStarts.Count) ? sortedStarts[i + 1] : instructions.Count;

                var block = new BasicBlock(i);
                block.Offset = start;

                for (int j = start; j < end; j++)
                {
                    block.Instructions.Add(instructions[j]);
                }

                blocks.Add(block);
            }

            return blocks;
        }

        /// <summary>
        /// Set ExceptionHandler for each block (CPython's i_except)
        /// </summary>
        private static void SetExceptionHandlers(
            List<BasicBlock> blocks,
            List<ExceptionTableEntry> exceptionTable,
            List<ByteCodeInstruction> originalInstructions)
        {
            // Build handler block lookup
            var handlerBlocks = new Dictionary<int, BasicBlock>();
            foreach (var block in blocks)
            {
                handlerBlocks[block.Offset] = block;
                block.IsExceptionHandler = false;
            }

            // Mark exception handler blocks
            foreach (var entry in exceptionTable)
            {
                if (handlerBlocks.TryGetValue(entry.HandlerOffset, out var handlerBlock))
                {
                    handlerBlock.IsExceptionHandler = true;
                    handlerBlock.StartDepth = entry.Depth;
                    handlerBlock.PreserveLasti = entry.Lasti;
                }
            }

            // Set ExceptionHandler for protected blocks
            foreach (var entry in exceptionTable)
            {
                if (!handlerBlocks.TryGetValue(entry.HandlerOffset, out var handlerBlock))
                {
                    continue;
                }

                foreach (var block in blocks)
                {
                    // Check if this block is within the protected region
                    int blockStart = block.Offset;
                    int blockEnd = block.Offset + block.Instructions.Count;

                    if (blockStart >= entry.StartOffset && blockStart < entry.EndOffset)
                    {
                        block.ExceptionHandler = handlerBlock;
                        block.StartDepth = entry.Depth;
                        block.PreserveLasti = entry.Lasti;
                    }
                }
            }
        }

        /// <summary>
        /// Link blocks together (successors, next)
        /// </summary>
        private static void LinkBlocks(
            List<BasicBlock> blocks,
            List<ByteCodeInstruction> originalInstructions)
        {
            var offsetToBlock = blocks.ToDictionary(b => b.Offset, b => b);

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];

                if (block.Instructions.Count == 0)
                {
                    continue;
                }

                var lastInstr = block.Instructions[block.Instructions.Count - 1];

                // Set fallthrough (next block)
                if (i + 1 < blocks.Count && !IsUnconditionalJump(lastInstr.OpCode))
                {
                    block.Next = blocks[i + 1];
                }

                // Set jump successors
                if (IsJumpInstruction(lastInstr.OpCode))
                {
                    int target = lastInstr.Argument;
                    if (offsetToBlock.TryGetValue(target, out var targetBlock))
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

        /// <summary>
        /// CPython's assemble.c:assemble_exception_table()
        /// Build exception table from CFG (reads i_except from blocks)
        /// </summary>
        public List<ExceptionTableEntry> BuildExceptionTable()
        {
            var table = new List<ExceptionTableEntry>();

            BasicBlock? currentHandler = null;
            int startOffset = -1;
            int currentDepth = 0;
            bool currentLasti = true;

            foreach (var block in AllBlocks)
            {
                // Check if exception handler changed
                if (block.ExceptionHandler != currentHandler)
                {
                    // Emit entry for previous handler region
                    if (currentHandler != null && startOffset >= 0)
                    {
                        table.Add(new ExceptionTableEntry(
                            start: startOffset,
                            end: block.Offset,
                            handler: currentHandler.Offset,
                            depth: currentDepth,
                            lasti: currentLasti
                        ));
                    }

                    // Start new handler region
                    currentHandler = block.ExceptionHandler;
                    startOffset = block.Offset;
                    currentDepth = block.StartDepth;
                    currentLasti = block.PreserveLasti;
                }
            }

            // Final entry
            if (currentHandler != null && startOffset >= 0)
            {
                var lastBlock = AllBlocks[AllBlocks.Count - 1];
                int endOffset = lastBlock.Offset + lastBlock.Instructions.Count;

                table.Add(new ExceptionTableEntry(
                    start: startOffset,
                    end: endOffset,
                    handler: currentHandler.Offset,
                    depth: currentDepth,
                    lasti: currentLasti
                ));
            }

            return table;
        }

        /// <summary>
        /// Flatten CFG to linear instructions
        /// </summary>
        public List<ByteCodeInstruction> ToInstructions()
        {
            var result = new List<ByteCodeInstruction>();

            // Calculate offsets for each block
            int offset = 0;
            foreach (var block in AllBlocks)
            {
                block.Offset = offset;
                offset += block.Instructions.Count;
            }

            // Flatten blocks to instructions
            foreach (var block in AllBlocks)
            {
                result.AddRange(block.Instructions);
            }

            // TODO: Update jump targets only if CFG was modified
            // Since we're not doing CFG modifications yet, skip this
            // UpdateJumpTargets(result);

            return result;
        }

        /// <summary>
        /// Update jump instruction targets after flattening
        /// </summary>
        private void UpdateJumpTargets(List<ByteCodeInstruction> instructions)
        {
            // Build instruction offset to block mapping
            var instrOffsetToBlock = new Dictionary<int, BasicBlock>();
            int currentOffset = 0;
            foreach (var block in AllBlocks)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    instrOffsetToBlock[currentOffset + i] = block;
                }
                currentOffset += block.Instructions.Count;
            }

            // Update jump targets
            for (int i = 0; i < instructions.Count; i++)
            {
                var instr = instructions[i];
                if (IsJumpInstruction(instr.OpCode))
                {
                    // Find the block this jump belongs to
                    if (instrOffsetToBlock.TryGetValue(i, out var sourceBlock) &&
                        sourceBlock.Successors.Count > 0)
                    {
                        // Update jump target to first successor's offset
                        var targetBlock = sourceBlock.Successors[0];
                        instructions[i] = new ByteCodeInstruction(
                            instr.OpCode,
                            targetBlock.Offset,  // New target offset
                            instr.LineNumber,
                            instr.ColumnOffset,
                            instr.FileName,
                            instr.ExceptHandler
                        );
                    }
                }
            }
        }
    }
}
