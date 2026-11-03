using System;

namespace SharpPy
{
    /// <summary>
    /// CPython PyMethodDef 호환: 메서드 descriptor
    /// C 확장 모듈의 메서드를 표현하는 descriptor
    /// </summary>
    public class PyMethodDescriptor : PyObject, IDescriptor
    {
        // The methods of str work on the text: a subclass's own __len__ or __getitem__ is not its slot.
        internal static Func<PyObject, PyObject[], PyDict, PyObject> ReadingStoredText(
            PyType ownerType, string name, Func<PyObject, PyObject[], PyDict, PyObject> implementation) =>
            // unicode__format__ with an empty spec is str(self), and the right operand of
            // __rmod__ is formatted: both keep the subclass.
            !ReferenceEquals(ownerType, PyType.StrType) || name is "__format__" or "__rmod__" ? implementation
                : (self, args, kwargs) => implementation(self is PyStrSubclass text ? text.GetStrValue() : self, args, kwargs);

        public string Name { get; }
        public PyType OwnerType { get; }
        internal readonly Func<PyObject, PyObject[], PyDict, PyObject> _implementation;
        private readonly int _minArgs;
        private readonly int _maxArgs;
        private readonly bool _acceptsKwargs;

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
        public override PyString ToRepr()
            => OwnerType != null
                ? new PyString($"<method '{Name}' of '{OwnerType.FullName}' objects>")
                : new PyString($"<method '{Name}'>");

        // Descriptor protocol
        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null)
            {
                // 클래스에서 접근 시 descriptor 자체 반환
                return this;
            }

            // method_get: descr_check.
            if (Name != "__new__" && !AppliesTo(OwnerType, instance))
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

        // descr_members and descr_getset: what every descriptor of a type says about itself.
        internal static PyObject DescriptorAttribute(string name, PyType owner, string descriptorName) => name switch
        {
            "__name__" => new PyString(descriptorName),
            "__qualname__" => new PyString(owner != null ? $"{owner.Name}.{descriptorName}" : descriptorName),
            "__objclass__" => owner,
            _ => null,
        };

        public override PyObject GetAttribute(string name) =>
            DescriptorAttribute(name, OwnerType, Name) ?? base.GetAttribute(name);

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

        /// <summary>
        /// VM slot calls already know the receiver. Pass it separately from the
        /// read-only argument array, as the existing bound descriptor path does.
        /// CPython v3.12.15, Objects/abstract.c: PyObject_GetItem / PyObject_SetItem.
        /// </summary>
        internal PyObject CallWithReceiver(PyObject receiver, PyObject[] args, PyDict kwargs)
        {
            if (GetType() == typeof(PyMethodDescriptor))
            {
                RejectUnwantedKeywords(kwargs);
                return _implementation(receiver, args, kwargs);
            }

            // Preserve Call overrides on C# descriptor subclasses.
            var fullArgs = new PyObject[args.Length + 1];
            fullArgs[0] = receiver;
            Array.Copy(args, 0, fullArgs, 1, args.Length);
            return Call(fullArgs, kwargs);
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
    public class PyNewMethodDescriptor : PyMethodDescriptor
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
        // tp_new_methoddef: a builtin method of the type, which copyreg compares with the type.
        public override PyObject GetAttribute(string name) => name switch
        {
            "__self__" => OwnerType,
            "__module__" => PyNone.Instance,
            _ => base.GetAttribute(name),
        };

        public override PyString ToRepr() => new PyString($"<built-in method {Name} of type object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(OwnerType):x}>");
        public override string ToString() => ToRepr().Value;
    }

    public class PyBoundMethodDescriptor : PyObject
    {
        public PyObject Instance { get; }
        public PyMethodDescriptor MethodDescriptor { get; }

        public PyBoundMethodDescriptor(PyObject instance, PyMethodDescriptor methodDescriptor)
        {
            Instance = instance;
            MethodDescriptor = methodDescriptor;
        }

        public override PyType GetPyType() => PyType.BuiltinFunctionOrMethodType;
        public override string GetTypeName() => "builtin_function_or_method";

        internal string QualifiedName => $"{MethodDescriptor.OwnerType.FullName}.{MethodDescriptor.Name}";

        /// <summary>
        /// The same CPython object as PyBuiltinBoundMethod above, so the same slot: two bound
        /// builtins are equal when the receiver is the same object and they wrap the same function
        /// (Objects/methodobject.c:306-:308), and only == / != compare (:298-:303).
        /// </summary>
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            if ((op != CompareOp.EQ && op != CompareOp.NE) || !(other is PyBoundMethodDescriptor bound))
                return PyNotImplemented.Instance;
            bool equal = ReferenceEquals(Instance, bound.Instance) &&
                ReferenceEquals(MethodDescriptor, bound.MethodDescriptor);
            return op == CompareOp.EQ ? PyBool.FromBool(equal) : PyBool.FromBool(!equal);
        }

        /// <summary>meth_hash (Objects/methodobject.c:316): function part xored with the receiver.</summary>
        public override int ToHash()
        {
            var self = PyObjectId.Of(Instance);
            return (int)(self ^ (self >> 32)) ^ MethodDescriptor.ToHash();
        }

        public override bool IsCallable() => true;

        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
#if DEBUG_DESCRIPTORS_LOG
            Console.WriteLine($"🎯 PyBoundMethodDescriptor.Call: Method={MethodDescriptor.Name}, Instance={Instance?.GetType().Name}");
            Console.WriteLine($"   Instance details: {Instance}");
            Console.WriteLine($"   Instance GetTypeName: {Instance?.GetTypeName()}");
#endif
            // Bound method: self는 이미 바인딩되어 있음
            MethodDescriptor.RejectUnwantedKeywords(kwargs);
            var result = MethodDescriptor._implementation(Instance, args, kwargs);
#if DEBUG_DESCRIPTORS_LOG
            Console.WriteLine($"   Result: {result?.GetType().Name}");
#endif
            return result;
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "__self__":
                    return Instance;
                case "__func__":
                    return MethodDescriptor;
                case "__name__":
                    return new PyString(MethodDescriptor.Name);
                case "__qualname__":
                    return new PyString(QualifiedName);
                default:
                    return base.GetAttribute(name);
            }
        }

        // CPython builtin_method_repr for a bound method_descriptor:
        // <built-in method NAME of TYPE object at 0xID> (address is the bound self).
        public override PyString ToRepr()
            => new PyString($"<built-in method {MethodDescriptor.Name} of {Instance.GetTypeName()} object at 0x{Instance.GetHashCode():x}>");
        public override string ToString() => ToRepr().Value;
    }

    /// <summary>
    /// CPython PyGetSetDef 호환: 속성 getter/setter descriptor
    /// </summary>
    public class PyGetSetDescriptor : PyObject, IDescriptor
    {
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

        public override PyObject GetAttribute(string name) =>
            PyMethodDescriptor.DescriptorAttribute(name, OwnerType, Name) ?? base.GetAttribute(name);
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
        public override PyString ToRepr() => new PyString(ToString());
    }

    /// <summary>
    /// CPython PyMemberDef 호환: 구조체 멤버 descriptor
    /// C 구조체의 멤버를 직접 노출하는 descriptor
    /// </summary>
    public class PyMemberDescriptor : PyObject, IDescriptor
    {
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

        public override PyObject GetAttribute(string name) =>
            PyMethodDescriptor.DescriptorAttribute(name, OwnerType, Name) ?? base.GetAttribute(name);
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
        public override PyString ToRepr() => new PyString(ToString());
    }

    /// <summary>
    /// CPython wrapper_descriptor 호환: slot wrapper descriptor
    /// 타입의 특수 메서드 슬롯을 래핑하는 descriptor
    /// </summary>
    public class PyWrapperDescriptor : PyObject, IDescriptor
    {
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

        public override PyObject GetAttribute(string name) =>
            PyMethodDescriptor.DescriptorAttribute(name, OwnerType, Name) ?? base.GetAttribute(name);
        public override string GetTypeName() => "wrapper_descriptor";

        // CPython wrapper_descriptor_repr: <slot wrapper 'NAME' of 'TYPE' objects>
        public override PyString ToRepr()
            => OwnerType != null
                ? new PyString($"<slot wrapper '{Name}' of '{OwnerType.FullName}' objects>")
                : new PyString($"<slot wrapper '{Name}'>");

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

            return _implementation(self, actualArgs, kwargs);
        }

        public override string ToString() => $"<slot wrapper '{Name}' of '{OwnerType.FullName}' objects>";
    }

    /// <summary>
    /// Bound wrapper descriptor
    /// </summary>
    public class PyBoundWrapperDescriptor : PyObject
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
            return WrapperDescriptor._implementation(Instance, args, kwargs);
        }

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "__self__":
                    return Instance;
                case "__func__":
                    return WrapperDescriptor;
                case "__name__":
                case "__qualname__":
                case "__objclass__":
                case "__doc__":
                    return WrapperDescriptor.GetAttribute(name);
                default:
                    return base.GetAttribute(name);
            }
        }

        // wrapper_richcompare: the same slot of the same object, == and != only.
        public override PyObject RichCompare(PyObject other, CompareOp op)
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
        public override PyString ToRepr()
            => new PyString($"<method-wrapper '{WrapperDescriptor.Name}' of {Instance.GetTypeName()} object at 0x{Instance.GetHashCode():x}>");
        public override string ToString() => ToRepr().Value;
    }
}
