using System;
using System.IO;
using SharpPy.Generated;

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
                Console.WriteLine("[DEBUG] TokenDebugger.OutputTokens START");

                if (string.IsNullOrEmpty(pythonFile) || !File.Exists(pythonFile))
                {
                    Console.WriteLine("Error: Python file not found.");
                    return;
                }

                Console.WriteLine($"[DEBUG] Reading file: {pythonFile}");
                var source = File.ReadAllText(pythonFile);
                Console.WriteLine($"[DEBUG] File content length: {source.Length}");

                Console.WriteLine($"🔧 SharpPy Token Analysis: {pythonFile}");
                Console.WriteLine("========================================");

                Console.WriteLine("[DEBUG] Creating tokenizer...");
                var tokenizer = new PyTokenizer(source, pythonFile);
                Console.WriteLine("[DEBUG] Tokenizer created, calling Tokenize()...");
                var tokens = tokenizer.Tokenize();
                Console.WriteLine($"[DEBUG] Tokenize() returned {tokens.Count} tokens");

                Console.WriteLine("Generated tokens:");
                foreach (var token in tokens)
                {
                    Console.WriteLine($"  {token.Type}: '{token.Value}' (line {token.Line}, col {token.Column})");
                }

                Console.WriteLine("========================================");
                Console.WriteLine($"✅ Total {tokens.Count} tokens generated");
                Console.WriteLine("[DEBUG] TokenDebugger.OutputTokens END");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during tokenization: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}