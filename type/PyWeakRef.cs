using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// A weak reference: <c>weakref.ref(x)</c>, the same object as <c>_weakref.ref</c>.
    ///
    /// What CPython's counterpart is: Objects/weakrefobject.c PyWeakReference, linked into the
    /// target's weakreflist at an offset the type declares (Objects/typeobject.c:2960). SharpPy has
    /// no per-type field offset, so the list lives on PyFinalizer, and the alive/dead answer follows
    /// the GC's collection instead of a reference count.
    ///
    /// Kept identical to 3.12.15 because it is measured, not remembered:
    ///   callback runs after __del__, newest first
    ///   a reference whose own object died first never calls back
    ///   ref(x) with no callback is the SAME object on a second call
    ///   hash of a live reference is the target's hash, cached
    ///   hash of a dead, never-hashed reference: TypeError: weak object has gone away
    ///     (Objects/weakrefobject.c:152)
    /// </summary>
    internal class PyWeakRef : PyObject
    {
        private readonly PyWeakCell _cell;
        internal readonly PyObject Callback;
        private long? _hash;

        internal PyWeakRef(PyObject target, PyObject callback)
        {
            _cell = PyFinalizer.WeakCell(target);
            Callback = callback;
        }

        internal bool Dead => _cell.Dead;

        /// <summary>
        /// The referent, or null. The read order is the algorithm: the strong local taken from Long
        /// pins the object, so it cannot be condemned after this point, and Short still set means it
        /// was not condemned before. When Short is empty a collection condemned the object; once its
        /// report has been drained the verdict is known, and an object that came back gets a fresh
        /// Short so the next collection is noticed too.
        /// </summary>
        internal PyObject Referent
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                var cell = _cell;
                if (cell.Dead) return null;
                var held = cell.Long.Target as PyObject;
                if (cell.Short.Target != null) return held;
                held = null;
                PyFinalizer.SettleCondemned();
                if (cell.Dead) return null;
                held = cell.Long.Target as PyObject;
                if (held != null) cell.Short.Target = held;
                return held;
            }
        }

        public override PyType GetPyType() => _customType ?? PyType.WeakRefType;
        public override string GetTypeName() => _customType?.Name ?? "weakref.ReferenceType";

        protected internal override PyString Repr() => SlotRepr();

        internal PyString SlotRepr()
        {
            var target = Referent;
            if (target == null) return new PyString($"<weakref at 0x{PyObjectId.Of(this):x}; dead>");
            return new PyString($"<weakref at 0x{PyObjectId.Of(this):x}; to '{target.GetTypeName()}' at 0x{PyObjectId.Of(target):x}>");
        }

        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => SlotCompare(other, op);

        // weakref_richcompare: == and != only; with a dead side the references themselves are
        // compared, otherwise PyObject_RichCompare of the referents.
        internal PyObject SlotCompare(PyObject other, CompareOp op)
        {
            if (!(other is PyWeakRef reference) || (op != CompareOp.EQ && op != CompareOp.NE))
                return PyNotImplemented.Instance;
            var mine = Referent;
            var theirs = reference.Referent;
            if (mine == null || theirs == null)
                return PyBool.FromBool(ReferenceEquals(this, other) == (op == CompareOp.EQ));
            return Compare(mine, theirs, op);
        }

        /// <summary>
        /// Measured: hash(r) == hash(a) while a lives, and the value survives after a dies - if it
        /// was taken once. Without it there is nothing left to report and CPython raises
        /// "weak object has gone away" (Objects/weakrefobject.c:152).
        /// </summary>
        public override int ToHash() => SlotHash();

        internal int SlotHash()
        {
            if (_hash.HasValue) return (int)_hash.Value;
            var target = Referent;
            if (target == null)
                throw PyTypeError.Create("weak object has gone away");
            // weakref_hash: PyObject_Hash of the referent, kept.
            var value = target.GetPythonHash();
            _hash = value;
            return (int)value;
        }

        protected internal override long Hash()
        {
            if (_hash.HasValue) return _hash.Value;
            SlotHash();
            return _hash.Value;
        }

        /// <summary>
        /// ref() - the object, or None. tp_call of PyWeakReference_Type
        /// (Objects/weakrefobject.c weakref_call).
        /// </summary>
        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            // _PyArg_NoKeywords("weakref", kw)
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("weakref() takes no keyword arguments");
            return SlotCall(args);
        }

        // PyArg_UnpackTuple(args, "weakref", 0, 0)
        internal PyObject SlotCall(PyObject[] args)
        {
            if (args != null && args.Length != 0)
                throw PyTypeError.Create($"weakref expected 0 arguments, got {args.Length}");
            return Referent ?? PyNone.Instance;
        }
    }

    /// <summary>
    /// An instance of a user class that derives from <c>weakref.ref</c>. CPython gives a weak
    /// reference subclass a __dict__ (Objects/weakrefobject.c:690 weakref_subclass sets
    /// tp_dictoffset); Lib/weakref.py:341 KeyedRef relies on it through __slots__.
    /// </summary>
    internal sealed class PyWeakRefSubclass : PyWeakRef
    {

        public PyWeakRefSubclass(PyClass instanceType, PyObject target, PyObject callback)
            : base(target, callback)
        {
            SetCustomType(instanceType);
        }


        // update_one_slot: a class that declares the method replaces the slot, otherwise the ref's own
        // slot stays. The type's method table calls the Slot* methods, so super().__call__() ends there.
        public override PyObject Call(PyObject[] args, PyDict kwargs) =>
            this.CallUserSpecialOrNull("__call__", args) ?? SlotCall(args);

        protected internal override PyString Repr() => SlotRepr();

        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => SlotCompare(other, op);

        public override int ToHash() => SlotHash();
    }
}
