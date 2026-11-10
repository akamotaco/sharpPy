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
                    return PyString.From(System.BitConverter.IsLittleEndian ? "IEEE, little-endian" : "IEEE, big-endian");
                }));
            // none_new, ellipsis_new and notimplemented_new
            foreach (var singleton in new PyObject[] { PyNone.Instance, PyEllipsis.Instance, PyNotImplemented.Instance })
            {
                var type = singleton.GetPyType();
                type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
                {
                    if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                        throw PyTypeError.Create($"{type.Name} takes no arguments");
                    return singleton;
                }, 0, int.MaxValue, acceptsKwargs: true);
            }
            PyCell.InstallNew();
            PyType.InstallExceptionAllocators();
            PyUnicodeError.InstallNew();
            // ellipsis_reduce, notimplemented_reduce
            Method(PyType.EllipsisType, "__reduce__", (self, args) => PyString.From("Ellipsis"));
            Method(PyNotImplemented.Instance.GetPyType(), "__reduce__", (self, args) => PyString.From("NotImplemented"));
            // range_reduce is METH_VARARGS and reads none of them; slice_reduce is METH_NOARGS.
            PyType.RangeType.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", PyType.RangeType, (self, args, kwargs) => PyTuple.From(new PyObject[]
            {
                PyType.RangeType,
                PyTuple.From(new[] { self.GetAttribute("start"), self.GetAttribute("stop"), self.GetAttribute("step") }),
            }));
            Method(PyType.SliceType, "__reduce__", (self, args) => PyTuple.From(new PyObject[]
            {
                PyType.SliceType,
                PyTuple.From(new[] { self.GetAttribute("start"), self.GetAttribute("stop"), self.GetAttribute("step") }),
            }));
            Slot(PyType.GenericAliasType, "__getitem__", (self, args) => PyGenericAlias.GetItemSlot(self, args[0]));
            MethodO(PyType.GenericAliasType, "__mro_entries__", (self, args) => PyGenericAlias.MroEntries(self));
            Method(PyType.GenericAliasType, "__reduce__", (self, args) => PyGenericAlias.Reduce(self));
            foreach (var sequence in new[] { PyType.ListType, PyType.TupleType, PyType.RangeType, PyType.MemoryViewType })
                sequence.CollectionFlags = PyType.TPFLAGS_SEQUENCE;
            foreach (var mapping in new[] { PyType.DictType, PyType.MappingProxyType })
                mapping.CollectionFlags = PyType.TPFLAGS_MAPPING;
            PyByteArray.InstallNew();
            InstallFrameAttributes(PyType.CoroutineType, "cr", self => ((SharpPy.Core.PyCoroutine)self).Frame, self => ((SharpPy.Core.PyCoroutine)self).IsFinished);
            // async_gen_memberlist: {"ag_running", T_BOOL, offsetof(PyAsyncGenObject, ag_running_async), READONLY}
            PyType.AsyncGeneratorType.TypeDict["ag_running"] = new PyMemberDescriptor("ag_running", PyType.AsyncGeneratorType,
                self => PyBool.FromBool(((SharpPy.Core.PyAsyncGenerator)self).Running), readOnly: true);
            InstallFrameAttributes(PyType.AsyncGeneratorType, "ag", self => ((SharpPy.Core.PyAsyncGenerator)self).Frame, self => false);
            // coro_memberlist: {"cr_origin", T_OBJECT, READONLY}
            PyType.CoroutineType.TypeDict["cr_origin"] = new PyMemberDescriptor("cr_origin", PyType.CoroutineType,
                self => ((SharpPy.Core.PyCoroutine)self).Origin, readOnly: true);
            PyType.CoroutineType.TypeDict["cr_await"] = new PyGetSetDescriptor("cr_await", PyType.CoroutineType,
                self => ((SharpPy.Core.PyCoroutine)self).YieldFrom ?? PyNone.Instance);
            PyCallableWrapper.Install(PyType.ClassMethodType, "classmethod");
            PyCallableWrapper.Install(PyType.StaticMethodType, "staticmethod");
            Slot(PyType.GenericAliasType, "__ror__", (self, args) => UnionOnTheRight(self, args[0]));
            Slot(PyType.UnionType, "__ror__", (self, args) => UnionOnTheRight(self, args[0]));
            Slot(PyType.UnionType, "__or__", (self, args) => PyUnionType.Or(self, args[0]));
            TypeTables.Install();
            PyExceptionTable.Install();
            InstallContainers();
            InstallViews();
            InstallIterators();
            PyType.ModuleType.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", PyType.ModuleType,
                (cls, args, kwargs) => PyModule.Allocate((PyType)cls), 0, int.MaxValue, acceptsKwargs: true);
            // module_dir: a module may answer for itself with a __dir__ of its own.
            PyType.ModuleType.TypeDict["__dir__"] = new PyMethodDescriptor("__dir__", PyType.ModuleType, (self, args, kwargs) =>
            {
                if (self.GetAttribute("__dict__") is not PyDict names)
                    throw PyTypeError.Create("<module>.__dict__ is not a dictionary");
                return names.ContainsKey(PyString.From("__dir__"))
                    ? names.GetItem(PyString.From("__dir__")).Call(Array.Empty<PyObject>(), null)
                    : PyList.FromIterable(names);
            }, 0, 0);
            PyType.ModuleType.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", PyType.ModuleType, (self, args, kwargs) =>
            {
                var name = args.Length > 0 ? args[0] : kwargs?.GetItem(PyString.From("name"));
                if (name == null)
                    throw PyArgs.MissingArgument("module", "name", 1);
                if (name is not PyString text)
                    throw PyArgs.BadArgument("module", "argument 'name'", "str", name);
                PyObject doc = args.Length > 1 ? args[1] : null;
                if (doc == null && kwargs != null && kwargs.ContainsKey(PyString.From("doc")))
                    doc = kwargs.GetItem(PyString.From("doc"));
                ((PyModule)self).Initialize(text.Value, doc ?? PyNone.Instance);
                return PyNone.Instance;
            }, 0, int.MaxValue);
            InstallSurfaces();
            // type_ready_fill_dict: every builtin's dict in CPython's layout, once all is registered.
            PySlotWrappers.LayOut();
        }

        private static void Slot(PyType type, string name, Func<PyObject, PyObject[], PyObject> operation)
        {
            if (!type.TypeDict.ContainsKey(name))
                type.TypeDict[name] = new PyWrapperDescriptor(name, type, (self, args, kwargs) => operation(self, args));
        }

        // METH_NOARGS and METH_O: the descriptor checks the convention (PyArgs.Method).
        private static void Method(PyType type, string name, Func<PyObject, PyObject[], PyObject> operation)
        {
            if (!type.TypeDict.ContainsKey(name))
                type.TypeDict[name] = PyArgs.Method(type, name, PyCall.NoArgs, operation);
        }

        private static void MethodO(PyType type, string name, Func<PyObject, PyObject[], PyObject> operation)
        {
            if (!type.TypeDict.ContainsKey(name))
                type.TypeDict[name] = PyArgs.Method(type, name, PyCall.One, operation);
        }

        private static void InstallObject()
        {
            // The __sizeof__ family: CPython's own formulas over the layouts in TypeLayout.g.cs. What
            // depends on CPython's allocation history (list and bytearray over-allocation, a str's
            // UTF-8 cache, a class's shared keys) is the size of a fresh object; docs/todo.md says so.
            Method(PyType.ObjectType, "__sizeof__", (self, args) => PyInt.From(ObjectSizeOf(self)));
            // int___sizeof___impl: offsetof(PyLongObject, long_value.ob_digit) + max(ndigits, 1) * 4
            Method(PyType.IntType, "__sizeof__", (self, args) =>
            {
                var value = self is PyBool flag ? (flag.Value ? 1 : 0) : System.Numerics.BigInteger.Abs(((PyInt)self).Value);
                long digits = Math.Max(1, ((long)value.GetBitLength() + 29) / 30);
                return PyInt.From(24 + digits * 4);
            });
            // unicode___sizeof___impl: sizeof(PyASCIIObject) + len + 1 for a compact ASCII str, else
            // sizeof(PyCompactUnicodeObject) + (len + 1) * kind.
            Method(PyType.StrType, "__sizeof__", (self, args) =>
            {
                var text = (PyString)self;
                int max = 0;
                for (int u = 0; u < text.Value.Length; u += PyString.PairWidth(text.Value, u))
                    max = Math.Max(max, PyString.CodePointAtUnit(text.Value, u));
                long length = text.CodePointLength();
                if (max < 0x80) return PyInt.From(40 + length + 1);
                int kind = max < 0x100 ? 1 : max < 0x10000 ? 2 : 4;
                return PyInt.From(56 + (length + 1) * kind);
            });
            // list___sizeof___impl: the type's size + allocated * sizeof(PyObject *)
            Method(PyType.ListType, "__sizeof__", (self, args) => PyInt.From(PyType.ListType.Layout.BasicSize + 8L * ((PyList)self).RawCount()));
            // bytearray___sizeof___impl: the type's size + ob_alloc (the bytes and their NUL)
            Method(PyType.BytearrayType, "__sizeof__", (self, args) =>
            {
                long length = ((PyObject)self).Size();
                return PyInt.From(PyType.BytearrayType.Layout.BasicSize + (length == 0 ? 0 : length + 1));
            });
            // type___sizeof___impl: sizeof(PyTypeObject), or sizeof(PyHeapTypeObject) and the size of
            // its cached keys for a class whose instances keep a dict.
            Method(PyType.TypeType, "__sizeof__", (self, args) =>
                PyInt.From(self is PyClass heap && heap.IsHeapType ? 920 + (heap.InstancesHaveDict() ? 768 : 0) : 416));
            // object_subclasshook: METH_CLASS|METH_VARARGS, NotImplemented whatever it is given.
            PyType.ObjectType.TypeDict["__subclasshook__"] = new PyClassMethodDescriptor("__subclasshook__", PyType.ObjectType,
                new PyBuiltinFunction("__subclasshook__", (args, kwargs) => PyNotImplemented.Instance));
        }

        private static void InstallSurfaces()
        {
            // module_members and module_getsets
            var module = PyType.ModuleType;
            module.TypeDict["__dict__"] = new PyMemberDescriptor("__dict__", module, self => ((PyModule)self).DictObject, readOnly: true);
            module.TypeDict["__annotations__"] = new PyGetSetDescriptor("__annotations__", module,
                // module_get_annotations: the dict's own, or a new empty one stored there.
                self =>
                {
                    var dict = ((PyModule)self).DictObject;
                    if (dict.TryGetStoredValue(PyString.From("__annotations__"), out var found)) return found;
                    var created = new PyDict();
                    dict.SetItem(PyString.From("__annotations__"), created);
                    return created;
                },
                (self, value) => ((PyModule)self).DictObject.SetItem(PyString.From("__annotations__"), value),
                // module_set_annotations with NULL
                deleter: self =>
                {
                    var dict = ((PyModule)self).DictObject;
                    if (!dict.TryGetStoredValue(PyString.From("__annotations__"), out _))
                        throw PyAttributeError.Create("__annotations__");
                    dict.DelItem(PyString.From("__annotations__"));
                });
            // module_getattro and super_getattro are the slots of their own types, so the wrappers.
            foreach (var type in new[] { PyType.ModuleType, PyType.SuperType })
                type.TypeDict["__getattribute__"] = new PyWrapperDescriptor("__getattribute__", type, (self, args, kwargs) =>
                {
                    PyObject.AttributeName(args[0]);
                    var name = (PyString)args[0];
                    return self.TpGetattro(name.Value);
                });
            // method_reduce: (getattr, (im_self, im_func.__name__))
            Method(PyType.MethodType, "__reduce__", (self, args) =>
            {
                var method = (PyMethod)self;
                return PyTuple.From(PyImportSystem.Import("builtins").GetAttribute("getattr"),
                    PyTuple.From(method.Instance, method.Function.GetAttribute("__name__")));
            });
            // gen_finalize as __del__, and gen_sizeof / frame_sizeof / code_sizeof.
            foreach (var type in new[] { PyType.GeneratorType, PyType.CoroutineType, PyType.AsyncGeneratorType })
            {
                Slot(type, "__del__", (self, args) => { PyFinalizer.FinalizeGenerator(self); return PyNone.Instance; });
                var owner = type;
                string codeName = ReferenceEquals(owner, PyType.GeneratorType) ? "gi_code"
                    : ReferenceEquals(owner, PyType.CoroutineType) ? "cr_code" : "ag_code";
                Method(type, "__sizeof__", (self, args) =>
                {
                    var code = (PyCodeObject)self.GetAttribute(codeName);
                    return PyInt.From(owner.Layout.BasicSize + owner.Layout.ItemSize * (long)(code.NLocalsPlus + code.StackSize));
                });
            }
            Method(PyFrame.FramePyType, "__sizeof__", (self, args) =>
            {
                var code = ((PyFrame)self).Code;
                return PyInt.From(PyFrame.FramePyType.Layout.BasicSize + PyFrame.FramePyType.Layout.ItemSize * (long)(code.NLocalsPlus + code.StackSize));
            });
            // code_sizeof: _PyObject_VAR_SIZE, rounded up to a pointer
            Method(PyCodeObject.CodePyType, "__sizeof__", (self, args) =>
            {
                long units = ((PyBytes)self.GetAttribute("co_code")).Value.Length / 2;
                long size = PyCodeObject.CodePyType.Layout.BasicSize + PyCodeObject.CodePyType.Layout.ItemSize * units;
                return PyInt.From((size + 7) & ~7L);
            });
            // ga_dir: dir(origin), then the alias's own names that dir did not list.
            Method(PyType.GenericAliasType, "__dir__", (self, args) =>
            {
                var names = (PyList)PyImportSystem.Import("builtins").GetAttribute("dir").Call(new[] { ((PyGenericAlias)self).Origin }, null);
                foreach (var own in PyGenericAlias.AttrExceptions)
                    if (!names.ContainsItem(PyString.From(own)).Value)
                        names.Append(PyString.From(own));
                return names;
            });
        }

        // object___sizeof___impl: tp_basicsize, and Py_SIZE(self) * tp_itemsize for a variable-size type.
        internal static long ObjectSizeOf(PyObject self)
        {
            var layout = self.GetPyType().Layout;
            long size = layout.BasicSize;
            if (layout.ItemSize > 0)
                size += layout.ItemSize * (self switch { PyBytes bytes => bytes.Value.Length, PyTuple tuple => tuple.Items.Length, _ => 0 });
            return size;
        }

        private static void InstallNumbers()
        {
            PyNumberSlots.Install();
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

            var complex = PyType.ComplexType;
            Slot(complex, "__neg__", (self, args) => self.NbNegative());
            Slot(complex, "__pos__", (self, args) => self.NbPositive());
            // complex___complex___impl: an exact complex, a subclass gives up its type.
            Method(complex, "__complex__", (self, args) => self.IsExactly(PyType.ComplexType)
                ? self : new PyComplex(((PyComplex)self).Real, ((PyComplex)self).Imag));
        }

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
            // unicode_repeat, str's own sq_repeat: a subclass's __mul__ is not asked again.
            Slot(PyType.StrType, "__mul__", (self, args) => ((PyString)self).Multiply(args[0]));
            Slot(PyType.StrType, "__rmul__", (self, args) => ((PyString)self).Multiply(args[0]));
            Slot(PyType.StrType, "__rmod__", (self, args) => args[0] is PyString format ? format.Modulo(self) : PyNotImplemented.Instance);

            Method(PyType.BytesType, "__bytes__", (self, args) => self);
            // bool_and and its reflections: two bools stay a bool, anything else is an int.
            foreach (var (name, operation) in new (string, Func<PyObject, PyObject, PyObject>)[]
            {
                ("and", (a, b) => a.BitwiseAnd(b)), ("or", (a, b) => a.BitwiseOr(b)), ("xor", (a, b) => a.BitwiseXor(b)),
            })
                Slot(PyType.BoolType, $"__r{name}__", (self, args) => args[0] is PyInt or PyBool ? operation(args[0], self) : PyNotImplemented.Instance);
            Slot(PyNotImplemented.Instance.GetPyType(), "__bool__", (self, args) => PyBool.FromBool(self.Bool()));
            // bytes_mod and bytearray_mod are nb_remainder: __rmod__(self, other) is mod(other, self).
            Slot(PyType.BytesType, "__mod__", (self, args) => self.Modulo(args[0]));
            Slot(PyType.BytesType, "__rmod__", (self, args) => args[0] is PyBytes format ? format.Modulo(self) : PyNotImplemented.Instance);

            var array = PyType.BytearrayType;
            Slot(array, "__len__", (self, args) => PyInt.From(self.Len()));
            Slot(array, "__iadd__", (self, args) => ((PyByteArray)self).InPlaceConcat(args[0]));
            Slot(array, "__imul__", (self, args) => ((PyByteArray)self).InPlaceRepeat(args[0]));
            Slot(array, "__contains__", (self, args) => self.ContainsItem(args[0]));
            Slot(array, "__getitem__", (self, args) => self.MpSubscript(args[0]));
            Slot(array, "__mod__", (self, args) => self.Modulo(args[0]));
            Slot(array, "__rmod__", (self, args) => args[0] is PyByteArray format ? format.Modulo(self) : PyNotImplemented.Instance);
        }

        private static PyObject SetOperand(PyObject value) =>
            (PyObject)(value as PySet) ?? (value as PyFrozenSet);

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
            Slot(PyType.FrozenSetType, "__len__", (self, args) => PyInt.From((self as PyFrozenSet).Len()));
            // frozenset_methods: __contains__ METH_COEXIST beside sq_contains
            PyType.FrozenSetType.TypeDict["__contains__"] = new PyMethodDescriptor("__contains__", PyType.FrozenSetType, (self, args, kwargs) =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"frozenset.__contains__() takes exactly one argument ({args.Length} given)");
                return (self as PyFrozenSet).ContainsItem(args[0]);
            }, acceptsKwargs: true);
            Slot(PyType.FrozenSetType, "__iter__", (self, args) => (self as PyFrozenSet).Iter());

            Method(PyType.DictType, "__reversed__", (self, args) =>
            {
                var keys = new List<PyObject>((self as PyDict).InternalDict.Count);
                foreach (var entry in (self as PyDict).GetInternalDict()) keys.Add(entry.Key);
                keys.Reverse();
                return new PyListIterator(new PyList(keys), "dict_reversekeyiterator");
            });

            // range_count and range_index: arithmetic for an int, a search for anything else.
            MethodO(PyType.RangeType, "count", (self, args) =>
            {
                if (args[0] is PyInt or PyBool && args[0] is not PyIntSubclass)
                    return PyInt.From(self.Contains(args[0]).Value ? 1 : 0);
                long count = 0;
                foreach (var item in PySet.IterableItems(self))
                    if (PyList.AreEqual(item, args[0])) count++;
                return PyInt.From(count);
            });
            MethodO(PyType.RangeType, "index", (self, args) =>
            {
                var range = (PyRange)self;
                if (args[0] is PyInt or PyBool && args[0] is not PyIntSubclass)
                {
                    if (!self.Contains(args[0]).Value) throw PyValueError.Create($"{args[0].ToRepr().Value} is not in range");
                    var value = args[0] is PyBool truth ? (truth.Value ? BigInteger.One : BigInteger.Zero) : ((PyInt)args[0]).Value;
                    return PyInt.From((value - range.Start) / range.Step);
                }
                long index = 0;
                foreach (var item in PySet.IterableItems(self))
                {
                    if (PyList.AreEqual(item, args[0])) return PyInt.From(index);
                    index++;
                }
                throw PyValueError.Create("sequence.index(x): x not in sequence");
            });
            Method(PyType.RangeType, "__reversed__", (self, args) => self.GetItem(new PySlice(PyNone.Instance, PyNone.Instance, PyInt.From(-1))).GetIterator());

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
            foreach (var iterable in new PyObject[] { new PyList(), PyTuple.From(), PyString.From(""), new PyRange(0L) })
                Method(iterable.GetIterator().GetPyType(), "__length_hint__", (self, args) => ((PyIterator)self).RemainingHint());
        }
    }
}
