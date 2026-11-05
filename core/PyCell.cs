using System;

namespace SharpPy
{
    /// <summary>
    /// CPython-compatible Cell object for closure variable storage
    /// Represents a mutable container for variables captured by closures
    /// </summary>
    public class PyCell : PyObject
    {
        // cell_repr (Objects/cellobject.c)
        protected internal override PyString Repr() => new PyString(HasValue
            ? $"<cell at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x}: {_value.GetTypeName()} object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_value):x}>"
            : $"<cell at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x}: empty>");

        private PyObject? _value;
        
        /// <summary>
        /// The value stored in this cell, or null if empty
        /// </summary>
        public PyObject? Value 
        { 
            get => _value;
            set => _value = value;
        }
        
        /// <summary>
        /// True if this cell contains a value (CPython 3.12: NULL은 빈 값으로 간주)
        /// </summary>
        public bool HasValue => _value != null && !PyNull.IsNull(_value);
        
        public PyCell(PyObject? value = null)
        {
            _value = value;
        }
        
        public override PyType GetPyType() => PyType.CellType;

        // cell_get_contents
        internal PyObject Contents() => HasValue ? _value : throw PyValueError.Create("Cell is empty");

        // cell_new
        internal static void InstallNew()
        {
            var type = PyType.CellType;
            type.ConstructedByNewSlot = true;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                if (args.Length > 1)
                    throw PyTypeError.Create($"cell expected at most 1 argument, got {args.Length}");
                return new PyCell(args.Length == 1 ? args[0] : null);
            });
        }

        // cell_richcompare: by the contents, and an empty cell is below any other.
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (other is not PyCell cell)
                return PyNotImplemented.Instance;
            if (HasValue && cell.HasValue)
                return Compare(_value, cell._value, op);
            int left = HasValue ? 1 : 0, right = cell.HasValue ? 1 : 0;
            return PyBool.FromBool(op switch
            {
                CompareOp.EQ => left == right,
                CompareOp.NE => left != right,
                CompareOp.LT => left < right,
                CompareOp.LE => left <= right,
                CompareOp.GT => left > right,
                _ => left >= right,
            });
        }

        public override void DelAttribute(string name)
        {
            if (name == "cell_contents") _value = null;
            else base.DelAttribute(name);
        }

        // cell_getsetlist (Objects/cellobject.c): cell_contents.
        public override PyObject GetAttribute(string name)
        {
            if (name != "cell_contents") return base.GetAttribute(name);
            return HasValue ? _value : throw PyValueError.Create("Cell is empty");
        }

        public override void SetAttribute(string name, PyObject value)
        {
            if (name == "cell_contents") _value = value;
            else base.SetAttribute(name, value);
        }
        
        public override string GetTypeName() => "cell";
        
        public override string ToString()
        {
            if (_value == null || PyNull.IsNull(_value))
                return "<cell: empty>";
            return $"<cell: {_value}>";
        }
        
        /// <summary>
        /// Get the cell value, throwing UnboundLocalError if empty (CPython 3.12: NULL도 빈 값)
        /// </summary>
        public PyObject GetValue()
        {
            if (_value == null || PyNull.IsNull(_value))
                throw new PythonException(new PyUnboundLocalError("local variable referenced before assignment"));
            return _value;
        }
        
        /// <summary>
        /// Set the cell value
        /// </summary>
        public void SetValue(PyObject value)
        {
            _value = value;
        }
        
        /// <summary>
        /// Clear the cell value - CPython 3.12 호환: NULL 상태로 설정
        /// </summary>
        public void Clear()
        {
            _value = PyNull.Instance;
        }
        
        /// <summary>
        /// Create a new cell with the given value
        /// </summary>
        public static PyCell Create(PyObject? value = null)
        {
            return new PyCell(value);
        }
    }
}