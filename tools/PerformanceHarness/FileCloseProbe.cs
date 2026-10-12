using System.Reflection;
using SharpPy;

internal static class FileCloseProbe
{
    internal static object Run()
    {
        string path = Path.GetTempFileName();
        var results = new List<object>();
        try
        {
            foreach (bool failFlush in new[] { false, true })
            {
                var file = new PyFileContextManager(path, "w");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var field = typeof(PyFileContextManager).GetField("stream", flags)!;
                using var original = (Stream)field.GetValue(file)!;
                var faulty = new FaultyStream(!failFlush);
                field.SetValue(file, faulty);
                if (failFlush)
                {
                    var writer = new StreamWriter(faulty, leaveOpen: true);
                    writer.Write("pending");
                    typeof(PyFileContextManager).GetField("writer", flags)!.SetValue(file, writer);
                }
                bool propagated = false;
                try { file.GetAttribute("close").Call(Array.Empty<PyObject>(), null); }
                catch (PythonException error) { propagated = error.PyException is PyOSError; }
                bool closed = file.GetAttribute("closed").PyBoolValue();
                if (!propagated || !closed || !faulty.Disposed) throw new InvalidOperationException("File cleanup failed");
                results.Add(new { operation = failFlush ? "flush" : "close", propagated, closed, disposed = faulty.Disposed });
            }
        }
        finally { File.Delete(path); }
        return results;
    }
    private sealed class FaultyStream(bool failClose) : MemoryStream
    {
        internal bool Disposed;
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("injected write failure");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("injected write failure");
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
            if (failClose) throw new IOException("injected close failure");
        }
    }
}
