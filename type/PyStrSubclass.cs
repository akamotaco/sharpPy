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
    public class PyStrSubclass : PyObject, IInstanceDictAccessor
    {
        // CPython: Objects/unicodeobject.c:14744 - self = type->tp_alloc(type, 0);
        // Objects/unicodeobject.c:14751-14764 - copy unicode data from original
        private readonly PyString _strValue;

        // CPython: 서브클래스 인스턴스는 __dict__ 슬롯을 가짐
        public Dictionary<string, PyObject> InstanceDict { get; }

        // CPython: ob_type 필드 - 서브클래스 타입
        private readonly PyClass _class;

        public PyStrSubclass(PyClass cls, string value)
        {
            _class = cls;
            _strValue = new PyString(value);
            InstanceDict = new Dictionary<string, PyObject>();
        }

        public PyStrSubclass(PyClass cls, PyString strValue)
        {
            _class = cls;
            _strValue = strValue;
            InstanceDict = new Dictionary<string, PyObject>();
        }

        // CPython: 서브클래스 인스턴스의 타입은 서브클래스
        public override PyType GetPyType() => _class;
        public override string GetTypeName() => _class.Name;

        // CPython: str 서브클래스는 str의 모든 연산을 지원
        // Objects/unicodeobject.c의 문자열 연산들

        #region String Operations - Delegate to PyString

        public override PyObject Add(PyObject other)
        {
            // CPython: str 연산 결과는 base str 타입 반환
            return _strValue.Add(other);
        }

        public override PyObject Multiply(PyObject other)
        {
            return _strValue.Multiply(other);
        }

        #endregion

        #region Comparison Operations - Delegate to PyString

        protected override PyObject PyEquals(PyObject other)
        {
            if (other is PyStrSubclass otherSub)
                other = otherSub._strValue;
            // Delegate to PyString's Equals which calls PyEquals internally
            return _strValue.Equals(other) ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyLess(PyObject other)
        {
            if (other is PyStrSubclass otherSub)
                other = otherSub._strValue;
            // Use string comparison operators
            var otherStr = other as PyString;
            if (otherStr == null)
                throw PyTypeError.Create($"'<' not supported between instances of 'str' and '{other.GetTypeName()}'");
            return string.CompareOrdinal(_strValue.Value, otherStr.Value) < 0 ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            if (other is PyStrSubclass otherSub)
                other = otherSub._strValue;
            var otherStr = other as PyString;
            if (otherStr == null)
                throw PyTypeError.Create($"'<=' not supported between instances of 'str' and '{other.GetTypeName()}'");
            return string.CompareOrdinal(_strValue.Value, otherStr.Value) <= 0 ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyGreater(PyObject other)
        {
            if (other is PyStrSubclass otherSub)
                other = otherSub._strValue;
            var otherStr = other as PyString;
            if (otherStr == null)
                throw PyTypeError.Create($"'>' not supported between instances of 'str' and '{other.GetTypeName()}'");
            return string.CompareOrdinal(_strValue.Value, otherStr.Value) > 0 ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            if (other is PyStrSubclass otherSub)
                other = otherSub._strValue;
            var otherStr = other as PyString;
            if (otherStr == null)
                throw PyTypeError.Create($"'>=' not supported between instances of 'str' and '{other.GetTypeName()}'");
            return string.CompareOrdinal(_strValue.Value, otherStr.Value) >= 0 ? PyBool.True : PyBool.False;
        }

        #endregion

        #region Type Conversion - Delegate to PyString

        public override PyString ToStr() => _strValue;
        public override PyString ToRepr() => new PyString($"'{_strValue.Value}'");
        public override int ToHash() => _strValue.ToHash();
        public override bool PyBoolValue() => _strValue.PyBoolValue();

        #endregion

        #region Attribute Access

        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12: Objects/object.c:1227-1304 (PyObject_GenericGetAttr)
            //
            // IMPORTANT: Do NOT delegate to _strValue.GetAttribute()!
            // _strValue is a plain PyString without custom type, so its MRO is just [str, object].
            //
            // We delegate to base.GetAttribute() which calls GenericGetAttribute().
            // GenericGetAttribute() will:
            // 1. Use GetPyType() to get the correct MRO (we override to return _class)
            // 2. Check for data descriptors in MRO
            // 3. Check InstanceDict (PyObject.GenericGetAttribute now handles PyStrSubclass)
            // 4. Check for non-data descriptors in MRO
            //
            // This ensures proper attribute lookup order per CPython

            // Special case: _value_ attribute for enum members
            // If not set in InstanceDict, return the underlying string value
            // This handles enum members during initialization before _value_ is set
            if (name == "_value_" && !InstanceDict.ContainsKey("_value_"))
            {
                return _strValue;
            }

            // Delegate everything to base class for proper MRO-based lookup
            return base.GetAttribute(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            // CPython: 인스턴스 __dict__에 저장
            // Objects/unicodeobject.c:14744 - type->tp_alloc allocates space for __dict__
            InstanceDict[name] = value;
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
