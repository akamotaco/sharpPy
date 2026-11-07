using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_queuemodule.c: SimpleQueue, whose put never blocks and takes no Python lock, so a
    /// __del__ or a weakref callback may call it in the middle of another put.
    /// </summary>
    internal static class _QueueModule
    {
        private static PyType _empty;

        internal static PyModule CreateQueueModule()
        {
            var module = new PyModule("_queue");
            _empty = new PyClass("Empty", new[] { PyType.ExceptionType },
                new Dictionary<string, PyObject> { ["__module__"] = PyString.From("queue") });
            var type = new PyType("SimpleQueue", new[] { PyType.ObjectType }, "_queue");
            // {"__class_getitem__", Py_GenericAlias, METH_O|METH_CLASS, PyDoc_STR("See PEP 585")}
            type.TypeDict["__class_getitem__"] = PyClassMethodDescriptor.GenericAliasGetItem(type);
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                if (args.Length != 0 || (kwargs != null && kwargs.Length() != 0))
                    throw PyTypeError.Create("SimpleQueue() takes no arguments");
                return new PySimpleQueue((PyType)cls);
            }, 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["put"] = new PyMethodDescriptor("put", type, Put, 1, 3, acceptsKwargs: true);
            type.TypeDict["put_nowait"] = new PyMethodDescriptor("put_nowait", type, (self, args, kwargs) =>
            {
                ((PySimpleQueue)self).Put(args[0]);
                return PyNone.Instance;
            }, 1, 1);
            type.TypeDict["get"] = new PyMethodDescriptor("get", type, Get, 0, 2, acceptsKwargs: true);
            type.TypeDict["get_nowait"] = new PyMethodDescriptor("get_nowait", type, (self, args, kwargs) =>
                ((PySimpleQueue)self).Take(false, -1), 0, 0);
            type.TypeDict["empty"] = new PyMethodDescriptor("empty", type, (self, args, kwargs) =>
                PyBool.FromBool(((PySimpleQueue)self).Count == 0), 0, 0);
            type.TypeDict["qsize"] = new PyMethodDescriptor("qsize", type, (self, args, kwargs) =>
                PyInt.From(((PySimpleQueue)self).Count), 0, 0);
            module.ModuleDict["SimpleQueue"] = type;
            module.ModuleDict["Empty"] = _empty;
            return module;
        }

        private static PyObject Put(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var key = PyString.From("item");
            if (args.Length == 0 && (kwargs == null || !kwargs.Contains(key).Value))
                throw PyTypeError.Create("put() missing required argument 'item' (pos 1)");
            var item = args.Length > 0 ? args[0] : kwargs.GetItem(key);
            ((PySimpleQueue)self).Put(item);
            return PyNone.Instance;
        }

        // _queue_SimpleQueue_get_impl
        private static PyObject Get(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var blockKey = PyString.From("block");
            var blockArgument = args.Length > 0 ? args[0] : kwargs != null && kwargs.Contains(blockKey).Value ? kwargs.GetItem(blockKey) : null;
            var timeoutArgument = args.Length > 1 ? args[1] : kwargs?.Get(PyString.From("timeout"), null);
            bool block = blockArgument == null || blockArgument.IsTrue();
            double timeout = -1;
            if (block && timeoutArgument != null && timeoutArgument is not PyNone)
            {
                timeout = timeoutArgument.AsFloat().Value;
                if (timeout < 0)
                    throw PyValueError.Create("'timeout' must be a non-negative number");
                if (timeout > 4294967.0)
                    throw PyOverflowError.Create("timeout value is too large");
            }
            return ((PySimpleQueue)self).Take(block, timeout);
        }

        private sealed class PySimpleQueue : PyObject
        {
            private readonly PyType _builtin;
            private readonly Queue<PyObject> _items = new();

            internal PySimpleQueue(PyType type) => _builtin = Allocated(type);

            internal int Count
            {
                get
                {
                    lock (_items)
                        return _items.Count;
                }
            }

            public override PyType GetPyType() => _customType ?? _builtin;
            public override string GetTypeName() => "SimpleQueue";

            internal void Put(PyObject item)
            {
                lock (_items)
                {
                    _items.Enqueue(item);
                    Monitor.Pulse(_items);
                }
            }

            internal PyObject Take(bool block, double timeout)
            {
                PyObject item = null;
                bool found;
                lock (_items)
                    found = _items.TryDequeue(out item);
                if (found)
                    return item;
                if (block)
                {
                    PyGil.AllowThreads(() =>
                    {
                        var limit = timeout < 0 ? null : Stopwatch.StartNew();
                        lock (_items)
                        {
                            while (!_items.TryDequeue(out item))
                            {
                                if (limit == null)
                                {
                                    Monitor.Wait(_items);
                                    continue;
                                }
                                double left = timeout - limit.Elapsed.TotalSeconds;
                                if (left <= 0)
                                    return;
                                Monitor.Wait(_items, (int)Math.Min(Math.Ceiling(left * 1000), int.MaxValue));
                            }
                        }
                    });
                }
                return item ?? throw new PythonException(PyBaseExceptionOf(_empty));
            }

            private static PyBaseException PyBaseExceptionOf(PyType type) =>
                type.Call(Array.Empty<PyObject>(), null) as PyBaseException;
        }
    }
}
