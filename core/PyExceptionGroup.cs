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

        // PyBaseExceptionGroupObject.msg and .excs, the slots of the READONLY members: the str the
        // constructor was given, and the one tuple made from the sequence.
        internal readonly PyObject Msg;
        internal readonly PyTuple Excs;

        /// <summary>The Python object when this is the struct of a subclass instance.</summary>
        internal PyObject Owner;

        /// <summary>_PyBaseExceptionGroup_Check: a group, or an instance of a subclass of one.</summary>
        internal static PyBaseExceptionGroup Of(PyObject exception) =>
            exception as PyBaseExceptionGroup ?? PyExceptionSlots.LayoutOf(exception) as PyBaseExceptionGroup;

        protected PyBaseExceptionGroup(PyObject[] callArgs, PyObject[] excs) : base(callArgs)
        {
            Exc = excs;
            Msg = callArgs[0];
            Excs = new PyTuple(excs);
        }

        public override PyType GetPyType() => PyType.BaseExceptionGroupType;

        public override string GetTypeName() => "BaseExceptionGroup";

        /// <summary>
        /// BaseExceptionGroup_str: "message (N sub-exception)" - note that the members are NOT
        /// included, unlike repr(), which CPython produces from args.
        /// </summary>
        public override PyString AsString() => GroupStr();

        internal PyString GroupStr()
            => new PyString($"{Msg.ToStr().Value} ({Exc.Length} sub-exception{(Exc.Length != 1 ? "s" : "")})");

        public override string ToString() => AsString().Value;

        /// <summary>
        /// BaseExceptionGroup_derive: CPython always builds a BaseExceptionGroup with
        /// (self.message, excs) - which then upgrades itself to ExceptionGroup when every member
        /// is an Exception subclass. So derive of an ExceptionGroup yields an ExceptionGroup, and
        /// derive of a BaseExceptionGroup holding KeyboardInterrupt stays a BaseExceptionGroup.
        /// </summary>
        private PyObject DeriveImpl(PyObject[] args)
        {
            // PyArg_ParseTuple(args, "O", &excs) - the format has no function name, so CPython
            // reports the anonymous wording. Measured.
            if (args.Length != 1)
                throw PyTypeError.Create(
                    $"function takes exactly 1 argument ({args.Length} given)");
            return NewGroup(new PyObject[] { Msg, args[0] }, PyType.BaseExceptionGroupType);
        }

        private PyObject SplitImpl(PyObject[] args)
        {
            // PyArg_UnpackTuple(args, "split", 1, 1, &matcher) - "split expected 1 argument, got 0".
            if (args.Length != 1)
                throw PyTypeError.Create($"split expected 1 argument, got {args.Length}");
            Matcher matcher = Matcher.From(args[0]);
            SplitResult matched = SplitRecursive(Owner ?? this, matcher, constructRest: true);
            return new PyTuple(new PyObject[]
            {
                matched.Match ?? PyNone.Instance,
                matched.Rest ?? PyNone.Instance,
            });
        }

        private PyObject SubgroupImpl(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"subgroup expected 1 argument, got {args.Length}");
            Matcher matcher = Matcher.From(args[0]);
            SplitResult matched = SplitRecursive(Owner ?? this, matcher, constructRest: false);
            return matched.Match ?? PyNone.Instance;
        }

        /// <summary>
        /// exceptiongroup_subset: wrap exs in a group derived from this one, or None when exs is
        /// empty. Metadata travels with it: traceback, __context__, __cause__.
        /// </summary>
        private PyObject Subset(PyObject[] exs)
        {
            if (exs.Length == 0) return null;

            PyObject original = Owner ?? this;
            PyObject derived = original.GetAttribute("derive").Call(new PyObject[] { new PyList(exs) }, null);
            if (Of(derived) == null)
                throw PyTypeError.Create("derive must return an instance of BaseExceptionGroup");

            foreach (var field in new[] { "__traceback__", "__context__", "__cause__", "__suppress_context__" })
                derived.SetAttribute(field, original.GetAttribute(field));
            // Each part gets its own list of notes, and only a sequence is copied.
            var notes = NotesOf(original);
            if (notes != null && (notes is PyList or PyTuple || notes.HasTypeSlot("__getitem__")) && notes is not PyDict)
                derived.SetAttribute("__notes__", new PyList(PySet.IterableItems(notes)));
            return derived;
        }

        private static PyObject NotesOf(PyObject exception)
        {
            try
            {
                return exception.GetAttribute("__notes__");
            }
            catch (PythonException error) when (error.PyException is PyAttributeError)
            {
                return null;
            }
        }

        /// <summary>
        /// exceptiongroup_split_recursive. Full match -> the exception itself. Leaf with no match
        /// -> the exception in the rest part (only when the caller wants a rest part). Otherwise
        /// this is a group, so recurse into the members and rebuild both halves through subset().
        /// </summary>
        private static SplitResult SplitRecursive(PyObject exc, Matcher matcher, bool constructRest, int depth = 0)
        {
            // Py_EnterRecursiveCall, C_RECURSION_LIMIT
            if (depth >= 3000)
                throw new PythonException(new PyRecursionError("maximum recursion depth exceeded in exceptiongroup_split_recursive"));
            var result = new SplitResult();

            if (matcher.Matches(exc))
            {
                result.Match = exc;
                return result;
            }

            var group = Of(exc);
            if (group == null)
            {
                if (constructRest) result.Rest = exc;
                return result;
            }

            var matchList = new List<PyObject>();
            var restList = constructRest ? new List<PyObject>() : null;

            for (int i = 0; i < group.Exc.Length; i++)
            {
                SplitResult inner = SplitRecursive(group.Exc[i], matcher, constructRest, depth + 1);
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
            PyObject matched = SplitRecursive(Owner ?? this, Matcher.ByLeafId(leafIds),
                                              constructRest: false).Match;
            return matched ?? PyNone.Instance;
        }

        /// <summary>collect_exception_group_leaf_ids.</summary>
        private static void CollectLeafIds(PyObject exc, HashSet<PyObject> leafIds)
        {
            if (exc is PyNone) return;
            var group = Of(exc);
            if (group == null)
            {
                leafIds.Add(exc);
                return;
            }
            for (int i = 0; i < group.Exc.Length; i++) CollectLeafIds(group.Exc[i], leafIds);
        }

        /// <summary>
        /// is_same_exception_metadata: two exceptions are the same reraise of one original when
        /// notes, traceback, cause and context are the very same objects.
        /// </summary>
        internal static bool SameMetadata(PyObject a, PyObject b)
        {
            if (!(PyExceptionSlots.ToTransport(a) is PyBaseException x) ||
                !(PyExceptionSlots.ToTransport(b) is PyBaseException y)) return false;
            return ReferenceEquals(NotesOf(a), NotesOf(b)) &&
                   ReferenceEquals(x.__traceback__, y.__traceback__) &&
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
            => NewGroup(new PyObject[] { new PyString(message), sequence }, PyType.BaseExceptionGroupType);

        /// <summary>BaseExceptionGroup_members and BaseExceptionGroup_methods (METH_VARARGS).</summary>
        internal static void InstallOn(PyType type)
        {
            type.TypeDict["message"] = new PyMemberDescriptor("message", type, self => Of(self).Msg, readOnly: true);
            type.TypeDict["exceptions"] = new PyMemberDescriptor("exceptions", type, self => Of(self).Excs, readOnly: true);
            type.TypeDict["derive"] = new PyMethodDescriptor("derive", type, (self, args, kwargs) => Of(self).DeriveImpl(args));
            type.TypeDict["split"] = new PyMethodDescriptor("split", type, (self, args, kwargs) => Of(self).SplitImpl(args));
            type.TypeDict["subgroup"] = new PyMethodDescriptor("subgroup", type, (self, args, kwargs) => Of(self).SubgroupImpl(args));
        }

        /// <summary>BaseExceptionGroup_new for a Python subclass: the struct behind the instance.</summary>
        internal static void FillStruct(PyExceptionInstance instance, PyObject[] args)
        {
            var layout = (PyBaseExceptionGroup)NewGroup(args, instance.InstanceType);
            layout.Owner = instance;
            instance.Layout = layout;
        }

        /// <summary>
        /// BaseExceptionGroup_new: the class asked for decides what may nest, and
        /// BaseExceptionGroup itself becomes ExceptionGroup when every member is an Exception.
        /// </summary>
        internal static PyObject NewGroup(PyObject[] callArgs, PyType cls)
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
                if (!(PyExceptionSlots.ToTransport(items[i]) is PyBaseException))
                    throw PyValueError.Create(
                        $"Item {i} of second argument (exceptions) is not an exception");
                if (!items[i].GetPyType().IsSubclassOf(PyType.ExceptionType))
                    nestedBaseExceptions = true;
            }

            // args is stored as given, so repr() and .args show the sequence object the caller
            // passed, exactly as CPython's BaseException_init does.
            bool isException = cls.IsSubclassOf(PyType.ExceptionType);
            if (ReferenceEquals(cls, PyType.ExceptionGroupType))
            {
                if (nestedBaseExceptions)
                    throw PyTypeError.Create("Cannot nest BaseExceptions in an ExceptionGroup");
            }
            else if (ReferenceEquals(cls, PyType.BaseExceptionGroupType))
            {
                // All nested exceptions are Exception subclasses: an ExceptionGroup.
                isException = !nestedBaseExceptions;
            }
            else if (nestedBaseExceptions && isException)
            {
                throw PyTypeError.Create($"Cannot nest BaseExceptions in '{cls.Name}'");
            }
            return isException ? new PyExceptionGroup(callArgs, items) : new PyBaseExceptionGroup(callArgs, items);
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
            // A str argument contributes characters, not UTF-16 units.
            if (value is PyString text) return PyString.Characters(text.Value);
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
                    return Of(exc) == null && _leafIds.Contains(exc);
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
