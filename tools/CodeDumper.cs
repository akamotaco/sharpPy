using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// A machine-readable code dump for tools/compare_bytecode.py --full: per instruction
    /// its name, its argument as dis would show it, and its line, then the exception table.
    /// </summary>
    public static class CodeDumper
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("SHARPPY_DIS_FULL") == "1";

        public static void Dump(PyCodeObject code)
        {
            Console.WriteLine("@code " + code.Name);
            var instructions = code.Instructions;
            long extended = 0;
            for (int i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];
                if (instruction.OpCode == ByteCodeOp.CACHE)
                    continue;
                if (instruction.OpCode == ByteCodeOp.EXTENDED_ARG)
                {
                    extended = (extended | (uint)instruction.Argument) << 8;
                    continue;
                }
                long argument = instruction.Argument > 255 ? instruction.Argument : extended | (uint)instruction.Argument;
                extended = 0;
                Console.WriteLine($"#{i * 2}\t{instruction.OpCode}\t{Detail(code, instruction.OpCode, (int)argument, i)}\t{instruction.LineNumber}");
            }
            foreach (var entry in code.ExceptionTable)
                Console.WriteLine($"@exc\t{entry.StartOffset}\t{entry.EndOffset}\t{entry.HandlerOffset}\t{entry.Depth}\t{(entry.Lasti ? 1 : 0)}");
            Console.WriteLine($"@raw\t{Convert.ToHexString(code.Tables.Code).ToLowerInvariant()}\t{Convert.ToHexString(code.Tables.LineTable).ToLowerInvariant()}\t{Convert.ToHexString(code.Tables.ExceptionTable).ToLowerInvariant()}\t{code.StackSize}");
            foreach (var constant in code.Constants)
                if (constant is PyCodeObject nested)
                    Dump(nested);
        }

        private static string Constant(PyCodeObject code, int index)
        {
            if (index < 0 || index >= code.Constants.Count)
                return "?" + index;
            var value = code.Constants[index];
            if (value is PyCodeObject nested)
                return "<code " + nested.Name + ">";
            if (value is PyFrozenSet)
                return "<frozenset>";
            return value.ToRepr().Value.Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static string Name(PyCodeObject code, int index) =>
            index >= 0 && index < code.Names.Count ? code.Names[index] : "?" + index;

        private static string LocalPlus(PyCodeObject code, int index)
        {
            var names = new List<string>(code.VarNames);
            foreach (var cell in code.CellVars)
                if (!names.Contains(cell))
                    names.Add(cell);
            names.AddRange(code.FreeVars);
            return index >= 0 && index < names.Count ? names[index] : "?" + index;
        }

        private static string Detail(PyCodeObject code, ByteCodeOp op, int argument, int word)
        {
            switch (op)
            {
                case ByteCodeOp.LOAD_CONST:
                case ByteCodeOp.RETURN_CONST:
                case ByteCodeOp.KW_NAMES:
                    return Constant(code, argument);
                case ByteCodeOp.LOAD_NAME:
                case ByteCodeOp.STORE_NAME:
                case ByteCodeOp.DELETE_NAME:
                case ByteCodeOp.STORE_GLOBAL:
                case ByteCodeOp.DELETE_GLOBAL:
                case ByteCodeOp.STORE_ATTR:
                case ByteCodeOp.DELETE_ATTR:
                case ByteCodeOp.IMPORT_NAME:
                case ByteCodeOp.IMPORT_FROM:
                case ByteCodeOp.LOAD_FROM_DICT_OR_GLOBALS:
                    return Name(code, argument);
                case ByteCodeOp.LOAD_GLOBAL:
                case ByteCodeOp.LOAD_ATTR:
                    return Name(code, argument >> 1) + "|" + (argument & 1);
                case ByteCodeOp.LOAD_SUPER_ATTR:
                    return Name(code, argument >> 2) + "|" + (argument & 3);
                case ByteCodeOp.LOAD_FAST:
                case ByteCodeOp.STORE_FAST:
                case ByteCodeOp.DELETE_FAST:
                case ByteCodeOp.LOAD_FAST_CHECK:
                case ByteCodeOp.LOAD_FAST_AND_CLEAR:
                case ByteCodeOp.LOAD_DEREF:
                case ByteCodeOp.STORE_DEREF:
                case ByteCodeOp.DELETE_DEREF:
                case ByteCodeOp.LOAD_CLOSURE:
                case ByteCodeOp.MAKE_CELL:
                case ByteCodeOp.LOAD_FROM_DICT_OR_DEREF:
                    return LocalPlus(code, argument);
                case ByteCodeOp.JUMP_FORWARD:
                case ByteCodeOp.POP_JUMP_IF_TRUE:
                case ByteCodeOp.POP_JUMP_IF_FALSE:
                case ByteCodeOp.POP_JUMP_IF_NONE:
                case ByteCodeOp.POP_JUMP_IF_NOT_NONE:
                case ByteCodeOp.FOR_ITER:
                case ByteCodeOp.SEND:
                    return ((word + 1 + PyAssemble.GetInlineCacheSize(op) + argument) * 2).ToString();
                case ByteCodeOp.JUMP_BACKWARD:
                case ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT:
                    return ((word + 1 + PyAssemble.GetInlineCacheSize(op) - argument) * 2).ToString();
                case ByteCodeOp.COMPARE_OP:
                    return ((CompareOp)argument).ToString();
                default:
                    return argument.ToString();
            }
        }
    }
}
