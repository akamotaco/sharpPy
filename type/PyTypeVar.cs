using System;

namespace SharpPy
{
    /// <summary>
    /// Python TypeVar implementation - PEP 695 Type Parameter Syntax
    /// Represents a type variable used in generic functions/classes
    /// CPython equivalent: typing.TypeVar
    /// </summary>
    public class PyTypeVar : PyObject
    {
        #region Core Properties

        /// <summary>
        /// TypeVar name (e.g., "T")
        /// CPython: TypeVar.__name__
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Optional bound type constraint
        /// CPython: TypeVar.__bound__
        /// </summary>
        public PyObject Bound { get; }

        /// <summary>
        /// Type constraints (for TypeVar('T', int, str, ...))
        /// CPython: TypeVar.__constraints__
        /// </summary>
        public PyTuple Constraints { get; }

        /// <summary>
        /// Whether this TypeVar is covariant
        /// CPython: TypeVar.__covariant__
        /// </summary>
        public bool Covariant { get; }

        /// <summary>
        /// Whether this TypeVar is contravariant
        /// CPython: TypeVar.__contravariant__
        /// </summary>
        public bool Contravariant { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Create a TypeVar with name only (most common case for PEP 695)
        /// </summary>
        public PyTypeVar(string name)
            : this(name, null, null, false, false)
        {
        }

        /// <summary>
        /// Create a TypeVar with name and optional bound
        /// </summary>
        public PyTypeVar(string name, PyObject bound)
            : this(name, bound, null, false, false)
        {
        }

        /// <summary>
        /// Full constructor with all options
        /// </summary>
        public PyTypeVar(string name, PyObject bound, PyTuple constraints, bool covariant, bool contravariant)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Bound = bound;
            Constraints = constraints ?? new PyTuple(Array.Empty<PyObject>());
            Covariant = covariant;
            Contravariant = contravariant;

            // CPython constraint: Cannot be both covariant and contravariant
            if (Covariant && Contravariant)
            {
                throw PyTypeError.Create("TypeVar cannot be both covariant and contravariant");
            }

            // CPython constraint: Cannot have both bound and constraints
            if (Bound != null && Constraints.Items.Length > 0)
            {
                throw PyTypeError.Create("TypeVar cannot have both 'bound' and 'constraints'");
            }
        }

        #endregion

        #region Type System

        public override PyType GetPyType() => PyType.TypeVarType;
        public override string GetTypeName() => "TypeVar";

        #endregion

        #region String Representation

        /// <summary>
        /// CPython repr(): TypeVar('T'), TypeVar('T', bound=int), etc.
        /// </summary>
        public override PyStr ToRepr()
        {
            // Build repr string based on CPython format
            var parts = new System.Collections.Generic.List<string> { $"'{Name}'" };

            if (Bound != null)
            {
                parts.Add($"bound={Bound.ToRepr().Value}");
            }

            if (Constraints.Items.Length > 0)
            {
                foreach (var constraint in Constraints.Items)
                {
                    parts.Add(constraint.ToRepr().Value);
                }
            }

            if (Covariant)
            {
                parts.Add("covariant=True");
            }

            if (Contravariant)
            {
                parts.Add("contravariant=True");
            }

            return new PyStr($"TypeVar({string.Join(", ", parts)})");
        }

        /// <summary>
        /// CPython str(): ~T (for simple TypeVars)
        /// </summary>
        public override PyStr ToStr()
        {
            // CPython 3.12: Simple TypeVars from PEP 695 show as ~T
            if (Bound == null && Constraints.Items.Length == 0 && !Covariant && !Contravariant)
            {
                return new PyStr($"~{Name}");
            }

            // Otherwise, use repr()
            return ToRepr();
        }

        #endregion

        #region Hash and Equality

        public override int ToHash()
        {
            // TypeVars are compared by identity, not value
            // Use object reference hash
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        protected override PyObject PyEquals(PyObject other)
        {
            // CPython: TypeVars are compared by identity (is, not ==)
            // Two TypeVars with the same name are NOT equal unless they're the same object
            return PyBool.FromBool(ReferenceEquals(this, other));
        }

        #endregion

        #region Attribute Access

        /// <summary>
        /// Provide access to TypeVar attributes (__name__, __bound__, etc.)
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__name__" => new PyStr(Name),
                "__bound__" => Bound ?? PyNone.Instance,
                "__constraints__" => Constraints,
                "__covariant__" => PyBool.FromBool(Covariant),
                "__contravariant__" => PyBool.FromBool(Contravariant),
                _ => base.GetAttribute(name)
            };
        }

        #endregion
    }

    /// <summary>
    /// Python ParamSpec implementation - PEP 612
    /// Represents a parameter specification for generic callables
    /// CPython equivalent: typing.ParamSpec
    /// </summary>
    public class PyParamSpec : PyObject
    {
        public string Name { get; }

        public PyParamSpec(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public override PyType GetPyType() => PyType.ParamSpecType;
        public override string GetTypeName() => "ParamSpec";

        public override PyStr ToRepr() => new PyStr($"ParamSpec('{Name}')");
        public override PyStr ToStr() => new PyStr($"~{Name}");

        public override int ToHash()
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return PyBool.FromBool(ReferenceEquals(this, other));
        }

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__name__" => new PyStr(Name),
                _ => base.GetAttribute(name)
            };
        }
    }

    /// <summary>
    /// Python TypeVarTuple implementation - PEP 646
    /// Represents a type variable tuple for variadic generics
    /// CPython equivalent: typing.TypeVarTuple
    /// </summary>
    public class PyTypeVarTuple : PyObject
    {
        public string Name { get; }

        public PyTypeVarTuple(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public override PyType GetPyType() => PyType.TypeVarTupleType;
        public override string GetTypeName() => "TypeVarTuple";

        public override PyStr ToRepr() => new PyStr($"TypeVarTuple('{Name}')");
        public override PyStr ToStr() => new PyStr($"~{Name}");

        public override int ToHash()
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return PyBool.FromBool(ReferenceEquals(this, other));
        }

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__name__" => new PyStr(Name),
                _ => base.GetAttribute(name)
            };
        }
    }
}
