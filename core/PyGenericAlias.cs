// Objects/genericaliasobject.c of CPython v3.12.15
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>list[int], tuple[str, int]: a PEP 585 alias, and the base of classes deriving from it.</summary>
    public class PyGenericAlias : PyObject
    {
        private readonly bool _starred;
        private PyTuple _parameters;

        public PyObject Origin { get; }
        public PyTuple Args { get; }

        // setup_ga
        public PyGenericAlias(PyObject origin, PyObject args, PyType type = null, bool starred = false)
        {
            if (type != null && !ReferenceEquals(type, PyType.GenericAliasType)) SetCustomType(type);
            _starred = starred;
            Origin = origin;
            Args = args as PyTuple ?? PyTuple.From(new[] { args });
        }

        public override PyType GetPyType() => _customType ?? PyType.GenericAliasType;

        public override string GetTypeName() => GetPyType().FullName;

        private static PyObject LookupAttr(PyObject value, string name)
        {
            try
            {
                return value.GetAttribute(name);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return null;
            }
        }

        internal static void Install()
        {
            var type = PyType.GenericAliasType;
            // ga_instancecheck and ga_subclasscheck (METH_O): a parameterized generic is no class.
            type.TypeDict["__instancecheck__"] = PyArgs.Method(type, "__instancecheck__", PyCall.One, (self, args) =>
                throw PyTypeError.Create("isinstance() argument 2 cannot be a parameterized generic"), 1, 1);
            type.TypeDict["__subclasscheck__"] = PyArgs.Method(type, "__subclasscheck__", PyCall.One, (self, args) =>
                throw PyTypeError.Create("issubclass() argument 2 cannot be a parameterized generic"), 1, 1);
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("GenericAlias() takes no keyword arguments");
                if (args.Length != 2)
                    throw PyTypeError.Create($"GenericAlias expected 2 arguments, got {args.Length}");
                return new PyGenericAlias(args[0], args[1], (PyType)cls);
            }, acceptsKwargs: true);
        }

        internal static PyObject Repr(PyObject self) => PyString.From(((PyGenericAlias)self).GaRepr());

        internal static PyObject GetItemSlot(PyObject self, PyObject item) => ((PyGenericAlias)self).GaGetItem(item);

        internal static PyObject MroEntries(PyObject self) => PyTuple.From(new[] { ((PyGenericAlias)self).Origin });

        internal static PyObject Reduce(PyObject self)
        {
            var alias = (PyGenericAlias)self;
            // ga_reduce: a starred alias is next(iter(origin[args])), which is how it unpickles.
            if (alias._starred)
                return PyTuple.From(PyVM.CurrentFrame?.Builtin("next") ?? throw PyAttributeError.Create("next"),
                    PyTuple.From(new PyGenericAlias(alias.Origin, alias.Args).GetIterator()));
            return PyTuple.From(new PyObject[] { alias.GetPyType(), PyTuple.From(new[] { alias.Origin, alias.Args }) });
        }

        // attr_exceptions (Objects/genericaliasobject.c:631): the alias's own names, which ga_getattro
        // answers itself and ga_dir adds to the origin's.
        internal static readonly string[] AttrExceptions =
        {
            "__class__", "__origin__", "__args__", "__unpacked__", "__parameters__", "__typing_unpacked_tuple_args__",
            "__mro_entries__", "__reduce_ex__", "__reduce__", "__copy__", "__deepcopy__",
        };

        // attr_exceptions and ga_getattro: what is not the alias's own goes to the origin.
        // A subclass inherits ga_getattro, which forwards to __origin__.
        internal override PyObject InheritedGetattro(string name) => TpGetattro(name);

        protected internal override PyObject TpGetattro(string name)
        {
            switch (name)
            {
                case "__class__": return GetPyType();
                case "__origin__": return Origin;
                case "__args__": return Args;
                case "__unpacked__": return PyBool.FromBool(_starred);
                case "__parameters__":
                    return _parameters ??= MakeParameters(Args);
                case "__typing_unpacked_tuple_args__":
                    return _starred && ReferenceEquals(Origin, PyType.TupleType) ? Args : PyNone.Instance;
                case "__mro_entries__":
                case "__reduce_ex__":
                case "__reduce__":
                case "__copy__":
                case "__deepcopy__":
                    return base.TpGetattro(name);
            }
            return Origin.GetAttribute(name);
        }

        // _Py_make_parameters
        internal static PyTuple MakeParameters(PyTuple args)
        {
            var parameters = new List<PyObject>();
            void Add(PyObject item)
            {
                if (!parameters.Any(known => ReferenceEquals(known, item)))
                    parameters.Add(item);
            }
            foreach (var t in args.Items)
            {
                // We don't want __parameters__ descriptor of a bare Python class.
                if (t is PyType)
                    continue;
                if (LookupAttr(t, "__typing_subst__") != null)
                    Add(t);
                else if (LookupAttr(t, "__parameters__") is PyTuple subparams)
                    foreach (var t2 in subparams.Items)
                        Add(t2);
            }
            return PyTuple.From(parameters.ToArray());
        }

        // _is_unpacked_typevartuple
        private static bool IsUnpackedTypeVarTuple(PyObject arg)
        {
            if (arg is PyType)
                return false;
            return LookupAttr(arg, "__typing_is_unpacked_typevartuple__") is PyObject value && value.IsTrue();
        }

        // _unpacked_tuple_args
        private static PyTuple UnpackedTupleArgs(PyObject arg)
        {
            if (arg is PyGenericAlias { _starred: true } alias && ReferenceEquals(alias.Origin, PyType.TupleType))
                return alias.Args;
            if (LookupAttr(arg, "__typing_unpacked_tuple_args__") is PyTuple result)
                return result;
            return null;
        }

        // _unpack_args
        private static PyObject UnpackArgs(PyObject item)
        {
            var items = item is PyTuple tuple ? tuple.Items : new[] { item };
            var newitems = new List<PyObject>();
            foreach (var part in items)
            {
                if (part is not PyType)
                {
                    var subargs = UnpackedTupleArgs(part);
                    if (subargs != null && !(subargs.Items.Length != 0 && subargs.Items[subargs.Items.Length - 1] is PyEllipsis))
                    {
                        newitems.AddRange(subargs.Items);
                        continue;
                    }
                }
                newitems.Add(part);
            }
            return PyTuple.From(newitems.ToArray());
        }

        private static int TupleIndex(PyObject[] self, PyObject item)
        {
            for (int i = 0; i < self.Length; i++)
                if (ReferenceEquals(self[i], item))
                    return i;
            return -1;
        }

        // subs_tvars
        private static PyObject SubsTvars(PyObject obj, PyTuple parameters, PyObject[] argitems)
        {
            if (LookupAttr(obj, "__parameters__") is PyTuple subparams && subparams.Items.Length != 0)
            {
                var subargs = new List<PyObject>();
                foreach (var original in subparams.Items)
                {
                    var arg = original;
                    int iparam = TupleIndex(parameters.Items, arg);
                    if (iparam >= 0)
                    {
                        var param = parameters.Items[iparam];
                        arg = argitems[iparam];
                        if (param.HasTypeSlot("__iter__") && arg is PyTuple unpacked) // TypeVarTuple
                        {
                            subargs.AddRange(unpacked.Items);
                            continue;
                        }
                    }
                    subargs.Add(arg);
                }
                return obj.GetItem(PyTuple.From(subargs.ToArray()));
            }
            return obj;
        }

        // _Py_subs_parameters
        internal static PyTuple SubsParameters(PyObject self, PyTuple args, PyTuple parameters, PyObject item)
        {
            int nparams = parameters.Items.Length;
            if (nparams == 0)
                throw PyTypeError.Create($"{self.ToRepr().Value} is not a generic class");
            item = UnpackArgs(item);
            foreach (var param in parameters.Items)
            {
                var prepare = LookupAttr(param, "__typing_prepare_subst__");
                if (prepare != null && prepare is not PyNone)
                    item = prepare.Call(new[] { self, item is PyTuple ? item : PyTuple.From(new[] { item }) }, null);
            }
            var argitems = item is PyTuple tuple ? tuple.Items : new[] { item };
            int nitems = argitems.Length;
            if (nitems != nparams)
                throw PyTypeError.Create($"Too {(nitems > nparams ? "many" : "few")} arguments for {self.ToRepr().Value}; actual {nitems}, expected {nparams}");

            var newargs = new List<PyObject>();
            foreach (var original in args.Items)
            {
                var arg = original;
                if (arg is PyType)
                {
                    newargs.Add(arg);
                    continue;
                }
                bool unpack = IsUnpackedTypeVarTuple(arg);
                var subst = LookupAttr(arg, "__typing_subst__");
                arg = subst != null
                    ? subst.Call(new[] { argitems[TupleIndex(parameters.Items, arg)] }, null)
                    : SubsTvars(arg, parameters, argitems);
                if (unpack)
                    newargs.AddRange(((PyTuple)arg).Items);
                else
                    newargs.Add(arg);
            }
            return PyTuple.From(newargs.ToArray());
        }

        // ga_getitem
        private PyObject GaGetItem(PyObject item)
        {
            _parameters ??= MakeParameters(Args);
            var newargs = SubsParameters(this, Args, _parameters, item);
            return new PyGenericAlias(Origin, newargs, PyType.GenericAliasType, _starred);
        }

        protected internal override PyObject MpSubscript(PyObject key) => GaGetItem(key);

        // ga_call and set_orig_class
        public override PyObject Call(PyObject[] args, PyDict kwargs)
        {
            var obj = Origin.Call(args, kwargs);
            try
            {
                obj.SetAttribute("__orig_class__", this);
            }
            catch (PythonException error) when (error.PyException is PyAttributeError or PyTypeError)
            {
            }
            return obj;
        }

        public override bool IsCallable() => true;

        // ga_hash
        public override int ToHash() => Origin.ToHash() ^ Args.ToHash();

        protected override int GetDefaultHash() => ToHash();

        // ga_repr_item
        private static string ReprItem(PyObject p)
        {
            if (p is PyEllipsis)
                return "...";
            if (LookupAttr(p, "__origin__") != null && LookupAttr(p, "__args__") != null)
                return p.ToRepr().Value;
            var qualname = LookupAttr(p, "__qualname__");
            if (qualname == null)
                return p.ToRepr().Value;
            var module = LookupAttr(p, "__module__");
            if (module == null || module is PyNone)
                return p.ToRepr().Value;
            if (module is PyString { Value: "builtins" })
                return qualname.AsString().Value;
            return $"{module.AsString().Value}.{qualname.AsString().Value}";
        }

        // ga_repr_items_list
        private static string ReprItemsList(PyObject p)
        {
            if (p is PyList list)
                return "[" + string.Join(", ", list.Items.Select(ReprItem)) + "]";
            return ReprItem(p);
        }

        // ga_repr
        private string GaRepr()
        {
            var text = new System.Text.StringBuilder();
            if (_starred)
                text.Append('*');
            text.Append(ReprItem(Origin)).Append('[');
            if (Args.Items.Length == 0)
                text.Append("()");
            else
                text.Append(string.Join(", ", Args.Items.Select(ReprItemsList)));
            text.Append(']');
            return text.ToString();
        }

        protected internal override PyString Repr() => PyString.From(GaRepr());

        protected internal override PyString Str() => ToRepr();

        protected internal override bool Bool() => true;

        // ga_richcompare
        protected override PyObject PyEquals(PyObject other)
        {
            if (other is not PyGenericAlias alias)
                return PyNotImplemented.Instance;
            if (_starred != alias._starred)
                return PyBool.False;
            if (!PyObject.CompareBool(Origin, alias.Origin, PyObject.CompareOp.EQ))
                return PyBool.False;
            return Compare(Args, alias.Args, CompareOp.EQ);
        }

        // ga_iter: *tuple[int] is an alias that unpacks, through a generic_alias_iterator.
        protected internal override PyObject Iter() => new PyGenericAliasIterator(this);

        public override PyObject BitwiseOr(PyObject other) => PyUnionType.Or(this, other);
    }

    /// <summary>
    /// gaiterobject (Objects/genericaliasobject.c): one starred alias of the one it was made from
    /// (ga_iternext, through Py_GenericAlias, so an exact GenericAlias), then nothing.
    /// ga_iter_reduce: (iter, (obj,)) while it has it, (iter, ((),)) after.
    /// </summary>
    internal sealed class PyGenericAliasIterator : PyIterator
    {
        private PyGenericAlias _obj;

        internal PyGenericAliasIterator(PyGenericAlias alias) => _obj = alias;

        protected override string IteratorName => "generic_alias_iterator";

        protected internal override PyObject IterNext()
        {
            if (_obj == null)
                throw PyStopIteration.Create();
            var alias = _obj;
            _obj = null;
            return new PyGenericAlias(alias.Origin, alias.Args, starred: true);
        }

        internal override PyObject Reduce() => _obj != null
            ? PyTuple.From(Builtin("iter"), PyTuple.From(_obj))
            : ReduceExhausted(PyTuple.Empty);
    }
}
