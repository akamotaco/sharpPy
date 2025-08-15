// bytecode_serialization_complete.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpPy
{
    // Binary serializer for bytecode (like Python's .pyc files)
    public static class BytecodeSerializer
    {
        private const uint MAGIC_NUMBER = 0x50594301; // "PYC\x01" 
        private const int VERSION = 1;

        public static void SaveToFile(CodeObject code, string filename)
        {
            using var stream = new FileStream(filename, FileMode.Create);
            using var writer = new BinaryWriter(stream);
            
            // Write header
            writer.Write(MAGIC_NUMBER);
            writer.Write(VERSION);
            writer.Write(DateTime.Now.Ticks); // Timestamp
            
            // Write code object
            WriteCodeObject(writer, code);
        }

        public static CodeObject LoadFromFile(string filename)
        {
            using var stream = new FileStream(filename, FileMode.Open);
            using var reader = new BinaryReader(stream);
            
            // Read and verify header
            var magic = reader.ReadUInt32();
            if (magic != MAGIC_NUMBER)
                throw new InvalidDataException("Invalid bytecode file format");
            
            var version = reader.ReadInt32();
            if (version != VERSION)
                throw new InvalidDataException($"Unsupported bytecode version: {version}");
            
            var timestamp = reader.ReadInt64(); // Skip timestamp for now
            
            // Read code object
            return ReadCodeObject(reader);
        }

        public static byte[] Serialize(CodeObject code)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            
            writer.Write(MAGIC_NUMBER);
            writer.Write(VERSION);
            writer.Write(DateTime.Now.Ticks);
            
            WriteCodeObject(writer, code);
            return stream.ToArray();
        }

        public static CodeObject Deserialize(byte[] data)
        {
            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream);
            
            var magic = reader.ReadUInt32();
            if (magic != MAGIC_NUMBER)
                throw new InvalidDataException("Invalid bytecode format");
            
            var version = reader.ReadInt32();
            var timestamp = reader.ReadInt64();
            
            return ReadCodeObject(reader);
        }

        private static void WriteCodeObject(BinaryWriter writer, CodeObject code)
        {
            writer.Write(code.Name ?? "");
            writer.Write(code.Filename ?? "");
            writer.Write(code.ArgumentCount);
            
            // Write instructions
            writer.Write(code.Instructions.Count);
            foreach (var instruction in code.Instructions)
            {
                writer.Write((byte)instruction.OpCode);
                writer.Write(instruction.Argument);
                writer.Write(instruction.LineNumber);
            }
            
            // Write constants (now PythonTypeObject)
            writer.Write(code.Constants.Count);
            foreach (var constant in code.Constants)
            {
                WriteConstant(writer, constant);
            }
            
            // Write names
            writer.Write(code.Names.Count);
            foreach (var name in code.Names)
            {
                writer.Write(name);
            }
            
            // Write variable names
            writer.Write(code.VarNames.Count);
            foreach (var varName in code.VarNames)
            {
                writer.Write(varName);
            }
        }

        private static CodeObject ReadCodeObject(BinaryReader reader)
        {
            var name = reader.ReadString();
            var filename = reader.ReadString();
            var argumentCount = reader.ReadInt32();
            
            // Read instructions
            var instructionCount = reader.ReadInt32();
            var instructions = new List<Instruction>();
            for (int i = 0; i < instructionCount; i++)
            {
                var opCode = (OpCode)reader.ReadByte();
                var argument = reader.ReadInt32();
                var lineNumber = reader.ReadInt32();
                instructions.Add(new Instruction(opCode, argument, lineNumber));
            }
            
            // Read constants (now PythonTypeObject)
            var constantCount = reader.ReadInt32();
            var constants = new List<PythonTypeObject>();
            for (int i = 0; i < constantCount; i++)
            {
                constants.Add(ReadConstant(reader));
            }
            
            // Read names
            var nameCount = reader.ReadInt32();
            var names = new List<string>();
            for (int i = 0; i < nameCount; i++)
            {
                names.Add(reader.ReadString());
            }
            
            // Read variable names
            var varNameCount = reader.ReadInt32();
            var varNames = new List<string>();
            for (int i = 0; i < varNameCount; i++)
            {
                varNames.Add(reader.ReadString());
            }
            
            return new CodeObject(name, filename, instructions, constants, names, varNames, argumentCount);
        }

        private static void WriteConstant(BinaryWriter writer, PythonTypeObject constant)
        {
            if (constant == null || constant is PythonNone)
            {
                writer.Write((byte)0); // None
            }
            else if (constant is PythonBool b)
            {
                writer.Write((byte)1);
                writer.Write(b.Value);
            }
            else if (constant is PythonInt i)
            {
                writer.Write((byte)2);
                writer.Write(i.Value);
            }
            else if (constant is PythonFloat f)
            {
                writer.Write((byte)3);
                writer.Write(f.Value);
            }
            else if (constant is PythonString s)
            {
                writer.Write((byte)4);
                writer.Write(s.Value);
            }
            else if (constant is PythonCodeObject codeObj)
            {
                writer.Write((byte)5);
                WriteCodeObject(writer, codeObj.Code);
            }
            else if (constant is PythonList list)
            {
                writer.Write((byte)6);
                writer.Write(list.Items.Count);
                foreach (var item in list.Items)
                {
                    WriteConstant(writer, item);
                }
            }
            else if (constant is PythonTuple tuple)
            {
                writer.Write((byte)7);
                writer.Write(tuple.Items.Count);
                foreach (var item in tuple.Items)
                {
                    WriteConstant(writer, item);
                }
            }
            else if (constant is PythonDict dict)
            {
                writer.Write((byte)8);
                writer.Write(dict.Items.Count);
                foreach (var kvp in dict.Items)
                {
                    WriteConstant(writer, kvp.Key);
                    WriteConstant(writer, kvp.Value);
                }
            }
            else
            {
                throw new InvalidOperationException($"Cannot serialize constant of type: {constant.GetType()}");
            }
        }

        private static PythonTypeObject ReadConstant(BinaryReader reader)
        {
            var type = reader.ReadByte();
            return type switch
            {
                0 => PythonNone.Instance,
                1 => new PythonBool(reader.ReadBoolean()),
                2 => new PythonInt(reader.ReadInt32()),
                3 => new PythonFloat(reader.ReadDouble()),
                4 => new PythonString(reader.ReadString()),
                5 => new PythonCodeObject(ReadCodeObject(reader)),
                6 => ReadList(reader),
                7 => ReadTuple(reader),
                8 => ReadDict(reader),
                _ => throw new InvalidDataException($"Unknown constant type: {type}")
            };
        }

        private static PythonList ReadList(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var list = new PythonList();
            for (int i = 0; i < count; i++)
            {
                list.Items.Add(ReadConstant(reader));
            }
            return list;
        }

        private static PythonTuple ReadTuple(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var tuple = new PythonTuple();
            for (int i = 0; i < count; i++)
            {
                tuple.Items.Add(ReadConstant(reader));
            }
            return tuple;
        }

        private static PythonDict ReadDict(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var dict = new PythonDict();
            for (int i = 0; i < count; i++)
            {
                var key = ReadConstant(reader);
                var value = ReadConstant(reader);
                dict.Items[key] = value;
            }
            return dict;
        }
    }

    // Compile-time functions for eval/exec
    public static class PythonCompiler
    {
        public static CodeObject Compile(string source, string filename = "<string>", string mode = "exec")
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            
            if (mode == "eval")
            {
                // Single expression mode
                var expression = parser.ParseExpression();
                var compiler = new BytecodeCompiler(filename);
                return compiler.CompileExpression(expression);
            }
            else
            {
                // Statement mode (exec)
                var statements = parser.Parse();
                var compiler = new BytecodeCompiler(filename);
                return compiler.Compile(statements);
            }
        }

        public static PythonTypeObject Eval(string expression, Environment globals = null, Environment locals = null)
        {
            globals = globals ?? new Environment();
            locals = locals ?? globals;
            
            var code = Compile(expression, "<eval>", "eval");
            var vm = new VirtualMachine(globals);
            return vm.Execute(code, locals);
        }

        public static PythonTypeObject Exec(string source, Environment globals = null, Environment locals = null)
        {
            globals = globals ?? new Environment();
            locals = locals ?? globals;
            
            var code = Compile(source, "<exec>", "exec");
            var vm = new VirtualMachine(globals);
            return vm.Execute(code, locals);
        }
    }

    // Enhanced bytecode disassembler for debugging
    public static class Disassembler
    {
        public static string Disassemble(CodeObject code)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Disassembly of {code.Name} ({code.Filename}):");
            sb.AppendLine($"Arguments: {code.ArgumentCount}");
            sb.AppendLine();
            
            if (code.Constants.Count > 0)
            {
                sb.AppendLine("Constants:");
                for (int i = 0; i < code.Constants.Count; i++)
                {
                    var constant = code.Constants[i];
                    var constStr = GetConstantRepresentation(constant);
                    sb.AppendLine($"  {i}: {constStr}");
                }
                sb.AppendLine();
            }
            
            if (code.Names.Count > 0)
            {
                sb.AppendLine($"Names: [{string.Join(", ", code.Names.Select(n => $"'{n}'"))}]");
                sb.AppendLine();
            }
            
            if (code.VarNames.Count > 0)
            {
                sb.AppendLine($"Variable names: [{string.Join(", ", code.VarNames.Select(n => $"'{n}'"))}]");
                sb.AppendLine();
            }
            
            sb.AppendLine("Bytecode:");
            for (int i = 0; i < code.Instructions.Count; i++)
            {
                var instruction = code.Instructions[i];
                var lineInfo = code.LineNumberTable.ContainsKey(i) ? $"L{code.LineNumberTable[i]:D3}" : "   ";
                
                var argStr = "";
                if (instruction.Argument != 0)
                {
                    argStr = instruction.OpCode switch
                    {
                        OpCode.LOAD_CONST => $"({GetConstantRepresentation(code.Constants[instruction.Argument])})",
                        OpCode.LOAD_NAME or OpCode.STORE_NAME => $"({code.Names[instruction.Argument]})",
                        _ => $"({instruction.Argument})"
                    };
                }
                
                sb.AppendLine($"  {i:D3} {lineInfo} {instruction.OpCode,-15} {instruction.Argument,3} {argStr}");
            }
            
            return sb.ToString();
        }

        private static string GetConstantRepresentation(PythonTypeObject constant)
        {
            return constant switch
            {
                null => "None",
                PythonNone => "None",
                PythonString s => $"'{s.Value}'",
                PythonBool b => b.Value ? "True" : "False",
                PythonInt i => i.Value.ToString(),
                PythonFloat f => f.Value.ToString(),
                PythonCodeObject c => $"<code object {c.Code.Name}>",
                PythonList l => $"[{string.Join(", ", l.Items.Select(GetConstantRepresentation))}]",
                PythonTuple t => $"({string.Join(", ", t.Items.Select(GetConstantRepresentation))})",
                PythonDict d => "{...}",
                _ => constant.ToPythonString()
            };
        }
    }

    // Helper class for advanced bytecode optimization (optional)
    public static class BytecodeOptimizer
    {
        public static CodeObject Optimize(CodeObject code)
        {
            // Perform peephole optimizations
            var optimizedInstructions = new List<Instruction>(code.Instructions);
            
            // Example: Remove consecutive LOAD_CONST + POP_TOP
            for (int i = 0; i < optimizedInstructions.Count - 1; i++)
            {
                if (optimizedInstructions[i].OpCode == OpCode.LOAD_CONST &&
                    optimizedInstructions[i + 1].OpCode == OpCode.POP_TOP)
                {
                    // Replace with NOP
                    optimizedInstructions[i] = new Instruction(OpCode.NOP);
                    optimizedInstructions[i + 1] = new Instruction(OpCode.NOP);
                }
            }
            
            // Remove NOPs
            optimizedInstructions.RemoveAll(inst => inst.OpCode == OpCode.NOP);
            
            return new CodeObject(
                code.Name,
                code.Filename,
                optimizedInstructions,
                code.Constants,
                code.Names,
                code.VarNames,
                code.ArgumentCount
            );
        }
    }
}