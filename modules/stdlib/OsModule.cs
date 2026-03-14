using System;
using System.Collections.Generic;
using System.IO;

#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy.Modules.Stdlib
{
    /// <summary>
    /// Python os 모듈 구현 - 파일 시스템 조작 및 시스템 정보
    /// </summary>
    public static class OsModule
    {
        public static PyModule CreateOsModule()
        {
            var module = new PyModule("os", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\os.py");

            // 시스템 정보
            module.ModuleDict["name"] = new PyStr(GetOsName());
            module.ModuleDict["sep"] = new PyStr(Path.DirectorySeparatorChar.ToString());
            module.ModuleDict["altsep"] = new PyStr(Path.AltDirectorySeparatorChar.ToString());
            module.ModuleDict["pathsep"] = new PyStr(Path.PathSeparator.ToString());
            module.ModuleDict["linesep"] = new PyStr(Environment.NewLine);
            
            // 현재 디렉토리 관련
            module.ModuleDict["getcwd"] = new PyBuiltinFunction("getcwd", GetCurrentDirectory);
            module.ModuleDict["chdir"] = new PyBuiltinFunction("chdir", ChangeDirectory);
            
            // 디렉토리 조작
            module.ModuleDict["listdir"] = new PyBuiltinFunction("listdir", ListDirectory);
            module.ModuleDict["mkdir"] = new PyBuiltinFunction("mkdir", MakeDirectory);
            module.ModuleDict["makedirs"] = new PyBuiltinFunction("makedirs", MakeDirectories);
            module.ModuleDict["rmdir"] = new PyBuiltinFunction("rmdir", RemoveDirectory);
            module.ModuleDict["removedirs"] = new PyBuiltinFunction("removedirs", RemoveDirectories);
            
            // 파일 조작
            module.ModuleDict["remove"] = new PyBuiltinFunction("remove", RemoveFile);
            module.ModuleDict["unlink"] = new PyBuiltinFunction("unlink", RemoveFile); // alias
            module.ModuleDict["rename"] = new PyBuiltinFunction("rename", RenameFile);
            
            // 파일 상태
            module.ModuleDict["stat"] = new PyBuiltinFunction("stat", GetFileStats);
            
            // 환경 변수
            module.ModuleDict["environ"] = new PyEnvironDict();
            module.ModuleDict["getenv"] = new PyBuiltinFunction("getenv", GetEnvironmentVariable);
            
            // os.path 서브모듈
            module.ModuleDict["path"] = CreateOsPathModule();
            
            return module;
        }

        private static string GetOsName()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return "nt";
            else if (Environment.OSVersion.Platform == PlatformID.Unix)
                return "posix";
            else
                return "unknown";
        }

        #region Directory Functions

        private static PyObject GetCurrentDirectory(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create("getcwd() takes no arguments");
                
            try
            {
                return new PyStr(Directory.GetCurrentDirectory());
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"getcwd failed: {ex.Message}");
            }
        }

        private static PyObject ChangeDirectory(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"chdir expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();

            try
            {
                Directory.SetCurrentDirectory(path.Value);
                return PyNone.Instance;
            }
            catch (DirectoryNotFoundException)
            {
                throw PyFileNotFoundError.Create($"No such file or directory: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"chdir failed: {ex.Message}");
            }
        }

        private static PyObject ListDirectory(PyObject[] args)
        {
            string path = ".";

            if (args.Length == 1)
                path = args[0].ToStr().Value;
            else if (args.Length > 1)
                throw PyTypeError.Create($"listdir expected at most 1 argument ({args.Length} given)");

            try
            {
                // Performance: Eliminated LINQ
                var entries = IOHelper.GetFileSystemEntries(path);
                var pyObjects = new PyObject[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                {
                    pyObjects[i] = new PyStr(IOHelper.GetFileName(entries[i]));
                }

                return new PyList(pyObjects);
            }
            catch (DirectoryNotFoundException)
            {
                throw PyFileNotFoundError.Create($"No such file or directory: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"listdir failed: {ex.Message}");
            }
        }

        private static PyObject MakeDirectory(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"mkdir expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();

            try
            {
                IOHelper.CreateDirectory(path.Value);
                return PyNone.Instance;
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"mkdir failed: {ex.Message}");
            }
        }

        // CPython 3.12: Lib/os.py:200-230
        private static PyObject MakeDirectories(PyObject[] args)
        {
            if (args.Length == 0 || args.Length > 3)
                throw PyTypeError.Create($"makedirs expected at most 3 arguments ({args.Length} given)");

            var path = args[0].ToStr();
            int mode = 0x1FF; // 0o777 in decimal = 511, in hex = 0x1FF (not used on Windows)
            bool existOk = false;

            // Parse optional arguments
            if (args.Length >= 2 && args[1] is PyInt modeInt)
            {
                mode = (int)modeInt.Value;
            }
            if (args.Length >= 3)
            {
                // CPython 3.12: exist_ok parameter (third argument)
                // Use PyObject.IsTrue() for truthiness testing
                // CPython reference: Objects/object.c:1675-1697 (PyObject_IsTrue)
                existOk = args[2].IsTrue();
            }

            try
            {
                // CPython 3.12: Lib/os.py:210-230
                // CreateDirectory creates intermediate directories automatically
                IOHelper.CreateDirectory(path.Value);
                return PyNone.Instance;
            }
            catch (Exception ex)
            {
                // CPython 3.12: Lib/os.py:227-230
                // If exist_ok=True and directory exists, don't raise error
                if (existOk && IOHelper.DirExists(path.Value))
                {
                    return PyNone.Instance;
                }
                throw PyOSError.Create($"makedirs failed: {ex.Message}");
            }
        }

        private static PyObject RemoveDirectory(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"rmdir expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();

            try
            {
                IOHelper.DeleteDirectory(path.Value, false); // 비어있는 디렉토리만 삭제
                return PyNone.Instance;
            }
            catch (DirectoryNotFoundException)
            {
                throw PyFileNotFoundError.Create($"No such file or directory: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"rmdir failed: {ex.Message}");
            }
        }

        private static PyObject RemoveDirectories(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"removedirs expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();

            try
            {
                IOHelper.DeleteDirectory(path.Value, true); // 하위 디렉토리까지 모두 삭제
                return PyNone.Instance;
            }
            catch (DirectoryNotFoundException)
            {
                throw PyFileNotFoundError.Create($"No such file or directory: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"removedirs failed: {ex.Message}");
            }
        }

        #endregion

        #region File Functions

        private static PyObject RemoveFile(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"remove expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();

            try
            {
                IOHelper.DeleteFile(path.Value);
                return PyNone.Instance;
            }
            catch (FileNotFoundException)
            {
                throw PyFileNotFoundError.Create($"No such file or directory: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"remove failed: {ex.Message}");
            }
        }

        private static PyObject RenameFile(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"rename expected 2 arguments ({args.Length} given)");

            var oldPath = args[0].ToStr();
            var newPath = args[1].ToStr();

            try
            {
                if (IOHelper.FileExists(oldPath.Value) || IOHelper.DirExists(oldPath.Value))
                    IOHelper.Move(oldPath.Value, newPath.Value);
                else
                    throw PyFileNotFoundError.Create($"No such file or directory: '{oldPath}'");

                return PyNone.Instance;
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"rename failed: {ex.Message}");
            }
        }

        private static PyObject GetFileStats(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"stat expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();

            try
            {
                FileSystemInfo info;

                if (IOHelper.FileExists(path.Value))
                    info = new FileInfo(path.Value);
                else if (IOHelper.DirExists(path.Value))
                    info = new DirectoryInfo(path.Value);
                else
                    throw PyFileNotFoundError.Create($"No such file or directory: '{path}'");

                return new PyStatResult(info);
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"stat failed: {ex.Message}");
            }
        }

        #endregion

        #region Environment Functions

        private static PyObject GetEnvironmentVariable(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 2)
                throw PyTypeError.Create($"getenv expected 1 or 2 arguments ({args.Length} given)");

            var key = args[0].ToStr();
            var value = Environment.GetEnvironmentVariable(key.Value);
            
            if (value != null)
                return new PyStr(value);
            else if (args.Length == 2)
                return args[1]; // 기본값 반환
            else
                return PyNone.Instance;
        }

        #endregion

        #region os.path Module

        private static PyModule CreateOsPathModule()
        {
            var pathModule = new PyModule("path");
            
            pathModule.ModuleDict["exists"] = new PyBuiltinFunction("exists", PathExists);
            pathModule.ModuleDict["isfile"] = new PyBuiltinFunction("isfile", PathIsFile);
            pathModule.ModuleDict["isdir"] = new PyBuiltinFunction("isdir", PathIsDirectory);
            pathModule.ModuleDict["join"] = new PyBuiltinFunction("join", PathJoin);
            pathModule.ModuleDict["split"] = new PyBuiltinFunction("split", PathSplit);
            pathModule.ModuleDict["dirname"] = new PyBuiltinFunction("dirname", PathDirname);
            pathModule.ModuleDict["basename"] = new PyBuiltinFunction("basename", PathBasename);
            pathModule.ModuleDict["abspath"] = new PyBuiltinFunction("abspath", PathAbspath);
            pathModule.ModuleDict["getsize"] = new PyBuiltinFunction("getsize", PathGetSize);
            
            return pathModule;
        }

        private static PyObject PathExists(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"exists expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();
            return PyBool.FromBool(IOHelper.FileExists(path.Value) || IOHelper.DirExists(path.Value));
        }

        private static PyObject PathIsFile(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"isfile expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();
            return PyBool.FromBool(IOHelper.FileExists(path.Value));
        }

        private static PyObject PathIsDirectory(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"isdir expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();
            return PyBool.FromBool(IOHelper.DirExists(path.Value));
        }

        private static PyObject PathJoin(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("join expected at least 1 argument (0 given)");

            // Performance: Eliminated LINQ
            var paths = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                paths[i] = args[i].ToStr().Value;
            }
            var result = IOHelper.CombinePath(paths);
            return new PyStr(result);
        }

        private static PyObject PathSplit(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"split expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();
            var dirname = IOHelper.GetDirectoryName(path.Value) ?? "";
            var basename = IOHelper.GetFileName(path.Value);

            return new PyTuple(new PyStr(dirname), new PyStr(basename));
        }

        private static PyObject PathDirname(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"dirname expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();
            var dirname = IOHelper.GetDirectoryName(path.Value) ?? "";
            return new PyStr(dirname);
        }

        private static PyObject PathBasename(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"basename expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();
            var basename = IOHelper.GetFileName(path.Value);
            return new PyStr(basename);
        }

        private static PyObject PathAbspath(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"abspath expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();
            var abspath = IOHelper.GetFullPath(path.Value);
            return new PyStr(abspath);
        }

        private static PyObject PathGetSize(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"getsize expected 1 argument ({args.Length} given)");

            var path = args[0].ToStr();
            
            try
            {
                var info = new FileInfo(path.Value);
                if (!info.Exists)
                    throw PyFileNotFoundError.Create($"No such file or directory: '{path}'");
                    
                return new PyInt((int)info.Length);
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"getsize failed: {ex.Message}");
            }
        }

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// os.environ - 환경 변수 딕셔너리
    /// </summary>
    public class PyEnvironDict : PyObject
    {
        public override PyType GetPyType() => PyType.DictType;
        public override string GetTypeName() => "dict";

        public override PyObject GetItem(PyObject key)
        {
            var keyStr = key.ToStr();
            var value = Environment.GetEnvironmentVariable(keyStr.Value);
            
            if (value != null)
                return new PyStr(value);
            else
                throw PyKeyError.Create($"'{keyStr}'");
        }

        public override void SetItem(PyObject key, PyObject value)
        {
            var keyStr = key.ToStr();
            var valueStr = value.ToStr();
            Environment.SetEnvironmentVariable(keyStr.Value, valueStr.Value);
        }

        public override PyBool Contains(PyObject key)
        {
            var keyStr = key.ToStr();
            return PyBool.FromBool(Environment.GetEnvironmentVariable(keyStr.Value) != null);
        }

        public override string ToString()
        {
            // Performance: Eliminated LINQ
            var envVars = Environment.GetEnvironmentVariables();
            var entries = new List<string>();
            int count = 0;
            foreach (System.Collections.DictionaryEntry entry in envVars)
            {
                if (count >= 3) break;
                entries.Add($"'{entry.Key}': '{entry.Value}'");
                count++;
            }
            return $"environ({{{string.Join(", ", entries)}...}})";
        }
    }

    /// <summary>
    /// os.stat() 결과 객체
    /// </summary>
    public class PyStatResult : PyObject
    {
        private readonly FileSystemInfo _info;

        public PyStatResult(FileSystemInfo info)
        {
            _info = info;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "stat_result";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "st_mode":
                    return new PyInt((_info is DirectoryInfo) ? 16877 : 33188); // 디렉토리/파일 구분
                case "st_size":
                    return new PyInt(_info is FileInfo fi ? (int)fi.Length : 0);
                case "st_mtime":
                    return new PyFloat(_info.LastWriteTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);
                case "st_ctime":
                    return new PyFloat(_info.CreationTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);
                case "st_atime":
                    return new PyFloat(_info.LastAccessTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);
                default:
                    throw PyAttributeError.Create($"'stat_result' object has no attribute '{name}'");
            }
        }

        public override string ToString() => $"os.stat_result(st_mode={(_info is DirectoryInfo ? 16877 : 33188)}, st_size={(_info is FileInfo fi ? fi.Length : 0)})";
    }

    #endregion
}