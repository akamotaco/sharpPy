// Modules/_opcode.c of CPython v3.12.15
namespace SharpPy.Modules
{
    internal static class _OpcodeModule
    {
        public static PyModule Create()
        {
            var module = new PyModule("_opcode", "Opcode support module.");
            module.ModuleDict["stack_effect"] = new PyBuiltinFunction("stack_effect", StackEffect);
            return module;
        }

        // _opcode_stack_effect_impl
        private static PyObject StackEffect(PyObject[] args, PyDict kwargs)
        {
            int opcode = args[0].ToInt();
            bool hasArg = opcode >= 90 && opcode < 256;
            PyObject oparg = args.Length > 1 ? args[1] : null;
            if (hasArg && (oparg == null || oparg is PyNone))
                throw PyValueError.Create("stack_effect: opcode requires oparg but oparg was not specified");
            if (!hasArg && oparg != null && oparg is not PyNone)
                throw PyValueError.Create("stack_effect: opcode does not permit oparg but oparg was specified");
            var jump = kwargs != null ? PyObject.KwValue(kwargs, "jump") : null;
            int argument = oparg == null || oparg is PyNone ? 0 : oparg.ToInt();
            if (jump == null || jump is PyNone)
                return PyInt.From(System.Math.Max(CFGOptimizer.StackEffect((ByteCodeOp)opcode, argument, true),
                    CFGOptimizer.StackEffect((ByteCodeOp)opcode, argument, false)));
            if (jump is not PyBool)
                throw PyValueError.Create("stack_effect: jump must be False, True or None");
            return PyInt.From(CFGOptimizer.StackEffect((ByteCodeOp)opcode, argument, jump.IsTrue()));
        }
    }
}
