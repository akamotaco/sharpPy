using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpPy.Modules
{
    /// <summary>
    /// _contextvars module - CPython 3.12: Python/context.c, Modules/_contextvarsmodule.c.
    /// CPython keeps a context in an immutable HAMT so that copying one costs nothing; ImmutableDictionary
    /// is the same structure in the base library. Variables are told apart by identity, as in the HAMT.
    /// </summary>
    internal static class _ContextvarsModule
    {
        private static readonly ImmutableDictionary<PyObject, PyObject> Empty =
            ImmutableDictionary.Create<PyObject, PyObject>(ReferenceEqualityComparer.Instance);

        private static readonly PyType ContextType = new PyType("Context", new[] { PyType.ObjectType }, "_contextvars");
        private static readonly PyType ContextVarType = new PyType("ContextVar", new[] { PyType.ObjectType }, "_contextvars");
        private static readonly PyType TokenType = new PyType("Token", new[] { PyType.ObjectType }, "_contextvars");
        private static readonly PyObject Missing = new PyMissing();

        // ts->context, one per thread.
        [ThreadStatic] private static PyContext? _current;

        private static PyContext Current => _current ??= new PyContext(Empty);

        internal static PyModule CreateContextvarsModule()
        {
            var module = new PyModule("_contextvars");
            module.ModuleDict["Context"] = ContextType;
            module.ModuleDict["ContextVar"] = ContextVarType;
            module.ModuleDict["Token"] = TokenType;
            module.ModuleDict["copy_context"] = new PyBuiltinFunction("copy_context", _ => new PyContext(Current.Vars));

            Method(ContextType, "__new__", (cls, args, kwargs) => new PyContext(Empty));
            Method(ContextType, "run", ContextRun, 1, int.MaxValue, acceptsKwargs: true);
            Method(ContextType, "copy", (self, args, kwargs) => new PyContext(((PyContext)self).Vars), 0, 0);
            Method(ContextType, "__len__", (self, args, kwargs) => new PyInt(((PyContext)self).Vars.Count), 0, 0);
            Method(ContextType, "__contains__", (self, args, kwargs) =>
                PyBool.FromBool(((PyContext)self).Vars.ContainsKey(CheckVar(args[0]))), 1, 1);
            Method(ContextType, "__getitem__", (self, args, kwargs) =>
                ((PyContext)self).Vars.TryGetValue(CheckVar(args[0]), out var value)
                    ? value : throw new PythonException(new PyKeyError(args[0].ToString())), 1, 1);
            Method(ContextType, "get", (self, args, kwargs) =>
                ((PyContext)self).Vars.TryGetValue(CheckVar(args[0]), out var value)
                    ? value : args.Length > 1 ? args[1] : PyNone.Instance, 1, 2);
            // tp_iter: a slot wrapper.
            ContextType.TypeDict["__iter__"] = new PyWrapperDescriptor("__iter__", ContextType, (self, args, kwargs) => IterOf(Keys((PyContext)self)));
            Method(ContextType, "keys", (self, args, kwargs) => IterOf(Keys((PyContext)self)), 0, 0);
            Method(ContextType, "values", (self, args, kwargs) =>
            {
                var values = new PyList();
                foreach (var pair in ((PyContext)self).Vars) values.Append(pair.Value);
                return IterOf(values);
            }, 0, 0);
            Method(ContextType, "items", (self, args, kwargs) =>
            {
                var items = new PyList();
                foreach (var pair in ((PyContext)self).Vars) items.Append(new PyTuple(new[] { pair.Key, pair.Value }));
                return IterOf(items);
            }, 0, 0);

            Method(ContextVarType, "__new__", ContextVarNew, 1, 1, acceptsKwargs: true);
            Method(ContextVarType, "get", ContextVarGet, 0, 1);
            Method(ContextVarType, "set", ContextVarSet, 1, 1);
            Method(ContextVarType, "reset", ContextVarReset, 1, 1);

            ContextVarType.TypeDict["name"] = new PyMemberDescriptor("name", ContextVarType, self => ((PyContextVar)self).Name, readOnly: true);
            TokenType.TypeDict["var"] = new PyGetSetDescriptor("var", TokenType, self => ((PyContextToken)self).Variable);
            TokenType.TypeDict["old_value"] = new PyGetSetDescriptor("old_value", TokenType, self => ((PyContextToken)self).Old ?? Missing);
            TokenType.TypeDict["MISSING"] = Missing;
            return module;
        }

        private static void Method(PyType type, string name, Func<PyObject, PyObject[], PyDict, PyObject> body,
            int min = 0, int max = int.MaxValue, bool acceptsKwargs = false)
        {
            type.TypeDict[name] = new PyMethodDescriptor(name, type, body, min, max, acceptsKwargs);
        }

        private static PyObject CheckVar(PyObject key) => key is PyContextVar
            ? key : throw PyTypeError.Create($"a ContextVar key was expected, got {key}");

        private static PyList Keys(PyContext context)
        {
            var keys = new PyList();
            foreach (var pair in context.Vars) keys.Append(pair.Key);
            return keys;
        }

        private static PyObject IterOf(PyList list) => list.GetIterator();

        // context_run: _PyContext_Enter, the call, _PyContext_Exit.
        private static PyObject ContextRun(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var context = (PyContext)self;
            if (context.Entered)
                throw PyRuntimeError.Create($"cannot enter context: {context} is already entered");

            var previous = Current;
            context.Entered = true;
            _current = context;
            try
            {
                var rest = new PyObject[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                return args[0].Call(rest, kwargs);
            }
            finally
            {
                _current = previous;
                context.Entered = false;
            }
        }

        // contextvar_tp_new: the name is positional, default keyword-only.
        private static PyObject ContextVarNew(PyObject cls, PyObject[] args, PyDict kwargs)
        {
            if (args[0] is not PyString name)
                throw PyTypeError.Create("context variable name must be a str");
            PyObject? fallback = null;
            if (kwargs != null) kwargs.TryGetStoredValue(PyString.From("default"), out fallback);
            return new PyContextVar(name, fallback);
        }

        private static PyObject ContextVarGet(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var variable = (PyContextVar)self;
            if (Current.Vars.TryGetValue(variable, out var value)) return value;
            if (args.Length > 0) return args[0];
            if (variable.Default != null) return variable.Default;
            throw new PythonException(new PyLookupError(variable.ToString()));
        }

        private static PyObject ContextVarSet(PyObject self, PyObject[] args, PyDict kwargs)
        {
            var context = Current;
            context.Vars.TryGetValue(self, out var old);
            context.Vars = context.Vars.SetItem(self, args[0]);
            return new PyContextToken(context, (PyContextVar)self, old);
        }

        private static PyObject ContextVarReset(PyObject self, PyObject[] args, PyDict kwargs)
        {
            if (args[0] is not PyContextToken token)
                throw PyTypeError.Create($"expected an instance of Token, got {args[0]}");
            if (token.Used)
                throw PyRuntimeError.Create($"{token} has already been used once");
            if (token.Variable != self)
                throw PyValueError.Create($"{token} was created by a different ContextVar");
            var context = Current;
            if (token.Context != context)
                throw PyValueError.Create($"{token} was created in a different Context");

            token.Used = true;
            context.Vars = token.Old != null ? context.Vars.SetItem(self, token.Old) : context.Vars.Remove(self);
            return PyNone.Instance;
        }

        private sealed class PyMissing : PyObject
        {
            public override string GetTypeName() => "Token.MISSING";
            public override string ToString() => "<Token.MISSING>";
        }

        private sealed class PyContext : PyObject
        {
            internal ImmutableDictionary<PyObject, PyObject> Vars;
            internal bool Entered;

            internal PyContext(ImmutableDictionary<PyObject, PyObject> vars) { Vars = vars; }

            public override PyType GetPyType() => ContextType;
            public override string GetTypeName() => "Context";
            protected internal override PyObject Iter() => IterOf(Keys(this));
        }

        private sealed class PyContextVar : PyObject
        {
            private readonly PyString _name;
            internal readonly PyObject? Default;

            internal PyContextVar(PyString name, PyObject? fallback)
            {
                _name = name;
                Default = fallback;
            }

            public override PyType GetPyType() => ContextVarType;
            public override string GetTypeName() => "ContextVar";
            public override string ToString() => $"<ContextVar name='{_name.Value}'>";

            internal PyString Name => _name;
        }

        private sealed class PyContextToken : PyObject
        {
            internal readonly PyContext Context;
            internal readonly PyContextVar Variable;
            internal readonly PyObject? Old;
            internal bool Used;

            internal PyContextToken(PyContext context, PyContextVar variable, PyObject? old)
            {
                Context = context;
                Variable = variable;
                Old = old;
            }

            public override PyType GetPyType() => TokenType;
            public override string GetTypeName() => "Token";

        }
    }
}
