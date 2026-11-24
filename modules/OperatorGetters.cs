using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// itemgetterobject (Modules/_operator.c:974): operator.itemgetter, a final type made from
    /// itemgetter_type_spec.
    /// </summary>
    public sealed class PyItemGetter : PyObject
    {
        internal static readonly PyType ItemGetterType = MakeType();

        private readonly long _nitems;
        private readonly PyObject _item;
        // -1 unless item is a single non-negative exact int
        private readonly long _index;

        private PyItemGetter(long nitems, PyObject item, long index)
        {
            _nitems = nitems;
            _item = item;
            _index = index;
        }

        public override PyType GetPyType() => ItemGetterType;
        public override bool IsCallable() => true;

        private static PyType MakeType()
        {
            var type = new PyType("itemgetter", new[] { PyType.ObjectType }, "operator");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => New(args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type,
                (self, args, kwargs) => ((PyItemGetter)self).Reduce(), 0, 0);
            // itemgetter_members: the offset of the vectorcall field in itemgetterobject.
            type.TypeDict["__vectorcalloffset__"] = new PyMemberDescriptor("__vectorcalloffset__", type,
                self => PyInt.From(40), readOnly: true);
            return type;
        }

        // itemgetter_new
        private static PyObject New(PyObject[] args, PyDict kwargs)
        {
            PyArgs.NoKeywords("itemgetter", kwargs);
            long nitems = args.Length;
            PyObject item;
            if (nitems <= 1)
            {
                PyArgs.Positional("itemgetter", args, null, 1, 1);
                item = args[0];
            }
            else
            {
                item = PyTuple.From(args);
            }
            long index = -1;
            // PyLong_CheckExact, then PyLong_AsSsize_t: a negative or out-of-range index keeps -1.
            if (item is PyInt { IsExact: true } exactInt && exactInt.Value >= 0 && exactInt.Value <= long.MaxValue)
                index = (long)exactInt.Value;
            return new PyItemGetter(nitems, item, index);
        }

        // itemgetter_call
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            PyArgs.NoKeywords("itemgetter", kwargs);
            PyArgs.Positional("itemgetter", args, null, 1, 1);
            return CallImpl(args[0]);
        }

        // itemgetter_call_impl
        private PyObject CallImpl(PyObject obj)
        {
            if (_nitems == 1)
            {
                if (_index >= 0 && obj is PyTuple { IsExact: true } tuple && _index < tuple.Items.Length)
                    return tuple.Items[_index];
                return PyAbstract.GetItem(obj, _item);
            }
            var items = ((PyTuple)_item).Items;
            var result = new PyObject[items.Length];
            for (int i = 0; i < items.Length; i++)
                result[i] = PyAbstract.GetItem(obj, items[i]);
            return PyTuple.From(result);
        }

        // itemgetter_repr: "%s(%R)" for one item, "%s%R" for the tuple of several.
        protected internal override PyObject Repr()
        {
            if (!PyReprGuard.Enter(this))
                return PyString.From($"{GetPyType().FullName}(...)");
            try
            {
                var item = _item.ToRepr().Value;
                return PyString.From(_nitems == 1 ? $"{GetPyType().FullName}({item})" : $"{GetPyType().FullName}{item}");
            }
            finally
            {
                PyReprGuard.Leave(this);
            }
        }

        // itemgetter_reduce
        private PyObject Reduce() => _nitems == 1
            ? PyTuple.From(GetPyType(), PyTuple.From(_item))
            : PyTuple.From(GetPyType(), _item);
    }

    /// <summary>
    /// attrgetterobject (Modules/_operator.c:1196): operator.attrgetter. attr is a tuple holding,
    /// per name, the interned str or, for a dotted name, the tuple of its interned parts.
    /// </summary>
    public sealed class PyAttrGetter : PyObject
    {
        internal static readonly PyType AttrGetterType = MakeType();

        private readonly PyObject[] _attr;

        private PyAttrGetter(PyObject[] attr)
        {
            _attr = attr;
        }

        public override PyType GetPyType() => AttrGetterType;
        public override bool IsCallable() => true;

        private static PyType MakeType()
        {
            var type = new PyType("attrgetter", new[] { PyType.ObjectType }, "operator");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => New(args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type,
                (self, args, kwargs) => ((PyAttrGetter)self).Reduce(), 0, 0);
            // attrgetter_members: the offset of the vectorcall field in attrgetterobject.
            type.TypeDict["__vectorcalloffset__"] = new PyMemberDescriptor("__vectorcalloffset__", type,
                self => PyInt.From(32), readOnly: true);
            return type;
        }

        // attrgetter_new
        private static PyObject New(PyObject[] args, PyDict kwargs)
        {
            PyArgs.NoKeywords("attrgetter", kwargs);
            if (args.Length <= 1)
                PyArgs.Positional("attrgetter", args, null, 1, 1);
            var attr = new PyObject[args.Length];
            for (int idx = 0; idx < args.Length; idx++)
            {
                if (args[idx] is not PyString item)
                    throw PyTypeError.Create("attribute name must be a string");
                var text = item.Value;
                if (text.IndexOf('.') < 0)
                {
                    attr[idx] = PyString.Intern(item);
                    continue;
                }
                // a tuple of the non-dotted names, each a substring of the str
                var parts = text.Split('.');
                var chain = new PyObject[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    chain[i] = PyString.InternFrom(parts[i]);
                attr[idx] = PyTuple.From(chain);
            }
            return new PyAttrGetter(attr);
        }

        // dotted_getattr
        private static PyObject DottedGetattr(PyObject obj, PyObject attr)
        {
            if (attr is PyTuple chain)
            {
                foreach (var name in chain.Items)
                    obj = obj.GetAttribute((PyString)name);
                return obj;
            }
            return obj.GetAttribute((PyString)attr);
        }

        // attrgetter_call
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            PyArgs.NoKeywords("attrgetter", kwargs);
            PyArgs.Positional("attrgetter", args, null, 1, 1);
            var obj = args[0];
            if (_attr.Length == 1)
                return DottedGetattr(obj, _attr[0]);
            var result = new PyObject[_attr.Length];
            for (int i = 0; i < _attr.Length; i++)
                result[i] = DottedGetattr(obj, _attr[i]);
            return PyTuple.From(result);
        }

        // dotjoinattr
        private static PyObject DotJoin(PyObject attr) => attr is PyTuple chain
            ? PyString.From(string.Join(".", Array.ConvertAll(chain.Items, part => ((PyString)part).Value)))
            : attr;

        // attrgetter_args
        private PyTuple Args() => PyTuple.From(Array.ConvertAll(_attr, DotJoin));

        // attrgetter_repr
        protected internal override PyObject Repr()
        {
            if (!PyReprGuard.Enter(this))
                return PyString.From($"{GetPyType().FullName}(...)");
            try
            {
                return _attr.Length == 1
                    ? PyString.From($"{GetPyType().FullName}({DotJoin(_attr[0]).ToRepr().Value})")
                    : PyString.From($"{GetPyType().FullName}{Args().ToRepr().Value}");
            }
            finally
            {
                PyReprGuard.Leave(this);
            }
        }

        // attrgetter_reduce
        private PyObject Reduce() => PyTuple.From(GetPyType(), Args());
    }

    /// <summary>
    /// methodcallerobject (Modules/_operator.c:1558): operator.methodcaller, the interned name, the
    /// call's positional tuple and its keyword dict (NULL when none were given).
    /// </summary>
    public sealed class PyMethodCaller : PyObject
    {
        internal static readonly PyType MethodCallerType = MakeType();

        private readonly PyString _name;
        private readonly PyTuple _args;
        private readonly PyDict _kwds;

        private PyMethodCaller(PyString name, PyTuple args, PyDict kwds)
        {
            _name = name;
            _args = args;
            _kwds = kwds;
        }

        public override PyType GetPyType() => MethodCallerType;
        public override bool IsCallable() => true;

        private static PyType MakeType()
        {
            var type = new PyType("methodcaller", new[] { PyType.ObjectType }, "operator");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => New(args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type,
                (self, args, kwargs) => ((PyMethodCaller)self).Reduce(), 0, 0);
            return type;
        }

        // methodcaller_new
        private static PyObject New(PyObject[] args, PyDict kwargs)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("methodcaller needs at least one argument, the method name");
            if (args[0] is not PyString name)
                throw PyTypeError.Create("method name must be a string");
            // tp_new receives a fresh kwargs dict from the call, or NULL.
            var kwds = kwargs != null && kwargs.Length() != 0 ? kwargs.Copy() : null;
            return new PyMethodCaller(PyString.Intern(name), PyTuple.From(args[1..]), kwds);
        }

        // methodcaller_call
        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            PyArgs.NoKeywords("methodcaller", kwargs);
            PyArgs.Positional("methodcaller", args, null, 1, 1);
            var method = args[0].GetAttribute(_name);
            return method.Call(_args.Items, _kwds);
        }

        // methodcaller_repr
        protected internal override PyObject Repr()
        {
            var typeName = GetPyType().FullName;
            if (!PyReprGuard.Enter(this))
                return PyString.From($"{typeName}(...)");
            try
            {
                int numkwdargs = _kwds?.Length() ?? 0;
                if (_args.Items.Length + numkwdargs == 0)
                    return PyString.From($"{typeName}({_name.ToRepr().Value})");
                var reprs = new List<string>();
                foreach (var arg in _args.Items)
                    reprs.Add(arg.ToRepr().Value);
                if (_kwds != null)
                {
                    foreach (var (key, value) in new List<KeyValuePair<PyObject, PyObject>>(_kwds.GetInternalDict()))
                        reprs.Add($"{((PyString)key).Value}={value.ToRepr().Value}");
                    if (reprs.Count != _args.Items.Length + numkwdargs)
                        throw PyRuntimeError.Create("keywords dict changed size during iteration");
                }
                return PyString.From($"{typeName}({_name.ToRepr().Value}, {string.Join(", ", reprs)})");
            }
            finally
            {
                PyReprGuard.Leave(this);
            }
        }

        // methodcaller_reduce: (type, (name, *args)), or (partial(type, name, **kwds), args).
        private PyObject Reduce()
        {
            if (_kwds == null || _kwds.Length() == 0)
            {
                var newargs = new PyObject[1 + _args.Items.Length];
                newargs[0] = _name;
                Array.Copy(_args.Items, 0, newargs, 1, _args.Items.Length);
                return PyTuple.From(GetPyType(), PyTuple.From(newargs));
            }
            var partial = PyImportSystem.Import("functools").GetAttribute("partial");
            var constructor = partial.Call(new PyObject[] { GetPyType(), _name }, _kwds.Copy());
            return PyTuple.From(constructor, _args);
        }
    }
}
