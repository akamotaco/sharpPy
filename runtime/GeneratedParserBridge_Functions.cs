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
            if (argumentsData.PosOnlyArgs != null && argumentsData.PosOnlyArgs.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Found {argumentsData.PosOnlyArgs.Count} posonlyargs");
                foreach (var arg in argumentsData.PosOnlyArgs)
                {
                    functionArgs.PosOnlyArgs.Add(new Arg(arg.Arg));
                    Console.WriteLine($"[DEBUG] Added posonly arg: {arg.Arg}");
                }
            }

            // Process args (regular positional or positional-or-keyword parameters)
            if (argumentsData.Args != null && argumentsData.Args.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Found {argumentsData.Args.Count} args");
                foreach (var arg in argumentsData.Args)
                {
                    functionArgs.Args.Add(new Arg(arg.Arg));
                    Console.WriteLine($"[DEBUG] Added regular arg: {arg.Arg}");
                }
            }

            // Process vararg (*args)
            if (argumentsData.VarArg != null)
            {
                functionArgs.VarArg = new Arg(argumentsData.VarArg.Arg);
                Console.WriteLine($"[DEBUG] Added vararg: *{argumentsData.VarArg.Arg}");
            }

            // Process kwonlyargs (keyword-only parameters after *)
            if (argumentsData.KwOnlyArgs != null && argumentsData.KwOnlyArgs.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Found {argumentsData.KwOnlyArgs.Count} kwonlyargs");
                foreach (var arg in argumentsData.KwOnlyArgs)
                {
                    functionArgs.KwOnlyArgs.Add(new Arg(arg.Arg));
                    Console.WriteLine($"[DEBUG] Added kwonly arg: {arg.Arg}");
                }
            }

            // Process kwarg (**kwargs)
            if (argumentsData.KwArg != null)
            {
                functionArgs.KwArg = new Arg(argumentsData.KwArg.Arg);
                Console.WriteLine($"[DEBUG] Added kwarg: **{argumentsData.KwArg.Arg}");
            }

            // Process defaults (default values for regular args)
            // CPython 3.12: defaults align with the LAST len(defaults) parameters in args
            if (argumentsData.Defaults != null && argumentsData.Defaults.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Found {argumentsData.Defaults.Count} defaults");
                foreach (var defaultExpr in argumentsData.Defaults.AsEnumerable())
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
                foreach (var defaultExpr in argumentsData.KwDefaults.AsEnumerable())
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
