using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Generated;

namespace SharpPy
{
    /// <summary>
    /// Function-related conversion methods for GeneratedParserBridge
    /// Handles function definitions, parameters, defaults, decorators
    /// </summary>
    public static partial class GeneratedParserBridge
    {
        /// <summary>
        /// Convert GeneratedArguments to CPython 3.12 compatible FunctionArguments
        /// </summary>
        private static FunctionArguments ConvertFunctionArguments(GeneratedArguments? argumentsData)
        {
            var functionArgs = new FunctionArguments();

            if (argumentsData == null)
            {
                Console.WriteLine("[DEBUG] ConvertFunctionArguments: argumentsData is null");
                return functionArgs;
            }

            Console.WriteLine($"[DEBUG] ConvertFunctionArguments: Processing GeneratedArguments");

            // Process posonlyargs (positional-only parameters before /)
            if (argumentsData.Posonlyargs != null && argumentsData.Posonlyargs.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Found {argumentsData.Posonlyargs.Count} posonlyargs");
                foreach (var argPtr in argumentsData.Posonlyargs.ToEnumerable<GeneratedArg>())
                {
                    functionArgs.PosOnlyArgs.Add(new Arg(argPtr.Arg.Value));
                    Console.WriteLine($"[DEBUG] Added posonly arg: {argPtr.Arg.Value}");
                }
            }

            // Process args (regular positional or positional-or-keyword parameters)
            if (argumentsData.Args != null && argumentsData.Args.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Found {argumentsData.Args.Count} args");
                foreach (var argPtr in argumentsData.Args.ToEnumerable<GeneratedArg>())
                {
                    functionArgs.Args.Add(new Arg(argPtr.Arg.Value));
                    Console.WriteLine($"[DEBUG] Added regular arg: {argPtr.Arg.Value}");
                }
            }

            // Process vararg (*args)
            if (argumentsData.Vararg != null)
            {
                functionArgs.VarArg = new Arg(argumentsData.Vararg.Arg.Value);
                Console.WriteLine($"[DEBUG] Added vararg: *{argumentsData.Vararg.Arg.Value}");
            }

            // Process kwonlyargs (keyword-only parameters after *)
            if (argumentsData.Kwonlyargs != null && argumentsData.Kwonlyargs.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Found {argumentsData.Kwonlyargs.Count} kwonlyargs");
                foreach (var argPtr in argumentsData.Kwonlyargs.ToEnumerable<GeneratedArg>())
                {
                    functionArgs.KwOnlyArgs.Add(new Arg(argPtr.Arg.Value));
                    Console.WriteLine($"[DEBUG] Added kwonly arg: {argPtr.Arg.Value}");
                }
            }

            // Process kwarg (**kwargs)
            if (argumentsData.Kwarg != null)
            {
                functionArgs.KwArg = new Arg(argumentsData.Kwarg.Arg.Value);
                Console.WriteLine($"[DEBUG] Added kwarg: **{argumentsData.Kwarg.Arg.Value}");
            }

            // Process defaults (default values for regular args)
            // CPython 3.12: defaults align with the LAST len(defaults) parameters in args
            if (argumentsData.Defaults != null && argumentsData.Defaults.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Found {argumentsData.Defaults.Count} defaults");
                foreach (var defaultExpr in argumentsData.Defaults.ToEnumerable<GeneratedExpr>())
                {
                    if (defaultExpr != null)
                    {
                        Console.WriteLine($"[DEBUG] Processing default: {defaultExpr.GetType().Name}");
                        var convertedDefault = ConvertAnyExpression(defaultExpr);
                        if (convertedDefault != null)
                        {
                            functionArgs.Defaults.Add(convertedDefault);
                            Console.WriteLine($"[DEBUG] Added default value: {convertedDefault}");
                        }
                    }
                }
            }

            // Process kw_defaults (default values for keyword-only args)
            if (argumentsData.KwDefaults != null && argumentsData.KwDefaults.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Found {argumentsData.KwDefaults.Count} kw_defaults");
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

            Console.WriteLine($"[DEBUG] Created FunctionArguments: {functionArgs}");
            return functionArgs;
        }

        // Note: ConvertDefaultToString is already defined in GeneratedParserBridge.cs
        // We don't duplicate it here
    }
}
