namespace SharpPy.Modules
{
    /// <summary>
    /// PC/msvcrtmodule.c, the part _pyio and the standard library reach on Windows. Descriptors
    /// here are always binary: the C runtime's text mode, which turns "\n" into "\r\n", has no
    /// place in a .NET stream, so setmode answers binary and changes nothing.
    /// </summary>
    internal static class MsvcrtModule
    {
        private const int O_TEXT = 0x4000, O_BINARY = 0x8000;

        internal static PyModule CreateModule()
        {
            var module = new PyModule("msvcrt");
            module.ModuleDict["setmode"] = new PyBuiltinFunction("setmode", args =>
            {
                if (args.Length != 2)
                    throw PyTypeError.Create($"setmode expected 2 arguments, got {args.Length}");
                PyFileDescriptors.Get(args[0].ToInt());
                int mode = args[1].ToInt();
                if (mode != O_TEXT && mode != O_BINARY)
                    throw new PythonException(PyOSError.FromArguments(new PyObject[] { new PyInt(22), new PyString("Invalid argument") }, null));
                return new PyInt(O_BINARY);
            });
            // The handle of a descriptor is the descriptor: there is no Win32 handle under a .NET stream.
            module.ModuleDict["get_osfhandle"] = new PyBuiltinFunction("get_osfhandle", args =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"get_osfhandle() takes exactly one argument ({args.Length} given)");
                PyFileDescriptors.Get(args[0].ToInt());
                return new PyInt(args[0].ToInt());
            });
            return module;
        }
    }
}
