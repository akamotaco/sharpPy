using System;
using System.Collections.Generic;
// Performance: Eliminated LINQ - no LINQ usage found

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
        /// DEPRECATED: This method is no longer used (as of 2025-10-26)
        /// Prefix instructions are now inserted in InstructionSequence BEFORE CFG conversion
        /// This ensures labels are automatically adjusted, avoiding the JUMP_BACKWARD bug
        /// See: InstructionSequence.InsertPrefixInstructions()
        ///
        /// OLD APPROACH (buggy):
        /// 1. InstructionSequence with labels
        /// 2. Convert to CFG (labels → instruction indexes)
        /// 3. Insert prefix instructions (shifts everything, breaks jumps!)
        ///
        /// NEW APPROACH (correct):
        /// 1. InstructionSequence with labels
        /// 2. Insert prefix instructions (InsertAt automatically adjusts labels)
        /// 3. Convert to CFG (labels already point correctly)
        /// </summary>
        [Obsolete("Use InstructionSequence.InsertPrefixInstructions() instead")]
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
                            instr.TargetBlock,
                            instr.ExceptBlock
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

                        // If this JUMP targets an instruction in EntryBlock or later, adjust it
                        // Because InsertPrefixInstructions shifts ALL instructions in EntryBlock and beyond
                        if (targetIndex >= entryBlockStartIndex)
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
                                instr.TargetBlock,
                                instr.ExceptBlock
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

        // Helper methods for opcode classification
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

        /// <summary>
        /// CPython's assemble.c:assemble_exception_table()
        /// Build exception table from CFG (reads i_except_handler_info from instructions)
        /// IMPORTANT: Must traverse instructions and read ExceptionHandlerOffset field
        /// </summary>
        public List<ExceptionTableEntry> BuildExceptionTable()
        {
            var table = new List<ExceptionTableEntry>();

            // Collect all instructions with their exception handler BasicBlock AND block boundaries
            // CPython 3.12: Each basic block can have separate exception table entries
            // even if they share the same handler (using BasicBlock reference, not offset)
            var instructions = new List<(int offset, BasicBlock? exceptBlock, bool isBlockStart)>();
            int currentOffset = 0;

            // CPython 3.12: Python/assemble.c:143-166
            // CRITICAL: Process blocks in instruction order (by block.Offset), NOT AllBlocks order!
            // AllBlocks order is creation order, which may not match instruction sequence order
            // (e.g., exception handler blocks created before fallthrough blocks)
            var sortedBlocks = new List<BasicBlock>(AllBlocks);
            sortedBlocks.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            foreach (var block in sortedBlocks)
            {
                bool isFirst = true;
                foreach (var instr in block.Instructions)
                {
                    instructions.Add((
                        currentOffset,
                        instr.ExceptBlock,  // CPython's i_except: BasicBlock reference
                        isFirst  // Mark first instruction of each block
                    ));

                    // CPython 3.12: Must account for inline cache size
                    // Include/internal/pycore_opcode.h - _PyOpcode_Caches table
                    currentOffset += PyAssemble.CountInstructionWords(instr);
                    isFirst = false;
                }
            }

            if (instructions.Count == 0)
            {
                return table;
            }

            // Traverse instructions and detect handler changes OR block boundaries
            // CPython 3.12: assemble.c:150-165 + flowgraph.c per-block except_stack
            BasicBlock? currentHandlerBlock = null;
            int startOffset = -1;

            for (int i = 0; i < instructions.Count; i++)
            {
                var (offset, exceptBlock, isBlockStart) = instructions[i];

                // Check if exception handler changed OR new basic block started
                // CPython: instr->i_except != handler (BasicBlock pointer comparison)
                // SharpPy extension: Also split on basic block boundaries (CPython has per-block except_stack)
                bool handlerChanged = exceptBlock != currentHandlerBlock;
                bool shouldSplit = handlerChanged || (isBlockStart && i > 0 && currentHandlerBlock != null);

                if (shouldSplit)
                {
                    // Emit entry for previous handler region
                    if (currentHandlerBlock != null && startOffset >= 0)
                    {
                        // Convert instruction index to byte offset (index * 2)
                        // CPython 3.12 stores instruction word offset, displays as byte offset
                        // Get handler offset, depth, and lasti from BasicBlock
                        int handlerOffset = currentHandlerBlock.Offset;
                        int depth = currentHandlerBlock.ExceptionDepth;
                        bool lasti = currentHandlerBlock.PreserveLasti;

                        table.Add(new ExceptionTableEntry(
                            start: startOffset * 2,
                            end: offset * 2,
                            handler: handlerOffset * 2,
                            depth: depth,
                            lasti: lasti
                        ));
                    }

                    // Start new handler region
                    currentHandlerBlock = exceptBlock;
                    startOffset = exceptBlock != null ? offset : -1;
                }
            }

            // Final entry (CPython: assemble.c:162-165)
            if (currentHandlerBlock != null && startOffset >= 0)
            {
                int endOffset = instructions.Count;

                // Get handler info from BasicBlock
                int handlerOffset = currentHandlerBlock.Offset;
                int depth = currentHandlerBlock.ExceptionDepth;
                bool lasti = currentHandlerBlock.PreserveLasti;

                // Convert instruction index to byte offset (index * 2)
                table.Add(new ExceptionTableEntry(
                    start: startOffset * 2,
                    end: endOffset * 2,
                    handler: handlerOffset * 2,
                    depth: depth,
                    lasti: lasti
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
                            instr.TargetBlock
                        );
                    }
                }
            }
        }

        /// <summary>
        /// CPython 3.12: compile.c:7590-7632 fix_cell_offsets()
        /// Remap cell/free variable indices to localsplus offsets
        /// CRITICAL: Must be called AFTER CFG is built but BEFORE final assembly
        /// </summary>
        public void FixCellOffsets(List<string> varNames, List<string> cellVars, List<string> freeVars)
        {
            int nlocals = varNames.Count;
            int ncellvars = cellVars.Count;
            int nfreevars = freeVars.Count;
            int noffsets = ncellvars + nfreevars;

            if (noffsets == 0)
            {
                return; // No cell/free vars to remap
            }

            // CPython compile.c:7484-7514: build_cellfixedoffsets()
            // Build mapping: cellvar_index → localsplus_offset
            int[] fixedmap = new int[noffsets];

            // Initialize: cellvars at nlocals+i, freevars at nlocals+ncellvars+i
            for (int i = 0; i < noffsets; i++)
            {
                fixedmap[i] = nlocals + i;
            }

            // If a cellvar is also a parameter (in varNames), use parameter's index
            for (int cellIndex = 0; cellIndex < ncellvars; cellIndex++)
            {
                string cellName = cellVars[cellIndex];
                int varIndex = varNames.IndexOf(cellName);
                if (varIndex >= 0)
                {
                    // This cellvar is also a parameter - use parameter's localsplus offset
                    fixedmap[cellIndex] = varIndex;
                }
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] FixCellOffsets: nlocals={nlocals}, ncellvars={ncellvars}, nfreevars={nfreevars}");
            Console.WriteLine($"   varNames: [{string.Join(", ", varNames)}]");
            for (int i = 0; i < ncellvars; i++)
            {
                Console.WriteLine($"   cellvar[{i}] '{cellVars[i]}' → localsplus[{fixedmap[i]}]");
            }
            for (int i = 0; i < nfreevars; i++)
            {
                Console.WriteLine($"   freevar[{i}] '{freeVars[i]}' → localsplus[{fixedmap[ncellvars + i]}]");
            }
#endif

            // CPython compile.c:7609-7629: Remap all deref/cell instructions
            foreach (var block in AllBlocks)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var instr = block.Instructions[i];
                    int oldoffset = instr.Argument;

                    switch (instr.OpCode)
                    {
                        case ByteCodeOp.MAKE_CELL:
                        case ByteCodeOp.LOAD_CLOSURE:
                        case ByteCodeOp.LOAD_DEREF:
                        case ByteCodeOp.STORE_DEREF:
                        case ByteCodeOp.DELETE_DEREF:
                        case ByteCodeOp.LOAD_FROM_DICT_OR_DEREF:
                            if (oldoffset >= 0 && oldoffset < noffsets)
                            {
                                int newoffset = fixedmap[oldoffset];
#if DEBUG_COMPILER_LOG
                                if (instr.OpCode == ByteCodeOp.MAKE_CELL)
                                {
                                    Console.WriteLine($"   Remapping {instr.OpCode}: arg {oldoffset} → {newoffset}");
                                }
#endif
                                block.Instructions[i] = new ByteCodeInstruction(
                                    instr.OpCode,
                                    newoffset,
                                    instr.LineNumber,
                                    instr.ColumnOffset,
                                    instr.FileName,
                                    instr.TargetBlock,
                                    instr.ExceptBlock
                                );
                            }
                            break;
                    }
                }
            }
        }
    }
}
