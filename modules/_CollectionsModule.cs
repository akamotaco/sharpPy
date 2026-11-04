using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython _collections C 확장 모듈
    /// CPython 3.12 호환 구현
    /// </summary>
    /// <summary>defaultdict is defined in collections/__init__.py here; _collections answers with that class.</summary>
    internal sealed class CollectionsNativeModule : PyModule
    {
        internal CollectionsNativeModule(string name, string doc) : base(name, doc) { }

        public override PyObject GetAttribute(string name) => name == "defaultdict"
            ? PyImportSystem.Import("collections").GetAttribute(name) : base.GetAttribute(name);
    }

    public static class _CollectionsModule
    {
        public static PyModule CreateCollectionsModule()
        {
            var module = new CollectionsNativeModule("_collections", "High performance container datatypes");

            // CPython 3.12: Core C extension types
            module.ModuleDict["deque"] = PyDequeModule.DequeType;
            module.ModuleDict["_deque_iterator"] = PyDequeModule.DequeIterType;
            module.ModuleDict["_deque_reverse_iterator"] = PyDequeModule.DequeRevIterType;

            return module;
        }
    }
}
