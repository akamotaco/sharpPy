using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// An instance of a user class that derives from float.
    ///
    /// CPython 3.12: Objects/floatobject.c float_subtype_new
    ///   tmp = PyNumber_Float(x);                       // the value is converted first
    ///   newobj = (PyFloatObject *)type->tp_alloc(type, 0);
    ///   ((PyFloatObject *)newobj)->ob_type = type;
    ///   ((PyFloatObject *)newobj)->ob_fval = ((PyFloatObject *)tmp)->ob_fval;
    ///
    /// so a float subclass instance *is* a float object whose type slot points at the subclass;
    /// repr, arithmetic, comparison, hashing and text formatting all keep float semantics while
    /// type(x), isinstance() and the subclass's own methods report the subclass. int and str
    /// already had this shape (PyIntSubclass, PyStrSubclass): a float whose _customType is the class.
    /// </summary>
    public class PyFloatSubclass : PyFloat
    {
        // float_subtype_new: ob_fval copied from the converted temporary.
        public PyFloatSubclass(PyClass cls, double value) : base(value) => SetCustomType(cls);

        public PyFloatSubclass(PyClass cls, PyFloat floatValue) : this(cls, floatValue.Value) { }

        // float_float: PyFloat_FromDouble(ob_fval), an exact float.
        public PyFloat GetFloatValue() => new PyFloat(Value);

        // Every slot is float's, inherited; float_pos is float_float.
        protected internal override PyObject NbPositive() => GetFloatValue();

    }
}
