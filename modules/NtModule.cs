using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

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
                return new PyString(Directory.GetCurrentDirectory());
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
                var entries = Directory.GetFileSystemEntries(path);
                var result = new List<PyObject>();
                foreach (var entry in entries)
                {
                    result.Add(new PyString(Path.GetFileName(entry)));
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
                Directory.CreateDirectory(path);
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
                Directory.Delete(path, false);
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
                File.Delete(path);
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
                if (File.Exists(src))
                    File.Move(src, dst);
                else if (Directory.Exists(src))
                    Directory.Move(src, dst);
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
                if (File.Exists(path))
                    info = new FileInfo(path);
                else if (Directory.Exists(path))
                    info = new DirectoryInfo(path);
                else
                    throw new FileNotFoundException();

                // CPython stat_result has: st_mode, st_ino, st_dev, st_nlink, st_uid, st_gid,
                // st_size, st_atime, st_mtime, st_ctime
                var result = new PyDict();
                result.SetItem(new PyString("st_size"), new PyInt(info is FileInfo fi ? fi.Length : 0));
                result.SetItem(new PyString("st_mtime"), new PyFloat(ToUnixTime(info.LastWriteTime)));
                result.SetItem(new PyString("st_atime"), new PyFloat(ToUnixTime(info.LastAccessTime)));
                result.SetItem(new PyString("st_ctime"), new PyFloat(ToUnixTime(info.CreationTime)));
                result.SetItem(new PyString("st_mode"), new PyInt(GetFileMode(info)));

                return result;
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
                    bool exists = File.Exists(path) || Directory.Exists(path);
                    return exists ? PyBool.True : PyBool.False;
                }

                // R_OK, W_OK, X_OK - simplified for cross-platform
                FileSystemInfo info;
                if (File.Exists(path))
                    info = new FileInfo(path);
                else if (Directory.Exists(path))
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

            return new PyString(value);
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
            return new PyString(args[0].ToStr().Value);
        }

        #endregion

        #region Helper Functions

        private static double ToUnixTime(DateTime dt)
        {
            return (dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static int GetFileMode(FileSystemInfo info)
        {
            // Simplified mode (Unix: 0o100644 for files, 0o040755 for dirs)
            if (info is DirectoryInfo)
                return 0x4000 | 0x1ED; // S_IFDIR | 0755
            else
                return 0x8000 | 0x1A4; // S_IFREG | 0644
        }

        private static PyDict CreateEnvironDict()
        {
            var environ = new PyDict();
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                environ.SetItem(new PyString((string)entry.Key), new PyString((string)entry.Value));
            }
            return environ;
        }

        #endregion
    }
}
