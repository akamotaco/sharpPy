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

        // CPython 3.12: Objects/typeobject.c:5339 - PyType_Type.tp_getattro = _Py_type_getattro
        // Override GetAttribute to use type_getattro logic (not PyClass custom logic)
        // This ensures type object attributes invoke descriptors correctly
        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12: Objects/typeobject.c:4800-4880 (type_getattro_impl)
            // For PyType_Type, metatype = &PyType_Type (type is its own metaclass)
            // Search in metatype's MRO for descriptors and invoke them with type as instance

            var metatype = GetPyType(); // Returns this (type is its own metaclass)

            // Search in MRO for descriptor
            foreach (var mroType in MRO)
            {
                if (mroType is PyType pyType && pyType.TypeDict != null)
                {
                    PyObject attr = null;
                    if ((pyType is PyClass cls && cls.ClassDict.TryGetValue(name, out attr)) || pyType.TypeDict.TryGetValue(name, out attr))
                    {
                        // Found attribute - check if it's a descriptor
                        if (attr is IDescriptor descriptor)
                        {
                            // Data descriptors bind through the metatype; other
                            // descriptors are found on type itself and stay unbound.
                            return descriptor.Get(descriptor.IsDataDescriptor() ? this : null, metatype);
                        }
                        return attr;
                    }
                }
            }

            // Not found in MRO, fall back to base implementation
            return base.GetAttribute(name);
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

            // type_new behind tp_new: type.__new__(metacls, name, bases, namespace, **kwargs)
            classDict["__new__"] = new PyStaticBuiltinMethod("__new__", TypeNew);

            // type___prepare__: a classmethod that makes the namespace.
            classDict["__prepare__"] = new PyClassmethod(new PyBuiltinFunction("__prepare__", (args, kwargs) => new PyDict()));

            // CPython 3.12: Add all getset_descriptors (matches CPython's type_getsets[])
            // These descriptors provide attribute access for type objects
            // Equivalent to CPython's getsetdef array in Objects/typeobject.c:1578-1591
            classDict["__dict__"] = new PyDictDescriptor();
            classDict["__bases__"] = new PyBasesDescriptor();
            classDict["__mro__"] = new PyMroDescriptor();
            classDict["__base__"] = new PyBaseDescriptor();
            classDict["__doc__"] = new PyDocDescriptor();

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

            // type_mro_impl (TYPE_MRO_METHODDEF, METH_NOARGS): mro_implementation computed again from
            // the bases, not the __mro__ the type holds.
            typeClass.ClassDict["mro"] = PyArgs.Method(typeClass, "mro", PyCall.NoArgs,
                (self, args) => new PyList(((PyType)self).CalculateC3MRO().ConvertAll(type => (PyObject)type)));

            // type_get_annotations: a class without any gets an empty dict of its own, kept in its dict.
            typeClass.ClassDict["__annotations__"] = new PyGetSetDescriptor("__annotations__", typeClass,
                self =>
                {
                    if (self is not PyClass cls || self is PyTypeMetaclass)
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
                return self is PyClass cls ? cls.TypeGetAttribute(name.Value) : self.GetAttribute(name.Value);
            });

            // type_call behind tp_call.
            typeClass.ClassDict["__call__"] = new PyWrapperDescriptor("__call__", typeClass, (self, args, kwargs) =>
                self is PyClass cls ? cls.TypeCall(args, kwargs) : self.Call(args, kwargs));

            // tp_flags as far as Python can see them: Py_TPFLAGS_HEAPTYPE, _BASETYPE, _IS_ABSTRACT.
            foreach (var (name, read) in new (string, Func<TypeLayout, int>)[]
            {
                ("__basicsize__", layout => layout.BasicSize), ("__itemsize__", layout => layout.ItemSize),
                ("__dictoffset__", layout => layout.DictOffset), ("__weakrefoffset__", layout => layout.WeakOffset),
            })
                typeClass.ClassDict[name] = new PyMemberDescriptor(name, typeClass, self => new PyInt(read(((PyType)self).Layout)), readOnly: true);

            typeClass.ClassDict["__flags__"] = new PyGetSetDescriptor("__flags__", typeClass, self =>
            {
                var type = (PyType)self;
                long flags = type.HasFeature(PyTypeFlags.BaseType) ? 1L << 10 : 0;
                if (type is PyClass cls && type is not PyTypeMetaclass)
                {
                    flags |= 1L << 9;
                    if (cls.ClassDict.TryGetValue("__abstractmethods__", out var abstractMethods) && abstractMethods.IsTrue())
                        flags |= 1L << 20;
                }
                return new PyInt(flags);
            });

            // type___subclasses___impl
            typeClass.ClassDict["__subclasses__"] = new PyMethodDescriptor("__subclasses__", typeClass, (self, args, kwargs) =>
                new PyList(((PyType)self).LiveSubclasses().ConvertAll(subclass => (PyObject)subclass)), minArgs: 0, maxArgs: 0);

            // type_get_text_signature: from the internal doc of a static type.
            typeClass.ClassDict["__text_signature__"] = new PyGetSetDescriptor("__text_signature__", typeClass,
                self => self is PyClass ? PyNone.Instance : PyDocs.TextSignature(self));

            // Add type.__format__ - CPython 3.12 (defaults to __str__)
            // This descriptor is for type objects themselves
            // Must be added AFTER typeClass is created so we can set OwnerType
            typeClass.ClassDict["__format__"] = new PyMethodDescriptor(
                "__format__",
                typeClass, // OwnerType is the type metaclass itself
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__format__() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__format__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    // CPython has no type.__format__; it resolves to object.__format__, so
                    // an empty spec is str(self) (metaclass __str__ included) and a
                    // non-empty spec raises naming type(obj), i.e. the metaclass.
                    if (args[0] is not PyString spec)
                        throw PyTypeError.Create($"__format__() argument must be str, not {args[0].GetTypeName()}");
                    if (spec.Value.Length == 0)
                        return type.AsString();
                    throw PyTypeError.Create(
                        $"unsupported format string passed to {type.GetPyType()?.Name ?? type.Name}.__format__");
                },
                minArgs: 1,
                maxArgs: 1
            );

            // Add type.__reduce_ex__ - CPython 3.12 (pickle support)
            typeClass.ClassDict["__reduce_ex__"] = new PyMethodDescriptor(
                "__reduce_ex__",
                typeClass,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__reduce_ex__() takes exactly 1 argument ({args.Length} given)");
                    if (self is not PyType type)
                        throw PyTypeError.Create("descriptor '__reduce_ex__' for 'type' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    // Return (type, (type.__name__,))
                    return new PyTuple(new PyObject[] {
                        typeClass,
                        new PyTuple(new PyObject[] { new PyString(type.Name) })
                    });
                },
                minArgs: 1,
                maxArgs: 1
            );

            #if DEBUG_LOG
            Console.WriteLine("✅ Global 'type' metaclass created successfully");
            #endif
            // Expose the same native descriptors used by attribute lookup in
            // type.__dict__, without constructing a snapshot on each access.
            foreach (var entry in typeClass.TypeDict)
                typeClass.ClassDict.TryAdd(entry.Key, entry.Value);
            typeClass.ClassDict["__repr__"] = typeClass.TypeDict["__repr__"];
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
                throw PyTypeError.Create($"type.__new__() argument 1 must be str, not {args[1].GetTypeName()}");
            if (args[2] is not PyTuple bases)
                throw PyTypeError.Create($"type.__new__() argument 2 must be tuple, not {args[2].GetTypeName()}");
            var ns = args[3] as PyDict;
            if (ns == null && args[3] is PyClassInstance dictInstance && dictInstance.IsDictSubclass())
                ns = dictInstance.GetDictStorage();
            if (ns == null)
                throw PyTypeError.Create($"type.__new__() argument 3 must be dict, not {args[3].GetTypeName()}");
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
                var winnerNew = winner.LookupInMRO("__new__");
                if (!ReferenceEquals(winnerNew, Instance.LookupInMRO("__new__")))
                    return winnerNew.Call(new[] { winner, args[1], args[2], args[3] }, kwargs);
                metatype = winner;
            }

            var baseTypes = new PyType[bases.Items.Length == 0 ? 1 : bases.Items.Length];
            baseTypes[0] = PyType.ObjectType;
            for (int i = 0; i < bases.Items.Length; i++) baseTypes[i] = (PyType)bases.Items[i];
            BestBase(baseTypes);

            // dict_merge: the storage, unless the class has its own __iter__, as OrderedDict does.
            var dict = new Dictionary<string, PyObject>();
            if (args[3].LookupUserSpecial("__iter__") == null)
            {
                foreach (var entry in ns.InternalDict)
                    if (entry.Key is PyString key)
                        dict[key.Value] = entry.Value;
            }
            else
            {
                foreach (var key in PySet.IterableItems(args[3].GetAttribute("keys").Call(Array.Empty<PyObject>(), null)))
                    if (key is PyString text)
                        dict[text.Value] = PyAbstract.GetItem(args[3], key);
            }
            // type_new_set_names and type_new_set_doc: both must encode.
            new PyString(name.Value).GetAttribute("encode").Call(Array.Empty<PyObject>(), null);
            if (dict.TryGetValue("__doc__", out var classDoc) && classDoc.GetType() == typeof(PyString))
                classDoc.GetAttribute("encode").Call(Array.Empty<PyObject>(), null);
            CheckSlots(dict, baseTypes);

            // type_new_set_attrs
            if (!dict.ContainsKey("__module__") && PyVM.CurrentFrame?.Globals.TryGetValue("__name__", out var module) == true)
                dict["__module__"] = module;
            if (!dict.ContainsKey("__doc__")) dict["__doc__"] = PyNone.Instance;
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
                    entry.Value.BindSpecialMethod(hook).Call(new PyObject[] { created, new PyString(entry.Key) }, null);
                }
                catch (PythonException error)
                {
                    error.PyException.GetAttribute("add_note").Call(new PyObject[] { new PyString(
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
            var method = type.Metaclass?.LookupInMRO("mro");
            if (method == null || ReferenceEquals(method, Instance.LookupInMRO("mro")))
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
            return parent != null && child is PyClass && child is not PyTypeMetaclass &&
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
        private static bool FreedByGc(PyType type) => type is PyClass && type is not PyTypeMetaclass || type.Layout.Gc;

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
                    self => self is PyType type ? ((IDescriptor)PyType.TypeType.TypeDict["__dict__"]).Get(type, Instance) : self.GetAttribute("__dict__"),
                    (self, value) =>
                    {
                        RefuseBuiltinDict(self);
                        if (self is PyClassInstance instance) instance.SetDict(value);
                        else self.SetAttribute("__dict__", value);
                    },
                    deleter: self =>
                    {
                        RefuseBuiltinDict(self);
                        if (self.GetPyType().IsSubclassOf(PyType.BaseExceptionType))
                            throw PyTypeError.Create("cannot delete __dict__");
                        if (self is PyClassInstance instance) instance.DropDict();
                        else ((IDictBackedInstance)self).InstanceDict.Clear();
                    });
            if (parent.WeakOffset == 0 && own.WeakOffset != 0)
                created.ClassDict["__weakref__"] = new PyGetSetDescriptor("__weakref__", created, self => PyNone.Instance);
            if (own.Slots == null) return;
            foreach (var stored in own.Slots)
            {
                created.ClassDict[stored] = new PyMemberDescriptor(stored, created,
                    self =>
                    {
                        if (self is PyClassInstance instance ? instance.TryGetSlot(stored, out var value)
                            : ((IInstanceDictAccessor)self).TryGetInstanceAttribute(stored, out value))
                            return value;
                        throw PyAttributeError.Create($"'{self.GetTypeName()}' object has no attribute '{stored}'");
                    },
                    (self, value) =>
                    {
                        if (self is PyClassInstance instance) instance.SetSlot(stored, value);
                        else ((IDictBackedInstance)self).InstanceDict[stored] = value;
                    },
                    deleter: self =>
                    {
                        bool removed = self is PyClassInstance instance ? instance.RemoveSlot(stored)
                            : ((IDictBackedInstance)self).InstanceDict.Remove(stored);
                        if (!removed) throw PyAttributeError.Create($"'{self.GetTypeName()}' object has no attribute '{stored}'");
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

        /// <summary>type_call for type itself (Objects/typeobject.c:1627).</summary>
        internal override PyObject TypeCall(PyObject[] args, PyDict kwargs)
        {
            bool noKeywords = kwargs == null || kwargs.Length() == 0;
            if (args.Length == 1 && noKeywords) return args[0].GetPyType();
            if (args.Length != 3) throw PyTypeError.Create("type() takes 1 or 3 arguments");
            var created = TypeNew(new[] { this, args[0], args[1], args[2] }, kwargs);
            // tp_init of what came out, which a more derived metaclass may have made.
            if (created is PyType)
            {
                var init = created.LookupSpecialMethodUncached("__init__");
                if (init != null) created.BindSpecialMethod(init).Call(args, kwargs);
            }
            return created;
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
            new PyString($"<built-in method {Name} of type object at 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_owner):x}>");

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

    /// <summary>
    /// CPython 3.12: getset_descriptor for __dict__ attribute
    /// Equivalent to CPython's type_dict getter in Objects/typeobject.c
    ///
    /// CPython implementation:
    /// static PyObject *
    /// type_dict(PyTypeObject *type, void *context)
    /// {
    ///     PyObject *dict = lookup_tp_dict(type);
    ///     if (dict == NULL) {
    ///         Py_RETURN_NONE;
    ///     }
    ///     return PyDictProxy_New(dict);
    /// }
    ///
    /// getsetdef: {"__dict__",  (getter)type_dict,  NULL, NULL}
    /// - getter: type_dict
    /// - setter: NULL (read-only)
    /// </summary>
    public class PyDictDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => "__dict__";
        PyType IDescrObject.DescrType => PyType.TypeType;
        public PyDictDescriptor()
        {
        }

        public override string ToString()
        {
            return "<attribute '__dict__' of 'type' objects>";
        }

        public override PyType GetPyType()
        {
            return PyType.GetSetDescriptorType;
        }

        public override string GetTypeName()
        {
            return "getset_descriptor";
        }

        /// <summary>
        /// CPython 3.12: __get__(self, obj, type=None)
        /// Equivalent to type_dict(PyTypeObject *type, void *context)
        /// </summary>
        public PyObject Get(PyObject instance, PyType owner)
        {
            #if DEBUG_LOG
            Console.WriteLine($"[PyDictDescriptor.__get__] instance={instance?.GetType().Name}, owner={owner?.Name}");
            #endif

            // CPython 3.12: If accessed from class (not instance), return self
            // Example: type.__dict__ (not SomeClass.__dict__)
            if (instance == null || instance == PyNone.Instance)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → Accessed from class, returning descriptor itself");
                #endif
                return this;
            }

            // CPython 3.12: instance should be a type/class object
            // Check PyClass first (PyClass inherits from PyType)
            if (instance is PyClass pyClass)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → Returning mappingproxy of {pyClass.Name}.__dict__ (count: {pyClass.ClassDict.Count})");
                #endif
                // CPython 3.12: Each access creates a NEW mappingproxy instance
                // (class.__dict__ is class.__dict__) returns False in CPython
                return new PyMappingProxy(pyClass.ClassDict);
            }
            // CPython 3.12: Handle builtin PyType instances (like dict, str, int, etc.)
            else if (instance is PyType pyType)
            {
                #if DEBUG_LOG
                Console.WriteLine($"  → Returning mappingproxy of {pyType.Name}.__dict__ (builtin type)");
                #endif
                // CPython 3.12: Return TypeDict as mappingproxy
                // All descriptors are now in TypeDict, no need to merge
                var typeDict = new Dictionary<string, PyObject>(pyType.TypeDict);
                foreach (var slot in pyType.SlotDict)
                    typeDict.TryAdd(slot.Key, slot.Value);
                return new PyMappingProxy(typeDict);
            }

            // CPython 3.12: If dict is NULL, return None
            #if DEBUG_LOG
            Console.WriteLine($"  → instance is not a type/class, returning None");
            #endif
            return PyNone.Instance;
        }

        /// <summary>
        /// CPython 3.12: setter=NULL in getsetdef
        /// Attempting to set __dict__ should raise AttributeError
        /// </summary>
        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create("attribute '__dict__' of 'type' objects is not writable");
        }

        /// <summary>
        /// CPython 3.12: deleter not supported
        /// </summary>
        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__dict__'");
        }

        /// <summary>
        /// CPython 3.12: getset_descriptor is a data descriptor (has both __get__ and __set__)
        /// Even though setter raises error, it's still considered a data descriptor
        /// </summary>
        public bool IsDataDescriptor()
        {
            return true; // CPython getset_descriptor is always data descriptor
        }
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __bases__ attribute
    /// Equivalent to CPython's type_get_bases getter/type_set_bases setter
    /// getsetdef: {"__bases__", (getter)type_get_bases, (setter)type_set_bases, NULL}
    /// </summary>
    public class PyBasesDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => "__bases__";
        PyType IDescrObject.DescrType => PyType.TypeType;
        public override string ToString() => "<attribute '__bases__' of 'type' objects>";
        public override PyType GetPyType() => PyType.GetSetDescriptorType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
                return this;

            // CPython: Return lookup_tp_bases(type)
            // Works for both PyClass and PyType
            if (instance is PyClass pyClass)
            {
                // CPython 3.12 requirement: type.__bases__ is type.__bases__ must be True
                // Return cached tuple to ensure identity consistency
                if (pyClass._cachedBasesTuple == null)
                {
                    var basesArray = new PyObject[pyClass.BaseTypes.Length];
                    for (int i = 0; i < pyClass.BaseTypes.Length; i++)
                    {
                        basesArray[i] = pyClass.BaseTypes[i];
                    }
                    pyClass._cachedBasesTuple = new PyTuple(basesArray);
                }
                return pyClass._cachedBasesTuple;
            }

            if (instance is PyType pyType)
            {
                // CPython 3.12 requirement: type.__bases__ is type.__bases__ must be True
                // Return cached tuple to ensure identity consistency
                if (pyType._cachedBasesTuple == null)
                {
                    var basesArray = new PyObject[pyType.BaseTypes.Length];
                    for (int i = 0; i < pyType.BaseTypes.Length; i++)
                    {
                        basesArray[i] = pyType.BaseTypes[i];
                    }
                    pyType._cachedBasesTuple = new PyTuple(basesArray);
                }
                return pyType._cachedBasesTuple;
            }

            throw PyTypeError.Create($"descriptor '__bases__' for 'type' objects doesn't apply to a '{instance.GetTypeName()}' object");
        }

        public void Set(PyObject instance, PyObject value)
        {
            // type's own table holds the setter (core/PyType.cs).
            ((PyGetSetDescriptor)PyType.TypeType.TypeDict["__bases__"]).Set(instance, value);
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__bases__'");
        }

        public bool IsDataDescriptor() => true;
    }

    /// <summary>
    /// CPython 3.12: type_getseters {"__base__", type_get_base} - first base or None.
    /// For any valid class the first base equals MRO[1] (C3 keeps bases[0] first).
    /// </summary>
    public class PyBaseDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => "__base__";
        PyType IDescrObject.DescrType => PyType.TypeType;
        public override string ToString() => "<attribute '__base__' of 'type' objects>";
        public override PyType GetPyType() => PyType.MemberDescriptorType;
        public override string GetTypeName() => "member_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            var target = (instance != null && instance != PyNone.Instance) ? instance : owner;
            if (target is PyType pt)
                return (PyObject)pt.TpBase ?? PyNone.Instance;
            var mro = (target as PyType)?.MRO ?? (target as PyClass)?.MRO;
            if (mro != null && mro.Count > 1)
            {
                return mro[1];
            }
            return PyNone.Instance;
        }

        public void Set(PyObject instance, PyObject value)
        {
            // CPython: setter=NULL (read-only)
            throw PyAttributeError.Create("attribute '__base__' of 'type' objects is not writable");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__base__'");
        }

        public bool IsDataDescriptor() => true;
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __mro__ attribute
    /// Equivalent to CPython's type_get_mro getter (setter=NULL, read-only)
    /// getsetdef: {"__mro__", (getter)type_get_mro, NULL, NULL}
    /// </summary>
    public class PyMroDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => "__mro__";
        PyType IDescrObject.DescrType => PyType.TypeType;
        public override string ToString() => "<attribute '__mro__' of 'type' objects>";
        public override PyType GetPyType() => PyType.GetSetDescriptorType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
#if DEBUG_LOG
            Console.WriteLine($"🔍 PyMroDescriptor.Get() called:");
            Console.WriteLine($"  instance = {instance} (type: {instance?.GetType().Name})");
            Console.WriteLine($"  owner = {owner} (type: {owner?.GetType().Name})");
            Console.WriteLine($"  instance is PyClass? {instance is PyClass}");
            Console.WriteLine($"  instance is PyType? {instance is PyType}");
            Console.WriteLine($"  instance is PyTypeMetaclass? {instance is PyTypeMetaclass}");
#endif
            if (instance is PyType { MroPending: true })
                return PyNone.Instance;
            // CPython 3.12: When accessing type.__mro__ (instance=NULL, owner=type),
            // return type's MRO, not the descriptor itself
            if (instance == null || instance == PyNone.Instance)
            {
                // If owner is a type, return its MRO
                if (owner is PyClass ownerClass)
                {
                    if (ownerClass._cachedMroTuple == null)
                    {
                        var mroArray = new PyObject[ownerClass.MRO.Count];
                        for (int i = 0; i < ownerClass.MRO.Count; i++)
                        {
                            mroArray[i] = ownerClass.MRO[i];
                        }
                        ownerClass._cachedMroTuple = new PyTuple(mroArray);
                    }
                    return ownerClass._cachedMroTuple;
                }

                if (owner is PyType ownerType)
                {
                    if (ownerType._cachedMroTuple == null)
                    {
                        var mroArray = new PyObject[ownerType.MRO.Count];
                        for (int i = 0; i < ownerType.MRO.Count; i++)
                        {
                            mroArray[i] = ownerType.MRO[i];
                        }
                        ownerType._cachedMroTuple = new PyTuple(mroArray);
                    }
                    return ownerType._cachedMroTuple;
                }

                // Fallback: return descriptor itself for unbound access
                return this;
            }

            // CPython: Return lookup_tp_mro(type)
            // Works for both PyClass and PyType
            if (instance is PyClass pyClass)
            {
                // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
                // Return cached tuple to ensure identity consistency
                if (pyClass._cachedMroTuple == null)
                {
                    var mroArray = new PyObject[pyClass.MRO.Count];
                    for (int i = 0; i < pyClass.MRO.Count; i++)
                    {
                        mroArray[i] = pyClass.MRO[i];
                    }
                    pyClass._cachedMroTuple = new PyTuple(mroArray);
                }
                return pyClass._cachedMroTuple;
            }

            if (instance is PyType pyType)
            {
                // CPython 3.12 requirement: type.__mro__ is type.__mro__ must be True
                // Return cached tuple to ensure identity consistency
                if (pyType._cachedMroTuple == null)
                {
                    var mroArray = new PyObject[pyType.MRO.Count];
                    for (int i = 0; i < pyType.MRO.Count; i++)
                    {
                        mroArray[i] = pyType.MRO[i];
                    }
                    pyType._cachedMroTuple = new PyTuple(mroArray);
                }
                return pyType._cachedMroTuple;
            }

            throw PyTypeError.Create($"descriptor '__mro__' for 'type' objects doesn't apply to a '{instance.GetTypeName()}' object");
        }

        public void Set(PyObject instance, PyObject value)
        {
            // CPython: setter=NULL (read-only)
            throw PyAttributeError.Create("attribute '__mro__' of 'type' objects is not writable");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__mro__'");
        }

        public bool IsDataDescriptor() => true;
    }

    /// <summary>
    /// CPython 3.12: getset_descriptor for __doc__ attribute
    /// Equivalent to CPython's type_get_doc getter/type_set_doc setter
    /// getsetdef: {"__doc__", (getter)type_get_doc, (setter)type_set_doc, NULL}
    /// </summary>
    public class PyDocDescriptor : PyObject, IDescriptor, IDescrObject
    {
        string IDescrObject.DescrName => "__doc__";
        PyType IDescrObject.DescrType => PyType.TypeType;
        public override string ToString() => "<attribute '__doc__' of 'type' objects>";
        protected internal override PyString Repr() => new PyString(ToString());
        public override PyType GetPyType() => PyType.GetSetDescriptorType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
                return this;

            // type_get_doc: a static type cuts its tp_doc, when it has one.
            if (instance is PyType withDoc && PyDocs.Of(withDoc) is { Doc: not null } internalDoc)
                return PyDocs.DocFromInternal(internalDoc.Name, internalDoc.Doc);

            if (instance is PyClass pyClass)
            {
                // CPython: PyDict_GetItemWithError(dict, &_Py_ID(__doc__))
                // type_get_doc: what the dict holds is asked as a descriptor.
                if (pyClass.ClassDict.TryGetValue("__doc__", out var doc))
                    return PyClassInstance.CallDescriptorGet(doc, null, pyClass);

                // Return None if not found
                return PyNone.Instance;
            }

            // Otherwise the dict's entry, asked as a descriptor with no instance.
            if (instance is PyType native && native.TypeDict.TryGetValue("__doc__", out var stored))
                return PyClassInstance.CallDescriptorGet(stored, null, native);
            return PyNone.Instance;
        }

        // check_set_special_type_attr
        public void Set(PyObject instance, PyObject value)
        {
            if (instance is not PyClass pyClass)
                throw PyTypeError.Create($"cannot set '__doc__' attribute of immutable type '{(instance as PyType)?.Name ?? instance.GetTypeName()}'");
            pyClass.ClassDict["__doc__"] = value;
            pyClass.InvalidateTypeCache();
        }

        public void Delete(PyObject instance) =>
            throw PyTypeError.Create($"cannot delete '__doc__' attribute of immutable type '{(instance as PyType)?.Name ?? instance.GetTypeName()}'");

        public bool IsDataDescriptor() => true;
    }
}
