using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Generated;

namespace SharpPy.Generated
{
    /// <summary>
    /// Function-related conversion methods for PyParserRuntime
    /// Handles function definitions, parameters, defaults, decorators
    /// </summary>
    public static partial class PyParserRuntime
    {
        /// <summary>
        /// Convert GeneratedArguments to CPython 3.12 compatible FunctionArguments
        /// </summary>
        private static FunctionArguments ConvertFunctionArguments(GeneratedArguments? argumentsData)
        {
            var functionArgs = new FunctionArguments();

            if (argumentsData == null)
            {
#if DEBUG_AST_LOG
                Console.WriteLine("[DEBUG] ConvertFunctionArguments: argumentsData is null");
#endif
                return functionArgs;
            }

#if DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ConvertFunctionArguments: Processing GeneratedArguments");
#endif

            // Process posonlyargs (positional-only parameters before /)
            if (argumentsData.Posonlyargs != null && argumentsData.Posonlyargs.Count > 0)
            {
#if DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] Found {argumentsData.Posonlyargs.Count} posonlyargs");
#endif
                foreach (var argPtr in argumentsData.Posonlyargs.ToEnumerable<GeneratedArg>())
                {
                    // CPython 3.12: Convert annotation if present
                    Expression? annotation = null;
                    if (argPtr.Annotation != null)
                    {
                        annotation = ConvertAnyExpression(argPtr.Annotation);
                    }
                    functionArgs.PosOnlyArgs.Add(new Arg(argPtr.Arg.Value, annotation));
#if DEBUG_AST_LOG
                    Console.WriteLine($"[DEBUG] Added posonly arg: {argPtr.Arg.Value}, annotation: {annotation}");
#endif
                }
            }

            // Process args (regular positional or positional-or-keyword parameters)
            if (argumentsData.Args != null && argumentsData.Args.Count > 0)
            {
#if DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] Found {argumentsData.Args.Count} args");
#endif
                foreach (var argPtr in argumentsData.Args.ToEnumerable<GeneratedArg>())
                {
                    // CPython 3.12: Convert annotation if present
                    Expression? annotation = null;
                    if (argPtr.Annotation != null)
                    {
                        annotation = ConvertAnyExpression(argPtr.Annotation);
                    }
                    functionArgs.Args.Add(new Arg(argPtr.Arg.Value, annotation));
#if DEBUG_AST_LOG
                    Console.WriteLine($"[DEBUG] Added regular arg: {argPtr.Arg.Value}, annotation: {annotation}");
#endif
                }
            }

            // Process vararg (*args)
            if (argumentsData.Vararg != null)
            {
                // CPython 3.12: Convert annotation if present
                Expression? annotation = null;
                if (argumentsData.Vararg.Annotation != null)
                {
                    annotation = ConvertAnyExpression(argumentsData.Vararg.Annotation);
                }
                functionArgs.VarArg = new Arg(argumentsData.Vararg.Arg.Value, annotation);
#if DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] Added vararg: *{argumentsData.Vararg.Arg.Value}, annotation: {annotation}");
#endif
            }

            // Process kwonlyargs (keyword-only parameters after *)
            if (argumentsData.Kwonlyargs != null && argumentsData.Kwonlyargs.Count > 0)
            {
#if DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] Found {argumentsData.Kwonlyargs.Count} kwonlyargs");
#endif
                foreach (var argPtr in argumentsData.Kwonlyargs.ToEnumerable<GeneratedArg>())
                {
                    // CPython 3.12: Convert annotation if present
                    Expression? annotation = null;
                    if (argPtr.Annotation != null)
                    {
                        annotation = ConvertAnyExpression(argPtr.Annotation);
                    }
                    functionArgs.KwOnlyArgs.Add(new Arg(argPtr.Arg.Value, annotation));
#if DEBUG_AST_LOG
                    Console.WriteLine($"[DEBUG] Added kwonly arg: {argPtr.Arg.Value}, annotation: {annotation}");
#endif
                }
            }

            // Process kwarg (**kwargs)
            if (argumentsData.Kwarg != null)
            {
                // CPython 3.12: Convert annotation if present
                Expression? annotation = null;
                if (argumentsData.Kwarg.Annotation != null)
                {
                    annotation = ConvertAnyExpression(argumentsData.Kwarg.Annotation);
                }
                functionArgs.KwArg = new Arg(argumentsData.Kwarg.Arg.Value, annotation);
#if DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] Added kwarg: **{argumentsData.Kwarg.Arg.Value}, annotation: {annotation}");
#endif
            }

            // Process defaults (default values for regular args)
            // CPython 3.12: defaults align with the LAST len(defaults) parameters in args
            if (argumentsData.Defaults != null && argumentsData.Defaults.Count > 0)
            {
#if DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] Found {argumentsData.Defaults.Count} defaults");
#endif
                foreach (var defaultExpr in argumentsData.Defaults.ToEnumerable<GeneratedExpr>())
                {
                    if (defaultExpr != null)
                    {
#if DEBUG_AST_LOG
                        Console.WriteLine($"[DEBUG] Processing default: {defaultExpr.GetType().Name}");
#endif
                        var convertedDefault = ConvertAnyExpression(defaultExpr);
                        if (convertedDefault != null)
                        {
                            functionArgs.Defaults.Add(convertedDefault);
#if DEBUG_AST_LOG
                            Console.WriteLine($"[DEBUG] Added default value: {convertedDefault}");
#endif
                        }
                    }
                }
            }

            // Process kw_defaults (default values for keyword-only args)
            if (argumentsData.KwDefaults != null && argumentsData.KwDefaults.Count > 0)
            {
#if DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] Found {argumentsData.KwDefaults.Count} kw_defaults");
#endif
                foreach (var defaultExpr in argumentsData.KwDefaults.ToEnumerable<GeneratedExpr>())
                {
                    if (defaultExpr != null)
                    {
                        var convertedDefault = ConvertAnyExpression(defaultExpr);
                        functionArgs.KwDefaults.Add(convertedDefault);
                    }
                    else
                    {
                        functionArgs.KwDefaults.Add(null);
                    }
                }
            }

#if DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] Created FunctionArguments: {functionArgs}");
#endif
            return functionArgs;
        }

        // Note: ConvertDefaultToString is already defined in PyParserRuntime.cs
        // We don't duplicate it here
    }
}
