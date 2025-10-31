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

        public override string ToString()
        {
            // Performance: Eliminated LINQ - manual conversion instead of Select + ToArray
            var argStrings = new string[Args.Length];
            for (int i = 0; i < Args.Length; i++)
            {
                argStrings[i] = Args[i].ToString();
            }
            return string.Join(" | ", argStrings);
        }

        /// <summary>
        /// Implement Union | Union logic
        /// </summary>
        public override PyObject BitwiseOr(PyObject other)
        {
            if (other is PyUnionType otherUnion)
            {
                // Flatten nested unions: (int | str) | (float | bool) → int | str | float | bool
                // Performance: Eliminated LINQ - manual array construction instead of ToArray
                var combinedArgs = new PyObject[Args.Length + otherUnion.Args.Length];
                for (int i = 0; i < Args.Length; i++)
                {
                    combinedArgs[i] = Args[i];
                }
                for (int i = 0; i < otherUnion.Args.Length; i++)
                {
                    combinedArgs[Args.Length + i] = otherUnion.Args[i];
                }
                return new PyUnionType(combinedArgs);
            }
            else if (other is PyType || other is PyBuiltinType)
            {
                // Union | Type → extend union
                // Performance: Eliminated LINQ - manual array construction instead of ToArray
                var combinedArgs = new PyObject[Args.Length + 1];
                for (int i = 0; i < Args.Length; i++)
                {
                    combinedArgs[i] = Args[i];
                }
                combinedArgs[Args.Length] = other;
                return new PyUnionType(combinedArgs);
            }

            return base.BitwiseOr(other);
        }

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