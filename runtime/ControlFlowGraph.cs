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
        /// CPython 3.12: compile.c:7517-7587 insert_prefix_instructions
        /// Insert prefix instructions into entry block BEFORE jump offset calculation
        /// This must be called AFTER CFG construction but BEFORE PyAssemble
        /// CRITICAL: Must adjust all JUMP targets that point to EntryBlock
        /// </summary>
        public void InsertPrefixInstructions(
            int codeFlags,
            List<string> cellVars,
            List<string> freeVars)
        {
            if (EntryBlock == null || EntryBlock.Instructions == null)
            {
                return;
            }

            bool isGenerator = (codeFlags & PyCodeObject.CO_GENERATOR) != 0;
            bool isCoroutine = (codeFlags & PyCodeObject.CO_COROUTINE) != 0;
            bool isAsyncGenerator = (codeFlags & PyCodeObject.CO_ASYNC_GENERATOR) != 0;

            int entryBlockOriginalInstructionCount = EntryBlock.Instructions.Count;
            int totalPrefixInstructions = 0;

            // Calculate how many prefix instructions we'll insert
            if (isGenerator || isCoroutine || isAsyncGenerator)
            {
                totalPrefixInstructions += 2; // RETURN_GENERATOR + POP_TOP
            }
            totalPrefixInstructions += cellVars.Count; // MAKE_CELL for each
            if (freeVars.Count > 0)
            {
                totalPrefixInstructions++; // COPY_FREE_VARS
            }

            // Save original instructions and adjust JUMP targets in EntryBlock
            var originalInstructions = new List<ByteCodeInstruction>(EntryBlock.Instructions);
            Console.WriteLine($"[CRITICAL] InsertPrefixInstructions: originalInstructions.Count = {originalInstructions.Count}, totalPrefixInstructions = {totalPrefixInstructions}");
            EntryBlock.Instructions.Clear();

            // Calculate entryBlockStartIndex (for adjusting jumps from other blocks)
            int entryBlockStartIndex = 0;
            foreach (var block in AllBlocks)
            {
                if (block == EntryBlock)
                {
                    break;
                }
                entryBlockStartIndex += block.Instructions.Count;
            }

            // CPython compile.c:7576-7584: Insert COPY_FREE_VARS at position 0
            if (freeVars.Count > 0)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔷 [CFG] Inserting COPY_FREE_VARS for {freeVars.Count} free variables");
#endif
                EntryBlock.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.COPY_FREE_VARS, freeVars.Count));
            }

            // CPython compile.c:7543-7574: Insert MAKE_CELL for each cellvar
            for (int cellIndex = 0; cellIndex < cellVars.Count; cellIndex++)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔷 [CFG] Inserting MAKE_CELL for {cellVars[cellIndex]} (index {cellIndex})");
#endif
                EntryBlock.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.MAKE_CELL, cellIndex));
            }

            // CPython compile.c:7523-7538: Insert RETURN_GENERATOR + POP_TOP for generators
            if (isGenerator || isCoroutine || isAsyncGenerator)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔷 [CFG] Inserting RETURN_GENERATOR + POP_TOP");
#endif
                EntryBlock.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.RETURN_GENERATOR, 0));
                EntryBlock.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.POP_TOP, 0));
            }

            // Now add original instructions, adjusting JUMP targets
            foreach (var instr in originalInstructions)
            {
                if (IsJumpInstruction(instr.OpCode))
                {
                    int targetIndex = instr.Argument;

                    // If JUMP targets within EntryBlock, adjust by prefix count
                    if (targetIndex >= entryBlockStartIndex &&
                        targetIndex < entryBlockStartIndex + entryBlockOriginalInstructionCount)
                    {
                        int newTargetIndex = targetIndex + totalPrefixInstructions;
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"🔷 [CFG] Adjusting JUMP {instr.OpCode} in EntryBlock: target {targetIndex} → {newTargetIndex}");
#endif
                        EntryBlock.Instructions.Add(new ByteCodeInstruction(
                            instr.OpCode,
                            newTargetIndex,
                            instr.LineNumber,
                            instr.ColumnOffset,
                            instr.FileName,
                            instr.ExceptHandler,
                            instr.ExceptionHandlerOffset
                        ));
                    }
                    else
                    {
                        EntryBlock.Instructions.Add(instr);
                    }
                }
                else
                {
                    EntryBlock.Instructions.Add(instr);
                }
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] Prefix instructions inserted. Entry block now has {EntryBlock.Instructions.Count} instructions");
            Console.WriteLine($"🔷 [CFG] Total prefix instructions: {totalPrefixInstructions}. Adjusting JUMP targets...");
#endif

            // CRITICAL: Adjust all JUMP instruction arguments that target the EntryBlock
            // Since we inserted prefix instructions at the beginning of EntryBlock,
            // all JUMP targets that point to EntryBlock need to be adjusted
            if (totalPrefixInstructions > 0)
            {
                AdjustJumpTargetsForPrefixInstructions(totalPrefixInstructions, entryBlockOriginalInstructionCount);
            }
        }

        /// <summary>
        /// Adjust JUMP instruction arguments after prefix instructions are inserted
        /// All JUMP targets that point to EntryBlock instructions need to be shifted
        /// </summary>
        private void AdjustJumpTargetsForPrefixInstructions(int prefixCount, int entryBlockOriginalCount)
        {
            // Calculate total instruction count before EntryBlock
            int entryBlockStartIndex = 0;

            // Find EntryBlock's starting index in the full instruction sequence
            foreach (var block in AllBlocks)
            {
                if (block == EntryBlock)
                {
                    break;
                }
                entryBlockStartIndex += block.Instructions.Count;
            }

            // Adjust all JUMP instructions in ALL blocks
            foreach (var block in AllBlocks)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var instr = block.Instructions[i];

                    // Check if this is a JUMP instruction
                    if (IsJumpInstruction(instr.OpCode))
                    {
                        int targetIndex = instr.Argument;

                        // If this JUMP targets an instruction in EntryBlock, adjust it
                        if (targetIndex >= entryBlockStartIndex &&
                            targetIndex < entryBlockStartIndex + entryBlockOriginalCount)
                        {
                            // This JUMP targets EntryBlock, adjust by prefix count
                            int newTargetIndex = targetIndex + prefixCount;
#if DEBUG_COMPILER_LOG
                            Console.WriteLine($"🔷 [CFG] Adjusting JUMP {instr.OpCode} in block {block.BlockId}: target {targetIndex} → {newTargetIndex}");
#endif
                            block.Instructions[i] = new ByteCodeInstruction(
                                instr.OpCode,
                                newTargetIndex,
                                instr.LineNumber,
                                instr.ColumnOffset,
                                instr.FileName,
                                instr.ExceptHandler,
                                instr.ExceptionHandlerOffset
                            );
                        }
                    }
                }
            }
        }

        /// <summary>
        /// CPython 3.12: Build CFG from InstructionSequence (label-based IR)
        /// This is the NEW path: InstructionSequence → CFG
        /// Uses CFGBuilder to convert label-based IR to CFG
        /// </summary>
        public static ControlFlowGraph FromInstructionSequence(InstructionSequence instrSeq)
        {
            return PyFlowGraph.Build(instrSeq);
        }

        /// <summary>
        /// CPython's assemble.c:push_instr_sequence() concept
        /// Convert linear instructions + exception table to CFG
        /// This is the LEGACY path: ByteCodeInstruction[] → CFG
        /// </summary>
        public static ControlFlowGraph FromInstructionsAndExceptionTable(
            List<ByteCodeInstruction> instructions,
            List<ExceptionTableEntry> exceptionTable)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [LEGACY] FromInstructionsAndExceptionTable 호출: {instructions.Count} instructions, {exceptionTable.Count} exception entries");
#endif
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
        /// Set ExceptionHandler for each INSTRUCTION (CPython's i_except_handler_info)
        /// This is instruction-level, not block-level
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

            // Mark exception handler blocks (for block-level metadata)
            foreach (var entry in exceptionTable)
            {
                if (handlerBlocks.TryGetValue(entry.HandlerOffset, out var handlerBlock))
                {
                    handlerBlock.IsExceptionHandler = true;
                    handlerBlock.StartDepth = entry.Depth;
                    handlerBlock.PreserveLasti = entry.Lasti;
                }
            }

            // Set ExceptionHandlerOffset for each INSTRUCTION
            // IMPORTANT: For nested try-except, we need the INNERMOST handler
            // So we process entries by range size (smallest first)
            var sortedEntries = exceptionTable
                .OrderBy(e => e.EndOffset - e.StartOffset)  // Smallest range first
                .ToList();

            // Iterate through blocks and instructions
            int currentOffset = 0;
            foreach (var block in blocks)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var instr = block.Instructions[i];
                    int handlerOffset = -1;
                    int depth = 0;
                    bool lasti = true;

                    // Find the innermost handler for this instruction offset
                    foreach (var entry in sortedEntries)
                    {
                        if (currentOffset >= entry.StartOffset && currentOffset < entry.EndOffset)
                        {
                            handlerOffset = entry.HandlerOffset;
                            depth = entry.Depth;
                            lasti = entry.Lasti;
                            break;  // Smallest range (innermost) wins
                        }
                    }

                    // Update instruction with handler info
                    block.Instructions[i] = new ByteCodeInstruction(
                        instr.OpCode,
                        instr.Argument,
                        instr.LineNumber,
                        instr.ColumnOffset,
                        instr.FileName,
                        instr.ExceptHandler,
                        handlerOffset  // This is the key: instruction-level handler offset
                    );

                    // Also set block-level handler for first instruction (compatibility)
                    if (i == 0 && handlerOffset >= 0 && handlerBlocks.TryGetValue(handlerOffset, out var handler))
                    {
                        block.ExceptionHandler = handler;
                        block.StartDepth = depth;
                        block.PreserveLasti = lasti;
                    }

                    currentOffset++;
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

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [LEGACY LinkBlocks] Linking {blocks.Count} blocks");
#endif

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];

                // Empty blocks always fall through to next block
                if (block.Instructions.Count == 0)
                {
                    if (i + 1 < blocks.Count)
                    {
                        block.Next = blocks[i + 1];
#if DEBUG_COMPILER_LOG
                        Console.WriteLine($"🔷 [LEGACY LinkBlocks]   Block {block.BlockId} (empty): Next → Block {block.Next.BlockId}");
#endif
                    }
                    continue;
                }

                var lastInstr = block.Instructions[block.Instructions.Count - 1];

#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔷 [LEGACY LinkBlocks]   Block {block.BlockId}: {block.Instructions.Count} instrs, last={lastInstr.OpCode}");
#endif

                // Set fallthrough (next block)
                if (i + 1 < blocks.Count && !IsUnconditionalJump(lastInstr.OpCode))
                {
                    block.Next = blocks[i + 1];
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"🔷 [LEGACY LinkBlocks]     Next → Block {block.Next.BlockId}");
#endif
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
        /// Build exception table from CFG (reads i_except_handler_info from instructions)
        /// IMPORTANT: Must traverse instructions and read ExceptionHandlerOffset field
        /// </summary>
        public List<ExceptionTableEntry> BuildExceptionTable()
        {
            var table = new List<ExceptionTableEntry>();

            // Collect all instructions with their handler info (INSTRUCTION-LEVEL)
            // CPython 3.12: assemble.c:150-161 reads i_except_handler_info (full struct)
            var instructions = new List<(int offset, ExceptHandlerInfo handlerInfo)>();
            int currentOffset = 0;
            foreach (var block in AllBlocks)
            {
                foreach (var instr in block.Instructions)
                {
                    instructions.Add((
                        currentOffset,
                        instr.ExceptHandler  // ← KEY: Use full ExceptHandlerInfo struct
                    ));
                    currentOffset++;
                }
            }

            if (instructions.Count == 0)
            {
                return table;
            }

            // Traverse instructions and detect handler changes
            // CPython 3.12: assemble.c:150-165 (same algorithm)
            ExceptHandlerInfo currentHandler = ExceptHandlerInfo.NoHandler;
            int startOffset = -1;

            for (int i = 0; i < instructions.Count; i++)
            {
                var (offset, handlerInfo) = instructions[i];

                // Check if exception handler changed
                // CPython: instr->i_except_handler_info.h_offset != handler.h_offset
                // We need to compare the full struct (offset, depth, lasti)
                if (handlerInfo.HandlerOffset != currentHandler.HandlerOffset ||
                    handlerInfo.StackDepth != currentHandler.StackDepth ||
                    handlerInfo.PreserveLasti != currentHandler.PreserveLasti)
                {
                    // Emit entry for previous handler region
                    if (currentHandler.HandlerOffset >= 0 && startOffset >= 0)
                    {
                        table.Add(new ExceptionTableEntry(
                            start: startOffset,
                            end: offset,
                            handler: currentHandler.HandlerOffset,
                            depth: currentHandler.StackDepth,
                            lasti: currentHandler.PreserveLasti
                        ));
                    }

                    // Start new handler region
                    currentHandler = handlerInfo;
                    startOffset = handlerInfo.HandlerOffset >= 0 ? offset : -1;
                }
            }

            // Final entry (CPython: assemble.c:162-165)
            if (currentHandler.HandlerOffset >= 0 && startOffset >= 0)
            {
                int endOffset = instructions.Count;

                table.Add(new ExceptionTableEntry(
                    start: startOffset,
                    end: endOffset,
                    handler: currentHandler.HandlerOffset,
                    depth: currentHandler.StackDepth,
                    lasti: currentHandler.PreserveLasti
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
