using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// An instance of a user class that derives from <c>list</c>.
    ///
    /// CPython has one object here: <c>list_new</c> allocates the <c>PyListObject</c> layout with
    /// <c>ob_type</c> set to the subclass (Objects/listobject.c <c>list_new</c> →
    /// <c>PyList_New</c> with the requested type), and slot inheritance gives the subclass
    /// <c>list_length</c>, <c>list_repr</c>, <c>list_concat</c> as its own slots. There is no second
    /// object and nothing to look through.
    ///
    /// SharpPy used to model that with a <c>PyClassInstance</c> carrying a separate
    /// <c>PyList</c> storage, which meant every builtin operation had to be taught to unwrap. This
    /// class is the shape CPython describes: the CLR type is the layout, so <c>x is PyList</c> is
    /// what <c>PyList_Check</c> already means, repr/len/iteration/equality/operators arrive by
    /// inheritance exactly as the slots do, and the only thing CLR inheritance cannot see is the
    /// Python-level class dict - handled the way <see cref="PyTupleSubclass"/> and
    /// <see cref="PyBytesSubclass"/> already handle it, through <see cref="SubclassInstanceSupport"/>.
    /// </summary>
    public class PyListSubclass : PyList, IInstanceDictAccessor
    {
        /// <summary>ob_type: the user class, not list.</summary>
        public PyClass InstanceType { get; }

        /// <summary>
        /// The instance __dict__ a heap type adds after the base layout
        /// (Objects/typeobject.c: a subclass of a variable-size base gets __dict__ at an offset).
        /// </summary>
        public Dictionary<string, PyObject> InstanceDict { get; }

        /// <summary>Arguments tp_new received; observable through __getnewargs__ on some types.</summary>
        public PyObject[] ConstructorArgs { get; set; }

        public PyListSubclass(PyClass instanceType, IEnumerable<PyObject> items = null)
            : base(items ?? Array.Empty<PyObject>())
        {
            InstanceType = instanceType;
            InstanceDict = new Dictionary<string, PyObject>();
        }

        public override PyType GetPyType() => InstanceType;

        public override string GetTypeName() => InstanceType.Name;

        /// <summary>
        /// A user __eq__/__lt__/... wins; otherwise the inherited list comparison runs on the
        /// contents, which is what list_richcompare does for any PyList_Check operand
        /// (Objects/listobject.c).
        /// </summary>
        public override PyObject RichCompare(PyObject other, CompareOp op) =>
            TryCompareSpecial(other, op, PyType.ListType) ?? base.RichCompare(other, op);

        // Declared dunders win, otherwise the inherited slot reads the value - the same decision
        // update_one_slot makes once (Objects/typeobject.c). See SubclassInstanceSupport.
        public override PyString ToRepr() => SubclassInstanceSupport.Repr(this, base.ToRepr);

        public override int Length() => SubclassInstanceSupport.Length(this, base.Length);

        public override PyBool Contains(PyObject item) =>
            SubclassInstanceSupport.Contains(this, item, () => base.Contains(item));

        public override PyObject GetIterator() =>
            SubclassInstanceSupport.Iterate(this, base.GetIterator);

        public override bool IsTrue() => SubclassInstanceSupport.IsTrue(this, base.IsTrue);

        public override PyObject GetAttribute(string name) =>
            SubclassInstanceSupport.GetAttribute(this, InstanceType, InstanceDict, name,
                n => base.GetAttribute(n));

        public override void SetAttribute(string name, PyObject value) =>
            SubclassInstanceSupport.SetAttribute(this, InstanceType, InstanceDict, name, value);
    }
}
