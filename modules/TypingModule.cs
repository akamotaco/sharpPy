using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// The _typing module (Modules/_typingmodule.c) and the types of Objects/typevarobject.c:
    /// TypeVar, ParamSpec, ParamSpecArgs, ParamSpecKwargs, TypeVarTuple, TypeAliasType and Generic.
    /// What these types do with types is typing.py's (call_typing_func_object); they hold the
    /// parameters, check the arguments they are made with and stand for PEP 695's syntax.
    /// </summary>
    internal static class TypingModule
    {
        // PyType_FromSpec of typevar_spec and the others, all "typing.<name>"; the layout table
        // (TypeLayout.g.cs) has them final, with a weakref slot where the spec has one.
        internal static readonly PyType TypeVarType = new("TypeVar", new[] { PyType.ObjectType }, "typing");
        internal static readonly PyType ParamSpecType = new("ParamSpec", new[] { PyType.ObjectType }, "typing");
        internal static readonly PyType ParamSpecArgsType = new("ParamSpecArgs", new[] { PyType.ObjectType }, "typing");
        internal static readonly PyType ParamSpecKwargsType = new("ParamSpecKwargs", new[] { PyType.ObjectType }, "typing");
        internal static readonly PyType TypeVarTupleType = new("TypeVarTuple", new[] { PyType.ObjectType }, "typing");
        // _PyTypeAlias_Type: a static type of Objects/typevarobject.c, not one made from a spec.
        internal static readonly PyType TypeAliasType = new("TypeAliasType", new[] { PyType.ObjectType }, "typing");

        private static PyObject _generic;
        private static bool _installed;

        // _Py_initialize_generic: the types exist with the interpreter, not only once _typing is
        // imported - PEP 695's syntax makes their instances without it.
        static TypingModule() => Install();

        // generic_spec: DEFAULT | BASETYPE | HAVE_GC and no IMMUTABLETYPE - a heap type a program may
        // change and subclass, with no instance dict (basicsize is a bare PyObject).
        internal static PyObject GenericType => _generic ??= MakeGeneric();

        internal static PyModule CreateTypingModule()
        {
            Install();
            var module = new PyModule("_typing");
            module.ModuleDict["__doc__"] = PyString.From("Accelerators for the typing module.\n");
            // _typing._idfunc: METH_O, x itself.
            module.ModuleDict["_idfunc"] = PyArgs.MethO("_typing", "_idfunc", args => args[0]);
            module.ModuleDict["TypeVar"] = TypeVarType;
            module.ModuleDict["TypeVarTuple"] = TypeVarTupleType;
            module.ModuleDict["ParamSpec"] = ParamSpecType;
            module.ModuleDict["ParamSpecArgs"] = ParamSpecArgsType;
            module.ModuleDict["ParamSpecKwargs"] = ParamSpecKwargsType;
            module.ModuleDict["Generic"] = GenericType;
            module.ModuleDict["TypeAliasType"] = TypeAliasType;
            return module;
        }

        private static void Install()
        {
            if (_installed)
                return;
            _installed = true;

            // ---- TypeVar ----
            TypeVarType.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", TypeVarType, TypeVarNew, acceptsKwargs: true);
            TypeVarType.TypeDict["__typing_subst__"] = Method(TypeVarType, "__typing_subst__", 1,
                (self, args) => CallTyping("_typevar_subst", self, args[0]));
            TypeVarType.TypeDict["__reduce__"] = Method(TypeVarType, "__reduce__", 0, (self, args) => ((PyTypeVar)self).Name);
            TypeVarType.TypeDict["__mro_entries__"] = MroEntries(TypeVarType, "TypeVar");
            Members(TypeVarType, ("__name__", self => ((PyTypeVar)self).Name),
                ("__covariant__", self => PyBool.FromBool(((PyTypeVar)self).Covariant)),
                ("__contravariant__", self => PyBool.FromBool(((PyTypeVar)self).Contravariant)),
                ("__infer_variance__", self => PyBool.FromBool(((PyTypeVar)self).InferVariance)));
            TypeVarType.TypeDict["__bound__"] = new PyGetSetDescriptor("__bound__", TypeVarType, self => ((PyTypeVar)self).GetBound());
            TypeVarType.TypeDict["__constraints__"] = new PyGetSetDescriptor("__constraints__", TypeVarType, self => ((PyTypeVar)self).GetConstraints());
            ReflectedOr(TypeVarType);

            // ---- ParamSpecArgs / ParamSpecKwargs ----
            foreach (var (type, label) in new[] { (ParamSpecArgsType, "ParamSpecArgs"), (ParamSpecKwargsType, "ParamSpecKwargs") })
            {
                var owner = type;
                string clinic = label.ToLowerInvariant();
                owner.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", owner, (cls, args, kwargs) =>
                {
                    var parsed = PyArgs.Unpack(clinic, args, kwargs, new[] { "origin" }, 0, 1, 1);
                    return new PyParamSpecAttr(owner, parsed[0]);
                }, acceptsKwargs: true);
                owner.TypeDict["__mro_entries__"] = MroEntries(owner, label);
                Members(owner, ("__origin__", self => ((PyParamSpecAttr)self).Origin));
            }

            // ---- ParamSpec ----
            ParamSpecType.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", ParamSpecType, ParamSpecNew, acceptsKwargs: true);
            ParamSpecType.TypeDict["__typing_subst__"] = Method(ParamSpecType, "__typing_subst__", 1,
                (self, args) => CallTyping("_paramspec_subst", self, args[0]));
            ParamSpecType.TypeDict["__typing_prepare_subst__"] = Method(ParamSpecType, "__typing_prepare_subst__", 2,
                (self, args) => CallTyping("_paramspec_prepare_subst", self, args[0], args[1]));
            ParamSpecType.TypeDict["__reduce__"] = Method(ParamSpecType, "__reduce__", 0, (self, args) => ((PyParamSpec)self).Name);
            ParamSpecType.TypeDict["__mro_entries__"] = MroEntries(ParamSpecType, "ParamSpec");
            Members(ParamSpecType, ("__name__", self => ((PyParamSpec)self).Name),
                ("__bound__", self => ((PyParamSpec)self).Bound ?? PyNone.Instance),
                ("__covariant__", self => PyBool.FromBool(((PyParamSpec)self).Covariant)),
                ("__contravariant__", self => PyBool.FromBool(((PyParamSpec)self).Contravariant)),
                ("__infer_variance__", self => PyBool.FromBool(((PyParamSpec)self).InferVariance)));
            ParamSpecType.TypeDict["args"] = new PyGetSetDescriptor("args", ParamSpecType,
                self => new PyParamSpecAttr(ParamSpecArgsType, self), doc: "Represents positional arguments.");
            ParamSpecType.TypeDict["kwargs"] = new PyGetSetDescriptor("kwargs", ParamSpecType,
                self => new PyParamSpecAttr(ParamSpecKwargsType, self), doc: "Represents keyword arguments.");
            ReflectedOr(ParamSpecType);

            // ---- TypeVarTuple ----
            TypeVarTupleType.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", TypeVarTupleType, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("typevartuple", args, kwargs, new[] { "name" }, 0, 1, 1);
                var name = CheckName("typevartuple", parsed[0]);
                return new PyTypeVarTuple(name, Caller());
            }, acceptsKwargs: true);
            TypeVarTupleType.TypeDict["__typing_subst__"] = Method(TypeVarTupleType, "__typing_subst__", 1,
                (self, args) => throw PyTypeError.Create("Substitution of bare TypeVarTuple is not supported"));
            TypeVarTupleType.TypeDict["__typing_prepare_subst__"] = Method(TypeVarTupleType, "__typing_prepare_subst__", 2,
                (self, args) => CallTyping("_typevartuple_prepare_subst", self, args[0], args[1]));
            TypeVarTupleType.TypeDict["__reduce__"] = Method(TypeVarTupleType, "__reduce__", 0, (self, args) => ((PyTypeVarTuple)self).Name);
            TypeVarTupleType.TypeDict["__mro_entries__"] = MroEntries(TypeVarTupleType, "TypeVarTuple");
            Members(TypeVarTupleType, ("__name__", self => ((PyTypeVarTuple)self).Name));

            // ---- TypeAliasType ----
            TypeAliasType.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", TypeAliasType, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("typealias", args, kwargs, new[] { "name", "value", "type_params" }, 0, 2, 2);
                var name = CheckName("typealias", parsed[0]);
                var typeParams = parsed[2];
                if (typeParams != null && typeParams is not PyTuple)
                    throw PyTypeError.Create("type_params must be a tuple");
                return new PyTypeAlias(name, typeParams, null, parsed[1], Caller());
            }, acceptsKwargs: true);
            TypeAliasType.TypeDict["__reduce__"] = Method(TypeAliasType, "__reduce__", 0, (self, args) => ((PyTypeAlias)self).Name);
            Members(TypeAliasType, ("__name__", self => ((PyTypeAlias)self).Name));
            TypeAliasType.TypeDict["__parameters__"] = new PyGetSetDescriptor("__parameters__", TypeAliasType,
                self => ((PyTypeAlias)self).TypeParams is { } p ? UnpackTypeVarTuples(p) : PyTuple.Empty);
            TypeAliasType.TypeDict["__type_params__"] = new PyGetSetDescriptor("__type_params__", TypeAliasType,
                self => ((PyTypeAlias)self).TypeParams ?? PyTuple.Empty);
            TypeAliasType.TypeDict["__value__"] = new PyGetSetDescriptor("__value__", TypeAliasType, self => ((PyTypeAlias)self).GetValue());
            TypeAliasType.TypeDict["__module__"] = new PyGetSetDescriptor("__module__", TypeAliasType, self => ((PyTypeAlias)self).GetModule());
            // typealias_as_number: nb_or = _Py_union_type_or, both ways.
            TypeAliasType.TypeDict["__ror__"] = new PyWrapperDescriptor("__ror__", TypeAliasType,
                (self, args, kwargs) => PyUnionType.Or(args[0], self), minArgs: 1, maxArgs: 1);
            // type_ready: docs, __module__ and the slot wrappers of each.
            PyDocs.SetTypeDocs();
        }

        // ---- the helpers of Objects/typevarobject.c ----

        // call_typing_func_object: typing.<name>(*args).
        internal static PyObject CallTyping(string name, params PyObject[] args) =>
            PyImportSystem.Import("typing").GetAttribute(name).Call(args, null);

        // type_check: None is NoneType; anything else typing._type_check(arg, msg).
        private static PyObject TypeCheck(PyObject arg, string message) =>
            arg is PyNone ? arg.GetPyType() : CallTyping("_type_check", arg, PyString.From(message));

        // make_union, the nb_or of TypeVar and ParamSpec: typing._make_union, so forward references
        // and None on either side make a typing.Union.
        internal static PyObject MakeUnion(PyObject self, PyObject other) => CallTyping("_make_union", self, other);

        /// <summary>
        /// caller(): the module of the function whose frame is running - PyFunction_GetModule of
        /// f_funcobj, which is the globals' __name__ that function was made in; None without one.
        /// </summary>
        private static PyObject Caller()
        {
            var frame = PyVM.CurrentFrame;
            if (frame?.Globals == null)
                return PyNone.Instance;
            return frame.Globals.TryGetValue("__name__", out var name) ? name : PyNone.Instance;
        }

        // typevartuple_unpack: typing.Unpack[tvt].
        private static PyObject Unpack(PyObject tvt) =>
            PyAbstract.GetItem(PyImportSystem.Import("typing").GetAttribute("Unpack"), tvt);

        // unpack_typevartuples: a TypeVarTuple among the parameters is passed unpacked.
        internal static PyTuple UnpackTypeVarTuples(PyTuple parameters)
        {
            bool any = false;
            foreach (var parameter in parameters.Items)
                if (ReferenceEquals(parameter.GetPyType(), TypeVarTupleType)) { any = true; break; }
            if (!any)
                return parameters;
            var items = new PyObject[parameters.Items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var parameter = parameters.Items[i];
                items[i] = ReferenceEquals(parameter.GetPyType(), TypeVarTupleType) ? Unpack(parameter) : parameter;
            }
            return PyTuple.From(items);
        }

        private static PyString CheckName(string clinic, PyObject name) =>
            name as PyString ?? throw PyTypeError.Create($"{clinic}() argument 'name' must be str, not {name.GetTypeName()}");

        // ---- constructors ----

        /// <summary>
        /// typevar_new_impl, after its clinic (_PyArg_UnpackKeywordsWithVararg): name, *constraints
        /// and the keyword-only bound, covariant, contravariant and infer_variance.
        /// </summary>
        private static PyObject TypeVarNew(PyObject cls, PyObject[] args, PyDict kwargs)
        {
            PyObject name = args.Length > 0 ? args[0] : null;
            PyObject bound = PyNone.Instance;
            bool covariant = false, contravariant = false, inferVariance = false;
            if (kwargs != null)
            {
                foreach (var pair in kwargs.InternalDict)
                {
                    string key = (pair.Key as PyString)?.Value;
                    switch (key)
                    {
                        case "name":
                            if (name != null)
                                throw PyTypeError.Create("invalid keyword argument for typevar()");
                            name = pair.Value;
                            break;
                        case "bound": bound = pair.Value; break;
                        case "covariant": covariant = pair.Value.IsTrue(); break;
                        case "contravariant": contravariant = pair.Value.IsTrue(); break;
                        case "infer_variance": inferVariance = pair.Value.IsTrue(); break;
                        default:
                            throw PyTypeError.Create($"'{key}' is an invalid keyword argument for typevar()");
                    }
                }
            }
            if (name == null)
                throw PyTypeError.Create("typevar() missing required argument 'name' (pos 1)");
            var nameText = CheckName("typevar", name);
            var constraints = new PyObject[Math.Max(0, args.Length - 1)];
            if (args.Length > 1)
                Array.Copy(args, 1, constraints, 0, constraints.Length);

            if (covariant && contravariant)
                throw PyValueError.Create("Bivariant types are not supported.");
            if (inferVariance && (covariant || contravariant))
                throw PyValueError.Create("Variance cannot be specified with infer_variance.");
            PyObject boundValue = bound is PyNone ? null : TypeCheck(bound, "Bound must be a type.");
            PyObject constraintsValue = null;
            if (constraints.Length == 1)
                throw PyTypeError.Create("A single constraint is not allowed");
            if (constraints.Length > 1)
            {
                if (boundValue != null)
                    throw PyTypeError.Create("Constraints cannot be combined with bound=...");
                constraintsValue = PyTuple.From(constraints);
            }
            return new PyTypeVar(nameText, boundValue, null, constraintsValue, null, covariant, contravariant, inferVariance, Caller());
        }

        // paramspec_new_impl: the bound is type-checked even when it is the default None.
        private static PyObject ParamSpecNew(PyObject cls, PyObject[] args, PyDict kwargs)
        {
            var parsed = PyArgs.Unpack("paramspec", args, kwargs,
                new[] { "name", "bound", "covariant", "contravariant", "infer_variance" }, 0, 1, 1);
            var name = CheckName("paramspec", parsed[0]);
            bool covariant = parsed[2]?.IsTrue() ?? false;
            bool contravariant = parsed[3]?.IsTrue() ?? false;
            bool inferVariance = parsed[4]?.IsTrue() ?? false;
            if (covariant && contravariant)
                throw PyValueError.Create("Bivariant types are not supported.");
            if (inferVariance && (covariant || contravariant))
                throw PyValueError.Create("Variance cannot be specified with infer_variance.");
            var bound = TypeCheck(parsed[1] ?? PyNone.Instance, "Bound must be a type.");
            return new PyParamSpec(name, bound, covariant, contravariant, inferVariance, Caller());
        }

        // _Py_make_typevar, _Py_make_paramspec, _Py_make_typevartuple and _Py_make_typealias: what
        // PEP 695's syntax makes, inferring variance, with no module of its own.
        internal static PyObject MakeTypeVar(PyObject name, PyObject evaluateBound, PyObject evaluateConstraints) =>
            new PyTypeVar((PyString)name, null, evaluateBound, null, evaluateConstraints, false, false, true, null);

        internal static PyObject MakeParamSpec(PyObject name) => new PyParamSpec((PyString)name, null, false, false, true, null);

        internal static PyObject MakeTypeVarTuple(PyObject name) => new PyTypeVarTuple((PyString)name, null);

        internal static PyObject MakeTypeAlias(PyTuple args) =>
            new PyTypeAlias((PyString)args.Items[0], args.Items[1], args.Items[2], null, null);

        // _Py_subscript_generic: typing._GenericAlias(Generic, params) for class C[T]'s bases.
        internal static PyObject SubscriptGeneric(PyObject parameters) =>
            CallTyping("_GenericAlias", GenericType, UnpackTypeVarTuples((PyTuple)parameters));

        // ---- the pieces each type spec lists ----

        private static PyMethodDescriptor Method(PyType type, string name, int arity, Func<PyObject, PyObject[], PyObject> body) =>
            new(name, type, (self, args, kwargs) => body(self, args), arity, arity);

        // {"__mro_entries__", ..., METH_O}: an instance cannot be a base.
        private static PyMethodDescriptor MroEntries(PyType type, string label) =>
            Method(type, "__mro_entries__", 1, (self, args) => throw PyTypeError.Create($"Cannot subclass an instance of {label}"));

        // T_OBJECT and T_BOOL members, READONLY.
        private static void Members(PyType type, params (string Name, Func<PyObject, PyObject> Get)[] members)
        {
            foreach (var (name, get) in members)
                type.TypeDict[name] = new PyMemberDescriptor(name, type, get, readOnly: true);
        }

        // nb_or = make_union reached from the right (wrap_binaryfunc_r).
        private static void ReflectedOr(PyType type) =>
            type.TypeDict["__ror__"] = new PyWrapperDescriptor("__ror__", type,
                (self, args, kwargs) => MakeUnion(args[0], self), minArgs: 1, maxArgs: 1);

        // Generic: __class_getitem__ and __init_subclass__ are typing.py's, called with the class.
        private static PyObject MakeGeneric()
        {
            var dict = new PyDict();
            dict.SetItem(PyString.From("__module__"), PyString.From("typing"));
            dict.SetItem(PyString.From("__doc__"), PyString.From(GenericDoc));
            dict.SetItem(PyString.From("__slots__"), PyTuple.Empty);
            var generic = (PyType)PyTypeMetaclass.TypeNew(new PyObject[]
            {
                PyTypeMetaclass.Instance, PyString.From("Generic"), PyTuple.From(new PyObject[] { PyType.ObjectType }), dict,
            }, null);
            // generic_spec has no __slots__ of its own; the empty slots only kept the instance dict away.
            generic.SetAttribute("__class_getitem__", ClassMethod(generic, "__class_getitem__", "_generic_class_getitem"));
            generic.SetAttribute("__init_subclass__", ClassMethod(generic, "__init_subclass__", "_generic_init_subclass"));
            generic.DelAttribute("__slots__");
            return generic;
        }

        // call_typing_args_kwargs: typing.<name>(cls, *args, **kwargs).
        private static PyClassMethodDescriptor ClassMethod(PyType owner, string name, string typingName) =>
            new(name, owner, new PyBuiltinFunction(name, (args, kwargs) =>
                PyImportSystem.Import("typing").GetAttribute(typingName).Call(args, kwargs)));

        private const string GenericDoc = "Abstract base class for generic types.\n\n" +
            "On Python 3.12 and newer, generic classes implicitly inherit from\n" +
            "Generic when they declare a parameter list after the class's name::\n\n" +
            "    class Mapping[KT, VT]:\n" +
            "        def __getitem__(self, key: KT) -> VT:\n" +
            "            ...\n" +
            "        # Etc.\n\n" +
            "On older versions of Python, however, generic classes have to\n" +
            "explicitly inherit from Generic.\n\n" +
            "After a class has been declared to be generic, it can then be used as\n" +
            "follows::\n\n" +
            "    def lookup_name[KT, VT](mapping: Mapping[KT, VT], key: KT, default: VT) -> VT:\n" +
            "        try:\n" +
            "            return mapping[key]\n" +
            "        except KeyError:\n" +
            "            return default\n";
    }

    /// <summary>A typevarobject, ParamSpec's and TypeVarTuple's kin: a managed dict for __module__.</summary>
    internal abstract class PyTypeParam : PyObject, IObjectDict
    {
        internal readonly PyString Name;
        private PyDict _dict;

        protected PyTypeParam(PyString name, PyObject module)
        {
            Name = name;
            // typevar_alloc: PyObject_SetAttrString(tv, "__module__", module) when there is one.
            if (module != null)
                SetAttribute("__module__", module);
        }

        public PyDict ObjectDict(bool create) => create ? _dict ??= new PyDict() : _dict;
        public void SetObjectDict(PyDict dict) => _dict = dict;
    }

    internal sealed class PyTypeVar : PyTypeParam
    {
        private PyObject _bound, _constraints;
        private readonly PyObject _evaluateBound, _evaluateConstraints;
        internal readonly bool Covariant, Contravariant, InferVariance;

        internal PyTypeVar(PyString name, PyObject bound, PyObject evaluateBound, PyObject constraints, PyObject evaluateConstraints,
            bool covariant, bool contravariant, bool inferVariance, PyObject module) : base(name, module)
        {
            _bound = bound;
            _evaluateBound = evaluateBound;
            _constraints = constraints;
            _evaluateConstraints = evaluateConstraints;
            Covariant = covariant;
            Contravariant = contravariant;
            InferVariance = inferVariance;
        }

        public override PyType GetPyType() => TypingModule.TypeVarType;

        // typevar_bound: computed once from evaluate_bound, None without either.
        internal PyObject GetBound()
        {
            if (_bound != null)
                return _bound;
            if (_evaluateBound == null)
                return PyNone.Instance;
            return _bound = _evaluateBound.Call(Array.Empty<PyObject>(), null);
        }

        // typevar_constraints: computed once from evaluate_constraints, () without either.
        internal PyObject GetConstraints()
        {
            if (_constraints != null)
                return _constraints;
            if (_evaluateConstraints == null)
                return PyTuple.Empty;
            return _constraints = _evaluateConstraints.Call(Array.Empty<PyObject>(), null);
        }

        // typevar_repr
        protected internal override PyString Repr() => InferVariance
            ? Name : PyString.From((Covariant ? "+" : Contravariant ? "-" : "~") + Name.Value);

        public override PyObject BitwiseOr(PyObject other) => TypingModule.MakeUnion(this, other);
    }

    internal sealed class PyParamSpec : PyTypeParam
    {
        internal readonly PyObject Bound;
        internal readonly bool Covariant, Contravariant, InferVariance;

        internal PyParamSpec(PyString name, PyObject bound, bool covariant, bool contravariant, bool inferVariance, PyObject module)
            : base(name, module)
        {
            Bound = bound;
            Covariant = covariant;
            Contravariant = contravariant;
            InferVariance = inferVariance;
        }

        public override PyType GetPyType() => TypingModule.ParamSpecType;

        // paramspec_repr
        protected internal override PyString Repr() => InferVariance
            ? Name : PyString.From((Covariant ? "+" : Contravariant ? "-" : "~") + Name.Value);

        public override PyObject BitwiseOr(PyObject other) => TypingModule.MakeUnion(this, other);
    }

    internal sealed class PyTypeVarTuple : PyTypeParam
    {
        internal PyTypeVarTuple(PyString name, PyObject module) : base(name, module) { }

        public override PyType GetPyType() => TypingModule.TypeVarTupleType;

        // typevartuple_repr
        protected internal override PyString Repr() => Name;

        // typevartuple_iter: iter((Unpack[self],)).
        protected internal override PyObject Iter() =>
            PyTuple.From(new[] { PyAbstract.GetItem(PyImportSystem.Import("typing").GetAttribute("Unpack"), this) }).GetIterator();
    }

    /// <summary>paramspecattrobject: ParamSpecArgs and ParamSpecKwargs, which keep their ParamSpec.</summary>
    internal sealed class PyParamSpecAttr : PyObject
    {
        private readonly PyType _type;
        internal readonly PyObject Origin;

        internal PyParamSpecAttr(PyType type, PyObject origin)
        {
            _type = type;
            Origin = origin;
        }

        public override PyType GetPyType() => _type;

        // paramspecargs_repr and paramspeckwargs_repr: "<name>.args" of a ParamSpec, else the origin's repr.
        protected internal override PyString Repr()
        {
            string suffix = ReferenceEquals(_type, TypingModule.ParamSpecArgsType) ? ".args" : ".kwargs";
            return PyString.From((Origin is PyParamSpec spec ? spec.Name.Value : Origin.ToRepr().Value) + suffix);
        }

        // paramspecattr_richcompare: the same type, == and != only, by the origins.
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (!ReferenceEquals(GetPyType(), other.GetPyType()) || (op != CompareOp.EQ && op != CompareOp.NE))
                return PyNotImplemented.Instance;
            return Origin.RichCompare(((PyParamSpecAttr)other).Origin, op);
        }
    }

    /// <summary>typealiasobject: the alias of a type statement or TypeAliasType(name, value).</summary>
    internal sealed class PyTypeAlias : PyObject
    {
        internal readonly PyString Name;
        internal readonly PyTuple TypeParams;
        private readonly PyObject _computeValue, _module;
        private PyObject _value;

        internal PyTypeAlias(PyString name, PyObject typeParams, PyObject computeValue, PyObject value, PyObject module)
        {
            Name = name;
            // typealias_alloc: no parameters is NULL, whether NULL, None or ().
            TypeParams = typeParams is PyTuple tuple && tuple.Items.Length > 0 ? tuple : null;
            _computeValue = computeValue;
            _value = value;
            _module = module;
        }

        public override PyType GetPyType() => TypingModule.TypeAliasType;

        // typealias_get_value: computed once.
        internal PyObject GetValue() => _value ??= _computeValue.Call(Array.Empty<PyObject>(), null);

        // typealias_module: the one given, else the module of compute_value's function, else None.
        internal PyObject GetModule()
        {
            if (_module != null)
                return _module;
            if (_computeValue is PyFunction function && function.LookupAttribute("__module__") is { } module)
                return module;
            return PyNone.Instance;
        }

        protected internal override PyString Repr() => Name;

        // typealias_subscript: only a generic alias, as types.GenericAlias.
        protected internal override PyObject MpSubscript(PyObject key) => TypeParams == null
            ? throw PyTypeError.Create("Only generic type aliases are subscriptable")
            : new PyGenericAlias(this, key);

        public override PyObject BitwiseOr(PyObject other) => PyUnionType.Or(this, other);
    }
}
