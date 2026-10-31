using System;

namespace SharpPy
{
    /// <summary>
    /// CPython PyMethodDef 호환: 메서드 descriptor
    /// C 확장 모듈의 메서드를 표현하는 descriptor
    /// </summary>
    public class PyMethodDescriptor : PyObject, IDescriptor
    {
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
            _implementation = implementation;
            _minArgs = minArgs;
            _maxArgs = maxArgs;
            _acceptsKwargs = acceptsKwargs;
        }

        public override PyType GetPyType() => PyType.MethodDescriptorType;
        public override string GetTypeName() => "method_descriptor";

        // CPython method_descriptor_repr: <method 'NAME' of 'TYPE' objects>
        public override PyString ToRepr()
            => OwnerType != null
                ? new PyString($"<method '{Name}' of '{OwnerType.Name}' objects>")
                : new PyString($"<method '{Name}'>");

        // Descriptor protocol
        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null)
            {
                // 클래스에서 접근 시 descriptor 자체 반환
                return this;
            }

            // 인스턴스에서 접근 시 bound method 반환
            return new PyBoundMethodDescriptor(instance, this);
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"attribute '{Name}' of '{OwnerType.Name}' objects is not writable");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"attribute '{Name}' of '{OwnerType.Name}' objects is not writable");
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
                throw PyTypeError.Create($"{OwnerType.Name}.{Name}() takes no keyword arguments");
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            RejectUnwantedKeywords(kwargs);

            // Unbound method call: 첫 번째 인자가 self여야 함
            if (args.Length < 1)
            {
                throw PyTypeError.Create($"unbound method {OwnerType.Name}.{Name}() needs an argument");
            }

            var self = args[0];
            var actualArgs = new PyObject[args.Length - 1];
            Array.Copy(args, 1, actualArgs, 0, args.Length - 1);

            return _implementation(self, actualArgs, kwargs);
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

        public override string ToString() => $"<method '{Name}' of '{OwnerType.Name}' objects>";
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
        // __new__ is a static builtin surface, not a <method ... of ... objects>
        // descriptor, so it must not inherit PyMethodDescriptor's repr.
        public override PyString ToRepr() => new PyString($"<built-in function {Name}>");
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
        private readonly string _doc;

        public PyGetSetDescriptor(
            string name,
            PyType ownerType,
            Func<PyObject, PyObject> getter,
            Action<PyObject, PyObject> setter = null,
            string doc = null)
        {
            Name = name;
            OwnerType = ownerType;
            _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _setter = setter;
            _doc = doc;
        }

        public override PyType GetPyType() => PyType.PropertyType;
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
                throw PyAttributeError.Create($"attribute '{Name}' of '{OwnerType.Name}' objects is not writable");
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
            throw PyAttributeError.Create(
                $"attribute '{Name}' of '{OwnerType.Name}' objects is not writable");
        }

        // Native getset descriptors have a setting slot even when it only
        // rejects writes. Read-only attributes still take data precedence.
        public bool IsDataDescriptor() => true;

        public override string ToString() => $"<attribute '{Name}' of '{OwnerType.Name}' objects>";
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

        public PyMemberDescriptor(
            string name,
            PyType ownerType,
            Func<PyObject, PyObject> getter,
            Action<PyObject, PyObject> setter = null,
            bool readOnly = false)
        {
            Name = name;
            OwnerType = ownerType;
            _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _setter = setter;
            _readonly = readOnly || setter == null;
        }

        public override PyType GetPyType() => PyType.PropertyType;
        public override string GetTypeName() => "member_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null)
            {
                // 클래스에서 접근 시 descriptor 자체 반환
                return this;
            }

            return _getter(instance);
        }

        public void Set(PyObject instance, PyObject value)
        {
            if (_readonly || _setter == null)
            {
                throw PyAttributeError.Create($"attribute '{Name}' of '{OwnerType.Name}' objects is not writable");
            }

            _setter(instance, value);
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"can't delete attribute '{Name}' of '{OwnerType.Name}' objects");
        }

        public bool IsDataDescriptor() => !_readonly && _setter != null;

        public override string ToString() => $"<member '{Name}' of '{OwnerType.Name}' objects>";
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
            _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
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
        public override string GetTypeName() => "wrapper_descriptor";

        // CPython wrapper_descriptor_repr: <slot wrapper 'NAME' of 'TYPE' objects>
        public override PyString ToRepr()
            => OwnerType != null
                ? new PyString($"<slot wrapper '{Name}' of '{OwnerType.Name}' objects>")
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
            throw PyAttributeError.Create($"attribute '{Name}' of '{OwnerType.Name}' objects is not writable");
        }

        // A wrapper/method descriptor has no tp_descr_set at all, so CPython reports it one level up,
        // in _PyObject_GenericSetAttrWithDict (Objects/object.c:1592-1598): the name resolved but the
        // object has no instance dict to delete from -
        // AttributeError "'int' object attribute '__add__' is read-only".
        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create(
                $"'{OwnerType.Name}' object attribute '{Name}' is read-only");
        }

        public bool IsDataDescriptor() => false;

        public override bool IsCallable() => true;

        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1)
            {
                throw PyTypeError.Create($"descriptor '{Name}' of '{OwnerType.Name}' object needs an argument");
            }

            var self = args[0];
            var actualArgs = new PyObject[args.Length - 1];
            Array.Copy(args, 1, actualArgs, 0, args.Length - 1);

            return _implementation(self, actualArgs, kwargs);
        }

        public override string ToString() => $"<slot wrapper '{Name}' of '{OwnerType.Name}' objects>";
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
                    return new PyString(WrapperDescriptor.Name);
                default:
                    return base.GetAttribute(name);
            }
        }

        // CPython method-wrapper repr: <method-wrapper 'NAME' of TYPE object at 0xID>
        public override PyString ToRepr()
            => new PyString($"<method-wrapper '{WrapperDescriptor.Name}' of {Instance.GetTypeName()} object at 0x{Instance.GetHashCode():x}>");
        public override string ToString() => ToRepr().Value;
    }
}
