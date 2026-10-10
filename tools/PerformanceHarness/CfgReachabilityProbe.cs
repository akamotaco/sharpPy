using SharpPy;

internal static class CfgReachabilityProbe
{
    public static object Run()
    {
        // An unreachable cycle cannot make itself reachable. Split at each
        // terminal, including the constant-return and re-raise forms.
        foreach (var terminal in new[] { ByteCodeOp.RETURN_CONST, ByteCodeOp.RETURN_VALUE,
            ByteCodeOp.RAISE_VARARGS, ByteCodeOp.RERAISE })
        {
            var cfg = new ControlFlowGraph();
            var dead = cfg.CreateBlock();
            cfg.EntryBlock.Instructions.Add(new ByteCodeInstruction(terminal));
            cfg.EntryBlock.Next = dead;
            dead.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.JUMP_BACKWARD, 0,
                targetBlock: dead));
            cfg.CalculatePredecessors();
            cfg.EliminateUnreachableCode();
            if (dead.Instructions.Count != 0) throw new Exception("retained unreachable cycle");
        }

        var sequence = new InstructionSequence();
        var cycle = sequence.NewLabel();
        sequence.AddOpWithArg(ByteCodeOp.RETURN_CONST, 0, 1);
        sequence.UseLabel(cycle);
        sequence.AddOpWithLabel(ByteCodeOp.JUMP, cycle, 2);
        var graph = PyFlowGraph.Build(sequence);
        if (graph.AllBlocks.Sum(b => b.Instructions.Count) != 1)
            throw new Exception("return boundary retained dead jump");

        // A suspended generator resumes after YIELD_VALUE; an injected error
        // can enter its exception handler. Neither edge may be dropped.
        var generator = new ControlFlowGraph();
        var resume = generator.CreateBlock();
        var handler = generator.CreateBlock();
        generator.EntryBlock.Next = resume;
        resume.Next = handler;
        generator.EntryBlock.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.YIELD_VALUE,
            exceptBlock: handler));
        resume.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.RETURN_CONST));
        handler.Instructions.Add(new ByteCodeInstruction(ByteCodeOp.RERAISE));
        generator.CalculatePredecessors();
        generator.EliminateUnreachableCode();
        if (resume.Instructions.Count != 1 || handler.Instructions.Count != 1)
            throw new Exception("lost generator resume or handler edge");
        return new { status = "PASS", terminal_cycles = 4, constant_return_boundary = true,
            generator_resume_and_handler = true };
    }
}
