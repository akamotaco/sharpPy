using System;
using System.Collections.Generic;
using SharpPy.Generated;

namespace SharpPy.Generated
{
    /// <summary>
    /// CPython 3.12: _PyPegen_* helper functions
    /// Translated from cpython-3.12/Parser/action_helpers.c
    /// </summary>
    public static partial class PyParserRuntime
    {
        // ============================================================
        // CPython 3.12: Parser helper functions from action_helpers.c
        // C -> C# direct translation
        // ============================================================

        /// <summary>
        /// CPython: NameDefaultPair *NameDefaultPair(Parser *p, arg_ty arg, expr_ty c, Token *tc)
        /// </summary>
        public static GeneratedNameDefaultPair NameDefaultPair(GeneratedArg arg, GeneratedExpr defaultValue, GeneratedTokenInfo? typeComment)
        {
            return new GeneratedNameDefaultPair
            {
                Arg = arg,
                Default = defaultValue,
                TypeComment = typeComment?.Value
            };
        }

        /// <summary>
        /// CPython: SlashWithDefault *SlashWithDefault(Parser *p, asdl_arg_seq *plain, asdl_seq *names_with_defaults)
        /// </summary>
        public static GeneratedSlashWithDefault SlashWithDefault(GeneratedArgSeq plain, GeneratedSeq namesWithDefaults)
        {
            var result = new GeneratedSlashWithDefault();

            // Add plain args
            if (plain != null && plain.Count > 0)
            {
                result.Args.AddRange(plain);
            }

            // Process names_with_defaults (GeneratedNameDefaultPair sequence)
            if (namesWithDefaults != null && namesWithDefaults.Count > 0)
            {
                foreach (var item in namesWithDefaults)
                {
                    var pair = (GeneratedNameDefaultPair)item;
                    result.Args.Add(pair.Arg);
                    result.Defaults.Add(pair.Default);
                }
            }

            return result;
        }

        /// <summary>
        /// CPython: StarEtc *StarEtc(Parser *p, arg_ty vararg, asdl_seq *kwonlyargs, arg_ty kwarg)
        /// </summary>
        public static GeneratedStarEtc StarEtc(GeneratedArg vararg, GeneratedSeq kwonlyargs, GeneratedArg kwarg)
        {
            var result = new GeneratedStarEtc
            {
                Vararg = vararg,
                Kwarg = kwarg
            };

            // Process kwonlyargs (GeneratedNameDefaultPair sequence)
            if (kwonlyargs != null && kwonlyargs.Count > 0)
            {
                foreach (var item in kwonlyargs)
                {
                    var pair = (GeneratedNameDefaultPair)item;
                    result.Kwonlyargs.Add(pair.Arg);
                    result.KwDefaults.Add(pair.Default);
                }
            }

            return result;
        }

        /// <summary>
        /// CPython: arguments_ty MakeArguments(Parser *p, asdl_arg_seq *slash_without_default,
        ///     SlashWithDefault *slash_with_default, asdl_arg_seq *plain_names,
        ///     asdl_seq *names_with_default, StarEtc *star_etc)
        /// </summary>
        public static GeneratedArguments MakeArguments(
            GeneratedArgSeq slashWithoutDefault,
            GeneratedSlashWithDefault slashWithDefault,
            GeneratedArgSeq plainNames,
            GeneratedSeq namesWithDefault,
            GeneratedStarEtc starEtc)
        {
            // CPython logic: combine all argument types into arguments_ty structure
            var posonlyargs = new GeneratedArgSeq();
            var args = new GeneratedArgSeq();
            var defaults = new GeneratedExprSeq();
            var kwonlyargs = new GeneratedArgSeq();
            var kw_defaults = new GeneratedExprSeq();

            // Handle slash_without_default
            if (slashWithoutDefault != null && slashWithoutDefault.Count > 0)
            {
                posonlyargs.AddRange(slashWithoutDefault);
            }

            // Handle slash_with_default
            if (slashWithDefault != null)
            {
                if (slashWithDefault.Args != null && slashWithDefault.Args.Count > 0)
                {
                    posonlyargs.AddRange(slashWithDefault.Args);
                }
                if (slashWithDefault.Defaults != null && slashWithDefault.Defaults.Count > 0)
                {
                    defaults.AddRange(slashWithDefault.Defaults);
                }
            }

            // Handle plain_names
            if (plainNames != null && plainNames.Count > 0)
            {
                args.AddRange(plainNames);
            }

            // Handle names_with_default
            if (namesWithDefault != null && namesWithDefault.Count > 0)
            {
                foreach (var item in namesWithDefault)
                {
                    var pair = (GeneratedNameDefaultPair)item;
                    args.Add(pair.Arg);
                    defaults.Add(pair.Default);
                }
            }

            // Handle star_etc
            GeneratedArg vararg = null;
            GeneratedArg kwarg = null;
            if (starEtc != null)
            {
                vararg = starEtc.Vararg;
                kwarg = starEtc.Kwarg;
                if (starEtc.Kwonlyargs != null && starEtc.Kwonlyargs.Count > 0)
                {
                    kwonlyargs.AddRange(starEtc.Kwonlyargs);
                }
                if (starEtc.KwDefaults != null && starEtc.KwDefaults.Count > 0)
                {
                    kw_defaults.AddRange(starEtc.KwDefaults);
                }
            }

            return new GeneratedArguments
            {
                Posonlyargs = posonlyargs,
                Args = args,
                Vararg = vararg,
                Kwonlyargs = kwonlyargs,
                KwDefaults = kw_defaults,
                Kwarg = kwarg,
                Defaults = defaults
            };
        }

        /// <summary>
        /// CPython: expr_ty JoinNamesWithDot(Parser *p, expr_ty first_name, expr_ty second_name)
        /// </summary>
        public static GeneratedExpr JoinNamesWithDot(GeneratedExpr firstName, GeneratedExpr secondName)
        {
            // CPython: firstName.id + "." + secondName.id
            // Extract string values and join with dot
            string first = firstName is GeneratedName n1 ? n1.Id : firstName.ToString();
            string second = secondName is GeneratedName n2 ? n2.Id : secondName.ToString();

            return new GeneratedName
            {
                Id = first + "." + second,
                Ctx = GeneratedLoad.Instance
            };
        }

        /// <summary>
        /// CPython: asdl_seq *JoinSequences(Parser *p, asdl_seq *a, asdl_seq *b)
        /// </summary>
        public static GeneratedSeq JoinSequences(GeneratedSeq a, GeneratedSeq b)
        {
            if (a == null) return b;
            if (b == null) return a;

            var result = new GeneratedSeq(a.Count + b.Count);
            result.AddRange(a);
            result.AddRange(b);
            return result;
        }

        /// <summary>
        /// CPython: expr_ty ClassDefDecorators(Parser *p, asdl_expr_seq *decorators, class_body_ty class_def)
        /// </summary>
        public static GeneratedStmt ClassDefDecorators(GeneratedExprSeq decorators, GeneratedStmt classDef)
        {
            // Attach decorators to ClassDef
            if (classDef is GeneratedClassDef cd)
            {
                cd.DecoratorList = decorators ?? GeneratedExprSeq.Empty;
            }
            return classDef;
        }

        /// <summary>
        /// CPython: stmt_ty FunctionDefDecorators(Parser *p, asdl_expr_seq *decorators, stmt_ty function_def)
        /// </summary>
        public static GeneratedStmt FunctionDefDecorators(GeneratedExprSeq decorators, GeneratedStmt functionDef)
        {
            // Attach decorators to FunctionDef or AsyncFunctionDef
            if (functionDef is GeneratedFunctionDef fd)
            {
                fd.DecoratorList = decorators ?? GeneratedExprSeq.Empty;
            }
            else if (functionDef is GeneratedAsyncFunctionDef afd)
            {
                afd.DecoratorList = decorators ?? GeneratedExprSeq.Empty;
            }
            return functionDef;
        }

        /// <summary>
        /// CPython: KeyPatternPair *KeyPatternPair(Parser *p, expr_ty key, pattern_ty pattern)
        /// </summary>
        public static GeneratedKeyPatternPair KeyPatternPair(GeneratedExpr key, GeneratedPattern pattern)
        {
            return new GeneratedKeyPatternPair
            {
                Key = key,
                Pattern = pattern
            };
        }

        /// <summary>
        /// CPython: asdl_stmt_seq* InteractiveExit(Parser *p)
        /// Returns NULL to signal EOF in interactive mode
        /// </summary>
        public static GeneratedStmtSeq? InteractiveExit()
        {
            // CPython: Sets errcode = E_EOF and returns NULL
            // SharpPy: Just return null to signal EOF
            return null;
        }

        /// <summary>
        /// CPython: stmt_ty CheckedFutureImport(Parser *p, identifier module, asdl_alias_seq *names, int level,
        ///                                                  int lineno, int col_offset, int end_lineno, int end_col_offset, PyArena *arena)
        /// </summary>
        public static GeneratedStmt CheckedFutureImport(
            GeneratedIdentifier module,
            GeneratedAliasSeq names,
            int level,
            int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            // CPython: return _PyAST_ImportFrom(module, names, level, lineno, col_offset, end_lineno, end_col_offset, arena);
            return new GeneratedImportFrom
            {
                Module = module,
                Names = names ?? GeneratedAliasSeq.Empty,
                Level = level,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// CPython: arg_ty AddTypeCommentToArg(Parser *p, arg_ty arg, Token *tc)
        /// </summary>
        public static GeneratedArg AddTypeCommentToArg(GeneratedArg arg, GeneratedTokenInfo? typeComment)
        {
            if (arg != null && typeComment != null)
            {
                arg.TypeComment = typeComment.Value;
            }
            return arg;
        }

        /// <summary>
        /// CPython: expr_ty NonparenGenexpInCall(Parser *p, expr_ty args, comprehension_ty *comp)
        /// </summary>
        public static GeneratedExpr NonparenGenexpInCall(GeneratedExpr args, GeneratedSeq comprehensions)
        {
            // Create generator expression without parentheses
            var compSeq = new GeneratedComprehensionSeq();
            if (comprehensions != null)
            {
                foreach (var item in comprehensions)
                {
                    if (item is GeneratedComprehension comp)
                    {
                        compSeq.Add(comp);
                    }
                }
            }

            return new GeneratedGeneratorExp
            {
                Elt = args,
                Generators = compSeq
            };
        }

        /// <summary>
        /// CPython: KeywordOrStarred *KeywordOrStarred(Parser *p, void *element, int is_keyword)
        /// </summary>
        public static GeneratedKeywordOrStarred KeywordOrStarred(GeneratedPtr element, int is_keyword)
        {
            if (is_keyword == 1)
            {
                return new GeneratedKeywordOrStarred
                {
                    Keyword = (GeneratedKeyword)element,
                    Starred = null
                };
            }
            else
            {
                return new GeneratedKeywordOrStarred
                {
                    Keyword = null,
                    Starred = (GeneratedExpr)element
                };
            }
        }

        /// <summary>
        /// CPython: void *ArgumentsParsingError(Parser *p, expr_ty e)
        /// </summary>
        public static GeneratedPtr? ArgumentsParsingError(GeneratedExpr e)
        {
            // CPython: Raise syntax error
            throw new SyntaxErrorException($"invalid syntax in arguments: {e}");
        }

        /// <summary>
        /// CPython: asdl_expr_seq *GetPatternKeys(Parser *p, asdl_seq *seq)
        /// </summary>
        public static GeneratedExprSeq GetPatternKeys(GeneratedSeq seq)
        {
            var result = new GeneratedExprSeq(seq?.Count ?? 0);
            if (seq != null)
            {
                foreach (var item in seq)
                {
                    if (item is GeneratedKeyPatternPair pair)
                    {
                        result.Add(pair.Key);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// CPython: asdl_pattern_seq *GetPatterns(Parser *p, asdl_seq *seq)
        /// </summary>
        public static GeneratedPatternSeq GetPatterns(GeneratedSeq seq)
        {
            var result = new GeneratedPatternSeq(seq?.Count ?? 0);
            if (seq != null)
            {
                foreach (var item in seq)
                {
                    if (item is GeneratedKeyPatternPair pair)
                    {
                        result.Add(pair.Pattern);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// CPython: expr_ty NameToken(Parser *p)
        /// Converts NAME token to Name expression node
        /// </summary>
        public static GeneratedExpr NameToken(GeneratedTokenInfo token)
        {
            return new GeneratedName
            {
                Id = new GeneratedIdentifier(token.Value),
                Ctx = GeneratedLoad.Instance,
                LineNo = token.Line, ColOffset = token.Column, EndLineNo = token.EndLine, EndColOffset = token.EndColumn
            };
        }

        /// <summary>
        /// CPython: asdl_seq *SingletonSeq(Parser *p, void *a)
        /// Creates a single-element asdl_seq* that contains a
        /// </summary>
        public static GeneratedSeq SingletonSeq(GeneratedPtr item)
        {
            if (item == null) return null;
            var seq = new GeneratedSeq(1);
            seq.Add(item);
            return seq;
        }

        /// <summary>
        /// CPython: asdl_seq *SeqInsertInFront(Parser *p, void *a, asdl_seq *seq)
        /// Creates a copy of seq and prepends a to it
        /// </summary>
        public static GeneratedSeq SeqInsertInFront(GeneratedPtr item, GeneratedSeq seq)
        {
            if (item == null) return seq;
            if (seq == null) return SingletonSeq(item);

            var new_seq = new GeneratedSeq(seq.Count + 1);
            new_seq.Add(item);
            new_seq.AddRange(seq);
            return new_seq;
        }

        /// <summary>
        /// CPython: CmpopExprPair *CmpopExprPair(Parser *p, cmpop_ty cmpop, expr_ty expr)
        /// Constructs a CmpopExprPair
        /// </summary>
        public static GeneratedCmpopExprPair CmpopExprPair(GeneratedCmpop cmpop, GeneratedExpr expr)
        {
            if (expr == null) return null;
            return new GeneratedCmpopExprPair
            {
                Cmpop = cmpop,
                Expr = expr
            };
        }

        /// <summary>
        /// CPython: asdl_int_seq *GetCmpops(Parser *p, asdl_seq *seq)
        /// Extracts comparison operators from CmpopExprPair sequence
        /// </summary>
        public static GeneratedCmpopSeq GetCmpops(GeneratedSeq seq)
        {
            if (seq == null || seq.Count == 0) return new GeneratedCmpopSeq();

            var result = new GeneratedCmpopSeq(seq.Count);
            foreach (var item in seq)
            {
                if (item is GeneratedCmpopExprPair pair)
                {
                    result.Add(pair.Cmpop);
                }
            }
            return result;
        }

        /// <summary>
        /// CPython: asdl_expr_seq *GetExprs(Parser *p, asdl_seq *seq)
        /// Extracts expressions from CmpopExprPair sequence
        /// </summary>
        public static GeneratedExprSeq GetExprs(GeneratedSeq seq)
        {
            if (seq == null || seq.Count == 0) return new GeneratedExprSeq();

            var result = new GeneratedExprSeq(seq.Count);
            foreach (var item in seq)
            {
                if (item is GeneratedCmpopExprPair pair)
                {
                    result.Add(pair.Expr);
                }
            }
            return result;
        }

        /// <summary>
        /// CPython: int SeqCountDots(asdl_seq *seq)
        /// Counts the total number of dots in seq's tokens
        /// </summary>
        public static int SeqCountDots(GeneratedSeq seq)
        {
            int number_of_dots = 0;
            if (seq == null) return 0;

            foreach (var item in seq)
            {
                // CPython checks token type: ELLIPSIS = 3 dots, DOT = 1 dot
                // In SharpPy, we check if item is a token info
                if (item is GeneratedTokenInfo token)
                {
                    if (token.Type == PyToken.Type.ELLIPSIS || token.Value == "...")
                    {
                        number_of_dots += 3;
                    }
                    else if (token.Type == PyToken.Type.DOT || token.Value == ".")
                    {
                        number_of_dots += 1;
                    }
                }
            }
            return number_of_dots;
        }

        /// <summary>
        /// CPython: alias_ty AliasForStar(Parser *p, int lineno, int col_offset, int end_lineno, int end_col_offset, PyArena *arena)
        /// Creates an alias with '*' as the identifier name
        /// </summary>
        public static GeneratedAlias AliasForStar(int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            return new GeneratedAlias
            {
                Name = new GeneratedIdentifier("*"),
                Asname = null,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno ?? lineno,
                EndColOffset = end_col_offset ?? col_offset
            };
        }

        /// <summary>
        /// CPython: asdl_identifier_seq *MapNamesToIds(Parser *p, asdl_expr_seq *seq)
        /// Creates a new asdl_seq* with the identifiers of all the names in seq
        /// </summary>
        public static GeneratedIdentifierSeq MapNamesToIds(GeneratedExprSeq seq)
        {
#if DEBUG_PARSE_LOG
            Console.WriteLine($"[DEBUG] MapNamesToIds: seq is null? {seq == null}, count: {seq?.Count ?? 0}");
#endif
            if (seq == null || seq.Count == 0)
            {
#if DEBUG_PARSE_LOG
                Console.WriteLine($"[DEBUG] MapNamesToIds: Returning empty GeneratedIdentifierSeq");
#endif
                return new GeneratedIdentifierSeq();
            }

            var result = new GeneratedIdentifierSeq(seq.Count);
            foreach (var item in seq)
            {
#if DEBUG_PARSE_LOG
                Console.WriteLine($"[DEBUG] MapNamesToIds: item type = {item?.GetType().Name}, is GeneratedName? {item is GeneratedName}");
#endif
                if (item is GeneratedName name)
                {
#if DEBUG_PARSE_LOG
                    Console.WriteLine($"[DEBUG] MapNamesToIds: Adding identifier from GeneratedName '{name.Id.Value}'");
#endif
                    result.Add(name.Id);
                }
                else if (item is GeneratedTokenInfo token)
                {
                    // CPython: ','.NAME+ returns a sequence of NAME tokens
                    // We need to extract the identifier from each token
#if DEBUG_PARSE_LOG
                    Console.WriteLine($"[DEBUG] MapNamesToIds: Processing GeneratedTokenInfo, value='{token.Value}'");
#endif
                    result.Add(new GeneratedIdentifier(token.Value));
                }
            }
#if DEBUG_PARSE_LOG
            Console.WriteLine($"[DEBUG] MapNamesToIds: Returning {result.Count} identifiers");
#endif
            return result;
        }

        /// <summary>
        /// CPython: arguments_ty EmptyArguments(Parser *p)
        /// Constructs an empty arguments_ty object, that gets used when a function accepts no arguments
        /// </summary>
        public static GeneratedArguments EmptyArguments()
        {
            return new GeneratedArguments
            {
                Posonlyargs = new GeneratedArgSeq(),
                Args = new GeneratedArgSeq(),
                Vararg = null,
                Kwonlyargs = new GeneratedArgSeq(),
                KwDefaults = new GeneratedExprSeq(),
                Kwarg = null,
                Defaults = new GeneratedExprSeq()
            };
        }

        /// <summary>
        /// CPython: void *DummyName(Parser *p, ...)
        /// Returns a placeholder/dummy name for temporary use
        /// </summary>
        public static GeneratedName DummyName()
        {
            return new GeneratedName
            {
                Id = new GeneratedIdentifier("<dummy>"),
                Ctx = GeneratedLoad.Instance
            };
        }

        /// <summary>
        /// CPython: expr_ty SetExprContext(Parser *p, expr_ty expr, expr_context_ty ctx)
        /// Creates an `expr_ty` equivalent to `expr` but with `ctx` as context
        /// </summary>
        public static GeneratedExpr SetExprContext(GeneratedExpr expr, GeneratedExprContext ctx)
        {
            var result = SetExprContextNode(expr, ctx);
            if (result != null && expr != null)
            {
                result.LineNo = expr.LineNo;
                result.ColOffset = expr.ColOffset;
                result.EndLineNo = expr.EndLineNo;
                result.EndColOffset = expr.EndColOffset;
            }
            return result;
        }

        private static GeneratedExpr SetExprContextNode(GeneratedExpr expr, GeneratedExprContext ctx)
        {
            if (expr == null) return null;

            // CPython logic: Set context for Name, Tuple, List, Subscript, Attribute, Starred
            switch (expr)
            {
                case GeneratedName name:
                    return new GeneratedName { Id = name.Id, Ctx = ctx };

                case GeneratedTuple tuple:
                    if (tuple.Elts == null)
                        return new GeneratedTuple { Elts = null, Ctx = ctx, LineNo = tuple.LineNo, ColOffset = tuple.ColOffset, EndLineNo = tuple.EndLineNo, EndColOffset = tuple.EndColOffset };
                    var tuple_elts = new GeneratedExprSeq(tuple.Elts.Count);
                    foreach (var elt in tuple.Elts)
                    {
                        tuple_elts.Add(SetExprContext((GeneratedExpr)elt, ctx));
                    }
                    return new GeneratedTuple { Elts = tuple_elts, Ctx = ctx };

                case GeneratedList list:
                    if (list.Elts == null)
                        return new GeneratedList { Elts = null, Ctx = ctx, LineNo = list.LineNo, ColOffset = list.ColOffset, EndLineNo = list.EndLineNo, EndColOffset = list.EndColOffset };
                    var list_elts = new GeneratedExprSeq(list.Elts.Count);
                    foreach (var elt in list.Elts)
                    {
                        list_elts.Add(SetExprContext((GeneratedExpr)elt, ctx));
                    }
                    return new GeneratedList { Elts = list_elts, Ctx = ctx };

                case GeneratedSubscript subscript:
                    return new GeneratedSubscript
                    {
                        Value = subscript.Value,
                        Slice = subscript.Slice,
                        Ctx = ctx
                    };

                case GeneratedAttribute attribute:
                    return new GeneratedAttribute
                    {
                        Value = attribute.Value,
                        Attr = attribute.Attr,
                        Ctx = ctx
                    };

                case GeneratedStarred starred:
                    return new GeneratedStarred
                    {
                        Value = SetExprContext(starred.Value, ctx),
                        Ctx = ctx
                    };

                default:
                    return expr;
            }
        }

    }

    // Exception for syntax errors
    public class SyntaxErrorException : Exception
    {
        public SyntaxErrorException(string message) : base(message) { }
    }
}
