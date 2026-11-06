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
