using System;
using System.Collections.Generic;
using System.IO;
#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy.Modules
{
    /// <summary>os.DirEntry: DirEntry_Type of Modules/posixmodule.c.</summary>
    internal sealed class PyDirEntry : PyObject
    {
        internal static readonly PyType Type = MakeType();

        private readonly string _name;
        private readonly string _path;

        internal PyDirEntry(string name, string path)
        {
            _name = name;
            _path = path;
        }

        private static PyType MakeType()
        {
            var type = new PyType("DirEntry", new[] { PyType.ObjectType }, "nt");
            void Method(string name, Func<PyDirEntry, PyObject> body) =>
                type.TypeDict[name] = new PyMethodDescriptor(name, type,
                    (self, args, kwargs) => body((PyDirEntry)self), 0, 0, acceptsKwargs: true);
            Method("is_dir", self => PyBool.FromBool(IOHelper.DirExists(self._path)));
            Method("is_file", self => PyBool.FromBool(IOHelper.FileExists(self._path)));
            Method("is_symlink", self => PyBool.FromBool(self.IsSymlink()));
            Method("is_junction", self => PyBool.False);
            Method("stat", self => NtModule.StatPath(self._path));
            Method("inode", self => new PyInt(0));
            Method("__fspath__", self => PyString.From(self._path));
            type.TypeDict["name"] = new PyMemberDescriptor("name", type, self => PyString.From(((PyDirEntry)self)._name), readOnly: true);
            type.TypeDict["path"] = new PyMemberDescriptor("path", type, self => PyString.From(((PyDirEntry)self)._path), readOnly: true);
            return type;
        }

        private bool IsSymlink()
        {
#if GODOT
            return false;
#else
            return new FileInfo(_path).LinkTarget != null;
#endif
        }

        public override PyType GetPyType() => Type;
        public override string GetTypeName() => "DirEntry";
        protected internal override PyString Repr() => PyString.From($"<DirEntry {PyString.From(_name).ToRepr().Value}>");
    }

    /// <summary>What os.scandir returns: an iterator and a context manager.</summary>
    internal sealed class PyScandirIterator : PyObject
    {
        private static readonly PyType Type = MakeType();

        private readonly IEnumerator<PyDirEntry> _entries;
        private bool _closed;

        internal PyScandirIterator(IEnumerable<PyDirEntry> entries) => _entries = entries.GetEnumerator();

        private static PyType MakeType()
        {
            var type = new PyType("ScandirIterator", new[] { PyType.ObjectType }, "nt");
            type.TypeDict["__enter__"] = new PyMethodDescriptor("__enter__", type, (self, args, kwargs) => self, 0, 0);
            type.TypeDict["__exit__"] = new PyMethodDescriptor("__exit__", type, (self, args, kwargs) =>
            {
                ((PyScandirIterator)self)._closed = true;
                return PyNone.Instance;
            });
            type.TypeDict["close"] = new PyMethodDescriptor("close", type, (self, args, kwargs) =>
            {
                ((PyScandirIterator)self)._closed = true;
                return PyNone.Instance;
            }, 0, 0);
            type.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", type, (self, args, kwargs) => self);
            type.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", type, (self, args, kwargs) => self.IterNext());
            return type;
        }

        public override PyType GetPyType() => Type;
        public override string GetTypeName() => "ScandirIterator";
        protected internal override PyObject Iter() => this;

        protected internal override PyObject IterNext()
        {
            if (_closed || !_entries.MoveNext())
            {
                _closed = true;
                throw PyStopIteration.Create();
            }
            return _entries.Current;
        }
    }
}
