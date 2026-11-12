using System;
using System.Collections.Generic;
using System.Threading;

namespace SharpPy
{
    /// <summary>
    /// What Include/cpython/pystate.h keeps per thread. CPython finds it through a thread local on
    /// every access. Here only the thread holding the GIL runs the VM, so the VM keeps these in its
    /// own fields and PyGil moves them in and out at a switch: the single-threaded path pays nothing.
    /// </summary>
    internal sealed class PyThreadState
    {
        internal readonly Stack<PyFrame> Frames = new();
        internal readonly List<Modules.PyLock> Sentinels = new();
        internal PyFrame InlineCall;
        internal int HostFrames;
        internal bool Tracing;
        internal PyObject TraceFunction;
        internal PyObject ProfileFunction;
        internal bool IsMain;
        internal int Ident = Environment.CurrentManagedThreadId;

        // tstate->dict as _thread._local uses it: self->key -> the localdummy of that local object.
        internal readonly Dictionary<long, Modules.PyThreadLocal.Dummy> LocalDummies = new();
        private int _pruneAt = 64;

        internal void AddLocalDummy(long key, Modules.PyThreadLocal.Dummy dummy)
        {
            // local_clear pops a dying local's dummy from every thread state; here a thread drops the
            // dummies of locals already collected whenever its table has doubled.
            if (LocalDummies.Count >= _pruneAt)
            {
                var dead = new List<long>();
                foreach (var pair in LocalDummies)
                    if (!pair.Value.OwnerAlive)
                        dead.Add(pair.Key);
                foreach (var gone in dead)
                    LocalDummies.Remove(gone);
                _pruneAt = Math.Max(64, 2 * LocalDummies.Count);
            }
            if (LocalDummies.Remove(key, out var replaced))
                replaced.Destroyed();
            LocalDummies[key] = dummy;
        }

        /// <summary>PyThreadState_Clear's Py_CLEAR(tstate->dict): each dummy goes, and its local dict with it.</summary>
        internal void ClearLocalDummies()
        {
            foreach (var dummy in LocalDummies.Values)
                dummy.Destroyed();
            LocalDummies.Clear();
        }
    }

    /// <summary>
    /// Python/ceval_gil.c. It starts with the first thread, so a program without threads has no lock
    /// and no thread state switch. A switch happens at the eval breaker or around a blocking call,
    /// never inside a C# operation.
    /// </summary>
    internal static class PyGil
    {
        private static readonly object Gate = new();
        private static bool _held;
        private static PyThreadState _holder;
        private static int _waiters;
        private static long _switchNumber;
        private static volatile bool _dropRequest;
        private static volatile bool _finalizing;
        private static int _intervalMilliseconds = 5;

        [ThreadStatic] private static PyThreadState _current;

        private static readonly List<PyThreadState> States = new();

        internal static bool Enabled { get; private set; }

        /// <summary>_PyErr_SetInterruptEx: the main thread raises at its next eval breaker.</summary>
        internal static volatile bool InterruptPending;

        internal static void Register(PyThreadState state)
        {
            lock (States)
                States.Add(state);
        }

        internal static void Unregister(PyThreadState state)
        {
            lock (States)
                States.Remove(state);
        }

        /// <summary>_PyEval_SetTraceAllThreads: the VM's fields are one thread, the states the others.</summary>
        internal static void ForAllThreads(Action<PyThreadState> action)
        {
            var bound = PyVM.Instance.BoundState;
            PyVM.Instance.Unbind(bound);
            lock (States)
            {
                action(bound);
                foreach (var state in States)
                    if (!ReferenceEquals(state, bound))
                        action(state);
            }
            PyVM.Instance.Bind(bound);
        }

        internal static PyThreadState Current => _current;

        internal static bool HeldByThisThread => _current != null && ReferenceEquals(_holder, _current);

        /// <summary>A host thread that has never run Python: PyGILState_Ensure makes its state.</summary>
        internal static PyThreadState AttachNew()
        {
            var state = new PyThreadState();
            Register(state);
            _current = state;
            return state;
        }

        internal static double SwitchInterval
        {
            get => _intervalMilliseconds / 1000.0;
            set => _intervalMilliseconds = Math.Max(1, (int)Math.Ceiling(value * 1000));
        }

        /// <summary>PyEval_InitThreads: the running thread holds it from here on.</summary>
        internal static void Enable()
        {
            if (Enabled)
                return;
            _current = PyVM.Instance.BoundState;
            Register(_current);
            _held = true;
            _holder = _current;
            Enabled = true;
        }

        /// <summary>A thread that has never run Python gets its state here.</summary>
        internal static void Attach(PyThreadState state) => _current = state;

        // take_gil
        internal static void Take(PyThreadState state)
        {
            lock (Gate)
            {
                _waiters++;
                // take_gil: once finalization has begun only the finalizing thread runs again; any
                // other thread waits for good, even while that thread has let the GIL go.
                while (_held || (_finalizing && !ReferenceEquals(state, _finalizer)))
                {
                    if (_finalizing && !ReferenceEquals(state, _finalizer))
                    {
                        Monitor.Wait(Gate);
                        continue;
                    }
                    if (!Monitor.Wait(Gate, _intervalMilliseconds) && _held)
                    {
                        _dropRequest = true;
                        PyPending.Flag = true;
                    }
                }
                _waiters--;
                _held = true;
                _holder = state;
                _dropRequest = false;
                _switchNumber++;
                Monitor.PulseAll(Gate);
            }
            PyVM.Instance.Bind(state);
        }

        // drop_gil
        internal static void Drop(PyThreadState state)
        {
            PyVM.Instance.Unbind(state);
            lock (Gate)
            {
                _held = false;
                _holder = null;
                Monitor.PulseAll(Gate);
            }
        }

        /// <summary>The eval breaker's half: FORCE_SWITCHING, so the waiter gets it.</summary>
        internal static void YieldIfRequested()
        {
            if (!_dropRequest)
                return;
            var state = _current;
            long seen;
            lock (Gate)
                seen = _switchNumber;
            Drop(state);
            lock (Gate)
            {
                while (_switchNumber == seen && _waiters > 0)
                    Monitor.Wait(Gate, 1);
            }
            Take(state);
        }

        /// <summary>Py_BEGIN_ALLOW_THREADS: the action touches no Python object.</summary>
        internal static void AllowThreads(Action action)
        {
            if (!Enabled)
            {
                action();
                return;
            }
            var state = _current;
            Drop(state);
            try
            {
                action();
            }
            finally
            {
                Take(state);
            }
        }

        /// <summary>_PyRuntimeState_SetFinalizing: this thread keeps the GIL to the end.</summary>
        internal static void BeginFinalizing()
        {
            lock (Gate)
            {
                _finalizer = _current;
                _finalizing = true;
            }
        }

        private static PyThreadState _finalizer;

        internal static bool Finalizing => _finalizing;
    }
}
