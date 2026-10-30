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
    public class PyFloatSubclass : PyObject, IInstanceDictAccessor
    {
        // CPython float_subtype_new: ob_fval copied from the converted temporary.
        private readonly PyFloat _floatValue;

        // ob_type - the subclass itself.
        private readonly PyClass _class;

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

        /// <summary>
        /// CPython float_subtype_new: convert the argument first, then carry the value into a new
        /// instance of the subclass. Single implementation shared by PyClass.CreateInstance and
        /// PyType.CreateInstance (PyClass hides CreateInstance with `new`, so both entry points
        /// exist for a float-derived class depending on the static type of the receiver).
        /// </summary>
        public static PyObject FloatSubtypeNew(PyClass cls, PyObject[] args, PyDict kwargs)
        {
            // Measured against CPython: the subclass path reports the same texts as float itself.
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("float() takes no keyword arguments");
            if (args.Length > 1)
                throw PyTypeError.Create($"float expected at most 1 argument, got {args.Length}");
            return new PyFloatSubclass(cls, args.Length == 0 ? new PyFloat(0.0) : args[0].AsFloat());
        }

        public PyFloat GetFloatValue() => _floatValue;
        public double Value => _floatValue.Value;

        public override PyType GetPyType() => _class;
        public override string GetTypeName() => _class.Name;

        #region Arithmetic - float semantics, result reported as plain float (CPython)

        /// <summary>
        /// CPython resolves the arithmetic slot through the subclass, so a dunder the user declared
        /// wins and the base slot on the value only runs when they declared none (measured: a float
        /// subclass with __add__ answers 'user-add', an int subclass with __add__ answers
        /// 'user-iadd'; SharpPy answered 4.5 and 7). Only user frames are consulted here, because
        /// going through the full MRO would land on int's/float's own slot delegate, which reads the
        /// value back through this same CLR virtual and recurses.
        /// </summary>
        private PyObject Num(string dunder, PyObject other, System.Func<PyObject> valueOp)
            => CallUserSpecialOrNull(dunder, new[] { other }) ?? valueOp();

        private PyObject Num0(string dunder, System.Func<PyObject> valueOp)
            => CallUserSpecialOrNull(dunder, System.Array.Empty<PyObject>()) ?? valueOp();


        public override PyObject Add(PyObject other) => Num("__add__", other, () => _floatValue.Add(other));
        public override PyObject Subtract(PyObject other) => Num("__sub__", other, () => _floatValue.Subtract(other));
        public override PyObject Multiply(PyObject other) => Num("__mul__", other, () => _floatValue.Multiply(other));
        public override PyObject Divide(PyObject other) => Num("__truediv__", other, () => _floatValue.Divide(other));
        public override PyObject FloorDivide(PyObject other) => Num("__floordiv__", other, () => _floatValue.FloorDivide(other));
        public override PyObject Modulo(PyObject other) => Num("__mod__", other, () => _floatValue.Modulo(other));
        public override PyObject Power(PyObject other) => Num("__pow__", other, () => _floatValue.Power(other));
        public override PyObject Negative() => Num0("__neg__", () => _floatValue.Negative());
        public override PyObject Positive() => Num0("__pos__", () => _floatValue.Positive());

        #endregion

        #region Comparison and hashing

        public override PyObject RichCompare(PyObject other, CompareOp op) =>
            _floatValue.RichCompare(other, op);

        // CPython: tp_hash is float's, so FSub(2.0) and 2.0 collide in a dict.
        public override int ToHash() => _floatValue.ToHash();

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
                return declared as PyFloat ?? throw PyTypeError.Create(
                    $"{_class.Name}.__float__ returned non-float (type {declared.GetTypeName()})");
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

        public override bool PyBoolValue() => _floatValue.PyBoolValue();

        public override PyString AsString() => _floatValue.AsString();
        public override PyString ToRepr() => _floatValue.ToRepr();

        #endregion

        #region Attribute access

        // Do NOT delegate to _floatValue.GetAttribute(): that instance has the plain
        // [float, object] MRO. base.GetAttribute() walks GetPyType(), i.e. the subclass MRO, and
        // the IInstanceDictAccessor seam above supplies the instance __dict__ layer.
        public override PyObject GetAttribute(string name)
        {
            if (name == "_value_" && !InstanceDict.ContainsKey("_value_"))
                return _floatValue;
            return base.GetAttribute(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            InstanceDict[name] = value;
        }

        #endregion
    }
}
