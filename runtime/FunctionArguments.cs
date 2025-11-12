using System;
using System.Collections.Generic;
using System.Text;

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

            // Performance: Eliminated LINQ - Direct loop instead of Select
            // Positional-only
            foreach (var arg in PosOnlyArgs)
            {
                names.Add(arg.Name);
            }

            // Regular args
            foreach (var arg in Args)
            {
                names.Add(arg.Name);
            }

            // *args
            if (VarArg != null)
                names.Add("*" + VarArg.Name);

            // Keyword-only
            foreach (var arg in KwOnlyArgs)
            {
                names.Add(arg.Name);
            }

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

            // Performance: Eliminated LINQ - Manual loops with StringBuilder
            if (PosOnlyArgs.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < PosOnlyArgs.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(PosOnlyArgs[i].Name);
                }
                parts.Add($"posonlyargs=[{sb}]");
            }

            if (Args.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Args.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Args[i].Name);
                }
                parts.Add($"args=[{sb}]");
            }

            if (VarArg != null)
                parts.Add($"vararg={VarArg.Name}");

            if (KwOnlyArgs.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < KwOnlyArgs.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(KwOnlyArgs[i].Name);
                }
                parts.Add($"kwonlyargs=[{sb}]");
            }

            if (KwArg != null)
                parts.Add($"kwarg={KwArg.Name}");

            if (Defaults.Count > 0)
                parts.Add($"defaults=[{Defaults.Count} items]");

            var result = new StringBuilder("arguments(");
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) result.Append(", ");
                result.Append(parts[i]);
            }
            result.Append(")");
            return result.ToString();
        }

        /// <summary>
        /// CPython 3.12 compatible AST dump format
        /// Returns: arguments(posonlyargs=[], args=[arg(arg='name')], kwonlyargs=[], kw_defaults=[], defaults=[...])
        /// </summary>
        public string ToPythonAst()
        {
            var parts = new List<string>();

            // Performance: Eliminated LINQ - Manual loops with StringBuilder
            // posonlyargs - always show even if empty
            if (PosOnlyArgs.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < PosOnlyArgs.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(PosOnlyArgs[i].ToString());
                }
                parts.Add($"posonlyargs=[{sb}]");
            }
            else
                parts.Add("posonlyargs=[]");

            // args - always show even if empty
            if (Args.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Args.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Args[i].ToString());
                }
                parts.Add($"args=[{sb}]");
            }
            else
                parts.Add("args=[]");

            // vararg - show if present
            if (VarArg != null)
                parts.Add($"vararg={VarArg.ToString()}");

            // kwonlyargs - always show even if empty
            if (KwOnlyArgs.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < KwOnlyArgs.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(KwOnlyArgs[i].ToString());
                }
                parts.Add($"kwonlyargs=[{sb}]");
            }
            else
                parts.Add("kwonlyargs=[]");

            // kw_defaults - always show even if empty
            if (KwDefaults.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < KwDefaults.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(KwDefaults[i]?.ToString() ?? "None");
                }
                parts.Add($"kw_defaults=[{sb}]");
            }
            else
                parts.Add("kw_defaults=[]");

            // kwarg - show if present
            if (KwArg != null)
                parts.Add($"kwarg={KwArg.ToString()}");

            // defaults - always show even if empty
            if (Defaults.Count > 0)
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
                var sb = new StringBuilder();
                for (int i = 0; i < defaultStrs.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(defaultStrs[i]);
                }
                parts.Add($"defaults=[{sb}]");
            }
            else
                parts.Add("defaults=[]");

            var result = new StringBuilder("arguments(");
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) result.Append(", ");
                result.Append(parts[i]);
            }
            result.Append(")");
            return result.ToString();
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
