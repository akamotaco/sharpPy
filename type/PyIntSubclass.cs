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
    public class PyIntSubclass : PyObject, IInstanceDictAccessor
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
        private readonly PyClass _class;

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

        public override PyObject Power(PyObject other)
        {
            return _intValue.Power(other);
        }

        #endregion

        #region Comparison Operations - Delegate to PyInt

        protected override PyObject PyEquals(PyObject other)
        {
            // CPython 3.12: Objects/longobject.c:3150-3173 (long_richcompare)
            // IntEnum (int subclass) should compare equal to int with same value
            if (other is PyIntSubclass otherSub)
                other = otherSub._intValue;
            // Delegate to PyInt's RichCompare for proper value comparison
            return _intValue.RichCompare(other, CompareOp.EQ);
        }

        protected override PyObject PyLess(PyObject other)
        {
            if (other is PyIntSubclass otherSub)
                other = otherSub._intValue;

            // CPython 3.12: Objects/longobject.c - int subclass comparison
            // Extract int value for comparison
            long thisValue = (long)_intValue.Value;
            long otherValue;

            if (other is PyInt otherInt)
                otherValue = (long)otherInt.Value;
            else if (other is PyFloat otherFloat)
                return thisValue < otherFloat.Value ? PyBool.True : PyBool.False;
            else if (other is PyBool otherBool)
                otherValue = otherBool.Value ? 1 : 0;
            else
                throw PyTypeError.Create($"'<' not supported between instances of 'int' and '{other.GetTypeName()}'");

            return thisValue < otherValue ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            if (other is PyIntSubclass otherSub)
                other = otherSub._intValue;

            // CPython 3.12: Objects/longobject.c - int subclass comparison
            long thisValue = (long)_intValue.Value;
            long otherValue;

            if (other is PyInt otherInt)
                otherValue = (long)otherInt.Value;
            else if (other is PyFloat otherFloat)
                return thisValue <= otherFloat.Value ? PyBool.True : PyBool.False;
            else if (other is PyBool otherBool)
                otherValue = otherBool.Value ? 1 : 0;
            else
                throw PyTypeError.Create($"'<=' not supported between instances of 'int' and '{other.GetTypeName()}'");

            return thisValue <= otherValue ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyGreater(PyObject other)
        {
            if (other is PyIntSubclass otherSub)
                other = otherSub._intValue;

            // CPython 3.12: Objects/longobject.c - int subclass comparison
            long thisValue = (long)_intValue.Value;
            long otherValue;

            if (other is PyInt otherInt)
                otherValue = (long)otherInt.Value;
            else if (other is PyFloat otherFloat)
                return thisValue > otherFloat.Value ? PyBool.True : PyBool.False;
            else if (other is PyBool otherBool)
                otherValue = otherBool.Value ? 1 : 0;
            else
                throw PyTypeError.Create($"'>' not supported between instances of 'int' and '{other.GetTypeName()}'");

            return thisValue > otherValue ? PyBool.True : PyBool.False;
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            if (other is PyIntSubclass otherSub)
                other = otherSub._intValue;

            // CPython 3.12: Objects/longobject.c - int subclass comparison
            long thisValue = (long)_intValue.Value;
            long otherValue;

            if (other is PyInt otherInt)
                otherValue = (long)otherInt.Value;
            else if (other is PyFloat otherFloat)
                return thisValue >= otherFloat.Value ? PyBool.True : PyBool.False;
            else if (other is PyBool otherBool)
                otherValue = otherBool.Value ? 1 : 0;
            else
                throw PyTypeError.Create($"'>=' not supported between instances of 'int' and '{other.GetTypeName()}'");

            return thisValue >= otherValue ? PyBool.True : PyBool.False;
        }

        #endregion

        #region Bitwise Operations - Use Python special methods

        // CPython 3.12: IntFlag bitwise operations are handled by Python-level __or__, __and__, etc.
        // Lib/enum.py:1550-1596 - Flag.__or__, Flag.__and__, Flag.__xor__, Flag.__invert__
        //
        // These methods call self.__class__(value | other_value) to create a new instance
        // of the same type with the combined flags.
        //
        // IMPORTANT: We must check for __or__ etc. in the MRO first, before falling back
        // to the default int bitwise operations. This matches CPython's special method lookup.

        public override PyObject BitwiseAnd(PyObject other)
        {
            // CPython: Objects/typeobject.c:8110-8130 - slot_nb_and calls __and__
            // Check if class defines __and__ method
            try
            {
                var andMethod = LookupSpecialMethod("__and__");
                if (andMethod != null)
                {
                    // Call the Python __and__ method
                    return andMethod.Call(new PyObject[] { this, other }, null);
                }
            }
            catch
            {
                // If special method lookup fails, fall through to default
            }

            // Fallback: use int's bitwise and
            return _intValue.BitwiseAnd(other);
        }

        public override PyObject BitwiseOr(PyObject other)
        {
            // CPython: Objects/typeobject.c:8110-8130 - slot_nb_or calls __or__
            // Check if class defines __or__ method (e.g., Flag.__or__)
            try
            {
                var orMethod = LookupSpecialMethod("__or__");
                if (orMethod != null)
                {
                    return orMethod.Call(new PyObject[] { this, other }, null);
                }
            }
            catch
            {
                // Fallback
            }

            return _intValue.BitwiseOr(other);
        }

        public override PyObject BitwiseXor(PyObject other)
        {
            // CPython: Objects/typeobject.c:8110-8130 - slot_nb_xor calls __xor__
            try
            {
                var xorMethod = LookupSpecialMethod("__xor__");
                if (xorMethod != null)
                {
                    return xorMethod.Call(new PyObject[] { this, other }, null);
                }
            }
            catch
            {
                // Fallback
            }

            return _intValue.BitwiseXor(other);
        }

        public override PyObject BitwiseNot()
        {
            // CPython: Objects/typeobject.c:8110-8130 - slot_nb_invert calls __invert__
            try
            {
                var invertMethod = LookupSpecialMethod("__invert__");
                if (invertMethod != null)
                {
                    return invertMethod.Call(new PyObject[] { this }, null);
                }
            }
            catch
            {
                // Fallback
            }

            return _intValue.BitwiseNot();
        }

        public override PyObject LeftShift(PyObject other)
        {
            try
            {
                var lshiftMethod = LookupSpecialMethod("__lshift__");
                if (lshiftMethod != null)
                {
                    return lshiftMethod.Call(new PyObject[] { this, other }, null);
                }
            }
            catch
            {
                // Fallback
            }

            return _intValue.LeftShift(other);
        }

        public override PyObject RightShift(PyObject other)
        {
            try
            {
                var rshiftMethod = LookupSpecialMethod("__rshift__");
                if (rshiftMethod != null)
                {
                    return rshiftMethod.Call(new PyObject[] { this, other }, null);
                }
            }
            catch
            {
                // Fallback
            }

            return _intValue.RightShift(other);
        }

        #endregion

        #region Unary Operations - Delegate to PyInt

        public override PyObject Negative()
        {
            return _intValue.Negative();
        }

        public override PyObject Positive()
        {
            return _intValue.Positive();
        }

        #endregion

        #region Type Conversion - Delegate to PyInt

        public override int ToInt() => _intValue.ToInt();
        public override double ToFloat() => _intValue.ToFloat();
        public override double ToDouble() => _intValue.ToDouble();
        public override bool PyBoolValue() => _intValue.PyBoolValue();
        public override PyString ToStr() => _intValue.ToStr();
        public override PyString ToRepr()
        {
            // CPython: 서브클래스는 기본 repr 사용하지만 타입명은 서브클래스
            return new PyString($"{_intValue.ToInt()}");
        }

        #endregion

        #region Hash - Delegate to PyInt

        public override int ToHash() => _intValue.ToHash();

        #endregion

        #region Attribute Access

        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12: Objects/object.c:1227-1304 (PyObject_GenericGetAttr)
            //
            // IMPORTANT: Do NOT delegate to _intValue.GetAttribute()!
            // _intValue is a plain PyInt without custom type, so its MRO is just [int, object].
            //
            // We delegate to base.GetAttribute() which calls GenericGetAttribute().
            // GenericGetAttribute() will:
            // 1. Use GetPyType() to get the correct MRO (we override to return _class)
            // 2. Check for data descriptors in MRO
            // 3. Check InstanceDict (PyObject.GenericGetAttribute now handles PyIntSubclass)
            // 4. Check for non-data descriptors in MRO
            //
            // This ensures proper attribute lookup order per CPython

            // Special case: _value_ attribute for enum members
            // If not set in InstanceDict, return the underlying int value
            // This handles enum members during initialization before _value_ is set
            if (name == "_value_" && !InstanceDict.ContainsKey("_value_"))
            {
                return _intValue;
            }

            // Delegate everything to base class for proper MRO-based lookup
            return base.GetAttribute(name);
        }

        public override void SetAttribute(string name, PyObject value)
        {
            // CPython: 인스턴스 __dict__에 저장
            // Objects/longobject.c:5665 - type->tp_alloc allocates space for __dict__
            InstanceDict[name] = value;
        }

        #endregion

        #region Get Underlying Int Value

        /// <summary>
        /// 내부 PyInt 값 반환 (enum._value_ 접근 등에 사용)
        /// </summary>
        public PyInt GetIntValue() => _intValue;

        #endregion
    }
}
