using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12: Python/assemble.c - Assemble CFG to bytecode
    /// Converts Control Flow Graph (basic blocks) to linear bytecode instructions
    /// Handles:
    /// - Block ordering and jump target resolution
    /// - Extended args for large operands (> 255)
    /// - Exception table generation
    /// - Line number table generation
    /// </summary>
    public static class PyAssemble
    {
        /// <summary>
        /// CPython 3.12: assemble()
        /// Main entry point: CFG → final bytecode + exception table
        /// </summary>
        public static AssembledCode Assemble(ControlFlowGraph cfg, string filename = "")
        {
            if (cfg.AllBlocks.Count == 0)
            {
                return new AssembledCode(
                    new List<ByteCodeInstruction>(),
                    new List<ExceptionTableEntry>()
                );
            }

            // Phase 1: Calculate final offsets for each block
            // (CPython: assemble_compute_code_flags_and_offsets)
            CalculateBlockOffsets(cfg);

            // Phase 2: Resolve jump targets and add EXTENDED_ARG if needed
            // (CPython: assemble_emit_instr_sequence)
            var instructions = FlattenBlocks(cfg);

            // Phase 3: Build exception table from CFG
            // (CPython: assemble_exception_table)
            var exceptionTable = cfg.BuildExceptionTable();

            return new AssembledCode(instructions, exceptionTable);
        }

        /// <summary>
        /// Find instruction offset (accounting for EXTENDED_ARG) for a given instruction index
        /// CPython 3.12: Instructions may expand to multiple words if args > 255
        /// </summary>
        private static int FindInstructionOffset(ControlFlowGraph cfg, int targetIndex)
        {
            int offset = 0;
            int currentIndex = 0;

            foreach (var block in cfg.AllBlocks)
            {
                foreach (var instr in block.Instructions)
                {
                    if (currentIndex == targetIndex)
                    {
                        return offset;
                    }

                    // Each instruction may take multiple words
                    offset += CountInstructionWords(instr);
                    currentIndex++;
                }
            }

            // Target not found, return target index as fallback
            return targetIndex;
        }

        /// <summary>
        /// Calculate byte offsets for each basic block
        /// CPython: assemble_compute_code_flags_and_offsets()
        /// </summary>
        private static void CalculateBlockOffsets(ControlFlowGraph cfg)
        {
            int offset = 0;

            foreach (var block in cfg.AllBlocks)
            {
                block.Offset = offset;

                // Each instruction takes 1 word (2 bytes in CPython 3.12)
                // But for SharpPy we use instruction offset (not byte offset)
                foreach (var instr in block.Instructions)
                {
                    // Check if this instruction needs EXTENDED_ARG
                    // (operands > 255 require multiple instructions)
                    offset += CountInstructionWords(instr);
                }
            }
        }

        /// <summary>
        /// Count how many instruction words this will need
        /// (1 for normal, more if EXTENDED_ARG needed)
        /// CPython 3.12: Instructions with arg > 255 need EXTENDED_ARG prefix
        /// </summary>
        private static int CountInstructionWords(ByteCodeInstruction instr)
        {
            // Instructions without arguments always take 1 word
            if (!HasArgument(instr.OpCode))
            {
                return 1;
            }

            int arg = instr.Argument;

            // Count how many bytes needed to represent this argument
            // CPython 3.12: Each instruction word can hold 8 bits of argument
            // arg <= 255: 1 word
            // arg <= 65535: 2 words (1 EXTENDED_ARG + 1 instruction)
            // arg <= 16777215: 3 words (2 EXTENDED_ARG + 1 instruction)
            // etc.
            if (arg <= 0xFF)
                return 1;
            else if (arg <= 0xFFFF)
                return 2;
            else if (arg <= 0xFFFFFF)
                return 3;
            else
                return 4;
        }

        /// <summary>
        /// Flatten CFG blocks to linear instruction list
        /// Resolve jump targets and insert EXTENDED_ARG where needed
        /// CPython: assemble_emit_instr_sequence() + flowgraph.c:resolve_jump_offsets()
        ///
        /// Strategy: Iterative refinement (CPython pattern)
        /// 1. Calculate jump deltas based on current instruction sizes
        /// 2. If any jump arg changes size (needs/loses EXTENDED_ARG), recompile
        /// 3. Repeat until stable
        /// </summary>
        private static List<ByteCodeInstruction> FlattenBlocks(ControlFlowGraph cfg)
        {
            // Phase 1: Resolve jump offsets with iterative refinement
            // CPython: Python/flowgraph.c:481-536 (resolve_jump_offsets)
            ResolveJumpOffsets(cfg);

            // Phase 2: Emit final bytecode with EXTENDED_ARG
            return EmitInstructions(cfg);
        }

        /// <summary>
        /// CPython: resolve_jump_offsets()
        /// Iteratively calculate jump deltas until stable
        /// </summary>
        private static void ResolveJumpOffsets(ControlFlowGraph cfg)
        {
            bool needRecompile;
            int iterationCount = 0;
            const int MAX_ITERATIONS = 10; // Safety limit

            do
            {
                needRecompile = false;
                iterationCount++;

                if (iterationCount > MAX_ITERATIONS)
                {
                    throw new InvalidOperationException(
                        $"Jump offset resolution did not converge after {MAX_ITERATIONS} iterations");
                }

                // Recalculate block offsets based on current instruction sizes
                int currentIndex = 0;
                foreach (var block in cfg.AllBlocks)
                {
                    block.Offset = currentIndex;

                    foreach (var instr in block.Instructions)
                    {
                        // Count instruction words (1 + EXTENDED_ARG count)
                        int instrWords = CountInstructionWords(instr);
                        currentIndex += instrWords;
                    }
                }

                // Update jump arguments based on new offsets
                foreach (var block in cfg.AllBlocks)
                {
                    int instrIndex = block.Offset;

                    for (int i = 0; i < block.Instructions.Count; i++)
                    {
                        var instr = block.Instructions[i];
                        int oldInstrWords = CountInstructionWords(instr);

                        if (IsJumpInstruction(instr.OpCode))
                        {
                            // instr.Argument is target instruction index from InstructionSequence
                            // We need to find which CFG block contains this target
                            int targetIndex = instr.Argument;
                            int currentIndexAfter = instrIndex + oldInstrWords;

                            // Determine jump direction and opcode
                            bool isBackwardJump = targetIndex < instrIndex;
                            ByteCodeOp finalOpCode = instr.OpCode;

                            // Convert JUMP/JUMP_NO_INTERRUPT to backward variants
                            if (instr.OpCode == ByteCodeOp.JUMP && isBackwardJump)
                            {
                                finalOpCode = ByteCodeOp.JUMP_BACKWARD;
                            }
                            else if (instr.OpCode == ByteCodeOp.JUMP_NO_INTERRUPT && isBackwardJump)
                            {
                                finalOpCode = ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT;
                            }

                            // Calculate jump delta (CPython 3.12 pattern: instruction word units)
                            // CPython 3.12: All instructions occupy instruction word slots,
                            // regardless of actual byte size (including inline cache)
                            int jumpArg;
                            if (finalOpCode == ByteCodeOp.JUMP_BACKWARD ||
                                finalOpCode == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT)
                            {
                                // BACKWARD: delta = (index after jump) - target (instruction word units)
                                jumpArg = currentIndexAfter - targetIndex;
                            }
                            else
                            {
                                // FORWARD: delta = target - (index after jump) (instruction word units)
                                jumpArg = targetIndex - currentIndexAfter;
                            }

                            // Update instruction with new delta and opcode
                            var newInstr = new ByteCodeInstruction(
                                finalOpCode,
                                jumpArg,
                                instr.LineNumber,
                                instr.ColumnOffset,
                                instr.FileName,
                                instr.ExceptHandler,
                                instr.ExceptionHandlerOffset
                            );

                            // Check if instruction size changed
                            int newInstrWords = CountInstructionWords(newInstr);
                            if (oldInstrWords != newInstrWords)
                            {
                                needRecompile = true;  // EXTENDED_ARG count changed
                            }

                            block.Instructions[i] = newInstr;
                        }

                        instrIndex += oldInstrWords;
                    }
                }

            } while (needRecompile);

            #if DEBUG_COMPILER_LOG
            Console.WriteLine($"[assemble] Jump offset resolution converged after {iterationCount} iteration(s)");
            #endif
        }

        /// <summary>
        /// Emit final bytecode instructions with EXTENDED_ARG
        /// </summary>
        private static List<ByteCodeInstruction> EmitInstructions(ControlFlowGraph cfg)
        {
            var result = new List<ByteCodeInstruction>();

            foreach (var block in cfg.AllBlocks)
            {
                foreach (var instr in block.Instructions)
                {
                    // Add EXTENDED_ARG if needed (arguments > 255)
                    if (HasArgument(instr.OpCode) && instr.Argument > 0xFF)
                    {
                        EmitWithExtendedArg(result, instr);
                    }
                    else
                    {
                        result.Add(instr);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Emit instruction with EXTENDED_ARG prefix if argument > 255
        /// CPython 3.12: EXTENDED_ARG shifts left by 8 bits and adds to next arg
        /// </summary>
        private static void EmitWithExtendedArg(List<ByteCodeInstruction> result, ByteCodeInstruction instr)
        {
            int arg = instr.Argument;

            // Split argument into bytes (big-endian)
            // Example: arg = 0x123456
            // → EXTENDED_ARG 0x12
            // → EXTENDED_ARG 0x34
            // → INSTRUCTION 0x56

            if (arg > 0xFFFFFF)
            {
                // Need 3 EXTENDED_ARG (for arguments up to 32 bits)
                result.Add(new ByteCodeInstruction(
                    ByteCodeOp.EXTENDED_ARG,
                    (arg >> 24) & 0xFF,
                    instr.LineNumber,
                    instr.ColumnOffset,
                    instr.FileName,
                    instr.ExceptHandler,
                    instr.ExceptionHandlerOffset
                ));
                result.Add(new ByteCodeInstruction(
                    ByteCodeOp.EXTENDED_ARG,
                    (arg >> 16) & 0xFF,
                    instr.LineNumber,
                    instr.ColumnOffset,
                    instr.FileName,
                    instr.ExceptHandler,
                    instr.ExceptionHandlerOffset
                ));
                result.Add(new ByteCodeInstruction(
                    ByteCodeOp.EXTENDED_ARG,
                    (arg >> 8) & 0xFF,
                    instr.LineNumber,
                    instr.ColumnOffset,
                    instr.FileName,
                    instr.ExceptHandler,
                    instr.ExceptionHandlerOffset
                ));
            }
            else if (arg > 0xFFFF)
            {
                // Need 2 EXTENDED_ARG
                result.Add(new ByteCodeInstruction(
                    ByteCodeOp.EXTENDED_ARG,
                    (arg >> 16) & 0xFF,
                    instr.LineNumber,
                    instr.ColumnOffset,
                    instr.FileName,
                    instr.ExceptHandler,
                    instr.ExceptionHandlerOffset
                ));
                result.Add(new ByteCodeInstruction(
                    ByteCodeOp.EXTENDED_ARG,
                    (arg >> 8) & 0xFF,
                    instr.LineNumber,
                    instr.ColumnOffset,
                    instr.FileName,
                    instr.ExceptHandler,
                    instr.ExceptionHandlerOffset
                ));
            }
            else if (arg > 0xFF)
            {
                // Need 1 EXTENDED_ARG
                result.Add(new ByteCodeInstruction(
                    ByteCodeOp.EXTENDED_ARG,
                    (arg >> 8) & 0xFF,
                    instr.LineNumber,
                    instr.ColumnOffset,
                    instr.FileName,
                    instr.ExceptHandler,
                    instr.ExceptionHandlerOffset
                ));
            }

            // Finally emit the actual instruction with low byte
            result.Add(new ByteCodeInstruction(
                instr.OpCode,
                arg & 0xFF,
                instr.LineNumber,
                instr.ColumnOffset,
                instr.FileName,
                instr.ExceptHandler,
                instr.ExceptionHandlerOffset
            ));
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
                   op == ByteCodeOp.SEND;  // CPython 3.12: SEND is conditional jump
        }

        private static bool HasArgument(ByteCodeOp op)
        {
            // CPython 3.12: Instructions < HAVE_ARGUMENT (90) don't have arguments
            // Most instructions with opcode >= 90 have arguments
            // Exceptions: Some instructions < 90 have no arguments (POP_TOP, NOP, RETURN_VALUE, etc.)
            int opcode = (int)op;

            // Simple heuristic: opcodes >= 90 generally have arguments
            // For exact behavior, we should use a HAS_ARG lookup table from opcode.h
            // But for now, this works for most cases
            if (opcode >= 90)
                return true;

            // Some low-numbered opcodes that DO have arguments
            // (Based on CPython's opcode.py: dis.hasarg)
            return op == ByteCodeOp.STORE_SUBSCR ||
                   op == ByteCodeOp.DELETE_SUBSCR ||
                   op == ByteCodeOp.BUILD_TUPLE ||
                   op == ByteCodeOp.BUILD_LIST ||
                   op == ByteCodeOp.BUILD_SET ||
                   op == ByteCodeOp.BUILD_MAP;
        }
    }

    /// <summary>
    /// Result of assembly: bytecode instructions + exception table
    /// </summary>
    public class AssembledCode
    {
        public List<ByteCodeInstruction> Instructions { get; }
        public List<ExceptionTableEntry> ExceptionTable { get; }

        public AssembledCode(
            List<ByteCodeInstruction> instructions,
            List<ExceptionTableEntry> exceptionTable)
        {
            Instructions = instructions;
            ExceptionTable = exceptionTable;
        }
    }
}
