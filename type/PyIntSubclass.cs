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
    /// An int whose ob_type is the subclass: _customType, as for every builtin a Python class
    /// derives from, so the exact-int fast paths (GetType and HasCustomType) pass it by.
    /// </summary>
    public class PyIntSubclass : PyInt
    {
        // CPython: Objects/longobject.c:5671-5674
        // newobj->long_value.lv_tag = tmp->long_value.lv_tag;
        // for (i = 0; i < n; i++) {
        //     newobj->long_value.ob_digit[i] = tmp->long_value.ob_digit[i];
        // }
        private readonly PyInt _intValue;


        public PyIntSubclass(PyClass cls, long value) : base(value, cls)
        {
            _intValue = new PyInt(value);
        }

        public PyIntSubclass(PyClass cls, PyInt intValue) : base(intValue.Value, cls)
        {
            _intValue = intValue;
        }

        // An int subclass instance is an int object whose type is the subclass (long_subtype_new):
        // every slot is int's, inherited. Two return int itself, not the subclass: long_long for
        // __int__ and unary +.
        protected internal override PyObject NbPositive() => _intValue;

        #region Attribute Access

        // The class's tp_getattro (SubclassInstanceSupport.GetAttribute); the payload type's own
        // attributes come last. An enum member reads _value_ before the class sets it.
        internal override PyObject GenericGetAttr(string name) =>
            name == "_value_" && !HeapDict.ContainsKey("_value_")
                ? _intValue
                : base.GenericGetAttr(name);


        #endregion

        #region Get Underlying Int Value

        /// <summary>
        /// 내부 PyInt 값 반환 (enum._value_ 접근 등에 사용)
        /// </summary>
        public PyInt GetIntValue() => _intValue;

        // long_long: the exact int of the value.
        protected internal override PyObject NbInt() => _intValue;

        #endregion
    }
}
