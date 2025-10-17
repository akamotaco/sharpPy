// Extension methods for GeneratedPtr type conversions
// Simplifies grammar action code by centralizing type casting logic
// C# Idiomatic approach - avoiding repetitive casting in grammar files

using System;

namespace SharpPy.Generated
{
    /// <summary>
    /// Extension methods for GeneratedPtr and derived types
    /// Provides fluent API for common type conversions in grammar action code
    /// </summary>
    public static class GeneratedPtrExtensions
    {
        // ============================================================
        // NAME Token → String Conversions
        // ============================================================

        /// <summary>
        /// Extracts string value from NAME token
        /// Grammar usage: a=NAME { ... a.GetNameValue() ... }
        /// Also handles nullable: a=[NAME] { ... a.GetNameValue() ... }
        /// </summary>
        public static string? GetNameValue(this GeneratedPtr? ptr)
        {
            return ((GeneratedTokenInfo?)ptr)?.Value;
        }

        // ============================================================
        // GeneratedName → Identifier Conversions
        // ============================================================

        /// <summary>
        /// Extracts identifier from GeneratedName
        /// Grammar usage: a=dotted_name { ... a.GetIdentifier() ... }
        /// Also handles nullable: a=[dotted_name] { ... a.GetIdentifier() ... }
        /// </summary>
        public static string? GetIdentifier(this GeneratedPtr? ptr)
        {
            return ((GeneratedName?)ptr)?.Id;
        }

        // ============================================================
        // TYPE_COMMENT Token → String Conversions
        // ============================================================

        /// <summary>
        /// Extracts string value from TYPE_COMMENT token
        /// Grammar usage: tc=[TYPE_COMMENT] { ... tc.GetCommentValue() ... }
        /// </summary>
        public static string? GetCommentValue(this GeneratedPtr? ptr)
        {
            return ((GeneratedTokenInfo?)ptr)?.Value;
        }

        // ============================================================
        // STRING Token → String Conversions
        // ============================================================

        /// <summary>
        /// Extracts string value from STRING token
        /// Grammar usage: s=STRING { ... s.GetStringValue() ... }
        /// Also handles nullable: s=[STRING] { ... s.GetStringValue() ... }
        /// </summary>
        public static string? GetStringValue(this GeneratedPtr? ptr)
        {
            return ((GeneratedTokenInfo?)ptr)?.Value;
        }

        // ============================================================
        // NUMBER Token → String Conversions
        // ============================================================

        /// <summary>
        /// Extracts string value from NUMBER token
        /// Grammar usage: n=NUMBER { ... n.GetNumberValue() ... }
        /// </summary>
        public static string GetNumberValue(this GeneratedPtr ptr)
        {
            if (ptr == null)
                throw new ArgumentNullException(nameof(ptr));

            return ((GeneratedTokenInfo)ptr).Value;
        }

        // ============================================================
        // Token Line Number Access
        // ============================================================

        /// <summary>
        /// Gets line number from token or AST node
        /// Grammar usage: a=NAME { ... a.GetLineNo() ... }
        /// Also handles nullable: a=[NAME] { ... a.GetLineNo() ... }
        /// Returns 0 for null pointers
        /// </summary>
        public static int GetLineNo(this GeneratedPtr? ptr)
        {
            if (ptr == null)
                return 0;

            if (ptr is GeneratedTokenInfo token)
                return token.Line;

            // For AST nodes with LineNo property
            var lineNoProp = ptr.GetType().GetProperty("LineNo");
            if (lineNoProp != null)
            {
                var value = lineNoProp.GetValue(ptr);
                if (value is int lineNo)
                    return lineNo;
            }

            throw new InvalidOperationException(
                $"Cannot get LineNo from {ptr.GetType().Name}. " +
                $"Type must be GeneratedTokenInfo or have LineNo property.");
        }

        // ============================================================
        // Token Column Number Access
        // ============================================================

        /// <summary>
        /// Gets column number from token
        /// Grammar usage: a=NAME { ... a.GetColumn() ... }
        /// Also handles nullable: a=[NAME] { ... a.GetColumn() ... }
        /// </summary>
        public static int? GetColumn(this GeneratedPtr? ptr)
        {
            if (ptr == null)
                return null;

            if (ptr is GeneratedTokenInfo token)
                return token.Column;

            throw new InvalidOperationException(
                $"Cannot get Column from {ptr.GetType().Name}. " +
                $"Type must be GeneratedTokenInfo.");
        }

        // ============================================================
        // Generic Token Value Access
        // ============================================================

        /// <summary>
        /// Generic token value extraction (for any token type)
        /// Attempts to extract Value property from GeneratedTokenInfo
        /// Also handles nullable tokens
        /// </summary>
        public static string? GetTokenValue(this GeneratedPtr? ptr)
        {
            return ((GeneratedTokenInfo?)ptr)?.Value;
        }
    }
}
