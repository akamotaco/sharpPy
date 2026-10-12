#if GODOT
using System;
using System.IO;

namespace SharpPy;

// Keep resource and user paths on Godot's virtual filesystem, including packs.
internal sealed class GodotFileStream : Stream
{
    private readonly Godot.FileAccess file;
    private readonly bool readable, writable;
    internal GodotFileStream(string path, char mode, bool update)
    {
        bool exists = Godot.FileAccess.FileExists(path);
        if (mode == 'r' && !exists) throw new FileNotFoundException(null, path);
        if (mode == 'x' && exists) throw new IOException("File exists", unchecked((int)0x80070050));
        readable = mode == 'r' || update;
        writable = mode != 'r' || update;
        var flags = mode == 'r' ? (update ? Godot.FileAccess.ModeFlags.ReadWrite : Godot.FileAccess.ModeFlags.Read)
            : mode == 'a' && exists ? Godot.FileAccess.ModeFlags.ReadWrite
            : update ? Godot.FileAccess.ModeFlags.WriteRead : Godot.FileAccess.ModeFlags.Write;
        file = Godot.FileAccess.Open(path, flags);
        if (file == null) throw new IOException("Godot file open failed: " + Godot.FileAccess.GetOpenError());
        if (mode == 'a') file.SeekEnd();
    }
    public override bool CanRead => readable;
    public override bool CanWrite => writable;
    public override bool CanSeek => true;
    public override long Length => checked((long)file.GetLength());
    public override long Position { get => checked((long)file.GetPosition()); set => Seek(value, SeekOrigin.Begin); }
    public override void Flush() { file.Flush(); CheckError(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var data = file.GetBuffer(count);
        CheckError();
        data.CopyTo(buffer, offset);
        return data.Length;
    }
    public override void Write(byte[] buffer, int offset, int count)
    {
        file.StoreBuffer(offset == 0 && count == buffer.Length ? buffer : buffer.AsSpan(offset, count).ToArray());
        CheckError();
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        long position = checked(offset + (origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? Position : Length));
        if (position < 0) throw new IOException("Invalid argument");
        file.Seek((ulong)position);
        return position;
    }
    public override void SetLength(long value)
    {
        if (value < 0) throw new IOException("Invalid argument");
        var error = file.Resize(value);
        if (error != Godot.Error.Ok) throw new IOException("Godot file resize failed: " + error);
    }
    private void CheckError()
    {
        var error = file.GetError();
        if (error != Godot.Error.Ok && error != Godot.Error.FileEof)
            throw new IOException("Godot file operation failed: " + error);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { file.Close(); file.Dispose(); }
        base.Dispose(disposing);
    }
}
#endif
