namespace SharpPy
{
    /// <summary>
    /// The calling convention of each method of a builtin type (core/BuiltinArity.g.cs, from
    /// tools/gen_builtin_arity.py), which is where CPython's arity errors come from: ml_flags
    /// METH_NOARGS and METH_O (Objects/descrobject.c method_vectorcall_NOARGS/_O), the argument
    /// clinic's _PyArg_CheckPositional and _PyArg_UnpackKeywords, and PyArg_ParseTuple
    /// (Python/getargs.c). The method body never sees a call its convention refuses.
    /// </summary>
    internal static partial class PyArity
    {
        /// <summary>
        /// QualifiedKeywordRefusal: a bound call refuses keywords with the type in the name
        /// (cfunction_check_kwargs); false for METH_VARARGS, whose bound call goes through
        /// cfunction_call and names the method alone. An unbound call always names the type
        /// (method_check_args).
        /// </summary>
        internal sealed record Convention(PyCall Kind, int Min, int Max, string[] Names = null,
            int PositionalOnly = 0, int RequiredKeywords = 0, bool QualifiedKeywordRefusal = false);

        /// <summary>
        /// The convention of a method of a type implemented in C# (a builtin, or a type of one of the
        /// C modules, keyed by its module), or null. A class written in Python has none.
        /// </summary>
        internal static Convention Of(PyType owner, string name)
        {
            if (owner == null || owner is PyClass { IsHeapType: true })
                return null;
            string type = owner.Module == null || owner.Module == "builtins" ? owner.Name : owner.Module + "." + owner.Name;
            return Table.TryGetValue(type + "." + name, out var found) ? found : null;
        }

        /// <summary>
        /// The checks of the convention, before the body runs. A METH_CLASS method is named
        /// after the class it is bound to (meth_get__qualname__ of a type self), given as cls.
        /// </summary>
        internal static void Check(Convention convention, PyType owner, string name, PyObject[] args, PyDict kwargs, bool bound = false, PyObject cls = null)
        {
            string qualname = (cls != null ? PyBuiltinFunction.QualifiedClassLabel(cls) : owner.Name) + "." + name;
            bool keywords = kwargs != null && kwargs.Length() != 0;
            bool qualified = !bound || convention.QualifiedKeywordRefusal;
            switch (convention.Kind)
            {
                case PyCall.NoArgs:
                    PyArgs.NoArgs(qualname, args, kwargs);
                    break;
                case PyCall.One:
                    PyArgs.One(qualname, args, kwargs);
                    break;
                case PyCall.Fast:
                    if (keywords)
                        throw PyTypeError.Create($"{(qualified ? qualname : name)}() takes no keyword arguments");
                    PyArgs.Positional(name, args, null, convention.Min, convention.Max);
                    break;
                case PyCall.ParseTuple:
                    if (keywords)
                        throw PyTypeError.Create($"{(qualified ? qualname : name)}() takes no keyword arguments");
                    PyArgs.ParseTuple(name, args, convention.Min, convention.Max);
                    break;
                case PyCall.Defining:
                    PyArgs.Unpack(name, args, kwargs, convention.Names, convention.PositionalOnly,
                        convention.Min, convention.Max, convention.RequiredKeywords);
                    break;
            }
        }
    }
}
