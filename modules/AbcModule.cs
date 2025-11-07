using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython 3.12 _abc module - C# implementation of ABC (Abstract Base Class) support
    /// Corresponds to CPython's Modules/_abc.c
    /// Purpose: Provides core ABC functionality outside Python object model to avoid circular dependencies
    /// </summary>
    public static class AbcModule
    {
        // Global cache invalidation counter (corresponds to abc_invalidation_counter in C)
        private static ulong _cacheInvalidationCounter = 0;

        /// <summary>
        /// Create the _abc module
        /// CPython: Modules/_abc.c:813-888
        /// </summary>
        public static PyModule CreateAbcModule()
        {
            var module = new PyModule("_abc", "Module contains faster C# implementation of abc.ABCMeta");

            // Core ABC functions
            module.ModuleDict["_abc_init"] = new PyBuiltinFunction("_abc_init", AbcInit);
            module.ModuleDict["_abc_register"] = new PyBuiltinFunction("_abc_register", AbcRegister);
            module.ModuleDict["_abc_instancecheck"] = new PyBuiltinFunction("_abc_instancecheck", AbcInstanceCheck);
            module.ModuleDict["_abc_subclasscheck"] = new PyBuiltinFunction("_abc_subclasscheck", AbcSubclassCheck);
            module.ModuleDict["get_cache_token"] = new PyBuiltinFunction("get_cache_token", GetCacheToken);
            module.ModuleDict["_get_dump"] = new PyBuiltinFunction("_get_dump", GetDump);
            module.ModuleDict["_reset_registry"] = new PyBuiltinFunction("_reset_registry", ResetRegistry);
            module.ModuleDict["_reset_caches"] = new PyBuiltinFunction("_reset_caches", ResetCaches);

            return module;
        }

        /// <summary>
        /// Internal ABC helper for class set-up. Should be never used outside abc module.
        /// CPython: Modules/_abc.c:431-482 (_abc__abc_init)
        /// </summary>
        private static PyObject AbcInit(PyObject[] args, PyDict kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"_abc_init() takes exactly 1 argument ({args.Length} given)");

            var cls = args[0];

            // Compute and set __abstractmethods__
            if (!ComputeAbstractMethods(cls))
                throw PyRuntimeError.Create("Failed to compute abstract methods");

            // Create and attach _abc_impl data structure
            var abcData = new AbcData();
            try
            {
                cls.SetAttribute("_abc_impl", abcData);
            }
            catch (Exception ex)
            {
                throw PyRuntimeError.Create($"Failed to set _abc_impl: {ex.Message}");
            }

            return PyNone.Instance;
        }

        /// <summary>
        /// Internal ABC helper for registering virtual subclasses
        /// CPython: Modules/_abc.c:520-567 (_abc__abc_register_impl)
        /// </summary>
        private static PyObject AbcRegister(PyObject[] args, PyDict kwargs)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"_abc_register() takes exactly 2 arguments ({args.Length} given)");

            var cls = args[0];
            var subclass = args[1];

            // Get _abc_impl data
            var abcData = GetAbcData(cls);
            if (abcData == null)
                throw PyTypeError.Create("_abc_impl is not set or is set to a wrong type");

            // Check if subclass is a class
            if (!(subclass is PyClass))
                throw PyTypeError.Create("Can only register classes");

            // Add to registry
            if (abcData.Registry == null)
                abcData.Registry = new PySet();

            abcData.Registry.Add(subclass);

            // Invalidate caches
            _cacheInvalidationCounter++;

            return subclass;
        }

        /// <summary>
        /// Internal ABC helper for instance checks
        /// CPython: Modules/_abc.c:580-653 (_abc__abc_instancecheck_impl)
        /// </summary>
        private static PyObject AbcInstanceCheck(PyObject[] args, PyDict kwargs)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"_abc_instancecheck() takes exactly 2 arguments ({args.Length} given)");

            var cls = args[0];
            var instance = args[1];

            // Get _abc_impl data
            var abcData = GetAbcData(cls);
            if (abcData == null)
                throw PyTypeError.Create("_abc_impl is not set or is set to a wrong type");

            // Get instance type
            var instanceType = instance.GetPyType();

            // Check cache first
            if (abcData.Cache != null && abcData.Cache.Contains(instanceType).Value)
                return PyBool.True;

            // Check negative cache
            if (abcData.NegativeCache != null &&
                abcData.NegativeCacheVersion == _cacheInvalidationCounter &&
                abcData.NegativeCache.Contains(instanceType).Value)
                return PyBool.False;

            // Check if instance type is subclass of cls
            var result = AbcSubclassCheckInternal(cls, instanceType, abcData);

            if (result)
            {
                // Add to cache
                if (abcData.Cache == null)
                    abcData.Cache = new PySet();
                abcData.Cache.Add(instanceType);
            }
            else
            {
                // Add to negative cache
                if (abcData.NegativeCache == null)
                {
                    abcData.NegativeCache = new PySet();
                    abcData.NegativeCacheVersion = _cacheInvalidationCounter;
                }
                abcData.NegativeCache.Add(instanceType);
            }

            return PyBool.FromBool(result);
        }

        /// <summary>
        /// Internal ABC helper for subclass checks
        /// CPython: Modules/_abc.c:666-758 (_abc__abc_subclasscheck_impl)
        /// </summary>
        private static PyObject AbcSubclassCheck(PyObject[] args, PyDict kwargs)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"_abc_subclasscheck() takes exactly 2 arguments ({args.Length} given)");

            var cls = args[0];
            var subclass = args[1];

            // Check if subclass is a class
            if (!(subclass is PyClass))
                throw PyTypeError.Create("issubclass() arg 1 must be a class");

            // Get _abc_impl data
            var abcData = GetAbcData(cls);
            if (abcData == null)
                throw PyTypeError.Create("_abc_impl is not set or is set to a wrong type");

            var result = AbcSubclassCheckInternal(cls, subclass, abcData);
            return PyBool.FromBool(result);
        }

        /// <summary>
        /// Get cache token for invalidation tracking
        /// CPython: Modules/_abc.c:760-767 (_abc_get_cache_token)
        /// </summary>
        private static PyObject GetCacheToken(PyObject[] args, PyDict kwargs)
        {
            return new PyInt((long)_cacheInvalidationCounter);
        }

        /// <summary>
        /// Get debug dump of ABC data (for testing)
        /// CPython: Modules/_abc.c:769-811 (_abc__get_dump)
        /// </summary>
        private static PyObject GetDump(PyObject[] args, PyDict kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"_get_dump() takes exactly 1 argument ({args.Length} given)");

            var cls = args[0];
            var abcData = GetAbcData(cls);
            if (abcData == null)
                return PyNone.Instance;

            var result = new PyDict();
            result.SetItem(new PyString("_abc_registry"), (PyObject)abcData.Registry ?? PyNone.Instance);
            result.SetItem(new PyString("_abc_cache"), (PyObject)abcData.Cache ?? PyNone.Instance);
            result.SetItem(new PyString("_abc_negative_cache"), (PyObject)abcData.NegativeCache ?? PyNone.Instance);
            result.SetItem(new PyString("_abc_negative_cache_version"), new PyInt((long)abcData.NegativeCacheVersion));

            return result;
        }

        /// <summary>
        /// Reset registry (for testing)
        /// CPython: Modules/_abc.c:217-236 (_abc__reset_registry)
        /// </summary>
        private static PyObject ResetRegistry(PyObject[] args, PyDict kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"_reset_registry() takes exactly 1 argument ({args.Length} given)");

            var cls = args[0];
            var abcData = GetAbcData(cls);
            if (abcData != null)
            {
                abcData.Registry = null;
                _cacheInvalidationCounter++;
            }

            return PyNone.Instance;
        }

        /// <summary>
        /// Reset caches (for testing)
        /// CPython: Modules/_abc.c:239-262 (_abc__reset_caches)
        /// </summary>
        private static PyObject ResetCaches(PyObject[] args, PyDict kwargs)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"_reset_caches() takes exactly 1 argument ({args.Length} given)");

            var cls = args[0];
            var abcData = GetAbcData(cls);
            if (abcData != null)
            {
                abcData.Cache = null;
                abcData.NegativeCache = null;
                abcData.NegativeCacheVersion = _cacheInvalidationCounter;
            }

            return PyNone.Instance;
        }

        // ============ Helper Methods ============

        /// <summary>
        /// Compute and set __abstractmethods__ for a class
        /// CPython: Modules/_abc.c:264-420 (compute_abstract_methods)
        /// </summary>
        private static bool ComputeAbstractMethods(PyObject cls)
        {
            try
            {
                var abstractMethods = new PySet();

                // Get class dict
                var classDict = GetClassDict(cls);
                if (classDict == null)
                    return false;

                // Iterate through class dict to find abstract methods
                foreach (var kvp in classDict)
                {
                    var name = kvp.Key;
                    var value = kvp.Value;

                    // Check if method has __isabstractmethod__ attribute set to True
                    try
                    {
                        var isAbstract = value.GetAttribute("__isabstractmethod__");
                        if (isAbstract != null && isAbstract.PyBoolValue())
                        {
                            abstractMethods.Add(new PyString(name));
                        }
                    }
                    catch
                    {
                        // No __isabstractmethod__ attribute, skip
                        continue;
                    }
                }

                // Also collect abstract methods from base classes
                var bases = GetBases(cls);
                if (bases != null)
                {
                    foreach (var baseCls in bases)
                    {
                        try
                        {
                            var baseAbstractMethods = baseCls.GetAttribute("__abstractmethods__");
                            if (baseAbstractMethods != null && baseAbstractMethods != PyNone.Instance)
                            {
                                // Add methods from base that are still abstract in this class
                                var iterator = baseAbstractMethods.GetIterator();
                                while (true)
                                {
                                    try
                                    {
                                        var methodName = iterator.Next();
                                        var methodNameStr = ((PyString)methodName).Value;

                                        // Check if overridden in current class or inherited
                                        // Use GetAttribute to check MRO, not just ClassDict
                                        try
                                        {
                                            var method = cls.GetAttribute(methodNameStr);
                                            if (method != null && method != PyNone.Instance)
                                            {
                                                // Method exists, check if still abstract
                                                try
                                                {
                                                    var isStillAbstract = method.GetAttribute("__isabstractmethod__");
                                                    if (isStillAbstract != null && isStillAbstract.PyBoolValue())
                                                    {
                                                        // Still abstract
                                                        abstractMethods.Add(methodName);
                                                    }
                                                    // else: Concrete implementation found, not abstract
                                                }
                                                catch
                                                {
                                                    // No __isabstractmethod__, so it's concrete
                                                }
                                            }
                                            else
                                            {
                                                // Method not found, still abstract
                                                abstractMethods.Add(methodName);
                                            }
                                        }
                                        catch
                                        {
                                            // Error getting attribute, assume still abstract
                                            abstractMethods.Add(methodName);
                                        }
                                    }
                                    catch (System.Exception ex) when (ex is PyStopIteration)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Base class doesn't have __abstractmethods__
                            continue;
                        }
                    }
                }

                // Create frozenset from abstract methods
                var frozenAbstractMethods = new PyFrozenSet(abstractMethods.Items);
                cls.SetAttribute("__abstractmethods__", frozenAbstractMethods);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Internal subclass check logic
        /// CPython: Modules/_abc.c:666-758 (_abc__abc_subclasscheck_impl)
        /// </summary>
        private static bool AbcSubclassCheckInternal(PyObject cls, PyObject subclass, AbcData abcData)
        {
#if DEBUG
            Console.WriteLine($"[AbcSubclassCheckInternal] Checking if {subclass} is subclass of {cls}");
#endif

            // Check registry first
            if (abcData.Registry != null && abcData.Registry.Contains(subclass).Value)
            {
#if DEBUG
                Console.WriteLine($"[AbcSubclassCheckInternal] Found in registry");
#endif
                return true;
            }

            // Check if subclass is a direct or indirect subclass using MRO
            try
            {
                // Use Python's issubclass logic
                // CPython: Modules/_abc.c lines 719-735
                // CPython uses pointer comparison: for (i = 0; i < n; i++) { if (PyTuple_GET_ITEM(mro, i) == cls)
                var mro = GetMRO(subclass);
#if DEBUG
                Console.WriteLine($"[AbcSubclassCheckInternal] MRO count: {mro?.Count ?? 0}");
#endif
                if (mro != null)
                {
                    for (int i = 0; i < mro.Count; i++)
                    {
                        var baseClass = mro[i];
#if DEBUG
                        Console.WriteLine($"  [AbcSubclassCheckInternal] MRO[{i}]: {baseClass}, ReferenceEquals={ReferenceEquals(baseClass, cls)}");
#endif
                        // Use ReferenceEquals for pointer comparison like CPython
                        // This is critical for ABC metaclass checks to work correctly
                        if (ReferenceEquals(baseClass, cls))
                        {
#if DEBUG
                            Console.WriteLine($"[AbcSubclassCheckInternal] MATCH! Returning true");
#endif
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine($"[AbcSubclassCheckInternal] Exception: {ex.Message}");
#endif
                // MRO not available or error
            }

#if DEBUG
            Console.WriteLine($"[AbcSubclassCheckInternal] No match, returning false");
#endif
            return false;
        }

        /// <summary>
        /// Get _abc_impl data from class
        /// CPython: Modules/_abc.c:114-129 (abc_data_of)
        /// </summary>
        private static AbcData GetAbcData(PyObject cls)
        {
            try
            {
                var impl = cls.GetAttribute("_abc_impl");
                return impl as AbcData;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get class dictionary
        /// </summary>
        private static Dictionary<string, PyObject> GetClassDict(PyObject cls)
        {
            if (cls is PyClass pyClass)
                return pyClass.ClassDict;
            return null;
        }

        /// <summary>
        /// Get base classes
        /// </summary>
        private static List<PyObject> GetBases(PyObject cls)
        {
            try
            {
                var bases = cls.GetAttribute("__bases__");
                if (bases is PyTuple tuple)
                {
                    var result = new List<PyObject>();
                    foreach (var item in tuple.Items)
                        result.Add(item);
                    return result;
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// Get MRO (Method Resolution Order)
        /// </summary>
        private static List<PyObject> GetMRO(PyObject cls)
        {
            try
            {
                var mro = cls.GetAttribute("__mro__");
                if (mro is PyTuple tuple)
                {
                    var result = new List<PyObject>();
                    foreach (var item in tuple.Items)
                        result.Add(item);
                    return result;
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// Internal data structure for ABC state
        /// Corresponds to _abc_data in CPython (Modules/_abc.c:37-43)
        /// </summary>
        private class AbcData : PyObject
        {
            public PySet Registry { get; set; }              // _abc_registry
            public PySet Cache { get; set; }                 // _abc_cache
            public PySet NegativeCache { get; set; }         // _abc_negative_cache
            public ulong NegativeCacheVersion { get; set; }  // _abc_negative_cache_version

            public AbcData()
            {
                Registry = null;
                Cache = null;
                NegativeCache = null;
                NegativeCacheVersion = _cacheInvalidationCounter;
            }

            public override PyType GetPyType() => PyType.ObjectType;
            public override string ToString() => "<_abc_data>";
        }
    }
}
