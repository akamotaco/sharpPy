using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible implementation of the 'type' metaclass
    /// This is the base metaclass for all Python classes
    /// 
    /// Key characteristics:
    /// - type.__class__ == type (type is its own metaclass)
    /// - type.__bases__ == (object,)
    /// - type.__init__ exists and is callable from subclasses
    /// - type.__new__ creates new classes
    /// - type.__call__ instantiates classes
    /// </summary>
    public class PyTypeMetaclass : PyClass
    {
        private static PyTypeMetaclass _instance;

        /// <summary>
        /// The global 'type' object - equivalent to CPython's type
        /// </summary>
        public static PyTypeMetaclass Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateTypeMetaclass();
                }
                return _instance;
            }
        }

        private PyTypeMetaclass(string name, PyType[] baseTypes, Dictionary<string, PyObject> classDict)
            : base(name, baseTypes, classDict)
        {
            // type is its own metaclass
            this.Metaclass = this;
        }

        // CPython 3.12: Objects/typeobject.c:7163-7182 (type_ready_fill_dict)
        // Override InitializeDescriptors to call InitializeTypeTypeDescriptors
        // This is called from PyType constructor, equivalent to type_add_getset
        protected override void InitializeDescriptors()
        {
            // CPython 3.12: Objects/typeobject.c:6701-6722 (type_add_getset)
            // For PyType_Type, tp_getset = type_getsets (Objects/typeobject.c:1577-1590)
            // SharpPy equivalent: Add type descriptors (__dict__, __bases__, etc.) to TypeDict
            InitializeTypeTypeDescriptors();

            // Also call base implementation for PyClass-specific descriptors
            base.InitializeDescriptors();
        }

        /// <summary>
        /// Creates the global type metaclass instance
        /// </summary>
        private static PyTypeMetaclass CreateTypeMetaclass()
        {
            #if DEBUG_LOG
            Console.WriteLine("🏗️ Creating global 'type' metaclass");
            #endif
            
            var classDict = new Dictionary<string, PyObject>();




            // type inherits from object
            var baseTypes = new PyType[] { PyType.ObjectType };

            // CPython 3.12: Objects/typeobject.c:5322-5367 (PyType_Type static struct)
            // Line 5323: PyVarObject_HEAD_INIT(&PyType_Type, 0) - type is its own metaclass
            // Line 5355: .tp_getset = type_getsets (contains __dict__, __bases__, etc.)
            //
            // SharpPy: PyTypeMetaclass constructor calls InitializeDescriptors() which:
            // 1. Calls InitializeTypeTypeDescriptors() to add type_getsets to TypeDict
            // 2. Calls base.InitializeDescriptors() for PyClass-specific descriptors
            var typeClass = new PyTypeMetaclass("type", baseTypes, classDict);

            // type___prepare__: METH_FASTCALL|METH_KEYWORDS|METH_CLASS, an empty namespace whatever it is given.
            typeClass.ClassDict["__prepare__"] = new PyClassMethodDescriptor("__prepare__", typeClass,
                new PyBuiltinFunction("__prepare__", (args, kwargs) => new PyDict()));

            // type_mro_impl (TYPE_MRO_METHODDEF, METH_NOARGS): mro_implementation computed again from
            // the bases, not the __mro__ the type holds.
            typeClass.ClassDict["mro"] = PyArgs.Method(typeClass, "mro", PyCall.NoArgs,
                (self, args) => new PyList(((PyType)self).CalculateC3MRO().ConvertAll(type => (PyObject)type)));

            // type_get_annotations: a class without any gets an empty dict of its own, kept in its dict.
            typeClass.ClassDict["__annotations__"] = new PyGetSetDescriptor("__annotations__", typeClass,
                self =>
                {
                    if (self is not PyClass cls || !cls.IsHeapType)
                        throw PyAttributeError.Create($"type object '{((PyType)self).Name}' has no attribute '__annotations__'");
                    if (!cls.ClassDict.TryGetValue("__annotations__", out var annotations))
                        cls.ClassDict["__annotations__"] = annotations = new PyDict();
                    return annotations;
                },
                (self, value) =>
                {
                    if (self is not PyClass cls) throw PyTypeError.Create($"cannot set '__annotations__' attribute of immutable type '{((PyType)self).Name}'");
                    cls.ClassDict["__annotations__"] = value;
                },
                deleter: self =>
                {
                    if (self is not PyClass cls || !cls.ClassDict.Remove("__annotations__"))
                        throw PyAttributeError.Create("__annotations__");
                });

            // type_get_type_params: () for a class that has none in its dict.
            typeClass.ClassDict["__type_params__"] = new PyGetSetDescriptor("__type_params__", typeClass,
                self => ReferenceEquals(self, Instance) ? PyTuple.Empty : self is PyClass cls && cls.ClassDict.TryGetValue("__type_params__", out var parameters) ? parameters : PyTuple.Empty,
                (self, value) =>
                {
                    if (self is not PyClass cls) throw PyTypeError.Create($"cannot set '__type_params__' attribute of immutable type '{self}'");
                    cls.ClassDict["__type_params__"] = value;
                },
                deleter: self => throw PyTypeError.Create($"cannot delete '__type_params__' attribute of immutable type '{((PyType)self).Name}'"));

            // type_init: everything is done by then, it only refuses a wrong call.
            typeClass.ClassDict["__init__"] = new PyWrapperDescriptor("__init__", typeClass, (self, args, kwargs) =>
            {
                if (args.Length == 1 && kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("type.__init__() takes no keyword arguments");
                if (args.Length != 1 && args.Length != 3)
                    throw PyTypeError.Create("type.__init__() takes 1 or 3 arguments");
                return PyNone.Instance;
            });

            // type_getattro behind tp_getattro.
            typeClass.ClassDict["__getattribute__"] = new PyWrapperDescriptor("__getattribute__", typeClass, (self, args, kwargs) =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                if (args[0] is not PyString name)
                    throw PyTypeError.Create($"attribute name must be string, not '{args[0].GetTypeName()}'");
                return ((PyType)self).TypeGetattro(name.Value);
            });

            // type_call behind tp_call.
            typeClass.ClassDict["__call__"] = new PyWrapperDescriptor("__call__", typeClass, (self, args, kwargs) =>
                ((PyType)self).TypeCall(args, kwargs));

            // tp_flags as far as Python can see them: Py_TPFLAGS_HEAPTYPE, _BASETYPE, _IS_ABSTRACT.
            foreach (var (name, read) in new (string, Func<TypeLayout, int>)[]
            {
                ("__basicsize__", layout => layout.BasicSize), ("__itemsize__", layout => layout.ItemSize),
                ("__dictoffset__", layout => layout.DictOffset), ("__weakrefoffset__", layout => layout.WeakOffset),
            })
                typeClass.ClassDict[name] = new PyMemberDescriptor(name, typeClass, self => PyInt.From(read(((PyType)self).Layout)), readOnly: true);

            typeClass.ClassDict["__flags__"] = new PyMemberDescriptor("__flags__", typeClass, self =>
            {
                var type = (PyType)self;
                long flags = type.HasFeature(PyTypeFlags.BaseType) ? 1L << 10 : 0;
                flags |= type.CollectionFlags;
                if (type.IsHeapType)
                    flags |= 1L << 9;
                if (type.HasFeature(PyTypeFlags.ImmutableType))
                    flags |= 1L << 8;
                if (type is PyClass cls && cls.IsHeapType)
                {
                    if (cls.ClassDict.TryGetValue("__abstractmethods__", out var abstractMethods) && abstractMethods.IsTrue())
                        flags |= 1L << 20;
                }
                return PyInt.From(flags);
            }, readOnly: true);

            // type___subclasses___impl
            typeClass.ClassDict["__subclasses__"] = PyArgs.Method(typeClass, "__subclasses__", PyCall.NoArgs, (self, args) =>
                new PyList(((PyType)self).LiveSubclasses().ConvertAll(subclass => (PyObject)subclass)));

            // type_get_text_signature: from the internal doc of a static type.
            typeClass.ClassDict["__text_signature__"] = new PyGetSetDescriptor("__text_signature__", typeClass,
                self => self is PyClass ? PyNone.Instance : PyDocs.TextSignature(self));


            #if DEBUG_LOG
            Console.WriteLine("✅ Global 'type' metaclass created successfully");
            #endif
            return typeClass;
        }

        /// <summary>_PyType_CalculateMetaclass (Objects/typeobject.c:3090).</summary>
        internal static PyType CalculateMetaclass(PyType metatype, PyObject[] bases)
        {
            var winner = metatype;
            foreach (var item in bases)
            {
                var baseMeta = item.GetPyType();
                if (ReferenceEquals(winner, baseMeta) || winner.IsSubclassOf(baseMeta)) continue;
                if (!baseMeta.IsSubclassOf(winner))
                    throw PyTypeError.Create("metaclass conflict: the metaclass of a derived class must be a (non-strict) subclass of the metaclasses of all its bases");
                winner = baseMeta;
            }
            return winner;
        }

        /// <summary>
        /// type_new (Objects/typeobject.c:3700). Every class is made here, whatever called it:
        /// __build_class__, type(...), a metaclass, or super().__new__ inside one.
        /// </summary>
        internal static PyObject TypeNew(PyObject[] args, PyDict kwargs)
        {
            if (args.Length == 0 || args[0] is not PyClass metatype)
                throw PyTypeError.Create("type.__new__(X): X is not a type object");
            if (args.Length != 4)
                throw PyTypeError.Create($"type.__new__() takes exactly 3 arguments ({args.Length - 1} given)");
            if (args[1] is not PyString name)
                throw PyArgs.BadArgument("type.__new__", "argument 1", "str", args[1]);
            if (args[2] is not PyTuple bases)
                throw PyArgs.BadArgument("type.__new__", "argument 2", "tuple", args[2]);
            var ns = args[3] as PyDict;
            if (ns == null)
                throw PyArgs.BadArgument("type.__new__", "argument 3", "dict", args[3]);
            if (name.Value.Contains('\0'))
                throw PyValueError.Create("type name must not contain null characters");

            foreach (var item in bases.Items)
                if (item is not PyType && item.HasTypeSlot("__mro_entries__"))
                    throw PyTypeError.Create("type() doesn't support MRO entry resolution; use types.new_class()");
            if (CalculateMetaclass(metatype, bases.Items) is not PyClass winner)
                throw PyTypeError.Create("metaclass conflict: the metaclass of a derived class must be a (non-strict) subclass of the metaclasses of all its bases");
            if (!ReferenceEquals(winner, metatype))
            {
                // A more derived metaclass with its own tp_new takes over.
                var winnerNew = winner.FindNameInMro("__new__");
                if (!ReferenceEquals(winnerNew, Instance.FindNameInMro("__new__")))
                    return winnerNew.Call(new[] { winner, args[1], args[2], args[3] }, kwargs);
                metatype = winner;
            }

            var baseTypes = new PyType[bases.Items.Length == 0 ? 1 : bases.Items.Length];
            baseTypes[0] = PyType.ObjectType;
            for (int i = 0; i < bases.Items.Length; i++) baseTypes[i] = (PyType)bases.Items[i];
            BestBase(baseTypes);

            // dict_merge: the storage, unless the class has its own __iter__, as OrderedDict does.
            // A key that is no str stays an entry of the dict, and a str subclass key stays its object.
            var dict = new PyTypeDictionary();
            if (PyDict.IterIsDictIter(args[3]))
            {
                foreach (var entry in ns.InternalDict)
                    dict.SetItem(entry.Key, entry.Value);
            }
            else
            {
                foreach (var key in PySet.IterableItems(args[3].GetAttribute("keys").Call(Array.Empty<PyObject>(), null)))
                    dict.SetItem(key, PyAbstract.GetItem(args[3], key));
            }
            // type_new_set_names and type_new_set_doc: both must encode.
            PyString.From(name.Value).GetAttribute("encode").Call(Array.Empty<PyObject>(), null);
            if (dict.TryGetValue("__doc__", out var classDoc) && classDoc.IsExactly(PyType.StrType))
                classDoc.GetAttribute("encode").Call(Array.Empty<PyObject>(), null);
            CheckSlots(dict, baseTypes);

            // type_new_set_attrs
            if (!dict.ContainsKey("__module__") && PyVM.CurrentFrame?.Globals.TryGetValue("__name__", out var module) == true)
                dict["__module__"] = module;
            if (dict.TryGetValue("__new__", out var newEntry) && newEntry is PyFunction)
                dict["__new__"] = new PyStaticmethod(newEntry);
            foreach (var implicitClassmethod in new[] { "__init_subclass__", "__class_getitem__" })
                if (dict.TryGetValue(implicitClassmethod, out var entry) && entry is PyFunction function)
                    dict[implicitClassmethod] = new PyClassmethod(function);
            PyObject classCell = null;
            if (dict.Remove("__classcell__", out classCell) && classCell is not PyCell)
                throw PyTypeError.Create($"__classcell__ must be a nonlocal cell, not {classCell.ToRepr().Value}");

            var created = new PyClass(name.Value, baseTypes, dict) { Metaclass = metatype };
            if (classCell is PyCell cell) cell.Value = created;
            // type_new_set_classdictcell: annotation scopes read the type's own dict from now on.
            if (created.ClassDict.Remove("__classdictcell__", out var dictCell) && dictCell is PyCell classDictCell)
                classDictCell.Value = new PyMappingProxy(created.ClassDict);
            AddInstanceDescriptors(created);
            // type_dict_set_doc: __doc__ None when the class has none, after the members and getsets.
            created.ClassDict.TryAdd("__doc__", PyNone.Instance);

            // mro_internal: a metaclass may answer another order.
            created.MroPending = true;
            List<PyType> order;
            try
            {
                order = MroInvoke(created);
            }
            finally
            {
                created.MroPending = false;
            }
            if (order != null)
                created.SetMro(order);

            // type_new_set_names, on a copy because __set_name__ may change the class.
            foreach (var entry in new Dictionary<string, PyObject>(created.ClassDict))
            {
                var hook = entry.Value.LookupSpecialMethodUncached("__set_name__");
                if (hook == null) continue;
                try
                {
                    entry.Value.BindSpecialMethod(hook).Call(new PyObject[] { created, PyString.From(entry.Key) }, null);
                }
                catch (PythonException error)
                {
                    error.PyException.GetAttribute("add_note").Call(new PyObject[] { PyString.From(
                        $"Error calling __set_name__ on '{entry.Value.GetTypeName()}' instance '{entry.Key}' in '{name.Value}'") }, null);
                    throw;
                }
            }

            // type_new_init_subclass
            new PySuper(created, created).GetAttribute("__init_subclass__").Call(new PyObject[0], kwargs);
            return created;
        }

        /// <summary>What a slot does with the function it found on the type.</summary>
        internal static PyObject Bind(PyObject found, PyObject self)
        {
            if (found is PyFunction function) return new PyMethod(self, function);
            return found is IDescriptor descriptor ? descriptor.Get(self, self.GetPyType()) : found;
        }

        /// <summary>solid_base: the nearest type whose instances differ in shape from its base's.</summary>
        private static PyType SolidBase(PyType type)
        {
            var parent = type.TpBase;
            var solid = parent != null ? SolidBase(parent) : PyType.ObjectType;
            return type.Layout.BasicSize != solid.Layout.BasicSize || type.Layout.ItemSize != solid.Layout.ItemSize
                ? type : solid;
        }

        /// <summary>mro_invoke and mro_check: what a metaclass's mro() answers, null without one.</summary>
        internal static List<PyType> MroInvoke(PyClass type)
        {
            var method = type.Metaclass?.FindNameInMro("mro");
            if (method == null || ReferenceEquals(method, Instance.FindNameInMro("mro")))
                return null;
            var order = new List<PyType>();
            var iterator = type.BindSpecialMethod(method).Call(new PyObject[0], null).GetIterator();
            try
            {
                while (true)
                {
                    var next = iterator.Next();
                    order.Add(next as PyType ?? throw PyTypeError.Create($"mro() returned a non-class ('{next.GetTypeName()}')"));
                }
            }
            catch (PythonException stop) when (stop.PyException is PyStopIteration) { }
            if (order.Count == 0)
                throw PyTypeError.Create("type MRO must not be empty");
            var solid = SolidBase(type);
            foreach (var entry in order)
            {
                var other = SolidBase(entry);
                if (!ReferenceEquals(solid, other) && !solid.IsSubclassOf(other))
                    throw PyTypeError.Create($"mro() returned base with unsuitable layout ('{entry.Name}')");
            }
            return order;
        }

        // type_is_subtype_base_chain: a reentrant mro() sees the new bases before the new MRO.
        private static bool BaseChainReaches(PyType start, PyType target)
        {
            var seen = new HashSet<PyType>();
            var pending = new Stack<PyType>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                var type = pending.Pop();
                if (ReferenceEquals(type, target)) return true;
                if (!seen.Add(type)) continue;
                foreach (var next in type.BaseTypes) pending.Push(next);
            }
            return false;
        }

        /// <summary>type_set_bases, up to the point where the bases change.</summary>
        internal static void CheckBases(PyClass type, PyType[] bases)
        {
            foreach (var entry in bases)
                if (ReferenceEquals(entry, type) || entry.IsSubclassOf(type) || BaseChainReaches(entry, type))
                    throw PyTypeError.Create("a __bases__ item causes an inheritance cycle");
            CheckAssignment(type.TpBase, BestBase(bases), "__bases__");
        }

        private static bool CompatibleWithTpBase(PyType child)
        {
            var parent = child.TpBase;
            return parent != null && child.IsHeapType &&
                child.Layout.BasicSize == parent.Layout.BasicSize && child.Layout.ItemSize == parent.Layout.ItemSize &&
                child.Layout.DictOffset == parent.Layout.DictOffset && child.Layout.WeakOffset == parent.Layout.WeakOffset &&
                child.Layout.Gc == parent.Layout.Gc;
        }

        private static bool SameSlotsAdded(PyType a, PyType b)
        {
            int size = a.TpBase.Layout.BasicSize;
            if (a.Layout.DictOffset == size && b.Layout.DictOffset == size) size += 8;
            if (a.Layout.WeakOffset == size && b.Layout.WeakOffset == size) size += 8;
            if (a is not SharpPy.PyClass || b is not SharpPy.PyClass) return false;
            var slotsA = a.Layout.Slots;
            var slotsB = b.Layout.Slots;
            if (slotsA != null && slotsB != null)
            {
                if (!slotsA.SequenceEqual(slotsB)) return false;
                size += 8 * slotsA.Length;
            }
            return size == a.Layout.BasicSize && size == b.Layout.BasicSize;
        }

        // Py_TPFLAGS_PREHEADER: the two bits, MANAGED_DICT and MANAGED_WEAKREF.
        private static int Preheader(PyType type) =>
            (type.Layout.DictOffset == -1 ? 1 : 0) | (type.Layout.WeakOffset == -32 ? 2 : 0);

        // tp_free: PyObject_GC_Del for whatever is collected, PyObject_Free otherwise.
        private static bool FreedByGc(PyType type) => type.IsHeapType || type.Layout.Gc;

        /// <summary>compatible_for_assignment: the highest supertypes each one matches are compatible.</summary>
        internal static void CheckAssignment(PyType oldTo, PyType newTo, string attribute)
        {
            if (FreedByGc(newTo) != FreedByGc(oldTo))
                throw PyTypeError.Create($"{attribute} assignment: '{newTo.Name}' deallocator differs from '{oldTo.Name}'");
            var newBase = newTo;
            var oldBase = oldTo;
            while (CompatibleWithTpBase(newBase)) newBase = newBase.TpBase;
            while (CompatibleWithTpBase(oldBase)) oldBase = oldBase.TpBase;
            bool differs = !ReferenceEquals(newBase, oldBase) &&
                (!ReferenceEquals(newBase.TpBase, oldBase.TpBase) || !SameSlotsAdded(newBase, oldBase));
            if (differs || Preheader(oldTo) != Preheader(newTo))
                throw PyTypeError.Create($"{attribute} assignment: '{newTo.Name}' object layout differs from '{oldTo.Name}'");
        }

        /// <summary>best_base: the base whose solid base extends all the others'.</summary>
        internal static PyType BestBase(PyType[] bases)
        {
            PyType primary = null;
            PyType winner = null;
            foreach (var candidate in bases)
            {
                if (!candidate.HasFeature(PyTypeFlags.BaseType))
                    throw PyTypeError.Create($"type '{candidate.FullName}' is not an acceptable base type");
                var solid = SolidBase(candidate);
                if (winner == null || (!ReferenceEquals(winner, solid) && !winner.IsSubclassOf(solid) && solid.IsSubclassOf(winner)))
                {
                    winner = solid;
                    primary = candidate;
                }
                else if (!ReferenceEquals(winner, solid) && !winner.IsSubclassOf(solid))
                    throw PyTypeError.Create("multiple bases have instance lay-out conflict");
            }
            return primary;
        }

        // _Py_Mangle as type_new_copy_slots applies it.
        private static string SlotAttribute(PyClass type, string slot) =>
            slot.StartsWith("__", StringComparison.Ordinal) && !slot.EndsWith("__", StringComparison.Ordinal) ? "_" + type.Name.TrimStart('_') + slot : slot;

        /// <summary>type_new_slots and type_new_descriptors: what a new class adds to its tp_base.</summary>
        internal static TypeLayout NewLayout(PyClass type)
        {
            var primary = type.TpBase;
            var parent = primary.Layout;
            bool mayAddDict = parent.DictOffset == 0;
            bool mayAddWeak = parent.WeakOffset == 0 && parent.ItemSize == 0;
            bool addDict = false, addWeak = false;
            string[] slots = null;
            if (!type.ClassDict.TryGetValue("__slots__", out var declared))
            {
                addDict = mayAddDict;
                addWeak = mayAddWeak;
            }
            else
            {
                var names = SharpPy.PyClass.EnumerateSlotNames(declared).ToList();
                addDict = mayAddDict && names.Contains("__dict__");
                addWeak = mayAddWeak && names.Contains("__weakref__");
                slots = names.Where(name => name is not ("__dict__" or "__weakref__"))
                    .Select(name => SlotAttribute(type, name)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                // type_new_slots_bases: a secondary base may provide them.
                foreach (var entry in type.BaseTypes)
                {
                    if (ReferenceEquals(entry, primary)) continue;
                    addDict |= mayAddDict && entry.Layout.DictOffset != 0;
                    addWeak |= mayAddWeak && entry.Layout.WeakOffset != 0;
                }
            }
            return parent with
            {
                BasicSize = parent.BasicSize + 8 * (slots?.Length ?? 0),
                DictOffset = addDict ? -1 : parent.DictOffset,
                WeakOffset = addWeak ? -32 : parent.WeakOffset,
                Gc = true,
                Slots = slots,
                NoInstances = false,
            };
        }

        // subtype_setdict: a metatype's base has a __dict__ getset, the class keeps type's.
        private static void RefuseBuiltinDict(PyObject self)
        {
            if (self is PyType)
                throw PyAttributeError.Create("attribute '__dict__' of 'type' objects is not writable");
            if (self.GetPyType().IsSubclassOf(PyType.ModuleType))
                throw PyAttributeError.Create("readonly attribute");
        }

        /// <summary>type_new_slots and type_new_descriptors: the getsets of a class whose instances
        /// get a __dict__ or a weak reference list that no base gives them.</summary>
        private static void AddInstanceDescriptors(PyClass created)
        {
            var parent = created.TpBase.Layout;
            var own = created.Layout;
            // type_add_getset: PyDict_SetDefault, what the class defined stays.
            if (parent.DictOffset == 0 && own.DictOffset != 0 && !created.ClassDict.ContainsKey("__dict__"))
                created.ClassDict["__dict__"] = new PyGetSetDescriptor("__dict__", created,
                    // subtype_dict and subtype_setdict: the generic get and set, deletion below.
                    self => self is PyType type ? ((IDescriptor)PyType.TypeType.TypeDict["__dict__"]).Get(type, Instance) : SubclassInstanceSupport.GenericGetDict(self),
                    (self, value) =>
                    {
                        RefuseBuiltinDict(self);
                        SubclassInstanceSupport.GenericSetDict(self, value);
                    },
                    deleter: self =>
                    {
                        RefuseBuiltinDict(self);
                        if (self.GetPyType().IsSubclassOf(PyType.BaseExceptionType))
                            throw PyTypeError.Create("cannot delete __dict__");
                        if (self is PyClassInstance instance) instance.DropDict();
                        else PyGlobalsDict.Clear(self.HeapDict);
                    });
            if (parent.WeakOffset == 0 && own.WeakOffset != 0)
                created.ClassDict["__weakref__"] = new PyGetSetDescriptor("__weakref__", created,
                    // subtype_getweakref: the head of the object's weakref list, None when empty.
                    self => PyFinalizer.WeakReferencesOf(self) is { Count: > 0 } references ? references[0] : PyNone.Instance);
            if (own.Slots == null) return;
            foreach (var stored in own.Slots)
            {
                created.ClassDict[stored] = new PyMemberDescriptor(stored, created,
                    self =>
                    {
                        if (self is PyClassInstance instance ? instance.TryGetSlot(stored, out var value)
                            : self.SlotStore.TryGetValue(stored, out value))
                            return value;
                        throw PyAttributeError.Create($"'{self.GetTypeName()}' object has no attribute '{stored}'");
                    },
                    (self, value) =>
                    {
                        if (self is PyClassInstance instance) instance.SetSlot(stored, value);
                        else self.SlotStore[stored] = value;
                    },
                    deleter: self =>
                    {
                        bool removed = self is PyClassInstance instance ? instance.RemoveSlot(stored)
                            : self.SlotStore.Remove(stored);
                        // PyMember_SetOne, T_OBJECT_EX deleted while unset: the member's name alone
                        if (!removed) throw PyAttributeError.Create(stored);
                    });
            }
        }

        /// <summary>The checks of type_new_slots_impl. Answers the names, null without __slots__.</summary>
        private static void CheckSlots(Dictionary<string, PyObject> dict, PyType[] bases)
        {
            if (!dict.TryGetValue("__slots__", out var slots)) return;
            var names = new List<PyObject>();
            if (slots is PyString) names.Add(slots);
            else
            {
                var iterator = slots.GetIterator();
                try
                {
                    while (true) names.Add(iterator.Next());
                }
                catch (PythonException stop) when (stop.PyException is PyStopIteration) { }
            }
            if (names.Count == 0) return;
            var primary = BestBase(bases).Layout;
            if (primary.ItemSize != 0)
                throw PyTypeError.Create($"nonempty __slots__ not supported for subtype of '{BestBase(bases).Name}'");
            // type_new_visit_slots: the __dict__ and __weakref__ a base already has cannot be added again.
            bool mayAddDict = primary.DictOffset == 0;
            bool mayAddWeak = primary.WeakOffset == 0 && primary.ItemSize == 0;
            foreach (var item in names)
            {
                if (item is not PyString text)
                    throw PyTypeError.Create($"__slots__ items must be strings, not '{item.GetTypeName()}'");
                if (text.IsIdentifier(new PyObject[0]) is not PyBool { Value: true })
                    throw PyTypeError.Create("__slots__ must be identifiers");
                if (text.Value == "__dict__")
                {
                    if (!mayAddDict)
                        throw PyTypeError.Create("__dict__ slot disallowed: we already got one");
                    mayAddDict = false;
                }
                else if (text.Value == "__weakref__")
                {
                    if (!mayAddWeak)
                        throw PyTypeError.Create("__weakref__ slot disallowed: we already got one");
                    mayAddWeak = false;
                }
                if (dict.ContainsKey(text.Value) && text.Value is not ("__qualname__" or "__classcell__") && !text.Value.StartsWith("__", StringComparison.Ordinal))
                    throw PyValueError.Create($"'{text.Value}' in __slots__ conflicts with class variable");
            }
        }

        /// <summary>
        /// Override GetPyType to return self (type is its own type)
        /// </summary>
        public override PyType GetPyType()
        {
            return this; // type.__class__ == type
        }



        // CPython 3.12: No need to override GetAttribute anymore
        // All type attributes (__dict__, __name__, __module__, etc.) are now handled
        // by descriptors in ClassDict, just like CPython does via getsetdef array
    }

    /// <summary>
    /// CPython 3.12: Static builtin method (like tp_new)
    /// Behaves like @staticmethod - does NOT bind to instance
    /// </summary>
    public class PyStaticBuiltinMethod : PyObject, IDescriptor, ICFunction
    {
        string ICFunction.MethodName => Name;
        PyObject ICFunction.MethodSelf => _owner;
        PyObject ICFunction.MethodModule => null;
        // A type's static methods (str.maketrans); tp_new_methoddef is not one.
        bool ICFunction.MethodIsStatic => Name != "__new__";

        public string Name { get; }
        public Func<PyObject[], PyDict, PyObject> Method { get; }

        private readonly PyType _owner;

        internal bool HasOwner => _owner != null;

        /// <summary>tp_new_wrapper: every builtin type's __new__, a method of the type object.</summary>
        internal static PyStaticBuiltinMethod ForNew(PyType owner, Func<PyObject[], PyDict, PyObject> method) =>
            new PyStaticBuiltinMethod("__new__", method, owner);

        protected internal override PyString Repr() => _owner == null ? (PyString)base.Repr() :
            PyString.From($"<built-in method {Name} of type object at {PyObjectId.Pointer(_owner)}>");

        public PyStaticBuiltinMethod(string name, Func<PyObject[], PyDict, PyObject> method, PyType owner = null)
        {
            Name = name;
            Method = method;
            _owner = owner;
        }

        public override string ToString()
        {
            return $"<built-in method '{Name}'>";
        }

        public override PyType GetPyType()
        {
            // CPython: builtin staticmethods (str.maketrans) are PyCFunction.
            return PyType.BuiltinFunctionOrMethodType;
        }

        // Descriptor protocol: Always return self (staticmethod behavior)
        public PyObject Get(PyObject instance, PyType owner)
        {
            // Staticmethod: return unbound function regardless of instance
            return this;
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"can't set attribute '{Name}'");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"can't delete attribute '{Name}'");
        }

        public bool IsDataDescriptor() => false;

        // Direct call: all arguments and kwargs passed as-is
        // CPython 3.12: Objects/typeobject.c - tp_new receives kwargs
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // tp_new_wrapper: a type's __new__ checks the subtype before tp_new.
            if (_owner != null && Name == "__new__")
                PyType.CheckNewArguments(_owner, args);
            // METH_STATIC: the method's calling convention (PyArity).
            else if (_owner != null && PyArity.Of(_owner, Name) is { } convention)
                PyArity.Check(convention, _owner, Name, args, kwargs);
            return Method(args, kwargs);
        }

        public override bool IsCallable()
        {
            return true;
        }
    }
}
