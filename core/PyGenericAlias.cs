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
        /// CPython 3.12: Objects/genericaliasobject.c:52-120 (ga_repr_item)
        /// </summary>
        public override PyStr ToStr()
        {
            if (Args is PyTuple tuple)
            {
                var argStrs = new string[tuple.Items.Length];
                for (int i = 0; i < tuple.Items.Length; i++)
                {
                    argStrs[i] = FormatTypeArg(tuple.Items[i]);
                }
                return new PyStr($"{Origin.Name}[{string.Join(", ", argStrs)}]");
            }
            return new PyStr($"{Origin.Name}[{FormatTypeArg(Args)}]");
        }

        /// <summary>
        /// Format a type argument for display.
        /// CPython 3.12: Uses __qualname__ for types, repr for others.
        /// Objects/genericaliasobject.c:52-120 (ga_repr_item)
        /// </summary>
        private string FormatTypeArg(PyObject arg)
        {
            // CPython: For types (which have __qualname__), use qualname if builtin
            if (arg is PyType typeArg)
            {
                // CPython: builtins use just the name, others use module.name
                if (string.IsNullOrEmpty(typeArg.Module) || typeArg.Module == "builtins")
                {
                    return typeArg.Name;
                }
                return $"{typeArg.Module}.{typeArg.Name}";
            }

            // For non-types (like literals), use str representation
            return arg.ToStr().Value;
        }

        public override PyStr ToRepr()
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

        /// <summary>
        /// CPython 3.12: Support | operator for Union types (PEP 604)
        /// Example: list[int] | None → types.UnionType
        /// Objects/genericaliasobject.c:ga_or / ga_ror
        /// </summary>
        public override PyObject BitwiseOr(PyObject other)
        {
            // GenericAlias | X → UnionType([self, X])
            if (other is PyUnionType otherUnion)
            {
                // GenericAlias | UnionType → flatten
                var combinedArgs = new PyObject[1 + otherUnion.Args.Length];
                combinedArgs[0] = this;
                for (int i = 0; i < otherUnion.Args.Length; i++)
                {
                    combinedArgs[1 + i] = otherUnion.Args[i];
                }
                return new PyUnionType(combinedArgs);
            }
            else
            {
                // GenericAlias | Type/None/GenericAlias/etc. → UnionType
                return new PyUnionType(new PyObject[] { this, other });
            }
        }

    }
}