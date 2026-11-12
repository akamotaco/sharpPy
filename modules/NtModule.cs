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
            module.ModuleDict["F_OK"] = PyInt.From(0);  // File exists
            module.ModuleDict["R_OK"] = PyInt.From(4);  // Read permission
            module.ModuleDict["W_OK"] = PyInt.From(2);  // Write permission
            module.ModuleDict["X_OK"] = PyInt.From(1);  // Execute permission

            // Core functions (CPython: posixmodule.c)
            // os.py reads which functions take dir_fd and the like from here.
            foreach (var (name, value) in new[]
            {
                ("O_RDONLY", 0), ("O_WRONLY", 1), ("O_RDWR", 2), ("O_APPEND", 8), ("O_RANDOM", 16),
                ("O_SEQUENTIAL", 32), ("O_TEMPORARY", 64), ("O_NOINHERIT", 128), ("O_CREAT", 256),
                ("O_TRUNC", 512), ("O_EXCL", 1024), ("O_SHORT_LIVED", 4096), ("O_TEXT", 16384),
                ("O_BINARY", 32768), ("TMP_MAX", 2147483647),
            })
                module.ModuleDict[name] = PyInt.From(value);
            module.ModuleDict["_have_functions"] = new PyList(new List<PyObject>
            {
                PyString.From("HAVE_FTRUNCATE"), PyString.From("HAVE_LSTAT"), PyString.From("MS_WINDOWS"),
            });
            module.ModuleDict["stat_result"] = PyStatResult.Type;
            // PyStructSequence_NewType (posixmodule.c), as stat_result and times_result
            var terminalSize = new PyStructSequenceType("os", "terminal_size", new[] { "columns", "lines" }, heap: true);
            module.ModuleDict["terminal_size"] = terminalSize;
            module.ModuleDict["get_terminal_size"] = new PyBuiltinFunction("get_terminal_size", args =>
            {
                // os_get_terminal_size's clinic: fd=<unrepresentable> through the int converter.
                if (args.Length > 0)
                    PyArgs.Int(args[0]);
                if (Console.IsOutputRedirected)
                    throw PyOSError.Create("[Errno 25] Inappropriate ioctl for device");
                return new PyStructSequence(terminalSize, new PyObject[] { PyInt.From(Console.WindowWidth), PyInt.From(Console.WindowHeight) });
            });
            var timesResult = new PyStructSequenceType("nt", "times_result", new[] { "user", "system", "children_user", "children_system", "elapsed" }, heap: true);
            module.ModuleDict["times_result"] = timesResult;
            module.ModuleDict["times"] = new PyBuiltinFunction("times", args =>
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                return new PyStructSequence(timesResult, new PyObject[]
                {
                    new PyFloat(process.UserProcessorTime.TotalSeconds), new PyFloat(process.PrivilegedProcessorTime.TotalSeconds),
                    new PyFloat(0), new PyFloat(0), new PyFloat(0),
                });
            });
            module.ModuleDict["DirEntry"] = PyDirEntry.Type;
            module.ModuleDict["lstat"] = Clinic("lstat", new[] { "path" }, 1, 1, new[] { "dir_fd" }, (a, k) => Stat(new[] { a[0] }, "lstat"));
            module.ModuleDict["scandir"] = Clinic("scandir", new[] { "path" }, 0, 1, null, (a, k) => Scandir(a[0] == null ? Array.Empty<PyObject>() : new[] { a[0] }));
            module.ModuleDict["replace"] = Clinic("replace", new[] { "src", "dst" }, 2, 2, new[] { "src_dir_fd", "dst_dir_fd" }, (a, k) => Replace(new[] { a[0], a[1] }));
            module.ModuleDict["chmod"] = Clinic("chmod", new[] { "path", "mode" }, 2, 2, new[] { "dir_fd", "follow_symlinks" }, (a, k) => Chmod(new[] { a[0], a[1] }));
            module.ModuleDict["utime"] = Clinic("utime", new[] { "path", "times" }, 1, 2, new[] { "ns", "dir_fd", "follow_symlinks" }, UtimeClinic);
            module.ModuleDict["strerror"] = new PyBuiltinFunction("strerror", Strerror);
            module.ModuleDict["cpu_count"] = new PyBuiltinFunction("cpu_count", args => PyInt.From(Environment.ProcessorCount));
            module.ModuleDict["_exit"] = new PyBuiltinFunction("_exit", args =>
            {
                Environment.Exit(args[0].ToInt());
                return PyNone.Instance;
            });
            module.ModuleDict["waitstatus_to_exitcode"] = new PyBuiltinFunction("waitstatus_to_exitcode", args => PyInt.From(args[0].ToInt() >> 8));
            // No child process is ever started here.
            module.ModuleDict["waitpid"] = new PyBuiltinFunction("waitpid", args =>
                throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(10), PyString.From("No child processes") }, null)));
            module.ModuleDict["system"] = new PyBuiltinFunction("system", ShellCommand);
            module.ModuleDict["pipe"] = new PyBuiltinFunction("pipe", Pipe);
            module.ModuleDict["open"] = Clinic("open", new[] { "path", "flags", "mode" }, 2, 3, new[] { "dir_fd" }, (a, k) => Open(new[] { a[0], a[1] }, null));
            module.ModuleDict["close"] = new PyBuiltinFunction("close", Close);
            module.ModuleDict["read"] = new PyBuiltinFunction("read", Read);
            module.ModuleDict["write"] = new PyBuiltinFunction("write", Write);
            module.ModuleDict["lseek"] = new PyBuiltinFunction("lseek", Lseek);
            module.ModuleDict["fsync"] = new PyBuiltinFunction("fsync", Fsync);
            module.ModuleDict["ftruncate"] = new PyBuiltinFunction("ftruncate", Ftruncate);
            module.ModuleDict["isatty"] = new PyBuiltinFunction("isatty", Isatty);
            module.ModuleDict["fstat"] = new PyBuiltinFunction("fstat", Fstat);
            // Descriptors here are never inherited by a child and never non-blocking.
            module.ModuleDict["set_inheritable"] = new PyBuiltinFunction("set_inheritable", args =>
            {
                PyFileDescriptors.Get(Descriptor(args[0]));
                return PyNone.Instance;
            });
            module.ModuleDict["get_inheritable"] = new PyBuiltinFunction("get_inheritable", args =>
            {
                PyFileDescriptors.Get(Descriptor(args[0]));
                return PyBool.False;
            });
            module.ModuleDict["get_blocking"] = new PyBuiltinFunction("get_blocking", args =>
            {
                PyFileDescriptors.Get(Descriptor(args[0]));
                return PyBool.True;
            });
            module.ModuleDict["device_encoding"] = new PyBuiltinFunction("device_encoding", args =>
                PyFileDescriptors.Get(Descriptor(args[0])) is PyConsoleStream { IsTerminal: true } ? PyString.From("utf-8") : PyNone.Instance);
            module.ModuleDict["getcwd"] = new PyBuiltinFunction("getcwd", Getcwd);
            module.ModuleDict["chdir"] = Clinic("chdir", new[] { "path" }, 1, 1, null, (a, k) => Chdir(new[] { a[0] }));
            module.ModuleDict["listdir"] = Clinic("listdir", new[] { "path" }, 0, 1, null, (a, k) => Listdir(a[0] == null ? Array.Empty<PyObject>() : new[] { a[0] }));
            module.ModuleDict["mkdir"] = Clinic("mkdir", new[] { "path", "mode" }, 1, 2, new[] { "dir_fd" }, (a, k) => Mkdir(a[1] == null ? new[] { a[0] } : new[] { a[0], a[1] }));
            module.ModuleDict["rmdir"] = Clinic("rmdir", new[] { "path" }, 1, 1, new[] { "dir_fd" }, (a, k) => Rmdir(new[] { a[0] }));
            module.ModuleDict["remove"] = Clinic("remove", new[] { "path" }, 1, 1, new[] { "dir_fd" }, (a, k) => Remove(new[] { a[0] }, "remove"));
            module.ModuleDict["unlink"] = Clinic("unlink", new[] { "path" }, 1, 1, new[] { "dir_fd" }, (a, k) => Remove(new[] { a[0] }, "unlink"));
            module.ModuleDict["rename"] = Clinic("rename", new[] { "src", "dst" }, 2, 2, new[] { "src_dir_fd", "dst_dir_fd" }, (a, k) => Rename(new[] { a[0], a[1] }));
            module.ModuleDict["stat"] = Clinic("stat", new[] { "path" }, 1, 1, new[] { "dir_fd", "follow_symlinks" },
                (a, k) => k["follow_symlinks"] ? Stat(new[] { a[0] }) : Stat(new[] { a[0] }, "lstat"));
            module.ModuleDict["access"] = Clinic("access", new[] { "path", "mode" }, 2, 2, new[] { "dir_fd", "effective_ids", "follow_symlinks" }, (a, k) => Access(new[] { a[0], a[1] }));
            module.ModuleDict["getenv"] = new PyBuiltinFunction("getenv", Getenv);
            module.ModuleDict["putenv"] = PyArgs.FastCall("nt", "putenv", 2, 2, Putenv);
            module.ModuleDict["unsetenv"] = new PyBuiltinFunction("unsetenv", Unsetenv);
            module.ModuleDict["getpid"] = new PyBuiltinFunction("getpid", Getpid);
            module.ModuleDict["fspath"] = new PyBuiltinFunction("fspath", Fspath);
            module.ModuleDict["_path_splitroot"] = PyArgs.MethO("nt", "_path_splitroot", args => PathSplitRoot(args[0]));
            module.ModuleDict["urandom"] = PyArgs.MethO("nt", "urandom", Urandom);

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
                return PyString.From(Directory.GetCurrentDirectory());
            }
            catch (Exception ex) when (IsOsFailure(ex))
            {
                throw Failed(ex, null);
            }
        }

        // chdir(path)
        // CPython: posixmodule.c os_chdir_impl
        private static PyObject Chdir(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"chdir() takes exactly 1 argument ({args.Length} given)");

            string path = PathOf(args[0], "chdir");
            try
            {
                Directory.SetCurrentDirectory(path);
                return PyNone.Instance;
            }
            catch (DirectoryNotFoundException)
            {
                throw WindowsOr(3, path);
            }
            catch (Exception ex) when (IsOsFailure(ex))
            {
                throw Failed(ex, path);
            }
        }

        // listdir(path='.') -> list[str]
        // CPython: posixmodule.c os_listdir_impl
        private static PyObject Listdir(PyObject[] args)
        {
            string path = args.Length > 0 && args[0] is not PyNone ? PathOf(args[0], "listdir", nullable: true) : ".";

            try
            {
                if (IOHelper.FileExists(path))
                    throw WindowsOr(267, path);
                var entries = IOHelper.GetFileSystemEntries(path);
                var result = new List<PyObject>();
                foreach (var entry in entries)
                {
                    result.Add(PyString.From(IOHelper.GetFileName(entry)));
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
            catch (Exception ex) when (IsOsFailure(ex))
            {
                throw Failed(ex, path);
            }
        }

        // mkdir(path, mode=0o777)
        // CPython: posixmodule.c os_mkdir_impl
        private static PyObject Mkdir(PyObject[] args)
        {
            if (args.Length < 1)
                throw PyArgs.MissingArgument("mkdir", "path", 1);

            string path = PathOf(args[0], "mkdir");
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
            catch (Exception ex) when (IsOsFailure(ex))
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

            string path = PathOf(args[0], "rmdir");
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
            catch (Exception ex) when (IsOsFailure(ex))
            {
                throw Failed(ex, path);
            }
        }

        // remove(path) / unlink(path)
        // CPython: posixmodule.c os_unlink_impl
        private static PyObject Remove(PyObject[] args) => Remove(args, "remove");

        private static PyObject Remove(PyObject[] args, string function)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"remove() takes exactly 1 argument ({args.Length} given)");

            string path = PathOf(args[0], function);
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
            return PyFinalizer.CollectAndDrain() + PyResourceBus.ReleaseUnreferenced(forceCollection: true) > 0;
        }

        // win32_error: what .NET threw, as the code GetLastError held.
        // What a failing system call looks like from the CLR: only this becomes OSError (posix_error);
        // a Python exception propagates, and a host fault is the VM's to convert.
        private static bool IsOsFailure(Exception error) =>
            error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                or System.Security.SecurityException;

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
            (int)(PyObject.IndexOrThrow(value).Value);

        // path_converter
        /// <summary>
        /// path_converter (Modules/posixmodule.c) for a path_t of function, argument, allow_fd and
        /// nullable: str, bytes or __fspath__, else "function: path should be ..., not T". A
        /// function that takes a descriptor reads it before calling this.
        /// </summary>
        private static string PathOf(PyObject value, string function = null, string argument = "path", bool allowFd = false, bool nullable = false)
        {
            string prefix = function == null ? "" : function + ": ";
            PyObject path = value;
            if (value is not PyString && value is not PyBytes)
            {
                var method = value.LookupSpecialMethodUncached("__fspath__");
                if (method == null)
                {
                    string allowed = allowFd && nullable ? "string, bytes, os.PathLike, integer or None"
                        : allowFd ? "string, bytes, os.PathLike or integer"
                        : nullable ? "string, bytes, os.PathLike or None" : "string, bytes or os.PathLike";
                    throw PyTypeError.Create($"{prefix}{argument} should be {allowed}, not {value.GetPyType().Name}");
                }
                path = value.BindSpecialMethod(method).Call(Array.Empty<PyObject>(), null);
                if (path is not PyString && path is not PyBytes)
                    throw PyTypeError.Create($"expected {value.GetPyType().Name}.__fspath__() to return str or bytes, not {path.GetPyType().Name}");
            }
            string text = path is PyBytes bytes ? System.Text.Encoding.UTF8.GetString(bytes.Value) : ((PyString)path).Value;
            if (text.Contains('\0'))
                throw PyValueError.Create($"{prefix}embedded null character in {argument}");
            return text;
        }

        internal static PyObject StatPath(string path) => Stat(new PyObject[] { PyString.From(path) });

        // os_scandir_impl
        private static PyObject Scandir(PyObject[] args)
        {
            string path = args.Length > 0 && args[0] is not PyNone ? PathOf(args[0], "scandir", nullable: true) : ".";
            try
            {
                if (IOHelper.FileExists(path))
                    throw WindowsOr(267, path);
                var entries = new List<PyDirEntry>();
                string separator = path.EndsWith("/", StringComparison.Ordinal) || path.EndsWith("\\", StringComparison.Ordinal) ? "" : Path.DirectorySeparatorChar.ToString();
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
            string source = PathOf(args[0], "replace", "src"), target = PathOf(args[1], "replace", "dst");
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
            string path = PathOf(args[0], "chmod");
            if (!IOHelper.FileExists(path) && !IOHelper.DirExists(path))
                throw WindowsOr(2, path);
            var attributes = File.GetAttributes(path);
            bool writable = (args[1].ToInt() & 0x80) != 0;
            File.SetAttributes(path, writable ? attributes & ~FileAttributes.ReadOnly : attributes | FileAttributes.ReadOnly);
            return PyNone.Instance;
        }

        /// <summary>
        /// The Argument Clinic of posixmodule.c for a path function on Windows: its positional-or-
        /// keyword parameters, then keyword-only ones. dir_fd (and src_dir_fd/dst_dir_fd) is
        /// unavailable unless None; follow_symlinks and effective_ids are bool, and a function
        /// without them refuses the value that would need them; the body gets the positional values.
        /// </summary>
        private static PyBuiltinFunction Clinic(string name, string[] positional, int minpos, int maxpos, string[] keywordOnly,
            Func<PyObject[], Dictionary<string, bool>, PyObject> body)
        {
            keywordOnly ??= Array.Empty<string>();
            var names = new string[positional.Length + keywordOnly.Length];
            positional.CopyTo(names, 0);
            keywordOnly.CopyTo(names, positional.Length);
            return new PyBuiltinFunction(name, (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack(name, args, kwargs, names, 0, minpos, maxpos);
                var flags = new Dictionary<string, bool> { ["follow_symlinks"] = true, ["effective_ids"] = false };
                var values = new PyObject[positional.Length];
                Array.Copy(parsed, values, positional.Length);
                bool srcOrDst = false;
                for (int i = 0; i < keywordOnly.Length; i++)
                {
                    var value = parsed[positional.Length + i];
                    if (value == null)
                        continue;
                    switch (keywordOnly[i])
                    {
                        case "dir_fd":
                            // dir_fd_unavailable: an int other than DEFAULT_DIR_FD is refused.
                            if (value is not PyNone)
                                throw PyNotImplementedError.Create("dir_fd unavailable on this platform");
                            break;
                        case "src_dir_fd":
                        case "dst_dir_fd":
                            srcOrDst |= value is not PyNone;
                            break;
                        case "follow_symlinks":
                            flags["follow_symlinks"] = value.IsTrue();
                            break;
                        case "effective_ids":
                            flags["effective_ids"] = value.IsTrue();
                            break;
                    }
                }
                if (srcOrDst)
                    throw PyNotImplementedError.Create($"{name}: src_dir_fd and dst_dir_fd unavailable on this platform");
                // follow_symlinks_specified: only stat answers follow_symlinks=False on Windows.
                if (!flags["follow_symlinks"] && name != "stat")
                    throw PyNotImplementedError.Create($"{name}: follow_symlinks unavailable on this platform");
                if (flags["effective_ids"])
                    throw PyNotImplementedError.Create($"{name}: effective_ids unavailable on this platform");
                if (name == "utime")
                {
                    // utime's ns, which the body reads as times in seconds.
                    var ns = parsed[2];
                    if (ns != null)
                    {
                        if (values[1] != null && values[1] is not PyNone)
                            throw PyValueError.Create("utime: you may specify either 'times' or 'ns' but not both");
                        if (ns is not PyTuple { Items.Length: 2 } pair)
                            throw PyTypeError.Create("utime: 'ns' must be a tuple of two ints");
                        values[1] = PyTuple.From(new PyObject[]
                        {
                            new PyFloat((double)PyObject.IndexOrThrow(pair.Items[0]).Value / 1e9),
                            new PyFloat((double)PyObject.IndexOrThrow(pair.Items[1]).Value / 1e9),
                        });
                    }
                }
                return body(values, flags);
            });
        }

        private static PyObject UtimeClinic(PyObject[] values, Dictionary<string, bool> flags) =>
            Utime(values[1] == null ? new[] { values[0] } : new[] { values[0], values[1] });

        private static PyObject Utime(PyObject[] args)
        {
            string path = PathOf(args[0], "utime");
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
        private static PyObject Strerror(PyObject[] args) => PyString.From(args[0].ToInt() switch
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
            return PyTuple.From(new PyObject[] { PyInt.From(PyFileDescriptors.Add(client)), PyInt.From(PyFileDescriptors.Add(server)) });
        }

        // os_open_impl
        private static PyObject Open(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 2)
                throw PyArgs.MissingArgument("open", (args.Length == 0 ? "path" : "flags"), args.Length + 1);
            string path = PathOf(args[0]);
            try
            {
                return PyInt.From(PyFileDescriptors.Open(path, Descriptor(args[1])));
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
                throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(22), PyString.From("Invalid argument") }, null));
            var buffer = new byte[length];
            var stream = PyFileDescriptors.Get(Descriptor(args[0]));
            int count = 0;
            PyGil.AllowThreads(() => count = stream.Read(buffer, 0, length));
            Array.Resize(ref buffer, count);
            return PyBytes.From(buffer);
        }

        // os_write_impl
        private static PyObject Write(PyObject[] args)
        {
            var stream = PyFileDescriptors.Get(Descriptor(args[0]));
            var data = BytesMethods.RequireBuffer(args[1]);
            PyGil.AllowThreads(() =>
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();
            });
            return PyInt.From(data.Length);
        }

        private static PyObject Lseek(PyObject[] args)
        {
            var origin = Descriptor(args[2]) switch { 0 => SeekOrigin.Begin, 1 => SeekOrigin.Current, _ => SeekOrigin.End };
            var stream = PyFileDescriptors.Get(Descriptor(args[0]));
            // lseek on a console or a pipe: ESPIPE
            if (!stream.CanSeek)
                throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(29), PyString.From("Illegal seek") }, null));
            return PyInt.From(stream.Seek((long)PyObject.TryGetIndex(args[1]).Value, origin));
        }

        private static PyObject Fsync(PyObject[] args)
        {
            PyFileDescriptors.Get(Descriptor(args[0])).Flush();
            return PyNone.Instance;
        }

        private static PyObject Ftruncate(PyObject[] args)
        {
            // ftruncate leaves the position where it was.
            var stream = PyFileDescriptors.Get(Descriptor(args[0]));
            long at = stream.CanSeek ? stream.Position : -1;
            stream.SetLength((long)PyObject.TryGetIndex(args[1]).Value);
            if (at >= 0)
                stream.Position = at;
            return PyNone.Instance;
        }

        private static PyObject Isatty(PyObject[] args)
        {
            // os_isatty's clinic: fd through the int converter, then isatty of the descriptor.
            PyArgs.Int(args[0]);
            try
            {
                return PyBool.FromBool(PyFileDescriptors.Get(Descriptor(args[0])) is PyConsoleStream { IsTerminal: true });
            }
            catch (PythonException)
            {
                return PyBool.False;
            }
        }

        private static PyObject Fstat(PyObject[] args)
        {
            var stream = PyFileDescriptors.Get(Descriptor(args[0]));
            if (stream is FileStream file)
            {
                file.Flush();
                return Stat(new PyObject[] { PyString.From(file.Name) });
            }
            // _Py_fstat_noraise: what is not a disk file has only its type, a character device
            // (the console) or a FIFO (a pipe); any other stream answers for its size.
            if (stream is PyConsoleStream)
                return new PyStatResult(0x2000, 0, 0);
            if (stream is System.IO.Pipes.PipeStream)
                return new PyStatResult(0x1000, 0, 0);
            if (stream.CanSeek)
                return new PyStatResult(0x8000 | 0x1A4, stream.Length, 0);
            throw new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(9), PyString.From("Bad file descriptor") }, null));
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

            string src = PathOf(args[0], "rename", "src");
            string dst = PathOf(args[1], "rename", "dst");
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
        // os_stat_impl and os_lstat_impl: stat's path_t allows a descriptor, lstat's does not.
        private static PyObject Stat(PyObject[] args) => Stat(args, "stat");

        private static PyObject Stat(PyObject[] args, string function)
        {
            if (function == "stat" && args[0] is not PyString && args[0] is not PyBytes && PyObject.TryGetIndex(args[0]) != null)
                return Fstat(args);
            string path = PathOf(args[0], function, allowFd: function == "stat");
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
            catch (Exception ex) when (IsOsFailure(ex))
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

            string path = PathOf(args[0], "access");
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
            // os_access_impl answers False for any failure of the system call, and only for that.
            catch (Exception ex) when (IsOsFailure(ex))
            {
                return PyBool.False;
            }
        }

        // getenv(key, default=None)
        // CPython: posixmodule.c os_getenv_impl
        private static PyObject Getenv(PyObject[] args)
        {
            if (args.Length < 1)
                throw PyArgs.MissingArgument("getenv", "key", 1);

            string key = args[0].ToStr().Value;
            string value = Environment.GetEnvironmentVariable(key);

            if (value == null)
                return args.Length > 1 ? args[1] : PyNone.Instance;

            return PyString.From(value);
        }

        // putenv(key, value)
        // CPython: posixmodule.c os_putenv_impl
        private static PyObject Unsetenv(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"unsetenv() takes exactly one argument ({args.Length} given)");
            string key = args[0] is PyString ps ? ps.Value : args[0].ToClrString();
            System.Environment.SetEnvironmentVariable(key, null);
            return PyInt.From(0);
        }

        private static PyObject Getpid(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"getpid() takes no arguments ({args.Length} given)");
            return PyInt.From(System.Diagnostics.Process.GetCurrentProcess().Id);
        }

        // os_system_impl: the command as the shell gets it. Where no shell can start, -1 as system() says.
        private static PyObject ShellCommand(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"system() takes exactly one argument ({args.Length} given)");
            string command = PathOf(args[0]);
            var start = new System.Diagnostics.ProcessStartInfo { UseShellExecute = false };
            if (OperatingSystem.IsWindows())
            {
                start.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                start.Arguments = "/c " + command;
            }
            else
            {
                start.FileName = "/bin/sh";
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add(command);
            }
            try
            {
                using var process = System.Diagnostics.Process.Start(start);
                PyGil.AllowThreads(() => process.WaitForExit());
                return PyInt.From(OperatingSystem.IsWindows() ? process.ExitCode : process.ExitCode << 8);
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            {
                return PyInt.From(-1);
            }
        }

        private static PyObject Putenv(PyObject[] args)
        {

            string key = args[0].ToStr().Value;
            string value = args[1].ToStr().Value;

            Environment.SetEnvironmentVariable(key, value);
            return PyNone.Instance;
        }

        internal static PyObject FsPathObject(PyObject path) => Fspath(new[] { path });

        // fspath(path) -> str
        /// <summary>
        /// os__path_splitroot_impl (Windows): PathCchSkipRoot over the path with / as \, the root and
        /// the rest cut from the path as given; a path with no root is ("", path).
        /// </summary>
        private static PyObject PathSplitRoot(PyObject value)
        {
            string path = PathOf(value, "_path_splitroot");
            string buffer = path.Replace('/', '\\');
            int end = SkipRoot(buffer);
            if (end < 0)
                return PyTuple.From(PyString.From(""), value);
            if (end == 0)
                return PyTuple.From(value, PyString.From(""));
            return PyTuple.From(PyString.From(path.Substring(0, end)), PyString.From(path.Substring(end)));
        }

        // PathCchSkipRoot: past a drive and its separator, a drive, a root separator, or a UNC server and share; -1 without a root.
        private static int SkipRoot(string path)
        {
            if (path.StartsWith("\\\\", StringComparison.Ordinal))
            {
                int server = path.IndexOf('\\', 2);
                if (server < 0)
                    return -1;
                int share = path.IndexOf('\\', server + 1);
                return share < 0 ? path.Length : share + 1;
            }
            if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
                return path.Length >= 3 && path[2] == '\\' ? 3 : 2;
            if (path.StartsWith('\\'))
                return 1;
            return -1;
        }

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
                environ.SetItem(windows ? PyString.From(name) : PyBytes.From(System.Text.Encoding.UTF8.GetBytes(name)),
                    windows ? PyString.From(value) : PyBytes.From(System.Text.Encoding.UTF8.GetBytes(value)));
            }
            return environ;
        }

        // urandom(n) -> bytes
        // CPython: posixmodule.c os_urandom_impl
        // Generate n random bytes using cryptographically strong random number generator
        private static PyObject Urandom(PyObject[] args)
        {

            int n = (int)args[0].ToInt();

            if (n < 0)
                throw PyValueError.Create("negative argument not allowed");

            if (n == 0)
                return PyBytes.From(new byte[0]);

            // Use .NET's cryptographically strong random number generator
            byte[] buffer = new byte[n];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(buffer);
            }

            return PyBytes.From(buffer);
        }

        #endregion
    }

    /// <summary>os.stat_result (Modules/posixmodule.c): ten items, three of them only by index.</summary>
    public sealed class PyStatResult : PyStructSequence
    {
        private static readonly string[] Extra = OperatingSystem.IsWindows()
            ? new[] { "st_file_attributes", "st_reparse_tag" } : new[] { "st_blksize", "st_blocks", "st_rdev" };

        internal static readonly PyStructSequenceType Type = new("os", "stat_result", Fields(), 10, heap: true);

        private static string[] Fields()
        {
            var fields = new List<string> { "st_mode", "st_ino", "st_dev", "st_nlink", "st_uid", "st_gid", "st_size", null, null, null,
                "st_atime", "st_mtime", "st_ctime", "st_atime_ns", "st_mtime_ns", "st_ctime_ns" };
            fields.AddRange(Extra);
            return fields.ToArray();
        }

        private static PyObject[] Values(int mode, long size, double atime, double mtime, double ctime)
        {
            PyObject Whole(double seconds) => PyInt.From((long)Math.Floor(seconds));
            PyObject Nanoseconds(double seconds) => PyInt.From((System.Numerics.BigInteger)(seconds * 1e9));
            var values = new List<PyObject>
            {
                PyInt.From(mode), PyInt.From(0), PyInt.From(0), PyInt.From(1), PyInt.From(0), PyInt.From(0), PyInt.From(size),
                Whole(atime), Whole(mtime), Whole(ctime), new PyFloat(atime), new PyFloat(mtime), new PyFloat(ctime),
                Nanoseconds(atime), Nanoseconds(mtime), Nanoseconds(ctime),
            };
            foreach (var name in Extra)
                values.Add(PyInt.From(0));
            return values.ToArray();
        }

        private PyStatResult(int mode, long size, double atime, double mtime, double ctime)
            : base(Type, Values(mode, size, atime, mtime, ctime)) { }

        public PyStatResult(FileSystemInfo info) : this(info is DirectoryInfo ? 0x4000 | 0x1ED : 0x8000 | 0x1A4,
            info is FileInfo file ? file.Length : 0, ToUnixTime(info.LastAccessTime), ToUnixTime(info.LastWriteTime), ToUnixTime(info.CreationTime)) { }

        // Godot virtual resources expose size and modification time, but no
        // creation/access timestamps. Keep stat results as value snapshots.
        internal PyStatResult(int mode, long size, double mtime) : this(mode, size, 0, mtime, 0) { }

        private static double ToUnixTime(DateTime dt)
        {
            return (dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
