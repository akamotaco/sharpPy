using SharpPy.Tools;

#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy
{
    #region Module and Import System

// Python 모듈 객체
public class PyModule : PyObject
{
    public string Name { get; private set; }
    public string FileName { get; }
    public IDictionary<string, PyObject> ModuleDict { get; }  // __dict__ (Variable == Attribute)
    public List<string> All { get; set; }  // __all__
    public bool IsInitialized { get; private set; }
    // True only for modules loaded from a real source file (via LoadModuleFromFile).
    // CPython builtins (sys, math, ...) have no __file__ and repr as "(built-in)".
    public bool HasSourceFile { get; internal set; }

    // The bundle this module came from: its submodules are looked up there.
    internal PyBundle Bundle { get; set; }

    // CPython 3.12 requirement: module.__dict__ is module.__dict__ must be True
    // Cache the PyDict wrapper to ensure identity consistency
    private PyDict _cachedDict;

    // md_dict, the one dict object of the module (frame->f_globals of its code).
    internal PyDict DictObject => _cachedDict ??= PyGlobalsDict.For(ModuleDict);

    public PyModule(string name, string fileName = null)
    {
        Name = name;
        FileName = fileName ?? $"{name}.py";
        ModuleDict = new PyNamespace();
        All = new List<string>();

        InitializeBuiltinAttributes(fileName != null);
    }
    
    private void InitializeBuiltinAttributes(bool hasFile)
    {
        ModuleDict["__name__"] = PyString.From(Name);
        // A builtin module has no __file__.
        if (hasFile && !string.IsNullOrEmpty(FileName) && !FileName.StartsWith("<", StringComparison.Ordinal))
            ModuleDict["__file__"] = PyString.From(FileName);
        ModuleDict["__doc__"] = PyString.From($"Module {Name}");

        // CPython 3.12: Set __package__
        // For packages (__init__.py): __package__ = __name__
        // For modules: __package__ = parent package name (or "" for top-level)
        bool isPackage = FileName != null && FileName.EndsWith("__init__.py", StringComparison.Ordinal);

        if (isPackage)
        {
            // Package: __package__ == __name__
            ModuleDict["__package__"] = PyString.From(Name);
        }
        else
        {
            // Module: __package__ is parent package
            int lastDot = Name.LastIndexOf('.');
            string packageName = lastDot >= 0 ? Name.Substring(0, lastDot) : "";
            ModuleDict["__package__"] = string.IsNullOrEmpty(packageName)
                ? PyNone.Instance
                : PyString.From(packageName);
        }
    }
    
    private bool _allocated;

    // ob_type: module, or the ModuleType subclass written in Python that allocated it.
    public override PyType GetPyType() => _customType ?? PyType.ModuleType;

    // new_module: a dict and nothing in it. module___init__ names it.
    internal static PyModule Allocate(PyType type)
    {
        var module = new PyModule("?") { _allocated = true };
        module.Allocated(type);
        module.ModuleDict.Clear();
        return module;
    }

    // module___init___impl
    internal void Initialize(string name, PyObject doc)
    {
        Name = name;
        ModuleDict["__name__"] = PyString.From(name);
        ModuleDict["__doc__"] = doc;
        foreach (var key in new[] { "__package__", "__loader__", "__spec__" })
            ModuleDict.TryAdd(key, PyNone.Instance);
    }

    // module_getattro; a module subclass's __getattribute__ and __getattr__ are its type's slot.
    internal override bool TpGetattroIsGeneric => false;
    protected internal override PyObject TpGetattro(string name) => ModuleGetAttro(name);

    internal PyObject ModuleGetAttro(string name)
    {
        // CPython 3.12: Objects/moduleobject.c:783 - _Py_module_getattro_impl. __dict__ and
        // __annotations__ are the module type's member and getset, found below like any other.
        // PyObject_GenericGetAttr over the module's type: a data descriptor (object's __class__)
        // comes before md_dict.
        var declared = GetPyType().FindNameInMro(name);
        if (declared != null && PyClassInstance.IsDescriptor(declared) && PyClassInstance.IsDataDescriptor(declared))
            return BindSpecialMethod(declared);

        if (ModuleDict.TryGetValue(name, out PyObject value))
            return value;

        // The rest of _PyObject_GenericGetAttrWithDict: a non-data descriptor or plain value of
        // the type; then module_getattro's own __getattr__ (PEP 562).
        if (declared != null)
            return BindSpecialMethod(declared);
        if (ModuleDict.TryGetValue("__getattr__", out var hook))
            return hook.Call(new PyObject[] { PyString.From(name) }, null);

        throw PyAttributeError.Create(ModuleDict.ContainsKey("__name__")
            ? $"module '{Name}' has no attribute '{name}'"
            : $"module has no attribute '{name}'");
    }
    
    // module_setattro; a module subclass's __setattr__ is its type's slot.
    protected internal override void TpSetattro(string name, PyObject value) => ModuleSetAttro(name, value);

    internal void ModuleSetAttro(string name, PyObject value)
    {
        // _PyObject_GenericSetAttrWithDict: a data descriptor of the type (the READONLY __dict__,
        // the __annotations__ getset, a subclass's own) first, then md_dict.
        if (PyClassInstance.FindDataDescriptorInMro(GetPyType(), name) is { } descriptor)
        {
            PyClassInstance.CallDescriptorSet(descriptor, this, value);
            return;
        }
        SetVariable(name, value);
    }
    
    // Variable 접근 (LEGB의 G 스코프)
    public PyObject GetVariable(string name)
    {
        return ModuleDict.TryGetValue(name, out PyObject value) ? value : null;
    }
    
    public void SetVariable(string name, PyObject value)
    {
        PyGlobalsDict.Store(ModuleDict, name, value);
    }

    
    // module_setattro with NULL: the name goes from the module's dict.
    protected internal override void TpDelattro(string name) => ModuleDelAttro(name);

    internal void ModuleDelAttro(string name)
    {
        // _PyObject_GenericSetAttrWithDict over md_dict: __dict__ is a READONLY member, __class__
        // object's getset, a data descriptor of a subclass deletes.
        if (PyClassInstance.FindDataDescriptorInMro(GetPyType(), name) is { } descriptor)
        {
            PyClassInstance.CallDescriptorDelete(descriptor, this);
            return;
        }
        if (!ModuleDict.Remove(name))
            throw PyAttributeError.Create($"'{(_customType is PyClass owner ? owner.Name : "module")}' object has no attribute '{name}'");
    }

    // 모듈 실행 (소스 코드 실행) - 전체 Python 인터프리터 파이프라인 사용
    /// <summary>Run code that is already compiled: a module out of a bundle.</summary>
    internal void ExecuteCode(PyCodeObject code)
    {
        PyVM.Instance.ExecuteModule(code, CreateModuleGlobalScope());
        IsInitialized = true;
    }

    public void Execute(string sourceCode)
    {
#if DEBUG_MODULE_LOG
        Console.WriteLine($"📄 모듈 '{Name}' 실행 중...");
#endif

        // 1단계: 파싱 (소스 → AST)
        var codeObject = Compiler.CompileSource(sourceCode, FileName);

        // 3단계: 모듈 전용 글로벌 스코프 생성
        var moduleGlobalScope = CreateModuleGlobalScope();

        // 4단계: VM 실행 (모듈 네임스페이스에서 실행)
        // What the module raises reaches the importer unchanged (_load_unlocked).
        PyVM.Instance.ExecuteModule(codeObject, moduleGlobalScope);

        IsInitialized = true;
    }

    /// <summary>
    /// 모듈 전용 글로벌 스코프 생성 (모듈의 __dict__를 직접 글로벌 스코프로 사용)
    /// </summary>
    // CPython importlib.reload: re-execute the module source in the EXISTING
    // module __dict__ (no clearing), propagate the original exception, and leave
    // the module registered in sys.modules even on failure (partial update stays).
    // Unlike Execute(), this does not wrap failures in ImportError nor remove the
    // sys.modules entry.
    public void ExecuteForReload(string sourceCode)
    {
        var codeObject = Compiler.CompileSource(sourceCode, FileName);
        var moduleGlobalScope = CreateModuleGlobalScope();
        PyVM.Instance.ExecuteModule(codeObject, moduleGlobalScope);
        IsInitialized = true;
    }

    private PyScopeChain CreateModuleGlobalScope()
    {
        // CPython 3.12 호환: 모듈 딕셔너리를 직접 글로벌 스코프로 사용
        // CPython 3.12: Python/ceval.c - frame->f_globals references module->md_dict
        // Pass module reference so globals() can return module.__dict__
        return new PyScopeChain(ModuleDict, Name, this);
    }

    // CPython 3.12: Objects/moduleobject.c module_repr - file-backed modules show
    // "from '<file>'"; builtin/native modules (no source file) show "(built-in)".
    public override string ToString()
    {
        // _module_repr: a module made by module() is what its dict says.
        if (_allocated)
            return ModuleDict.TryGetValue("__file__", out var file) && file is PyString path
                ? $"<module '{Name}' from {path.ToRepr().Value}>"
                : $"<module '{Name}'>";
        return HasSourceFile ? $"<module '{Name}' from '{FileName}'>" : $"<module '{Name}' (built-in)>";
    }

    // module_repr; a module subclass's __repr__ is its tp_repr, which PyObject_Repr reaches first.
    protected internal override PyString Repr() => PyString.From(ToString());
    
}

    // Python import 시스템
    public partial class PyImportSystem
    {
        // CPython 3.12: Python/import.c:185 - PyDict_New()
        // sys.modules 캐시 - CPython과 동일하게 PyDict 사용
        public static PyDict SysModules { get; } = new PyDict();

        // CPython 3.12: Helper methods for SysModules access with string keys
        // CPython uses PyUnicode_FromString + PyDict_GetItem pattern
        public static bool TryGetModule(string name, out PyModule module)
        {
            var key = PyString.From(name);
            if (SysModules.InternalDict.TryGetValue(key, out var value) && value is PyModule pyModule)
            {
                module = pyModule;
                return true;
            }
            module = null;
            return false;
        }

        public static void SetModule(string name, PyModule module)
        {
            // CPython 3.12: Python/import.c:630 - PyDict_SetItemString
            SysModules.SetItem(PyString.From(name), module);
        }

        public static void RemoveModule(string name)
        {
            // CPython 3.12: Python/import.c - PyDict_DelItemString
            var key = PyString.From(name);
            if (SysModules.ContainsKey(key)) SysModules.DelItem(key);
        }

        // C# 구현 모듈들 (CPython 3.12 C 확장 모듈만)
        private static Dictionary<string, Func<PyModule>> _builtinModules = new Dictionary<string, Func<PyModule>>
        {
            // CPython 3.12 Built-in 모듈
            ["builtins"] = () => PyBuiltinsModule.Instance,

            // CPython 3.12 C 확장 모듈 (Python 모듈의 백엔드)
            ["_operator"] = () => SharpPy.Modules._OperatorModule.CreateOperatorModule(),  // operator.py가 사용
            ["_abc"] = () => SharpPy.Modules.AbcModule.CreateAbcModule(),  // abc.py가 사용
            ["_opcode"] = () => SharpPy.Modules._OpcodeModule.Create(),

            // CPython 3.12 Built-in C 모듈 (성능 중요)
            ["math"] = () => SharpPy.Modules.MathModule.CreateMathModule(),
            ["cmath"] = () => SharpPy.Modules.CmathModule.CreateModule(),
            ["msvcrt"] = () => SharpPy.Modules.MsvcrtModule.CreateModule(),
            ["_wmi"] = () => SharpPy.Modules.WmiModule.CreateModule(),
            ["time"] = () => new TimeModule(),
            ["_testcapi"] = () => SharpPy.Modules._TestCapiModule.Create(),
            ["_heapq"] = () => SharpPy.Modules._HeapqModule.Create(),
            ["_bisect"] = () => SharpPy.Modules._BisectModule.Create(),
            ["itertools"] = SharpPy.Modules.ItertoolsModule.Create,
            ["_collections"] = () => SharpPy.Modules._CollectionsModule.CreateCollectionsModule(),
            ["_functools"] = () => SharpPy.Modules._FunctoolsModule.CreateFunctoolsModule(),

            // CPython 3.12 C 확장 모듈 (Python 모듈의 백엔드)
            ["_random"] = () => SharpPy.Modules.RandomModule.CreateRandomModule(),  // random.py가 사용
            ["_codecs"] = () => SharpPy.Modules._CodecsModule.CreateCodecsModule(),
            // CPython's binascii is Modules/binascii.c. Only the four primitives the hex and
            // base64 codecs need exist here; Lib/encodings/hex_codec.py and base64_codec.py import
            // this module, so it has a name of its own.
            ["binascii"] = () => PyBinascii.CreateBinasciiModule(),  // codecs.py가 사용
            // CPython's gc is a built-in C module (Modules/gcmodule.c), so there is no Lib/gc.py
            // to copy: the surface and its refusals are implemented against measured behaviour.
            ["gc"] = () => SharpPy.Modules.GcModule.CreateGcModule(),
            // Modules/atexitmodule.c is a C module, so it is implemented natively rather than copied
            // from Lib (there is no Lib/atexit.py). Lib/weakref.py registers its reaper through it.
            ["atexit"] = () => SharpPy.Modules.AtexitModule.CreateAtexitModule(),
            ["_string"] = () => SharpPy.Modules._StringModule.CreateStringModule(),
            ["_thread"] = () => SharpPy.Modules._ThreadModule.CreateThreadModule(),
            ["_contextvars"] = () => SharpPy.Modules._ContextvarsModule.CreateContextvarsModule(),
            ["zlib"] = () => SharpPy.Modules.ZlibModule.CreateZlibModule(),
            ["_csv"] = () => SharpPy.Modules._CsvModule.CreateCsvModule(),
            ["array"] = () => SharpPy.Modules.ArrayModule.CreateArrayModule(),
            ["_hashes"] = () => SharpPy.Modules._HashesModule.CreateHashlibModule(),
            ["_struct"] = () => SharpPy.Modules._StructModule.CreateStructModule(),
            ["errno"] = () => SharpPy.Modules.ErrnoModule.CreateErrnoModule(),
            ["marshal"] = () => SharpPy.Modules.MarshalModule.CreateMarshalModule(),
            ["_queue"] = () => SharpPy.Modules._QueueModule.CreateQueueModule(),
            ["unicodedata"] = () => SharpPy.Modules.UnicodedataModule.CreateModule(),
            ["_signal"] = () => SharpPy.Modules._SignalModule.CreateSignalModule(),
            ["sharppy"] = () => SharpPy.Modules.SharpPyModule.CreateSharpPyModule(),
            ["_weakref"] = () => SharpPy.Modules.WeakrefModule.CreateWeakrefModule(),

            // OS 인터페이스 C 모듈 (cross-platform)
            ["nt"] = () => SharpPy.Modules.NtModule.CreateNtModule(),  // os.py가 사용 (Windows/Linux/Mac)
            ["posix"] = () => SharpPy.Modules.NtModule.CreateNtModule(),  // os.py가 사용 (alias)

            // 시스템 인터페이스 Built-in 모듈
            ["sys"] = () => SharpPy.Modules.SysModule.CreateSysModule(),
            ["_tokenize"] = () => SharpPy.Modules._TokenizeModule.Create(),
            ["_importlib_native"] = () => SharpPy.Modules.ImportlibModule.CreateImportlibModule(),
            ["_imp"] = SharpPy.Modules.ImpModule.Create,
            // The C functions Lib/_typing.py stands in front of (Objects/typevarobject.c, unionobject.c).
            ["_typing_native"] = () =>
            {
                var module = new PyModule("_typing_native");
                module.ModuleDict["union_type_or"] = new PyBuiltinFunction("union_type_or", args => PyUnionType.Or(args[0], args[1]));
                return module;
            },
            ["_io"] = () => SharpPy.Modules.IoModule.CreateIoModule(),  // io.py가 사용 (CPython Modules/_io)

            // CPython 3.12 C 확장 모듈 (Python 모듈의 백엔드)

            // CPython 3.12: 다음 모듈들은 순수 Python으로 Lib/에서 로드됨:
            // - types (Lib/types.py)
            // - random (Lib/random.py + _random C# 모듈)
            // - os (Lib/os.py + nt C# 모듈)
            // - datetime (Lib/datetime.py + _datetime C# 모듈)

            // TODO: CPython 호환을 위해 Python으로 전환 필요:
            // - urllib → Lib/urllib/ Python 모듈로 전환 (완료)
            // - asyncio → Lib/asyncio/ Python 모듈로 전환
            ["_sre"] = () => SharpPy.Modules._SreModule.CreateSreModule(),  // re.py가 사용
            // urllib는 Lib/urllib/ 디렉토리의 Python 모듈로 전환됨 (CPython 3.12 호환)

            // 주석: 다음 모듈들은 순수 Python 모듈로 Lib/ 디렉토리에서 로드됨:
            // functools, contextlib, typing, abc, collections, json, traceback, pathlib
        };


        // import module_name (dotted import 지원)
        /// <summary>
        /// CPython 3.12 compatible import
        /// Implements: __import__(name, globals, locals, fromlist, level)
        /// </summary>
        public static PyModule Import(string moduleName, int level = 0, string[] fromlist = null, IDictionary<string, PyObject> globals = null)
        {
            // PyImport_ImportModuleLevelObject: a failed import loses importlib's frames.
            try
            {
                return ImportLevel(moduleName, level, fromlist, globals);
            }
            catch (PythonException error) when (error.PyException is PyBaseException exception)
            {
                RemoveImportlibFrames(exception);
                throw;
            }
        }

        private static PyModule ImportLevel(string moduleName, int level, string[] fromlist, IDictionary<string, PyObject> globals)
        {
            // CPython 3.12: level parameter for relative imports
            // level=0: absolute import (default)
            // level>0: relative import (1=., 2=.., etc)

            // CPython 3.12: Use globals dict to resolve relative imports
            if (level > 0)
            {
                // Resolve relative import using globals['__package__'] or globals['__name__']
                moduleName = ResolveRelativeImport(moduleName, level, globals);
            }

            // CPython 3.12: fromlist parameter affects what is returned
            // If fromlist is empty/null AND module name has dots, return top-level package
            // If fromlist has items, return the actual module with those attributes
            bool hasFrom = fromlist != null && fromlist.Length > 0;

            if (!hasFrom && moduleName.Contains('.'))
            {
                // CPython behavior: import collections.abc → return collections (not collections.abc)
                // First, load the full module path (ensures all submodules are loaded)
                var fullModule = Import(moduleName);

                // Then return only the top-level package
                var firstPart = moduleName.Split('.')[0];
                if (TryGetModule(firstPart, out var topModule))
                    return topModule;
                throw new Exception($"Module '{firstPart}' not found in sys.modules");
            }

            // Load the main module
            var module = Import(moduleName);

            // CPython 3.12: Python/import.c:2938-2947 (_handle_fromlist)
            // IMPORTANT: Even if module was cached, we must still handle fromlist!
            // The fromlist items (submodules) need to be imported regardless of
            // whether the parent module was newly loaded or from cache.
            if (hasFrom)
            {
                HandleFromList(module, fromlist);
            }

            return module;
        }

        // _ERR_MSG of Lib/importlib/_bootstrap.py: the name by its repr.
        private static string Repr(string name) => PyString.From(name).ToRepr().Value;

        public static PyModule Import(string moduleName)
        {
            // CPython 3.12: Python/import.c:1673 - Check sys.modules cache first
            if (TryGetModule(moduleName, out PyModule cachedModule))
            {
                return cachedModule;
            }
            // _find_and_load: None in sys.modules stops the import.
            if (SysModules.InternalDict.TryGetValue(PyString.From(moduleName), out var blocked) && blocked is PyNone)
                throw PyModuleNotFoundError.Create($"import of {moduleName} halted; None in sys.modules", moduleName);

            // 2. 점으로 구분된 모듈명 처리 (예: package.submodule)
            if (moduleName.Contains('.'))
            {
                return ImportDottedModule(moduleName);
            }

            return FindAndLoad(moduleName, PyNone.Instance, null)
                ?? throw PyModuleNotFoundError.Create($"No module named {Repr(moduleName)}", moduleName);
        }

        /// <summary>
        /// 점으로 구분된 모듈 import (예: package.submodule)
        /// </summary>
        private static PyModule ImportDottedModule(string dottedName)
        {
            var parts = dottedName.Split('.');
            PyModule currentModule = null;
            string currentPath = "";
            
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = i == 0 ? part : $"{currentPath}.{part}";
                
                // CPython 3.12: Check sys.modules cache for each step
                if (TryGetModule(currentPath, out PyModule existingModule))
                {
                    currentModule = existingModule;
                    continue;
                }
                
                // 서브모듈 검색
                PyModule subModule;
                if (i == 0)
                    subModule = FindAndLoad(part, PyNone.Instance, null);
                else
                {
                    // _find_and_load_unlocked: the parent's __path__, which a plain module lacks.
                    if (!currentModule.ModuleDict.TryGetValue("__path__", out var searchPath))
                        throw PyModuleNotFoundError.Create(
                            $"No module named {Repr(currentPath)}; {Repr(currentPath.Substring(0, currentPath.LastIndexOf('.')))} is not a package", currentPath);
                    subModule = FindAndLoad(currentPath, searchPath, currentModule);
                }
                
                if (subModule == null)
                {
                    throw PyModuleNotFoundError.Create($"No module named {Repr(currentPath)}", currentPath);
                }
                
                // CPython 3.12: Register in sys.modules
                SetModule(currentPath, subModule);
                
                // 부모 모듈에 서브모듈 attribute 설정
                if (currentModule != null)
                {
                    currentModule.SetAttribute(part, subModule);
                }
                
                currentModule = subModule;
            }
            
            return currentModule;
        }
        
        // sys.path 가져오기
        private static PyList GetSysPath()
        {
            // CPython 3.12: Use sys.path if sys module is loaded
            if (TryGetModule("sys", out PyModule sysModule))
            {
                if (sysModule.ModuleDict.TryGetValue("path", out PyObject pathObj) && pathObj is PyList pathList)
                {
                    return pathList;
                }
            }

            // sys 모듈이 없으면 기본 경로 생성
            return CreateDefaultSysPath();
        }

        // 기본 sys.path 생성 (CPython 호환 구조)
        private static PyList CreateDefaultSysPath() =>
            SharpPy.Modules.SysModule.CreateSysPath();

        /// <summary>
        /// <summary>
        /// CPython 3.12: Handle fromlist items by ensuring they are loaded as submodules
        /// Python/import.c:2940-2948 - Calls _handle_fromlist
        /// importlib/_bootstrap.py:1020-1040 - _handle_fromlist implementation
        /// </summary>
        // _handle_fromlist's except clause: only the submodule itself being absent is silent, and not
        // when sys.modules holds None for it.
        private static bool IsMissing(PythonException error, string fullName) =>
            error.PyException.GetPyType().IsSubclassOf(PyType.ModuleNotFoundErrorType) &&
            error.PyException.LookupAttribute("name") is PyString name && name.Value == fullName &&
            !(SysModules.InternalDict.TryGetValue(PyString.From(fullName), out var entry) && entry is PyNone);

        // PyImport_ImportModuleLevelObject: _handle_fromlist runs for a package, a module that has
        // __path__ (read with getattr, so a module's __getattr__ answers and its errors propagate).
        private static void HandleFromList(PyModule module, string[] fromlist)
        {
            if (fromlist == null || fromlist.Length == 0 || module.LookupAttribute("__path__") == null)
                return;
            var names = new PyObject[fromlist.Length];
            for (int i = 0; i < names.Length; i++) names[i] = PyString.From(fromlist[i]);
            HandleFromList(module, names, recursive: false);
        }

        // importlib._bootstrap._handle_fromlist: a name the module does not have is imported as its
        // submodule; '*' does that for the names in __all__.
        private static void HandleFromList(PyModule module, IEnumerable<PyObject> fromlist, bool recursive)
        {
            foreach (var item in fromlist)
            {
                if (item is not PyString name)
                {
                    string where = recursive ? module.GetAttribute("__name__").ToStr().Value + ".__all__" : "``from list''";
                    throw PyTypeError.Create($"Item in {where} must be str, not {item.GetTypeName()}");
                }
                if (name.Value == "*")
                {
                    if (!recursive && module.LookupAttribute("__all__") is { } all)
                        HandleFromList(module, new List<PyObject>(PySet.IterableItems(all)), recursive: true);
                    continue;
                }
                if (module.LookupAttribute(name.Value) != null)
                    continue;
                string fromName = $"{module.GetAttribute("__name__").ToStr().Value}.{name.Value}";
                try
                {
                    var subModule = Import(fromName);
                    module.ModuleDict[name.Value] = subModule;
                }
                catch (PythonException error) when (IsMissing(error, fromName))
                {
                }
            }
        }

        /// 상대 import 경로 해석 (.module, ..module 등)
        /// </summary>
        /// <summary>
        /// CPython 3.12 compatible relative import resolution
        /// Based on CPython's resolve_name() in Python/import.c
        /// </summary>
        private static string ResolveRelativeImport(string moduleName, int level, IDictionary<string, PyObject> globals)
        {
            if (globals == null)
            {
                throw PyImportError.Create("attempted relative import with no known parent package");
            }

            // resolve_name (Python/import.c): __package__, else __spec__.parent, else __name__
            // (and __path__) with an ImportWarning.
            string package;
            globals.TryGetValue("__package__", out var packageObject);
            if (packageObject is PyNone) packageObject = null;
            globals.TryGetValue("__spec__", out var spec);
            if (packageObject != null)
            {
                package = (packageObject as PyString)?.Value ?? throw PyTypeError.Create("package must be a string");
            }
            else if (spec != null && spec is not PyNone)
            {
                package = (spec.GetAttribute("parent") as PyString)?.Value
                    ?? throw PyTypeError.Create("__spec__.parent must be a string");
            }
            else
            {
                PyWarnings.Warn(PyType.ImportWarningType,
                    "can't resolve package from __spec__ or __package__, falling back on __name__ and __path__", 1);
                if (!globals.TryGetValue("__name__", out var nameObject))
                    throw PyKeyError.Create(PyString.From("'__name__' not in globals"));
                package = (nameObject as PyString)?.Value ?? throw PyTypeError.Create("__name__ must be a string");
                if (!globals.ContainsKey("__path__"))
                {
                    int lastDot = package.LastIndexOf('.');
                    package = lastDot >= 0 ? package.Substring(0, lastDot) : "";
                }
            }

            if (string.IsNullOrEmpty(package))
            {
                throw PyImportError.Create("attempted relative import with no known parent package");
            }

            // 4. Walk up 'level - 1' times
            string[] parts = package.Split('.');
            int levelsUp = level - 1;

            if (levelsUp >= parts.Length)
            {
                throw PyImportError.Create("attempted relative import beyond top-level package");
            }

            // Performance: Eliminated LINQ - manual array copy instead of Take + ToArray
            int targetLength = parts.Length - levelsUp;
            string[] targetParts = new string[targetLength];
            for (int i = 0; i < targetLength; i++)
            {
                targetParts[i] = parts[i];
            }
            string targetPackage = string.Join(".", targetParts);

            // 5. Combine with relative name
            if (string.IsNullOrEmpty(moduleName))
            {
                return targetPackage;  // from .. import something
            }
            else
            {
                return $"{targetPackage}.{moduleName}";  // from ..module import something
            }
        }

        // abc and contextlib modules are now loaded as .py files from Lib/ directory

        // asyncio 모듈 생성
    }
#endregion
}
