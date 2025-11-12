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
            // CPython: Check for __future__ import with barry_as_FLUFL
            if (level == 0 && module != null && module.Value == "__future__")
            {
                if (names != null)
                {
                    foreach (var item in names)
                    {
                        if (item is GeneratedAlias alias && alias.Name.Value == "barry_as_FLUFL")
                        {
                            // CPython: p->flags |= PyPARSE_BARRY_AS_BDFL;
                            // SharpPy: Parser flags handling (currently ignored)
                        }
                    }
                }
            }

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
        /// CPython: expr_ty _PyPegen_constant_from_token(Parser *p, Token *tok)
        /// Used for FSTRING_MIDDLE tokens - needs escape processing
        /// CPython reference: Parser/string_parser.c:1491-1503 (fstring_decode_literal)
        /// </summary>
        public static GeneratedExpr ConstantFromToken(GeneratedTokenInfo token)
        {
            // CPython: F-string middle parts need escape sequence processing
            // The tokenizer extracts the literal text, but doesn't process escapes
            // We must call ProcessEscapeSequences() to handle \n, \t, \x41, etc.
            string processed = ProcessEscapeSequences(token.Value);
            return new GeneratedConstant
            {
                Value = new GeneratedPyConstantString(processed)
            };
        }

        /// <summary>
        /// CPython: expr_ty _PyPegen_constant_from_string(Parser *p, Token *tok)
        /// This function DOES decode the token value (removes quotes, processes escapes)
        /// Used for STRING tokens that need decoding
        /// </summary>
        public static GeneratedExpr DecodedConstantFromToken(GeneratedTokenInfo token)
        {
            // Check if it's a bytes literal by looking at the prefix
            bool isBytes = token.Value.Length > 0 &&
                          (token.Value[0] == 'b' || token.Value[0] == 'B');

            if (isBytes)
            {
                // CPython: bytes literals need special handling - escape sequences produce raw bytes
                // CPython reference: Parser/string_parser.c:650-850 (_PyPegen_decode_bytes_with_escapes)
                byte[] bytes = DecodeBytesLiteral(token.Value);
                return new GeneratedConstant
                {
                    Value = new GeneratedPyConstantBytes(bytes)
                };
            }
            else
            {
                // CPython: Calls _PyPegen_parse_string to decode quotes and escapes
                string decoded = DecodeStringLiteral(token.Value);
                return new GeneratedConstant
                {
                    Value = new GeneratedPyConstantString(decoded)
                };
            }
        }

        /// <summary>
        /// CPython: expr_ty EnsureReal(Parser *p, expr_ty number)
        /// </summary>
        public static GeneratedExpr EnsureReal(GeneratedExpr number)
        {
            // Ensure number is real (not imaginary)
            return number;
        }

        /// <summary>
        /// CPython: expr_ty EnsureImaginary(Parser *p, expr_ty number)
        /// </summary>
        public static GeneratedExpr EnsureImaginary(GeneratedExpr number)
        {
            // Convert to imaginary number
            return number;
        }

        /// <summary>
        /// CPython: expr_ty FormattedValue(Parser *p, expr_ty expression, Token *debug,
        ///                                            ResultTokenWithMetadata *conversion, ResultTokenWithMetadata *format,
        ///                                            Token *closing_brace, int lineno, int col_offset, int end_lineno, int end_col_offset, PyArena *arena)
        /// </summary>
        public static GeneratedExpr FormattedValue(
            GeneratedExpr expression,
            GeneratedTokenInfo debug,
            GeneratedResultTokenWithMetadata conversion,
            GeneratedResultTokenWithMetadata format,
            GeneratedTokenInfo closingBrace,
            int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            int conversion_val = -1;

#if DEBUG_FSTRING_LOG
            Console.WriteLine($"[FSTRING-FORMATTED_VALUE] FormattedValue called:");
            Console.WriteLine($"  conversion is null: {conversion == null}");
            if (conversion != null)
            {
                Console.WriteLine($"  conversion.GetType(): {conversion.GetType().Name}");
                Console.WriteLine($"  conversion.Metadata: {conversion.Metadata}");
                if (conversion.Metadata != null)
                {
                    Console.WriteLine($"  conversion.Metadata.GetType(): {conversion.Metadata.GetType().Name}");
                }
            }
#endif

            if (conversion != null)
            {
                GeneratedExpr conversion_expr = (GeneratedExpr)conversion.Metadata;
                // CPython: assert(conversion_expr->kind == Name_kind);
                if (conversion_expr is GeneratedName name)
                {
                    string id = name.Id.Value;
#if DEBUG_FSTRING_LOG
                    Console.WriteLine($"  conversion NAME id: '{id}'");
                    Console.WriteLine($"  conversion char code: {(int)id[0]}");
#endif
                    if (id.Length > 1 || !(id[0] == 's' || id[0] == 'r' || id[0] == 'a'))
                    {
                        // CPython: RAISE_SYNTAX_ERROR
                        throw new SyntaxErrorException($"f-string: invalid conversion character '{id}': expected 's', 'r', or 'a'");
                    }
                    conversion_val = id[0];
#if DEBUG_FSTRING_LOG
                    Console.WriteLine($"  conversion_val set to: {conversion_val} ('{(char)conversion_val}')");
#endif
                }
            }
            else if (debug != null && format == null)
            {
                // CPython: If no conversion is specified, use !r for debug expressions
                conversion_val = 'r';
            }

#if DEBUG_FSTRING_LOG
            Console.WriteLine($"  Final conversion_val: {conversion_val}");
#endif

            GeneratedExpr formatted_value = new GeneratedFormattedValue
            {
                Value = expression,
                Conversion = conversion_val,
                FormatSpec = format != null ? (GeneratedExpr)format.Metadata : null,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };

            if (debug != null)
            {
                // CPython: Find the non whitespace token after the "="
                int debug_end_line, debug_end_offset;
                GeneratedPtr debug_metadata;

                if (conversion != null)
                {
                    GeneratedExpr conv_expr = (GeneratedExpr)conversion.Metadata;
                    debug_end_line = conv_expr.LineNo;
                    debug_end_offset = conv_expr.ColOffset;
                    debug_metadata = conversion.Token;
                }
                else if (format != null)
                {
                    GeneratedExpr fmt_expr = (GeneratedExpr)format.Metadata;
                    debug_end_line = fmt_expr.LineNo;
                    debug_end_offset = fmt_expr.ColOffset + 1;
                    debug_metadata = format.Token;
                }
                else
                {
                    debug_end_line = end_lineno;
                    debug_end_offset = end_col_offset;
                    debug_metadata = closingBrace;
                }

                GeneratedExpr debug_text = new GeneratedConstant
                {
                    Value = new GeneratedPyConstantString(debug_metadata?.ToString() ?? ""),
                    LineNo = lineno,
                    ColOffset = col_offset + 1,
                    EndLineNo = debug_end_line,
                    EndColOffset = debug_end_offset - 1
                };

                var values = new GeneratedExprSeq(2);
                values.Add(debug_text);
                values.Add(formatted_value);

                return new GeneratedJoinedStr
                {
                    Values = values,
                    LineNo = lineno,
                    ColOffset = col_offset,
                    EndLineNo = debug_end_line,
                    EndColOffset = debug_end_offset
                };
            }
            else
            {
                return formatted_value;
            }
        }

        /// <summary>
        /// CPython: expr_ty JoinedStr(Parser *p, Token *a, asdl_expr_seq *b, Token *c)
        /// </summary>
        public static GeneratedExpr JoinedStr(GeneratedTokenInfo a, GeneratedExprSeq values, GeneratedTokenInfo c)
        {
            return new GeneratedJoinedStr
            {
                Values = values ?? GeneratedExprSeq.Empty
            };
        }

        /// <summary>
        /// CPython: ResultTokenWithMetadata *CheckFstringConversion(Parser *p, Token *conv_token, expr_ty conv)
        /// </summary>
        public static GeneratedResultTokenWithMetadata CheckFstringConversion(GeneratedTokenInfo convToken, GeneratedExpr conv)
        {
#if DEBUG_FSTRING_LOG
            Console.WriteLine($"[FSTRING-CHECK_CONVERSION] CheckFstringConversion called:");
            Console.WriteLine($"  convToken: {convToken?.Type}:'{convToken?.Value}'");
            Console.WriteLine($"  conv is null: {conv == null}");
            if (conv != null)
            {
                Console.WriteLine($"  conv.GetType(): {conv.GetType().Name}");
                if (conv is GeneratedName name)
                {
                    Console.WriteLine($"  conv NAME id: '{name.Id.Value}'");
                }
            }
#endif
            return new GeneratedResultTokenWithMetadata
            {
                Token = convToken,
                Metadata = conv
            };
        }

        /// <summary>
        /// CPython: ResultTokenWithMetadata *SetupFullFormatSpec(Parser *p, Token *colon, asdl_expr_seq *spec, ...)
        /// </summary>
        // Updated to return GeneratedExpr? to match new grammar return type
        // Changed from GeneratedResultTokenWithMetadata to GeneratedExpr?
        public static GeneratedExpr? SetupFullFormatSpec(
            GeneratedTokenInfo? colon,
            GeneratedExprSeq? spec,
            int lineno, int col_offset, int? end_lineno, int? end_col_offset)
        {
            if (colon == null) return null;
            if (spec == null || spec.Count == 0)
            {
                return PyAst.Constant(new GeneratedPyConstantString(""), null, lineno, col_offset, end_lineno, end_col_offset);
            }

            // CPython: format_spec is ALWAYS a JoinedStr, even for simple constants
            // This matches CPython's AST structure
            return PyAst.JoinedStr(spec, lineno, col_offset, end_lineno, end_col_offset);
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
                Ctx = GeneratedLoad.Instance
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
            if (expr == null) return null;

            // CPython logic: Set context for Name, Tuple, List, Subscript, Attribute, Starred
            switch (expr)
            {
                case GeneratedName name:
                    return new GeneratedName { Id = name.Id, Ctx = ctx };

                case GeneratedTuple tuple:
                    var tuple_elts = new GeneratedExprSeq(tuple.Elts.Count);
                    foreach (var elt in tuple.Elts)
                    {
                        tuple_elts.Add(SetExprContext((GeneratedExpr)elt, ctx));
                    }
                    return new GeneratedTuple { Elts = tuple_elts, Ctx = ctx };

                case GeneratedList list:
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

        /// <summary>
        /// Decode bytes literal with escape sequences directly to byte array
        /// CPython reference: Parser/string_parser.c:650-850 (_PyPegen_decode_bytes_with_escapes)
        /// </summary>
        private static byte[] DecodeBytesLiteral(string s)
        {
            bool rawmode = false;
            int startIdx = 0;

            // Skip prefix (b, B, br, Br, BR, bR, rb, rB, Rb, RB)
            while (startIdx < s.Length)
            {
                char c = s[startIdx];
                if (c == 'b' || c == 'B')
                {
                    startIdx++;
                }
                else if (c == 'r' || c == 'R')
                {
                    rawmode = true;
                    startIdx++;
                }
                else if (c == 'f' || c == 'F')
                {
                    startIdx++;
                }
                else
                {
                    break;
                }
            }

            if (startIdx >= s.Length) return new byte[0];

            // Determine quote character
            char quote = s[startIdx];
            if (quote != '\'' && quote != '\"')
            {
                return new byte[0];
            }

            startIdx++;
            int endIdx = s.Length;

            // Find trailing quote
            if (endIdx > 0 && s[endIdx - 1] == quote)
            {
                endIdx--;
            }
            else
            {
                return new byte[0];
            }

            // Handle triple quotes
            if (endIdx - startIdx >= 4 &&
                startIdx + 1 < s.Length && s[startIdx] == quote && s[startIdx + 1] == quote)
            {
                startIdx += 2;
                if (endIdx >= 2 && s[endIdx - 1] == quote && s[endIdx - 2] == quote)
                {
                    endIdx -= 2;
                }
            }

            if (endIdx < startIdx)
            {
                return new byte[0];
            }

            string literal = s.Substring(startIdx, endIdx - startIdx);

            // Process escape sequences to byte array
            if (rawmode)
            {
                // Raw mode: no escape processing, ASCII encoding only
                return System.Text.Encoding.ASCII.GetBytes(literal);
            }
            else
            {
                // Process escape sequences and build byte array
                return ProcessBytesEscapeSequences(literal);
            }
        }

        /// <summary>
        /// Process escape sequences in bytes literals directly to byte array
        /// CPython reference: Parser/string_parser.c:650-850
        /// Key difference from string escapes: \xff produces byte 0xFF, not UTF-8 encoded char
        /// </summary>
        private static byte[] ProcessBytesEscapeSequences(string input)
        {
            if (string.IsNullOrEmpty(input)) return new byte[0];

            var result = new System.Collections.Generic.List<byte>();
            int i = 0;

            while (i < input.Length)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    char next = input[i + 1];

                    switch (next)
                    {
                        // Basic escape sequences
                        case '\\':
                            result.Add((byte)'\\');
                            i += 2;
                            break;
                        case '\'':
                            result.Add((byte)'\'');
                            i += 2;
                            break;
                        case '\"':
                            result.Add((byte)'\"');
                            i += 2;
                            break;
                        case 'a':
                            result.Add(0x07);  // Bell
                            i += 2;
                            break;
                        case 'b':
                            result.Add(0x08);  // Backspace
                            i += 2;
                            break;
                        case 'f':
                            result.Add(0x0C);  // Form feed
                            i += 2;
                            break;
                        case 'n':
                            result.Add(0x0A);  // Newline
                            i += 2;
                            break;
                        case 'r':
                            result.Add(0x0D);  // Carriage return
                            i += 2;
                            break;
                        case 't':
                            result.Add(0x09);  // Tab
                            i += 2;
                            break;
                        case 'v':
                            result.Add(0x0B);  // Vertical tab
                            i += 2;
                            break;

                        // Hex escape: \xHH (exactly 2 hex digits) - CRITICAL for bytes
                        case 'x':
                            if (i + 3 < input.Length)
                            {
                                string hexStr = input.Substring(i + 2, 2);
                                if (IsHexDigits(hexStr, 2))
                                {
                                    int value = Convert.ToInt32(hexStr, 16);
                                    result.Add((byte)value);  // Direct byte, not UTF-8 encoded
                                    i += 4;
                                    break;
                                }
                            }
                            // Invalid hex escape: keep backslash
                            result.Add((byte)'\\');
                            i++;
                            break;

                        // Octal escape: \0-\377 (up to 3 octal digits)
                        case '0': case '1': case '2': case '3':
                        case '4': case '5': case '6': case '7':
                            {
                                int octalValue = 0;
                                int octalDigits = 0;
                                int j = i + 1;

                                while (j < input.Length && octalDigits < 3 &&
                                       input[j] >= '0' && input[j] <= '7')
                                {
                                    octalValue = octalValue * 8 + (input[j] - '0');
                                    octalDigits++;
                                    j++;

                                    if (octalValue > 255)
                                    {
                                        octalValue /= 8;
                                        octalDigits--;
                                        j--;
                                        break;
                                    }
                                }

                                if (octalDigits > 0)
                                {
                                    result.Add((byte)octalValue);
                                    i = j;
                                }
                                else
                                {
                                    result.Add((byte)'\\');
                                    i++;
                                }
                            }
                            break;

                        // Invalid escape: keep backslash
                        default:
                            result.Add((byte)'\\');
                            i++;
                            break;
                    }
                }
                else
                {
                    // Regular character: only ASCII allowed in bytes literals
                    byte b = (byte)input[i];
                    result.Add(b);
                    i++;
                }
            }

            return result.ToArray();
        }

        // Helper: Process escape sequences in string literals
        // CPython reference: Objects/unicodeobject.c:6049-6300 (_PyUnicode_DecodeUnicodeEscapeInternal2)
        private static string ProcessEscapeSequences(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = new System.Text.StringBuilder(input.Length);
            int i = 0;

            while (i < input.Length)
            {
                if (input[i] == '\\' && i + 1 < input.Length)
                {
                    char next = input[i + 1];

                    switch (next)
                    {
                        // Basic escape sequences
                        case '\\':
                            result.Append('\\');
                            i += 2;
                            break;
                        case '\'':
                            result.Append('\'');
                            i += 2;
                            break;
                        case '\"':
                            result.Append('\"');
                            i += 2;
                            break;
                        case 'a':
                            result.Append('\a');  // Bell (0x07)
                            i += 2;
                            break;
                        case 'b':
                            result.Append('\b');  // Backspace (0x08)
                            i += 2;
                            break;
                        case 'f':
                            result.Append('\f');  // Form feed (0x0C)
                            i += 2;
                            break;
                        case 'n':
                            result.Append('\n');  // Newline (0x0A)
                            i += 2;
                            break;
                        case 'r':
                            result.Append('\r');  // Carriage return (0x0D)
                            i += 2;
                            break;
                        case 't':
                            result.Append('\t');  // Tab (0x09)
                            i += 2;
                            break;
                        case 'v':
                            result.Append('\v');  // Vertical tab (0x0B)
                            i += 2;
                            break;

                        // Hex escape: \xHH (exactly 2 hex digits)
                        case 'x':
                            if (i + 3 < input.Length)
                            {
                                string hexStr = input.Substring(i + 2, 2);
                                if (IsHexDigits(hexStr, 2))
                                {
                                    int value = Convert.ToInt32(hexStr, 16);
                                    result.Append((char)value);
                                    i += 4;
                                    break;
                                }
                            }
                            // Invalid hex escape: keep as-is
                            result.Append('\\');
                            i++;
                            break;

                        // Unicode escape: \uHHHH (exactly 4 hex digits)
                        case 'u':
                            if (i + 5 < input.Length)
                            {
                                string hexStr = input.Substring(i + 2, 4);
                                if (IsHexDigits(hexStr, 4))
                                {
                                    int value = Convert.ToInt32(hexStr, 16);
                                    result.Append((char)value);
                                    i += 6;
                                    break;
                                }
                            }
                            // Invalid unicode escape: keep as-is
                            result.Append('\\');
                            i++;
                            break;

                        // Unicode escape: \UHHHHHHHH (exactly 8 hex digits)
                        case 'U':
                            if (i + 9 < input.Length)
                            {
                                string hexStr = input.Substring(i + 2, 8);
                                if (IsHexDigits(hexStr, 8))
                                {
                                    int value = Convert.ToInt32(hexStr, 16);
                                    if (value <= 0x10FFFF)  // Valid Unicode range
                                    {
                                        result.Append(char.ConvertFromUtf32(value));
                                        i += 10;
                                        break;
                                    }
                                }
                            }
                            // Invalid unicode escape: keep as-is
                            result.Append('\\');
                            i++;
                            break;

                        // Octal escape: \0-\377 (up to 3 octal digits)
                        case '0': case '1': case '2': case '3':
                        case '4': case '5': case '6': case '7':
                            {
                                int octalValue = 0;
                                int octalDigits = 0;
                                int j = i + 1;

                                // Read up to 3 octal digits
                                while (j < input.Length && octalDigits < 3 &&
                                       input[j] >= '0' && input[j] <= '7')
                                {
                                    octalValue = octalValue * 8 + (input[j] - '0');
                                    octalDigits++;
                                    j++;

                                    // Stop if value would exceed 255 (0377 octal)
                                    if (octalValue > 255)
                                    {
                                        // Backtrack one digit
                                        octalValue /= 8;
                                        octalDigits--;
                                        j--;
                                        break;
                                    }
                                }

                                if (octalDigits > 0)
                                {
                                    result.Append((char)octalValue);
                                    i = j;
                                }
                                else
                                {
                                    // Should never happen
                                    result.Append('\\');
                                    i++;
                                }
                            }
                            break;

                        // Named Unicode escape: \N{name}
                        // TODO: This requires a Unicode name database, skipping for now
                        case 'N':
                            // For now, keep as-is
                            result.Append('\\');
                            i++;
                            break;

                        // Unknown escape: keep the backslash
                        default:
                            result.Append('\\');
                            i++;
                            break;
                    }
                }
                else
                {
                    // Regular character
                    result.Append(input[i]);
                    i++;
                }
            }

            return result.ToString();
        }

        // Helper: Check if string contains only hex digits
        private static bool IsHexDigits(string s, int expectedLength)
        {
            if (s.Length != expectedLength) return false;

            foreach (char c in s)
            {
                if (!((c >= '0' && c <= '9') ||
                      (c >= 'a' && c <= 'f') ||
                      (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        // Helper: Decode string literal
        // CPython: _PyPegen_parse_string in Parser/string_parser.c:239-302
        private static string DecodeStringLiteral(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            int startIdx = 0;
            bool bytesmode = false;
            bool rawmode = false;

            // CPython: Skip prefix (r, R, b, B, u, U) - string_parser.c:251-268
            while (startIdx < s.Length && char.IsLetter(s[startIdx]))
            {
                char prefix = char.ToLower(s[startIdx]);
                if (prefix == 'b')
                {
                    bytesmode = true;
                    startIdx++;
                }
                else if (prefix == 'u')
                {
                    startIdx++;  // Just skip 'u' prefix
                }
                else if (prefix == 'r')
                {
                    rawmode = true;
                    startIdx++;
                }
                else if (prefix == 'f')
                {
                    // F-strings are handled separately, but skip if present
                    startIdx++;
                }
                else
                {
                    break;
                }
            }

            if (startIdx >= s.Length) return "";

            // CPython: Determine quote character - string_parser.c:270-273
            char quote = s[startIdx];
            if (quote != '\'' && quote != '\"')
            {
                return s;  // Invalid string literal
            }

            // CPython: Skip leading quote - string_parser.c:275-276
            startIdx++;
            int endIdx = s.Length;

            // CPython: Find trailing quote - string_parser.c:286
            if (endIdx > 0 && s[endIdx - 1] == quote)
            {
                endIdx--;
            }
            else
            {
                return s;  // Invalid string literal
            }

            // CPython: Handle triple quotes - string_parser.c:291-302
            if (endIdx - startIdx >= 4 &&
                startIdx + 1 < s.Length && s[startIdx] == quote && s[startIdx + 1] == quote)
            {
                // Triple quoted string: skip two more quotes at start
                startIdx += 2;

                // Check and skip two more quotes at end
                if (endIdx >= 2 && s[endIdx - 1] == quote && s[endIdx - 2] == quote)
                {
                    endIdx -= 2;
                }
            }

            // Extract string content
            // Safety check: ensure we don't have negative length
            if (endIdx < startIdx)
            {
                // This shouldn't happen with valid string literals
                // If it does, return empty string instead of crashing
                return "";
            }
            string literal = s.Substring(startIdx, endIdx - startIdx);

            // CPython: Handle escape sequences (only if not raw mode)
            // CPython reference: Objects/unicodeobject.c:6049-6300 (_PyUnicode_DecodeUnicodeEscapeInternal2)
            if (!rawmode)
            {
                literal = ProcessEscapeSequences(literal);
            }

            return literal;
        }

    }

    // Exception for syntax errors
    public class SyntaxErrorException : Exception
    {
        public SyntaxErrorException(string message) : base(message) { }
    }
}
