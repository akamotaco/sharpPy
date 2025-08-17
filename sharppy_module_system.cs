// ModuleSystem class from enhanced_modules_environment.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace SharpPy
{
    public static class ModuleSystem
    {
        private static Dictionary<string, PythonModule> loadedModules = new Dictionary<string, PythonModule>();

        // module_system.cs의 ImportModule 함수만 수정
        public static PythonModule ImportModule(string name, List<string> searchPaths = null)
        {
            if (loadedModules.ContainsKey(name))
                return loadedModules[name];

            // 내장 모듈은 searchPaths 전달
            var module = CreateBuiltinModule(name, searchPaths);
            if (module != null)
            {
                loadedModules[name] = module;
                return module;
            }

            // 표준 라이브러리 모듈 확인 (이 줄만 추가!)
            module = StandardLibrary.CreateStdlibModule(name, searchPaths);
            if (module != null)
            {
                loadedModules[name] = module;
                return module;
            }
            
            if (searchPaths == null)
                searchPaths = new List<string> { "." };

            try
            {
                var parts = name.Split('.');
                
                foreach (var searchPath in searchPaths)
                {
                    // 1. 직접 .py 파일 확인
                    var directFilePath = Path.Combine(searchPath, 
                        string.Join(Path.DirectorySeparatorChar.ToString(), parts) + ".py");
                    
                    if (File.Exists(directFilePath))
                    {
                        // searchPaths를 전달하여 모듈 생성
                        module = new PythonModule(name, searchPaths);
                        string code = File.ReadAllText(directFilePath);
                        var interpreter = new SharpPy.PythonInterpreter();
                        interpreter.SetGlobalEnv(module.ModuleEnv);
                        interpreter.Execute(code, directFilePath);
                        loadedModules[name] = module;
                        return module;
                    }

                    // 2. 패키지 디렉토리 확인
                    var packagePath = Path.Combine(searchPath, 
                        string.Join(Path.DirectorySeparatorChar.ToString(), parts));
                    
                    if (Directory.Exists(packagePath))
                    {
                        module = CreatePackageModule(name, packagePath, searchPaths);
                        if (module != null)
                        {
                            loadedModules[name] = module;
                            return module;
                        }
                    }

                    // 3. 중간 패키지 처리 (이전 코드와 동일하지만 searchPaths 전달)
                    if (parts.Length > 1)
                    {
                        for (int i = 1; i <= parts.Length; i++)
                        {
                            var subParts = parts.Take(i).ToArray();
                            var subName = string.Join(".", subParts);
                            var subPath = Path.Combine(searchPath, 
                                string.Join(Path.DirectorySeparatorChar.ToString(), subParts));
                            
                            if (loadedModules.ContainsKey(subName))
                                continue;
                            
                            if (Directory.Exists(subPath))
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
                                if (File.Exists(filePath))
                                {
                                    module = new PythonModule(subName, searchPaths);
                                    string code = File.ReadAllText(filePath);
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
                        var filePath = Path.Combine(searchPath, name + ".py");
                        if (File.Exists(filePath))
                        {
                            module = new PythonModule(name, searchPaths);
                            string code = File.ReadAllText(filePath);
                            var interpreter = new SharpPy.PythonInterpreter();
                            interpreter.SetGlobalEnv(module.ModuleEnv);
                            interpreter.Execute(code, filePath);
                            loadedModules[name] = module;
                            return module;
                        }
                        
                        var packageDir = Path.Combine(searchPath, name);
                        if (Directory.Exists(packageDir))
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
                
                var initFile = Path.Combine(packageDir, "__init__.py");
                if (File.Exists(initFile))
                {
                    string code = File.ReadAllText(initFile);
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

        public static PythonTypeObject ImportFrom(string moduleName, string itemName, List<string> searchPaths = null)
        {
            var fullName = $"{moduleName}.{itemName}";
            try
            {
                var fullModule = ImportModule(fullName, searchPaths);
                return fullModule;
            }
            catch (PythonException)
            {
                // 전체 경로가 실패하면 패키지에서 아이템 찾기
            }

            var module = ImportModule(moduleName, searchPaths);
            
            try
            {
                return module.GetAttribute(itemName);
            }
            catch (PythonException)
            {
                try
                {
                    var subModule = ImportModule($"{moduleName}.{itemName}", searchPaths);
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

        private static PythonModule CreateBuiltinModule(string name, List<string> searchPaths = null)
        {
            return name switch
            {
                "math" => CreateMathModule(searchPaths),
                "random" => CreateRandomModule(searchPaths),
                "os" => CreateOsModule(searchPaths),
                _ => null
            };
        }

        private static PythonModule CreateMathModule(List<string> searchPaths = null)
        {
            var module = new PythonModule("math", searchPaths);

            module.SetAttribute("pi", new PythonFloat(Math.PI));
            module.SetAttribute("e", new PythonFloat(Math.E));

            module.SetAttribute("sqrt", new BuiltinFunction("sqrt", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "sqrt() takes exactly one numeric argument");
                return new PythonFloat(Math.Sqrt(NumberHelper.ToDouble(args[0])));
            }));

            module.SetAttribute("pow", new BuiltinFunction("pow", args =>
            {
                if (args.Count != 2 || !NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "pow() takes exactly two numeric arguments");
                return new PythonFloat(Math.Pow(NumberHelper.ToDouble(args[0]), NumberHelper.ToDouble(args[1])));
            }));

            module.SetAttribute("sin", new BuiltinFunction("sin", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "sin() takes exactly one numeric argument");
                return new PythonFloat(Math.Sin(NumberHelper.ToDouble(args[0])));
            }));

            module.SetAttribute("cos", new BuiltinFunction("cos", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "cos() takes exactly one numeric argument");
                return new PythonFloat(Math.Cos(NumberHelper.ToDouble(args[0])));
            }));

            module.SetAttribute("tan", new BuiltinFunction("tan", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "tan() takes exactly one numeric argument");
                return new PythonFloat(Math.Tan(NumberHelper.ToDouble(args[0])));
            }));

            module.SetAttribute("floor", new BuiltinFunction("floor", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "floor() takes exactly one numeric argument");
                return new PythonInt((int)Math.Floor(NumberHelper.ToDouble(args[0])));
            }));

            module.SetAttribute("ceil", new BuiltinFunction("ceil", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "ceil() takes exactly one numeric argument");
                return new PythonInt((int)Math.Ceiling(NumberHelper.ToDouble(args[0])));
            }));

            return module;
        }


        private static PythonModule CreateRandomModule(List<string> searchPaths = null)
        {
            var module = new PythonModule("random", searchPaths);
            var random = new Random();

            module.SetAttribute("random", new BuiltinFunction("random", args =>
            {
                if (args.Count != 0) throw new PythonException("TypeError", "random() takes no arguments");
                return new PythonFloat(random.NextDouble());
            }));

            module.SetAttribute("randint", new BuiltinFunction("randint", args =>
            {
                if (args.Count != 2 || !NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "randint() takes exactly two integer arguments");
                return new PythonInt(random.Next(NumberHelper.ToInt(args[0]), NumberHelper.ToInt(args[1]) + 1));
            }));

            module.SetAttribute("choice", new BuiltinFunction("choice", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "choice() takes exactly one argument");
                if (args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) throw new PythonException("IndexError", "choice() from empty sequence");
                    return list.Items[random.Next(list.Items.Count)];
                }
                if (args[0] is PythonTuple tuple)
                {
                    if (tuple.Items.Count == 0) throw new PythonException("IndexError", "choice() from empty sequence");
                    return tuple.Items[random.Next(tuple.Items.Count)];
                }
                throw new PythonException("TypeError", "choice() argument must be a sequence");
            }));

            module.SetAttribute("shuffle", new BuiltinFunction("shuffle", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "shuffle() takes exactly one argument");
                if (args[0] is PythonList list)
                {
                    for (int i = list.Items.Count - 1; i > 0; i--)
                    {
                        int j = random.Next(i + 1);
                        (list.Items[i], list.Items[j]) = (list.Items[j], list.Items[i]);
                    }
                    return PythonNone.Instance;
                }
                throw new PythonException("TypeError", "shuffle() argument must be a list");
            }));

            return module;
        }

        private static PythonModule CreateOsModule(List<string> searchPaths = null)
        {
            var module = new PythonModule("os", searchPaths);

            module.SetAttribute("getcwd", new BuiltinFunction("getcwd", args =>
            {
                if (args.Count != 0) throw new PythonException("TypeError", "getcwd() takes no arguments");
                return new PythonString(Directory.GetCurrentDirectory());
            }));

            module.SetAttribute("listdir", new BuiltinFunction("listdir", args =>
            {
                string path = args.Count == 0 ? "." : (args[0] as PythonString)?.Value ?? ".";
                var list = new PythonList();
                try
                {
                    foreach (var item in Directory.GetFileSystemEntries(path))
                        list.Items.Add(new PythonString(Path.GetFileName(item)));
                }
                catch (Exception ex)
                {
                    throw new PythonException("OSError", $"listdir() error: {ex.Message}");
                }
                return list;
            }));

            module.SetAttribute("path", CreateOsPathModule());

            return module;
        }

        private static PythonModule CreateOsPathModule()
        {
            var module = new PythonModule("path");

            module.SetAttribute("exists", new BuiltinFunction("exists", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "exists() takes exactly one argument");
                string path = (args[0] as PythonString)?.Value ?? "";
                return new PythonBool(File.Exists(path) || Directory.Exists(path));
            }));

            module.SetAttribute("isfile", new BuiltinFunction("isfile", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "isfile() takes exactly one argument");
                string path = (args[0] as PythonString)?.Value ?? "";
                return new PythonBool(File.Exists(path));
            }));

            module.SetAttribute("isdir", new BuiltinFunction("isdir", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "isdir() takes exactly one argument");
                string path = (args[0] as PythonString)?.Value ?? "";
                return new PythonBool(Directory.Exists(path));
            }));

            return module;
        }
    }
}