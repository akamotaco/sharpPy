using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12's _PyCompile_InstructionSequence
    /// Label-based intermediate representation before CFG conversion
    /// Python/compile.c: InstructionSequence is used to build bytecode with labels
    /// Labels are resolved to offsets later during assembly
    /// </summary>

    /// <summary>
    /// Jump target label - corresponds to CPython's _PyCfgJumpTargetLabel
    /// Just an ID, offset is determined later
    /// </summary>
    public struct Label : IEquatable<Label>
    {
        public int Id { get; }

        public static readonly Label NoLabel = new Label(-1);

        public Label(int id)
        {
            Id = id;
        }

        public bool IsValid => Id >= 0;

        public bool Equals(Label other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is Label other && Equals(other);
        public override int GetHashCode() => Id;
        public static bool operator ==(Label left, Label right) => left.Equals(right);
        public static bool operator !=(Label left, Label right) => !left.Equals(right);

        public override string ToString() => IsValid ? $"Label({Id})" : "NoLabel";
    }

    /// <summary>
    /// Single instruction in the sequence - corresponds to CPython's _PyCompile_Instruction
    /// Can have either an integer argument OR a label target (for jumps)
    /// </summary>
    public struct Instruction
    {
        public ByteCodeOp OpCode { get; }

        /// <summary>
        /// For non-jump instructions (LOAD_CONST, LOAD_NAME, etc.)
        /// </summary>
        public int? Arg { get; }

        /// <summary>
        /// For jump instructions (JUMP_FORWARD, POP_JUMP_IF_FALSE, etc.)
        /// </summary>
        public Label? Target { get; }

        public int LineNumber { get; }
        public int ColumnOffset { get; }
        public string? FileName { get; }
        public ExceptHandlerInfo ExceptHandler { get; }

        // Constructor for non-jump instructions with integer argument
        public Instruction(
            ByteCodeOp opCode,
            int arg,
            int lineNumber,
            int columnOffset,
            string? fileName,
            ExceptHandlerInfo exceptHandler)
        {
            OpCode = opCode;
            Arg = arg;
            Target = null;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
            FileName = fileName;
            ExceptHandler = exceptHandler;
        }

        // Constructor for jump instructions with label target
        public Instruction(
            ByteCodeOp opCode,
            Label target,
            int lineNumber,
            int columnOffset,
            string? fileName,
            ExceptHandlerInfo exceptHandler)
        {
            OpCode = opCode;
            Arg = null;
            Target = target;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
            FileName = fileName;
            ExceptHandler = exceptHandler;
        }

        // Constructor for instructions with no argument (NOP, POP_TOP, etc.)
        public Instruction(
            ByteCodeOp opCode,
            int lineNumber,
            int columnOffset,
            string? fileName,
            ExceptHandlerInfo exceptHandler)
        {
            OpCode = opCode;
            Arg = null;
            Target = null;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
            FileName = fileName;
            ExceptHandler = exceptHandler;
        }

        public bool IsJump => Target.HasValue;

        public override string ToString()
        {
            if (Target.HasValue)
                return $"{OpCode} -> {Target.Value}";
            else if (Arg.HasValue)
                return $"{OpCode} {Arg.Value}";
            else
                return $"{OpCode}";
        }
    }

    /// <summary>
    /// Label-based instruction sequence - corresponds to CPython's _PyCompile_InstructionSequence
    /// This is the intermediate representation used during compilation
    /// Labels are not yet resolved to byte offsets
    /// </summary>
    public class InstructionSequence
    {
        private readonly List<Instruction> _instructions;

        /// <summary>
        /// Maps label ID to instruction index where the label is placed
        /// Corresponds to CPython's label mapping in instr_sequence
        /// </summary>
        private readonly Dictionary<int, int> _labelTargets;

        private int _nextLabelId;

        public InstructionSequence()
        {
            _instructions = new List<Instruction>();
            _labelTargets = new Dictionary<int, int>();
            _nextLabelId = 0;
        }

        /// <summary>
        /// Create a new label - corresponds to CPython's instr_sequence_new_label()
        /// The label is just an ID, position is set later with UseLabel()
        /// </summary>
        public Label NewLabel()
        {
            return new Label(_nextLabelId++);
        }

        /// <summary>
        /// Mark the current position as the target for this label
        /// Corresponds to CPython's instr_sequence_use_label()
        /// </summary>
        public void UseLabel(Label label)
        {
            if (!label.IsValid)
                throw new ArgumentException("Invalid label", nameof(label));

            // Record that this label points to the next instruction
            _labelTargets[label.Id] = _instructions.Count;
        }

        /// <summary>
        /// Add an instruction with no argument
        /// </summary>
        public void AddOp(
            ByteCodeOp opCode,
            int lineNumber,
            int columnOffset = -1,
            string? fileName = null,
            ExceptHandlerInfo? exceptHandler = null)
        {
            _instructions.Add(new Instruction(
                opCode,
                lineNumber,
                columnOffset,
                fileName,
                exceptHandler ?? ExceptHandlerInfo.NoHandler
            ));
        }

        /// <summary>
        /// Add an instruction with integer argument
        /// </summary>
        public void AddOpWithArg(
            ByteCodeOp opCode,
            int arg,
            int lineNumber,
            int columnOffset = -1,
            string? fileName = null,
            ExceptHandlerInfo? exceptHandler = null)
        {
            _instructions.Add(new Instruction(
                opCode,
                arg,
                lineNumber,
                columnOffset,
                fileName,
                exceptHandler ?? ExceptHandlerInfo.NoHandler
            ));
        }

        /// <summary>
        /// Add a jump instruction with label target
        /// </summary>
        public void AddOpWithLabel(
            ByteCodeOp opCode,
            Label target,
            int lineNumber,
            int columnOffset = -1,
            string? fileName = null,
            ExceptHandlerInfo? exceptHandler = null)
        {
            if (!target.IsValid)
                throw new ArgumentException("Invalid label for jump target", nameof(target));

            _instructions.Add(new Instruction(
                opCode,
                target,
                lineNumber,
                columnOffset,
                fileName,
                exceptHandler ?? ExceptHandlerInfo.NoHandler
            ));
        }

        /// <summary>
        /// Get the instruction index where a label points to
        /// Returns -1 if label not found (not yet used)
        /// </summary>
        public int GetLabelTarget(Label label)
        {
            if (!label.IsValid)
                return -1;

            return _labelTargets.TryGetValue(label.Id, out int target) ? target : -1;
        }

        /// <summary>
        /// Check if a label has been used (position marked)
        /// </summary>
        public bool IsLabelUsed(Label label)
        {
            return label.IsValid && _labelTargets.ContainsKey(label.Id);
        }

        /// <summary>
        /// Get all instructions (read-only)
        /// </summary>
        public IReadOnlyList<Instruction> Instructions => _instructions.AsReadOnly();

        /// <summary>
        /// Get instruction count
        /// </summary>
        public int Count => _instructions.Count;

        /// <summary>
        /// CPython 3.12: Insert instruction at specific position
        /// Used by wrap_in_stopiteration_handler to insert SETUP_CLEANUP at start
        /// </summary>
        public void InsertAt(int index, Instruction instruction)
        {
            _instructions.Insert(index, instruction);

            // Update label map: all labels pointing to index >= insertion point need to be shifted by 1
            var labelsToUpdate = new List<(int labelId, int oldIndex)>();
            foreach (var kvp in _labelTargets)
            {
                if (kvp.Value >= index)
                {
                    labelsToUpdate.Add((kvp.Key, kvp.Value));
                }
            }

            foreach (var (labelId, oldIndex) in labelsToUpdate)
            {
                _labelTargets[labelId] = oldIndex + 1;
            }
        }

        /// <summary>
        /// DEPRECATED: Convert to linear bytecode instructions
        /// This is a legacy bridge for LEGACY compilation path only
        /// CFG path uses InstructionSequence → PyFlowGraph → CFG → PyAssemble
        /// </summary>
        public List<ByteCodeInstruction> ToByteCodeInstructions()
        {
            var result = new List<ByteCodeInstruction>();

            foreach (var instr in _instructions)
            {
                if (instr.IsJump && instr.Target.HasValue)
                {
                    // Resolve label to instruction index
                    int targetInstrIndex = GetLabelTarget(instr.Target.Value);
                    if (targetInstrIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"Jump to undefined label: {instr.Target.Value}");
                    }

                    result.Add(new ByteCodeInstruction(
                        instr.OpCode,
                        targetInstrIndex,
                        instr.LineNumber,
                        instr.ColumnOffset,
                        instr.FileName,
                        instr.ExceptHandler
                    ));
                }
                else
                {
                    // Regular instruction with integer argument (or no argument)
                    result.Add(new ByteCodeInstruction(
                        instr.OpCode,
                        instr.Arg ?? 0,
                        instr.LineNumber,
                        instr.ColumnOffset,
                        instr.FileName,
                        instr.ExceptHandler
                    ));
                }
            }

            return result;
        }

        /// <summary>
        /// CPython 3.12: compile.c:7517-7587 insert_prefix_instructions
        /// Insert prefix instructions at the start of InstructionSequence
        /// CRITICAL: This must be called BEFORE CFG conversion so labels are automatically adjusted
        /// </summary>
        public void InsertPrefixInstructions(
            int codeFlags,
            List<string> cellVars,
            List<string> freeVars)
        {
            bool isGenerator = (codeFlags & PyCodeObject.CO_GENERATOR) != 0;
            bool isCoroutine = (codeFlags & PyCodeObject.CO_COROUTINE) != 0;
            bool isAsyncGenerator = (codeFlags & PyCodeObject.CO_ASYNC_GENERATOR) != 0;

            int insertPosition = 0;

            // CPython compile.c:7576-7584: Insert COPY_FREE_VARS at position 0
            if (freeVars.Count > 0)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔷 [InstrSeq] Inserting COPY_FREE_VARS for {freeVars.Count} free variables at position {insertPosition}");
#endif
                InsertAt(insertPosition++, new Instruction(
                    ByteCodeOp.COPY_FREE_VARS,
                    freeVars.Count,
                    0, // lineNumber
                    -1, // columnOffset
                    null, // fileName
                    ExceptHandlerInfo.NoHandler
                ));
            }

            // CPython compile.c:7543-7574: Insert MAKE_CELL for each cellvar
            for (int cellIndex = 0; cellIndex < cellVars.Count; cellIndex++)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔷 [InstrSeq] Inserting MAKE_CELL for {cellVars[cellIndex]} (index {cellIndex}) at position {insertPosition}");
#endif
                InsertAt(insertPosition++, new Instruction(
                    ByteCodeOp.MAKE_CELL,
                    cellIndex,
                    0, // lineNumber
                    -1, // columnOffset
                    null, // fileName
                    ExceptHandlerInfo.NoHandler
                ));
            }

            // CPython compile.c:7523-7538: Insert RETURN_GENERATOR + POP_TOP for generators
            if (isGenerator || isCoroutine || isAsyncGenerator)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔷 [InstrSeq] Inserting RETURN_GENERATOR at position {insertPosition}");
#endif
                InsertAt(insertPosition++, new Instruction(
                    ByteCodeOp.RETURN_GENERATOR,
                    0, // lineNumber
                    -1, // columnOffset
                    null, // fileName
                    ExceptHandlerInfo.NoHandler
                ));

#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🔷 [InstrSeq] Inserting POP_TOP at position {insertPosition}");
#endif
                InsertAt(insertPosition++, new Instruction(
                    ByteCodeOp.POP_TOP,
                    0, // lineNumber
                    -1, // columnOffset
                    null, // fileName
                    ExceptHandlerInfo.NoHandler
                ));
            }

#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔷 [InstrSeq] Prefix instructions inserted. Total: {insertPosition}. New instruction count: {_instructions.Count}");
#endif
        }

        /// <summary>
        /// Debug: Print all instructions with labels
        /// </summary>
        public void DebugPrint()
        {
            Console.WriteLine($"=== InstructionSequence ({_instructions.Count} instructions) ===");

            for (int i = 0; i < _instructions.Count; i++)
            {
                // Check if any label points to this instruction
                var labelsHere = _labelTargets.Where(kv => kv.Value == i)
                    .Select(kv => new Label(kv.Key))
                    .ToList();

                if (labelsHere.Any())
                {
                    foreach (var label in labelsHere)
                    {
                        Console.WriteLine($"  {label}:");
                    }
                }

                Console.WriteLine($"    {i,4}: {_instructions[i]}");
            }
        }
    }
}
