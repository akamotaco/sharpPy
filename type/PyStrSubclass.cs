using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python str 서브클래스 인스턴스 구현
    /// CPython 3.12: Objects/unicodeobject.c:14733-14826 (unicode_subtype_new)
    ///
    /// CPython에서 str 서브클래스 인스턴스는:
    /// 1. PyUnicodeObject 구조체를 사용 (실제 문자열 데이터 포함)
    /// 2. ob_type이 서브클래스 타입을 가리킴
    /// 3. __dict__ 슬롯에 추가 속성 저장
    ///
    /// SharpPy에서는 PyString의 모든 동작을 위임하면서 추가 속성을 지원
    /// </summary>
    public class PyStrSubclass : PyString, IDictBackedInstance, IHeapInstance
    {
        // CPython: Objects/unicodeobject.c:14744 - self = type->tp_alloc(type, 0);
        // Objects/unicodeobject.c:14751-14764 - copy unicode data from original
        private readonly PyString _strValue;

        // CPython: 서브클래스 인스턴스는 __dict__ 슬롯을 가짐
        public Dictionary<string, PyObject> InstanceDict { get; }

        // CPython: ob_type 필드 - 서브클래스 타입
        private PyClass _class;

        public void ChangeClass(PyClass target) => _class = target;

        public PyStrSubclass(PyClass cls, string value) : base(value)
        {
            _class = cls;
            _strValue = new PyString(value);
            InstanceDict = new Dictionary<string, PyObject>();
            SetCustomType(cls);
        }

        public PyStrSubclass(PyClass cls, PyString strValue) : base(strValue.Value)
        {
            _class = cls;
            _strValue = strValue;
            InstanceDict = new Dictionary<string, PyObject>();
            SetCustomType(cls);
        }

        // CPython: 서브클래스 인스턴스의 타입은 서브클래스
        public override PyType GetPyType() => _class;
        public override string GetTypeName() => _class.Name;

        // CPython: str 서브클래스는 str의 모든 연산을 지원
        // Objects/unicodeobject.c의 문자열 연산들

        #region String Operations - Delegate to PyString

        // sq_concat, sq_repeat and nb_remainder of str on the value. A written __add__, __mul__ or
        // __mod__ is slot_nb_* on the type, which binary_op1 (PyVM.BinaryOp1) calls first.
        public override PyObject Add(PyObject other) => _strValue.Add(other);
        public override PyObject Multiply(PyObject other) => _strValue.Multiply(other);
        public override PyObject Modulo(PyObject other) => _strValue.Modulo(other);

        // CPython 3.12: str subclasses inherit the whole unicode protocol; these slots
        // were missing, so len()/indexing/slices/`in`/iteration failed on subclasses.
        protected internal override long Len() => _strValue.Len();

        public override PyObject GetItem(PyObject index) =>
            CallUserSpecialOrNull("__getitem__", new[] { index }) ?? _strValue.GetItem(index);

        public override PyList AsList() => new PyList(new List<PyObject>(PySet.IterableItems(this)));


        public override PyBool Contains(PyObject item)
        {
            // `x in s` is always a bool: CPython coerces whatever __contains__ returns.
            var declared = CallUserSpecialOrNull("__contains__", new[] { item });
            return declared == null ? _strValue.Contains(item) : PyBool.FromBool(declared.IsTrue());
        }


        /// <summary>
        /// CPython's tp_richcompare for a str subclass is the slot wrapper around the declared
        /// dunder: measured su == "z" is 'user-str-eq' where SharpPy answered False, because the
        /// inherited value comparison produced an answer before the class was consulted.
        /// NotImplemented still falls through to the payload comparison, as in CPython.
        /// </summary>
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            var dunder = op switch
            {
                CompareOp.EQ => "__eq__",
                CompareOp.NE => "__ne__",
                CompareOp.LT => "__lt__",
                CompareOp.LE => "__le__",
                CompareOp.GT => "__gt__",
                CompareOp.GE => "__ge__",
                _ => null,
            };
            if (dunder != null)
            {
                var declared = CallUserSpecialOrNull(dunder, new[] { other });
                if (declared != null && !ReferenceEquals(declared, PyNotImplemented.Instance))
                    return declared;
            }
            return _strValue.RichCompare(other, op);
        }

        #endregion

        #region Comparison Operations - Delegate to PyString

        protected override PyObject PyEquals(PyObject other)
        {
            // CPython: unicode_richcompare compares contents whenever both sides are
            // unicode instances (subclasses included). _strValue.Equals(other) was CLR
            // reference equality, so SS("x") == "x" answered False.
            if (other is PyStrSubclass otherSub)
                return PyBool.FromBool(string.Equals(_strValue.Value, otherSub._strValue.Value));
            if (other is PyString otherStr)
                return PyBool.FromBool(string.Equals(_strValue.Value, otherStr.Value));
            return PyNotImplemented.Instance;
        }

        protected override PyObject PyLess(PyObject other)
        {
            if (other is PyStrSubclass otherSub)
                other = otherSub._strValue;
            // Use string comparison operators
            var otherStr = other as PyString;
            if (otherStr == null)
                throw PyTypeError.Create($"'<' not supported between instances of 'str' and '{other.GetTypeName()}'");
            return PyString.CompareText(_strValue.Value, otherStr.Value) < 0 ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            if (other is PyStrSubclass otherSub)
                other = otherSub._strValue;
            var otherStr = other as PyString;
            if (otherStr == null)
                throw PyTypeError.Create($"'<=' not supported between instances of 'str' and '{other.GetTypeName()}'");
            return PyString.CompareText(_strValue.Value, otherStr.Value) <= 0 ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyGreater(PyObject other)
        {
            if (other is PyStrSubclass otherSub)
                other = otherSub._strValue;
            var otherStr = other as PyString;
            if (otherStr == null)
                throw PyTypeError.Create($"'>' not supported between instances of 'str' and '{other.GetTypeName()}'");
            return PyString.CompareText(_strValue.Value, otherStr.Value) > 0 ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            if (other is PyStrSubclass otherSub)
                other = otherSub._strValue;
            var otherStr = other as PyString;
            if (otherStr == null)
                throw PyTypeError.Create($"'>=' not supported between instances of 'str' and '{other.GetTypeName()}'");
            return PyString.CompareText(_strValue.Value, otherStr.Value) >= 0 ? PyBool.True : PyBool.False;
        }

        #endregion

        #region Type Conversion - Delegate to PyString

        protected internal override PyString Repr() => (PyString)_strValue.Repr();
        public override int ToHash() => _strValue.ToHash();
        protected internal override bool Bool() => _strValue.Bool();

        #endregion

        #region Attribute Access

        // The class's tp_getattro (SubclassInstanceSupport.GetAttribute); the payload type's own
        // attributes come last. An enum member reads _value_ before the class sets it.
        public override PyObject GetAttribute(string name)
        {
            if (name == "_value_" && !InstanceDict.ContainsKey("_value_"))
                return _strValue;
            return SubclassInstanceSupport.GetAttribute(this, _class, name);
        }

        internal override PyObject GenericGetAttr(string name) =>
            SubclassInstanceSupport.GenericGetAttribute(this, _class, InstanceDict, name, base.GetAttribute);

        public override void SetAttribute(string name, PyObject value)
        {
            SubclassInstanceSupport.SetAttribute(this, (PyClass)GetPyType(), InstanceDict, name, value);
        }

        #endregion

        #region Get Underlying String Value

        /// <summary>
        /// 내부 PyString 값 반환 (enum._value_ 접근 등에 사용)
        /// </summary>
        public PyString GetStrValue() => _strValue;

        /// <summary>
        /// 내부 문자열 값 반환
        /// </summary>
        public string GetStringValue() => _strValue.Value;

        #endregion
    }
}
