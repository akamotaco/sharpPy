// Generated PyParserBase from Python.asdl
// CPython 3.12 compatible - Auto-generated, DO NOT EDIT

using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Tokenizer;

// CPython 3.12: Type aliases for grammar compatibility
// Allows python_cs.gram to use C type names directly
using stmt_ty = SharpPy.Generated.GeneratedStmt;
using expr_ty = SharpPy.Generated.GeneratedExpr;
using alias_ty = SharpPy.Generated.GeneratedAlias;
using arguments_ty = SharpPy.Generated.GeneratedArguments;
using asdl_stmt_seq = SharpPy.Generated.GeneratedStmtSeq;
using asdl_expr_seq = SharpPy.Generated.GeneratedExprSeq;
using asdl_identifier_seq = SharpPy.Generated.GeneratedIdentifierSeq;
using asdl_pattern_seq = SharpPy.Generated.GeneratedPatternSeq;
using asdl_int_seq = SharpPy.Generated.GeneratedCmpopSeq;  // CPython: int sequence used for comparison operators
using asdl_keyword_seq = SharpPy.Generated.GeneratedKeywordSeq;
using asdl_seq = SharpPy.Generated.GeneratedSeq;
using keyword_ty = SharpPy.Generated.GeneratedKeyword;

namespace SharpPy.Generated
{
    // ============================================================
    // PyParserBase - Base parser class
    // ============================================================

    /// <summary>
    /// Base class for generated parser - provides common parsing logic
    /// CPython 3.12: Parser/pegen.c equivalent
    /// </summary>
    public abstract class PyParserBase<TResult>
    {
        protected List<GeneratedTokenInfo> _tokens;
        protected int _position = 0;  // CPython: mark
        protected string _filename;
        protected string? _pendingSyntaxError = null;
        protected int _pendingErrorPosition = -1;
        protected bool _callInvalidRules = true;
        // CPython 3.12: Memo cache uses (position, rule_name) tuple keys
        protected Dictionary<(int, string), GeneratedAstNode?> _memoCache = new();

        // CPython Parser fields for recursion and error tracking
        protected int _level = 0;  // Nesting depth for recursion limit
        protected GeneratedTokenInfo? _knownErrToken = null;  // Error location tracking
        protected int _errorIndicator = 0;  // Error state flag
        protected const int MAX_RECURSION_DEPTH = 1000;  // Python's recursion limit

        // Left-recursion handling (Warth et al. algorithm)
        protected class LREntry
        {
            public GeneratedPtr? Result { get; set; }
            public int EndPos { get; set; }
            public bool IsGrowing { get; set; }
        }
        protected Dictionary<(int, string), LREntry> _lrCache = new();

        // CPython 3.12: Token-based memoization
        // Each Token owns its Memo list - no global cache needed
        // MemoEntry is defined in GeneratedTokenInfo (PyTokenizer.cs)

        protected PyParserBase(List<GeneratedTokenInfo> tokens, string filename)
        {
            // CPython 3.12: Filter out COMMENT, NL, TYPE_COMMENT tokens before parsing
            _tokens = tokens.Where(t => 
                t.Type != GeneratedTokenType.COMMENT &&
                t.Type != GeneratedTokenType.NL &&
                t.Type != GeneratedTokenType.TYPE_COMMENT).ToList();
            _filename = filename;
        }

        protected GeneratedTokenInfo? CurrentToken
        {
            get => _position < _tokens.Count ? _tokens[_position] : null;
        }

        public abstract TResult Parse();

        protected virtual GeneratedModule ParseFile()
        {
            // Override in generated parser
            throw new NotImplementedException("ParseFile must be overridden");
        }

        protected GeneratedTokenInfo? ExpectToken(GeneratedTokenType type)
        {
            var token = CurrentToken;
            if (token != null && token.Type == type)
            {
                _position++;
                return token;
            }
            return null;
        }

        protected GeneratedTokenInfo? Expect(GeneratedTokenType type, string value)
        {
            var token = CurrentToken;
            if (token != null && token.Type == type && token.Value == value)
            {
                _position++;
                return token;
            }
            return null;
        }

        protected GeneratedTokenInfo? ExpectName()
        {
            var token = CurrentToken;
            if (token != null && token.Type == GeneratedTokenType.NAME)
            {
                _position++;
                return token;
            }
            return null;
        }

        /// <summary>
        /// Handle left-recursive rules using memoization
        /// CPython 3.12: Implements Warth et al. 'Packrat Parsers Can Support Left Recursion'
        /// Algorithm: SEED (FAIL) → BASE CASE → GROW → TERMINATE
        /// </summary>
        protected GeneratedPtr? TryLeftRecursive(string ruleName, Func<GeneratedPtr?> ruleFunc)
        {
            // Check recursion depth
            if (_level >= MAX_RECURSION_DEPTH)
            {
                throw new StackOverflowException($"Maximum recursion depth exceeded in rule {ruleName}");
            }

            var key = (_position, ruleName);

            // Recursive call - return current seed
            if (_lrCache.TryGetValue(key, out var lrEntry) && lrEntry.IsGrowing)
            {
                _position = lrEntry.EndPos;
                return lrEntry.Result;
            }

            _level++;
            try
            {
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[LR] {ruleName}: Starting at pos={_position}, seeding with FAIL");
                #endif
                // SEED PHASE: Start with FAIL seed
                _lrCache[key] = new LREntry { Result = null, EndPos = _position, IsGrowing = true };

                // First attempt - base case should execute
                var result = ruleFunc();
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[LR] {ruleName}: First attempt result={(result == null ? "null" : "not-null")}, pos={_position}");
                #endif

                if (result == null)
                {
                    // Real failure - base case also failed
                    _lrCache.Remove(key);
                    return null;
                }

                // GROWTH PHASE: Seed succeeded, now grow
                GeneratedPtr? lastResult = result;
                int lastEndPos = _position;
                _lrCache[key] = new LREntry { Result = result, EndPos = _position, IsGrowing = true };

                while (true)
                {
                    #if DEBUG_PARSE_LOG
                    Console.WriteLine($"[LR] {ruleName}: Growth attempt, resetting to pos={key.Item1}");
                    #endif
                    // Reset to start position for next growth attempt
                    _position = key.Item1;
                    var newResult = ruleFunc();
                    #if DEBUG_PARSE_LOG
                    Console.WriteLine($"[LR] {ruleName}: Growth result={(newResult == null ? "null" : "not-null")}, pos={_position}, lastEndPos={lastEndPos}");
                    #endif

                    // Termination: no progress made
                    if (newResult == null || _position <= lastEndPos)
                    {
                        #if DEBUG_PARSE_LOG
                        Console.WriteLine($"[LR] {ruleName}: Terminating, returning lastResult at pos={lastEndPos}");
                        #endif
                        _position = lastEndPos;
                        _lrCache.Remove(key);
                        return lastResult;
                    }

                    #if DEBUG_PARSE_LOG
                    Console.WriteLine($"[LR] {ruleName}: Growth succeeded, updating seed");
                    #endif
                    // Grow: update seed with new result
                    lastResult = newResult;
                    lastEndPos = _position;
                    _lrCache[key] = new LREntry { Result = newResult, EndPos = _position, IsGrowing = true };
                }
            }
            finally
            {
                _level--;
            }
        }

        /// <summary>
        /// Handle memoized (non-left-recursive) rules with token-based caching
        /// CPython 3.12: Implements _PyPegen_is_memoized() + _PyPegen_update_memo() pattern
        /// Pattern:
        ///   1. Get current token: Token *t = p->tokens[p->mark]
        ///   2. Check cache: for (Memo *m = t->memo; m != NULL; m = m->next)
        ///   3. If hit: p->mark = m->mark; return m->node
        ///   4. If miss: parse, then update token's memo list
        /// </summary>
        protected GeneratedPtr? TryMemoized(string ruleName, Func<GeneratedPtr?> ruleFunc)
        {
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[MEMO] {ruleName} at pos={_position}");
            #endif
            // STEP 1: Get current token (CPython: Token *t = p->tokens[p->mark])
            if (_position >= _tokens.Count)
            {
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[MEMO] {ruleName}: Beyond token count, parsing directly");
                #endif
                // ENDMARKER or beyond - don't memoize, just parse
                return ruleFunc();
            }

            var token = _tokens[_position];
            int startMark = _position;

            // STEP 2: CHECK CACHE - _PyPegen_is_memoized(p, type, &res)
            // CPython 3.12: Cache key must include call_invalid_rules state for expression rules
            if (token.Memo != null)
            {
                // CPython: for (Memo *m = t->memo; m != NULL; m = m->next)
                // CPython: Cache key is m->type (rule type), NOT affected by call_invalid_rules
                var cached = token.Memo.FirstOrDefault(m => m.RuleType == ruleName);
                if (cached != null)
                {
                    #if DEBUG_PARSE_LOG
                    Console.WriteLine($"[MEMO] {ruleName}: Cache HIT at pos={_position}, returning cached result (null={cached.Node == null}), newPos={cached.Mark}");
                    #endif
                    // Cache HIT - restore mark and return cached result
                    // CPython: p->mark = m->mark; *(void**)(pres) = m->node; return 1;
                    _position = cached.Mark;
                    return cached.Node as GeneratedPtr;
                }
            }
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[MEMO] {ruleName}: Cache MISS at pos={_position}, parsing...");
            #endif

            // STEP 3: Cache MISS - parse the rule
            var result = ruleFunc();
            int endMark = _position;
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[MEMO] {ruleName}: Parse completed, result={(result == null ? "null" : "not-null")}, pos={startMark}->{endMark}");
            #endif

            // STEP 4: UPDATE CACHE - _PyPegen_update_memo(p, mark, type, node)
            if (token.Memo == null)
            {
                token.Memo = new List<MemoEntry>();
            }

            // CPython: Search for existing entry and update, or insert new
            // CPython: Cache key is just rule name, independent of call_invalid_rules
            var existing = token.Memo.FirstOrDefault(m => m.RuleType == ruleName);
            if (existing != null)
            {
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[MEMO] {ruleName}: Updating existing cache entry");
                #endif
                // Update existing entry (shouldn't happen in normal flow, but CPython does this)
                existing.Node = result;
                existing.Mark = endMark;
            }
            else
            {
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[MEMO] {ruleName}: Adding new cache entry");
                #endif
                // Insert new memo entry
                // CPython: _PyPegen_insert_memo() adds to front of linked list
                // CPython: Cache key is just rule name, independent of call_invalid_rules
                token.Memo.Add(new MemoEntry
                {
                    RuleType = ruleName,
                    Node = result,
                    Mark = endMark  // Position AFTER parsing (can be same as start if failed)
                });
            }

            return result;
        }

        /// <summary>
        /// Convert NAME token to AST Name expression
        /// CPython 3.12: Used in grammar actions
        /// </summary>
        protected GeneratedName? NameToken(GeneratedTokenInfo? token)
        {
            if (token == null) return null;
            var name = new GeneratedName();
            name.Id = token.Value ?? "";
            name.Ctx = GeneratedLoad.Instance;  // Default context
            name.LineNo = token.Line;
            name.ColOffset = token.Column;
            name.EndLineNo = token.EndLine;
            name.EndColOffset = token.EndColumn;
            return name;
        }

        /// <summary>
        /// Convert NUMBER token to AST Constant expression
        /// CPython 3.12: Numbers are represented as Constant nodes
        /// </summary>
        protected GeneratedConstant? NumberToken(GeneratedTokenInfo? token)
        {
            if (token == null) return null;
            var constant = new GeneratedConstant();
            // Parse number value
            if (token.Value.Contains(".") || token.Value.Contains("e") || token.Value.Contains("E"))
            {
                constant.Value = new GeneratedPyConstantFloat(double.Parse(token.Value));
            }
            else
            {
                constant.Value = new GeneratedPyConstantInt(long.Parse(token.Value));
            }
            constant.LineNo = token.Line;
            constant.ColOffset = token.Column;
            constant.EndLineNo = token.EndLine;
            constant.EndColOffset = token.EndColumn;
            return constant;
        }

        /// <summary>
        /// Convert STRING token to AST Constant expression
        /// CPython 3.12: Strings are represented as Constant nodes
        /// </summary>
        protected GeneratedConstant? StringToken(GeneratedTokenInfo? token)
        {
            if (token == null) return null;
            var constant = new GeneratedConstant();
            // Parse string value
            constant.Value = new GeneratedPyConstantString(token.Value);
            constant.LineNo = token.Line;
            constant.ColOffset = token.Column;
            constant.EndLineNo = token.EndLine;
            constant.EndColOffset = token.EndColumn;
            return constant;
        }

    }

    // ============================================================
    // _PyAST_* Helper Functions
    // ============================================================

    /// <summary>
    /// AST node factory functions - CPython 3.12: Python-ast.c
    /// </summary>
    public static partial class AstFactory
    {
        public static GeneratedMod _PyAST_Module(GeneratedStmtSeq body, GeneratedTypeIgnoreSeq type_ignores)
        {
            var node = new GeneratedModule();
            node.Body = body;
            node.TypeIgnores = type_ignores;
            return node;
        }

        public static GeneratedMod _PyAST_Interactive(GeneratedStmtSeq body)
        {
            var node = new GeneratedInteractive();
            node.Body = body;
            return node;
        }

        public static GeneratedMod _PyAST_Expression(GeneratedExpr body)
        {
            var node = new GeneratedExpression();
            node.Body = body;
            return node;
        }

        public static GeneratedMod _PyAST_FunctionType(GeneratedExprSeq argtypes, GeneratedExpr returns)
        {
            var node = new GeneratedFunctionType();
            node.Argtypes = argtypes;
            node.Returns = returns;
            return node;
        }

        public static GeneratedStmt _PyAST_FunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedExpr? returns, string? type_comment, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedFunctionDef();
            node.Name = name;
            node.Args = args;
            node.Body = body;
            node.DecoratorList = decorator_list;
            node.Returns = returns;
            node.TypeComment = type_comment;
            node.TypeParams = type_params;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_FunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_FunctionDef(name, args, body, decorator_list, null, null, type_params, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_AsyncFunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedExpr? returns, string? type_comment, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAsyncFunctionDef();
            node.Name = name;
            node.Args = args;
            node.Body = body;
            node.DecoratorList = decorator_list;
            node.Returns = returns;
            node.TypeComment = type_comment;
            node.TypeParams = type_params;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AsyncFunctionDef(string name, GeneratedArguments args, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_AsyncFunctionDef(name, args, body, decorator_list, null, null, type_params, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_ClassDef(string name, GeneratedExprSeq bases, GeneratedKeywordSeq keywords, GeneratedStmtSeq body, GeneratedExprSeq decorator_list, GeneratedTypeParamSeq type_params, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedClassDef();
            node.Name = name;
            node.Bases = bases;
            node.Keywords = keywords;
            node.Body = body;
            node.DecoratorList = decorator_list;
            node.TypeParams = type_params;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Return(GeneratedExpr? value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedReturn();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Return(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Return(null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Delete(GeneratedExprSeq targets, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedDelete();
            node.Targets = targets;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Assign(GeneratedExprSeq targets, GeneratedExpr value, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAssign();
            node.Targets = targets;
            node.Value = value;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Assign(GeneratedExprSeq targets, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Assign(targets, value, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_TypeAlias(GeneratedExpr name, GeneratedTypeParamSeq type_params, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTypeAlias();
            node.Name = name;
            node.TypeParams = type_params;
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AugAssign(GeneratedExpr target, GeneratedOperator op, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAugAssign();
            node.Target = target;
            node.Op = op;
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AnnAssign(GeneratedExpr target, GeneratedExpr annotation, GeneratedExpr? value, int simple, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAnnAssign();
            node.Target = target;
            node.Annotation = annotation;
            node.Value = value;
            node.Simple = simple;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AnnAssign(GeneratedExpr target, GeneratedExpr annotation, int simple, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_AnnAssign(target, annotation, null, simple, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_For(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedFor();
            node.Target = target;
            node.Iter = iter;
            node.Body = body;
            node.Orelse = orelse;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_For(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_For(target, iter, body, orelse, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_AsyncFor(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAsyncFor();
            node.Target = target;
            node.Iter = iter;
            node.Body = body;
            node.Orelse = orelse;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AsyncFor(GeneratedExpr target, GeneratedExpr iter, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_AsyncFor(target, iter, body, orelse, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_While(GeneratedExpr test, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedWhile();
            node.Test = test;
            node.Body = body;
            node.Orelse = orelse;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_If(GeneratedExpr test, GeneratedStmtSeq body, GeneratedStmtSeq orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedIf();
            node.Test = test;
            node.Body = body;
            node.Orelse = orelse;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_With(GeneratedWithitemSeq items, GeneratedStmtSeq body, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedWith();
            node.Items = items;
            node.Body = body;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_With(GeneratedWithitemSeq items, GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_With(items, body, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_AsyncWith(GeneratedWithitemSeq items, GeneratedStmtSeq body, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAsyncWith();
            node.Items = items;
            node.Body = body;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_AsyncWith(GeneratedWithitemSeq items, GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_AsyncWith(items, body, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Match(GeneratedExpr subject, GeneratedMatchCaseSeq cases, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatch();
            node.Subject = subject;
            node.Cases = cases;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Raise(GeneratedExpr? exc, GeneratedExpr? cause, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedRaise();
            node.Exc = exc;
            node.Cause = cause;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Raise(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Raise(null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Try(GeneratedStmtSeq body, GeneratedExcepthandlerSeq handlers, GeneratedStmtSeq orelse, GeneratedStmtSeq finalbody, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTry();
            node.Body = body;
            node.Handlers = handlers;
            node.Orelse = orelse;
            node.Finalbody = finalbody;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_TryStar(GeneratedStmtSeq body, GeneratedExcepthandlerSeq handlers, GeneratedStmtSeq orelse, GeneratedStmtSeq finalbody, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTryStar();
            node.Body = body;
            node.Handlers = handlers;
            node.Orelse = orelse;
            node.Finalbody = finalbody;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Assert(GeneratedExpr test, GeneratedExpr? msg, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAssert();
            node.Test = test;
            node.Msg = msg;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Assert(GeneratedExpr test, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Assert(test, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Import(GeneratedAliasSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedImport();
            node.Names = names;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_ImportFrom(string? module, GeneratedAliasSeq names, int? level, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedImportFrom();
            node.Module = module;
            node.Names = names;
            node.Level = level;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_ImportFrom(GeneratedAliasSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_ImportFrom(null, names, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedStmt _PyAST_Global(GeneratedIdentifierSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedGlobal();
            node.Names = names;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Nonlocal(GeneratedIdentifierSeq names, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedNonlocal();
            node.Names = names;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Expr(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedExprStmt();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Pass(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedPass.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Break(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBreak.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedStmt _PyAST_Continue(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedContinue.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_BoolOp(GeneratedBoolop op, GeneratedExprSeq values, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedBoolOp();
            node.Op = op;
            node.Values = values;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_NamedExpr(GeneratedExpr target, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedNamedExpr();
            node.Target = target;
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_BinOp(GeneratedExpr left, GeneratedOperator op, GeneratedExpr right, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedBinOp();
            node.Left = left;
            node.Op = op;
            node.Right = right;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_UnaryOp(GeneratedUnaryop op, GeneratedExpr operand, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedUnaryOp();
            node.Op = op;
            node.Operand = operand;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Lambda(GeneratedArguments args, GeneratedExpr body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedLambda();
            node.Args = args;
            node.Body = body;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_IfExp(GeneratedExpr test, GeneratedExpr body, GeneratedExpr orelse, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedIfExp();
            node.Test = test;
            node.Body = body;
            node.Orelse = orelse;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Dict(GeneratedExprSeq keys, GeneratedExprSeq values, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedDict();
            node.Keys = keys;
            node.Values = values;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Set(GeneratedExprSeq elts, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedSet();
            node.Elts = elts;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_ListComp(GeneratedExpr elt, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedListComp();
            node.Elt = elt;
            node.Generators = generators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_SetComp(GeneratedExpr elt, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedSetComp();
            node.Elt = elt;
            node.Generators = generators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_DictComp(GeneratedExpr key, GeneratedExpr value, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedDictComp();
            node.Key = key;
            node.Value = value;
            node.Generators = generators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_GeneratorExp(GeneratedExpr elt, GeneratedComprehensionSeq generators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedGeneratorExp();
            node.Elt = elt;
            node.Generators = generators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Await(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAwait();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Yield(GeneratedExpr? value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedYield();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Yield(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Yield(null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr _PyAST_YieldFrom(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedYieldFrom();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Compare(GeneratedExpr left, GeneratedCmpopSeq ops, GeneratedExprSeq comparators, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedCompare();
            node.Left = left;
            node.Ops = ops;
            node.Comparators = comparators;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Call(GeneratedExpr func, GeneratedExprSeq args, GeneratedKeywordSeq keywords, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedCall();
            node.Func = func;
            node.Args = args;
            node.Keywords = keywords;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_FormattedValue(GeneratedExpr value, int conversion, GeneratedExpr? format_spec, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedFormattedValue();
            node.Value = value;
            node.Conversion = conversion;
            node.FormatSpec = format_spec;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_FormattedValue(GeneratedExpr value, int conversion, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_FormattedValue(value, conversion, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr _PyAST_JoinedStr(GeneratedExprSeq values, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedJoinedStr();
            node.Values = values;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Constant(GeneratedPyConstant value, string? kind, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedConstant();
            node.Value = value;
            node.Kind = kind;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Constant(GeneratedPyConstant value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Constant(value, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExpr _PyAST_Attribute(GeneratedExpr value, string attr, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAttribute();
            node.Value = value;
            node.Attr = attr;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Subscript(GeneratedExpr value, GeneratedExpr slice, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedSubscript();
            node.Value = value;
            node.Slice = slice;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Starred(GeneratedExpr value, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedStarred();
            node.Value = value;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Name(string id, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedName();
            node.Id = id;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_List(GeneratedExprSeq elts, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedList();
            node.Elts = elts;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Tuple(GeneratedExprSeq elts, GeneratedExprContext ctx, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTuple();
            node.Elts = elts;
            node.Ctx = ctx;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Slice(GeneratedExpr? lower, GeneratedExpr? upper, GeneratedExpr? step, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedSlice();
            node.Lower = lower;
            node.Upper = upper;
            node.Step = step;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExpr _PyAST_Slice(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_Slice(null, null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedExprContext _PyAST_Load(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLoad.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExprContext _PyAST_Store(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedStore.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExprContext _PyAST_Del(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedDel.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedBoolop _PyAST_And(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedAnd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedBoolop _PyAST_Or(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedOr.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Add(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedAdd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Sub(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedSub.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Mult(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedMult.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_MatMult(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedMatMult.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Div(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedDiv.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Mod(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedMod_.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_Pow(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedPow.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_LShift(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLShift.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_RShift(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedRShift.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_BitOr(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBitOr.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_BitXor(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBitXor.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_BitAnd(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedBitAnd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedOperator _PyAST_FloorDiv(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedFloorDiv.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop _PyAST_Invert(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedInvert.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop _PyAST_Not(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedNot.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop _PyAST_UAdd(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedUAdd.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedUnaryop _PyAST_USub(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedUSub.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_Eq(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedEq.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_NotEq(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedNotEq.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_Lt(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLt.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_LtE(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedLtE.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_Gt(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedGt.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_GtE(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedGtE.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_Is(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedIs.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_IsNot(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedIsNot.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_In(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedIn.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedCmpop _PyAST_NotIn(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = GeneratedNotIn.Instance;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedComprehension _PyAST_comprehension(GeneratedExpr target, GeneratedExpr iter, GeneratedExprSeq ifs, int is_async)
        {
            var node = new GeneratedComprehension();
            node.Target = target;
            node.Iter = iter;
            node.Ifs = ifs;
            node.IsAsync = is_async;
            return node;
        }

        public static GeneratedExcepthandler _PyAST_ExceptHandler(GeneratedExpr? type, string? name, GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedExceptHandler();
            node.Type = type;
            node.Name = name;
            node.Body = body;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedExcepthandler _PyAST_ExceptHandler(GeneratedStmtSeq body, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_ExceptHandler(null, null, body, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedArguments _PyAST_arguments(GeneratedArgSeq posonlyargs, GeneratedArgSeq args, GeneratedArg? vararg, GeneratedArgSeq kwonlyargs, GeneratedExprSeq kw_defaults, GeneratedArg? kwarg, GeneratedExprSeq defaults)
        {
            var node = new GeneratedArguments();
            node.Posonlyargs = posonlyargs;
            node.Args = args;
            node.Vararg = vararg;
            node.Kwonlyargs = kwonlyargs;
            node.KwDefaults = kw_defaults;
            node.Kwarg = kwarg;
            node.Defaults = defaults;
            return node;
        }

        public static GeneratedArguments _PyAST_arguments(GeneratedArgSeq posonlyargs, GeneratedArgSeq args, GeneratedArgSeq kwonlyargs, GeneratedExprSeq kw_defaults, GeneratedExprSeq defaults)
        {
            return _PyAST_arguments(posonlyargs, args, null, kwonlyargs, kw_defaults, null, defaults);
        }

        public static GeneratedArg _PyAST_arg(string arg, GeneratedExpr? annotation, string? type_comment, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedArg();
            node.Arg = arg;
            node.Annotation = annotation;
            node.TypeComment = type_comment;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedArg _PyAST_arg(string arg, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_arg(arg, null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedKeyword _PyAST_keyword(string? arg, GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedKeyword();
            node.Arg = arg;
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedKeyword _PyAST_keyword(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_keyword(null, value, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedAlias _PyAST_alias(string name, string? asname, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedAlias();
            node.Name = name;
            node.Asname = asname;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedAlias _PyAST_alias(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_alias(name, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedWithitem _PyAST_withitem(GeneratedExpr context_expr, GeneratedExpr? optional_vars)
        {
            var node = new GeneratedWithitem();
            node.ContextExpr = context_expr;
            node.OptionalVars = optional_vars;
            return node;
        }

        public static GeneratedWithitem _PyAST_withitem(GeneratedExpr context_expr)
        {
            return _PyAST_withitem(context_expr, null);
        }

        public static GeneratedMatchCase _PyAST_match_case(GeneratedPattern pattern, GeneratedExpr? guard, GeneratedStmtSeq body)
        {
            var node = new GeneratedMatchCase();
            node.Pattern = pattern;
            node.Guard = guard;
            node.Body = body;
            return node;
        }

        public static GeneratedMatchCase _PyAST_match_case(GeneratedPattern pattern, GeneratedStmtSeq body)
        {
            return _PyAST_match_case(pattern, null, body);
        }

        public static GeneratedPattern _PyAST_MatchValue(GeneratedExpr value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchValue();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchSingleton(GeneratedPyConstant value, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchSingleton();
            node.Value = value;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchSequence(GeneratedPatternSeq patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchSequence();
            node.Patterns = patterns;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchMapping(GeneratedExprSeq keys, GeneratedPatternSeq patterns, string? rest, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchMapping();
            node.Keys = keys;
            node.Patterns = patterns;
            node.Rest = rest;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchMapping(GeneratedExprSeq keys, GeneratedPatternSeq patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_MatchMapping(keys, patterns, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern _PyAST_MatchClass(GeneratedExpr cls, GeneratedPatternSeq patterns, GeneratedIdentifierSeq kwd_attrs, GeneratedPatternSeq kwd_patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchClass();
            node.Cls = cls;
            node.Patterns = patterns;
            node.KwdAttrs = kwd_attrs;
            node.KwdPatterns = kwd_patterns;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchStar(string? name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchStar();
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchStar(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_MatchStar(null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern _PyAST_MatchAs(GeneratedPattern? pattern, string? name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchAs();
            node.Pattern = pattern;
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedPattern _PyAST_MatchAs(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_MatchAs(null, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedPattern _PyAST_MatchOr(GeneratedPatternSeq patterns, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedMatchOr();
            node.Patterns = patterns;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedTypeIgnore _PyAST_TypeIgnore(int lineno, string tag)
        {
            var node = new GeneratedTypeIgnoreNode();
            node.Lineno = lineno;
            node.Tag = tag;
            return node;
        }

        public static GeneratedTypeParam _PyAST_TypeVar(string name, GeneratedExpr? bound, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTypeVar();
            node.Name = name;
            node.Bound = bound;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedTypeParam _PyAST_TypeVar(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return _PyAST_TypeVar(name, null, lineno, col_offset, end_lineno, end_col_offset);
        }

        public static GeneratedTypeParam _PyAST_ParamSpec(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedParamSpec();
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }

        public static GeneratedTypeParam _PyAST_TypeVarTuple(string name, int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var node = new GeneratedTypeVarTuple();
            node.Name = name;
            node.LineNo = lineno;
            node.ColOffset = col_offset;
            node.EndLineNo = end_lineno ?? 0;
            node.EndColOffset = end_col_offset ?? 0;
            return node;
        }


    }

    // ============================================================
    // _PyPegen_* Helper Functions
    // ============================================================

    /// <summary>
    /// PEG parser helper functions - CPython 3.12: Parser/pegen.c
    /// </summary>
    public static partial class PegenHelpers
    {
        // CPython: _PyPegen_seq_flatten
        // Flatten list of sequences into single sequence
        // CPython 3.12: Returns NULL if total size is 0 (assert fails in CPython)
        public static GeneratedStmtSeq? _PyPegen_seq_flatten(System.Collections.Generic.List<GeneratedStmtSeq> sequences)
        {
            // CPython: Calculate flattened size
            int totalSize = 0;
            foreach (var seq in sequences)
            {
                totalSize += seq.Count;
            }

            // CPython: assert(flattened_seq_size > 0)
            if (totalSize == 0)
            {
                return null;  // CPython would assert fail
            }

            // CPython: Allocate and flatten
            var result = new GeneratedStmtSeq(totalSize);
            foreach (var seq in sequences)
            {
                result.AddRange(seq);
            }
            return result;
        }

        // CPython: _PyPegen_singleton_seq
        public static GeneratedStmtSeq _PyPegen_singleton_seq(GeneratedStmt item)
        {
            var seq = new GeneratedStmtSeq(1);
            seq.Add(item);
            return seq;
        }

        public static GeneratedExprSeq _PyPegen_singleton_seq(GeneratedExpr item)
        {
            var seq = new GeneratedExprSeq(1);
            seq.Add(item);
            return seq;
        }

        public static GeneratedAliasSeq _PyPegen_singleton_seq(GeneratedAlias item)
        {
            var seq = new GeneratedAliasSeq(1);
            seq.Add(item);
            return seq;
        }

        // For grammar helper types like SlashWithDefault
        public static GeneratedSeq _PyPegen_singleton_seq(GeneratedSlashWithDefault item)
        {
            var seq = new GeneratedSeq(1);
            seq.Add(item);
            return seq;
        }

        // CPython: _PyPegen_seq_insert_in_front
        // Typed sequence versions - same type input and output
        public static GeneratedExprSeq _PyPegen_seq_insert_in_front(GeneratedExpr item, GeneratedExprSeq seq)
        {
            var newSeq = new GeneratedExprSeq(seq.Count + 1);
            newSeq.Add(item);
            newSeq.AddRange(seq);
            return newSeq;
        }

        public static GeneratedPatternSeq _PyPegen_seq_insert_in_front(GeneratedPattern item, GeneratedPatternSeq seq)
        {
            var newSeq = new GeneratedPatternSeq(seq.Count + 1);
            newSeq.Add(item);
            newSeq.AddRange(seq);
            return newSeq;
        }

        // CPython: _PyPegen_seq_count_dots
        public static int _PyPegen_seq_count_dots(GeneratedIdentifierSeq? seq)
        {
            return seq?.Count ?? 0;
        }

        // CPython: _PyPegen_seq_count_dots (token list overload)
        public static int _PyPegen_seq_count_dots(System.Collections.Generic.List<GeneratedTokenInfo>? tokens)
        {
            if (tokens == null) return 0;
            int count = 0;
            foreach (var token in tokens)
            {
                // '.' counts as 1, '...' (ELLIPSIS) counts as 3
                if (token.Value == "...")
                    count += 3;
                else
                    count += 1;
            }
            return count;
        }

        // CPython: _PyPegen_map_names_to_ids
        // Extract identifier strings from NAME tokens
        public static GeneratedIdentifierSeq _PyPegen_map_names_to_ids(System.Collections.Generic.List<GeneratedTokenInfo> tokens)
        {
            var ids = new GeneratedIdentifierSeq(tokens.Count);
            foreach (var token in tokens)
            {
                ids.Add(token.Value ?? string.Empty);
            }
            return ids;
        }

        // CPython: _PyPegen_map_names_to_ids
        // Extract identifier strings from Name expressions
        public static GeneratedIdentifierSeq _PyPegen_map_names_to_ids(GeneratedExprSeq names)
        {
            var ids = new GeneratedIdentifierSeq(names.Count);
            foreach (var name in names)
            {
                if (name is GeneratedName nameExpr)
                {
                    ids.Add(nameExpr.Id);
                }
            }
            return ids;
        }

        // CPython: _PyPegen_alias_for_star
        public static GeneratedAlias _PyPegen_alias_for_star(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            var alias = new GeneratedAlias();
            alias.Name = "*";
            alias.Asname = null;
            alias.LineNo = lineno;
            alias.ColOffset = col_offset;
            alias.EndLineNo = end_lineno ?? 0;
            alias.EndColOffset = end_col_offset ?? 0;
            return alias;
        }

        // CPython: _PyPegen_empty_arguments
        public static GeneratedArguments _PyPegen_empty_arguments()
        {
            var args = new GeneratedArguments();
            args.Posonlyargs = GeneratedArgSeq.Empty;
            args.Args = GeneratedArgSeq.Empty;
            args.Kwonlyargs = GeneratedArgSeq.Empty;
            args.KwDefaults = GeneratedExprSeq.Empty;
            args.Defaults = GeneratedExprSeq.Empty;
            return args;
        }

        // CPython: _PyPegen_set_expr_context
        public static GeneratedExpr _PyPegen_set_expr_context(GeneratedExpr expr, GeneratedExprContext ctx)
        {
            // Update expr context based on type
            switch (expr)
            {
                case GeneratedName name:
                    name.Ctx = ctx;
                    break;
                case GeneratedAttribute attr:
                    attr.Ctx = ctx;
                    break;
                case GeneratedSubscript subscript:
                    subscript.Ctx = ctx;
                    break;
                case GeneratedList list:
                    list.Ctx = ctx;
                    foreach (var elt in list.Elts.ToEnumerable<GeneratedExpr>())
                    {
                        _PyPegen_set_expr_context(elt, ctx);
                    }
                    break;
                case GeneratedTuple tuple:
                    tuple.Ctx = ctx;
                    foreach (var elt in tuple.Elts.ToEnumerable<GeneratedExpr>())
                    {
                        _PyPegen_set_expr_context(elt, ctx);
                    }
                    break;
                case GeneratedStarred starred:
                    starred.Ctx = ctx;
                    _PyPegen_set_expr_context(starred.Value, ctx);
                    break;
            }
            return expr;
        }

        // CPython: _PyPegen_make_module
        public static GeneratedModule _PyPegen_make_module(GeneratedStmtSeq? body)
        {
            var module = new GeneratedModule();
            module.Body = body ?? GeneratedStmtSeq.Empty;
            module.TypeIgnores = GeneratedTypeIgnoreSeq.Empty;
            return module;
        }

        // Helper: Decode string literal (remove quotes, handle escapes)
        public static string DecodeStringLiteral(string literal)
        {
            if (string.IsNullOrEmpty(literal))
                return string.Empty;

            // Handle string prefixes: r, b, u, f, etc.
            var workingLiteral = literal;
            var isRaw = false;
            while (workingLiteral.Length > 0 && char.IsLetter(workingLiteral[0]))
            {
                var prefix = char.ToLower(workingLiteral[0]);
                if (prefix == 'r')
                    isRaw = true;
                workingLiteral = workingLiteral.Substring(1);
            }

            // Remove quotes: \"hello\" -> hello, 'world' -> world
            if (workingLiteral.Length >= 2)
            {
                // Triple-quoted strings
                if (workingLiteral.StartsWith("\"\"\"") || workingLiteral.StartsWith("'''"))
                {
                    if (workingLiteral.Length >= 6)
                        workingLiteral = workingLiteral.Substring(3, workingLiteral.Length - 6);
                }
                // Single/double-quoted strings
                else if (workingLiteral.StartsWith("\"") || workingLiteral.StartsWith("'"))
                {
                    workingLiteral = workingLiteral.Substring(1, workingLiteral.Length - 2);
                }
            }

            // Handle escape sequences (unless raw string)
            if (!isRaw && workingLiteral.Contains("\\"))
            {
                workingLiteral = workingLiteral
                    .Replace("\\n", "\n")
                    .Replace("\\t", "\t")
                    .Replace("\\r", "\r")
                    .Replace("\\\\", "\\")
                    .Replace("\\\"", "\"")
                    .Replace("\\'", "'");
            }

            return workingLiteral;
        }

        // CPython: _PyPegen_seq_append_to_end
        public static GeneratedExprSeq _PyPegen_seq_append_to_end(GeneratedExprSeq seq, GeneratedExpr item)
        {
            var newSeq = new GeneratedExprSeq(seq.Count + 1);
            newSeq.AddRange(seq);
            newSeq.Add(item);
            return newSeq;
        }

        // CPython: Py_None, Py_True, Py_False, Py_Ellipsis
        // Note: These are constant VALUES for AST construction, not runtime PyObjects
        public static GeneratedPyConstant Py_None => GeneratedPyConstant.None;
        public static GeneratedPyConstant Py_True => GeneratedPyConstant.True;
        public static GeneratedPyConstant Py_False => GeneratedPyConstant.False;
        public static GeneratedPyConstant Py_Ellipsis => GeneratedPyConstant.Ellipsis;

        // CPython: _PyPegen_dummy_name
        public static GeneratedName _PyPegen_dummy_name()
        {
            return new GeneratedName { Id = "_", Ctx = GeneratedStore.Instance };
        }

        // CPython: _PyPegen_get_cmpops
        // Extract comparison operators from (cmpop, expr) pairs
        public static GeneratedCmpopSeq _PyPegen_get_cmpops(GeneratedSeq pairs)
        {
            var ops = new GeneratedCmpopSeq();
            // TODO: Extract cmpops from pairs - needs pair structure definition
            return ops;
        }

        // Overload for AstNodeSeq
        public static GeneratedCmpopSeq _PyPegen_get_cmpops(GeneratedAstNodeSeq pairs)
        {
            var seq = new GeneratedSeq();
            seq.AddRange(pairs);
            return _PyPegen_get_cmpops(seq);
        }

        // CPython: _PyPegen_get_exprs
        // Extract expressions from (cmpop, expr) pairs
        public static GeneratedExprSeq _PyPegen_get_exprs(GeneratedSeq pairs)
        {
            var exprs = new GeneratedExprSeq();
            // TODO: Extract exprs from pairs - needs pair structure definition
            return exprs;
        }

        // Overload for AstNodeSeq
        public static GeneratedExprSeq _PyPegen_get_exprs(GeneratedAstNodeSeq pairs)
        {
            var seq = new GeneratedSeq();
            seq.AddRange(pairs);
            return _PyPegen_get_exprs(seq);
        }

        // CPython: _PyPegen_key_value_pair
        // Create a key-value pair for dictionary literals
        public static GeneratedKeyValuePair _PyPegen_key_value_pair(GeneratedExpr? key, GeneratedExpr value)
        {
            return new GeneratedKeyValuePair { Key = key ?? value, Value = value };
        }

        // CPython: _PyPegen_get_keys
        // Extract keys from (key, value) pairs
        public static GeneratedExprSeq _PyPegen_get_keys(GeneratedSeq pairs)
        {
            var keys = new GeneratedExprSeq();
            foreach (var pair in pairs)
            {
                if (pair is GeneratedKeyValuePair kvp)
                {
                    keys.Add(kvp.Key);
                }
            }
            return keys;
        }

        // CPython: _PyPegen_get_values
        // Extract values from (key, value) pairs
        public static GeneratedExprSeq _PyPegen_get_values(GeneratedSeq pairs)
        {
            var values = new GeneratedExprSeq();
            foreach (var pair in pairs)
            {
                if (pair is GeneratedKeyValuePair kvp)
                {
                    values.Add(kvp.Value);
                }
            }
            return values;
        }

        // CPython: _PyPegen_seq_extract_starred_exprs
        // Extract starred expressions from KeywordOrStarred sequence
        public static GeneratedExprSeq _PyPegen_seq_extract_starred_exprs(GeneratedKeywordOrStarredSeq seq)
        {
            var exprs = new GeneratedExprSeq();
            foreach (var item in seq.ToEnumerable<GeneratedKeywordOrStarred>())
            {
                if (item.Starred != null)
                {
                    exprs.Add(item.Starred);
                }
            }
            return exprs;
        }

        // CPython: _PyPegen_seq_delete_starred_exprs
        // Extract keywords (non-starred items) from KeywordOrStarred sequence
        public static GeneratedKeywordSeq _PyPegen_seq_delete_starred_exprs(GeneratedKeywordOrStarredSeq seq)
        {
            var keywords = new GeneratedKeywordSeq();
            foreach (var item in seq.ToEnumerable<GeneratedKeywordOrStarred>())
            {
                if (item.Keyword != null)
                {
                    keywords.Add(item.Keyword);
                }
            }
            return keywords;
        }

        // Overload for MixedSeq
        public static GeneratedExprSeq _PyPegen_seq_extract_starred_exprs(GeneratedSeq seq)
        {
            var kwSeq = new GeneratedKeywordOrStarredSeq();
            kwSeq.AddRange(seq.Cast<GeneratedKeywordOrStarred>());
            return _PyPegen_seq_extract_starred_exprs(kwSeq);
        }

        public static GeneratedKeywordSeq _PyPegen_seq_delete_starred_exprs(GeneratedSeq seq)
        {
            var kwSeq = new GeneratedKeywordOrStarredSeq();
            kwSeq.AddRange(seq.Cast<GeneratedKeywordOrStarred>());
            return _PyPegen_seq_delete_starred_exprs(kwSeq);
        }

        // Conversion: GeneratedAstNodeSeq to GeneratedSeq
        public static GeneratedSeq ToMixedSeq(GeneratedAstNodeSeq inputSeq)
        {
            var seq = new GeneratedSeq();
            seq.AddRange(inputSeq.ToRawList());
            return seq;
        }

        // Conversion: GeneratedKeywordOrStarredSeq to GeneratedSeq
        public static GeneratedSeq ToMixedSeq(GeneratedKeywordOrStarredSeq inputSeq)
        {
            var seq = new GeneratedSeq();
            seq.AddRange(inputSeq.ToRawList());
            return seq;
        }

        // CPython: _PyPegen_check_legacy_stmt
        // Check if NAME is 'print' or 'exec' (legacy Python 2 statements)
        public static bool CheckLegacyStmt(GeneratedExpr? expr)
        {
            if (expr is not GeneratedName name)
            {
                return false;
            }

            var id = name.Id;
            return id == "print" || id == "exec";
        }

        // Legacy alias for backward compatibility
        public static bool _PyPegen_check_legacy_stmt(GeneratedExpr? expr) => CheckLegacyStmt(expr);

        // CPython: RAISE_SYNTAX_ERROR_KNOWN_RANGE macro
        // Raise syntax error with range information
        public static GeneratedPtr? RaiseSyntaxErrorKnownRange(GeneratedAstNode? start, GeneratedAstNode? end, string message)
        {
            // Extract location info from start/end nodes
            var startLine = start?.LineNo ?? 1;
            var startCol = start?.ColOffset ?? 0;
            var endLine = end?.EndLineNo ?? startLine;
            var endCol = end?.EndColOffset ?? startCol;

            // Format message with location info
            var fullMessage = $"{message} (line {startLine}, col {startCol})";
            throw new PySyntaxErrorException(fullMessage);
        }

        // CPython: CHECK(type, expr) macro
        // Null-check and cast - throws if null, otherwise returns typed result
        public static T CHECK<T>(T? value) where T : class
        {
            if (value == null)
            {
                throw new PySyntaxErrorException("CHECK failed: unexpected null value");
            }
            return value;
        }

        /// <summary>
        /// CPython 3.12: CHECK_NULL_ALLOWED - allows NULL return without error
        /// Used for helper functions like _PyPegen_seq_extract_starred_exprs
        /// Returns nullable type - NULL is valid if no error occurred
        /// </summary>
        public static T? CHECK_NULL_ALLOWED<T>(T? value) where T : class
        {
            // CPython: if (result == NULL && PyErr_Occurred()) p->error_indicator = 1;
            // In C#: We don't set error_indicator here, just return null
            // The caller is responsible for checking null and handling it
            return value;
        }

    }


    // ============================================================
    // Parser Helper Types
    // CPython 3.12: Parser-specific types (not in ASDL)
    // Note: Some types (SlashWithDefault, StarEtc, KeywordOrStarred, KeyValuePair) are already defined in GeneratedAstTypes.cs
    // ============================================================

    public class GeneratedNameDefaultPair : GeneratedPtr
    {
        public GeneratedArg Arg { get; set; }
        public GeneratedExpr Default { get; set; }
        public string? TypeComment { get; set; }
    }

    public class GeneratedKeyPatternPair : GeneratedPtr
    {
        public GeneratedExpr Key { get; set; }
        public GeneratedPattern Pattern { get; set; }
    }

    public class GeneratedCmpopExprPair : GeneratedPtr
    {
        public GeneratedCmpop Op { get; set; }
        public GeneratedExpr Expr { get; set; }
    }

    public class GeneratedResultTokenWithMetadata : GeneratedPtr
    {
        public GeneratedTokenInfo Token { get; set; }
        public object? Metadata { get; set; }
    }

    // ============================================================
    // ASTHelpers - Utility functions
    // ============================================================

    /// <summary>
    /// Helper functions for AST manipulation
    /// </summary>
    public static class ASTHelpers
    {
        public static string ExtractStringValue(GeneratedExpr expr)
        {
            if (expr is GeneratedName name)
            {
                return name.Id;
            }
            throw new InvalidOperationException("Expected Name expression");
        }

        public static GeneratedOperator ExtractOpKind(GeneratedAstNode op)
        {
            // Extract operator kind from AST node
            if (op is GeneratedOperator opNode) return opNode;
            // TODO: Implement more comprehensive operator extraction
            return GeneratedAdd.Instance;
        }

        public static GeneratedExprSeq ExtractCallArgs(GeneratedExpr call)
        {
            if (call is GeneratedCall callExpr)
            {
                return callExpr.Args;
            }
            return GeneratedExprSeq.Empty;
        }

        public static GeneratedKeywordSeq ExtractCallKeywords(GeneratedExpr call)
        {
            if (call is GeneratedCall callExpr)
            {
                return callExpr.Keywords;
            }
            return GeneratedKeywordSeq.Empty;
        }

    }

}
