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
        private PyObject Num0(string dunder, System.Func<PyObject> valueOp)
            => CallUserSpecialOrNull(dunder, System.Array.Empty<PyObject>()) ?? valueOp();

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
        /// Measured: iter(Q(1)) is a list_iterator when the class declares __iter__, and
        /// TypeError: 'Q' object is not iterable when it does not - the message names the subclass,
        /// which the payload's own error could not do. Lookup only, then InvokeIteratorMethod: the
        /// call-and-return helper would hand an iterator back to be called again.
        /// </summary>
        public override PyObject GetIterator()
        {
            var declared = LookupUserSpecial("__iter__");
            if (declared != null)
                return InvokeIteratorMethod(declared);
            throw PyTypeError.Create($"'{GetTypeName()}' object is not iterable");
        }

        /// <summary>
        /// CPython reads nb_divmod through the subclass: a user __divmod__ wins, otherwise int's
        /// arithmetic on the payload. Not overridden before, so divmod() never reached a dunder for
        /// an int subclass.
        /// </summary>
        public override PyObject DivMod(PyObject other)
        {
            var declared = CallUserSpecialOrNull("__divmod__", new[] { other });
            if (declared != null)
                return declared;
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

        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            var result = TryCompareSpecial(other, op, PyType.IntType);
            if (result != null) return result;
            // Use the full payload and dispatch the operation only once.
            if (other is PyIntSubclass otherSubclass)
                other = otherSubclass._intValue;
            return _intValue.RichCompare(other, op);
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

        // nb_invert: a unary slot, dispatched on the CLR virtual, so a written __invert__ is read here.
        public override PyObject BitwiseNot() => Num0("__invert__", () => _intValue.BitwiseNot());

        #endregion

        #region Unary Operations - Delegate to PyInt

        public override PyObject Negative()
        {
            return Num0("__neg__", () => _intValue.Negative());
        }

        public override PyObject Positive()
        {
            return Num0("__pos__", () => _intValue.Positive());
        }

        #endregion

        #region Type Conversion - Delegate to PyInt

        public override int ToInt() => _intValue.ToInt();
        public override double ToFloat() => _intValue.ToFloat();
        public override bool PyBoolValue() => _intValue.PyBoolValue();
        public override PyString AsString() => SubclassInstanceSupport.Str(this, ToRepr);
        public override PyString ToRepr() => SubclassInstanceSupport.Repr(this, _intValue.ToRepr);

        #endregion

        #region Hash - Delegate to PyInt

        // Native-width Python hash for observable hash() and OSError errnomap.
        // ToHash remains the existing CLR dictionary hash until numeric key
        // hashing and equality can be changed together.
        internal override long GetPythonHash()
        {
            var method = LookupSpecialMethodUncached("__hash__");
            PyObject result;
            if (method is PyWrapperDescriptor wrapper && method.GetType() == typeof(PyWrapperDescriptor))
            {
                // Exact builtin slot wrappers bind without user code. Pass the
                // receiver directly, avoiding a temporary bound wrapper object.
                result = wrapper._implementation(this, Array.Empty<PyObject>(), null);
            }
            else
            {
                // slot_tp_hash replaces lookup/binding errors, but not call errors.
                try { if (method != null) method = BindSpecialMethod(method); }
                catch (PythonException) { method = null; }
                if (method == null || method == PyNone.Instance)
                    throw PyTypeError.Create($"unhashable type: '{GetTypeName()}'");
                result = method.Call(Array.Empty<PyObject>(), null);
            }
            var hashValue = result as PyInt ?? (result as PyIntSubclass)?.GetIntValue();
            System.Numerics.BigInteger value;
            if (hashValue != null) value = hashValue.Value;
            else if (result is PyBool boolean) value = boolean.Value ? 1 : 0;
            else throw PyTypeError.Create("__hash__ method should return an integer");
            // Preserve signed native-width user results; reduce only on overflow.
            long hash = value < nint.MinValue || value > nint.MaxValue
                ? PyInt.PythonHash(value) : (long)value;
            return hash == -1 ? -2 : hash;
        }

        public override int ToHash() => _intValue.ToHash();

        #endregion

        #region Attribute Access

        // The class's tp_getattro (SubclassInstanceSupport.GetAttribute); the payload type's own
        // attributes come last. An enum member reads _value_ before the class sets it.
        public override PyObject GetAttribute(string name)
        {
            if (name == "_value_" && !InstanceDict.ContainsKey("_value_"))
                return _intValue;
            return SubclassInstanceSupport.GetAttribute(this, _class, name);
        }

        internal override PyObject GenericGetAttr(string name) =>
            SubclassInstanceSupport.GenericGetAttribute(this, _class, InstanceDict, name, base.GetAttribute);

        public override void SetAttribute(string name, PyObject value)
        {
            SubclassInstanceSupport.SetAttribute(this, (PyClass)GetPyType(), InstanceDict, name, value);
        }

        #endregion

        #region Get Underlying Int Value

        /// <summary>
        /// 내부 PyInt 값 반환 (enum._value_ 접근 등에 사용)
        /// </summary>
        public PyInt GetIntValue() => _intValue;

        // slot_nb_int and slot_nb_float: a declared method wins, the value otherwise.
        public override PyInt AsInt()
        {
            if (SubclassInstanceSupport.FirstClassDictEntry(_class, "__int__") == null) return _intValue;
            return SubclassInstanceSupport.Int(this);
        }

        public override PyFloat AsFloat()
        {
            if (SubclassInstanceSupport.FirstClassDictEntry(_class, "__float__") == null) return _intValue.AsFloat();
            return SubclassInstanceSupport.Float(this);
        }

        #endregion
    }
}
