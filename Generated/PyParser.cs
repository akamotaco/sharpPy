// Generated Parser from python_py.gram
// CPython 3.12 compatible - Auto-generated, DO NOT EDIT

using System;
using System.Collections.Generic;
using SharpPy.Generated;

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
        // Grammar Rules (193 rules from python_py.gram)
        // ============================================================

        /// <summary>
        /// Rule: file
        /// Alternatives: 1
        /// Return Type: GeneratedMod
        /// </summary>
        private GeneratedMod? Parse_File()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if ((a = (GeneratedStmtSeq)ParseOptional(() => Parse_Statements())) == null) return null;
                if (Expect(PyToken.Type.ENDMARKER, "ENDMARKER") == null) return null;

                // Action code from grammar
                return ( GeneratedMod ) PyParserHelpers . MakeModule (( GeneratedStmtSeq ?) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if ((a = Parse_StatementNewline()) == null) return null;

                // Action code from grammar
                return ( GeneratedMod ) PyAst . Interactive (( GeneratedStmtSeq ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_Expressions()) == null) return null;
                if (ParseZeroOrMore(() => Expect(PyToken.Type.NEWLINE, "NEWLINE")) == null) return null;
                if (Expect(PyToken.Type.ENDMARKER, "ENDMARKER") == null) return null;

                // Action code from grammar
                return ( GeneratedMod ) PyAst . Expression (( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;
                GeneratedExpr? b = null;

                if (ExpectOp("(") == null) return null;
                if ((a = (GeneratedExprSeq)ParseOptional(() => Parse_TypeExpressions())) == null) return null;
                if (ExpectOp(")") == null) return null;
                if (ExpectOp("->") == null) return null;
                if ((b = Parse_Expression()) == null) return null;
                if (ParseZeroOrMore(() => Expect(PyToken.Type.NEWLINE, "NEWLINE")) == null) return null;
                if (Expect(PyToken.Type.ENDMARKER, "ENDMARKER") == null) return null;

                // Action code from grammar
                return ( GeneratedMod ) PyAst . FunctionType (( GeneratedExprSeq ?) a ,( GeneratedExpr ) b );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if ((a = ParseOneOrMore(() => Parse_Statement())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . FlattenStatementSequence ( a ). Cast < GeneratedStmtSeq >();
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if ((a = Parse_CompoundStmt()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SingletonSequence (( GeneratedStmt ) a ). Cast < GeneratedStmtSeq >();
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if ((a = Parse_SimpleStmts()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if ((a = Parse_CompoundStmt()) == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                return PyParserHelpers . SingletonSequence (( GeneratedStmt ) a ). Cast < GeneratedStmtSeq >();
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_SimpleStmts() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                return PyParserHelpers . SingletonSequence ( Check < GeneratedStmt >( PyAst . Pass ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ))). Cast < GeneratedStmtSeq >();
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (Expect(PyToken.Type.ENDMARKER, "ENDMARKER") == null) return null;

                // Action code from grammar
                return PyParserHelpers . InteractiveExit ();
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if ((a = Parse_SimpleStmt()) == null) return null;
                if (NegativeLookahead(() => ExpectOp(";")) == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                return PyParserHelpers . SingletonSequence (( GeneratedStmt ) a ). Cast < GeneratedStmtSeq >();
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if ((a = ParseGatherPlus(() => ExpectOp(";"), () => Parse_SimpleStmt())) == null) return null;
                if (ParseOptional(() => ExpectOp(";")) == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                return ( GeneratedStmtSeq ) a;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: simple_stmt
        /// Alternatives: 14
        /// Return Type: GeneratedStmt
        /// </summary>
        private GeneratedStmt? Parse_SimpleStmt()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Assignment() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectSoftKeyword("type")) == null) return null;
                if (Parse_TypeAlias() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;

                if ((e = Parse_StarExpressions()) == null) return null;

                // Action code from grammar
                return PyAst . Expr (( GeneratedExpr ) e , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectKeyword("return")) == null) return null;
                if (Parse_ReturnStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ParseGroup()) == null) return null;
                if (Parse_ImportStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectKeyword("raise")) == null) return null;
                if (Parse_RaiseStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("pass") == null) return null;

                // Action code from grammar
                return PyAst . Pass ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectKeyword("del")) == null) return null;
                if (Parse_DelStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectKeyword("yield")) == null) return null;
                if (Parse_YieldStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectKeyword("assert")) == null) return null;
                if (Parse_AssertStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 11
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("break") == null) return null;

                // Action code from grammar
                return PyAst . Break ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 12
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("continue") == null) return null;

                // Action code from grammar
                return PyAst . Continue ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 13
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectKeyword("global")) == null) return null;
                if (Parse_GlobalStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 14
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectKeyword("nonlocal")) == null) return null;
                if (Parse_NonlocalStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (PositiveLookahead(() => ParseGroup()) == null) return null;
                if ((a = Parse_FunctionDef()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (PositiveLookahead(() => ExpectKeyword("if")) == null) return null;
                if ((a = Parse_IfStmt()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (PositiveLookahead(() => ParseGroup()) == null) return null;
                if ((a = Parse_ClassDef()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (PositiveLookahead(() => ParseGroup()) == null) return null;
                if ((a = Parse_WithStmt()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (PositiveLookahead(() => ParseGroup()) == null) return null;
                if ((a = Parse_ForStmt()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (PositiveLookahead(() => ExpectKeyword("try")) == null) return null;
                if ((a = Parse_TryStmt()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if (PositiveLookahead(() => ExpectKeyword("while")) == null) return null;
                if ((a = Parse_WhileStmt()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmt? a = null;

                if ((a = Parse_MatchStmt()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;
                GeneratedPtr? c = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Expression()) == null) return null;
                if ((c = ParseOptional(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return CheckVersion ( 6 , "Variable annotation syntax is" , PyAst . AnnAssign ( Check < GeneratedExpr >( PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedStore.Instance )),( GeneratedExpr ) b ,( GeneratedExpr ?) c , 1 , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;
                GeneratedExpr? b = null;
                GeneratedPtr? c = null;

                if ((a = ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Expression()) == null) return null;
                if ((c = ParseOptional(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return CheckVersion ( 6 , "Variable annotations syntax is" , PyAst . AnnAssign (( GeneratedExpr ) a ,( GeneratedExpr ) b ,( GeneratedExpr ?) c , 0 , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;
                GeneratedPtr? b = null;
                GeneratedTokenInfo? tc = null;

                if ((a = (GeneratedExprSeq)ParseOneOrMore(() => ParseGroup())) == null) return null;
                if ((b = ParseGroup()) == null) return null;
                if (NegativeLookahead(() => ExpectOp("=")) == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;

                // Action code from grammar
                return PyAst . Assign (( GeneratedExprSeq ) a ,( GeneratedExpr ) b , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedOperator? b = null;
                GeneratedPtr? c = null;

                if ((a = Parse_SingleTarget()) == null) return null;
                if ((b = Parse_Augassign()) == null) return null;
                if ((c = ParseGroup()) == null) return null;

                // Action code from grammar
                return PyAst . AugAssign (( GeneratedExpr ) a ,( GeneratedOperator ) b ,( GeneratedExpr ) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidAssignment() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_YieldExpr()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_StarExpressions()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("+=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedAdd.Instance );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("-=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedSub.Instance );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("*=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedMult.Instance );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("@=") == null) return null;

                // Action code from grammar
                return CheckVersion ( 5 , "The '@' operator is" , PyParserHelpers . AugOperator ( GeneratedMatMult.Instance ));
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("/=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedDiv.Instance );
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("%=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedMod_.Instance );
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("&=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedBitAnd.Instance );
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("|=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedBitOr.Instance );
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("^=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedBitXor.Instance );
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("<<=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedLShift.Instance );
            }

            // Alternative 11
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp(">>=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedRShift.Instance );
            }

            // Alternative 12
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("**=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedPow.Instance );
            }

            // Alternative 13
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("//=") == null) return null;

                // Action code from grammar
                return PyParserHelpers . AugOperator ( GeneratedFloorDiv.Instance );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("return") == null) return null;
                if ((a = (GeneratedExpr)ParseOptional(() => Parse_StarExpressions())) == null) return null;

                // Action code from grammar
                return PyAst . Return (( GeneratedExpr ?) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedPtr? b = null;

                if (ExpectKeyword("raise") == null) return null;
                if ((a = Parse_Expression()) == null) return null;
                if ((b = ParseOptional(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyAst . Raise (( GeneratedExpr ) a ,( GeneratedExpr ?) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("raise") == null) return null;

                // Action code from grammar
                return PyAst . Raise ( null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (ExpectKeyword("global") == null) return null;
                if ((a = (GeneratedExprSeq)ParseGatherPlus(() => ExpectOp(","), () => Expect(PyToken.Type.NAME, "NAME"))) == null) return null;

                // Action code from grammar
                return PyAst . Global ( Check < GeneratedIdentifierSeq >( PyParserHelpers . MapNamesToIds (( GeneratedExprSeq ) a )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (ExpectKeyword("nonlocal") == null) return null;
                if ((a = (GeneratedExprSeq)ParseGatherPlus(() => ExpectOp(","), () => Expect(PyToken.Type.NAME, "NAME"))) == null) return null;

                // Action code from grammar
                return PyAst . Nonlocal ( Check < GeneratedIdentifierSeq >( PyParserHelpers . MapNamesToIds (( GeneratedExprSeq ) a )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (ExpectKeyword("del") == null) return null;
                if ((a = Parse_DelTargets()) == null) return null;
                if (PositiveLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return PyAst . Delete (( GeneratedExprSeq ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidDelStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? y = null;

                if ((y = Parse_YieldExpr()) == null) return null;

                // Action code from grammar
                return PyAst . Expr (( GeneratedExpr ) y , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedPtr? b = null;

                if (ExpectKeyword("assert") == null) return null;
                if ((a = Parse_Expression()) == null) return null;
                if ((b = ParseOptional(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyAst . Assert (( GeneratedExpr ) a ,( GeneratedExpr ?) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidImport() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_ImportName() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_ImportFrom() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedAliasSeq? a = null;

                if (ExpectKeyword("import") == null) return null;
                if ((a = Parse_DottedAsNames()) == null) return null;

                // Action code from grammar
                return PyAst . Import (( GeneratedAliasSeq ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedExpr? b = null;
                GeneratedAliasSeq? c = null;

                if (ExpectKeyword("from") == null) return null;
                if ((a = ParseZeroOrMore(() => ParseGroup())) == null) return null;
                if ((b = Parse_DottedName()) == null) return null;
                if (ExpectKeyword("import") == null) return null;
                if ((c = Parse_ImportFromTargets()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CheckedFutureImport ( b . GetIdentifier (),( GeneratedAliasSeq ) c , PyParserHelpers . SeqCountDots ( a . ToRawList ()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedAliasSeq? b = null;

                if (ExpectKeyword("from") == null) return null;
                if ((a = ParseOneOrMore(() => ParseGroup())) == null) return null;
                if (ExpectKeyword("import") == null) return null;
                if ((b = Parse_ImportFromTargets()) == null) return null;

                // Action code from grammar
                return PyAst . ImportFrom ( null ,( GeneratedAliasSeq ) b , PyParserHelpers . SeqCountDots ( a . ToRawList ()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedAliasSeq? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = Parse_ImportFromAsNames()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_ImportFromAsNames() == null) return null;
                if (NegativeLookahead(() => ExpectOp(",")) == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("*") == null) return null;

                // Action code from grammar
                return PyParserHelpers . SingletonSequence ( Check < GeneratedAlias >( PyParserHelpers . AliasForStar ( _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ))). Cast < GeneratedAliasSeq >();
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidImportFromTargets() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedAliasSeq? a = null;

                if ((a = (GeneratedAliasSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_ImportFromAsName())) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedPtr? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((b = ParseOptional(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyAst . alias ( a . GetNameValue (), b . GetNameValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedAliasSeq? a = null;

                if ((a = (GeneratedAliasSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_DottedAsName())) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedPtr? b = null;

                if ((a = Parse_DottedName()) == null) return null;
                if ((b = ParseOptional(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyAst . alias ( a . GetIdentifier (), b . GetNameValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: dotted_name
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_DottedName()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = Parse_DottedName()) == null) return null;
                if (ExpectOp(".") == null) return null;
                if ((b = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . JoinNamesWithDot ( a , NameToken ( b ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Expect(PyToken.Type.NAME, "NAME") == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: block
        /// Alternatives: 3
        /// Return Type: GeneratedStmtSeq
        /// </summary>
        private GeneratedStmtSeq? Parse_Block()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (Expect(PyToken.Type.INDENT, "INDENT") == null) return null;
                if ((a = Parse_Statements()) == null) return null;
                if (Expect(PyToken.Type.DEDENT, "DEDENT") == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_SimpleStmts() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidBlock() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = (GeneratedExprSeq)ParseOneOrMore(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;
                GeneratedStmt? b = null;

                if ((a = Parse_Decorators()) == null) return null;
                if ((b = Parse_ClassDefRaw()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . ClassDefDecorators ( a , b );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_ClassDefRaw() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidClassDefRaw() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTypeParamSeq? t = null;
                GeneratedPtr? b = null;
                GeneratedStmtSeq? c = null;

                if (ExpectKeyword("class") == null) return null;
                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((t = (GeneratedTypeParamSeq)ParseOptional(() => Parse_TypeParams())) == null) return null;
                if ((b = ParseOptional(() => ParseGroup())) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((c = Parse_Block()) == null) return null;

                // Action code from grammar
                return PyAst . ClassDef ( a . GetNameValue (),( b != null )?(( GeneratedCall ) b ). Args : null !,( b != null )?(( GeneratedCall ) b ). Keywords : null !, c , null !, t , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? d = null;
                GeneratedStmt? f = null;

                if ((d = Parse_Decorators()) == null) return null;
                if ((f = Parse_FunctionDefRaw()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . FunctionDefDecorators ( d , f );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_FunctionDefRaw() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidDefRaw() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

                if (ExpectKeyword("def") == null) return null;
                if ((n = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((t = (GeneratedTypeParamSeq)ParseOptional(() => Parse_TypeParams())) == null) return null;
                if (PositiveLookahead(() => PositiveLookahead(() => ExpectOp("("))) == null) return null;
                if ((params_ = (GeneratedArguments)ParseOptional(() => Parse_Params())) == null) return null;
                if (ExpectOp(")") == null) return null;
                if ((a = ParseOptional(() => ParseGroup())) == null) return null;
                if (PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Parse_FuncTypeComment())) == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return PyAst . FunctionDef ( n . GetNameValue (),( params_ != null )? params_ :( GeneratedArguments ) Check < GeneratedArguments >( PyParserHelpers . EmptyArguments ()), b , null ,( GeneratedExpr ?) a , tc . GetCommentValue (), t , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

                if (Expect(PyToken.Type.ASYNC, "ASYNC") == null) return null;
                if (ExpectKeyword("def") == null) return null;
                if ((n = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((t = (GeneratedTypeParamSeq)ParseOptional(() => Parse_TypeParams())) == null) return null;
                if (PositiveLookahead(() => PositiveLookahead(() => ExpectOp("("))) == null) return null;
                if ((params_ = (GeneratedArguments)ParseOptional(() => Parse_Params())) == null) return null;
                if (ExpectOp(")") == null) return null;
                if ((a = ParseOptional(() => ParseGroup())) == null) return null;
                if (PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Parse_FuncTypeComment())) == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return CheckVersion ( 5 , "Async functions are" , PyAst . AsyncFunctionDef ( n . GetNameValue (),( params_ != null )? params_ :( GeneratedArguments ) Check < GeneratedArguments >( PyParserHelpers . EmptyArguments ()), b , null ,( GeneratedExpr ?) a , tc . GetCommentValue (), t , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if ((a = Parse_InvalidParameters()) == null) return null;

                // Action code from grammar
                return ( GeneratedArguments ) a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArguments? a = null;

                if ((a = Parse_Parameters()) == null) return null;

                // Action code from grammar
                return ( GeneratedArguments ) a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;
                GeneratedArgSeq? b = null;
                GeneratedSeq? c = null;
                GeneratedStarEtc? d = null;

                if ((a = Parse_SlashNoDefault()) == null) return null;
                if ((b = (GeneratedArgSeq)ParseZeroOrMore(() => Parse_ParamNoDefault())) == null) return null;
                if ((c = ParseZeroOrMore(() => Parse_ParamWithDefault())) == null) return null;
                if ((d = (GeneratedStarEtc)ParseOptional(() => Parse_StarEtc())) == null) return null;

                // Action code from grammar
                return CheckVersion ( 8 , "Positional-only parameters are" , PyParserHelpers . MakeArguments (( GeneratedArgSeq ) a , null , b ,( GeneratedNameDefaultPairSeq ?) c ,( GeneratedStarEtc ?) d ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSlashWithDefault? a = null;
                GeneratedSeq? b = null;
                GeneratedStarEtc? c = null;

                if ((a = Parse_SlashWithDefault()) == null) return null;
                if ((b = ParseZeroOrMore(() => Parse_ParamWithDefault())) == null) return null;
                if ((c = (GeneratedStarEtc)ParseOptional(() => Parse_StarEtc())) == null) return null;

                // Action code from grammar
                return CheckVersion ( 8 , "Positional-only parameters are" , PyParserHelpers . MakeArguments ( null ,( GeneratedSlashWithDefault ) a , null ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedStarEtc ?) c ));
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;
                GeneratedSeq? b = null;
                GeneratedStarEtc? c = null;

                if ((a = (GeneratedArgSeq)ParseOneOrMore(() => Parse_ParamNoDefault())) == null) return null;
                if ((b = ParseZeroOrMore(() => Parse_ParamWithDefault())) == null) return null;
                if ((c = (GeneratedStarEtc)ParseOptional(() => Parse_StarEtc())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . MakeArguments ( null , null , a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedStarEtc ?) c );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedStarEtc? b = null;

                if ((a = ParseOneOrMore(() => Parse_ParamWithDefault())) == null) return null;
                if ((b = (GeneratedStarEtc)ParseOptional(() => Parse_StarEtc())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . MakeArguments ( null , null , null ,( GeneratedNameDefaultPairSeq ?) a ,( GeneratedStarEtc ?) b );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStarEtc? a = null;

                if ((a = Parse_StarEtc()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . MakeArguments ( null , null , null , null ,( GeneratedStarEtc ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;

                if ((a = (GeneratedArgSeq)ParseOneOrMore(() => Parse_ParamNoDefault())) == null) return null;
                if (ExpectOp("/") == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;

                if ((a = (GeneratedArgSeq)ParseOneOrMore(() => Parse_ParamNoDefault())) == null) return null;
                if (ExpectOp("/") == null) return null;
                if (PositiveLookahead(() => ExpectOp(")")) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if ((a = ParseZeroOrMore(() => Parse_ParamNoDefault())) == null) return null;
                if ((b = ParseOneOrMore(() => Parse_ParamWithDefault())) == null) return null;
                if (ExpectOp("/") == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return PyParserHelpers . SlashWithDefault (( GeneratedArgSeq ?) a ,( GeneratedNameDefaultPairSeq ) b );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if ((a = ParseZeroOrMore(() => Parse_ParamNoDefault())) == null) return null;
                if ((b = ParseOneOrMore(() => Parse_ParamWithDefault())) == null) return null;
                if (ExpectOp("/") == null) return null;
                if (PositiveLookahead(() => ExpectOp(")")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SlashWithDefault (( GeneratedArgSeq ?) a ,( GeneratedNameDefaultPairSeq ) b );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidStarEtc() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Parse_ParamNoDefault()) == null) return null;
                if ((b = ParseZeroOrMore(() => Parse_ParamMaybeDefault())) == null) return null;
                if ((c = (GeneratedArg)ParseOptional(() => Parse_Kwds())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . StarEtc (( GeneratedArg ) a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Parse_ParamNoDefaultStarAnnotation()) == null) return null;
                if ((b = ParseZeroOrMore(() => Parse_ParamMaybeDefault())) == null) return null;
                if ((c = (GeneratedArg)ParseOptional(() => Parse_Kwds())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . StarEtc (( GeneratedArg ) a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (ExpectOp("*") == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((b = ParseOneOrMore(() => Parse_ParamMaybeDefault())) == null) return null;
                if ((c = (GeneratedArg)ParseOptional(() => Parse_Kwds())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . StarEtc ( null ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if ((a = Parse_Kwds()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . StarEtc ( null , null ,( GeneratedArg ) a );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidKwds() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (ExpectOp("**") == null) return null;
                if ((a = Parse_ParamNoDefault()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedTokenInfo? tc = null;

                if ((a = Parse_Param()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;

                // Action code from grammar
                return PyParserHelpers . AddTypeCommentToArg ( a , tc );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedTokenInfo? tc = null;

                if ((a = Parse_Param()) == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;
                if (PositiveLookahead(() => ExpectOp(")")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . AddTypeCommentToArg ( a , tc );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedTokenInfo? tc = null;

                if ((a = Parse_ParamStarAnnotation()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;

                // Action code from grammar
                return PyParserHelpers . AddTypeCommentToArg ( a , tc );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedTokenInfo? tc = null;

                if ((a = Parse_ParamStarAnnotation()) == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;
                if (PositiveLookahead(() => ExpectOp(")")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . AddTypeCommentToArg ( a , tc );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;
                GeneratedTokenInfo? tc = null;

                if ((a = Parse_Param()) == null) return null;
                if ((c = Parse_Default()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;

                // Action code from grammar
                return PyParserHelpers . NameDefaultPair ( a , c , tc ?. Value );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;
                GeneratedTokenInfo? tc = null;

                if ((a = Parse_Param()) == null) return null;
                if ((c = Parse_Default()) == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;
                if (PositiveLookahead(() => ExpectOp(")")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . NameDefaultPair ( a , c , tc ?. Value );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;
                GeneratedTokenInfo? tc = null;

                if ((a = Parse_Param()) == null) return null;
                if ((c = (GeneratedExpr)ParseOptional(() => Parse_Default())) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;

                // Action code from grammar
                return PyParserHelpers . NameDefaultPair ( a , c , tc ?. Value );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;
                GeneratedTokenInfo? tc = null;

                if ((a = Parse_Param()) == null) return null;
                if ((c = (GeneratedExpr)ParseOptional(() => Parse_Default())) == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;
                if (PositiveLookahead(() => ExpectOp(")")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . NameDefaultPair ( a , c , tc ?. Value );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((b = (GeneratedExpr)ParseOptional(() => Parse_Annotation())) == null) return null;

                // Action code from grammar
                return PyAst . arg ( a . GetNameValue (), b , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((b = Parse_StarAnnotation()) == null) return null;

                // Action code from grammar
                return PyAst . arg ( a . GetNameValue (), b , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp(":") == null) return null;
                if ((a = Parse_Expression()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp(":") == null) return null;
                if ((a = Parse_StarExpression()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("=") == null) return null;
                if ((a = Parse_Expression()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidDefault() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidIfStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmt? c = null;

                if (ExpectKeyword("if") == null) return null;
                if ((a = Parse_NamedExpression()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((c = Parse_ElifStmt()) == null) return null;

                // Action code from grammar
                return PyAst . If (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b , Check < GeneratedStmtSeq >( PyParserHelpers . SingletonSequence (( GeneratedStmt ) c ). Cast < GeneratedStmtSeq >()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? c = null;

                if (ExpectKeyword("if") == null) return null;
                if ((a = Parse_NamedExpression()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((c = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null) return null;

                // Action code from grammar
                return PyAst . If (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidElifStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmt? c = null;

                if (ExpectKeyword("elif") == null) return null;
                if ((a = Parse_NamedExpression()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((c = Parse_ElifStmt()) == null) return null;

                // Action code from grammar
                return PyAst . If (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b , Check < GeneratedStmtSeq >( PyParserHelpers . SingletonSequence (( GeneratedStmt ) c ). Cast < GeneratedStmtSeq >()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? c = null;

                if (ExpectKeyword("elif") == null) return null;
                if ((a = Parse_NamedExpression()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((c = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null) return null;

                // Action code from grammar
                return PyAst . If (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidElseStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;

                if (ExpectKeyword("else") == null) return null;
                if (PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return b;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidWhileStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? c = null;

                if (ExpectKeyword("while") == null) return null;
                if ((a = Parse_NamedExpression()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((c = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null) return null;

                // Action code from grammar
                return PyAst . While (( GeneratedExpr ) a ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidForStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

                if (ExpectKeyword("for") == null) return null;
                if ((t = Parse_StarTargets()) == null) return null;
                if (ExpectKeyword("in") == null) return null;
                if ((ex = Parse_StarExpressions()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((el = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null) return null;

                // Action code from grammar
                return PyAst . For (( GeneratedExpr ) t ,( GeneratedExpr ) ex ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) el , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

                if (Expect(PyToken.Type.ASYNC, "ASYNC") == null) return null;
                if (ExpectKeyword("for") == null) return null;
                if ((t = Parse_StarTargets()) == null) return null;
                if (ExpectKeyword("in") == null) return null;
                if ((ex = Parse_StarExpressions()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((el = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null) return null;

                // Action code from grammar
                return CheckVersion ( 5 , "Async for loops are" , PyAst . AsyncFor (( GeneratedExpr ) t ,( GeneratedExpr ) ex ,( GeneratedStmtSeq ) b ,( GeneratedStmtSeq ?) el , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidForTarget() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidWithStmtIndent() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedWithitemSeq? a = null;
                GeneratedStmtSeq? b = null;

                if (ExpectKeyword("with") == null) return null;
                if (ExpectOp("(") == null) return null;
                if ((a = (GeneratedWithitemSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_WithItem())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp(")") == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return PyAst . With (( GeneratedWithitemSeq ) a ,( GeneratedStmtSeq ) b , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedWithitemSeq? a = null;
                GeneratedTokenInfo? tc = null;
                GeneratedStmtSeq? b = null;

                if (ExpectKeyword("with") == null) return null;
                if ((a = (GeneratedWithitemSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_WithItem())) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return PyAst . With (( GeneratedWithitemSeq ) a ,( GeneratedStmtSeq ) b , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedWithitemSeq? a = null;
                GeneratedStmtSeq? b = null;

                if (Expect(PyToken.Type.ASYNC, "ASYNC") == null) return null;
                if (ExpectKeyword("with") == null) return null;
                if (ExpectOp("(") == null) return null;
                if ((a = (GeneratedWithitemSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_WithItem())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp(")") == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return CheckVersion ( 5 , "Async with statements are" , PyAst . AsyncWith (( GeneratedWithitemSeq ) a ,( GeneratedStmtSeq ) b , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedWithitemSeq? a = null;
                GeneratedTokenInfo? tc = null;
                GeneratedStmtSeq? b = null;

                if (Expect(PyToken.Type.ASYNC, "ASYNC") == null) return null;
                if (ExpectKeyword("with") == null) return null;
                if ((a = (GeneratedWithitemSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_WithItem())) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((tc = (GeneratedTokenInfo)ParseOptional(() => Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT"))) == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return CheckVersion ( 5 , "Async with statements are" , PyAst . AsyncWith (( GeneratedWithitemSeq ) a ,( GeneratedStmtSeq ) b , tc . GetCommentValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidWithStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;
                GeneratedExpr? t = null;

                if ((e = Parse_Expression()) == null) return null;
                if (ExpectKeyword("as") == null) return null;
                if ((t = Parse_StarTarget()) == null) return null;
                if (PositiveLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return PyAst . withitem (( GeneratedExpr ) e ,( GeneratedExpr ) t );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidWithItem() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;

                if ((e = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyAst . withitem (( GeneratedExpr ) e , null );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidTryStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;
                GeneratedStmtSeq? f = null;

                if (ExpectKeyword("try") == null) return null;
                if (PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((f = Parse_FinallyBlock()) == null) return null;

                // Action code from grammar
                return PyAst . Try (( GeneratedStmtSeq ) b , null , null ,( GeneratedStmtSeq ) f , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;
                GeneratedExcepthandlerSeq? ex = null;
                GeneratedStmtSeq? el = null;
                GeneratedStmtSeq? f = null;

                if (ExpectKeyword("try") == null) return null;
                if (PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((ex = (GeneratedExcepthandlerSeq)ParseOneOrMore(() => Parse_ExceptBlock())) == null) return null;
                if ((el = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null) return null;
                if ((f = (GeneratedStmtSeq)ParseOptional(() => Parse_FinallyBlock())) == null) return null;

                // Action code from grammar
                return PyAst . Try (( GeneratedStmtSeq ) b ,( GeneratedExcepthandlerSeq ) ex ,( GeneratedStmtSeq ?) el ,( GeneratedStmtSeq ?) f , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;
                GeneratedExcepthandlerSeq? ex = null;
                GeneratedStmtSeq? el = null;
                GeneratedStmtSeq? f = null;

                if (ExpectKeyword("try") == null) return null;
                if (PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) == null) return null;
                if ((b = Parse_Block()) == null) return null;
                if ((ex = (GeneratedExcepthandlerSeq)ParseOneOrMore(() => Parse_ExceptStarBlock())) == null) return null;
                if ((el = (GeneratedStmtSeq)ParseOptional(() => Parse_ElseBlock())) == null) return null;
                if ((f = (GeneratedStmtSeq)ParseOptional(() => Parse_FinallyBlock())) == null) return null;

                // Action code from grammar
                return CheckVersion ( 11 , "Exception groups are" , PyAst . TryStar (( GeneratedStmtSeq ) b ,( GeneratedExcepthandlerSeq ) ex ,( GeneratedStmtSeq ?) el ,( GeneratedStmtSeq ?) f , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidExceptStmtIndent() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;
                GeneratedPtr? t = null;
                GeneratedStmtSeq? b = null;

                if (ExpectKeyword("except") == null) return null;
                if ((e = Parse_Expression()) == null) return null;
                if ((t = ParseOptional(() => ParseGroup())) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return PyAst . ExceptHandler (( GeneratedExpr ) e , t != null ?(( GeneratedName ) NameToken (( GeneratedTokenInfo ) t )). Id : null ,( GeneratedStmtSeq ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? b = null;

                if (ExpectKeyword("except") == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return PyAst . ExceptHandler ( null , null ,( GeneratedStmtSeq ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidExceptStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidExceptStarStmtIndent() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;
                GeneratedPtr? t = null;
                GeneratedStmtSeq? b = null;

                if (ExpectKeyword("except") == null) return null;
                if (ExpectOp("*") == null) return null;
                if ((e = Parse_Expression()) == null) return null;
                if ((t = ParseOptional(() => ParseGroup())) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Block()) == null) return null;

                // Action code from grammar
                return PyAst . ExceptHandler (( GeneratedExpr ) e ,( t != null )?(( GeneratedName ) NameToken (( GeneratedTokenInfo ) t )). Id : null ,( GeneratedStmtSeq ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidExceptStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidFinallyStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStmtSeq? a = null;

                if (ExpectKeyword("finally") == null) return null;
                if (PositiveLookahead(() => PositiveLookahead(() => ExpectOp(":"))) == null) return null;
                if ((a = Parse_Block()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? subject = null;
                GeneratedMatchCaseSeq? cases = null;

                if (ExpectSoftKeyword("match") == null) return null;
                if ((subject = Parse_SubjectExpr()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (Expect(PyToken.Type.INDENT, "INDENT") == null) return null;
                if ((cases = (GeneratedMatchCaseSeq)ParseOneOrMore(() => Parse_CaseBlock())) == null) return null;
                if (Expect(PyToken.Type.DEDENT, "DEDENT") == null) return null;

                // Action code from grammar
                return CheckVersion ( 10 , "Pattern matching is" , PyAst . Match (( GeneratedExpr ) subject ,( GeneratedMatchCaseSeq ) cases , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidMatchStmt() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;
                GeneratedExprSeq? values = null;

                if ((value = Parse_StarNamedExpression()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((values = (GeneratedExprSeq)ParseOptional(() => Parse_StarNamedExpressions())) == null) return null;

                // Action code from grammar
                return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) value ,( GeneratedExprSeq ?) values ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_NamedExpression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidCaseBlock() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? pattern = null;
                GeneratedExpr? guard = null;
                GeneratedStmtSeq? body = null;

                if (ExpectSoftKeyword("case") == null) return null;
                if ((pattern = Parse_Patterns()) == null) return null;
                if ((guard = (GeneratedExpr)ParseOptional(() => Parse_Guard())) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((body = Parse_Block()) == null) return null;

                // Action code from grammar
                return PyAst . match_case (( GeneratedPattern ) pattern ,( GeneratedExpr ?) guard ,( GeneratedStmtSeq ) body );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? guard = null;

                if (ExpectKeyword("if") == null) return null;
                if ((guard = Parse_NamedExpression()) == null) return null;

                // Action code from grammar
                return guard;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? patterns = null;

                if ((patterns = (GeneratedPatternSeq)Parse_OpenSequencePattern()) == null) return null;

                // Action code from grammar
                return PyAst . MatchSequence (( GeneratedPatternSeq ) patterns , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Pattern() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_AsPattern() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_OrPattern() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? pattern = null;
                GeneratedExpr? target = null;

                if ((pattern = Parse_OrPattern()) == null) return null;
                if (ExpectKeyword("as") == null) return null;
                if ((target = Parse_PatternCaptureTarget()) == null) return null;

                // Action code from grammar
                return PyAst . MatchAs (( GeneratedPattern ) pattern , target . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidAsPattern() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? patterns = null;

                if ((patterns = (GeneratedPatternSeq)ParseGatherPlus(() => ExpectOp("|"), () => Parse_ClosedPattern())) == null) return null;

                // Action code from grammar
                return patterns . Count == 1 ?( GeneratedPattern ) patterns [ 0 ]: PyAst . MatchOr (( GeneratedPatternSeq ) patterns , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: closed_pattern
        /// Alternatives: 8
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_ClosedPattern()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_LiteralPattern()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_CapturePattern()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_WildcardPattern()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_ValuePattern()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_GroupPattern()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_SequencePattern()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_MappingPattern()) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? a = null;

                if ((a = Parse_ClassPattern()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;

                if ((value = Parse_SignedNumber()) == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return PyAst . MatchValue (( GeneratedExpr ) value , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;

                if ((value = Parse_ComplexNumber()) == null) return null;

                // Action code from grammar
                return PyAst . MatchValue (( GeneratedExpr ) value , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;

                if ((value = Parse_Strings()) == null) return null;

                // Action code from grammar
                return PyAst . MatchValue (( GeneratedExpr ) value , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("None") == null) return null;

                // Action code from grammar
                return PyAst . MatchSingleton ( GeneratedPyConstant . None , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("True") == null) return null;

                // Action code from grammar
                return PyAst . MatchSingleton ( GeneratedPyConstant . True , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("False") == null) return null;

                // Action code from grammar
                return PyAst . MatchSingleton ( GeneratedPyConstant . False , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_SignedNumber() == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_ComplexNumber() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Strings() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("None") == null) return null;

                // Action code from grammar
                return PyAst . Constant ( GeneratedPyConstant . None , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("True") == null) return null;

                // Action code from grammar
                return PyAst . Constant ( GeneratedPyConstant . True , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("False") == null) return null;

                // Action code from grammar
                return PyAst . Constant ( GeneratedPyConstant . False , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? real = null;
                GeneratedExpr? imag = null;

                if ((real = Parse_SignedRealNumber()) == null) return null;
                if (ExpectOp("+") == null) return null;
                if ((imag = Parse_ImaginaryNumber()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) real , GeneratedAdd.Instance ,( GeneratedExpr ) imag , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? real = null;
                GeneratedExpr? imag = null;

                if ((real = Parse_SignedRealNumber()) == null) return null;
                if (ExpectOp("-") == null) return null;
                if ((imag = Parse_ImaginaryNumber()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) real , GeneratedSub.Instance ,( GeneratedExpr ) imag , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? number = null;

                if ((number = Expect(PyToken.Type.NUMBER, "NUMBER")) == null) return null;

                // Action code from grammar
                return NumberToken ( number );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? number = null;

                if (ExpectOp("-") == null) return null;
                if ((number = Expect(PyToken.Type.NUMBER, "NUMBER")) == null) return null;

                // Action code from grammar
                return PyAst . UnaryOp ( GeneratedUSub.Instance , NumberToken ( number ), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_RealNumber() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? real = null;

                if (ExpectOp("-") == null) return null;
                if ((real = Parse_RealNumber()) == null) return null;

                // Action code from grammar
                return PyAst . UnaryOp ( GeneratedUSub.Instance ,( GeneratedExpr ) real , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? real = null;

                if ((real = Expect(PyToken.Type.NUMBER, "NUMBER")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . EnsureReal ( NumberToken ( real ));
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? imag = null;

                if ((imag = Expect(PyToken.Type.NUMBER, "NUMBER")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . EnsureImaginary ( NumberToken ( imag ));
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? target = null;

                if ((target = Parse_PatternCaptureTarget()) == null) return null;

                // Action code from grammar
                return PyAst . MatchAs ( null , target . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? name = null;

                if (NegativeLookahead(() => ExpectSoftKeyword("_")) == null) return null;
                if ((name = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SetExprContext ( NameToken ( name ), GeneratedStore.Instance );
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectSoftKeyword("_") == null) return null;

                // Action code from grammar
                return PyAst . MatchAs ( null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? attr = null;

                if ((attr = Parse_Attr()) == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return PyAst . MatchValue (( GeneratedExpr ) attr , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: attr
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Attr()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? value = null;
                GeneratedTokenInfo? attr = null;

                if ((value = Parse_NameOrAttr()) == null) return null;
                if (ExpectOp(".") == null) return null;
                if ((attr = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return PyAst . Attribute (( GeneratedExpr ) value , attr . Value , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: name_or_attr
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_NameOrAttr()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Attr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Expect(PyToken.Type.NAME, "NAME") == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? pattern = null;

                if (ExpectOp("(") == null) return null;
                if ((pattern = Parse_Pattern()) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return pattern;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? patterns = null;

                if (ExpectOp("[") == null) return null;
                if ((patterns = (GeneratedSeq)ParseOptional(() => Parse_MaybeSequencePattern())) == null) return null;
                if (ExpectOp("]") == null) return null;

                // Action code from grammar
                return PyAst . MatchSequence (( GeneratedPatternSeq ?) patterns , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? patterns = null;

                if (ExpectOp("(") == null) return null;
                if ((patterns = (GeneratedSeq)ParseOptional(() => Parse_OpenSequencePattern())) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . MatchSequence (( GeneratedPatternSeq ?) patterns , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPattern? pattern = null;
                GeneratedSeq? patterns = null;

                if ((pattern = Parse_MaybeStarPattern()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((patterns = (GeneratedSeq)ParseOptional(() => Parse_MaybeSequencePattern())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SeqInsertInFront (( GeneratedPattern ) pattern ,( GeneratedSeq ?) patterns ). Cast < GeneratedPatternSeq >();
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? patterns = null;

                if ((patterns = ParseGatherPlus(() => ExpectOp(","), () => Parse_MaybeStarPattern())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return patterns;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_StarPattern() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Pattern() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_pattern
        /// Alternatives: 2
        /// Return Type: GeneratedPattern
        /// </summary>
        private GeneratedPattern? Parse_StarPattern()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? target = null;

                if (ExpectOp("*") == null) return null;
                if ((target = Parse_PatternCaptureTarget()) == null) return null;

                // Action code from grammar
                return PyAst . MatchStar ( target . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("*") == null) return null;
                if (Parse_WildcardPattern() == null) return null;

                // Action code from grammar
                return PyAst . MatchStar ( null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("{") == null) return null;
                if (ExpectOp("}") == null) return null;

                // Action code from grammar
                return PyAst . MatchMapping ( null , null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? rest = null;

                if (ExpectOp("{") == null) return null;
                if ((rest = Parse_DoubleStarPattern()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp("}") == null) return null;

                // Action code from grammar
                return PyAst . MatchMapping ( null , null , rest . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? items = null;
                GeneratedExpr? rest = null;

                if (ExpectOp("{") == null) return null;
                if ((items = Parse_ItemsPattern()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((rest = Parse_DoubleStarPattern()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp("}") == null) return null;

                // Action code from grammar
                return PyAst . MatchMapping ( Check < GeneratedExprSeq >( PyParserHelpers . GetPatternKeys ( items )), Check < GeneratedPatternSeq >( PyParserHelpers . GetPatterns ( items )), rest . GetIdentifier (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? items = null;

                if (ExpectOp("{") == null) return null;
                if ((items = Parse_ItemsPattern()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp("}") == null) return null;

                // Action code from grammar
                return PyAst . MatchMapping ( Check < GeneratedExprSeq >( PyParserHelpers . GetPatternKeys ( items )), Check < GeneratedPatternSeq >( PyParserHelpers . GetPatterns ( items )), null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (ParseGatherPlus(() => ExpectOp(","), () => Parse_KeyValuePattern()) == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? key = null;
                GeneratedPattern? pattern = null;

                if ((key = ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((pattern = Parse_Pattern()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . KeyPatternPair (( GeneratedExpr ) key ,( GeneratedPattern ) pattern );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? target = null;

                if (ExpectOp("**") == null) return null;
                if ((target = Parse_PatternCaptureTarget()) == null) return null;

                // Action code from grammar
                return target;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? cls = null;

                if ((cls = Parse_NameOrAttr()) == null) return null;
                if (ExpectOp("(") == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . MatchClass (( GeneratedExpr ) cls , null , null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? cls = null;
                GeneratedPatternSeq? patterns = null;

                if ((cls = Parse_NameOrAttr()) == null) return null;
                if (ExpectOp("(") == null) return null;
                if ((patterns = Parse_PositionalPatterns()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . MatchClass (( GeneratedExpr ) cls ,( GeneratedPatternSeq ) patterns , null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? cls = null;
                GeneratedSeq? keywords = null;

                if ((cls = Parse_NameOrAttr()) == null) return null;
                if (ExpectOp("(") == null) return null;
                if ((keywords = Parse_KeywordPatterns()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . MatchClass (( GeneratedExpr ) cls , null , Check < GeneratedIdentifierSeq >( PyParserHelpers . MapNamesToIds ( Check < GeneratedExprSeq >( PyParserHelpers . GetPatternKeys (( GeneratedSeq ) keywords )))), Check < GeneratedPatternSeq >( PyParserHelpers . GetPatterns (( GeneratedSeq ) keywords )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? cls = null;
                GeneratedPatternSeq? patterns = null;
                GeneratedSeq? keywords = null;

                if ((cls = Parse_NameOrAttr()) == null) return null;
                if (ExpectOp("(") == null) return null;
                if ((patterns = Parse_PositionalPatterns()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((keywords = Parse_KeywordPatterns()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . MatchClass (( GeneratedExpr ) cls ,( GeneratedPatternSeq ) patterns , Check < GeneratedIdentifierSeq >( PyParserHelpers . MapNamesToIds ( Check < GeneratedExprSeq >( PyParserHelpers . GetPatternKeys (( GeneratedSeq ) keywords )))), Check < GeneratedPatternSeq >( PyParserHelpers . GetPatterns (( GeneratedSeq ) keywords )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidClassPattern() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? args = null;

                if ((args = (GeneratedPatternSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_Pattern())) == null) return null;

                // Action code from grammar
                return args;
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

            Reset(_mark);
            {
                CaptureStart();


                if (ParseGatherPlus(() => ExpectOp(","), () => Parse_KeywordPattern()) == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? arg = null;
                GeneratedPattern? value = null;

                if ((arg = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (ExpectOp("=") == null) return null;
                if ((value = Parse_Pattern()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . KeyPatternPair ( NameToken ( arg ),( GeneratedPattern ) value );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? n = null;
                GeneratedTypeParamSeq? t = null;
                GeneratedExpr? b = null;

                if (ExpectSoftKeyword("type") == null) return null;
                if ((n = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((t = (GeneratedTypeParamSeq)ParseOptional(() => Parse_TypeParams())) == null) return null;
                if (ExpectOp("=") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                return CheckVersion ( 12 , "Type statement is" , PyAst . TypeAlias ( Check < GeneratedExpr >( PyParserHelpers . SetExprContext ( NameToken ( n ), GeneratedStore.Instance )),( GeneratedTypeParamSeq ?) t ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTypeParamSeq? t = null;

                if (ExpectOp("[") == null) return null;
                if ((t = Parse_TypeParamSeq()) == null) return null;
                if (ExpectOp("]") == null) return null;

                // Action code from grammar
                return CheckVersion ( 12 , "Type parameter lists are" , t );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTypeParamSeq? a = null;

                if ((a = (GeneratedTypeParamSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_TypeParam())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return a;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: type_param
        /// Alternatives: 5
        /// Return Type: GeneratedTypeParam
        /// </summary>
        private GeneratedTypeParam? Parse_TypeParam()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((b = (GeneratedExpr)ParseOptional(() => Parse_TypeParamBound())) == null) return null;

                // Action code from grammar
                return PyAst . TypeVar ( a . GetNameValue (),( GeneratedExpr ?) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? colon = null;
                GeneratedExpr? e = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((colon = ExpectOp(":")) == null) return null;
                if ((e = Parse_Expression()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorStartingFrom ( colon ,( GeneratedExpr ) e is GeneratedTuple ? "cannot use constraints with TypeVarTuple" : "cannot use bound with TypeVarTuple" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return PyAst . TypeVarTuple ( a . GetNameValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? colon = null;
                GeneratedExpr? e = null;

                if (ExpectOp("**") == null) return null;
                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((colon = ExpectOp(":")) == null) return null;
                if ((e = Parse_Expression()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorStartingFrom ( colon ,( GeneratedExpr ) e is GeneratedTuple ? "cannot use constraints with ParamSpec" : "cannot use bound with ParamSpec" );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("**") == null) return null;
                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return PyAst . ParamSpec ( a . GetNameValue (), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? e = null;

                if (ExpectOp(":") == null) return null;
                if ((e = Parse_Expression()) == null) return null;

                // Action code from grammar
                return e;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if ((a = Parse_Expression()) == null) return null;
                if ((b = ParseOneOrMore(() => ParseGroup())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) a ,( GeneratedSeq ) b ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_Expression()) == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SingletonSequence (( GeneratedExpr ) a ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Expression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: expression
        /// Alternatives: 5
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Expression()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidExpression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidLegacyExpression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedExpr? c = null;

                if ((a = Parse_Disjunction()) == null) return null;
                if (ExpectKeyword("if") == null) return null;
                if ((b = Parse_Disjunction()) == null) return null;
                if (ExpectKeyword("else") == null) return null;
                if ((c = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyAst . IfExp (( GeneratedExpr ) b ,( GeneratedExpr ) a ,( GeneratedExpr ) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Disjunction() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Lambdef() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("yield") == null) return null;
                if (ExpectKeyword("from") == null) return null;
                if ((a = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyAst . YieldFrom (( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("yield") == null) return null;
                if ((a = (GeneratedExpr)ParseOptional(() => Parse_StarExpressions())) == null) return null;

                // Action code from grammar
                return PyAst . Yield (( GeneratedExpr ?) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if ((a = Parse_StarExpression()) == null) return null;
                if ((b = ParseOneOrMore(() => ParseGroup())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) a ,( GeneratedSeq ) b ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_StarExpression()) == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return PyAst . Tuple ( Check < GeneratedExprSeq >( PyParserHelpers . SingletonSequence (( GeneratedExpr ) a ). Cast < GeneratedExprSeq >()), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_StarExpression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_expression
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_StarExpression()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyAst . Starred (( GeneratedExpr ) a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Expression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = (GeneratedExprSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_StarNamedExpression())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyAst . Starred (( GeneratedExpr ) a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_NamedExpression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (ExpectOp(":=") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                return CheckVersion ( 8 , "Assignment expressions are" , PyAst . NamedExpr ( Check < GeneratedExpr >( PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedStore.Instance )),( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_AssignmentExpression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidNamedExpression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Expression() == null) return null;
                if (NegativeLookahead(() => ExpectOp(":=")) == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: disjunction
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Disjunction()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if ((a = Parse_Conjunction()) == null) return null;
                if ((b = ParseOneOrMore(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyAst . BoolOp ( GeneratedOr.Instance , Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) a ,( GeneratedSeq ) b ). Cast < GeneratedExprSeq >()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Conjunction() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: conjunction
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Conjunction()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if ((a = Parse_Inversion()) == null) return null;
                if ((b = ParseOneOrMore(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyAst . BoolOp ( GeneratedAnd.Instance , Check < GeneratedExprSeq >( PyParserHelpers . SeqInsertInFront (( GeneratedExpr ) a ,( GeneratedSeq ) b ). Cast < GeneratedExprSeq >()), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Inversion() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: inversion
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Inversion()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("not") == null) return null;
                if ((a = Parse_Inversion()) == null) return null;

                // Action code from grammar
                return PyAst . UnaryOp ( GeneratedNot.Instance ,( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Comparison() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if ((a = Parse_BitwiseOr()) == null) return null;
                if ((b = ParseOneOrMore(() => Parse_CompareOpBitwiseOrPair())) == null) return null;

                // Action code from grammar
                return PyAst . Compare (( GeneratedExpr ) a , Check < GeneratedCmpopSeq >( PyParserHelpers . GetCmpops (( GeneratedSeq ) b ). Cast < GeneratedCmpopSeq >()), Check < GeneratedExprSeq >( PyParserHelpers . GetExprs (( GeneratedSeq ) b )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_BitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_EqBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_NoteqBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_LteBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_LtBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_GteBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_GtBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_NotinBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_IsnotBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_IsBitwiseOr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("==") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedEq.Instance ,( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ParseGroup() == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedNotEq.Instance ,( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("<=") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedLtE.Instance ,( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("<") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedLt.Instance ,( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp(">=") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedGtE.Instance ,( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp(">") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedGt.Instance ,( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("not") == null) return null;
                if (ExpectKeyword("in") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedNotIn.Instance ,( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("in") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedIn.Instance ,( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("is") == null) return null;
                if (ExpectKeyword("not") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedIsNot.Instance ,( GeneratedExpr ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("is") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CmpopExprPair ( GeneratedIs.Instance ,( GeneratedExpr ) a );
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: bitwise_or
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_BitwiseOr()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_BitwiseOr()) == null) return null;
                if (ExpectOp("|") == null) return null;
                if ((b = Parse_BitwiseXor()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedBitOr.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_BitwiseXor() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: bitwise_xor
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_BitwiseXor()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_BitwiseXor()) == null) return null;
                if (ExpectOp("^") == null) return null;
                if ((b = Parse_BitwiseAnd()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedBitXor.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_BitwiseAnd() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: bitwise_and
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_BitwiseAnd()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_BitwiseAnd()) == null) return null;
                if (ExpectOp("&") == null) return null;
                if ((b = Parse_ShiftExpr()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedBitAnd.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_ShiftExpr() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: shift_expr
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_ShiftExpr()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_ShiftExpr()) == null) return null;
                if (ExpectOp("<<") == null) return null;
                if ((b = Parse_Sum()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedLShift.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_ShiftExpr()) == null) return null;
                if (ExpectOp(">>") == null) return null;
                if ((b = Parse_Sum()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedRShift.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Sum() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: sum
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Sum()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Sum()) == null) return null;
                if (ExpectOp("+") == null) return null;
                if ((b = Parse_Term()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedAdd.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Sum()) == null) return null;
                if (ExpectOp("-") == null) return null;
                if ((b = Parse_Term()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedSub.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Term() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: term
        /// Alternatives: 6
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Term()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Term()) == null) return null;
                if (ExpectOp("*") == null) return null;
                if ((b = Parse_Factor()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedMult.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Term()) == null) return null;
                if (ExpectOp("/") == null) return null;
                if ((b = Parse_Factor()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedDiv.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Term()) == null) return null;
                if (ExpectOp("//") == null) return null;
                if ((b = Parse_Factor()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedFloorDiv.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Term()) == null) return null;
                if (ExpectOp("%") == null) return null;
                if ((b = Parse_Factor()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedMod_.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Term()) == null) return null;
                if (ExpectOp("@") == null) return null;
                if ((b = Parse_Factor()) == null) return null;

                // Action code from grammar
                return CheckVersion ( 5 , "The '@' operator is" , PyAst . BinOp (( GeneratedExpr ) a , GeneratedMatMult.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Factor() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: factor
        /// Alternatives: 4
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Factor()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("+") == null) return null;
                if ((a = Parse_Factor()) == null) return null;

                // Action code from grammar
                return PyAst . UnaryOp ( GeneratedUAdd.Instance ,( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("-") == null) return null;
                if ((a = Parse_Factor()) == null) return null;

                // Action code from grammar
                return PyAst . UnaryOp ( GeneratedUSub.Instance ,( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("~") == null) return null;
                if ((a = Parse_Factor()) == null) return null;

                // Action code from grammar
                return PyAst . UnaryOp ( GeneratedInvert.Instance ,( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Power() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_AwaitPrimary()) == null) return null;
                if (ExpectOp("**") == null) return null;
                if ((b = Parse_Factor()) == null) return null;

                // Action code from grammar
                return PyAst . BinOp (( GeneratedExpr ) a , GeneratedPow.Instance ,( GeneratedExpr ) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_AwaitPrimary() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: await_primary
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_AwaitPrimary()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (Expect(PyToken.Type.AWAIT, "AWAIT") == null) return null;
                if ((a = Parse_Primary()) == null) return null;

                // Action code from grammar
                return CheckVersion ( 5 , "Await expressions are" , PyAst . Await (( GeneratedExpr ) a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Primary() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: primary
        /// Alternatives: 5
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Primary()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = Parse_Primary()) == null) return null;
                if (ExpectOp(".") == null) return null;
                if ((b = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return PyAst . Attribute (( GeneratedExpr ) a , b . GetNameValue (), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Primary()) == null) return null;
                if ((b = Parse_Genexp()) == null) return null;

                // Action code from grammar
                return PyAst . Call (( GeneratedExpr ) a , Check < GeneratedExprSeq >( PyParserHelpers . SingletonSequence (( GeneratedExpr ) b ). Cast < GeneratedExprSeq >()), null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Primary()) == null) return null;
                if (ExpectOp("(") == null) return null;
                if ((b = (GeneratedExpr)ParseOptional(() => Parse_Arguments())) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . Call (( GeneratedExpr ) a ,( b != null )?(( GeneratedCall ) b ). Args : null !,( b != null )?(( GeneratedCall ) b ). Keywords : null !, _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Primary()) == null) return null;
                if (ExpectOp("[") == null) return null;
                if ((b = Parse_Slices()) == null) return null;
                if (ExpectOp("]") == null) return null;

                // Action code from grammar
                return PyAst . Subscript (( GeneratedExpr ) a ,( GeneratedExpr ) b , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Atom() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_Slice()) == null) return null;
                if (NegativeLookahead(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = (GeneratedExprSeq)ParseGatherPlus(() => ExpectOp(","), () => ParseGroup())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return PyAst . Tuple ( a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedPtr? c = null;

                if ((a = (GeneratedExpr)ParseOptional(() => Parse_Expression())) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = (GeneratedExpr)ParseOptional(() => Parse_Expression())) == null) return null;
                if ((c = ParseOptional(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyAst . Slice (( GeneratedExpr ?) a ,( GeneratedExpr ?) b ,( GeneratedExpr ?) c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_NamedExpression()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? name = null;

                if ((name = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return NameToken ( name );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("True") == null) return null;

                // Action code from grammar
                return PyAst . Constant ( GeneratedPyConstant . True , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("False") == null) return null;

                // Action code from grammar
                return PyAst . Constant ( GeneratedPyConstant . False , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("None") == null) return null;

                // Action code from grammar
                return PyAst . Constant ( GeneratedPyConstant . None , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ParseGroup()) == null) return null;
                if (Parse_Strings() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? number = null;

                if ((number = Expect(PyToken.Type.NUMBER, "NUMBER")) == null) return null;

                // Action code from grammar
                return NumberToken ( number );
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectOp("(")) == null) return null;
                if (ParseGroup() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectOp("[")) == null) return null;
                if (ParseGroup() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();


                if (PositiveLookahead(() => ExpectOp("{")) == null) return null;
                if (ParseGroup() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("...") == null) return null;

                // Action code from grammar
                return PyAst . Constant ( GeneratedPyConstant . Ellipsis , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = ParseGroup()) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return ( GeneratedExpr ) a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidGroup() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArguments? a = null;
                GeneratedExpr? b = null;

                if (ExpectKeyword("lambda") == null) return null;
                if ((a = (GeneratedArguments)ParseOptional(() => Parse_LambdaParams())) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyAst . Lambda (( a != null )? a :( GeneratedArguments ) Check < GeneratedArguments >( PyParserHelpers . EmptyArguments ()), b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if ((a = Parse_InvalidLambdaParameters()) == null) return null;

                // Action code from grammar
                return ( GeneratedArguments ) a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArguments? a = null;

                if ((a = Parse_LambdaParameters()) == null) return null;

                // Action code from grammar
                return ( GeneratedArguments ) a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;
                GeneratedArgSeq? b = null;
                GeneratedSeq? c = null;
                GeneratedStarEtc? d = null;

                if ((a = Parse_LambdaSlashNoDefault()) == null) return null;
                if ((b = (GeneratedArgSeq)ParseZeroOrMore(() => Parse_LambdaParamNoDefault())) == null) return null;
                if ((c = ParseZeroOrMore(() => Parse_LambdaParamWithDefault())) == null) return null;
                if ((d = (GeneratedStarEtc)ParseOptional(() => Parse_LambdaStarEtc())) == null) return null;

                // Action code from grammar
                return CheckVersion ( 8 , "Positional-only parameters are" , PyParserHelpers . MakeArguments (( GeneratedArgSeq ) a , null , b ,( GeneratedNameDefaultPairSeq ?) c ,( GeneratedStarEtc ?) d ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSlashWithDefault? a = null;
                GeneratedSeq? b = null;
                GeneratedStarEtc? c = null;

                if ((a = Parse_LambdaSlashWithDefault()) == null) return null;
                if ((b = ParseZeroOrMore(() => Parse_LambdaParamWithDefault())) == null) return null;
                if ((c = (GeneratedStarEtc)ParseOptional(() => Parse_LambdaStarEtc())) == null) return null;

                // Action code from grammar
                return CheckVersion ( 8 , "Positional-only parameters are" , PyParserHelpers . MakeArguments ( null ,( GeneratedSlashWithDefault ) a , null ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedStarEtc ?) c ));
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;
                GeneratedSeq? b = null;
                GeneratedStarEtc? c = null;

                if ((a = (GeneratedArgSeq)ParseOneOrMore(() => Parse_LambdaParamNoDefault())) == null) return null;
                if ((b = ParseZeroOrMore(() => Parse_LambdaParamWithDefault())) == null) return null;
                if ((c = (GeneratedStarEtc)ParseOptional(() => Parse_LambdaStarEtc())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . MakeArguments ( null , null , a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedStarEtc ?) c );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedStarEtc? b = null;

                if ((a = ParseOneOrMore(() => Parse_LambdaParamWithDefault())) == null) return null;
                if ((b = (GeneratedStarEtc)ParseOptional(() => Parse_LambdaStarEtc())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . MakeArguments ( null , null , null ,( GeneratedNameDefaultPairSeq ?) a ,( GeneratedStarEtc ?) b );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedStarEtc? a = null;

                if ((a = Parse_LambdaStarEtc()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . MakeArguments ( null , null , null , null ,( GeneratedStarEtc ) a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;

                if ((a = (GeneratedArgSeq)ParseOneOrMore(() => Parse_LambdaParamNoDefault())) == null) return null;
                if (ExpectOp("/") == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArgSeq? a = null;

                if ((a = (GeneratedArgSeq)ParseOneOrMore(() => Parse_LambdaParamNoDefault())) == null) return null;
                if (ExpectOp("/") == null) return null;
                if (PositiveLookahead(() => ExpectOp(":")) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if ((a = ParseZeroOrMore(() => Parse_LambdaParamNoDefault())) == null) return null;
                if ((b = ParseOneOrMore(() => Parse_LambdaParamWithDefault())) == null) return null;
                if (ExpectOp("/") == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return PyParserHelpers . SlashWithDefault (( GeneratedArgSeq ?) a ,( GeneratedNameDefaultPairSeq ) b );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if ((a = ParseZeroOrMore(() => Parse_LambdaParamNoDefault())) == null) return null;
                if ((b = ParseOneOrMore(() => Parse_LambdaParamWithDefault())) == null) return null;
                if (ExpectOp("/") == null) return null;
                if (PositiveLookahead(() => ExpectOp(":")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SlashWithDefault (( GeneratedArgSeq ?) a ,( GeneratedNameDefaultPairSeq ) b );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidLambdaStarEtc() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Parse_LambdaParamNoDefault()) == null) return null;
                if ((b = ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault())) == null) return null;
                if ((c = (GeneratedArg)ParseOptional(() => Parse_LambdaKwds())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . StarEtc (( GeneratedArg ) a ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? b = null;
                GeneratedArg? c = null;

                if (ExpectOp("*") == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((b = ParseOneOrMore(() => Parse_LambdaParamMaybeDefault())) == null) return null;
                if ((c = (GeneratedArg)ParseOptional(() => Parse_LambdaKwds())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . StarEtc ( null ,( GeneratedNameDefaultPairSeq ?) b ,( GeneratedArg ?) c );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if ((a = Parse_LambdaKwds()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . StarEtc ( null , null ,( GeneratedArg ) a );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidLambdaKwds() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (ExpectOp("**") == null) return null;
                if ((a = Parse_LambdaParamNoDefault()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if ((a = Parse_LambdaParam()) == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if ((a = Parse_LambdaParam()) == null) return null;
                if (PositiveLookahead(() => ExpectOp(":")) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;

                if ((a = Parse_LambdaParam()) == null) return null;
                if ((c = Parse_Default()) == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return PyParserHelpers . NameDefaultPair (( GeneratedArg ) a ,( GeneratedExpr ) c , null );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;

                if ((a = Parse_LambdaParam()) == null) return null;
                if ((c = Parse_Default()) == null) return null;
                if (PositiveLookahead(() => ExpectOp(":")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . NameDefaultPair (( GeneratedArg ) a ,( GeneratedExpr ) c , null );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;

                if ((a = Parse_LambdaParam()) == null) return null;
                if ((c = (GeneratedExpr)ParseOptional(() => Parse_Default())) == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return PyParserHelpers . NameDefaultPair (( GeneratedArg ) a ,( GeneratedExpr ?) c , null );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;
                GeneratedExpr? c = null;

                if ((a = Parse_LambdaParam()) == null) return null;
                if ((c = (GeneratedExpr)ParseOptional(() => Parse_Default())) == null) return null;
                if (PositiveLookahead(() => ExpectOp(":")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . NameDefaultPair (( GeneratedArg ) a ,( GeneratedExpr ?) c , null );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return PyAst . arg ( a . GetNameValue (), null , null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_FstringReplacementField() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? t = null;

                if ((t = Expect(PyToken.Type.FSTRING_MIDDLE, "FSTRING_MIDDLE")) == null) return null;

                // Action code from grammar
                return PyPegen . ConstantFromToken ( t );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;
                GeneratedTokenInfo? debug_expr = null;
                GeneratedResultTokenWithMetadata? conversion = null;
                GeneratedResultTokenWithMetadata? format = null;
                GeneratedTokenInfo? rbrace = null;

                if (ExpectOp("{") == null) return null;
                if ((a = ParseGroup()) == null) return null;
                if ((debug_expr = (GeneratedTokenInfo)ParseOptional(() => ExpectOp("="))) == null) return null;
                if ((conversion = (GeneratedResultTokenWithMetadata)ParseOptional(() => Parse_FstringConversion())) == null) return null;
                if ((format = (GeneratedResultTokenWithMetadata)ParseOptional(() => Parse_FstringFullFormatSpec())) == null) return null;
                if ((rbrace = ExpectOp("}")) == null) return null;

                // Action code from grammar
                return GeneratedParserBridge . _PyPegen_formatted_value (( GeneratedExpr ) a , debug_expr , conversion , format , rbrace , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidReplacementField() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? conv_token = null;
                GeneratedTokenInfo? conv = null;

                if ((conv_token = ExpectSoftKeyword("!")) == null) return null;
                if ((conv = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return GeneratedParserBridge . _PyPegen_check_fstring_conversion ( conv_token , GeneratedParserBridge . _PyPegen_name_token ( conv ));
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? colon = null;
                GeneratedSeq? spec = null;

                if ((colon = ExpectOp(":")) == null) return null;
                if ((spec = ParseZeroOrMore(() => Parse_FstringFormatSpec())) == null) return null;

                // Action code from grammar
                return GeneratedParserBridge . _PyPegen_setup_full_format_spec ( colon , spec . Cast < GeneratedExprSeq >(), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? t = null;

                if ((t = Expect(PyToken.Type.FSTRING_MIDDLE, "FSTRING_MIDDLE")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . DecodedConstantFromToken ( t );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_FstringReplacementField() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedSeq? b = null;
                GeneratedTokenInfo? c = null;

                if ((a = Expect(PyToken.Type.FSTRING_START, "FSTRING_START")) == null) return null;
                if ((b = ParseZeroOrMore(() => Parse_FstringMiddle())) == null) return null;
                if ((c = Expect(PyToken.Type.FSTRING_END, "FSTRING_END")) == null) return null;

                // Action code from grammar
                return GeneratedParserBridge . _PyPegen_joined_str ( a , b . Cast < GeneratedExprSeq >(), c );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? s = null;

                if ((s = Expect(PyToken.Type.STRING, "STRING")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . ConstantFromString ( s );
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: strings
        /// Alternatives: 1
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Strings()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = (GeneratedExprSeq)ParseOneOrMore(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . ConcatenateStrings ( a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (ExpectOp("[") == null) return null;
                if ((a = (GeneratedExprSeq)ParseOptional(() => Parse_StarNamedExpressions())) == null) return null;
                if (ExpectOp("]") == null) return null;

                // Action code from grammar
                return PyAst . List ( a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = ParseOptional(() => ParseGroup())) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . Tuple (( GeneratedExprSeq ?) a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (ExpectOp("{") == null) return null;
                if ((a = Parse_StarNamedExpressions()) == null) return null;
                if (ExpectOp("}") == null) return null;

                // Action code from grammar
                return PyAst . Set ( a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if (ExpectOp("{") == null) return null;
                if ((a = (GeneratedSeq)ParseOptional(() => Parse_DoubleStarredKvpairs())) == null) return null;
                if (ExpectOp("}") == null) return null;

                // Action code from grammar
                return PyAst . Dict ( Check < GeneratedExprSeq >( PyParserHelpers . GetKeys ( a )), Check < GeneratedExprSeq >( PyParserHelpers . GetValues ( a )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("{") == null) return null;
                if (Parse_InvalidDoubleStarredKvpairs() == null) return null;
                if (ExpectOp("}") == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if ((a = ParseGatherPlus(() => ExpectOp(","), () => Parse_DoubleStarredKvpair())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("**") == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . KeyValuePair ( null , a );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Kvpair() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Expression()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . KeyValuePair ( a , b );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedComprehensionSeq? a = null;

                if ((a = (GeneratedComprehensionSeq)ParseOneOrMore(() => Parse_ForIfClause())) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedExprSeq? c = null;

                if (Expect(PyToken.Type.ASYNC, "ASYNC") == null) return null;
                if (ExpectKeyword("for") == null) return null;
                if ((a = Parse_StarTargets()) == null) return null;
                if (ExpectKeyword("in") == null) return null;
                if ((b = Parse_Disjunction()) == null) return null;
                if ((c = (GeneratedExprSeq)ParseZeroOrMore(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return CheckVersion ( 6 , "Async comprehensions are" , PyAst . comprehension (( GeneratedExpr ) a ,( GeneratedExpr ) b , c , is_async : 1 ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedExprSeq? c = null;

                if (ExpectKeyword("for") == null) return null;
                if ((a = Parse_StarTargets()) == null) return null;
                if (ExpectKeyword("in") == null) return null;
                if ((b = Parse_Disjunction()) == null) return null;
                if ((c = (GeneratedExprSeq)ParseZeroOrMore(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyAst . comprehension (( GeneratedExpr ) a ,( GeneratedExpr ) b , c , is_async : 0 );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidForTarget() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (ExpectOp("[") == null) return null;
                if ((a = Parse_NamedExpression()) == null) return null;
                if ((b = Parse_ForIfClauses()) == null) return null;
                if (ExpectOp("]") == null) return null;

                // Action code from grammar
                return PyAst . ListComp ( a , b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidComprehension() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (ExpectOp("{") == null) return null;
                if ((a = Parse_NamedExpression()) == null) return null;
                if ((b = Parse_ForIfClauses()) == null) return null;
                if (ExpectOp("}") == null) return null;

                // Action code from grammar
                return PyAst . SetComp ( a , b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidComprehension() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (ExpectOp("(") == null) return null;
                if ((a = ParseGroup()) == null) return null;
                if ((b = Parse_ForIfClauses()) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . GeneratorExp (( GeneratedExpr ) a , b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidComprehension() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedKeyValuePair? a = null;
                GeneratedComprehensionSeq? b = null;

                if (ExpectOp("{") == null) return null;
                if ((a = Parse_Kvpair()) == null) return null;
                if ((b = Parse_ForIfClauses()) == null) return null;
                if (ExpectOp("}") == null) return null;

                // Action code from grammar
                return PyAst . DictComp ( a . Key , a . Value , b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidDictComprehension() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: arguments
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_Arguments()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_Args()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (PositiveLookahead(() => ExpectOp(")")) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidArguments() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;
                GeneratedPtr? b = null;

                if ((a = (GeneratedExprSeq)ParseGatherPlus(() => ExpectOp(","), () => ParseGroup())) == null) return null;
                if ((b = ParseOptional(() => ParseGroup())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . CollectCallSeqs ( a ,( GeneratedSeq ?) b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if ((a = Parse_Kwargs()) == null) return null;

                // Action code from grammar
                return PyAst . Call ( PyParserHelpers . DummyName (), CheckNullAllowed < GeneratedExprSeq >( PyParserHelpers . SeqExtractStarredExprs ( a )), CheckNullAllowed < GeneratedKeywordSeq >( PyParserHelpers . SeqDeleteStarredExprs ( a )), _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedSeq? b = null;

                if ((a = ParseGatherPlus(() => ExpectOp(","), () => Parse_KwargOrStarred())) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((b = ParseGatherPlus(() => ExpectOp(","), () => Parse_KwargOrDoubleStarred())) == null) return null;

                // Action code from grammar
                return PyParserHelpers . JoinSequences ( a , b );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ParseGatherPlus(() => ExpectOp(","), () => Parse_KwargOrStarred()) == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ParseGatherPlus(() => ExpectOp(","), () => Parse_KwargOrDoubleStarred()) == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidStarredExpression() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyAst . Starred ( a , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("*") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "Invalid star expression" );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidKwarg() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (ExpectOp("=") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . KeywordOrStarred ( Check < GeneratedKeyword >( PyAst . keyword ( a . GetNameValue (), b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset )), is_keyword : 1 );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_StarredExpression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . KeywordOrStarred ( a , is_keyword : 0 );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidKwarg() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (ExpectOp("=") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . KeywordOrStarred ( Check < GeneratedKeyword >( PyAst . keyword ( a . GetNameValue (), b , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset )), is_keyword : 1 );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("**") == null) return null;
                if ((a = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . KeywordOrStarred ( Check < GeneratedKeyword >( PyAst . keyword ( null , a , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset )), is_keyword : 1 );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_StarTarget()) == null) return null;
                if (NegativeLookahead(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return a;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if ((a = Parse_StarTarget()) == null) return null;
                if ((b = ParseZeroOrMore(() => ParseGroup())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return PyAst . Tuple (( GeneratedExprSeq ?) Check < GeneratedExprSeq >(( GeneratedExprSeq ) PyParserHelpers . SeqInsertInFront ( a , b )), GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = (GeneratedExprSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_StarTarget())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedSeq? b = null;

                if ((a = Parse_StarTarget()) == null) return null;
                if ((b = ParseOneOrMore(() => ParseGroup())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SeqInsertInFront ( a , b ). Cast < GeneratedExprSeq >();
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_StarTarget()) == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                return PyParserHelpers . SingletonSeq ( a ). Cast < GeneratedExprSeq >();
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: star_target
        /// Alternatives: 2
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_StarTarget()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPtr? a = null;

                if (ExpectOp("*") == null) return null;
                if ((a = ParseGroup()) == null) return null;

                // Action code from grammar
                return PyAst . Starred (( GeneratedExpr ) Check < GeneratedExpr >(( GeneratedExpr ) PyParserHelpers . SetExprContext (( GeneratedExpr ) a , GeneratedStore.Instance )), GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_TargetWithStarAtom() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: target_with_star_atom
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_TargetWithStarAtom()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if (ExpectOp(".") == null) return null;
                if ((b = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (NegativeLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Attribute ( a , b . GetNameValue (), GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if (ExpectOp("[") == null) return null;
                if ((b = Parse_Slices()) == null) return null;
                if (ExpectOp("]") == null) return null;
                if (NegativeLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Subscript ( a , b , GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_StarAtom() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedStore.Instance );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = Parse_TargetWithStarAtom()) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyParserHelpers . SetExprContext ( a , GeneratedStore.Instance );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = (GeneratedExprSeq)ParseOptional(() => Parse_StarTargetsTupleSeq())) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . Tuple ( a , GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (ExpectOp("[") == null) return null;
                if ((a = (GeneratedExprSeq)ParseOptional(() => Parse_StarTargetsListSeq())) == null) return null;
                if (ExpectOp("]") == null) return null;

                // Action code from grammar
                return PyAst . List ( a , GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_SingleSubscriptAttributeTarget() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedStore.Instance );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = Parse_SingleTarget()) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if (ExpectOp(".") == null) return null;
                if ((b = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (NegativeLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Attribute ( a , b . GetNameValue (), GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if (ExpectOp("[") == null) return null;
                if ((b = Parse_Slices()) == null) return null;
                if (ExpectOp("]") == null) return null;
                if (NegativeLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Subscript ( a , b , GeneratedStore.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: t_primary
        /// Alternatives: 5
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_TPrimary()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if (ExpectOp(".") == null) return null;
                if ((b = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (PositiveLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Attribute ( a , b . GetNameValue (), GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if (ExpectOp("[") == null) return null;
                if ((b = Parse_Slices()) == null) return null;
                if (ExpectOp("]") == null) return null;
                if (PositiveLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Subscript ( a , b , GeneratedLoad.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if ((b = Parse_Genexp()) == null) return null;
                if (PositiveLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Call ( a , Check < GeneratedExprSeq >( PyParserHelpers . SingletonSeq ( b ). Cast < GeneratedExprSeq >()), null , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if (ExpectOp("(") == null) return null;
                if ((b = (GeneratedExpr)ParseOptional(() => Parse_Arguments())) == null) return null;
                if (ExpectOp(")") == null) return null;
                if (PositiveLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Call ( a ,( b != null )?(( GeneratedCall ) b ). Args : null !,( b != null )?(( GeneratedCall ) b ). Keywords : null !, _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_Atom()) == null) return null;
                if (PositiveLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("(") == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("[") == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp(".") == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = (GeneratedExprSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_DelTarget())) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;

                // Action code from grammar
                return a;
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: del_target
        /// Alternatives: 3
        /// Return Type: GeneratedExpr
        /// </summary>
        private GeneratedExpr? Parse_DelTarget()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if (ExpectOp(".") == null) return null;
                if ((b = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (NegativeLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Attribute ( a , b . GetNameValue (), GeneratedDel.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_TPrimary()) == null) return null;
                if (ExpectOp("[") == null) return null;
                if ((b = Parse_Slices()) == null) return null;
                if (ExpectOp("]") == null) return null;
                if (NegativeLookahead(() => Parse_TLookahead()) == null) return null;

                // Action code from grammar
                return PyAst . Subscript ( a , b , GeneratedDel.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_DelTAtom() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SetExprContext ( NameToken ( a ), GeneratedDel.Instance );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = Parse_DelTarget()) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyParserHelpers . SetExprContext ( a , GeneratedDel.Instance );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = (GeneratedExprSeq)ParseOptional(() => Parse_DelTargets())) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return PyAst . Tuple ( a , GeneratedDel.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if (ExpectOp("[") == null) return null;
                if ((a = (GeneratedExprSeq)ParseOptional(() => Parse_DelTargets())) == null) return null;
                if (ExpectOp("]") == null) return null;

                // Action code from grammar
                return PyAst . List ( a , GeneratedDel.Instance , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedExpr? b = null;
                GeneratedExpr? c = null;

                if ((a = ParseGatherPlus(() => ExpectOp(","), () => Parse_Expression())) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (ExpectOp("*") == null) return null;
                if ((b = Parse_Expression()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (ExpectOp("**") == null) return null;
                if ((c = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SeqAppendToEnd (( Check < GeneratedSeq >( PyParserHelpers . SeqAppendToEnd ( a , b )). Cast < GeneratedExprSeq >()), c ). Cast < GeneratedExprSeq >();
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedExpr? b = null;

                if ((a = ParseGatherPlus(() => ExpectOp(","), () => Parse_Expression())) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (ExpectOp("*") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SeqAppendToEnd ( a , b ). Cast < GeneratedExprSeq >();
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;
                GeneratedExpr? b = null;

                if ((a = ParseGatherPlus(() => ExpectOp(","), () => Parse_Expression())) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (ExpectOp("**") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SeqAppendToEnd ( a , b ). Cast < GeneratedExprSeq >();
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Parse_Expression()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (ExpectOp("**") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SeqAppendToEnd (( Check < GeneratedSeq >( PyParserHelpers . SingletonSeq ( a )). Cast < GeneratedExprSeq >()), b ). Cast < GeneratedExprSeq >();
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("*") == null) return null;
                if ((a = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SingletonSeq ( a ). Cast < GeneratedExprSeq >();
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("**") == null) return null;
                if ((a = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyParserHelpers . SingletonSeq ( a ). Cast < GeneratedExprSeq >();
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExprSeq? a = null;

                if ((a = (GeneratedExprSeq)ParseGatherPlus(() => ExpectOp(","), () => Parse_Expression())) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? t = null;

                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if ((t = Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT")) == null) return null;
                if (PositiveLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return t;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_InvalidDoubleTypeComments() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT") == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseGroup() == null) return null;
                if ((a = ExpectOp(",")) == null) return null;
                if (ParseGatherPlus(() => ExpectOp(","), () => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorStartingFrom ( a , "iterable argument unpacking follows keyword argument unpacking" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if ((a = Parse_Expression()) == null) return null;
                if ((b = Parse_ForIfClauses()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , PyPegen . GetLastComprehensionItem ( PyPegen . LastItem < GeneratedComprehension >( b )), "Generator expression must be parenthesized" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((b = ExpectOp("=")) == null) return null;
                if (Parse_Expression() == null) return null;
                if (Parse_ForIfClauses() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "invalid syntax. Maybe you meant '==' or ':=' instead of '='?" );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (ParseOptional(() => ParseGroup()) == null) return null;
                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((b = ExpectOp("=")) == null) return null;
                if (PositiveLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "expected argument value expression" );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if ((a = Parse_Args()) == null) return null;
                if ((b = Parse_ForIfClauses()) == null) return null;

                // Action code from grammar
                return GeneratedParserBridge . _PyPegen_nonparen_genexp_in_call ( a , b );
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedComprehensionSeq? b = null;

                if (Parse_Args() == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((a = Parse_Expression()) == null) return null;
                if ((b = Parse_ForIfClauses()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , PyPegen . GetLastComprehensionItem ( PyPegen . LastItem < GeneratedComprehension >( b )), "Generator expression must be parenthesized" );
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_Args()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (Parse_Args() == null) return null;

                // Action code from grammar
                return GeneratedParserBridge . _PyPegen_arguments_parsing_error ( a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = (GeneratedTokenInfo)ParseGroup()) == null) return null;
                if ((b = ExpectOp("=")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "cannot assign to %s" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((b = ExpectOp("=")) == null) return null;
                if (Parse_Expression() == null) return null;
                if (Parse_ForIfClauses() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "invalid syntax. Maybe you meant '==' or ':=' instead of '='?" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (NegativeLookahead(() => ParseGroup()) == null) return null;
                if ((a = Parse_Expression()) == null) return null;
                if ((b = ExpectOp("=")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "expression cannot contain assignment, perhaps you meant \"==\"?" );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = ExpectOp("**")) == null) return null;
                if (Parse_Expression() == null) return null;
                if (ExpectOp("=") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "cannot assign to keyword argument unpacking" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;
                GeneratedExpr? c = null;

                if ((a = Parse_Disjunction()) == null) return null;
                if (ExpectKeyword("if") == null) return null;
                if ((b = Parse_Disjunction()) == null) return null;
                if (ExpectKeyword("else") == null) return null;
                if ((c = Parse_Expression()) == null) return null;

                // Action code from grammar
                return PyAst . IfExp ( b , a , c , _start_lineno, _start_col_offset, _end_lineno, _end_col_offset );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Disjunction() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Lambdef() == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (NegativeLookahead(() => ExpectOp("(")) == null) return null;
                if ((b = Parse_StarExpressions()) == null) return null;

                // Action code from grammar
                return PyPegen . CheckLegacyStmt ( NameToken ( a ))? RaiseSyntaxErrorKnownRange ( NameToken ( a ), b , "Missing parentheses in call to '{a.GetNameValue()}'. Did you mean {a.GetNameValue()}(...)?" ): null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if (NegativeLookahead(() => ParseGroup()) == null) return null;
                if ((a = Parse_Disjunction()) == null) return null;
                if ((b = Parse_ExpressionWithoutInvalid()) == null) return null;

                // Action code from grammar
                return PyPegen . CheckLegacyStmt ( a )? null : _tokens [ _mark - 1 ]. Level == 0 ? null : RaiseSyntaxErrorKnownRange ( a , b , "invalid syntax. Perhaps you forgot a comma?" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExpr? b = null;

                if ((a = Parse_Disjunction()) == null) return null;
                if (ExpectKeyword("if") == null) return null;
                if ((b = Parse_Disjunction()) == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "expected 'else' after 'if' expression" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if ((a = ExpectKeyword("lambda")) == null) return null;
                if (ParseOptional(() => Parse_LambdaParams()) == null) return null;
                if ((b = ExpectOp(":")) == null) return null;
                if (PositiveLookahead(() => Expect(PyToken.Type.FSTRING_MIDDLE, "FSTRING_MIDDLE")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "f-string: lambda expressions are not allowed without parentheses" );
            }

            Reset(_mark);
            return null;
        }

        /// <summary>
        /// Rule: invalid_named_expression
        /// Alternatives: 3
        /// </summary>
        private GeneratedPtr? Parse_InvalidNamedExpression()
        {
            int _mark = Mark();

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_Expression()) == null) return null;
                if (ExpectOp(":=") == null) return null;
                if (Parse_Expression() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "cannot use assignment expressions with %s" , PyPegen . GetExprName ( a ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if (ExpectOp("=") == null) return null;
                if ((b = Parse_BitwiseOr()) == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "invalid syntax. Maybe you meant '==' or ':=' instead of '='?" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (NegativeLookahead(() => ParseGroup()) == null) return null;
                if ((a = Parse_BitwiseOr()) == null) return null;
                if ((b = ExpectOp("=")) == null) return null;
                if (Parse_BitwiseOr() == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "cannot assign to %s here. Maybe you meant '==' instead of '='?" , PyPegen . GetExprName ( a ));
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_InvalidAnnAssignTarget()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Parse_Expression() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "only single target (not %s) can be annotated" , PyPegen . GetExprName ( a ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_StarNamedExpression()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (ParseZeroOrMore(() => Parse_StarNamedExpressions()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Parse_Expression() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "only single target (not tuple) can be annotated" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_Expression()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Parse_Expression() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "illegal target for annotation" );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ParseZeroOrMore(() => ParseGroup()) == null) return null;
                if ((a = Parse_StarExpressions()) == null) return null;
                if (ExpectOp("=") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorInvalidTarget ( "assign to" , a );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ParseZeroOrMore(() => ParseGroup()) == null) return null;
                if ((a = Parse_YieldExpr()) == null) return null;
                if (ExpectOp("=") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "assignment to yield expression not possible" );
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_StarExpressions()) == null) return null;
                if (Parse_Augassign() == null) return null;
                if (ParseGroup() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "'%s' is an illegal expression for augmented assignment" , PyPegen . GetExprName ( a ));
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_List() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (Parse_Tuple() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = Parse_InvalidAnnAssignTarget()) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("del") == null) return null;
                if ((a = Parse_StarExpressions()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorInvalidTarget ( "delete" , a );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ParseGroup() == null) return null;
                if ((a = Parse_StarredExpression()) == null) return null;
                if (Parse_ForIfClauses() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "iterable unpacking cannot be used in comprehension" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedExprSeq? b = null;

                if (ParseGroup() == null) return null;
                if ((a = Parse_StarNamedExpression()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((b = Parse_StarNamedExpressions()) == null) return null;
                if (Parse_ForIfClauses() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , PyPegen . LastItem < GeneratedExpr >( b ), "did you forget parentheses around the comprehension target?" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;
                GeneratedTokenInfo? b = null;

                if (ParseGroup() == null) return null;
                if ((a = Parse_StarNamedExpression()) == null) return null;
                if ((b = ExpectOp(",")) == null) return null;
                if (Parse_ForIfClauses() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "did you forget parentheses around the comprehension target?" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("{") == null) return null;
                if ((a = ExpectOp("**")) == null) return null;
                if (Parse_BitwiseOr() == null) return null;
                if (Parse_ForIfClauses() == null) return null;
                if (ExpectOp("}") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "dict unpacking cannot be used in dict comprehension" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectSoftKeyword("/")) == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "at least one argument must precede /" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseGroup() == null) return null;
                if (ParseZeroOrMore(() => Parse_ParamMaybeDefault()) == null) return null;
                if ((a = ExpectOp("/")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "/ may appear only once" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (ParseOptional(() => Parse_SlashNoDefault()) == null) return null;
                if (ParseZeroOrMore(() => Parse_ParamNoDefault()) == null) return null;
                if (Parse_InvalidParametersHelper() == null) return null;
                if ((a = Parse_ParamNoDefault()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "parameter without a default follows parameter with a default" );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (ParseZeroOrMore(() => Parse_ParamNoDefault()) == null) return null;
                if ((a = ExpectOp("(")) == null) return null;
                if (ParseOneOrMore(() => Parse_ParamNoDefault()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if ((b = ExpectOp(")")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "Function parameters cannot be parenthesized" );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ParseZeroOrMore(() => Parse_ParamMaybeDefault()) == null) return null;
                if (ExpectOp("*") == null) return null;
                if (ParseGroup() == null) return null;
                if (ParseZeroOrMore(() => Parse_ParamMaybeDefault()) == null) return null;
                if ((a = ExpectOp("/")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "/ must be ahead of *" );
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseOneOrMore(() => Parse_ParamMaybeDefault()) == null) return null;
                if (ExpectOp("/") == null) return null;
                if ((a = ExpectOp("*")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "expected comma between / and *" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectOp("=")) == null) return null;
                if (PositiveLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "expected default value expression" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectOp("*")) == null) return null;
                if (ParseGroup() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "named arguments must follow bare *" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("*") == null) return null;
                if (ExpectOp(",") == null) return null;
                if (Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "bare * has associated type comment" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("*") == null) return null;
                if (Parse_Param() == null) return null;
                if ((a = ExpectOp("=")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "var-positional argument cannot have default value" );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("*") == null) return null;
                if (ParseGroup() == null) return null;
                if (ParseZeroOrMore(() => Parse_ParamMaybeDefault()) == null) return null;
                if ((a = ExpectOp("*")) == null) return null;
                if (ParseGroup() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "* argument may appear only once" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("**") == null) return null;
                if (Parse_Param() == null) return null;
                if ((a = ExpectOp("=")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "var-keyword argument cannot have default value" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (ExpectOp("**") == null) return null;
                if (Parse_Param() == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((a = Parse_Param()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "arguments cannot follow var-keyword argument" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("**") == null) return null;
                if (Parse_Param() == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((a = (GeneratedTokenInfo)ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "arguments cannot follow var-keyword argument" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSlashWithDefault? a = null;

                if ((a = Parse_SlashWithDefault()) == null) return null;

                // Action code from grammar
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedSeq? a = null;

                if ((a = ParseOneOrMore(() => Parse_ParamWithDefault())) == null) return null;

                // Action code from grammar
                return null;
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectSoftKeyword("/")) == null) return null;
                if (ExpectOp(",") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "at least one argument must precede /" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseGroup() == null) return null;
                if (ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault()) == null) return null;
                if ((a = ExpectOp("/")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "/ may appear only once" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (ParseOptional(() => Parse_LambdaSlashNoDefault()) == null) return null;
                if (ParseZeroOrMore(() => Parse_LambdaParamNoDefault()) == null) return null;
                if (Parse_InvalidLambdaParametersHelper() == null) return null;
                if ((a = Parse_LambdaParamNoDefault()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "parameter without a default follows parameter with a default" );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (ParseZeroOrMore(() => Parse_LambdaParamNoDefault()) == null) return null;
                if ((a = ExpectOp("(")) == null) return null;
                if (ParseGatherPlus(() => ExpectOp(","), () => Parse_LambdaParam()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if ((b = ExpectOp(")")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "Lambda expression parameters cannot be parenthesized" );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault()) == null) return null;
                if (ExpectOp("*") == null) return null;
                if (ParseGroup() == null) return null;
                if (ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault()) == null) return null;
                if ((a = ExpectOp("/")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "/ must be ahead of *" );
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseOneOrMore(() => Parse_LambdaParamMaybeDefault()) == null) return null;
                if (ExpectOp("/") == null) return null;
                if ((a = ExpectOp("*")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "expected comma between / and *" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedSlashWithDefault? a = null;

                if ((a = Parse_LambdaSlashWithDefault()) == null) return null;

                // Action code from grammar
                return GeneratedParserBridge . _PyPegen_singleton_seq ( a );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ParseOneOrMore(() => Parse_LambdaParamWithDefault()) == null) return null;

                // Default action: no captures (unexpected)
                return null;
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("*") == null) return null;
                if (ParseGroup() == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "named arguments must follow bare *" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("*") == null) return null;
                if (Parse_LambdaParam() == null) return null;
                if ((a = ExpectOp("=")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "var-positional argument cannot have default value" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("*") == null) return null;
                if (ParseGroup() == null) return null;
                if (ParseZeroOrMore(() => Parse_LambdaParamMaybeDefault()) == null) return null;
                if ((a = ExpectOp("*")) == null) return null;
                if (ParseGroup() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "* argument may appear only once" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("**") == null) return null;
                if (Parse_LambdaParam() == null) return null;
                if ((a = ExpectOp("=")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "var-keyword argument cannot have default value" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedArg? a = null;

                if (ExpectOp("**") == null) return null;
                if (Parse_LambdaParam() == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((a = Parse_LambdaParam()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "arguments cannot follow var-keyword argument" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("**") == null) return null;
                if (Parse_LambdaParam() == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((a = (GeneratedTokenInfo)ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "arguments cannot follow var-keyword argument" );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (Expect(PyToken.Type.TYPE_COMMENT, "TYPE_COMMENT") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (Expect(PyToken.Type.INDENT, "INDENT") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "Cannot have two type comments on def" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (Parse_Expression() == null) return null;
                if (ExpectKeyword("as") == null) return null;
                if ((a = Parse_Expression()) == null) return null;
                if (PositiveLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorInvalidTarget ( "assign to" , a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ParseOptional(() => Expect(PyToken.Type.ASYNC, "ASYNC")) == null) return null;
                if (ExpectKeyword("for") == null) return null;
                if ((a = Parse_StarExpressions()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorInvalidTarget ( "use in for loop" , a );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = Parse_StarredExpression()) == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "cannot use starred expression here" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("(") == null) return null;
                if ((a = ExpectOp("**")) == null) return null;
                if (Parse_Expression() == null) return null;
                if (ExpectOp(")") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "cannot use double starred expression here" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("import")) == null) return null;
                if (ParseGatherPlus(() => ExpectOp(","), () => Parse_DottedName()) == null) return null;
                if (ExpectKeyword("from") == null) return null;
                if (Parse_DottedName() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorStartingFrom ( a , "Did you mean to use 'from ... import ...' instead?" );
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

            Reset(_mark);
            {
                CaptureStart();


                if (Parse_ImportFromAsNames() == null) return null;
                if (ExpectOp(",") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "trailing comma not allowed without surrounding parentheses" );
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

            Reset(_mark);
            {
                CaptureStart();


                if (ParseOptional(() => Expect(PyToken.Type.ASYNC, "ASYNC")) == null) return null;
                if (ExpectKeyword("with") == null) return null;
                if (ParseGatherPlus(() => ExpectOp(","), () => ParseGroup()) == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ParseOptional(() => Expect(PyToken.Type.ASYNC, "ASYNC")) == null) return null;
                if (ExpectKeyword("with") == null) return null;
                if (ExpectOp("(") == null) return null;
                if (ParseGatherPlus(() => ExpectOp(","), () => ParseGroup()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp(")") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseOptional(() => Expect(PyToken.Type.ASYNC, "ASYNC")) == null) return null;
                if ((a = ExpectKeyword("with")) == null) return null;
                if (ParseGatherPlus(() => ExpectOp(","), () => ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'with' statement on line %d" , a . GetLineNo ());
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseOptional(() => Expect(PyToken.Type.ASYNC, "ASYNC")) == null) return null;
                if ((a = ExpectKeyword("with")) == null) return null;
                if (ExpectOp("(") == null) return null;
                if (ParseGatherPlus(() => ExpectOp(","), () => ParseGroup()) == null) return null;
                if (ParseOptional(() => ExpectOp(",")) == null) return null;
                if (ExpectOp(")") == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'with' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("try")) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'try' statement on line %d" , a . GetLineNo ());
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("try") == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Parse_Block() == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected 'except' or 'finally' block" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedTokenInfo? b = null;

                if (ExpectKeyword("try") == null) return null;
                if (ExpectOp(":") == null) return null;
                if (ParseZeroOrMore(() => Parse_Block()) == null) return null;
                if (ParseOneOrMore(() => Parse_ExceptBlock()) == null) return null;
                if ((a = ExpectKeyword("except")) == null) return null;
                if ((b = ExpectOp("*")) == null) return null;
                if (Parse_Expression() == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "cannot have both 'except' and 'except*' on the same 'try'" );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectKeyword("try") == null) return null;
                if (ExpectOp(":") == null) return null;
                if (ParseZeroOrMore(() => Parse_Block()) == null) return null;
                if (ParseOneOrMore(() => Parse_ExceptStarBlock()) == null) return null;
                if ((a = ExpectKeyword("except")) == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "cannot have both 'except' and 'except*' on the same 'try'" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (ExpectKeyword("except") == null) return null;
                if (ParseOptional(() => ExpectOp("*")) == null) return null;
                if ((a = Parse_Expression()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (Parse_Expressions() == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorStartingFrom ( a , "multiple exception types must be parenthesized" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("except")) == null) return null;
                if (ParseOptional(() => ExpectOp("*")) == null) return null;
                if (Parse_Expression() == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("except")) == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("except")) == null) return null;
                if (ExpectOp("*") == null) return null;
                if (ParseGroup() == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected one or more exception types" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("finally")) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'finally' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("except")) == null) return null;
                if (Parse_Expression() == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'except' statement on line %d" , a . GetLineNo ());
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("except")) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'except' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("except")) == null) return null;
                if (ExpectOp("*") == null) return null;
                if (Parse_Expression() == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'except*' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectSoftKeyword("match") == null) return null;
                if (Parse_SubjectExpr() == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                return CheckVersion ( 10 , "Pattern matching is" , RaiseSyntaxError ( "expected ':'" ));
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? subject = null;

                if ((a = ExpectSoftKeyword("match")) == null) return null;
                if ((subject = Parse_SubjectExpr()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'match' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectSoftKeyword("case") == null) return null;
                if (Parse_Patterns() == null) return null;
                if (ParseOptional(() => Parse_Guard()) == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectSoftKeyword("case")) == null) return null;
                if (Parse_Patterns() == null) return null;
                if (ParseOptional(() => Parse_Guard()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'case' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (Parse_OrPattern() == null) return null;
                if (ExpectKeyword("as") == null) return null;
                if ((a = ExpectSoftKeyword("_")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "cannot use '_' as a target" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if (Parse_OrPattern() == null) return null;
                if (ExpectKeyword("as") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.NAME, "NAME")) == null) return null;
                if ((a = Parse_Expression()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "invalid pattern target" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? a = null;

                if (Parse_NameOrAttr() == null) return null;
                if (ExpectOp("(") == null) return null;
                if ((a = Parse_InvalidClassArgumentPattern()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( PyPegen . FirstItem < GeneratedPattern >( a ), PyPegen . LastItem < GeneratedPattern >( a ), "positional patterns follow keyword patterns" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedPatternSeq? a = null;

                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (Parse_KeywordPatterns() == null) return null;
                if (ExpectOp(",") == null) return null;
                if ((a = Parse_PositionalPatterns()) == null) return null;

                // Action code from grammar
                return a;
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("if") == null) return null;
                if (Parse_NamedExpression() == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("if")) == null) return null;
                if (Parse_NamedExpression() == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'if' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("elif") == null) return null;
                if (Parse_NamedExpression() == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("elif")) == null) return null;
                if (Parse_NamedExpression() == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'elif' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("else")) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'else' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("while") == null) return null;
                if (Parse_NamedExpression() == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("while")) == null) return null;
                if (Parse_NamedExpression() == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'while' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();


                if (ParseOptional(() => Expect(PyToken.Type.ASYNC, "ASYNC")) == null) return null;
                if (ExpectKeyword("for") == null) return null;
                if (Parse_StarTargets() == null) return null;
                if (ExpectKeyword("in") == null) return null;
                if (Parse_StarExpressions() == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseOptional(() => Expect(PyToken.Type.ASYNC, "ASYNC")) == null) return null;
                if ((a = ExpectKeyword("for")) == null) return null;
                if (Parse_StarTargets() == null) return null;
                if (ExpectKeyword("in") == null) return null;
                if (Parse_StarExpressions() == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after 'for' statement on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ParseOptional(() => Expect(PyToken.Type.ASYNC, "ASYNC")) == null) return null;
                if ((a = ExpectKeyword("def")) == null) return null;
                if (Expect(PyToken.Type.NAME, "NAME") == null) return null;
                if (ParseOptional(() => Parse_TypeParams()) == null) return null;
                if (ExpectOp("(") == null) return null;
                if (ParseOptional(() => Parse_Params()) == null) return null;
                if (ExpectOp(")") == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after function definition on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectKeyword("class") == null) return null;
                if (Expect(PyToken.Type.NAME, "NAME") == null) return null;
                if (ParseOptional(() => Parse_TypeParams()) == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;

                // Action code from grammar
                RaiseSyntaxError ( "expected ':'" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if ((a = ExpectKeyword("class")) == null) return null;
                if (Expect(PyToken.Type.NAME, "NAME") == null) return null;
                if (ParseOptional(() => Parse_TypeParams()) == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (Expect(PyToken.Type.NEWLINE, "NEWLINE") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.INDENT, "INDENT")) == null) return null;

                // Action code from grammar
                return RaiseIndentationError ( "expected an indented block after class definition on line %d" , a . GetLineNo ());
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

            Reset(_mark);
            {
                CaptureStart();


                if (ParseGatherPlus(() => ExpectOp(","), () => Parse_DoubleStarredKvpair()) == null) return null;
                if (ExpectOp(",") == null) return null;
                if (Parse_InvalidKvpair() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (Parse_Expression() == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((a = ExpectOp("*")) == null) return null;
                if (Parse_BitwiseOr() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorStartingFrom ( a , "cannot use a starred expression in a dictionary value" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (Parse_Expression() == null) return null;
                if ((a = ExpectOp(":")) == null) return null;
                if (PositiveLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "expression expected after dictionary key and ':'" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedExpr? a = null;

                if ((a = Parse_Expression()) == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return RaiseErrorKnownLocation ( typeof ( PySyntaxErrorException ), a . GetLineNo (), a . EndColOffset - 1 , a . EndLineNo ,- 1 , "':' expected after dictionary key" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (Parse_Expression() == null) return null;
                if (ExpectOp(":") == null) return null;
                if ((a = ExpectOp("*")) == null) return null;
                if (Parse_BitwiseOr() == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorStartingFrom ( a , "cannot use a starred expression in a dictionary value" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (Parse_Expression() == null) return null;
                if ((a = ExpectOp(":")) == null) return null;
                if (PositiveLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "expression expected after dictionary key and ':'" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;
                GeneratedExpr? b = null;

                if ((a = ExpectOp("*")) == null) return null;
                if (Parse_Expression() == null) return null;
                if (ExpectOp("=") == null) return null;
                if ((b = Parse_Expression()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownRange ( a , b , "cannot assign to iterable argument unpacking" );
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

            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("{") == null) return null;
                if ((a = ExpectOp("=")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "f-string: valid expression required before '='" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("{") == null) return null;
                if ((a = ExpectOp("!")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "f-string: valid expression required before '!'" );
            }

            // Alternative 3
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("{") == null) return null;
                if ((a = ExpectOp(":")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "f-string: valid expression required before ':'" );
            }

            // Alternative 4
            Reset(_mark);
            {
                CaptureStart();

                GeneratedTokenInfo? a = null;

                if (ExpectOp("{") == null) return null;
                if ((a = ExpectOp("}")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorKnownLocation ( a , "f-string: valid expression required before '}'" );
            }

            // Alternative 5
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("{") == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorOnNextToken ( "f-string: expecting a valid expression after '{'" );
            }

            // Alternative 6
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("{") == null) return null;
                if (ParseGroup() == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting '=', or '!', or ':', or '}'" );
            }

            // Alternative 7
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("{") == null) return null;
                if (ParseGroup() == null) return null;
                if (ExpectOp("=") == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting '!', or ':', or '}'" );
            }

            // Alternative 8
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("{") == null) return null;
                if (ParseGroup() == null) return null;
                if (ParseOptional(() => ExpectOp("=")) == null) return null;
                if (Parse_InvalidConversionCharacter() == null) return null;

                // Default action: no captures (unexpected)
                return null;
            }

            // Alternative 9
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("{") == null) return null;
                if (ParseGroup() == null) return null;
                if (ParseOptional(() => ExpectOp("=")) == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (NegativeLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting ':' or '}'" );
            }

            // Alternative 10
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("{") == null) return null;
                if (ParseGroup() == null) return null;
                if (ParseOptional(() => ExpectOp("=")) == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (ExpectOp(":") == null) return null;
                if (ParseZeroOrMore(() => Parse_FstringFormatSpec()) == null) return null;
                if (NegativeLookahead(() => ExpectOp("}")) == null) return null;

                // Action code from grammar
                return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting '}', or format specs" );
            }

            // Alternative 11
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("{") == null) return null;
                if (ParseGroup() == null) return null;
                if (ParseOptional(() => ExpectOp("=")) == null) return null;
                if (ParseOptional(() => ParseGroup()) == null) return null;
                if (NegativeLookahead(() => ExpectOp("}")) == null) return null;

                // Action code from grammar
                return PyErr_Occurred ()? null : RaiseSyntaxErrorOnNextToken ( "f-string: expecting '}'" );
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

            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("!") == null) return null;
                if (PositiveLookahead(() => ParseGroup()) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorOnNextToken ( "f-string: missing conversion character" );
            }

            // Alternative 2
            Reset(_mark);
            {
                CaptureStart();


                if (ExpectOp("!") == null) return null;
                if (NegativeLookahead(() => Expect(PyToken.Type.NAME, "NAME")) == null) return null;

                // Action code from grammar
                RaiseSyntaxErrorOnNextToken ( "f-string: invalid conversion character" );
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
