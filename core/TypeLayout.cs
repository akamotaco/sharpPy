using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// What tp_basicsize, tp_itemsize, tp_dictoffset and tp_weaklistoffset say about a type's
    /// instances. Nothing is laid out by these numbers here: they are the input of the decisions
    /// CPython takes from them (best_base, type_new_slots, compatible_for_assignment).
    /// Slots is ht_slots, null for a type without __slots__. NoInstances is DISALLOW_INSTANTIATION.
    /// </summary>
    internal sealed record TypeLayout(int BasicSize, int ItemSize, int DictOffset, int WeakOffset, bool Gc,
                                      string[] Slots = null, bool NoInstances = false, bool Final = false,
                                      bool Spec = false);

    internal static partial class TypeLayouts
    {
        // A type of SharpPy's own that CPython does not have adds to its base, like any C type with fields.
        // The reference interpreter's numbers for a builtin of this name, if it has one.
        internal static bool Measured(string name, out TypeLayout layout) => Table.TryGetValue(name, out layout);

        internal static TypeLayout Of(PyType type)
        {
            if (Table.TryGetValue(type.Name, out var layout))
                return layout;
            var parent = type.BaseTypes.FirstOrDefault()?.Layout ?? Table["object"];
            return parent with { BasicSize = parent.BasicSize + 16, NoInstances = false, Final = false, Spec = false };
        }
    }
}
