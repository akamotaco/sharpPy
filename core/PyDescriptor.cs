using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython PyMethodDef 호환: 메서드 descriptor
    /// C 확장 모듈의 메서드를 표현하는 descriptor
    /// </summary>
    public class PyMethodDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => Name;
        PyType IDescrObject.DescrType => OwnerType;

        // The methods of str work on the text: a subclass's own __len__ or __getitem__ is not its slot.
        internal static Func<PyObject, PyObject[], PyDict, PyObject> ReadingStoredText(
            PyType ownerType, string name, Func<PyObject, PyObject[], PyDict, PyObject> implementation) =>
            // unicode__format__ with an empty spec is str(self), and the right operand of
            // __rmod__ is formatted: both keep the subclass.
            !ReferenceEquals(ownerType, PyType.StrType) || name is "__format__" or "__rmod__" ? implementation
                : implementation;

        public string Name { get; }
        public PyType OwnerType { get; }
        internal readonly Func<PyObject, PyObject[], PyDict, PyObject> _implementation;
        private readonly int _minArgs;
        private readonly int _maxArgs;
        private readonly bool _acceptsKwargs;
        private PyArity.Convention _convention;
        private bool _conventionKnown;

        /// <summary>
        /// method_vectorcall_*: the checks of the method's calling convention, after descr_check
        /// and before the body. Without a known convention, the descriptor's own keyword rule.
        /// </summary>
        internal void CheckCall(PyObject[] args, PyDict kwargs, bool bound = false)
        {
            if (!_conventionKnown)
            {
                _convention = PyArity.Of(OwnerType, Name);
                _conventionKnown = true;
            }
            if (_convention != null)
                PyArity.Check(_convention, OwnerType, Name, args, kwargs, bound);
            else
                RejectUnwantedKeywords(kwargs);
        }

        public PyMethodDescriptor(
            string name,
            PyType ownerType,
            Func<PyObject, PyObject[], PyDict, PyObject> implementation,
            int minArgs = 0,
            int maxArgs = int.MaxValue,
            bool acceptsKwargs = false)
        {
            Name = name;
            OwnerType = ownerType;
            _implementation = ReadingStoredText(ownerType, name, implementation);
            _minArgs = minArgs;
            _maxArgs = maxArgs;
            _acceptsKwargs = acceptsKwargs;
        }

        public override PyType GetPyType() => PyType.MethodDescriptorType;
        public override string GetTypeName() => "method_descriptor";

        // CPython method_descriptor_repr: <method 'NAME' of 'TYPE' objects>
        protected internal override PyString Repr()
            => OwnerType != null
                ? PyString.From($"<method '{Name}' of '{OwnerType.FullName}' objects>")
                : PyString.From($"<method '{Name}'>");

        // Descriptor protocol
        public PyObject Get(PyObject instance, PyType owner)
        {
            // A type's __new__ is tp_new_methoddef, a builtin function already bound to the type
            // (add_tp_new_wrapper): it has no tp_descr_get, so an instance hands it back as is.
            if (instance == null || Name == "__new__")
                return this;

            // method_get: descr_check.
            if (!AppliesTo(OwnerType, instance))
                throw PyTypeError.Create($"descriptor '{Name}' for '{OwnerType.FullName}' objects doesn't apply to a '{instance.GetTypeName()}' object");

            // 인스턴스에서 접근 시 bound method 반환
            return new PyBoundMethodDescriptor(instance, this);
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"attribute '{Name}' of '{OwnerType.FullName}' objects is not writable");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"attribute '{Name}' of '{OwnerType.FullName}' objects is not writable");
        }

        public bool IsDataDescriptor() => false;

        // Method descriptor는 직접 호출 불가 (unbound)
        public override bool IsCallable() => true;

        /// <summary>
        /// CPython 3.12: only descriptors declared with METH_KEYWORDS accept keyword arguments.
        /// `int.bit_length` is METH_NOARGS and `str.upper` is METH_VARARGS, so CPython answers
        ///   (1).bit_length(x=1)   -> TypeError: int.bit_length() takes no keyword arguments
        ///   'a'.upper(x=1)        -> TypeError: str.upper() takes no keyword arguments
        /// _acceptsKwargs existed but was never read, so keywords were dropped and the call returned
        /// a value: a silent wrong answer rather than a wrong message.
        /// </summary>
        internal void RejectUnwantedKeywords(PyDict kwargs)
        {
            // `__new__` is excluded: CPython answers it from the type-call machinery, not from the
            // descriptor flags, and the label differs. Measured on CPython 3.12.15:
            //   class A: pass; A(x=1)                -> TypeError: A() takes no arguments
            //   object.__new__(A, x=1)               -> TypeError: A() takes no arguments
            //   tuple.__new__(tuple, iterable=[])    -> TypeError: tuple() takes no keyword arguments
            // The generic `{Type}.{name}()` form below is wrong for those, so `__new__` keeps its own
            // path and is tracked separately in docs/known_gaps.md.
            if (!_acceptsKwargs && Name != "__new__" && kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{OwnerType.FullName}.{Name}() takes no keyword arguments");
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            if (Name == "__new__")
                RejectUnwantedKeywords(kwargs);

            // Unbound method call: 첫 번째 인자가 self여야 함
            if (args.Length < 1)
            {
                throw PyTypeError.Create($"unbound method {OwnerType.FullName}.{Name}() needs an argument");
            }

            var self = args[0];
            if (Name != "__new__" && !AppliesTo(OwnerType, self))
                throw PyTypeError.Create($"descriptor '{Name}' for '{OwnerType.FullName}' objects doesn't apply to a '{self.GetTypeName()}' object");
            var actualArgs = new PyObject[args.Length - 1];
            Array.Copy(args, 1, actualArgs, 0, args.Length - 1);
            if (Name != "__new__")
                CheckCall(actualArgs, kwargs);

            return _implementation(self, actualArgs, kwargs);
        }

        // descr_check: PyObject_TypeCheck(obj, descr->d_type), the real type and no __class__.
        internal static bool AppliesTo(PyType owner, PyObject self)
        {
            if (owner == null || ReferenceEquals(owner, PyType.ObjectType))
                return true;
            if (self is PyType && (ReferenceEquals(owner, PyType.TypeType) || owner is PyTypeMetaclass))
                return true;
            var kind = self.GetPyType();
            return ReferenceEquals(kind, owner) || kind.MRO.Contains(owner);
        }

        public override string ToString() => $"<method '{Name}' of '{OwnerType.FullName}' objects>";
    }

    /// <summary>
    /// Bound method descriptor (method_descriptor가 인스턴스에 바인딩된 형태)
    /// </summary>
    /// <summary>
    /// CPython: __new__ is a static builtin surface (builtin_function_or_method),
    /// not a method_descriptor, even when implemented with descriptor plumbing.
    /// </summary>
    public class PyNewMethodDescriptor : PyMethodDescriptor, ICFunction
    {
        public PyNewMethodDescriptor(
            string name,
            PyType ownerType,
            Func<PyObject, PyObject[], PyDict, PyObject> implementation,
            int minArgs = 0,
            int maxArgs = int.MaxValue,
            bool acceptsKwargs = false)
            : base(name, ownerType, implementation, minArgs, maxArgs, acceptsKwargs)
        {
        }

        public override PyType GetPyType() => PyType.BuiltinFunctionOrMethodType;
        public override string GetTypeName() => "builtin_function_or_method";

        /// <summary>
        /// The C tp_new this __new__ stands for, which tp_new_wrapper compares. Itself, unless the
        /// type shares a base's tp_new in CPython and only allocates differently here.
        /// </summary>
        internal PyObject TpNew { get; init; }

        // tp_new_wrapper: the subtype is checked before tp_new.
        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            PyType.CheckNewArguments(OwnerType, args);
            return base.Call(args, kwargs);
        }

        // type->tp_new(subtype, args, kwds) from C: no tp_new_wrapper.
        internal PyObject CallTpNew(PyObject[] args, PyDict kwargs) => base.Call(args, kwargs);

        // tp_new_methoddef: a builtin method of the type, which copyreg compares with the type.
        string ICFunction.MethodName => Name;
        PyObject ICFunction.MethodSelf => OwnerType;
        PyObject ICFunction.MethodModule => null;

        protected internal override PyString Repr() => PyString.From($"<built-in method {Name} of type object at {PyObjectId.Pointer(OwnerType)}>");
        public override string ToString() => ToRepr().Value;
    }

    /// <summary>
    /// A PyCFunctionObject with m_self (Objects/methodobject.c): a method_descriptor bound to its
    /// instance by method_get, or a METH_CLASS function bound to a class by classmethod_get, which
    /// gets the class in front of its arguments. Equality, hash, repr and type are the same for both.
    /// </summary>
    public class PyBoundMethodDescriptor : PyObject, ICFunction
    {
        public PyObject Instance { get; }
        public PyMethodDescriptor MethodDescriptor { get; }

        // METH_CLASS: what classmethod_get bound, called with the class first.
        internal PyObject Callable { get; }

        public PyBoundMethodDescriptor(PyObject instance, PyMethodDescriptor methodDescriptor)
        {
            Instance = instance;
            MethodDescriptor = methodDescriptor;
        }

        internal PyBoundMethodDescriptor(PyType cls, PyObject callable)
        {
            Instance = cls;
            Callable = callable;
        }

        // m_ml: the function both kinds compare and hash by.
        private PyObject Function => (PyObject)MethodDescriptor ?? Callable;

        public override PyType GetPyType() => PyType.BuiltinFunctionOrMethodType;
        public override string GetTypeName() => "builtin_function_or_method";

        /// <summary>
        /// meth_richcompare (Objects/methodobject.c:292): equal when m_self is the same object and
        /// they wrap the same function (:306-:308); only == and != compare (:298-:303).
        /// </summary>
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if ((op != CompareOp.EQ && op != CompareOp.NE) || !(other is PyBoundMethodDescriptor bound))
                return PyNotImplemented.Instance;
            bool equal = ReferenceEquals(Instance, bound.Instance) && ReferenceEquals(Function, bound.Function);
            return op == CompareOp.EQ ? PyBool.FromBool(equal) : PyBool.FromBool(!equal);
        }

        /// <summary>meth_hash (Objects/methodobject.c:316): function part xored with m_self.</summary>
        public override int ToHash()
        {
            var self = PyObjectId.Of(Instance);
            return (int)(self ^ (self >> 32)) ^ (MethodDescriptor != null ? MethodDescriptor.ToHash() : (int)Callable.GetPythonHash());
        }

        public override bool IsCallable() => true;

        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            if (MethodDescriptor == null)
            {
                var withClass = new PyObject[args.Length + 1];
                withClass[0] = Instance;
                Array.Copy(args, 0, withClass, 1, args.Length);
                return Callable.Call(withClass, kwargs);
            }
            MethodDescriptor.CheckCall(args, kwargs, bound: true);
            return MethodDescriptor._implementation(Instance, args, kwargs);
        }

        string ICFunction.MethodName => MethodDescriptor?.Name
            ?? (Callable is ICFunction function ? function.MethodName : Callable.GetAttribute("__name__").AsString().Value);
        PyObject ICFunction.MethodSelf => Instance;
        PyObject ICFunction.MethodModule => null;

        // meth_repr: "<built-in method %s of %s object at %p>", the type of m_self and m_self.
        protected internal override PyString Repr() => PyString.From(
            $"<built-in method {((ICFunction)this).MethodName} of {Instance.GetPyType().FullName} object at {PyObjectId.Pointer(Instance)}>");
        public override string ToString() => ToRepr().Value;
    }

    /// <summary>
    /// CPython PyGetSetDef 호환: 속성 getter/setter descriptor
    /// </summary>
    public class PyGetSetDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => Name;
        PyType IDescrObject.DescrType => OwnerType;

        public string Name { get; }
        public PyType OwnerType { get; }
        private readonly Func<PyObject, PyObject> _getter;
        private readonly Action<PyObject, PyObject> _setter;
        private readonly Action<PyObject> _deleter;
        private readonly string _doc;

        public PyGetSetDescriptor(
            string name,
            PyType ownerType,
            Func<PyObject, PyObject> getter,
            Action<PyObject, PyObject> setter = null,
            string doc = null,
            Action<PyObject> deleter = null)
        {
            Name = name;
            OwnerType = ownerType;
            _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _setter = setter;
            _deleter = deleter;
            _doc = doc;
        }

        /// <summary>
        /// Whether this getset has a setting slot. CPython's generic setattr routes an assignment to
        /// a data descriptor; SharpPy registers some getsets without their setter, so the write path
        /// needs to tell the two apart rather than fail a name that used to land in the dict.
        /// </summary>
        internal bool HasSetter => _setter != null;

        public override PyType GetPyType() => PyType.GetSetDescriptorType;

        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null)
            {
                // 클래스에서 접근 시 descriptor 자체 반환
                return this;
            }

            // getter 호출
            return _getter(instance);
        }

        public void Set(PyObject instance, PyObject value)
        {
            if (_setter == null)
            {
                throw PyAttributeError.Create($"attribute '{Name}' of '{OwnerType.FullName}' objects is not writable");
            }

            _setter(instance, value);
        }

        // Deletion reaches the same slot as assignment (CPython calls descr_set with value=NULL), so
        // a getset without a setter reports the getset_set error, not a generic "can't delete":
        // Objects/descrobject.c:255-264 - AttributeError "attribute 'real' of 'int' objects is not
        // writable". That is what delattr(1, "real") shows, while delattr("s", "real") - where nothing
        // resolves - reports "has no attribute" instead.
        public void Delete(PyObject instance)
        {
            // A getset with a setter is deleted through it (descr_set with NULL); without one the
            // getset_set refusal is what the interpreter reports (Objects/descrobject.c:255-:264).
            if (_deleter != null)
            {
                _deleter(instance);
                return;
            }
            throw PyAttributeError.Create(
                $"attribute '{Name}' of '{OwnerType.FullName}' objects is not writable");
        }

        // Native getset descriptors have a setting slot even when it only
        // rejects writes. Read-only attributes still take data precedence.
        public bool IsDataDescriptor() => true;

        public override string ToString() => $"<attribute '{Name}' of '{OwnerType.FullName}' objects>";
        protected internal override PyString Repr() => PyString.From(ToString());
    }

    /// <summary>
    /// CPython PyMemberDef 호환: 구조체 멤버 descriptor
    /// C 구조체의 멤버를 직접 노출하는 descriptor
    /// </summary>
    public class PyMemberDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => Name;
        PyType IDescrObject.DescrType => OwnerType;

        public string Name { get; }
        public PyType OwnerType { get; }
        private readonly Func<PyObject, PyObject> _getter;
        private readonly Action<PyObject, PyObject> _setter;
        private readonly bool _readonly;
        private readonly Action<PyObject> _deleter;

        public PyMemberDescriptor(
            string name,
            PyType ownerType,
            Func<PyObject, PyObject> getter,
            Action<PyObject, PyObject> setter = null,
            bool readOnly = false,
            Action<PyObject> deleter = null)
        {
            Name = name;
            OwnerType = ownerType;
            _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _setter = setter;
            _readonly = readOnly || setter == null;
            _deleter = deleter;
        }

        public override PyType GetPyType() => PyType.MemberDescriptorType;

        public override string GetTypeName() => "member_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null)
            {
                // 클래스에서 접근 시 descriptor 자체 반환
                return this;
            }

            Check(instance);
            return _getter(instance);
        }

        public void Set(PyObject instance, PyObject value)
        {
            if (_readonly || _setter == null)
            {
                throw PyAttributeError.Create("readonly attribute");
            }

            Check(instance);
            _setter(instance, value);
        }

        /// <summary>
        /// A writable T_OBJECT member is deleted by member_set with NULL (Objects/descrobject.c), and
        /// PyMember_GetOne reads that NULL back as None - which is exactly what del e.code followed by
        /// e.code shows for SystemExit. A read-only member reports the same "is not writable" error a
        /// failed assignment gives.
        /// </summary>
        public void Delete(PyObject instance)
        {
            if (_readonly || _setter == null)
                throw PyAttributeError.Create("readonly attribute");
            Check(instance);
            if (_deleter != null) _deleter(instance);
            else _setter(instance, PyNone.Instance);
        }

        // descr_check and descr_setcheck
        private void Check(PyObject instance)
        {
            if (!PyMethodDescriptor.AppliesTo(OwnerType, instance))
                throw PyTypeError.Create($"descriptor '{Name}' for '{OwnerType.FullName}' objects doesn't apply to a '{instance.GetTypeName()}' object");
        }

        // A member has tp_descr_set whether or not it can be written.
        public bool IsDataDescriptor() => true;

        public override string ToString() => $"<member '{Name}' of '{OwnerType.FullName}' objects>";
        protected internal override PyString Repr() => PyString.From(ToString());
    }

    /// <summary>
    /// CPython wrapper_descriptor 호환: slot wrapper descriptor
    /// 타입의 특수 메서드 슬롯을 래핑하는 descriptor
    /// </summary>
    public class PyWrapperDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => Name;
        PyType IDescrObject.DescrType => OwnerType;

        public string Name { get; }
        public PyType OwnerType { get; }
        internal readonly Func<PyObject, PyObject[], PyDict, PyObject> _implementation;

        public PyWrapperDescriptor(
            string name,
            PyType ownerType,
            Func<PyObject, PyObject[], PyDict, PyObject> implementation)
        {
            Name = name;
            OwnerType = ownerType;
            _implementation = PyMethodDescriptor.ReadingStoredText(ownerType, name, implementation ?? throw new ArgumentNullException(nameof(implementation)));
        }

        // Registration-site compatibility: arg-count validation lives in the
        // implementation lambdas; these named arguments are accepted and ignored
        // so CPython wrapper_descriptor dunders can move classes without rewriting messages.
        public PyWrapperDescriptor(
            string name,
            PyType ownerType,
            Func<PyObject, PyObject[], PyDict, PyObject> implementation,
            int minArgs,
            int maxArgs,
            bool acceptsKwargs = false)
            : this(name, ownerType, implementation)
        {
        }

        public override PyType GetPyType() => PyType.WrapperDescriptorType;

        /// <summary>
        /// A wrapper of the type's slot (PySlotWrappers), whose slot is the CLR virtual itself: a
        /// dispatch from that virtual finding it is the type's own C slot, not a method to call.
        /// </summary>
        internal bool CallsVirtual { get; init; }

        public override string GetTypeName() => "wrapper_descriptor";

        // CPython wrapper_descriptor_repr: <slot wrapper 'NAME' of 'TYPE' objects>
        protected internal override PyString Repr()
            => OwnerType != null
                ? PyString.From($"<slot wrapper '{Name}' of '{OwnerType.FullName}' objects>")
                : PyString.From($"<slot wrapper '{Name}'>");

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null)
            {
                return this;
            }

            return new PyBoundWrapperDescriptor(instance, this);
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"attribute '{Name}' of '{OwnerType.FullName}' objects is not writable");
        }

        // A wrapper/method descriptor has no tp_descr_set at all, so CPython reports it one level up,
        // in _PyObject_GenericSetAttrWithDict (Objects/object.c:1592-1598): the name resolved but the
        // object has no instance dict to delete from -
        // AttributeError "'int' object attribute '__add__' is read-only".
        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create(
                $"'{OwnerType.FullName}' object attribute '{Name}' is read-only");
        }

        public bool IsDataDescriptor() => false;

        public override bool IsCallable() => true;

        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1)
            {
                throw PyTypeError.Create($"descriptor '{Name}' of '{OwnerType.FullName}' object needs an argument");
            }

            var self = args[0];
            if (Name != "__new__" && !PyMethodDescriptor.AppliesTo(OwnerType, self))
                throw PyTypeError.Create($"descriptor '{Name}' requires a '{OwnerType.FullName}' object but received a '{self.GetTypeName()}'");
            var actualArgs = new PyObject[args.Length - 1];
            Array.Copy(args, 1, actualArgs, 0, args.Length - 1);
            CheckSlotArguments(actualArgs, kwargs);

            return _implementation(self, actualArgs, kwargs);
        }

        public override string ToString() => $"<slot wrapper '{Name}' of '{OwnerType.FullName}' objects>";

        // slotdefs (Objects/typeobject.c): the wrap_* function of each slot, and with it the check of
        // its arguments, the same for every type. check_num_args says "expected N argument(s), got M";
        // PyArg_UnpackTuple(args, "", min, max) says the same with the empty name in front.
        private static readonly Dictionary<string, (int Min, int Max, bool Unpack)> SlotArity = BuildSlotArity();

        private static Dictionary<string, (int, int, bool)> BuildSlotArity()
        {
            var table = new Dictionary<string, (int, int, bool)>();
            foreach (var name in ("__repr__ __str__ __hash__ __iter__ __next__ __len__ __bool__ __neg__ __pos__ __abs__ " +
                                  "__invert__ __int__ __float__ __index__ __await__ __aiter__ __anext__ __del__").Split(' '))
                table[name] = (0, 0, false);
            foreach (var name in ("__lt__ __le__ __eq__ __ne__ __gt__ __ge__ __getattribute__ __delattr__ __getitem__ " +
                                  "__delitem__ __contains__ __delete__ " +
                                  "__add__ __radd__ __sub__ __rsub__ __mul__ __rmul__ __mod__ __rmod__ __divmod__ __rdivmod__ " +
                                  "__lshift__ __rlshift__ __rshift__ __rrshift__ __and__ __rand__ __xor__ __rxor__ __or__ __ror__ " +
                                  "__floordiv__ __rfloordiv__ __truediv__ __rtruediv__ __matmul__ __rmatmul__ " +
                                  "__iadd__ __isub__ __imul__ __imod__ __ilshift__ __irshift__ __iand__ __ixor__ __ior__ " +
                                  "__ifloordiv__ __itruediv__ __imatmul__").Split(' '))
                table[name] = (1, 1, false);
            foreach (var name in new[] { "__setitem__", "__setattr__", "__set__" })
                table[name] = (2, 2, true);
            foreach (var name in new[] { "__pow__", "__rpow__", "__ipow__", "__get__" })
                table[name] = (1, 2, true);
            foreach (var name in new[] { "__buffer__", "__release_buffer__" })
                table[name] = (1, 1, true);
            return table;
        }

        /// <summary>wrapperdescr_raw_call and the slot's wrap_* function: what is checked before the slot runs.</summary>
        internal void CheckSlotArguments(PyObject[] args, PyDict kwargs)
        {
            if (!SlotArity.TryGetValue(Name, out var arity))
                return;
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"wrapper {Name}() takes no keyword arguments");
            if (!arity.Unpack)
            {
                if (args.Length != arity.Min)
                    throw PyTypeError.Create($"expected {arity.Min} argument{(arity.Min == 1 ? "" : "s")}, got {args.Length}");
                return;
            }
            PyArgs.Positional("", args, null, arity.Min, arity.Max);
        }
    }

    /// <summary>
    /// Bound wrapper descriptor
    /// </summary>
    public class PyBoundWrapperDescriptor : PyObject, IWrapperObject
    {
        public PyObject Instance { get; }
        public PyWrapperDescriptor WrapperDescriptor { get; }

        public PyBoundWrapperDescriptor(PyObject instance, PyWrapperDescriptor wrapperDescriptor)
        {
            Instance = instance;
            WrapperDescriptor = wrapperDescriptor;
        }

        public override PyType GetPyType() => PyType.MethodWrapperType;
        public override string GetTypeName() => "method-wrapper";

        public override bool IsCallable() => true;

        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            WrapperDescriptor.CheckSlotArguments(args, kwargs);
            return WrapperDescriptor._implementation(Instance, args, kwargs);
        }

        IDescrObject IWrapperObject.WrapperDescr => WrapperDescriptor;
        PyObject IWrapperObject.WrapperSelf => Instance;

        // wrapper_richcompare: the same slot of the same object, == and != only.
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if ((op != CompareOp.EQ && op != CompareOp.NE) || other is not PyBoundWrapperDescriptor wrapper)
                return PyNotImplemented.Instance;
            bool same = ReferenceEquals(WrapperDescriptor, wrapper.WrapperDescriptor) && ReferenceEquals(Instance, wrapper.Instance);
            return PyBool.FromBool(same == (op == CompareOp.EQ));
        }

        // wrapper_hash: not based on the hash of __self__, which may have none.
        public override int ToHash() => HashCode.Combine(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Instance),
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(WrapperDescriptor));

        // CPython method-wrapper repr: <method-wrapper 'NAME' of TYPE object at 0xID>
        protected internal override PyString Repr()
            => PyString.From($"<method-wrapper '{WrapperDescriptor.Name}' of {Instance.GetTypeName()} object at {PyObjectId.Pointer(Instance)}>");
        public override string ToString() => ToRepr().Value;
    }
}
