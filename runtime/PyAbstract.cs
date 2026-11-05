namespace SharpPy
{
    /// <summary>
    /// PyObject_GetItem, PyObject_SetItem and PyObject_DelItem (Objects/abstract.c): what the
    /// subscript opcodes do, whichever spelling of the subscript reached them.
    /// </summary>
    internal static class PyAbstract
    {
        private enum PowerSlot { None, Python, Int, Float, Complex }

        private static PowerSlot SlotOf(PyObject operand)
        {
            var type = operand.GetPyType();
            if (type is PyClass cls && SubclassInstanceSupport.FirstClassDictEntry(cls, "__pow__") != null)
                return PowerSlot.Python;
            if (IntValue(operand) != null) return PowerSlot.Int;
            if (type.IsSubclassOf(PyType.FloatType) || ReferenceEquals(type, PyType.FloatType)) return PowerSlot.Float;
            if (type.IsSubclassOf(PyType.ComplexType) || ReferenceEquals(type, PyType.ComplexType)) return PowerSlot.Complex;
            return PowerSlot.None;
        }

        private static PyInt IntValue(PyObject operand) => operand switch
        {
            PyInt exact => exact,
            PyBool flag => new PyInt(flag.Value ? 1 : 0),
            PyIntSubclass subclass => subclass.GetIntValue(),
            _ => null,
        };

        private static PyObject RunPowerSlot(PowerSlot slot, PyObject v, PyObject w, PyObject z)
        {
            switch (slot)
            {
                // slot_nb_power: three-argument power has no __rpow__.
                case PowerSlot.Python:
                    return SlotOf(v) == PowerSlot.Python
                        ? v.CallSpecial(v.LookupSpecialMethodUncached("__pow__"), w, z)
                        : PyNotImplemented.Instance;
                case PowerSlot.Int:
                    var a = IntValue(v);
                    var b = IntValue(w);
                    var c = IntValue(z);
                    return a != null && b != null && c != null ? a.PowerMod(b, c) : PyNotImplemented.Instance;
                case PowerSlot.Float:
                    throw PyTypeError.Create("pow() 3rd argument not allowed unless all arguments are integers");
                default:
                    throw PyValueError.Create("complex modulo");
            }
        }

        /// <summary>ternary_op: pow(v, w, z) with a modulus that is not None.</summary>
        internal static PyObject Power(PyObject v, PyObject w, PyObject z)
        {
            if (v is PyWeakProxy || w is PyWeakProxy || z is PyWeakProxy)
                return Power(PyWeakProxy.Unwrap(v), PyWeakProxy.Unwrap(w), PyWeakProxy.Unwrap(z));
            var slotV = SlotOf(v);
            var slotW = ReferenceEquals(w.GetPyType(), v.GetPyType()) ? PowerSlot.None : SlotOf(w);
            if (slotW == slotV) slotW = PowerSlot.None;
            PyObject result;
            if (slotV != PowerSlot.None)
            {
                if (slotW != PowerSlot.None && w.GetPyType().IsSubclassOf(v.GetPyType()))
                {
                    result = RunPowerSlot(slotW, v, w, z);
                    if (result is not PyNotImplemented) return result;
                    slotW = PowerSlot.None;
                }
                result = RunPowerSlot(slotV, v, w, z);
                if (result is not PyNotImplemented) return result;
            }
            if (slotW != PowerSlot.None)
            {
                result = RunPowerSlot(slotW, v, w, z);
                if (result is not PyNotImplemented) return result;
            }
            var slotZ = SlotOf(z);
            if (slotZ != PowerSlot.None && slotZ != slotV && slotZ != slotW)
            {
                result = RunPowerSlot(slotZ, v, w, z);
                if (result is not PyNotImplemented) return result;
            }
            throw PyTypeError.Create(
                $"unsupported operand type(s) for ** or pow(): '{v.GetTypeName()}', '{w.GetTypeName()}', '{z.GetTypeName()}'");
        }

        // PyObject_IsSubclass
        internal static bool IsSubclass(PyObject derived, PyObject cls) =>
            SharpPy.Modules.BuiltinsModule.IsSubclass(new[] { derived, cls }).IsTrue();

        internal static PyObject GetItem(PyObject target, PyObject key)
        {
            if (target is PyType type)
                return TypeSubscript(type, key);
            var kind = target.GetPyType();
            switch (kind.LookupSpecial("__getitem__"))
            {
                case null: return target.GetItem(key);
                case PyMethodDescriptor descriptor: return descriptor.CallWithReceiver(target, new[] { key }, null);
                case PyFunction function: return function.Call(new[] { target, key }, null);
                case var method when kind is PyClass: return target.CallSpecial(method, key);
                default: return target.GetItem(key);
            }
        }

        // The metatype's mp_subscript, then type_subscript's __class_getitem__.
        private static PyObject TypeSubscript(PyType type, PyObject key)
        {
            var metaGetItem = (type.GetPyType() as PyClass)?.LookupInMRO("__getitem__");
            if (metaGetItem != null)
                return metaGetItem.Call(new[] { type, key }, null);
            return type.GetItem(key);
        }

        internal static void SetItem(PyObject target, PyObject key, PyObject value)
        {
            var kind = target.GetPyType();
            switch (kind.LookupSpecial("__setitem__"))
            {
                case null: target.SetItem(key, value); break;
                case PyMethodDescriptor descriptor: descriptor.CallWithReceiver(target, new[] { key, value }, null); break;
                case PyFunction function: function.Call(new[] { target, key, value }, null); break;
                case var method when kind is PyClass: target.CallSpecial(method, key, value); break;
                default: target.SetItem(key, value); break;
            }
        }

        internal static void DelItem(PyObject target, PyObject key)
        {
            var kind = target.GetPyType();
            if (target is PyClassInstance)
            {
                target.DelItem(key);
                return;
            }
            switch (kind.LookupSpecial("__delitem__"))
            {
                case null: target.DelItem(key); break;
                case PyMethodDescriptor descriptor: descriptor.CallWithReceiver(target, new[] { key }, null); break;
                case PyWrapperDescriptor wrapper: wrapper.Call(new[] { target, key }, null); break;
                case PyFunction function: function.Call(new[] { target, key }, null); break;
                case var method when kind is PyClass: target.CallSpecial(method, key); break;
                default: target.DelItem(key); break;
            }
        }

        // PySequence_GetItem, PySequence_SetItem and PySequence_DelItem: a negative index counts
        // from sq_length, and sq_item gets what results. A __getitem__ written in Python is
        // slot_sq_item and takes that index as it is. The sq_item of a builtin is its subscript
        // over [0, len), SharpPy keeping one subscript per type; an index the length does not
        // bring into range is the IndexError the subscript of len raises in the type's words.
        private static PyObject SequenceIndex(PyObject s, long i, string slot)
        {
            long length = -1;
            if (i < 0 && s.HasTypeSlot("__len__"))
            {
                length = s.Size();
                i += length;
            }
            if (i < 0 && s.GetPyType().LookupSpecial(slot) is not PyFunction)
                return new PyInt(length < 0 ? s.Size() : length);
            return new PyInt(i);
        }

        private static bool IsMappingOnly(PyObject s) => s is PyDict || s.GetPyType().IsSubclassOf(PyType.DictType);

        // PySequence_Size: sq_length; a mapping's length is not a sequence's.
        internal static long SequenceSize(PyObject s)
        {
            if (s == null)
                throw NullError();
            if (IsMappingOnly(s))
                throw PyTypeError.Create($"{s.GetTypeName()} is not a sequence");
            return s.Size();
        }

        private static Exception NullError() => PySystemError.Create("null argument to internal routine");

        internal static PyObject SequenceGetItem(PyObject s, long i)
        {
            if (s == null)
                throw NullError();
            if (!PyVM.IsSequence(s))
                throw PyTypeError.Create(IsMappingOnly(s)
                    ? $"{s.GetTypeName()} is not a sequence"
                    : $"'{s.GetTypeName()}' object does not support indexing");
            return GetItem(s, SequenceIndex(s, i, "__getitem__"));
        }

        internal static void SequenceSetItem(PyObject s, long i, PyObject o)
        {
            if (s == null)
                throw NullError();
            if (o == null)
            {
                SequenceDelItem(s, i);
                return;
            }
            if (IsMappingOnly(s) || !s.HasTypeSlot("__setitem__"))
                throw PyTypeError.Create(IsMappingOnly(s)
                    ? $"{s.GetTypeName()} is not a sequence"
                    : $"'{s.GetTypeName()}' object does not support item assignment");
            SetItem(s, SequenceIndex(s, i, "__setitem__"), o);
        }

        internal static void SequenceDelItem(PyObject s, long i)
        {
            if (s == null)
                throw NullError();
            if (IsMappingOnly(s) || !s.HasTypeSlot("__delitem__"))
                throw PyTypeError.Create(IsMappingOnly(s)
                    ? $"{s.GetTypeName()} is not a sequence"
                    : $"'{s.GetTypeName()}' object doesn't support item deletion");
            DelItem(s, SequenceIndex(s, i, "__delitem__"));
        }
    }
}
