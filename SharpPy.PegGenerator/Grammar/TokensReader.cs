using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SharpPy.PegGenerator.Grammar
{
    /// <summary>
    /// Represents a token definition from Grammar/Tokens file
    /// </summary>
    public class TokenDefinition
    {
        public string Name { get; set; } = "";
        public string? Value { get; set; }  // null for complex tokens like NAME, NUMBER
        public bool IsLiteral => Value != null;
        public bool IsKeyword { get; set; } = false; // Python keywords

        public override string ToString()
        {
            return Value != null ? $"{Name} = '{Value}'" : Name;
        }
    }

    /// <summary>
    /// Reads and parses the Grammar/Tokens file
    /// </summary>
    public class TokensReader
    {
        private static readonly Regex TokenLineRegex = new Regex(
            @"^(?<name>[A-Z_]+)(?:\s+(?<value>'.*'))?(?:\s*#.*)?$",
            RegexOptions.Compiled);

        /// <summary>
        /// Read tokens from the Grammar/Tokens file
        /// </summary>
        public static List<TokenDefinition> ReadTokens(string tokensFilePath)
        {
            if (!File.Exists(tokensFilePath))
            {
                throw new FileNotFoundException($"Tokens file not found: {tokensFilePath}");
            }

            var tokens = new List<TokenDefinition>();
            var lines = File.ReadAllLines(tokensFilePath);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                // Parse token definition
                var match = TokenLineRegex.Match(trimmed);
                if (match.Success)
                {
                    var name = match.Groups["name"].Value;
                    var valueGroup = match.Groups["value"];
                    var value = valueGroup.Success ? valueGroup.Value.Trim('\'') : null;

                    tokens.Add(new TokenDefinition
                    {
                        Name = name,
                        Value = value
                    });
                }
                else
                {
                    // Handle simple token names without values
                    if (Regex.IsMatch(trimmed, @"^[A-Z_]+$"))
                    {
                        tokens.Add(new TokenDefinition
                        {
                            Name = trimmed,
                            Value = null
                        });
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not parse token line: {trimmed}");
                    }
                }
            }

            return tokens;
        }

        /// <summary>
        /// Generate C# enum for tokens
        /// </summary>
        public static string GenerateTokenEnum(List<TokenDefinition> tokens)
        {
            var enumValues = new List<string>();

            foreach (var token in tokens)
            {
                enumValues.Add($"    {token.Name}");
            }

            return $@"public enum TokenType
{{
{string.Join(",\n", enumValues)}
}}";
        }

        /// <summary>
        /// Generate C# token literals dictionary
        /// </summary>
        public static string GenerateTokenLiterals(List<TokenDefinition> tokens)
        {
            var literals = new List<string>();

            foreach (var token in tokens)
            {
                if (token.IsLiteral)
                {
                    literals.Add($"    {{\"{token.Value}\", TokenType.{token.Name}}}");
                }
            }

            return $@"private static readonly Dictionary<string, TokenType> TokenLiterals = new()
{{
{string.Join(",\n", literals)}
}};";
        }
    }
}