using System;

namespace SharpPy
{
    /// <summary>
    /// The internal doc strings of builtins (core/BuiltinDocs.g.cs, from tools/gen_builtin_docs.py)
    /// and the two functions of Objects/typeobject.c that cut __doc__ and __text_signature__ out of
    /// one: "name(signature)\n--\n\ndoc" in the Argument Clinic form, or plain text.
    /// </summary>
    internal static partial class PyDocs
    {
        private const string EndMarker = ")\n--\n\n";

        internal static string Lookup(string key) => key != null && Internal.TryGetValue(key, out var doc) ? doc : null;

        // find_signature: the doc starts with the last component of name and an opening parenthesis.
        private static int FindSignature(string name, string doc)
        {
            if (doc == null)
                return -1;
            int dot = name.LastIndexOf('.');
            if (dot >= 0)
                name = name.Substring(dot + 1);
            if (!doc.StartsWith(name, StringComparison.Ordinal) || doc.Length <= name.Length || doc[name.Length] != '(')
                return -1;
            return name.Length;
        }

        // skip_signature: just past ")\n--\n\n", unless a blank line comes first.
        private static int SkipSignature(string doc, int at)
        {
            for (; at < doc.Length; at++)
            {
                if (string.CompareOrdinal(doc, at, EndMarker, 0, EndMarker.Length) == 0)
                    return at + EndMarker.Length;
                if (doc[at] == '\n' && at + 1 < doc.Length && doc[at + 1] == '\n')
                    return -1;
            }
            return -1;
        }

        // _PyType_DocWithoutSignature
        private static string WithoutSignature(string name, string doc)
        {
            int start = FindSignature(name, doc);
            if (start >= 0)
            {
                int end = SkipSignature(doc, start);
                if (end >= 0)
                    return doc.Substring(end);
            }
            return doc;
        }

        // _PyType_GetDocFromInternalDoc
        internal static PyObject DocFromInternal(string name, string doc)
        {
            string text = WithoutSignature(name, doc);
            return string.IsNullOrEmpty(text) ? PyNone.Instance : PyString.From(text);
        }

        // _PyType_GetTextSignatureFromInternalDoc
        internal static PyObject TextSignatureFromInternal(string name, string doc)
        {
            int start = FindSignature(name, doc);
            int end = start >= 0 ? SkipSignature(doc, start) : -1;
            if (end < 0)
                return PyNone.Instance;
            end -= EndMarker.Length - 1;
            return PyString.From(doc.Substring(start, end - start));
        }

        // A static type of builtins; type itself is one, though SharpPy builds it as a class.
        // A static type of another module is keyed by its module too.
        private static string TypeKey(PyType type) =>
            type.IsHeapType ? null
            : type.Module != null && type.Module != "builtins" ? type.Module + "." + type.Name
            : type.Name;

        /// <summary>The name a method descriptor is cut by (ml_name) and its internal doc.</summary>
        internal static (string Name, string Doc) Of(PyObject callable)
        {
            switch (callable)
            {
                case PyMethodDescriptor method:
                    return (method.Name, Member(method.OwnerType, method.Name));
                case PyBoundMethodDescriptor bound:
                    return Of((PyObject)bound.MethodDescriptor ?? bound.Callable);
                case PyWrapperDescriptor wrapper:
                    return (wrapper.Name, Member(wrapper.OwnerType, wrapper.Name));
                case PyBoundWrapperDescriptor bound:
                    return Of(bound.WrapperDescriptor);
                case PyClassMethodDescriptor method:
                    return (method.Name, Member(method.OwnerType, method.Name));
                case PyGetSetDescriptor getset:
                    return (getset.Name, Member(getset.OwnerType, getset.Name));
                case PyMemberDescriptor member:
                    return (member.Name, Member(member.OwnerType, member.Name));
                case PyBuiltinFunction function when function.OwnerType != null:
                    return (function.Name, Member(function.OwnerType, function.Name));
                case PyBuiltinFunction function:
                    return (function.Name, function.OwnerClass != null
                        ? Lookup(function.OwnerClass + "." + function.Name)
                        : Lookup(function.OwnerModule + "." + function.Name));
                case PyType type:
                    return (type.Name, TypeKey(type) is { } key ? Lookup(key) : null);
                default:
                    return (null, null);
            }
        }

        // The owner itself or, for a method it inherits, the type that defines it.
        private static string Member(PyType owner, string name)
        {
            if (owner == null)
                return null;
            var key = TypeKey(owner);
            return key == null ? null : Lookup(key + "." + name);
        }

        private static readonly System.Collections.Generic.HashSet<PyType> _readied = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// type_ready's type_dict_set_doc: every static type holds its tp_doc, cut, in its dict. Run
        /// over the static types there are, from object down, when builtins is made and when a native
        /// module has made its own (PyModule_AddType readies them); a type is done once.
        /// </summary>
        internal static void SetTypeDocs()
        {
            var pending = new System.Collections.Generic.Stack<PyType>();
            pending.Push(PyType.ObjectType);
            while (pending.Count > 0)
            {
                var type = pending.Pop();
                if (type.IsHeapType)
                    continue;
                lock (_readied)
                {
                    if (!_readied.Add(type))
                        goto next;
                    PySlotWrappers.AddModuleType(type);
                    if (TypeKey(type) is { } key && Lookup(key) is { } doc
                        && (!type.TypeDict.TryGetValue("__doc__", out var stored) || stored is PyNone))
                        type.TypeDict["__doc__"] = DocFromInternal(type.Name, doc);
                    // PyType_FromMetaclass: a type made from a spec holds __module__, the part of
                    // tp_name before the last dot, in its dict, where its instances see it too.
                    if (TypeLayouts.Measured(type.Name, out var layout) && layout.Spec && type.Module is { Length: > 0 } module
                        && !type.TypeDict.ContainsKey("__module__"))
                        type.TypeDict["__module__"] = PyString.From(module);
                }
                next:
                foreach (var subclass in type.LiveSubclasses())
                    pending.Push(subclass);
            }
        }

        // method_get_doc and wrapper_get_doc
        internal static PyObject Doc(PyObject callable)
        {
            var (name, doc) = Of(callable);
            return doc == null ? PyNone.Instance : DocFromInternal(name, doc);
        }

        // method_get_text_signature and wrapper_get_text_signature
        internal static PyObject TextSignature(PyObject callable)
        {
            var (name, doc) = Of(callable);
            return doc == null ? PyNone.Instance : TextSignatureFromInternal(name, doc);
        }

        // getset_get_doc and member_get_doc: the text as it stands.
        internal static PyObject PlainDoc(PyObject descriptor)
        {
            var (_, doc) = Of(descriptor);
            return doc == null ? PyNone.Instance : PyString.From(doc);
        }
    }
}
