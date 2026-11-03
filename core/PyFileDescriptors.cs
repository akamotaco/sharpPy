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
            new PythonException(PyOSError.FromArguments(new PyObject[] { new PyInt(9), new PyString("Bad file descriptor") }, null));

        // _open_osfhandle: the lowest number that is free.
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
            var options = (flags & O_TEMPORARY) != 0 ? FileOptions.DeleteOnClose : FileOptions.None;
            var stream = new FileStream(path, mode, access, FileShare.ReadWrite | FileShare.Delete, 1, options);
            if ((flags & O_APPEND) != 0)
                stream.Seek(0, SeekOrigin.End);
            return Add(stream);
        }
    }

    /// <summary>What open(fd) reads and writes: the table's stream, closed with the file if closefd.</summary>
    internal sealed class PyDescriptorStream : Stream
    {
        private readonly Stream _inner;
        private readonly bool _closeDescriptor;
        private bool _released;

        internal int Descriptor { get; }

        internal PyDescriptorStream(int descriptor, bool closeDescriptor)
        {
            Descriptor = descriptor;
            _inner = PyFileDescriptors.Get(descriptor);
            _closeDescriptor = closeDescriptor;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (!_released && disposing)
            {
                _released = true;
                _inner.Flush();
                if (_closeDescriptor)
                    PyFileDescriptors.Close(Descriptor);
            }
            base.Dispose(disposing);
        }
    }
}
