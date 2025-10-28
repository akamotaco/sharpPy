using System;
using System.Collections.Generic;
using System.Linq;

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

            RemoveUnreachableBlocks();       // Mark and remove unreachable blocks
            EliminateEmptyBlocks();          // Remove blocks with no instructions
            RemoveRedundantJumps();          // Remove jumps to next block (fallthrough)

            // NOTE: CPython 3.12 also does:
            // - inline_small_exit_blocks() - not implemented yet
            // - remove_redundant_nops_and_pairs() - removed in CPython 3.11+
            // - mark_reachable() + delete unreachable - covered by RemoveUnreachableBlocks()
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
            _cfg.AllBlocks = _cfg.AllBlocks.Where(b => reachable.Contains(b)).ToList();
        }

        /// <summary>
        /// Eliminate empty blocks (blocks with no instructions)
        /// CPython 3.12: flowgraph.c eliminate_empty_basic_blocks (lines 914-948)
        /// </summary>
        private void EliminateEmptyBlocks()
        {
            // Step 1: Update fallthrough chain (b_next) to skip empty blocks
            for (var b = _cfg.EntryBlock; b != null; b = b.Next)
            {
                var next = b.Next;
                while (next != null && next.Instructions.Count == 0)
                {
                    next = next.Next;  // Skip empty blocks
                }
                b.Next = next;
            }

            // Step 2: If entry block is empty, skip to next non-empty block
            while (_cfg.EntryBlock != null && _cfg.EntryBlock.Instructions.Count == 0)
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

                        // Skip past any empty blocks
                        var originalTarget = target;
                        while (target != null && target.Instructions.Count == 0)
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

    }
}
