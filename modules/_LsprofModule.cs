using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_lsprof.c: the fast profiler behind cProfile, a sys.monitoring tool
    /// (PY_MONITORING_PROFILER_ID). The rotating trees of entries and subentries keyed by code
    /// object or PyMethodDef are dictionaries keyed by identity here; getstats enumerates them in
    /// insertion order where CPython's tree goes by address, which no caller relies on.
    /// </summary>
    internal static class _LsprofModule
    {
        // Written by Brett Rosen and Ted Czotter

        private const int POF_ENABLED = 0x001, POF_SUBCALLS = 0x002, POF_BUILTINS = 0x004, POF_EXT_TIMER = 0x008, POF_NOMEMORY = 0x100;

        /// <summary>_lsprof_state</summary>
        internal static readonly PyType ProfilerType = MakeProfilerType();

        // PyStructSequence_NewType(&profiler_entry_desc) and (&profiler_subentry_desc)
        internal static readonly PyStructSequenceType StatsEntryType = new("_lsprof", "profiler_entry",
            new[] { "code", "callcount", "reccallcount", "totaltime", "inlinetime", "calls" }, heap: true);

        internal static readonly PyStructSequenceType StatsSubentryType = new("_lsprof", "profiler_subentry",
            new[] { "code", "callcount", "reccallcount", "totaltime", "inlinetime" }, heap: true);

        /// <summary>ProfilerSubEntry: a function called from another function.</summary>
        internal sealed class ProfilerSubEntry
        {
            internal long Tt, It, Callcount, Recursivecallcount, RecursionLevel;
        }

        /// <summary>ProfilerEntry: a function or user defined block.</summary>
        internal sealed class ProfilerEntry
        {
            internal PyObject UserObj; // PyCodeObject, or a descriptive str for builtins
            internal long Tt;          // total time in this entry
            internal long It;          // inline time in this entry (not in subcalls)
            internal long Callcount;   // how many times this was called
            internal long Recursivecallcount; // how many times called recursively
            internal long RecursionLevel;
            internal Dictionary<ProfilerEntry, ProfilerSubEntry> Calls;
        }

        /// <summary>ProfilerContext</summary>
        internal sealed class ProfilerContext
        {
            internal long T0, Subt;
            internal ProfilerContext Previous;
            internal ProfilerEntry CtxEntry;
        }

        /// <summary>ProfilerObject</summary>
        internal sealed class Profiler : PyObject
        {
            internal Dictionary<object, ProfilerEntry> ProfilerEntries = new(ReferenceEqualityComparer.Instance);
            internal ProfilerContext CurrentProfilerContext;
            internal ProfilerContext FreelistProfilerContext;
            internal int Flags;
            internal PyObject ExternalTimer;
            internal double ExternalTimerUnit;
            internal int ToolId;
            internal PyObject Missing;

            internal Profiler(PyType type)
            {
                if (!ReferenceEquals(type, ProfilerType))
                    SetCustomType(type);
            }

            public override PyType GetPyType() => _customType ?? ProfilerType;
        }

        private static Profiler Self(PyObject self) => self as Profiler
            ?? throw PyTypeError.Create($"descriptor requires a '_lsprof.Profiler' object but received a '{self.GetTypeName()}'");

        // CallExternalTimer
        private static long CallExternalTimer(Profiler pObj)
        {
            PyObject o;
            // External timer can do arbitrary things so we need a flag to prevent horrible things to happen
            pObj.Flags |= POF_EXT_TIMER;
            try
            {
                o = pObj.ExternalTimer.Call(Array.Empty<PyObject>(), null);
            }
            catch (PythonException error)
            {
                PyUnraisable.Report(error, pObj.ExternalTimer);
                return 0;
            }
            finally
            {
                pObj.Flags &= ~POF_EXT_TIMER;
            }
            try
            {
                // An integer that will be scaled in getstats, or a double measured in seconds that
                // becomes a large integer as the profiler works with _PyTime_t internally.
                return pObj.ExternalTimerUnit > 0.0
                    ? PyTime.FromNanosecondsObject(o)
                    : PyTime.FromSecondsObject(o, PyTimeRound.Floor);
            }
            catch (PythonException error)
            {
                PyUnraisable.Report(error, pObj.ExternalTimer);
                return 0;
            }
        }

        // call_timer
        private static long CallTimer(Profiler pObj) =>
            pObj.ExternalTimer != null ? CallExternalTimer(pObj) : PyTime.GetPerfCounter();

        /// <summary>
        /// normalizeUserObj: a built-in function becomes a descriptive string, because of built-in
        /// methods - keeping a reference to __self__ is probably not a good idea.
        /// </summary>
        private static PyObject NormalizeUserObj(PyObject obj)
        {
            if (!PyLegacyTracing.IsCFunction(obj) || obj is not ICFunction fn)
                return obj;
            PyObject mself;
            try { mself = fn.MethodSelf; }
            catch (PythonException) { mself = null; }
            var mod = PyCFunction.Module(obj);
            if (mself == null)
            {
                // built-in function: look up the module name
                PyObject modname = null;
                if (mod is PyString)
                    modname = mod;
                else if (mod is PyModule module)
                    modname = module.ModuleDict.TryGetValue("__name__", out var n) && n is PyString ? n : null;
                if (modname is PyString text && text.Value != "builtins")
                    return PyString.From($"<{text.Value}.{fn.MethodName}>");
                return PyString.From($"<{fn.MethodName}>");
            }
            // built-in method: try to return repr(getattr(type(__self__), __name__))
            var mo = mself.GetPyType().LookupSpecial(fn.MethodName);
            if (mo != null)
            {
                try { return mo.ToRepr(); }
                catch (PythonException) { }
            }
            // Otherwise, use __module__
            if (mod is PyString)
                return PyString.From($"<built-in method {mod.ToStr().Value}.{fn.MethodName}>");
            return PyString.From($"<built-in method {fn.MethodName}>");
        }

        // newProfilerEntry
        private static ProfilerEntry NewProfilerEntry(Profiler pObj, object key, PyObject userObj)
        {
            var self = new ProfilerEntry { UserObj = NormalizeUserObj(userObj) };
            pObj.ProfilerEntries[key] = self;
            return self;
        }

        // getEntry
        private static ProfilerEntry GetEntry(Profiler pObj, object key) =>
            pObj.ProfilerEntries.TryGetValue(key, out var entry) ? entry : null;

        // getSubEntry
        private static ProfilerSubEntry GetSubEntry(ProfilerEntry caller, ProfilerEntry entry) =>
            caller.Calls != null && caller.Calls.TryGetValue(entry, out var sub) ? sub : null;

        // newSubEntry
        private static ProfilerSubEntry NewSubEntry(ProfilerEntry caller, ProfilerEntry entry)
        {
            var self = new ProfilerSubEntry();
            (caller.Calls ??= new Dictionary<ProfilerEntry, ProfilerSubEntry>(ReferenceEqualityComparer.Instance))[entry] = self;
            return self;
        }

        // clearEntries
        private static void ClearEntries(Profiler pObj)
        {
            pObj.ProfilerEntries = new Dictionary<object, ProfilerEntry>(ReferenceEqualityComparer.Instance);
            // release the memory hold by the ProfilerContexts
            pObj.CurrentProfilerContext = null;
            pObj.FreelistProfilerContext = null;
        }

        // initContext
        private static void InitContext(Profiler pObj, ProfilerContext self, ProfilerEntry entry)
        {
            self.CtxEntry = entry;
            self.Subt = 0;
            self.Previous = pObj.CurrentProfilerContext;
            pObj.CurrentProfilerContext = self;
            ++entry.RecursionLevel;
            if ((pObj.Flags & POF_SUBCALLS) != 0 && self.Previous != null)
            {
                // find or create an entry for me in my caller's entry
                var caller = self.Previous.CtxEntry;
                var subentry = GetSubEntry(caller, entry) ?? NewSubEntry(caller, entry);
                ++subentry.RecursionLevel;
            }
            self.T0 = CallTimer(pObj);
        }

        // Stop
        private static void Stop(Profiler pObj, ProfilerContext self, ProfilerEntry entry)
        {
            long tt = CallTimer(pObj) - self.T0;
            long it = tt - self.Subt;
            if (self.Previous != null)
                self.Previous.Subt += tt;
            pObj.CurrentProfilerContext = self.Previous;
            if (--entry.RecursionLevel == 0)
                entry.Tt += tt;
            else
                ++entry.Recursivecallcount;
            entry.It += it;
            entry.Callcount++;
            if ((pObj.Flags & POF_SUBCALLS) != 0 && self.Previous != null)
            {
                // find or create an entry for me in my caller's entry
                var caller = self.Previous.CtxEntry;
                var subentry = GetSubEntry(caller, entry);
                if (subentry != null)
                {
                    if (--subentry.RecursionLevel == 0)
                        subentry.Tt += tt;
                    else
                        ++subentry.Recursivecallcount;
                    subentry.It += it;
                    ++subentry.Callcount;
                }
            }
        }

        /// <summary>
        /// ptrace_enter_call: entering a call to the function identified by key (a code object or
        /// a PyMethodDef). The exception in flight, if any, is not this function's to disturb.
        /// </summary>
        private static void PtraceEnterCall(Profiler pObj, object key, PyObject userObj)
        {
            var profEntry = GetEntry(pObj, key) ?? NewProfilerEntry(pObj, key, userObj);
            // grab a ProfilerContext out of the free list
            var pContext = pObj.FreelistProfilerContext;
            if (pContext != null)
                pObj.FreelistProfilerContext = pContext.Previous;
            else
                // free list exhausted, allocate a new one
                pContext = new ProfilerContext();
            InitContext(pObj, pContext, profEntry);
        }

        // ptrace_leave_call: leaving a call to the function identified by key
        private static void PtraceLeaveCall(Profiler pObj, object key)
        {
            var pContext = pObj.CurrentProfilerContext;
            if (pContext == null)
                return;
            var profEntry = GetEntry(pObj, key);
            if (profEntry != null)
                Stop(pObj, pContext, profEntry);
            else
                pObj.CurrentProfilerContext = pContext.Previous;
            // put pContext into the free list
            pContext.Previous = pObj.FreelistProfilerContext;
            pObj.FreelistProfilerContext = pContext;
        }

        // pending_exception
        private static void PendingException(Profiler pObj)
        {
            if ((pObj.Flags & POF_NOMEMORY) != 0)
            {
                pObj.Flags -= POF_NOMEMORY;
                throw new PythonException(new PyMemoryError("memory was exhausted while profiling"));
            }
        }

        // statsForSubEntry
        private static PyObject StatsForSubEntry(ProfilerEntry entry, ProfilerSubEntry sentry, double factor) =>
            new PyStructSequence(StatsSubentryType, new PyObject[]
            {
                entry.UserObj, PyInt.From(sentry.Callcount), PyInt.From(sentry.Recursivecallcount),
                new PyFloat(factor * sentry.Tt), new PyFloat(factor * sentry.It),
            });

        // statsForEntry
        private static void StatsForEntry(ProfilerEntry entry, double factor, PyList list)
        {
            if (entry.Callcount == 0)
                return; // skip
            PyObject sublist;
            if (entry.Calls != null && entry.Calls.Count != 0)
            {
                var calls = new PyList();
                foreach (var pair in entry.Calls)
                    calls.Append(StatsForSubEntry(pair.Key, pair.Value, factor));
                sublist = calls;
            }
            else
                sublist = PyNone.Instance;
            list.Append(new PyStructSequence(StatsEntryType, new PyObject[]
            {
                entry.UserObj, PyInt.From(entry.Callcount), PyInt.From(entry.Recursivecallcount),
                new PyFloat(factor * entry.Tt), new PyFloat(factor * entry.It), sublist,
            }));
        }

        // _lsprof_Profiler_getstats_impl
        private static PyObject Getstats(Profiler self)
        {
            PendingException(self);
            double factor;
            if (self.ExternalTimer == null || self.ExternalTimerUnit == 0.0)
                factor = 1.0 / PyTime.FromSeconds(1);
            else
                factor = self.ExternalTimerUnit;
            var list = new PyList();
            foreach (var entry in self.ProfilerEntries.Values)
                StatsForEntry(entry, factor, list);
            return list;
        }

        // setSubcalls
        private static void SetSubcalls(Profiler pObj, int nvalue)
        {
            if (nvalue == 0)
                pObj.Flags &= ~POF_SUBCALLS;
            else if (nvalue > 0)
                pObj.Flags |= POF_SUBCALLS;
        }

        // setBuiltins
        private static void SetBuiltins(Profiler pObj, int nvalue)
        {
            if (nvalue == 0)
                pObj.Flags &= ~POF_BUILTINS;
            else if (nvalue > 0)
                pObj.Flags |= POF_BUILTINS;
        }

        // pystart_callback
        private static PyObject PystartCallback(Profiler self, PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create($"_pystart_callback expected 2 arguments, got {args.Length}");
            var code = args[0];
            PtraceEnterCall(self, code, code);
            return PyNone.Instance;
        }

        // pyreturn_callback
        private static PyObject PyreturnCallback(Profiler self, PyObject[] args)
        {
            if (args.Length < 3)
                throw PyTypeError.Create($"_pyreturn_callback expected 3 arguments, got {args.Length}");
            PtraceLeaveCall(self, args[0]);
            return PyNone.Instance;
        }

        /// <summary>
        /// The PyMethodDef a builtin_function_or_method calls, which identifies a builtin's entry:
        /// the method descriptor a bound method came from, or the function object itself.
        /// </summary>
        private static object MethodDef(PyObject cfunc) => cfunc switch
        {
            PyBoundMethodDescriptor bound => (object)bound.MethodDescriptor ?? bound.Callable,
            _ => cfunc,
        };

        // get_cfunc_from_callable
        private static PyObject GetCfuncFromCallable(PyObject callable, PyObject selfArg, PyObject missing)
        {
            if (PyLegacyTracing.IsCFunction(callable))
                return callable;
            if (ReferenceEquals(callable.GetPyType(), PyType.MethodDescriptorType))
            {
                // For backwards compatibility need to convert to builtin method. If no arg, skip.
                if (ReferenceEquals(selfArg, missing))
                    return null;
                PyObject meth;
                try
                {
                    meth = ((IDescriptor)callable).Get(selfArg, selfArg.GetPyType());
                }
                catch (PythonException)
                {
                    return null;
                }
                if (PyLegacyTracing.IsCFunction(meth))
                    return meth;
            }
            return null;
        }

        // ccall_callback
        private static PyObject CcallCallback(Profiler self, PyObject[] args)
        {
            if (args.Length < 4)
                throw PyTypeError.Create($"_ccall_callback expected 4 arguments, got {args.Length}");
            if ((self.Flags & POF_BUILTINS) != 0)
            {
                var cfunc = GetCfuncFromCallable(args[2], args[3], self.Missing);
                if (cfunc != null)
                    PtraceEnterCall(self, MethodDef(cfunc), cfunc);
            }
            return PyNone.Instance;
        }

        // creturn_callback
        private static PyObject CreturnCallback(Profiler self, PyObject[] args)
        {
            if (args.Length < 4)
                throw PyTypeError.Create($"_creturn_callback expected 4 arguments, got {args.Length}");
            if ((self.Flags & POF_BUILTINS) != 0)
            {
                var cfunc = GetCfuncFromCallable(args[2], args[3], self.Missing);
                if (cfunc != null)
                    PtraceLeaveCall(self, MethodDef(cfunc));
            }
            return PyNone.Instance;
        }

        // callback_table
        private static readonly (int Event, string CallbackMethod)[] CallbackTable =
        {
            (PyInstrumentation.PY_START, "_pystart_callback"),
            (PyInstrumentation.PY_RESUME, "_pystart_callback"),
            (PyInstrumentation.PY_THROW, "_pystart_callback"),
            (PyInstrumentation.PY_RETURN, "_pyreturn_callback"),
            (PyInstrumentation.PY_YIELD, "_pyreturn_callback"),
            (PyInstrumentation.PY_UNWIND, "_pyreturn_callback"),
            (PyInstrumentation.CALL, "_ccall_callback"),
            (PyInstrumentation.C_RETURN, "_creturn_callback"),
            (PyInstrumentation.C_RAISE, "_creturn_callback"),
        };

        private static PyObject Monitoring() => PyImportSystem.Import("sys").GetAttribute("monitoring");

        // profiler_enable: "|pp:enable"
        private static PyObject ProfilerEnable(Profiler self, PyObject[] args, PyDict kwds)
        {
            var parsed = PyArgs.Unpack("enable", args, kwds, new[] { "subcalls", "builtins" }, 0, 0, 2);
            int subcalls = parsed[0] == null ? -1 : parsed[0].IsTrue() ? 1 : 0;
            int builtins = parsed[1] == null ? -1 : parsed[1].IsTrue() ? 1 : 0;
            SetSubcalls(self, subcalls);
            SetBuiltins(self, builtins);

            var monitoring = Monitoring();
            try
            {
                monitoring.GetAttribute("use_tool_id").Call(new PyObject[] { PyInt.From(self.ToolId), PyString.From("cProfile") }, null);
            }
            catch (PythonException)
            {
                throw PyValueError.Create("Another profiling tool is already active");
            }

            int allEvents = 0;
            foreach (var (ev, callbackMethod) in CallbackTable)
            {
                int eventBit = 1 << ev;
                var callback = self.GetAttribute(callbackMethod);
                monitoring.GetAttribute("register_callback").Call(new PyObject[] { PyInt.From(self.ToolId), PyInt.From(eventBit), callback }, null);
                allEvents |= eventBit;
            }
            monitoring.GetAttribute("set_events").Call(new PyObject[] { PyInt.From(self.ToolId), PyInt.From(allEvents) }, null);
            self.Flags |= POF_ENABLED;
            return PyNone.Instance;
        }

        // flush_unmatched
        private static void FlushUnmatched(Profiler pObj)
        {
            while (pObj.CurrentProfilerContext != null)
            {
                var pContext = pObj.CurrentProfilerContext;
                var profEntry = pContext.CtxEntry;
                if (profEntry != null)
                    Stop(pObj, pContext, profEntry);
                else
                    pObj.CurrentProfilerContext = pContext.Previous;
            }
        }

        // profiler_disable
        private static PyObject ProfilerDisable(Profiler self)
        {
            if ((self.Flags & POF_EXT_TIMER) != 0)
                throw PyRuntimeError.Create("cannot disable profiler in external timer");
            if ((self.Flags & POF_ENABLED) != 0)
            {
                var monitoring = Monitoring();
                foreach (var (ev, _) in CallbackTable)
                    monitoring.GetAttribute("register_callback").Call(new PyObject[] { PyInt.From(self.ToolId), PyInt.From(1 << ev), PyNone.Instance }, null);
                monitoring.GetAttribute("set_events").Call(new PyObject[] { PyInt.From(self.ToolId), PyInt.From(0) }, null);
                monitoring.GetAttribute("free_tool_id").Call(new PyObject[] { PyInt.From(self.ToolId) }, null);
                self.Flags &= ~POF_ENABLED;
                FlushUnmatched(self);
            }
            PendingException(self);
            return PyNone.Instance;
        }

        // profiler_clear
        private static PyObject ProfilerClear(Profiler pObj)
        {
            if ((pObj.Flags & POF_EXT_TIMER) != 0)
                throw PyRuntimeError.Create("cannot clear profiler in external timer");
            ClearEntries(pObj);
            return PyNone.Instance;
        }

        // profiler_init: "|Odpp:Profiler"
        private static PyObject ProfilerInit(Profiler pObj, PyObject[] args, PyDict kw)
        {
            var parsed = PyArgs.Unpack("Profiler", args, kw, new[] { "timer", "timeunit", "subcalls", "builtins" }, 0, 0, 4);
            var timer = parsed[0];
            double timeunit = parsed[1] == null ? 0.0 : parsed[1].ToFloat();
            int subcalls = parsed[2] == null ? 1 : parsed[2].IsTrue() ? 1 : 0;
            int builtins = parsed[3] == null ? 1 : parsed[3].IsTrue() ? 1 : 0;
            SetSubcalls(pObj, subcalls);
            SetBuiltins(pObj, builtins);
            pObj.ExternalTimerUnit = timeunit;
            pObj.ExternalTimer = timer;
            pObj.ToolId = PyInstrumentation.PROFILER_ID;
            pObj.Missing = Monitoring().GetAttribute("MISSING");
            return PyNone.Instance;
        }

        private const string ProfilerDoc =
            "Profiler(timer=None, timeunit=None, subcalls=True, builtins=True)\n\n" +
            "    Builds a profiler object using the specified timer function.\n" +
            "    The default timer is a fast built-in one based on real time.\n" +
            "    For custom timer functions returning integers, timeunit can\n" +
            "    be a float specifying a scale (i.e. how long each integer unit\n" +
            "    is, in seconds).\n";

        private static PyMethodDescriptor Method(PyType type, string name, Func<Profiler, PyObject[], PyDict, PyObject> body)
        {
            var method = new PyMethodDescriptor(name, type, (self, args, kwargs) => body(Self(self), args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            return method;
        }

        private static void NoArgs(string name, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"Profiler.{name}() takes no keyword arguments");
            if (args.Length != 0)
                throw PyTypeError.Create($"Profiler.{name}() takes no arguments ({args.Length} given)");
        }

        private static void FastNoKeywords(string name, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"Profiler.{name}() takes no keyword arguments");
        }

        // _lsprof_profiler_type_spec: "_lsprof.Profiler", BASETYPE | IMMUTABLETYPE
        private static PyType MakeProfilerType()
        {
            var type = new PyType("Profiler", new[] { PyType.ObjectType }, "_lsprof");
            type.TypeDict["__module__"] = PyString.From("_lsprof");
            type.TypeDict["__doc__"] = PyString.From(ProfilerDoc);
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => new Profiler((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) => ProfilerInit(Self(self), args, kwargs));
            // profiler_methods
            type.TypeDict["getstats"] = Method(type, "getstats", (self, args, kwargs) =>
            {
                if (args.Length != 0 || kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("getstats() takes no arguments");
                return Getstats(self);
            });
            type.TypeDict["enable"] = Method(type, "enable", ProfilerEnable);
            type.TypeDict["disable"] = Method(type, "disable", (self, args, kwargs) =>
            {
                NoArgs("disable", args, kwargs);
                return ProfilerDisable(self);
            });
            type.TypeDict["clear"] = Method(type, "clear", (self, args, kwargs) =>
            {
                NoArgs("clear", args, kwargs);
                return ProfilerClear(self);
            });
            type.TypeDict["_pystart_callback"] = Method(type, "_pystart_callback", (self, args, kwargs) =>
            {
                FastNoKeywords("_pystart_callback", kwargs);
                return PystartCallback(self, args);
            });
            type.TypeDict["_pyreturn_callback"] = Method(type, "_pyreturn_callback", (self, args, kwargs) =>
            {
                FastNoKeywords("_pyreturn_callback", kwargs);
                return PyreturnCallback(self, args);
            });
            type.TypeDict["_ccall_callback"] = Method(type, "_ccall_callback", (self, args, kwargs) =>
            {
                FastNoKeywords("_ccall_callback", kwargs);
                return CcallCallback(self, args);
            });
            type.TypeDict["_creturn_callback"] = Method(type, "_creturn_callback", (self, args, kwargs) =>
            {
                FastNoKeywords("_creturn_callback", kwargs);
                return CreturnCallback(self, args);
            });
            return type;
        }

        /// <summary>PyInit__lsprof and _lsprof_exec.</summary>
        internal static PyModule Create()
        {
            var module = new PyModule("_lsprof");
            module.ApplyNativeDoc();
            // PyModule_AddType
            module.ModuleDict["Profiler"] = ProfilerType;
            module.ModuleDict["profiler_entry"] = StatsEntryType;
            module.ModuleDict["profiler_subentry"] = StatsSubentryType;
            return module;
        }
    }
}
