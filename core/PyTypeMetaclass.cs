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

            // Add other essential type methods...
            classDict["mro"] = new PyBuiltinMethod("mro", (self, args) =>
            {
                if (self is PyClass pyClass)
                {
                    // Performance: Eliminated LINQ - manual conversion instead of Cast + ToList
                    var mroList = new List<PyObject>(pyClass.MRO.Count);
                    for (int i = 0; i < pyClass.MRO.Count; i++)
                    {
                        mroList.Add(pyClass.MRO[i]);
                    }
                    return new PyList(mroList);
                }
                return new PyList();
            }, 1);

            // CPython 3.12: Add all getset_descriptors (matches CPython's type_getsets[])
            // These descriptors provide attribute access for type objects
            // Equivalent to CPython's getsetdef array in Objects/typeobject.c:1578-1591
            classDict["__dict__"] = new PyDictDescriptor();
            classDict["__bases__"] = new PyBasesDescriptor();
            classDict["__mro__"] = new PyMroDescriptor();
            classDict["__base__"] = new PyBaseDescriptor();
            classDict["__doc__"] = new PyDocDescriptor();

            // CPython 3.12: Objects/typeobject.c:4482
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")},
            classDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                (cls, arg) => new PyGenericAlias(cls as PyType ?? throw PyTypeError.Create("Expected type"), arg)
            );

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
                self => self is PyClass cls && cls.ClassDict.TryGetValue("__type_params__", out var parameters) ? parameters : PyTuple.Empty,
                (self, value) =>
                {
                    if (self is not PyClass cls) throw PyTypeError.Create($"cannot set '__type_params__' attribute of immutable type '{self}'");
                    cls.ClassDict["__type_params__"] = value;
                });

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
                self is PyClass cls ? cls.TypeGetAttribute(((PyString)args[0]).Value) : self.GetAttribute(((PyString)args[0]).Value));

            // type_call behind tp_call.
            typeClass.ClassDict["__call__"] = new PyWrapperDescriptor("__call__", typeClass, (self, args, kwargs) =>
                self is PyClass cls ? cls.TypeCall(args, kwargs) : self.Call(args, kwargs));

            // tp_flags as far as Python can see them: Py_TPFLAGS_HEAPTYPE, _BASETYPE, _IS_ABSTRACT.
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
                if (item is not PyType && item.LookupSpecialMethodUncached("__mro_entries__") != null)
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

            var dict = new Dictionary<string, PyObject>();
            foreach (var entry in ns.InternalDict)
                if (entry.Key is PyString key)
                    dict[key.Value] = entry.Value;
            var slotNames = CheckSlots(dict, baseTypes);

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
            AddInstanceDescriptors(created, baseTypes, slotNames);

            // mro_internal: a metaclass may answer another order.
            if (MroInvoke(created) is { } order)
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

        // Exceptions that add C fields to PyBaseExceptionObject; the rest share its layout.
        private static readonly HashSet<string> ExceptionsWithFields = new()
        {
            "BaseException", "BaseExceptionGroup", "StopIteration", "SystemExit", "ImportError", "OSError", "SyntaxError",
            "AttributeError", "NameError", "UnicodeEncodeError", "UnicodeDecodeError", "UnicodeTranslateError",
        };

        /// <summary>solid_base: the nearest type that adds to the instance layout.</summary>
        private static PyType SolidBase(PyType type)
        {
            if (ReferenceEquals(type, PyType.ObjectType)) return type;
            if (!(type is PyClass _) || type is PyTypeMetaclass)
            {
                bool isException = false;
                foreach (var ancestor in type.MRO)
                    if (ancestor.Name == "BaseException") isException = true;
                if (!isException || ExceptionsWithFields.Contains(type.Name)) return type;
            }
            else if (type is PyClass cls && cls.ClassDict.TryGetValue("__slots__", out var slots) && slots.Length() != 0)
            {
                return type;
            }
            return BestBase(type.BaseTypes.ToArray());
        }

        private static PyType BuiltinLayout(PyClass type)
        {
            foreach (var ancestor in type.MRO)
                if (!(ancestor is PyClass _) || ancestor is PyTypeMetaclass) return SolidBase(ancestor);
            return PyType.ObjectType;
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
            foreach (var entry in bases)
                if (!entry.HasFeature(PyTypeFlags.BaseType))
                    throw PyTypeError.Create($"type '{entry.Name}' is not an acceptable base type");
            var newBase = BestBase(bases);
            var oldBase = BestBase(type.BaseTypes.ToArray());
            if (!ReferenceEquals(newBase, oldBase))
                throw PyTypeError.Create($"__bases__ assignment: '{newBase.Name}' object layout differs from '{oldBase.Name}'");
        }

        /// <summary>compatible_for_assignment: the two classes lay their instances out alike.</summary>
        internal static void CheckClassAssignment(PyClass current, PyClass target)
        {
            // same_slots_added: the slots they do not share carry the same names.
            var slots = new[] { new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal) };
            foreach (var (one, other, names) in new[] { (current, target, slots[0]), (target, current, slots[1]) })
                foreach (var ancestor in one.MRO)
                    if (ancestor is PyClass cls && !other.IsSubclassOf(ancestor))
                        foreach (var entry in cls.ClassDict)
                            if (entry.Value is PyMemberDescriptor) names.Add(entry.Key);
            bool sameSlots = current.InstancesHaveDict() == target.InstancesHaveDict() && slots[0].SetEquals(slots[1]);
            if (!sameSlots || !ReferenceEquals(BuiltinLayout(current), BuiltinLayout(target)))
                throw PyTypeError.Create($"__class__ assignment: '{target.Name}' object layout differs from '{current.Name}'");
        }

        /// <summary>best_base: the solid base of the bases, one layout that extends all the others.</summary>
        private static PyType BestBase(PyType[] bases)
        {
            PyType winner = null;
            foreach (var candidate in bases)
            {
                var solid = SolidBase(candidate);
                if (winner == null) winner = solid;
                else if (ReferenceEquals(winner, solid) || winner.IsSubclassOf(solid)) { }
                else if (solid.IsSubclassOf(winner)) winner = solid;
                else throw PyTypeError.Create("multiple bases have instance lay-out conflict");
            }
            return winner;
        }

        // subtype_setdict: a metatype's base has a __dict__ getset, the class keeps type's.
        private static void RefuseTypeDict(PyObject self)
        {
            if (self is PyType)
                throw PyAttributeError.Create("attribute '__dict__' of 'type' objects is not writable");
            if (self.GetPyType().IsSubclassOf(PyType.ModuleType))
                throw PyAttributeError.Create("readonly attribute");
        }

        /// <summary>type_new_slots and type_new_descriptors: the getsets of a class whose instances
        /// get a __dict__ or a weak reference list that no base gives them.</summary>
        private static void AddInstanceDescriptors(PyClass created, PyType[] bases, List<string> slotNames)
        {
            bool hasDict = false, hasWeakList = false;
            foreach (var type in bases)
                foreach (var ancestor in type.MRO)
                {
                    if (ReferenceEquals(ancestor, Instance) || ancestor.Name == "BaseException") hasDict = true;
                    if (ReferenceEquals(ancestor, Instance)) hasWeakList = true;
                    if (ancestor is PyClass cls && cls.ClassDict.ContainsKey("__dict__")) hasDict = true;
                    if (ancestor is PyClass weak && weak.ClassDict.ContainsKey("__weakref__")) hasWeakList = true;
                    // Variable-size instances keep no weak reference list.
                    if (ReferenceEquals(ancestor, PyType.IntType) || ReferenceEquals(ancestor, PyType.TupleType) || ReferenceEquals(ancestor, PyType.BytesType))
                        hasWeakList = true;
                }
            if (!hasDict && (slotNames == null || slotNames.Contains("__dict__")))
                created.ClassDict["__dict__"] = new PyGetSetDescriptor("__dict__", created,
                    self => self is PyType type ? ((IDescriptor)PyType.TypeType.TypeDict["__dict__"]).Get(type, Instance) : self.GetAttribute("__dict__"),
                    (self, value) =>
                    {
                        RefuseTypeDict(self);
                        self.SetAttribute("__dict__", value);
                    },
                    deleter: self =>
                    {
                        RefuseTypeDict(self);
                        if (self.GetPyType().IsSubclassOf(PyType.BaseExceptionType))
                            throw PyTypeError.Create("cannot delete __dict__");
                        self.SetAttribute("__dict__", new PyDict());
                    });
            if (!hasWeakList && (slotNames == null || slotNames.Contains("__weakref__")))
                created.ClassDict["__weakref__"] = new PyGetSetDescriptor("__weakref__", created, self => PyNone.Instance);
            if (slotNames == null) return;
            foreach (var slot in slotNames)
            {
                if (slot is "__dict__" or "__weakref__") continue;
                string stored = slot.StartsWith("__") && !slot.EndsWith("__")
                    ? "_" + created.Name.TrimStart('_') + slot : slot;
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
        private static List<string> CheckSlots(Dictionary<string, PyObject> dict, PyType[] bases)
        {
            if (!dict.TryGetValue("__slots__", out var slots)) return null;
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
            var result = new List<string>();
            if (names.Count == 0) return result;
            foreach (var type in bases)
                foreach (var ancestor in type.MRO)
                    if (ReferenceEquals(ancestor, PyType.IntType) || ReferenceEquals(ancestor, PyType.TupleType) || ReferenceEquals(ancestor, PyType.BytesType))
                        throw PyTypeError.Create($"nonempty __slots__ not supported for subtype of '{ancestor.Name}'");
            // type_new_visit_slots: the __dict__ and __weakref__ a base already has cannot be added again.
            bool mayAddDict = !bases.Any(type => type is PyClass cls && cls.InstancesHaveDict());
            bool mayAddWeak = !bases.Any(type => type.HasFeature(PyTypeFlags.ManagedWeakRef) ||
                type.MRO.Any(ancestor => ancestor is PyClass cls && cls.ClassDict.ContainsKey("__weakref__")));
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
                if (dict.ContainsKey(text.Value) && text.Value is not ("__qualname__" or "__classcell__") && !text.Value.StartsWith("__"))
                    throw PyValueError.Create($"'{text.Value}' in __slots__ conflicts with class variable");
                result.Add(text.Value);
            }
            return result;
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
    /// Helper class for built-in methods
    /// </summary>
    /// <summary>
    /// CPython 3.12 호환: Descriptor protocol을 구현한 builtin method
    /// 클래스에서 접근 시 unbound, 인스턴스에서 접근 시 자동으로 bound method 반환
    /// </summary>
    public class PyBuiltinMethod : PyObject, IDescriptor
    {
        public string Name { get; }
        public Func<PyObject, PyObject[], PyObject> Method { get; }
        public int ArgCount { get; } // -1 for variable args

        public PyBuiltinMethod(string name, Func<PyObject, PyObject[], PyObject> method, int argCount = -1)
        {
            Name = name;
            Method = method;
            ArgCount = argCount;
        }

        public override string ToString()
        {
            return $"<built-in method '{Name}'>";
        }

        public override PyType GetPyType()
        {
            return PyType.WrapperDescriptorType; // object-level slot wrappers (CPython)
        }

        // Descriptor protocol: instance.method → bound method
        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
            {
                // 클래스에서 접근: Class.method → unbound
                return this;
            }

            // 인스턴스에서 접근: instance.method → bound method
            return new PyBoundBuiltinMethod(instance, this);
        }

        public void Set(PyObject instance, PyObject value)
        {
            throw PyAttributeError.Create($"can't set attribute '{Name}'");
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create($"can't delete attribute '{Name}'");
        }

        public bool IsDataDescriptor() => false; // non-data descriptor

        // Unbound call: 첫 인자를 self로 사용
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            // The first argument is 'self' for unbound methods
            if (args.Length < 1)
            {
                throw PyTypeError.Create($"{Name}() missing required 'self' argument");
            }

            var self = args[0];
            var methodArgs = new PyObject[args.Length - 1];
            Array.Copy(args, 1, methodArgs, 0, methodArgs.Length);

            return Method(self, methodArgs);
        }

        public override bool IsCallable()
        {
            return true;
        }
    }

    /// <summary>
    /// Bound builtin method: self가 이미 바인딩된 메서드
    /// </summary>
    public class PyBoundBuiltinMethod : PyObject
    {
        private PyObject _instance;
        private PyBuiltinMethod _method;

        public PyBoundBuiltinMethod(PyObject instance, PyBuiltinMethod method)
        {
            _instance = instance;
            _method = method;
        }

        public override string ToString()
        {
            return $"<bound method '{_method.Name}' of {_instance}>";
        }

        public override PyType GetPyType()
        {
            return PyType.MethodWrapperType; // CPython: object-level bound slot wrappers
        }

        // Bound call: self 자동 추가
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return _method.Method(_instance, args);
        }

        public override bool IsCallable()
        {
            return true;
        }

        // Bound method attributes
        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__self__" => _instance,
                "__func__" => _method,
                "__name__" => new PyString(_method.Name),
                _ => base.GetAttribute(name)
            };
        }
    }

    /// <summary>
    /// CPython 3.12: Static builtin method (like tp_new)
    /// Behaves like @staticmethod - does NOT bind to instance
    /// </summary>
    public class PyStaticBuiltinMethod : PyObject, IDescriptor
    {
        public string Name { get; }
        public Func<PyObject[], PyDict, PyObject> Method { get; }

        private readonly PyType _owner;

        public PyStaticBuiltinMethod(string name, Func<PyObject[], PyDict, PyObject> method, PyType owner = null)
        {
            Name = name;
            Method = method;
            _owner = owner;
        }

        // tp_new_methoddef: __self__ is the type, which copyreg compares.
        public override PyObject GetAttribute(string name) => name switch
        {
            "__self__" when _owner != null => _owner,
            "__name__" => new PyString(Name),
            "__qualname__" when _owner != null => new PyString($"{_owner.Name}.{Name}"),
            _ => base.GetAttribute(name),
        };

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
    public class PyDictDescriptor : PyObject, IDescriptor
    {
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

        // PyDescrObject: inspect tells the type's own __dict__ descriptor from a shadowing one by these.
        public override PyObject GetAttribute(string name) => name switch
        {
            "__name__" => new PyString("__dict__"),
            "__objclass__" => PyType.TypeType,
            _ => base.GetAttribute(name),
        };

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
    public class PyBasesDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__bases__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
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
    public class PyBaseDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__base__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            var target = (instance != null && instance != PyNone.Instance) ? instance : owner;
            if (target is PyType pt)
            {
                if (pt.BaseTypes != null && pt.BaseTypes.Length > 0)
                {
                    return pt.BaseTypes[0];
                }
            }
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
    public class PyMroDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__mro__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
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
    public class PyDocDescriptor : PyObject, IDescriptor
    {
        public override string ToString() => "<attribute '__doc__' of 'type' objects>";
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "getset_descriptor";

        public PyObject Get(PyObject instance, PyType owner)
        {
            if (instance == null || instance == PyNone.Instance)
                return this;

            if (instance is PyClass pyClass)
            {
                // CPython: PyDict_GetItemWithError(dict, &_Py_ID(__doc__))
                // type_get_doc: what the dict holds is asked as a descriptor.
                if (pyClass.ClassDict.TryGetValue("__doc__", out var doc))
                    return PyClassInstance.CallDescriptorGet(doc, null, pyClass);

                // Return None if not found
                return PyNone.Instance;
            }

            return PyNone.Instance;
        }

        public void Set(PyObject instance, PyObject value)
        {
            if (instance is PyClass pyClass)
            {
                // CPython: PyDict_SetItem(dict, &_Py_ID(__doc__), value)
                pyClass.ClassDict["__doc__"] = value;
                pyClass.InvalidateTypeCache();
            }
            else
            {
                throw PyAttributeError.Create("attribute '__doc__' of 'type' objects is not writable");
            }
        }

        public void Delete(PyObject instance)
        {
            throw PyAttributeError.Create("can't delete attribute '__doc__'");
        }

        public bool IsDataDescriptor() => true;
    }
}
