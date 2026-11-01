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
    }
}
