using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpPy.Core;

namespace SharpPy.Modules
{
    /// <summary>
    /// Basic asyncio module implementation for SharpPy
    /// Provides minimal asyncio.run() and asyncio.sleep() support
    /// </summary>
    public static class AsyncioModule
    {
        private static SimpleEventLoop? _runningLoop = null;

        /// <summary>
        /// asyncio.run(coro) - Run a coroutine to completion
        /// </summary>
        public static PyObject Run(PyVM vm, PyObject[] args, PyDict? kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("run() takes exactly one argument");

            var coro = args[0];
            if (!(coro is PyCoroutine coroutine))
                throw PyTypeError.Create("run() argument must be a coroutine");

            // Create and run simple event loop
            var loop = new SimpleEventLoop(vm);
            _runningLoop = loop;

            try
            {
                return loop.RunUntilComplete(coroutine);
            }
            finally
            {
                _runningLoop = null;
                loop.Close();
            }
        }

        /// <summary>
        /// asyncio.sleep(delay) - Suspend execution for given time
        /// </summary>
        public static PyObject Sleep(PyVM vm, PyObject[] args, PyDict? kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("sleep() takes exactly one argument");

            var delayObj = args[0];
            // CPython 3.12: Modules/_asynciomodule.c - asyncio.sleep equivalent
            double delay;

            if (delayObj is PyFloat pyFloat)
                delay = pyFloat.Value;
            else if (delayObj is PyInt pyInt)
                delay = (double)pyInt.Value;
            else
                throw PyTypeError.Create("sleep() argument must be a number");

            // Create a simple coroutine that returns after delay
            return new SleepCoroutine(vm, delay);
        }

        /// <summary>
        /// asyncio.create_task(coro) - Create a Task from coroutine
        /// </summary>
        public static PyObject CreateTask(PyVM vm, PyObject[] args, PyDict? kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create("create_task() takes exactly one argument");

            var coro = args[0];
            if (!(coro is PyCoroutine coroutine))
                throw PyTypeError.Create("create_task() argument must be a coroutine");

            if (_runningLoop == null)
                throw PyRuntimeError.Create("no running event loop");

            return _runningLoop.CreateTask(coroutine);
        }

        /// <summary>
        /// asyncio.get_event_loop() - Get current event loop
        /// </summary>
        public static PyObject GetEventLoop(PyVM vm, PyObject[] args, PyDict? kwargs)
        {
            if (_runningLoop == null)
                throw PyRuntimeError.Create("no running event loop");

            return _runningLoop;
        }
    }

    /// <summary>
    /// Simple event loop implementation for basic asyncio support
    /// </summary>
    public class SimpleEventLoop : PyObject
    {
        private readonly PyVM _vm;
        private bool _running = false;
        private bool _closed = false;

        public override string GetTypeName() => "AbstractEventLoop";
        public override PyType GetPyType() => PyType.ObjectType;

        public SimpleEventLoop(PyVM vm)
        {
            _vm = vm;
        }

        public PyObject RunUntilComplete(PyCoroutine coroutine)
        {
            if (_closed)
                throw PyRuntimeError.Create("Event loop is closed");

            _running = true;
            try
            {
                // Run the coroutine until completion
                PyObject? value = PyNone.Instance;

                while (true)
                {
                    try
                    {
                        // Send value to coroutine - will throw PyStopIteration when done
                        value = coroutine.Send(value);

                        // If we get here, coroutine yielded a value (await)
                        // For now, we'll treat all awaited values as completed immediately
                        // In a real event loop, this would schedule the awaited operation
                        value = PyNone.Instance;
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration stopIteration)
                    {
                        // Coroutine completed - return the result
                        return stopIteration.Value ?? PyNone.Instance;
                    }
                }
            }
            finally
            {
                _running = false;
            }
        }

        public PyObject CreateTask(PyCoroutine coroutine)
        {
            // For now, just return the coroutine as a "task"
            // In a real implementation, this would wrap it in a Task object
            return coroutine;
        }

        public void Close()
        {
            _closed = true;
        }
    }

    /// <summary>
    /// Simple sleep coroutine - just returns None after conceptual delay
    /// For now, this is a simplified implementation without proper async scheduling
    /// </summary>
    public class SleepCoroutine : PyCoroutine
    {
        private readonly double _delay;
        private bool _started = false;

        public SleepCoroutine(PyVM vm, double delay)
            : base(CreateDummyFrame(vm), vm, "sleep")
        {
            _delay = delay;
        }

        private static PyFrame CreateDummyFrame(PyVM vm)
        {
            // Create a minimal frame that just returns None
            var code = new PyCodeObject(
                name: "sleep",
                instructions: new List<ByteCodeInstruction>
                {
                    new ByteCodeInstruction(ByteCodeOp.LOAD_CONST, 0), // Load None
                    new ByteCodeInstruction(ByteCodeOp.RETURN_VALUE, 0)
                },
                constants: new List<PyObject> { PyNone.Instance },
                names: new List<string>(),
                varNames: new List<string>(),
                argCount: 0,
                posonlyArgCount: 0,
                freeVars: new List<string>(),
                cellVars: new List<string>(),
                defaultValues: new List<PyObject>(),
                flags: 0,
                fileName: "<sleep>",
                exceptionTable: new List<ExceptionTableEntry>()
            );

            return new PyFrame(code, new PyCell[0], new PyScopeChain());
        }

        public new PyObject Send(PyObject value)
        {
            if (!_started)
            {
                _started = true;
                // Simulate async delay (in real implementation would schedule callback)
                if (_delay > 0)
                {
                    Thread.Sleep((int)(_delay * 1000));
                }
            }

            // Sleep is complete, finish the coroutine
            throw PyStopIteration.Create(PyNone.Instance);
        }
    }
}