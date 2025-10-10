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
            WriteLine("using static SharpPy.GeneratedParserBridge;");
            WriteLine();
            WriteLine("// CPython 3.12: Type aliases for grammar compatibility");
            WriteLine("// Allows python_cs.gram to use C type names directly");
            WriteLine("using mod_ty = SharpPy.Generated.GeneratedMod;");
            WriteLine("using stmt_ty = SharpPy.Generated.GeneratedStmt;");
            WriteLine("using expr_ty = SharpPy.Generated.GeneratedExpr;");
            WriteLine("using alias_ty = SharpPy.Generated.GeneratedAlias;");
            WriteLine("using arguments_ty = SharpPy.Generated.GeneratedArguments;");
            WriteLine("using asdl_stmt_seq = SharpPy.Generated.GeneratedStmtSeq;");
            WriteLine("using asdl_expr_seq = SharpPy.Generated.GeneratedExprSeq;");
            WriteLine("using asdl_arg_seq = SharpPy.Generated.GeneratedArgSeq;");
            WriteLine("using asdl_identifier_seq = SharpPy.Generated.GeneratedIdentifierSeq;");
            WriteLine("using asdl_pattern_seq = SharpPy.Generated.GeneratedPatternSeq;");
            WriteLine("using asdl_int_seq = SharpPy.Generated.GeneratedCmpopSeq;  // CPython: int sequence used for comparison operators");
            WriteLine("using asdl_keyword_seq = SharpPy.Generated.GeneratedKeywordSeq;");
            WriteLine("using asdl_seq = SharpPy.Generated.GeneratedSeq;");
            WriteLine("using keyword_ty = SharpPy.Generated.GeneratedKeyword;");
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

            // Parser helper types (not in ASDL, defined in pegen)
            GenerateParserHelperTypes();

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
            WriteLine("protected string? _source = null;  // CPython 3.12: Source code for error reporting");
            WriteLine("protected string[]? _sourceLines = null;  // CPython 3.12: Source lines for error reporting");
            WriteLine("protected string? _pendingSyntaxError = null;");
            WriteLine("protected int _pendingErrorPosition = -1;");
            WriteLine("protected bool _callInvalidRules = true;");
            WriteLine("// CPython 3.12: Memo cache uses (position, rule_name) tuple keys");
            WriteLine("protected Dictionary<(int, string), GeneratedAstNode> _memoCache = new();");
            WriteLine();
            WriteLine("// CPython Parser fields for recursion and error tracking");
            WriteLine("protected int _level = 0;  // Nesting depth for recursion limit");
            WriteLine("protected GeneratedTokenInfo _knownErrToken = null;  // Error location tracking");
            WriteLine("protected int _errorIndicator = 0;  // Error state flag");
            WriteLine("protected const int MAX_RECURSION_DEPTH = 1000;  // Python's recursion limit");
            WriteLine();
            WriteLine("// Left-recursion handling (Warth et al. algorithm)");
            WriteLine("protected class LREntry");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedPtr Result { get; set; }");
            WriteLine("public int EndPos { get; set; }");
            WriteLine("public bool IsGrowing { get; set; }");
            _indentLevel--;
            WriteLine("}");
            WriteLine("protected Dictionary<(int, string), LREntry> _lrCache = new();");
            WriteLine();
            WriteLine("// CPython 3.12: ResultTokenWithMetadata for f-string conversions/formats");
            WriteLine("// Must inherit from GeneratedPtr to be used as optional rule result");
            WriteLine("protected class ResultTokenWithMetadata : GeneratedPtr");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedTokenInfo Token { get; set; }");
            WriteLine("public object Metadata { get; set; }");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: Token-based memoization");
            WriteLine("// Each Token owns its Memo list - no global cache needed");
            WriteLine("// MemoEntry is defined in GeneratedTokenInfo (PyTokenizer.cs)");
            WriteLine();

            // Constructor
            WriteLine("protected PyParserBase(List<GeneratedTokenInfo> tokens, string filename)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython 3.12: Filter out COMMENT, NL, TYPE_COMMENT tokens before parsing");
            WriteLine("_tokens = tokens.Where(t => ");
            WriteLine("    t.Type != GeneratedTokenType.COMMENT &&");
            WriteLine("    t.Type != GeneratedTokenType.NL &&");
            WriteLine("    t.Type != GeneratedTokenType.TYPE_COMMENT).ToList();");
            WriteLine("_filename = filename;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Constructor with source code (CPython 3.12)
            WriteLine("// CPython 3.12: Constructor with source code for error reporting");
            WriteLine("protected PyParserBase(List<GeneratedTokenInfo> tokens, string filename, string source)");
            WriteLine("    : this(tokens, filename)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_source = source;");
            WriteLine("// CPython 3.12: Use universal newlines (like Python's str.splitlines)");
            WriteLine("_sourceLines = source.Replace(\"\\r\\n\", \"\\n\").Replace(\"\\r\", \"\\n\").Split('\\n');");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Current token property
            WriteLine("protected GeneratedTokenInfo CurrentToken");
            WriteLine("{");
            _indentLevel++;
            WriteLine("get => _position < _tokens.Count ? _tokens[_position] : null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Abstract Parse method
            WriteLine("public abstract TResult Parse();");
            WriteLine();

            // CPython 3.12: Abstract keyword checker (implemented in generated parser)
            WriteLine("// CPython 3.12: _get_keyword_or_name_type - Must be implemented by generated parser");
            WriteLine("protected abstract int GetKeywordOrNameType(string name, int nameLen);");
            WriteLine();

            // CPython 3.12: Get source line for error reporting
            WriteLine("// CPython 3.12: Get source line for error reporting (like CPython's _PyPegen_get_source_line)");
            WriteLine("protected string? GetSourceLine(int lineNumber)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (_sourceLines == null || lineNumber <= 0 || lineNumber > _sourceLines.Length)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return _sourceLines[lineNumber - 1];  // Convert 1-based to 0-based index");
            _indentLevel--;
            WriteLine("}");
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
            // CPython 3.12: _PyPegen_expect_token equivalent
            WriteLine("protected GeneratedTokenInfo ExpectToken(GeneratedTokenType type)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var token = CurrentToken;");
            WriteLine("if (token == null) return null;");
            WriteLine();
            WriteLine("// CPython 3.12: If token is NAME, check if it's a keyword");
            WriteLine("// This implements initialize_token + _get_keyword_or_name_type logic");
            WriteLine("int tokenTypeInt = (int)token.Type;");
            WriteLine("if (token.Type == GeneratedTokenType.NAME)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("tokenTypeInt = GetKeywordOrNameType(token.Value, token.Value.Length);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[ExpectToken] type={(int)type}, tokenTypeInt={tokenTypeInt}, match={tokenTypeInt == (int)type}\");");
            WriteLine("#endif");
            WriteLine("if (tokenTypeInt == (int)type)");
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
            WriteLine("protected GeneratedTokenInfo Expect(GeneratedTokenType type, string value)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var token = CurrentToken;");
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[Expect] type={(int)type}, value='{value}', token={(token != null ? $\"{(int)token.Type}:'{token.Value}'\" : \"null\")}, match={token != null && token.Type == type && token.Value == value}\");");
            WriteLine("#endif");
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
            WriteLine("protected GeneratedTokenInfo ExpectName()");
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

            // ExpectSoftKeyword method - CPython 3.12: _PyPegen_expect_soft_keyword
            WriteLine("/// <summary>");
            WriteLine("/// CPython 3.12: _PyPegen_expect_soft_keyword");
            WriteLine("/// Soft keywords: _, case, match, type");
            WriteLine("/// These are NAME tokens that are treated as keywords only in specific contexts");
            WriteLine("/// </summary>");
            WriteLine("protected GeneratedTokenInfo ExpectSoftKeyword(string keyword)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var token = CurrentToken;");
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[ExpectSoftKeyword] keyword='{keyword}', token={(token != null ? $\\\"{(int)token.Type}:'{token.Value}'\\\" : \\\"null\\\")}, match={token != null && token.Type == GeneratedTokenType.NAME && token.Value == keyword}\");");
            WriteLine("#endif");
            WriteLine("// CPython: t->type != NAME → return NULL");
            WriteLine("if (token == null || token.Type != GeneratedTokenType.NAME)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("// CPython: strcmp(keyword, the_token) == 0");
            WriteLine("if (token.Value == keyword)");
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

            // ExpectForcedToken method - CPython 3.12: _PyPegen_expect_forced_token
            WriteLine("/// <summary>");
            WriteLine("/// CPython 3.12: _PyPegen_expect_forced_token");
            WriteLine("/// Token *_PyPegen_expect_forced_token(Parser *p, int type, const char* expected)");
            WriteLine("/// Forced token must match or raise syntax error immediately");
            WriteLine("/// </summary>");
            WriteLine("protected GeneratedTokenInfo ExpectForcedToken(GeneratedTokenType type, string expected)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (_pendingSyntaxError != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("var token = CurrentToken;");
            WriteLine("if (token == null || token.Type != type || token.Value != expected)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: RAISE_SYNTAX_ERROR_KNOWN_LOCATION(t, \"expected '%s'\", expected)");
            WriteLine("_pendingSyntaxError = $\"expected '{expected}'\";");
            WriteLine("_pendingErrorPosition = _position;");
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("_position++;");
            WriteLine("return token;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // ExpectForcedResult method - CPython 3.12: _PyPegen_expect_forced_result
            WriteLine("/// <summary>");
            WriteLine("/// CPython 3.12: _PyPegen_expect_forced_result");
            WriteLine("/// void*_PyPegen_expect_forced_result(Parser *p, void* result, const char* expected)");
            WriteLine("/// Forced result must be non-null or raise syntax error immediately");
            WriteLine("/// </summary>");
            WriteLine("protected T ExpectForcedResult<T>(T result, string expected) where T : class");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (_pendingSyntaxError != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("if (result == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: RAISE_SYNTAX_ERROR(\"expected (%s)\", expected)");
            WriteLine("_pendingSyntaxError = $\"expected ({expected})\";");
            WriteLine("_pendingErrorPosition = _position;");
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return result;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // TryLeftRecursive - Warth et al. algorithm for left recursion
            WriteLine("/// <summary>");
            WriteLine("/// Handle left-recursive rules using memoization");
            WriteLine("/// CPython 3.12: Implements Warth et al. 'Packrat Parsers Can Support Left Recursion'");
            WriteLine("/// Algorithm: SEED (FAIL) → BASE CASE → GROW → TERMINATE");
            WriteLine("/// </summary>");
            WriteLine("protected GeneratedPtr TryLeftRecursive(string ruleName, Func<GeneratedPtr> ruleFunc)");
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
            WriteLine("// CPython 3.12: Check memoization first (like _PyPegen_is_memoized)");
            WriteLine("var key = (_position, ruleName);");
            WriteLine("if (_lrCache.TryGetValue(key, out var lrEntry))");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[LR] {ruleName}: Memo HIT at pos={_position}, returning cached result, newPos={lrEntry.EndPos}\");");
            WriteLine("#endif");
            WriteLine("_position = lrEntry.EndPos;");
            WriteLine("return lrEntry.Result;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("_level++;");
            WriteLine("int _mark = _position;");
            WriteLine("int _resmark = _position;");
            WriteLine("GeneratedPtr _res = null;");
            WriteLine();
            WriteLine("try");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython 3.12: Growth loop");
            WriteLine("while (true)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Update memo with current result (like _PyPegen_update_memo)");
            WriteLine("// Use _resmark (previous iteration's end position), not _position");
            WriteLine("_lrCache[key] = new LREntry { Result = _res, EndPos = _resmark, IsGrowing = false };");
            WriteLine();
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[LR] {ruleName}: Loop iteration, _mark={_mark}, _resmark={_resmark}\");");
            WriteLine("#endif");
            WriteLine();
            WriteLine("// Reset position and try to parse (like primary_raw)");
            WriteLine("_position = _mark;");
            WriteLine("var _raw = ruleFunc();");
            WriteLine();
            WriteLine("// Check for progress");
            WriteLine("if (_raw == null || _position <= _resmark)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[LR] {ruleName}: No progress, terminating. _raw={((_raw == null) ? \"null\" : \"non-null\")}, pos={_position}, _resmark={_resmark}\");");
            WriteLine("#endif");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// Made progress - update and continue");
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[LR] {ruleName}: Progress made, _resmark {_resmark} -> {_position}\");");
            WriteLine("#endif");
            WriteLine("_resmark = _position;");
            WriteLine("_res = _raw;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// Restore final position");
            WriteLine("_position = _resmark;");
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[LR] {ruleName}: Returning _res at pos={_position}\");");
            WriteLine("#endif");
            WriteLine("return _res;");
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
            WriteLine("protected GeneratedPtr TryMemoized(string ruleName, Func<GeneratedPtr> ruleFunc)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO] {ruleName} at pos={_position}\");");
            WriteLine("#endif");
            WriteLine("// STEP 1: Get current token (CPython: Token *t = p->tokens[p->mark])");
            WriteLine("if (_position >= _tokens.Count)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO] {ruleName}: Beyond token count, parsing directly\");");
            WriteLine("#endif");
            WriteLine("// ENDMARKER or beyond - don't memoize, just parse");
            WriteLine("return ruleFunc();");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("var token = _tokens[_position];");
            WriteLine("int startMark = _position;");
            WriteLine();
            WriteLine("// STEP 2: CHECK CACHE - _PyPegen_is_memoized(p, type, &res)");
            WriteLine("// CPython 3.12: Cache key must include call_invalid_rules state for expression rules");
            WriteLine("if (token.Memo != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: for (Memo *m = t->memo; m != NULL; m = m->next)");
            WriteLine("// CPython: Cache key is m->type (rule type), NOT affected by call_invalid_rules");
            WriteLine("var cached = token.Memo.FirstOrDefault(m => m.RuleType == ruleName);");
            WriteLine("if (cached != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO] {ruleName}: Cache HIT at pos={_position}, returning cached result (null={cached.Node == null}), newPos={cached.Mark}\");");
            WriteLine("#endif");
            WriteLine("// Cache HIT - restore mark and return cached result");
            WriteLine("// CPython: p->mark = m->mark; *(void**)(pres) = m->node; return 1;");
            WriteLine("_position = cached.Mark;");
            WriteLine("return cached.Node as GeneratedPtr;");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO] {ruleName}: Cache MISS at pos={_position}, parsing...\");");
            WriteLine("#endif");
            WriteLine();
            WriteLine("// STEP 3: Cache MISS - parse the rule");
            WriteLine("var result = ruleFunc();");
            WriteLine("int endMark = _position;");
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO] {ruleName}: Parse completed, result={(result == null ? \"null\" : \"not-null\")}, pos={startMark}->{endMark}\");");
            WriteLine("#endif");
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
            WriteLine("// CPython: Cache key is just rule name, independent of call_invalid_rules");
            WriteLine("var existing = token.Memo.FirstOrDefault(m => m.RuleType == ruleName);");
            WriteLine("if (existing != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO] {ruleName}: Updating existing cache entry\");");
            WriteLine("#endif");
            WriteLine("// Update existing entry (shouldn't happen in normal flow, but CPython does this)");
            WriteLine("existing.Node = result;");
            WriteLine("existing.Mark = endMark;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO] {ruleName}: Adding new cache entry\");");
            WriteLine("#endif");
            WriteLine("// Insert new memo entry");
            WriteLine("// CPython: _PyPegen_insert_memo() adds to front of linked list");
            WriteLine("// CPython: Cache key is just rule name, independent of call_invalid_rules");
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

            // CPython 3.12: _PyPegen_is_memoized and _PyPegen_update_memo for left-recursive wrappers
            WriteLine("/// <summary>");
            WriteLine("/// CPython 3.12: _PyPegen_is_memoized - Check if rule result is memoized");
            WriteLine("/// Used by left-recursive wrapper for growth loop");
            WriteLine("/// </summary>");
            WriteLine("protected bool TryGetMemoized(string ruleName, out GeneratedPtr result)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO-GET] {ruleName} at pos={_position}\");");
            WriteLine("#endif");
            WriteLine("var key = (_position, ruleName);");
            WriteLine("if (_lrCache.TryGetValue(key, out var entry))");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO-GET] {ruleName} HIT: result={(entry.Result != null ? \"non-null\" : \"null\")}, endPos={entry.EndPos}\");");
            WriteLine("#endif");
            WriteLine("_position = entry.EndPos;");
            WriteLine("result = entry.Result;");
            WriteLine("return true;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO-GET] {ruleName} MISS\");");
            WriteLine("#endif");
            WriteLine("result = null;");
            WriteLine("return false;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// CPython 3.12: _PyPegen_update_memo - Update memoization for left-recursive growth");
            WriteLine("/// </summary>");
            WriteLine("protected void UpdateMemoized(string ruleName, int mark, GeneratedPtr result, int endPos)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[MEMO-UPDATE] {ruleName} at mark={mark}: result={(result != null ? \"non-null\" : \"null\")}, endPos={endPos}\");");
            WriteLine("#endif");
            WriteLine("var key = (mark, ruleName);");
            WriteLine("_lrCache[key] = new LREntry { Result = result, EndPos = endPos, IsGrowing = false };");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Token conversion helpers
            WriteLine("/// <summary>");
            WriteLine("/// Convert NAME token to AST Name expression");
            WriteLine("/// CPython 3.12: Used in grammar actions");
            WriteLine("/// </summary>");
            WriteLine("protected GeneratedName NameToken(GeneratedTokenInfo token)");
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
            WriteLine("protected GeneratedConstant NumberToken(GeneratedTokenInfo token)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (token == null) return null;");
            WriteLine("var constant = new GeneratedConstant();");
            WriteLine("// CPython 3.12: Parse number value (decimal, hex, octal, binary, float)");
            WriteLine("string value = token.Value;");
            WriteLine();
            WriteLine("// Check for complex numbers (j suffix) - TODO: implement PyComplex");
            WriteLine("if (value.EndsWith(\"j\", StringComparison.OrdinalIgnoreCase) || value.EndsWith(\"J\"))");
            WriteLine("{");
            WriteLine("    // For now, treat as comment/unsupported");
            WriteLine("    throw new System.NotImplementedException(\"Complex numbers not yet supported\");");
            WriteLine("}");
            WriteLine("// Check for floating point");
            WriteLine("else if (value.Contains(\".\") || value.Contains(\"e\", StringComparison.OrdinalIgnoreCase))");
            WriteLine("{");
            WriteLine("    constant.Value = new GeneratedPyConstantFloat(double.Parse(value));");
            WriteLine("}");
            WriteLine("// Check for hexadecimal (0x or 0X)");
            WriteLine("else if (value.StartsWith(\"0x\", StringComparison.OrdinalIgnoreCase))");
            WriteLine("{");
            WriteLine("    constant.Value = new GeneratedPyConstantInt(Convert.ToInt64(value, 16));");
            WriteLine("}");
            WriteLine("// Check for octal (0o or 0O)");
            WriteLine("else if (value.StartsWith(\"0o\", StringComparison.OrdinalIgnoreCase))");
            WriteLine("{");
            WriteLine("    constant.Value = new GeneratedPyConstantInt(Convert.ToInt64(value.Substring(2), 8));");
            WriteLine("}");
            WriteLine("// Check for binary (0b or 0B)");
            WriteLine("else if (value.StartsWith(\"0b\", StringComparison.OrdinalIgnoreCase))");
            WriteLine("{");
            WriteLine("    constant.Value = new GeneratedPyConstantInt(Convert.ToInt64(value.Substring(2), 2));");
            WriteLine("}");
            WriteLine("// Decimal integer");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    constant.Value = new GeneratedPyConstantInt(long.Parse(value));");
            WriteLine("}");
            WriteLine();
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
            WriteLine("/// Convert STRING token to token wrapper for _PyPegen_constant_from_string");
            WriteLine("/// CPython 3.12: Token is passed to string_parser.c for decoding");
            WriteLine("/// Note: Returns GeneratedTokenInfo directly to trigger DecodeStringLiteral");
            WriteLine("/// </summary>");
            WriteLine("protected GeneratedTokenInfo StringToken(GeneratedTokenInfo token)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Return token as-is so _PyPegen_constant_from_string can decode it");
            WriteLine("return token;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // RaiseErrorKnownLocation helper
            WriteLine("/// <summary>");
            WriteLine("/// CPython 3.12: Raise syntax error at known token location");
            WriteLine("/// Uses PySyntaxErrorException with location info for parsing errors");
            WriteLine("/// </summary>");
            WriteLine("protected void RaiseErrorKnownLocation(GeneratedTokenInfo token, string message)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (token == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_errorIndicator = 1;");
            WriteLine("throw new PySyntaxErrorException(message);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// Mark error and throw exception with location");
            WriteLine("_errorIndicator = 1;");
            WriteLine("_knownErrToken = token;");
            WriteLine("var locationMsg = $\"  File \\\"{_filename}\\\", line {token.Line}\\n    {message}\";");
            WriteLine("throw new PySyntaxErrorException(locationMsg);");
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
            WriteLine("public static GeneratedStmtSeq _PyPegen_seq_flatten(System.Collections.Generic.List<GeneratedStmtSeq> sequences)");
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
            WriteLine("public static GeneratedSeq _PyPegen_singleton_seq(GeneratedSlashWithDefault item)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var seq = new GeneratedSeq(1);");
            WriteLine("seq.Add(item);");
            WriteLine("return seq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // CPython 3.12: _PyPegen_slash_with_default
            WriteLine("// CPython 3.12: _PyPegen_slash_with_default");
            WriteLine("// Creates a SlashWithDefault structure for positional-only parameters");
            WriteLine("// plain_names: arguments without defaults (e.g., \"self\" in \"self, other=()\")");
            WriteLine("// names_with_defaults: arguments with defaults (e.g., \"other=()\" -> arg + default value)");
            WriteLine("public static GeneratedSlashWithDefault? _PyPegen_slash_with_default(");
            _indentLevel++;
            WriteLine("IEnumerable<GeneratedArg>? plain_names,");
            WriteLine("GeneratedSeq? names_with_defaults)");
            _indentLevel--;
            WriteLine("{");
            _indentLevel++;
            WriteLine("var slash_with_default = new GeneratedSlashWithDefault();");
            WriteLine();
            WriteLine("// CPython: plain_names are args without defaults");
            WriteLine("if (plain_names != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("foreach (var arg in plain_names)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("slash_with_default.Args.Add(arg);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython: names_with_defaults is a seq of NameDefaultPair");
            WriteLine("// Extract both args and defaults from NameDefaultPair");
            WriteLine("if (names_with_defaults != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("foreach (var item in names_with_defaults)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (item is GeneratedNameDefaultPair pair)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("slash_with_default.Args.Add(pair.Arg);");
            WriteLine("slash_with_default.Defaults.Add(pair.Default);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("return slash_with_default;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // seq_insert_in_front
            WriteLine("// CPython: _PyPegen_seq_insert_in_front");
            WriteLine("// Typed sequence versions - same type input and output");
            WriteLine("public static GeneratedExprSeq _PyPegen_seq_insert_in_front(GeneratedExpr item, GeneratedExprSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (seq == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var singletonSeq = new GeneratedExprSeq();");
            WriteLine("singletonSeq.Add(item);");
            WriteLine("return singletonSeq;");
            _indentLevel--;
            WriteLine("}");
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
            WriteLine("if (seq == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var singletonSeq = new GeneratedPatternSeq();");
            WriteLine("singletonSeq.Add(item);");
            WriteLine("return singletonSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("var newSeq = new GeneratedPatternSeq(seq.Count + 1);");
            WriteLine("newSeq.Add(item);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// CPython 3.12: Generic version for asdl_seq* - matches CPython's void* signature");
            WriteLine("public static GeneratedSeq _PyPegen_seq_insert_in_front(GeneratedPtr item, GeneratedSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (seq == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var singletonSeq = new GeneratedSeq();");
            WriteLine("singletonSeq.Add(item);");
            WriteLine("return singletonSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("var newSeq = new GeneratedSeq(seq.Count + 1);");
            WriteLine("newSeq.Add(item);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // seq_count_dots
            WriteLine("// CPython: _PyPegen_seq_count_dots");
            WriteLine("public static int _PyPegen_seq_count_dots(GeneratedIdentifierSeq seq)");
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
            WriteLine("foreach (var elt in list.Elts.ToEnumerable<GeneratedExpr>())");
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
            WriteLine("foreach (var elt in tuple.Elts.ToEnumerable<GeneratedExpr>())");
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
            WriteLine("public static GeneratedModule _PyPegen_make_module(GeneratedStmtSeq body)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var module = new GeneratedModule();");
            WriteLine("module.Body = body ?? GeneratedStmtSeq.Empty;");
            WriteLine("module.TypeIgnores = GeneratedTypeIgnoreSeq.Empty;");
            WriteLine("return module;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // constant_from_string (moved to instance method - generated in CSharpCodeGenerator)
            // concatenate_strings (moved to instance method - generated in CSharpCodeGenerator)

            // DecodeStringLiteral helper
            WriteLine("// Helper: Decode string literal (remove quotes, handle escapes)");
            WriteLine("public static string DecodeStringLiteral(string literal)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (string.IsNullOrEmpty(literal))");
            _indentLevel++;
            WriteLine("return string.Empty;");
            _indentLevel--;
            WriteLine();
            WriteLine("// Handle string prefixes: r, b, u, f, etc.");
            WriteLine("var workingLiteral = literal;");
            WriteLine("var isRaw = false;");
            WriteLine("while (workingLiteral.Length > 0 && char.IsLetter(workingLiteral[0]))");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var prefix = char.ToLower(workingLiteral[0]);");
            WriteLine("if (prefix == 'r')");
            _indentLevel++;
            WriteLine("isRaw = true;");
            _indentLevel--;
            WriteLine("workingLiteral = workingLiteral.Substring(1);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// Remove quotes: \\\"hello\\\" -> hello, 'world' -> world");
            WriteLine("if (workingLiteral.Length >= 2)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Triple-quoted strings");
            WriteLine("if (workingLiteral.StartsWith(\"\\\"\\\"\\\"\") || workingLiteral.StartsWith(\"'''\"))");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (workingLiteral.Length >= 6)");
            _indentLevel++;
            WriteLine("workingLiteral = workingLiteral.Substring(3, workingLiteral.Length - 6);");
            _indentLevel--;
            _indentLevel--;
            WriteLine("}");
            WriteLine("// Single/double-quoted strings");
            WriteLine("else if (workingLiteral.StartsWith(\"\\\"\") || workingLiteral.StartsWith(\"'\"))");
            WriteLine("{");
            _indentLevel++;
            WriteLine("workingLiteral = workingLiteral.Substring(1, workingLiteral.Length - 2);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// Handle escape sequences (unless raw string)");
            WriteLine("if (!isRaw && workingLiteral.Contains(\"\\\\\"))");
            WriteLine("{");
            _indentLevel++;
            WriteLine("workingLiteral = workingLiteral");
            _indentLevel++;
            WriteLine(".Replace(\"\\\\n\", \"\\n\")");
            WriteLine(".Replace(\"\\\\t\", \"\\t\")");
            WriteLine(".Replace(\"\\\\r\", \"\\r\")");
            WriteLine(".Replace(\"\\\\\\\\\", \"\\\\\")");
            WriteLine(".Replace(\"\\\\\\\"\", \"\\\"\")");
            WriteLine(".Replace(\"\\\\'\", \"'\");");
            _indentLevel--;
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("return workingLiteral;");
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

            // keyword_or_starred
            WriteLine("// CPython: _PyPegen_keyword_or_starred");
            WriteLine("// Construct a KeywordOrStarred");
            WriteLine("public static GeneratedKeywordOrStarred _PyPegen_keyword_or_starred(object element, int is_keyword)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return new GeneratedKeywordOrStarred");
            WriteLine("{");
            _indentLevel++;
            WriteLine("Keyword = is_keyword != 0 ? element as GeneratedKeyword : null,");
            WriteLine("Starred = is_keyword == 0 ? element as GeneratedExpr : null");
            _indentLevel--;
            WriteLine("};");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // collect_call_seqs
            WriteLine("// CPython: _PyPegen_collect_call_seqs");
            WriteLine("// Collect arguments and keywords from call sequences");
            WriteLine("public static GeneratedExpr _PyPegen_collect_call_seqs(");
            _indentLevel++;
            WriteLine("GeneratedExprSeq a,");
            WriteLine("GeneratedSeq? b,");
            WriteLine("int lineno, int col_offset, int end_lineno, int end_col_offset)");
            _indentLevel--;
            WriteLine("{");
            _indentLevel++;
            WriteLine("int args_len = a.Count;");
            WriteLine("int total_len = args_len;");
            WriteLine();
            WriteLine("if (b == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return new GeneratedCall");
            WriteLine("{");
            _indentLevel++;
            WriteLine("Func = _PyPegen_dummy_name(),");
            WriteLine("Args = a,");
            WriteLine("Keywords = null,");
            WriteLine("LineNo = lineno,");
            WriteLine("ColOffset = col_offset,");
            WriteLine("EndLineNo = end_lineno,");
            WriteLine("EndColOffset = end_col_offset");
            _indentLevel--;
            WriteLine("};");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("var starreds = _PyPegen_seq_extract_starred_exprs(b);");
            WriteLine("var keywords = _PyPegen_seq_delete_starred_exprs(b);");
            WriteLine();
            WriteLine("if (starreds != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("total_len += starreds.Count;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("var args = new GeneratedExprSeq();");
            WriteLine();
            WriteLine("// Copy args from 'a'");
            WriteLine("for (int i = 0; i < args_len; i++)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("args.Add(a[i]);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// Append starred expressions");
            WriteLine("if (starreds != null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("for (int i = 0; i < starreds.Count; i++)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("args.Add(starreds[i]);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("return new GeneratedCall");
            WriteLine("{");
            _indentLevel++;
            WriteLine("Func = _PyPegen_dummy_name(),");
            WriteLine("Args = args,");
            WriteLine("Keywords = keywords,");
            WriteLine("LineNo = lineno,");
            WriteLine("ColOffset = col_offset,");
            WriteLine("EndLineNo = end_lineno,");
            WriteLine("EndColOffset = end_col_offset");
            _indentLevel--;
            WriteLine("};");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // get_cmpops - extract comparison operators from CmpopExprPair
            WriteLine("// CPython: _PyPegen_get_cmpops");
            WriteLine("// asdl_int_seq *_PyPegen_get_cmpops(Parser *p, asdl_seq *seq)");
            WriteLine("// Extract comparison operators from (cmpop, expr) pairs");
            WriteLine("public static GeneratedCmpopSeq _PyPegen_get_cmpops(GeneratedSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (pairs == null || pairs.Count == 0)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return new GeneratedCmpopSeq();");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("var ops = new GeneratedCmpopSeq();");
            WriteLine("foreach (var item in pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var pair = (GeneratedCmpopExprPair)item;");
            WriteLine("ops.Add(pair.Cmpop);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return ops;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// Overload for AstNodeSeq");
            WriteLine("// CPython 3.12: Type cast with shared reference (no copy)");
            WriteLine("public static GeneratedCmpopSeq _PyPegen_get_cmpops(GeneratedAstNodeSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: implicit cast shares same PyObject** array");
            WriteLine("return _PyPegen_get_cmpops(pairs.Cast<GeneratedSeq>());");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // get_exprs - extract expressions from CmpopExprPair
            WriteLine("// CPython: _PyPegen_get_exprs");
            WriteLine("// asdl_expr_seq *_PyPegen_get_exprs(Parser *p, asdl_seq *seq)");
            WriteLine("// Extract expressions from (cmpop, expr) pairs");
            WriteLine("public static GeneratedExprSeq _PyPegen_get_exprs(GeneratedSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (pairs == null || pairs.Count == 0)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return new GeneratedExprSeq();");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("var exprs = new GeneratedExprSeq();");
            WriteLine("foreach (var item in pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var pair = (GeneratedCmpopExprPair)item;");
            WriteLine("exprs.Add(pair.Expr);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return exprs;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// Overload for AstNodeSeq");
            WriteLine("// CPython 3.12: Type cast with shared reference (no copy)");
            WriteLine("public static GeneratedExprSeq _PyPegen_get_exprs(GeneratedAstNodeSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: implicit cast shares same PyObject** array");
            WriteLine("return _PyPegen_get_exprs(pairs.Cast<GeneratedSeq>());");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // key_value_pair - create key-value pair
            WriteLine("// CPython: _PyPegen_key_value_pair");
            WriteLine("// Create a key-value pair for dictionary literals");
            WriteLine("public static GeneratedKeyValuePair _PyPegen_key_value_pair(GeneratedExpr key, GeneratedExpr value)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return new GeneratedKeyValuePair { Key = key ?? value, Value = value };");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // get_keys - extract keys from key-value pairs
            WriteLine("// CPython: _PyPegen_get_keys");
            WriteLine("// Extract keys from (key, value) pairs");
            WriteLine("public static GeneratedExprSeq _PyPegen_get_keys(GeneratedSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var keys = new GeneratedExprSeq();");
            WriteLine("if (pairs != null)");
            WriteLine("{");
            _indentLevel++;
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
            _indentLevel--;
            WriteLine("}");
            WriteLine("return keys;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // get_values - extract values from key-value pairs
            WriteLine("// CPython: _PyPegen_get_values");
            WriteLine("// Extract values from (key, value) pairs");
            WriteLine("public static GeneratedExprSeq _PyPegen_get_values(GeneratedSeq pairs)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var values = new GeneratedExprSeq();");
            WriteLine("if (pairs != null)");
            WriteLine("{");
            _indentLevel++;
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
            WriteLine("foreach (var item in seq.ToEnumerable<GeneratedKeywordOrStarred>())");
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
            WriteLine("foreach (var item in seq.ToEnumerable<GeneratedKeywordOrStarred>())");
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
            // CPython 3.12: Type cast with shared reference (no copy)
            WriteLine("// Overload for MixedSeq");
            WriteLine("// CPython 3.12: Type cast shares same underlying array (no copy)");
            WriteLine("public static GeneratedExprSeq _PyPegen_seq_extract_starred_exprs(GeneratedSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: implicit cast shares same PyObject** array");
            WriteLine("// C#: Cast<T>() shares same List<GeneratedPtr> via _initialize");
            WriteLine("return _PyPegen_seq_extract_starred_exprs(seq.Cast<GeneratedKeywordOrStarredSeq>());");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// CPython 3.12: Type cast with shared reference (no copy)");
            WriteLine("public static GeneratedKeywordSeq _PyPegen_seq_delete_starred_exprs(GeneratedSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: implicit cast shares same PyObject** array");
            WriteLine("// C#: Cast<T>() shares same List<GeneratedPtr> via _initialize");
            WriteLine("return _PyPegen_seq_delete_starred_exprs(seq.Cast<GeneratedKeywordOrStarredSeq>());");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Conversion helpers for Seq types
            // CPython 3.12: Type casts share the same underlying array (no copy)
            // C's implicit void* conversion = C#'s Cast<T>() with shared _items reference
            WriteLine("// Conversion: GeneratedAstNodeSeq to GeneratedSeq");
            WriteLine("// CPython 3.12: Type cast with shared reference (no copy)");
            WriteLine("public static GeneratedSeq ToMixedSeq(GeneratedAstNodeSeq inputSeq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: implicit cast shares same PyObject** array");
            WriteLine("// C#: Cast<T>() shares same List<GeneratedPtr> via _initialize");
            WriteLine("return inputSeq.Cast<GeneratedSeq>();");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("// Conversion: GeneratedKeywordOrStarredSeq to GeneratedSeq");
            WriteLine("// CPython 3.12: Type cast with shared reference (no copy)");
            WriteLine("public static GeneratedSeq ToMixedSeq(GeneratedKeywordOrStarredSeq inputSeq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: implicit cast shares same PyObject** array");
            WriteLine("// C#: Cast<T>() shares same List<GeneratedPtr> via _initialize");
            WriteLine("return inputSeq.Cast<GeneratedSeq>();");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // CheckLegacyStmt - CPython 3.12: Parser/action_helpers.c:876
            WriteLine("// CPython: _PyPegen_check_legacy_stmt");
            WriteLine("// Check if NAME is 'print' or 'exec' (legacy Python 2 statements)");
            WriteLine("public static bool CheckLegacyStmt(GeneratedExpr expr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (expr is not GeneratedName name)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return false;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("var id = name.Id;");
            WriteLine("return id == \"print\" || id == \"exec\";");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// Legacy alias for backward compatibility");
            WriteLine("public static bool _PyPegen_check_legacy_stmt(GeneratedExpr expr) => CheckLegacyStmt(expr);");
            WriteLine();

            // RaiseSyntaxErrorKnownRange - CPython 3.12: RAISE_SYNTAX_ERROR_KNOWN_RANGE macro
            WriteLine("// CPython: RAISE_SYNTAX_ERROR_KNOWN_RANGE macro");
            WriteLine("// Raise syntax error with range information");
            WriteLine("public static GeneratedPtr RaiseSyntaxErrorKnownRange(GeneratedAstNode start, GeneratedAstNode end, string message)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Extract location info from start/end nodes");
            WriteLine("var startLine = start?.LineNo ?? 1;");
            WriteLine("var startCol = start?.ColOffset ?? 0;");
            WriteLine("var endLine = end?.EndLineNo ?? startLine;");
            WriteLine("var endCol = end?.EndColOffset ?? startCol;");
            WriteLine();
            WriteLine("// Format message with location info");
            WriteLine("var fullMessage = $\"{message} (line {startLine}, col {startCol})\";");
            WriteLine("throw new PySyntaxErrorException(fullMessage);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // CHECK<T> - CPython 3.12: CHECK macro
            WriteLine("// CPython: CHECK(type, expr) macro");
            WriteLine("// Null-check and cast - throws if null, otherwise returns typed result");
            WriteLine("public static T CHECK<T>(T? value) where T : class");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (value == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("throw new PySyntaxErrorException(\"CHECK failed: unexpected null value\");");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return value;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// CPython 3.12: CHECK_NULL_ALLOWED - allows NULL return without error");
            WriteLine("/// Used for helper functions like _PyPegen_seq_extract_starred_exprs");
            WriteLine("/// Returns nullable type - NULL is valid if no error occurred");
            WriteLine("/// </summary>");
            WriteLine("public static T? CHECK_NULL_ALLOWED<T>(T? value) where T : class");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// CPython: if (result == NULL && PyErr_Occurred()) p->error_indicator = 1;");
            WriteLine("// In C#: We don't set error_indicator here, just return null");
            WriteLine("// The caller is responsible for checking null and handling it");
            WriteLine("return value;");
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

            // CPython 3.12: _PyPegen_add_type_comment_to_arg
            WriteLine("// CPython: _PyPegen_add_type_comment_to_arg");
            WriteLine("// action_helpers.c: Add type comment to argument (Python 2 legacy)");
            WriteLine("public static GeneratedArg _PyPegen_add_type_comment_to_arg(GeneratedArg arg, GeneratedTokenInfo tc)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// If no type comment, return arg as-is");
            WriteLine("if (tc == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return arg;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("// Type comments are Python 2 legacy feature");
            WriteLine("// CPython 3.12 parses them but mostly ignores them");
            WriteLine("// For now, we return the arg unchanged");
            WriteLine("return arg;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // _PyPegen_check_barry_as_flufl - Easter egg from PEP 401
            WriteLine("// CPython: _PyPegen_check_barry_as_flufl");
            WriteLine("// Easter egg: from __future__ import barry_as_BDFL");
            WriteLine("// Returns 0 (false) if token is '!=', non-zero (true) otherwise");
            WriteLine("public static bool _PyPegen_check_barry_as_flufl(GeneratedTokenInfo tok)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// SharpPy doesn't implement barry_as_BDFL flag");
            WriteLine("// CPython: return strcmp(tok_str, \"!=\")");
            WriteLine("// strcmp returns 0 if equal, non-zero if different");
            WriteLine("// In C#: return false if tok is \"!=\", true otherwise");
            WriteLine("return tok.Value != \"!=\";");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            _indentLevel--;  // Close PegenHelpers class
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
                _ => "GeneratedPtr"
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

        private void GenerateParserHelperTypes()
        {
            WriteLine();
            WriteLine("// ============================================================");
            WriteLine("// Parser Helper Types");
            WriteLine("// CPython 3.12: Parser-specific types (not in ASDL)");
            WriteLine("// Note: Some types (SlashWithDefault, StarEtc, KeywordOrStarred, KeyValuePair) are already defined in GeneratedAstTypes.cs");
            WriteLine("// ============================================================");
            WriteLine();

            // NameDefaultPair
            WriteLine("public class GeneratedNameDefaultPair : GeneratedPtr");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedArg Arg { get; set; }");
            WriteLine("public GeneratedExpr Default { get; set; }");
            WriteLine("public string? TypeComment { get; set; }");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // KeyPatternPair
            WriteLine("public class GeneratedKeyPatternPair : GeneratedPtr");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedExpr Key { get; set; }");
            WriteLine("public GeneratedPattern Pattern { get; set; }");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // KeyPatternPairSeq
            WriteLine("public class GeneratedKeyPatternPairSeq : GeneratedSeq");
            WriteLine("{");
            WriteLine("}");
            WriteLine();

            // CmpopExprPair
            // CPython 3.12: typedef struct { cmpop_ty cmpop; expr_ty expr; } CmpopExprPair;
            WriteLine("public class GeneratedCmpopExprPair : GeneratedPtr");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedCmpop Cmpop { get; set; }  // CPython: cmpop_ty cmpop");
            WriteLine("public GeneratedExpr Expr { get; set; }    // CPython: expr_ty expr");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // ResultTokenWithMetadata
            WriteLine("public class GeneratedResultTokenWithMetadata : GeneratedPtr");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedTokenInfo Token { get; set; }");
            WriteLine("public object? Metadata { get; set; }");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
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

        private void Indent()
        {
            _indentLevel++;
        }

        private void Dedent()
        {
            _indentLevel--;
        }
    }
}
