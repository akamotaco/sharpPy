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

        // The writers the process started with. While they are still in place nothing has taken
        // the console over, so the bytes go to the standard handle as written, as an fd's do; a
        // host that set its own writer gets text.
        private static readonly TextWriter OriginalOut = Console.Out, OriginalError = Console.Error;
        private static readonly TextReader OriginalIn = Console.In;
        private static Stream _rawOut, _rawError, _rawIn;

        private static TextWriter WriterOf(int descriptor) => descriptor == 2 ? Console.Error : Console.Out;

        private Stream RawWriter()
        {
#if GODOT
            // The host owns the console there, writers included.
            return null;
#endif
            if (_descriptor == 2)
                return ReferenceEquals(Console.Error, OriginalError) ? _rawError ??= Console.OpenStandardError() : null;
            return ReferenceEquals(Console.Out, OriginalOut) ? _rawOut ??= Console.OpenStandardOutput() : null;
        }

#if GODOT
        private static Stream RawReader() => null;
#else
        private static Stream RawReader() => ReferenceEquals(Console.In, OriginalIn) ? _rawIn ??= Console.OpenStandardInput() : null;
#endif

        public override bool CanRead => _descriptor == 0;
        public override bool CanWrite => _descriptor != 0;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

#if GODOT
        // The output panel a host shows is read as it is written, as a terminal is.
        internal bool IsTerminal => true;
#else
        internal bool IsTerminal => _descriptor switch
        {
            0 => !Console.IsInputRedirected,
            1 => !Console.IsOutputRedirected,
            _ => !Console.IsErrorRedirected,
        };
#endif

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count == 0)
                return;
            var raw = RawWriter();
            if (raw != null)
            {
                // Whatever the CLR side wrote first goes out first.
                WriterOf(_descriptor).Flush();
                raw.Write(buffer, offset, count);
                raw.Flush();
                return;
            }
            var chars = new char[_decoder.GetCharCount(buffer, offset, count, false)];
            int written = _decoder.GetChars(buffer, offset, count, chars, 0, false);
            WriterOf(_descriptor).Write(chars, 0, written);
        }

        public override void Flush() => WriterOf(_descriptor).Flush();

        // A line of Console.In at a time, as the bytes of its UTF-8.
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pendingAt >= _pending.Length && RawReader() is { } raw)
                return raw.Read(buffer, offset, count);
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
