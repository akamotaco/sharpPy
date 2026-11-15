namespace SharpPy
{
    /// <summary>
    /// PyCapsule (Objects/capsule.c): a pointer and its name, which C modules hand each other; here
    /// the C# object the pointer would point at.
    /// </summary>
    internal sealed class PyCapsule : PyObject
    {
        internal static readonly PyType CapsuleType = MakeType();

        internal readonly string Name;
        internal readonly object Pointer;

        internal PyCapsule(string name, object pointer)
        {
            Name = name;
            Pointer = pointer;
        }

        public override PyType GetPyType() => CapsuleType;

        // capsule_repr
        protected internal override PyString Repr() =>
            PyString.From($"<capsule object {(Name == null ? "NULL" : $"\"{Name}\"")} at {PyObjectId.Pointer(this)}>");

        private static PyType MakeType() => new PyType("PyCapsule", new[] { PyType.ObjectType });
    }
}
