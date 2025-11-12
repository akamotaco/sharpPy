namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible generic type alias: list[int], tuple[str, int], etc.
    /// Represents a parameterized generic type.
    /// </summary>
    public class PyGenericAlias : PyObject
    {
        public PyType Origin { get; }
        public PyObject Args { get; }

        public PyGenericAlias(PyType origin, PyObject args)
        {
            Origin = origin;
            Args = args;
        }

        public override PyType GetPyType() => PyType.GenericAliasType;
        
        /// <summary>
        /// String representation: list[int], tuple[str, int], etc.
        /// </summary>
        public override PyString ToStr()
        {
            if (Args is PyTuple tuple)
            {
                var argStrs = new string[tuple.Items.Length];
                for (int i = 0; i < tuple.Items.Length; i++)
                {
                    argStrs[i] = tuple.Items[i].ToStr().Value;
                }
                return new PyString($"{Origin.Name}[{string.Join(", ", argStrs)}]");
            }
            return new PyString($"{Origin.Name}[{Args.ToStr().Value}]");
        }

        public override PyString ToRepr()
        {
            return ToStr();
        }

        /// <summary>
        /// For type checking and comparison
        /// </summary>
        public override bool PyBoolValue() => true;

        /// <summary>
        /// Hash based on origin type and arguments
        /// </summary>
        protected override int GetDefaultHash()
        {
            return Origin.GetHashCode() ^ Args.GetHashCode();
        }

        /// <summary>
        /// Equality comparison for generic aliases
        /// </summary>
        protected override PyObject PyEquals(PyObject other)
        {
            if (other is PyGenericAlias otherAlias)
            {
                return PyBool.FromBool(
                    ReferenceEquals(Origin, otherAlias.Origin) && 
                    Args.Equals(otherAlias.Args)
                );
            }
            return PyBool.False;
        }

        /// <summary>
        /// CPython 3.12: Generic alias subscripting for type parameters
        /// Example: Point[int] where Point = tuple[T, T]
        /// </summary>
        public override PyObject GetItem(PyObject key)
        {
            // For type aliases like Point[T] = tuple[T, T], Point[int] should return tuple[int, int]
            // This implements parameterization of generic aliases
            return new PyGenericAlias(Origin, key);
        }
    }
}