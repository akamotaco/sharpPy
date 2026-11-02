using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python 3.12 types.UnionType implementation
    /// Created when using the | operator between types (PEP 585)
    /// </summary>
    public class PyUnionType : PyObject
    {
        public PyObject[] Args { get; }

        public PyUnionType(PyObject[] args)
        {
            Args = args ?? throw new ArgumentNullException(nameof(args));
        }

        public override PyType GetPyType() => PyType.UnionType;

        public override PyString AsString() => new PyString(ToString());
        public override PyString ToRepr() => new PyString(ToString());

        public override string ToString()
        {
            // Performance: Eliminated LINQ - manual conversion instead of Select + ToArray
            var argStrings = new string[Args.Length];
            for (int i = 0; i < Args.Length; i++)
            {
                // union_repr_item: None for NoneType, a class by its dotted name, anything else by repr.
                argStrings[i] = Args[i] switch
                {
                    PyType type when type.Name == "NoneType" => "None",
                    PyType type when string.IsNullOrEmpty(type.Module) || type.Module == "builtins" => type.Name,
                    PyType type => $"{type.Module}.{type.Name}",
                    var other => other.ToRepr().Value,
                };
            }
            return string.Join(" | ", argStrings);
        }

        public override PyObject BitwiseOr(PyObject other) => Or(this, other);

        // is_unionable
        private static bool IsUnionable(PyObject value) =>
            value is PyNone or PyType or PyBuiltinType or PyGenericAlias or PyUnionType ||
            value.GetPyType().Name == "TypeAliasType";

        private static void Append(List<PyObject> parts, PyObject value)
        {
            if (value is PyUnionType union)
            {
                foreach (var item in union.Args) Append(parts, item);
                return;
            }
            if (value is PyNone) value = value.GetPyType();
            // dedup_and_flatten_args: a generic alias is the same by value, the rest by identity.
            foreach (var seen in parts)
                if (ReferenceEquals(seen, value) || (seen is PyGenericAlias && value is PyGenericAlias &&
                    seen.RichCompare(value, CompareOp.EQ).IsTrue()))
                    return;
            parts.Add(value);
        }

        /// <summary>_Py_union_type_or</summary>
        internal static PyObject Or(PyObject left, PyObject right)
        {
            if (!IsUnionable(left) || !IsUnionable(right))
                return PyNotImplemented.Instance;
            var parts = new List<PyObject>();
            Append(parts, left);
            Append(parts, right);
            return parts.Count == 1 ? parts[0] : new PyUnionType(parts.ToArray());
        }

        // union_richcompare: as sets of their members.
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            if (other is not PyUnionType union || (op != CompareOp.EQ && op != CompareOp.NE))
                return PyNotImplemented.Instance;
            bool equal = new PySet(Args).RichCompare(new PySet(union.Args), CompareOp.EQ).IsTrue();
            return PyBool.FromBool(equal == (op == CompareOp.EQ));
        }

        // union_hash: hash(frozenset(args))
        public override int ToHash() => new PyFrozenSet(Args).ToHash();

        internal override long GetPythonHash() => new PyFrozenSet(Args).GetPythonHash();

        /// <summary>
        /// Check if a value is an instance of any type in this union
        /// </summary>
        public bool IsInstance(PyObject value)
        {
            foreach (var arg in Args)
            {
                if (arg is PyType type && value.GetPyType() == type)
                    return true;
                if (arg is PyBuiltinType builtinType && value.GetPyType().Name == builtinType.Name)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Support subscript operations for compatibility
        /// </summary>
        public override PyObject GetItem(PyObject key)
        {
            throw PyTypeError.Create("'UnionType' object is not subscriptable");
        }

        public override bool Equals(object? obj)
        {
            if (obj is PyUnionType other)
            {
                if (Args.Length != other.Args.Length)
                    return false;

                // Order doesn't matter for union equality
                // Performance: Eliminated LINQ - manual loops instead of All + Contains
                for (int i = 0; i < Args.Length; i++)
                {
                    bool found = false;
                    for (int j = 0; j < other.Args.Length; j++)
                    {
                        if (Args[i].Equals(other.Args[j]))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        return false;
                }

                for (int i = 0; i < other.Args.Length; i++)
                {
                    bool found = false;
                    for (int j = 0; j < Args.Length; j++)
                    {
                        if (other.Args[i].Equals(Args[j]))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        return false;
                }

                return true;
            }
            return false;
        }

        public override int GetHashCode()
        {
            // Union types with same args should have same hash regardless of order
            // Performance: Eliminated LINQ - manual sorting and aggregation instead of Select + OrderBy + Aggregate
            if (Args.Length == 0)
                return 0;

            var hashes = new int[Args.Length];
            for (int i = 0; i < Args.Length; i++)
            {
                hashes[i] = Args[i].GetHashCode();
            }

            // Sort hashes for order-independent hashing
            Array.Sort(hashes);

            // Aggregate with XOR
            int result = hashes[0];
            for (int i = 1; i < hashes.Length; i++)
            {
                result ^= hashes[i];
            }

            return result;
        }
    }
}