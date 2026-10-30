using System;

namespace SharpPy
{
    /// <summary>
    /// <c>tp_flags</c>, transcribed. CPython keeps one bit field per type and answers "what kind
    /// of object is this" from it instead of asking the object: <c>PyList_Check(op)</c> is
    /// <c>PyType_HasFeature(Py_TYPE(op), Py_TPFLAGS_LIST_SUBCLASS)</c>
    /// (Include/listobject.h:24), and the test itself is two instructions
    /// (<c>PyType_HasFeature</c>, Include/object.h:959; the internal spelling in 3.12 is
    /// <c>_PyType_HasFeature</c>, used for the base-type gate at Objects/typeobject.c:2789).
    ///
    /// Bit values are copied verbatim from Include/object.h so that a line-by-line comparison of
    /// this enum with the header is meaningful. Bits CPython defines but SharpPy has no meaning
    /// for (VECTORCALL, IS_ABSTRACT, VALID_VERSION_TAG, ...) are omitted, not renumbered: a later
    /// commit can add one without renumbering its neighbours.
    ///
    /// Two families deliberately do NOT get a bit, because CPython gives them none:
    ///   set / frozenset - Include/setobject.h:36 PySet_Check and :30 PyAnySet_Check are plain
    ///                     PyType_IsSubtype tests, so SharpPy tests the MRO for those two
    ///   bytes-like      - CPython uses a buffer-proto question, not a flag
    /// Using a flag where CPython walks the MRO would be as much a divergence as the other way
    /// round: it would answer differently for a type whose flag and MRO disagree, and only one of
    /// those two is computed from the base list.
    /// </summary>
    [Flags]
    public enum PyTypeFlags : ulong
    {
        None = 0UL,

        /// <summary>
        /// Include/object.h:474. In CPython this replaces an explicit tp_weaklistoffset: the
        /// interpreter finds the object's weakref list at a managed offset. SharpPy's weakref work
        /// (docs/lifetime_and_gc_policy.md) needs the same "does this type hold weakrefs" answer
        /// before a weakref can be created, which is why the bit is declared here already.
        /// </summary>
        ManagedWeakRef = 1UL << 3,

        /// <summary>Include/object.h:484 - pattern matching's Sequence() and PyType_IsSubtype-free
        /// sequence answer (Objects/typeobject.c:7001 COLLECTION_FLAGS).</summary>
        Sequence = 1UL << 5,

        /// <summary>Include/object.h:486 - mutually exclusive with Sequence
        /// (Objects/typeobject.c:7021 asserts that in PyType_Ready's pre-checks).</summary>
        Mapping = 1UL << 6,

        /// <summary>Include/object.h:494 - a static type whose layout cannot change.</summary>
        ImmutableType = 1UL << 8,

        /// <summary>Include/object.h:497 - a type created by executing a class statement.
        /// SharpPy: <c>PyClass</c>. CPython uses this to decide the type object itself is
        /// collectable and its dictionary/weakref list live at managed offsets.</summary>
        HeapType = 1UL << 9,

        /// <summary>
        /// Include/object.h:500 - subclassable. The absence is observable: CPython refuses
        /// "type 'bool' is not an acceptable base type" at Objects/typeobject.c:2789, and
        /// PyBool_Type really has no BaseType bit (Objects/boolobject.c:190 tp_flags is
        /// Py_TPFLAGS_DEFAULT alone).
        /// </summary>
        BaseType = 1UL << 10,

        /// <summary>Include/object.h:518 - the type participates in the cyclic collector.
        /// SharpPy's lifetime stays on the CLR collector (room B of
        /// docs/lifetime_and_gc_policy.md), so this bit records the CPython fact and nothing more.</summary>
        HaveGC = 1UL << 14,

        /// <summary>Include/object.h:540 - an undocumented flag that lets one positional
        /// subpattern match the subject itself instead of a mapped attribute.</summary>
        MatchSelf = 1UL << 22,

        /// <summary>Include/object.h:543 - items live at the end of the instance allocation.</summary>
        ItemsAtEnd = 1UL << 23,

        // The family bits: Include/object.h:545-551. Propagated to subtypes by
        // inherit_special (Objects/typeobject.c:6726), which is the mechanism that makes
        // PyList_Check true for an instance of a user class deriving from list.
        LongSubclass = 1UL << 24,
        ListSubclass = 1UL << 25,
        TupleSubclass = 1UL << 26,
        BytesSubclass = 1UL << 27,
        UnicodeSubclass = 1UL << 28,
        DictSubclass = 1UL << 29,
        BaseExceptionSubclass = 1UL << 30,
        TypeSubclass = 1UL << 31,
    }
}
