using System.Collections.Generic;
namespace SharpPy
{
    #region Descriptor Protocol

// Descriptor 프로토콜 인터페이스
public interface IDescriptor
{
    PyObject Get(PyObject instance, PyType owner);
    void Set(PyObject instance, PyObject value);
    void Delete(PyObject instance);
    bool IsDataDescriptor(); // __set__ 또는 __delete__가 있으면 data descriptor
}

    /// <summary>
    /// property (Objects/descrobject.c): tp_new allocates, property_init fills, and getter, setter
    /// and deleter build a new object through type(self), so a subclass stays a subclass.
    /// </summary>
    public class PyProperty : PyObject, IDescriptor, IDictBackedInstance
    {
        private PyObject? _get;
        private PyObject? _set;
        private PyObject? _del;
        private PyObject? _doc;
        private PyObject? _name;
        private bool _getterDoc;
        private static bool _installed;

        public Dictionary<string, PyObject> InstanceDict { get; } = new();

        public PyProperty() => InitializePropertyDescriptors();

        // A property made by native code: PyDescr with C functions.
        public PyProperty(PyObject getter, PyObject? setter = null) : this()
        {
            _get = getter;
            _set = setter;
        }

        private static PyObject? OrNull(PyObject? value) => value is PyNone ? null : value;

        // property_init_impl
        private void Initialize(PyObject[] args, PyDict? kwargs)
        {
            if (args.Length > 4)
                throw PyTypeError.Create($"property expected at most 4 arguments, got {args.Length}");
            var names = new[] { "fget", "fset", "fdel", "doc" };
            var given = new PyObject?[4];
            Array.Copy(args, given, args.Length);
            if (kwargs != null)
                foreach (var entry in kwargs.GetInternalDict())
                {
                    int index = entry.Key is PyString key ? Array.IndexOf(names, key.Value) : -1;
                    if (index < 0)
                        throw PyTypeError.Create($"property() got an unexpected keyword argument {entry.Key.ToRepr().Value}");
                    if (index < args.Length)
                        throw PyTypeError.Create($"argument for property() given by name ('{names[index]}') and position ({index + 1})");
                    given[index] = entry.Value;
                }
            _get = OrNull(given[0]);
            _set = OrNull(given[1]);
            _del = OrNull(given[2]);
            _doc = null;
            _name = null;
            _getterDoc = false;
            PyObject? doc = OrNull(given[3]);
            if (doc == null && _get != null)
            {
                try
                {
                    doc = OrNull(_get.GetAttribute("__doc__"));
                }
                catch (PythonException error) when (error.PyException is PyAttributeError)
                {
                }
                _getterDoc = doc != null;
            }
            if (ReferenceEquals(GetPyType(), PyType.PropertyType))
            {
                _doc = doc;
                return;
            }
            // A subclass's own __doc__ would shadow the member: it goes in the instance.
            try
            {
                SetAttribute("__doc__", doc ?? PyNone.Instance);
            }
            catch (PythonException error) when (!_getterDoc && error.PyException is PyAttributeError)
            {
            }
        }

        // property_copy
        private PyObject Copy(PyObject? get, PyObject? set, PyObject? del)
        {
            get = OrNull(get) ?? _get ?? PyNone.Instance;
            set = OrNull(set) ?? _set ?? PyNone.Instance;
            del = OrNull(del) ?? _del ?? PyNone.Instance;
            PyObject doc = _getterDoc && get is not PyNone ? PyNone.Instance : _doc ?? PyNone.Instance;
            var created = GetPyType().Call(new[] { get, set, del, doc }, null);
            if (created is PyProperty copy)
                copy._name = _name;
            return created;
        }

        private static PyProperty Self(PyObject self, string name) => self as PyProperty ?? throw PyTypeError.Create(
            $"descriptor '{name}' for 'property' objects doesn't apply to a '{self.GetTypeName()}' object");

        private static void Member(string name, Func<PyProperty, PyObject?> read) =>
            PyType.PropertyType.TypeDict[name] = new PyMemberDescriptor(name, PyType.PropertyType,
                self => read(Self(self, name)) ?? PyNone.Instance, readOnly: true);

        private static void Copier(string name, Func<PyProperty, PyObject, PyObject> copy) =>
            PyType.PropertyType.TypeDict[name] = new PyMethodDescriptor(name, PyType.PropertyType,
                (self, args, kwargs) => copy(Self(self, name), args[0]), minArgs: 1, maxArgs: 1);

        /// <summary>The slots of the type, reachable by name.</summary>
        internal static void InitializePropertyDescriptors()
        {
            if (_installed) return;
            _installed = true;
            var type = PyType.PropertyType;
            type.ConstructedByNewSlot = true;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var created = new PyProperty();
                if (!ReferenceEquals(cls, PyType.PropertyType))
                    created.SetCustomType((PyType)cls);
                return created;
            }, 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) =>
            {
                Self(self, "__init__").Initialize(args, kwargs);
                return PyNone.Instance;
            }, 0, int.MaxValue);
            type.TypeDict["__set_name__"] = new PyMethodDescriptor("__set_name__", type, (self, args, kwargs) =>
            {
                if (args.Length != 2)
                    throw PyTypeError.Create($"__set_name__() takes 2 positional arguments but {args.Length} were given");
                Self(self, "__set_name__")._name = args[1];
                return PyNone.Instance;
            }, minArgs: 0, maxArgs: int.MaxValue);
            Copier("getter", (self, function) => self.Copy(function, null, null));
            Copier("setter", (self, function) => self.Copy(null, function, null));
            Copier("deleter", (self, function) => self.Copy(null, null, function));
            Member("fget", self => self._get);
            Member("fset", self => self._set);
            Member("fdel", self => self._del);
            type.TypeDict["__doc__"] = new PyMemberDescriptor("__doc__", type,
                self => Self(self, "__doc__")._doc ?? PyNone.Instance,
                (self, value) => Self(self, "__doc__")._doc = value);
            type.TypeDict["__isabstractmethod__"] = new PyGetSetDescriptor("__isabstractmethod__", type, self =>
            {
                foreach (var part in new[] { "fget", "fset", "fdel" })
                {
                    var function = self.GetAttribute(part);
                    if (function is PyNone) continue;
                    try
                    {
                        if (function.GetAttribute("__isabstractmethod__").IsTrue()) return PyBool.True;
                    }
                    catch (PythonException e) when (e.PyException is PyAttributeError) { }
                }
                return PyBool.False;
            });
            type.TypeDict["__get__"] = new PyWrapperDescriptor("__get__", type, (self, args, kwargs) =>
            {
                var target = args[0] is PyNone ? null : args[0];
                if (target == null && (args.Length < 2 || args[1] is PyNone))
                    throw PyTypeError.Create("__get__(None, None) is invalid");
                return Self(self, "__get__").DescriptorGet(target);
            }, 1, 2);
            type.TypeDict["__set__"] = new PyWrapperDescriptor("__set__", type, (self, args, kwargs) =>
            {
                Self(self, "__set__").DescriptorSet(args[0], args[1]);
                return PyNone.Instance;
            }, 2, 2);
            type.TypeDict["__delete__"] = new PyWrapperDescriptor("__delete__", type, (self, args, kwargs) =>
            {
                Self(self, "__delete__").DescriptorSet(args[0], null);
                return PyNone.Instance;
            }, 1, 1);
        }

        private static string? QualifiedName(PyObject? instance) =>
            (instance?.GetPyType().GetAttribute("__qualname__") as PyString)?.Value;

        // The three messages of property_descr_get and property_descr_set.
        private Exception Missing(PyObject? instance, string what)
        {
            var qualified = QualifiedName(instance);
            var owner = new PyString(qualified ?? "").ToRepr().Value;
            if (_name != null && qualified != null)
                return PyAttributeError.Create($"property {_name.ToRepr().Value} of {owner} object has no {what}");
            return PyAttributeError.Create(qualified != null ? $"property of {owner} object has no {what}" : $"property has no {what}");
        }

        // property_descr_get
        private PyObject DescriptorGet(PyObject? instance)
        {
            if (instance == null || instance is PyNone)
                return this;
            if (_get == null)
                throw Missing(instance, "getter");
            return _get.Call(new[] { instance }, null);
        }

        // property_descr_set
        private void DescriptorSet(PyObject instance, PyObject? value)
        {
            var function = value == null ? _del : _set;
            if (function == null)
                throw Missing(instance, value == null ? "deleter" : "setter");
            function.Call(value == null ? new[] { instance } : new[] { instance, value }, null);
        }

        // A subclass that declares a slot's method replaces the slot.
        private PyObject? Declared(string name) => _customType != null ? LookupUserSpecial(name) : null;

        public PyObject Get(PyObject instance, PyType owner)
        {
            var declared = Declared("__get__");
            return declared != null
                ? BindSpecialMethod(declared).Call(new[] { instance ?? PyNone.Instance, owner }, null)
                : DescriptorGet(instance);
        }

        public void Set(PyObject instance, PyObject value)
        {
            var declared = Declared("__set__");
            if (declared != null) BindSpecialMethod(declared).Call(new[] { instance, value }, null);
            else DescriptorSet(instance, value);
        }

        public void Delete(PyObject instance)
        {
            var declared = Declared("__delete__");
            if (declared != null) BindSpecialMethod(declared).Call(new[] { instance }, null);
            else DescriptorSet(instance, null);
        }

        // tp_descr_set exists whether or not a setter was given.
        public bool IsDataDescriptor() => true;

        public override PyObject LookupAttribute(string name) =>
            name == "__set_name__" ? GetAttribute(name) : base.LookupAttribute(name);


        public override string GetTypeName() => GetPyType().Name;
        public override PyType GetPyType() => _customType ?? PyType.PropertyType;
        public override string ToString() => "<property object>";
    }

    #endregion
}