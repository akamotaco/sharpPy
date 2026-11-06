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
    public class PyFloatSubclass : PyFloat, IDictBackedInstance, IHeapInstance
    {
        // float_subtype_new: ob_fval copied from the converted temporary, which stays as the exact
        // float float_float answers with.
        private readonly PyFloat _floatValue;

        public void ChangeClass(PyClass target) => SetCustomType(target);

        // float subclasses get an instance __dict__ (tp_alloc with the subclass's basicsize).
        public Dictionary<string, PyObject> InstanceDict { get; }

        public PyFloatSubclass(PyClass cls, double value) : this(cls, new PyFloat(value)) { }

        public PyFloatSubclass(PyClass cls, PyFloat floatValue) : base(floatValue.Value)
        {
            SetCustomType(cls);
            _floatValue = floatValue;
            InstanceDict = new Dictionary<string, PyObject>();
        }

        public PyFloat GetFloatValue() => _floatValue;

        // Every slot is float's, inherited; float_pos is float_float, an exact float.
        protected internal override PyObject NbPositive() => _floatValue;

        // An enum member reads _value_ before the class sets it.
        internal override PyObject GenericGetAttr(string name) =>
            name == "_value_" && !InstanceDict.ContainsKey("_value_")
                ? _floatValue
                : base.GenericGetAttr(name);
    }
}
