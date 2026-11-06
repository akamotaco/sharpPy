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


        #endregion

        #region Dunders - declared method wins, payload value is the fallback

        /// <summary>
        /// A declared dunder is looked up on the user frames only. These instances are value-carrying
        /// CLR wrappers, so consulting the full MRO through the ordinary special-method path can
        /// re-enter the same virtual. LookupUserSpecial stops at the first non-PyClass frame, which is
        /// also exactly CPython's precedence: the subclass's own definition shadows bytes' slot.
        /// </summary>



        #endregion
    }
}
