using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python int 서브클래스 인스턴스 구현
    /// CPython 3.12: Objects/longobject.c:5648-5677 (long_subtype_new)
    ///
    /// CPython에서 int 서브클래스 인스턴스는:
    /// 1. PyLongObject 구조체를 사용 (실제 int 값 포함)
    /// 2. ob_type이 서브클래스 타입을 가리킴
    /// 3. __dict__ 슬롯에 추가 속성 저장
    ///
    /// SharpPy에서는 PyInt의 모든 동작을 위임하면서 추가 속성을 지원
    /// </summary>
    public class PyIntSubclass : PyObject, IDictBackedInstance, IHeapInstance
    {
        // CPython: Objects/longobject.c:5671-5674
        // newobj->long_value.lv_tag = tmp->long_value.lv_tag;
        // for (i = 0; i < n; i++) {
        //     newobj->long_value.ob_digit[i] = tmp->long_value.ob_digit[i];
        // }
        private readonly PyInt _intValue;

        // CPython: 서브클래스 인스턴스는 __dict__ 슬롯을 가짐
        public Dictionary<string, PyObject> InstanceDict { get; }

        // CPython: ob_type 필드 - 서브클래스 타입
        private PyClass _class;

        public void ChangeClass(PyClass target) => _class = target;

        public PyIntSubclass(PyClass cls, long value)
        {
            _class = cls;
            _intValue = new PyInt(value);
            InstanceDict = new Dictionary<string, PyObject>();
        }

        public PyIntSubclass(PyClass cls, PyInt intValue)
        {
            _class = cls;
            _intValue = intValue;
            InstanceDict = new Dictionary<string, PyObject>();
        }

        // CPython: 서브클래스 인스턴스의 타입은 서브클래스
        public override PyType GetPyType() => _class;
        public override string GetTypeName() => _class.Name;

        // CPython: int 서브클래스는 int의 모든 연산을 지원
        // Objects/longobject.c의 산술 연산들은 타입 체크 없이 long_value를 직접 사용

        #region Arithmetic Operations - Delegate to PyInt

        // The unary slots dispatch on the CLR virtual, so a written __neg__ is read here.
        // The binary slots are the base type's on the value. A dunder the class writes is
        // slot_nb_* on the type, which binary_op1 and SLOT1BINFULL (PyVM.BinaryOp1) call first.
        public override PyObject Add(PyObject other)
        {
            // CPython: int 연산 결과는 base int 타입 반환
            return _intValue.Add(other);
        }

        public override PyObject Subtract(PyObject other)
        {
            return _intValue.Subtract(other);
        }

        public override PyObject Multiply(PyObject other)
        {
            return _intValue.Multiply(other);
        }

        public override PyObject Divide(PyObject other)
        {
            return _intValue.Divide(other);
        }

        public override PyObject FloorDivide(PyObject other)
        {
            return _intValue.FloorDivide(other);
        }

        public override PyObject Modulo(PyObject other)
        {
            return _intValue.Modulo(other);
        }

        /// <summary>
        /// CPython reads nb_divmod through the subclass: a user __divmod__ wins, otherwise int's
        /// arithmetic on the payload. Not overridden before, so divmod() never reached a dunder for
        /// an int subclass.
        /// </summary>
        public override PyObject DivMod(PyObject other)
        {
            // long_divmod on the value; a written __divmod__ is the class's slot.
            var self = GetIntValue();
            var quotient = self.FloorDivide(other);
            if (ReferenceEquals(quotient, PyNotImplemented.Instance))
                return quotient;
            return new PyTuple(quotient, self.Modulo(other));
        }

        public override PyObject Power(PyObject other)
        {
            return _intValue.Power(other);
        }

        #endregion

        #region Comparison Operations - Delegate to PyInt

        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            // Use the full payload and dispatch the operation only once.
            if (other is PyIntSubclass otherSubclass)
                other = otherSubclass._intValue;
            return _intValue.TpRichCompare(other, op);
        }

        #endregion

        #region Bitwise Operations - Use Python special methods

        // nb_and, nb_or, nb_xor, nb_lshift, nb_rshift: int's on the value; a written __or__ (Flag)
        // is reached through binary_op1 first.
        public override PyObject BitwiseAnd(PyObject other) => _intValue.BitwiseAnd(other);
        public override PyObject BitwiseOr(PyObject other) => _intValue.BitwiseOr(other);
        public override PyObject BitwiseXor(PyObject other) => _intValue.BitwiseXor(other);
        public override PyObject LeftShift(PyObject other) => _intValue.LeftShift(other);
        public override PyObject RightShift(PyObject other) => _intValue.RightShift(other);

        // nb_invert of the value; a written __invert__ is the class's slot.
        protected internal override PyObject NbInvert() => _intValue.NbInvert();

        #endregion

        #region Unary Operations - Delegate to PyInt

        protected internal override PyObject NbNegative()
        {
            return _intValue.NbNegative();
        }

        protected internal override PyObject NbPositive()
        {
            return _intValue.NbPositive();
        }

        #endregion

        #region Type Conversion - Delegate to PyInt

        public override int ToInt() => _intValue.ToInt();
        public override double ToFloat() => _intValue.ToFloat();
        protected internal override bool Bool() => _intValue.IsTrue();
        protected internal override PyString Repr() => (PyString)_intValue.Repr();

        #endregion

        #region Hash - Delegate to PyInt

        // long_hash of the value: the inherited int slot.
        protected internal override long Hash() => _intValue.Hash();

        public override int ToHash() => _intValue.ToHash();

        #endregion

        #region Attribute Access

        // The class's tp_getattro (SubclassInstanceSupport.GetAttribute); the payload type's own
        // attributes come last. An enum member reads _value_ before the class sets it.
        internal override PyObject GenericGetAttr(string name) =>
            name == "_value_" && !InstanceDict.ContainsKey("_value_")
                ? _intValue
                : base.GenericGetAttr(name);


        #endregion

        #region Get Underlying Int Value

        /// <summary>
        /// 내부 PyInt 값 반환 (enum._value_ 접근 등에 사용)
        /// </summary>
        public PyInt GetIntValue() => _intValue;

        // The inherited long_long and long_float, on the value.
        protected internal override PyObject NbInt() => _intValue;
        protected internal override PyObject NbFloat() => _intValue.NbFloat();

        #endregion
    }
}
