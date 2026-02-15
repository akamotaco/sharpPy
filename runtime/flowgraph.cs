using System;
using System.Collections.Generic;
// Performance: Eliminated LINQ

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

            // Phase 5: Link ALL blocks via Next in AllBlocks order (creation order)
            // CPython: Python/flowgraph.c:243 - g->g_curblock->b_next = block
            // AllBlocks contains blocks in the order they were created (= InstructionSequence order after sorting)
#if DEBUG_LOG
            Console.WriteLine($"[FLOWGRAPH] AllBlocks.Count = {cfg.AllBlocks.Count}");
            for (int i = 0; i < cfg.AllBlocks.Count; i++)
            {
                Console.WriteLine($"[FLOWGRAPH] AllBlocks[{i}] = Block {cfg.AllBlocks[i].BlockId}, Instructions.Count={cfg.AllBlocks[i].Instructions.Count}");
            }
#endif
            for (int i = 0; i < cfg.AllBlocks.Count - 1; i++)
            {
                cfg.AllBlocks[i].Next = cfg.AllBlocks[i + 1];
#if DEBUG_LOG
                Console.WriteLine($"[FLOWGRAPH] Set Block {cfg.AllBlocks[i].BlockId}.Next = Block {cfg.AllBlocks[i + 1].BlockId}");
#endif
            }

            // NOTE: Exception handler propagation is already done in CreateBasicBlocks (Phase 3)
            // during instruction processing. No need for a separate phase.
            // CPython's label_exception_targets() happens during CFG build, not after.

            // Phase 6: Calculate predecessors and eliminate unreachable code
            // CPython 3.12: Python/flowgraph.c:1600-1610
            cfg.CalculatePredecessors();
            cfg.EliminateUnreachableCode();

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

                    // CPython 3.12: Python/flowgraph.c:809-820
                    // CRITICAL FIX: Labels may point to pseudo-instructions (SETUP_*, POP_BLOCK)
                    // We must find the next real instruction to use as block boundary
                    // Otherwise, jumps can land in the middle of an instruction sequence
                    // causing stack corruption (e.g., jumping to BUILD_LIST without preceding PUSH_NULL)
                    // runtime/flowgraph.cs:171-183
                    if (targetIndex >= 0 && targetIndex < instructions.Count)
                    {
                        int realTargetIndex = FindNextRealInstruction(instrSeq, targetIndex);
                        boundaries.Add(realTargetIndex);
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

                // CPython 3.12: Python/flowgraph.c:809-820
                // SETUP_* instructions (block push) create new basic blocks
                // The instruction AFTER SETUP_* is the start of the try block
                // CRITICAL: Even though SETUP_* is removed during CFG construction,
                // the try block must start at a block boundary so exception handler propagation works correctly
                if (IsBlockPush(instr.OpCode) && i + 1 < instructions.Count)
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
            // Performance: Eliminated LINQ - replaced OrderBy().ToList() with manual sorting
            var sortedStarts = new List<int>(blockStarts);
            sortedStarts.Sort();
            var instructions = instrSeq.Instructions;

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

            // CRITICAL: Link ALL blocks via Next in CREATION order (AllBlocks), NOT sorted order!
            // CPython: Python/flowgraph.c:243 - g->g_curblock->b_next = block
            // CPython links blocks in the order they're created, which matches the source code order
            // Linking in sorted order would put exception handler blocks BEFORE normal code!
            // We'll link them AFTER all blocks are added to cfg.AllBlocks

            // CPython 3.12 pattern: Per-block ExceptStack
            // Each block carries its own exception stack state
            // Store ExceptStack for each block (block start index → ExceptStack)
            var blockExceptStacks = new Dictionary<int, ExceptStack>();
            blockExceptStacks[sortedStarts[0]] = new ExceptStack(); // Entry block starts with empty stack

            // Second pass: process instructions with per-block ExceptStack
            // Track real instruction count to update block offsets
            int realInstructionCount = 0;

            // CPython 3.12: Python/flowgraph.c:stackdepth()
            // Track operand stack depth to correctly set ExceptionDepth.
            // The exception table's depth field = stack depth at the SETUP instruction,
            // so exception dispatch can unwind to that level (preserving e.g. for-loop iterators).
            int operandDepth = 0;

            for (int i = 0; i < sortedStarts.Count; i++)
            {
                int start = sortedStarts[i];
                int end = (i + 1 < sortedStarts.Count) ? sortedStarts[i + 1] : instructions.Count;

                var block = indexToBlock[start];

                // CPython pattern: Get this block's ExceptStack (or create if jump target)
                if (!blockExceptStacks.ContainsKey(start))
                {
                    // This block hasn't been visited yet - should not happen in well-formed code
                    blockExceptStacks[start] = new ExceptStack();
                }
                var exceptStack = blockExceptStacks[start];

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

                            // CPython 3.12: Python/flowgraph.c:809-820
                            // CRITICAL FIX: Labels may point to pseudo-instructions (SETUP_*, POP_BLOCK)
                            // Must find next real instruction before block lookup
                            // runtime/flowgraph.cs:284-298
                            if (targetIndex >= 0 && targetIndex < instructions.Count)
                            {
                                targetIndex = FindNextRealInstruction(instrSeq, targetIndex);
                            }

                            // IMPORTANT: Look up block using original targetIndex (block start),
                            // but the block's Offset will be updated to point to the real instruction
                            if (indexToBlock.TryGetValue(targetIndex, out var handlerBlock))
                            {
                                // FIX: Update handlerBlock.Offset to use correct CFG offset from labelToOffset
                                // handlerBlock.Offset is still the old InstructionSequence offset (includes pseudo-instructions)
                                // We need the CFG offset (after pseudo-instruction removal) from BuildLabelMapping
                                // CPython 3.12: Python/flowgraph.c:619-620
                                // Block offsets are recalculated during assembly
                                // No need to update handlerBlock.Offset here

                                // CPython pattern: Copy ExceptStack for target block (handler branch) BEFORE pushing
                                // CRITICAL: The handler block needs current stack state BEFORE pushing the new handler
                                // CPython flowgraph.c:811-820 does copy_except_stack() before push_except_block()
                                // This ensures cleanup code doesn't point to itself as the handler
                                if (!blockExceptStacks.ContainsKey(targetIndex))
                                {
                                    blockExceptStacks[targetIndex] = exceptStack.Copy();
                                }

                                // Now push the handler to the current block's stack (after copying)
                                exceptStack.Push(handlerBlock);

                                // CPython 3.12: Python/flowgraph.c:stackdepth()
                                // ExceptionDepth = operand stack depth at the SETUP instruction.
                                // This ensures exception dispatch preserves stack items below
                                // the try block (e.g. for-loop iterators).
                                // PreserveLasti: SETUP_CLEANUP/WITH preserve last instruction
                                // for reraise; SETUP_FINALLY does not.
                                if (instr.OpCode == ByteCodeOp.SETUP_CLEANUP || instr.OpCode == ByteCodeOp.SETUP_WITH)
                                {
                                    handlerBlock.PreserveLasti = true;
                                    handlerBlock.ExceptionDepth = operandDepth;
                                }
                                else // SETUP_FINALLY
                                {
                                    handlerBlock.PreserveLasti = false;
                                    handlerBlock.ExceptionDepth = operandDepth;
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

                    // CPython 3.12: Python/flowgraph.c:stackdepth()
                    // Track operand stack depth for exception table depth calculation.
                    // Uses fall-through stack effects (normal execution path).
                    {
                        var (pop, push) = StackEffectAnalyzer.GetStackEffect(instr.OpCode, instr.Arg ?? 0);
                        operandDepth += (push - pop);
                        if (operandDepth < 0) operandDepth = 0; // safety clamp
                    }

                    // Get current exception handler from stack
                    // CPython 3.12: flowgraph.c:825-849 (set i_except for each instruction)
                    // Store BasicBlock reference directly (CPython's i_except is a pointer to basicblock)
                    var currentHandlerBlock = exceptStack.Top();

#if DEBUG_LOG
                    if (currentHandlerBlock != null && (instr.OpCode == ByteCodeOp.RAISE_VARARGS || instr.OpCode == ByteCodeOp.LOAD_CONST))
                    {
                        Console.WriteLine($"[FLOWGRAPH-EXCEPT] Instr {instr.OpCode} at {realInstructionCount}, handler={currentHandlerBlock.Offset}, stack depth={exceptStack.Depth}");
                    }
#endif

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
                        // For jump instructions, resolve target BasicBlock for TargetBlock reference
                        // Also get CFG offset for Argument field
                        string labelKey = instr.Target.Value.ToString();
                        int targetCfgOffset;
                        BasicBlock? targetBlock = null;

                        if (labelToOffset.TryGetValue(labelKey, out targetCfgOffset))
                        {
                            // CPython 3.12: Python/flowgraph.c:589-593
                            // In CPython, each basicblock owns a label (b->b_label)
                            // When a label is used, it points directly to the block that owns it
                            //
                            // SharpPy: Labels point to InstructionSequence positions
                            // A block "owns" a label if the label points to the block's start position
                            // CRITICAL: Use exact match (blockStart == targetInstrIndex), NOT range check!
                            // Range check would match wrong blocks when pseudo-instructions are involved
                            int targetInstrIndex = instrSeq.GetLabelTarget(instr.Target.Value);

                            // CPython 3.12: Python/flowgraph.c:809-820
                            // CRITICAL FIX: Labels may point to pseudo-instructions (SETUP_*, POP_BLOCK)
                            // Must find next real instruction before block lookup
                            // runtime/flowgraph.cs:393-403
                            if (targetInstrIndex >= 0 && targetInstrIndex < instructions.Count)
                            {
                                targetInstrIndex = FindNextRealInstruction(instrSeq, targetInstrIndex);
                            }

                            // Direct lookup: label points to block start position
                            if (indexToBlock.TryGetValue(targetInstrIndex, out targetBlock))
                            {
                                // Found the block that owns this label
                            }
                            else
                            {
                                // Label points to a position that's not a block start
                                // This shouldn't happen in well-formed code
                                throw new InvalidOperationException(
                                    $"Label {instr.Target.Value} points to InstrSeq[{targetInstrIndex}], " +
                                    $"but no block starts at that position. Available block starts: " +
                                    string.Join(", ", sortedStarts.Take(10)));
                            }
                        }
                        else
                        {
                            // Fallback: resolve using InstructionSequence offset
                            // (This path is used when labelToOffset doesn't have the label)
                            int targetInstrIndex = instrSeq.GetLabelTarget(instr.Target.Value);

                            // CPython 3.12: Python/flowgraph.c:809-820
                            // CRITICAL FIX: Labels may point to pseudo-instructions (SETUP_*, POP_BLOCK)
                            // Must find next real instruction before block lookup
                            // runtime/flowgraph.cs:410-424
                            if (targetInstrIndex >= 0 && targetInstrIndex < instructions.Count)
                            {
                                targetInstrIndex = FindNextRealInstruction(instrSeq, targetInstrIndex);
                            }

                            // CPython 3.12: Check if target is within valid instruction range
                            // Labels pointing past the end of code are unreachable (after RETURN/RAISE)
                            if (targetInstrIndex >= 0 && targetInstrIndex < instructions.Count)
                            {
                                // CPython 3.12: Python/flowgraph.c:589-593
                                // Labels always point to block start positions
                                // Use direct lookup instead of range check
                                if (indexToBlock.TryGetValue(targetInstrIndex, out targetBlock))
                                {
                                    // Found the block
                                }

                                if (targetBlock == null)
                                {
                                    // CPython 3.12: This should never happen if blockStarts is correctly computed
                                    var blockInfo = string.Join(", ", sortedStarts.Select((start, i) => {
                                        int end = (i + 1 < sortedStarts.Count) ? sortedStarts[i + 1] : instructions.Count;
                                        return $"[{start}..{end})";
                                    }));
                                    throw new InvalidOperationException(
                                        $"Jump target block not found for instruction index {targetInstrIndex}. " +
                                        $"Total instructions: {instructions.Count}, Blocks: {blockInfo}, " +
                                        $"Jump from index {i} (opcode: {instr.OpCode})");
                                }

                                targetCfgOffset = targetBlock.Offset;
                            }
                            else
                            {
                                // Target is outside instruction range (unreachable code after RETURN/RAISE)
                                // Skip this instruction - it's dead code that will never execute
#if DEBUG_COMPILER_LOG
                                Console.WriteLine($"[flowgraph] Skipping jump from index {i} to out-of-range target {targetInstrIndex} (total instructions: {instructions.Count})");
#endif
                                continue;  // Skip adding this instruction to the CFG
                            }
                        }

                        #if DEBUG_COMPILER_LOG
                        if (instr.OpCode == ByteCodeOp.JUMP_NO_INTERRUPT)
                        {
                            Console.WriteLine($"[flowgraph] JUMP_NO_INTERRUPT target label {instr.Target.Value} resolves to CFG offset {targetCfgOffset}");
                        }
                        #endif

                        // CPython 3.12: Create instruction with TargetBlock and ExceptBlock references
                        bcInstr = new ByteCodeInstruction(
                            instr.OpCode,
                            targetCfgOffset,  // Argument: target CFG offset
                            instr.LineNumber,
                            instr.ColumnOffset,
                            instr.FileName,
                            targetBlock,       // TargetBlock: i_target (BasicBlock reference)
                            currentHandlerBlock  // ExceptBlock: i_except (BasicBlock reference)
                        );
                    }
                    else
                    {
                        // Regular instruction with integer argument (or no argument)
                        // CPython 3.12: Only set ExceptBlock (no TargetBlock for non-jump instructions)
                        bcInstr = new ByteCodeInstruction(
                            instr.OpCode,
                            instrArg,  // Use instrArg (may be modified for YIELD_VALUE)
                            instr.LineNumber,
                            instr.ColumnOffset,
                            instr.FileName,
                            null,              // TargetBlock: null (not a jump)
                            currentHandlerBlock  // ExceptBlock: i_except (BasicBlock reference)
                        );
                    }

                    block.Instructions.Add(bcInstr);
                    realInstructionCount++;  // Increment count for each real instruction added

                    // CPython pattern: Propagate ExceptStack to jump targets
                    if (instr.IsJump && instr.Target.HasValue)
                    {
                        int targetInstrIndex = instrSeq.GetLabelTarget(instr.Target.Value);
                        if (targetInstrIndex >= 0 && !blockExceptStacks.ContainsKey(targetInstrIndex))
                        {
                            blockExceptStacks[targetInstrIndex] = exceptStack.Copy();
                        }
                    }
                }

                // CPython pattern: Propagate ExceptStack to fallthrough block
                if (i + 1 < sortedStarts.Count)
                {
                    int nextBlockStart = sortedStarts[i + 1];
                    if (!blockExceptStacks.ContainsKey(nextBlockStart))
                    {
                        blockExceptStacks[nextBlockStart] = exceptStack.Copy();
                    }
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
            // Performance: Eliminated LINQ - replaced Select() with manual loop
            foreach (var startIndex in sortedStarts)
            {
                var block = indexToBlock[startIndex];
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

            // Update jump targets to use final block offsets
            // CPython: After block offset recalculation, jump i_oparg references updated b_offset
            // Note: ExceptBlock and TargetBlock are BasicBlock references, so they don't need offset updates
            foreach (var block in cfg.AllBlocks)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var instr = block.Instructions[i];

                    // Update jump target Argument (if this is a jump instruction)
                    if (IsJumpInstruction(instr.OpCode))
                    {
                        if (oldToNewOffset.TryGetValue(instr.Argument, out int updatedTargetOffset))
                        {
                            // Create new instruction with updated jump target offset
                            // TargetBlock and ExceptBlock references remain unchanged
                            block.Instructions[i] = new ByteCodeInstruction(
                                instr.OpCode,
                                updatedTargetOffset,  // Updated jump target offset
                                instr.LineNumber,
                                instr.ColumnOffset,
                                instr.FileName,
                                instr.TargetBlock,     // Keep same TargetBlock reference
                                instr.ExceptBlock      // Keep same ExceptBlock reference
                            );
                        }
                    }
                }
            }

            return indexToBlock;
        }

        // CPython 3.12: Exception handler resolution removed
        // ExceptBlock (BasicBlock reference) is set directly during CFG building

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
            // NOTE: block.Next is already set globally in CreateBasicBlocks() for ALL blocks
            // Here we only set Successors (control flow targets), not Next (iteration order)
            for (int i = 0; i < cfg.AllBlocks.Count; i++)
            {
                var block = cfg.AllBlocks[i];

                // Empty blocks don't have jump successors
                if (block.Instructions.Count == 0)
                {
                    continue;
                }

                var lastInstr = block.Instructions[block.Instructions.Count - 1];

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
                   op == ByteCodeOp.RETURN_VALUE ||
                   op == ByteCodeOp.RAISE_VARARGS;  // CPython: RAISE_VARARGS never returns
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

        /// <summary>
        /// CPython's copy_except_stack: Create a deep copy of the exception stack
        /// </summary>
        public ExceptStack Copy()
        {
            var copy = new ExceptStack();
            copy._depth = _depth;
            for (int i = 0; i < _depth; i++)
            {
                copy._handlers[i] = _handlers[i];
            }
            return copy;
        }
    }
}
