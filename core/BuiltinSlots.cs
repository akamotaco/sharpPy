using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpPy
{
    /// <summary>
    /// The slot wrappers CPython's add_operators puts in the dict of every builtin type. The operations
    /// live on the PyObject virtuals; this makes them reachable as attributes: dir(), hasattr(), T.__add__
    /// and super().__add__ all read the type's dict. Each wrapper works on the stored value, so a subclass
    /// reaching it through super() does not land in its own method again.
    /// </summary>
    internal static class BuiltinSlots
    {
        private static bool _installed;

        internal static void Install()
        {
            if (_installed) return;
            _installed = true;
            InstallObject();
            InstallNumbers();
            InstallText();
            PyGenericAlias.Install();
            PySlotWrappers.Install();
            PyComplex.InstallNew();
            // bool_new
            PyType.BoolType.TypeDict["__new__"] = new PyStaticBuiltinMethod("__new__", (args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("bool() takes no keyword arguments");
                if (args.Length > 2)
                    throw PyTypeError.Create($"bool expected at most 1 argument, got {args.Length - 1}");
                return PyBool.FromBool(args.Length > 1 && args[1].IsTrue());
            }, PyType.BoolType);
            PyExceptionSlots.InstallReduce();
            // float___getformat___impl
            PyType.FloatType.TypeDict["__getformat__"] = new PyClassMethodDescriptor("__getformat__", PyType.FloatType,
                new PyBuiltinFunction("__getformat__", (args, kwargs) =>
                {
                    if (args.Length != 2 || args[1] is not PyString kind)
                        throw PyTypeError.Create("__getformat__() argument must be str");
                    if (kind.Value != "double" && kind.Value != "float")
                        throw PyValueError.Create("__getformat__() argument 1 must be 'double' or 'float'");
                    return new PyString(System.BitConverter.IsLittleEndian ? "IEEE, little-endian" : "IEEE, big-endian");
                }));
            // none_new, ellipsis_new and notimplemented_new
            foreach (var singleton in new PyObject[] { PyNone.Instance, PyEllipsis.Instance, PyNotImplemented.Instance })
            {
                var type = singleton.GetPyType();
                type.ConstructedByNewSlot = true;
                type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
                {
                    if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                        throw PyTypeError.Create($"{type.Name} takes no arguments");
                    return singleton;
                }, 0, int.MaxValue, acceptsKwargs: true);
            }
            PyCell.InstallNew();
            PyUnicodeError.InstallNew();
            PyType.MappingProxyType.ConstructedByNewSlot = true;
            // ellipsis_reduce, notimplemented_reduce
            Method(PyType.EllipsisType, "__reduce__", (self, args) => new PyString("Ellipsis"));
            Method(PyNotImplemented.Instance.GetPyType(), "__reduce__", (self, args) => new PyString("NotImplemented"));
            // range_reduce, slice_reduce
            Method(PyType.RangeType, "__reduce__", (self, args) => new PyTuple(new PyObject[]
            {
                PyType.RangeType,
                new PyTuple(new[] { self.GetAttribute("start"), self.GetAttribute("stop"), self.GetAttribute("step") }),
            }));
            Method(PyType.SliceType, "__reduce__", (self, args) => new PyTuple(new PyObject[]
            {
                PyType.SliceType,
                new PyTuple(new[] { self.GetAttribute("start"), self.GetAttribute("stop"), self.GetAttribute("step") }),
            }));
            Slot(PyType.GenericAliasType, "__repr__", (self, args) => PyGenericAlias.Repr(self));
            Slot(PyType.GenericAliasType, "__getitem__", (self, args) => PyGenericAlias.GetItemSlot(self, args[0]));
            MethodO(PyType.GenericAliasType, "__mro_entries__", (self, args) => PyGenericAlias.MroEntries(self));
            Method(PyType.GenericAliasType, "__reduce__", (self, args) => PyGenericAlias.Reduce(self));
            foreach (var sequence in new[] { PyType.ListType, PyType.TupleType, PyType.RangeType, PyType.MemoryViewType })
                sequence.CollectionFlags = PyType.TPFLAGS_SEQUENCE;
            foreach (var mapping in new[] { PyType.DictType, PyType.MappingProxyType })
                mapping.CollectionFlags = PyType.TPFLAGS_MAPPING;
            PyByteArray.InstallNew();
            InstallFrameAttributes(PyType.CoroutineType, "cr", self => ((SharpPy.Core.PyCoroutine)self).Frame, self => ((SharpPy.Core.PyCoroutine)self).IsFinished);
            InstallFrameAttributes(PyType.AsyncGeneratorType, "ag", self => ((SharpPy.Core.PyAsyncGenerator)self).Frame, self => false);
            PyType.CoroutineType.TypeDict["cr_origin"] = new PyGetSetDescriptor("cr_origin", PyType.CoroutineType,
                self => ((SharpPy.Core.PyCoroutine)self).Origin);
            PyType.CoroutineType.TypeDict["cr_await"] = new PyGetSetDescriptor("cr_await", PyType.CoroutineType,
                self => ((SharpPy.Core.PyCoroutine)self).YieldFrom ?? PyNone.Instance);
            PyCallableWrapper.Install(PyType.ClassMethodType, "classmethod");
            PyCallableWrapper.Install(PyType.StaticMethodType, "staticmethod");
            Slot(PyType.TypeType, "__ror__", (self, args) => UnionOnTheRight(self, args[0]));
            Slot(PyType.GenericAliasType, "__ror__", (self, args) => UnionOnTheRight(self, args[0]));
            Slot(PyType.UnionType, "__ror__", (self, args) => UnionOnTheRight(self, args[0]));
            Slot(PyType.UnionType, "__or__", (self, args) => PyUnionType.Or(self, args[0]));
            TypeTables.Install();
            PyExceptionTable.Install();
            InstallContainers();
            InstallViews();
            InstallIterators();
            TypeTables.Finish();
            PyType.ModuleType.ConstructedByNewSlot = true;
            PyType.ModuleType.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", PyType.ModuleType,
                (cls, args, kwargs) => PyModule.Allocate((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            // module_dir: a module may answer for itself with a __dir__ of its own.
            PyType.ModuleType.TypeDict["__dir__"] = new PyMethodDescriptor("__dir__", PyType.ModuleType, (self, args, kwargs) =>
            {
                if (self.GetAttribute("__dict__") is not PyDict names)
                    throw PyTypeError.Create("<module>.__dict__ is not a dictionary");
                return names.ContainsKey(new PyString("__dir__"))
                    ? names.GetItem(new PyString("__dir__")).Call(Array.Empty<PyObject>(), null)
                    : PyList.FromIterable(names);
            }, 0, 0);
            PyType.ModuleType.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", PyType.ModuleType, (self, args, kwargs) =>
            {
                var name = args.Length > 0 ? args[0] : kwargs?.GetItem(new PyString("name"));
                if (name == null)
                    throw PyTypeError.Create("module() missing required argument 'name' (pos 1)");
                if (name is not PyString text)
                    throw PyTypeError.Create($"module() argument 'name' must be str, not {name.GetTypeName()}");
                PyObject doc = args.Length > 1 ? args[1] : null;
                if (doc == null && kwargs != null && kwargs.ContainsKey(new PyString("doc")))
                    doc = kwargs.GetItem(new PyString("doc"));
                ((PyModule)self).Initialize(text.Value, doc ?? PyNone.Instance);
                return PyNone.Instance;
            }, 0, int.MaxValue);
        }

        private static void Slot(PyType type, string name, Func<PyObject, PyObject[], PyObject> operation)
        {
            if (!type.TypeDict.ContainsKey(name))
                type.TypeDict[name] = new PyWrapperDescriptor(name, type, (self, args, kwargs) => operation(self, args));
        }

        private static void Method(PyType type, string name, Func<PyObject, PyObject[], PyObject> operation)
        {
            if (type.TypeDict.ContainsKey(name))
                return;
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) =>
            {
                if (args.Length != 0)
                    throw PyTypeError.Create($"{type.Name}.{name}() takes no arguments ({args.Length} given)");
                return operation(self, args);
            });
        }

        private static void MethodO(PyType type, string name, Func<PyObject, PyObject[], PyObject> operation)
        {
            if (type.TypeDict.ContainsKey(name))
                return;
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"{type.Name}.{name}() takes exactly one argument ({args.Length} given)");
                return operation(self, args);
            });
        }

        private static void InstallObject()
        {
            Method(PyType.ObjectType, "__sizeof__", (self, args) => new PyInt(16));
            // object_subclasshook: METH_CLASS|METH_VARARGS, NotImplemented whatever it is given.
            PyType.ObjectType.TypeDict["__subclasshook__"] = new PyClassMethodDescriptor("__subclasshook__", PyType.ObjectType,
                new PyBuiltinFunction("__subclasshook__", (args, kwargs) => PyNotImplemented.Instance));
        }

        private static PyObject Int(PyObject value) => value is PyIntSubclass subclass ? subclass.GetIntValue() : value;

        private static PyObject Complex(PyObject value) => value switch
        {
            PyComplex => value,
            PyBool truth => new PyComplex(truth.Value ? 1 : 0, 0),
            PyInt number => new PyComplex((double)number.Value, 0),
            PyFloat real => new PyComplex(real.Value, 0),
            _ => null,
        };

        private static PyObject Reflected(PyObject self, PyObject other, Func<PyObject, PyObject, PyObject> operation)
        {
            var left = Complex(other);
            return left == null ? PyNotImplemented.Instance : operation(left, self);
        }

        private static void InstallNumbers()
        {
            var truth = new Func<PyObject, PyObject[], PyObject>((self, args) => PyBool.FromBool(self switch
            {
                PyIntSubclass number => number.GetIntValue().IsTrue(),
                PyFloatSubclass real => real.GetFloatValue().IsTrue(),
                PyComplex value => value.Real != 0 || value.Imag != 0,
                _ => self.Bool(),
            }));
            foreach (var type in new[] { PyType.IntType, PyType.FloatType, PyType.ComplexType })
                Slot(type, "__bool__", truth);
            Slot(PyType.RangeType, "__bool__", (self, args) =>
            {
                foreach (var first in PySet.IterableItems(self)) return PyBool.True;
                return PyBool.False;
            });
            Slot(PyType.NoneType, "__bool__", (self, args) => PyBool.False);

            Method(PyType.IntType, "is_integer", (self, args) => PyBool.True);
            Slot(PyType.IntType, "__rpow__", (self, args) => args[0] is PyInt or PyIntSubclass
                ? Int(args[0]).Power(Int(self)) : PyNotImplemented.Instance);

            var complex = PyType.ComplexType;
            Slot(complex, "__add__", (self, args) => Complex(args[0]) is { } other ? self.Add(other) : PyNotImplemented.Instance);
            Slot(complex, "__sub__", (self, args) => Complex(args[0]) is { } other ? self.Subtract(other) : PyNotImplemented.Instance);
            Slot(complex, "__mul__", (self, args) => Complex(args[0]) is { } other ? self.Multiply(other) : PyNotImplemented.Instance);
            Slot(complex, "__truediv__", (self, args) => Complex(args[0]) is { } other ? self.Divide(other) : PyNotImplemented.Instance);
            Slot(complex, "__pow__", (self, args) => Complex(args[0]) is { } other ? self.Power(other) : PyNotImplemented.Instance);
            Slot(complex, "__radd__", (self, args) => Reflected(self, args[0], (a, b) => a.Add(b)));
            Slot(complex, "__rsub__", (self, args) => Reflected(self, args[0], (a, b) => a.Subtract(b)));
            Slot(complex, "__rmul__", (self, args) => Reflected(self, args[0], (a, b) => a.Multiply(b)));
            Slot(complex, "__rtruediv__", (self, args) => Reflected(self, args[0], (a, b) => a.Divide(b)));
            Slot(complex, "__rpow__", (self, args) => Reflected(self, args[0], (a, b) => a.Power(b)));
            Slot(complex, "__neg__", (self, args) => self.NbNegative());
            Slot(complex, "__pos__", (self, args) => self.NbPositive());
            // complex___complex___impl: an exact complex, a subclass gives up its type.
            Method(complex, "__complex__", (self, args) => self.GetType() == typeof(PyComplex)
                ? self : new PyComplex(((PyComplex)self).Real, ((PyComplex)self).Imag));
        }

        private static PyObject StoredText(PyObject self) =>
            (self as PyStrSubclass)?.GetStrValue() ?? PyNativeStorage.Unwrap(self);

        // _Py_union_type_or with the operands as written: None | int.
        private static PyObject UnionOnTheRight(PyObject self, PyObject left) => PyUnionType.Or(left, self);

        // gen_getframe, gen_getrunning, gen_getsuspended and their cr_ and ag_ twins
        private static void InstallFrameAttributes(PyType type, string prefix, Func<PyObject, PyFrame> frame, Func<PyObject, bool> finished)
        {
            void Attribute(string name, Func<PyObject, PyObject> get)
            {
                if (!type.TypeDict.ContainsKey(name))
                    type.TypeDict[name] = new PyGetSetDescriptor(name, type, get);
            }
            Attribute(prefix + "_frame", self => finished(self) || frame(self).State == PyFrame.FrameState.Completed ? PyNone.Instance : frame(self));
            Attribute(prefix + "_code", self => frame(self).Code);
            Attribute(prefix + "_running", self => PyBool.FromBool(frame(self).State == PyFrame.FrameState.Executing));
            Attribute(prefix + "_suspended", self => PyBool.FromBool(!finished(self) && frame(self).State == PyFrame.FrameState.Suspended));
        }

        private static void InstallText()
        {
            // unicode_repeat reads the text; a subclass's own __mul__ is not asked again.
            Slot(PyType.StrType, "__mul__", (self, args) => StoredText(self).Multiply(args[0]));
            Slot(PyType.StrType, "__rmul__", (self, args) => StoredText(self).Multiply(args[0]));
            Slot(PyType.StrType, "__rmod__", (self, args) => args[0] is PyString format ? format.Modulo(self) : PyNotImplemented.Instance);

            Method(PyType.BytesType, "__bytes__", (self, args) => self);
            // bool_and and its reflections: two bools stay a bool, anything else is an int.
            foreach (var (name, operation) in new (string, Func<PyObject, PyObject, PyObject>)[]
            {
                ("and", (a, b) => a.BitwiseAnd(b)), ("or", (a, b) => a.BitwiseOr(b)), ("xor", (a, b) => a.BitwiseXor(b)),
            })
                Slot(PyType.BoolType, $"__r{name}__", (self, args) => args[0] is PyInt or PyBool ? operation(args[0], self) : PyNotImplemented.Instance);
            Slot(PyNotImplemented.Instance.GetPyType(), "__bool__", (self, args) => PyBool.FromBool(self.Bool()));
            Method(PyType.BytesType, "__getnewargs__", (self, args) => new PyTuple(new[] { self }));
            Slot(PyType.BytesType, "__mod__", (self, args) => self.Modulo(args[0]));
            Slot(PyType.BytesType, "__rmod__", (self, args) => args[0] is PyBytes format ? format.Modulo(self) : PyNotImplemented.Instance);

            var array = PyType.BytearrayType;
            Slot(array, "__len__", (self, args) => new PyInt(self.Len()));
            Slot(array, "__iadd__", (self, args) => ((PyByteArray)self).InPlaceConcat(args[0]));
            Slot(array, "__imul__", (self, args) => ((PyByteArray)self).InPlaceRepeat(args[0]));
            Slot(array, "__contains__", (self, args) => self.ContainsItem(args[0]));
            Slot(array, "__getitem__", (self, args) => self.MpSubscript(args[0]));
            Slot(array, "__mod__", (self, args) => self.Modulo(args[0]));
            Slot(array, "__rmod__", (self, args) => args[0] is PyByteArray format ? format.Modulo(self) : PyNotImplemented.Instance);
        }

        private static PyObject SetOperand(PyObject value) =>
            (PyObject)PyNativeStorage.ForSet(value) ?? PyNativeStorage.ForFrozenSet(value);

        private static void SetOperator(PyType type, string name, Func<PyObject, PyObject, PyObject> operation)
        {
            Slot(type, $"__{name}__", (self, args) =>
                SetOperand(args[0]) is { } other ? operation(SetOperand(self), other) : PyNotImplemented.Instance);
            Slot(type, $"__r{name}__", (self, args) =>
                SetOperand(args[0]) is { } other ? operation(other, SetOperand(self)) : PyNotImplemented.Instance);
        }

        private static void InstallContainers()
        {
            foreach (var type in new[] { PyType.SetType, PyType.FrozenSetType })
            {
                SetOperator(type, "and", (a, b) => a.BitwiseAnd(b));
                SetOperator(type, "or", (a, b) => a.BitwiseOr(b));
                SetOperator(type, "sub", (a, b) => a.Subtract(b));
                SetOperator(type, "xor", (a, b) => a.BitwiseXor(b));
            }
            Slot(PyType.FrozenSetType, "__len__", (self, args) => new PyInt(PyNativeStorage.ForFrozenSet(self).Len()));
            Slot(PyType.FrozenSetType, "__contains__", (self, args) => PyNativeStorage.ForFrozenSet(self).ContainsItem(args[0]));
            Slot(PyType.FrozenSetType, "__iter__", (self, args) => PyNativeStorage.ForFrozenSet(self).Iter());

            Method(PyType.DictType, "__reversed__", (self, args) =>
            {
                var keys = new List<PyObject>(PyNativeStorage.ForDict(self).InternalDict.Count);
                foreach (var entry in PyNativeStorage.ForDict(self).GetInternalDict()) keys.Add(entry.Key);
                keys.Reverse();
                return new PyListIterator(new PyList(keys), "dict_reversekeyiterator");
            });

            // range_count and range_index: arithmetic for an int, a search for anything else.
            MethodO(PyType.RangeType, "count", (self, args) =>
            {
                if (args[0] is PyInt or PyBool && args[0] is not PyIntSubclass)
                    return new PyInt(self.Contains(args[0]).Value ? 1 : 0);
                long count = 0;
                foreach (var item in PySet.IterableItems(self))
                    if (PyList.AreEqual(item, args[0])) count++;
                return new PyInt(count);
            });
            MethodO(PyType.RangeType, "index", (self, args) =>
            {
                var range = (PyRange)self;
                if (args[0] is PyInt or PyBool && args[0] is not PyIntSubclass)
                {
                    if (!self.Contains(args[0]).Value) throw PyValueError.Create($"{args[0].ToRepr().Value} is not in range");
                    var value = args[0] is PyBool truth ? (truth.Value ? BigInteger.One : BigInteger.Zero) : ((PyInt)args[0]).Value;
                    return new PyInt((value - range.Start) / range.Step);
                }
                long index = 0;
                foreach (var item in PySet.IterableItems(self))
                {
                    if (PyList.AreEqual(item, args[0])) return new PyInt(index);
                    index++;
                }
                throw PyValueError.Create("sequence.index(x): x not in sequence");
            });
            Method(PyType.RangeType, "__reversed__", (self, args) => self.GetItem(new PySlice(PyNone.Instance, PyNone.Instance, new PyInt(-1))).GetIterator());

            foreach (var generic in new[] { PyType.BaseExceptionGroupType, PyType.GetIteratorType("enumerate"), PyType.MappingProxyType, PyType.WeakRefType })
                if (!generic.TypeDict.ContainsKey("__class_getitem__"))
                    generic.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(generic);

            PyBaseExceptionGroup.InstallOn(PyType.BaseExceptionGroupType);

            var proxy = PyType.MappingProxyType;
            Slot(proxy, "__iter__", (self, args) => self.Iter());
            PyMappingProxy.Install();
        }

        private static PyType ViewType(string name) => name switch
        {
            "dict_keys" => PyType.DictKeysType,
            "dict_values" => PyType.DictValuesType,
            _ => PyType.DictItemsType,
        };

        private static void InstallViews()
        {
            foreach (var name in new[] { "dict_keys", "dict_items" })
            {
                var view = ViewType(name);
                foreach (var (slot, operation) in new (string, Func<PyObject, PyObject, PyObject>)[]
                {
                    ("and", (a, b) => a.BitwiseAnd(b)), ("or", (a, b) => a.BitwiseOr(b)),
                    ("sub", (a, b) => a.Subtract(b)), ("xor", (a, b) => a.BitwiseXor(b)),
                })
                {
                    Slot(view, $"__{slot}__", (self, args) => operation(self, args[0]));
                    Slot(view, $"__r{slot}__", (self, args) => operation(new PySet(new List<PyObject>(PySet.IterableItems(args[0]))), new PySet(new List<PyObject>(PySet.IterableItems(self)))));
                }
                MethodO(view, "isdisjoint", (self, args) =>
                {
                    var mine = new PySet(new List<PyObject>(PySet.IterableItems(self)));
                    foreach (var item in PySet.IterableItems(args[0]))
                        if (mine.Contains(item).Value) return PyBool.False;
                    return PyBool.True;
                });
            }
            foreach (var name in new[] { "dict_keys", "dict_values", "dict_items" })
            {
                var view = ViewType(name);
                Method(view, "__reversed__", (self, args) =>
                {
                    var items = new List<PyObject>(PySet.IterableItems(self));
                    items.Reverse();
                    return new PyListIterator(new PyList(items), name.Replace("dict_", "dict_reverse").TrimEnd('s') + "iterator");
                });
            }
        }

        private static void InstallIterators()
        {
            foreach (var iterable in new PyObject[] { new PyList(), new PyTuple(), new PyString(""), new PyRange(0L) })
                Method(iterable.GetIterator().GetPyType(), "__length_hint__", (self, args) => ((PyIterator)self).RemainingHint());
        }
    }
}
