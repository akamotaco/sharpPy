// TextIOWrapper over any binary buffer (Modules/_io/textio.c): what open() returns is PyFileContextManager,
// this is the class called directly, as tokenize.open does. It decodes the buffer once and reads from that.
using System.Collections.Generic;

namespace SharpPy
{
    internal sealed class PyTextWrapper : PyObject, IDictBackedInstance
    {
        private readonly PyObject _buffer;
        private readonly string _encoding;
        private readonly string _errors;
        private PyObject _reader;

        public Dictionary<string, PyObject> InstanceDict { get; } = new();

        public PyTextWrapper(PyObject buffer, string encoding, string errors)
        {
            _buffer = buffer;
            _encoding = encoding;
            _errors = errors;
        }

        public override PyType GetPyType() => PyFileContextManager.TextType;

        internal static void Install()
        {
            var type = PyFileContextManager.TextType;
            type.ConstructedByNewSlot = true;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                PyObject Option(int position, string name) => args.Length > position ? args[position]
                    : kwargs != null ? KwValue(kwargs, name) : null;
                var buffer = Option(0, "buffer") ?? throw PyTypeError.Create("TextIOWrapper() missing required argument 'buffer' (pos 1)");
                return new PyTextWrapper(buffer, (Option(1, "encoding") as PyString)?.Value ?? "utf-8", (Option(2, "errors") as PyString)?.Value ?? "strict");
            }, acceptsKwargs: true);
        }

        private PyObject Reader()
        {
            if (_reader == null)
            {
                var data = _buffer.GetAttribute("read").Call(new PyObject[0], null);
                var text = (PyString)data.GetAttribute("decode").Call(new PyObject[] { new PyString(_encoding), new PyString(_errors) }, null);
                _reader = PyStringIO.StringType.Call(new PyObject[] { new PyString(text.Value.Replace("\r\n", "\n").Replace('\r', '\n')) }, null);
            }
            return _reader;
        }

        public override void SetAttribute(string name, PyObject value) => InstanceDict[name] = value;

        public override PyObject GetIterator() => Reader().GetIterator();

        public override PyObject Next() => Reader().Next();

        public override PyObject GetAttribute(string name)
        {
            if (InstanceDict.TryGetValue(name, out var stored))
                return stored;
            switch (name)
            {
                case "__class__": return PyFileContextManager.TextType;
                case "__dict__": return base.GetAttribute(name);
                case "buffer": return _buffer;
                case "encoding": return new PyString(_encoding);
                case "errors": return new PyString(_errors);
                case "name": return _buffer.GetAttribute("name");
                case "closed": return _buffer.GetAttribute("closed");
                case "newlines": return PyNone.Instance;
                case "line_buffering":
                case "write_through":
                    return PyBool.False;
                case "close":
                case "flush":
                case "fileno":
                case "isatty":
                case "readable":
                case "writable":
                case "seekable":
                    return _buffer.GetAttribute(name);
                case "detach":
                    return new PyBuiltinFunction("detach", args => _buffer);
                case "write":
                    return new PyBuiltinFunction("write", args =>
                    {
                        var data = args[0].GetAttribute("encode").Call(new PyObject[] { new PyString(_encoding), new PyString(_errors) }, null);
                        _buffer.GetAttribute("write").Call(new[] { data }, null);
                        return new PyInt(args[0].Length());
                    });
                case "__enter__":
                    return new PyBuiltinFunction("__enter__", args => this);
                case "__exit__":
                    return new PyBuiltinFunction("__exit__", args =>
                    {
                        _buffer.GetAttribute("close").Call(new PyObject[0], null);
                        return PyNone.Instance;
                    });
                case "__iter__":
                    return new PyBuiltinFunction("__iter__", args => GetIterator());
                case "__next__":
                    return new PyBuiltinFunction("__next__", args => Next());
                default:
                    return Reader().GetAttribute(name);
            }
        }
    }
}
