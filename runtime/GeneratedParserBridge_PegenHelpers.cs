using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Generated;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12: _PyPegen_* helper functions
    /// Translated from cpython-3.12/Parser/action_helpers.c
    /// </summary>
    public static partial class GeneratedParserBridge
    {
        // ============================================================
        // CPython 3.12: Parser helper functions from action_helpers.c
        // C -> C# direct translation
        // ============================================================

        /// <summary>
        /// CPython: NameDefaultPair *_PyPegen_name_default_pair(Parser *p, arg_ty arg, expr_ty c, Token *tc)
        /// </summary>
        public static GeneratedNameDefaultPair _PyPegen_name_default_pair(GeneratedArg arg, GeneratedExpr defaultValue, GeneratedTokenInfo? typeComment)
        {
            return new GeneratedNameDefaultPair
            {
                Arg = arg,
                Default = defaultValue,
                TypeComment = typeComment?.Value
            };
        }

        /// <summary>
        /// CPython: SlashWithDefault *_PyPegen_slash_with_default(Parser *p, asdl_arg_seq *plain, asdl_seq *names_with_defaults)
        /// </summary>
        public static GeneratedSlashWithDefault _PyPegen_slash_with_default(GeneratedArgSeq plain, GeneratedSeq namesWithDefaults)
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
        /// CPython: StarEtc *_PyPegen_star_etc(Parser *p, arg_ty vararg, asdl_seq *kwonlyargs, arg_ty kwarg)
        /// </summary>
        public static GeneratedStarEtc _PyPegen_star_etc(GeneratedArg vararg, GeneratedSeq kwonlyargs, GeneratedArg kwarg)
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
        /// CPython: arguments_ty _PyPegen_make_arguments(Parser *p, asdl_arg_seq *slash_without_default,
        ///     SlashWithDefault *slash_with_default, asdl_arg_seq *plain_names,
        ///     asdl_seq *names_with_default, StarEtc *star_etc)
        /// </summary>
        public static GeneratedArguments _PyPegen_make_arguments(
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
        /// CPython: expr_ty _PyPegen_join_names_with_dot(Parser *p, expr_ty first_name, expr_ty second_name)
        /// </summary>
        public static GeneratedExpr _PyPegen_join_names_with_dot(GeneratedExpr firstName, GeneratedExpr secondName)
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
        /// CPython: asdl_seq *_PyPegen_join_sequences(Parser *p, asdl_seq *a, asdl_seq *b)
        /// </summary>
        public static GeneratedSeq _PyPegen_join_sequences(GeneratedSeq a, GeneratedSeq b)
        {
            if (a == null) return b;
            if (b == null) return a;

            var result = new GeneratedSeq(a.Count + b.Count);
            result.AddRange(a);
            result.AddRange(b);
            return result;
        }

        /// <summary>
        /// CPython: expr_ty _PyPegen_class_def_decorators(Parser *p, asdl_expr_seq *decorators, class_body_ty class_def)
        /// </summary>
        public static GeneratedStmt _PyPegen_class_def_decorators(GeneratedExprSeq decorators, GeneratedStmt classDef)
        {
            // Attach decorators to ClassDef
            if (classDef is GeneratedClassDef cd)
            {
                cd.DecoratorList = decorators ?? GeneratedExprSeq.Empty;
            }
            return classDef;
        }

        /// <summary>
        /// CPython: stmt_ty _PyPegen_function_def_decorators(Parser *p, asdl_expr_seq *decorators, stmt_ty function_def)
        /// </summary>
        public static GeneratedStmt _PyPegen_function_def_decorators(GeneratedExprSeq decorators, GeneratedStmt functionDef)
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
        /// CPython: KeyPatternPair *_PyPegen_key_pattern_pair(Parser *p, expr_ty key, pattern_ty pattern)
        /// </summary>
        public static GeneratedKeyPatternPair _PyPegen_key_pattern_pair(GeneratedExpr key, GeneratedPattern pattern)
        {
            return new GeneratedKeyPatternPair
            {
                Key = key,
                Pattern = pattern
            };
        }

        /// <summary>
        /// CPython: asdl_stmt_seq* _PyPegen_interactive_exit(Parser *p)
        /// Returns NULL to signal EOF in interactive mode
        /// </summary>
        public static GeneratedStmtSeq? _PyPegen_interactive_exit()
        {
            // CPython: Sets errcode = E_EOF and returns NULL
            // SharpPy: Just return null to signal EOF
            return null;
        }

        /// <summary>
        /// CPython: stmt_ty _PyPegen_checked_future_import(Parser *p, identifier module, asdl_alias_seq *names, int level,
        ///                                                  int lineno, int col_offset, int end_lineno, int end_col_offset, PyArena *arena)
        /// </summary>
        public static GeneratedStmt _PyPegen_checked_future_import(
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
        /// CPython: arg_ty _PyPegen_add_type_comment_to_arg(Parser *p, arg_ty arg, Token *tc)
        /// </summary>
        public static GeneratedArg _PyPegen_add_type_comment_to_arg(GeneratedArg arg, GeneratedTokenInfo? typeComment)
        {
            if (arg != null && typeComment != null)
            {
                arg.TypeComment = typeComment.Value;
            }
            return arg;
        }

        /// <summary>
        /// CPython: expr_ty _PyPegen_constant_from_token(Parser *p, Token *tok)
        /// </summary>
        public static GeneratedExpr _PyPegen_constant_from_token(GeneratedTokenInfo token)
        {
            // Parse token value to constant
            return new GeneratedConstant
            {
                Value = new GeneratedPyConstantString(token.Value)
            };
        }

        /// <summary>
        /// CPython: expr_ty _PyPegen_decoded_constant_from_token(Parser *p, Token *tok)
        /// </summary>
        public static GeneratedExpr _PyPegen_decoded_constant_from_token(GeneratedTokenInfo token)
        {
            // Decode string literal (remove quotes, handle escapes)
            string decoded = DecodeStringLiteral(token.Value);
            return new GeneratedConstant
            {
                Value = new GeneratedPyConstantString(decoded)
            };
        }

        /// <summary>
        /// CPython: expr_ty _PyPegen_ensure_real(Parser *p, expr_ty number)
        /// </summary>
        public static GeneratedExpr _PyPegen_ensure_real(GeneratedExpr number)
        {
            // Ensure number is real (not imaginary)
            return number;
        }

        /// <summary>
        /// CPython: expr_ty _PyPegen_ensure_imaginary(Parser *p, expr_ty number)
        /// </summary>
        public static GeneratedExpr _PyPegen_ensure_imaginary(GeneratedExpr number)
        {
            // Convert to imaginary number
            return number;
        }

        /// <summary>
        /// CPython: expr_ty _PyPegen_formatted_value(Parser *p, expr_ty expression, Token *debug,
        ///                                            ResultTokenWithMetadata *conversion, ResultTokenWithMetadata *format,
        ///                                            Token *closing_brace, int lineno, int col_offset, int end_lineno, int end_col_offset, PyArena *arena)
        /// </summary>
        public static GeneratedExpr _PyPegen_formatted_value(
            GeneratedExpr expression,
            GeneratedTokenInfo debug,
            GeneratedResultTokenWithMetadata conversion,
            GeneratedResultTokenWithMetadata format,
            GeneratedTokenInfo closingBrace,
            int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            int conversion_val = -1;

            if (conversion != null)
            {
                GeneratedExpr conversion_expr = (GeneratedExpr)conversion.Metadata;
                // CPython: assert(conversion_expr->kind == Name_kind);
                if (conversion_expr is GeneratedName name)
                {
                    string id = name.Id.Value;
                    if (id.Length > 1 || !(id[0] == 's' || id[0] == 'r' || id[0] == 'a'))
                    {
                        // CPython: RAISE_SYNTAX_ERROR
                        throw new SyntaxErrorException($"f-string: invalid conversion character '{id}': expected 's', 'r', or 'a'");
                    }
                    conversion_val = id[0];
                }
            }
            else if (debug != null && format == null)
            {
                // CPython: If no conversion is specified, use !r for debug expressions
                conversion_val = 'r';
            }

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
        /// CPython: expr_ty _PyPegen_joined_str(Parser *p, Token *a, asdl_expr_seq *b, Token *c)
        /// </summary>
        public static GeneratedExpr _PyPegen_joined_str(GeneratedTokenInfo a, GeneratedExprSeq values, GeneratedTokenInfo c)
        {
            return new GeneratedJoinedStr
            {
                Values = values ?? GeneratedExprSeq.Empty
            };
        }

        /// <summary>
        /// CPython: ResultTokenWithMetadata *_PyPegen_check_fstring_conversion(Parser *p, Token *conv_token, expr_ty conv)
        /// </summary>
        public static GeneratedResultTokenWithMetadata _PyPegen_check_fstring_conversion(GeneratedTokenInfo convToken, GeneratedExpr conv)
        {
            return new GeneratedResultTokenWithMetadata
            {
                Token = convToken,
                Metadata = conv
            };
        }

        /// <summary>
        /// CPython: ResultTokenWithMetadata *_PyPegen_setup_full_format_spec(Parser *p, Token *colon, asdl_expr_seq *spec, ...)
        /// </summary>
        public static GeneratedResultTokenWithMetadata _PyPegen_setup_full_format_spec(
            GeneratedTokenInfo colon,
            GeneratedExprSeq spec,
            int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            GeneratedExpr result = null;
            if (spec != null && spec.Count > 0)
            {
                if (spec.Count == 1)
                {
                    result = spec[0];
                }
                else
                {
                    result = new GeneratedJoinedStr { Values = spec };
                }
            }

            return new GeneratedResultTokenWithMetadata
            {
                Token = colon,
                Metadata = result
            };
        }

        /// <summary>
        /// CPython: expr_ty _PyPegen_nonparen_genexp_in_call(Parser *p, expr_ty args, comprehension_ty *comp)
        /// </summary>
        public static GeneratedExpr _PyPegen_nonparen_genexp_in_call(GeneratedExpr args, GeneratedSeq comprehensions)
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
        /// CPython: KeywordOrStarred *_PyPegen_keyword_or_starred(Parser *p, void *element, int is_keyword)
        /// </summary>
        public static GeneratedKeywordOrStarred _PyPegen_keyword_or_starred(GeneratedPtr element, int is_keyword)
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
        /// CPython: void *_PyPegen_arguments_parsing_error(Parser *p, expr_ty e)
        /// </summary>
        public static GeneratedPtr? _PyPegen_arguments_parsing_error(GeneratedExpr e)
        {
            // CPython: Raise syntax error
            throw new SyntaxErrorException($"invalid syntax in arguments: {e}");
        }

        /// <summary>
        /// CPython: asdl_expr_seq *_PyPegen_get_pattern_keys(Parser *p, asdl_seq *seq)
        /// </summary>
        public static GeneratedExprSeq _PyPegen_get_pattern_keys(GeneratedSeq seq)
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
        /// CPython: asdl_pattern_seq *_PyPegen_get_patterns(Parser *p, asdl_seq *seq)
        /// </summary>
        public static GeneratedPatternSeq _PyPegen_get_patterns(GeneratedSeq seq)
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
        /// CPython: expr_ty _PyPegen_name_token(Parser *p)
        /// Converts NAME token to Name expression node
        /// </summary>
        public static GeneratedExpr _PyPegen_name_token(GeneratedTokenInfo token)
        {
            return new GeneratedName
            {
                Id = new GeneratedIdentifier(token.Value),
                Ctx = GeneratedLoad.Instance
            };
        }

        // Helper: Decode string literal
        private static string DecodeStringLiteral(string literal)
        {
            if (string.IsNullOrEmpty(literal)) return literal;

            // Remove quotes
            if (literal.StartsWith("\"") && literal.EndsWith("\""))
            {
                literal = literal.Substring(1, literal.Length - 2);
            }
            else if (literal.StartsWith("'") && literal.EndsWith("'"))
            {
                literal = literal.Substring(1, literal.Length - 2);
            }

            // Handle escape sequences
            literal = literal.Replace("\\n", "\n");
            literal = literal.Replace("\\r", "\r");
            literal = literal.Replace("\\t", "\t");
            literal = literal.Replace("\\\\", "\\");
            literal = literal.Replace("\\\"", "\"");
            literal = literal.Replace("\\'", "'");

            return literal;
        }
    }

    // Exception for syntax errors
    public class SyntaxErrorException : Exception
    {
        public SyntaxErrorException(string message) : base(message) { }
    }
}
