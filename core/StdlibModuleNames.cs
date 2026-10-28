namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 sys.stdlib_module_names (frozen data captured from the
    /// reference runtime CPython 3.12.15 used for parity testing).
    /// Used by sys.stdlib_module_names and by NameError display suggestions
    /// ("Did you forget to import ...").
    /// </summary>
    internal static class StdlibModuleNames
    {
        public static readonly string[] Names =
        {
            "__future__", "_abc", "_aix_support", "_ast", "_asyncio", "_bisect", "_blake2", "_bz2", "_codecs",
            "_codecs_cn", "_codecs_hk", "_codecs_iso2022", "_codecs_jp", "_codecs_kr", "_codecs_tw", "_collections",
            "_collections_abc", "_compat_pickle", "_compression", "_contextvars", "_crypt", "_csv", "_ctypes",
            "_curses", "_curses_panel", "_datetime", "_dbm", "_decimal", "_elementtree", "_frozen_importlib",
            "_frozen_importlib_external", "_functools", "_gdbm", "_hashlib", "_heapq", "_imp", "_io", "_json",
            "_locale", "_lsprof", "_lzma", "_markupbase", "_md5", "_msi", "_multibytecodec", "_multiprocessing",
            "_opcode", "_operator", "_osx_support", "_overlapped", "_pickle", "_posixshmem", "_posixsubprocess",
            "_py_abc", "_pydatetime", "_pydecimal", "_pyio", "_pylong", "_queue", "_random", "_scproxy", "_sha1",
            "_sha2", "_sha3", "_signal", "_sitebuiltins", "_socket", "_sqlite3", "_sre", "_ssl", "_stat",
            "_statistics", "_string", "_strptime", "_struct", "_symtable", "_thread", "_threading_local", "_tkinter",
            "_tokenize", "_tracemalloc", "_typing", "_uuid", "_warnings", "_weakref", "_weakrefset", "_winapi",
            "_wmi", "_zoneinfo", "abc", "aifc", "antigravity", "argparse", "array", "ast", "asyncio", "atexit",
            "audioop", "base64", "bdb", "binascii", "bisect", "builtins", "bz2", "cProfile", "calendar", "cgi",
            "cgitb", "chunk", "cmath", "cmd", "code", "codecs", "codeop", "collections", "colorsys", "compileall",
            "concurrent", "configparser", "contextlib", "contextvars", "copy", "copyreg", "crypt", "csv", "ctypes",
            "curses", "dataclasses", "datetime", "dbm", "decimal", "difflib", "dis", "doctest", "email", "encodings",
            "ensurepip", "enum", "errno", "faulthandler", "fcntl", "filecmp", "fileinput", "fnmatch", "fractions",
            "ftplib", "functools", "gc", "genericpath", "getopt", "getpass", "gettext", "glob", "graphlib", "grp",
            "gzip", "hashlib", "heapq", "hmac", "html", "http", "idlelib", "imaplib", "imghdr", "importlib",
            "inspect", "io", "ipaddress", "itertools", "json", "keyword", "lib2to3", "linecache", "locale",
            "logging", "lzma", "mailbox", "mailcap", "marshal", "math", "mimetypes", "mmap", "modulefinder",
            "msilib", "msvcrt", "multiprocessing", "netrc", "nis", "nntplib", "nt", "ntpath", "nturl2path",
            "numbers", "opcode", "operator", "optparse", "os", "ossaudiodev", "pathlib", "pdb", "pickle",
            "pickletools", "pipes", "pkgutil", "platform", "plistlib", "poplib", "posix", "posixpath", "pprint",
            "profile", "pstats", "pty", "pwd", "py_compile", "pyclbr", "pydoc", "pydoc_data", "pyexpat", "queue",
            "quopri", "random", "re", "readline", "reprlib", "resource", "rlcompleter", "runpy", "sched", "secrets",
            "select", "selectors", "shelve", "shlex", "shutil", "signal", "site", "smtplib", "sndhdr", "socket",
            "socketserver", "spwd", "sqlite3", "sre_compile", "sre_constants", "sre_parse", "ssl", "stat",
            "statistics", "string", "stringprep", "struct", "subprocess", "sunau", "symtable", "sys", "sysconfig",
            "syslog", "tabnanny", "tarfile", "telnetlib", "tempfile", "termios", "textwrap", "this", "threading",
            "time", "timeit", "tkinter", "token", "tokenize", "tomllib", "trace", "traceback", "tracemalloc", "tty",
            "turtle", "turtledemo", "types", "typing", "unicodedata", "unittest", "urllib", "uu", "uuid", "venv",
            "warnings", "wave", "weakref", "webbrowser", "winreg", "winsound", "wsgiref", "xdrlib", "xml", "xmlrpc",
            "zipapp", "zipfile", "zipimport", "zlib", "zoneinfo",
        };

        private static readonly System.Collections.Generic.HashSet<string> Lookup =
            new(System.StringComparer.Ordinal);

        private static PyFrozenSet? _frozen;

        static StdlibModuleNames()
        {
            foreach (var n in Names) Lookup.Add(n);
        }

        public static bool Contains(string name) => Lookup.Contains(name);

        public static PyFrozenSet FrozenSet()
        {
            if (_frozen == null)
            {
                var items = new PyObject[Names.Length];
                for (int i = 0; i < Names.Length; i++) items[i] = new PyString(Names[i]);
                _frozen = new PyFrozenSet(items);
            }
            return _frozen;
        }
    }
}
