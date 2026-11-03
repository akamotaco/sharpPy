using System;
using System.IO;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// File descriptors 0, 1 and 2. .NET reaches the console through Console.In, Console.Out and
    /// Console.Error, which a host replaces with Console.SetOut: Godot sends them to its output
    /// panel, the REPL captures them. So the bytes Python writes go through those writers, decoded
    /// as UTF-8 with the state kept between writes, and reading takes the text of Console.In.
    /// </summary>
    internal sealed class PyConsoleStream : Stream
    {
        private readonly int _descriptor;
        private readonly Decoder _decoder = new UTF8Encoding(false, false).GetDecoder();
        private byte[] _pending = Array.Empty<byte>();
        private int _pendingAt;

        internal PyConsoleStream(int descriptor) => _descriptor = descriptor;

        private static TextWriter WriterOf(int descriptor) => descriptor == 2 ? Console.Error : Console.Out;

        public override bool CanRead => _descriptor == 0;
        public override bool CanWrite => _descriptor != 0;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        internal bool IsTerminal => _descriptor switch
        {
            0 => !Console.IsInputRedirected,
            1 => !Console.IsOutputRedirected,
            _ => !Console.IsErrorRedirected,
        };

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count == 0)
                return;
            var chars = new char[_decoder.GetCharCount(buffer, offset, count, false)];
            int written = _decoder.GetChars(buffer, offset, count, chars, 0, false);
            WriterOf(_descriptor).Write(chars, 0, written);
        }

        public override void Flush() => WriterOf(_descriptor).Flush();

        // A line of Console.In at a time, as the bytes of its UTF-8.
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pendingAt >= _pending.Length)
            {
                string line = Console.In.ReadLine();
                if (line == null)
                    return 0;
                _pending = Encoding.UTF8.GetBytes(line + "\n");
                _pendingAt = 0;
            }
            int taken = Math.Min(count, _pending.Length - _pendingAt);
            Array.Copy(_pending, _pendingAt, buffer, offset, taken);
            _pendingAt += taken;
            return taken;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new IOException("Illegal seek", 29);
        public override void SetLength(long value) => throw new IOException("Invalid argument", 22);
    }
}
