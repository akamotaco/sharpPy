using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// lru_list_elem (Modules/_functoolsmodule.c:770): a link of the bounded cache's list and the
    /// value the cache dict holds for its key. functools._lru_list_elem cannot be instantiated.
    /// </summary>
    internal sealed class PyLruListElem : PyObject
    {
        internal static readonly PyType ElemType = MakeType();

        internal PyLruListElem Prev, Next;
        internal long Hash;
        internal PyObject Key, Result;

        private static PyType MakeType()
        {
            var type = new PyType("_lru_list_elem", new[] { PyType.ObjectType }, "functools");
            // Py_TPFLAGS_DISALLOW_INSTANTIATION: tp_new stays NULL.
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => throw PyTypeError.Create("cannot create 'functools._lru_list_elem' instances"),
                0, int.MaxValue, acceptsKwargs: true);
            return type;
        }

        public override PyType GetPyType() => ElemType;
    }

    /// <summary>
    /// lru_cache_object (Modules/_functoolsmodule.c:803): functools._lru_cache_wrapper, the C
    /// lru_cache that functools.lru_cache takes over its pure-Python one. root is the list's
    /// sentinel; the wrapper is chosen by maxsize once, in lru_cache_new.
    /// </summary>
    public sealed class PyLruCacheWrapper : PyObject, IObjectDict, IDescriptor
    {
        internal static readonly PyType LruCacheType = MakeType();
        // state->kwd_mark: an object() that separates the positional arguments from the keywords.
        private static readonly PyObject KwdMark = PyType.ObjectType.Call(Array.Empty<PyObject>(), null);

        private enum Kind { Uncached, Infinite, Bounded }

        private readonly PyLruListElem _root = new PyLruListElem();
        private readonly Kind _kind;
        private readonly bool _typed;
        private readonly PyDict _cache = new PyDict();
        private long _hits;
        private readonly PyObject _func;
        private readonly long _maxsize;
        private long _misses;
        private readonly PyObject _cacheInfoType;
        private PyDict _dict;

        public PyDict ObjectDict(bool create) => create ? _dict ??= new PyDict() : _dict;
        public void SetObjectDict(PyDict dict) => _dict = dict;

        private PyLruCacheWrapper(PyObject func, Kind kind, long maxsize, bool typed, PyObject cacheInfoType)
        {
            _root.Prev = _root.Next = _root;
            _func = func;
            _kind = kind;
            _maxsize = maxsize;
            _typed = typed;
            _cacheInfoType = cacheInfoType;
        }

        public override PyType GetPyType() => LruCacheType;
        public override bool IsCallable() => true;

        private static PyType MakeType()
        {
            var type = new PyType("_lru_cache_wrapper", new[] { PyType.ObjectType }, "functools");
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => New(args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            type.TypeDict["__get__"] = new PyWrapperDescriptor("__get__", type, (self, args, kwargs) =>
            {
                var (target, owner) = TypeTables.DescrGetArguments(args);
                return ((PyLruCacheWrapper)self).Get(target, owner as PyType);
            }, 1, 2);
            TypeTables.GenericAttributes(type);
            TypeTables.GenericSetAttributes(type);
            // lru_cache_methods
            type.TypeDict["cache_info"] = new PyMethodDescriptor("cache_info", type,
                (self, args, kwargs) => ((PyLruCacheWrapper)self).CacheInfo(), 0, 0);
            type.TypeDict["cache_clear"] = new PyMethodDescriptor("cache_clear", type,
                (self, args, kwargs) => ((PyLruCacheWrapper)self).CacheClear(), 0, 0);
            // lru_cache_reduce: the __qualname__ the instance's dict holds, so pickle finds it by name.
            type.TypeDict["__reduce__"] = new PyMethodDescriptor("__reduce__", type,
                (self, args, kwargs) => self.GetAttribute("__qualname__"), 0, 0);
            // __copy__ and __deepcopy__: METH_VARARGS, the object itself.
            type.TypeDict["__copy__"] = new PyMethodDescriptor("__copy__", type,
                (self, args, kwargs) => { PyArgs.NoKeywords("__copy__", kwargs); return self; }, 0, int.MaxValue);
            type.TypeDict["__deepcopy__"] = new PyMethodDescriptor("__deepcopy__", type,
                (self, args, kwargs) => { PyArgs.NoKeywords("__deepcopy__", kwargs); return self; }, 0, int.MaxValue);
            // lru_cache_memberlist's __dictoffset__ and __weaklistoffset__ are read by PyType_FromSpec,
            // not added to the dict; lru_cache_getsetlist:
            type.TypeDict["__dict__"] = SubclassInstanceSupport.GenericDictGetSet(type);
            return type;
        }

        // lru_cache_new: "OOpO:lru_cache" over user_function, maxsize, typed, cache_info_type.
        private static PyObject New(PyObject[] args, PyDict kwargs)
        {
            var parsed = PyArgs.Unpack("lru_cache", args, kwargs,
                new[] { "user_function", "maxsize", "typed", "cache_info_type" }, 0, 4, 4);
            var func = parsed[0];
            var maxsizeObject = parsed[1];
            bool typed = parsed[2].IsTrue();
            if (!func.IsCallable())
                throw PyTypeError.Create("the first argument must be callable");
            Kind kind;
            long maxsize;
            if (maxsizeObject is PyNone)
            {
                kind = Kind.Infinite;
                maxsize = -1;
            }
            else if (PyObject.IndexCheck(maxsizeObject))
            {
                maxsize = PyArgs.AsSsize(maxsizeObject, PyArgs.SsizeError.Overflow);
                if (maxsize < 0)
                    maxsize = 0;
                kind = maxsize == 0 ? Kind.Uncached : Kind.Bounded;
            }
            else
            {
                throw PyTypeError.Create("maxsize should be integer or None");
            }
            return new PyLruCacheWrapper(func, kind, maxsize, typed, parsed[3]);
        }

        // lru_cache_descr_get
        public PyObject Get(PyObject instance, PyType owner) =>
            instance == null || instance is PyNone ? this : new PyMethod(instance, this);

        public void Set(PyObject instance, PyObject value) => throw PyAttributeError.Create("__set__");

        public void Delete(PyObject instance) => throw PyAttributeError.Create("__delete__");

        public bool IsDataDescriptor() => false;

        // lru_cache_call
        public override PyObject Call(PyObject[] args, PyDict kwargs = null) => _kind switch
        {
            Kind.Uncached => Uncached(args, kwargs),
            Kind.Infinite => Infinite(args, kwargs),
            _ => Bounded(args, kwargs),
        };

        // lru_cache_make_key
        private PyObject MakeKey(PyObject[] args, PyDict kwds)
        {
            int kwdsSize = kwds?.Length() ?? 0;
            if (!_typed && kwdsSize == 0)
            {
                // common scalar keys drop the enclosing args tuple
                if (args.Length == 1 && args[0] is PyString { IsExact: true } or PyInt { IsExact: true })
                    return args[0];
                return PyTuple.From(args);
            }
            int keySize = args.Length;
            if (kwdsSize != 0)
                keySize += kwdsSize * 2 + 1;
            if (_typed)
                keySize += args.Length + kwdsSize;
            var key = new PyObject[keySize];
            int keyPos = 0;
            foreach (var item in args)
                key[keyPos++] = item;
            var pairs = kwdsSize != 0 ? new List<KeyValuePair<PyObject, PyObject>>(kwds.GetInternalDict()) : null;
            if (pairs != null)
            {
                key[keyPos++] = KwdMark;
                foreach (var (keyword, value) in pairs)
                {
                    key[keyPos++] = keyword;
                    key[keyPos++] = value;
                }
            }
            if (_typed)
            {
                foreach (var item in args)
                    key[keyPos++] = item.GetPyType();
                if (pairs != null)
                    foreach (var (_, value) in pairs)
                        key[keyPos++] = value.GetPyType();
            }
            return PyTuple.From(key);
        }

        // uncached_lru_cache_wrapper
        private PyObject Uncached(PyObject[] args, PyDict kwds)
        {
            _misses++;
            return _func.Call(args, kwds);
        }

        // infinite_lru_cache_wrapper
        private PyObject Infinite(PyObject[] args, PyDict kwds)
        {
            var key = MakeKey(args, kwds);
            long hash = key.GetPythonHash();
            if (_cache.InternalDict.TryGetKnownHash(key, hash, out var cached))
            {
                _hits++;
                return cached;
            }
            _misses++;
            var result = _func.Call(args, kwds);
            _cache.SetKnownHash(key, result, hash);
            return result;
        }

        // lru_cache_extract_link
        private static void ExtractLink(PyLruListElem link)
        {
            link.Prev.Next = link.Next;
            link.Next.Prev = link.Prev;
        }

        // lru_cache_append_link
        private void AppendLink(PyLruListElem link)
        {
            var last = _root.Prev;
            last.Next = _root.Prev = link;
            link.Prev = last;
            link.Next = _root;
        }

        // lru_cache_prepend_link
        private void PrependLink(PyLruListElem link)
        {
            var first = _root.Next;
            first.Prev = _root.Next = link;
            link.Prev = _root;
            link.Next = first;
        }

        // bounded_lru_cache_wrapper
        private PyObject Bounded(PyObject[] args, PyDict kwds)
        {
            var key = MakeKey(args, kwds);
            long hash = key.GetPythonHash();
            if (_cache.InternalDict.TryGetKnownHash(key, hash, out var found))
            {
                var hit = (PyLruListElem)found;
                ExtractLink(hit);
                AppendLink(hit);
                _hits++;
                return hit.Result;
            }
            _misses++;
            var result = _func.Call(args, kwds);
            // The same key added during the call: the link update is done already.
            if (_cache.InternalDict.TryGetKnownHash(key, hash, out _))
                return result;
            PyLruListElem link;
            if (_cache.Length() < _maxsize || ReferenceEquals(_root.Next, _root))
            {
                // not full: a new link
                link = new PyLruListElem { Hash = hash, Key = key, Result = result };
                _cache.SetKnownHash(key, link, hash);
                AppendLink(link);
                return result;
            }
            // full: the oldest link is reused for the new key and moved to the front
            link = _root.Next;
            ExtractLink(link);
            bool popped;
            try
            {
                popped = _cache.InternalDict.RemoveKnownHash(link.Key, link.Hash, out _);
            }
            catch (PythonException)
            {
                // restored as the oldest link, the error raised as the user function's would be
                PrependLink(link);
                throw;
            }
            // the user function or another thread removed the old key: the link is an orphan
            if (!popped)
                return result;
            link.Hash = hash;
            link.Key = key;
            link.Result = result;
            _cache.SetKnownHash(key, link, hash);
            AppendLink(link);
            return result;
        }

        // _functools__lru_cache_wrapper_cache_info_impl
        private PyObject CacheInfo() => _cacheInfoType.Call(new PyObject[]
        {
            PyInt.From(_hits), PyInt.From(_misses), _maxsize == -1 ? PyNone.Instance : PyInt.From(_maxsize),
            PyInt.From(_cache.Length()),
        }, null);

        // _functools__lru_cache_wrapper_cache_clear_impl
        private PyObject CacheClear()
        {
            _root.Prev = _root.Next = _root;
            _hits = _misses = 0;
            _cache.Clear();
            return PyNone.Instance;
        }
    }
}
