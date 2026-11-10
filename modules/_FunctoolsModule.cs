using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython _functools C 확장 모듈 - 단순한 스텁 구현
    /// 향후 완전한 CPython 3.12 호환 구현으로 대체 예정
    /// </summary>
    public static class _FunctoolsModule
    {
        public static PyModule CreateFunctoolsModule()
        {
            var module = new PyModule("_functools", "Fast C implementation of functools functions");

            // 기본적인 스텁 구현만 제공
            module.ModuleDict["reduce"] = new PyBuiltinFunction("reduce", PyReduce);
            // the C partial, which functools.py takes in place of its own class
            module.ModuleDict["partial"] = PyPartial.PartialType;
            module.ModuleDict["cmp_to_key"] = new PyBuiltinFunction("cmp_to_key", PyCmpToKey);

            return module;
        }

        // reduce - CPython 3.12 compatible implementation
        // functools_reduce: PyArg_UnpackTuple("reduce", 2, 3), PyObject_GetIter, then PyIter_Next
        // until it ends; only the end of the iterator stops the loop, every other error propagates.
        public static PyObject PyReduce(PyObject[] args, PyDict? kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("reduce() takes no keyword arguments");
            PyArgs.Positional("reduce", args, null, 2, 3);
            var function = args[0];
            PyObject? result = args.Length == 3 ? args[2] : null;
            PyObject iterator;
            try
            {
                iterator = args[1].GetIterator();
            }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                throw PyTypeError.Create("reduce() arg 2 must support iteration");
            }
            while (true)
            {
                PyObject item;
                try
                {
                    item = iterator.Next();
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    break;
                }
                result = result == null ? item : function.Call(new[] { result, item }, null);
            }
            return result ?? throw PyTypeError.Create("reduce() of empty iterable with no initial value");
        }

        /// <summary>
        /// cmp_to_key - CPython 3.12: Modules/_functoolsmodule.c:202-280 (cmp_to_key)
        /// Converts a comparison function (cmp(a, b) -> int) to a key function
        /// </summary>
        public static PyObject PyCmpToKey(PyObject[] args, PyDict? kwargs)
        {
            // _functools_cmp_to_key's clinic: cmp_to_key(mycmp).
            var compare = PyArgs.Unpack("cmp_to_key", args, kwargs, new[] { "mycmp" }, 0, 1, 1)[0];
            // functools_cmp_to_key_impl: a keyobject with no object yet.
            return new PyKeyWrapper(compare, null);
        }
    }

    /// <summary>
    /// keyobject (Modules/_functoolsmodule.c): functools.KeyWrapper, what cmp_to_key returns and
    /// what calling it makes. The first holds no object; each call wraps one in a new keyobject.
    /// </summary>
    public class PyKeyWrapper : PyObject
    {
        private static readonly PyType KeyWrapperType = MakeType();

        private readonly PyObject _cmpFunc;
        // keyobject.object, the T_OBJECT member obj; null is NULL.
        private PyObject _object;

        public PyKeyWrapper(PyObject cmpFunc, PyObject obj)
        {
            _cmpFunc = cmpFunc;
            _object = obj;
        }

        private static PyType MakeType()
        {
            var type = new PyType("KeyWrapper", new[] { PyType.ObjectType }, "functools");
            type.TypeDict["obj"] = new PyMemberDescriptor("obj", type,
                self => ((PyKeyWrapper)self)._object ?? PyNone.Instance,
                (self, value) => ((PyKeyWrapper)self)._object = value,
                deleter: self => ((PyKeyWrapper)self)._object = null);
            type.TypeDict["__call__"] = new PyWrapperDescriptor("__call__", type, (self, args, kwargs) => self.Call(args, kwargs));
            // tp_richcompare without tp_hash: unhashable.
            type.TypeDict["__hash__"] = PyNone.Instance;
            foreach (var (name, op) in new[] { ("__lt__", CompareOp.LT), ("__le__", CompareOp.LE), ("__eq__", CompareOp.EQ),
                         ("__ne__", CompareOp.NE), ("__gt__", CompareOp.GT), ("__ge__", CompareOp.GE) })
                type.TypeDict[name] = new PyWrapperDescriptor(name, type, (self, args, kwargs) => self.TpRichCompare(args[0], op));
            return type;
        }

        public override PyType GetPyType() => KeyWrapperType;


        // keyobject_call: PyArg_ParseTupleAndKeywords(args, kwds, "O:K", {"obj"}).
        public override PyObject Call(PyObject[] args, PyDict? kwargs)
        {
            var parsed = PyArgs.Unpack("K", args, kwargs, new[] { "obj" }, 0, 1, 1);
            return new PyKeyWrapper(_cmpFunc, parsed[0]);
        }

        protected internal override PyString Repr() => PyString.From($"<functools.KeyWrapper object at {PyObjectId.Pointer(this)}>");

        // keyobject_richcompare: both keyobjects of the same type, the user's three-way result
        // compared with zero by PyObject_RichCompare.
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (other is not PyKeyWrapper otherKey)
                throw PyTypeError.Create("other argument must be K instance");
            if (_object == null || otherKey._object == null)
                throw PyAttributeError.Create("object");
            var result = _cmpFunc.Call(new[] { _object, otherKey._object }, null);
            return Compare(result, PyInt.From(0), op);
        }
    }

    /// <summary>
    /// partialobject (Modules/_functoolsmodule.c): functools.partial, a subclassable type whose
    /// func, args and keywords are READONLY members and whose instances have a __dict__.
    /// </summary>
    public sealed class PyPartial : PyObject, IObjectDict
    {
        internal static readonly PyType PartialType = MakeType();

        internal PyObject Fn;
        internal PyTuple Args;
        internal PyDict Kw;
        private PyDict _dict;

        public PyDict ObjectDict(bool create) => create ? _dict ??= new PyDict() : _dict;
        public void SetObjectDict(PyDict dict) => _dict = dict;

        private PyPartial(PyType type)
        {
            if (!ReferenceEquals(type, PartialType))
                SetCustomType(type);
        }

        // ob_type: partial, or the Python class that allocated the partial layout.
        public override PyType GetPyType() => _customType ?? PartialType;
        public override bool IsCallable() => true;


        private static PyType MakeType()
        {
            // partial_type_spec: "functools.partial"
            var type = new PyType("partial", new[] { PyType.ObjectType }, "functools");
            // PyType_FromSpec stores the module part of the spec's name in the type's dict.
            type.TypeDict["__module__"] = PyString.From("functools");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => New((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            // Py_tp_call, Py_tp_repr, PyObject_GenericGetAttr and PyObject_GenericSetAttr
            type.TypeDict["__call__"] = new PyWrapperDescriptor("__call__", type,
                (self, args, kwargs) => ((PyPartial)self).CallPartial(args, kwargs));
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type,
                (self, args, kwargs) => ((PyPartial)self).Repr(), minArgs: 0, maxArgs: 0);
            TypeTables.GenericAttributes(type);
            TypeTables.GenericSetAttributes(type);
            // partial_methods
            type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type,
                (self, args, kwargs) => ((PyPartial)self).Reduce(), 0, 0);
            type.TypeDict["__setstate__"] = new PyMethodDescriptor("__setstate__", type,
                (self, args, kwargs) => { ((PyPartial)self).SetState(args[0]); return PyNone.Instance; }, 1, 1);
            type.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(type);
            // partial_memberlist: READONLY members, and the offset CPython publishes
            type.TypeDict["func"] = new PyMemberDescriptor("func", type, self => ((PyPartial)self).Fn, readOnly: true);
            type.TypeDict["args"] = new PyMemberDescriptor("args", type, self => ((PyPartial)self).Args, readOnly: true);
            type.TypeDict["keywords"] = new PyMemberDescriptor("keywords", type, self => ((PyPartial)self).Kw, readOnly: true);
            type.TypeDict["__vectorcalloffset__"] = new PyMemberDescriptor("__vectorcalloffset__", type, self => PyInt.From(56), readOnly: true);
            // partial_getsetlist: PyObject_GenericGetDict and PyObject_GenericSetDict
            type.TypeDict["__dict__"] = SubclassInstanceSupport.GenericDictGetSet(type);
            type.TypeDict["__doc__"] = PyString.From("partial(func, *args, **keywords) - new function with partial application\n    of the given arguments and keywords.\n");
            return type;
        }

        // partial_new
        private static PyObject New(PyType type, PyObject[] args, PyDict kw)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("type 'partial' takes at least one argument");
            PyTuple pargs = null;
            PyDict pkw = null;
            var func = args[0];
            // A partial (its tp_call is partial_call) without a __dict__ is flattened into this one.
            if (func is PyPartial part && part._dict == null)
            {
                pargs = part.Args;
                pkw = part.Kw;
                func = part.Fn;
            }
            if (!func.IsCallable())
                throw PyTypeError.Create("the first argument must be callable");
            var partial = new PyPartial(type) { Fn = func };
            var nargs = PyTuple.From(args[1..]);
            partial.Args = pargs == null ? nargs : (PyTuple)pargs.Add(nargs);
            if (pkw == null || pkw.Length() == 0)
            {
                partial.Kw = kw == null ? new PyDict() : kw.Copy();
            }
            else
            {
                partial.Kw = pkw.Copy();
                if (kw != null)
                    partial.Kw.Update(kw);
            }
            return partial;
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null) => CallPartial(args, kwargs);

        // partial_call: the stored keywords copied and merged with the call's, the stored arguments first.
        private PyObject CallPartial(PyObject[] args, PyDict kwargs)
        {
            PyDict kwargs2;
            if (Kw.Length() == 0)
            {
                kwargs2 = kwargs;
            }
            else
            {
                kwargs2 = Kw.Copy();
                if (kwargs != null)
                    kwargs2.Update(kwargs);
            }
            var stored = Args.Items;
            var all = new PyObject[stored.Length + args.Length];
            Array.Copy(stored, all, stored.Length);
            Array.Copy(args, 0, all, stored.Length, args.Length);
            return Fn.Call(all, kwargs2);
        }

        // partial_repr: Py_ReprEnter, then tp_name(func, args..., key=value...)
        protected internal override PyObject Repr()
        {
            if (!PyReprGuard.Enter(this))
                return PyString.From("...");
            try
            {
                var text = new System.Text.StringBuilder();
                foreach (var arg in Args.Items)
                    text.Append(", ").Append(arg.ToRepr().Value);
                foreach (var (key, value) in new List<KeyValuePair<PyObject, PyObject>>(Kw.GetInternalDict()))
                    text.Append(", ").Append(key.AsString().Value).Append('=').Append(value.ToRepr().Value);
                return PyString.From($"{GetPyType().FullName}({Fn.ToRepr().Value}{text})");
            }
            finally
            {
                PyReprGuard.Leave(this);
            }
        }

        // partial_reduce: (type, (fn,), (fn, args, kw, dict or None))
        private PyObject Reduce() => PyTuple.From(GetPyType(), PyTuple.From(Fn),
            PyTuple.From(new PyObject[] { Fn, Args, Kw, (PyObject)_dict ?? PyNone.Instance }));

        // partial_setstate
        private void SetState(PyObject state)
        {
            if (state is not PyTuple tuple || tuple.Items.Length != 4)
                throw PyTypeError.Create("invalid partial state");
            var fn = tuple.Items[0];
            var fnargs = tuple.Items[1];
            var kw = tuple.Items[2];
            var dict = tuple.Items[3];
            if (!fn.IsCallable() || fnargs is not PyTuple argsTuple || (kw is not PyNone && kw is not PyDict))
                throw PyTypeError.Create("invalid partial state");
            if (!argsTuple.IsExact)
                argsTuple = PyTuple.From(argsTuple.Items);
            PyDict keywords = kw is PyNone ? new PyDict() : kw is PyDict { IsExact: true } exact ? exact : ((PyDict)kw).Copy();
            Fn = fn;
            Args = argsTuple;
            Kw = keywords;
            _dict = dict is PyNone ? null : dict as PyDict
                ?? throw PyTypeError.Create($"setting partial object's dictionary to a non-dict");
        }
    }
}
