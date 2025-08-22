// ModuleSystem class from enhanced_modules_environment.cs
using System;
using System.Collections.Generic;
using System.Linq;

// Godot 환경이면 Godot_IO 사용, 아니면 DotNet_IO 사용
// 아래 줄 중 하나만 선택하여 사용
#if GODOT
using Godot_IO;  // Godot 환경용
#else
using DotNet_IO;  // 일반 .NET 환경용
#endif

namespace SharpPy
{
    public static class ModuleSystem
    {
        private static Dictionary<string, PythonModule> loadedModules = new Dictionary<string, PythonModule>();

        // 인터프리터에서 sys.path를 가져오는 헬퍼 메서드
        private static List<string> GetSearchPaths(Environment env, List<string> overridePaths = null)
        {
            // 명시적으로 제공된 경로가 있으면 사용
            if (overridePaths != null && overridePaths.Count > 0)
                return overridePaths;

            // sys 모듈에서 path 가져오기
            try
            {
                if (env.HasVariable("sys"))
                {
                    var sysModule = env.GetVariable("sys");
                    if (sysModule is SysModule sys)
                    {
                        return sys.Path.ToStringList();
                    }
                    else if (sysModule is PythonModule module)
                    {
                        var pathAttr = module.GetAttribute("path");
                        if (pathAttr is SysPath sysPath)
                        {
                            return sysPath.ToStringList();
                        }
                        else if (pathAttr is PythonList pathList)
                        {
                            return pathList.Items
                                .Where(item => item is PythonString)
                                .Select(item => ((PythonString)item).Value)
                                .ToList();
                        }
                    }
                }
            }
            catch { }

            // sys.path를 찾을 수 없으면 기본값 사용
            return new List<string> { "." };
        }

        public static PythonModule ImportModule(Environment parentEnv, string name, List<string> searchPaths = null)
        {
            // 1. sys.modules에서 먼저 확인 (표준 Python 동작)
            try
            {
                if (parentEnv != null && parentEnv.HasVariable("sys"))
                {
                    var sys = parentEnv.GetVariable("sys");

                    // SysModuleInstance인 경우
                    if (sys is SysModuleInstance sysInstance)
                    {
                        // 이미 로드된 모듈 확인
                        var loadedModule = sysInstance.GetLoadedModule(name);
                        if (loadedModule != null)
                            return loadedModule;

                        // searchPaths가 없으면 sys.path 사용
                        if (searchPaths == null)
                        {
                            searchPaths = sysInstance.GetSearchPaths();
                        }
                    }
                    // 일반 PythonModule인 경우 (기존 방식)
                    else if (sys is PythonModule sysModule)
                    {
                        var sysModules = sysModule.GetAttribute("modules") as PythonDict;
                        if (sysModules != null)
                        {
                            var key = new PythonString(name);
                            if (sysModules.Items.ContainsKey(key))
                            {
                                return sysModules.Items[key] as PythonModule;
                            }
                        }

                        // searchPaths가 없으면 sys.path 사용
                        if (searchPaths == null)
                        {
                            var sysPath = sysModule.GetAttribute("path");
                            if (sysPath is SysPathList pathList)
                            {
                                searchPaths = pathList.Items
                                    .Where(item => item is PythonString)
                                    .Select(item => ((PythonString)item).Value)
                                    .ToList();
                            }
                            else if (sysPath is PythonList list)
                            {
                                searchPaths = list.Items
                                    .Where(item => item is PythonString)
                                    .Select(item => ((PythonString)item).Value)
                                    .ToList();
                            }
                        }
                    }
                }
            }
            catch { }

            // searchPaths가 여전히 null이면 기본값
            if (searchPaths == null)
            {
                searchPaths = new List<string> { "." };
            }

            // 2. sys 모듈 특별 처리 (내장 모듈)
            if (name == "sys" && parentEnv != null)
            {
                try
                {
                    var sys = parentEnv.GetVariable("sys");
                    if (sys is SysModuleInstance)
                    {
                        // 이미 초기화된 sys 모듈 반환
                        return sys as PythonModule;
                    }
                }
                catch { }
                // sys가 없으면 에러 (PythonInterpreter를 통해서만 생성 가능)
                throw new PythonException("ImportError",
                    "sys module must be initialized through PythonInterpreter");
            }

            // 3. 로컬 캐시 확인 (하위 호환성)
            if (loadedModules.ContainsKey(name))
            {
                var cachedModule = loadedModules[name];

                // 캐시된 모듈에도 __builtins__가 없으면 추가
                if (parentEnv != null && !cachedModule.ModuleEnv.HasVariable("__builtins__"))
                {
                    var builtins = parentEnv.GetVariable("__builtins__");
                    if (builtins != null)
                    {
                        cachedModule.ModuleEnv.SetVariable("__builtins__", builtins);
                    }
                }

                // sys.modules에도 등록
                RegisterInSysModules(parentEnv, name, cachedModule);

                return cachedModule;
            }

            // searchPaths가 없으면 sys.path에서 가져오기
            searchPaths = GetSearchPaths(parentEnv, searchPaths);

            // 3. 표준 라이브러리 모듈 확인
            var module = StandardLibrary.CreateStdlibModule(name, searchPaths);
            if (module != null)
            {
                // 표준 라이브러리 모듈에도 __builtins__ 추가
                if (parentEnv != null)
                {
                    try
                    {
                        var builtins = parentEnv.GetVariable("__builtins__");
                        if (builtins != null)
                        {
                            module.ModuleEnv.SetVariable("__builtins__", builtins);
                        }
                    }
                    catch { }
                }

                loadedModules[name] = module;
                RegisterInSysModules(parentEnv, name, module);
                return module;
            }

            // searchPaths는 이미 위에서 설정됨

            try
            {
                var parts = name.Split('.');

                foreach (var searchPath in searchPaths)
                {
                    // 1. 직접 .py 파일 확인
                    var directFilePath = Helper.CombinePath(searchPath,
                        string.Join("/", parts) + ".py");  // 항상 슬래시 사용

                    if (Helper.FileExists(directFilePath))
                    {
                        module = new PythonModule(name, searchPaths);

                        // 모듈의 __name__과 __file__ 설정
                        module.ModuleEnv.SetVariable("__name__", new PythonString(name));
                        module.ModuleEnv.SetVariable("__file__", new PythonString(directFilePath));

                        // 현재 파일 정보 설정 - 이것이 핵심!
                        module.ModuleEnv.CurrentFileName = directFilePath;

                        // __builtins__ 설정
                        if (parentEnv != null)
                        {
                            try
                            {
                                var builtins = parentEnv.GetVariable("__builtins__");
                                if (builtins != null)
                                {
                                    module.ModuleEnv.SetVariable("__builtins__", builtins);
                                }
                            }
                            catch { }
                        }

                        string code = Helper.ReadAllText(directFilePath);
                        var interpreter = new PythonInterpreter();
                        interpreter.SetGlobalEnv(module.ModuleEnv);
                        interpreter.Execute(code, directFilePath);
                        loadedModules[name] = module;
                        RegisterInSysModules(parentEnv, name, module);

                        return module;
                    }

                    // 2. 패키지 디렉토리 확인
                    var packagePath = Helper.CombinePath(searchPath,
                        string.Join("/", parts));  // 항상 슬래시 사용

                    if (Helper.DirExists(packagePath))
                    {
                        module = CreatePackageModule(name, packagePath, searchPaths);
                        if (module != null)
                        {
                            loadedModules[name] = module;
                            RegisterInSysModules(parentEnv, name, module);
                            return module;
                        }
                    }

                    // 3. 중간 패키지 처리
                    if (parts.Length > 1)
                    {
                        for (int i = 1; i <= parts.Length; i++)
                        {
                            var subParts = parts.Take(i).ToArray();
                            var subName = string.Join(".", subParts);
                            var subPath = Helper.CombinePath(searchPath,
                                string.Join("/", subParts));  // 항상 슬래시 사용

                            if (loadedModules.ContainsKey(subName))
                                continue;

                            if (Helper.DirExists(subPath))
                            {
                                var subModule = CreatePackageModule(subName, subPath, searchPaths);
                                if (subModule != null)
                                {
                                    loadedModules[subName] = subModule;

                                    if (i > 1)
                                    {
                                        var parentName = string.Join(".", subParts.Take(i - 1));
                                        if (loadedModules.TryGetValue(parentName, out var parentModule))
                                        {
                                            parentModule.SetAttribute(subParts.Last(), subModule);
                                        }
                                    }
                                }
                            }

                            if (i == parts.Length)
                            {
                                var filePath = subPath + ".py";
                                if (Helper.FileExists(filePath))
                                {
                                    module = new PythonModule(subName, searchPaths);

                                    // 모듈 설정
                                    module.ModuleEnv.SetVariable("__name__", new PythonString(subName));
                                    module.ModuleEnv.SetVariable("__file__", new PythonString(filePath));
                                    module.ModuleEnv.CurrentFileName = filePath;

                                    // __builtins__ 설정
                                    if (parentEnv != null)
                                    {
                                        try
                                        {
                                            var builtins = parentEnv.GetVariable("__builtins__");
                                            if (builtins != null)
                                            {
                                                module.ModuleEnv.SetVariable("__builtins__", builtins);
                                            }
                                        }
                                        catch { }
                                    }

                                    string code = Helper.ReadAllText(filePath);
                                    var interpreter = new SharpPy.PythonInterpreter();
                                    interpreter.SetGlobalEnv(module.ModuleEnv);
                                    interpreter.Execute(code, filePath);
                                    loadedModules[subName] = module;

                                    if (i > 1)
                                    {
                                        var parentName = string.Join(".", subParts.Take(i - 1));
                                        if (loadedModules.TryGetValue(parentName, out var parentModule))
                                        {
                                            parentModule.SetAttribute(subParts.Last(), module);
                                        }
                                    }

                                    return module;
                                }
                            }
                        }

                        if (loadedModules.ContainsKey(name))
                            return loadedModules[name];
                    }
                }

                // 4. 단순 파일명 처리
                if (parts.Length == 1)
                {
                    foreach (var searchPath in searchPaths)
                    {
                        var filePath = Helper.CombinePath(searchPath, name + ".py");
                        if (Helper.FileExists(filePath))
                        {
                            module = new PythonModule(name, searchPaths);

                            // 모듈 설정
                            module.ModuleEnv.SetVariable("__name__", new PythonString(name));
                            module.ModuleEnv.SetVariable("__file__", new PythonString(filePath));
                            module.ModuleEnv.CurrentFileName = filePath;

                            // __builtins__를 먼저 설정
                            if (parentEnv != null)
                            {
                                try
                                {
                                    var builtins = parentEnv.GetVariable("__builtins__");
                                    if (builtins != null)
                                    {
                                        module.ModuleEnv.SetVariable("__builtins__", builtins);
                                    }
                                }
                                catch { }
                            }
                            string code = Helper.ReadAllText(filePath);
                            var interpreter = new SharpPy.PythonInterpreter();
                            interpreter.SetGlobalEnv(module.ModuleEnv);
                            interpreter.Execute(code, filePath);
                            loadedModules[name] = module;
                            return module;
                        }

                        var packageDir = Helper.CombinePath(searchPath, name);
                        if (Helper.DirExists(packageDir))
                        {
                            module = CreatePackageModule(name, packageDir, searchPaths);
                            if (module != null)
                            {
                                loadedModules[name] = module;
                                return module;
                            }
                        }
                    }
                }
            }
            catch (PythonException ex)
            {
                throw new PythonException("ImportError",
                    $"Failed to import module '{name}': {ex.Message}",
                    ex.Line, ex.Column, ex.FileName);
            }
            catch (Exception ex)
            {
                throw new PythonException("ImportError",
                    $"Failed to import module '{name}': {ex.Message}");
            }

            throw new PythonException("ImportError", $"No module named '{name}'");
        }

        private static PythonModule CreatePackageModule(string packageName, string packageDir, List<string> searchPaths = null)
        {
            try
            {
                // searchPaths 전달하여 모듈 생성
                var module = new PythonModule(packageName, searchPaths);

                // 패키지 디렉토리를 검색 경로에 추가
                if (!module.ModuleEnv.SearchPaths.Contains(packageDir))
                {
                    module.ModuleEnv.SearchPaths.Insert(0, packageDir);
                }

                var initFile = Helper.CombinePath(packageDir, "__init__.py");
                if (Helper.FileExists(initFile))
                {
                    // 파일 정보 설정
                    module.ModuleEnv.SetVariable("__name__", new PythonString(packageName));
                    module.ModuleEnv.SetVariable("__file__", new PythonString(initFile));
                    module.ModuleEnv.CurrentFileName = initFile;

                    string code = Helper.ReadAllText(initFile);
                    var interpreter = new SharpPy.PythonInterpreter();
                    interpreter.SetGlobalEnv(module.ModuleEnv);
                    interpreter.Execute(code, initFile);
                }

                module.SetAttribute("__path__", new PythonString(packageDir));
                module.SetAttribute("__package__", new PythonString(packageName));

                return module;
            }
            catch (Exception ex)
            {
                throw new PythonException("ImportError",
                    $"Failed to create package module '{packageName}': {ex.Message}");
            }
        }

        public static PythonTypeObject ImportFrom(Environment parentEnv, string moduleName, string itemName, List<string> searchPaths = null)
        {
            var fullName = $"{moduleName}.{itemName}";
            try
            {
                var fullModule = ImportModule(parentEnv, fullName, searchPaths);
                return fullModule;
            }
            catch (PythonException)
            {
                // 전체 경로가 실패하면 패키지에서 아이템 찾기
            }

            var module = ImportModule(parentEnv, moduleName, searchPaths);

            try
            {
                return module.GetAttribute(itemName);
            }
            catch (PythonException)
            {
                try
                {
                    var subModule = ImportModule(parentEnv, $"{moduleName}.{itemName}", searchPaths);
                    module.SetAttribute(itemName, subModule);
                    return subModule;
                }
                catch (PythonException)
                {
                    throw new PythonException("ImportError",
                        $"cannot import name '{itemName}' from '{moduleName}'");
                }
            }
        }

        // Clear cache method for development/testing
        public static void ClearModuleCache()
        {
            loadedModules.Clear();
        }

        // Check if module is loaded
        public static bool IsModuleLoaded(string name)
        {
            return loadedModules.ContainsKey(name);
        }

        // Get loaded module names
        public static List<string> GetLoadedModules()
        {
            return loadedModules.Keys.ToList();
        }

        // sys.modules에 모듈 등록 헬퍼
        private static void RegisterInSysModules(Environment env, string name, PythonModule module)
        {
            if (env == null || module == null) return;

            try
            {
                if (env.HasVariable("sys"))
                {
                    var sys = env.GetVariable("sys");

                    // SysModuleInstance인 경우 직접 메서드 사용
                    if (sys is SysModuleInstance sysInstance)
                    {
                        sysInstance.RegisterModule(name, module);
                    }
                    // 일반 PythonModule인 경우 (기존 방식)
                    else if (sys is PythonModule sysModule)
                    {
                        var sysModules = sysModule.GetAttribute("modules") as PythonDict;
                        if (sysModules != null)
                        {
                            sysModules.Items[new PythonString(name)] = module;
                        }
                    }
                }
            }
            catch { }
        }
    }
}