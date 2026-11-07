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
        // long_subtype_new copies the digits of the exact int it made first (Objects/longobject.c:5671).
        public PyIntSubclass(PyClass cls, long value) : base(value, cls) { }

        public PyIntSubclass(PyClass cls, PyInt intValue) : base(intValue.Value, cls) { }

        // An int subclass instance is an int object whose type is the subclass (long_subtype_new):
        // every slot is int's, inherited. Two return int itself, not the subclass: long_long for
        // __int__ and unary +, which is _PyLong_Copy - a new exact int of the value.
        protected internal override PyObject NbPositive() => GetIntValue();

        #region Attribute Access



        #endregion

        #region Get Underlying Int Value

        /// <summary>_PyLong_Copy: the exact int of the value.</summary>
        public PyInt GetIntValue() => PyInt.From(Value);

        // long_long: the exact int of the value.
        protected internal override PyObject NbInt() => GetIntValue();

        #endregion
    }
}
