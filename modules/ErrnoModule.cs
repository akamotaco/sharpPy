namespace SharpPy.Modules
{
    /// <summary>
    /// errno module - CPython 3.12: Modules/errnomodule.c. The names a script compares OSError.errno with; the
    /// values are the ones PyOSError maps to its subclasses.
    /// </summary>
    internal static class ErrnoModule
    {
        internal static PyModule CreateErrnoModule()
        {
            var module = new PyModule("errno");
            var errorcode = new PyDict();
            foreach (var (name, code) in new[]
            {
                ("EPERM", 1),
                ("ENOENT", 2),
                ("ESRCH", 3),
                ("EINTR", 4),
                ("EIO", 5),
                ("ENXIO", 6),
                ("E2BIG", 7),
                ("ENOEXEC", 8),
                ("EBADF", 9),
                ("ECHILD", 10),
                ("EAGAIN", 11),
                ("ENOMEM", 12),
                ("EACCES", 13),
                ("EFAULT", 14),
                ("EBUSY", 16),
                ("EEXIST", 17),
                ("EXDEV", 18),
                ("ENODEV", 19),
                ("ENOTDIR", 20),
                ("EISDIR", 21),
                ("EINVAL", 22),
                ("ENFILE", 23),
                ("EMFILE", 24),
                ("ENOTTY", 25),
                ("EFBIG", 27),
                ("ENOSPC", 28),
                ("ESPIPE", 29),
                ("EROFS", 30),
                ("EMLINK", 31),
                ("EPIPE", 32),
                ("EDOM", 33),
                ("ERANGE", 34),
                ("EDEADLK", 36),
                ("ENAMETOOLONG", 38),
                ("ENOLCK", 39),
                ("ENOSYS", 40),
                ("ENOTEMPTY", 41),
                ("EILSEQ", 42),
                ("ETIMEDOUT", 10060),
                ("ECONNREFUSED", 10061),
                ("ECONNABORTED", 10053),
                ("ECONNRESET", 10054),
                ("EINPROGRESS", 10036),
                ("EALREADY", 10037),
                ("EWOULDBLOCK", 10035),
            })
            {
                module.ModuleDict[name] = new PyInt(code);
                errorcode.SetItem(new PyInt(code), new PyString(name));
            }
            module.ModuleDict["errorcode"] = errorcode;
            return module;
        }
    }
}
