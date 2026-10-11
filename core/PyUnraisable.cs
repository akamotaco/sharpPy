using System;

namespace SharpPy
{
    // CPython 3.12 Python/errors.c: UnraisableHookArgs and PyErr_WriteUnraisable.
    internal sealed class PyUnraisableHookArgs : PyTuple
    {
        private static readonly string[] Fields = { "exc_type", "exc_value", "exc_traceback", "err_msg", "object" };
        private static readonly PyType ArgsType = new HookArgsType();

        private sealed class HookArgsType : PyType
        {
            public HookArgsType() : base("UnraisableHookArgs", new[] { PyType.TupleType })
            {
                TypeDict["n_fields"] = new PyInt(5);
                TypeDict["n_sequence_fields"] = new PyInt(5);
                TypeDict["n_unnamed_fields"] = new PyInt(0);
            }
            public override PyObject Call(PyObject[] args, PyDict kwargs = null)
            {
                int count = args.Length + (kwargs?.Length() ?? 0);
                if (count > 2)
                    throw PyTypeError.Create($"structseq() takes at most 2 arguments ({count} given)");
                PyObject sequence = args.Length > 0 ? args[0] : null;
                PyObject dictionary = args.Length > 1 ? args[1] : null;
                if (kwargs != null)
                    foreach (var entry in kwargs.InternalDict)
                    {
                        var key = entry.Key.ToStr().Value;
                        if (key != "sequence" && key != "dict")
                            throw PyTypeError.Create($"'{key}' is an invalid keyword argument for structseq()");
                        int position = key == "sequence" ? 1 : 2;
                        if (args.Length >= position)
                            throw PyTypeError.Create($"argument for structseq() given by name ('{key}') and position ({position})");
                        if (key == "sequence") sequence = entry.Value;
                        else dictionary = entry.Value;
                    }
                if (sequence == null)
                    throw PyTypeError.Create("structseq() missing required argument 'sequence' (pos 1)");
                PyTuple tuple;
                try { tuple = (PyTuple)PyType.TupleType.Call(new PyObject[] { sequence }, null); }
                catch (PythonException error) when (error.PyException is PyTypeError)
                { throw PyTypeError.Create("constructor requires a sequence"); }
                if (tuple.Items.Length != 5)
                    throw PyTypeError.Create($"UnraisableHookArgs() takes a 5-sequence ({tuple.Items.Length}-sequence given)");
                if (dictionary != null && dictionary is not PyDict)
                    throw PyTypeError.Create("UnraisableHookArgs() takes a dict as second arg, if any");
                return new PyUnraisableHookArgs((PyObject[])tuple.Items.Clone());
            }
        }

        internal PyUnraisableHookArgs(PyObject[] items) : base(items) { }
        public override PyType GetPyType() => ArgsType;
        public override string GetTypeName() => "UnraisableHookArgs";
        public override PyObject GetAttribute(string name)
        {
            int index = Array.IndexOf(Fields, name);
            return index >= 0 ? Items[index] : base.GetAttribute(name);
        }
        public override void SetAttribute(string name, PyObject value)
            => throw PyAttributeError.Create(Array.IndexOf(Fields, name) >= 0 ? "readonly attribute" : $"'UnraisableHookArgs' object has no attribute '{name}'");
        public override void DelAttribute(string name) => SetAttribute(name, PyNone.Instance);
        public override PyString ToRepr()
        {
            var parts = new string[5];
            for (int i = 0; i < parts.Length; i++) parts[i] = Fields[i] + "=" + Items[i].ToRepr().Value;
            return new PyString("UnraisableHookArgs(" + string.Join(", ", parts) + ")");
        }
        public override PyString ToStr() => ToRepr();
    }

    internal static class PyUnraisable
    {
        private static string FormatObject(PyObject value, bool repr)
        {
            if (value is PyClassInstance)
            {
                string name = repr ? "__repr__" : "__str__";
                var method = value.LookupSpecialMethodUncached(name);
                if (method != null)
                {
                    var result = value.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
                    if (result is PyString text) return text.Value;
                    throw PyTypeError.Create($"{name} returned non-string (type {result.GetTypeName()})");
                }
            }
            return repr ? value.ToRepr().Value : value.ToStr().Value;
        }

        internal static void Report(PythonException error, PyObject target)
        {
            var exception = error.PyException;
            var args = new PyUnraisableHookArgs(new PyObject[] {
                exception.GetPyType(), exception, (PyObject)exception.__traceback__ ?? PyNone.Instance,
                PyNone.Instance, target
            });
            PyObject hook = null;
            if (PyImportSystem.TryGetModule("sys", out var sys))
                sys.ModuleDict.TryGetValue("unraisablehook", out hook);
            try
            {
                if (hook != null && hook is not PyNone)
                {
                    try { hook.Call(new PyObject[] { args }, null); return; }
                    catch (PythonException hookError)
                    {
                        var failure = hookError.PyException;
                        args = new PyUnraisableHookArgs(new PyObject[] {
                            failure.GetPyType(), failure, (PyObject)failure.__traceback__ ?? PyNone.Instance,
                            new PyString("Exception ignored in sys.unraisablehook"), hook
                        });
                    }
                }
                WriteDefault(args);
            }
            catch (PythonException) { } // Reporting must not replace the original cleanup.
        }

        internal static PyObject DefaultHook(PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("sys.unraisablehook() takes no keyword arguments");
            if (args.Length != 1)
                throw PyTypeError.Create($"sys.unraisablehook() takes exactly one argument ({args.Length} given)");
            if (args[0] is not PyUnraisableHookArgs info)
                throw PyTypeError.Create("sys.unraisablehook argument type must be UnraisableHookArgs");
            WriteDefault(info);
            return PyNone.Instance;
        }

        private static void WriteDefault(PyUnraisableHookArgs args)
        {
            PyObject stream = null;
            if (PyImportSystem.TryGetModule("sys", out var sys))
                sys.ModuleDict.TryGetValue("stderr", out stream);
            if (stream is PyNone) return;
            if (sys != null && stream == null) return;
            bool console = stream == null || stream is Modules.PyFile;
            void Write(string value)
            {
                if (console)
                {
                    if (stream is Modules.PyFile file && file.Name == "stdout") Console.Out.Write(value);
                    else Console.Error.Write(value);
                }
                else stream.GetAttribute("write").Call(new PyObject[] { new PyString(value) }, null);
            }
            var value = args.Items[1];
            var message = args.Items[3];
            var target = args.Items[4];
            if (target is not PyNone)
            {
                Write(message is PyNone ? "Exception ignored in: " : message.ToStr().Value + ": ");
                string repr;
                try { repr = FormatObject(target, true); }
                catch (PythonException) { repr = "<object repr() failed>"; }
                Write(repr);
                Write("\n");
            }
            else if (message is not PyNone) Write(message.ToStr().Value + ":\n");
            if (args.Items[2] is PyTraceback traceback)
            {
                try
                {
                    Write("Traceback (most recent call last):\n");
                    for (var tb = traceback; tb != null; tb = tb.Next)
                    {
                        Write($"  File \"{tb.Frame.Code.FileName}\", line {tb.LineNo}, in {tb.Frame.Code.Name}\n");
                        var lines = tb.Frame.Code.SourceLines;
                        if (lines != null && tb.LineNo > 0 && tb.LineNo <= lines.Count)
                            Write("    " + lines[tb.LineNo - 1].Trim() + "\n");
                    }
                }
                catch (PythonException) { }
            }
            if (args.Items[0] is not PyType type) return;
            PyObject module = null;
            PyObject name = null;
            try { module = type.GetAttribute("__module__"); }
            catch (PythonException) { }
            try { name = type.GetAttribute("__qualname__"); }
            catch (PythonException) { }
            if (module is not PyString moduleName) Write("<unknown>");
            else if (moduleName.Value != "builtins" && moduleName.Value != "__main__")
                Write(moduleName.Value + ".");
            Write(name is PyString qualifiedName ? qualifiedName.Value : "<unknown>");
            if (value is not PyNone)
            {
                Write(": ");
                string text;
                try { text = FormatObject(value, false); }
                catch (PythonException) { text = "<exception str() failed>"; }
                Write(text);
            }
            Write("\n");
            if (console)
            {
                if (stream is Modules.PyFile file && file.Name == "stdout") Console.Out.Flush();
                else Console.Error.Flush();
            }
            else stream.GetAttribute("flush").Call(Array.Empty<PyObject>(), null);
        }
    }
}
