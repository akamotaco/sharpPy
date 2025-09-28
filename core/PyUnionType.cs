using System;
using System.Collections.Generic;
using System.Linq;

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
            var argStrings = Args.Select(arg => arg.ToString()).ToArray();
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
                var combinedArgs = new List<PyObject>(Args);
                combinedArgs.AddRange(otherUnion.Args);
                return new PyUnionType(combinedArgs.ToArray());
            }
            else if (other is PyType || other is PyBuiltinType)
            {
                // Union | Type → extend union
                var combinedArgs = new List<PyObject>(Args) { other };
                return new PyUnionType(combinedArgs.ToArray());
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
                return Args.All(arg => other.Args.Contains(arg)) &&
                       other.Args.All(arg => Args.Contains(arg));
            }
            return false;
        }

        public override int GetHashCode()
        {
            // Union types with same args should have same hash regardless of order
            return Args.Select(arg => arg.GetHashCode()).OrderBy(h => h).Aggregate((h1, h2) => h1 ^ h2);
        }
    }
}