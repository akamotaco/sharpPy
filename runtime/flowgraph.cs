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
        /// IMPORTANT: Skip pseudo-instructions and return offset of first real instruction
        /// </summary>
        private static Dictionary<string, int> BuildLabelMapping(InstructionSequence instrSeq)
        {
            var mapping = new Dictionary<string, int>();

            // CPython 3.12 approach: Build mapping AFTER accounting for pseudo-instruction removal
            // We need to calculate what the offset will be after SETUP_*/POP_BLOCK are skipped

            // First pass: Build InstructionSequence offset → CFG offset mapping
            var instrSeqToCfgOffset = new Dictionary<int, int>();
            int cfgOffset = 0;
            for (int i = 0; i < instrSeq.Instructions.Count; i++)
            {
                var instr = instrSeq.Instructions[i];

                // Skip pseudo-instructions
                if (IsBlockPush(instr.OpCode) || IsBlockPop(instr.OpCode))
                {
                    // Don't increment cfgOffset for pseudo-instructions
                    continue;
                }

                // Map InstructionSequence offset → CFG offset (after pseudo removal)
                instrSeqToCfgOffset[i] = cfgOffset;
                cfgOffset++;
            }

            // Second pass: Resolve labels to CFG offsets
            for (int labelId = 0; labelId < 1000; labelId++)
            {
                var label = new Label(labelId);
                int instrSeqOffset = instrSeq.GetLabelTarget(label);

                if (instrSeqOffset >= 0)
                {
                    // Find next real instruction in InstructionSequence
                    int realInstrSeqOffset = FindNextRealInstruction(instrSeq, instrSeqOffset);

                    // Convert to CFG offset (after pseudo-instruction removal)
                    if (instrSeqToCfgOffset.TryGetValue(realInstrSeqOffset, out int resolvedCfgOffset))
                    {
                        mapping[$"Label({labelId})"] = resolvedCfgOffset;
                        mapping[$"with_cleanup_{labelId}"] = resolvedCfgOffset;
                        mapping[$"label_{labelId}"] = resolvedCfgOffset;
                    }
                }
            }

            return mapping;
        }

        /// <summary>
        /// Find next real (non-pseudo) instruction starting from given offset
        /// CPython flowgraph.c removes pseudo-instructions before label resolution
        /// SharpPy resolves labels first, so we need to skip pseudo-instructions here
        /// </summary>
        private static int FindNextRealInstruction(InstructionSequence instrSeq, int startOffset)
        {
            var instructions = instrSeq.Instructions;

            for (int i = startOffset; i < instructions.Count; i++)
            {
                var instr = instructions[i];

                // Skip pseudo-instructions (SETUP_*, POP_BLOCK)
                if (IsBlockPush(instr.OpCode) || IsBlockPop(instr.OpCode))
                {
                    continue;  // Keep looking for real instruction
                }

                // Found first real instruction
                return i;
            }

            // No real instruction found after startOffset (shouldn't happen normally)
            // Return original offset as fallback
            return startOffset;
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

            // CPython 3.12: Create ExceptStack to track exception handlers
            var exceptStack = new ExceptStack();

            // Create instruction index → block mapping
            var indexToBlock = new Dictionary<int, BasicBlock>();

            // First pass: create blocks and map indices
            for (int i = 0; i < sortedStarts.Count; i++)
            {
                int start = sortedStarts[i];
                var block = cfg.CreateBlock();
                block.Offset = start;
                indexToBlock[start] = block;
            }

            // Second pass: process instructions with ExceptStack
            // Track real instruction count to update block offsets
            int realInstructionCount = 0;

            for (int i = 0; i < sortedStarts.Count; i++)
            {
                int start = sortedStarts[i];
                int end = (i + 1 < sortedStarts.Count) ? sortedStarts[i + 1] : instructions.Count;

                var block = indexToBlock[start];

                // IMPORTANT: Update block.Offset to point to first real instruction (not pseudo-instruction)
                // This is crucial for exception table generation
                int blockRealStart = -1;

                // Add instructions to this block
                for (int j = start; j < end; j++)
                {
                    var instr = instructions[j];

                    // CPython 3.12 pattern: Handle SETUP_*/POP_BLOCK pseudo-instructions
                    if (IsBlockPush(instr.OpCode))
                    {
                        // SETUP_FINALLY/CLEANUP/WITH: Push handler to ExceptStack
                        if (instr.Target.HasValue)
                        {
                            int targetIndex = instrSeq.GetLabelTarget(instr.Target.Value);
                            // IMPORTANT: Look up block using original targetIndex (block start),
                            // but the block's Offset will be updated to point to the real instruction
                            if (indexToBlock.TryGetValue(targetIndex, out var handlerBlock))
                            {
                                // FIX: Update handlerBlock.Offset to use correct CFG offset from labelToOffset
                                // handlerBlock.Offset is still the old InstructionSequence offset (includes pseudo-instructions)
                                // We need the CFG offset (after pseudo-instruction removal) from BuildLabelMapping
                                string labelKey = instr.Target.Value.ToString();
                                if (labelToOffset.TryGetValue(labelKey, out int correctCfgOffset))
                                {
                                    // Update the block's offset to the correct CFG offset
                                    handlerBlock.Offset = correctCfgOffset;
                                }

                                exceptStack.Push(handlerBlock);

                                // CPython 3.12 exception table semantics:
                                // - SETUP_FINALLY: depth=0 (pop all), lasti=false (don't preserve)
                                // - SETUP_CLEANUP: depth=1 (keep exception), lasti=true (preserve for reraise)
                                // - SETUP_WITH: depth=1, lasti=true
                                if (instr.OpCode == ByteCodeOp.SETUP_CLEANUP || instr.OpCode == ByteCodeOp.SETUP_WITH)
                                {
                                    handlerBlock.PreserveLasti = true;
                                    handlerBlock.ExceptionDepth = 1;
                                }
                                else // SETUP_FINALLY
                                {
                                    handlerBlock.PreserveLasti = false;
                                    handlerBlock.ExceptionDepth = 0;
                                }
                            }
                        }
                        // SKIP: Don't add pseudo-instruction to bytecode
                        continue;
                    }
                    else if (IsBlockPop(instr.OpCode))
                    {
                        // POP_BLOCK: Pop handler from ExceptStack
                        // NOTE: CPython uses per-block ExceptStack copies, so each handler branch
                        // gets a fresh copy and can independently pop the same SETUP_CLEANUP.
                        // SharpPy uses a simplified linear approach, so we ignore underflows here.
                        if (exceptStack.Depth > 0)
                        {
                            exceptStack.Pop();
                        }
                        // SKIP: Don't add pseudo-instruction to bytecode
                        continue;
                    }

                    // This is a real instruction - record block start if first in block
                    if (blockRealStart == -1)
                    {
                        blockRealStart = realInstructionCount;
                        block.Offset = realInstructionCount;
                    }

                    // Get current exception handler from stack
                    // CPython 3.12: flowgraph.c:825-849 (set i_except for each instruction)
                    var currentHandler = exceptStack.Top();
                    int handlerOffset = currentHandler?.Offset ?? -1;

                    // Resolve ExceptHandlerInfo: convert HandlerLabel → HandlerOffset
                    var resolvedHandler = ResolveExceptHandler(instr.ExceptHandler, labelToOffset);

                    // If no explicit handler info, use ExceptStack
                    // CPython 3.12: instr->i_except = handler (flowgraph.c:849)
                    if (resolvedHandler.HandlerOffset == -1 && handlerOffset >= 0)
                    {
                        // CPython 3.12: Get depth and lasti from handler block (not stack depth!)
                        // depth = number of stack values to preserve on exception
                        // lasti = whether to preserve last_i for reraise
                        int depth = currentHandler?.ExceptionDepth ?? 0;
                        bool lasti = currentHandler?.PreserveLasti ?? false;

                        resolvedHandler = new ExceptHandlerInfo(handlerOffset, depth, lasti);
                    }

                    // CPython 3.12: Special handling for YIELD_VALUE (flowgraph.c:846-847)
                    // YIELD_VALUE stores exception stack depth in its argument
                    int instrArg = instr.Arg ?? 0;
                    if (instr.OpCode == ByteCodeOp.YIELD_VALUE)
                    {
                        instrArg = exceptStack.Depth;
                    }

                    // Convert Instruction to ByteCodeInstruction
                    ByteCodeInstruction bcInstr;

                    if (instr.IsJump && instr.Target.HasValue)
                    {
                        // For jump instructions, resolve target to CFG offset using labelToOffset mapping
                        // IMPORTANT: Don't use targetBlock.Offset here because it might not be updated yet!
                        // labelToOffset already contains the correct CFG offset (after pseudo-instruction removal)
                        string labelKey = instr.Target.Value.ToString();
                        int targetCfgOffset;

                        if (labelToOffset.TryGetValue(labelKey, out targetCfgOffset))
                        {
                            // Use the pre-calculated CFG offset from labelToOffset
                        }
                        else
                        {
                            // Fallback: resolve using InstructionSequence offset (shouldn't happen for well-formed code)
                            int targetInstrIndex = instrSeq.GetLabelTarget(instr.Target.Value);

                            // Find which block contains this target instruction
                            BasicBlock? targetBlock = null;
                            for (int bi = 0; bi < sortedStarts.Count; bi++)
                            {
                                int blockStart = sortedStarts[bi];
                                int blockEnd = (bi + 1 < sortedStarts.Count) ? sortedStarts[bi + 1] : instructions.Count;

                                if (blockStart <= targetInstrIndex && targetInstrIndex < blockEnd)
                                {
                                    if (indexToBlock.TryGetValue(blockStart, out var candidateBlock))
                                    {
                                        targetBlock = candidateBlock;
                                        break;
                                    }
                                }
                            }

                            if (targetBlock == null)
                            {
                                throw new InvalidOperationException(
                                    $"Jump target block not found for instruction index {targetInstrIndex}");
                            }

                            targetCfgOffset = targetBlock.Offset;
                        }

                        #if DEBUG_COMPILER_LOG
                        if (instr.OpCode == ByteCodeOp.JUMP_NO_INTERRUPT)
                        {
                            Console.WriteLine($"[flowgraph] JUMP_NO_INTERRUPT target label {instr.Target.Value} resolves to CFG offset {targetCfgOffset}");
                        }
                        #endif

                        // Store target CFG offset
                        bcInstr = new ByteCodeInstruction(
                            instr.OpCode,
                            targetCfgOffset,  // Target CFG offset from labelToOffset mapping
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
                            instrArg,  // Use instrArg (may be modified for YIELD_VALUE)
                            instr.LineNumber,
                            instr.ColumnOffset,
                            instr.FileName,
                            resolvedHandler,  // Use resolved handler
                            resolvedHandler.HandlerOffset  // Set ExceptionHandlerOffset
                        );
                    }

                    block.Instructions.Add(bcInstr);
                    realInstructionCount++;  // Increment count for each real instruction added
                }
            }

            // Set entry block
            if (indexToBlock.TryGetValue(0, out var entryBlock))
            {
                cfg.EntryBlock = entryBlock;
            }

            // Recalculate block offsets after pseudo-opcodes were removed
            // (pseudo-opcodes like SETUP_FINALLY were skipped in the loop above)
            // IMPORTANT: Build mapping for ALL instruction offsets, not just block starts!
            // This is needed for updating ExceptionHandlerOffset which can point anywhere
            int finalOffset = 0;
            var oldToNewOffset = new Dictionary<int, int>();
            foreach (var block in sortedStarts.Select(s => indexToBlock[s]))
            {
                int oldBlockOffset = block.Offset;
                block.Offset = finalOffset;

                // Map ALL instructions in this block (not just block start)
                // Exception handler offsets can point to any instruction
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    oldToNewOffset[oldBlockOffset + i] = finalOffset + i;
                }

                finalOffset += block.Instructions.Count;
            }

            // Update jump targets and exception handler offsets to use final block offsets
            // CPython: After block offset recalculation, jump i_oparg references updated b_offset
            foreach (var block in cfg.AllBlocks)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var instr = block.Instructions[i];
                    bool needUpdate = false;
                    int newArgument = instr.Argument;
                    int newExceptionHandlerOffset = instr.ExceptionHandlerOffset;

                    // Update jump target (if this is a jump instruction)
                    if (IsJumpInstruction(instr.OpCode))
                    {
                        if (oldToNewOffset.TryGetValue(instr.Argument, out int updatedTargetOffset))
                        {
                            newArgument = updatedTargetOffset;
                            needUpdate = true;
                        }
                    }

                    // Update exception handler offset
                    if (instr.ExceptionHandlerOffset >= 0)
                    {
                        if (oldToNewOffset.TryGetValue(instr.ExceptionHandlerOffset, out int updatedHandlerOffset))
                        {
                            newExceptionHandlerOffset = updatedHandlerOffset;
                            needUpdate = true;
                        }
                    }

                    if (needUpdate)
                    {
                        // Create new instruction with updated offsets
                        block.Instructions[i] = new ByteCodeInstruction(
                            instr.OpCode,
                            newArgument,  // Updated jump target or original argument
                            instr.LineNumber,
                            instr.ColumnOffset,
                            instr.FileName,
                            instr.ExceptHandler,
                            newExceptionHandlerOffset  // Updated handler offset
                        );
                    }
                }
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
            return op == ByteCodeOp.JUMP ||
                   op == ByteCodeOp.JUMP_FORWARD ||
                   op == ByteCodeOp.JUMP_BACKWARD ||
                   op == ByteCodeOp.JUMP_NO_INTERRUPT ||
                   op == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT ||
                   op == ByteCodeOp.POP_JUMP_IF_FALSE ||
                   op == ByteCodeOp.POP_JUMP_IF_TRUE ||
                   op == ByteCodeOp.POP_JUMP_IF_NONE ||
                   op == ByteCodeOp.POP_JUMP_IF_NOT_NONE ||
                   op == ByteCodeOp.FOR_ITER ||
                   op == ByteCodeOp.SEND;  // CPython 3.12: SEND jumps on StopIteration
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

        private static bool IsBlockPush(ByteCodeOp op)
        {
            return op == ByteCodeOp.SETUP_FINALLY ||
                   op == ByteCodeOp.SETUP_CLEANUP ||
                   op == ByteCodeOp.SETUP_WITH;
        }

        private static bool IsBlockPop(ByteCodeOp op)
        {
            return op == ByteCodeOp.POP_BLOCK;
        }
    }

    /// <summary>
    /// CPython 3.12: ExceptStack - Track exception handlers during CFG building
    /// Corresponds to CPython's except_stack in flowgraph.c
    /// </summary>
    internal class ExceptStack
    {
        private const int MaxBlocks = 20; // CO_MAXBLOCKS from CPython
        private readonly BasicBlock?[] _handlers = new BasicBlock?[MaxBlocks];
        private int _depth = 0;

        public int Depth => _depth;

        public void Push(BasicBlock handler)
        {
            if (_depth >= MaxBlocks)
            {
                throw new Exception($"ExceptStack overflow: depth={_depth}, max={MaxBlocks}");
            }
            _handlers[_depth++] = handler;
        }

        public BasicBlock? Pop()
        {
            if (_depth <= 0)
            {
                throw new Exception("ExceptStack underflow");
            }
            return _handlers[--_depth];
        }

        public BasicBlock? Top()
        {
            return _depth > 0 ? _handlers[_depth - 1] : null;
        }
    }
}
