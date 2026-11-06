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
        // unicode_subtype_new copies the data of the exact str it made first; that str stays as
        // the exact value str() and the enum's _value_ give back.
        private readonly PyString _strValue;

        // CPython: 서브클래스 인스턴스는 __dict__ 슬롯을 가짐
        public Dictionary<string, PyObject> InstanceDict { get; }

        public void ChangeClass(PyClass target) => SetCustomType(target);

        public PyStrSubclass(PyClass cls, string value) : this(cls, new PyString(value)) { }

        public PyStrSubclass(PyClass cls, PyString strValue) : base(strValue.Value)
        {
            _strValue = strValue;
            InstanceDict = new Dictionary<string, PyObject>();
            SetCustomType(cls);
        }

        // Every slot is str's, inherited: a str whose ob_type is the subclass.

        // An enum member reads _value_ before the class sets it.
        internal override PyObject GenericGetAttr(string name) =>
            name == "_value_" && !InstanceDict.ContainsKey("_value_")
                ? _strValue
                : base.GenericGetAttr(name);

        /// <summary>The exact str of the value.</summary>
        public PyString GetStrValue() => _strValue;

        public string GetStringValue() => Value;
    }
}
