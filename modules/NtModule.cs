using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython 3.12 'nt' module (Windows) / 'posix' module (Unix)
    /// Cross-platform implementation using .NET Core
    ///
    /// CPython: Modules/posixmodule.c (same file for both nt and posix)
    /// SharpPy: One C# module for all platforms
    /// </summary>
    public static class NtModule
    {
        public static PyModule CreateNtModule()
        {
            // Module name depends on platform (CPython 3.12 behavior)
            string moduleName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "nt" : "posix";
            var module = new PyModule(moduleName);

            // Constants - File access modes (CPython: posixmodule.c line 14000+)
            module.ModuleDict["F_OK"] = new PyInt(0);  // File exists
            module.ModuleDict["R_OK"] = new PyInt(4);  // Read permission
            module.ModuleDict["W_OK"] = new PyInt(2);  // Write permission
            module.ModuleDict["X_OK"] = new PyInt(1);  // Execute permission

            // Core functions (CPython: posixmodule.c)
            module.ModuleDict["getcwd"] = new PyBuiltinFunction("getcwd", Getcwd);
            module.ModuleDict["chdir"] = new PyBuiltinFunction("chdir", Chdir);
            module.ModuleDict["listdir"] = new PyBuiltinFunction("listdir", Listdir);
            module.ModuleDict["mkdir"] = new PyBuiltinFunction("mkdir", Mkdir);
            module.ModuleDict["rmdir"] = new PyBuiltinFunction("rmdir", Rmdir);
            module.ModuleDict["remove"] = new PyBuiltinFunction("remove", Remove);
            module.ModuleDict["unlink"] = new PyBuiltinFunction("unlink", Remove);  // alias
            module.ModuleDict["rename"] = new PyBuiltinFunction("rename", Rename);
            module.ModuleDict["stat"] = new PyBuiltinFunction("stat", Stat);
            module.ModuleDict["access"] = new PyBuiltinFunction("access", Access);
            module.ModuleDict["getenv"] = new PyBuiltinFunction("getenv", Getenv);
            module.ModuleDict["putenv"] = new PyBuiltinFunction("putenv", Putenv);
            module.ModuleDict["fspath"] = new PyBuiltinFunction("fspath", Fspath);
            module.ModuleDict["urandom"] = new PyBuiltinFunction("urandom", Urandom);

            // CPython 3.12: environ is a mapping
            module.ModuleDict["environ"] = CreateEnvironDict();

            return module;
        }

        #region Core Functions

        // getcwd() -> str
        // CPython: posixmodule.c os_getcwd_impl
        private static PyObject Getcwd(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create("getcwd() takes no arguments");

            try
            {
                return new PyStr(Directory.GetCurrentDirectory());
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"[Errno 2] {ex.Message}");
            }
        }

        // chdir(path)
        // CPython: posixmodule.c os_chdir_impl
        private static PyObject Chdir(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"chdir() takes exactly 1 argument ({args.Length} given)");

            string path = args[0].ToStr().Value;
            try
            {
                Directory.SetCurrentDirectory(path);
                return PyNone.Instance;
            }
            catch (DirectoryNotFoundException)
            {
                throw PyOSError.Create($"[Errno 2] No such file or directory: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"[Errno 1] {ex.Message}");
            }
        }

        // listdir(path='.') -> list[str]
        // CPython: posixmodule.c os_listdir_impl
        private static PyObject Listdir(PyObject[] args)
        {
            string path = args.Length > 0 ? args[0].ToStr().Value : ".";

            try
            {
                var entries = IOHelper.GetFileSystemEntries(path);
                var result = new List<PyObject>();
                foreach (var entry in entries)
                {
                    result.Add(new PyStr(IOHelper.GetFileName(entry)));
                }
                return new PyList(result);
            }
            catch (DirectoryNotFoundException)
            {
                throw PyOSError.Create($"[Errno 2] No such file or directory: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"[Errno 1] {ex.Message}");
            }
        }

        // mkdir(path, mode=0o777)
        // CPython: posixmodule.c os_mkdir_impl
        private static PyObject Mkdir(PyObject[] args)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("mkdir() missing required argument: 'path' (pos 1)");

            string path = args[0].ToStr().Value;
            // Note: .NET doesn't support Unix-style mode parameter

            try
            {
                IOHelper.CreateDirectory(path);
                return PyNone.Instance;
            }
            catch (IOException ex)
            {
                throw PyOSError.Create($"[Errno 17] File exists: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"[Errno 1] {ex.Message}");
            }
        }

        // rmdir(path)
        // CPython: posixmodule.c os_rmdir_impl
        private static PyObject Rmdir(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"rmdir() takes exactly 1 argument ({args.Length} given)");

            string path = args[0].ToStr().Value;
            try
            {
                IOHelper.DeleteDirectory(path, false);
                return PyNone.Instance;
            }
            catch (DirectoryNotFoundException)
            {
                throw PyOSError.Create($"[Errno 2] No such file or directory: '{path}'");
            }
            catch (IOException)
            {
                throw PyOSError.Create($"[Errno 39] Directory not empty: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"[Errno 1] {ex.Message}");
            }
        }

        // remove(path) / unlink(path)
        // CPython: posixmodule.c os_unlink_impl
        private static PyObject Remove(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"remove() takes exactly 1 argument ({args.Length} given)");

            string path = args[0].ToStr().Value;
            try
            {
                IOHelper.DeleteFile(path);
                return PyNone.Instance;
            }
            catch (FileNotFoundException)
            {
                throw PyOSError.Create($"[Errno 2] No such file or directory: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"[Errno 1] {ex.Message}");
            }
        }

        // rename(src, dst)
        // CPython: posixmodule.c os_rename_impl
        private static PyObject Rename(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"rename() takes exactly 2 arguments ({args.Length} given)");

            string src = args[0].ToStr().Value;
            string dst = args[1].ToStr().Value;
            try
            {
                if (IOHelper.FileExists(src) || IOHelper.DirExists(src))
                    IOHelper.Move(src, dst);
                else
                    throw new FileNotFoundException();
                return PyNone.Instance;
            }
            catch (FileNotFoundException)
            {
                throw PyOSError.Create($"[Errno 2] No such file or directory: '{src}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"[Errno 1] {ex.Message}");
            }
        }

        // stat(path) -> stat_result
        // CPython: posixmodule.c os_stat_impl
        private static PyObject Stat(PyObject[] args)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("stat() missing required argument: 'path' (pos 1)");

            string path = args[0].ToStr().Value;
            try
            {
                FileSystemInfo info;
                if (IOHelper.FileExists(path))
                    info = new FileInfo(path);
                else if (IOHelper.DirExists(path))
                    info = new DirectoryInfo(path);
                else
                    throw new FileNotFoundException();

                // CPython 3.12: Return stat_result object with attributes (not dict)
                return new PyStatResult(info);
            }
            catch (FileNotFoundException)
            {
                throw PyOSError.Create($"[Errno 2] No such file or directory: '{path}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"[Errno 1] {ex.Message}");
            }
        }

        // access(path, mode) -> bool
        // CPython: posixmodule.c os_access_impl
        private static PyObject Access(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"access() takes exactly 2 arguments ({args.Length} given)");

            string path = args[0].ToStr().Value;
            int mode = args[1].ToInt();

            try
            {
                // F_OK (0): exists
                if (mode == 0)
                {
                    bool exists = IOHelper.FileExists(path) || IOHelper.DirExists(path);
                    return exists ? PyBool.True : PyBool.False;
                }

                // R_OK, W_OK, X_OK - simplified for cross-platform
                FileSystemInfo info;
                if (IOHelper.FileExists(path))
                    info = new FileInfo(path);
                else if (IOHelper.DirExists(path))
                    info = new DirectoryInfo(path);
                else
                    return PyBool.False;

                // Basic permission check (limited in .NET)
                bool canRead = (info.Attributes & FileAttributes.ReadOnly) == 0 || (mode & 4) != 0;
                return canRead ? PyBool.True : PyBool.False;
            }
            catch
            {
                return PyBool.False;
            }
        }

        // getenv(key, default=None)
        // CPython: posixmodule.c os_getenv_impl
        private static PyObject Getenv(PyObject[] args)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("getenv() missing required argument: 'key' (pos 1)");

            string key = args[0].ToStr().Value;
            string value = Environment.GetEnvironmentVariable(key);

            if (value == null)
                return args.Length > 1 ? args[1] : PyNone.Instance;

            return new PyStr(value);
        }

        // putenv(key, value)
        // CPython: posixmodule.c os_putenv_impl
        private static PyObject Putenv(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"putenv() takes exactly 2 arguments ({args.Length} given)");

            string key = args[0].ToStr().Value;
            string value = args[1].ToStr().Value;

            Environment.SetEnvironmentVariable(key, value);
            return PyNone.Instance;
        }

        // fspath(path) -> str
        // CPython: posixmodule.c os_fspath_impl
        private static PyObject Fspath(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"fspath() takes exactly 1 argument ({args.Length} given)");

            // For now, just convert to string (proper implementation would check __fspath__)
            return new PyStr(args[0].ToStr().Value);
        }

        #endregion

        #region Helper Functions

        private static PyDict CreateEnvironDict()
        {
            var environ = new PyDict();
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                environ.SetItem(new PyStr((string)entry.Key), new PyStr((string)entry.Value));
            }
            return environ;
        }

        // urandom(n) -> bytes
        // CPython: posixmodule.c os_urandom_impl
        // Generate n random bytes using cryptographically strong random number generator
        private static PyObject Urandom(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"urandom() takes exactly 1 argument ({args.Length} given)");

            int n = (int)args[0].ToInt();

            if (n < 0)
                throw PyValueError.Create("negative argument not allowed");

            if (n == 0)
                return new PyBytes(new byte[0]);

            // Use .NET's cryptographically strong random number generator
            byte[] buffer = new byte[n];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(buffer);
            }

            return new PyBytes(buffer);
        }

        #endregion
    }

    /// <summary>
    /// CPython 3.12 stat_result object (os.stat_result)
    /// CPython: Objects/structseq.c + Modules/posixmodule.c
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
                    // CPython: posixmodule.c - file mode bits
                    // S_IFDIR (0o40000) for directory, S_IFREG (0o100000) for regular file
                    return new PyInt((_info is DirectoryInfo) ? 0x4000 | 0x1ED : 0x8000 | 0x1A4);
                case "st_size":
                    return new PyInt(_info is FileInfo fi ? fi.Length : 0);
                case "st_mtime":
                    return new PyFloat(ToUnixTime(_info.LastWriteTime));
                case "st_ctime":
                    return new PyFloat(ToUnixTime(_info.CreationTime));
                case "st_atime":
                    return new PyFloat(ToUnixTime(_info.LastAccessTime));
                case "st_ino":
                    return new PyInt(0); // inode (not available on Windows)
                case "st_dev":
                    return new PyInt(0); // device (not available in .NET)
                case "st_nlink":
                    return new PyInt(1); // number of hard links
                case "st_uid":
                    return new PyInt(0); // user id (not available on Windows)
                case "st_gid":
                    return new PyInt(0); // group id (not available on Windows)
                default:
                    throw PyAttributeError.Create($"'stat_result' object has no attribute '{name}'");
            }
        }

        private static double ToUnixTime(DateTime dt)
        {
            return (dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public override PyStr ToRepr()
        {
            var mode = (_info is DirectoryInfo) ? 0x4000 | 0x1ED : 0x8000 | 0x1A4;
            var size = _info is FileInfo fi ? fi.Length : 0;
            return new PyStr($"os.stat_result(st_mode={mode}, st_ino=0, st_dev=0, st_nlink=1, st_uid=0, st_gid=0, st_size={size}, st_atime={ToUnixTime(_info.LastAccessTime)}, st_mtime={ToUnixTime(_info.LastWriteTime)}, st_ctime={ToUnixTime(_info.CreationTime)})");
        }

        public override string ToString() => ToRepr().Value;
    }
}
