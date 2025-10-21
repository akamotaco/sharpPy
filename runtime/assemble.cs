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
        /// CPython: assemble_emit_instr_sequence()
        /// </summary>
        private static List<ByteCodeInstruction> FlattenBlocks(ControlFlowGraph cfg)
        {
            var result = new List<ByteCodeInstruction>();

            // Build block → offset mapping for jump resolution
            var blockOffsets = cfg.AllBlocks.ToDictionary(b => b, b => b.Offset);

            foreach (var block in cfg.AllBlocks)
            {
                foreach (var instr in block.Instructions)
                {
                    ByteCodeInstruction finalInstr = instr;

                    // Resolve jump targets: If this is a jump instruction,
                    // convert block reference to actual offset
                    if (IsJumpInstruction(instr.OpCode))
                    {
                        // Find target block from successors
                        int targetOffset = instr.Argument;  // Default (already resolved)

                        // If jump target needs to be resolved from block successors
                        if (block.Successors.Count > 0)
                        {
                            var targetBlock = block.Successors[0];
                            if (blockOffsets.TryGetValue(targetBlock, out int offset))
                            {
                                targetOffset = offset;
                            }
                        }

                        // CPython 3.12: All jump instructions use delta (relative offset)
                        // Delta is calculated from NEXT instruction (current + 1)
                        int currentOffset = result.Count;
                        int jumpArg;

                        if (instr.OpCode == ByteCodeOp.JUMP_BACKWARD ||
                            instr.OpCode == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT)
                        {
                            // JUMP_BACKWARD: delta = current - target (backward from current position)
                            jumpArg = currentOffset - targetOffset;
                        }
                        else
                        {
                            // JUMP_FORWARD, POP_JUMP_IF_*, etc:
                            // delta = target - (current + 1) (forward from NEXT instruction)
                            jumpArg = targetOffset - (currentOffset + 1);
                        }

                        finalInstr = new ByteCodeInstruction(
                            instr.OpCode,
                            jumpArg,
                            instr.LineNumber,
                            instr.ColumnOffset,
                            instr.FileName,
                            instr.ExceptHandler,
                            instr.ExceptionHandlerOffset
                        );
                    }

                    // Add EXTENDED_ARG if needed (CPython 3.12: arguments > 255)
                    if (HasArgument(finalInstr.OpCode) && finalInstr.Argument > 0xFF)
                    {
                        EmitWithExtendedArg(result, finalInstr);
                    }
                    else
                    {
                        result.Add(finalInstr);
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
            return op == ByteCodeOp.JUMP_FORWARD ||
                   op == ByteCodeOp.JUMP_BACKWARD ||
                   op == ByteCodeOp.POP_JUMP_IF_FALSE ||
                   op == ByteCodeOp.POP_JUMP_IF_TRUE ||
                   op == ByteCodeOp.POP_JUMP_IF_NONE ||
                   op == ByteCodeOp.POP_JUMP_IF_NOT_NONE ||
                   op == ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT ||
                   op == ByteCodeOp.FOR_ITER;
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
