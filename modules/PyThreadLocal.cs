using System;
using System.Collections.Generic;
using System.Threading;

namespace SharpPy.Modules
{
    /// <summary>
    /// localobject (Modules/_threadmodule.c): attributes kept per thread. Each thread's dict is the
    /// one _ldict finds for this object in the thread's state - created, with __init__ called on the
    /// arguments the object was made with, the first time a thread other than the creating one asks.
    /// As there, the thread holds only a localdummy, and the local object owns the dicts (dummies),
    /// so a thread that lives on never keeps a cycle through a local dict alive.
    /// </summary>
    internal sealed class PyThreadLocal : PyObject
    {
        internal static readonly PyType LocalType = CreateType();

        private readonly PyObject[] _args;
        private readonly PyDict _kwargs;
        // self->key: what each thread state's dict files this object's dummy under.
        private readonly long _key = Interlocked.Increment(ref _lastKey);
        // self->dummies: weakref(dummy) -> ldict.
        private readonly Dictionary<Dummy, Dictionary<string, PyObject>> _dicts = new(ReferenceEqualityComparer.Instance);
        private static long _lastKey;

        /// <summary>
        /// localdummyobject: what the thread state's dict holds. It refers to its local object
        /// weakly, so a thread never keeps one alive; Destroyed is _localdummy_destroyed, the callback
        /// of the weakref the local object keys its dicts by, run when the thread state is cleared.
        /// </summary>
        internal sealed class Dummy
        {
            private readonly WeakReference<PyThreadLocal> _owner;

            internal Dummy(PyThreadLocal owner) => _owner = new WeakReference<PyThreadLocal>(owner);

            internal bool OwnerAlive => _owner.TryGetTarget(out _);

            internal void Destroyed()
            {
                if (_owner.TryGetTarget(out var owner))
                    owner.Forget(this);
            }
        }

        private PyThreadLocal(PyType type, PyObject[] args, PyDict kwargs)
        {
            if (!ReferenceEquals(type, LocalType))
                SetCustomType(type);
            _args = args;
            _kwargs = kwargs;
            // _local_create_dummy: the creating thread's dict, whose __init__ type_call runs.
            CreateDummy();
        }

        public override PyType GetPyType() => _customType ?? LocalType;

        // PyThreadState_GetDict(): the running thread's.
        private static PyThreadState ThreadState() =>
            PyVM.Instance.BoundState ?? throw PySystemError.Create("Couldn't get thread-state dictionary");

        // _local_create_dummy
        private Dictionary<string, PyObject> CreateDummy()
        {
            var dummy = new Dummy(this);
            var dict = new Dictionary<string, PyObject>();
            lock (_dicts)
                _dicts[dummy] = dict;
            ThreadState().AddLocalDummy(_key, dummy);
            return dict;
        }

        // _localdummy_destroyed: the thread is gone, and its dict with it.
        private void Forget(Dummy dummy)
        {
            lock (_dicts)
                _dicts.Remove(dummy);
        }

        // local_new
        private static PyObject New(PyObject cls, PyObject[] args, PyDict kwargs)
        {
            var type = (PyType)cls;
            if (PyType.TypeInitIsObjectInit(type) && (args.Length != 0 || (kwargs != null && kwargs.Length() != 0)))
                throw PyTypeError.Create("Initialization arguments are not supported");
            return new PyThreadLocal(type, args, kwargs);
        }

        // _ldict: this thread's dict, made and initialized by tp_init on first use.
        private Dictionary<string, PyObject> LocalDict()
        {
            var state = ThreadState();
            if (state.LocalDummies.TryGetValue(_key, out var found))
                lock (_dicts)
                    if (_dicts.TryGetValue(found, out var existing))
                        return existing;
            var dict = CreateDummy();
            var type = GetPyType();
            if (!PyType.TypeInitIsObjectInit(type))
            {
                try
                {
                    // tp_init(self, self->args, self->kw): slot_tp_init for a class written in Python.
                    var init = type.FindNameInMro("__init__");
                    var result = BindSpecialMethod(init).Call(_args, _kwargs);
                    if (result != null && result is not PyNone)
                        throw PyTypeError.Create($"__init__() should return None, not '{result.GetTypeName()}'");
                }
                catch
                {
                    // PyDict_DelItem(tdict, self->key): the next access makes a new one.
                    if (state.LocalDummies.Remove(_key, out var failed))
                        failed.Destroyed();
                    throw;
                }
            }
            return dict;
        }

        internal override bool TpGetattroIsGeneric => false;

        // local_getattro
        protected internal override PyObject TpGetattro(PyString name)
        {
            var dict = LocalDict();
            if (name.Value == "__dict__")
                return PyGlobalsDict.For(dict);
            var type = GetPyType();
            if (ReferenceEquals(type, LocalType) && dict.TryGetValue(name.Value, out var value))
                return value;
            return SubclassInstanceSupport.GenericGetAttrWithDict(this, type, name.Value, dict, null)
                ?? throw AttributeNotFound(name.Value);
        }

        // local_setattro
        protected internal override void TpSetattro(PyString name, PyObject value) => SetOrDelete(name, value);

        protected internal override void TpDelattro(PyString name) => SetOrDelete(name, null);

        private void SetOrDelete(PyString name, PyObject value)
        {
            var dict = LocalDict();
            if (name.Value == "__dict__")
                throw PyAttributeError.Create($"'{GetPyType().FullName}' object attribute '__dict__' is read-only");
            SubclassInstanceSupport.GenericSetAttrWithDict(this, GetPyType(), name, value, dict, null, given: true);
        }

        // The slot wrappers' name argument: a str (PyObject_GetAttr's check).
        private static PyString Name(PyObject name)
        {
            PyObject.AttributeName(name);
            return (PyString)name;
        }

        private static PyType CreateType()
        {
            var type = new PyType("_local", new[] { PyType.ObjectType }, "_thread");
            type.TypeDict["__module__"] = PyString.From("_thread");
            type.TypeDict["__doc__"] = PyString.From("Thread-local data");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, New, 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__getattribute__"] = new PyWrapperDescriptor("__getattribute__", type, (self, args, kwargs) =>
                ((PyThreadLocal)self).TpGetattro(Name(args[0])));
            type.TypeDict["__setattr__"] = new PyWrapperDescriptor("__setattr__", type, (self, args, kwargs) =>
            {
                ((PyThreadLocal)self).SetOrDelete(Name(args[0]), args[1]);
                return PyNone.Instance;
            });
            type.TypeDict["__delattr__"] = new PyWrapperDescriptor("__delattr__", type, (self, args, kwargs) =>
            {
                ((PyThreadLocal)self).SetOrDelete(Name(args[0]), null);
                return PyNone.Instance;
            });
            return type;
        }
    }
}
