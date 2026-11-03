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

        public override PyString ToRepr()
        {
            var target = Referent ?? PyNone.Instance;
            return new PyString($"<weakproxy at 0x{PyObjectId.Of(this):x} to {target.GetTypeName()} at 0x{PyObjectId.Of(target):x}>");
        }

        public override PyString AsString() => new PyString(Target.ToClrString());

        public override string ToClrString() => Target.ToClrString();

        public override int ToHash() => throw PyTypeError.Create($"unhashable type: '{GetTypeName()}'");

        internal override long GetPythonHash() => throw PyTypeError.Create($"unhashable type: '{GetTypeName()}'");

        public override PyObject RichCompare(PyObject other, CompareOp op) => Target.RichCompare(Unwrap(other), op);

        public override bool PyBoolValue() => Target.IsTrue();

        public override bool IsTrue() => Target.IsTrue();

        public override PyObject GetAttribute(string name) => Target.GetAttribute(name);

        public override void SetAttribute(string name, PyObject value) => Target.SetAttribute(name, value);

        public override void DelAttribute(string name) => Target.DelAttribute(name);

        public override bool IsInstance(PyType type) => base.IsInstance(type) || Target.IsInstance(type);

        public override bool IsCallable() => _callable;

        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            if (!_callable)
                throw PyTypeError.Create($"'{GetTypeName()}' object is not callable");
            return Target.Call(args, kwargs);
        }

        public override PyInt AsInt() => Target.AsInt();

        public override PyFloat AsFloat() => Target.AsFloat();

        public override int ToInt() => Target.ToInt();

        public override double ToFloat() => Target.ToFloat();

        public override int Length() => Target.Length();

        public override PyObject Positive() => Target.Positive();

        public override PyObject Negative() => Target.Negative();

        public override PyObject BitwiseNot() => Target.BitwiseNot();

        public override PyObject DivMod(PyObject other) => Target.DivMod(Unwrap(other));

        public override PyObject GetIterator() => Target.GetIterator();

        public override PyObject Next()
        {
            var target = Target;
            if (!target.HasTypeSlot("__next__"))
                throw PyTypeError.Create($"Weakref proxy referenced a non-iterator '{target.GetTypeName()}' object");
            return target.Next();
        }

        public override PyBool Contains(PyObject item) => Target.Contains(item);

        public override PyObject GetItem(PyObject index) => PyAbstract.GetItem(Target, index);

        public override void SetItem(PyObject index, PyObject value) => PyAbstract.SetItem(Target, index, value);

        public override void DelItem(PyObject index) => PyAbstract.DelItem(Target, index);

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
                        return new PyInt(target.Length());
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
