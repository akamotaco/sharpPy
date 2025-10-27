using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 basicblock structure (Python/compile.c)
    /// Represents a basic block in the control flow graph
    /// </summary>
    public class BasicBlock
    {
        /// <summary>
        /// Unique block identifier
        /// </summary>
        public int BlockId { get; set; }

        /// <summary>
        /// Instructions in this block
        /// </summary>
        public List<ByteCodeInstruction> Instructions { get; set; }

        /// <summary>
        /// CPython's i_except: Exception handler block for this block
        /// If not null, this block is protected by the exception handler
        /// </summary>
        public BasicBlock? ExceptionHandler { get; set; }

        /// <summary>
        /// Jump targets (for conditional/unconditional jumps)
        /// </summary>
        public List<BasicBlock> Successors { get; set; }

        /// <summary>
        /// Fallthrough block (next block in sequence)
        /// </summary>
        public BasicBlock? Next { get; set; }

        /// <summary>
        /// Instruction offset (resolved after flattening)
        /// -1 means not yet resolved
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// Is this block an exception handler entry point?
        /// </summary>
        public bool IsExceptionHandler { get; set; }

        /// <summary>
        /// CPython 3.12: Exception table depth (number of stack values to preserve)
        /// - SETUP_FINALLY: depth=0 (pop all)
        /// - SETUP_CLEANUP/WITH: depth=1 (keep exception object)
        /// </summary>
        public int ExceptionDepth { get; set; } = 0;

        /// <summary>
        /// Stack depth at block entry (for exception handling)
        /// </summary>
        public int StartDepth { get; set; }

        /// <summary>
        /// Preserve lasti flag (for exception table)
        /// CPython's h_preserve_lasti
        /// </summary>
        public bool PreserveLasti { get; set; }

        public BasicBlock(int blockId)
        {
            BlockId = blockId;
            Instructions = new List<ByteCodeInstruction>();
            Successors = new List<BasicBlock>();
            Offset = -1;
            StartDepth = 0;
            PreserveLasti = true;
        }

        public override string ToString()
        {
            return $"Block{BlockId} (offset={Offset}, instrs={Instructions.Count}, handler={ExceptionHandler?.BlockId.ToString() ?? "none"})";
        }
    }
}
