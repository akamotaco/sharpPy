using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// add_operators and slotdefs (Objects/typeobject.c): CPython gives a type a wrapper in its dict for
    /// every slot it fills, and the dict is one: tp_dict. SharpPy's builtin types answer through CLR
    /// virtuals; the wrappers for the slots each type fills (SlotTable.g.cs) go into its TypeDict, which
    /// is then laid out in type_ready's order. A wrapper works on the native value, not the instance.
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
            foreach (var type in new[] { PyType.ListType, PyType.TupleType, PyType.StrType, PyType.BytesType, PyType.BytearrayType })
                IndexArgRepeat(type);
        }

        /// <summary>
        /// wrap_indexargfunc: the __mul__, __rmul__ and __imul__ of a type whose slot is sq_repeat or
        /// sq_inplace_repeat take a count, PyNumber_AsSsize_t(arg, PyExc_OverflowError), not an operand.
        /// </summary>
        /// The one place a repeat slot becomes its wrapper: each sequence type passes its __mul__,
        /// __rmul__ and __imul__ through here once its own table is in (a wrapper already made is
        /// left as it is).
        internal static void IndexArgRepeat(PyType type)
        {
            foreach (var name in new[] { "__mul__", "__rmul__", "__imul__" })
            {
                if (!type.TypeDict.TryGetValue(name, out var present))
                    continue;
                Func<PyObject, PyObject[], PyDict, PyObject> inner;
                bool callsVirtual = false;
                switch (present)
                {
                    case PyWrapperDescriptor { IndexArgument: true }:
                        continue;
                    case PyWrapperDescriptor wrapper:
                        inner = wrapper._implementation;
                        callsVirtual = wrapper.CallsVirtual;
                        break;
                    // A slot registered as a method (array's __imul__) is a slot wrapper all the same.
                    case PyMethodDescriptor method when method.GetType() == typeof(PyMethodDescriptor):
                        inner = method._implementation;
                        break;
                    default:
                        continue;
                }
                type.TypeDict[name] = new PyWrapperDescriptor(name, type, (self, args, kwargs) =>
                    inner(self, new PyObject[] { PyInt.From(PyArgs.AsSsize(args[0], PyArgs.SsizeError.Overflow)) }, kwargs))
                {
                    IndexArgument = true,
                    CallsVirtual = callsVirtual,
                };
            }
        }

        private static readonly List<(PyType Type, string[] Order)> Layouts = new List<(PyType, string[])>();

        private static void Lay(PyType type, string names) =>
            Layouts.Add((type, names.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

        /// <summary>
        /// type_ready_fill_dict's layout of each builtin's tp_dict (__new__, add_operators, the methods,
        /// members and getsets, __doc__), in the order CPython 3.12 has it (SlotTable.g.cs). Run once,
        /// after every builtin type has registered what it has; a name CPython's dict lacks keeps its
        /// registration order after the others.
        /// </summary>
        internal static void LayOut()
        {
            LayAll();
            foreach (var (type, order) in Layouts)
            {
                var old = type.TypeDict;
                var laid = new Dictionary<string, PyObject>(old.Count);
                foreach (var name in order)
                    if (old.TryGetValue(name, out var value))
                        laid[name] = value;
                foreach (var entry in old)
                    laid.TryAdd(entry.Key, entry.Value);
                old.Clear();
                foreach (var entry in laid)
                    old[entry.Key] = entry.Value;
            }
        }

        /// <summary>
        /// add_operators for a static type of a C module, when type_ready (PyDocs.SetTypeDocs)
        /// readies it: the wrappers core/SlotTable.g.cs lists for "<module>.<name>", and None for
        /// a slot CPython sets to PyObject_HashNotImplemented.
        /// </summary>
        internal static void AddModuleType(PyType type)
        {
            if (type.Module is not { Length: > 0 } module || module == "builtins" || !ModuleSlots.TryGetValue(module + "." + type.Name, out var names))
                return;
            Add(type, names);
            foreach (var entry in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (entry.StartsWith('-') && !type.TypeDict.ContainsKey(entry[1..]))
                    type.TypeDict[entry[1..]] = PyNone.Instance;
        }

        private static void Add(PyType type, string names)
        {
            var listed = names.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // "-name": a slot CPython keeps as None in the dict, which has no wrapper.
            foreach (var entry in listed)
            {
                if (entry.StartsWith('-'))
                    continue;
                string name = entry;
                // add_operators: a name the dict holds already (a method, METH_COEXIST or not) stays.
                if (type.TypeDict.ContainsKey(name))
                    continue;
                if (!Implemented.Contains(name))
                    continue;
                string slot = name;
                var owner = type;
                type.TypeDict[name] = new PyWrapperDescriptor(name, type,
                    (self, args, kwargs) => Run(owner, self, slot, args, kwargs)) { CallsVirtual = true };
            }
        }

        private static readonly HashSet<string> Implemented = new HashSet<string>
        {
            "__repr__", "__str__", "__hash__", "__eq__", "__ne__", "__lt__", "__le__", "__gt__", "__ge__",
            "__iter__", "__next__", "__len__", "__contains__", "__getitem__", "__setitem__", "__delitem__", "__call__",
            "__bool__", "__neg__", "__pos__", "__invert__", "__abs__",
            "__add__", "__sub__", "__mul__", "__mod__", "__divmod__", "__pow__", "__lshift__", "__rshift__",
            "__and__", "__xor__", "__or__", "__floordiv__", "__truediv__",
            "__getattribute__", "__setattr__", "__delattr__",
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
                    return PyInt.From(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(self));
                case "__str__":
                    // object_str: the type's tp_repr itself, not PyObject_Repr, so what it answers
                    // is judged by PyObject_Str ("__str__ returned non-string"). __repr__ =
                    // object.__str__ brings it back here.
                    PyCRecursion.Enter(" while calling a Python object");
                    try { return self.TpRepr(); }
                    finally { PyCRecursion.Leave(); }
                default:
                    return PyClass.ObjectRepr(self);
            }
        }

        private static PyObject Run(PyType owner, PyObject self, string name, PyObject[] args, PyDict kwargs)
        {
            // The arity of the slot is checked by the wrapper descriptor (CheckSlotArguments).
            if (ReferenceEquals(owner, PyType.ObjectType))
                return RunObject(self, name, args);
            var value = self;
            switch (name)
            {
                case "__eq__":
                case "__ne__":
                case "__lt__":
                case "__le__":
                case "__gt__":
                case "__ge__":
                    return value.TpRichCompare(args[0], Comparison(name));
                case "__hash__": return PyInt.From(value.Hash());
                case "__repr__": return value.Repr();
                case "__str__": return value.Str();
                case "__iter__": return value.Iter();
                case "__next__": return value.IterNext();
                case "__len__": return value is PyRange range ? PyInt.From(range.ClampedLength()) : PyInt.From(value.Len());
                case "__contains__": return value.ContainsItem(args[0]);
                case "__getitem__": return value.MpSubscript(args[0]);
                case "__setitem__":
                    value.MpAssSubscript(args[0], args[1]);
                    return PyNone.Instance;
                case "__delitem__":
                    value.MpDelSubscript(args[0]);
                    return PyNone.Instance;
                // wrap_call: PyObject_Call(self, args, kwds).
                case "__call__": return PyArity.ObjectCall(value, args, kwargs);
                case "__bool__": return PyBool.FromBool(value.Bool());
                case "__abs__": return value.NbAbsolute();
                case "__neg__": return value.NbNegative();
                case "__pos__": return value.NbPositive();
                case "__invert__": return value.NbInvert();
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
                // wrap_binaryfunc over tp_getattro, wrap_setattr and wrap_delattr (hackcheck first).
                case "__getattribute__":
                    PyObject.AttributeName(args[0]);
                    // tp_getattro by the name object; PyObject_GenericGetAttr names its AttributeError.
                    try { return value.TpGetattro((PyString)args[0]); }
                    catch (PythonException error) when (value.TpGetattroIsGeneric && error.PyException is PyAttributeError missing)
                    {
                        missing.SetContextIfUnset((PyString)args[0], value);
                        throw;
                    }
                case "__setattr__":
                    PyType.Hackcheck(value, "__setattr__");
                    PyObject.AttributeName(args[0]);
                    value.TpSetattro((PyString)args[0], args[1]);
                    return PyNone.Instance;
                case "__delattr__":
                    PyType.Hackcheck(value, "__delattr__");
                    PyObject.AttributeName(args[0]);
                    value.TpDelattro((PyString)args[0]);
                    return PyNone.Instance;
                default: return value.Divide(args[0]);
            }
        }
    }
}
