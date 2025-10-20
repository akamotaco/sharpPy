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
        /// </summary>
        public void Optimize()
        {
            // CPython optimization order
            RemoveUnreachableBlocks();
            // EliminateEmptyBlocks();  // TODO: Implement
            // MergeFallthroughBlocks();  // TODO: Implement

            // Within-block optimizations (peephole patterns)
            OptimizeWithinBlocks();
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

                // Mark exception handler as reachable
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
        /// </summary>
        private void EliminateEmptyBlocks()
        {
            // TODO: Implement empty block elimination
            // This requires updating predecessors to point to the block's successor
        }

        /// <summary>
        /// Merge blocks that always fall through to each other
        /// </summary>
        private void MergeFallthroughBlocks()
        {
            // TODO: Implement block merging
            // Merge block A and B if A's only successor is B and B's only predecessor is A
        }

        /// <summary>
        /// Apply peephole optimizations within each block
        /// Reuses existing ByteCodeOptimizer patterns
        /// </summary>
        private void OptimizeWithinBlocks()
        {
            foreach (var block in _cfg.AllBlocks)
            {
                if (block.Instructions.Count == 0)
                {
                    continue;
                }

                // Apply optimizations to this block's instructions
                OptimizeInstructionSequence(block.Instructions);
            }
        }

        /// <summary>
        /// Optimize a sequence of instructions (peephole patterns)
        /// Based on ByteCodeOptimizer's existing logic
        /// </summary>
        private void OptimizeInstructionSequence(List<ByteCodeInstruction> instructions)
        {
            // Pattern: LOAD_CONST, POP_TOP → (remove both)
            for (int i = 0; i < instructions.Count - 1; i++)
            {
                var inst1 = instructions[i];
                var inst2 = instructions[i + 1];

                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.POP_TOP)
                {
                    // Remove both instructions
                    instructions.RemoveAt(i + 1);
                    instructions.RemoveAt(i);
                    i--; // Recheck from this position
                    continue;
                }
            }

            // Pattern: LOAD_CONST <True>, POP_JUMP_IF_TRUE → JUMP_FORWARD
            // Pattern: LOAD_CONST <False>, POP_JUMP_IF_FALSE → JUMP_FORWARD
            for (int i = 0; i < instructions.Count - 1; i++)
            {
                var inst1 = instructions[i];
                var inst2 = instructions[i + 1];

                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst1.Argument >= 0 && inst1.Argument < _constants.Count)
                {
                    var constVal = _constants[inst1.Argument];

                    // True + POP_JUMP_IF_TRUE → unconditional jump
                    if (constVal is PyBool boolVal && boolVal.Value &&
                        inst2.OpCode == ByteCodeOp.POP_JUMP_IF_TRUE)
                    {
                        instructions[i] = new ByteCodeInstruction(
                            ByteCodeOp.JUMP_FORWARD,
                            inst2.Argument,
                            inst2.LineNumber,
                            inst2.ColumnOffset,
                            inst2.FileName,
                            inst2.ExceptHandler
                        );
                        instructions.RemoveAt(i + 1);
                        continue;
                    }

                    // False + POP_JUMP_IF_FALSE → unconditional jump
                    if (constVal is PyBool boolVal2 && !boolVal2.Value &&
                        inst2.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE)
                    {
                        instructions[i] = new ByteCodeInstruction(
                            ByteCodeOp.JUMP_FORWARD,
                            inst2.Argument,
                            inst2.LineNumber,
                            inst2.ColumnOffset,
                            inst2.FileName,
                            inst2.ExceptHandler
                        );
                        instructions.RemoveAt(i + 1);
                        continue;
                    }
                }
            }

            // More peephole patterns can be added here...
            // NOTE: Constant folding is now done at compile-time in compile.cs, not here
        }

    }
}
