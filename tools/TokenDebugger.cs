using System;
using System.IO;

namespace SharpPy.Tools
{
    /// <summary>
    /// 토큰화 디버깅을 위한 클래스
    /// </summary>
    public class TokenDebugger
    {
        public void OutputTokens(string pythonFile)
        {
            try
            {
                if (string.IsNullOrEmpty(pythonFile) || !File.Exists(pythonFile))
                {
                    Console.WriteLine("Error: Python file not found.");
                    return;
                }

                var source = File.ReadAllText(pythonFile);
                Console.WriteLine($"🔧 SharpPy Token Analysis: {pythonFile}");
                Console.WriteLine("========================================");

                var lexer = new PyLexer(source);
                var tokens = lexer.Tokenize(debugOutput: true);

                Console.WriteLine("========================================");
                Console.WriteLine($"✅ Total {tokens.Count} tokens generated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during tokenization: {ex.Message}");
            }
        }
    }
}