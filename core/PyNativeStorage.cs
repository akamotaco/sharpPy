namespace SharpPy
{
    /// <summary>
    /// Where a user-defined class derives from a builtin container, CPython does not copy any
    /// method into the subclass: <c>PyType_Ready</c>'s slot inheritance installs the *base's* C
    /// function as the subclass slot (Objects/typeobject.c - update_one_slot / inherit_slots), so
    /// <c>class C(list)</c> ends up with <c>list_length</c>, <c>list_repr</c>, <c>list_subscript</c>
    /// as its own slots, reading the value out of the object itself.
    ///
    /// SharpPy stores that value in a separate storage object on <see cref="PyClassInstance"/>.
    /// This class is the single place that turns "the slot inherited from the base" into "the
    /// storage of that instance", so every builtin descriptor asks for one thing and reports the
    /// same self-check error CPython's descrobject.c reports.
    ///
    /// CPython wording, from Objects/descrobject.c (call_method_descriptor) and
    /// Objects/abstract.c (generic method/slot wrappers):
    ///   method_descriptor    "descriptor 'add' for 'set' objects doesn't apply to a 'C' object"
    ///   slot wrapper         "descriptor 'add' requires a 'set' object but received a 'C'"
    /// </summary>
    internal static class PyNativeStorage
    {
        /// <summary>
        /// The set value of <paramref name="self"/>: the object itself when it is a set, otherwise
        /// the storage a <c>set</c> subclass instance carries. Null when self is neither, which is
        /// what the descriptor self-check turns into a TypeError.
        /// </summary>
        internal static PySet ForSet(PyObject self)
        {
            if (self is PySet set)
                return set;
            if (self is PyClassInstance instance)
                return instance.GetSetStorage();
            return null;
        }

        /// <summary>
        /// Same for frozenset. A <c>frozenset</c> subclass instance gets its value at construction
        /// time, because CPython builds it in tp_new (Objects/setobject.c:1003 frozenset_new) and
        /// the type has no tp_init at all (Objects/setobject.c:2262 "0, tp_init").
        /// </summary>
        internal static PyFrozenSet ForFrozenSet(PyObject self)
        {
            if (self is PyFrozenSet frozen)
                return frozen;
            if (self is PyClassInstance instance)
                return instance.GetFrozenStorage();
            return null;
        }

        internal static PyList ForList(PyObject self)
        {
            if (self is PyList list)
                return list;
            if (self is PyClassInstance instance)
                return instance.GetListStorage();
            return null;
        }

        internal static PyDict ForDict(PyObject self)
        {
            if (self is PyDict dict)
                return dict;
            if (self is PyClassInstance instance)
                return instance.GetDictStorage();
            return null;
        }

        /// <summary>
        /// The value of the builtin base this instance carries, in the order an MRO would meet it.
        /// A class can carry at most one variable-size builtin base (CPython refuses
        /// "multiple bases have instance lay-out conflict"), so "the first one present" is the
        /// inherited slot's value for every protocol operation at once: length, containment,
        /// iteration and repr.
        /// </summary>
        /// <summary>
        /// The value a comparison has to look at. CPython's equality functions test the right-hand
        /// operand with PyList_Check / PyDict_Check / PySet_Check (Objects/listobject.c
        /// list_richcompare, Objects/dictobject.c dict_equal, Objects/setobject.c set_richcompare),
        /// and those macros accept subclasses, so [1, 2] == C([1, 2]) compares contents even though
        /// the right side is a user-class object.
        /// </summary>
        internal static PyObject Unwrap(PyObject value) =>
            value is PyClassInstance instance ? instance.NativeStorage() ?? value : value;

        internal static PyObject ForAny(PyObject self)
        {
            if (self is PyClassInstance instance)
                return instance.NativeStorage();
            return self is PyList or PyDict or PySet or PyFrozenSet or PyTuple or PyString ? self : null;
        }
    }
}
