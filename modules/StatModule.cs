using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_stat.c: the S_IF* constants and S_IS* tests of the platform, which Lib/stat.py takes
    /// over its own with "from _stat import *". The values are those of the Windows build: mode_t is
    /// an unsigned short there (PC/pyconfig.h), S_IFLNK, S_IFSOCK and the non-standard types are
    /// _stat.c's own fallbacks, and the FILE_ATTRIBUTE_* and IO_REPARSE_TAG_* names are added.
    /// </summary>
    internal static class StatModule
    {
        private const uint S_IFMT = 0xF000;
        private const uint S_IFDIR = 0x4000, S_IFCHR = 0x2000, S_IFBLK = 0x6000, S_IFREG = 0x8000;
        private const uint S_IFIFO = 0x1000, S_IFLNK = 0xA000, S_IFSOCK = 0xC000;
        private const uint S_ISUID = 0x800, S_ISGID = 0x400, S_ISVTX = 0x200;

        internal static PyModule Create()
        {
            var module = new PyModule("_stat");
            var d = module.ModuleDict;
            // stat_methods
            Test(d, "S_ISDIR", mode => (mode & S_IFMT) == S_IFDIR);
            Test(d, "S_ISCHR", mode => (mode & S_IFMT) == S_IFCHR);
            Test(d, "S_ISBLK", mode => (mode & S_IFMT) == S_IFBLK);
            Test(d, "S_ISREG", mode => (mode & S_IFMT) == S_IFREG);
            Test(d, "S_ISFIFO", mode => (mode & S_IFMT) == S_IFIFO);
            Test(d, "S_ISLNK", mode => (mode & S_IFMT) == S_IFLNK);
            Test(d, "S_ISSOCK", mode => (mode & S_IFMT) == S_IFSOCK);
            // S_ISDOOR, S_ISPORT and S_ISWHT: 0 where the platform has no such file type.
            Test(d, "S_ISDOOR", mode => false);
            Test(d, "S_ISPORT", mode => false);
            Test(d, "S_ISWHT", mode => false);
            d["S_IMODE"] = PyArgs.MethO("_stat", "S_IMODE", args => PyInt.From(AsMode(args[0]) & 0xFFF));
            d["S_IFMT"] = PyArgs.MethO("_stat", "S_IFMT", args => PyInt.From(AsMode(args[0]) & S_IFMT));
            d["filemode"] = PyArgs.MethO("_stat", "filemode", args => PyString.From(FileMode(AsMode(args[0]))));

            // stat_exec
            Add(d, "S_IFDIR", S_IFDIR); Add(d, "S_IFCHR", S_IFCHR); Add(d, "S_IFBLK", S_IFBLK);
            Add(d, "S_IFREG", S_IFREG); Add(d, "S_IFIFO", S_IFIFO); Add(d, "S_IFLNK", S_IFLNK);
            Add(d, "S_IFSOCK", S_IFSOCK); Add(d, "S_IFDOOR", 0); Add(d, "S_IFPORT", 0); Add(d, "S_IFWHT", 0);
            Add(d, "S_ISUID", S_ISUID); Add(d, "S_ISGID", S_ISGID); Add(d, "S_ISVTX", S_ISVTX);
            Add(d, "S_ENFMT", S_ISGID);
            Add(d, "S_IREAD", 0x100); Add(d, "S_IWRITE", 0x80); Add(d, "S_IEXEC", 0x40);
            Add(d, "S_IRWXU", 0x1C0); Add(d, "S_IRUSR", 0x100); Add(d, "S_IWUSR", 0x80); Add(d, "S_IXUSR", 0x40);
            Add(d, "S_IRWXG", 0x38); Add(d, "S_IRGRP", 0x20); Add(d, "S_IWGRP", 0x10); Add(d, "S_IXGRP", 0x8);
            Add(d, "S_IRWXO", 0x7); Add(d, "S_IROTH", 0x4); Add(d, "S_IWOTH", 0x2); Add(d, "S_IXOTH", 0x1);
            Add(d, "UF_NODUMP", 0x1); Add(d, "UF_IMMUTABLE", 0x2); Add(d, "UF_APPEND", 0x4);
            Add(d, "UF_OPAQUE", 0x8); Add(d, "UF_NOUNLINK", 0x10); Add(d, "UF_COMPRESSED", 0x20);
            Add(d, "UF_HIDDEN", 0x8000); Add(d, "SF_ARCHIVED", 0x10000); Add(d, "SF_IMMUTABLE", 0x20000);
            Add(d, "SF_APPEND", 0x40000); Add(d, "SF_NOUNLINK", 0x100000); Add(d, "SF_SNAPSHOT", 0x200000);
            string[] fields = { "ST_MODE", "ST_INO", "ST_DEV", "ST_NLINK", "ST_UID", "ST_GID", "ST_SIZE", "ST_ATIME", "ST_MTIME", "ST_CTIME" };
            for (uint i = 0; i < fields.Length; i++)
                Add(d, fields[i], i);
            // MS_WINDOWS: the file attributes and reparse tags of winnt.h.
            Add(d, "FILE_ATTRIBUTE_ARCHIVE", 0x20); Add(d, "FILE_ATTRIBUTE_COMPRESSED", 0x800);
            Add(d, "FILE_ATTRIBUTE_DEVICE", 0x40); Add(d, "FILE_ATTRIBUTE_DIRECTORY", 0x10);
            Add(d, "FILE_ATTRIBUTE_ENCRYPTED", 0x4000); Add(d, "FILE_ATTRIBUTE_HIDDEN", 0x2);
            Add(d, "FILE_ATTRIBUTE_INTEGRITY_STREAM", 0x8000); Add(d, "FILE_ATTRIBUTE_NORMAL", 0x80);
            Add(d, "FILE_ATTRIBUTE_NOT_CONTENT_INDEXED", 0x2000); Add(d, "FILE_ATTRIBUTE_NO_SCRUB_DATA", 0x20000);
            Add(d, "FILE_ATTRIBUTE_OFFLINE", 0x1000); Add(d, "FILE_ATTRIBUTE_READONLY", 0x1);
            Add(d, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400); Add(d, "FILE_ATTRIBUTE_SPARSE_FILE", 0x200);
            Add(d, "FILE_ATTRIBUTE_SYSTEM", 0x4); Add(d, "FILE_ATTRIBUTE_TEMPORARY", 0x100);
            Add(d, "FILE_ATTRIBUTE_VIRTUAL", 0x10000);
            Add(d, "IO_REPARSE_TAG_SYMLINK", 0xA000000C); Add(d, "IO_REPARSE_TAG_MOUNT_POINT", 0xA0000003);
            Add(d, "IO_REPARSE_TAG_APPEXECLINK", 0x8000001B);
            return module;
        }

        private static void Add(PyNamespace d, string name, uint value) => d[name] = PyInt.From(value);

        // stat_S_ISFUNC
        private static void Test(PyNamespace d, string name, Func<uint, bool> test) =>
            d[name] = PyArgs.MethO("_stat", name, args => PyBool.FromBool(test(AsMode(args[0]))));

        // _PyLong_AsMode_t: PyLong_AsUnsignedLong, then the range of mode_t (unsigned short).
        private static uint AsMode(PyObject value)
        {
            var mode = PyArgs.AsUnsignedLong(value);
            if (mode > ushort.MaxValue)
                throw PyOverflowError.Create("mode out of range");
            return (uint)mode;
        }

        // filetype and fileperm
        private static string FileMode(uint mode)
        {
            var buf = new char[10];
            buf[0] = (mode & S_IFMT) switch
            {
                S_IFREG => '-',
                S_IFDIR => 'd',
                S_IFLNK => 'l',
                S_IFBLK => 'b',
                S_IFCHR => 'c',
                S_IFIFO => 'p',
                S_IFSOCK => 's',
                _ => '?',
            };
            buf[1] = (mode & 0x100) != 0 ? 'r' : '-';
            buf[2] = (mode & 0x80) != 0 ? 'w' : '-';
            buf[3] = (mode & S_ISUID) != 0 ? ((mode & 0x40) != 0 ? 's' : 'S') : ((mode & 0x40) != 0 ? 'x' : '-');
            buf[4] = (mode & 0x20) != 0 ? 'r' : '-';
            buf[5] = (mode & 0x10) != 0 ? 'w' : '-';
            buf[6] = (mode & S_ISGID) != 0 ? ((mode & 0x8) != 0 ? 's' : 'S') : ((mode & 0x8) != 0 ? 'x' : '-');
            buf[7] = (mode & 0x4) != 0 ? 'r' : '-';
            buf[8] = (mode & 0x2) != 0 ? 'w' : '-';
            buf[9] = (mode & S_ISVTX) != 0 ? ((mode & 0x1) != 0 ? 't' : 'T') : ((mode & 0x1) != 0 ? 'x' : '-');
            return new string(buf);
        }
    }
}
