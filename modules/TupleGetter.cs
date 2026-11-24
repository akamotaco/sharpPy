using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// _tuplegetterobject (Modules/_collectionsmodule.c:2358): the field descriptor namedtuple puts
    /// in a class, an index and a writable __doc__.
    /// </summary>
    public sealed class PyTupleGetter : PyObject, IDescriptor
    {
        internal static readonly PyType TupleGetterType = MakeType();

        private readonly long _index;
        private PyObject _doc;

        private PyTupleGetter(long index, PyObject doc)
        {
            _index = index;
            _doc = doc;
        }

        public override PyType GetPyType() => TupleGetterType;

        private static PyType MakeType()
        {
            var type = new PyType("_tuplegetter", new[] { PyType.ObjectType }, "collections");
            // tuplegetter_new: the clinic's (index: Py_ssize_t, doc: object, /), no keywords.
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                PyArgs.NoKeywords("_tuplegetter", kwargs);
                PyArgs.Positional("_tuplegetter", args, null, 2, 2);
                return new PyTupleGetter(PyArgs.Ssize(args[0]), args[1]);
            }, 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__get__"] = new PyWrapperDescriptor("__get__", type, (self, args, kwargs) =>
            {
                var (target, owner) = TypeTables.DescrGetArguments(args);
                return ((PyTupleGetter)self).Get(target, owner as PyType);
            }, 1, 2);
            type.TypeDict["__set__"] = new PyWrapperDescriptor("__set__", type, (self, args, kwargs) =>
            {
                ((PyTupleGetter)self).Set(args[0], args[1]);
                return PyNone.Instance;
            }, 2, 2);
            type.TypeDict["__delete__"] = new PyWrapperDescriptor("__delete__", type, (self, args, kwargs) =>
            {
                ((PyTupleGetter)self).Delete(args[0]);
                return PyNone.Instance;
            }, 1, 1);
            // tuplegetter_methods
            type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type,
                (self, args, kwargs) => ((PyTupleGetter)self).Reduce(), 0, 0);
            // tuplegetter_members: __doc__, a writable T_OBJECT
            type.TypeDict["__doc__"] = new PyMemberDescriptor("__doc__", type,
                self => ((PyTupleGetter)self)._doc ?? PyNone.Instance,
                (self, value) => ((PyTupleGetter)self)._doc = value,
                deleter: self => ((PyTupleGetter)self)._doc = null);
            return type;
        }

        // tuplegetter_descr_get
        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null)
                return this;
            if (instance is not PyTuple tuple)
            {
                if (instance is PyNone)
                    return this;
                throw PyTypeError.Create(
                    $"descriptor for index '{_index}' for tuple subclasses doesn't apply to '{instance.GetPyType().FullName}' object");
            }
            if ((ulong)_index >= (ulong)tuple.Items.Length)
                throw PyIndexError.Create("tuple index out of range");
            return tuple.Items[_index];
        }

        // tuplegetter_descr_set
        public void Set(PyObject instance, PyObject value) => throw PyAttributeError.Create("can't set attribute");

        public void Delete(PyObject instance) => throw PyAttributeError.Create("can't delete attribute");

        public bool IsDataDescriptor() => true;

        // tuplegetter_reduce: (type, (index, doc))
        private PyObject Reduce() => PyTuple.From(GetPyType(), PyTuple.From(PyInt.From(_index), _doc ?? PyNone.Instance));

        // tuplegetter_repr: _PyType_Name, the part of tp_name after the last dot.
        protected internal override PyObject Repr() =>
            PyString.From($"{GetPyType().Name}({_index}, {(_doc ?? PyNone.Instance).ToRepr().Value})");
    }

    internal static class CountElements
    {
        // _collections__count_elements_impl
        internal static PyObject Run(PyObject mapping, PyObject iterable)
        {
            var iterator = iterable.GetIterator();
            var one = PyInt.From(1);
            var mappingGet = mapping.GetPyType().FindNameInMro("get");
            var dictGet = PyType.DictType.FindNameInMro("get");
            var mappingSetitem = mapping.GetPyType().FindNameInMro("__setitem__");
            var dictSetitem = PyType.DictType.FindNameInMro("__setitem__");
            if (mappingGet != null && ReferenceEquals(mappingGet, dictGet)
                && mappingSetitem != null && ReferenceEquals(mappingSetitem, dictSetitem)
                && mapping is PyDict dict)
            {
                // the fast path: one hash for the lookup and the store
                while (Next(iterator) is { } key)
                {
                    long hash = key.GetPythonHash();
                    if (!dict.InternalDict.TryGetKnownHash(key, hash, out var oldval))
                        dict.SetKnownHash(key, one, hash);
                    else
                        dict.SetKnownHash(key, PyVM.Instance.ExecuteBinaryOpType(oldval, one, BinaryOpType.ADD), hash);
                }
                return PyNone.Instance;
            }
            var boundGet = mapping.GetAttribute("get");
            var zero = PyInt.From(0);
            while (Next(iterator) is { } item)
            {
                var oldval = boundGet.Call(new[] { item, zero }, null);
                var newval = PyVM.Instance.ExecuteBinaryOpType(oldval, one, BinaryOpType.ADD);
                PyAbstract.SetItem(mapping, item, newval);
            }
            return PyNone.Instance;
        }

        // PyIter_Next: null at the end, every other error raised.
        private static PyObject Next(PyObject iterator)
        {
            try
            {
                return iterator.Next();
            }
            catch (PythonException error) when (error.PyException is PyStopIteration)
            {
                return null;
            }
        }
    }
}
