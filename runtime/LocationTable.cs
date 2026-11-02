// Python/assemble.c and the reading side of Objects/codeobject.c, CPython v3.12.15: co_code, co_linetable
// and co_exceptiontable as bytes.
using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>What Python sees of a code object's instructions: the tables, built once at assembly.</summary>
    public sealed class AssembledTables
    {
        public byte[] Code = Array.Empty<byte>();
        public byte[] LineTable = Array.Empty<byte>();
        public byte[] ExceptionTable = Array.Empty<byte>();
    }

    /// <summary>One entry of co_positions: lineno is -1 when there is none.</summary>
    public readonly record struct SourcePosition(int Line, int EndLine, int Column, int EndColumn);

    public static class LocationTable
    {
        private const int CONTINUATION_BIT = 64;

        // Include/cpython/code.h: PyCodeLocationInfoKind
        private const int SHORT0 = 0;
        private const int ONE_LINE0 = 10;
        private const int LONG = 14;
        private const int NO_COLUMNS = 13;
        private const int NONE = 15;

        // assemble_emit_exception_table_item
        private static void EmitExceptionItem(List<byte> table, int value, int msb)
        {
            if (value >= 1 << 24)
            {
                table.Add((byte)((value >> 24) | CONTINUATION_BIT | msb));
                msb = 0;
            }
            if (value >= 1 << 18)
            {
                table.Add((byte)(((value >> 18) & 0x3f) | CONTINUATION_BIT | msb));
                msb = 0;
            }
            if (value >= 1 << 12)
            {
                table.Add((byte)(((value >> 12) & 0x3f) | CONTINUATION_BIT | msb));
                msb = 0;
            }
            if (value >= 1 << 6)
            {
                table.Add((byte)(((value >> 6) & 0x3f) | CONTINUATION_BIT | msb));
                msb = 0;
            }
            table.Add((byte)((value & 0x3f) | msb));
        }

        // assemble_exception_table: entries are in code units in the table.
        internal static byte[] ExceptionBytes(List<ExceptionTableEntry> entries)
        {
            var table = new List<byte>();
            foreach (var entry in entries)
            {
                int start = entry.StartOffset / 2;
                int size = (entry.EndOffset - entry.StartOffset) / 2;
                EmitExceptionItem(table, start, 1 << 7);
                EmitExceptionItem(table, size, 0);
                EmitExceptionItem(table, entry.HandlerOffset / 2, 0);
                EmitExceptionItem(table, (entry.Depth << 1) | (entry.Lasti ? 1 : 0), 0);
            }
            return table.ToArray();
        }

        private static void WriteVarint(List<byte> table, uint value)
        {
            while (value >= 64)
            {
                table.Add((byte)(0x40 | (value & 0x3f)));
                value >>= 6;
            }
            table.Add((byte)value);
        }

        private static void WriteSignedVarint(List<byte> table, int value) =>
            WriteVarint(table, value < 0 ? ((uint)(-value) << 1) | 1 : (uint)value << 1);

        private static void FirstByte(List<byte> table, int code, int length) =>
            table.Add((byte)(0x80 | (code << 3) | (length - 1)));

        // write_location_info_entry
        private static void WriteEntry(List<byte> table, SourcePosition loc, int isize, ref int lineno)
        {
            if (loc.Line < 0)
            {
                FirstByte(table, NONE, isize);
                return;
            }
            int lineDelta = loc.Line - lineno;
            int column = loc.Column;
            int endColumn = loc.EndColumn;
            if (column < 0 || endColumn < 0)
            {
                if (loc.EndLine == loc.Line || loc.EndLine < 0)
                {
                    FirstByte(table, NO_COLUMNS, isize);
                    WriteSignedVarint(table, lineDelta);
                    lineno = loc.Line;
                    return;
                }
            }
            else if (loc.EndLine == loc.Line)
            {
                if (lineDelta == 0 && column < 80 && endColumn - column < 16 && endColumn >= column)
                {
                    FirstByte(table, SHORT0 + (column >> 3), isize);
                    table.Add((byte)(((column & 7) << 4) | (endColumn - column)));
                    return;
                }
                if (lineDelta >= 0 && lineDelta < 3 && column < 128 && endColumn < 128)
                {
                    FirstByte(table, ONE_LINE0 + lineDelta, isize);
                    table.Add((byte)column);
                    table.Add((byte)endColumn);
                    lineno = loc.Line;
                    return;
                }
            }
            FirstByte(table, LONG, isize);
            WriteSignedVarint(table, loc.Line - lineno);
            WriteVarint(table, (uint)(loc.EndLine - loc.Line));
            WriteVarint(table, (uint)(loc.Column + 1));
            WriteVarint(table, (uint)(loc.EndColumn + 1));
            lineno = loc.Line;
        }

        // assemble_emit_location
        private static void Emit(List<byte> table, SourcePosition loc, int isize, ref int lineno)
        {
            if (isize == 0)
                return;
            while (isize > 8)
            {
                WriteEntry(table, loc, 8, ref lineno);
                isize -= 8;
            }
            WriteEntry(table, loc, isize, ref lineno);
        }

        // assemble_location_info: an instruction's caches are in its location.
        internal static byte[] LineBytes(List<ByteCodeInstruction> instructions, int firstLineNo)
        {
            var table = new List<byte>();
            int lineno = firstLineNo;
            var loc = new SourcePosition(-1, -1, -1, -1);
            int size = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.OpCode != ByteCodeOp.CACHE)
                {
                    var here = instruction.LineNumber < 0
                        ? new SourcePosition(-1, -1, -1, -1)
                        : new SourcePosition(instruction.LineNumber, instruction.End.Line, instruction.ColumnOffset, instruction.End.Column);
                    if (here != loc)
                    {
                        Emit(table, loc, size, ref lineno);
                        loc = here;
                        size = 0;
                    }
                }
                size++;
            }
            Emit(table, loc, size, ref lineno);
            return table.ToArray();
        }

        // write_instr
        internal static byte[] CodeBytes(List<ByteCodeInstruction> instructions)
        {
            var code = new byte[instructions.Count * 2];
            for (int i = 0; i < instructions.Count; i++)
            {
                code[i * 2] = (byte)instructions[i].OpCode;
                code[i * 2 + 1] = instructions[i].OpCode == ByteCodeOp.CACHE ? (byte)0 : (byte)(instructions[i].Argument & 0xFF);
            }
            return code;
        }

        private static int ReadVarint(byte[] table, ref int index)
        {
            int value = table[index] & 63;
            int shift = 0;
            while ((table[index++] & 64) != 0)
            {
                shift += 6;
                value |= (table[index] & 63) << shift;
            }
            return value;
        }

        private static int ReadSignedVarint(byte[] table, ref int index)
        {
            int value = ReadVarint(table, ref index);
            return (value & 1) != 0 ? -(value >> 1) : value >> 1;
        }

        /// <summary>The instructions of co_code: one per code unit, as the assembler leaves them.</summary>
        internal static List<ByteCodeInstruction> ReadInstructions(byte[] code, byte[] lineTable, int firstLineNo)
        {
            var positions = Read(lineTable, firstLineNo);
            var instructions = new List<ByteCodeInstruction>(code.Length / 2);
            for (int i = 0; i < code.Length / 2; i++)
            {
                var position = i < positions.Count ? positions[i] : new SourcePosition(-1, -1, -1, -1);
                instructions.Add(new ByteCodeInstruction((ByteCodeOp)code[i * 2], code[i * 2 + 1], position.Line,
                    position.Column, new EndPosition(position.EndLine, position.EndColumn)));
            }
            return instructions;
        }

        // parse_varint of Python/ceval.c: big-endian groups of six bits.
        private static int ReadExceptionItem(byte[] table, ref int index)
        {
            int value = table[index] & 63;
            while ((table[index++] & 64) != 0)
                value = (value << 6) | (table[index] & 63);
            return value;
        }

        /// <summary>co_exceptiontable: the entries, with offsets in bytes as the VM reads them.</summary>
        internal static List<ExceptionTableEntry> ReadExceptionTable(byte[] table)
        {
            var entries = new List<ExceptionTableEntry>();
            int index = 0;
            while (index < table.Length)
            {
                int start = ReadExceptionItem(table, ref index);
                int size = ReadExceptionItem(table, ref index);
                int handler = ReadExceptionItem(table, ref index);
                int depthAndLasti = ReadExceptionItem(table, ref index);
                entries.Add(new ExceptionTableEntry(0, 0, 0, 0, false)
                {
                    StartOffset = start * 2,
                    EndOffset = (start + size) * 2,
                    HandlerOffset = handler * 2,
                    Depth = depthAndLasti >> 1,
                    Lasti = (depthAndLasti & 1) != 0,
                });
            }
            return entries;
        }

        /// <summary>_PyCode_InitAddressRange and advance_with_locations: one position for each code unit.</summary>
        public static List<SourcePosition> Read(byte[] table, int firstLineNo)
        {
            var result = new List<SourcePosition>();
            int line = firstLineNo;
            int index = 0;
            while (index < table.Length)
            {
                int first = table[index++];
                int code = (first >> 3) & 15;
                int length = (first & 7) + 1;
                SourcePosition position;
                switch (code)
                {
                    case NONE:
                        position = new SourcePosition(-1, -1, -1, -1);
                        break;
                    case LONG:
                        line += ReadSignedVarint(table, ref index);
                        int endLine = line + ReadVarint(table, ref index);
                        int column = ReadVarint(table, ref index) - 1;
                        int endColumn = ReadVarint(table, ref index) - 1;
                        position = new SourcePosition(line, endLine, column, endColumn);
                        break;
                    case NO_COLUMNS:
                        line += ReadSignedVarint(table, ref index);
                        position = new SourcePosition(line, line, -1, -1);
                        break;
                    case >= ONE_LINE0:
                        line += code - ONE_LINE0;
                        int start = table[index++];
                        position = new SourcePosition(line, line, start, table[index++]);
                        break;
                    default:
                        int second = table[index++];
                        int low = (code << 3) | (second >> 4);
                        position = new SourcePosition(line, line, low, low + (second & 15));
                        break;
                }
                for (int i = 0; i < length; i++)
                    result.Add(position);
            }
            return result;
        }
    }
}
