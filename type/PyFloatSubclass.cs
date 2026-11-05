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
    /// already had this shape (PyIntSubclass, PyStrSubclass); without it a "float subclass" was
    /// an ordinary object with no value at all.
    /// </summary>
    public class PyFloatSubclass : PyObject, IDictBackedInstance, IHeapInstance
    {
        // CPython float_subtype_new: ob_fval copied from the converted temporary.
        private readonly PyFloat _floatValue;

        // ob_type - the subclass itself.
        private PyClass _class;

        public void ChangeClass(PyClass target) => _class = target;

        // float subclasses get an instance __dict__ (tp_alloc with the subclass's basicsize).
        public Dictionary<string, PyObject> InstanceDict { get; }

        public PyFloatSubclass(PyClass cls, double value)
        {
            _class = cls;
            _floatValue = new PyFloat(value);
            InstanceDict = new Dictionary<string, PyObject>();
        }

        public PyFloatSubclass(PyClass cls, PyFloat floatValue)
        {
            _class = cls;
            _floatValue = floatValue;
            InstanceDict = new Dictionary<string, PyObject>();
        }

        public PyFloat GetFloatValue() => _floatValue;
        public double Value => _floatValue.Value;

        public override PyType GetPyType() => _class;
        public override string GetTypeName() => _class.Name;

        #region Arithmetic - float semantics, result reported as plain float (CPython)

        // The unary slots dispatch on the CLR virtual, so a written __neg__ is read here.
        private PyObject Num0(string dunder, System.Func<PyObject> valueOp)
            => CallUserSpecialOrNull(dunder, System.Array.Empty<PyObject>()) ?? valueOp();

        // The binary slots are the base type's on the value. A dunder the class writes is
        // slot_nb_* on the type, which binary_op1 and SLOT1BINFULL (PyVM.BinaryOp1) call first.
        public override PyObject Add(PyObject other) => _floatValue.Add(other);
        public override PyObject Subtract(PyObject other) => _floatValue.Subtract(other);
        public override PyObject Multiply(PyObject other) => _floatValue.Multiply(other);
        public override PyObject Divide(PyObject other) => _floatValue.Divide(other);
        public override PyObject FloorDivide(PyObject other) => _floatValue.FloorDivide(other);
        public override PyObject Modulo(PyObject other) => _floatValue.Modulo(other);
        public override PyObject Power(PyObject other) => _floatValue.Power(other);
        public override PyObject Negative() => Num0("__neg__", () => _floatValue.Negative());
        public override PyObject Positive() => Num0("__pos__", () => _floatValue.Positive());

        #endregion

        #region Comparison and hashing

        public override PyObject RichCompare(PyObject other, CompareOp op) =>
            TryCompareSpecial(other, op, PyType.FloatType) ?? _floatValue.RichCompare(other, op);

        // CPython: tp_hash is float's, so FSub(2.0) and 2.0 collide in a dict.
        // _Py_HashDouble: a NaN hashes by the identity of the object asked.
        public override int ToHash() => double.IsNaN(Value)
            ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this) : _floatValue.ToHash();

        #endregion

        #region Conversion

        public override double ToFloat() => _floatValue.ToFloat();
        public override int ToInt() => _floatValue.ToInt();


        // float(FSub(2.5)) must return an exact float, not the subclass
        // (Objects/abstract.c PyNumber_Float: nb_float then PyFloat_Check).
        /// <summary>
        /// Measured: float(FO) answers through a user __float__ (9.5), and keeps the payload when
        /// the class declares none (2.7). Validation text is CPython's, which names the type.
        /// </summary>
        public override PyFloat AsFloat()
        {
            var declared = CallUserSpecialOrNull("__float__", System.Array.Empty<PyObject>());
            if (declared != null)
                return PyFloat.CheckFloatResult(this, declared);
            return new PyFloat(_floatValue.Value);
        }

        /// <summary>
        /// Measured: int(FO) honours a user __int__ (77); with none declared, float's truncation
        /// applies (2).
        /// </summary>
        public override PyInt AsInt()
        {
            var declared = CallUserSpecialOrNull("__int__", System.Array.Empty<PyObject>());
            if (declared != null)
                return declared as PyInt ?? throw PyTypeError.Create(
                    $"__int__ returned non-int (type {declared.GetTypeName()})");
            return _floatValue.AsInt();
        }

        /// <summary>
        /// CPython reads nb_divmod through the subclass, so a user __divmod__ wins and anything
        /// else falls back to float's arithmetic on the payload. This virtual was not overridden at
        /// all before, so divmod() never reached a dunder for a float subclass.
        /// </summary>
        public override PyObject DivMod(PyObject other)
            => CallUserSpecialOrNull("__divmod__", new[] { other })
               ?? PyFloat.DivModValues(_floatValue, other);

        protected internal override bool Bool() => _floatValue.IsTrue();

        protected internal override PyString Repr() => (PyString)_floatValue.Repr();

        #endregion

        #region Attribute access

        // Do NOT delegate to _floatValue.GetAttribute(): that instance has the plain
        // [float, object] MRO. base.GetAttribute() walks GetPyType(), i.e. the subclass MRO, and
        // the IInstanceDictAccessor seam above supplies the instance __dict__ layer.
        // The class's tp_getattro (SubclassInstanceSupport.GetAttribute); the payload type's own
        // attributes come last. An enum member reads _value_ before the class sets it.
        public override PyObject GetAttribute(string name)
        {
            if (name == "_value_" && !InstanceDict.ContainsKey("_value_"))
                return _floatValue;
            return SubclassInstanceSupport.GetAttribute(this, _class, name);
        }

        internal override PyObject GenericGetAttr(string name) =>
            SubclassInstanceSupport.GenericGetAttribute(this, _class, InstanceDict, name, base.GetAttribute);

        public override void SetAttribute(string name, PyObject value)
        {
            SubclassInstanceSupport.SetAttribute(this, (PyClass)GetPyType(), InstanceDict, name, value);
        }

        #endregion
    }
}
