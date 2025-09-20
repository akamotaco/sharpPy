using System;

namespace SharpPy.Utils
{
    /// <summary>
    /// String utility functions for common operations
    /// </summary>
    public static class StringUtils
    {
        /// <summary>
        /// Removes quotes (both single and double) from the beginning and end of a string
        /// </summary>
        /// <param name="input">The input string</param>
        /// <returns>String with quotes trimmed from both ends</returns>
        public static string TrimQuotes(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return input.Trim('"', '\'');
        }

        /// <summary>
        /// Removes only double quotes from the beginning and end of a string
        /// </summary>
        /// <param name="input">The input string</param>
        /// <returns>String with double quotes trimmed from both ends</returns>
        public static string TrimDoubleQuotes(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return input.Trim('"');
        }

        /// <summary>
        /// Removes only single quotes from the beginning and end of a string
        /// </summary>
        /// <param name="input">The input string</param>
        /// <returns>String with single quotes trimmed from both ends</returns>
        public static string TrimSingleQuotes(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return input.Trim('\'');
        }

        /// <summary>
        /// Removes string literal quotes including triple quotes (CPython compatible)
        /// </summary>
        /// <param name="lexeme">The string lexeme with quotes</param>
        /// <returns>String with appropriate quotes removed</returns>
        public static string TrimStringLiteralQuotes(this string lexeme)
        {
            if (string.IsNullOrEmpty(lexeme))
                return lexeme;

            // Handle triple quotes first
            if ((lexeme.StartsWith("'''") && lexeme.EndsWith("'''")) ||
                (lexeme.StartsWith("\"\"\"") && lexeme.EndsWith("\"\"\"")))
            {
                return lexeme.Substring(3, lexeme.Length - 6);
            }

            // Handle single quotes
            if ((lexeme.StartsWith("'") && lexeme.EndsWith("'")) ||
                (lexeme.StartsWith("\"") && lexeme.EndsWith("\"")))
            {
                return lexeme.Substring(1, lexeme.Length - 2);
            }

            return lexeme;
        }
    }
}