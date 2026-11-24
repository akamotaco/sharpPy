using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_pickle.c (CPython 3.12.15): the pickle accelerator. This file holds the module
    /// (PickleState, _pickle_exec, the module functions) and the helpers the pickler and the
    /// unpickler share; PicklerObject.cs and UnpicklerObject.cs hold the two types and their memo
    /// proxies, PickleBufferObject.cs Objects/picklebufobject.c.
    ///
    /// The per-module state of the C module is kept once, in static fields: the types of a native
    /// module here are static (as functools.partial and the _csv types are), so a second module
    /// object shares them, where a second PyInit__pickle in CPython makes new types.
    /// </summary>
    internal static class PickleModule
    {
        // enum HIGHEST_PROTOCOL / DEFAULT_PROTOCOL
        internal const int HIGHEST_PROTOCOL = 5;
        internal const int DEFAULT_PROTOCOL = 4;

        // enum opcode
        internal const byte MARK = (byte)'(';
        internal const byte STOP = (byte)'.';
        internal const byte POP = (byte)'0';
        internal const byte POP_MARK = (byte)'1';
        internal const byte DUP = (byte)'2';
        internal const byte FLOAT = (byte)'F';
        internal const byte INT = (byte)'I';
        internal const byte BININT = (byte)'J';
        internal const byte BININT1 = (byte)'K';
        internal const byte LONG = (byte)'L';
        internal const byte BININT2 = (byte)'M';
        internal const byte NONE = (byte)'N';
        internal const byte PERSID = (byte)'P';
        internal const byte BINPERSID = (byte)'Q';
        internal const byte REDUCE = (byte)'R';
        internal const byte STRING = (byte)'S';
        internal const byte BINSTRING = (byte)'T';
        internal const byte SHORT_BINSTRING = (byte)'U';
        internal const byte UNICODE = (byte)'V';
        internal const byte BINUNICODE = (byte)'X';
        internal const byte APPEND = (byte)'a';
        internal const byte BUILD = (byte)'b';
        internal const byte GLOBAL = (byte)'c';
        internal const byte DICT = (byte)'d';
        internal const byte EMPTY_DICT = (byte)'}';
        internal const byte APPENDS = (byte)'e';
        internal const byte GET = (byte)'g';
        internal const byte BINGET = (byte)'h';
        internal const byte INST = (byte)'i';
        internal const byte LONG_BINGET = (byte)'j';
        internal const byte LIST = (byte)'l';
        internal const byte EMPTY_LIST = (byte)']';
        internal const byte OBJ = (byte)'o';
        internal const byte PUT = (byte)'p';
        internal const byte BINPUT = (byte)'q';
        internal const byte LONG_BINPUT = (byte)'r';
        internal const byte SETITEM = (byte)'s';
        internal const byte TUPLE = (byte)'t';
        internal const byte EMPTY_TUPLE = (byte)')';
        internal const byte SETITEMS = (byte)'u';
        internal const byte BINFLOAT = (byte)'G';
        // Protocol 2.
        internal const byte PROTO = 0x80;
        internal const byte NEWOBJ = 0x81;
        internal const byte EXT1 = 0x82;
        internal const byte EXT2 = 0x83;
        internal const byte EXT4 = 0x84;
        internal const byte TUPLE1 = 0x85;
        internal const byte TUPLE2 = 0x86;
        internal const byte TUPLE3 = 0x87;
        internal const byte NEWTRUE = 0x88;
        internal const byte NEWFALSE = 0x89;
        internal const byte LONG1 = 0x8a;
        internal const byte LONG4 = 0x8b;
        // Protocol 3 (Python 3.x)
        internal const byte BINBYTES = (byte)'B';
        internal const byte SHORT_BINBYTES = (byte)'C';
        // Protocol 4
        internal const byte SHORT_BINUNICODE = 0x8c;
        internal const byte BINUNICODE8 = 0x8d;
        internal const byte BINBYTES8 = 0x8e;
        internal const byte EMPTY_SET = 0x8f;
        internal const byte ADDITEMS = 0x90;
        internal const byte FROZENSET = 0x91;
        internal const byte NEWOBJ_EX = 0x92;
        internal const byte STACK_GLOBAL = 0x93;
        internal const byte MEMOIZE = 0x94;
        internal const byte FRAME = 0x95;
        // Protocol 5
        internal const byte BYTEARRAY8 = 0x96;
        internal const byte NEXT_BUFFER = 0x97;
        internal const byte READONLY_BUFFER = 0x98;

        // enum: BATCHSIZE, FAST_NESTING_LIMIT, WRITE_BUF_SIZE, PREFETCH, FRAME_SIZE_*
        internal const int BATCHSIZE = 1000;
        internal const int FAST_NESTING_LIMIT = 50;
        internal const int WRITE_BUF_SIZE = 4096;
        internal const int PREFETCH = 8192 * 16;
        internal const int FRAME_SIZE_MIN = 4;
        internal const int FRAME_SIZE_TARGET = 64 * 1024;
        internal const int FRAME_HEADER_SIZE = 9;

        // PickleState: the exception classes, the copyreg and _compat_pickle tables, and the
        // callables the pickler stores in pickles (codecs.encode, getattr, functools.partial).
        internal static PyType PickleError;
        internal static PyType PicklingError;
        internal static PyType UnpicklingError;
        internal static PyDict DispatchTable;
        internal static PyDict ExtensionRegistry;
        internal static PyDict ExtensionCache;
        internal static PyDict InvertedRegistry;
        internal static PyDict NameMapping2to3;
        internal static PyDict ImportMapping2to3;
        internal static PyDict NameMapping3to2;
        internal static PyDict ImportMapping3to2;
        internal static PyObject CodecsEncode;
        internal static PyObject Getattr;
        internal static PyObject Partial;

        private static readonly object _excLock = new object();

        // PyInit__pickle / _pickle_exec
        internal static PyModule Create()
        {
            var module = new PyModule("_pickle");
            var d = module.ModuleDict;

            // pickle_methods
            d["dump"] = new PyBuiltinFunction("dump", (args, kwargs) => Dump(args, kwargs));
            d["dumps"] = new PyBuiltinFunction("dumps", (args, kwargs) => Dumps(args, kwargs));
            d["load"] = new PyBuiltinFunction("load", (args, kwargs) => Load(args, kwargs));
            d["loads"] = new PyBuiltinFunction("loads", (args, kwargs) => Loads(args, kwargs));

            // _pickle_exec: the types (CREATE_TYPE, made once with the static fields), then
            // PyModule_AddType of PickleBuffer, Pickler and Unpickler.
            d["PickleBuffer"] = PickleBufferObject.PickleBufferType;
            d["Pickler"] = PicklerObject.PicklerType;
            d["Unpickler"] = UnpicklerObject.UnpicklerType;

            // Initialize the exceptions.
            lock (_excLock)
            {
                if (PickleError == null)
                {
                    var pickleError = PyExceptionSlots.NewException("_pickle", "PickleError", PyType.ExceptionType);
                    PicklingError = PyExceptionSlots.NewException("_pickle", "PicklingError", pickleError);
                    UnpicklingError = PyExceptionSlots.NewException("_pickle", "UnpicklingError", pickleError);
                    PickleError = pickleError;
                }
            }
            d["PickleError"] = PickleError;
            d["PicklingError"] = PicklingError;
            d["UnpicklingError"] = UnpicklingError;

            _Pickle_InitState();
            return module;
        }

        // _Pickle_InitState
        private static void _Pickle_InitState()
        {
            Getattr = PyBuiltinsModule.Instance.ModuleDict["getattr"];

            var copyreg = PyImportSystem.Import("copyreg");
            DispatchTable = ExactDict(copyreg.GetAttribute("dispatch_table"), "copyreg.dispatch_table should be a dict, not ");
            ExtensionRegistry = ExactDict(copyreg.GetAttribute("_extension_registry"), "copyreg._extension_registry should be a dict, not ");
            InvertedRegistry = ExactDict(copyreg.GetAttribute("_inverted_registry"), "copyreg._inverted_registry should be a dict, not ");
            ExtensionCache = ExactDict(copyreg.GetAttribute("_extension_cache"), "copyreg._extension_cache should be a dict, not ");

            // Load the 2.x -> 3.x stdlib module mapping tables
            var compatPickle = PyImportSystem.Import("_compat_pickle");
            NameMapping2to3 = ExactDict(compatPickle.GetAttribute("NAME_MAPPING"), "_compat_pickle.NAME_MAPPING should be a dict, not ");
            ImportMapping2to3 = ExactDict(compatPickle.GetAttribute("IMPORT_MAPPING"), "_compat_pickle.IMPORT_MAPPING should be a dict, not ");
            // ... and the 3.x -> 2.x mapping tables
            NameMapping3to2 = ExactDict(compatPickle.GetAttribute("REVERSE_NAME_MAPPING"), "_compat_pickle.REVERSE_NAME_MAPPING should be a dict, not ");
            ImportMapping3to2 = ExactDict(compatPickle.GetAttribute("REVERSE_IMPORT_MAPPING"), "_compat_pickle.REVERSE_IMPORT_MAPPING should be a dict, not ");

            // _PyImport_GetModuleAttrString("codecs", "encode")
            CodecsEncode = PyImportSystem.Import("codecs").GetAttribute("encode");
            if (!CodecsEncode.IsCallable())
                throw PyRuntimeError.Create($"codecs.encode should be a callable, not {CodecsEncode.GetTypeName()}");

            Partial = PyImportSystem.Import("functools").GetAttribute("partial");
        }

        // The PyDict_CheckExact tests of _Pickle_InitState.
        private static PyDict ExactDict(PyObject value, string message) =>
            value is PyDict dict && value.IsExactly(PyType.DictType)
                ? dict
                : throw PyRuntimeError.Create(message + value.GetTypeName());

        // PyErr_SetString(st->PicklingError, ...) and the like.
        internal static PythonException Error(PyType type, string message) => PyExceptionSlots.Raise(type, message);

        // Whether a raised exception is an instance of type (PyErr_ExceptionMatches).
        internal static bool Matches(PythonException error, PyType type) =>
            error.PyException.GetPyType().IsSubclassOf(type);

        // _Pickle_FastCall: PyObject_CallOneArg
        internal static PyObject _Pickle_FastCall(PyObject func, PyObject obj) =>
            func.Call(new[] { obj }, null);

        // _PyObject_LookupAttr: the attribute, or null when it raised AttributeError.
        internal static PyObject LookupAttr(PyObject obj, PyString name) => obj.LookupAttribute(name);

        internal static PyObject LookupAttr(PyObject obj, string name) => obj.LookupAttribute(name);

        // PyIter_Next: the next item, or null at the end; every other error propagates.
        internal static PyObject PyIter_Next(PyObject iterator)
        {
            try
            {
                return iterator.Next();
            }
            catch (PythonException error) when (error.PyException is PyStopIteration)
            {
                return null;
            }
        }

        /// <summary>
        /// init_method_ref: the method found as an attribute of self, deconstructed when it is a
        /// Python method bound to self (to avoid the cycle pickler -> bound method -> pickler).
        /// </summary>
        internal static void init_method_ref(PyObject self, string name, out PyObject methodFunc, out PyObject methodSelf)
        {
            var func = LookupAttr(self, name);
            if (func == null)
            {
                methodSelf = null;
                methodFunc = null;
                return;
            }
            if (func is PyMethod method && ReferenceEquals(method.Instance, self))
            {
                // Deconstruct a bound Python method
                methodSelf = self;
                methodFunc = method.Function;
                return;
            }
            methodSelf = null;
            methodFunc = func;
        }

        // reconstruct_method: bind a method if it was deconstructed
        internal static PyObject reconstruct_method(PyObject func, PyObject self) =>
            self != null ? new PyMethod(self, func) : func;

        // call_method
        internal static PyObject call_method(PyObject func, PyObject self, PyObject obj) =>
            self != null ? func.Call(new[] { self, obj }, null) : func.Call(new[] { obj }, null);

        // PyUnicode_Split(name, '.', -1): the parts, each a str of the code points between dots.
        private static List<PyString> SplitDots(PyString name)
        {
            var points = name.ToCodePoints();
            var parts = new List<PyString>();
            int start = 0;
            for (int i = 0; i <= points.Length; i++)
            {
                if (i == points.Length || points[i] == '.')
                {
                    parts.Add(PyString.FromCodePoints(points[start..i]));
                    start = i + 1;
                }
            }
            return parts;
        }

        // get_dotted_path
        internal static List<PyString> get_dotted_path(PyObject obj, PyObject name)
        {
            if (name is not PyString text)
                throw PyTypeError.Create($"must be str, not {name.GetTypeName()}");
            var dottedPath = SplitDots(text);
            foreach (var subpath in dottedPath)
            {
                if (subpath.Value == "<locals>")
                {
                    if (obj == null)
                        throw PyAttributeError.Create($"Can't get local object {name.ToRepr().Value}");
                    throw PyAttributeError.Create($"Can't get local attribute {name.ToRepr().Value} on {obj.ToRepr().Value}");
                }
            }
            return dottedPath;
        }

        /// <summary>
        /// get_deep_attribute: each name looked up on the last (_PyObject_LookupAttr), null when one
        /// is missing; an error other than AttributeError propagates.
        /// </summary>
        internal static PyObject get_deep_attribute(PyObject obj, List<PyString> names, out PyObject parent)
        {
            parent = null;
            foreach (var name in names)
            {
                parent = obj;
                obj = LookupAttr(parent, name);
                if (obj == null)
                {
                    parent = null;
                    return null;
                }
            }
            return obj;
        }

        // getattribute
        internal static PyObject getattribute(PyObject obj, PyObject name, bool allowQualname)
        {
            PyObject attr;
            if (allowQualname)
            {
                var dottedPath = get_dotted_path(obj, name);
                attr = get_deep_attribute(obj, dottedPath, out _);
            }
            else
            {
                // PyObject_GetAttr: the name must be a str.
                PyObject.AttributeName(name);
                attr = LookupAttr(obj, (PyString)name);
            }
            if (attr == null)
                throw PyAttributeError.Create($"Can't get attribute {name.ToRepr().Value} on {obj.ToRepr().Value}");
            return attr;
        }

        // _checkmodule: 0 when global is reachable as module.dotted_path, else -1.
        private static int _checkmodule(PyObject moduleName, PyObject module, PyObject global, List<PyString> dottedPath)
        {
            if (module is PyNone)
                return -1;
            if (moduleName is PyString text && text.Value == "__main__")
                return -1;
            var candidate = get_deep_attribute(module, dottedPath, out _);
            if (candidate == null)
                return -1;
            if (!ReferenceEquals(candidate, global))
                return -1;
            return 0;
        }

        // whichmodule
        internal static PyObject whichmodule(PyObject global, List<PyString> dottedPath)
        {
            var moduleName = LookupAttr(global, "__module__");
            if (moduleName != null)
            {
                /* In some rare cases (e.g., bound methods of extension types),
                   __module__ can be None. If it is so, then search sys.modules for
                   the module of global. */
                if (moduleName is not PyNone)
                    return moduleName;
            }

            // Fallback on walking sys.modules: _PySys_GetRequiredAttr(&_Py_ID(modules))
            if (!PyImportSystem.TryGetModule("sys", out var sys) || !sys.ModuleDict.TryGetValue("modules", out var modules)
                || modules is PyNone)
                throw PyRuntimeError.Create("lost sys.modules");
            if (modules is PyDict dict && modules.IsExactly(PyType.DictType))
            {
                int i = 0;
                while (dict.Next(ref i, out var name, out var module))
                {
                    if (_checkmodule(name, module, global, dottedPath) == 0)
                        return name;
                }
            }
            else
            {
                var iterator = modules.GetIterator();
                PyObject name;
                while ((name = PyIter_Next(iterator)) != null)
                {
                    var module = PyAbstract.GetItem(modules, name);
                    if (_checkmodule(name, module, global, dottedPath) == 0)
                        return name;
                }
            }

            /* If no module is found, use __main__. */
            return PyString.InternFrom("__main__");
        }

        // The "p" (bool) converter of the clinic: PyObject_IsTrue.
        internal static bool PyObject_IsTrue(PyObject value) => value.IsTrue();

        /// <summary>
        /// _pickle.dump(obj, file, protocol=None, *, fix_imports=True, buffer_callback=None)
        /// (_pickle_dump_impl)
        /// </summary>
        private static PyObject Dump(PyObject[] args, PyDict kwargs)
        {
            var a = PyArgs.Unpack("dump", args, kwargs, new[] { "obj", "file", "protocol", "fix_imports", "buffer_callback" }, 0, 2, 3);
            var obj = a[0];
            var file = a[1];
            var protocol = a[2] ?? PyNone.Instance;
            bool fixImports = a[3] == null || PyObject_IsTrue(a[3]);
            var bufferCallback = a[4] ?? PyNone.Instance;

            var pickler = PicklerObject._Pickler_New();
            pickler._Pickler_SetProtocol(protocol, fixImports);
            pickler._Pickler_SetOutputStream(file);
            pickler._Pickler_SetBufferCallback(bufferCallback);
            pickler.dump(obj);
            pickler._Pickler_FlushToFile();
            return PyNone.Instance;
        }

        /// <summary>
        /// _pickle.dumps(obj, protocol=None, *, fix_imports=True, buffer_callback=None)
        /// (_pickle_dumps_impl)
        /// </summary>
        private static PyObject Dumps(PyObject[] args, PyDict kwargs)
        {
            var a = PyArgs.Unpack("dumps", args, kwargs, new[] { "obj", "protocol", "fix_imports", "buffer_callback" }, 0, 1, 2);
            var obj = a[0];
            var protocol = a[1] ?? PyNone.Instance;
            bool fixImports = a[2] == null || PyObject_IsTrue(a[2]);
            var bufferCallback = a[3] ?? PyNone.Instance;

            var pickler = PicklerObject._Pickler_New();
            pickler._Pickler_SetProtocol(protocol, fixImports);
            pickler._Pickler_SetBufferCallback(bufferCallback);
            pickler.dump(obj);
            return pickler._Pickler_GetString();
        }

        // The clinic's keyword-only fix_imports, encoding, errors and buffers of load and loads.
        private static void UnpickleOptions(string fname, PyObject[] a, int first, out bool fixImports, out string encoding,
            out string errors, out PyObject buffers)
        {
            fixImports = a[first] == null || PyObject_IsTrue(a[first]);
            encoding = a[first + 1] == null ? "ASCII" : PyArgs.ClinicStr(fname, "argument 'encoding'", "str", a[first + 1]);
            errors = a[first + 2] == null ? "strict" : PyArgs.ClinicStr(fname, "argument 'errors'", "str", a[first + 2]);
            buffers = a[first + 3];
        }

        /// <summary>
        /// _pickle.load(file, *, fix_imports=True, encoding='ASCII', errors='strict', buffers=())
        /// (_pickle_load_impl)
        /// </summary>
        private static PyObject Load(PyObject[] args, PyDict kwargs)
        {
            var a = PyArgs.Unpack("load", args, kwargs, new[] { "file", "fix_imports", "encoding", "errors", "buffers" }, 0, 1, 1);
            UnpickleOptions("load", a, 1, out bool fixImports, out string encoding, out string errors, out var buffers);

            var unpickler = UnpicklerObject._Unpickler_New();
            unpickler._Unpickler_SetInputStream(a[0]);
            unpickler._Unpickler_SetInputEncoding(encoding, errors);
            unpickler._Unpickler_SetBuffers(buffers);
            unpickler.FixImports = fixImports;
            return unpickler.load();
        }

        /// <summary>
        /// _pickle.loads(data, /, *, fix_imports=True, encoding='ASCII', errors='strict', buffers=())
        /// (_pickle_loads_impl)
        /// </summary>
        private static PyObject Loads(PyObject[] args, PyDict kwargs)
        {
            var a = PyArgs.Unpack("loads", args, kwargs, new[] { "data", "fix_imports", "encoding", "errors", "buffers" }, 1, 1, 1);
            UnpickleOptions("loads", a, 1, out bool fixImports, out string encoding, out string errors, out var buffers);

            var unpickler = UnpicklerObject._Unpickler_New();
            unpickler._Unpickler_SetStringInput(a[0]);
            unpickler._Unpickler_SetInputEncoding(encoding, errors);
            unpickler._Unpickler_SetBuffers(buffers);
            unpickler.FixImports = fixImports;
            return unpickler.load();
        }
    }
}
