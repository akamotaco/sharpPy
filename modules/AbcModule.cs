using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// _abc (Modules/_abc.c): the registry and the two caches are sets of weak references, kept
    /// on an _abc_data object the class holds in _abc_impl.
    /// </summary>
    public static class AbcModule
    {
        private static ulong _invalidationCounter;

        public static PyModule CreateAbcModule()
        {
            WeakrefModule.RegisterWeakRefType();
            var module = new PyModule("_abc", "Module contains faster C# implementation of abc.ABCMeta");
            void Function(string name, int count, Func<PyObject[], PyObject> body) =>
                module.ModuleDict[name] = new PyBuiltinFunction(name, args =>
                {
                    if (args.Length != count)
                        throw PyTypeError.Create($"{name} expected {count} arguments, got {args.Length}");
                    return body(args);
                });
            Function("get_cache_token", 0, args => PyInt.From((long)_invalidationCounter));
            Function("_abc_init", 1, args => Init(args[0]));
            Function("_abc_register", 2, args => Register(args[0], args[1]));
            Function("_abc_instancecheck", 2, args => InstanceCheck(args[0], args[1]));
            Function("_abc_subclasscheck", 2, args => SubclassCheck(args[0], args[1]));
            Function("_get_dump", 1, args =>
            {
                var impl = Impl(args[0]);
                return PyTuple.From(new PyObject[]
                {
                    Copy(impl.Registry), Copy(impl.Cache), Copy(impl.NegativeCache), PyInt.From((long)impl.NegativeVersion),
                });
            });
            Function("_reset_registry", 1, args =>
            {
                Impl(args[0]).Registry?.Clear();
                return PyNone.Instance;
            });
            Function("_reset_caches", 1, args =>
            {
                var impl = Impl(args[0]);
                impl.Cache?.Clear();
                impl.NegativeCache?.Clear();
                return PyNone.Instance;
            });
            return module;
        }

        private static PyObject Copy(PySet set) => set == null ? PyNone.Instance : new PySet(set.Items);

        // _get_impl
        private static AbcData Impl(PyObject self) => self.GetAttribute("_abc_impl") as AbcData
            ?? throw PyTypeError.Create("_abc_impl is set to a wrong type");

        private static PyObject NewRef(PyObject target, PyObject callback = null) =>
            PyType.WeakRefType.Call(callback == null ? new[] { target } : new[] { target, callback }, null);

        // _in_weak_set
        private static bool InWeakSet(PySet set, PyObject target)
        {
            if (set == null || set.Length() == 0) return false;
            try
            {
                return set.Contains(NewRef(target)).Value;
            }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                return false;
            }
        }

        // _add_to_weak_set: the callback takes a dead reference out of the set.
        private static void AddToWeakSet(ref PySet set, PyObject target)
        {
            set ??= new PySet();
            var owner = set;
            set.Add(NewRef(target, new PyBuiltinFunction("_destroy", args =>
            {
                owner.Discard(args[0]);
                return PyNone.Instance;
            })));
        }

        // _PyObject_IsAbstract
        private static bool IsAbstract(PyObject value)
        {
            try
            {
                return value.GetAttribute("__isabstractmethod__").IsTrue();
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return false;
            }
        }

        private static PyObject AttributeOrNull(PyObject target, PyObject name)
        {
            try
            {
                return target.GetAttribute(((PyString)name).Value);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return null;
            }
        }

        private static IEnumerable<PyObject> Iterate(PyObject iterable)
        {
            var iterator = iterable.GetIterator();
            while (true)
            {
                PyObject item;
                try { item = iterator.Next(); }
                catch (PythonException error) when (error.PyException is PyStopIteration) { yield break; }
                yield return item;
            }
        }

        // compute_abstract_methods
        private static void ComputeAbstractMethods(PyObject self)
        {
            var abstracts = new PySet();
            var items = self.GetAttribute("__dict__").GetAttribute("items").Call(Array.Empty<PyObject>(), null);
            foreach (var item in new List<PyObject>(Iterate(items)))
            {
                var pair = item.AsTuple();
                if (pair.Length() != 2)
                    throw PyTypeError.Create("items() returned item which size is not 2");
                if (IsAbstract(pair.Items[1]))
                    abstracts.Add(pair.Items[0]);
            }
            if (self.GetAttribute("__bases__") is not PyTuple bases)
                throw PyTypeError.Create("__bases__ is not tuple");
            foreach (var entry in bases.Items)
            {
                var inherited = AttributeOrNull(entry, PyString.From("__abstractmethods__"));
                if (inherited == null) continue;
                foreach (var key in Iterate(inherited))
                {
                    var value = AttributeOrNull(self, key);
                    if (value != null && IsAbstract(value))
                        abstracts.Add(key);
                }
            }
            self.SetAttribute("__abstractmethods__", new PyFrozenSet(abstracts.Items));
        }

        // _abc__abc_init
        private static PyObject Init(PyObject self)
        {
            ComputeAbstractMethods(self);
            self.SetAttribute("_abc_impl", new AbcData());
            if (self is PyClass cls && cls.ClassDict.TryGetValue("__abc_tpflags__", out var flags))
            {
                if (flags is PyInt value)
                {
                    int bits = value.ToInt() & PyType.COLLECTION_FLAGS;
                    if (bits == PyType.COLLECTION_FLAGS)
                        throw PyTypeError.Create("__abc_tpflags__ cannot be both Py_TPFLAGS_SEQUENCE and Py_TPFLAGS_MAPPING");
                    cls.CollectionFlags |= bits;
                }
                cls.ClassDict.Remove("__abc_tpflags__");
            }
            return PyNone.Instance;
        }

        // _abc__abc_register_impl
        private static PyObject Register(PyObject self, PyObject subclass)
        {
            if (subclass is not PyType registered)
                throw PyTypeError.Create("Can only register classes");
            if (PyAbstract.IsSubclass(subclass, self))
                return subclass;
            // Tested after "already a subclass": X.register(X) is a no-op.
            if (PyAbstract.IsSubclass(self, subclass))
                throw PyRuntimeError.Create("Refusing to create an inheritance cycle");
            var impl = Impl(self);
            AddToWeakSet(ref impl.Registry, subclass);
            _invalidationCounter++;
            if (self is PyType type && (type.CollectionFlags & PyType.COLLECTION_FLAGS) != 0)
                registered.SetCollectionFlagRecursive(type.CollectionFlags & PyType.COLLECTION_FLAGS);
            return subclass;
        }

        private static PyObject AskSubclassCheck(PyObject self, PyObject subclass) =>
            self.GetAttribute("__subclasscheck__").Call(new[] { subclass }, null);

        // _abc__abc_instancecheck_impl
        private static PyObject InstanceCheck(PyObject self, PyObject instance)
        {
            var impl = Impl(self);
            var subclass = instance.GetAttribute("__class__");
            if (InWeakSet(impl.Cache, subclass))
                return PyBool.True;
            var subtype = instance.GetPyType();
            if (ReferenceEquals(subtype, subclass))
            {
                if (impl.NegativeVersion == _invalidationCounter && InWeakSet(impl.NegativeCache, subclass))
                    return PyBool.False;
                return AskSubclassCheck(self, subclass);
            }
            var result = AskSubclassCheck(self, subclass);
            return result.IsTrue() ? result : AskSubclassCheck(self, subtype);
        }

        // subclasscheck_check_registry
        private static bool CheckRegistry(AbcData impl, PyObject subclass)
        {
            if (InWeakSet(impl.Registry, subclass))
                return true;
            if (impl.Registry == null)
                return false;
            foreach (var entry in new List<PyObject>(impl.Registry.Items))
            {
                var target = ((PyWeakRef)entry).Referent;
                if (target != null && PyAbstract.IsSubclass(subclass, target))
                {
                    AddToWeakSet(ref impl.Cache, subclass);
                    return true;
                }
            }
            return false;
        }

        // _abc__abc_subclasscheck_impl
        private static PyObject SubclassCheck(PyObject self, PyObject subclass)
        {
            if (subclass is not PyType type)
                throw PyTypeError.Create("issubclass() arg 1 must be a class");
            var impl = Impl(self);
            if (InWeakSet(impl.Cache, subclass))
                return PyBool.True;
            if (impl.NegativeVersion < _invalidationCounter)
            {
                impl.NegativeCache?.Clear();
                impl.NegativeVersion = _invalidationCounter;
            }
            else if (InWeakSet(impl.NegativeCache, subclass))
            {
                return PyBool.False;
            }

            var ok = self.GetAttribute("__subclasshook__").Call(new[] { subclass }, null);
            if (ReferenceEquals(ok, PyBool.True))
            {
                AddToWeakSet(ref impl.Cache, subclass);
                return PyBool.True;
            }
            if (ReferenceEquals(ok, PyBool.False))
            {
                AddToWeakSet(ref impl.NegativeCache, subclass);
                return PyBool.False;
            }
            if (ok is not PyNotImplemented)
                throw PyAssertionError.Create("__subclasshook__ must return either False, True, or NotImplemented");

            foreach (var entry in type.MRO)
                if (ReferenceEquals(entry, self))
                {
                    AddToWeakSet(ref impl.Cache, subclass);
                    return PyBool.True;
                }

            if (CheckRegistry(impl, subclass))
                return PyBool.True;

            var subclasses = self.GetAttribute("__subclasses__").Call(Array.Empty<PyObject>(), null);
            if (subclasses is not PyList list)
                throw PyTypeError.Create("__subclasses__() must return a list");
            foreach (var entry in new List<PyObject>(list.Items))
                if (PyAbstract.IsSubclass(subclass, entry))
                {
                    AddToWeakSet(ref impl.Cache, subclass);
                    return PyBool.True;
                }

            AddToWeakSet(ref impl.NegativeCache, subclass);
            return PyBool.False;
        }

        private sealed class AbcData : PyObject
        {
            internal PySet Registry;
            internal PySet Cache;
            internal PySet NegativeCache;
            internal ulong NegativeVersion = _invalidationCounter;

            public override string GetTypeName() => "_abc_data";
        }
    }
}
