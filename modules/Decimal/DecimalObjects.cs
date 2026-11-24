// Modules/_decimal/_decimal.c (CPython 3.12.15): the object structs PyDecObject,
// PyDecSignalDictObject, PyDecContextObject and PyDecContextManagerObject. Each type's slots are
// the C# virtuals, which call the C functions in DecimalModule. An instance of a Python subclass
// is the same struct with its class in _customType (tp_alloc of the subclass).
using SharpPy.Modules.Mpdecimal;
using static SharpPy.Modules.Mpdecimal.Mpd;

namespace SharpPy.Modules.CDecimal
{
    /// <summary>PyDecObject: the cached hash, the mpd_t and its static data of _Py_DEC_MINALLOC words.</summary>
    internal sealed class PyDecObject : PyObject, INbPower
    {
        internal long hash = -1;
        internal readonly mpd_t dec;

        // PyDecType_New: flags MPD_STATIC|MPD_STATIC_DATA, the data the object's own array.
        internal PyDecObject(PyType type)
        {
            if (!ReferenceEquals(type, DecimalModule.PyDec_Type))
                SetCustomType(type);
            dec = new mpd_t(MPD_STATIC | MPD_STATIC_DATA, 0, 0, 0, DecimalModule._Py_DEC_MINALLOC,
                new ulong[DecimalModule._Py_DEC_MINALLOC]);
        }

        public override PyType GetPyType() => _customType ?? DecimalModule.PyDec_Type;

        protected internal override PyObject Repr() => DecimalModule.dec_repr(this);
        protected internal override PyObject Str() => DecimalModule.dec_str(this);
        protected internal override long Hash() => DecimalModule.dec_hash(this);
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => DecimalModule.dec_richcompare(this, other, op);
        protected internal override bool Bool() => DecimalModule.nm_nonzero(this);
        public override PyObject Add(PyObject other) => DecimalModule.nm_mpd_qadd(this, other);
        public override PyObject Subtract(PyObject other) => DecimalModule.nm_mpd_qsub(this, other);
        public override PyObject Multiply(PyObject other) => DecimalModule.nm_mpd_qmul(this, other);
        public override PyObject Modulo(PyObject other) => DecimalModule.nm_mpd_qrem(this, other);
        public override PyObject DivMod(PyObject other) => DecimalModule.nm_mpd_qdivmod(this, other);
        public override PyObject Power(PyObject other) => DecimalModule.nm_mpd_qpow(this, other, PyNone.Instance);
        // nb_power with a modulus: nm_mpd_qpow(base, exp, mod), whichever operand this is.
        public PyObject NbPower(PyObject v, PyObject w, PyObject z) => DecimalModule.nm_mpd_qpow(v, w, z);
        public override PyObject FloorDivide(PyObject other) => DecimalModule.nm_mpd_qdivint(this, other);
        public override PyObject Divide(PyObject other) => DecimalModule.nm_mpd_qdiv(this, other);
        protected internal override PyObject NbNegative() => DecimalModule.nm_mpd_qminus(this);
        protected internal override PyObject NbPositive() => DecimalModule.nm_mpd_qplus(this);
        protected internal override PyObject NbAbsolute() => DecimalModule.nm_mpd_qabs(this);
        protected internal override PyObject NbInt() => DecimalModule.nm_dec_as_long(this);
        protected internal override PyObject NbFloat() => DecimalModule.PyDec_AsFloat(this);
    }

    /// <summary>
    /// PyDecSignalDictObject: uint32_t *flags, the traps or the status of a context, NULL until a
    /// context ties it to one. The pointer is the context object and which field it names.
    /// </summary>
    internal sealed class PyDecSignalDictObject : PyObject
    {
        internal PyDecContextObject flagsOwner;
        internal bool flagsAreTraps;

        internal PyDecSignalDictObject(PyType type)
        {
            if (!ReferenceEquals(type, DecimalModule.PyDecSignalDictMixin_Type))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? DecimalModule.PyDecSignalDictMixin_Type;

        protected internal override PyObject Repr() => DecimalModule.signaldict_repr(this);
        protected internal override long Hash() => HashNotImplemented(this);
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op) => DecimalModule.signaldict_richcompare(this, other, op);
        protected internal override long Len() => DecimalModule.signaldict_len(this);
        protected internal override PyObject Iter() => DecimalModule.signaldict_iter(this);
        protected internal override PyObject MpSubscript(PyObject key) => DecimalModule.signaldict_getitem(this, key);
        protected internal override void MpAssSubscript(PyObject key, PyObject value) => DecimalModule.signaldict_setitem(this, key, value);
        protected internal override void MpDelSubscript(PyObject key) => DecimalModule.signaldict_setitem(this, key, null);
    }

    /// <summary>PyDecContextObject: the mpd_context_t, the traps and flags SignalDicts, capitals, tstate.</summary>
    internal sealed class PyDecContextObject : PyObject
    {
        internal mpd_context_t ctx;
        internal PyObject traps;
        internal PyObject flags;
        internal int capitals;
        internal object tstate;

        internal PyDecContextObject(PyType type)
        {
            if (!ReferenceEquals(type, DecimalModule.PyDecContext_Type))
                SetCustomType(type);
        }

        public override PyType GetPyType() => _customType ?? DecimalModule.PyDecContext_Type;

        protected internal override PyObject Repr() => DecimalModule.context_repr(this);
        protected internal override PyObject TpGetattro(PyString name) => DecimalModule.context_getattr(this, name);
        internal override bool TpGetattroIsGeneric => false;
        protected internal override void TpSetattro(PyString name, PyObject value) => DecimalModule.context_setattr(this, name, value);
        protected internal override void TpDelattro(PyString name) => DecimalModule.context_setattr(this, name, null);

        // PyObject_GenericGetAttr and PyObject_GenericSetAttr, which context_getattr and
        // context_setattr end in.
        internal PyObject GenericGetAttrBase(PyString name) => base.TpGetattro(name);
        internal void GenericSetAttrBase(PyString name, PyObject value)
        {
            if (value == null)
                base.TpDelattro(name);
            else
                base.TpSetattro(name, value);
        }
    }

    /// <summary>PyDecContextManagerObject: the local (inner) and the global (outer) context.</summary>
    internal sealed class PyDecContextManagerObject : PyObject
    {
        internal PyObject local;
        internal PyObject global;

        public override PyType GetPyType() => DecimalModule.PyDecContextManager_Type;
    }
}
