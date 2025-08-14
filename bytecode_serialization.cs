// bytecode_serialization.cs
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
            
            // Write constants
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
            
            // Read constants
            var constantCount = reader.ReadInt32();
            var constants = new List<object>();
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

        private static void WriteConstant(BinaryWriter writer, object constant)
        {
            if (constant == null)
            {
                writer.Write((byte)0); // None
            }
            else if (constant is bool b)
            {
                writer.Write((byte)1);
                writer.Write(b);
            }
            else if (constant is int i)
            {
                writer.Write((byte)2);
                writer.Write(i);
            }
            else if (constant is double d)
            {
                writer.Write((byte)3);
                writer.Write(d);
            }
            else if (constant is string s)
            {
                writer.Write((byte)4);
                writer.Write(s);
            }
            else if (constant is CodeObject code)
            {
                writer.Write((byte)5);
                WriteCodeObject(writer, code);
            }
            else
            {
                throw new InvalidOperationException($"Cannot serialize constant of type: {constant.GetType()}");
            }
        }

        private static object ReadConstant(BinaryReader reader)
        {
            var type = reader.ReadByte();
            return type switch
            {
                0 => null,
                1 => reader.ReadBoolean(),
                2 => reader.ReadInt32(),
                3 => reader.ReadDouble(),
                4 => reader.ReadString(),
                5 => ReadCodeObject(reader),
                _ => throw new InvalidDataException($"Unknown constant type: {type}")
            };
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

        public static object Eval(string expression, Environment globals = null, Environment locals = null)
        {
            globals = globals ?? new Environment();
            locals = locals ?? globals;
            
            var code = Compile(expression, "<eval>", "eval");
            var vm = new VirtualMachine(globals);
            return vm.Execute(code, locals);
        }

        public static object Exec(string source, Environment globals = null, Environment locals = null)
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
                    var constStr = constant switch
                    {
                        null => "None",
                        string s => $"'{s}'",
                        bool b => b ? "True" : "False",
                        CodeObject c => $"<code object {c.Name}>",
                        _ => constant.ToString()
                    };
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

        private static string GetConstantRepresentation(object constant)
        {
            return constant switch
            {
                null => "None",
                string s => $"'{s}'",
                bool b => b ? "True" : "False",
                CodeObject c => $"<code object {c.Name}>",
                _ => constant.ToString()
            };
        }
    }
}