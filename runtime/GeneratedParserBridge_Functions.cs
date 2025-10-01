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
        /// Convert GeneratedFunctionDef arguments dictionary to CPython 3.12 compatible FunctionArguments
        /// </summary>
        private static FunctionArguments ConvertFunctionArguments(object argumentsData)
        {
            var functionArgs = new FunctionArguments();

            if (argumentsData == null)
            {
                Console.WriteLine("[DEBUG] ConvertFunctionArguments: argumentsData is null");
                return functionArgs;
            }

            Console.WriteLine($"[DEBUG] ConvertFunctionArguments: type={argumentsData.GetType().Name}");

            if (argumentsData is not Dictionary<string, object> argsDict)
            {
                Console.WriteLine("[DEBUG] ConvertFunctionArguments: not a Dictionary");
                return functionArgs;
            }

            Console.WriteLine($"[DEBUG] ConvertFunctionArguments: Dictionary with keys: [{string.Join(", ", argsDict.Keys)}]");

            // Process posonlyargs (positional-only parameters before /)
            if (argsDict.TryGetValue("posonlyargs", out var posonlyValue) && posonlyValue is List<object> posonlyList)
            {
                Console.WriteLine($"[DEBUG] Found posonlyargs list with {posonlyList.Count} items");
                foreach (var item in posonlyList)
                {
                    var argName = ExtractArgName(item);
                    if (argName != null)
                    {
                        functionArgs.PosOnlyArgs.Add(new Arg(argName));
                        Console.WriteLine($"[DEBUG] Added posonly arg: {argName}");
                    }
                }
            }

            // Process args (regular positional or positional-or-keyword parameters)
            if (argsDict.TryGetValue("args", out var argsValue) && argsValue is List<object> argsList)
            {
                Console.WriteLine($"[DEBUG] Found args list with {argsList.Count} items");
                foreach (var item in argsList)
                {
                    var argName = ExtractArgName(item);
                    if (argName != null)
                    {
                        functionArgs.Args.Add(new Arg(argName));
                        Console.WriteLine($"[DEBUG] Added regular arg: {argName}");
                    }
                }
            }

            // IMPORTANT: Also check defaults list for parameters with default values
            // Sometimes parameters are ONLY in defaults list as { param, defaultValue }
            if (argsDict.TryGetValue("defaults", out var defaultsCheckValue) && defaultsCheckValue is List<object> defaultsCheckList)
            {
                Console.WriteLine($"[DEBUG] Checking defaults list for additional parameters");
                foreach (var defaultItem in defaultsCheckList)
                {
                    if (defaultItem != null)
                    {
                        var itemType = defaultItem.GetType();
                        var paramProp = itemType.GetProperty("param");
                        if (paramProp != null)
                        {
                            var paramValue = paramProp.GetValue(defaultItem);
                            var paramName = ExtractArgName(paramValue);
                            if (paramName != null)
                            {
                                // Check if this parameter is already in Args
                                bool alreadyExists = functionArgs.Args.Any(a => a.Name == paramName);
                                if (!alreadyExists)
                                {
                                    functionArgs.Args.Add(new Arg(paramName));
                                    Console.WriteLine($"[DEBUG] Added parameter from defaults: {paramName}");
                                }
                            }
                        }
                    }
                }
            }

            // Process vararg (*args)
            if (argsDict.TryGetValue("vararg", out var varargValue) && varargValue != null)
            {
                var argName = ExtractArgName(varargValue);
                if (argName != null)
                {
                    functionArgs.VarArg = new Arg(argName);
                    Console.WriteLine($"[DEBUG] Added vararg: *{argName}");
                }
            }

            // Process kwonlyargs (keyword-only parameters after *)
            if (argsDict.TryGetValue("kwonlyargs", out var kwonlyValue) && kwonlyValue is List<object> kwonlyList)
            {
                Console.WriteLine($"[DEBUG] Found kwonlyargs list with {kwonlyList.Count} items");
                foreach (var item in kwonlyList)
                {
                    var argName = ExtractArgName(item);
                    if (argName != null)
                    {
                        functionArgs.KwOnlyArgs.Add(new Arg(argName));
                        Console.WriteLine($"[DEBUG] Added kwonly arg: {argName}");
                    }
                }
            }

            // Process kwarg (**kwargs)
            if (argsDict.TryGetValue("kwarg", out var kwargValue) && kwargValue != null)
            {
                var argName = ExtractArgName(kwargValue);
                if (argName != null)
                {
                    functionArgs.KwArg = new Arg(argName);
                    Console.WriteLine($"[DEBUG] Added kwarg: **{argName}");
                }
            }

            // Process defaults (default values for regular args)
            // CPython 3.12: defaults align with the LAST len(defaults) parameters in args
            if (argsDict.TryGetValue("defaults", out var defaultsValue) && defaultsValue is List<object> defaultsList)
            {
                Console.WriteLine($"[DEBUG] Found defaults list with {defaultsList.Count} items");
                foreach (var defaultItem in defaultsList)
                {
                    Console.WriteLine($"[DEBUG] Processing default item: {defaultItem} (Type: {defaultItem?.GetType().Name})");

                    // Try to extract defaultValue from anonymous type { param, defaultValue }
                    object? actualDefault = null;
                    if (defaultItem != null)
                    {
                        var itemType = defaultItem.GetType();
                        var defaultValueProp = itemType.GetProperty("defaultValue");
                        if (defaultValueProp != null)
                        {
                            actualDefault = defaultValueProp.GetValue(defaultItem);
                            Console.WriteLine($"[DEBUG] Extracted defaultValue from anonymous type: {actualDefault}");
                        }
                        else
                        {
                            // No 'defaultValue' property, use the item itself
                            actualDefault = defaultItem;
                        }
                    }

                    if (actualDefault is GeneratedExpr defaultExpr)
                    {
                        Console.WriteLine($"[DEBUG] Default is GeneratedExpr: {defaultExpr.ExpressionType}");
                        var convertedDefault = ConvertAnyExpression(defaultExpr);
                        Console.WriteLine($"[DEBUG] ConvertedDefault result: {convertedDefault} (null: {convertedDefault == null})");
                        if (convertedDefault != null)
                        {
                            functionArgs.Defaults.Add(convertedDefault);
                            Console.WriteLine($"[DEBUG] Added default value: {convertedDefault}");
                        }
                        else
                        {
                            Console.WriteLine($"[DEBUG] ConvertAnyExpression returned null for {defaultExpr.ExpressionType}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] Default item is not GeneratedExpr after extraction, type: {actualDefault?.GetType().Name}");
                    }
                }
            }

            // Process kw_defaults (default values for keyword-only args)
            if (argsDict.TryGetValue("kw_defaults", out var kwDefaultsValue) && kwDefaultsValue is List<object> kwDefaultsList)
            {
                Console.WriteLine($"[DEBUG] Found kw_defaults list with {kwDefaultsList.Count} items");
                foreach (var defaultItem in kwDefaultsList)
                {
                    if (defaultItem is GeneratedExpr defaultExpr)
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

        /// <summary>
        /// Extract argument name from various formats (GeneratedExpr, string, etc.)
        /// </summary>
        private static string? ExtractArgName(object argItem)
        {
            if (argItem == null)
                return null;

            // Check if it's a string directly
            if (argItem is string strName && !string.IsNullOrEmpty(strName))
                return strName;

            // Check if it's a GeneratedExpr with an 'arg' property
            if (argItem is GeneratedExpr genExpr && genExpr.Value != null)
            {
                var valueType = genExpr.Value.GetType();
                var argProperty = valueType.GetProperty("arg");

                if (argProperty != null)
                {
                    var argValue = argProperty.GetValue(genExpr.Value);
                    if (argValue is string paramName)
                    {
                        return paramName;
                    }
                }
            }

            return null;
        }

        // Note: ConvertDefaultToString is already defined in GeneratedParserBridge.cs
        // We don't duplicate it here
    }
}
