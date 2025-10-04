// PyParserBase.cs 코드 생성기
// CPython 3.12의 pegen.c + Python-ast.c 등가 (C# 버전)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpPy.PegGenerator.Asdl
{
    public class PyParserBaseGenerator
    {
        private readonly StringBuilder _sb = new();
        private int _indentLevel = 0;

        public string GenerateParserBase(AsdlModule module)
        {
            _sb.Clear();
            _indentLevel = 0;

            // 파일 헤더
            WriteLine("// Generated PyParserBase from Python.asdl");
            WriteLine("// CPython 3.12 compatible - Auto-generated, DO NOT EDIT");
            WriteLine();
            WriteLine("using System;");
            WriteLine("using System.Collections.Generic;");
            WriteLine("using System.Linq;");
            WriteLine("using SharpPy.Tokenizer;");
            WriteLine();
            WriteLine("namespace SharpPy.Generated");
            WriteLine("{");
            _indentLevel++;

            // PyParserBase abstract class
            GenerateParserBaseClass();

            // _PyAST_* helper functions for each constructor
            GenerateAstHelpers(module);

            // _PyPegen_* helper functions
            GeneratePegenHelpers(module);

            // ASTHelpers static class
            GenerateAstHelpersClass();

            _indentLevel--;
            WriteLine("}");

            return _sb.ToString();
        }

        private void GenerateParserBaseClass()
        {
            WriteLine("// ============================================================");
            WriteLine("// PyParserBase - Base parser class");
            WriteLine("// ============================================================");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// Base class for generated parser - provides common parsing logic");
            WriteLine("/// CPython 3.12: Parser/pegen.c equivalent");
            WriteLine("/// </summary>");
            WriteLine("public abstract class PyParserBase<TResult>");
            WriteLine("{");
            _indentLevel++;

            // Fields - CPython 3.12 Parser struct equivalent
            WriteLine("protected List<GeneratedTokenInfo> _tokens;");
            WriteLine("protected int _position = 0;  // CPython: mark");
            WriteLine("protected string _filename;");
            WriteLine("protected string? _pendingSyntaxError = null;");
            WriteLine("protected int _pendingErrorPosition = -1;");
            WriteLine("protected bool _callInvalidRules = true;");
            WriteLine("// CPython 3.12: Memo cache uses (position, rule_name) tuple keys");
            WriteLine("protected Dictionary<(int, string), GeneratedAstNode?> _memoCache = new();");
            WriteLine();
            WriteLine("// CPython Parser fields for recursion and error tracking");
            WriteLine("protected int _level = 0;  // Nesting depth for recursion limit");
            WriteLine("protected GeneratedTokenInfo? _knownErrToken = null;  // Error location tracking");
            WriteLine("protected int _errorIndicator = 0;  // Error state flag");
            WriteLine("protected const int MAX_RECURSION_DEPTH = 1000;  // Python's recursion limit");
            WriteLine();
            WriteLine("// Left-recursion handling (Warth et al. algorithm)");
            WriteLine("protected class LREntry");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public object? Result { get; set; }");
            WriteLine("public int EndPos { get; set; }");
            WriteLine("public bool IsGrowing { get; set; }");
            _indentLevel--;
            WriteLine("}");
            WriteLine("protected Dictionary<(int, string), LREntry> _lrCache = new();");
            WriteLine();
            WriteLine("// CPython 3.12: Token-based memoization");
            WriteLine("// Each Token owns its Memo list - no global cache needed");
            WriteLine("// MemoEntry is defined in GeneratedTokenInfo (PyTokenizer.cs)");
            WriteLine();

            // Constructor
            WriteLine("protected PyParserBase(List<GeneratedTokenInfo> tokens, string filename)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_tokens = tokens;");
            WriteLine("_filename = filename;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Current token property
            WriteLine("protected GeneratedTokenInfo? CurrentToken");
            WriteLine("{");
            _indentLevel++;
            WriteLine("get => _position < _tokens.Count ? _tokens[_position] : null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Abstract Parse method
            WriteLine("public abstract TResult Parse();");
            WriteLine();

            // ParseFile method (default implementation)
            WriteLine("protected virtual GeneratedModule ParseFile()");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Override in generated parser");
            WriteLine("throw new NotImplementedException(\"ParseFile must be overridden\");");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // ExpectToken method
            WriteLine("protected GeneratedTokenInfo? ExpectToken(GeneratedTokenType type)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var token = CurrentToken;");
            WriteLine("if (token != null && token.Type == type)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position++;");
            WriteLine("return token;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Expect method (with value)
            WriteLine("protected GeneratedTokenInfo? Expect(GeneratedTokenType type, string value)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var token = CurrentToken;");
            WriteLine("if (token != null && token.Type == type && token.Value == value)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position++;");
            WriteLine("return token;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // ExpectName method - expect NAME token
            WriteLine("protected GeneratedTokenInfo? ExpectName()");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var token = CurrentToken;");
            WriteLine("if (token != null && token.Type == GeneratedTokenType.NAME)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position++;");
            WriteLine("return token;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // TryLeftRecursive - Warth et al. algorithm for left recursion
            WriteLine("/// <summary>");
            WriteLine("/// Handle left-recursive rules using memoization");
            WriteLine("/// CPython 3.12: Implements Warth et al. 'Packrat Parsers Can Support Left Recursion'");
            WriteLine("/// Algorithm: SEED (FAIL) → BASE CASE → GROW → TERMINATE");
            WriteLine("/// </summary>");
            WriteLine("protected T? TryLeftRecursive<T>(string ruleName, Func<T?> ruleFunc) where T : class");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Check recursion depth");
            WriteLine("if (_level >= MAX_RECURSION_DEPTH)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("throw new StackOverflowException($\"Maximum recursion depth exceeded in rule {ruleName}\");");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("var key = (_position, ruleName);");
            WriteLine();
            WriteLine("// Recursive call - return current seed");
            WriteLine("if (_lrCache.TryGetValue(key, out var lrEntry) && lrEntry.IsGrowing)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position = lrEntry.EndPos;");
            WriteLine("return lrEntry.Result as T;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("_level++;");
            WriteLine("try");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// SEED PHASE: Start with FAIL seed");
            WriteLine("_lrCache[key] = new LREntry { Result = null, EndPos = _position, IsGrowing = true };");
            WriteLine();
            WriteLine("// First attempt - base case should execute");
            WriteLine("var result = ruleFunc();");
            WriteLine();
            WriteLine("if (result == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Real failure - base case also failed");
            WriteLine("_lrCache.Remove(key);");
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// GROWTH PHASE: Seed succeeded, now grow");
            WriteLine("T? lastResult = result;");
            WriteLine("int lastEndPos = _position;");
            WriteLine("_lrCache[key] = new LREntry { Result = result, EndPos = _position, IsGrowing = true };");
            WriteLine();
            WriteLine("while (true)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Reset to start position for next growth attempt");
            WriteLine("_position = key.Item1;");
            WriteLine("var newResult = ruleFunc();");
            WriteLine();
            WriteLine("// Termination: no progress made");
            WriteLine("if (newResult == null || _position <= lastEndPos)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position = lastEndPos;");
            WriteLine("_lrCache.Remove(key);");
            WriteLine("return lastResult;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// Grow: update seed with new result");
            WriteLine("lastResult = newResult;");
            WriteLine("lastEndPos = _position;");
            WriteLine("_lrCache[key] = new LREntry { Result = newResult, EndPos = _position, IsGrowing = true };");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine("finally");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_level--;");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // TryMemoized - CPython 3.12 pattern: _PyPegen_is_memoized + _PyPegen_update_memo
            WriteLine("/// <summary>");
            WriteLine("/// Handle memoized (non-left-recursive) rules with token-based caching");
            WriteLine("/// CPython 3.12: Implements _PyPegen_is_memoized() + _PyPegen_update_memo() pattern");
            WriteLine("/// Pattern:");
            WriteLine("///   1. Get current token: Token *t = p->tokens[p->mark]");
            WriteLine("///   2. Check cache: for (Memo *m = t->memo; m != NULL; m = m->next)");
            WriteLine("///   3. If hit: p->mark = m->mark; return m->node");
            WriteLine("///   4. If miss: parse, then update token's memo list");
            WriteLine("/// </summary>");
            WriteLine("protected T? TryMemoized<T>(string ruleName, Func<T?> ruleFunc) where T : class");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// STEP 1: Get current token (CPython: Token *t = p->tokens[p->mark])");
            WriteLine("if (_position >= _tokens.Count)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// ENDMARKER or beyond - don't memoize, just parse");
            WriteLine("return ruleFunc();");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("var token = _tokens[_position];");
            WriteLine("int startMark = _position;");
            WriteLine();
            WriteLine("// STEP 2: CHECK CACHE - _PyPegen_is_memoized(p, type, &res)");
            WriteLine("if (token.Memo != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: for (Memo *m = t->memo; m != NULL; m = m->next)");
            WriteLine("var cached = token.Memo.FirstOrDefault(m => m.RuleType == ruleName);");
            WriteLine("if (cached != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Cache HIT - restore mark and return cached result");
            WriteLine("// CPython: p->mark = m->mark; *(void**)(pres) = m->node; return 1;");
            WriteLine("_position = cached.Mark;");
            WriteLine("return cached.Node as T;");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// STEP 3: Cache MISS - parse the rule");
            WriteLine("var result = ruleFunc();");
            WriteLine("int endMark = _position;");
            WriteLine();
            WriteLine("// STEP 4: UPDATE CACHE - _PyPegen_update_memo(p, mark, type, node)");
            WriteLine("if (token.Memo == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("token.Memo = new List<MemoEntry>();");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython: Search for existing entry and update, or insert new");
            WriteLine("var existing = token.Memo.FirstOrDefault(m => m.RuleType == ruleName);");
            WriteLine("if (existing != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Update existing entry (shouldn't happen in normal flow, but CPython does this)");
            WriteLine("existing.Node = result;");
            WriteLine("existing.Mark = endMark;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Insert new memo entry");
            WriteLine("// CPython: _PyPegen_insert_memo() adds to front of linked list");
            WriteLine("token.Memo.Add(new MemoEntry");
            WriteLine("{");
            _indentLevel++;
            WriteLine("RuleType = ruleName,");
            WriteLine("Node = result,");
            WriteLine("Mark = endMark  // Position AFTER parsing (can be same as start if failed)");
            _indentLevel--;
            WriteLine("});");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("return result;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Token conversion helpers
            WriteLine("/// <summary>");
            WriteLine("/// Convert NAME token to AST Name expression");
            WriteLine("/// CPython 3.12: Used in grammar actions");
            WriteLine("/// </summary>");
            WriteLine("protected GeneratedName? NameToken(GeneratedTokenInfo? token)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (token == null) return null;");
            WriteLine("var name = new GeneratedName();");
            WriteLine("name.Id = token.Value ?? \"\";");
            WriteLine("name.Ctx = GeneratedLoad.Instance;  // Default context");
            WriteLine("name.LineNo = token.Line;");
            WriteLine("name.ColOffset = token.Column;");
            WriteLine("name.EndLineNo = token.EndLine;");
            WriteLine("name.EndColOffset = token.EndColumn;");
            WriteLine("return name;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // NumberToken helper
            WriteLine("/// <summary>");
            WriteLine("/// Convert NUMBER token to AST Constant expression");
            WriteLine("/// CPython 3.12: Numbers are represented as Constant nodes");
            WriteLine("/// </summary>");
            WriteLine("protected GeneratedConstant? NumberToken(GeneratedTokenInfo? token)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (token == null) return null;");
            WriteLine("var constant = new GeneratedConstant();");
            WriteLine("// Parse number value");
            WriteLine("if (token.Value.Contains(\".\") || token.Value.Contains(\"e\") || token.Value.Contains(\"E\"))");
            WriteLine("{");
            WriteLine("    constant.Value = new GeneratedPyConstantFloat(double.Parse(token.Value));");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    constant.Value = new GeneratedPyConstantInt(long.Parse(token.Value));");
            WriteLine("}");
            WriteLine("constant.LineNo = token.Line;");
            WriteLine("constant.ColOffset = token.Column;");
            WriteLine("constant.EndLineNo = token.EndLine;");
            WriteLine("constant.EndColOffset = token.EndColumn;");
            WriteLine("return constant;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // StringToken helper
            WriteLine("/// <summary>");
            WriteLine("/// Convert STRING token to AST Constant expression");
            WriteLine("/// CPython 3.12: Strings are represented as Constant nodes");
            WriteLine("/// </summary>");
            WriteLine("protected GeneratedConstant? StringToken(GeneratedTokenInfo? token)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (token == null) return null;");
            WriteLine("var constant = new GeneratedConstant();");
            WriteLine("// Parse string value");
            WriteLine("constant.Value = new GeneratedPyConstantString(token.Value);");
            WriteLine("constant.LineNo = token.Line;");
            WriteLine("constant.ColOffset = token.Column;");
            WriteLine("constant.EndLineNo = token.EndLine;");
            WriteLine("constant.EndColOffset = token.EndColumn;");
            WriteLine("return constant;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private void GenerateAstHelpers(AsdlModule module)
        {
            WriteLine("// ============================================================");
            WriteLine("// _PyAST_* Helper Functions");
            WriteLine("// ============================================================");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// AST node factory functions - CPython 3.12: Python-ast.c");
            WriteLine("/// </summary>");
            WriteLine("public static partial class AstFactory");
            WriteLine("{");
            _indentLevel++;

            foreach (var type in module.Types)
            {
                foreach (var constructor in type.Constructors)
                {
                    GenerateAstFactoryMethod(type.Name, constructor, type.Attributes);
                }
            }

            WriteLine();
            // CPython 3.12: Singleton constructors (Pass, Break, Continue) are auto-generated
            // by GenerateAstFactoryMethod when constructor.Fields.Count == 0

            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private void GenerateAstFactoryMethod(string typeName, AsdlConstructor constructor, AsdlAttributes? attributes)
        {
            var constructorName = ToPascalCase(constructor.Name);

            // CPython 3.12: Name collision avoidance in C# (must match AsdlCodeGenerator)
            // - stmt.Expr vs expr base type → ExprStmt
            // - type_ignore.TypeIgnore vs type_ignore base → TypeIgnoreNode
            // - operator.Mod vs mod base type → Mod_ (underscore suffix for consistency)
            if (typeName == "stmt" && constructorName == "Expr")
            {
                constructorName = "ExprStmt";
            }
            else if (typeName == "type_ignore" && constructorName == "TypeIgnore")
            {
                constructorName = "TypeIgnoreNode";
            }
            else if (typeName == "operator" && constructorName == "Mod")
            {
                constructorName = "Mod_";  // Modulo operator (%) - renamed to avoid collision with 'mod' base type
            }

            var className = $"Generated{constructorName}";
            var returnType = $"Generated{ToPascalCase(typeName)}";

            // Singleton 타입 (Pass, Break, Continue)
            if (constructor.Fields.Count == 0)
            {
                WriteLine($"public static {returnType} _PyAST_{constructor.Name}(int lineno, int col_offset, int? end_lineno, int? end_col_offset)");
                WriteLine("{");
                _indentLevel++;
                WriteLine($"var node = {className}.Instance;");
                WriteLine("node.LineNo = lineno;");
                WriteLine("node.ColOffset = col_offset;");
                WriteLine("node.EndLineNo = end_lineno ?? 0;");
                WriteLine("node.EndColOffset = end_col_offset ?? 0;");
                WriteLine("return node;");
                _indentLevel--;
                WriteLine("}");
                WriteLine();
                return;
            }

            // 필드가 있는 타입
            var parameters = new List<string>();
            foreach (var field in constructor.Fields)
            {
                var csharpType = MapAsdlTypeToCSharp(field.Type, field.Cardinality);
                var paramName = field.Name;
                parameters.Add($"{csharpType} {paramName}");
            }

            // EXTRA parameters (lineno, col_offset, etc.) - only if attributes exist
            // CPython 3.12: type_ignore has no attributes, so don't add position params
            if (attributes != null && attributes.Fields.Count > 0)
            {
                parameters.Add("int lineno");
                parameters.Add("int col_offset");
                parameters.Add("int? end_lineno");
                parameters.Add("int? end_col_offset");
            }

            WriteLine($"public static {returnType} _PyAST_{constructor.Name}({string.Join(", ", parameters)})");
            WriteLine("{");
            _indentLevel++;
            WriteLine($"var node = new {className}();");

            // Set fields
            foreach (var field in constructor.Fields)
            {
                var propName = ToPascalCase(field.Name);
                WriteLine($"node.{propName} = {field.Name};");
            }

            // Set location attributes (only if attributes exist)
            if (attributes != null && attributes.Fields.Count > 0)
            {
                WriteLine("node.LineNo = lineno;");
                WriteLine("node.ColOffset = col_offset;");
                WriteLine("node.EndLineNo = end_lineno ?? 0;");
                WriteLine("node.EndColOffset = end_col_offset ?? 0;");
            }
            WriteLine("return node;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Overload without optional parameters (for convenience)
            if (constructor.Fields.Any(f => f.Cardinality == FieldCardinality.Optional))
            {
                var requiredParams = new List<string>();
                foreach (var field in constructor.Fields.Where(f => f.Cardinality != FieldCardinality.Optional))
                {
                    var csharpType = MapAsdlTypeToCSharp(field.Type, field.Cardinality);
                    requiredParams.Add($"{csharpType} {field.Name}");
                }

                // Only add position params if attributes exist
                if (attributes != null && attributes.Fields.Count > 0)
                {
                    requiredParams.Add("int lineno");
                    requiredParams.Add("int col_offset");
                    requiredParams.Add("int? end_lineno");
                    requiredParams.Add("int? end_col_offset");
                }

                WriteLine($"public static {returnType} _PyAST_{constructor.Name}({string.Join(", ", requiredParams)})");
                WriteLine("{");
                _indentLevel++;

                var allParams = new List<string>();
                foreach (var field in constructor.Fields)
                {
                    if (field.Cardinality == FieldCardinality.Optional)
                    {
                        allParams.Add("null");
                    }
                    else
                    {
                        allParams.Add(field.Name);
                    }
                }

                // Only add position params if attributes exist
                if (attributes != null && attributes.Fields.Count > 0)
                {
                    allParams.Add("lineno");
                    allParams.Add("col_offset");
                    allParams.Add("end_lineno");
                    allParams.Add("end_col_offset");
                }

                WriteLine($"return _PyAST_{constructor.Name}({string.Join(", ", allParams)});");
                _indentLevel--;
                WriteLine("}");
                WriteLine();
            }
        }

        private void GeneratePegenHelpers(AsdlModule module)
        {
            WriteLine("// ============================================================");
            WriteLine("// _PyPegen_* Helper Functions");
            WriteLine("// ============================================================");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// PEG parser helper functions - CPython 3.12: Parser/pegen.c");
            WriteLine("/// </summary>");
            WriteLine("public static partial class PegenHelpers");
            WriteLine("{");
            _indentLevel++;

            // seq_flatten
            WriteLine("// CPython: _PyPegen_seq_flatten");
            WriteLine("// Flatten list of sequences into single sequence");
            WriteLine("// CPython 3.12: Returns NULL if total size is 0 (assert fails in CPython)");
            WriteLine("public static GeneratedStmtSeq? _PyPegen_seq_flatten(System.Collections.Generic.List<GeneratedStmtSeq> sequences)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: Calculate flattened size");
            WriteLine("int totalSize = 0;");
            WriteLine("foreach (var seq in sequences)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("totalSize += seq.Count;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython: assert(flattened_seq_size > 0)");
            WriteLine("if (totalSize == 0)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return null;  // CPython would assert fail");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython: Allocate and flatten");
            WriteLine("var result = new GeneratedStmtSeq(totalSize);");
            WriteLine("foreach (var seq in sequences)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("result.AddRange(seq);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return result;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // singleton_seq
            WriteLine("// CPython: _PyPegen_singleton_seq");
            WriteLine("public static GeneratedStmtSeq _PyPegen_singleton_seq(GeneratedStmt item)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var seq = new GeneratedStmtSeq(1);");
            WriteLine("seq.Add(item);");
            WriteLine("return seq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedExprSeq _PyPegen_singleton_seq(GeneratedExpr item)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var seq = new GeneratedExprSeq(1);");
            WriteLine("seq.Add(item);");
            WriteLine("return seq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedAliasSeq _PyPegen_singleton_seq(GeneratedAlias item)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var seq = new GeneratedAliasSeq(1);");
            WriteLine("seq.Add(item);");
            WriteLine("return seq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// For grammar helper types like SlashWithDefault");
            WriteLine("public static GeneratedMixedSeq _PyPegen_singleton_seq(GeneratedSlashWithDefault item)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var seq = new GeneratedMixedSeq(1);");
            WriteLine("seq.Add(item);");
            WriteLine("return seq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();


            // seq_insert_in_front
            WriteLine("// CPython: _PyPegen_seq_insert_in_front");
            WriteLine("public static GeneratedExprSeq _PyPegen_seq_insert_in_front(GeneratedExpr item, GeneratedExprSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var newSeq = new GeneratedExprSeq(seq.Count + 1);");
            WriteLine("newSeq.Add(item);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedPatternSeq _PyPegen_seq_insert_in_front(GeneratedPattern item, GeneratedPatternSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var newSeq = new GeneratedPatternSeq(seq.Count + 1);");
            WriteLine("newSeq.Add(item);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// Generic version for MixedSeq - handles any AST node type");
            WriteLine("public static GeneratedMixedSeq _PyPegen_seq_insert_in_front(GeneratedExpr item, GeneratedMixedSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var newSeq = new GeneratedMixedSeq(seq.Count + 1);");
            WriteLine("newSeq.Add(item);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedMixedSeq _PyPegen_seq_insert_in_front(GeneratedPattern item, GeneratedMixedSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var newSeq = new GeneratedMixedSeq(seq.Count + 1);");
            WriteLine("newSeq.Add(item);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedMixedSeq _PyPegen_seq_insert_in_front(GeneratedAstNode item, GeneratedMixedSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var newSeq = new GeneratedMixedSeq(seq.Count + 1);");
            WriteLine("newSeq.Add(item);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // seq_count_dots
            WriteLine("// CPython: _PyPegen_seq_count_dots");
            WriteLine("public static int _PyPegen_seq_count_dots(GeneratedIdentifierSeq? seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return seq?.Count ?? 0;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // seq_count_dots - overload for token list (from ('.' | '...')+ pattern)
            WriteLine("// CPython: _PyPegen_seq_count_dots (token list overload)");
            WriteLine("public static int _PyPegen_seq_count_dots(System.Collections.Generic.List<GeneratedTokenInfo>? tokens)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (tokens == null) return 0;");
            WriteLine("int count = 0;");
            WriteLine("foreach (var token in tokens)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// '.' counts as 1, '...' (ELLIPSIS) counts as 3");
            WriteLine("if (token.Value == \"...\")");
            WriteLine("    count += 3;");
            WriteLine("else");
            WriteLine("    count += 1;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return count;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // map_names_to_ids - from token list
            WriteLine("// CPython: _PyPegen_map_names_to_ids");
            WriteLine("// Extract identifier strings from NAME tokens");
            WriteLine("public static GeneratedIdentifierSeq _PyPegen_map_names_to_ids(System.Collections.Generic.List<GeneratedTokenInfo> tokens)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var ids = new GeneratedIdentifierSeq(tokens.Count);");
            WriteLine("foreach (var token in tokens)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("ids.Add(token.Value ?? string.Empty);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return ids;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // map_names_to_ids - from expr seq
            WriteLine("// CPython: _PyPegen_map_names_to_ids");
            WriteLine("// Extract identifier strings from Name expressions");
            WriteLine("public static GeneratedIdentifierSeq _PyPegen_map_names_to_ids(GeneratedExprSeq names)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var ids = new GeneratedIdentifierSeq(names.Count);");
            WriteLine("foreach (var name in names)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (name is GeneratedName nameExpr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("ids.Add(nameExpr.Id);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return ids;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // alias_for_star
            WriteLine("// CPython: _PyPegen_alias_for_star");
            WriteLine("public static GeneratedAlias _PyPegen_alias_for_star(int lineno, int col_offset, int? end_lineno, int? end_col_offset)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var alias = new GeneratedAlias();");
            WriteLine("alias.Name = \"*\";");
            WriteLine("alias.Asname = null;");
            WriteLine("alias.LineNo = lineno;");
            WriteLine("alias.ColOffset = col_offset;");
            WriteLine("alias.EndLineNo = end_lineno ?? 0;");
            WriteLine("alias.EndColOffset = end_col_offset ?? 0;");
            WriteLine("return alias;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // empty_arguments
            WriteLine("// CPython: _PyPegen_empty_arguments");
            WriteLine("public static GeneratedArguments _PyPegen_empty_arguments()");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var args = new GeneratedArguments();");
            WriteLine("args.Posonlyargs = GeneratedArgSeq.Empty;");
            WriteLine("args.Args = GeneratedArgSeq.Empty;");
            WriteLine("args.Kwonlyargs = GeneratedArgSeq.Empty;");
            WriteLine("args.KwDefaults = GeneratedExprSeq.Empty;");
            WriteLine("args.Defaults = GeneratedExprSeq.Empty;");
            WriteLine("return args;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // set_expr_context
            WriteLine("// CPython: _PyPegen_set_expr_context");
            WriteLine("public static GeneratedExpr _PyPegen_set_expr_context(GeneratedExpr expr, GeneratedExprContext ctx)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Update expr context based on type");
            WriteLine("switch (expr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("case GeneratedName name:");
            _indentLevel++;
            WriteLine("name.Ctx = ctx;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedAttribute attr:");
            _indentLevel++;
            WriteLine("attr.Ctx = ctx;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedSubscript subscript:");
            _indentLevel++;
            WriteLine("subscript.Ctx = ctx;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedList list:");
            _indentLevel++;
            WriteLine("list.Ctx = ctx;");
            WriteLine("foreach (var elt in list.Elts)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_PyPegen_set_expr_context(elt, ctx);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedTuple tuple:");
            _indentLevel++;
            WriteLine("tuple.Ctx = ctx;");
            WriteLine("foreach (var elt in tuple.Elts)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_PyPegen_set_expr_context(elt, ctx);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedStarred starred:");
            _indentLevel++;
            WriteLine("starred.Ctx = ctx;");
            WriteLine("_PyPegen_set_expr_context(starred.Value, ctx);");
            WriteLine("break;");
            _indentLevel--;
            _indentLevel--;
            WriteLine("}");
            WriteLine("return expr;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // make_module
            WriteLine("// CPython: _PyPegen_make_module");
            WriteLine("public static GeneratedModule _PyPegen_make_module(GeneratedStmtSeq? body)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var module = new GeneratedModule();");
            WriteLine("module.Body = body ?? GeneratedStmtSeq.Empty;");
            WriteLine("module.TypeIgnores = GeneratedTypeIgnoreSeq.Empty;");
            WriteLine("return module;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // seq_append_to_end
            WriteLine("// CPython: _PyPegen_seq_append_to_end");
            WriteLine("public static GeneratedExprSeq _PyPegen_seq_append_to_end(GeneratedExprSeq seq, GeneratedExpr item)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var newSeq = new GeneratedExprSeq(seq.Count + 1);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("newSeq.Add(item);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Constant singletons - AST layer (not runtime PyObject)
            WriteLine("// CPython: Py_None, Py_True, Py_False, Py_Ellipsis");
            WriteLine("// Note: These are constant VALUES for AST construction, not runtime PyObjects");
            WriteLine("public static GeneratedPyConstant Py_None => GeneratedPyConstant.None;");
            WriteLine("public static GeneratedPyConstant Py_True => GeneratedPyConstant.True;");
            WriteLine("public static GeneratedPyConstant Py_False => GeneratedPyConstant.False;");
            WriteLine("public static GeneratedPyConstant Py_Ellipsis => GeneratedPyConstant.Ellipsis;");
            WriteLine();

            // dummy_name
            WriteLine("// CPython: _PyPegen_dummy_name");
            WriteLine("public static GeneratedName _PyPegen_dummy_name()");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return new GeneratedName { Id = \"_\", Ctx = GeneratedStore.Instance };");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // get_cmpops - extract comparison operators from KeywordOrStarred pairs
            WriteLine("// CPython: _PyPegen_get_cmpops");
            WriteLine("// Extract comparison operators from (cmpop, expr) pairs");
            WriteLine("public static GeneratedCmpopSeq _PyPegen_get_cmpops(GeneratedMixedSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var ops = new GeneratedCmpopSeq();");
            WriteLine("// TODO: Extract cmpops from pairs - needs pair structure definition");
            WriteLine("return ops;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// Overload for AstNodeSeq");
            WriteLine("public static GeneratedCmpopSeq _PyPegen_get_cmpops(GeneratedAstNodeSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var mixed = new GeneratedMixedSeq();");
            WriteLine("mixed.AddRange(pairs);");
            WriteLine("return _PyPegen_get_cmpops(mixed);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // get_exprs - extract expressions from KeywordOrStarred pairs
            WriteLine("// CPython: _PyPegen_get_exprs");
            WriteLine("// Extract expressions from (cmpop, expr) pairs");
            WriteLine("public static GeneratedExprSeq _PyPegen_get_exprs(GeneratedMixedSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var exprs = new GeneratedExprSeq();");
            WriteLine("// TODO: Extract exprs from pairs - needs pair structure definition");
            WriteLine("return exprs;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// Overload for AstNodeSeq");
            WriteLine("public static GeneratedExprSeq _PyPegen_get_exprs(GeneratedAstNodeSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var mixed = new GeneratedMixedSeq();");
            WriteLine("mixed.AddRange(pairs);");
            WriteLine("return _PyPegen_get_exprs(mixed);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // key_value_pair - create key-value pair
            WriteLine("// CPython: _PyPegen_key_value_pair");
            WriteLine("// Create a key-value pair for dictionary literals");
            WriteLine("public static GeneratedKeyValuePair _PyPegen_key_value_pair(GeneratedExpr? key, GeneratedExpr value)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return new GeneratedKeyValuePair { Key = key ?? value, Value = value };");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // get_keys - extract keys from key-value pairs
            WriteLine("// CPython: _PyPegen_get_keys");
            WriteLine("// Extract keys from (key, value) pairs");
            WriteLine("public static GeneratedExprSeq _PyPegen_get_keys(GeneratedMixedSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var keys = new GeneratedExprSeq();");
            WriteLine("foreach (var pair in pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (pair is GeneratedKeyValuePair kvp)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("keys.Add(kvp.Key);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return keys;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // get_values - extract values from key-value pairs
            WriteLine("// CPython: _PyPegen_get_values");
            WriteLine("// Extract values from (key, value) pairs");
            WriteLine("public static GeneratedExprSeq _PyPegen_get_values(GeneratedMixedSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var values = new GeneratedExprSeq();");
            WriteLine("foreach (var pair in pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (pair is GeneratedKeyValuePair kvp)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("values.Add(kvp.Value);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return values;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // seq_extract_starred_exprs - extract starred expressions
            WriteLine("// CPython: _PyPegen_seq_extract_starred_exprs");
            WriteLine("// Extract starred expressions from KeywordOrStarred sequence");
            WriteLine("public static GeneratedExprSeq _PyPegen_seq_extract_starred_exprs(GeneratedKeywordOrStarredSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var exprs = new GeneratedExprSeq();");
            WriteLine("foreach (var item in seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (item.Starred != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("exprs.Add(item.Starred);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return exprs;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // seq_delete_starred_exprs - extract keywords (non-starred)
            WriteLine("// CPython: _PyPegen_seq_delete_starred_exprs");
            WriteLine("// Extract keywords (non-starred items) from KeywordOrStarred sequence");
            WriteLine("public static GeneratedKeywordSeq _PyPegen_seq_delete_starred_exprs(GeneratedKeywordOrStarredSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var keywords = new GeneratedKeywordSeq();");
            WriteLine("foreach (var item in seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (item.Keyword != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("keywords.Add(item.Keyword);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return keywords;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Overload for MixedSeq - cast to KeywordOrStarredSeq
            WriteLine("// Overload for MixedSeq");
            WriteLine("public static GeneratedExprSeq _PyPegen_seq_extract_starred_exprs(GeneratedMixedSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var kwSeq = new GeneratedKeywordOrStarredSeq();");
            WriteLine("kwSeq.AddRange(seq.Cast<GeneratedKeywordOrStarred>());");
            WriteLine("return _PyPegen_seq_extract_starred_exprs(kwSeq);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedKeywordSeq _PyPegen_seq_delete_starred_exprs(GeneratedMixedSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var kwSeq = new GeneratedKeywordOrStarredSeq();");
            WriteLine("kwSeq.AddRange(seq.Cast<GeneratedKeywordOrStarred>());");
            WriteLine("return _PyPegen_seq_delete_starred_exprs(kwSeq);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Conversion helpers for Seq types
            WriteLine("// Conversion: GeneratedAstNodeSeq to GeneratedMixedSeq");
            WriteLine("public static GeneratedMixedSeq ToMixedSeq(GeneratedAstNodeSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var mixed = new GeneratedMixedSeq();");
            WriteLine("foreach (var item in seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("mixed.Add(item);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return mixed;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// Conversion: GeneratedKeywordOrStarredSeq to GeneratedMixedSeq");
            WriteLine("public static GeneratedMixedSeq ToMixedSeq(GeneratedKeywordOrStarredSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var mixed = new GeneratedMixedSeq();");
            WriteLine("foreach (var item in seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("mixed.Add(item);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return mixed;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private void GenerateAstHelpersClass()
        {
            WriteLine("// ============================================================");
            WriteLine("// ASTHelpers - Utility functions");
            WriteLine("// ============================================================");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// Helper functions for AST manipulation");
            WriteLine("/// </summary>");
            WriteLine("public static class ASTHelpers");
            WriteLine("{");
            _indentLevel++;

            WriteLine("public static string ExtractStringValue(GeneratedExpr expr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (expr is GeneratedName name)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return name.Id;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("throw new InvalidOperationException(\"Expected Name expression\");");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedOperator ExtractOpKind(GeneratedAstNode op)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Extract operator kind from AST node");
            WriteLine("if (op is GeneratedOperator opNode) return opNode;");
            WriteLine("// TODO: Implement more comprehensive operator extraction");
            WriteLine("return GeneratedAdd.Instance;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedExprSeq ExtractCallArgs(GeneratedExpr call)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (call is GeneratedCall callExpr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return callExpr.Args;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return GeneratedExprSeq.Empty;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedKeywordSeq ExtractCallKeywords(GeneratedExpr call)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (call is GeneratedCall callExpr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return callExpr.Keywords;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return GeneratedKeywordSeq.Empty;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private string MapAsdlTypeToCSharp(string asdlType, FieldCardinality cardinality)
        {
            var baseType = AsdlBuiltins.IsBuiltin(asdlType)
                ? MapBuiltinType(asdlType)
                : $"Generated{ToPascalCase(asdlType)}";

            return cardinality switch
            {
                FieldCardinality.Optional => $"{baseType}?",
                FieldCardinality.Sequence => $"Generated{ToPascalCase(asdlType)}Seq",
                _ => baseType
            };
        }

        private string MapBuiltinType(string asdlType)
        {
            return asdlType switch
            {
                "identifier" => "string",
                "int" => "int",
                "string" => "string",
                "constant" => "GeneratedPyConstant",
                "singleton" => "bool?",
                _ => "object"
            };
        }

        private string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            // CPython 3.12: snake_case → PascalCase
            // 각 단어의 첫 글자만 대문자로, 나머지는 그대로 유지
            var parts = name.Split('_');
            return string.Join("", parts.Select(p =>
                p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1) : ""
            ));
        }

        private void WriteLine(string line = "")
        {
            if (string.IsNullOrEmpty(line))
            {
                _sb.AppendLine();
            }
            else
            {
                _sb.AppendLine(new string(' ', _indentLevel * 4) + line);
            }
        }
    }
}
