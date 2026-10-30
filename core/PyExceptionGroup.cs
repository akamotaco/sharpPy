using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// PEP 654 exception groups. CPython 3.12 keeps all of this together in
    /// Objects/exceptions.c, and this class is a transliteration of that block:
    ///
    ///   BaseExceptionGroup_new        argument parsing and validation (the one place that
    ///                                 decides ValueError vs TypeError, and whether
    ///                                 BaseExceptionGroup(...) becomes an ExceptionGroup)
    ///   BaseExceptionGroup_derive     "new group with my metadata, these members"
    ///   exceptiongroup_subset         build the derived part, copy traceback/cause/context
    ///   exceptiongroup_split_recursive  the single matcher + recursive partial match
    ///   BaseExceptionGroup_split/subgroup  the two Python-visible entry points
    ///
    /// The VM does not get its own copy of this logic. exception_group_match in Python/ceval.c
    /// calls exc.split(match_type) as an ordinary attribute lookup, so a subclass can override
    /// it, and _PyExc_PrepReraiseStar reuses the same recursive split with an identity matcher.
    /// Both used to have a second, weaker implementation in PyVM (CLR-type based, message-carrying
    /// group names like "unhandled exceptions"); those copies are gone.
    /// </summary>
    public class PyBaseExceptionGroup : PyException
    {
        /// <summary>
        /// CPython PyBaseExceptionGroupObject.excs: a tuple of exception instances, read-only
        /// once built. It holds PyObject rather than PyException because a BaseExceptionGroup
        /// legitimately contains BaseException-only members (KeyboardInterrupt and friends).
        /// </summary>
        internal readonly PyObject[] Exc;

        protected PyBaseExceptionGroup(PyObject[] callArgs, PyObject[] excs) : base(callArgs)
        {
            Exc = excs;
        }

        public override PyType GetPyType() => PyType.BaseExceptionGroupType;

        public override string GetTypeName() => "BaseExceptionGroup";

        /// <summary>
        /// BaseExceptionGroup_str: "message (N sub-exception)" - note that the members are NOT
        /// included, unlike repr(), which CPython produces from args.
        /// </summary>
        public override PyString AsString()
            => new PyString($"{Message} ({Exc.Length} sub-exception{(Exc.Length != 1 ? "s" : "")})");

        public override string ToString() => AsString().Value;

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                // BaseExceptionGroup_members: message and exceptions are read-only members.
                case "message":
                    return new PyString(Message);
                case "exceptions":
                    return new PyTuple(Exc);
                case "derive":
                    return new PyBoundBuiltinMethod(this,
                        new PyBuiltinMethod("derive", DeriveImpl, 1));
                case "split":
                    return new PyBoundBuiltinMethod(this,
                        new PyBuiltinMethod("split", SplitImpl, 1));
                case "subgroup":
                    return new PyBoundBuiltinMethod(this,
                        new PyBuiltinMethod("subgroup", SubgroupImpl, 1));
                default:
                    return base.GetAttribute(name);
            }
        }

        /// <summary>
        /// BaseExceptionGroup_derive: CPython always builds a BaseExceptionGroup with
        /// (self.message, excs) - which then upgrades itself to ExceptionGroup when every member
        /// is an Exception subclass. So derive of an ExceptionGroup yields an ExceptionGroup, and
        /// derive of a BaseExceptionGroup holding KeyboardInterrupt stays a BaseExceptionGroup.
        /// </summary>
        private PyObject DeriveImpl(PyObject self, PyObject[] args)
        {
            // PyArg_ParseTuple(args, "O", &excs) - the format has no function name, so CPython
            // reports the anonymous wording. Measured.
            if (args.Length != 1)
                throw PyTypeError.Create(
                    $"function takes exactly 1 argument ({args.Length} given)");
            return NewGroup(new PyObject[] { new PyString(Message), args[0] },
                            requestedBase: false);
        }

        private PyObject SplitImpl(PyObject self, PyObject[] args)
        {
            // PyArg_UnpackTuple(args, "split", 1, 1, &matcher) - "split expected 1 argument, got 0".
            if (args.Length != 1)
                throw PyTypeError.Create($"split expected 1 argument, got {args.Length}");
            Matcher matcher = Matcher.From(args[0]);
            SplitResult matched = SplitRecursive(this, matcher, constructRest: true);
            return new PyTuple(new PyObject[]
            {
                matched.Match ?? PyNone.Instance,
                matched.Rest ?? PyNone.Instance,
            });
        }

        private PyObject SubgroupImpl(PyObject self, PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"subgroup expected 1 argument, got {args.Length}");
            Matcher matcher = Matcher.From(args[0]);
            SplitResult matched = SplitRecursive(this, matcher, constructRest: false);
            return matched.Match ?? PyNone.Instance;
        }

        /// <summary>
        /// exceptiongroup_subset: wrap exs in a group derived from this one, or None when exs is
        /// empty. Metadata travels with it: traceback, __context__, __cause__.
        /// </summary>
        private PyObject Subset(PyObject[] exs)
        {
            if (exs.Length == 0) return null;

            PyObject derived = DeriveImpl(this, new PyObject[] { new PyList(exs) });
            if (!(derived is PyBaseExceptionGroup))
                throw PyTypeError.Create("derive must return an instance of BaseExceptionGroup");

            PyBaseExceptionGroup group = (PyBaseExceptionGroup)derived;
            group.__traceback__ = __traceback__;
            group.__context__ = __context__;
            group.__cause__ = __cause__;
            group.__suppress_context__ = __suppress_context__;
            return group;
        }

        /// <summary>
        /// exceptiongroup_split_recursive. Full match -> the exception itself. Leaf with no match
        /// -> the exception in the rest part (only when the caller wants a rest part). Otherwise
        /// this is a group, so recurse into the members and rebuild both halves through subset().
        /// </summary>
        private static SplitResult SplitRecursive(PyObject exc, Matcher matcher, bool constructRest)
        {
            var result = new SplitResult();

            if (matcher.Matches(exc))
            {
                result.Match = exc;
                return result;
            }

            if (!(exc is PyBaseExceptionGroup group))
            {
                if (constructRest) result.Rest = exc;
                return result;
            }

            var matchList = new List<PyObject>();
            var restList = constructRest ? new List<PyObject>() : null;

            for (int i = 0; i < group.Exc.Length; i++)
            {
                SplitResult inner = SplitRecursive(group.Exc[i], matcher, constructRest);
                if (inner.Match != null) matchList.Add(inner.Match);
                if (inner.Rest != null) restList.Add(inner.Rest);
            }

            result.Match = group.Subset(matchList.ToArray());
            if (constructRest) result.Rest = group.Subset(restList.ToArray());
            return result;
        }

        /// <summary>
        /// exception_group_projection: keep the members of reraised, by identity, with the nesting
        /// structure of this group. _PyExc_PrepReraiseStar uses it to rebuild the part of the
        /// original group that handlers re-raised instead of replacing.
        /// </summary>
        internal PyObject Projection(IList<PyObject> keep)
        {
            var leafIds = new HashSet<PyObject>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < keep.Count; i++) CollectLeafIds(keep[i], leafIds);
            PyObject matched = SplitRecursive(this, Matcher.ByLeafId(leafIds),
                                              constructRest: false).Match;
            return matched ?? PyNone.Instance;
        }

        /// <summary>collect_exception_group_leaf_ids.</summary>
        private static void CollectLeafIds(PyObject exc, HashSet<PyObject> leafIds)
        {
            if (exc is PyNone) return;
            if (!(exc is PyBaseExceptionGroup group))
            {
                leafIds.Add(exc);
                return;
            }
            for (int i = 0; i < group.Exc.Length; i++) CollectLeafIds(group.Exc[i], leafIds);
        }

        /// <summary>
        /// is_same_exception_metadata: two exceptions are the same reraise of one original when
        /// traceback, cause and context are the very same objects. __notes__ has no storage in
        /// this interpreter yet, so that one field of the comparison is not observable here.
        /// </summary>
        internal static bool SameMetadata(PyObject a, PyObject b)
        {
            if (!(a is PyBaseException x) || !(b is PyBaseException y)) return false;
            return ReferenceEquals(x.__traceback__, y.__traceback__) &&
                   ReferenceEquals(x.__cause__, y.__cause__) &&
                   ReferenceEquals(x.__context__, y.__context__);
        }

        /// <summary>
        /// _PyExc_CreateExceptionGroup(msg, excs) - the constructor the interpreter itself calls
        /// when it has to wrap a naked exception for an except* clause or report several
        /// unhandled exceptions together. CPython always uses the empty message for that, and it
        /// hands the constructor the sequence object it already holds rather than converting:
        /// the naked-wrap path in Python/ceval.c passes a tuple, the reraise path passes the list
        /// of freshly raised exceptions. That is observable, because .args keeps the object and
        /// repr therefore shows ExceptionGroup('', (ValueError('plain'),)) with round brackets.
        /// </summary>
        internal static PyObject Create(string message, PyObject sequence)
            => NewGroup(new PyObject[] { new PyString(message), sequence },
                        requestedBase: false);

        /// <summary>
        /// BaseExceptionGroup_new. requestedBase distinguishes "BaseExceptionGroup(...)" from
        /// "ExceptionGroup(...)"; without it, a group whose members are all Exception subclasses
        /// is reported as an ExceptionGroup, which is why ExceptionGroup and
        /// BaseExceptionGroup('m', [ValueError()]) are the same class.
        /// </summary>
        internal static PyObject NewGroup(PyObject[] callArgs, bool requestedBase)
        {
            if (callArgs.Length != 2)
                throw PyTypeError.Create(
                    $"BaseExceptionGroup.__new__() takes exactly 2 arguments ({callArgs.Length} given)");
            if (!(callArgs[0] is PyString message))
                throw PyTypeError.Create(
                    $"BaseExceptionGroup.__new__() argument 1 must be str, not {callArgs[0].GetTypeName()}");

            PyObject[] items = ItemsOf(callArgs[1]);
            if (items.Length == 0)
                throw PyValueError.Create(
                    "second argument (exceptions) must be a non-empty sequence");

            bool nestedBaseExceptions = false;
            for (int i = 0; i < items.Length; i++)
            {
                if (!(items[i] is PyBaseException))
                    throw PyValueError.Create(
                        $"Item {i} of second argument (exceptions) is not an exception");
                // Exception, not BaseException-only. KeyboardInterrupt/SystemExit are the
                // BaseException-only family; this interpreter does not define those names yet,
                // which docs/known_gaps.md tracks separately.
                if (!(items[i] is PyException)) nestedBaseExceptions = true;
            }

            // args is stored as given, so repr() and .args show the sequence object the caller
            // passed, exactly as CPython's BaseException_init does.
            if (requestedBase)
            {
                if (nestedBaseExceptions)
                    return new PyBaseExceptionGroup(callArgs, items);
                return new PyExceptionGroup(callArgs, items);
            }
            if (nestedBaseExceptions)
                throw PyTypeError.Create("Cannot nest BaseExceptions in an ExceptionGroup");
            return new PyExceptionGroup(callArgs, items);
        }

        /// <summary>
        /// PySequence_Check + PySequence_Tuple for the constructor. A str is a sequence in Python,
        /// so ExceptionGroup('m', 'abc') reports item 0 as not an exception rather than treating
        /// the string as a single member.
        /// </summary>
        private static PyObject[] ItemsOf(PyObject value)
        {
            if (value is PyList list) return list.Items.ToArray();
            if (value is PyTuple tuple) return tuple.Items.ToArray();
            if (value is PyString text)
            {
                var chars = new PyObject[text.Value.Length];
                for (int i = 0; i < chars.Length; i++) chars[i] = new PyString(text.Value[i].ToString());
                return chars;
            }
            if (value is PyBytes bytes)
            {
                var ints = new PyObject[bytes.Value.Length];
                for (int i = 0; i < ints.Length; i++) ints[i] = new PyInt(bytes.Value[i]);
                return ints;
            }

            if (value is PyRange)
            {
                // A range is a sequence in CPython, so it is enumerated here (PySequence_Tuple
                // materialises it too). Big ranges blow up in both interpreters.
                var steps = new List<PyObject>();
                var rangeIterator = value.GetIterator();
                while (true)
                {
                    PyObject step;
                    try { step = rangeIterator.Next(); }
                    catch (PythonException error) when (error.PyException is PyStopIteration) { break; }
                    steps.Add(step);
                }
                return steps.ToArray();
            }
            // PySequence_Check is not "iterable": measured, CPython answers
            // ExceptionGroup('m', {'a': 1}) and ExceptionGroup('m', (e for e in ...)) with
            // TypeError: second argument (exceptions) must be a sequence, while range(2) gets
            // far enough to report item 0 as not an exception.
            throw PyTypeError.Create("second argument (exceptions) must be a sequence");
        }

        /// <summary>
        /// BaseExceptionGroup_members are declared READONLY in CPython, so assigning them fails
        // with the member descriptor's own text rather than a missing-attribute error.
        /// </summary>
        public override void SetAttribute(string name, PyObject value)
        {
            if (name == "message" || name == "exceptions")
                throw PyAttributeError.Create("readonly attribute");
            base.SetAttribute(name, value);
        }

        internal struct SplitResult
        {
            public PyObject Match;
            public PyObject Rest;
        }

        /// <summary>
        /// get_matcher_type + exceptiongroup_split_check_match. CPython accepts a Python function
        /// (predicate), an exception class, or an exact tuple of exception classes; anything else
        /// is "expected a function, exception type or tuple of exception types".
        /// </summary>
        internal readonly struct Matcher
        {
            private readonly PyObject _byType;
            private readonly PyObject _predicate;
            private readonly HashSet<PyObject> _leafIds;

            private Matcher(PyObject byType, PyObject predicate, HashSet<PyObject> leafIds)
            {
                _byType = byType;
                _predicate = predicate;
                _leafIds = leafIds;
            }

            internal static Matcher From(PyObject value)
            {
                if (value is PyFunction) return new Matcher(null, value, null);
                if (IsExceptionClass(value)) return new Matcher(value, null, null);
                if (value is PyTuple tuple)
                {
                    for (int i = 0; i < tuple.Items.Length; i++)
                        if (!IsExceptionClass(tuple.Items[i])) goto NotAMatcher;
                    return new Matcher(value, null, null);
                }
            NotAMatcher:
                throw PyTypeError.Create(
                    "expected a function, exception type or tuple of exception types");
            }

            internal static Matcher ByLeafId(HashSet<PyObject> ids) => new Matcher(null, null, ids);

            internal bool Matches(PyObject exc)
            {
                if (_leafIds != null)
                {
                    // EXCEPTION_GROUP_MATCH_INSTANCE_IDS never matches a group itself; only the
                    // leaves inside it are collected.
                    return !(exc is PyBaseExceptionGroup) && _leafIds.Contains(exc);
                }
                if (_predicate != null)
                    return _predicate.Call(new PyObject[] { exc }, null).PyBoolValue();
                return PyVM.ExceptionMatchesHandler(exc, _byType);
            }

            private static bool IsExceptionClass(PyObject value)
                => value is PyType type && type.IsSubclassOf(PyType.BaseExceptionType);
        }
    }

    /// <summary>
    /// ExceptionGroup = BaseExceptionGroup + Exception. Members may not include
    /// BaseException-only exceptions, and a group built by the interpreter is always one of these
    /// unless it really does hold a BaseException-only member.
    /// </summary>
    public class PyExceptionGroup : PyBaseExceptionGroup
    {
        internal PyExceptionGroup(PyObject[] callArgs, PyObject[] excs) : base(callArgs, excs) { }

        public override PyType GetPyType() => PyType.ExceptionGroupType;

        public override string GetTypeName() => "ExceptionGroup";
    }
}
