// Generated Parser from python_py.gram
// CPython 3.12 compatible - Auto-generated, DO NOT EDIT

using System;
using System.Collections.Generic;
using SharpPy.Generated;
using static SharpPy.Generated.PyParserRuntime;

namespace SharpPy.Generated
{
    /// <summary>
    /// PEG Parser for Python 3.12
    /// Generated from python_py.gram
    /// </summary>
    public partial class PyParser : PyParserBase<GeneratedPtr>
    {
        // ============================================================
        // Parser State
        // ============================================================

        // Note: This is a partial class - other parts are in PyParserBase.cs
        // Token list and position tracking

        /// <summary>
        /// Constructor - CPython: _PyPegen_Parser_New
        /// </summary>
        public PyParser(List<GeneratedTokenInfo> tokens, string filename)
            : base(tokens, filename)
        {
        }

        /// <summary>
        /// Constructor with source code for error reporting
        /// </summary>
        public PyParser(List<GeneratedTokenInfo> tokens, string filename, string source)
            : base(tokens, filename, source)
        {
        }

        // ============================================================
        // Entry Point
        // ============================================================

        /// <summary>
        /// Main entry point for parsing - CPython: _PyPegen_run_parser
        /// </summary>
        public override GeneratedPtr Parse()
        {
            // Entry point: parse file (CPython: start rule)
            var result = Parse_File();
            if (result == null)
            {
                throw new Exception("Parse failed at top level");
            }
            return result;
        }

        // ============================================================
        // Grammar Rules (244 rules from python_cs.gram)
        // ============================================================

        /// <summary>
        /// Rule: file
        /// Alternatives: 1
        /// Return Type: GeneratedMod
        /// </summary>
        private GeneratedMod? Parse_File()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] file at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if (
                    ((a = (GeneratedStmtSeq)ParseOptional(() => Parse_Statements())) == null || true) &&
                    ExpectToken(PyToken.Type.ENDMARKER) != null
                )
                {
                    // Action code from grammar
                    return ( GeneratedMod ) PyParserHelpers . MakeModule (( GeneratedStmtSeq ?) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: interactive
        /// Alternatives: 1
        /// Return Type: GeneratedMod
        /// </summary>
        private GeneratedMod? Parse_Interactive()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] interactive at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if ((a = Parse_StatementNewline()) != null)
                {
                    // Action code from grammar
                    return ( GeneratedMod ) PyAst . Interactive (( GeneratedStmtSeq ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: eval
        /// Alternatives: 1
        /// Return Type: GeneratedMod
        /// </summary>
        private GeneratedMod? Parse_Eval()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] eval at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_Expressions()) != null &&
                    ParseZeroOrMore(() => ExpectToken(PyToken.Type.NEWLINE)) != null &&
                    ExpectToken(PyToken.Type.ENDMARKER) != null
                )
                {
                    // Action code from grammar
                    return ( GeneratedMod ) PyAst . Expression (( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: func_type
        /// Alternatives: 1
        /// Return Type: GeneratedMod
        /// </summary>
        private GeneratedMod? Parse_FuncType()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] func_type at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;
                GeneratedExpr? b = null;

                if (
                    ExpectOp("(") != null &&
                    ((a = (GeneratedExprSeq)ParseOptional(() => Parse_TypeExpressions())) == null || true) &&
                    ExpectOp(")") != null &&
                    ExpectOp("->") != null &&
                    (b = Parse_Expression()) != null &&
                    ParseZeroOrMore(() => ExpectToken(PyToken.Type.NEWLINE)) != null &&
                    ExpectToken(PyToken.Type.ENDMARKER) != null
                )
                {
                    // Action code from grammar
                    return ( GeneratedMod ) PyAst . FunctionType (( GeneratedExprSeq ?) a ,( GeneratedExpr ) b );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: statements
        /// Alternatives: 1
        /// Return Type: GeneratedStmtSeq
        /// </summary>
        private GeneratedStmtSeq? Parse_Statements()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] statements at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if ((a = ParseOneOrMore(() => Parse_Statement())) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . FlattenStatementSequence ( a ). Cast < GeneratedStmtSeq >();
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: statement
        /// Alternatives: 2
        /// Return Type: GeneratedStmtSeq
        /// </summary>
        private GeneratedStmtSeq? Parse_Statement()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] statement at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if ((a = Parse_CompoundStmt()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . SingletonSequence (( GeneratedStmt ) a ). Cast < GeneratedStmtSeq >();
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if ((a = Parse_SimpleStmts()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: statement_newline
        /// Alternatives: 4
        /// Return Type: GeneratedStmtSeq
        /// </summary>
        private GeneratedStmtSeq? Parse_StatementNewline()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] statement_newline at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (
                    (a = Parse_CompoundStmt()) != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SingletonSequence (( GeneratedStmt ) a ). Cast < GeneratedStmtSeq >();
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? _alt_var = null;

                if ((_alt_var = Parse_SimpleStmts()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectToken(PyToken.Type.NEWLINE) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . SingletonSequence ( Check < GeneratedStmt >( PyAst . Pass ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ))). Cast < GeneratedStmtSeq >();
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectToken(PyToken.Type.ENDMARKER) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . InteractiveExit ();
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: simple_stmts
        /// Alternatives: 2
        /// Return Type: GeneratedStmtSeq
        /// </summary>
        private GeneratedStmtSeq? Parse_SimpleStmts()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] simple_stmts at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (
                    (a = Parse_SimpleStmt()) != null &&
                    NegativeLookahead(() => ExpectOp(";")) != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SingletonSequence (( GeneratedStmt ) a ). Cast < GeneratedStmtSeq >();
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(";"), () => Parse_SimpleStmt())) != null &&
                    (ParseOptional(() => ExpectOp(";")) == null || true) &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    return ( GeneratedStmtSeq ) a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: simple_stmt
        /// Alternatives: 14
        /// Return Type: GeneratedStmt
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedStmt? Parse_SimpleStmt()
        {
            return (GeneratedStmt?)TryMemoized("simple_stmt", Parse_SimpleStmt_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: simple_stmt
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedStmt? Parse_SimpleStmt_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] simple_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = Parse_Assignment()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectSoftKeyword("type")) != null &&
                    Parse_TypeAlias() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;

                if ((e = Parse_StarExpressions()) != null)
                {
                    // Action code from grammar
                    return PyAst . Expr (( GeneratedExpr ) e , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectKeyword("return")) != null &&
                    Parse_ReturnStmt() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => Parse_Tmp1()) != null &&
                    Parse_ImportStmt() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectKeyword("raise")) != null &&
                    Parse_RaiseStmt() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("pass") != null)
                {
                    // Action code from grammar
                    return PyAst . Pass ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectKeyword("del")) != null &&
                    Parse_DelStmt() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectKeyword("yield")) != null &&
                    Parse_YieldStmt() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectKeyword("assert")) != null &&
                    Parse_AssertStmt() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 11
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("break") != null)
                {
                    // Action code from grammar
                    return PyAst . Break ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 12
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("continue") != null)
                {
                    // Action code from grammar
                    return PyAst . Continue ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 13
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectKeyword("global")) != null &&
                    Parse_GlobalStmt() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 14
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectKeyword("nonlocal")) != null &&
                    Parse_NonlocalStmt() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: compound_stmt
        /// Alternatives: 8
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_CompoundStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] compound_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (
                    PositiveLookahead(() => Parse_Tmp2()) != null &&
                    (a = Parse_FunctionDef()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (
                    PositiveLookahead(() => ExpectKeyword("if")) != null &&
                    (a = Parse_IfStmt()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (
                    PositiveLookahead(() => Parse_Tmp3()) != null &&
                    (a = Parse_ClassDef()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (
                    PositiveLookahead(() => Parse_Tmp4()) != null &&
                    (a = Parse_WithStmt()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (
                    PositiveLookahead(() => Parse_Tmp5()) != null &&
                    (a = Parse_ForStmt()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (
                    PositiveLookahead(() => ExpectKeyword("try")) != null &&
                    (a = Parse_TryStmt()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (
                    PositiveLookahead(() => ExpectKeyword("while")) != null &&
                    (a = Parse_WhileStmt()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if ((a = Parse_MatchStmt()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: assignment
        /// Alternatives: 5
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_Assignment()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] assignment at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;
                GeneratedPtr? c = null;

                if (
                    (a = ExpectName()) != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Expression()) != null &&
                    ((c = ParseOptional(() => Parse_Tmp6())) == null || true)
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 6 , "Variable annotation syntax is" , PyAst . AnnAssign ( Check < GeneratedExpr >( PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedStore.Instance )),( GeneratedExpr ) b ,( GeneratedExpr ?) c , 1 , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;
                GeneratedExpr? b = null;
                GeneratedPtr? c = null;

                if (
                    (a = Parse_Tmp7()) != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Expression()) != null &&
                    ((c = ParseOptional(() => Parse_Tmp8())) == null || true)
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 6 , "Variable annotations syntax is" , PyAst . AnnAssign (( GeneratedExpr ) a ,( GeneratedExpr ) b ,( GeneratedExpr ?) c , 0 , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;
                GeneratedPtr? b = null;
                GeneratedTokenInfo? tc = null;

                if (
                    (a = ParseOneOrMore(() => Parse_Tmp9())?.Cast<GeneratedExprSeq>()) != null &&
                    (b = Parse_Tmp10()) != null &&
                    NegativeLookahead(() => ExpectOp("=")) != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Assign (( GeneratedExprSeq ) a ,( GeneratedExpr ) b , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedOperator? b = null;
                GeneratedPtr? c = null;

                if (
                    (a = Parse_SingleTarget()) != null &&
                    (b = Parse_Augassign()) != null &&
                    (c = Parse_Tmp11()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . AugAssign (( GeneratedExpr ) a ,( GeneratedOperator ) b ,( GeneratedExpr ) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidAssignment()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: annotated_rhs
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_AnnotatedRhs()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] annotated_rhs at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_YieldExpr()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_StarExpressions()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: augassign
        /// Alternatives: 13
        /// Return Type: GeneratedOperator
        /// </summary>
        private GeneratedOperator? Parse_Augassign()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] augassign at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("+=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedAdd.Instance );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("-=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedSub.Instance );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("*=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedMult.Instance );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("@=") != null)
                {
                    // Action code from grammar
                    return CheckVersion ( 5 , "The '@' operator is" , PyParserHelpers . AugOperator ( GeneratedMatMult.Instance ));
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("/=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedDiv.Instance );
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("%=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedMod_.Instance );
                }
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("&=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedBitAnd.Instance );
                }
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("|=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedBitOr.Instance );
                }
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("^=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedBitXor.Instance );
                }
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("<<=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedLShift.Instance );
                }
            }

            // Alternative 11
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp(">>=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedRShift.Instance );
                }
            }

            // Alternative 12
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("**=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedPow.Instance );
                }
            }

            // Alternative 13
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("//=") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . AugOperator ( GeneratedFloorDiv.Instance );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: return_stmt
        /// Alternatives: 1
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_ReturnStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] return_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("return") != null &&
                    ((a = (GeneratedExpr)ParseOptional(() => Parse_StarExpressions())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Return (( GeneratedExpr ?) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: raise_stmt
        /// Alternatives: 2
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_RaiseStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] raise_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedPtr? b = null;

                if (
                    ExpectKeyword("raise") != null &&
                    (a = Parse_Expression()) != null &&
                    ((b = ParseOptional(() => Parse_Tmp12())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Raise (( GeneratedExpr ) a ,( GeneratedExpr ?) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("raise") != null)
                {
                    // Action code from grammar
                    return PyAst . Raise ( null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: global_stmt
        /// Alternatives: 1
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_GlobalStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] global_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    ExpectKeyword("global") != null &&
                    (a = ParseGatherPlus(() => ExpectOp(","), () => ExpectName())?.Cast<GeneratedExprSeq>()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Global ( Check < GeneratedIdentifierSeq >( PyParserHelpers . MapNamesToIds (( GeneratedExprSeq ) a )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: nonlocal_stmt
        /// Alternatives: 1
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_NonlocalStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] nonlocal_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    ExpectKeyword("nonlocal") != null &&
                    (a = ParseGatherPlus(() => ExpectOp(","), () => ExpectName())?.Cast<GeneratedExprSeq>()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Nonlocal ( Check < GeneratedIdentifierSeq >( PyParserHelpers . MapNamesToIds (( GeneratedExprSeq ) a )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: del_stmt
        /// Alternatives: 2
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_DelStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] del_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    ExpectKeyword("del") != null &&
                    (a = Parse_DelTargets()) != null &&
                    PositiveLookahead(() => Parse_Tmp13()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Delete (( GeneratedExprSeq ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidDelStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: yield_stmt
        /// Alternatives: 1
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_YieldStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] yield_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? y = null;

                if ((y = Parse_YieldExpr()) != null)
                {
                    // Action code from grammar
                    return PyAst . Expr (( GeneratedExpr ) y , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: assert_stmt
        /// Alternatives: 1
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_AssertStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] assert_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedPtr? b = null;

                if (
                    ExpectKeyword("assert") != null &&
                    (a = Parse_Expression()) != null &&
                    ((b = ParseOptional(() => Parse_Tmp14())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Assert (( GeneratedExpr ) a ,( GeneratedExpr ?) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: import_stmt
        /// Alternatives: 3
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_ImportStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] import_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidImport()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = Parse_ImportName()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = Parse_ImportFrom()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: import_name
        /// Alternatives: 1
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_ImportName()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] import_name at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedAliasSeq? a = null;

                if (
                    ExpectKeyword("import") != null &&
                    (a = Parse_DottedAsNames()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Import (( GeneratedAliasSeq ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: import_from
        /// Alternatives: 2
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_ImportFrom()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] import_from at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedExpr? b = null;
                GeneratedAliasSeq? c = null;

                if (
                    ExpectKeyword("from") != null &&
                    (a = ParseZeroOrMore(() => Parse_Tmp15())) != null &&
                    (b = Parse_DottedName()) != null &&
                    ExpectKeyword("import") != null &&
                    (c = Parse_ImportFromTargets()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CheckedFutureImport ( b . GetIdentifier (),( GeneratedAliasSeq ) c , PyParserHelpers . SeqCountDots ( a . ToRawList ()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedAliasSeq? b = null;

                if (
                    ExpectKeyword("from") != null &&
                    (a = ParseOneOrMore(() => Parse_Tmp16())) != null &&
                    ExpectKeyword("import") != null &&
                    (b = Parse_ImportFromTargets()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . ImportFrom ( null ,( GeneratedAliasSeq ) b , PyParserHelpers . SeqCountDots ( a . ToRawList ()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: import_from_targets
        /// Alternatives: 4
        /// Return Type: GeneratedAliasSeq
        /// </summary>
        private GeneratedAliasSeq? Parse_ImportFromTargets()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] import_from_targets at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedAliasSeq? a = null;

                if (
                    ExpectOp("(") != null &&
                    (a = Parse_ImportFromAsNames()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_ImportFromAsNames() != null &&
                    NegativeLookahead(() => ExpectOp(",")) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("*") != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . SingletonSequence ( Check < GeneratedAlias >( PyParserHelpers . AliasForStar ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ))). Cast < GeneratedAliasSeq >();
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedAliasSeq? _alt_var = null;

                if ((_alt_var = (GeneratedAliasSeq)Parse_InvalidImportFromTargets()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: import_from_as_names
        /// Alternatives: 1
        /// Return Type: GeneratedAliasSeq
        /// </summary>
        private GeneratedAliasSeq? Parse_ImportFromAsNames()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] import_from_as_names at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedAliasSeq? a = null;

                if ((a = ParseGatherPlus(() => ExpectOp(","), () => Parse_ImportFromAsName())?.Cast<GeneratedAliasSeq>()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: import_from_as_name
        /// Alternatives: 1
        /// Return Type: GeneratedAlias
        /// </summary>
        private GeneratedAlias? Parse_ImportFromAsName()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] import_from_as_name at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedPtr? b = null;

                if (
                    (a = ExpectName()) != null &&
                    ((b = ParseOptional(() => Parse_Tmp17())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . alias ( a . GetNameValue (), b . GetNameValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: dotted_as_names
        /// Alternatives: 1
        /// Return Type: GeneratedAliasSeq
        /// </summary>
        private GeneratedAliasSeq? Parse_DottedAsNames()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] dotted_as_names at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedAliasSeq? a = null;

                if ((a = ParseGatherPlus(() => ExpectOp(","), () => Parse_DottedAsName())?.Cast<GeneratedAliasSeq>()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: dotted_as_name
        /// Alternatives: 1
        /// Return Type: GeneratedAlias
        /// </summary>
        private GeneratedAlias? Parse_DottedAsName()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] dotted_as_name at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedPtr? b = null;

                if (
                    (a = Parse_DottedName()) != null &&
                    ((b = ParseOptional(() => Parse_Tmp18())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . alias ( a . GetIdentifier (), b . GetNameValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: dotted_name
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_DottedName()
        {
            return (GeneratedExpr?)TryLeftRecursive("dotted_name", Parse_DottedName_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: dotted_name
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_DottedName_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] dotted_name at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = Parse_DottedName()) != null &&
                    ExpectOp(".") != null &&
                    (b = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . JoinNamesWithDot ( a , NameToken ( b ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = ExpectNameExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: block
        /// Alternatives: 3
        /// Return Type: GeneratedStmtSeq
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedStmtSeq? Parse_Block()
        {
            return (GeneratedStmtSeq?)TryMemoized("block", Parse_Block_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: block
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedStmtSeq? Parse_Block_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] block at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if (
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    ExpectToken(PyToken.Type.INDENT) != null &&
                    (a = Parse_Statements()) != null &&
                    ExpectToken(PyToken.Type.DEDENT) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? _alt_var = null;

                if ((_alt_var = Parse_SimpleStmts()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? _alt_var = null;

                if ((_alt_var = (GeneratedStmtSeq)Parse_InvalidBlock()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: decorators
        /// Alternatives: 1
        /// Return Type: GeneratedExprSeq
        /// </summary>
        private GeneratedExprSeq? Parse_Decorators()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] decorators at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = ParseOneOrMore(() => Parse_Tmp19())?.Cast<GeneratedExprSeq>()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: class_def
        /// Alternatives: 2
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_ClassDef()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] class_def at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;
                GeneratedStmt? b = null;

                if (
                    (a = Parse_Decorators()) != null &&
                    (b = Parse_ClassDefRaw()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . ClassDefDecorators ( a , b );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = Parse_ClassDefRaw()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: class_def_raw
        /// Alternatives: 2
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_ClassDefRaw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] class_def_raw at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidClassDefRaw()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTypeParamSeq? t = null;
                GeneratedPtr? b = null;
                GeneratedStmtSeq? c = null;

                if (
                    ExpectKeyword("class") != null &&
                    (a = ExpectName()) != null &&
                    ((t = (GeneratedTypeParamSeq)ParseOptional(() => Parse_TypeParams())) == null || true) &&
                    ((b = ParseOptional(() => Parse_Tmp20())) == null || true) &&
                    ExpectOp(":") != null &&
                    (c = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . ClassDef ( a . GetNameValue (),( b != null )?(( GeneratedCall ) b ). Args : null !,( b != null )?(( GeneratedCall ) b ). Keywords : null !, c , null !, t , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: function_def
        /// Alternatives: 2
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_FunctionDef()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] function_def at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? d = null;
                GeneratedStmt? f = null;

                if (
                    (d = Parse_Decorators()) != null &&
                    (f = Parse_FunctionDefRaw()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . FunctionDefDecorators ( d , f );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = Parse_FunctionDefRaw()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: function_def_raw
        /// Alternatives: 3
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_FunctionDefRaw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] function_def_raw at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidDefRaw()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? n = null;
                GeneratedTypeParamSeq? t = null;
                GeneratedArguments? params_ = null;
                GeneratedPtr? a = null;
                GeneratedTokenInfo? tc = null;
                GeneratedStmtSeq? b = null;

                if (
                    ExpectKeyword("def") != null &&
                    (n = ExpectName()) != null &&
                    ((t = (GeneratedTypeParamSeq)ParseOptional(() => Parse_TypeParams())) == null || true) &&
                    PositiveLookahead(() => PositiveLookahead(() => ExpectOp("("))) != null &&
                    ((params_ = (GeneratedArguments)ParseOptional(() => Parse_Params())) == null || true) &&
                    ExpectOp(")") != null &&
                    ((a = ParseOptional(() => Parse_Tmp21())) == null || true) &&
                    PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => Parse_FuncTypeComment())) == null || true) &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . FunctionDef ( n . GetNameValue (),( params_ != null )? params_ :( GeneratedArguments ) Check < GeneratedArguments >( PyParserHelpers . EmptyArguments ()), b , null ,( GeneratedExpr ?) a , tc . GetCommentValue (), t , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? n = null;
                GeneratedTypeParamSeq? t = null;
                GeneratedArguments? params_ = null;
                GeneratedPtr? a = null;
                GeneratedTokenInfo? tc = null;
                GeneratedStmtSeq? b = null;

                if (
                    ExpectToken(PyToken.Type.ASYNC) != null &&
                    ExpectKeyword("def") != null &&
                    (n = ExpectName()) != null &&
                    ((t = (GeneratedTypeParamSeq)ParseOptional(() => Parse_TypeParams())) == null || true) &&
                    PositiveLookahead(() => PositiveLookahead(() => ExpectOp("("))) != null &&
                    ((params_ = (GeneratedArguments)ParseOptional(() => Parse_Params())) == null || true) &&
                    ExpectOp(")") != null &&
                    ((a = ParseOptional(() => Parse_Tmp22())) == null || true) &&
                    PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => Parse_FuncTypeComment())) == null || true) &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 5 , "Async functions are" , PyAst . AsyncFunctionDef ( n . GetNameValue (),( params_ != null )? params_ :( GeneratedArguments ) Check < GeneratedArguments >( PyParserHelpers . EmptyArguments ()), b , null ,( GeneratedExpr ?) a , tc . GetCommentValue (), t , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: params
        /// Alternatives: 2
        /// Return Type: GeneratedArguments
        /// </summary>
        private GeneratedArguments? Parse_Params()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] params at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if ((a = Parse_InvalidParameters()) != null)
                {
                    // Action code from grammar
                    return ( GeneratedArguments ) a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArguments? a = null;

                if ((a = Parse_Parameters()) != null)
                {
                    // Action code from grammar
                    return ( GeneratedArguments ) a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: parameters
        /// Alternatives: 5
        /// Return Type: GeneratedArguments
        /// </summary>
        private GeneratedArguments? Parse_Parameters()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] parameters at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;
                GeneratedArgSeq? b = null;
                GeneratedSeq? c = null;
                GeneratedStarEtc? d = null;

                if (
                    (a = Parse_SlashNoDefault()) != null &&
                    (b = ParseZeroOrMore(() => Parse_ParamNoDefault())?.Cast<GeneratedArgSeq>()) != null &&
                    (c = ParseZeroOrMore(() => Parse_ParamWithDefault())) != null &&
                    ((d = (GeneratedStarEtc)ParseOptional(() => Parse_StarEtc())) == null || true)
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 8 , "Positional-only parameters are" , PyParserHelpers . MakeArguments (( GeneratedArgSeq ) a , null , b ,( GeneratedNameDefaultPairSeq ?) c ,( GeneratedStarEtc ?) d ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSlashWithDefault? a = null;
                GeneratedSeq? b = null;
                GeneratedStarEtc? c = null;

                if (
                    (a = Parse_SlashWithDefault()) != null &&
                    (b = ParseZeroOrMore(() => Parse_ParamWithDefault())) != null &&
                    ((c = (GeneratedStarEtc)ParseOptional(() => Parse_StarEtc())) == null || true)
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 8 , "Positional-only parameters are" , PyParserHelpers . MakeArguments ( null ,( GeneratedSlashWithDefault ) a , null ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedStarEtc ?) c ));
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;
                GeneratedSeq? b = null;
                GeneratedStarEtc? c = null;

                if (
                    (a = ParseOneOrMore(() => Parse_ParamNoDefault())?.Cast<GeneratedArgSeq>()) != null &&
                    (b = ParseZeroOrMore(() => Parse_ParamWithDefault())) != null &&
                    ((c = (GeneratedStarEtc)ParseOptional(() => Parse_StarEtc())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . MakeArguments ( null , null , a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedStarEtc ?) c );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedStarEtc? b = null;

                if (
                    (a = ParseOneOrMore(() => Parse_ParamWithDefault())) != null &&
                    ((b = (GeneratedStarEtc)ParseOptional(() => Parse_StarEtc())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . MakeArguments ( null , null , null ,( GeneratedNameDefaultPairSeq ?) a ,( GeneratedStarEtc ?) b );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStarEtc? a = null;

                if ((a = Parse_StarEtc()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . MakeArguments ( null , null , null , null ,( GeneratedStarEtc ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: slash_no_default
        /// Alternatives: 2
        /// Return Type: GeneratedArgSeq
        /// </summary>
        private GeneratedArgSeq? Parse_SlashNoDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] slash_no_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;

                if (
                    (a = ParseOneOrMore(() => Parse_ParamNoDefault())?.Cast<GeneratedArgSeq>()) != null &&
                    ExpectOp("/") != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;

                if (
                    (a = ParseOneOrMore(() => Parse_ParamNoDefault())?.Cast<GeneratedArgSeq>()) != null &&
                    ExpectOp("/") != null &&
                    PositiveLookahead(() => ExpectOp(")")) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: slash_with_default
        /// Alternatives: 2
        /// Return Type: GeneratedSlashWithDefault
        /// </summary>
        private GeneratedSlashWithDefault? Parse_SlashWithDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] slash_with_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = ParseZeroOrMore(() => Parse_ParamNoDefault())) != null &&
                    (b = ParseOneOrMore(() => Parse_ParamWithDefault())) != null &&
                    ExpectOp("/") != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SlashWithDefault (( GeneratedArgSeq ?) a ,( GeneratedNameDefaultPairSeq ) b );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = ParseZeroOrMore(() => Parse_ParamNoDefault())) != null &&
                    (b = ParseOneOrMore(() => Parse_ParamWithDefault())) != null &&
                    ExpectOp("/") != null &&
                    PositiveLookahead(() => ExpectOp(")")) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SlashWithDefault (( GeneratedArgSeq ?) a ,( GeneratedNameDefaultPairSeq ) b );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_etc
        /// Alternatives: 5
        /// Return Type: GeneratedStarEtc
        /// </summary>
        private GeneratedStarEtc? Parse_StarEtc()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] star_etc at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStarEtc? _alt_var = null;

                if ((_alt_var = (GeneratedStarEtc)Parse_InvalidStarEtc()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (
                    ExpectOp("*") != null &&
                    (a = Parse_ParamNoDefault()) != null &&
                    (b = ParseZeroOrMore(() => Parse_ParamMaybeDefault())) != null &&
                    ((c = (GeneratedArg)ParseOptional(() => Parse_Kwds())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . StarEtc (( GeneratedArg ) a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (
                    ExpectOp("*") != null &&
                    (a = Parse_ParamNoDefaultStarAnnotation()) != null &&
                    (b = ParseZeroOrMore(() => Parse_ParamMaybeDefault())) != null &&
                    ((c = (GeneratedArg)ParseOptional(() => Parse_Kwds())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . StarEtc (( GeneratedArg ) a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (
                    ExpectOp("*") != null &&
                    ExpectOp(",") != null &&
                    (b = ParseOneOrMore(() => Parse_ParamMaybeDefault())) != null &&
                    ((c = (GeneratedArg)ParseOptional(() => Parse_Kwds())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . StarEtc ( null ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if ((a = Parse_Kwds()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . StarEtc ( null , null ,( GeneratedArg ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: kwds
        /// Alternatives: 2
        /// Return Type: GeneratedArg
        /// </summary>
        private GeneratedArg? Parse_Kwds()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] kwds at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? _alt_var = null;

                if ((_alt_var = (GeneratedArg)Parse_InvalidKwds()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (
                    ExpectOp("**") != null &&
                    (a = Parse_ParamNoDefault()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: param_no_default
        /// Alternatives: 2
        /// Return Type: GeneratedArg
        /// </summary>
        private GeneratedArg? Parse_ParamNoDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] param_no_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedTokenInfo? tc = null;

                if (
                    (a = Parse_Param()) != null &&
                    ExpectOp(",") != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . AddTypeCommentToArg ( a , tc );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedTokenInfo? tc = null;

                if (
                    (a = Parse_Param()) != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true) &&
                    PositiveLookahead(() => ExpectOp(")")) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . AddTypeCommentToArg ( a , tc );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: param_no_default_star_annotation
        /// Alternatives: 2
        /// Return Type: GeneratedArg
        /// </summary>
        private GeneratedArg? Parse_ParamNoDefaultStarAnnotation()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] param_no_default_star_annotation at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedTokenInfo? tc = null;

                if (
                    (a = Parse_ParamStarAnnotation()) != null &&
                    ExpectOp(",") != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . AddTypeCommentToArg ( a , tc );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedTokenInfo? tc = null;

                if (
                    (a = Parse_ParamStarAnnotation()) != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true) &&
                    PositiveLookahead(() => ExpectOp(")")) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . AddTypeCommentToArg ( a , tc );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: param_with_default
        /// Alternatives: 2
        /// Return Type: GeneratedNameDefaultPair
        /// </summary>
        private GeneratedNameDefaultPair? Parse_ParamWithDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] param_with_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;
                GeneratedTokenInfo? tc = null;

                if (
                    (a = Parse_Param()) != null &&
                    (c = Parse_Default()) != null &&
                    ExpectOp(",") != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . NameDefaultPair ( a , c , tc ?. Value );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;
                GeneratedTokenInfo? tc = null;

                if (
                    (a = Parse_Param()) != null &&
                    (c = Parse_Default()) != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true) &&
                    PositiveLookahead(() => ExpectOp(")")) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . NameDefaultPair ( a , c , tc ?. Value );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: param_maybe_default
        /// Alternatives: 2
        /// Return Type: GeneratedNameDefaultPair
        /// </summary>
        private GeneratedNameDefaultPair? Parse_ParamMaybeDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] param_maybe_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;
                GeneratedTokenInfo? tc = null;

                if (
                    (a = Parse_Param()) != null &&
                    ((c = (GeneratedExpr)ParseOptional(() => Parse_Default())) == null || true) &&
                    ExpectOp(",") != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . NameDefaultPair ( a , c , tc ?. Value );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;
                GeneratedTokenInfo? tc = null;

                if (
                    (a = Parse_Param()) != null &&
                    ((c = (GeneratedExpr)ParseOptional(() => Parse_Default())) == null || true) &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true) &&
                    PositiveLookahead(() => ExpectOp(")")) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . NameDefaultPair ( a , c , tc ?. Value );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: param
        /// Alternatives: 1
        /// Return Type: GeneratedArg
        /// </summary>
        private GeneratedArg? Parse_Param()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] param at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectName()) != null &&
                    ((b = (GeneratedExpr)ParseOptional(() => Parse_Annotation())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . arg ( a . GetNameValue (), b , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: param_star_annotation
        /// Alternatives: 1
        /// Return Type: GeneratedArg
        /// </summary>
        private GeneratedArg? Parse_ParamStarAnnotation()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] param_star_annotation at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectName()) != null &&
                    (b = Parse_StarAnnotation()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . arg ( a . GetNameValue (), b , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: annotation
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Annotation()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] annotation at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp(":") != null &&
                    (a = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_annotation
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_StarAnnotation()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] star_annotation at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp(":") != null &&
                    (a = Parse_StarExpression()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: default
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Default()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("=") != null &&
                    (a = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: if_stmt
        /// Alternatives: 3
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_IfStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] if_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidIfStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmt? c = null;

                if (
                    ExpectKeyword("if") != null &&
                    (a = Parse_NamedExpression()) != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null &&
                    (c = Parse_ElifStmt()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . If (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b , Check < GeneratedStmtSeq >( PyParserHelpers . SingletonSequence (( GeneratedStmt ) c ). Cast < GeneratedStmtSeq >()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? c = null;

                if (
                    ExpectKeyword("if") != null &&
                    (a = Parse_NamedExpression()) != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null &&
                    ((c = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . If (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: elif_stmt
        /// Alternatives: 3
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_ElifStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] elif_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidElifStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmt? c = null;

                if (
                    ExpectKeyword("elif") != null &&
                    (a = Parse_NamedExpression()) != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null &&
                    (c = Parse_ElifStmt()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . If (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b , Check < GeneratedStmtSeq >( PyParserHelpers . SingletonSequence (( GeneratedStmt ) c ). Cast < GeneratedStmtSeq >()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? c = null;

                if (
                    ExpectKeyword("elif") != null &&
                    (a = Parse_NamedExpression()) != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null &&
                    ((c = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . If (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: else_block
        /// Alternatives: 2
        /// Return Type: GeneratedStmtSeq
        /// </summary>
        private GeneratedStmtSeq? Parse_ElseBlock()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] else_block at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? _alt_var = null;

                if ((_alt_var = (GeneratedStmtSeq)Parse_InvalidElseStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;

                if (
                    ExpectKeyword("else") != null &&
                    PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) != null &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return b;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: while_stmt
        /// Alternatives: 2
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_WhileStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] while_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidWhileStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? c = null;

                if (
                    ExpectKeyword("while") != null &&
                    (a = Parse_NamedExpression()) != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null &&
                    ((c = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . While (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: for_stmt
        /// Alternatives: 4
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_ForStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] for_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidForStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? t = null;
                GeneratedExpr? ex = null;
                GeneratedTokenInfo? tc = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? el = null;

                if (
                    ExpectKeyword("for") != null &&
                    (t = Parse_StarTargets()) != null &&
                    ExpectKeyword("in") != null &&
                    (ex = Parse_StarExpressions()) != null &&
                    ExpectOp(":") != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true) &&
                    (b = Parse_Block()) != null &&
                    ((el = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . For (( GeneratedExpr ) t ,( GeneratedExpr ) ex ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) el , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? t = null;
                GeneratedExpr? ex = null;
                GeneratedTokenInfo? tc = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? el = null;

                if (
                    ExpectToken(PyToken.Type.ASYNC) != null &&
                    ExpectKeyword("for") != null &&
                    (t = Parse_StarTargets()) != null &&
                    ExpectKeyword("in") != null &&
                    (ex = Parse_StarExpressions()) != null &&
                    ExpectOp(":") != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true) &&
                    (b = Parse_Block()) != null &&
                    ((el = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null || true)
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 5 , "Async for loops are" , PyAst . AsyncFor (( GeneratedExpr ) t ,( GeneratedExpr ) ex ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) el , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidForTarget()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: with_stmt
        /// Alternatives: 6
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_WithStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] with_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidWithStmtIndent()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedWithitemSeq? a = null;
                GeneratedStmtSeq? b = null;

                if (
                    ExpectKeyword("with") != null &&
                    ExpectOp("(") != null &&
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_WithItem())?.Cast<GeneratedWithitemSeq>()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp(")") != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . With (( GeneratedWithitemSeq ) a ,( GeneratedStmtSeq ) b , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedWithitemSeq? a = null;
                GeneratedTokenInfo? tc = null;
                GeneratedStmtSeq? b = null;

                if (
                    ExpectKeyword("with") != null &&
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_WithItem())?.Cast<GeneratedWithitemSeq>()) != null &&
                    ExpectOp(":") != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true) &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . With (( GeneratedWithitemSeq ) a ,( GeneratedStmtSeq ) b , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedWithitemSeq? a = null;
                GeneratedStmtSeq? b = null;

                if (
                    ExpectToken(PyToken.Type.ASYNC) != null &&
                    ExpectKeyword("with") != null &&
                    ExpectOp("(") != null &&
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_WithItem())?.Cast<GeneratedWithitemSeq>()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp(")") != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 5 , "Async with statements are" , PyAst . AsyncWith (( GeneratedWithitemSeq ) a ,( GeneratedStmtSeq ) b , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedWithitemSeq? a = null;
                GeneratedTokenInfo? tc = null;
                GeneratedStmtSeq? b = null;

                if (
                    ExpectToken(PyToken.Type.ASYNC) != null &&
                    ExpectKeyword("with") != null &&
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_WithItem())?.Cast<GeneratedWithitemSeq>()) != null &&
                    ExpectOp(":") != null &&
                    ((tc = (GeneratedTokenInfo)ParseOptional(() => ExpectToken(PyToken.Type.TYPE_COMMENT))) == null || true) &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 5 , "Async with statements are" , PyAst . AsyncWith (( GeneratedWithitemSeq ) a ,( GeneratedStmtSeq ) b , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidWithStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: with_item
        /// Alternatives: 3
        /// Return Type: GeneratedWithitem
        /// </summary>
        private GeneratedWithitem? Parse_WithItem()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] with_item at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;
                GeneratedExpr? t = null;

                if (
                    (e = Parse_Expression()) != null &&
                    ExpectKeyword("as") != null &&
                    (t = Parse_StarTarget()) != null &&
                    PositiveLookahead(() => Parse_Tmp23()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . withitem (( GeneratedExpr ) e ,( GeneratedExpr ) t );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedWithitem? _alt_var = null;

                if ((_alt_var = (GeneratedWithitem)Parse_InvalidWithItem()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;

                if ((e = Parse_Expression()) != null)
                {
                    // Action code from grammar
                    return PyAst . withitem (( GeneratedExpr ) e , null );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: try_stmt
        /// Alternatives: 4
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_TryStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] try_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidTryStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? f = null;

                if (
                    ExpectKeyword("try") != null &&
                    PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) != null &&
                    (b = Parse_Block()) != null &&
                    (f = Parse_FinallyBlock()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Try (( GeneratedStmtSeq ) b , null , null ,( GeneratedStmtSeq ) f , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;
                GeneratedExcepthandlerSeq? ex = null;
                GeneratedStmtSeq? el = null;
                GeneratedStmtSeq? f = null;

                if (
                    ExpectKeyword("try") != null &&
                    PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) != null &&
                    (b = Parse_Block()) != null &&
                    (ex = ParseOneOrMore(() => Parse_ExceptBlock())?.Cast<GeneratedExcepthandlerSeq>()) != null &&
                    ((el = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null || true) &&
                    ((f = (GeneratedStmtSeq)ParseOptional(() => Parse_FinallyBlock())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Try (( GeneratedStmtSeq ) b ,( GeneratedExcepthandlerSeq ) ex ,( GeneratedStmtSeq ?) el ,( GeneratedStmtSeq ?) f , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;
                GeneratedExcepthandlerSeq? ex = null;
                GeneratedStmtSeq? el = null;
                GeneratedStmtSeq? f = null;

                if (
                    ExpectKeyword("try") != null &&
                    PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) != null &&
                    (b = Parse_Block()) != null &&
                    (ex = ParseOneOrMore(() => Parse_ExceptStarBlock())?.Cast<GeneratedExcepthandlerSeq>()) != null &&
                    ((el = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null || true) &&
                    ((f = (GeneratedStmtSeq)ParseOptional(() => Parse_FinallyBlock())) == null || true)
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 11 , "Exception groups are" , PyAst . TryStar (( GeneratedStmtSeq ) b ,( GeneratedExcepthandlerSeq ) ex ,( GeneratedStmtSeq ?) el ,( GeneratedStmtSeq ?) f , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: except_block
        /// Alternatives: 4
        /// Return Type: GeneratedExcepthandler
        /// </summary>
        private GeneratedExcepthandler? Parse_ExceptBlock()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] except_block at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExcepthandler? _alt_var = null;

                if ((_alt_var = (GeneratedExcepthandler)Parse_InvalidExceptStmtIndent()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;
                GeneratedPtr? t = null;
                GeneratedStmtSeq? b = null;

                if (
                    ExpectKeyword("except") != null &&
                    (e = Parse_Expression()) != null &&
                    ((t = ParseOptional(() => Parse_Tmp24())) == null || true) &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . ExceptHandler (( GeneratedExpr ) e , t != null ?(( GeneratedName ) NameToken (( GeneratedTokenInfo ) t )). Id : null ,( GeneratedStmtSeq ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;

                if (
                    ExpectKeyword("except") != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . ExceptHandler ( null , null ,( GeneratedStmtSeq ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExcepthandler? _alt_var = null;

                if ((_alt_var = (GeneratedExcepthandler)Parse_InvalidExceptStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: except_star_block
        /// Alternatives: 3
        /// Return Type: GeneratedExcepthandler
        /// </summary>
        private GeneratedExcepthandler? Parse_ExceptStarBlock()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] except_star_block at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExcepthandler? _alt_var = null;

                if ((_alt_var = (GeneratedExcepthandler)Parse_InvalidExceptStarStmtIndent()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;
                GeneratedPtr? t = null;
                GeneratedStmtSeq? b = null;

                if (
                    ExpectKeyword("except") != null &&
                    ExpectOp("*") != null &&
                    (e = Parse_Expression()) != null &&
                    ((t = ParseOptional(() => Parse_Tmp25())) == null || true) &&
                    ExpectOp(":") != null &&
                    (b = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . ExceptHandler (( GeneratedExpr ) e ,( t != null )?(( GeneratedName ) NameToken (( GeneratedTokenInfo ) t )). Id : null ,( GeneratedStmtSeq ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExcepthandler? _alt_var = null;

                if ((_alt_var = (GeneratedExcepthandler)Parse_InvalidExceptStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: finally_block
        /// Alternatives: 2
        /// Return Type: GeneratedStmtSeq
        /// </summary>
        private GeneratedStmtSeq? Parse_FinallyBlock()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] finally_block at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? _alt_var = null;

                if ((_alt_var = (GeneratedStmtSeq)Parse_InvalidFinallyStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if (
                    ExpectKeyword("finally") != null &&
                    PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) != null &&
                    (a = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: match_stmt
        /// Alternatives: 2
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_MatchStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] match_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? subject = null;
                GeneratedMatchCaseSeq? cases = null;

                if (
                    ExpectSoftKeyword("match") != null &&
                    (subject = Parse_SubjectExpr()) != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    ExpectToken(PyToken.Type.INDENT) != null &&
                    (cases = ParseOneOrMore(() => Parse_CaseBlock())?.Cast<GeneratedMatchCaseSeq>()) != null &&
                    ExpectToken(PyToken.Type.DEDENT) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 10 , "Pattern matching is" , PyAst . Match (( GeneratedExpr ) subject ,( GeneratedMatchCaseSeq ) cases , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? _alt_var = null;

                if ((_alt_var = (GeneratedStmt)Parse_InvalidMatchStmt()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: subject_expr
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_SubjectExpr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] subject_expr at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;
                GeneratedExprSeq? values = null;

                if (
                    (value = Parse_StarNamedExpression()) != null &&
                    ExpectOp(",") != null &&
                    ((values = (GeneratedExprSeq)ParseOptional(() => Parse_StarNamedExpressions())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) value ,( GeneratedExprSeq ?) values ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_NamedExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: case_block
        /// Alternatives: 2
        /// Return Type: GeneratedMatchCase
        /// </summary>
        private GeneratedMatchCase? Parse_CaseBlock()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] case_block at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedMatchCase? _alt_var = null;

                if ((_alt_var = (GeneratedMatchCase)Parse_InvalidCaseBlock()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? pattern = null;
                GeneratedExpr? guard = null;
                GeneratedStmtSeq? body = null;

                if (
                    ExpectSoftKeyword("case") != null &&
                    (pattern = Parse_Patterns()) != null &&
                    ((guard = (GeneratedExpr)ParseOptional(() => Parse_Guard())) == null || true) &&
                    ExpectOp(":") != null &&
                    (body = Parse_Block()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . match_case (( GeneratedPattern ) pattern ,( GeneratedExpr ?) guard ,( GeneratedStmtSeq ) body );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: guard
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Guard()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] guard at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? guard = null;

                if (
                    ExpectKeyword("if") != null &&
                    (guard = Parse_NamedExpression()) != null
                )
                {
                    // Action code from grammar
                    return guard;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: patterns
        /// Alternatives: 2
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_Patterns()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] patterns at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? patterns = null;

                if ((patterns = Parse_OpenSequencePattern()?.Cast<GeneratedPatternSeq>()) != null)
                {
                    // Action code from grammar
                    return PyAst . MatchSequence (( GeneratedPatternSeq ) patterns , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? _alt_var = null;

                if ((_alt_var = Parse_Pattern()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: pattern
        /// Alternatives: 2
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_Pattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? _alt_var = null;

                if ((_alt_var = Parse_AsPattern()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? _alt_var = null;

                if ((_alt_var = Parse_OrPattern()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: as_pattern
        /// Alternatives: 2
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_AsPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] as_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? pattern = null;
                GeneratedExpr? target = null;

                if (
                    (pattern = Parse_OrPattern()) != null &&
                    ExpectKeyword("as") != null &&
                    (target = Parse_PatternCaptureTarget()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchAs (( GeneratedPattern ) pattern , target . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? _alt_var = null;

                if ((_alt_var = (GeneratedPattern)Parse_InvalidAsPattern()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: or_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_OrPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] or_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? patterns = null;

                if ((patterns = ParseGatherPlus(() => ExpectOp("|"), () => Parse_ClosedPattern())?.Cast<GeneratedPatternSeq>()) != null)
                {
                    // Action code from grammar
                    return patterns . Count == 1 ?( GeneratedPattern ) patterns [ 0 ]: PyAst . MatchOr (( GeneratedPatternSeq ) patterns , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: closed_pattern
        /// Alternatives: 8
        /// Return Type: GeneratedPattern
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedPattern? Parse_ClosedPattern()
        {
            return (GeneratedPattern?)TryMemoized("closed_pattern", Parse_ClosedPattern_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: closed_pattern
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedPattern? Parse_ClosedPattern_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] closed_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_LiteralPattern()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_CapturePattern()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_WildcardPattern()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_ValuePattern()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_GroupPattern()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_SequencePattern()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_MappingPattern()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_ClassPattern()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: literal_pattern
        /// Alternatives: 6
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_LiteralPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] literal_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;

                if (
                    (value = Parse_SignedNumber()) != null &&
                    NegativeLookahead(() => Parse_Tmp26()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchValue (( GeneratedExpr ) value , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;

                if ((value = Parse_ComplexNumber()) != null)
                {
                    // Action code from grammar
                    return PyAst . MatchValue (( GeneratedExpr ) value , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;

                if ((value = Parse_Strings()) != null)
                {
                    // Action code from grammar
                    return PyAst . MatchValue (( GeneratedExpr ) value , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("None") != null)
                {
                    // Action code from grammar
                    return PyAst . MatchSingleton ( GeneratedPyConstant . None , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("True") != null)
                {
                    // Action code from grammar
                    return PyAst . MatchSingleton ( GeneratedPyConstant . True , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("False") != null)
                {
                    // Action code from grammar
                    return PyAst . MatchSingleton ( GeneratedPyConstant . False , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: literal_expr
        /// Alternatives: 6
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_LiteralExpr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] literal_expr at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_SignedNumber() != null &&
                    NegativeLookahead(() => Parse_Tmp27()) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_ComplexNumber()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Strings()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("None") != null)
                {
                    // Action code from grammar
                    return PyAst . Constant ( GeneratedPyConstant . None , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("True") != null)
                {
                    // Action code from grammar
                    return PyAst . Constant ( GeneratedPyConstant . True , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("False") != null)
                {
                    // Action code from grammar
                    return PyAst . Constant ( GeneratedPyConstant . False , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: complex_number
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_ComplexNumber()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] complex_number at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? real = null;
                GeneratedExpr? imag = null;

                if (
                    (real = Parse_SignedRealNumber()) != null &&
                    ExpectOp("+") != null &&
                    (imag = Parse_ImaginaryNumber()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) real , GeneratedAdd.Instance ,( GeneratedExpr ) imag , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? real = null;
                GeneratedExpr? imag = null;

                if (
                    (real = Parse_SignedRealNumber()) != null &&
                    ExpectOp("-") != null &&
                    (imag = Parse_ImaginaryNumber()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) real , GeneratedSub.Instance ,( GeneratedExpr ) imag , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: signed_number
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_SignedNumber()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] signed_number at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? number = null;

                if ((number = ExpectToken(PyToken.Type.NUMBER)) != null)
                {
                    // Action code from grammar
                    return NumberToken ( number );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? number = null;

                if (
                    ExpectOp("-") != null &&
                    (number = ExpectToken(PyToken.Type.NUMBER)) != null
                )
                {
                    // Action code from grammar
                    return PyAst . UnaryOp ( GeneratedUSub.Instance , NumberToken ( number ), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: signed_real_number
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_SignedRealNumber()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] signed_real_number at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_RealNumber()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? real = null;

                if (
                    ExpectOp("-") != null &&
                    (real = Parse_RealNumber()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . UnaryOp ( GeneratedUSub.Instance ,( GeneratedExpr ) real , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: real_number
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_RealNumber()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] real_number at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? real = null;

                if ((real = ExpectToken(PyToken.Type.NUMBER)) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . EnsureReal ( NumberToken ( real ));
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: imaginary_number
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_ImaginaryNumber()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] imaginary_number at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? imag = null;

                if ((imag = ExpectToken(PyToken.Type.NUMBER)) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . EnsureImaginary ( NumberToken ( imag ));
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: capture_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_CapturePattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] capture_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? target = null;

                if ((target = Parse_PatternCaptureTarget()) != null)
                {
                    // Action code from grammar
                    return PyAst . MatchAs ( null , target . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: pattern_capture_target
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_PatternCaptureTarget()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] pattern_capture_target at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? name = null;

                if (
                    NegativeLookahead(() => ExpectSoftKeyword("_")) != null &&
                    (name = ExpectName()) != null &&
                    NegativeLookahead(() => Parse_Tmp28()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SetExprContext ( NameToken ( name ), GeneratedStore.Instance );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: wildcard_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_WildcardPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] wildcard_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectSoftKeyword("_") != null)
                {
                    // Action code from grammar
                    return PyAst . MatchAs ( null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: value_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_ValuePattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] value_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? attr = null;

                if (
                    (attr = Parse_Attr()) != null &&
                    NegativeLookahead(() => Parse_Tmp29()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchValue (( GeneratedExpr ) attr , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: attr
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_Attr()
        {
            return (GeneratedExpr?)TryLeftRecursive("attr", Parse_Attr_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: attr
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_Attr_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] attr at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;
                GeneratedTokenInfo? attr = null;

                if (
                    (value = Parse_NameOrAttr()) != null &&
                    ExpectOp(".") != null &&
                    (attr = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Attribute (( GeneratedExpr ) value , attr . Value , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: name_or_attr
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_NameOrAttr()
        {
            return (GeneratedExpr?)TryLeftRecursive("name_or_attr", Parse_NameOrAttr_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: name_or_attr
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_NameOrAttr_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] name_or_attr at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Attr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = ExpectNameExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: group_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_GroupPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] group_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? pattern = null;

                if (
                    ExpectOp("(") != null &&
                    (pattern = Parse_Pattern()) != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return pattern;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: sequence_pattern
        /// Alternatives: 2
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_SequencePattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] sequence_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? patterns = null;

                if (
                    ExpectOp("[") != null &&
                    ((patterns = (GeneratedSeq)ParseOptional(() => Parse_MaybeSequencePattern())) == null || true) &&
                    ExpectOp("]") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchSequence (( GeneratedPatternSeq ?) patterns , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? patterns = null;

                if (
                    ExpectOp("(") != null &&
                    ((patterns = (GeneratedSeq)ParseOptional(() => Parse_OpenSequencePattern())) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchSequence (( GeneratedPatternSeq ?) patterns , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: open_sequence_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedSeq
        /// </summary>
        private GeneratedSeq? Parse_OpenSequencePattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] open_sequence_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? pattern = null;
                GeneratedSeq? patterns = null;

                if (
                    (pattern = Parse_MaybeStarPattern()) != null &&
                    ExpectOp(",") != null &&
                    ((patterns = (GeneratedSeq)ParseOptional(() => Parse_MaybeSequencePattern())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SeqInsertInFront (( GeneratedPattern ) pattern ,( GeneratedSeq ?) patterns ). Cast < GeneratedPatternSeq >();
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: maybe_sequence_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedSeq
        /// </summary>
        private GeneratedSeq? Parse_MaybeSequencePattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] maybe_sequence_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? patterns = null;

                if (
                    (patterns = ParseGatherPlus(() => ExpectOp(","), () => Parse_MaybeStarPattern())) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return patterns;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: maybe_star_pattern
        /// Alternatives: 2
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_MaybeStarPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] maybe_star_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? _alt_var = null;

                if ((_alt_var = Parse_StarPattern()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? _alt_var = null;

                if ((_alt_var = Parse_Pattern()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_pattern
        /// Alternatives: 2
        /// Return Type: GeneratedPattern
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedPattern? Parse_StarPattern()
        {
            return (GeneratedPattern?)TryMemoized("star_pattern", Parse_StarPattern_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: star_pattern
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedPattern? Parse_StarPattern_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] star_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? target = null;

                if (
                    ExpectOp("*") != null &&
                    (target = Parse_PatternCaptureTarget()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchStar ( target . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("*") != null &&
                    Parse_WildcardPattern() != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchStar ( null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: mapping_pattern
        /// Alternatives: 4
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_MappingPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] mapping_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("{") != null &&
                    ExpectOp("}") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchMapping ( null , null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? rest = null;

                if (
                    ExpectOp("{") != null &&
                    (rest = Parse_DoubleStarPattern()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp("}") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchMapping ( null , null , rest . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? items = null;
                GeneratedExpr? rest = null;

                if (
                    ExpectOp("{") != null &&
                    (items = Parse_ItemsPattern()) != null &&
                    ExpectOp(",") != null &&
                    (rest = Parse_DoubleStarPattern()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp("}") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchMapping ( Check < GeneratedExprSeq >( PyParserHelpers . GetPatternKeys ( items )), Check < GeneratedPatternSeq >( PyParserHelpers . GetPatterns ( items )), rest . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? items = null;

                if (
                    ExpectOp("{") != null &&
                    (items = Parse_ItemsPattern()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp("}") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchMapping ( Check < GeneratedExprSeq >( PyParserHelpers . GetPatternKeys ( items )), Check < GeneratedPatternSeq >( PyParserHelpers . GetPatterns ( items )), null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: items_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedSeq
        /// </summary>
        private GeneratedSeq? Parse_ItemsPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] items_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? _alt_var = null;

                if ((_alt_var = ParseGatherPlus(() => ExpectOp(","), () => Parse_KeyValuePattern())) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: key_value_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedKeyPatternPair
        /// </summary>
        private GeneratedKeyPatternPair? Parse_KeyValuePattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] key_value_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? key = null;
                GeneratedPattern? pattern = null;

                if (
                    (key = Parse_Tmp30()) != null &&
                    ExpectOp(":") != null &&
                    (pattern = Parse_Pattern()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . KeyPatternPair (( GeneratedExpr ) key ,( GeneratedPattern ) pattern );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: double_star_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_DoubleStarPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] double_star_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? target = null;

                if (
                    ExpectOp("**") != null &&
                    (target = Parse_PatternCaptureTarget()) != null
                )
                {
                    // Action code from grammar
                    return target;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: class_pattern
        /// Alternatives: 5
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_ClassPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] class_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? cls = null;

                if (
                    (cls = Parse_NameOrAttr()) != null &&
                    ExpectOp("(") != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchClass (( GeneratedExpr ) cls , null , null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? cls = null;
                GeneratedPatternSeq? patterns = null;

                if (
                    (cls = Parse_NameOrAttr()) != null &&
                    ExpectOp("(") != null &&
                    (patterns = Parse_PositionalPatterns()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchClass (( GeneratedExpr ) cls ,( GeneratedPatternSeq ) patterns , null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? cls = null;
                GeneratedSeq? keywords = null;

                if (
                    (cls = Parse_NameOrAttr()) != null &&
                    ExpectOp("(") != null &&
                    (keywords = Parse_KeywordPatterns()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchClass (( GeneratedExpr ) cls , null , Check < GeneratedIdentifierSeq >( PyParserHelpers . MapNamesToIds ( Check < GeneratedExprSeq >( PyParserHelpers . GetPatternKeys (( GeneratedSeq ) keywords )))), Check < GeneratedPatternSeq >( PyParserHelpers . GetPatterns (( GeneratedSeq ) keywords )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? cls = null;
                GeneratedPatternSeq? patterns = null;
                GeneratedSeq? keywords = null;

                if (
                    (cls = Parse_NameOrAttr()) != null &&
                    ExpectOp("(") != null &&
                    (patterns = Parse_PositionalPatterns()) != null &&
                    ExpectOp(",") != null &&
                    (keywords = Parse_KeywordPatterns()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . MatchClass (( GeneratedExpr ) cls ,( GeneratedPatternSeq ) patterns , Check < GeneratedIdentifierSeq >( PyParserHelpers . MapNamesToIds ( Check < GeneratedExprSeq >( PyParserHelpers . GetPatternKeys (( GeneratedSeq ) keywords )))), Check < GeneratedPatternSeq >( PyParserHelpers . GetPatterns (( GeneratedSeq ) keywords )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? _alt_var = null;

                if ((_alt_var = (GeneratedPattern)Parse_InvalidClassPattern()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: positional_patterns
        /// Alternatives: 1
        /// Return Type: GeneratedPatternSeq
        /// </summary>
        private GeneratedPatternSeq? Parse_PositionalPatterns()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] positional_patterns at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? args = null;

                if ((args = ParseGatherPlus(() => ExpectOp(","), () => Parse_Pattern())?.Cast<GeneratedPatternSeq>()) != null)
                {
                    // Action code from grammar
                    return args;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: keyword_patterns
        /// Alternatives: 1
        /// Return Type: GeneratedSeq
        /// </summary>
        private GeneratedSeq? Parse_KeywordPatterns()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] keyword_patterns at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? _alt_var = null;

                if ((_alt_var = ParseGatherPlus(() => ExpectOp(","), () => Parse_KeywordPattern())) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: keyword_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedKeyPatternPair
        /// </summary>
        private GeneratedKeyPatternPair? Parse_KeywordPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] keyword_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? arg = null;
                GeneratedPattern? value = null;

                if (
                    (arg = ExpectName()) != null &&
                    ExpectOp("=") != null &&
                    (value = Parse_Pattern()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . KeyPatternPair ( NameToken ( arg ),( GeneratedPattern ) value );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: type_alias
        /// Alternatives: 1
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_TypeAlias()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] type_alias at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? n = null;
                GeneratedTypeParamSeq? t = null;
                GeneratedExpr? b = null;

                if (
                    ExpectSoftKeyword("type") != null &&
                    (n = ExpectName()) != null &&
                    ((t = (GeneratedTypeParamSeq)ParseOptional(() => Parse_TypeParams())) == null || true) &&
                    ExpectOp("=") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 12 , "Type statement is" , PyAst . TypeAlias ( Check < GeneratedExpr >( PyParserHelpers . SetExprContext ( NameToken ( n ), GeneratedStore.Instance )),( GeneratedTypeParamSeq ?) t ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: type_params
        /// Alternatives: 1
        /// Return Type: GeneratedTypeParamSeq
        /// </summary>
        private GeneratedTypeParamSeq? Parse_TypeParams()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] type_params at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTypeParamSeq? t = null;

                if (
                    ExpectOp("[") != null &&
                    (t = Parse_TypeParamSeq()) != null &&
                    ExpectOp("]") != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 12 , "Type parameter lists are" , t );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: type_param_seq
        /// Alternatives: 1
        /// Return Type: GeneratedTypeParamSeq
        /// </summary>
        private GeneratedTypeParamSeq? Parse_TypeParamSeq()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] type_param_seq at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTypeParamSeq? a = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_TypeParam())?.Cast<GeneratedTypeParamSeq>()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: type_param
        /// Alternatives: 5
        /// Return Type: GeneratedTypeParam
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedTypeParam? Parse_TypeParam()
        {
            return (GeneratedTypeParam?)TryMemoized("type_param", Parse_TypeParam_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: type_param
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedTypeParam? Parse_TypeParam_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] type_param at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectName()) != null &&
                    ((b = (GeneratedExpr)ParseOptional(() => Parse_TypeParamBound())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . TypeVar ( a . GetNameValue (),( GeneratedExpr ?) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? colon = null;
                GeneratedExpr? e = null;

                if (
                    ExpectOp("*") != null &&
                    (a = ExpectName()) != null &&
                    (colon = ExpectOp(":")) != null &&
                    (e = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorStartingFrom ( colon ,( GeneratedExpr ) e is GeneratedTuple ? "cannot use constraints with TypeVarTuple" : "cannot use bound with TypeVarTuple" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("*") != null &&
                    (a = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . TypeVarTuple ( a . GetNameValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? colon = null;
                GeneratedExpr? e = null;

                if (
                    ExpectOp("**") != null &&
                    (a = ExpectName()) != null &&
                    (colon = ExpectOp(":")) != null &&
                    (e = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorStartingFrom ( colon ,( GeneratedExpr ) e is GeneratedTuple ? "cannot use constraints with ParamSpec" : "cannot use bound with ParamSpec" );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("**") != null &&
                    (a = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . ParamSpec ( a . GetNameValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: type_param_bound
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_TypeParamBound()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] type_param_bound at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;

                if (
                    ExpectOp(":") != null &&
                    (e = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return e;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: expressions
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Expressions()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] expressions at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = Parse_Expression()) != null &&
                    (b = ParseOneOrMore(() => Parse_Tmp31())) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) a ,( GeneratedSeq ) b ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_Expression()) != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SingletonSequence (( GeneratedExpr ) a ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Expression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: expression
        /// Alternatives: 5
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Expression()
        {
            return (GeneratedExpr?)TryMemoized("expression", Parse_Expression_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: expression
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Expression_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidLegacyExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedExpr? c = null;

                if (
                    (a = Parse_Disjunction()) != null &&
                    ExpectKeyword("if") != null &&
                    (b = Parse_Disjunction()) != null &&
                    ExpectKeyword("else") != null &&
                    (c = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . IfExp (( GeneratedExpr ) b ,( GeneratedExpr ) a ,( GeneratedExpr ) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Disjunction()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Lambdef()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: yield_expr
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_YieldExpr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] yield_expr at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("yield") != null &&
                    ExpectKeyword("from") != null &&
                    (a = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . YieldFrom (( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("yield") != null &&
                    ((a = (GeneratedExpr)ParseOptional(() => Parse_StarExpressions())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Yield (( GeneratedExpr ?) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_expressions
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_StarExpressions()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] star_expressions at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = Parse_StarExpression()) != null &&
                    (b = ParseOneOrMore(() => Parse_Tmp32())) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) a ,( GeneratedSeq ) b ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_StarExpression()) != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SingletonSequence (( GeneratedExpr ) a ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_StarExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_expression
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_StarExpression()
        {
            return (GeneratedExpr?)TryMemoized("star_expression", Parse_StarExpression_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: star_expression
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_StarExpression_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] star_expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("*") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Starred (( GeneratedExpr ) a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Expression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_named_expressions
        /// Alternatives: 1
        /// Return Type: GeneratedExprSeq
        /// </summary>
        private GeneratedExprSeq? Parse_StarNamedExpressions()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] star_named_expressions at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_StarNamedExpression())?.Cast<GeneratedExprSeq>()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_named_expression
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_StarNamedExpression()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] star_named_expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("*") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Starred (( GeneratedExpr ) a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_NamedExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: assignment_expression
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_AssignmentExpression()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] assignment_expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectName()) != null &&
                    ExpectOp(":=") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 8 , "Assignment expressions are" , PyAst . NamedExpr ( Check < GeneratedExpr >( PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedStore.Instance )),( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: named_expression
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_NamedExpression()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] named_expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_AssignmentExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidNamedExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expression() != null &&
                    NegativeLookahead(() => ExpectOp(":=")) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: disjunction
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Disjunction()
        {
            return (GeneratedExpr?)TryMemoized("disjunction", Parse_Disjunction_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: disjunction
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Disjunction_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] disjunction at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = Parse_Conjunction()) != null &&
                    (b = ParseOneOrMore(() => Parse_Tmp33())) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BoolOp ( GeneratedOr.Instance , Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) a ,( GeneratedSeq ) b ). Cast < GeneratedExprSeq >()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Conjunction()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: conjunction
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Conjunction()
        {
            return (GeneratedExpr?)TryMemoized("conjunction", Parse_Conjunction_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: conjunction
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Conjunction_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] conjunction at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = Parse_Inversion()) != null &&
                    (b = ParseOneOrMore(() => Parse_Tmp34())) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BoolOp ( GeneratedAnd.Instance , Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) a ,( GeneratedSeq ) b ). Cast < GeneratedExprSeq >()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Inversion()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: inversion
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Inversion()
        {
            return (GeneratedExpr?)TryMemoized("inversion", Parse_Inversion_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: inversion
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Inversion_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] inversion at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("not") != null &&
                    (a = Parse_Inversion()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . UnaryOp ( GeneratedNot.Instance ,( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Comparison()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: comparison
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Comparison()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] comparison at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = Parse_BitwiseOr()) != null &&
                    (b = ParseOneOrMore(() => Parse_CompareOpBitwiseOrPair())) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Compare (( GeneratedExpr ) a , Check < GeneratedCmpopSeq >( PyParserHelpers . GetCmpops (( GeneratedSeq ) b ). Cast < GeneratedCmpopSeq >()), Check < GeneratedExprSeq >( PyParserHelpers . GetExprs (( GeneratedSeq ) b )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_BitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: compare_op_bitwise_or_pair
        /// Alternatives: 10
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_CompareOpBitwiseOrPair()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] compare_op_bitwise_or_pair at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_EqBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_NoteqBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_LteBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_LtBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_GteBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_GtBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_NotinBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_InBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_IsnotBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();

                GeneratedCmpopExprPair? _alt_var = null;

                if ((_alt_var = Parse_IsBitwiseOr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: eq_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_EqBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] eq_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("==") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedEq.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: noteq_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_NoteqBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] noteq_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    Parse_Tmp35() != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedNotEq.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lte_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_LteBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lte_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("<=") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedLtE.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lt_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_LtBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lt_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("<") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedLt.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: gte_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_GteBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] gte_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp(">=") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedGtE.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: gt_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_GtBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] gt_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp(">") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedGt.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: notin_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_NotinBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] notin_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("not") != null &&
                    ExpectKeyword("in") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedNotIn.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: in_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_InBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] in_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("in") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedIn.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: isnot_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_IsnotBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] isnot_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("is") != null &&
                    ExpectKeyword("not") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedIsNot.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: is_bitwise_or
        /// Alternatives: 1
        /// Return Type: GeneratedCmpopExprPair
        /// </summary>
        private GeneratedCmpopExprPair? Parse_IsBitwiseOr()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] is_bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("is") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CmpopExprPair ( GeneratedIs.Instance ,( GeneratedExpr ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: bitwise_or
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_BitwiseOr()
        {
            return (GeneratedExpr?)TryLeftRecursive("bitwise_or", Parse_BitwiseOr_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: bitwise_or
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_BitwiseOr_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] bitwise_or at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_BitwiseOr()) != null &&
                    ExpectOp("|") != null &&
                    (b = Parse_BitwiseXor()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedBitOr.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_BitwiseXor()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: bitwise_xor
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_BitwiseXor()
        {
            return (GeneratedExpr?)TryLeftRecursive("bitwise_xor", Parse_BitwiseXor_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: bitwise_xor
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_BitwiseXor_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] bitwise_xor at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_BitwiseXor()) != null &&
                    ExpectOp("^") != null &&
                    (b = Parse_BitwiseAnd()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedBitXor.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_BitwiseAnd()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: bitwise_and
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_BitwiseAnd()
        {
            return (GeneratedExpr?)TryLeftRecursive("bitwise_and", Parse_BitwiseAnd_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: bitwise_and
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_BitwiseAnd_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] bitwise_and at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_BitwiseAnd()) != null &&
                    ExpectOp("&") != null &&
                    (b = Parse_ShiftExpr()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedBitAnd.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_ShiftExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: shift_expr
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_ShiftExpr()
        {
            return (GeneratedExpr?)TryLeftRecursive("shift_expr", Parse_ShiftExpr_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: shift_expr
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_ShiftExpr_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] shift_expr at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_ShiftExpr()) != null &&
                    ExpectOp("<<") != null &&
                    (b = Parse_Sum()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedLShift.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_ShiftExpr()) != null &&
                    ExpectOp(">>") != null &&
                    (b = Parse_Sum()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedRShift.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Sum()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: sum
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_Sum()
        {
            return (GeneratedExpr?)TryLeftRecursive("sum", Parse_Sum_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: sum
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_Sum_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] sum at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Sum()) != null &&
                    ExpectOp("+") != null &&
                    (b = Parse_Term()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedAdd.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Sum()) != null &&
                    ExpectOp("-") != null &&
                    (b = Parse_Term()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedSub.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Term()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: term
        /// Alternatives: 6
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_Term()
        {
            return (GeneratedExpr?)TryLeftRecursive("term", Parse_Term_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: term
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_Term_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] term at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Term()) != null &&
                    ExpectOp("*") != null &&
                    (b = Parse_Factor()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedMult.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Term()) != null &&
                    ExpectOp("/") != null &&
                    (b = Parse_Factor()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedDiv.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Term()) != null &&
                    ExpectOp("//") != null &&
                    (b = Parse_Factor()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedFloorDiv.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Term()) != null &&
                    ExpectOp("%") != null &&
                    (b = Parse_Factor()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedMod_.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Term()) != null &&
                    ExpectOp("@") != null &&
                    (b = Parse_Factor()) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 5 , "The '@' operator is" , PyAst . BinOp (( GeneratedExpr ) a , GeneratedMatMult.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Factor()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: factor
        /// Alternatives: 4
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Factor()
        {
            return (GeneratedExpr?)TryMemoized("factor", Parse_Factor_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: factor
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Factor_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] factor at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("+") != null &&
                    (a = Parse_Factor()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . UnaryOp ( GeneratedUAdd.Instance ,( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("-") != null &&
                    (a = Parse_Factor()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . UnaryOp ( GeneratedUSub.Instance ,( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("~") != null &&
                    (a = Parse_Factor()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . UnaryOp ( GeneratedInvert.Instance ,( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Power()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: power
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Power()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] power at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_AwaitPrimary()) != null &&
                    ExpectOp("**") != null &&
                    (b = Parse_Factor()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . BinOp (( GeneratedExpr ) a , GeneratedPow.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_AwaitPrimary()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: await_primary
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_AwaitPrimary()
        {
            return (GeneratedExpr?)TryMemoized("await_primary", Parse_AwaitPrimary_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: await_primary
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_AwaitPrimary_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] await_primary at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectToken(PyToken.Type.AWAIT) != null &&
                    (a = Parse_Primary()) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 5 , "Await expressions are" , PyAst . Await (( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Primary()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: primary
        /// Alternatives: 5
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_Primary()
        {
            return (GeneratedExpr?)TryLeftRecursive("primary", Parse_Primary_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: primary
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_Primary_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] primary at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = Parse_Primary()) != null &&
                    ExpectOp(".") != null &&
                    (b = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Attribute (( GeneratedExpr ) a , b . GetNameValue (), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Primary()) != null &&
                    (b = Parse_Genexp()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Call (( GeneratedExpr ) a , Check < GeneratedExprSeq >( PyParserHelpers . SingletonSequence (( GeneratedExpr ) b ). Cast < GeneratedExprSeq >()), null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Primary()) != null &&
                    ExpectOp("(") != null &&
                    ((b = (GeneratedExpr)ParseOptional(() => Parse_Arguments())) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . Call (( GeneratedExpr ) a ,( b != null )?(( GeneratedCall ) b ). Args : null !,( b != null )?(( GeneratedCall ) b ). Keywords : null !, _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Primary()) != null &&
                    ExpectOp("[") != null &&
                    (b = Parse_Slices()) != null &&
                    ExpectOp("]") != null
                )
                {
                    // Action code from grammar
                    return PyAst . Subscript (( GeneratedExpr ) a ,( GeneratedExpr ) b , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Atom()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: slices
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Slices()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] slices at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_Slice()) != null &&
                    NegativeLookahead(() => ExpectOp(",")) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_Tmp36())?.Cast<GeneratedExprSeq>()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple ( a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: slice
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Slice()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] slice at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedPtr? c = null;

                if (
                    ((a = (GeneratedExpr)ParseOptional(() => Parse_Expression())) == null || true) &&
                    ExpectOp(":") != null &&
                    ((b = (GeneratedExpr)ParseOptional(() => Parse_Expression())) == null || true) &&
                    ((c = ParseOptional(() => Parse_Tmp37())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Slice (( GeneratedExpr ?) a ,( GeneratedExpr ?) b ,( GeneratedExpr ?) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_NamedExpression()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: atom
        /// Alternatives: 10
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Atom()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] atom at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? name = null;

                if ((name = ExpectName()) != null)
                {
                    // Action code from grammar
                    return NameToken ( name );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("True") != null)
                {
                    // Action code from grammar
                    return PyAst . Constant ( GeneratedPyConstant . True , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("False") != null)
                {
                    // Action code from grammar
                    return PyAst . Constant ( GeneratedPyConstant . False , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("None") != null)
                {
                    // Action code from grammar
                    return PyAst . Constant ( GeneratedPyConstant . None , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => Parse_Tmp38()) != null &&
                    Parse_Strings() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? number = null;

                if ((number = ExpectToken(PyToken.Type.NUMBER)) != null)
                {
                    // Action code from grammar
                    return NumberToken ( number );
                }
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectOp("(")) != null &&
                    Parse_Tmp39() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectOp("[")) != null &&
                    Parse_Tmp40() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();


                if (
                    PositiveLookahead(() => ExpectOp("{")) != null &&
                    Parse_Tmp41() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("...") != null)
                {
                    // Action code from grammar
                    return PyAst . Constant ( GeneratedPyConstant . Ellipsis , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: group
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Group()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] group at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if (
                    ExpectOp("(") != null &&
                    (a = Parse_Tmp42()) != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return ( GeneratedExpr ) a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidGroup()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambdef
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Lambdef()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambdef at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArguments? a = null;
                GeneratedExpr? b = null;

                if (
                    ExpectKeyword("lambda") != null &&
                    ((a = (GeneratedArguments)ParseOptional(() => Parse_LambdaParams())) == null || true) &&
                    ExpectOp(":") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Lambda (( a != null )? a :( GeneratedArguments ) Check < GeneratedArguments >( PyParserHelpers . EmptyArguments ()), b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_params
        /// Alternatives: 2
        /// Return Type: GeneratedArguments
        /// </summary>
        private GeneratedArguments? Parse_LambdaParams()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_params at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if ((a = Parse_InvalidLambdaParameters()) != null)
                {
                    // Action code from grammar
                    return ( GeneratedArguments ) a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArguments? a = null;

                if ((a = Parse_LambdaParameters()) != null)
                {
                    // Action code from grammar
                    return ( GeneratedArguments ) a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_parameters
        /// Alternatives: 5
        /// Return Type: GeneratedArguments
        /// </summary>
        private GeneratedArguments? Parse_LambdaParameters()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_parameters at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;
                GeneratedArgSeq? b = null;
                GeneratedSeq? c = null;
                GeneratedStarEtc? d = null;

                if (
                    (a = Parse_LambdaSlashNoDefault()) != null &&
                    (b = ParseZeroOrMore(() => Parse_LambdaParamNoDefault())?.Cast<GeneratedArgSeq>()) != null &&
                    (c = ParseZeroOrMore(() => Parse_LambdaParamWithDefault())) != null &&
                    ((d = (GeneratedStarEtc)ParseOptional(() => Parse_LambdaStarEtc())) == null || true)
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 8 , "Positional-only parameters are" , PyParserHelpers . MakeArguments (( GeneratedArgSeq ) a , null , b ,( GeneratedNameDefaultPairSeq ?) c ,( GeneratedStarEtc ?) d ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSlashWithDefault? a = null;
                GeneratedSeq? b = null;
                GeneratedStarEtc? c = null;

                if (
                    (a = Parse_LambdaSlashWithDefault()) != null &&
                    (b = ParseZeroOrMore(() => Parse_LambdaParamWithDefault())) != null &&
                    ((c = (GeneratedStarEtc)ParseOptional(() => Parse_LambdaStarEtc())) == null || true)
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 8 , "Positional-only parameters are" , PyParserHelpers . MakeArguments ( null ,( GeneratedSlashWithDefault ) a , null ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedStarEtc ?) c ));
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;
                GeneratedSeq? b = null;
                GeneratedStarEtc? c = null;

                if (
                    (a = ParseOneOrMore(() => Parse_LambdaParamNoDefault())?.Cast<GeneratedArgSeq>()) != null &&
                    (b = ParseZeroOrMore(() => Parse_LambdaParamWithDefault())) != null &&
                    ((c = (GeneratedStarEtc)ParseOptional(() => Parse_LambdaStarEtc())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . MakeArguments ( null , null , a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedStarEtc ?) c );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedStarEtc? b = null;

                if (
                    (a = ParseOneOrMore(() => Parse_LambdaParamWithDefault())) != null &&
                    ((b = (GeneratedStarEtc)ParseOptional(() => Parse_LambdaStarEtc())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . MakeArguments ( null , null , null ,( GeneratedNameDefaultPairSeq ?) a ,( GeneratedStarEtc ?) b );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStarEtc? a = null;

                if ((a = Parse_LambdaStarEtc()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . MakeArguments ( null , null , null , null ,( GeneratedStarEtc ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_slash_no_default
        /// Alternatives: 2
        /// Return Type: GeneratedArgSeq
        /// </summary>
        private GeneratedArgSeq? Parse_LambdaSlashNoDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_slash_no_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;

                if (
                    (a = ParseOneOrMore(() => Parse_LambdaParamNoDefault())?.Cast<GeneratedArgSeq>()) != null &&
                    ExpectOp("/") != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;

                if (
                    (a = ParseOneOrMore(() => Parse_LambdaParamNoDefault())?.Cast<GeneratedArgSeq>()) != null &&
                    ExpectOp("/") != null &&
                    PositiveLookahead(() => ExpectOp(":")) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_slash_with_default
        /// Alternatives: 2
        /// Return Type: GeneratedSlashWithDefault
        /// </summary>
        private GeneratedSlashWithDefault? Parse_LambdaSlashWithDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_slash_with_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = ParseZeroOrMore(() => Parse_LambdaParamNoDefault())) != null &&
                    (b = ParseOneOrMore(() => Parse_LambdaParamWithDefault())) != null &&
                    ExpectOp("/") != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SlashWithDefault (( GeneratedArgSeq ?) a ,( GeneratedNameDefaultPairSeq ) b );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = ParseZeroOrMore(() => Parse_LambdaParamNoDefault())) != null &&
                    (b = ParseOneOrMore(() => Parse_LambdaParamWithDefault())) != null &&
                    ExpectOp("/") != null &&
                    PositiveLookahead(() => ExpectOp(":")) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SlashWithDefault (( GeneratedArgSeq ?) a ,( GeneratedNameDefaultPairSeq ) b );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_star_etc
        /// Alternatives: 4
        /// Return Type: GeneratedStarEtc
        /// </summary>
        private GeneratedStarEtc? Parse_LambdaStarEtc()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_star_etc at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStarEtc? _alt_var = null;

                if ((_alt_var = (GeneratedStarEtc)Parse_InvalidLambdaStarEtc()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (
                    ExpectOp("*") != null &&
                    (a = Parse_LambdaParamNoDefault()) != null &&
                    (b = ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault())) != null &&
                    ((c = (GeneratedArg)ParseOptional(() => Parse_LambdaKwds())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . StarEtc (( GeneratedArg ) a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (
                    ExpectOp("*") != null &&
                    ExpectOp(",") != null &&
                    (b = ParseOneOrMore(() => Parse_LambdaParamMaybeDefault())) != null &&
                    ((c = (GeneratedArg)ParseOptional(() => Parse_LambdaKwds())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . StarEtc ( null ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if ((a = Parse_LambdaKwds()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . StarEtc ( null , null ,( GeneratedArg ) a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_kwds
        /// Alternatives: 2
        /// Return Type: GeneratedArg
        /// </summary>
        private GeneratedArg? Parse_LambdaKwds()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_kwds at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? _alt_var = null;

                if ((_alt_var = (GeneratedArg)Parse_InvalidLambdaKwds()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (
                    ExpectOp("**") != null &&
                    (a = Parse_LambdaParamNoDefault()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_param_no_default
        /// Alternatives: 2
        /// Return Type: GeneratedArg
        /// </summary>
        private GeneratedArg? Parse_LambdaParamNoDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_param_no_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (
                    (a = Parse_LambdaParam()) != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (
                    (a = Parse_LambdaParam()) != null &&
                    PositiveLookahead(() => ExpectOp(":")) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_param_with_default
        /// Alternatives: 2
        /// Return Type: GeneratedNameDefaultPair
        /// </summary>
        private GeneratedNameDefaultPair? Parse_LambdaParamWithDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_param_with_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;

                if (
                    (a = Parse_LambdaParam()) != null &&
                    (c = Parse_Default()) != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . NameDefaultPair (( GeneratedArg ) a ,( GeneratedExpr ) c , null );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;

                if (
                    (a = Parse_LambdaParam()) != null &&
                    (c = Parse_Default()) != null &&
                    PositiveLookahead(() => ExpectOp(":")) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . NameDefaultPair (( GeneratedArg ) a ,( GeneratedExpr ) c , null );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_param_maybe_default
        /// Alternatives: 2
        /// Return Type: GeneratedNameDefaultPair
        /// </summary>
        private GeneratedNameDefaultPair? Parse_LambdaParamMaybeDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_param_maybe_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;

                if (
                    (a = Parse_LambdaParam()) != null &&
                    ((c = (GeneratedExpr)ParseOptional(() => Parse_Default())) == null || true) &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . NameDefaultPair (( GeneratedArg ) a ,( GeneratedExpr ?) c , null );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;

                if (
                    (a = Parse_LambdaParam()) != null &&
                    ((c = (GeneratedExpr)ParseOptional(() => Parse_Default())) == null || true) &&
                    PositiveLookahead(() => ExpectOp(":")) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . NameDefaultPair (( GeneratedArg ) a ,( GeneratedExpr ?) c , null );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: lambda_param
        /// Alternatives: 1
        /// Return Type: GeneratedArg
        /// </summary>
        private GeneratedArg? Parse_LambdaParam()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] lambda_param at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectName()) != null)
                {
                    // Action code from grammar
                    return PyAst . arg ( a . GetNameValue (), null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: fstring_middle
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_FstringMiddle()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] fstring_middle at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_FstringReplacementField()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? t = null;

                if ((t = ExpectToken(PyToken.Type.FSTRING_MIDDLE)) != null)
                {
                    // Action code from grammar
                    return ConstantFromToken ( t );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: fstring_replacement_field
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_FstringReplacementField()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] fstring_replacement_field at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;
                GeneratedTokenInfo? debug_expr = null;
                GeneratedResultTokenWithMetadata? conversion = null;
                GeneratedResultTokenWithMetadata? format = null;
                GeneratedTokenInfo? rbrace = null;

                if (
                    ExpectOp("{") != null &&
                    (a = Parse_Tmp43()) != null &&
                    ((debug_expr = (GeneratedTokenInfo)ParseOptional(() => ExpectOp("="))) == null || true) &&
                    ((conversion = (GeneratedResultTokenWithMetadata)ParseOptional(() => Parse_FstringConversion())) == null || true) &&
                    ((format = (GeneratedResultTokenWithMetadata)ParseOptional(() => Parse_FstringFullFormatSpec())) == null || true) &&
                    (rbrace = ExpectOp("}")) != null
                )
                {
                    // Action code from grammar
                    return FormattedValue (( GeneratedExpr ) a , debug_expr , conversion , format , rbrace , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidReplacementField()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: fstring_conversion
        /// Alternatives: 1
        /// Return Type: GeneratedResultTokenWithMetadata
        /// </summary>
        private GeneratedResultTokenWithMetadata? Parse_FstringConversion()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] fstring_conversion at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? conv_token = null;
                GeneratedTokenInfo? conv = null;

                if (
                    (conv_token = ExpectSoftKeyword("!")) != null &&
                    (conv = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return CheckFstringConversion ( conv_token , NameToken ( conv ));
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: fstring_full_format_spec
        /// Alternatives: 1
        /// Return Type: GeneratedResultTokenWithMetadata
        /// </summary>
        private GeneratedResultTokenWithMetadata? Parse_FstringFullFormatSpec()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] fstring_full_format_spec at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? colon = null;
                GeneratedSeq? spec = null;

                if (
                    (colon = ExpectOp(":")) != null &&
                    (spec = ParseZeroOrMore(() => Parse_FstringFormatSpec())) != null
                )
                {
                    // Action code from grammar
                    return SetupFullFormatSpec ( colon , spec . Cast < GeneratedExprSeq >(), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: fstring_format_spec
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_FstringFormatSpec()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] fstring_format_spec at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? t = null;

                if ((t = ExpectToken(PyToken.Type.FSTRING_MIDDLE)) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . DecodedConstantFromToken ( t );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_FstringReplacementField()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: fstring
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Fstring()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] fstring at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedSeq? b = null;
                GeneratedTokenInfo? c = null;

                if (
                    (a = ExpectToken(PyToken.Type.FSTRING_START)) != null &&
                    (b = ParseZeroOrMore(() => Parse_FstringMiddle())) != null &&
                    (c = ExpectToken(PyToken.Type.FSTRING_END)) != null
                )
                {
                    // Action code from grammar
                    return JoinedStr ( a , b . Cast < GeneratedExprSeq >(), c );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: string
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_String()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] string at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? s = null;

                if ((s = ExpectToken(PyToken.Type.STRING)) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . ConstantFromString ( s );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: strings
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Strings()
        {
            return (GeneratedExpr?)TryMemoized("strings", Parse_Strings_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: strings
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Strings_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] strings at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = ParseOneOrMore(() => Parse_Tmp44())?.Cast<GeneratedExprSeq>()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . ConcatenateStrings ( a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: list
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_List()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] list at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    ExpectOp("[") != null &&
                    ((a = (GeneratedExprSeq)ParseOptional(() => Parse_StarNamedExpressions())) == null || true) &&
                    ExpectOp("]") != null
                )
                {
                    // Action code from grammar
                    return PyAst . List ( a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: tuple
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Tuple()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] tuple at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if (
                    ExpectOp("(") != null &&
                    ((a = ParseOptional(() => Parse_Tmp45())) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple (( GeneratedExprSeq ?) a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: set
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Set()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] set at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    ExpectOp("{") != null &&
                    (a = Parse_StarNamedExpressions()) != null &&
                    ExpectOp("}") != null
                )
                {
                    // Action code from grammar
                    return PyAst . Set ( a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: dict
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Dict()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] dict at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if (
                    ExpectOp("{") != null &&
                    ((a = (GeneratedSeq)ParseOptional(() => Parse_DoubleStarredKvpairs())) == null || true) &&
                    ExpectOp("}") != null
                )
                {
                    // Action code from grammar
                    return PyAst . Dict ( Check < GeneratedExprSeq >( PyParserHelpers . GetKeys ( a )), Check < GeneratedExprSeq >( PyParserHelpers . GetValues ( a )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("{") != null &&
                    Parse_InvalidDoubleStarredKvpairs() != null &&
                    ExpectOp("}") != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: double_starred_kvpairs
        /// Alternatives: 1
        /// Return Type: GeneratedSeq
        /// </summary>
        private GeneratedSeq? Parse_DoubleStarredKvpairs()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] double_starred_kvpairs at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_DoubleStarredKvpair())) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: double_starred_kvpair
        /// Alternatives: 2
        /// Return Type: GeneratedKeyValuePair
        /// </summary>
        private GeneratedKeyValuePair? Parse_DoubleStarredKvpair()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] double_starred_kvpair at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("**") != null &&
                    (a = Parse_BitwiseOr()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . KeyValuePair ( null , a );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedKeyValuePair? _alt_var = null;

                if ((_alt_var = Parse_Kvpair()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: kvpair
        /// Alternatives: 1
        /// Return Type: GeneratedKeyValuePair
        /// </summary>
        private GeneratedKeyValuePair? Parse_Kvpair()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] kvpair at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Expression()) != null &&
                    ExpectOp(":") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . KeyValuePair ( a , b );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: for_if_clauses
        /// Alternatives: 1
        /// Return Type: GeneratedComprehensionSeq
        /// </summary>
        private GeneratedComprehensionSeq? Parse_ForIfClauses()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] for_if_clauses at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedComprehensionSeq? a = null;

                if ((a = ParseOneOrMore(() => Parse_ForIfClause())?.Cast<GeneratedComprehensionSeq>()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: for_if_clause
        /// Alternatives: 3
        /// Return Type: GeneratedComprehension
        /// </summary>
        private GeneratedComprehension? Parse_ForIfClause()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] for_if_clause at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedExprSeq? c = null;

                if (
                    ExpectToken(PyToken.Type.ASYNC) != null &&
                    ExpectKeyword("for") != null &&
                    (a = Parse_StarTargets()) != null &&
                    ExpectKeyword("in") != null &&
                    (b = Parse_Disjunction()) != null &&
                    (c = ParseZeroOrMore(() => Parse_Tmp46())?.Cast<GeneratedExprSeq>()) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 6 , "Async comprehensions are" , PyAst . comprehension (( GeneratedExpr ) a ,( GeneratedExpr ) b , c , is_async : 1 ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedExprSeq? c = null;

                if (
                    ExpectKeyword("for") != null &&
                    (a = Parse_StarTargets()) != null &&
                    ExpectKeyword("in") != null &&
                    (b = Parse_Disjunction()) != null &&
                    (c = ParseZeroOrMore(() => Parse_Tmp47())?.Cast<GeneratedExprSeq>()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . comprehension (( GeneratedExpr ) a ,( GeneratedExpr ) b , c , is_async : 0 );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedComprehension? _alt_var = null;

                if ((_alt_var = (GeneratedComprehension)Parse_InvalidForTarget()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: listcomp
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Listcomp()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] listcomp at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (
                    ExpectOp("[") != null &&
                    (a = Parse_NamedExpression()) != null &&
                    (b = Parse_ForIfClauses()) != null &&
                    ExpectOp("]") != null
                )
                {
                    // Action code from grammar
                    return PyAst . ListComp ( a , b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidComprehension()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: setcomp
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Setcomp()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] setcomp at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (
                    ExpectOp("{") != null &&
                    (a = Parse_NamedExpression()) != null &&
                    (b = Parse_ForIfClauses()) != null &&
                    ExpectOp("}") != null
                )
                {
                    // Action code from grammar
                    return PyAst . SetComp ( a , b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidComprehension()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: genexp
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Genexp()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] genexp at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (
                    ExpectOp("(") != null &&
                    (a = Parse_Tmp48()) != null &&
                    (b = Parse_ForIfClauses()) != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . GeneratorExp (( GeneratedExpr ) a , b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidComprehension()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: dictcomp
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Dictcomp()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] dictcomp at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedKeyValuePair? a = null;
                GeneratedComprehensionSeq? b = null;

                if (
                    ExpectOp("{") != null &&
                    (a = Parse_Kvpair()) != null &&
                    (b = Parse_ForIfClauses()) != null &&
                    ExpectOp("}") != null
                )
                {
                    // Action code from grammar
                    return PyAst . DictComp ( a . Key , a . Value , b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidDictComprehension()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: arguments
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Arguments()
        {
            return (GeneratedExpr?)TryMemoized("arguments", Parse_Arguments_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: arguments
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_Arguments_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] arguments at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_Args()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    PositiveLookahead(() => ExpectOp(")")) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidArguments()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: args
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Args()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] args at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;
                GeneratedPtr? b = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_Tmp49())?.Cast<GeneratedExprSeq>()) != null &&
                    ((b = ParseOptional(() => Parse_Tmp50())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . CollectCallSeqs ( a ,( GeneratedSeq ?) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if ((a = Parse_Kwargs()) != null)
                {
                    // Action code from grammar
                    return PyAst . Call ( PyParserHelpers . DummyName (), CheckNullAllowed < GeneratedExprSeq >( PyParserHelpers . SeqExtractStarredExprs ( a )), CheckNullAllowed < GeneratedKeywordSeq >( PyParserHelpers . SeqDeleteStarredExprs ( a )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: kwargs
        /// Alternatives: 3
        /// Return Type: GeneratedSeq
        /// </summary>
        private GeneratedSeq? Parse_Kwargs()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] kwargs at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_KwargOrStarred())) != null &&
                    ExpectOp(",") != null &&
                    (b = ParseGatherPlus(() => ExpectOp(","), () => Parse_KwargOrDoubleStarred())) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . JoinSequences ( a , b );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? _alt_var = null;

                if ((_alt_var = ParseGatherPlus(() => ExpectOp(","), () => Parse_KwargOrStarred())) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? _alt_var = null;

                if ((_alt_var = ParseGatherPlus(() => ExpectOp(","), () => Parse_KwargOrDoubleStarred())) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: starred_expression
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_StarredExpression()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] starred_expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = (GeneratedExpr)Parse_InvalidStarredExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("*") != null &&
                    (a = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Starred ( a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("*") != null)
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "Invalid star expression" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: kwarg_or_starred
        /// Alternatives: 3
        /// Return Type: GeneratedKeywordOrStarred
        /// </summary>
        private GeneratedKeywordOrStarred? Parse_KwargOrStarred()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] kwarg_or_starred at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedKeywordOrStarred? _alt_var = null;

                if ((_alt_var = (GeneratedKeywordOrStarred)Parse_InvalidKwarg()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectName()) != null &&
                    ExpectOp("=") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . KeywordOrStarred ( Check < GeneratedKeyword >( PyAst . keyword ( a . GetNameValue (), b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset )), is_keyword : 1 );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_StarredExpression()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . KeywordOrStarred ( a , is_keyword : 0 );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: kwarg_or_double_starred
        /// Alternatives: 3
        /// Return Type: GeneratedKeywordOrStarred
        /// </summary>
        private GeneratedKeywordOrStarred? Parse_KwargOrDoubleStarred()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] kwarg_or_double_starred at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedKeywordOrStarred? _alt_var = null;

                if ((_alt_var = (GeneratedKeywordOrStarred)Parse_InvalidKwarg()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectName()) != null &&
                    ExpectOp("=") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . KeywordOrStarred ( Check < GeneratedKeyword >( PyAst . keyword ( a . GetNameValue (), b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset )), is_keyword : 1 );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("**") != null &&
                    (a = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . KeywordOrStarred ( Check < GeneratedKeyword >( PyAst . keyword ( null , a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset )), is_keyword : 1 );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_targets
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_StarTargets()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] star_targets at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_StarTarget()) != null &&
                    NegativeLookahead(() => ExpectOp(",")) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = Parse_StarTarget()) != null &&
                    (b = ParseZeroOrMore(() => Parse_Tmp51())) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple (( GeneratedExprSeq ?) Check < GeneratedExprSeq >(( GeneratedExprSeq ) PyParserHelpers . SeqInsertInFront ( a , b )), GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_targets_list_seq
        /// Alternatives: 1
        /// Return Type: GeneratedExprSeq
        /// </summary>
        private GeneratedExprSeq? Parse_StarTargetsListSeq()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] star_targets_list_seq at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_StarTarget())?.Cast<GeneratedExprSeq>()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_targets_tuple_seq
        /// Alternatives: 2
        /// Return Type: GeneratedExprSeq
        /// </summary>
        private GeneratedExprSeq? Parse_StarTargetsTupleSeq()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] star_targets_tuple_seq at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if (
                    (a = Parse_StarTarget()) != null &&
                    (b = ParseOneOrMore(() => Parse_Tmp52())) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SeqInsertInFront ( a , b ). Cast < GeneratedExprSeq >();
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_StarTarget()) != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SingletonSeq ( a ). Cast < GeneratedExprSeq >();
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_target
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_StarTarget()
        {
            return (GeneratedExpr?)TryMemoized("star_target", Parse_StarTarget_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: star_target
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_StarTarget_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] star_target at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if (
                    ExpectOp("*") != null &&
                    (a = Parse_Tmp53()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Starred (( GeneratedExpr ) Check < GeneratedExpr >(( GeneratedExpr ) PyParserHelpers . SetExprContext (( GeneratedExpr ) a , GeneratedStore.Instance )), GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_TargetWithStarAtom()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: target_with_star_atom
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_TargetWithStarAtom()
        {
            return (GeneratedExpr?)TryMemoized("target_with_star_atom", Parse_TargetWithStarAtom_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: target_with_star_atom
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_TargetWithStarAtom_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] target_with_star_atom at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    ExpectOp(".") != null &&
                    (b = ExpectName()) != null &&
                    NegativeLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Attribute ( a , b . GetNameValue (), GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    ExpectOp("[") != null &&
                    (b = Parse_Slices()) != null &&
                    ExpectOp("]") != null &&
                    NegativeLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Subscript ( a , b , GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_StarAtom()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_atom
        /// Alternatives: 4
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_StarAtom()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] star_atom at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectName()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedStore.Instance );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("(") != null &&
                    (a = Parse_TargetWithStarAtom()) != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SetExprContext ( a , GeneratedStore.Instance );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    ExpectOp("(") != null &&
                    ((a = (GeneratedExprSeq)ParseOptional(() => Parse_StarTargetsTupleSeq())) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple ( a , GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    ExpectOp("[") != null &&
                    ((a = (GeneratedExprSeq)ParseOptional(() => Parse_StarTargetsListSeq())) == null || true) &&
                    ExpectOp("]") != null
                )
                {
                    // Action code from grammar
                    return PyAst . List ( a , GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: single_target
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_SingleTarget()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] single_target at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_SingleSubscriptAttributeTarget()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectName()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedStore.Instance );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("(") != null &&
                    (a = Parse_SingleTarget()) != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: single_subscript_attribute_target
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_SingleSubscriptAttributeTarget()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] single_subscript_attribute_target at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    ExpectOp(".") != null &&
                    (b = ExpectName()) != null &&
                    NegativeLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Attribute ( a , b . GetNameValue (), GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    ExpectOp("[") != null &&
                    (b = Parse_Slices()) != null &&
                    ExpectOp("]") != null &&
                    NegativeLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Subscript ( a , b , GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: t_primary
        /// Alternatives: 5
        /// Return Type: GeneratedExpr
        /// Left-recursive rule - uses TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_TPrimary()
        {
            return (GeneratedExpr?)TryLeftRecursive("t_primary", Parse_TPrimary_Raw);
        }

        /// <summary>
        /// Raw parsing method for left-recursive rule: t_primary
        /// Called by TryLeftRecursive wrapper
        /// </summary>
        private GeneratedExpr? Parse_TPrimary_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] t_primary at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    ExpectOp(".") != null &&
                    (b = ExpectName()) != null &&
                    PositiveLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Attribute ( a , b . GetNameValue (), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    ExpectOp("[") != null &&
                    (b = Parse_Slices()) != null &&
                    ExpectOp("]") != null &&
                    PositiveLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Subscript ( a , b , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    (b = Parse_Genexp()) != null &&
                    PositiveLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Call ( a , Check < GeneratedExprSeq >( PyParserHelpers . SingletonSeq ( b ). Cast < GeneratedExprSeq >()), null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    ExpectOp("(") != null &&
                    ((b = (GeneratedExpr)ParseOptional(() => Parse_Arguments())) == null || true) &&
                    ExpectOp(")") != null &&
                    PositiveLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Call ( a ,( b != null )?(( GeneratedCall ) b ). Args : null !,( b != null )?(( GeneratedCall ) b ). Keywords : null !, _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_Atom()) != null &&
                    PositiveLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: t_lookahead
        /// Alternatives: 3
        /// </summary>
        private GeneratedPtr? Parse_TLookahead()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] t_lookahead at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("(")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("[")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(".")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: del_targets
        /// Alternatives: 1
        /// Return Type: GeneratedExprSeq
        /// </summary>
        private GeneratedExprSeq? Parse_DelTargets()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] del_targets at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_DelTarget())?.Cast<GeneratedExprSeq>()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true)
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: del_target
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_DelTarget()
        {
            return (GeneratedExpr?)TryMemoized("del_target", Parse_DelTarget_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: del_target
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedExpr? Parse_DelTarget_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] del_target at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    ExpectOp(".") != null &&
                    (b = ExpectName()) != null &&
                    NegativeLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Attribute ( a , b . GetNameValue (), GeneratedDel.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_TPrimary()) != null &&
                    ExpectOp("[") != null &&
                    (b = Parse_Slices()) != null &&
                    ExpectOp("]") != null &&
                    NegativeLookahead(() => Parse_TLookahead()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . Subscript ( a , b , GeneratedDel.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_DelTAtom()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: del_t_atom
        /// Alternatives: 4
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_DelTAtom()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] del_t_atom at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectName()) != null)
                {
                    // Action code from grammar
                    return PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedDel.Instance );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("(") != null &&
                    (a = Parse_DelTarget()) != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SetExprContext ( a , GeneratedDel.Instance );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    ExpectOp("(") != null &&
                    ((a = (GeneratedExprSeq)ParseOptional(() => Parse_DelTargets())) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return PyAst . Tuple ( a , GeneratedDel.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (
                    ExpectOp("[") != null &&
                    ((a = (GeneratedExprSeq)ParseOptional(() => Parse_DelTargets())) == null || true) &&
                    ExpectOp("]") != null
                )
                {
                    // Action code from grammar
                    return PyAst . List ( a , GeneratedDel.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: type_expressions
        /// Alternatives: 7
        /// Return Type: GeneratedExprSeq
        /// </summary>
        private GeneratedExprSeq? Parse_TypeExpressions()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] type_expressions at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedExpr? b = null;
                GeneratedExpr? c = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_Expression())) != null &&
                    ExpectOp(",") != null &&
                    ExpectOp("*") != null &&
                    (b = Parse_Expression()) != null &&
                    ExpectOp(",") != null &&
                    ExpectOp("**") != null &&
                    (c = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SeqAppendToEnd (( Check < GeneratedSeq >( PyParserHelpers . SeqAppendToEnd ( a , b )). Cast < GeneratedExprSeq >()), c ). Cast < GeneratedExprSeq >();
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_Expression())) != null &&
                    ExpectOp(",") != null &&
                    ExpectOp("*") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SeqAppendToEnd ( a , b ). Cast < GeneratedExprSeq >();
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ParseGatherPlus(() => ExpectOp(","), () => Parse_Expression())) != null &&
                    ExpectOp(",") != null &&
                    ExpectOp("**") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SeqAppendToEnd ( a , b ). Cast < GeneratedExprSeq >();
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    ExpectOp("*") != null &&
                    (a = Parse_Expression()) != null &&
                    ExpectOp(",") != null &&
                    ExpectOp("**") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SeqAppendToEnd (( Check < GeneratedSeq >( PyParserHelpers . SingletonSeq ( a )). Cast < GeneratedExprSeq >()), b ). Cast < GeneratedExprSeq >();
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("*") != null &&
                    (a = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SingletonSeq ( a ). Cast < GeneratedExprSeq >();
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("**") != null &&
                    (a = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SingletonSeq ( a ). Cast < GeneratedExprSeq >();
                }
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = ParseGatherPlus(() => ExpectOp(","), () => Parse_Expression())?.Cast<GeneratedExprSeq>()) != null)
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: func_type_comment
        /// Alternatives: 3
        /// Return Type: GeneratedTokenInfo
        /// </summary>
        private GeneratedTokenInfo? Parse_FuncTypeComment()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] func_type_comment at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? t = null;

                if (
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    (t = ExpectToken(PyToken.Type.TYPE_COMMENT)) != null &&
                    PositiveLookahead(() => Parse_Tmp54()) != null
                )
                {
                    // Action code from grammar
                    return t;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? _alt_var = null;

                if ((_alt_var = (GeneratedTokenInfo)Parse_InvalidDoubleTypeComments()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? _alt_var = null;

                if ((_alt_var = ExpectToken(PyToken.Type.TYPE_COMMENT)) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_arguments
        /// Alternatives: 7
        /// </summary>
        private GeneratedPtr? Parse_InvalidArguments()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_arguments at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    Parse_Tmp55() != null &&
                    (a = ExpectOp(",")) != null &&
                    ParseGatherPlus(() => ExpectOp(","), () => Parse_Tmp56()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorStartingFrom ( a , "iterable argument unpacking follows keyword argument unpacking" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (
                    (a = Parse_Expression()) != null &&
                    (b = Parse_ForIfClauses()) != null &&
                    ExpectOp(",") != null &&
                    (ParseOptional(() => Parse_Tmp57()) == null || true)
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , GetLastComprehensionItem ( LastItem < GeneratedComprehension >( b )), "Generator expression must be parenthesized" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = ExpectName()) != null &&
                    (b = ExpectOp("=")) != null &&
                    Parse_Expression() != null &&
                    Parse_ForIfClauses() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "invalid syntax. Maybe you meant '==' or ':=' instead of '='?" );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (ParseOptional(() => Parse_Tmp58()) == null || true) &&
                    (a = ExpectName()) != null &&
                    (b = ExpectOp("=")) != null &&
                    PositiveLookahead(() => Parse_Tmp59()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "expected argument value expression" );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (
                    (a = Parse_Args()) != null &&
                    (b = Parse_ForIfClauses()) != null
                )
                {
                    // Action code from grammar
                    return NonparenGenexpInCall ( a , b );
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (
                    Parse_Args() != null &&
                    ExpectOp(",") != null &&
                    (a = Parse_Expression()) != null &&
                    (b = Parse_ForIfClauses()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , GetLastComprehensionItem ( LastItem < GeneratedComprehension >( b )), "Generator expression must be parenthesized" );
                }
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_Args()) != null &&
                    ExpectOp(",") != null &&
                    Parse_Args() != null
                )
                {
                    // Action code from grammar
                    return ArgumentsParsingError ( a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_kwarg
        /// Alternatives: 4
        /// </summary>
        private GeneratedPtr? Parse_InvalidKwarg()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_kwarg at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = (GeneratedTokenInfo)Parse_Tmp60()) != null &&
                    (b = ExpectOp("=")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "cannot assign to %s" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = ExpectName()) != null &&
                    (b = ExpectOp("=")) != null &&
                    Parse_Expression() != null &&
                    Parse_ForIfClauses() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "invalid syntax. Maybe you meant '==' or ':=' instead of '='?" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    NegativeLookahead(() => Parse_Tmp61()) != null &&
                    (a = Parse_Expression()) != null &&
                    (b = ExpectOp("=")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "expression cannot contain assignment, perhaps you meant \"==\"?" );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectOp("**")) != null &&
                    Parse_Expression() != null &&
                    ExpectOp("=") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "cannot assign to keyword argument unpacking" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: expression_without_invalid
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_ExpressionWithoutInvalid()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] expression_without_invalid at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedExpr? c = null;

                if (
                    (a = Parse_Disjunction()) != null &&
                    ExpectKeyword("if") != null &&
                    (b = Parse_Disjunction()) != null &&
                    ExpectKeyword("else") != null &&
                    (c = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return PyAst . IfExp ( b , a , c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Disjunction()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Lambdef()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_legacy_expression
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidLegacyExpression()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_legacy_expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectName()) != null &&
                    NegativeLookahead(() => ExpectOp("(")) != null &&
                    (b = Parse_StarExpressions()) != null
                )
                {
                    // Action code from grammar
                    return CheckLegacyStmt ( NameToken ( a ))? RaiseSyntaxErrorKnownRange ( NameToken ( a ), b , "Missing parentheses in call to '{a.GetNameValue()}'. Did you mean {a.GetNameValue()}(...)?" ): null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_expression
        /// Alternatives: 3
        /// </summary>
        private GeneratedPtr? Parse_InvalidExpression()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    NegativeLookahead(() => Parse_Tmp62()) != null &&
                    (a = Parse_Disjunction()) != null &&
                    (b = Parse_ExpressionWithoutInvalid()) != null
                )
                {
                    // Action code from grammar
                    return CheckLegacyStmt ( a )? null : _tokens [ _mark - 1 ]. Level == 0 ? null : RaiseSyntaxErrorKnownRange ( a , b , "invalid syntax. Perhaps you forgot a comma?" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = Parse_Disjunction()) != null &&
                    ExpectKeyword("if") != null &&
                    (b = Parse_Disjunction()) != null &&
                    NegativeLookahead(() => Parse_Tmp63()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "expected 'else' after 'if' expression" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    (a = ExpectKeyword("lambda")) != null &&
                    (ParseOptional(() => Parse_LambdaParams()) == null || true) &&
                    (b = ExpectOp(":")) != null &&
                    PositiveLookahead(() => ExpectToken(PyToken.Type.FSTRING_MIDDLE)) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "f-string: lambda expressions are not allowed without parentheses" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_named_expression
        /// Alternatives: 3
        /// CPython (memo) - uses TryMemoized wrapper
        /// </summary>
        private GeneratedPtr? Parse_InvalidNamedExpression()
        {
            return (GeneratedPtr?)TryMemoized("invalid_named_expression", Parse_InvalidNamedExpression_Raw);
        }

        /// <summary>
        /// Raw parsing method for memoized rule: invalid_named_expression
        /// Called by TryMemoized wrapper
        /// </summary>
        private GeneratedPtr? Parse_InvalidNamedExpression_Raw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE-RAW] invalid_named_expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_Expression()) != null &&
                    ExpectOp(":=") != null &&
                    Parse_Expression() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "cannot use assignment expressions with %s" , GetExprName ( a ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectName()) != null &&
                    ExpectOp("=") != null &&
                    (b = Parse_BitwiseOr()) != null &&
                    NegativeLookahead(() => Parse_Tmp64()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "invalid syntax. Maybe you meant '==' or ':=' instead of '='?" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    NegativeLookahead(() => Parse_Tmp65()) != null &&
                    (a = Parse_BitwiseOr()) != null &&
                    (b = ExpectOp("=")) != null &&
                    Parse_BitwiseOr() != null &&
                    NegativeLookahead(() => Parse_Tmp66()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "cannot assign to %s here. Maybe you meant '==' instead of '='?" , GetExprName ( a ));
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_assignment
        /// Alternatives: 6
        /// </summary>
        private GeneratedPtr? Parse_InvalidAssignment()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_assignment at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_InvalidAnnAssignTarget()) != null &&
                    ExpectOp(":") != null &&
                    Parse_Expression() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "only single target (not %s) can be annotated" , GetExprName ( a ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_StarNamedExpression()) != null &&
                    ExpectOp(",") != null &&
                    ParseZeroOrMore(() => Parse_StarNamedExpressions()) != null &&
                    ExpectOp(":") != null &&
                    Parse_Expression() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "only single target (not tuple) can be annotated" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_Expression()) != null &&
                    ExpectOp(":") != null &&
                    Parse_Expression() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "illegal target for annotation" );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ParseZeroOrMore(() => Parse_Tmp67()) != null &&
                    (a = Parse_StarExpressions()) != null &&
                    ExpectOp("=") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorInvalidTarget ( "assign to" , a );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ParseZeroOrMore(() => Parse_Tmp68()) != null &&
                    (a = Parse_YieldExpr()) != null &&
                    ExpectOp("=") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "assignment to yield expression not possible" );
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_StarExpressions()) != null &&
                    Parse_Augassign() != null &&
                    Parse_Tmp69() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "'%s' is an illegal expression for augmented assignment" , GetExprName ( a ));
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_ann_assign_target
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_InvalidAnnAssignTarget()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_ann_assign_target at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_List()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? _alt_var = null;

                if ((_alt_var = Parse_Tuple()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("(") != null &&
                    (a = Parse_InvalidAnnAssignTarget()) != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_del_stmt
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidDelStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_del_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("del") != null &&
                    (a = Parse_StarExpressions()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorInvalidTarget ( "delete" , a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_block
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidBlock()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_block at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_comprehension
        /// Alternatives: 3
        /// </summary>
        private GeneratedPtr? Parse_InvalidComprehension()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_comprehension at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    Parse_Tmp70() != null &&
                    (a = Parse_StarredExpression()) != null &&
                    Parse_ForIfClauses() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "iterable unpacking cannot be used in comprehension" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExprSeq? b = null;

                if (
                    Parse_Tmp71() != null &&
                    (a = Parse_StarNamedExpression()) != null &&
                    ExpectOp(",") != null &&
                    (b = Parse_StarNamedExpressions()) != null &&
                    Parse_ForIfClauses() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , LastItem < GeneratedExpr >( b ), "did you forget parentheses around the comprehension target?" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    Parse_Tmp72() != null &&
                    (a = Parse_StarNamedExpression()) != null &&
                    (b = ExpectOp(",")) != null &&
                    Parse_ForIfClauses() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "did you forget parentheses around the comprehension target?" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_dict_comprehension
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidDictComprehension()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_dict_comprehension at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("{") != null &&
                    (a = ExpectOp("**")) != null &&
                    Parse_BitwiseOr() != null &&
                    Parse_ForIfClauses() != null &&
                    ExpectOp("}") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "dict unpacking cannot be used in dict comprehension" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_parameters
        /// Alternatives: 6
        /// </summary>
        private GeneratedPtr? Parse_InvalidParameters()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_parameters at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectSoftKeyword("/")) != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "at least one argument must precede /" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    Parse_Tmp73() != null &&
                    ParseZeroOrMore(() => Parse_ParamMaybeDefault()) != null &&
                    (a = ExpectOp("/")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "/ may appear only once" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (
                    (ParseOptional(() => Parse_SlashNoDefault()) == null || true) &&
                    ParseZeroOrMore(() => Parse_ParamNoDefault()) != null &&
                    Parse_InvalidParametersHelper() != null &&
                    (a = Parse_ParamNoDefault()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "parameter without a default follows parameter with a default" );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    ParseZeroOrMore(() => Parse_ParamNoDefault()) != null &&
                    (a = ExpectOp("(")) != null &&
                    ParseOneOrMore(() => Parse_ParamNoDefault()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    (b = ExpectOp(")")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "Function parameters cannot be parenthesized" );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (ParseOptional(() => Parse_Tmp74()) == null || true) &&
                    ParseZeroOrMore(() => Parse_ParamMaybeDefault()) != null &&
                    ExpectOp("*") != null &&
                    Parse_Tmp75() != null &&
                    ParseZeroOrMore(() => Parse_ParamMaybeDefault()) != null &&
                    (a = ExpectOp("/")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "/ must be ahead of *" );
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ParseOneOrMore(() => Parse_ParamMaybeDefault()) != null &&
                    ExpectOp("/") != null &&
                    (a = ExpectOp("*")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "expected comma between / and *" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_default
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidDefault()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_default at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectOp("=")) != null &&
                    PositiveLookahead(() => Parse_Tmp76()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "expected default value expression" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_star_etc
        /// Alternatives: 4
        /// </summary>
        private GeneratedPtr? Parse_InvalidStarEtc()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_star_etc at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectOp("*")) != null &&
                    Parse_Tmp77() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "named arguments must follow bare *" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("*") != null &&
                    ExpectOp(",") != null &&
                    ExpectToken(PyToken.Type.TYPE_COMMENT) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "bare * has associated type comment" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("*") != null &&
                    Parse_Param() != null &&
                    (a = ExpectOp("=")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "var-positional argument cannot have default value" );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("*") != null &&
                    Parse_Tmp78() != null &&
                    ParseZeroOrMore(() => Parse_ParamMaybeDefault()) != null &&
                    (a = ExpectOp("*")) != null &&
                    Parse_Tmp79() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "* argument may appear only once" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_kwds
        /// Alternatives: 3
        /// </summary>
        private GeneratedPtr? Parse_InvalidKwds()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_kwds at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("**") != null &&
                    Parse_Param() != null &&
                    (a = ExpectOp("=")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "var-keyword argument cannot have default value" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (
                    ExpectOp("**") != null &&
                    Parse_Param() != null &&
                    ExpectOp(",") != null &&
                    (a = Parse_Param()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "arguments cannot follow var-keyword argument" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("**") != null &&
                    Parse_Param() != null &&
                    ExpectOp(",") != null &&
                    (a = (GeneratedTokenInfo)Parse_Tmp80()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "arguments cannot follow var-keyword argument" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_parameters_helper
        /// Alternatives: 2
        /// Return Type: GeneratedArguments
        /// </summary>
        private GeneratedArguments? Parse_InvalidParametersHelper()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_parameters_helper at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSlashWithDefault? a = null;

                if ((a = Parse_SlashWithDefault()) != null)
                {
                    // Action code from grammar
                    return null;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if ((a = ParseOneOrMore(() => Parse_ParamWithDefault())) != null)
                {
                    // Action code from grammar
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_lambda_parameters
        /// Alternatives: 6
        /// </summary>
        private GeneratedPtr? Parse_InvalidLambdaParameters()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_lambda_parameters at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectSoftKeyword("/")) != null &&
                    ExpectOp(",") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "at least one argument must precede /" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    Parse_Tmp81() != null &&
                    ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault()) != null &&
                    (a = ExpectOp("/")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "/ may appear only once" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (
                    (ParseOptional(() => Parse_LambdaSlashNoDefault()) == null || true) &&
                    ParseZeroOrMore(() => Parse_LambdaParamNoDefault()) != null &&
                    Parse_InvalidLambdaParametersHelper() != null &&
                    (a = Parse_LambdaParamNoDefault()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "parameter without a default follows parameter with a default" );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    ParseZeroOrMore(() => Parse_LambdaParamNoDefault()) != null &&
                    (a = ExpectOp("(")) != null &&
                    ParseGatherPlus(() => ExpectOp(","), () => Parse_LambdaParam()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    (b = ExpectOp(")")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "Lambda expression parameters cannot be parenthesized" );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (ParseOptional(() => Parse_Tmp82()) == null || true) &&
                    ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault()) != null &&
                    ExpectOp("*") != null &&
                    Parse_Tmp83() != null &&
                    ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault()) != null &&
                    (a = ExpectOp("/")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "/ must be ahead of *" );
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ParseOneOrMore(() => Parse_LambdaParamMaybeDefault()) != null &&
                    ExpectOp("/") != null &&
                    (a = ExpectOp("*")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "expected comma between / and *" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_lambda_parameters_helper
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidLambdaParametersHelper()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_lambda_parameters_helper at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSlashWithDefault? a = null;

                if ((a = Parse_LambdaSlashWithDefault()) != null)
                {
                    // Action code from grammar
                    return SingletonSeq ( a );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ParseOneOrMore(() => Parse_LambdaParamWithDefault())) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_lambda_star_etc
        /// Alternatives: 3
        /// </summary>
        private GeneratedPtr? Parse_InvalidLambdaStarEtc()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_lambda_star_etc at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("*") != null &&
                    Parse_Tmp84() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "named arguments must follow bare *" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("*") != null &&
                    Parse_LambdaParam() != null &&
                    (a = ExpectOp("=")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "var-positional argument cannot have default value" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("*") != null &&
                    Parse_Tmp85() != null &&
                    ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault()) != null &&
                    (a = ExpectOp("*")) != null &&
                    Parse_Tmp86() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "* argument may appear only once" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_lambda_kwds
        /// Alternatives: 3
        /// </summary>
        private GeneratedPtr? Parse_InvalidLambdaKwds()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_lambda_kwds at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("**") != null &&
                    Parse_LambdaParam() != null &&
                    (a = ExpectOp("=")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "var-keyword argument cannot have default value" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (
                    ExpectOp("**") != null &&
                    Parse_LambdaParam() != null &&
                    ExpectOp(",") != null &&
                    (a = Parse_LambdaParam()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "arguments cannot follow var-keyword argument" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("**") != null &&
                    Parse_LambdaParam() != null &&
                    ExpectOp(",") != null &&
                    (a = (GeneratedTokenInfo)Parse_Tmp87()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "arguments cannot follow var-keyword argument" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_double_type_comments
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidDoubleTypeComments()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_double_type_comments at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectToken(PyToken.Type.TYPE_COMMENT) != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    ExpectToken(PyToken.Type.TYPE_COMMENT) != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    ExpectToken(PyToken.Type.INDENT) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "Cannot have two type comments on def" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_with_item
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidWithItem()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_with_item at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    Parse_Expression() != null &&
                    ExpectKeyword("as") != null &&
                    (a = Parse_Expression()) != null &&
                    PositiveLookahead(() => Parse_Tmp88()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorInvalidTarget ( "assign to" , a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_for_target
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidForTarget()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_for_target at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (ParseOptional(() => ExpectToken(PyToken.Type.ASYNC)) == null || true) &&
                    ExpectKeyword("for") != null &&
                    (a = Parse_StarExpressions()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorInvalidTarget ( "use in for loop" , a );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_group
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidGroup()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_group at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectOp("(") != null &&
                    (a = Parse_StarredExpression()) != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "cannot use starred expression here" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("(") != null &&
                    (a = ExpectOp("**")) != null &&
                    Parse_Expression() != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "cannot use double starred expression here" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_import
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidImport()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_import at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("import")) != null &&
                    ParseGatherPlus(() => ExpectOp(","), () => Parse_DottedName()) != null &&
                    ExpectKeyword("from") != null &&
                    Parse_DottedName() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorStartingFrom ( a , "Did you mean to use 'from ... import ...' instead?" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_import_from_targets
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidImportFromTargets()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_import_from_targets at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_ImportFromAsNames() != null &&
                    ExpectOp(",") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "trailing comma not allowed without surrounding parentheses" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_with_stmt
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidWithStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_with_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    (ParseOptional(() => ExpectToken(PyToken.Type.ASYNC)) == null || true) &&
                    ExpectKeyword("with") != null &&
                    ParseGatherPlus(() => ExpectOp(","), () => Parse_Tmp89()) != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    (ParseOptional(() => ExpectToken(PyToken.Type.ASYNC)) == null || true) &&
                    ExpectKeyword("with") != null &&
                    ExpectOp("(") != null &&
                    ParseGatherPlus(() => ExpectOp(","), () => Parse_Tmp90()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp(")") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_with_stmt_indent
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidWithStmtIndent()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_with_stmt_indent at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (ParseOptional(() => ExpectToken(PyToken.Type.ASYNC)) == null || true) &&
                    (a = ExpectKeyword("with")) != null &&
                    ParseGatherPlus(() => ExpectOp(","), () => Parse_Tmp91()) != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'with' statement on line %d" , a . GetLineNo ());
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (ParseOptional(() => ExpectToken(PyToken.Type.ASYNC)) == null || true) &&
                    (a = ExpectKeyword("with")) != null &&
                    ExpectOp("(") != null &&
                    ParseGatherPlus(() => ExpectOp(","), () => Parse_Tmp92()) != null &&
                    (ParseOptional(() => ExpectOp(",")) == null || true) &&
                    ExpectOp(")") != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'with' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_try_stmt
        /// Alternatives: 4
        /// </summary>
        private GeneratedPtr? Parse_InvalidTryStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_try_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("try")) != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'try' statement on line %d" , a . GetLineNo ());
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("try") != null &&
                    ExpectOp(":") != null &&
                    Parse_Block() != null &&
                    NegativeLookahead(() => Parse_Tmp93()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected 'except' or 'finally' block" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (
                    ExpectKeyword("try") != null &&
                    ExpectOp(":") != null &&
                    ParseZeroOrMore(() => Parse_Block()) != null &&
                    ParseOneOrMore(() => Parse_ExceptBlock()) != null &&
                    (a = ExpectKeyword("except")) != null &&
                    (b = ExpectOp("*")) != null &&
                    Parse_Expression() != null &&
                    (ParseOptional(() => Parse_Tmp94()) == null || true) &&
                    ExpectOp(":") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "cannot have both 'except' and 'except*' on the same 'try'" );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectKeyword("try") != null &&
                    ExpectOp(":") != null &&
                    ParseZeroOrMore(() => Parse_Block()) != null &&
                    ParseOneOrMore(() => Parse_ExceptStarBlock()) != null &&
                    (a = ExpectKeyword("except")) != null &&
                    (ParseOptional(() => Parse_Tmp95()) == null || true) &&
                    ExpectOp(":") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "cannot have both 'except' and 'except*' on the same 'try'" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_except_stmt
        /// Alternatives: 4
        /// </summary>
        private GeneratedPtr? Parse_InvalidExceptStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_except_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    ExpectKeyword("except") != null &&
                    (ParseOptional(() => ExpectOp("*")) == null || true) &&
                    (a = Parse_Expression()) != null &&
                    ExpectOp(",") != null &&
                    Parse_Expressions() != null &&
                    (ParseOptional(() => Parse_Tmp96()) == null || true) &&
                    ExpectOp(":") != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorStartingFrom ( a , "multiple exception types must be parenthesized" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("except")) != null &&
                    (ParseOptional(() => ExpectOp("*")) == null || true) &&
                    Parse_Expression() != null &&
                    (ParseOptional(() => Parse_Tmp97()) == null || true) &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("except")) != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("except")) != null &&
                    ExpectOp("*") != null &&
                    Parse_Tmp98() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected one or more exception types" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_finally_stmt
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidFinallyStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_finally_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("finally")) != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'finally' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_except_stmt_indent
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidExceptStmtIndent()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_except_stmt_indent at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("except")) != null &&
                    Parse_Expression() != null &&
                    (ParseOptional(() => Parse_Tmp99()) == null || true) &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'except' statement on line %d" , a . GetLineNo ());
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("except")) != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'except' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_except_star_stmt_indent
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidExceptStarStmtIndent()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_except_star_stmt_indent at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("except")) != null &&
                    ExpectOp("*") != null &&
                    Parse_Expression() != null &&
                    (ParseOptional(() => Parse_Tmp100()) == null || true) &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'except*' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_match_stmt
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidMatchStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_match_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectSoftKeyword("match") != null &&
                    Parse_SubjectExpr() != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    return CheckVersion ( 10 , "Pattern matching is" , RaiseSyntaxError ( "expected ':'" ));
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? subject = null;

                if (
                    (a = ExpectSoftKeyword("match")) != null &&
                    (subject = Parse_SubjectExpr()) != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'match' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_case_block
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidCaseBlock()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_case_block at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectSoftKeyword("case") != null &&
                    Parse_Patterns() != null &&
                    (ParseOptional(() => Parse_Guard()) == null || true) &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectSoftKeyword("case")) != null &&
                    Parse_Patterns() != null &&
                    (ParseOptional(() => Parse_Guard()) == null || true) &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'case' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_as_pattern
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidAsPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_as_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    Parse_OrPattern() != null &&
                    ExpectKeyword("as") != null &&
                    (a = ExpectSoftKeyword("_")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "cannot use '_' as a target" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    Parse_OrPattern() != null &&
                    ExpectKeyword("as") != null &&
                    NegativeLookahead(() => ExpectName()) != null &&
                    (a = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "invalid pattern target" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_class_pattern
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidClassPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_class_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? a = null;

                if (
                    Parse_NameOrAttr() != null &&
                    ExpectOp("(") != null &&
                    (a = Parse_InvalidClassArgumentPattern()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( FirstItem < GeneratedPattern >( a ), LastItem < GeneratedPattern >( a ), "positional patterns follow keyword patterns" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_class_argument_pattern
        /// Alternatives: 1
        /// Return Type: GeneratedPatternSeq
        /// </summary>
        private GeneratedPatternSeq? Parse_InvalidClassArgumentPattern()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_class_argument_pattern at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? a = null;

                if (
                    (ParseOptional(() => Parse_Tmp101()) == null || true) &&
                    Parse_KeywordPatterns() != null &&
                    ExpectOp(",") != null &&
                    (a = Parse_PositionalPatterns()) != null
                )
                {
                    // Action code from grammar
                    return a;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_if_stmt
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidIfStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_if_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("if") != null &&
                    Parse_NamedExpression() != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("if")) != null &&
                    Parse_NamedExpression() != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'if' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_elif_stmt
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidElifStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_elif_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("elif") != null &&
                    Parse_NamedExpression() != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("elif")) != null &&
                    Parse_NamedExpression() != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'elif' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_else_stmt
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidElseStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_else_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("else")) != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'else' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_while_stmt
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidWhileStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_while_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("while") != null &&
                    Parse_NamedExpression() != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("while")) != null &&
                    Parse_NamedExpression() != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'while' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_for_stmt
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidForStmt()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_for_stmt at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    (ParseOptional(() => ExpectToken(PyToken.Type.ASYNC)) == null || true) &&
                    ExpectKeyword("for") != null &&
                    Parse_StarTargets() != null &&
                    ExpectKeyword("in") != null &&
                    Parse_StarExpressions() != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (ParseOptional(() => ExpectToken(PyToken.Type.ASYNC)) == null || true) &&
                    (a = ExpectKeyword("for")) != null &&
                    Parse_StarTargets() != null &&
                    ExpectKeyword("in") != null &&
                    Parse_StarExpressions() != null &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after 'for' statement on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_def_raw
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidDefRaw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_def_raw at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (ParseOptional(() => ExpectToken(PyToken.Type.ASYNC)) == null || true) &&
                    (a = ExpectKeyword("def")) != null &&
                    ExpectName() != null &&
                    (ParseOptional(() => Parse_TypeParams()) == null || true) &&
                    ExpectOp("(") != null &&
                    (ParseOptional(() => Parse_Params()) == null || true) &&
                    ExpectOp(")") != null &&
                    (ParseOptional(() => Parse_Tmp102()) == null || true) &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after function definition on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_class_def_raw
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidClassDefRaw()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_class_def_raw at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("class") != null &&
                    ExpectName() != null &&
                    (ParseOptional(() => Parse_TypeParams()) == null || true) &&
                    (ParseOptional(() => Parse_Tmp103()) == null || true) &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxError ( "expected ':'" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    (a = ExpectKeyword("class")) != null &&
                    ExpectName() != null &&
                    (ParseOptional(() => Parse_TypeParams()) == null || true) &&
                    (ParseOptional(() => Parse_Tmp104()) == null || true) &&
                    ExpectOp(":") != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    NegativeLookahead(() => ExpectToken(PyToken.Type.INDENT)) != null
                )
                {
                    // Action code from grammar
                    return RaiseIndentationError ( "expected an indented block after class definition on line %d" , a . GetLineNo ());
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_double_starred_kvpairs
        /// Alternatives: 3
        /// </summary>
        private GeneratedPtr? Parse_InvalidDoubleStarredKvpairs()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_double_starred_kvpairs at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ParseGatherPlus(() => ExpectOp(","), () => Parse_DoubleStarredKvpair()) != null &&
                    ExpectOp(",") != null &&
                    Parse_InvalidKvpair() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    Parse_Expression() != null &&
                    ExpectOp(":") != null &&
                    (a = ExpectOp("*")) != null &&
                    Parse_BitwiseOr() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorStartingFrom ( a , "cannot use a starred expression in a dictionary value" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    Parse_Expression() != null &&
                    (a = ExpectOp(":")) != null &&
                    PositiveLookahead(() => Parse_Tmp105()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "expression expected after dictionary key and ':'" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_kvpair
        /// Alternatives: 3
        /// </summary>
        private GeneratedPtr? Parse_InvalidKvpair()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_kvpair at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (
                    (a = Parse_Expression()) != null &&
                    NegativeLookahead(() => Parse_Tmp106()) != null
                )
                {
                    // Action code from grammar
                    return RaiseErrorKnownLocation ( typeof ( PySyntaxErrorException ), a . GetLineNo (), a . EndColOffset - 1 , a . EndLineNo ,- 1 , "':' expected after dictionary key" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    Parse_Expression() != null &&
                    ExpectOp(":") != null &&
                    (a = ExpectOp("*")) != null &&
                    Parse_BitwiseOr() != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorStartingFrom ( a , "cannot use a starred expression in a dictionary value" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    Parse_Expression() != null &&
                    (a = ExpectOp(":")) != null &&
                    PositiveLookahead(() => Parse_Tmp107()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "expression expected after dictionary key and ':'" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_starred_expression
        /// Alternatives: 1
        /// </summary>
        private GeneratedPtr? Parse_InvalidStarredExpression()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_starred_expression at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if (
                    (a = ExpectOp("*")) != null &&
                    Parse_Expression() != null &&
                    ExpectOp("=") != null &&
                    (b = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownRange ( a , b , "cannot assign to iterable argument unpacking" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_replacement_field
        /// Alternatives: 11
        /// </summary>
        private GeneratedPtr? Parse_InvalidReplacementField()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_replacement_field at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("{") != null &&
                    (a = ExpectOp("=")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "f-string: valid expression required before '='" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("{") != null &&
                    (a = ExpectOp("!")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "f-string: valid expression required before '!'" );
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("{") != null &&
                    (a = ExpectOp(":")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "f-string: valid expression required before ':'" );
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (
                    ExpectOp("{") != null &&
                    (a = ExpectOp("}")) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorKnownLocation ( a , "f-string: valid expression required before '}'" );
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("{") != null &&
                    NegativeLookahead(() => Parse_Tmp108()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorOnNextToken ( "f-string: expecting a valid expression after '{'" );
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("{") != null &&
                    Parse_Tmp109() != null &&
                    NegativeLookahead(() => Parse_Tmp110()) != null
                )
                {
                    // Action code from grammar
                    return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting '=', or '!', or ':', or '}'" );
                }
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("{") != null &&
                    Parse_Tmp111() != null &&
                    ExpectOp("=") != null &&
                    NegativeLookahead(() => Parse_Tmp112()) != null
                )
                {
                    // Action code from grammar
                    return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting '!', or ':', or '}'" );
                }
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("{") != null &&
                    Parse_Tmp113() != null &&
                    (ParseOptional(() => ExpectOp("=")) == null || true) &&
                    Parse_InvalidConversionCharacter() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("{") != null &&
                    Parse_Tmp114() != null &&
                    (ParseOptional(() => ExpectOp("=")) == null || true) &&
                    (ParseOptional(() => Parse_Tmp115()) == null || true) &&
                    NegativeLookahead(() => Parse_Tmp116()) != null
                )
                {
                    // Action code from grammar
                    return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting ':' or '}'" );
                }
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("{") != null &&
                    Parse_Tmp117() != null &&
                    (ParseOptional(() => ExpectOp("=")) == null || true) &&
                    (ParseOptional(() => Parse_Tmp118()) == null || true) &&
                    ExpectOp(":") != null &&
                    ParseZeroOrMore(() => Parse_FstringFormatSpec()) != null &&
                    NegativeLookahead(() => ExpectOp("}")) != null
                )
                {
                    // Action code from grammar
                    return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting '}', or format specs" );
                }
            }

            // Alternative 11
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("{") != null &&
                    Parse_Tmp119() != null &&
                    (ParseOptional(() => ExpectOp("=")) == null || true) &&
                    (ParseOptional(() => Parse_Tmp120()) == null || true) &&
                    NegativeLookahead(() => ExpectOp("}")) != null
                )
                {
                    // Action code from grammar
                    return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting '}'" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_conversion_character
        /// Alternatives: 2
        /// </summary>
        private GeneratedPtr? Parse_InvalidConversionCharacter()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] invalid_conversion_character at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("!") != null &&
                    PositiveLookahead(() => Parse_Tmp121()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorOnNextToken ( "f-string: missing conversion character" );
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("!") != null &&
                    NegativeLookahead(() => ExpectName()) != null
                )
                {
                    // Action code from grammar
                    RaiseSyntaxErrorOnNextToken ( "f-string: invalid conversion character" );
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_1
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp1()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_1 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("import")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("from")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_2
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp2()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_2 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("def")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("@")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectToken(PyToken.Type.ASYNC)) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_3
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp3()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_3 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("class")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("@")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_4
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp4()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_4 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("with")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectToken(PyToken.Type.ASYNC)) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_5
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp5()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_5 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("for")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectToken(PyToken.Type.ASYNC)) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_6
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp6()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_6 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? d = null;

                if (
                    ExpectOp("=") != null &&
                    (d = Parse_AnnotatedRhs()) != null
                )
                {
                    // Action code from grammar
                    return d;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_7
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp7()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_7 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? b = null;

                if (
                    ExpectOp("(") != null &&
                    (b = Parse_SingleTarget()) != null &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return b;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_SingleSubscriptAttributeTarget()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_8
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp8()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_8 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? d = null;

                if (
                    ExpectOp("=") != null &&
                    (d = Parse_AnnotatedRhs()) != null
                )
                {
                    // Action code from grammar
                    return d;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_9
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp9()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_9 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? z = null;

                if (
                    (z = Parse_StarTargets()) != null &&
                    ExpectOp("=") != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_10
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp10()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_10 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_11
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp11()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_11 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_12
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp12()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_12 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? z = null;

                if (
                    ExpectKeyword("from") != null &&
                    (z = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_13
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp13()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_13 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(";")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectToken(PyToken.Type.NEWLINE)) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_14
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp14()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_14 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? z = null;

                if (
                    ExpectOp(",") != null &&
                    (z = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_15
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp15()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_15 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(".")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("...")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_16
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp16()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_16 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(".")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("...")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_17
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp17()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_17 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? z = null;

                if (
                    ExpectKeyword("as") != null &&
                    (z = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_18
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp18()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_18 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? z = null;

                if (
                    ExpectKeyword("as") != null &&
                    (z = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_19
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp19()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_19 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? f = null;

                if (
                    ExpectOp("@") != null &&
                    (f = Parse_NamedExpression()) != null &&
                    ExpectToken(PyToken.Type.NEWLINE) != null
                )
                {
                    // Action code from grammar
                    return f;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_20
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp20()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_20 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? z = null;

                if (
                    ExpectOp("(") != null &&
                    ((z = (GeneratedExpr)ParseOptional(() => Parse_Arguments())) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_21
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp21()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_21 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? z = null;

                if (
                    ExpectOp("->") != null &&
                    (z = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_22
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp22()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_22 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? z = null;

                if (
                    ExpectOp("->") != null &&
                    (z = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_23
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp23()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_23 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(")")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_24
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp24()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_24 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? z = null;

                if (
                    ExpectKeyword("as") != null &&
                    (z = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_25
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp25()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_25 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? z = null;

                if (
                    ExpectKeyword("as") != null &&
                    (z = ExpectName()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_26
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp26()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_26 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("+")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("-")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_27
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp27()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_27 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("+")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("-")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_28
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp28()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_28 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(".")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("(")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("=")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_29
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp29()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_29 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(".")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("(")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("=")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_30
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp30()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_30 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_LiteralExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Attr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_31
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp31()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_31 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? c = null;

                if (
                    ExpectOp(",") != null &&
                    (c = Parse_Expression()) != null
                )
                {
                    // Action code from grammar
                    return c;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_32
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp32()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_32 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? c = null;

                if (
                    ExpectOp(",") != null &&
                    (c = Parse_StarExpression()) != null
                )
                {
                    // Action code from grammar
                    return c;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_33
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp33()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_33 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? c = null;

                if (
                    ExpectKeyword("or") != null &&
                    (c = Parse_Conjunction()) != null
                )
                {
                    // Action code from grammar
                    return c;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_34
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp34()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_34 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? c = null;

                if (
                    ExpectKeyword("and") != null &&
                    (c = Parse_Inversion()) != null
                )
                {
                    // Action code from grammar
                    return c;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_35
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp35()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_35 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? tok = null;

                if ((tok = ExpectOp("!=")) != null)
                {
                    // Action code from grammar
                    return ASTHelpers . CheckBarryAsFlufl ( tok )? null : tok;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_36
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp36()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_36 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Slice()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarredExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_37
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp37()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_37 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? d = null;

                if (
                    ExpectOp(":") != null &&
                    ((d = (GeneratedExpr)ParseOptional(() => Parse_Expression())) == null || true)
                )
                {
                    // Action code from grammar
                    return d;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_38
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp38()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_38 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectToken(PyToken.Type.STRING)) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectToken(PyToken.Type.FSTRING_START)) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_39
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp39()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_39 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Tuple()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Group()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Genexp()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_40
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp40()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_40 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_List()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Listcomp()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_41
        /// Alternatives: 4
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp41()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_41 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Dict()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Set()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Dictcomp()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Setcomp()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_42
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp42()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_42 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_NamedExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_43
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp43()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_43 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_44
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp44()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_44 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Fstring()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_String()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_45
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp45()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_45 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? y = null;
                GeneratedExprSeq? z = null;

                if (
                    (y = Parse_StarNamedExpression()) != null &&
                    ExpectOp(",") != null &&
                    ((z = (GeneratedExprSeq)ParseOptional(() => Parse_StarNamedExpressions())) == null || true)
                )
                {
                    // Action code from grammar
                    return PyParserHelpers . SeqInsertInFront ( y , z ). Cast < GeneratedExprSeq >();
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_46
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp46()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_46 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? z = null;

                if (
                    ExpectKeyword("if") != null &&
                    (z = Parse_Disjunction()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_47
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp47()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_47 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? z = null;

                if (
                    ExpectKeyword("if") != null &&
                    (z = Parse_Disjunction()) != null
                )
                {
                    // Action code from grammar
                    return z;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_48
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp48()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_48 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_AssignmentExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expression() != null &&
                    NegativeLookahead(() => ExpectOp(":=")) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_49
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp49()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_49 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarredExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Tmp122() != null &&
                    NegativeLookahead(() => ExpectOp("=")) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_50
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp50()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_50 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? k = null;

                if (
                    ExpectOp(",") != null &&
                    (k = Parse_Kwargs()) != null
                )
                {
                    // Action code from grammar
                    return k;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_51
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp51()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_51 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? c = null;

                if (
                    ExpectOp(",") != null &&
                    (c = Parse_StarTarget()) != null
                )
                {
                    // Action code from grammar
                    return c;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_52
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp52()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_52 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? c = null;

                if (
                    ExpectOp(",") != null &&
                    (c = Parse_StarTarget()) != null
                )
                {
                    // Action code from grammar
                    return c;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_53
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp53()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_53 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    NegativeLookahead(() => ExpectOp("*")) != null &&
                    Parse_StarTarget() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_54
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp54()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_54 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectToken(PyToken.Type.NEWLINE) != null &&
                    ExpectToken(PyToken.Type.INDENT) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_55
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp55()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_55 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = Parse_Tmp123()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Kwargs()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_56
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp56()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_56 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_StarredExpression() != null &&
                    NegativeLookahead(() => ExpectOp("=")) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_57
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp57()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_57 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Args()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expression() != null &&
                    Parse_ForIfClauses() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_58
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp58()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_58 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Args() != null &&
                    ExpectOp(",") != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_59
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp59()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_59 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(")")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_60
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp60()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_60 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("True")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("False")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("None")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_61
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp61()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_61 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectName() != null &&
                    ExpectOp("=") != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_62
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp62()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_62 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectName() != null &&
                    ExpectToken(PyToken.Type.STRING) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectToken(PyToken.Type.SOFT_KEYWORD)) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_63
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp63()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_63 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("else")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_64
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp64()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_64 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("=")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":=")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_65
        /// Alternatives: 6
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp65()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_65 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_List()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Tuple()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_Genexp()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("True")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("None")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("False")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_66
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp66()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_66 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("=")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":=")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_67
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp67()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_67 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_StarTargets() != null &&
                    ExpectOp("=") != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_68
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp68()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_68 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_StarTargets() != null &&
                    ExpectOp("=") != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_69
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp69()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_69 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_70
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp70()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_70 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("[")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("(")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("{")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_71
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp71()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_71 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("[")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("{")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_72
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp72()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_72 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("[")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("{")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_73
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp73()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_73 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_SlashNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_SlashWithDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_74
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp74()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_74 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_SlashNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_SlashWithDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_75
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp75()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_75 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_ParamNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_76
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp76()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_76 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(")")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_77
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp77()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_77 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(")")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp(",") != null &&
                    Parse_Tmp124() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_78
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp78()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_78 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_ParamNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_79
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp79()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_79 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_ParamNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_80
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp80()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_80 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("*")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("**")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("/")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_81
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp81()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_81 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_LambdaSlashNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_LambdaSlashWithDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_82
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp82()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_82 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_LambdaSlashNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_LambdaSlashWithDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_83
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp83()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_83 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_LambdaParamNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_84
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp84()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_84 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp(",") != null &&
                    Parse_Tmp125() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_85
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp85()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_85 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_LambdaParamNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_86
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp86()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_86 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_LambdaParamNoDefault()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_87
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp87()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_87 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("*")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("**")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("/")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_88
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp88()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_88 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(")")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_89
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp89()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_89 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expression() != null &&
                    (ParseOptional(() => Parse_Tmp126()) == null || true)
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_90
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp90()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_90 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expressions() != null &&
                    (ParseOptional(() => Parse_Tmp127()) == null || true)
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_91
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp91()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_91 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expression() != null &&
                    (ParseOptional(() => Parse_Tmp128()) == null || true)
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_92
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp92()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_92 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expressions() != null &&
                    (ParseOptional(() => Parse_Tmp129()) == null || true)
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_93
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp93()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_93 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("except")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectKeyword("finally")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_94
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp94()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_94 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    ExpectName() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_95
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp95()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_95 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expression() != null &&
                    (ParseOptional(() => Parse_Tmp130()) == null || true)
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_96
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp96()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_96 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    ExpectName() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_97
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp97()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_97 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    ExpectName() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_98
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp98()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_98 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectToken(PyToken.Type.NEWLINE)) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_99
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp99()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_99 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    ExpectName() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_100
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp100()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_100 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    ExpectName() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_101
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp101()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_101 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_PositionalPatterns() != null &&
                    ExpectOp(",") != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_102
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp102()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_102 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("->") != null &&
                    Parse_Expression() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_103
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp103()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_103 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("(") != null &&
                    (ParseOptional(() => Parse_Arguments()) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_104
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp104()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_104 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("(") != null &&
                    (ParseOptional(() => Parse_Arguments()) == null || true) &&
                    ExpectOp(")") != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_105
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp105()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_105 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("}")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_106
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp106()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_106 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_107
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp107()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_107 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("}")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(",")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_108
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp108()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_108 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_109
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp109()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_109 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_110
        /// Alternatives: 4
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp110()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_110 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("=")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("!")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("}")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_111
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp111()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_111 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_112
        /// Alternatives: 3
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp112()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_112 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("!")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("}")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_113
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp113()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_113 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_114
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp114()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_114 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_115
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp115()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_115 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("!") != null &&
                    ExpectName() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_116
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp116()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_116 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("}")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_117
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp117()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_117 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_118
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp118()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_118 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("!") != null &&
                    ExpectName() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_119
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp119()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_119 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_YieldExpr()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarExpressions()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_120
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp120()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_120 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectOp("!") != null &&
                    ExpectName() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_121
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp121()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_121 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("}")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_122
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp122()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_122 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_AssignmentExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expression() != null &&
                    NegativeLookahead(() => ExpectOp(":=")) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_123
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp123()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_123 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ParseGatherPlus(() => ExpectOp(","), () => Parse_Tmp131()) != null &&
                    ExpectOp(",") != null &&
                    Parse_Kwargs() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_124
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp124()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_124 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(")")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("**")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_125
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp125()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_125 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp(":")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)ExpectOp("**")) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_126
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp126()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_126 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    Parse_StarTarget() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_127
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp127()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_127 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    Parse_StarTarget() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_128
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp128()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_128 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    Parse_StarTarget() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_129
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp129()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_129 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    Parse_StarTarget() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_130
        /// Alternatives: 1
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp130()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_130 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();


                if (
                    ExpectKeyword("as") != null &&
                    ExpectName() != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_131
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp131()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_131 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_StarredExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Tmp132() != null &&
                    NegativeLookahead(() => ExpectOp("=")) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: _tmp_132
        /// Alternatives: 2
        /// Return Type: GeneratedPtr
        /// </summary>
        private GeneratedPtr? Parse_Tmp132()
        {
            int _mark = Mark();

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[RULE] _tmp_132 at pos={_position}");
            #endif

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? _alt_var = null;

                if ((_alt_var = (GeneratedPtr)Parse_AssignmentExpression()) != null)
                {
                    // Default action: return single unnamed item
                    return _alt_var;
                }
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (
                    Parse_Expression() != null &&
                    NegativeLookahead(() => ExpectOp(":=")) != null
                )
                {
                    // Default action: no captures (unexpected)
                    return null;
                }
            }

            Reset(_mark);
            return null;
        }

        // ============================================================
        // GetKeywordOrNameType - Abstract method implementation
        // ============================================================

        /// <summary>
        /// Returns PyToken.Type for keyword or NAME
        /// CPython: Parser/pegen.c - _PyPegen_keyword_or_name_type
        /// CPython 3.12: All keywords are tokenized as NAME, this method distinguishes them
        /// </summary>
        protected override int GetKeywordOrNameType(string name, int nameLen)
        {
            // CPython 3.12: Keywords are parsed as NAME tokens
            // The parser uses string comparison to identify keywords
            // All keywords and names return NAME token type
            return (int)PyToken.Type.NAME;
        }

        // ============================================================
        // Helper Methods (inherited from PyParserBase)
        // ============================================================

        // Note: All helper methods (Mark, Reset, Expect*, ParseOptional,
        // ParseZeroOrMore, ParseOneOrMore, etc.) are inherited from PyParserBase

        /// <summary>
        /// Check if a string is a Python keyword - for backward compatibility
        /// </summary>
        public static bool IsKeyword(string name)
        {
            return _hardKeywords.Contains(name);
        }

        // Hard keywords set for IsKeyword check
        private static readonly HashSet<string> _hardKeywords = new()
        {
            "and",
            "as",
            "assert",
            "async",
            "await",
            "break",
            "class",
            "continue",
            "def",
            "del",
            "elif",
            "else",
            "except",
            "False",
            "finally",
            "for",
            "from",
            "global",
            "if",
            "import",
            "in",
            "is",
            "lambda",
            "None",
            "nonlocal",
            "not",
            "or",
            "pass",
            "raise",
            "return",
            "True",
            "try",
            "while",
            "with",
            "yield",
        };

        // ============================================================
        // @trailer code (Entry Points and Custom Methods)
        // ============================================================

        // CPython 3.12: @trailer block contains entry point methods
        // that map to grammar rules (file, interactive, eval, etc.)


        // CPython 3.12: Entry point for file parsing
        public GeneratedMod ParseFile()
        {
        var result = Parse_File();
        if (result == null || _pendingSyntaxError != null)
        {
        var errorMsg = _pendingSyntaxError ?? "invalid syntax";

        // CPython 3.12: Get error location from known error token or current position
        // _knownErrToken is set by RaiseSyntaxError, but for parse failures we use _position
        GeneratedTokenInfo errorToken = _knownErrToken;

        // If no known error token, use the token at current parse position
        if (errorToken == null && _position < _tokens.Count)
        {
        errorToken = _tokens[_position];
        }
        // Fallback: use first token if no error token found
        else if (errorToken == null && _tokens.Count > 0)
        {
        errorToken = _tokens[0];
        }

        // CPython 3.12: If we're at a valid position, the error token should be here
        if (errorToken != null)
        {
        var ex = new PySyntaxErrorException(errorMsg, _filename,
        errorToken.Line, errorToken.Column,
        errorToken.EndColumn,
        GetSourceLine(errorToken.Line));
        throw ex;
        }

        throw new PySyntaxErrorException(errorMsg);
        }
        return result;
        }

        // CPython 3.12: Entry point for interactive parsing
        public GeneratedMod ParseInteractive()
        {
        var result = Parse_Interactive();
        if (result == null || _pendingSyntaxError != null)
        {
        var errorMsg = _pendingSyntaxError ?? "invalid syntax";

        GeneratedTokenInfo errorToken = _knownErrToken;
        if (errorToken == null && _position < _tokens.Count)
        {
        errorToken = _tokens[_position];
        }
        else if (errorToken == null && _tokens.Count > 0)
        {
        errorToken = _tokens[0];
        }

        if (errorToken != null)
        {
        var ex = new PySyntaxErrorException(errorMsg, _filename,
        errorToken.Line, errorToken.Column,
        errorToken.EndColumn,
        GetSourceLine(errorToken.Line));
        throw ex;
        }

        throw new PySyntaxErrorException(errorMsg);
        }
        return result;
        }

        // CPython 3.12: Entry point for eval parsing
        public GeneratedMod ParseEval()
        {
        var result = Parse_Eval();
        if (result == null || _pendingSyntaxError != null)
        {
        var errorMsg = _pendingSyntaxError ?? "invalid syntax";

        GeneratedTokenInfo errorToken = _knownErrToken;
        if (errorToken == null && _position < _tokens.Count)
        {
        errorToken = _tokens[_position];
        }
        else if (errorToken == null && _tokens.Count > 0)
        {
        errorToken = _tokens[0];
        }

        if (errorToken != null)
        {
        var ex = new PySyntaxErrorException(errorMsg, _filename,
        errorToken.Line, errorToken.Column,
        errorToken.EndColumn,
        GetSourceLine(errorToken.Line));
        throw ex;
        }

        throw new PySyntaxErrorException(errorMsg);
        }
        return result;
        }

        // CPython 3.12: Entry point for function type parsing
        public GeneratedMod ParseFuncType()
        {
        var result = Parse_FuncType();
        if (result == null || _pendingSyntaxError != null)
        {
        var errorMsg = _pendingSyntaxError ?? "invalid syntax";

        GeneratedTokenInfo errorToken = _knownErrToken;
        if (errorToken == null && _position < _tokens.Count)
        {
        errorToken = _tokens[_position];
        }
        else if (errorToken == null && _tokens.Count > 0)
        {
        errorToken = _tokens[0];
        }

        if (errorToken != null)
        {
        var ex = new PySyntaxErrorException(errorMsg, _filename,
        errorToken.Line, errorToken.Column,
        errorToken.EndColumn,
        GetSourceLine(errorToken.Line));
        throw ex;
        }

        throw new PySyntaxErrorException(errorMsg);
        }
        return result;
        }


    }
}
