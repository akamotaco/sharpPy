using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// Bridge class to integrate PEG parser with existing codebase
    /// Allows gradual migration from recursive descent to PEG parser
    /// </summary>
    public static class PyParserBridge
    {
        private static bool _usePegParser = true;
        private static bool _enableComparison = true;

        /// <summary>
        /// Enable or disable PEG parser usage
        /// </summary>
        public static void SetPegParserEnabled(bool enabled)
        {
            _usePegParser = enabled;
        }

        /// <summary>
        /// Enable or disable parser comparison mode
        /// </summary>
        public static void SetComparisonEnabled(bool enabled)
        {
            _enableComparison = enabled;
        }

        /// <summary>
        /// Main parsing entry point with fallback support
        /// </summary>
        public static List<Statement> ParseSource(string source, string filename = "<string>")
        {
            if (_usePegParser)
            {
                try
                {
                    var pegResult = PyPegParser.ParseSource(source, filename);

                    if (_enableComparison)
                    {
                        // Compare with original parser for validation
                        try
                        {
                            var originalResult = PyParser.ParseSource(source, filename);
                            CompareParseResults(originalResult, pegResult, filename);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Original parser failed for {filename}: {ex.Message}");
                        }
                    }

                    return pegResult;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"PEG parser failed for {filename}: {ex.Message}");

                    if (_enableComparison)
                    {
                        Console.WriteLine("Falling back to original parser...");
                        return PyParser.ParseSource(source, filename);
                    }

                    throw;
                }
            }
            else
            {
                return PyParser.ParseSource(source, filename);
            }
        }

        /// <summary>
        /// Compare parse results between original and PEG parsers
        /// </summary>
        private static void CompareParseResults(List<Statement> original, List<Statement> peg, string filename)
        {
            if (original == null && peg == null) return;

            if (original == null || peg == null)
            {
                Console.WriteLine($"Warning: Parser result mismatch in {filename} - one parser returned null");
                return;
            }

            if (original.Count != peg.Count)
            {
                Console.WriteLine($"Warning: Statement count mismatch in {filename} - Original: {original.Count}, PEG: {peg.Count}");
            }

            // Detailed comparison can be added here
            for (int i = 0; i < Math.Min(original.Count, peg.Count); i++)
            {
                CompareStatements(original[i], peg[i], $"{filename}:{i}");
            }
        }

        /// <summary>
        /// Compare individual statements
        /// </summary>
        private static void CompareStatements(Statement original, Statement peg, string context)
        {
            if (original == null && peg == null) return;

            if (original == null || peg == null)
            {
                Console.WriteLine($"Warning: Statement mismatch at {context} - one is null");
                return;
            }

            if (original.GetType() != peg.GetType())
            {
                Console.WriteLine($"Warning: Statement type mismatch at {context} - Original: {original.GetType().Name}, PEG: {peg.GetType().Name}");
                return;
            }

            // Type-specific comparisons can be added here
            switch (original)
            {
                case ExpressionStatement origExpr when peg is ExpressionStatement pegExpr:
                    CompareExpressions(origExpr.Expression, pegExpr.Expression, context);
                    break;

                case AssignStatement origAssign when peg is AssignStatement pegAssign:
                    CompareExpressions(origAssign.Value, pegAssign.Value, $"{context}.value");
                    if (origAssign.VariableName != pegAssign.VariableName)
                    {
                        Console.WriteLine($"Warning: Assignment variable name mismatch at {context}");
                    }
                    break;

                case FunctionDefStatement origFunc when peg is FunctionDefStatement pegFunc:
                    if (origFunc.Name != pegFunc.Name)
                    {
                        Console.WriteLine($"Warning: Function name mismatch at {context} - Original: {origFunc.Name}, PEG: {pegFunc.Name}");
                    }
                    break;

                // Add more specific comparisons as needed
            }
        }

        /// <summary>
        /// Compare expressions
        /// </summary>
        private static void CompareExpressions(Expression original, Expression peg, string context)
        {
            if (original == null && peg == null) return;

            if (original == null || peg == null)
            {
                Console.WriteLine($"Warning: Expression mismatch at {context} - one is null");
                return;
            }

            if (original.GetType() != peg.GetType())
            {
                Console.WriteLine($"Warning: Expression type mismatch at {context} - Original: {original.GetType().Name}, PEG: {peg.GetType().Name}");
                return;
            }

            // Type-specific expression comparisons can be added here
            switch (original)
            {
                case ConstantExpression origLit when peg is ConstantExpression pegLit:
                    if (!object.Equals(origLit.Value, pegLit.Value))
                    {
                        Console.WriteLine($"Warning: Literal value mismatch at {context} - Original: {origLit.Value}, PEG: {pegLit.Value}");
                    }
                    break;

                case NameExpression origName when peg is NameExpression pegName:
                    if (origName.Name != pegName.Name)
                    {
                        Console.WriteLine($"Warning: Name mismatch at {context} - Original: {origName.Name}, PEG: {pegName.Name}");
                    }
                    break;

                // Add more specific comparisons as needed
            }
        }
    }

    /// <summary>
    /// Configuration for PEG parser migration
    /// </summary>
    public static class PegParserConfig
    {
        /// <summary>
        /// Initialize PEG parser for testing
        /// </summary>
        public static void InitializeForTesting()
        {
            PyParserBridge.SetPegParserEnabled(true);
            PyParserBridge.SetComparisonEnabled(true);
        }

        /// <summary>
        /// Initialize PEG parser for production
        /// </summary>
        public static void InitializeForProduction()
        {
            PyParserBridge.SetPegParserEnabled(true);
            PyParserBridge.SetComparisonEnabled(false);
        }

        /// <summary>
        /// Disable PEG parser (use original)
        /// </summary>
        public static void UseOriginalParser()
        {
            PyParserBridge.SetPegParserEnabled(false);
            PyParserBridge.SetComparisonEnabled(false);
        }
    }
}