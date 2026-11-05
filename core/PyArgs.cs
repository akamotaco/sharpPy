using System;

namespace SharpPy;

/// <summary>
/// How the argument clinic calls a C method, which decides the wording of every arity error
/// (Objects/descrobject.c for METH_NOARGS and METH_O, Python/getargs.c for the rest).
/// </summary>
internal enum PyCall
{
    /// <summary>METH_NOARGS: "Type.name() takes no arguments (1 given)".</summary>
    NoArgs,
    /// <summary>METH_O: "Type.name() takes exactly one argument (0 given)".</summary>
    One,
    /// <summary>METH_FASTCALL, positional only: _PyArg_CheckPositional, "name expected at most 1 argument, got 2".</summary>
    Fast,
    /// <summary>METH_METHOD|METH_FASTCALL|METH_KEYWORDS over positional-only parameters: _PyArg_UnpackKeywords.</summary>
    Defining,
    /// <summary>METH_VARARGS read by PyArg_ParseTuple: "name() takes at most 3 arguments (4 given)".</summary>
    ParseTuple,
}

/// <summary>Python/getargs.c: the checks the argument clinic makes before a method runs.</summary>
internal static class PyArgs
{
    private static string Plural(long n) => n == 1 ? "" : "s";

    // method_vectorcall_NOARGS
    internal static void NoArgs(string qualname, PyObject[] args, PyDict kwargs)
    {
        if (kwargs != null && kwargs.Length() != 0)
            throw PyTypeError.Create($"{qualname}() takes no keyword arguments");
        if (args.Length != 0)
            throw PyTypeError.Create($"{qualname}() takes no arguments ({args.Length} given)");
    }

    // method_vectorcall_O
    internal static void One(string qualname, PyObject[] args, PyDict kwargs)
    {
        if (kwargs != null && kwargs.Length() != 0)
            throw PyTypeError.Create($"{qualname}() takes no keyword arguments");
        if (args.Length != 1)
            throw PyTypeError.Create($"{qualname}() takes exactly one argument ({args.Length} given)");
    }

    /// <summary>
    /// The keyword refusal of a METH_FASTCALL function: _PyObject_FunctionStr names a module-level
    /// function with its module ("_heapq.heappush()"), a builtin without it.
    /// </summary>
    internal static void NoKeywords(string funcstr, PyDict kwargs)
    {
        if (kwargs != null && kwargs.Length() != 0)
            throw PyTypeError.Create($"{funcstr}() takes no keyword arguments");
    }

    // _PyArg_CheckPositional, after _PyArg_NoKwnames
    internal static void Positional(string name, PyObject[] args, PyDict kwargs, int min, int max)
    {
        if (kwargs != null && kwargs.Length() != 0)
            throw PyTypeError.Create($"{name}() takes no keyword arguments");
        int nargs = args.Length;
        if (nargs < min)
            throw PyTypeError.Create($"{name} expected {(min == max ? "" : "at least ")}{min} argument{Plural(min)}, got {nargs}");
        if (nargs > max)
            throw PyTypeError.Create($"{name} expected {(min == max ? "" : "at most ")}{max} argument{Plural(max)}, got {nargs}");
    }

    // vgetargs1_impl: the count of a format with min required and max in all.
    internal static void ParseTuple(string name, PyObject[] args, int min, int max)
    {
        int len = args.Length;
        if (len >= min && len <= max)
            return;
        int count = len < min ? min : max;
        string how = min == max ? "exactly" : len < min ? "at least" : "at most";
        throw PyTypeError.Create($"{name}() takes {how} {count} argument{Plural(count)} ({len} given)");
    }

    /// <summary>
    /// _PyArg_UnpackKeywords. names lists every parameter; the first posonly of them take no
    /// keyword. The result has one slot per parameter, null where nothing was given.
    /// </summary>
    internal static PyObject[] Unpack(string fname, PyObject[] args, PyDict kwargs, string[] names, int posonly, int minpos, int maxpos, int minkw = 0)
    {
        int nargs = args.Length;
        int nkwargs = kwargs?.Length() ?? 0;
        int maxargs = names.Length;
        int minposonly = Math.Min(posonly, minpos);
        int reqlimit = minkw != 0 ? maxpos + minkw : minpos;
        // The clinic code of a method without parameters says only this.
        if (maxargs == 0 && nargs + nkwargs > 0)
            throw PyTypeError.Create($"{fname}() takes no arguments");
        if (nargs + nkwargs > maxargs)
            throw PyTypeError.Create($"{fname}() takes at most {maxargs} {(nargs == 0 ? "keyword " : "")}argument{Plural(maxargs)} ({nargs + nkwargs} given)");
        if (nargs > maxpos)
            throw PyTypeError.Create(maxpos == 0
                ? $"{fname}() takes no positional arguments"
                : $"{fname}() takes {(minpos < maxpos ? "at most" : "exactly")} {maxpos} positional argument{Plural(maxpos)} ({nargs} given)");
        if (nargs < minposonly)
            throw PyTypeError.Create($"{fname}() takes {(minposonly < maxpos ? "at least" : "exactly")} {minposonly} positional argument{Plural(minposonly)} ({nargs} given)");
        var buffer = new PyObject[maxargs];
        Array.Copy(args, buffer, nargs);
        PyObject Keyword(string name)
        {
            foreach (var pair in kwargs.InternalDict)
                if (pair.Key is PyString key && key.Value == name)
                    return pair.Value;
            return null;
        }
        for (int i = Math.Max(nargs, posonly); i < maxargs; i++)
        {
            PyObject current;
            if (nkwargs != 0)
                current = Keyword(names[i]);
            else if (i >= reqlimit)
                break;
            else
                current = null;
            buffer[i] = current;
            if (current != null)
                nkwargs--;
            else if (i < minpos || (maxpos <= i && i < reqlimit))
                throw PyTypeError.Create($"{fname}() missing required argument '{names[i]}' (pos {i + 1})");
        }
        if (nkwargs > 0)
        {
            for (int i = posonly; i < nargs; i++)
                if (Keyword(names[i]) != null)
                    throw PyTypeError.Create($"argument for {fname}() given by name ('{names[i]}') and position ({i + 1})");
            // error_unexpected_keyword_arg
            foreach (var pair in kwargs.InternalDict)
            {
                if (pair.Key is not PyString key)
                    throw PyTypeError.Create("keywords must be strings");
                if (Array.IndexOf(names, key.Value, posonly) < 0)
                    throw PyTypeError.Create($"'{key.Value}' is an invalid keyword argument for {fname}()");
            }
            throw PyTypeError.Create($"invalid keyword argument for {fname}()");
        }
        return buffer;
    }

    /// <summary>
    /// A method of a native type with the clinic's checks for its kind in front of the body.
    /// For Defining, min and max count the positional-only parameters.
    /// </summary>
    internal static void Define(PyType type, string name, PyCall kind, Func<PyObject, PyObject[], PyObject> body, int min = 0, int max = 0) =>
        type.TypeDict[name] = Method(type, name, kind, body, min, max);

    /// <summary>PyDescr_NewMethod of one PyMethodDef: the method_descriptor Define stores.</summary>
    internal static PyMethodDescriptor Method(PyType type, string name, PyCall kind, Func<PyObject, PyObject[], PyObject> body, int min = 0, int max = 0)
    {
        string qualname = type.Name + "." + name;
        var names = new string[max];
        return new PyMethodDescriptor(name, type, (self, args, kwargs) =>
        {
            switch (kind)
            {
                case PyCall.NoArgs: NoArgs(qualname, args, kwargs); break;
                case PyCall.One: One(qualname, args, kwargs); break;
                case PyCall.Fast: Positional(name, args, kwargs, min, max); break;
                default: Unpack(name, args, kwargs, names, max, min, max); break;
            }
            return body(self, args);
        }, minArgs: 0, maxArgs: int.MaxValue, acceptsKwargs: true);
    }

    // _Py_convert_optional_to_ssize_t: None, or an index that fits.
    internal static long OptionalSsize(PyObject value, long fallback = -1)
    {
        if (value == null || value is PyNone)
            return fallback;
        if (PyObject.TryGetIndex(value) == null)
            throw PyTypeError.Create($"argument should be integer or None, not '{value.GetTypeName()}'");
        return BytesMethods.AsSsize(value);
    }

    // The Py_ssize_t converter: _PyNumber_Index, then PyLong_AsSsize_t.
    internal static long Ssize(PyObject value)
    {
        var index = PyObject.TryGetIndex(value)
            ?? throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
        if (index.Value < long.MinValue || index.Value > long.MaxValue)
            throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
        return (long)index.Value;
    }

    // PyNumber_AsSsize_t(value, PyExc_OverflowError): __index__, and an overflow named by the object.
    internal static long IndexSsize(PyObject value)
    {
        var index = PyObject.TryGetIndex(value)
            ?? throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
        if (index.Value < long.MinValue || index.Value > long.MaxValue)
            throw PyOverflowError.Create($"cannot fit '{value.GetTypeName()}' into an index-sized integer");
        return (long)index.Value;
    }

    /// <summary>
    /// The count sq_repeat receives as a Py_ssize_t. A repeat that cannot be allocated is the
    /// MemoryError of list_repeat and the others (size > PY_SSIZE_T_MAX / n); an empty sequence
    /// stays empty however large the count.
    /// </summary>
    internal static PyInt RepeatCount(PyObject sequence, long count)
    {
        if (count > int.MaxValue)
        {
            if (sequence.Length() == 0) return new PyInt(0);
            throw PyMemoryError.Create();
        }
        return new PyInt(count);
    }

    /// <summary>
    /// _PyEval_SliceIndex: None is the default, __index__ is read and a value past Py_ssize_t is
    /// clipped (PyNumber_AsSsize_t with no exception). Clamped again to the CLR index range, which
    /// ADJUST_INDICES then treats as it would the larger value.
    /// </summary>
    internal static int SliceIndex(PyObject value, int missing)
    {
        if (value == null || value is PyNone)
            return missing;
        var index = PyObject.TryGetIndex(value)
            ?? throw PyTypeError.Create("slice indices must be integers or None or have an __index__ method");
        return index.Value > int.MaxValue ? int.MaxValue : index.Value < int.MinValue ? int.MinValue : (int)index.Value;
    }

    /// <summary>The Py_ssize_t converter ("n"), clamped to the CLR range for a count or a limit.</summary>
    internal static int SsizeCount(PyObject value)
    {
        long count = Ssize(value);
        return count > int.MaxValue ? int.MaxValue : count < int.MinValue ? int.MinValue : (int)count;
    }

    /// <summary>
    /// A width ("n") that the result will be as long as: one no CLR string can have is the MemoryError
    /// PyUnicode_New raises for it.
    /// </summary>
    internal static int Width(PyObject value)
    {
        long width = Ssize(value);
        if (width > int.MaxValue)
            throw PyMemoryError.Create();
        return width < int.MinValue ? int.MinValue : (int)width;
    }

    /// <summary>
    /// The "i" format of PyArg_ParseTuple: PyLong_AsLong, then the range of an int. A C long is
    /// 32 bits on Windows, so there the long overflows first.
    /// </summary>
    internal static int ParseInt(PyObject value)
    {
        var index = PyObject.TryGetIndex(value)
            ?? throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
        var number = index.Value;
        if (OperatingSystem.IsWindows() ? number < int.MinValue || number > int.MaxValue : number < long.MinValue || number > long.MaxValue)
            throw PyOverflowError.Create("Python int too large to convert to C long");
        if (number > int.MaxValue)
            throw PyOverflowError.Create("signed integer is greater than maximum");
        if (number < int.MinValue)
            throw PyOverflowError.Create("signed integer is less than minimum");
        return (int)number;
    }

    // The int converter: _PyLong_AsInt.
    internal static int Int(PyObject value)
    {
        var index = PyObject.TryGetIndex(value)
            ?? throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer");
        if (index.Value < int.MinValue || index.Value > int.MaxValue)
            throw PyOverflowError.Create("Python int too large to convert to C int");
        return (int)index.Value;
    }
}
