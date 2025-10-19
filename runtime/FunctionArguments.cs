using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible arguments structure for function definitions
    /// Represents: arguments(posonlyargs, args, kwonlyargs, kw_defaults, defaults)
    /// </summary>
    public class FunctionArguments
    {
        /// <summary>
        /// Positional-only parameters (before /)
        /// CPython 3.12: posonlyargs
        /// </summary>
        public List<Arg> PosOnlyArgs { get; set; }

        /// <summary>
        /// Regular positional or positional-or-keyword parameters
        /// CPython 3.12: args
        /// </summary>
        public List<Arg> Args { get; set; }

        /// <summary>
        /// *args parameter (vararg)
        /// CPython 3.12: vararg
        /// </summary>
        public Arg? VarArg { get; set; }

        /// <summary>
        /// Keyword-only parameters (after *)
        /// CPython 3.12: kwonlyargs
        /// </summary>
        public List<Arg> KwOnlyArgs { get; set; }

        /// <summary>
        /// Default values for keyword-only parameters
        /// CPython 3.12: kw_defaults
        /// </summary>
        public List<Expression?> KwDefaults { get; set; }

        /// <summary>
        /// **kwargs parameter
        /// CPython 3.12: kwarg
        /// </summary>
        public Arg? KwArg { get; set; }

        /// <summary>
        /// Default values for regular positional parameters
        /// CPython 3.12: defaults
        /// Maps to the last len(defaults) parameters in Args
        /// </summary>
        public List<Expression> Defaults { get; set; }

        public FunctionArguments()
        {
            PosOnlyArgs = new List<Arg>();
            Args = new List<Arg>();
            VarArg = null;
            KwOnlyArgs = new List<Arg>();
            KwDefaults = new List<Expression?>();
            KwArg = null;
            Defaults = new List<Expression>();
        }

        /// <summary>
        /// Get all parameter names in order
        /// </summary>
        public List<string> GetAllParameterNames()
        {
            var names = new List<string>();

            // Positional-only
            names.AddRange(PosOnlyArgs.Select(a => a.Name));

            // Regular args
            names.AddRange(Args.Select(a => a.Name));

            // *args
            if (VarArg != null)
                names.Add("*" + VarArg.Name);

            // Keyword-only
            names.AddRange(KwOnlyArgs.Select(a => a.Name));

            // **kwargs
            if (KwArg != null)
                names.Add("**" + KwArg.Name);

            return names;
        }

        /// <summary>
        /// Get parameter names and their default values
        /// Returns (paramNames, defaultValues) where defaultValues align with last N params
        /// </summary>
        public (List<string>, List<PyObject>) GetParametersAndDefaults(PyScope scope)
        {
            var paramNames = new List<string>();
            var defaults = new List<PyObject>();

            // Add positional-only parameters
            foreach (var arg in PosOnlyArgs)
            {
                paramNames.Add(arg.Name);
            }

            // Add regular parameters
            foreach (var arg in Args)
            {
                paramNames.Add(arg.Name);
            }

            // Add *args if present
            if (VarArg != null)
            {
                paramNames.Add("*" + VarArg.Name);
            }

            // Add keyword-only parameters
            foreach (var arg in KwOnlyArgs)
            {
                paramNames.Add(arg.Name);
            }

            // Add **kwargs if present
            if (KwArg != null)
            {
                paramNames.Add("**" + KwArg.Name);
            }

            // Evaluate default values
            // CPython 3.12: defaults align with the LAST len(defaults) parameters in Args
            foreach (var defaultExpr in Defaults)
            {
                defaults.Add(defaultExpr.Evaluate(scope));
            }

            // Keyword-only defaults
            foreach (var kwDefault in KwDefaults)
            {
                if (kwDefault != null)
                {
                    defaults.Add(kwDefault.Evaluate(scope));
                }
            }

            return (paramNames, defaults);
        }

        public override string ToString()
        {
            var parts = new List<string>();

            if (PosOnlyArgs.Any())
                parts.Add($"posonlyargs=[{string.Join(", ", PosOnlyArgs.Select(a => a.Name))}]");

            if (Args.Any())
                parts.Add($"args=[{string.Join(", ", Args.Select(a => a.Name))}]");

            if (VarArg != null)
                parts.Add($"vararg={VarArg.Name}");

            if (KwOnlyArgs.Any())
                parts.Add($"kwonlyargs=[{string.Join(", ", KwOnlyArgs.Select(a => a.Name))}]");

            if (KwArg != null)
                parts.Add($"kwarg={KwArg.Name}");

            if (Defaults.Any())
                parts.Add($"defaults=[{Defaults.Count} items]");

            return $"arguments({string.Join(", ", parts)})";
        }

        /// <summary>
        /// CPython 3.12 compatible AST dump format
        /// Returns: arguments(posonlyargs=[], args=[arg(arg='name')], kwonlyargs=[], kw_defaults=[], defaults=[...])
        /// </summary>
        public string ToPythonAst()
        {
            var parts = new List<string>();

            // posonlyargs - always show even if empty
            if (PosOnlyArgs.Any())
                parts.Add($"posonlyargs=[{string.Join(", ", PosOnlyArgs.Select(a => a.ToString()))}]");
            else
                parts.Add("posonlyargs=[]");

            // args - always show even if empty
            if (Args.Any())
                parts.Add($"args=[{string.Join(", ", Args.Select(a => a.ToString()))}]");
            else
                parts.Add("args=[]");

            // vararg - show if present
            if (VarArg != null)
                parts.Add($"vararg={VarArg.ToString()}");

            // kwonlyargs - always show even if empty
            if (KwOnlyArgs.Any())
                parts.Add($"kwonlyargs=[{string.Join(", ", KwOnlyArgs.Select(a => a.ToString()))}]");
            else
                parts.Add("kwonlyargs=[]");

            // kw_defaults - always show even if empty
            if (KwDefaults.Any())
                parts.Add($"kw_defaults=[{string.Join(", ", KwDefaults.Select(d => d?.ToString() ?? "None"))}]");
            else
                parts.Add("kw_defaults=[]");

            // kwarg - show if present
            if (KwArg != null)
                parts.Add($"kwarg={KwArg.ToString()}");

            // defaults - always show even if empty
            if (Defaults.Any())
            {
                var defaultStrs = new List<string>();
                foreach (var def in Defaults)
                {
                    if (def is ConstantExpression constExpr)
                    {
                        if (constExpr.Value is PyString pyStr)
                            defaultStrs.Add($"Constant(value='{pyStr.Value}')");
                        else if (constExpr.Value is PyInt pyInt)
                            defaultStrs.Add($"Constant(value={pyInt.Value})");
                        else if (constExpr.Value is PyFloat pyFloat)
                            defaultStrs.Add($"Constant(value={pyFloat.Value})");
                        else if (constExpr.Value is PyBool pyBool)
                            defaultStrs.Add($"Constant(value={(pyBool.Value ? "True" : "False")})");
                        else if (constExpr.Value is PyNone)
                            defaultStrs.Add("Constant(value=None)");
                        else
                            defaultStrs.Add($"Constant(value={constExpr.Value})");
                    }
                    else
                    {
                        defaultStrs.Add(def.ToString());
                    }
                }
                parts.Add($"defaults=[{string.Join(", ", defaultStrs)}]");
            }
            else
                parts.Add("defaults=[]");

            return $"arguments({string.Join(", ", parts)})";
        }
    }

    /// <summary>
    /// CPython 3.12 compatible arg structure
    /// Represents a single function parameter: arg(arg='name', annotation=None)
    /// </summary>
    public class Arg
    {
        /// <summary>
        /// Parameter name
        /// CPython 3.12: arg
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Type annotation (optional)
        /// CPython 3.12: annotation
        /// </summary>
        public Expression? Annotation { get; set; }

        public Arg(string name, Expression? annotation = null)
        {
            Name = name;
            Annotation = annotation;
        }

        public override string ToString()
        {
            if (Annotation != null)
                return $"arg(arg='{Name}', annotation={Annotation})";
            return $"arg(arg='{Name}')";
        }
    }
}
