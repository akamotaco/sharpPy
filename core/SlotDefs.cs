namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 슬롯 정의 테이블
    /// CPython 3.12: Objects/typeobject.c:8547-8924 (slotdefs)
    ///
    /// 슬롯 카테고리:
    /// - tp_*: 타입 객체 슬롯 (tp_repr, tp_hash, tp_call, etc.)
    /// - sq_*: 시퀀스 프로토콜 슬롯 (sq_length, sq_item, etc.)
    /// - mp_*: 매핑 프로토콜 슬롯 (mp_length, mp_subscript, etc.)
    /// - nb_*: 숫자 프로토콜 슬롯 (nb_add, nb_subtract, etc.)
    /// </summary>
    public static class SlotDefs
    {
        /// <summary>
        /// 상속 가능한 특수 메서드 슬롯들
        /// CPython 3.12: Objects/typeobject.c:6800-6996 (inherit_slots)
        /// </summary>
        public static readonly string[] InheritableSlots = new[]
        {
            // Iterator protocol - CPython: tp_iter, tp_iternext
            // Objects/typeobject.c:6956-6957
            "__iter__",
            "__next__",

            // Sequence protocol - CPython: sq_length, sq_item, sq_ass_item, sq_contains
            // Objects/typeobject.c:6884-6889
            "__len__",
            "__getitem__",
            "__setitem__",
            "__delitem__",
            "__contains__",

            // Callable protocol - CPython: tp_call
            // Objects/typeobject.c:6936
            "__call__",

            // Rich comparison - CPython: tp_richcompare
            // Objects/typeobject.c:6950
            "__eq__",
            "__ne__",
            "__lt__",
            "__le__",
            "__gt__",
            "__ge__",

            // String representation - CPython: tp_str, tp_repr
            // Objects/typeobject.c:6922, 6938
            "__str__",
            "__repr__",

            // Attribute access - CPython: tp_getattro, tp_setattro
            // Objects/typeobject.c:6916, 6920
            "__getattribute__",
            "__setattr__",
            "__delattr__",

            // Hashing - CPython: tp_hash
            // Objects/typeobject.c:6951
            "__hash__",

            // Context manager protocol
            "__enter__",
            "__exit__",

            // Numeric protocol - CPython: nb_* slots
            // Objects/typeobject.c:6858-6883
            "__add__",
            "__radd__",
            "__iadd__",
            "__sub__",
            "__rsub__",
            "__isub__",
            "__mul__",
            "__rmul__",
            "__imul__",
            "__truediv__",
            "__rtruediv__",
            "__itruediv__",
            "__floordiv__",
            "__rfloordiv__",
            "__ifloordiv__",
            "__mod__",
            "__rmod__",
            "__imod__",
            "__pow__",
            "__rpow__",
            "__ipow__",
            "__neg__",
            "__pos__",
            "__abs__",
            "__invert__",
            "__and__",
            "__rand__",
            "__iand__",
            "__or__",
            "__ror__",
            "__ior__",
            "__xor__",
            "__rxor__",
            "__ixor__",
            "__lshift__",
            "__rlshift__",
            "__ilshift__",
            "__rshift__",
            "__rrshift__",
            "__irshift__",

            // Type conversion - CPython: nb_int, nb_float, nb_index, nb_bool
            "__int__",
            "__float__",
            "__index__",
            "__bool__",
        };

        /// <summary>
        /// 매핑 타입 전용 메서드 (dict 서브클래스용)
        /// CPython 3.12: Objects/dictobject.c
        /// </summary>
        public static readonly string[] MappingMethods = new[]
        {
            "keys",
            "values",
            "items",
            "get",
            "pop",
            "popitem",
            "clear",
            "update",
            "setdefault",
        };

        /// <summary>
        /// 빌트인 타입 목록
        /// </summary>
        public static bool IsBuiltinType(PyType type)
        {
            return type == PyType.ListType ||
                   type == PyType.DictType ||
                   type == PyType.TupleType ||
                   type == PyType.SetType ||
                   type == PyType.FrozenSetType ||
                   type == PyType.StrType ||
                   type == PyType.BytesType ||
                   type == PyType.IntType ||
                   type == PyType.FloatType ||
                   type == PyType.BoolType ||
                   type == PyType.ObjectType;
        }
    }
}
