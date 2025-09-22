using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// Tool for comparing original parser vs PEG parser results
    /// </summary>
    public static class ParserComparisonTool
    {
        public class ComparisonResult
        {
            public string FileName { get; set; }
            public bool OriginalParserSuccess { get; set; }
            public bool PegParserSuccess { get; set; }
            public List<Statement> OriginalAST { get; set; }
            public List<Statement> PegAST { get; set; }
            public List<string> Differences { get; set; } = new List<string>();
            public Exception OriginalError { get; set; }
            public Exception PegError { get; set; }

            public bool BothSucceeded => OriginalParserSuccess && PegParserSuccess;
            public bool ASTMatches => BothSucceeded && Differences.Count == 0;
        }

        /// <summary>
        /// Compare both parsers on a single file
        /// </summary>
        public static ComparisonResult CompareFile(string filename)
        {
            if (!File.Exists(filename))
            {
                throw new FileNotFoundException($"File not found: {filename}");
            }

            var source = File.ReadAllText(filename);
            return CompareSource(source, filename);
        }

        /// <summary>
        /// Compare both parsers on source code
        /// </summary>
        public static ComparisonResult CompareSource(string source, string filename = "<string>")
        {
            var result = new ComparisonResult
            {
                FileName = filename
            };

            // Test original parser
            try
            {
                result.OriginalAST = PyParser.ParseSource(source, filename);
                result.OriginalParserSuccess = true;
                Console.WriteLine($"✅ Original parser succeeded for {filename}");
            }
            catch (Exception ex)
            {
                result.OriginalError = ex;
                result.OriginalParserSuccess = false;
                Console.WriteLine($"❌ Original parser failed for {filename}: {ex.Message}");
            }

            // Test PEG parser
            try
            {
                result.PegAST = PyPegParser.ParseSource(source, filename);
                result.PegParserSuccess = true;
                Console.WriteLine($"✅ PEG parser succeeded for {filename}");
            }
            catch (Exception ex)
            {
                result.PegError = ex;
                result.PegParserSuccess = false;
                Console.WriteLine($"❌ PEG parser failed for {filename}: {ex.Message}");
            }

            // Compare ASTs if both succeeded
            if (result.BothSucceeded)
            {
                result.Differences = CompareASTs(result.OriginalAST, result.PegAST, filename);
                if (result.ASTMatches)
                {
                    Console.WriteLine($"🎯 AST comparison: MATCH for {filename}");
                }
                else
                {
                    Console.WriteLine($"⚠️  AST comparison: {result.Differences.Count} differences for {filename}");
                    foreach (var diff in result.Differences)
                    {
                        Console.WriteLine($"   - {diff}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Compare AST structures between two statement lists
        /// </summary>
        private static List<string> CompareASTs(List<Statement> original, List<Statement> peg, string context)
        {
            var differences = new List<string>();

            if (original == null && peg == null)
                return differences;

            if (original == null)
            {
                differences.Add("Original AST is null, PEG AST is not null");
                return differences;
            }

            if (peg == null)
            {
                differences.Add("PEG AST is null, Original AST is not null");
                return differences;
            }

            if (original.Count != peg.Count)
            {
                differences.Add($"Statement count: Original={original.Count}, PEG={peg.Count}");
            }

            int minCount = Math.Min(original.Count, peg.Count);
            for (int i = 0; i < minCount; i++)
            {
                var stmtDiffs = CompareStatements(original[i], peg[i], $"{context}:stmt[{i}]");
                differences.AddRange(stmtDiffs);
            }

            return differences;
        }

        /// <summary>
        /// Compare two statements
        /// </summary>
        private static List<string> CompareStatements(Statement original, Statement peg, string context)
        {
            var differences = new List<string>();

            if (original == null && peg == null)
                return differences;

            if (original == null)
            {
                differences.Add($"{context}: Original is null, PEG is {peg?.GetType().Name}");
                return differences;
            }

            if (peg == null)
            {
                differences.Add($"{context}: PEG is null, Original is {original?.GetType().Name}");
                return differences;
            }

            if (original.GetType() != peg.GetType())
            {
                differences.Add($"{context}: Type mismatch - Original: {original.GetType().Name}, PEG: {peg.GetType().Name}");
                return differences;
            }

            // Type-specific comparisons
            switch (original)
            {
                case AssignStatement origAssign when peg is AssignStatement pegAssign:
                    if (origAssign.VariableName != pegAssign.VariableName)
                    {
                        differences.Add($"{context}: Variable name - Original: '{origAssign.VariableName}', PEG: '{pegAssign.VariableName}'");
                    }

                    var exprDiffs = CompareExpressions(origAssign.Value, pegAssign.Value, $"{context}.value");
                    differences.AddRange(exprDiffs);
                    break;

                case ExpressionStatement origExpr when peg is ExpressionStatement pegExpr:
                    var exprDiffs2 = CompareExpressions(origExpr.Expression, pegExpr.Expression, $"{context}.expression");
                    differences.AddRange(exprDiffs2);
                    break;

                case FunctionDefStatement origFunc when peg is FunctionDefStatement pegFunc:
                    if (origFunc.Name != pegFunc.Name)
                    {
                        differences.Add($"{context}: Function name - Original: '{origFunc.Name}', PEG: '{pegFunc.Name}'");
                    }
                    break;

                default:
                    // For other statement types, just note that they exist
                    differences.Add($"{context}: {original.GetType().Name} comparison not implemented");
                    break;
            }

            return differences;
        }

        /// <summary>
        /// Compare two expressions
        /// </summary>
        private static List<string> CompareExpressions(Expression original, Expression peg, string context)
        {
            var differences = new List<string>();

            if (original == null && peg == null)
                return differences;

            if (original == null)
            {
                differences.Add($"{context}: Original is null, PEG is {peg?.GetType().Name}");
                return differences;
            }

            if (peg == null)
            {
                differences.Add($"{context}: PEG is null, Original is {original?.GetType().Name}");
                return differences;
            }

            if (original.GetType() != peg.GetType())
            {
                differences.Add($"{context}: Type mismatch - Original: {original.GetType().Name}, PEG: {peg.GetType().Name}");
                return differences;
            }

            // Type-specific comparisons
            switch (original)
            {
                case ConstantExpression origConst when peg is ConstantExpression pegConst:
                    // Extract actual values for comparison (strip display formatting)
                    var origValue = GetConstantValue(origConst.Value);
                    var pegValue = GetConstantValue(pegConst.Value);
                    if (origValue != pegValue)
                    {
                        differences.Add($"{context}: Constant value - Original: {origValue}, PEG: {pegValue}");
                    }
                    break;

                case NameExpression origName when peg is NameExpression pegName:
                    if (origName.Name != pegName.Name)
                    {
                        differences.Add($"{context}: Name - Original: '{origName.Name}', PEG: '{pegName.Name}'");
                    }
                    break;

                case BinaryOpExpression origBin when peg is BinaryOpExpression pegBin:
                    if (origBin.Operator != pegBin.Operator)
                    {
                        differences.Add($"{context}: Binary operator - Original: '{origBin.Operator}' (token), PEG: '{pegBin.Operator}' (CPython 3.12 style)");
                    }

                    var leftDiffs = CompareExpressions(origBin.Left, pegBin.Left, $"{context}.left");
                    var rightDiffs = CompareExpressions(origBin.Right, pegBin.Right, $"{context}.right");
                    differences.AddRange(leftDiffs);
                    differences.AddRange(rightDiffs);
                    break;

                case CompareExpression origComp when peg is CompareExpression pegComp:
                    if (origComp.Op != pegComp.Op)
                    {
                        differences.Add($"{context}: Compare operator - Original: '{origComp.Op}' (token), PEG: '{pegComp.Op}' (CPython 3.12 style)");
                    }

                    var leftDiffs2 = CompareExpressions(origComp.Left, pegComp.Left, $"{context}.left");
                    var rightDiffs2 = CompareExpressions(origComp.Right, pegComp.Right, $"{context}.right");
                    differences.AddRange(leftDiffs2);
                    differences.AddRange(rightDiffs2);
                    break;

                default:
                    // For other expression types, just note that they exist
                    differences.Add($"{context}: {original.GetType().Name} comparison not implemented");
                    break;
            }

            return differences;
        }

        /// <summary>
        /// Run comparison tests on multiple files
        /// </summary>
        public static List<ComparisonResult> RunBatchComparison(params string[] filenames)
        {
            var results = new List<ComparisonResult>();

            Console.WriteLine($"🔍 Running parser comparison on {filenames.Length} files...");
            Console.WriteLine();

            foreach (var filename in filenames)
            {
                try
                {
                    var result = CompareFile(filename);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 Failed to compare {filename}: {ex.Message}");
                }
                Console.WriteLine();
            }

            // Summary
            PrintSummary(results);

            return results;
        }

        /// <summary>
        /// Extract the actual value from a PyObject for comparison
        /// </summary>
        private static string GetConstantValue(object pyObject)
        {
            if (pyObject == null) return "null";

            // Handle PyString objects - extract the actual string value
            if (pyObject.GetType().Name == "PyString")
            {
                var valueProperty = pyObject.GetType().GetProperty("Value");
                if (valueProperty != null)
                {
                    return valueProperty.GetValue(pyObject)?.ToString() ?? "null";
                }
            }

            // Handle other PyObject types similarly if needed
            if (pyObject.GetType().Name.StartsWith("Py"))
            {
                var valueProperty = pyObject.GetType().GetProperty("Value");
                if (valueProperty != null)
                {
                    return valueProperty.GetValue(pyObject)?.ToString() ?? "null";
                }
            }

            return pyObject.ToString() ?? "null";
        }

        /// <summary>
        /// Print summary of comparison results
        /// </summary>
        private static void PrintSummary(List<ComparisonResult> results)
        {
            Console.WriteLine(new string('=', 60));
            Console.WriteLine("📊 COMPARISON SUMMARY");
            Console.WriteLine(new string('=', 60));

            var totalFiles = results.Count;
            var bothSucceeded = results.Count(r => r.BothSucceeded);
            var astMatches = results.Count(r => r.ASTMatches);
            var originalOnly = results.Count(r => r.OriginalParserSuccess && !r.PegParserSuccess);
            var pegOnly = results.Count(r => !r.OriginalParserSuccess && r.PegParserSuccess);
            var bothFailed = results.Count(r => !r.OriginalParserSuccess && !r.PegParserSuccess);

            Console.WriteLine($"Total files tested: {totalFiles}");
            Console.WriteLine($"Both parsers succeeded: {bothSucceeded} ({bothSucceeded * 100.0 / totalFiles:F1}%)");
            Console.WriteLine($"AST matches: {astMatches} ({astMatches * 100.0 / totalFiles:F1}%)");
            Console.WriteLine($"Original parser only: {originalOnly}");
            Console.WriteLine($"PEG parser only: {pegOnly}");
            Console.WriteLine($"Both failed: {bothFailed}");

            Console.WriteLine();
            Console.WriteLine("📋 Detailed Results:");
            foreach (var result in results)
            {
                var status = result.ASTMatches ? "✅ MATCH" :
                            result.BothSucceeded ? "⚠️  DIFF" :
                            result.OriginalParserSuccess ? "❌ PEG_FAIL" :
                            result.PegParserSuccess ? "❌ ORIG_FAIL" : "💥 BOTH_FAIL";

                Console.WriteLine($"  {status} {result.FileName}");
            }
        }

    }
}