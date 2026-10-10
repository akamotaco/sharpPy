using System;
using System.Collections.Generic;

namespace SharpPy
{
    // Analyze before SETUP_* pseudo-instructions are removed. Handler depth is
    // the value stack at setup, not the number of active exception handlers.
    internal static class ExceptionStackDepth
    {
        internal static Dictionary<int, int> Analyze(InstructionSequence sequence)
        {
            var instructions = sequence.Instructions;
            var handlers = new Dictionary<int, int>();
            bool hasHandlers = false;
            foreach (var instruction in instructions)
                if (instruction.OpCode is ByteCodeOp.SETUP_FINALLY or ByteCodeOp.SETUP_CLEANUP or ByteCodeOp.SETUP_WITH) { hasHandlers = true; break; }
            if (!hasHandlers) return handlers;
            var depths = new int[instructions.Count];
            Array.Fill(depths, -1);
            var pending = new Queue<int>();
            int active = -1;
            void Visit(int index, int depth)
            {
                if (index == instructions.Count) return;
                if (index < 0 || index > instructions.Count || depth < 0)
                    throw new InvalidOperationException($"Invalid stack depth {depth} at instruction {index}");
                if (depths[index] >= 0)
                {
                    if (depths[index] != depth)
                        throw new InvalidOperationException($"Stack depth mismatch from {active} ({instructions[active].OpCode}, line {instructions[active].LineNumber}) at {index} ({instructions[index].OpCode}): {depths[index]} vs {depth}");
                    return;
                }
                depths[index] = depth;
                pending.Enqueue(index);
            }
            bool generator = false;
            foreach (var instruction in instructions)
                if (instruction.OpCode == ByteCodeOp.RETURN_GENERATOR) { generator = true; break; }
            Visit(0, generator ? 1 : 0);
            while (pending.Count != 0)
            {
                int index = pending.Dequeue();
                active = index;
                var instruction = instructions[index];
                var op = instruction.OpCode;
                int depth = depths[index];
                if (op is ByteCodeOp.SETUP_FINALLY or ByteCodeOp.SETUP_CLEANUP or ByteCodeOp.SETUP_WITH)
                {
                    int preserved = depth - (op == ByteCodeOp.SETUP_WITH ? 1 : 0);
                    handlers[index] = preserved;
                    Visit(sequence.GetLabelTarget(instruction.Target.Value), preserved + 1 + (op == ByteCodeOp.SETUP_FINALLY ? 0 : 1));
                    Visit(index + 1, depth);
                    continue;
                }
                int arg = instruction.Arg ?? 0;
                if (instruction.Target.HasValue)
                    Visit(sequence.GetLabelTarget(instruction.Target.Value), depth + Effect(op, arg, true));
                if (op is ByteCodeOp.JUMP or ByteCodeOp.JUMP_NO_INTERRUPT or ByteCodeOp.JUMP_FORWARD or
                    ByteCodeOp.JUMP_BACKWARD or ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT or
                    ByteCodeOp.RETURN_VALUE or ByteCodeOp.RETURN_CONST or ByteCodeOp.RAISE_VARARGS or ByteCodeOp.RERAISE)
                    continue;
                Visit(index + 1, depth + Effect(op, arg, false));
            }
            return handlers;
        }

        // CPython 3.12 stack effects, with SharpPy's FOR_ITER/END_FOR convention:
        // the exhausted iterator is popped by FOR_ITER, and END_FOR is a marker.
        private static int Effect(ByteCodeOp op, int arg, bool jump) => op switch
        {
            ByteCodeOp.FOR_ITER => jump ? -1 : 1,
            ByteCodeOp.END_FOR => 0,
            ByteCodeOp.LOAD_GLOBAL => 1 + (arg & 1),
            ByteCodeOp.LOAD_ATTR => arg & 1,
            ByteCodeOp.LOAD_SUPER_ATTR => (arg & 1) - 2,
            ByteCodeOp.CALL => -arg - 1,
            ByteCodeOp.CALL_FUNCTION_EX => -2 - (arg & 1),
            ByteCodeOp.BUILD_LIST or ByteCodeOp.BUILD_TUPLE or ByteCodeOp.BUILD_SET or ByteCodeOp.BUILD_STRING => 1 - arg,
            ByteCodeOp.BUILD_MAP => 1 - 2 * arg,
            ByteCodeOp.BUILD_CONST_KEY_MAP => -arg,
            ByteCodeOp.BUILD_SLICE => arg == 3 ? -2 : -1,
            ByteCodeOp.UNPACK_SEQUENCE => arg - 1,
            ByteCodeOp.UNPACK_EX => (arg & 255) + (arg >> 8),
            ByteCodeOp.MAKE_FUNCTION => -System.Numerics.BitOperations.PopCount((uint)(arg & 15)),
            ByteCodeOp.FORMAT_VALUE => (arg & 4) != 0 ? -1 : 0,
            ByteCodeOp.PUSH_NULL or ByteCodeOp.LOAD_CONST or ByteCodeOp.LOAD_NAME or ByteCodeOp.LOAD_FAST or
                ByteCodeOp.LOAD_FAST_CHECK or ByteCodeOp.LOAD_FAST_AND_CLEAR or ByteCodeOp.LOAD_CLOSURE or
                ByteCodeOp.LOAD_DEREF or ByteCodeOp.LOAD_LOCALS or ByteCodeOp.LOAD_BUILD_CLASS or
                ByteCodeOp.LOAD_ASSERTION_ERROR or ByteCodeOp.IMPORT_FROM or ByteCodeOp.COPY or
                ByteCodeOp.PUSH_EXC_INFO or ByteCodeOp.WITH_EXCEPT_START or ByteCodeOp.BEFORE_WITH or
                ByteCodeOp.BEFORE_ASYNC_WITH or ByteCodeOp.GET_ANEXT or ByteCodeOp.GET_LEN or
                ByteCodeOp.MATCH_MAPPING or ByteCodeOp.MATCH_SEQUENCE or ByteCodeOp.MATCH_KEYS => 1,
            ByteCodeOp.POP_TOP or ByteCodeOp.POP_EXCEPT or ByteCodeOp.STORE_NAME or ByteCodeOp.STORE_FAST or
                ByteCodeOp.STORE_GLOBAL or ByteCodeOp.STORE_DEREF or ByteCodeOp.DELETE_ATTR or
                ByteCodeOp.BINARY_OP or ByteCodeOp.BINARY_SUBSCR or ByteCodeOp.COMPARE_OP or ByteCodeOp.IS_OP or
                ByteCodeOp.CONTAINS_OP or ByteCodeOp.IMPORT_NAME or ByteCodeOp.LIST_APPEND or ByteCodeOp.SET_ADD or
                ByteCodeOp.LIST_EXTEND or ByteCodeOp.SET_UPDATE or ByteCodeOp.DICT_UPDATE or ByteCodeOp.DICT_MERGE or
                ByteCodeOp.END_SEND or ByteCodeOp.CLEANUP_THROW or ByteCodeOp.CALL_INTRINSIC_2 or
                ByteCodeOp.POP_JUMP_IF_FALSE or ByteCodeOp.POP_JUMP_IF_TRUE or
                ByteCodeOp.POP_JUMP_IF_NONE or ByteCodeOp.POP_JUMP_IF_NOT_NONE => -1,
            ByteCodeOp.STORE_ATTR or ByteCodeOp.DELETE_SUBSCR or ByteCodeOp.MAP_ADD or
                ByteCodeOp.BINARY_SLICE or ByteCodeOp.MATCH_CLASS or ByteCodeOp.END_ASYNC_FOR => -2,
            ByteCodeOp.STORE_SUBSCR => -3,
            ByteCodeOp.STORE_SLICE => -4,
            ByteCodeOp.CACHE or ByteCodeOp.NOP or ByteCodeOp.RESUME or ByteCodeOp.RETURN_GENERATOR or
                ByteCodeOp.MAKE_CELL or ByteCodeOp.COPY_FREE_VARS or ByteCodeOp.POP_BLOCK or ByteCodeOp.SWAP or
                ByteCodeOp.JUMP or ByteCodeOp.JUMP_NO_INTERRUPT or ByteCodeOp.JUMP_FORWARD or ByteCodeOp.JUMP_BACKWARD or
                ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT or ByteCodeOp.DELETE_NAME or ByteCodeOp.DELETE_GLOBAL or
                ByteCodeOp.DELETE_FAST or ByteCodeOp.DELETE_DEREF or ByteCodeOp.UNARY_POSITIVE or
                ByteCodeOp.UNARY_NEGATIVE or ByteCodeOp.UNARY_NOT or ByteCodeOp.UNARY_INVERT or
                ByteCodeOp.GET_ITER or ByteCodeOp.GET_YIELD_FROM_ITER or ByteCodeOp.GET_AITER or ByteCodeOp.GET_AWAITABLE or
                ByteCodeOp.SEND or ByteCodeOp.YIELD_VALUE or ByteCodeOp.KW_NAMES or ByteCodeOp.SETUP_ANNOTATIONS or
                ByteCodeOp.CHECK_EXC_MATCH or ByteCodeOp.CHECK_EG_MATCH or ByteCodeOp.CALL_INTRINSIC_1 or
                ByteCodeOp.LOAD_FROM_DICT_OR_DEREF or ByteCodeOp.LOAD_FROM_DICT_OR_GLOBALS => 0,
            _ => throw new InvalidOperationException($"Unspecified stack effect: {op}")
        };
    }
}
