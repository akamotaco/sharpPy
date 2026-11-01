using System;

namespace SharpPy
{
    /// <summary>
    /// add_operators (Objects/typeobject.c): CPython gives each type a __len__, __contains__ and so on
    /// for every slot it fills. SharpPy's builtin containers answer through CLR virtuals and most have no
    /// entry in their type table, so `d.__len__` failed, and functools.lru_cache reads exactly that. This
    /// table is the one place that exposes a slot: bound on an instance, unbound on a type, and only for
    /// the types that have the slot, measured with hasattr on the reference interpreter.
    /// __repr__ and __str__ stay out: AsString and ToRepr look those names up, so a wrapper that calls
    /// them would call itself.
    /// </summary>
    internal static class PySlotWrappers
    {
        private static readonly System.Collections.Generic.Dictionary<string, PyObject> Unbound =
            new System.Collections.Generic.Dictionary<string, PyObject>();

        private static bool HasSlot(PyType type, string name)
        {
            bool Is(PyType wanted) => type.IsSubclassOf(wanted);
            return name switch
            {
                "__len__" or "__contains__" => Is(PyType.DictType) || Is(PyType.SetType) || Is(PyType.FrozenSetType)
                    || Is(PyType.BytesType) || Is(PyType.RangeType),
                "__iter__" => Is(PyType.DictType) || Is(PyType.SetType) || Is(PyType.FrozenSetType)
                    || Is(PyType.StrType) || Is(PyType.RangeType),
                "__mul__" => Is(PyType.StrType),
                "__getitem__" => Is(PyType.BytesType) || Is(PyType.RangeType),
                "__eq__" or "__ne__" or "__lt__" or "__le__" or "__gt__" or "__ge__" or "__hash__" => true,
                _ => false,
            };
        }

        private static PyObject Run(PyObject self, string name, PyObject[] args) => name switch
        {
            "__len__" => new PyInt(self.Length()),
            "__contains__" => self.Contains(args[0]),
            "__iter__" => self.GetIterator(),
            "__mul__" => self.Multiply(args[0]),
            "__getitem__" => self.GetItem(args[0]),
            "__eq__" => self.RichCompare(args[0], PyObject.CompareOp.EQ),
            "__ne__" => self.RichCompare(args[0], PyObject.CompareOp.NE),
            "__lt__" => self.RichCompare(args[0], PyObject.CompareOp.LT),
            "__le__" => self.RichCompare(args[0], PyObject.CompareOp.LE),
            "__gt__" => self.RichCompare(args[0], PyObject.CompareOp.GT),
            "__ge__" => self.RichCompare(args[0], PyObject.CompareOp.GE),
            _ => new PyInt(self.ToHash()),
        };

        private static int Arity(string name) =>
            name is "__len__" or "__iter__" or "__hash__" ? 0 : 1;

        internal static PyObject TryBind(PyObject self, string name)
        {
            if (self is PyClassInstance) return null;
            int arity = Arity(name);
            if (self is PyType type)
            {
                if (!HasSlot(type, name)) return null;
                // One object per slot: object.__lt__ is object.__lt__, and a class that inherits it hands out the
                // same one, which is what functools.total_ordering tests.
                lock (Unbound)
                {
                    if (Unbound.TryGetValue(name, out var unbound)) return unbound;
                    unbound = new PyBuiltinFunction(name, args =>
                {
                    if (args.Length != arity + 1)
                        throw PyTypeError.Create($"expected {arity + 1} arguments, got {args.Length}");
                    return Run(args[0], name, args[1..]);
                });
                    Unbound[name] = unbound;
                    return unbound;
                }
            }
            if (!HasSlot(self.GetPyType(), name)) return null;
            // __hash__ on an instance is measured narrower than on a type: a list says None through its table.
            return new PyBuiltinFunction(name, args =>
            {
                if (args.Length != arity)
                    throw PyTypeError.Create($"expected {arity} argument{(arity == 1 ? "" : "s")}, got {args.Length}");
                return Run(self, name, args);
            });
        }
    }
}
