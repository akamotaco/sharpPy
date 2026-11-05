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

        protected internal override PyString Str() => new PyString(ToString());
        protected internal override PyString Repr() => new PyString(ToString());

        // union_getitem
        internal PyObject Subscript(PyObject item)
        {
            var arguments = new PyTuple(Args);
            var parameters = PyGenericAlias.MakeParameters(arguments);
            var replaced = PyGenericAlias.SubsParameters(this, arguments, parameters, item);
            var result = replaced.Items.Length == 0 ? null : replaced.Items[0];
            // PyNumber_Or of each substituted argument in turn.
            for (int i = 1; i < replaced.Items.Length; i++)
                result = PyVM.Instance.ExecuteBinaryOpType(result, replaced.Items[i], BinaryOpType.OR);
            return result;
        }

        public override string ToString()
        {
            var argStrings = new string[Args.Length];
            for (int i = 0; i < Args.Length; i++)
                argStrings[i] = ReprItem(Args[i]);
            return string.Join(" | ", argStrings);
        }

        // union_repr_item: None for NoneType; what has __origin__ and __args__ by repr; a class by
        // __module__.__qualname__, the builtins without the module; the rest by repr.
        private static string ReprItem(PyObject p)
        {
            if (ReferenceEquals(p, PyType.NoneType))
                return "None";
            if (p.LookupAttribute("__origin__") != null && p.LookupAttribute("__args__") != null)
                return p.ToRepr().Value;
            var qualname = p.LookupAttribute("__qualname__");
            if (qualname == null)
                return p.ToRepr().Value;
            var module = p.LookupAttribute("__module__");
            if (module == null || module is PyNone)
                return p.ToRepr().Value;
            if (module is PyString { Value: "builtins" })
                return qualname.ToStr().Value;
            return $"{module.ToStr().Value}.{qualname.ToStr().Value}";
        }

        public override PyObject BitwiseOr(PyObject other) => Or(this, other);

        // is_unionable
        private static bool IsUnionable(PyObject value) =>
            value is PyNone or PyType or PyGenericAlias or PyUnionType ||
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
                    PyObject.CompareBool(seen, value, PyObject.CompareOp.EQ)))
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
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (other is not PyUnionType union || (op != CompareOp.EQ && op != CompareOp.NE))
                return PyNotImplemented.Instance;
            bool equal = PyObject.CompareBool(new PySet(Args), new PySet(union.Args), PyObject.CompareOp.EQ);
            return PyBool.FromBool(equal == (op == CompareOp.EQ));
        }

        // union_hash: hash(frozenset(args))
        public override int ToHash() => new PyFrozenSet(Args).ToHash();

        protected internal override long Hash() => new PyFrozenSet(Args).GetPythonHash();

        /// <summary>
        /// Check if a value is an instance of any type in this union
        /// </summary>
        public bool IsInstance(PyObject value)
        {
            foreach (var arg in Args)
            {
                if (arg is PyType type && value.GetPyType() == type)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Support subscript operations for compatibility
        /// </summary>
        // mp_subscript is union_getitem.
        protected internal override PyObject MpSubscript(PyObject key) => Subscript(key);

        public override string GetTypeName() => "types.UnionType";

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