using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible PEG parser entry point
    /// SharpPy now exclusively uses PEG parser for 100% CPython 3.12 compatibility
    /// </summary>
    public static class PyParserBridge
    {
        /// <summary>
        /// Main parsing entry point - uses CPython 3.12 compatible PEG parser
        /// </summary>
        public static List<Statement> ParseSource(string source, string filename = "<string>")
        {
#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] Bridge: Using CPython 3.12 compatible PEG parser for {filename}");
#endif
            return PyPegParser.ParseSource(source, filename);
        }
    }

    /// <summary>
    /// Configuration for CPython 3.12 compatible parser
    /// </summary>
    public static class ParserConfig
    {
        /// <summary>
        /// Initialize parser for production use
        /// </summary>
        public static void Initialize()
        {
            // CPython 3.12 PEG parser is always enabled
            // No configuration needed - parser is production ready
        }
    }
}