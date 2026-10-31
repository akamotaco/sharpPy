using System;

namespace SharpPy
{
    /// <summary>
    /// add_operators (Objects/typeobject.c): CPython gives each type a __len__, __contains__ and so on
    /// for every slot it fills. SharpPy's builtin containers answer through CLR virtuals and most have no
    /// entry in their type table, so `d.__len__` failed, and functools.lru_cache reads exactly that. This
    /// table is the one place that exposes a slot as a bound method, and only for the types that have
    /// the slot, measured with hasattr on the reference interpreter.
    /// </summary>
    internal static class PySlotWrappers
    {
        internal static PyObject TryBind(PyObject self, string name)
        {
            if (self is PyType || self is PyClassInstance) return null;
            return name switch
            {
                "__len__" when self is PyDict or PySet or PyFrozenSet or PyBytes or PyRange
                    => Bound(name, 0, _ => new PyInt(self.Length())),
                "__contains__" when self is PyDict or PySet or PyFrozenSet or PyBytes or PyRange
                    => Bound(name, 1, a => self.Contains(a[0])),
                "__iter__" when self is PyDict or PySet or PyFrozenSet or PyString or PyRange
                    => Bound(name, 0, _ => self.GetIterator()),
                "__mul__" when self is PyString
                    => Bound(name, 1, a => self.Multiply(a[0])),
                "__getitem__" when self is PyBytes or PyRange
                    => Bound(name, 1, a => self.GetItem(a[0])),
                "__eq__" => Compare(self, name, PyObject.CompareOp.EQ),
                "__ne__" => Compare(self, name, PyObject.CompareOp.NE),
                "__lt__" => Compare(self, name, PyObject.CompareOp.LT),
                "__le__" => Compare(self, name, PyObject.CompareOp.LE),
                "__gt__" => Compare(self, name, PyObject.CompareOp.GT),
                "__ge__" => Compare(self, name, PyObject.CompareOp.GE),
                "__hash__" when self is PyFrozenSet or PyString or PyBytes or PyTuple or PyRange
                    => Bound(name, 0, _ => new PyInt(self.ToHash())),
                _ => null,
            };
        }

        private static PyObject Compare(PyObject self, string name, PyObject.CompareOp op)
            => Bound(name, 1, a => self.RichCompare(a[0], op));

        private static PyObject Bound(string name, int arity, Func<PyObject[], PyObject> slot)
            => new PyBuiltinFunction(name, args =>
            {
                if (args.Length != arity)
                    throw PyTypeError.Create($"expected {arity} argument{(arity == 1 ? "" : "s")}, got {args.Length}");
                return slot(args);
            });
    }
}
