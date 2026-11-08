using System;

namespace SharpPy
{
    /// <summary>
    /// weakref.proxy(x) (Objects/weakrefobject.c): a PyWeakReference whose type forwards every slot
    /// to the referent, with UNWRAP applied to the other operands. A dead referent is ReferenceError.
    /// </summary>
    internal sealed class PyWeakProxy : PyWeakRef
    {
        private readonly bool _callable;

        internal PyWeakProxy(PyObject target, PyObject callback) : base(target, callback) =>
            _callable = target.IsCallable();

        public override PyType GetPyType() => _callable ? PyType.CallableProxyType : PyType.ProxyType;

        public override string GetTypeName() => _callable ? "weakref.CallableProxyType" : "weakref.ProxyType";

        // proxy_checkref
        private PyObject Target => Referent ?? throw PyReferenceError.Create("weakly-referenced object no longer exists");

        // UNWRAP
        internal static PyObject Unwrap(PyObject value) => value is PyWeakProxy proxy ? proxy.Target : value;

        protected internal override PyString Repr()
        {
            var target = Referent ?? PyNone.Instance;
            return PyString.From($"<weakproxy at {PyObjectId.Pointer(this)} to {target.GetTypeName()} at {PyObjectId.Pointer(target)}>");
        }

        protected internal override PyString Str() => PyString.From(Target.ToClrString());

        public override string ToClrString() => Target.ToClrString();

        // A proxy's type is not weakref's: its tp_hash is PyObject_HashNotImplemented, not weakref_hash.
        protected internal override long Hash() => HashNotImplemented(this);

        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => Compare(Target, Unwrap(other), op);

        protected internal override bool Bool() => Target.IsTrue();


        protected internal override PyObject TpGetattro(string name) => Target.GetAttribute(name);

        protected internal override void TpSetattro(string name, PyObject value) => Target.SetAttribute(name, value);

        protected internal override void TpDelattro(string name) => Target.DelAttribute(name);

        public override bool IsInstance(PyType type) => base.IsInstance(type) || Target.IsInstance(type);

        public override bool IsCallable() => _callable;

        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            if (!_callable)
                throw PyTypeError.Create($"'{GetTypeName()}' object is not callable");
            return Target.Call(args, kwargs);
        }

        // proxy_int and proxy_float: PyNumber_Long and PyNumber_Float of the referent.
        protected internal override PyObject NbInt() => Target.AsInt();

        protected internal override PyObject NbFloat() => Target.AsFloat();

        public override int ToInt() => Target.ToInt();

        public override double ToFloat() => Target.ToFloat();

        protected internal override long Len() => Target.Length();

        protected internal override PyObject NbPositive() => Target.Positive();

        protected internal override PyObject NbNegative() => Target.Negative();

        protected internal override PyObject NbInvert() => Target.BitwiseNot();

        public override PyObject DivMod(PyObject other) => Target.DivMod(Unwrap(other));

        protected internal override PyObject Iter() => Target.GetIterator();

        protected internal override PyObject IterNext()
        {
            var target = Target;
            // proxy_iternext: PyIter_Check of the referent.
            if (!target.IsIterator)
                throw PyTypeError.Create($"Weakref proxy referenced a non-iterator '{target.GetTypeName()}' object");
            return target.Next();
        }

        protected internal override PyBool ContainsItem(PyObject item) => Target.Contains(item);

        protected internal override PyObject MpSubscript(PyObject index) => PyAbstract.GetItem(Target, index);

        protected internal override void MpAssSubscript(PyObject index, PyObject value) => PyAbstract.SetItem(Target, index, value);

        protected internal override void MpDelSubscript(PyObject index) => PyAbstract.DelItem(Target, index);

        private static readonly (string, BinaryOpType, BinaryOpType)[] NumberSlots =
        {
            ("add", BinaryOpType.ADD, BinaryOpType.INPLACE_ADD), ("sub", BinaryOpType.SUBTRACT, BinaryOpType.INPLACE_SUBTRACT),
            ("mul", BinaryOpType.MULTIPLY, BinaryOpType.INPLACE_MULTIPLY), ("truediv", BinaryOpType.TRUE_DIVIDE, BinaryOpType.INPLACE_TRUE_DIVIDE),
            ("floordiv", BinaryOpType.FLOOR_DIVIDE, BinaryOpType.INPLACE_FLOOR_DIVIDE), ("mod", BinaryOpType.MODULO, BinaryOpType.INPLACE_MODULO),
            ("pow", BinaryOpType.POWER, BinaryOpType.INPLACE_POWER), ("lshift", BinaryOpType.LSHIFT, BinaryOpType.INPLACE_LSHIFT),
            ("rshift", BinaryOpType.RSHIFT, BinaryOpType.INPLACE_RSHIFT), ("and", BinaryOpType.AND, BinaryOpType.INPLACE_AND),
            ("or", BinaryOpType.OR, BinaryOpType.INPLACE_OR), ("xor", BinaryOpType.XOR, BinaryOpType.INPLACE_XOR),
            ("matmul", BinaryOpType.MATRIX_MULTIPLY, BinaryOpType.INPLACE_MATRIX_MULTIPLY),
        };

        // WRAP_METHOD and the slots reached by name: what the referent's own method answers.
        private static readonly string[] Forwarded =
        {
            "__index__", "__bytes__", "__reversed__", "__abs__", "__len__", "__getitem__", "__setitem__", "__delitem__",
            "__iter__", "__next__", "__contains__", "__bool__", "__int__", "__float__", "__neg__", "__pos__", "__invert__",
            "__divmod__",
        };

        private static PyObject Binary(PyObject left, PyObject right, BinaryOpType operation) =>
            PyVM.Instance.ExecuteBinaryOpType(Unwrap(left), Unwrap(right), operation);

        private static void Install(PyType type)
        {
            foreach (var (name, regular, inplace) in NumberSlots)
            {
                type.TypeDict[$"__{name}__"] = new PyWrapperDescriptor($"__{name}__", type,
                    (self, args, kwargs) => Binary(self, args[0], regular), 1, 2);
                type.TypeDict[$"__r{name}__"] = new PyWrapperDescriptor($"__r{name}__", type,
                    (self, args, kwargs) => Binary(args[0], self, regular), 1, 2);
                type.TypeDict[$"__i{name}__"] = new PyWrapperDescriptor($"__i{name}__", type,
                    (self, args, kwargs) => Binary(self, args[0], inplace), 1, 2);
            }
            foreach (var name in Forwarded)
            {
                var slot = name;
                type.TypeDict[slot] = new PyWrapperDescriptor(slot, type, (self, args, kwargs) =>
                {
                    var target = Unwrap(self);
                    // proxy_length: PyObject_Size, so what has no length says TypeError.
                    if (slot == "__len__")
                        return PyInt.From(target.Length());
                    var unwrapped = new PyObject[args.Length];
                    for (int i = 0; i < args.Length; i++) unwrapped[i] = Unwrap(args[i]);
                    return target.GetAttribute(slot).Call(unwrapped, kwargs);
                }, 0, int.MaxValue);
            }
        }

        internal static void InstallTypes()
        {
            Install(PyType.ProxyType);
            Install(PyType.CallableProxyType);
            // tp_hash PyObject_HashNotImplemented: type_ready writes __hash__ = None.
            PyType.ProxyType.TypeDict["__hash__"] = PyNone.Instance;
            PyType.CallableProxyType.TypeDict["__hash__"] = PyNone.Instance;
            PyType.CallableProxyType.TypeDict["__call__"] = new PyWrapperDescriptor("__call__", PyType.CallableProxyType,
                (self, args, kwargs) => self.Call(args, kwargs), 0, int.MaxValue);
        }

        // PyWeakref_NewProxy
        internal static PyObject Create(PyObject target, PyObject callback)
        {
            if (!target.GetPyType().SupportsWeakReference())
                throw PyTypeError.Create($"cannot create weak reference to '{target.GetTypeName()}' object");
            if (callback is PyNone) callback = null;
            if (callback == null)
                foreach (var reference in PyFinalizer.WeakReferencesOf(target))
                    if (reference is PyWeakProxy { Callback: null })
                        return reference;
            var proxy = new PyWeakProxy(target, callback);
            PyFinalizer.TrackWeakReference(target, proxy);
            return proxy;
        }
    }
}
