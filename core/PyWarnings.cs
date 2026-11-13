using System;

namespace SharpPy
{
    /// <summary>
    /// Python/_warnings.c of CPython v3.12.15: the _warnings module (filters, _onceregistry,
    /// _defaultaction, warn, warn_explicit, _filters_mutated) and the C API every warning of the
    /// interpreter goes through (PyErr_WarnEx, PyErr_WarnExplicitObject, PyErr_ResourceWarning,
    /// _PyErr_WarnUnawaitedCoroutine). Lib/warnings.py takes its functions from here; a warning
    /// shows through warnings._showwarnmsg once that module is imported, and is written to
    /// sys.stderr by show_warning before.
    /// </summary>
    internal static class PyWarnings
    {
        // WarningsState
        private static readonly PyList Filters = InitFilters();
        private static PyList _filters = Filters;
        private static PyDict _onceRegistry = new();
        private static PyObject _defaultAction = PyString.From("default");
        private static long _filtersVersion;

        private const string LegacyThrowMessage = "the (type, exc, tb) signature of throw() is deprecated, use the single-arg signature instead.";

        internal static void DeprecatedThrow(bool asynchronous = false) => Deprecated(asynchronous
            ? "the (type, exc, tb) signature of athrow() is deprecated, use the single-arg signature instead." : LegacyThrowMessage);

        /// <summary>PyErr_WarnEx(category, text, stack_level): category None is RuntimeWarning.</summary>
        internal static void Warn(PyType category, string message, int stacklevel) =>
            DoWarn(PyString.From(message), category ?? PyType.RuntimeWarningType, stacklevel, null, null);

        /// <summary>PyErr_WarnEx(PyExc_DeprecationWarning, message, 1).</summary>
        internal static void Deprecated(string message) => Warn(PyType.DeprecationWarningType, message, 1);

        /// <summary>PyErr_ResourceWarning(source, stack_level, ...): the source object goes to the WarningMessage.</summary>
        internal static void ResourceWarning(PyObject source, string message, int stacklevel) =>
            DoWarn(PyString.From(message), PyType.ResourceWarningType, stacklevel, source, null);

        /// <summary>PyErr_WarnExplicitObject: category None is RuntimeWarning.</summary>
        internal static void WarnExplicit(PyObject category, PyObject message, PyObject filename, int lineno, PyObject module, PyObject registry) =>
            WarnExplicitCore(category ?? PyType.RuntimeWarningType, message, filename, lineno, module, registry, null, null);

        // init_filters
        private static PyList InitFilters()
        {
            PyObject Filter(PyType category, string action, string module) => PyTuple.From(new PyObject[]
                { PyString.From(action), PyNone.Instance, category, module == null ? PyNone.Instance : PyString.From(module), PyInt.From(0) });
            return new PyList(new[]
            {
                Filter(PyType.DeprecationWarningType, "default", "__main__"),
                Filter(PyType.DeprecationWarningType, "ignore", null),
                Filter(PyType.PendingDeprecationWarningType, "ignore", null),
                Filter(PyType.ImportWarningType, "ignore", null),
                Filter(PyType.ResourceWarningType, "ignore", null),
            });
        }

        // check_matched: None matches, an exact str compares, anything else is a regex's match().
        private static bool CheckMatched(PyObject filter, PyObject text)
        {
            if (filter is PyNone)
                return true;
            if (filter.IsExactly(PyType.StrType))
                return text is PyString s && ((PyString)filter).Value == s.Value;
            return filter.GetAttribute("match").Call(new[] { text }, null).IsTrue();
        }

        // get_warnings_attr: the attribute of the warnings module; with tryImport it is imported.
        private static PyObject WarningsAttr(string name, bool tryImport)
        {
            PyObject module;
            if (tryImport)
            {
                try
                {
                    module = PyImportSystem.Import("warnings");
                }
                catch (PythonException error) when (error.PyException is PyImportError)
                {
                    return null;
                }
            }
            else
            {
                if (!PyImportSystem.TryGetModule("warnings", out var found))
                    return null;
                module = found;
            }
            return module.LookupAttribute(name);
        }

        // get_once_registry
        private static PyDict OnceRegistry()
        {
            var registry = WarningsAttr("onceregistry", false);
            if (registry == null)
                return _onceRegistry;
            if (registry is not PyDict dict)
                throw PyTypeError.Create($"_warnings.onceregistry must be a dict, not '{registry.GetPyType().FullName}'");
            return _onceRegistry = dict;
        }

        // get_default_action
        private static PyObject DefaultAction()
        {
            var action = WarningsAttr("defaultaction", false);
            if (action == null)
                return _defaultAction;
            if (action is not PyString)
                throw PyTypeError.Create($"_warnings.defaultaction must be a string, not '{action.GetPyType().FullName}'");
            return _defaultAction = action;
        }

        // get_filter: the action of the first filter that matches, else the default action.
        private static PyString GetFilter(PyObject category, PyObject text, long lineno, PyObject module, out PyObject item)
        {
            var warningsFilters = WarningsAttr("filters", false);
            if (warningsFilters != null)
                _filters = warningsFilters as PyList;
            var filters = warningsFilters ?? _filters;
            if (filters is not PyList list)
                throw PyValueError.Create("_warnings.filters must be a list");
            // WarningsState.filters could change while we are iterating over it.
            for (int i = 0; i < list.RawCount(); i++)
            {
                var candidate = list.RawGet(i);
                if (candidate is not PyTuple tuple || tuple.Items.Length != 5)
                    throw PyValueError.Create($"_warnings.filters item {i} isn't a 5-tuple");
                PyObject action = tuple.Items[0], msg = tuple.Items[1], cat = tuple.Items[2], mod = tuple.Items[3], lineObject = tuple.Items[4];
                if (action is not PyString actionText)
                    throw PyTypeError.Create($"action must be a string, not '{action.GetPyType().FullName}'");
                bool goodMessage = CheckMatched(msg, text);
                bool goodModule = CheckMatched(mod, module);
                bool isSubclass = PyAbstract.IsSubclass(category, cat);
                long line = PyArgs.LongAsSsize(lineObject);
                if (goodMessage && isSubclass && goodModule && (line == 0 || lineno == line))
                {
                    item = tuple;
                    return actionText;
                }
            }
            item = PyNone.Instance;
            return (PyString)DefaultAction();
        }

        // already_warned: the registry is cleared when its version is not the filters'.
        private static bool AlreadyWarned(PyDict registry, PyObject key, bool shouldSet)
        {
            var versionKey = PyString.From("version");
            if (!registry.TryGetStoredValue(versionKey, out var version) || !version.IsExactly(PyType.IntType)
                || ((PyInt)version).Value != _filtersVersion)
            {
                registry.Clear();
                registry.SetItemStorage(versionKey, PyInt.From(_filtersVersion));
            }
            else if (registry.TryGetStoredValue(key, out var warned) && warned.IsTrue())
                return true;
            if (shouldSet)
                registry.SetItemStorage(key, PyBool.True);
            return false;
        }

        // normalize_module: the file name without ".py", "<unknown>" for none.
        private static PyObject NormalizeModule(PyObject filename)
        {
            string text = ((PyString)filename).Value;
            if (text.Length == 0)
                return PyString.From("<unknown>");
            return text.EndsWith(".py", StringComparison.Ordinal) ? PyString.From(text.Substring(0, text.Length - 3)) : filename;
        }

        // update_registry
        private static bool UpdateRegistry(PyDict registry, PyObject text, PyObject category, bool addZero) =>
            AlreadyWarned(registry, addZero ? PyTuple.From(new[] { text, category, PyInt.From(0) }) : PyTuple.From(new[] { text, category }), true);

        // show_warning: "filename:lineno: category: text" and the source line on sys.stderr; it
        // never fails.
        private static void ShowWarning(PyObject filename, int lineno, PyObject text, PyObject category, PyObject sourceline)
        {
            try
            {
                var name = category.GetAttribute("__name__");
                var sys = PyImportSystem.Import("sys");
                if (!sys.ModuleDict.TryGetValue("stderr", out var stderr))
                {
                    Console.Error.WriteLine("lost sys.stderr");
                    return;
                }
                var write = stderr.GetAttribute("write");
                void Write(PyObject value) => write.Call(new[] { value }, null);
                Write(filename.ToStr());
                Write(PyString.From($":{lineno}: "));
                Write(name.ToStr());
                Write(PyString.From(": "));
                Write(text.ToStr());
                Write(PyString.From("\n"));
                if (sourceline != null)
                {
                    // CPython writes the line itself here, not the truncated copy it makes.
                    Write(sourceline.ToStr());
                    Write(PyString.From("\n"));
                }
                else
                    PyErrorDisplay.DisplaySourceLine(stderr, (filename as PyString)?.Value, lineno, 2);
            }
            catch (PythonException)
            {
                // PyErr_Clear
            }
        }

        // call_show_warning: warnings._showwarnmsg(WarningMessage(...)), else show_warning.
        private static void CallShowWarning(PyObject category, PyObject text, PyObject message, PyObject filename, int lineno,
            PyObject linenoObject, PyObject sourceline, PyObject source)
        {
            var show = WarningsAttr("_showwarnmsg", source != null);
            if (show == null)
            {
                ShowWarning(filename, lineno, text, category, sourceline);
                return;
            }
            if (!show.IsCallable())
                throw PyTypeError.Create("warnings._showwarnmsg() must be set to a callable");
            var messageClass = WarningsAttr("WarningMessage", false)
                ?? throw PyRuntimeError.Create("unable to get warnings.WarningMessage");
            var warning = messageClass.Call(new[] { message, category, filename, linenoObject, PyNone.Instance, PyNone.Instance,
                source ?? PyNone.Instance }, null);
            show.Call(new[] { warning }, null);
        }

        // warn_explicit; module null is "from the file name", registry null or None is none.
        private static PyObject WarnExplicitCore(PyObject category, PyObject message, PyObject filename, int lineno,
            PyObject module, PyObject registry, PyObject sourceline, PyObject source)
        {
            // module is None late in shutdown, when the filters are gone: ignore the warning.
            if (module is PyNone)
                return PyNone.Instance;
            if (registry != null && registry is not PyDict && registry is not PyNone)
                throw PyTypeError.Create("'registry' must be a dict or None");
            module ??= NormalizeModule(filename);
            PyObject text;
            if (SharpPy.Modules.BuiltinsModule.IsInstance(new[] { message, (PyObject)PyType.WarningType }).IsTrue())
            {
                text = message.ToStr();
                category = message.GetPyType();
            }
            else
            {
                text = message;
                message = category.Call(new[] { message }, null);
            }
            var linenoObject = PyInt.From(lineno);
            if (source is PyNone)
                source = null;
            var key = PyTuple.From(new[] { text, category, linenoObject });
            var dictRegistry = registry as PyDict;
            if (dictRegistry != null && AlreadyWarned(dictRegistry, key, false))
                return PyNone.Instance;

            var action = GetFilter(category, text, lineno, module, out var item);
            if (action.Value == "error")
                throw new PythonException((PyBaseException)message);
            if (action.Value == "ignore")
                return PyNone.Instance;

            // Store in the registry that we've been here, *except* when the action is "always".
            bool warned = false;
            if (action.Value != "always")
            {
                dictRegistry?.SetItemStorage(key, PyBool.True);
                if (action.Value == "once")
                    warned = UpdateRegistry(dictRegistry ?? OnceRegistry(), text, category, false);
                else if (action.Value == "module")
                {
                    if (dictRegistry != null)
                        warned = UpdateRegistry(dictRegistry, text, category, false);
                }
                else if (action.Value != "default")
                    throw PyRuntimeError.Create($"Unrecognized action ({action.ToRepr().Value}) in warnings.filters:\n {item.ToRepr().Value}");
            }
            if (!warned)
                CallShowWarning(category, text, message, filename, lineno, linenoObject, sourceline, source);
            return PyNone.Instance;
        }

        // is_internal_filename: importlib's bootstrap frames do not count.
        private static bool IsInternalFilename(string filename) =>
            filename != null && filename.Contains("importlib") && filename.Contains("_bootstrap");

        private static bool IsFilenameToSkip(string filename, PyTuple skipFilePrefixes)
        {
            if (skipFilePrefixes == null || filename == null)
                return false;
            foreach (var prefix in skipFilePrefixes.Items)
                if (filename.StartsWith(((PyString)prefix).Value, StringComparison.Ordinal))
                    return true;
            return false;
        }

        // next_external_frame
        private static PyFrame NextExternalFrame(PyFrame frame, PyTuple skipFilePrefixes)
        {
            do
                frame = frame.ParentFrame;
            while (frame != null && (IsInternalFilename(frame.Code.FileName) || IsFilenameToSkip(frame.Code.FileName, skipFilePrefixes)));
            return frame;
        }

        // setup_context: the frame stack_level up, its file, line, globals' registry and __name__.
        private static void SetupContext(long stackLevel, PyTuple skipFilePrefixes, out PyObject filename, out int lineno,
            out PyObject module, out PyObject registry)
        {
            if (skipFilePrefixes != null)
                foreach (var prefix in skipFilePrefixes.Items)
                    if (prefix is not PyString)
                        throw PyTypeError.Create($"Found non-str '{prefix.GetPyType().FullName}' in skip_file_prefixes.");
            var frame = PyVM.CurrentFrame;
            // Stack level comparisons to Python code is off by one as there is no warnings-related
            // stack level to avoid.
            if (stackLevel <= 0 || (frame != null && IsInternalFilename(frame.Code.FileName)))
            {
                while (--stackLevel > 0 && frame != null)
                    frame = frame.ParentFrame;
            }
            else
            {
                while (--stackLevel > 0 && frame != null)
                    frame = NextExternalFrame(frame, skipFilePrefixes);
            }
            PyDict globals;
            if (frame == null)
            {
                globals = PyImportSystem.Import("sys").DictObject;
                filename = PyString.From("sys");
                lineno = 1;
            }
            else
            {
                globals = PyGlobalsDict.DictOf(frame.Globals);
                filename = PyString.From(frame.Code.FileName);
                lineno = frame.CurrentLineNumber;
            }
            // the globals' __warningregistry__, made when there is none; warn_explicit checks it
            var registryKey = PyString.From("__warningregistry__");
            if (!globals.TryGetStoredValue(registryKey, out registry))
            {
                registry = new PyDict();
                globals.SetItemStorage(registryKey, registry);
            }
            module = globals.TryGetStoredValue(PyString.From("__name__"), out var name) && (name is PyNone || name is PyString)
                ? name : PyString.From("<string>");
        }

        // get_category
        private static PyObject GetCategory(PyObject message, PyObject category)
        {
            if (SharpPy.Modules.BuiltinsModule.IsInstance(new[] { message, (PyObject)PyType.WarningType }).IsTrue())
                category = message.GetPyType();
            else if (category == null || category is PyNone)
                category = PyType.UserWarningType;
            bool isWarning;
            try
            {
                isWarning = PyAbstract.IsSubclass(category, PyType.WarningType);
            }
            catch (PythonException)
            {
                isWarning = false;
            }
            if (!isWarning)
                throw PyTypeError.Create($"category must be a Warning subclass, not '{category.GetPyType().FullName}'");
            return category;
        }

        // do_warn
        private static PyObject DoWarn(PyObject message, PyObject category, long stackLevel, PyObject source, PyTuple skipFilePrefixes)
        {
            SetupContext(stackLevel, skipFilePrefixes, out var filename, out int lineno, out var module, out var registry);
            return WarnExplicitCore(category, message, filename, lineno, module, registry, null, source);
        }

        // get_source_line: module_globals' loader's get_source, the line lineno.
        private static PyObject GetSourceLine(PyDict moduleGlobals, int lineno)
        {
            var loader = PyImportSystem.External.GetAttribute("_bless_my_loader").Call(new PyObject[] { moduleGlobals }, null);
            if (loader is PyNone)
                return null;
            if (!moduleGlobals.TryGetStoredValue(PyString.From("__name__"), out var moduleName))
                return null;
            var getSource = loader.LookupAttribute("get_source");
            if (getSource == null)
                return null;
            var source = getSource.Call(new[] { moduleName }, null);
            if (source is PyNone)
                return null;
            // PyUnicode_Splitlines: the str's own lines, whatever a subclass's splitlines says.
            if (source is not PyString text)
                throw PyTypeError.Create($"must be str, not {source.GetPyType().FullName}");
            var lines = PyString.SplitLinesCore(text.Value, false);
            // PyList_GetItem: an index out of range is an IndexError.
            if (lineno - 1 < 0 || lineno - 1 >= lines.Count)
                throw PyIndexError.Create("list index out of range");
            return lines[lineno - 1];
        }

        // _PyErr_WarnUnawaitedCoroutine: warnings._warn_unawaited_coroutine, else a RuntimeWarning of
        // the interpreter's; it never raises.
        internal static void WarnUnawaitedCoroutine(PyObject coroutine, string qualname)
        {
            bool warned = false;
            try
            {
                var function = WarningsAttr("_warn_unawaited_coroutine", true);
                if (function != null)
                {
                    function.Call(new[] { coroutine }, null);
                    warned = true;
                }
            }
            catch (PythonException error)
            {
                warned = error.PyException is PyRuntimeWarning;
                PyUnraisable.Report(error, coroutine);
            }
            if (!warned)
            {
                try
                {
                    DoWarn(PyString.From($"coroutine '{qualname}' was never awaited"), PyType.RuntimeWarningType, 1, coroutine, null);
                }
                catch (PythonException error)
                {
                    PyUnraisable.Report(error, coroutine);
                }
            }
        }

        /// <summary>_PyWarnings_Init and warnings_module_exec.</summary>
        internal static PyModule CreateModule()
        {
            var module = new PyModule("_warnings");
            module.ModuleDict["__doc__"] = PyString.From("_warnings provides basic warning filtering support.\nIt is a helper module to speed up interpreter start-up.");
            module.ModuleDict["filters"] = Filters;
            module.ModuleDict["_onceregistry"] = _onceRegistry;
            module.ModuleDict["_defaultaction"] = _defaultAction;
            module.ModuleDict["warn"] = new PyBuiltinFunction("warn", (args, kwargs) =>
            {
                var p = PyArgs.Unpack("warn", args, kwargs, new[] { "message", "category", "stacklevel", "source", "skip_file_prefixes" }, 0, 1, 4);
                var category = GetCategory(p[0], p[1]);
                long stacklevel = p[2] == null ? 1 : PyArgs.Ssize(p[2]);
                PyTuple skip = null;
                if (p[4] != null)
                {
                    skip = p[4] as PyTuple ?? throw PyArgs.BadArgument("warn", "argument 'skip_file_prefixes'", "tuple", p[4]);
                    if (skip.Items.Length > 0)
                        stacklevel = Math.Max(stacklevel, 2);
                    else
                        skip = null;
                }
                return DoWarn(p[0], category, stacklevel, p[3] ?? PyNone.Instance, skip);
            });
            module.ModuleDict["warn_explicit"] = new PyBuiltinFunction("warn_explicit", (args, kwargs) =>
            {
                var p = PyArgs.Unpack("warn_explicit", args, kwargs,
                    new[] { "message", "category", "filename", "lineno", "module", "registry", "module_globals", "source" }, 0, 4, 8);
                if (p[2] is not PyString filename)
                    throw PyArgs.BadArgument("warn_explicit", "argument 'filename'", "str", p[2]);
                int lineno = PyArgs.Int(p[3]);
                PyObject sourceLine = null;
                if (p[6] != null && p[6] is not PyNone)
                {
                    if (p[6] is not PyDict moduleGlobals)
                        throw PyTypeError.Create($"module_globals must be a dict, not '{p[6].GetPyType().FullName}'");
                    sourceLine = GetSourceLine(moduleGlobals, lineno);
                }
                return WarnExplicitCore(p[1], p[0], filename, lineno, p[4], p[5] ?? PyNone.Instance, sourceLine, p[7] ?? PyNone.Instance);
            });
            module.ModuleDict["_filters_mutated"] = new PyBuiltinFunction("_filters_mutated", args =>
            {
                if (args.Length != 0)
                    throw PyTypeError.Create($"_filters_mutated() takes no arguments ({args.Length} given)");
                _filtersVersion++;
                return PyNone.Instance;
            });
            return module;
        }
    }
}
