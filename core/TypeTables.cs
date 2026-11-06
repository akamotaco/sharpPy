using System;
using SharpPy.Core;
using SharpPy.Modules;

namespace SharpPy
{
    /// <summary>
    /// tp_members, tp_getset and tp_methods of the builtin types that are not numbers or text. CPython
    /// finds every attribute of these through the type's dict: type(x).name, vars(type(x)) and dir(x)
    /// all read it, and PyObject_GenericGetAttr is what reads it for x.name. So a field is listed here
    /// with the kind of descriptor CPython gives it, and the object says how to read it.
    /// </summary>
    internal static class TypeTables
    {
        private static void Members<T>(PyType type, Func<T, string, PyObject> read, params string[] names) where T : PyObject
        {
            foreach (var name in names)
                type.TypeDict[name] = new PyMemberDescriptor(name, type, self => read((T)self, name));
        }

        private static void GetSets<T>(PyType type, Func<T, string, PyObject> read, params string[] names) where T : PyObject
        {
            foreach (var name in names)
                type.TypeDict[name] = new PyGetSetDescriptor(name, type, self => read((T)self, name));
        }

        // The object answers a method with a callable bound to it, so the descriptor calls that.
        private static void Methods<T>(PyType type, Func<T, string, PyObject> read, params string[] names) where T : PyObject
        {
            foreach (var name in names)
                type.TypeDict[name] = new PyMethodDescriptor(name, type,
                    (self, args, kwargs) => read((T)self, name).Call(args, kwargs), acceptsKwargs: true);
        }

        private static void Wrapper(PyType type, string name, Func<PyObject, PyObject[], PyObject> operation) =>
            type.TypeDict[name] = new PyWrapperDescriptor(name, type, (self, args, kwargs) => operation(self, args));

        // add_operators for tp_repr, tp_hash and tp_richcompare
        internal static void Repr(params PyType[] types)
        {
            foreach (var type in types)
                Wrapper(type, "__repr__", (self, args) => self.Repr());
        }

        internal static void Compared(params PyType[] types)
        {
            foreach (var type in types)
                foreach (var (name, op) in new[]
                {
                    ("__lt__", PyObject.CompareOp.LT), ("__le__", PyObject.CompareOp.LE), ("__eq__", PyObject.CompareOp.EQ),
                    ("__ne__", PyObject.CompareOp.NE), ("__gt__", PyObject.CompareOp.GT), ("__ge__", PyObject.CompareOp.GE),
                })
                    Wrapper(type, name, (self, args) => self.TpRichCompare(args[0], op));
        }

        internal static void Hashed(params PyType[] types)
        {
            foreach (var type in types)
                Wrapper(type, "__hash__", (self, args) => new PyInt(self.Hash()));
        }

        // A type that sets tp_getattro to PyObject_GenericGetAttr has the wrapper in its own dict.
        internal static void GenericAttributes(params PyType[] types)
        {
            var generic = PyType.ObjectType.TypeDict["__getattribute__"];
            foreach (var type in types)
                Wrapper(type, "__getattribute__", (self, args) => generic.Call(new[] { self, args[0] }, null));
        }

        [ThreadStatic] private static PyObject _fieldOwner;
        [ThreadStatic] private static string _fieldName;

        // These answer from a tp_getattro or an instance switch. A kind that has no answer comes
        // back here through the generic lookup, and that is the AttributeError.
        private static PyObject Field(PyObject self, string name)
        {
            if (ReferenceEquals(_fieldOwner, self) && _fieldName == name)
                throw PyAttributeError.Create($"'{self.GetTypeName()}' object has no attribute '{name}'");
            var (owner, field) = (_fieldOwner, _fieldName);
            (_fieldOwner, _fieldName) = (self, name);
            try
            {
                return self.GetAttribute(name);
            }
            finally
            {
                (_fieldOwner, _fieldName) = (owner, field);
            }
        }

        internal static void Install()
        {
            var code = PyCodeObject.CodePyType;
            Members<PyCodeObject>(code, (self, name) => self.Read(name),
                "co_argcount", "co_posonlyargcount", "co_kwonlyargcount", "co_stacksize", "co_flags", "co_nlocals",
                "co_consts", "co_names", "co_filename", "co_name", "co_qualname", "co_firstlineno", "co_linetable",
                "co_exceptiontable");
            GetSets<PyCodeObject>(code, (self, name) => self.Read(name),
                "co_lnotab", "_co_code_adaptive", "co_varnames", "co_cellvars", "co_freevars", "co_code");
            Methods<PyCodeObject>(code, (self, name) => self.Read(name), "co_lines", "co_positions", "replace", "_varname_from_oparg");
            Repr(code);
            Hashed(code);
            Compared(code);

            var frame = PyFrame.FramePyType;
            GetSets<PyFrame>(frame, (self, name) => self.Read(name), "f_back", "f_locals", "f_lineno", "f_lasti",
                "f_globals", "f_builtins", "f_code");
            // frame_settrace: None or a deletion clears it.
            frame.TypeDict["f_trace"] = new PyGetSetDescriptor("f_trace", frame, self => ((PyFrame)self).Read("f_trace"),
                (self, value) => ((PyFrame)self).Trace = value is PyNone ? null : value, deleter: self => ((PyFrame)self).Trace = null);
            // frame_settrace_opcodes: a bool, which this interpreter does not act on.
            frame.TypeDict["f_trace_opcodes"] = new PyGetSetDescriptor("f_trace_opcodes", frame, self => ((PyFrame)self).Read("f_trace_opcodes"),
                (self, value) => { if (value is not PyBool) throw PyTypeError.Create("attribute value type must be bool"); });
            // frame_memberlist: f_trace_lines is a T_BOOL member.
            frame.TypeDict["f_trace_lines"] = new PyMemberDescriptor("f_trace_lines", frame, self => ((PyFrame)self).Read("f_trace_lines"),
                (self, value) => ((PyFrame)self).TraceLines = value is PyBool flag ? flag.Value : throw PyTypeError.Create("attribute value type must be bool"),
                deleter: self => throw PyTypeError.Create("can't delete numeric/char attribute"));
            Methods<PyFrame>(frame, (self, name) => self.Read(name), "clear");
            Repr(frame);

            var traceback = PyTraceback.TracebackPyType;
            Members<PyTraceback>(traceback, (self, name) => self.Read(name), "tb_frame", "tb_lasti");
            GetSets<PyTraceback>(traceback, (self, name) => self.Read(name), "tb_lineno");
            traceback.TypeDict["tb_next"] = new PyGetSetDescriptor("tb_next", traceback, self => ((PyTraceback)self).Read("tb_next"),
                (self, value) => ((PyTraceback)self).SetNext(value), deleter: self => throw PyTypeError.Create("can't delete tb_next attribute"));

            PyType.CellType.TypeDict["cell_contents"] = new PyGetSetDescriptor("cell_contents", PyType.CellType, self => ((PyCell)self).Contents(),
                (self, value) => ((PyCell)self).Value = value, deleter: self => ((PyCell)self).Value = null);
            PyType.CellType.TypeDict["__hash__"] = PyNone.Instance;

            Members<PyRange>(PyType.RangeType, (self, name) => self.Read(name), "start", "stop", "step");
            Members<PySlice>(PyType.SliceType, (self, name) => self.Read(name), "start", "stop", "step");

            GetSets<PyMemoryView>(PyType.MemoryViewType, (self, name) => self.Read(name), "obj", "nbytes", "readonly", "itemsize",
                "format", "ndim", "shape", "strides", "suboffsets", "c_contiguous", "f_contiguous", "contiguous");


            // The ones that keep a tp_getattro of their own, or have several implementations
            // behind one type: the object already answers these names.
            Members<PySuper>(PyType.SuperType, (self, name) => self.Own(name), "__thisclass__", "__self__", "__self_class__");
            Wrapper(PyType.SuperType, "__get__", (self, args) => ((IDescriptor)self).Get(args[0] is PyNone ? null : args[0], args.Length > 1 ? args[1] as PyType : null));
            Members<PyObject>(PyType.MethodType, Field, "__func__", "__self__");
            PyCFunction.Install();
            foreach (var type in new[] { PyType.MethodDescriptorType, PyType.WrapperDescriptorType, PyType.ClassMethodDescriptorType,
                         PyType.GetSetDescriptorType, PyType.MemberDescriptorType })
            {
                PyDescrObject.Install(type);
                Wrapper(type, "__get__", DescriptorGet);
            }
            foreach (var type in new[] { PyType.GetSetDescriptorType, PyType.MemberDescriptorType })
            {
                Wrapper(type, "__set__", DescriptorSet);
                Wrapper(type, "__delete__", DescriptorDelete);
            }
            PyMethodWrapper.Install();
            // method_get_doc, method_get_text_signature and their wrapper_ counterparts cut the
            // internal doc; getset_get_doc and member_get_doc hand it over as it is.
            foreach (var type in new[] { PyType.BuiltinFunctionOrMethodType, PyType.MethodDescriptorType, PyType.WrapperDescriptorType,
                         PyType.ClassMethodDescriptorType, PyType.MethodWrapperType })
            {
                GetSets<PyObject>(type, (self, name) => PyDocs.Doc(self), "__doc__");
                GetSets<PyObject>(type, (self, name) => PyDocs.TextSignature(self), "__text_signature__");
            }
            foreach (var type in new[] { PyType.GetSetDescriptorType, PyType.MemberDescriptorType })
                GetSets<PyObject>(type, (self, name) => PyDocs.PlainDoc(self), "__doc__");
            Members<PyGenericAlias>(PyType.GenericAliasType, Field, "__origin__", "__args__", "__unpacked__");
            GetSets<PyGenericAlias>(PyType.GenericAliasType, Field, "__parameters__", "__typing_unpacked_tuple_args__");
            Members<PyUnionType>(PyType.UnionType, (self, name) => new PyTuple(self.Args), "__args__");
            GetSets<PyUnionType>(PyType.UnionType, (self, name) => PyGenericAlias.MakeParameters(new PyTuple(self.Args)), "__parameters__");
            Wrapper(PyType.UnionType, "__getitem__", (self, args) => ((PyUnionType)self).Subscript(args[0]));
            Repr(PyType.UnionType);
            // ga_repr itself: the virtual ToRepr of a subclass instance comes back here through __repr__.
            Wrapper(PyType.GenericAliasType, "__repr__", (self, args) => PyGenericAlias.Repr(self));
            Hashed(PyType.UnionType);
            Compared(PyType.UnionType);

            // PEP 688: what memoryview(x) does, by the method.
            foreach (var type in new[] { PyType.BytesType, PyType.BytearrayType, PyType.MemoryViewType })
                Wrapper(type, "__buffer__", (self, args) => PyType.MemoryViewType.Call(new[] { self }, null));
            foreach (var type in new[] { PyType.BytearrayType, PyType.MemoryViewType })
                Wrapper(type, "__release_buffer__", (self, args) => PyNone.Instance);
            Repr(PyType.NoneType, PyType.NotImplementedType, PyType.EllipsisType);
            Hashed(PyType.NoneType);
            Compared(PyType.NoneType);

            // A metaclass reaches these by super(): type's own answer, with no hook asked again.
            PyType.TypeType.TypeDict["__instancecheck__"] = new PyMethodDescriptor("__instancecheck__", PyType.TypeType,
                (self, args, kwargs) => BuiltinsModule.RealIsInstance(args[0], self), 1, 1);
            PyType.TypeType.TypeDict["__subclasscheck__"] = new PyMethodDescriptor("__subclasscheck__", PyType.TypeType,
                (self, args, kwargs) => BuiltinsModule.RealIsSubclass(args[0], self), 1, 1);
            PyType.TypeType.TypeDict["__abstractmethods__"] = new PyGetSetDescriptor("__abstractmethods__", PyType.TypeType,
                self => !ReferenceEquals(self, PyType.TypeType) && self is PyClass cls && cls.ClassDict.TryGetValue("__abstractmethods__", out var names)
                    ? names : throw PyAttributeError.Create("__abstractmethods__"),
                (self, value) => ((PyClass)self).ClassDict["__abstractmethods__"] = value);
            Wrapper(PyType.TypeType, "__or__", (self, args) => PyUnionType.Or(self, args[0]));
            Wrapper(PyType.TypeType, "__ror__", (self, args) => PyUnionType.Or(args[0], self));

            GenericAttributes(PyType.RangeType, PyType.SliceType, PyType.MethodType, PyType.MemoryViewType, PyType.GenericAliasType,
                PyType.UnionType, PyType.BaseExceptionType, PyType.PropertyType, PyType.GetIteratorType("enumerate"), PyType.ReversedType,
                PyType.BytearrayType, PyType.BytesType, PyType.IntType, PyType.TupleType, PyType.ListType, PyType.DictType,
                PyType.SetType, PyType.ComplexType, PyType.ZipType, PyType.MapType, PyType.FilterType);
        }

        internal static void InstallSre(PyType pattern, PyType match, PyType scanner)
        {
            Members<PySreScanner>(scanner, (self, name) => self.Read(name), "pattern");
            Methods<PySreScanner>(scanner, (self, name) => self.Read(name), "match", "search");
            GetSets<PySrePattern>(pattern, (self, name) => self.Read(name), "groupindex");
            Members<PySrePattern>(pattern, (self, name) => self.Read(name), "pattern", "flags", "groups");
            Methods<PySrePattern>(pattern, (self, name) => self.Read(name), "match", "fullmatch", "search",
                "findall", "finditer", "split", "sub", "subn", "scanner", "__copy__", "__deepcopy__");
            Repr(pattern);
            Hashed(pattern);
            Compared(pattern);
            Members<PySreMatch>(match, (self, name) => self.Read(name), "string", "re", "pos", "endpos");
            GetSets<PySreMatch>(match, (self, name) => self.Read(name), "lastindex", "lastgroup", "regs");
            Methods<PySreMatch>(match, (self, name) => self.Read(name), "group", "groups", "groupdict", "start",
                "end", "span", "expand", "__copy__", "__deepcopy__");
            Wrapper(match, "__getitem__", (self, args) => self.MpSubscript(args[0]));
            Repr(match);
        }

        // Py_tp_setattro of PyObject_GenericSetAttr: the __setattr__ and __delattr__ wrappers.
        internal static void GenericSetAttributes(params PyType[] types)
        {
            var set = PyType.ObjectType.TypeDict["__setattr__"];
            var delete = PyType.ObjectType.TypeDict["__delattr__"];
            foreach (var type in types)
            {
                Wrapper(type, "__setattr__", (self, args) => set.Call(new[] { self, args[0], args[1] }, null));
                Wrapper(type, "__delattr__", (self, args) => delete.Call(new[] { self, args[0] }, null));
            }
        }

        private static PyObject[] Prepended(PyObject self, PyObject[] args)
        {
            var all = new PyObject[args.Length + 1];
            all[0] = self;
            Array.Copy(args, 0, all, 1, args.Length);
            return all;
        }

        // After every install: the last word on the kind of what is in the dict.
        internal static void Finish()
        {
            // Where the implementation is a slot in one and a method in the other, the type says which.
            foreach (var (type, name) in new[]
            {
                (PyType.DictType, "__setitem__"), (PyType.DictType, "__delitem__"), (PyType.TupleType, "__len__"),
                (PyType.TupleType, "__rmul__"), (PyType.SetType, "__iand__"), (PyType.SetType, "__isub__"), (PyType.SetType, "__ixor__"),
            })
                if (type.TypeDict.TryGetValue(name, out var method) && method is PyMethodDescriptor)
                    Wrapper(type, name, (self, args) => method.Call(Prepended(self, args), null));
            foreach (var (type, name) in new[]
            {
                (PyType.DictType, "__contains__"), (PyType.SetType, "__contains__"), (PyType.FrozenSetType, "__contains__"),
                (PyType.IntType, "__ceil__"), (PyType.IntType, "__floor__"), (PyType.IntType, "__trunc__"),
                (PyType.FloatType, "__ceil__"), (PyType.FloatType, "__floor__"), (PyType.FloatType, "__trunc__"),
            })
                if (type.TypeDict.TryGetValue(name, out var wrapper) && wrapper is PyWrapperDescriptor)
                    type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) => wrapper.Call(Prepended(self, args), null));
        }

        // wrap_descr_get
        // wrap_descr_get (Objects/typeobject.c): PyArg_UnpackTuple(args, "", 1, 2), None is NULL.
        private static PyObject DescriptorGet(PyObject self, PyObject[] args)
        {
            PyArgs.Positional("", args, null, 1, 2);
            var instance = args[0] is not PyNone ? args[0] : null;
            var owner = args.Length > 1 && args[1] is not PyNone ? args[1] : null;
            if (instance == null && owner == null)
                throw PyTypeError.Create("__get__(None, None) is invalid");
            // classmethod_get is the one tp_descr_get here that reads a type that is not a type.
            if (self is PyClassMethodDescriptor classMethod)
                return classMethod.ClassMethodGet(instance, owner);
            return ((IDescriptor)self).Get(instance, owner as PyType);
        }

        // wrap_descr_set: PyArg_UnpackTuple(args, "", 2, 2).
        private static PyObject DescriptorSet(PyObject self, PyObject[] args)
        {
            PyArgs.Positional("", args, null, 2, 2);
            ((IDescriptor)self).Set(args[0], args[1]);
            return PyNone.Instance;
        }

        // wrap_descr_delete: check_num_args(args, 1).
        private static PyObject DescriptorDelete(PyObject self, PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
            ((IDescriptor)self).Delete(args[0]);
            return PyNone.Instance;
        }
    }
}
