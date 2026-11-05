using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// An instance of a Python class that derives from bytes. CPython keeps such an object as a real
    /// bytes object whose type slot points at the subclass, so it carries byte value semantics *and*
    /// the subclass's dunders. Before this type existed, constructing B(b"xy") for class B(bytes)
    /// returned a plain bytes object: type(B(b"xy")).__name__ was 'bytes', and every declared dunder
    /// (__len__, __add__, __contains__, __iter__, __getitem__, __eq__, __hash__, __repr__) was ignored.
    /// </summary>
    public class PyBytesSubclass : PyBytes, IDictBackedInstance, IHeapInstance
    {
        public PyClass InstanceType { get; private set; }

        public void ChangeClass(PyClass target) => InstanceType = target;
        public Dictionary<string, PyObject> InstanceDict { get; }
        public PyObject[] ConstructorArgs { get; set; }

        public PyBytesSubclass(PyClass instanceType, byte[] value) : base(value)
        {
            InstanceType = instanceType;
            InstanceDict = new Dictionary<string, PyObject>();
        }

        /// <summary>
        /// CPython's bytes_new for a subtype: the value is built first under bytes_new's own argument
        /// rules, then allocated as the subclass. The refusal pair is CPython's - a codec without a
        /// string argument, and a string without a codec - and both were previously unreachable for
        /// subclasses because the construction path returned a plain bytes object.
        /// </summary>
        internal static PyObject BytesSubtypeNew(PyClass cls, PyObject[] args, PyDict kwargs)
        {
            return new PyBytesSubclass(cls, ((PyBytes)BytesMethods.BytesNew(args, kwargs)).Value) { ConstructorArgs = args };
        }

        public override PyType GetPyType() => InstanceType;
        public override string GetTypeName() => InstanceType.Name;

        public override bool IsInstance(PyType type) =>
            InstanceType.IsSubclassOf(type) || base.IsInstance(type);

        #region Attribute access

        public override PyObject GetAttribute(string name) =>
            SubclassInstanceSupport.GetAttribute(this, InstanceType, name);

        internal override PyObject GenericGetAttr(string name) =>
            SubclassInstanceSupport.GenericGetAttribute(this, InstanceType, InstanceDict, name, n => base.GetAttribute(n));

        public override void SetAttribute(string name, PyObject value) =>
            SubclassInstanceSupport.SetAttribute(this, InstanceType, InstanceDict, name, value);

        #endregion

        #region Dunders - declared method wins, payload value is the fallback

        /// <summary>
        /// A declared dunder is looked up on the user frames only. These instances are value-carrying
        /// CLR wrappers, so consulting the full MRO through the ordinary special-method path can
        /// re-enter the same virtual. LookupUserSpecial stops at the first non-PyClass frame, which is
        /// also exactly CPython's precedence: the subclass's own definition shadows bytes' slot.
        /// </summary>
        private PyObject Op(string dunder, PyObject other, Func<PyObject> valueOp)
            => CallUserSpecialOrNull(dunder, new[] { other }) ?? valueOp();

        public override PyObject Add(PyObject other) => Op("__add__", other, () => base.Add(other));

        public override PyObject Multiply(PyObject other) => Op("__mul__", other, () => base.Multiply(other));

        public override int Length()
        {
            var declared = LookupUserSpecial("__len__");
            if (declared != null)
                return ToLengthResult(BindSpecialMethod(declared).Call(Array.Empty<PyObject>(), null));
            return base.Length();
        }

        public override PyBool Contains(PyObject item)
        {
            // CPython coerces __contains__'s result with truthiness, it does not require a bool.
            var declared = LookupUserSpecial("__contains__");
            if (declared != null)
                return BindSpecialMethod(declared).Call(new PyObject[] { item }, null).IsTrue()
                    ? PyBool.True
                    : PyBool.False;
            return base.Contains(item);
        }

        public override PyObject GetItem(PyObject index) =>
            Op("__getitem__", index, () => base.GetItem(index));

        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            var special = TryCompareSpecial(other, op, PyType.BytesType);
            return special ?? base.RichCompare(other, op);
        }

        public override int GetHashCode()
        {
            // CPython calls tp_hash, and a user __hash__ must return an int. The value is the Python
            // hash; the CLR hash code is only what this runtime happens to key on.
            var declared = LookupUserSpecial("__hash__");
            if (declared != null)
            {
                var result = BindSpecialMethod(declared).Call(Array.Empty<PyObject>(), null);
                if (PyInt.TryLongSelf(result, out var hashValue))
                    return (int)hashValue.Value;
                throw PyTypeError.Create(
                    $"__hash__ method should return an integer (return type {result.GetTypeName()})");
            }
            return base.GetHashCode();
        }

        #endregion
    }
}
