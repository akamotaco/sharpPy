using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace SharpPy.Core
{
    /// <summary>
    /// CPython 3.12 Native Coroutine - PEP 492 구현
    /// Generator와 구별되는 별도 타입으로 __await__ 메서드만 구현
    /// </summary>
    public class PyCoroutine : PyObject
    {
        static PyCoroutine()
        {
            InitializeCoroutineDescriptors();
        }

        private static void InitializeCoroutineDescriptors()
        {
            var corType = PyType.CoroutineType;

            // send method descriptor
            PyGenerator.InstallNames(corType, self => ((PyCoroutine)self).NameObject, (self, value) => ((PyCoroutine)self).NameObject = value,
                self => ((PyCoroutine)self).Qualname, (self, value) => ((PyCoroutine)self).Qualname = value);
            corType.TypeDict["send"] = new PyMethodDescriptor(
                "send", corType,
                (self, args, kwargs) => {
                    var coroutine = (PyCoroutine)self;
                    CheckSendArguments("coroutine", args, kwargs);
                    return coroutine.Send(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            corType.TypeDict["throw"] = new PyMethodDescriptor("throw", corType,
                (self, args, kwargs) => {
                    var coroutine = (PyCoroutine)self;
                    CheckThrowArguments("coroutine", args, kwargs);
                    if (args.Length > 1) PyWarnings.DeprecatedThrow();
                    return coroutine.Throw(args);
                }, minArgs: 1, maxArgs: 3);

            // close method descriptor
            corType.TypeDict["close"] = new PyMethodDescriptor(
                "close", corType,
                (self, args, kwargs) => {
                    var coroutine = (PyCoroutine)self;
                    CheckCloseArguments("coroutine", args, kwargs);
                    coroutine.Close();
                    return PyNone.Instance;
                },
                minArgs: 0, maxArgs: 0
            );

            // __await__ method descriptor
            corType.TypeDict["__await__"] = new PyWrapperDescriptor(
                "__await__", corType,
                (self, args, kwargs) => {
                    var coroutine = (PyCoroutine)self;
                    return coroutine.GetAwaiter();
                },
                minArgs: 0, maxArgs: 0
            );
        }

        public override PyType GetPyType() => PyType.CoroutineType;

        internal static void CheckSendArguments(string typeName, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{typeName}.send() takes no keyword arguments");
            if (args.Length != 1)
                throw PyTypeError.Create($"{typeName}.send() takes exactly one argument ({args.Length} given)");
        }

        internal static void CheckCloseArguments(string typeName, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{typeName}.close() takes no keyword arguments");
            if (args.Length != 0)
                throw PyTypeError.Create($"{typeName}.close() takes no arguments ({args.Length} given)");
        }

        internal static void CheckThrowArguments(string typeName, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create($"{typeName}.throw() takes no keyword arguments");
            if (args.Length < 1)
                throw PyTypeError.Create("throw expected at least 1 argument, got 0");
            if (args.Length > 3)
                throw PyTypeError.Create($"throw expected at most 3 arguments, got {args.Length}");
        }

        // The frame runs in the one generator core (gen_send_ex2, _gen_throw, gen_close), as a
        // coroutine: its errors say "coroutine" and a finished one cannot be reused.
        internal readonly PyGenerator Core;

        public string Name { get => Core.Name; set => Core.Name = value; }
        internal PyObject NameObject { get => Core.NameObject; set => Core.NameObject = value; }
        public PyObject Qualname { get => Core.Qualname; set => Core.Qualname = value; }
        public bool IsFinished => Core.IsFinished;
        internal PyFrame Frame => Core.Frame;

        public PyCoroutine(PyFrame frame, PyVM vm, string name, string qualname = null)
        {
            Core = new PyGenerator(frame ?? throw new ArgumentNullException(nameof(frame)), vm, name, PyGenerator.GenKind.Coroutine);
            Core.Qualname = PyString.From(qualname ?? frame.Code.QualName ?? name);
            frame.OwnerGenerator = this;
            frame.IsCoroutine = true;
            if (OriginDepth > 0)
                Origin = ComputeOrigin();
        }

        /// <summary>_PyGen_yf: what this coroutine is awaiting.</summary>
        internal PyObject YieldFrom => Core.GetYieldFromReceiver();

        internal PyObject Origin { get; } = PyNone.Instance;
        internal static int OriginDepth;

        // compute_cr_origin
        private static PyObject ComputeOrigin()
        {
            var entries = new List<PyObject>();
            PyObject frame = PyVM.CurrentFrame;
            while (frame is PyFrame current && entries.Count < OriginDepth)
            {
                entries.Add(PyTuple.From(new PyObject[]
                {
                    PyString.From(current.Code.FileName), current.GetAttribute("f_lineno"), PyString.From(current.Code.Name),
                }));
                frame = current.GetAttribute("f_back");
            }
            return PyTuple.From(entries.ToArray());
        }

        // _PyGen_Finalize: a coroutine that never ran says so, one that did is closed.
        internal void Finalize()
        {
            if (Core.IsFinished)
                return;
            if (!Core.Started)
            {
                Core.MarkFinished();
                var warnings = PyImportSystem.Import("warnings");
                warnings.GetAttribute("_warn_unawaited_coroutine").Call(new PyObject[] { this }, null);
                return;
            }
            Core.Close();
        }

        /// <summary>coro_await: the coroutine_wrapper over this coroutine.</summary>
        public PyIterator GetAwaiter() => new PyCoroutineAwaiter(this);

        public PyObject Send(PyObject? value = null) => Core.Send(value ?? PyNone.Instance);

        public void Close() => Core.Close();

        internal PyFrame ResumedBy { set => Core.ResumedBy = value; }

        internal PyObject Throw(PyObject[] args) =>
            Core.Throw(args[0], args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);

        protected internal override PyString Repr() =>
            PyString.From($"<coroutine object {Qualname.ToClrString()} at {PyObjectId.Pointer(this)}>");

        public override string ToString() => $"<coroutine object {Name} at {PyObjectId.Pointer(this)}>";
    }

    /// <summary>
    /// Coroutine의 __await__ 메서드가 반환하는 Awaiter
    /// </summary>
    public class PyCoroutineAwaiter : PyIterator
    {
        private static readonly PyType WrapperType = new PyType("coroutine_wrapper", new[] { PyType.ObjectType });

        static PyCoroutineAwaiter()
        {
            WrapperType.TypeDict["throw"] = new PyMethodDescriptor("throw", WrapperType,
                (self, args, kwargs) => {
                    var wrapper = (PyCoroutineAwaiter)self;
                    PyCoroutine.CheckThrowArguments("coroutine_wrapper", args, kwargs);
                    if (args.Length > 1) PyWarnings.DeprecatedThrow();
                    return wrapper._coroutine.Throw(args);
                }, minArgs: 1, maxArgs: 3);
            WrapperType.TypeDict["close"] = new PyMethodDescriptor("close", WrapperType,
                (self, args, kwargs) => {
                    var wrapper = (PyCoroutineAwaiter)self;
                    PyCoroutine.CheckCloseArguments("coroutine_wrapper", args, kwargs);
                    wrapper.Close();
                    return PyNone.Instance;
                }, minArgs: 0, maxArgs: 0);
            WrapperType.TypeDict["send"] = new PyMethodDescriptor("send", WrapperType,
                (self, args, kwargs) => {
                    var wrapper = (PyCoroutineAwaiter)self;
                    PyCoroutine.CheckSendArguments("coroutine_wrapper", args, kwargs);
                    return wrapper.Send(args[0]);
                }, minArgs: 1, maxArgs: 1);
            // tp_iter: a slot wrapper.
            WrapperType.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", WrapperType,
                (self, args, kwargs) => {
                    return self;
                });
            // tp_iternext: a slot wrapper.
            WrapperType.TypeDict["__next__"] = new PyWrapperDescriptor("__next__", WrapperType,
                (self, args, kwargs) => {
                    return ((PyCoroutineAwaiter)self).IterNext();
                });
        }


        private readonly PyCoroutine _coroutine;
        internal PyCoroutine Coroutine => _coroutine;

        public override PyType GetPyType() => WrapperType;
        public override bool IsCallable() => false;

        public PyObject Send(PyObject value) => _coroutine.Send(value);
        public void Close() => _coroutine.Close();
        internal PyObject Throw(PyObject[] args) => _coroutine.Throw(args);

        internal PyFrame ResumedBy { set => _coroutine.ResumedBy = value; }
        
        public PyCoroutineAwaiter(PyCoroutine coroutine)
        {
            _coroutine = coroutine;
        }
        
        protected internal override PyObject IterNext()
        {
            return _coroutine.Send(null);
        }
        
        public override string ToString()
        {
            return $"<coroutine_wrapper at {PyObjectId.Pointer(this)}>";
        }
    }
}
