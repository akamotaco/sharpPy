using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// add_operators and slotdefs (Objects/typeobject.c): CPython gives a type a wrapper in its dict for
    /// every slot it fills. SharpPy's builtin types answer through CLR virtuals, so what they have is kept
    /// in PyType.SlotDict, one table (SlotTable.g.cs) for vars(), dir() and attribute access.
    /// Dispatch does not read it: a special method lookup that met a slot wrapper would come back to the
    /// same virtual. A wrapper therefore works on the native value and not on the instance.
    /// </summary>
    internal static partial class PySlotWrappers
    {
        private static bool _installed;

        internal static void Install()
        {
            if (_installed)
                return;
            _installed = true;
            AddAll();
        }

        private static void Add(PyType type, string names)
        {
            foreach (var name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // add_operators: a slot in a builtin's dict is a slot wrapper, however it was registered.
                if (type.TypeDict.TryGetValue(name, out var present))
                {
                    if (present.GetType() == typeof(PyMethodDescriptor) && !Coexists(type, name))
                        type.TypeDict[name] = new PyWrapperDescriptor(name, type, ((PyMethodDescriptor)present)._implementation);
                    continue;
                }
                if (!Implemented.Contains(name))
                    continue;
                string slot = name;
                var owner = type;
                type.SlotDict[name] = new PyWrapperDescriptor(name, type,
                    (self, args, kwargs) => Run(owner, self, slot, args, kwargs)) { CallsVirtual = true };
            }
        }

        // METH_COEXIST: these stay methods, beside the slot.
        private static bool Coexists(PyType type, string name) =>
            (ReferenceEquals(type, PyType.ListType) && name == "__getitem__") ||
            (ReferenceEquals(type, PyType.DictType) && name is "__getitem__" or "__contains__") ||
            ((ReferenceEquals(type, PyType.SetType) || ReferenceEquals(type, PyType.FrozenSetType)) && name == "__contains__");

        private static readonly HashSet<string> Implemented = new HashSet<string>
        {
            "__repr__", "__str__", "__hash__", "__eq__", "__ne__", "__lt__", "__le__", "__gt__", "__ge__",
            "__iter__", "__next__", "__len__", "__contains__", "__getitem__", "__setitem__", "__delitem__", "__call__",
            "__bool__", "__neg__", "__pos__", "__invert__", "__abs__",
            "__add__", "__sub__", "__mul__", "__mod__", "__divmod__", "__pow__", "__lshift__", "__rshift__",
            "__and__", "__xor__", "__or__", "__floordiv__", "__truediv__",
        };

        private static PyObject.CompareOp Comparison(string name) => name switch
        {
            "__eq__" => PyObject.CompareOp.EQ,
            "__ne__" => PyObject.CompareOp.NE,
            "__lt__" => PyObject.CompareOp.LT,
            "__le__" => PyObject.CompareOp.LE,
            "__gt__" => PyObject.CompareOp.GT,
            _ => PyObject.CompareOp.GE,
        };

        private const int CRecursionLimit = 3000;
        [ThreadStatic] private static int _strDepth;

        // object_richcompare, object_repr, object_str, _Py_HashPointer
        private static PyObject RunObject(PyObject self, string name, PyObject[] args)
        {
            switch (name)
            {
                case "__eq__":
                    return ReferenceEquals(self, args[0]) ? PyBool.True : PyNotImplemented.Instance;
                case "__ne__":
                    var equal = self.RichCompare(args[0], PyObject.CompareOp.EQ);
                    return equal == PyNotImplemented.Instance ? equal : PyBool.FromBool(!equal.IsTrue());
                case "__lt__":
                case "__le__":
                case "__gt__":
                case "__ge__":
                    return PyNotImplemented.Instance;
                case "__hash__":
                    return new PyInt(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(self));
                case "__str__":
                    // object_str: repr(self), which __repr__ = object.__str__ brings back here.
                    if (_strDepth >= CRecursionLimit)
                        throw new PythonException(new PyRecursionError("maximum recursion depth exceeded while calling a Python object"));
                    _strDepth++;
                    try { return self.ToRepr(); }
                    finally { _strDepth--; }
                default:
                    return PyClass.ObjectRepr(self);
            }
        }

        private static PyObject Run(PyType owner, PyObject self, string name, PyObject[] args, PyDict kwargs)
        {
            // The arity of the slot is checked by the wrapper descriptor (CheckSlotArguments).
            if (ReferenceEquals(owner, PyType.ObjectType))
                return RunObject(self, name, args);
            var value = PyNativeStorage.Unwrap(self);
            switch (name)
            {
                case "__eq__":
                case "__ne__":
                case "__lt__":
                case "__le__":
                case "__gt__":
                case "__ge__":
                    return value.RichCompare(PyNativeStorage.Unwrap(args[0]), Comparison(name));
                case "__hash__": return new PyInt(value.GetPythonHash());
                case "__repr__": return value.ToRepr();
                case "__str__": return value.AsString();
                case "__iter__": return value.GetIterator();
                case "__next__": return value.Next();
                case "__len__": return value is PyRange range ? new PyInt(range.ClampedLength()) : new PyInt(value.Length());
                case "__contains__": return value.Contains(args[0]);
                case "__getitem__": return value.GetItem(args[0]);
                case "__setitem__":
                    value.SetItem(args[0], args[1]);
                    return PyNone.Instance;
                case "__delitem__":
                    value.DelItem(args[0]);
                    return PyNone.Instance;
                case "__call__": return value.Call(args, kwargs);
                case "__bool__": return PyBool.FromBool(value.IsTrue());
                case "__abs__": return PyBuiltinFunction.Absolute(value);
                case "__neg__": return value.Negative();
                case "__pos__": return value.Positive();
                case "__invert__": return value.BitwiseNot();
                case "__add__": return value.Add(args[0]);
                case "__sub__": return value.Subtract(args[0]);
                case "__mul__": return value.Multiply(args[0]);
                case "__mod__": return value.Modulo(args[0]);
                case "__divmod__": return value.DivMod(args[0]);
                case "__pow__": return value.Power(args[0]);
                case "__lshift__": return value.LeftShift(args[0]);
                case "__rshift__": return value.RightShift(args[0]);
                case "__and__": return value.BitwiseAnd(args[0]);
                case "__xor__": return value.BitwiseXor(args[0]);
                case "__or__": return value.BitwiseOr(args[0]);
                case "__floordiv__": return value.FloorDivide(args[0]);
                default: return value.Divide(args[0]);
            }
        }

        // The end of attribute lookup: what the regular lookup found nothing for.
        internal static PyObject TryBind(PyObject self, string name)
        {
            if (self is PyType type)
            {
                foreach (var level in type.MRO)
                    if (level.SlotDict.TryGetValue(name, out var slot))
                        return slot;
                self = type;
            }
            var kind = self.GetPyType();
            foreach (var level in kind.MRO)
                if (level.SlotDict.TryGetValue(name, out var slot))
                    return ((IDescriptor)slot).Get(self, kind);
            return null;
        }
    }
}
