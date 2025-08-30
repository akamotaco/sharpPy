using System;

namespace SharpPy
{
    /// <summary>
    /// CPython-compatible Cell object for closure variable storage
    /// Represents a mutable container for variables captured by closures
    /// </summary>
    public class PyCell : PyObject
    {
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
        /// True if this cell contains a value
        /// </summary>
        public bool HasValue => _value != null;
        
        public PyCell(PyObject? value = null)
        {
            _value = value;
        }
        
        public override PyType GetPyType() => PyType.CellType;
        
        public override string GetTypeName() => "cell";
        
        public override string ToString()
        {
            if (_value == null)
                return "<cell: empty>";
            return $"<cell: {_value}>";
        }
        
        /// <summary>
        /// Get the cell value, throwing UnboundLocalError if empty
        /// </summary>
        public PyObject GetValue()
        {
            if (_value == null)
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
        /// Clear the cell value
        /// </summary>
        public void Clear()
        {
            _value = null;
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