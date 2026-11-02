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
            // os.py reads which functions take dir_fd and the like from here.
            foreach (var (name, value) in new[]
            {
                ("O_RDONLY", 0), ("O_WRONLY", 1), ("O_RDWR", 2), ("O_APPEND", 8), ("O_RANDOM", 16),
                ("O_SEQUENTIAL", 32), ("O_TEMPORARY", 64), ("O_NOINHERIT", 128), ("O_CREAT", 256),
                ("O_TRUNC", 512), ("O_EXCL", 1024), ("O_SHORT_LIVED", 4096), ("O_TEXT", 16384),
                ("O_BINARY", 32768), ("TMP_MAX", 2147483647),
            })
                module.ModuleDict[name] = new PyInt(value);
            module.ModuleDict["_have_functions"] = new PyList(new List<PyObject>
            {
                new PyString("HAVE_FTRUNCATE"), new PyString("HAVE_LSTAT"), new PyString("MS_WINDOWS"),
            });
            module.ModuleDict["stat_result"] = PyStatResult.Type;
            module.ModuleDict["DirEntry"] = PyDirEntry.Type;
            module.ModuleDict["lstat"] = new PyBuiltinFunction("lstat", Stat);
            module.ModuleDict["scandir"] = new PyBuiltinFunction("scandir", Scandir);
            module.ModuleDict["replace"] = new PyBuiltinFunction("replace", Replace);
            module.ModuleDict["chmod"] = new PyBuiltinFunction("chmod", Chmod);
            module.ModuleDict["utime"] = new PyBuiltinFunction("utime", Utime);
            module.ModuleDict["strerror"] = new PyBuiltinFunction("strerror", Strerror);
            module.ModuleDict["cpu_count"] = new PyBuiltinFunction("cpu_count", args => new PyInt(Environment.ProcessorCount));
            module.ModuleDict["_exit"] = new PyBuiltinFunction("_exit", args =>
            {
                Environment.Exit(args[0].ToInt());
                return PyNone.Instance;
            });
            module.ModuleDict["waitstatus_to_exitcode"] = new PyBuiltinFunction("waitstatus_to_exitcode", args => new PyInt(args[0].ToInt() >> 8));
            // No child process is ever started here.
            module.ModuleDict["waitpid"] = new PyBuiltinFunction("waitpid", args =>
                throw new PythonException(PyOSError.FromArguments(new PyObject[] { new PyInt(10), new PyString("No child processes") }, null)));
            module.ModuleDict["pipe"] = new PyBuiltinFunction("pipe", Pipe);
            module.ModuleDict["open"] = new PyBuiltinFunction("open", Open);
            module.ModuleDict["close"] = new PyBuiltinFunction("close", Close);
            module.ModuleDict["read"] = new PyBuiltinFunction("read", Read);
            module.ModuleDict["write"] = new PyBuiltinFunction("write", Write);
            module.ModuleDict["lseek"] = new PyBuiltinFunction("lseek", Lseek);
            module.ModuleDict["fsync"] = new PyBuiltinFunction("fsync", Fsync);
            module.ModuleDict["ftruncate"] = new PyBuiltinFunction("ftruncate", Ftruncate);
            module.ModuleDict["isatty"] = new PyBuiltinFunction("isatty", Isatty);
            module.ModuleDict["fstat"] = new PyBuiltinFunction("fstat", Fstat);
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
            module.ModuleDict["unsetenv"] = new PyBuiltinFunction("unsetenv", Unsetenv);
            module.ModuleDict["getpid"] = new PyBuiltinFunction("getpid", Getpid);
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

            string path = PathOf(args[0]);
            try
            {
                Directory.SetCurrentDirectory(path);
                return PyNone.Instance;
            }
            catch (DirectoryNotFoundException)
            {
                throw WindowsOr(3, path);
            }
            catch (Exception ex)
            {
                throw Failed(ex, path);
            }
        }

        // listdir(path='.') -> list[str]
        // CPython: posixmodule.c os_listdir_impl
        private static PyObject Listdir(PyObject[] args)
        {
            string path = args.Length > 0 && args[0] is not PyNone ? PathOf(args[0]) : ".";

            try
            {
                if (IOHelper.FileExists(path))
                    throw WindowsOr(267, path);
                var entries = IOHelper.GetFileSystemEntries(path);
                var result = new List<PyObject>();
                foreach (var entry in entries)
                {
                    result.Add(new PyString(IOHelper.GetFileName(entry)));
                }
                return new PyList(result);
            }
            catch (PythonException)
            {
                throw;
            }
            catch (DirectoryNotFoundException)
            {
                throw WindowsOr(3, path);
            }
            catch (Exception ex)
            {
                throw Failed(ex, path);
            }
        }

        // mkdir(path, mode=0o777)
        // CPython: posixmodule.c os_mkdir_impl
        private static PyObject Mkdir(PyObject[] args)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("mkdir() missing required argument: 'path' (pos 1)");

            string path = PathOf(args[0]);
            // Note: .NET doesn't support Unix-style mode parameter

            try
            {
                // CreateDirectoryW makes one level and refuses what exists.
                if (IOHelper.DirExists(path) || IOHelper.FileExists(path))
                    throw WindowsOr(183, path);
                string parent = Path.GetDirectoryName(Path.GetFullPath(path));
                if (parent != null && !IOHelper.DirExists(parent))
                    throw WindowsOr(3, path);
                IOHelper.CreateDirectory(path);
                return PyNone.Instance;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw Failed(ex, path);
            }
        }

        // rmdir(path)
        // CPython: posixmodule.c os_rmdir_impl
        private static PyObject Rmdir(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"rmdir() takes exactly 1 argument ({args.Length} given)");

            string path = PathOf(args[0]);
            int attempt = 0;
            while (true)
            try
            {
                IOHelper.DeleteDirectory(path, false);
                return PyNone.Instance;
            }
            catch (Exception ex) when (RetryAfterRelease(ex, ref attempt)) { }
            catch (DirectoryNotFoundException)
            {
                throw WindowsOr(3, path);
            }
            catch (IOException)
            {
                throw WindowsOr(145, path);
            }
            catch (Exception ex)
            {
                throw Failed(ex, path);
            }
        }

        // remove(path) / unlink(path)
        // CPython: posixmodule.c os_unlink_impl
        private static PyObject Remove(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"remove() takes exactly 1 argument ({args.Length} given)");

            string path = PathOf(args[0]);
            int attempt = 0;
            while (true)
            try
            {
                // .NET File.Delete is silent for missing files; CPython reports them.
                if (!IOHelper.FileExists(path))
                {
#if GODOT
                    throw PyOSError.Create($"[Errno 2] No such file or directory: '{path}'");
#else
                    throw PyOSError.CreateWindowsFileError(2, path);
#endif
                }
                IOHelper.DeleteFile(path);
                return PyNone.Instance;
            }
            catch (Exception ex) when (RetryAfterRelease(ex, ref attempt)) { }
            catch (FileNotFoundException)
            {
                throw WindowsOr(2, path);
            }
            catch (DirectoryNotFoundException)
            {
                throw WindowsOr(3, path);
            }
            catch (UnauthorizedAccessException)
            {
                throw WindowsOr(5, path);
            }
            catch (IOException)
            {
                throw WindowsOr(5, path);
            }
        }

        // Windows refuses to delete or rename anything a handle still has open. CPython does not get
        // here for an abandoned open(), because its reference count hit zero and iobase_finalize
        // already ran close() (Modules/_io/iobase.c:298). SharpPy can still hold the handle when the
        // CLR has not collected the dead file object yet, so before reporting a failure we let the
        // release bus take back whatever Python can no longer reach and try once (core/PyResourceBus.cs).
        // The outcome then matches CPython; only the moment of release differs, which is exactly the
        // trade docs/lifetime_and_gc_policy.md records for resource state. Success path pays nothing:
        // the filter is never entered.
        private static bool RetryAfterRelease(Exception error, ref int attempt)
        {
            if (attempt++ != 0) return false;
            if (!(error is IOException) && !(error is UnauthorizedAccessException)) return false;
            return PyResourceBus.ReleaseUnreferenced(forceCollection: true) > 0;
        }

        // win32_error: what .NET threw, as the code GetLastError held.
        private static PythonException Failed(Exception error, string path)
        {
            int code = error switch
            {
                FileNotFoundException => 2,
                DirectoryNotFoundException => 3,
                UnauthorizedAccessException => 5,
                IOException when (error.HResult & 0xffff) != 0 => error.HResult & 0xffff,
                _ => 5,
            };
            return WindowsOr(code, path);
        }

        private static int Descriptor(PyObject value) =>
            (int)(PyObject.TryGetIndex(value)?.Value ?? throw PyTypeError.Create($"'{value.GetTypeName()}' object cannot be interpreted as an integer"));

        // path_converter
        private static string PathOf(PyObject value)
        {
            var path = Fspath(new[] { value });
            return path is PyBytes bytes ? System.Text.Encoding.UTF8.GetString(bytes.Value) : ((PyString)path).Value;
        }

        internal static PyObject StatPath(string path) => Stat(new PyObject[] { new PyString(path) });

        // os_scandir_impl
        private static PyObject Scandir(PyObject[] args)
        {
            string path = args.Length > 0 && args[0] is not PyNone ? PathOf(args[0]) : ".";
            try
            {
                if (IOHelper.FileExists(path))
                    throw WindowsOr(267, path);
                var entries = new List<PyDirEntry>();
                string separator = path.EndsWith("/") || path.EndsWith("\\") ? "" : Path.DirectorySeparatorChar.ToString();
                foreach (var entry in IOHelper.GetFileSystemEntries(path))
                {
                    string name = IOHelper.GetFileName(entry);
                    entries.Add(new PyDirEntry(name, path + separator + name));
                }
                return new PyScandirIterator(entries);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw Failed(error, path);
            }
        }

        // os_replace_impl: the destination goes.
        private static PyObject Replace(PyObject[] args)
        {
            string source = PathOf(args[0]), target = PathOf(args[1]);
            try
            {
                if (IOHelper.DirExists(source))
                    Directory.Move(source, target);
                else if (IOHelper.FileExists(source))
                    File.Move(source, target, true);
                else
                    throw WindowsOr(2, source, target);
                return PyNone.Instance;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw Failed(error, source);
            }
        }

        // win32_chmod: the one bit Windows has.
        private static PyObject Chmod(PyObject[] args)
        {
            string path = PathOf(args[0]);
            if (!IOHelper.FileExists(path) && !IOHelper.DirExists(path))
                throw WindowsOr(2, path);
            var attributes = File.GetAttributes(path);
            bool writable = (args[1].ToInt() & 0x80) != 0;
            File.SetAttributes(path, writable ? attributes & ~FileAttributes.ReadOnly : attributes | FileAttributes.ReadOnly);
            return PyNone.Instance;
        }

        private static PyObject Utime(PyObject[] args)
        {
            string path = PathOf(args[0]);
            if (!IOHelper.FileExists(path) && !IOHelper.DirExists(path))
                throw WindowsOr(2, path);
            var now = DateTime.Now;
            var access = now;
            var modified = now;
            if (args.Length > 1 && args[1] is PyTuple times)
            {
                access = DateTime.UnixEpoch.AddSeconds(times.Items[0].AsFloat().Value).ToLocalTime();
                modified = DateTime.UnixEpoch.AddSeconds(times.Items[1].AsFloat().Value).ToLocalTime();
            }
            File.SetLastAccessTime(path, access);
            File.SetLastWriteTime(path, modified);
            return PyNone.Instance;
        }

        // The C runtime's strerror, for the numbers errno.c names on Windows.
        private static PyObject Strerror(PyObject[] args) => new PyString(args[0].ToInt() switch
        {
            1 => "Operation not permitted",
            2 => "No such file or directory",
            5 => "Input/output error",
            9 => "Bad file descriptor",
            13 => "Permission denied",
            17 => "File exists",
            20 => "Not a directory",
            21 => "Is a directory",
            22 => "Invalid argument",
            int other => $"Unknown error {other}",
        });

        // os_pipe_impl
        private static PyObject Pipe(PyObject[] args)
        {
            var server = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
            var client = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, server.ClientSafePipeHandle);
            return new PyTuple(new PyObject[] { new PyInt(PyFileDescriptors.Add(client)), new PyInt(PyFileDescriptors.Add(server)) });
        }

        // os_open_impl
        private static PyObject Open(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 2)
                throw PyTypeError.Create($"open() missing required argument '{(args.Length == 0 ? "path" : "flags")}' (pos {args.Length + 1})");
            string path = PathOf(args[0]);
            try
            {
                return new PyInt(PyFileDescriptors.Open(path, Descriptor(args[1])));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw Failed(error, path);
            }
        }

        private static PyObject Close(PyObject[] args)
        {
            PyFileDescriptors.Close(Descriptor(args[0]));
            return PyNone.Instance;
        }

        // os_read_impl
        private static PyObject Read(PyObject[] args)
        {
            int length = Descriptor(args[1]);
            if (length < 0)
                throw new PythonException(PyOSError.FromArguments(new PyObject[] { new PyInt(22), new PyString("Invalid argument") }, null));
            var buffer = new byte[length];
            var stream = PyFileDescriptors.Get(Descriptor(args[0]));
            int count = 0;
            PyGil.AllowThreads(() => count = stream.Read(buffer, 0, length));
            Array.Resize(ref buffer, count);
            return new PyBytes(buffer);
        }

        // os_write_impl
        private static PyObject Write(PyObject[] args)
        {
            var stream = PyFileDescriptors.Get(Descriptor(args[0]));
            var data = BytesMethods.Buffer(args[1]);
            PyGil.AllowThreads(() =>
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();
            });
            return new PyInt(data.Length);
        }

        private static PyObject Lseek(PyObject[] args)
        {
            var origin = Descriptor(args[2]) switch { 0 => SeekOrigin.Begin, 1 => SeekOrigin.Current, _ => SeekOrigin.End };
            return new PyInt(PyFileDescriptors.Get(Descriptor(args[0])).Seek((long)PyObject.TryGetIndex(args[1]).Value, origin));
        }

        private static PyObject Fsync(PyObject[] args)
        {
            PyFileDescriptors.Get(Descriptor(args[0])).Flush();
            return PyNone.Instance;
        }

        private static PyObject Ftruncate(PyObject[] args)
        {
            PyFileDescriptors.Get(Descriptor(args[0])).SetLength((long)PyObject.TryGetIndex(args[1]).Value);
            return PyNone.Instance;
        }

        private static PyObject Isatty(PyObject[] args)
        {
            int fd = Descriptor(args[0]);
            return PyBool.FromBool(fd switch
            {
                0 => !Console.IsInputRedirected,
                1 => !Console.IsOutputRedirected,
                2 => !Console.IsErrorRedirected,
                _ => false,
            });
        }

        private static PyObject Fstat(PyObject[] args)
        {
            if (PyFileDescriptors.Get(Descriptor(args[0])) is FileStream file)
            {
                file.Flush();
                return Stat(new PyObject[] { new PyString(file.Name) });
            }
            throw new PythonException(PyOSError.FromArguments(new PyObject[] { new PyInt(9), new PyString("Bad file descriptor") }, null));
        }

        // Host-shaped error text: Windows CPython uses "[WinError n] <localized>",
        // Godot builds keep the existing POSIX-style [Errno n] surface.
        internal static PythonException WindowsOr(int winCode, string path, string path2 = null)
        {
#if GODOT
            int errno = winCode is 2 or 3 ? 2 : winCode is 5 or 13 ? 13 : winCode == 183 ? 17 : 22;
            string posix = errno switch
            {
                2 => "No such file or directory",
                13 => "Permission denied",
                17 => "File exists",
                _ => "Invalid argument",
            };
            return PyOSError.Create(path2 == null
                ? $"[Errno {errno}] {posix}: '{path}'"
                : $"[Errno {errno}] {posix}: '{path}' -> '{path2}'");
#else
            return PyOSError.CreateWindowsFileError(winCode, path, path2);
#endif
        }

        // rename(src, dst)
        // CPython: posixmodule.c os_rename_impl
        private static PyObject Rename(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"rename() takes exactly 2 arguments ({args.Length} given)");

            string src = PathOf(args[0]);
            string dst = PathOf(args[1]);
            int attempt = 0;
            while (true)
            try
            {
                if (IOHelper.FileExists(src) || IOHelper.DirExists(src))
                    IOHelper.Move(src, dst);
                else
                    throw new FileNotFoundException();
                return PyNone.Instance;
            }
            catch (Exception ex) when (RetryAfterRelease(ex, ref attempt)) { }
            catch (FileNotFoundException)
            {
                throw WindowsOr(2, src, dst);
            }
            catch (DirectoryNotFoundException)
            {
                throw WindowsOr(3, src, dst);
            }
            catch (UnauthorizedAccessException)
            {
                throw WindowsOr(5, src, dst);
            }
            catch (IOException)
            {
                // Covers destination-already-exists (WinError 183) and cross-device cases.
                throw WindowsOr(183, src, dst);
            }
        }

        // stat(path) -> stat_result
        // CPython: posixmodule.c os_stat_impl
        private static PyObject Stat(PyObject[] args)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("stat() missing required argument: 'path' (pos 1)");

            string path = PathOf(args[0]);
            try
            {
                FileSystemInfo info;
#if GODOT
                if (path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal))
                {
                    if (IOHelper.FileExists(path))
                    {
                        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
                        if (file == null) throw new IOException($"Cannot read '{path}'");
                        return new PyStatResult(0x8000 | 0x1A4, (long)file.GetLength(), Godot.FileAccess.GetModifiedTime(path));
                    }
                    if (IOHelper.DirExists(path)) return new PyStatResult(0x4000 | 0x1ED, 0, 0);
                    throw new FileNotFoundException();
                }
#endif
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
                throw WindowsOr(2, path);
            }
            catch (Exception ex)
            {
                throw Failed(ex, path);
            }
        }

        // access(path, mode) -> bool
        // CPython: posixmodule.c os_access_impl
        private static PyObject Access(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"access() takes exactly 2 arguments ({args.Length} given)");

            string path = PathOf(args[0]);
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

            return new PyString(value);
        }

        // putenv(key, value)
        // CPython: posixmodule.c os_putenv_impl
        private static PyObject Unsetenv(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"unsetenv() takes exactly one argument ({args.Length} given)");
            string key = args[0] is PyString ps ? ps.Value : args[0].ToClrString();
            System.Environment.SetEnvironmentVariable(key, null);
            return new PyInt(0);
        }

        private static PyObject Getpid(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"getpid() takes no arguments ({args.Length} given)");
            return new PyInt(System.Diagnostics.Process.GetCurrentProcess().Id);
        }

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

            // PyOS_FSPath
            var path = args[0];
            if (path is PyString || path is PyBytes)
                return path;
            var method = path.LookupSpecialMethodUncached("__fspath__");
            if (method == null)
                throw PyTypeError.Create($"expected str, bytes or os.PathLike object, not {path.GetTypeName()}");
            var answer = path.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
            if (answer is PyString || answer is PyBytes)
                return answer;
            throw PyTypeError.Create($"expected {path.GetTypeName()}.__fspath__() to return str or bytes, not {answer.GetTypeName()}");
        }

        #endregion

        #region Helper Functions

        // convertenviron: str on Windows, bytes elsewhere.
        private static PyDict CreateEnvironDict()
        {
            var environ = new PyDict();
            bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                string name = (string)entry.Key, value = (string)entry.Value;
                environ.SetItem(windows ? new PyString(name) : new PyBytes(System.Text.Encoding.UTF8.GetBytes(name)),
                    windows ? new PyString(value) : new PyBytes(System.Text.Encoding.UTF8.GetBytes(value)));
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
        private readonly int _mode;
        private readonly long _size;
        private readonly double _mtime, _ctime, _atime;

        public PyStatResult(FileSystemInfo info)
        {
            _mode = info is DirectoryInfo ? 0x4000 | 0x1ED : 0x8000 | 0x1A4;
            _size = info is FileInfo file ? file.Length : 0;
            _mtime = ToUnixTime(info.LastWriteTime);
            _ctime = ToUnixTime(info.CreationTime);
            _atime = ToUnixTime(info.LastAccessTime);
        }

        // Godot virtual resources expose size and modification time, but no
        // creation/access timestamps. Keep stat results as value snapshots.
        internal PyStatResult(int mode, long size, double mtime)
        { _mode = mode; _size = size; _mtime = mtime; }

        internal static readonly PyType Type = new PyType("stat_result", new[] { PyType.ObjectType });

        public override PyType GetPyType() => Type;
        public override string GetTypeName() => "stat_result";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "st_mode":
                    // CPython: posixmodule.c - file mode bits
                    // S_IFDIR (0o40000) for directory, S_IFREG (0o100000) for regular file
                    return new PyInt(_mode);
                case "st_size":
                    return new PyInt(_size);
                case "st_mtime":
                    return new PyFloat(_mtime);
                case "st_ctime":
                    return new PyFloat(_ctime);
                case "st_atime":
                    return new PyFloat(_atime);
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

        public override PyString ToRepr()
        {
            return new PyString($"os.stat_result(st_mode={_mode}, st_ino=0, st_dev=0, st_nlink=1, st_uid=0, st_gid=0, st_size={_size}, st_atime={_atime}, st_mtime={_mtime}, st_ctime={_ctime})");
        }

        public override string ToString() => ToRepr().Value;
    }
}
