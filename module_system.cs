// ModuleSystem class from enhanced_modules_environment.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpPy
{
    public static class ModuleSystem
    {
        private static Dictionary<string, PythonModule> loadedModules = new Dictionary<string, PythonModule>();

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

            // Use default search paths if none provided
            if (searchPaths == null)
            {
                searchPaths = new List<string> { "." };
            }

            // Try to load from file system using search paths
            try
            {
                string foundPath = null;

                // Handle package.module notation (e.g., "folder.m")
                if (name.Contains('.'))
                {
                    var parts = name.Split('.');
                    var relativePath = string.Join(Path.DirectorySeparatorChar.ToString(), parts) + ".py";
                    
                    // Search in all paths
                    foreach (var searchPath in searchPaths)
                    {
                        var candidatePath = Path.Combine(searchPath, relativePath);
                        if (File.Exists(candidatePath))
                        {
                            foundPath = candidatePath;
                            break;
                        }
                        
                        // Try folder/module structure
                        var folderPath = Path.Combine(searchPath, parts[0], parts[1] + ".py");
                        if (File.Exists(folderPath))
                        {
                            foundPath = folderPath;
                            break;
                        }
                    }
                }
                else
                {
                    // Simple module name - could be a file or a package directory
                    foreach (var searchPath in searchPaths)
                    {
                        // First try as a .py file
                        var candidatePath = Path.Combine(searchPath, name + ".py");
                        if (File.Exists(candidatePath))
                        {
                            foundPath = candidatePath;
                            break;
                        }
                        
                        // Then try as a package directory
                        var packageDir = Path.Combine(searchPath, name);
                        if (Directory.Exists(packageDir))
                        {
                            // Create a package module (directory-based)
                            module = CreatePackageModule(name, packageDir);
                            if (module != null)
                            {
                                loadedModules[name] = module;
                                return module;
                            }
                        }
                    }
                }

                if (foundPath != null)
                {
                    string code = File.ReadAllText(foundPath);
                    module = new PythonModule(name);

                    var interpreter = new SharpPy.PythonInterpreter();
                    interpreter.SetGlobalEnv(module.ModuleEnv);
                    interpreter.Execute(code, foundPath);

                    loadedModules[name] = module;
                    return module;
                }
            }
            catch (PythonException ex)
            {
                throw new PythonException("ImportError", $"Failed to import module '{name}': {ex.Message}", ex.Line, ex.Column, ex.FileName);
            }
            catch (Exception ex)
            {
                throw new PythonException("ImportError", $"Failed to import module '{name}': {ex.Message}");
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