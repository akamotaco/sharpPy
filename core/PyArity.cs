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
            => Check(convention, (cls != null ? PyBuiltinFunction.QualifiedClassLabel(cls) : owner.Name) + "." + name, name, args, kwargs, bound);

        /// <summary>
        /// The convention of a module's function ("<__module__>.<name>"), or null: builtins and the
        /// C modules SharpPy implements.
        /// </summary>
        internal static Convention OfFunction(string module, string name) =>
            module != null && Table.TryGetValue(module + "." + name, out var found) ? found : null;

        // A builtin type's key in the table: its name, or "<module>.<name>".
        private static string TypeKey(PyType type) =>
            type.Module == null || type.Module == "builtins" ? type.Name : type.Module + "." + type.Name;

        /// <summary>
        /// PyObject_Call's vectorcall branch (Objects/call.c _PyVectorcall_Call): a callable with
        /// vectorcall has the keyword dict unpacked by _PyStack_UnpackDict, which refuses a name that
        /// is not a str before the callable runs. A builtin function or bound method has it unless it
        /// is METH_VARARGS (measured, core/BuiltinArity.g.cs), an unbound method descriptor always.
        /// A Python function checks in its own call; a static type when it has tp_vectorcall
        /// (measured), else its tp_new or tp_init parser meets the names in order;
        /// tp_new_wrapper is METH_VARARGS.
        /// </summary>
        internal static bool RefusesNonStrKeywordFirst(PyObject callable) => callable switch
        {
            PyBuiltinFunction { Name: "__new__" } or PyMethodDescriptor { Name: "__new__" } => false,
            PyMethodDescriptor => true,
            PyBoundMethodDescriptor { MethodDescriptor: { } method } =>
                !KeywordsNotFirst.Contains(TypeKey(method.OwnerType) + "." + method.Name),
            // classmethod_get's binding of a METH_CLASS method: the function it wraps.
            PyBoundMethodDescriptor { Callable: PyBuiltinFunction bound } => RefusesNonStrKeywordFirst(bound),
            PyBuiltinFunction function when function.OwnerType != null =>
                !KeywordsNotFirst.Contains(TypeKey(function.OwnerType) + "." + function.Name),
            PyBuiltinFunction function when function.OwnerClass != null =>
                !KeywordsNotFirst.Contains(function.OwnerClass + "." + function.Name),
            PyBuiltinFunction function =>
                !KeywordsNotFirst.Contains(function.OwnerModule + "." + function.Name),
            PyClass { IsHeapType: true } => false,
            PyType type => TypeKeywordsFirst.Contains(TypeKey(type)),
            _ => false,
        };

        /// <summary>
        /// PyObject_Call (Objects/call.c): a dict of keywords, the vectorcall branch's refusal of a
        /// name that is not a str, then the callable. CALL_FUNCTION_EX and wrap_call call through it.
        /// </summary>
        internal static PyObject ObjectCall(PyObject callable, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0 && RefusesNonStrKeywordFirst(callable))
                foreach (var pair in kwargs.InternalDict)
                    if (pair.Key is not PyString)
                        throw PyTypeError.Create("keywords must be strings");
            return callable.Call(args, kwargs);
        }

        /// <summary>The checks of the convention, the callable named qualname (_PyObject_FunctionStr).</summary>
        internal static void Check(Convention convention, string qualname, string name, PyObject[] args, PyDict kwargs, bool bound)
        {
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
