// ModuleSystem class from enhanced_modules_environment.cs

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

            var module = CreateBuiltinModule(name);
            if (module != null)
            {
                loadedModules[name] = module;
                return module;
            }

            if (searchPaths == null)
                searchPaths = new List<string> { "." };

            try
            {
                // 점(.)으로 구분된 패키지 경로 처리
                var parts = name.Split('.');
                
                foreach (var searchPath in searchPaths)
                {
                    // 1. 직접 .py 파일 확인 (예: folder/z/m.py)
                    var directFilePath = Path.Combine(searchPath, 
                        string.Join(Path.DirectorySeparatorChar.ToString(), parts) + ".py");
                    
                    if (File.Exists(directFilePath))
                    {
                        module = new PythonModule(name);
                        string code = File.ReadAllText(directFilePath);
                        var interpreter = new SharpPy.PythonInterpreter();
                        interpreter.SetGlobalEnv(module.ModuleEnv);
                        interpreter.Execute(code, directFilePath);
                        loadedModules[name] = module;
                        return module;
                    }

                    // 2. 패키지 디렉토리 확인 (예: folder/z/)
                    var packagePath = Path.Combine(searchPath, 
                        string.Join(Path.DirectorySeparatorChar.ToString(), parts));
                    
                    if (Directory.Exists(packagePath))
                    {
                        module = CreatePackageModule(name, packagePath);
                        if (module != null)
                        {
                            loadedModules[name] = module;
                            return module;
                        }
                    }

                    // 3. 중간 패키지가 없는 경우 가상 패키지 생성
                    // 예: "folder.z"를 import할 때 folder/z/ 디렉토리만 있고 
                    // folder/__init__.py가 없어도 동작하도록
                    if (parts.Length > 1)
                    {
                        // 부모 패키지 경로들을 차례로 확인하고 필요시 생성
                        for (int i = 1; i <= parts.Length; i++)
                        {
                            var subParts = parts.Take(i).ToArray();
                            var subName = string.Join(".", subParts);
                            var subPath = Path.Combine(searchPath, 
                                string.Join(Path.DirectorySeparatorChar.ToString(), subParts));
                            
                            // 이미 로드된 경우 스킵
                            if (loadedModules.ContainsKey(subName))
                                continue;
                            
                            // 디렉토리가 존재하면 패키지로 처리
                            if (Directory.Exists(subPath))
                            {
                                var subModule = CreatePackageModule(subName, subPath);
                                if (subModule != null)
                                {
                                    loadedModules[subName] = subModule;
                                    
                                    // 부모 패키지가 있으면 속성으로 추가
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
                            
                            // 마지막 부분이 .py 파일인지 확인
                            if (i == parts.Length)
                            {
                                var filePath = subPath + ".py";
                                if (File.Exists(filePath))
                                {
                                    module = new PythonModule(subName);
                                    string code = File.ReadAllText(filePath);
                                    var interpreter = new SharpPy.PythonInterpreter();
                                    interpreter.SetGlobalEnv(module.ModuleEnv);
                                    interpreter.Execute(code, filePath);
                                    loadedModules[subName] = module;
                                    
                                    // 부모 패키지에 속성으로 추가
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
                        
                        // 전체 이름으로 다시 확인
                        if (loadedModules.ContainsKey(name))
                            return loadedModules[name];
                    }
                }
                
                // 4. 단순 파일명인 경우 기존 로직 유지
                if (parts.Length == 1)
                {
                    foreach (var searchPath in searchPaths)
                    {
                        // .py 파일 확인
                        var filePath = Path.Combine(searchPath, name + ".py");
                        if (File.Exists(filePath))
                        {
                            module = new PythonModule(name);
                            string code = File.ReadAllText(filePath);
                            var interpreter = new SharpPy.PythonInterpreter();
                            interpreter.SetGlobalEnv(module.ModuleEnv);
                            interpreter.Execute(code, filePath);
                            loadedModules[name] = module;
                            return module;
                        }
                        
                        // 패키지 디렉토리 확인
                        var packageDir = Path.Combine(searchPath, name);
                        if (Directory.Exists(packageDir))
                        {
                            module = CreatePackageModule(name, packageDir);
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

        private static PythonModule CreatePackageModule(string packageName, string packageDir)
        {
            try
            {
                var module = new PythonModule(packageName);
                
                // Check for __init__.py first (traditional Python package)
                var initFile = Path.Combine(packageDir, "__init__.py");
                if (File.Exists(initFile))
                {
                    string code = File.ReadAllText(initFile);
                    var interpreter = new SharpPy.PythonInterpreter();
                    interpreter.SetGlobalEnv(module.ModuleEnv);
                    interpreter.Execute(code, initFile);
                }
                
                // Add all .py files in the directory as submodules
                var pyFiles = Directory.GetFiles(packageDir, "*.py");
                foreach (var pyFile in pyFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(pyFile);
                    if (fileName != "__init__") // Skip __init__.py as it's already processed
                    {
                        try
                        {
                            var submodule = new PythonModule($"{packageName}.{fileName}");
                            string code = File.ReadAllText(pyFile);
                            var interpreter = new SharpPy.PythonInterpreter();
                            interpreter.SetGlobalEnv(submodule.ModuleEnv);
                            interpreter.Execute(code, pyFile);
                            
                            // Add submodule to package
                            module.SetAttribute(fileName, submodule);
                            
                            // Also cache the submodule for direct access
                            loadedModules[$"{packageName}.{fileName}"] = submodule;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Failed to load submodule {fileName}: {ex.Message}");
                        }
                    }
                }
                
                return module;
            }
            catch (Exception ex)
            {
                throw new PythonException("ImportError", $"Failed to create package module '{packageName}': {ex.Message}");
            }
        }
        
        private static PythonModule CreateBuiltinModule(string name)
        {
            return name switch
            {
                "math" => CreateMathModule(),
                "random" => CreateRandomModule(),
                "os" => CreateOsModule(),
                _ => null
            };
        }

        private static PythonModule CreateMathModule()
        {
            var module = new PythonModule("math");

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

        private static PythonModule CreateRandomModule()
        {
            var module = new PythonModule("random");
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

        private static PythonModule CreateOsModule()
        {
            var module = new PythonModule("os");

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