using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>An instance of a Python class over a builtin value: __class__ can change.</summary>
    /// <summary>
    /// Attribute access for instances of a builtin type that a Python class derives from
    /// (str/bytes/tuple/int/float subclasses). Their class dicts hold user descriptors and functions,
    /// which the CLR inheritance chain does not see, so MRO lookup plus the instance __dict__ has to
    /// run before falling back to the payload type's own behaviour.
    ///
    /// Shared by PyTupleSubclass and PyBytesSubclass. C# single inheritance prevents a common base
    /// class here (the payload types differ), which is why this is a helper rather than a superclass.
    /// </summary>
    internal static class SubclassInstanceSupport
    {
        internal static PyObject FirstClassDictEntry(PyClass instanceType, string name)
        {
            foreach (var mroType in instanceType.MRO)
            {
                if (mroType is PyClass pyClass && pyClass.ClassDict.TryGetValue(name, out PyObject value))
                    return value;
            }
            return null;
        }

        // --- inherited slots versus a declared dunder --------------------------------------
        // CPython decides once, in update_one_slot: if the class dict names __len__, __repr__,
        // __contains__..., the slot becomes a wrapper that calls that function; otherwise the slot
        // stays the base's C function. A CLR subclass of PyList/PyDict/PySet inherits the base's C#
        // behaviour instead, so the same decision is needed before each protocol method. Written
        // once here because C# single inheritance gives the four container subclasses no common
        // superclass (Objects/typeobject.c: update_one_slot).

        /// <summary>
        /// tp_repr is inherited, so a subclass prints its contents ([1, 2], {'a': 1}); a class that
        /// declares __repr__ replaces the slot. CPython also insists the declared function returned
        /// a str, and says so when it did not.
        /// </summary>
        // object_set_class
        internal static PyClass ClassAssignment(PyClass current, PyObject value)
        {
            if (value is not PyType target)
                throw PyTypeError.Create($"__class__ must be set to a class, not '{value.GetTypeName()}' object");
            if (target is not PyClass heapTarget || target is PyTypeMetaclass)
                throw PyTypeError.Create("__class__ assignment only supported for mutable types or ModuleType subclasses");
            PyTypeMetaclass.CheckAssignment(current, heapTarget, "__class__");
            return heapTarget;
        }

        /// <summary>
        /// _PyObject_GenericGetAttrWithDict (Objects/object.c): a data descriptor of the type, then
        /// the instance dict (dict, else the object's own store), then a non-data descriptor or plain
        /// value of the type. Null when the name is in none of them: the AttributeError, or what a
        /// builtin's own tp_getattro answers beyond its dict, is the caller's.
        /// </summary>
        internal static PyObject GenericGetAttrWithDict(PyObject self, PyType type, string name,
            Dictionary<string, PyObject> dict, IInstanceDictAccessor accessor)
        {
            var attribute = type.FindNameInMro(name);
            IDescriptor descriptor = attribute as IDescriptor
                ?? (attribute != null && attribute.LookupSpecialMethodUncached("__get__") != null
                    ? new PyDescriptorWrapper(attribute) : null);
            if (descriptor != null && descriptor.IsDataDescriptor())
                return descriptor.Get(self, type);
            if (dict != null ? dict.TryGetValue(name, out PyObject instanceValue)
                : accessor != null && accessor.TryGetInstanceAttribute(name, out instanceValue))
                return instanceValue;
            if (attribute == null)
                return null;
            if (attribute.GetType() == typeof(PyFunction))
                return new PyMethod(self, attribute);
            return descriptor != null ? descriptor.Get(self, type) : attribute;
        }

        /// <summary>
        /// slot_tp_setattro (Objects/typeobject.c): a __setattr__ or __delattr__ written in a class
        /// body is the slot of the type, which calls the one the operation names (value null to
        /// delete). False when the class wrote neither, so the generic set or delete applies.
        /// </summary>
        internal static bool SetattroHook(PyObject self, PyClass type, string name, PyObject value)
        {
            var method = type.FindNameInMro(value == null ? "__delattr__" : "__setattr__", out bool written);
            if (!written)
                return false;
            var bound = self.BindSpecialMethod(method);
            bound.Call(value == null ? new PyObject[] { new PyString(name) } : new[] { new PyString(name), value }, null);
            return true;
        }


        /// <summary>
        /// _PyObject_GenericSetAttrWithDict (Objects/object.c), value null to delete: a data
        /// descriptor of the type sets or deletes; else the instance dict - the dict given, else the
        /// store given - which an object without __dict__ lacks. object's __class__ getset
        /// (object_set_class) refuses deletion.
        /// </summary>
        internal static void GenericSetAttrWithDict(PyObject self, PyType type, string name, PyObject value,
            Dictionary<string, PyObject> dict, IInstanceDictStore store)
        {
            if (name == "__class__" && value == null)
                throw PyTypeError.Create("can't delete __class__ attribute");
            var descriptor = PyClassInstance.FindDataDescriptorInMro(type, name);
            if (descriptor != null)
            {
                if (value == null) PyClassInstance.CallDescriptorDelete(descriptor, self);
                else PyClassInstance.CallDescriptorSet(descriptor, self, value);
                return;
            }
            bool hasDict = type is PyClass heap ? heap.InstancesHaveDict() : dict != null || store != null;
            if (value == null)
            {
                if (dict != null ? PyGlobalsDict.Delete(dict, name) : store != null && store.RemoveInstanceAttribute(name))
                    return;
                throw NoAttribute(self, type, name, hasDict);
            }
            if (!hasDict && !(type is PyClass slotted && slotted.IsSlottedAttributeName(name)))
                throw NoAttribute(self, type, name, hasDict);
            if (dict != null) PyGlobalsDict.Store(dict, name, value);
            else store.SetInstanceAttribute(name, value);
        }

        // A name the type holds but that is no data descriptor is read-only without an instance dict.
        private static Exception NoAttribute(PyObject self, PyType type, string name, bool hasDict) =>
            !hasDict && type.FindNameInMro(name) != null
                ? PyAttributeError.Create($"'{self.GetTypeName()}' object attribute '{name}' is read-only")
                : self.NoSuchAttribute(name);

        /// <summary>The generic set over the dict kept beside a builtin's value; value null to delete.</summary>
        internal static void GenericSetAttribute(PyObject self, PyClass instanceType,
            Dictionary<string, PyObject> instanceDict, string name, PyObject value)
        {
            // object_set_class
            if (name == "__class__" && value != null)
            {
                self.SetCustomType(ClassAssignment(instanceType, value));
                return;
            }
            // object_set_dict: the storage stays, __dict__ is a live view of it.
            if (name == "__dict__" && value != null)
            {
                ReplaceDict(instanceDict, value);
                return;
            }
            GenericSetAttrWithDict(self, instanceType, name, value, instanceDict, null);
        }

        // PyObject_GenericSetDict over a dict kept beside the value: its contents become the new dict's.
        private static void ReplaceDict(Dictionary<string, PyObject> instanceDict, PyObject value)
        {
            var replacement = (value as PyDict)
                ?? throw PyTypeError.Create($"__dict__ must be set to a dictionary, not a '{value.GetTypeName()}'");
            var copy = new List<KeyValuePair<PyObject, PyObject>>(replacement.InternalDict);
            PyGlobalsDict.Clear(instanceDict);
            foreach (var entry in copy) PyGlobalsDict.Store(instanceDict, ((PyString)entry.Key).Value, entry.Value);
        }

        /// <summary>
        /// PyObject_GenericGetDict: the instance dict, wherever this runtime keeps it - a Python
        /// instance's own store, the dict a builtin holds (IObjectDict, IDictBackedInstance), or the
        /// one kept beside a builtin's value for a class written in Python.
        /// </summary>
        internal static PyObject GenericGetDict(PyObject self) => self switch
        {
            PyClassInstance instance => instance.GetAttributeGeneric("__dict__"),
            IObjectDict holder => holder.ObjectDict(true),
            IDictBackedInstance owner => PyGlobalsDict.OfInstance(owner.InstanceDict),
            _ => PyGlobalsDict.For(self.HeapDict),
        };

        /// <summary>PyObject_GenericSetDict: a dict, which becomes the object's.</summary>
        internal static void GenericSetDict(PyObject self, PyObject value)
        {
            switch (self)
            {
                case PyClassInstance instance:
                    instance.SetDict(value);
                    break;
                case IObjectDict holder:
                    holder.SetObjectDict(value as PyDict ?? throw PyTypeError.Create(
                        $"__dict__ must be set to a dictionary, not a '{value.GetTypeName()}'"));
                    break;
                case IDictBackedInstance owner:
                    ReplaceDict(owner.InstanceDict, value);
                    break;
                default:
                    ReplaceDict(self.HeapDict, value);
                    break;
            }
        }

        /// <summary>{"__dict__", PyObject_GenericGetDict, PyObject_GenericSetDict}, which refuses deletion.</summary>
        internal static PyGetSetDescriptor GenericDictGetSet(PyType type) => new("__dict__", type,
            GenericGetDict, GenericSetDict, deleter: self => throw PyTypeError.Create("cannot delete __dict__"));
    }
}
