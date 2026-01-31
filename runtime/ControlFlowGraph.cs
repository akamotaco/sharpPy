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
            // CRITICAL: Must iterate using b_next (execution order), NOT AllBlocks (creation order)!
            // This MUST match the iteration order used in assemble.cs
            // CPython: for (basicblock *b = entryblock; b != NULL; b = b->b_next)
            for (BasicBlock? block = EntryBlock; block != null; block = block.Next)
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
                    int wordCount = PyAssemble.CountInstructionWords(instr);
                    currentOffset += wordCount;

                    #if DEBUG_LOG
                    if (instr.ExceptBlock != null)
                    {
                        Console.WriteLine($"[EXCTABLE-BUILD] Instr {instr.OpCode} at offset {currentOffset - wordCount}, wordCount={wordCount}, exceptBlock={instr.ExceptBlock.Offset}");
                    }
                    #endif

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
        ///
        /// localsplus layout: [varnames] [non-param cellvars] [freevars]
        /// - Param cell vars: use their varnames index
        /// - Non-param cell vars: use nlocals + (position among non-param cells)
        /// - Free vars: use nlocals + num_nonparam_cells + freeIndex
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

            // Count non-param cells and assign localsplus offsets
            int nonParamCellCount = 0;
            for (int cellIndex = 0; cellIndex < ncellvars; cellIndex++)
            {
                string cellName = cellVars[cellIndex];
                int varIndex = varNames.IndexOf(cellName);
                if (varIndex >= 0)
                {
                    // Param cell: use varnames index
                    fixedmap[cellIndex] = varIndex;
                }
                else
                {
                    // Non-param cell: use nlocals + position among non-param cells
                    fixedmap[cellIndex] = nlocals + nonParamCellCount;
                    nonParamCellCount++;
                }
            }

            // Free vars: use nlocals + num_nonparam_cells + freeIndex
            for (int freeIndex = 0; freeIndex < nfreevars; freeIndex++)
            {
                fixedmap[ncellvars + freeIndex] = nlocals + nonParamCellCount + freeIndex;
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [CFG] FixCellOffsets: nlocals={nlocals}, ncellvars={ncellvars}, nfreevars={nfreevars}, nonParamCells={nonParamCellCount}");
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

            // CPython compile.c:7609-7629: Remap all cell/deref instructions
            // The compiler emits cell index (0 to ncellvars-1) for cell vars
            // and ncellvars + freeIndex for free vars
            // We remap cell indices to localsplus offset (varnames index for param cells,
            // or nlocals + position for non-param cells)
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
                                Console.WriteLine($"   Remapping {instr.OpCode}: arg {oldoffset} → {newoffset}");
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

        /// <summary>
        /// CPython 3.12: Python/flowgraph.c:1600-1610
        /// Calculate predecessor count for each block
        /// Used to eliminate unreachable code (blocks with predecessors == 0)
        /// </summary>
        public void CalculatePredecessors()
        {
            // Reset all predecessor counts
            foreach (var block in AllBlocks)
            {
                block.Predecessors = 0;
            }

            // Entry block always has 1 predecessor (the caller)
            if (EntryBlock != null)
            {
                EntryBlock.Predecessors = 1;
            }

            // Count predecessors based on control flow
            for (BasicBlock? b = EntryBlock; b != null; b = b.Next)
            {
                // Fallthrough to next block
                if (b.Next != null)
                {
                    b.Next.Predecessors++;
                }

                // Jump successors
                foreach (var successor in b.Successors)
                {
                    successor.Predecessors++;
                }
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"[CFG] Predecessor counts:");
            for (BasicBlock? b = EntryBlock; b != null; b = b.Next)
            {
                Console.WriteLine($"  Block {b.BlockId}: predecessors={b.Predecessors}, instructions={b.Instructions.Count}");
            }
#endif
        }

        /// <summary>
        /// CPython 3.12: Python/flowgraph.c:1600-1610
        /// Delete unreachable instructions (blocks with no predecessors)
        /// This eliminates dead code after RETURN/RAISE statements
        /// </summary>
        public void EliminateUnreachableCode()
        {
            // CPython: for (basicblock *b = g->g_entryblock; b != NULL; b = b->b_next) {
            //            if (b->b_predecessors == 0) {
            //                b->b_iused = 0;
            //            }
            //          }
            for (BasicBlock? b = EntryBlock; b != null; b = b.Next)
            {
                if (b.Predecessors == 0)
                {
#if DEBUG_COMPILER_LOG
                    Console.WriteLine($"[CFG] Eliminating unreachable Block {b.BlockId} ({b.Instructions.Count} instructions)");
#endif
                    b.Instructions.Clear();
                }
            }
        }
    }
}
