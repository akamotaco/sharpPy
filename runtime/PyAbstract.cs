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
            switch (type.LookupSpecial("__class_getitem__"))
            {
                case null: return type.GetItem(key);
                case PyBuiltinClassMethod classMethod: return classMethod.Call(new[] { type, key }, null);
                case IDescriptor descriptor: return descriptor.Get(null, type).Call(new[] { key }, null);
                case var method: return method.Call(new[] { type, key }, null);
            }
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
    }
}
