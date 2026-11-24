using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// PyPickleBufferObject (Objects/picklebufobject.c): pickle.PickleBuffer, the wrapper of a
    /// potentially out-of-band buffer, which _pickle exposes. It holds the view the original object
    /// exported (PyBUF_FULL_RO); its own buffer is the original object's, asked for again.
    /// A final type, with weak references.
    /// </summary>
    public sealed class PickleBufferObject : PyObject
    {
        internal static readonly PyType PickleBufferType = MakeType();

        // The view exported by the original object; null once released (view.obj == NULL).
        private PyMemoryView _view;
        // view.obj: the object whose buffer the view is.
        private PyObject _obj;

        private PickleBufferObject() { }

        public override PyType GetPyType() => PickleBufferType;

        private static PyType MakeType()
        {
            // PyPickleBuffer_Type: tp_name "pickle.PickleBuffer"
            var type = new PyType("PickleBuffer", new[] { PyType.ObjectType }, "pickle");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => picklebuf_new((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            // picklebuf_as_buffer: bf_getbuffer and bf_releasebuffer, through wrap_buffer and wrap_releasebuffer.
            type.TypeDict["__buffer__"] = new PyWrapperDescriptor("__buffer__", type, (self, args, kwargs) => TypeTables.WrapBuffer(self, args[0]));
            type.TypeDict["__release_buffer__"] = new PyWrapperDescriptor("__release_buffer__", type,
                (self, args, kwargs) => TypeTables.WrapReleaseBuffer(self, args[0]));
            // picklebuf_methods
            PyArgs.Define(type, "raw", PyCall.NoArgs, (self, args) => ((PickleBufferObject)self).picklebuf_raw());
            PyArgs.Define(type, "release", PyCall.NoArgs, (self, args) => ((PickleBufferObject)self).picklebuf_release());
            return type;
        }

        /// <summary>
        /// PyObject_GetBuffer(obj, view, flags): the object's buffer as a view, or the TypeError of
        /// an object that exports none.
        /// </summary>
        internal static PyMemoryView PyObject_GetBuffer(PyObject obj, int flags)
        {
            if (obj is not PyString && obj.SupportsBuffer())
                return obj.GetBuffer(flags);
            if (PyMemoryView.FromBufferSlot(obj, flags) is { } exported)
                return exported;
            throw PyTypeError.Create($"a bytes-like object is required, not '{obj.GetTypeName()}'");
        }

        // PyMemoryView_FromObject
        internal static PyMemoryView PyMemoryView_FromObject(PyObject obj)
        {
            if (obj is not PyString && obj.SupportsBuffer())
                return obj.GetBuffer(PyMemoryView.FullReadOnly);
            if (PyMemoryView.FromBufferSlot(obj, PyMemoryView.FullReadOnly) is { } exported)
                return exported;
            throw PyTypeError.Create($"memoryview: a bytes-like object is required, not '{obj.GetTypeName()}'");
        }

        // The object a view's buffer belongs to (view.obj): a PickleBuffer redirects to its original.
        private static PyObject ViewObject(PyObject baseObj) =>
            baseObj is PickleBufferObject pickleBuffer && pickleBuffer._obj != null ? pickleBuffer._obj : baseObj;

        // PyPickleBuffer_FromObject
        internal static PyObject PyPickleBuffer_FromObject(PyObject baseObj)
        {
            var self = new PickleBufferObject();
            self._view = PyObject_GetBuffer(baseObj, PyMemoryView.FullReadOnly);
            self._obj = ViewObject(baseObj);
            return self;
        }

        // PyPickleBuffer_GetBuffer
        internal static PyMemoryView PyPickleBuffer_GetBuffer(PyObject obj)
        {
            if (obj is not PickleBufferObject self)
                throw PyTypeError.Create($"expected PickleBuffer, {obj.GetTypeName()} found");
            if (self._view == null)
                throw PyValueError.Create("operation forbidden on released PickleBuffer object");
            return self._view;
        }

        // PyPickleBuffer_Release
        internal static void PyPickleBuffer_Release(PyObject obj)
        {
            if (obj is not PickleBufferObject self)
                throw PyTypeError.Create($"expected PickleBuffer, {obj.GetTypeName()} found");
            self.PyBuffer_Release();
        }

        // PyBuffer_Release(&self->view)
        private void PyBuffer_Release()
        {
            var view = _view;
            _view = null;
            _obj = null;
            if (view != null && !view.IsReleased)
                view.Release();
        }

        // picklebuf_new: PickleBuffer(object, /)
        private static PyObject picklebuf_new(PyType type, PyObject[] args, PyDict kwargs)
        {
            // PyArg_ParseTupleAndKeywords(args, kwds, "O:PickleBuffer", {""}, &base)
            var parsed = PyArgs.Unpack("PickleBuffer", args, kwargs, new[] { "" }, 1, 1, 1);
            return PyPickleBuffer_FromObject(parsed[0]);
        }

        // picklebuf_getbuf: the original object's buffer, asked for again.
        public override PyMemoryView GetBuffer(int flags)
        {
            if (_view == null)
                throw PyValueError.Create("operation forbidden on released PickleBuffer object");
            return PyObject_GetBuffer(_obj, flags);
        }

        public override bool SupportsBuffer() => true;

        // picklebuf_raw: a memoryview of the raw memory underlying this buffer.
        private PyObject picklebuf_raw()
        {
            if (_view == null)
                throw PyValueError.Create("operation forbidden on released PickleBuffer object");
            if (!_view.IsCContiguous)
                throw PyBufferError.Create("cannot extract raw buffer from non-contiguous buffer");
            var m = PyMemoryView_FromObject(this);
            /* Mutate memoryview instance to make it a "raw" memoryview: format "B", one
               dimension, itemsize 1, shape (length,), strides (1,). */
            var raw = m.Reinterpret("B", 1);
            m.Release();
            return raw;
        }

        // picklebuf_release
        private PyObject picklebuf_release()
        {
            PyBuffer_Release();
            return PyNone.Instance;
        }
    }
}
