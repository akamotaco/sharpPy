using System;
using System.Collections.Generic;
using System.IO;

namespace SharpPy
{
    /// <summary>
    /// The C runtime's table of file descriptors, which Modules/posixmodule.c reaches through open(),
    /// read() and write(). .NET has streams and no numbers, so the numbers live here.
    /// </summary>
    internal static class PyFileDescriptors
    {
        private const int O_WRONLY = 1, O_RDWR = 2, O_APPEND = 8, O_TEMPORARY = 64, O_CREAT = 256, O_TRUNC = 512, O_EXCL = 1024;

        private static readonly Dictionary<int, Stream> Table = new();

        private static PythonException BadDescriptor() =>
            new PythonException(PyOSError.FromArguments(new PyObject[] { PyInt.From(9), PyString.From("Bad file descriptor") }, null));

        // _open_osfhandle: the lowest number that is free.
        /// <summary>
        /// CPython closes a file the moment its last reference goes, so a path it reopens or removes
        /// is no longer held by an abandoned file object. Here such an object waits for the collector;
        /// when the path is still open for writing in this table (a writer is what can hold data the
        /// next open would miss), the collector finishes the ones Python can no longer reach
        /// (iobase_finalize flushes and closes them) before the path is used again. Removing and
        /// renaming a held file retry after the same pass (NtModule.RetryAfterRelease).
        /// </summary>
        internal static void ReleaseUnreachable(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception) { return; }
            // A device (os.devnull is \\.\nul) keeps no data for the next open to miss.
            if (full.StartsWith(@"\\.\", StringComparison.Ordinal))
                return;
            bool held = false;
            lock (Table)
                foreach (var stream in Table.Values)
                    if (stream is FileStream { CanWrite: true } file && string.Equals(file.Name, full, StringComparison.OrdinalIgnoreCase))
                    {
                        held = true;
                        break;
                    }
            if (held)
                PyFinalizer.CollectAndDrain();
        }

        internal static int Add(Stream stream)
        {
            lock (Table)
            {
                int fd = 3;
                while (Table.ContainsKey(fd)) fd++;
                Table[fd] = stream;
                return fd;
            }
        }

        internal static Stream Get(int fd)
        {
            lock (Table)
            {
                if (Table.TryGetValue(fd, out var stream)) return stream;
                if (fd is < 0 or > 2) throw BadDescriptor();
                return Table[fd] = new PyConsoleStream(fd);
            }
        }

        internal static void Close(int fd)
        {
            Stream stream;
            lock (Table)
            {
                if (!Table.Remove(fd, out stream))
                    throw BadDescriptor();
            }
            stream.Dispose();
        }

        internal static int Open(string path, int flags)
        {
            bool write = (flags & (O_WRONLY | O_RDWR)) != 0;
            FileMode mode;
            if ((flags & O_CREAT) != 0)
                mode = (flags & O_EXCL) != 0 ? FileMode.CreateNew : (flags & O_TRUNC) != 0 ? FileMode.Create : FileMode.OpenOrCreate;
            else
                mode = (flags & O_TRUNC) != 0 ? FileMode.Truncate : FileMode.Open;
            var access = (flags & O_RDWR) != 0 ? FileAccess.ReadWrite : write ? FileAccess.Write : FileAccess.Read;
#if GODOT
            // res:// and user:// are Godot's file system, packs included.
            if (Godot_IO.Helper.IsGodotPath(path))
            {
                bool exists = Godot.FileAccess.FileExists(path);
                if ((flags & O_CREAT) == 0 && !exists)
                    throw new FileNotFoundException(null, path);
                if ((flags & O_CREAT) != 0 && (flags & O_EXCL) != 0 && exists)
                    throw new IOException("File exists", unchecked((int)0x80070050));
                bool update = (flags & O_RDWR) != 0;
                char kind = !write ? 'r' : (flags & O_APPEND) != 0 ? 'a' : (flags & O_TRUNC) != 0 || !exists ? 'w' : 'r';
                var godot = new GodotFileStream(path, kind, update || (kind == 'r' && write));
                return Add(godot);
            }
#endif
            // _wopen shares reading and writing, not deletion; _O_TEMPORARY is the exception.
            bool temporary = (flags & O_TEMPORARY) != 0;
            var share = temporary ? FileShare.ReadWrite | FileShare.Delete : FileShare.ReadWrite;
            var stream = new FileStream(path, mode, access, share, 1, temporary ? FileOptions.DeleteOnClose : FileOptions.None);
            if ((flags & O_APPEND) != 0)
                stream.Seek(0, SeekOrigin.End);
            return Add(stream);
        }
    }

}
