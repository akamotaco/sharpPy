using System;
using System.Collections.Generic;
// Performance: Eliminated LINQ

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 flowgraph.c optimization functions
    /// Optimizes control flow graph at the block level
    /// </summary>
    public class CFGOptimizer
    {
        private ControlFlowGraph _cfg;
        private List<PyObject> _constants;

        public CFGOptimizer(ControlFlowGraph cfg, List<PyObject> constants)
        {
            _cfg = cfg;
            _constants = constants;
        }

        /// <summary>
        /// Apply all CFG optimizations (CPython's optimize_cfg)
        /// CPython 3.12: flowgraph.c optimize_cfg (lines 1581-1617)
        /// </summary>
        public void Optimize()
        {
            // CPython 3.12 optimization order (structural optimizations only)
            // NOTE: CPython 3.12 does NOT perform peephole optimizations at CFG level
            // Peephole optimizations (constant folding, etc.) are done at AST level in ast_opt.c

            OptimizePatternMatchingSwaps(); // Remove redundant SWAPs from pattern matching (TEMPORARY FIX)
            RemoveUnreachableBlocks();       // Mark and remove unreachable blocks
            EliminateEmptyBlocks();          // Remove blocks with no instructions
            RemoveRedundantJumps();          // Remove jumps to next block (fallthrough)
            PushColdBlocksToEnd();           // CPython 3.12: flowgraph.c:1963-2039 - move cold blocks to end

            // NOTE: CPython 3.12 also does:
            // - inline_small_exit_blocks() - not implemented yet
            // - remove_redundant_nops_and_pairs() - removed in CPython 3.11+
            // - mark_reachable() + delete unreachable - covered by RemoveUnreachableBlocks()
        }

        /// <summary>
        /// TEMPORARY FIX: Remove all consecutive SWAPs that follow UNPACK_EX in pattern matching
        /// CPython reference: Python/flowgraph.c lines 1217-1311 (swaptimize function)
        /// TODO: Implement full CPython swaptimize algorithm
        /// </summary>
        private void OptimizePatternMatchingSwaps()
        {
            foreach (var block in _cfg.AllBlocks)
            {
                for (int i = 0; i < block.Instructions.Count; i++)
                {
                    var curr = block.Instructions[i];

                    // Pattern: UNPACK_EX followed by consecutive SWAPs/NOPs
                    // In pattern matching, UNPACK_EX already places values in correct order
                    // CPython: Python/ceval.c lines 1950-2040 (unpack_iterable)
                    if (curr.OpCode == ByteCodeOp.UNPACK_EX)
                    {
                        // Remove all consecutive SWAPs and NOPs after UNPACK_EX
                        int j = i + 1;
                        while (j < block.Instructions.Count)
                        {
                            var inst = block.Instructions[j];
                            if (inst.OpCode == ByteCodeOp.SWAP || inst.OpCode == ByteCodeOp.NOP)
                            {
                                // Convert to NOP
                                block.Instructions[j] = new ByteCodeInstruction(
                                    ByteCodeOp.NOP,
                                    0,
                                    inst.LineNumber,
                                    inst.ColumnOffset,
                                    inst.FileName,
                                    inst.TargetBlock,
                                    inst.ExceptBlock
                                );
                                j++;
                            }
                            else
                            {
                                // Stop at first non-SWAP/non-NOP instruction
                                break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Remove blocks that are never reached
        /// </summary>
        private void RemoveUnreachableBlocks()
        {
            var reachable = new HashSet<BasicBlock>();
            var queue = new Queue<BasicBlock>();

            // Start from entry block
            queue.Enqueue(_cfg.EntryBlock);
            reachable.Add(_cfg.EntryBlock);

            // Exception handler blocks must ALWAYS be reachable
            // even if they're not reached through normal control flow
            foreach (var block in _cfg.AllBlocks)
            {
                if (block.IsExceptionHandler)
                {
                    reachable.Add(block);
                    queue.Enqueue(block);
                }
            }

            // Build mapping: instruction offset → block
            // Use Block.Offset which is set by PyFlowGraph (instruction sequence index)
            var offsetToBlock = new Dictionary<int, BasicBlock>();
            foreach (var b in _cfg.AllBlocks)
            {
                offsetToBlock[b.Offset] = b;
            }

            // CRITICAL: Mark exception handlers AND protected blocks (ALL blocks, not just reachable)
            // CPython 3.12: Blocks with exception handler references must be kept
            foreach (var block in _cfg.AllBlocks)
            {
                bool hasHandlerInfo = false;
                foreach (var instr in block.Instructions)
                {
                    // CPython 3.12: instr->i_except is a BasicBlock pointer (not offset)
                    if (instr.ExceptBlock != null)
                    {
                        hasHandlerInfo = true;
                        // ExceptBlock is already a BasicBlock reference
                        var handlerBlock = instr.ExceptBlock;
                        if (!reachable.Contains(handlerBlock))
                        {
                            reachable.Add(handlerBlock);
                            queue.Enqueue(handlerBlock);
                        }
                    }
                }

                // CRITICAL: Mark THIS block as reachable if it has handler info (protected region)
                // CPython 3.12: Protected blocks must not be removed even if unreachable via normal flow
                if (hasHandlerInfo && !reachable.Contains(block))
                {
                    reachable.Add(block);
                    queue.Enqueue(block);
                }
            }

            while (queue.Count > 0)
            {
                var block = queue.Dequeue();

                // Mark successors as reachable
                foreach (var successor in block.Successors)
                {
                    if (!reachable.Contains(successor))
                    {
                        reachable.Add(successor);
                        queue.Enqueue(successor);
                    }
                }

                // Mark fallthrough as reachable
                if (block.Next != null && !reachable.Contains(block.Next))
                {
                    reachable.Add(block.Next);
                    queue.Enqueue(block.Next);
                }

                // Mark exception handler as reachable (block-level, for compatibility)
                if (block.ExceptionHandler != null && !reachable.Contains(block.ExceptionHandler))
                {
                    reachable.Add(block.ExceptionHandler);
                    queue.Enqueue(block.ExceptionHandler);
                }
            }

            // Remove unreachable blocks
            // Performance: Eliminated LINQ - replaced Where().ToList() with manual filtering
            var reachableBlocks = new List<BasicBlock>();
            foreach (var block in _cfg.AllBlocks)
            {
                if (reachable.Contains(block))
                {
                    reachableBlocks.Add(block);
                }
            }
            _cfg.AllBlocks = reachableBlocks;
        }

        /// <summary>
        /// Eliminate empty blocks (blocks with no instructions)
        /// CPython 3.12: flowgraph.c eliminate_empty_basic_blocks (lines 914-948)
        /// </summary>
        private void EliminateEmptyBlocks()
        {
            // CRITICAL: Build set of blocks that are exception handler targets
            // These blocks MUST NOT be eliminated even if empty
            // CPython 3.12 requires exception handler blocks to be emitted for exception table
            var exceptionHandlerBlocks = new HashSet<BasicBlock>();
            foreach (var block in _cfg.AllBlocks)
            {
                foreach (var instr in block.Instructions)
                {
                    if (instr.ExceptBlock != null)
                    {
                        exceptionHandlerBlocks.Add(instr.ExceptBlock);
                    }
                }
            }

            // Step 1: Update fallthrough chain (b_next) to skip empty blocks
            // EXCEPT for exception handler blocks - they must be preserved
            for (var b = _cfg.EntryBlock; b != null; b = b.Next)
            {
                var next = b.Next;
                while (next != null && next.Instructions.Count == 0 && !exceptionHandlerBlocks.Contains(next))
                {
                    next = next.Next;  // Skip empty blocks (but not exception handlers)
                }
                b.Next = next;
            }

            // Step 2: If entry block is empty, skip to next non-empty block
            // EXCEPT if it's an exception handler - preserve it
            while (_cfg.EntryBlock != null && _cfg.EntryBlock.Instructions.Count == 0 && !exceptionHandlerBlocks.Contains(_cfg.EntryBlock))
            {
                _cfg.EntryBlock = _cfg.EntryBlock.Next;
            }

            // Step 3: Update all jump targets from empty blocks to non-empty blocks
            // Build offset-to-block mapping
            var offsetToBlock = new Dictionary<int, BasicBlock>();
            foreach (var block in _cfg.AllBlocks)
            {
                offsetToBlock[block.Offset] = block;
            }

            for (var b = _cfg.EntryBlock; b != null; b = b.Next)
            {
                if (b.Instructions.Count == 0)
                {
                    // Should not happen after steps 1-2, but defensive check
                    continue;
                }

                for (int i = 0; i < b.Instructions.Count; i++)
                {
                    var instr = b.Instructions[i];

                    // Check if this is a jump instruction
                    if (IsJumpOpcode(instr.OpCode))
                    {
                        // Find the target block
                        if (!offsetToBlock.TryGetValue(instr.Argument, out var target))
                        {
                            continue;  // Invalid target, skip
                        }

                        // Skip past any empty blocks (except exception handlers)
                        var originalTarget = target;
                        while (target != null && target.Instructions.Count == 0 && !exceptionHandlerBlocks.Contains(target))
                        {
                            target = target.Next;
                        }

                        // Update jump target if it changed
                        if (target != null && target != originalTarget)
                        {
                            // Update the instruction to point to the new target
                            b.Instructions[i] = new ByteCodeInstruction(
                                instr.OpCode,
                                target.Offset,  // Use target's offset
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
        /// Check if opcode is a jump instruction
        /// </summary>
        private bool IsJumpOpcode(ByteCodeOp opcode)
        {
            return opcode == ByteCodeOp.JUMP_FORWARD ||
                   opcode == ByteCodeOp.JUMP_BACKWARD ||
                   opcode == ByteCodeOp.POP_JUMP_IF_TRUE ||
                   opcode == ByteCodeOp.POP_JUMP_IF_FALSE ||
                   opcode == ByteCodeOp.POP_JUMP_IF_NONE ||
                   opcode == ByteCodeOp.POP_JUMP_IF_NOT_NONE ||
                   opcode == ByteCodeOp.FOR_ITER;
        }

        /// <summary>
        /// Check if opcode is an unconditional jump
        /// </summary>
        private bool IsUnconditionalJump(ByteCodeOp opcode)
        {
            return opcode == ByteCodeOp.JUMP_FORWARD ||
                   opcode == ByteCodeOp.JUMP_BACKWARD;
        }

        /// <summary>
        /// Merge blocks that always fall through to each other
        /// CPython 3.12: No explicit merge function, handled by eliminate_empty_blocks + remove_redundant_jumps
        /// </summary>
        private void MergeFallthroughBlocks()
        {
            // NOTE: CPython 3.12 does not have a dedicated merge_fallthrough_blocks() function.
            // Instead, it achieves the same effect through:
            // 1. eliminate_empty_basic_blocks() - removes empty blocks
            // 2. remove_redundant_jumps() - removes jumps to next block
            // 3. inline_small_exit_blocks() - inlines small exit blocks (SharpPy: not implemented yet)
            //
            // This function is kept as a placeholder for future optimization if needed.
        }

        /// <summary>
        /// Remove redundant jumps (jump to next block)
        /// CPython 3.12: flowgraph.c remove_redundant_jumps (lines 1058-1081)
        /// </summary>
        private void RemoveRedundantJumps()
        {
            // Build offset-to-block mapping
            var offsetToBlock = new Dictionary<int, BasicBlock>();
            foreach (var block in _cfg.AllBlocks)
            {
                offsetToBlock[block.Offset] = block;
            }

            for (var b = _cfg.EntryBlock; b != null; b = b.Next)
            {
                if (b.Instructions.Count == 0)
                {
                    continue;  // Should not happen after EliminateEmptyBlocks, but defensive
                }

                var last = b.Instructions[b.Instructions.Count - 1];

                // Check if last instruction is an unconditional jump
                if (IsUnconditionalJump(last.OpCode))
                {
                    // Find the target block
                    if (!offsetToBlock.TryGetValue(last.Argument, out var target))
                    {
                        continue;  // Invalid target, skip
                    }

                    // If jump target is the next block (fallthrough), remove the jump
                    if (target == b.Next)
                    {
                        // Replace jump with NOP
                        b.Instructions[b.Instructions.Count - 1] = new ByteCodeInstruction(
                            ByteCodeOp.NOP,
                            0,
                            last.LineNumber,
                            last.ColumnOffset,
                            last.FileName,
                            last.TargetBlock,
                            last.ExceptBlock
                        );
                    }
                }
            }
        }

        // NOTE: OptimizeWithinBlocks() removed in CPython 3.12 compatibility update
        // CPython 3.12 does NOT perform peephole optimizations at CFG level
        // All peephole optimizations (constant folding, dead code elimination, etc.)
        // are performed at AST level in Python/ast_opt.c (SharpPy: PyASTOptimizer.cs)

        /// <summary>
        /// CPython 3.12: flowgraph.c:1963-2039 push_cold_blocks_to_end()
        /// Moves "cold" blocks (exception handlers, rarely executed code) to the end.
        /// This ensures SEND's target (END_SEND) is not blocked by CLEANUP_THROW.
        /// </summary>
        private void PushColdBlocksToEnd()
        {
            if (_cfg.EntryBlock == null || _cfg.AllBlocks.Count <= 1)
            {
                return;
            }

            // CPython 3.12: flowgraph.c:1913-1938 mark_cold()
            // Step 1: Mark all blocks reachable from entry via normal control flow as "warm"
            var warmBlocks = new HashSet<BasicBlock>();
            var queue = new Queue<BasicBlock>();

            queue.Enqueue(_cfg.EntryBlock);
            warmBlocks.Add(_cfg.EntryBlock);

            while (queue.Count > 0)
            {
                var block = queue.Dequeue();

                // Follow normal control flow (successors and fallthrough)
                foreach (var successor in block.Successors)
                {
                    if (!warmBlocks.Contains(successor))
                    {
                        warmBlocks.Add(successor);
                        queue.Enqueue(successor);
                    }
                }

                // CPython 3.12: flowgraph.c:1894-1908 (mark_warm)
                // Also follow jump targets via instruction TargetBlock references
                // IMPORTANT: Only follow normal control flow jumps, NOT exception handlers
                foreach (var instr in block.Instructions)
                {
                    if (instr.TargetBlock != null && !warmBlocks.Contains(instr.TargetBlock))
                    {
                        // Check if this is a normal jump (not exception handler setup)
                        // Exception handlers are set up by SETUP_FINALLY/SETUP_CLEANUP which are pseudo-ops
                        // Normal jumps include: JUMP_*, FOR_ITER, SEND, etc.
                        bool isNormalJump = instr.OpCode == ByteCodeOp.JUMP_FORWARD ||
                                           instr.OpCode == ByteCodeOp.JUMP_BACKWARD ||
                                           instr.OpCode == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT ||
                                           instr.OpCode == ByteCodeOp.JUMP_NO_INTERRUPT ||
                                           instr.OpCode == ByteCodeOp.POP_JUMP_IF_TRUE ||
                                           instr.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE ||
                                           instr.OpCode == ByteCodeOp.POP_JUMP_IF_NONE ||
                                           instr.OpCode == ByteCodeOp.POP_JUMP_IF_NOT_NONE ||
                                           instr.OpCode == ByteCodeOp.FOR_ITER ||
                                           instr.OpCode == ByteCodeOp.SEND;
                        if (isNormalJump)
                        {
                            warmBlocks.Add(instr.TargetBlock);
                            queue.Enqueue(instr.TargetBlock);
                        }
                    }
                }

                // Follow fallthrough (b_next) only if block doesn't end with unconditional jump/return
                if (block.Next != null && !warmBlocks.Contains(block.Next))
                {
                    bool hasFallthrough = true;
                    if (block.Instructions.Count > 0)
                    {
                        var lastInstr = block.Instructions[block.Instructions.Count - 1];
                        // No fallthrough if ends with unconditional jump, return, raise, etc.
                        // CPython 3.12: Python/flowgraph.c:1875-1885 (BB_HAS_FALLTHROUGH)
                        if (lastInstr.OpCode == ByteCodeOp.JUMP_FORWARD ||
                            lastInstr.OpCode == ByteCodeOp.JUMP_BACKWARD ||
                            lastInstr.OpCode == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT ||
                            lastInstr.OpCode == ByteCodeOp.JUMP_NO_INTERRUPT ||
                            lastInstr.OpCode == ByteCodeOp.RETURN_VALUE ||
                            lastInstr.OpCode == ByteCodeOp.RETURN_CONST ||
                            lastInstr.OpCode == ByteCodeOp.RETURN_GENERATOR ||
                            lastInstr.OpCode == ByteCodeOp.RAISE_VARARGS ||
                            lastInstr.OpCode == ByteCodeOp.RERAISE ||
                            lastInstr.OpCode == ByteCodeOp.YIELD_VALUE)  // YIELD_VALUE suspends, no fallthrough to next block
                        {
                            hasFallthrough = false;
                        }
                    }
                    if (hasFallthrough)
                    {
                        warmBlocks.Add(block.Next);
                        queue.Enqueue(block.Next);
                    }
                }
            }

            // Step 2: Identify cold blocks (not warm = exception handlers, etc.)
            // CPython 3.12: flowgraph.c:1937 - b->b_cold = 1 for non-warm blocks
            var coldBlocks = new List<BasicBlock>();
            var warmBlocksList = new List<BasicBlock>();

            foreach (var block in _cfg.AllBlocks)
            {
                if (warmBlocks.Contains(block))
                {
                    warmBlocksList.Add(block);
                }
                else
                {
                    coldBlocks.Add(block);
                }
            }

            // If no cold blocks, nothing to do
            if (coldBlocks.Count == 0)
            {
                return;
            }

            // Step 3: CPython 3.12: flowgraph.c:1976-1992
            // If cold block has fallthrough to warm block, add explicit jump
            foreach (var coldBlock in coldBlocks)
            {
                if (coldBlock.Instructions.Count == 0)
                {
                    continue;
                }

                var lastInstr = coldBlock.Instructions[coldBlock.Instructions.Count - 1];
                bool hasFallthrough = !(lastInstr.OpCode == ByteCodeOp.JUMP_FORWARD ||
                                        lastInstr.OpCode == ByteCodeOp.JUMP_BACKWARD ||
                                        lastInstr.OpCode == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT ||
                                        lastInstr.OpCode == ByteCodeOp.JUMP_NO_INTERRUPT ||
                                        lastInstr.OpCode == ByteCodeOp.RETURN_VALUE ||
                                        lastInstr.OpCode == ByteCodeOp.RETURN_CONST ||
                                        lastInstr.OpCode == ByteCodeOp.RETURN_GENERATOR ||
                                        lastInstr.OpCode == ByteCodeOp.RAISE_VARARGS ||
                                        lastInstr.OpCode == ByteCodeOp.RERAISE ||
                                        lastInstr.OpCode == ByteCodeOp.YIELD_VALUE);

                if (hasFallthrough && coldBlock.Next != null && warmBlocks.Contains(coldBlock.Next))
                {
                    // CPython 3.12: flowgraph.c:1984 - basicblock_addop(explicit_jump, JUMP, ...)
                    // Add explicit JUMP to the original next (warm) block
                    // Note: We use JUMP_BACKWARD since cold blocks are moved to end
                    // The assembler will compute the correct backward offset
                    var targetWarmBlock = coldBlock.Next;
                    var jumpInstr = new ByteCodeInstruction(
                        ByteCodeOp.JUMP_BACKWARD,
                        targetWarmBlock.Offset,
                        lastInstr.LineNumber,
                        lastInstr.ColumnOffset,
                        lastInstr.FileName,
                        targetWarmBlock,  // TargetBlock
                        null  // ExceptBlock
                    );
                    coldBlock.Instructions.Add(jumpInstr);
                }
            }

            // Step 4: CPython 3.12: flowgraph.c:1995-2033
            // Reorder AllBlocks: warm blocks first, then cold blocks
            _cfg.AllBlocks.Clear();
            _cfg.AllBlocks.AddRange(warmBlocksList);
            _cfg.AllBlocks.AddRange(coldBlocks);

            // Step 5: Rebuild the Next chain based on new order
            for (int i = 0; i < _cfg.AllBlocks.Count - 1; i++)
            {
                _cfg.AllBlocks[i].Next = _cfg.AllBlocks[i + 1];
            }
            if (_cfg.AllBlocks.Count > 0)
            {
                _cfg.AllBlocks[_cfg.AllBlocks.Count - 1].Next = null;
            }

            // Update entry block (should still be first warm block)
            if (_cfg.AllBlocks.Count > 0)
            {
                _cfg.EntryBlock = _cfg.AllBlocks[0];
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"[CFG] PushColdBlocksToEnd: {warmBlocksList.Count} warm, {coldBlocks.Count} cold blocks");
            foreach (var wb in warmBlocksList)
            {
                var firstOp = wb.Instructions.Count > 0 ? wb.Instructions[0].OpCode.ToString() : "empty";
                Console.WriteLine($"  Warm block {wb.BlockId}: starts with {firstOp}, Successors.Count={wb.Successors.Count}");
            }
            foreach (var cb in coldBlocks)
            {
                if (cb.Instructions.Count > 0)
                {
                    Console.WriteLine($"  Cold block {cb.BlockId}: starts with {cb.Instructions[0].OpCode}");
                }
            }
#endif
        }

    }
}
