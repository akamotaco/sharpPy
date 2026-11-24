using System;
using SharpPy.Modules.Datetime;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_zoneinfo.c (CPython 3.12.15): "C implementation of the zoneinfo module", the
    /// ZoneInfo type over the datetime C API. Every C function is one method below, under its C
    /// name. The module has state (m_size): each import makes a new zoneinfo_state, and with it a
    /// new ZoneInfo type, as PyType_FromModuleAndSpec does in zoneinfomodule_exec.
    ///
    /// Memory management has no counterpart: zoneinfo_traverse, zoneinfo_clear, zoneinfo_dealloc,
    /// xdecref_ttinfo, free_tzrule, strong_cache_node_free, module_traverse, module_clear and
    /// module_free release what the CLR collector reclaims, and the PyMem_Malloc failure paths of
    /// load_data cannot happen (an array allocation that fails is a MemoryError).
    /// </summary>
    internal static class ZoneinfoModule
    {
        /// <summary>_ttinfo: a UTC offset, a DST offset and an abbreviation.</summary>
        internal sealed class TTInfo
        {
            internal PyObject utcoff;
            internal PyObject dstoff;
            internal PyObject tzname;
            internal int utcoff_seconds; // C long: 32 bits on Windows
        }

        /// <summary>_tzrule: the rule after the last transition, from the POSIX TZ string.</summary>
        internal sealed class TzRule
        {
            internal TTInfo std = new TTInfo();
            internal TTInfo dst = new TTInfo();
            internal int dst_diff;
            internal TransitionRuleType start;
            internal TransitionRuleType end;
            internal bool std_only;
        }

        /// <summary>TransitionRuleType: the year_to_timestamp function pointer is the virtual.</summary>
        internal abstract class TransitionRuleType
        {
            internal abstract long year_to_timestamp(int year);
        }

        /// <summary>CalendarRule: Mm.n.d[/time].</summary>
        internal sealed class CalendarRule : TransitionRuleType
        {
            internal byte month;   /* 1 - 12 */
            internal byte week;    /* 1 - 5 */
            internal byte day;     /* 0 - 6 */
            internal short hour;   /* -167 - 167, RFC 8536 §3.3.1 */
            internal sbyte minute; /* signed 2 digits */
            internal sbyte second; /* signed 2 digits */

            internal override long year_to_timestamp(int year) => calendarrule_year_to_timestamp(this, year);
        }

        /// <summary>DayRule: Jn[/time] or n[/time].</summary>
        internal sealed class DayRule : TransitionRuleType
        {
            internal byte julian;  /* 0, 1 */
            internal ushort day;   /* 0 - 365 */
            internal short hour;   /* -167 - 167, RFC 8536 §3.3.1 */
            internal sbyte minute; /* signed 2 digits */
            internal sbyte second; /* signed 2 digits */

            internal override long year_to_timestamp(int year) => dayrule_year_to_timestamp(this, year);
        }

        /// <summary>StrongCacheNode: a node of the LRU list of the strong cache.</summary>
        internal sealed class StrongCacheNode
        {
            internal StrongCacheNode next;
            internal StrongCacheNode prev;
            internal PyObject key;
            internal PyObject zone;
        }

        /// <summary>zoneinfo_state: the module state.</summary>
        internal sealed class ZoneinfoState
        {
            internal PyType ZoneInfoType;

            // Imports
            internal PyObject io_open;
            internal PyObject _tzpath_find_tzfile;
            internal PyObject _common_mod;

            // Caches
            internal PyDict TIMEDELTA_CACHE;
            internal PyObject ZONEINFO_WEAK_CACHE;
            internal StrongCacheNode ZONEINFO_STRONG_CACHE;

            internal TTInfo NO_TTINFO = new TTInfo();
        }

        /// <summary>PyZoneInfo_ZoneInfo: PyDateTime_TZInfo and the zone's tables.</summary>
        internal sealed class PyZoneInfo : PyDateTimeTZInfo
        {
            internal readonly ZoneinfoState State;
            internal PyObject key;
            internal PyObject file_repr;
            internal int num_transitions;
            internal int num_ttinfos;
            internal long[] trans_list_utc;
            internal long[][] trans_list_wall = new long[2][];
            internal TTInfo[] trans_ttinfos; // References to the ttinfo for each transition
            internal TTInfo ttinfo_before;
            internal TzRule tzrule_after = new TzRule();
            internal TTInfo[] _ttinfos; // Unique array of ttinfos
            internal bool fixed_offset;
            internal int source;

            // type->tp_alloc(type, 0): the ZoneInfo layout with its class.
            internal PyZoneInfo(ZoneinfoState state, PyType type)
            {
                State = state;
                if (!ReferenceEquals(type, state.ZoneInfoType))
                    SetCustomType(type);
            }

            protected override PyType DefaultType => State.ZoneInfoType;

            protected internal override PyObject Repr() => zoneinfo_repr(this);
            protected internal override PyObject Str() => zoneinfo_str(this);
        }

        // Constants
        private const int EPOCHORDINAL = 719163;
        private static readonly int[] DAYS_IN_MONTH = { -1, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        private static readonly int[] DAYS_BEFORE_MONTH = { -1, 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };

        private const int SOURCE_NOCACHE = 0;
        private const int SOURCE_CACHE = 1;
        private const int SOURCE_FILE = 2;

        private const int ZONEINFO_STRONG_CACHE_MAX_SIZE = 8;

        // ---- the C API the module calls -------------------------------------------------------

        // PyObject_CallMethod(obj, name, ...)
        private static PyObject CallMethod(PyObject obj, string name, params PyObject[] args) =>
            obj.GetAttribute(name).Call(args, null);

        // PyDateTime_Check / PyDateTime_CheckExact
        private static bool PyDateTime_Check(PyObject o) => o is PyDateTimeDateTime;
        private static bool PyDateTime_CheckExact(PyObject o) =>
            o is PyDateTimeDateTime && ReferenceEquals(o.GetPyType(), DatetimeModule.DateTimeType);

        // PyTuple_GetItem: a borrowed item, IndexError past the end.
        private static PyObject PyTuple_GetItem(PyObject tuple, int i)
        {
            if (tuple is not PyTuple t)
                throw PySystemError.Create("bad argument to internal function");
            if (i < 0 || i >= t.Items.Length)
                throw PyIndexError.Create("tuple index out of range");
            return t.Items[i];
        }

        // PyTuple_Size
        private static int PyTuple_Size(PyObject tuple)
        {
            if (tuple is not PyTuple t)
                throw PySystemError.Create("bad argument to internal function");
            return t.Items.Length;
        }

        // PyLong_AsLongLong: __index__, then a C long long.
        private static long PyLong_AsLongLong(PyObject num)
        {
            var value = PyObject.IndexOrThrow(num).Value;
            if (value < long.MinValue || value > long.MaxValue)
                throw PyOverflowError.Create("int too big to convert");
            return (long)value;
        }

        // PyLong_AsLong: __index__, then a C long (32 bits on Windows).
        private static int PyLong_AsLong(PyObject num) => (int)PyArgs.CLong(num);

        // PyLong_AsSsize_t: an int (no __index__), then a Py_ssize_t.
        private static long PyLong_AsSsize_t(PyObject num)
        {
            if (num is not PyInt and not PyBool)
                throw PyTypeError.Create("an integer is required");
            var value = PyObject.IndexOrThrow(num).Value;
            if (value < long.MinValue || value > long.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C ssize_t");
            return (long)value;
        }

        // A C function that returned its error value with no exception set; the call that made it
        // reports it (_Py_CheckFunctionResult), naming its callable: see NullChecked.
        private sealed class NullReturn : Exception
        {
        }

        private static Exception NullWithoutError() => new NullReturn();

        // _Py_CheckFunctionResult: "<callable> returned NULL without setting an exception".
        private static PyObject NullChecked(Func<PyObject> callable, Func<PyObject> call)
        {
            try
            {
                return call();
            }
            catch (NullReturn)
            {
                throw PySystemError.Create($"{callable().ToRepr().Value} returned NULL without setting an exception");
            }
        }

        // The bound method a call through the type's dict made.
        private static PyObject BoundMethod(PyType type, string name, PyObject self) =>
            ((IDescriptor)type.TypeDict[name]).Get(self, self.GetPyType());

        // ---- the type ------------------------------------------------------------------------

        // zoneinfo_new_instance
        private static PyObject zoneinfo_new_instance(ZoneinfoState state, PyType type, PyObject key)
        {
            PyObject file_obj = null;
            PyObject file_path = state._tzpath_find_tzfile.Call(new[] { key }, null);
            if (file_path is PyNone)
                file_obj = CallMethod(state._common_mod, "load_tzdata", key);

            PyZoneInfo self = null;
            try
            {
                self = new PyZoneInfo(state, type);

                if (file_obj == null)
                    file_obj = state.io_open.Call(new[] { file_path, PyString.From("rb") }, null);

                load_data(state, self, file_obj);

                var opened = file_obj;
                file_obj = null;
                CallMethod(opened, "close");

                self.key = key;
            }
            catch (PythonException exc)
            {
                // cleanup: the file is closed with the error kept as the close's context.
                if (file_obj != null)
                {
                    try
                    {
                        CallMethod(file_obj, "close");
                    }
                    catch (PythonException tmp)
                    {
                        // _PyErr_ChainExceptions1
                        tmp.PyException.ContextSet(exc.PyException);
                        throw;
                    }
                }
                throw;
            }
            return self;
        }

        // get_weak_cache
        private static PyObject get_weak_cache(ZoneinfoState state, PyType type)
        {
            if (ReferenceEquals(type, state.ZoneInfoType))
                return state.ZONEINFO_WEAK_CACHE;
            return type.GetAttribute("_weak_cache");
        }

        private static readonly string[] zoneinfo_new_kwlist = { "key" };

        // zoneinfo_new
        private static PyObject zoneinfo_new(ZoneinfoState state, PyType type, PyObject[] args, PyDict kw)
        {
            // PyArg_ParseTupleAndKeywords(args, kw, "O", kwlist, &key)
            var key = DatetimeModule.ParseTupleAndKeywords(null, args, kw, zoneinfo_new_kwlist, 1)[0];

            var instance = zone_from_strong_cache(state, type, key);
            if (instance != null)
                return instance;

            var weak_cache = get_weak_cache(state, type);
            // "O" with key and None: only the key is passed.
            instance = CallMethod(weak_cache, "get", key);

            if (instance is PyNone)
            {
                var tmp = zoneinfo_new_instance(state, type, key);
                instance = CallMethod(weak_cache, "setdefault", key, tmp);
                if (instance is PyZoneInfo zone)
                    zone.source = SOURCE_CACHE;
            }

            update_strong_cache(state, type, key, instance);
            return instance;
        }

        private static readonly string[] from_file_keywords = { "", "key" };

        // zoneinfo_ZoneInfo_from_file_impl
        private static PyObject zoneinfo_ZoneInfo_from_file_impl(ZoneinfoState state, PyType type, PyObject file_obj, PyObject key)
        {
            var self = new PyZoneInfo(state, type);
            var file_repr = PyString.From(file_obj.ToRepr().Value);
            load_data(state, self, file_obj);
            self.source = SOURCE_FILE;
            self.file_repr = file_repr;
            self.key = key;
            return self;
        }

        // zoneinfo_ZoneInfo_no_cache_impl
        private static PyObject zoneinfo_ZoneInfo_no_cache_impl(ZoneinfoState state, PyType type, PyObject key)
        {
            var @out = zoneinfo_new_instance(state, type, key);
            ((PyZoneInfo)@out).source = SOURCE_NOCACHE;
            return @out;
        }

        // zoneinfo_ZoneInfo_clear_cache_impl
        private static PyObject zoneinfo_ZoneInfo_clear_cache_impl(ZoneinfoState state, PyType type, PyObject only_keys)
        {
            var weak_cache = get_weak_cache(state, type);

            if (only_keys == null || only_keys is PyNone)
            {
                // The error of clear() is reported after the strong cache is cleared.
                PythonException error = null;
                try
                {
                    CallMethod(weak_cache, "clear");
                }
                catch (PythonException e)
                {
                    error = e;
                }

                clear_strong_cache(state, type);
                if (error != null)
                    throw error;
            }
            else
            {
                var pop = PyString.From("pop");
                foreach (var item in PyObject.IterateItems(only_keys))
                {
                    // Remove from strong cache
                    eject_from_strong_cache(state, type, item);

                    // Remove from weak cache
                    weak_cache.GetAttribute(pop).Call(new[] { item, PyNone.Instance }, null);
                }
            }

            return PyNone.Instance;
        }

        // zoneinfo_ZoneInfo_utcoffset_impl
        private static PyObject zoneinfo_ZoneInfo_utcoffset_impl(ZoneinfoState state, PyObject self, PyObject dt)
        {
            var tti = find_ttinfo(state, (PyZoneInfo)self, dt);
            return tti.utcoff;
        }

        // zoneinfo_ZoneInfo_dst_impl
        private static PyObject zoneinfo_ZoneInfo_dst_impl(ZoneinfoState state, PyObject self, PyObject dt)
        {
            var tti = find_ttinfo(state, (PyZoneInfo)self, dt);
            return tti.dstoff;
        }

        // zoneinfo_ZoneInfo_tzname_impl
        private static PyObject zoneinfo_ZoneInfo_tzname_impl(ZoneinfoState state, PyObject self, PyObject dt)
        {
            var tti = find_ttinfo(state, (PyZoneInfo)self, dt);
            return tti.tzname;
        }

        // zoneinfo_fromutc
        private static PyObject zoneinfo_fromutc(PyObject obj_self, PyObject dt)
        {
            if (!PyDateTime_Check(dt))
                throw PyTypeError.Create("fromutc: argument must be a datetime");
            // GET_DT_TZINFO
            if (!ReferenceEquals(((PyDateTimeDateTime)dt).TzinfoOrNone, obj_self))
                throw PyValueError.Create("fromutc: dt.tzinfo is not self");

            var self = (PyZoneInfo)obj_self;

            long timestamp = get_local_timestamp(dt);
            int num_trans = self.num_transitions;

            TTInfo tti;
            bool fold = false;

            if (num_trans >= 1 && timestamp < self.trans_list_utc[0])
            {
                tti = self.ttinfo_before;
            }
            else if (num_trans == 0 || timestamp > self.trans_list_utc[num_trans - 1])
            {
                tti = find_tzrule_ttinfo_fromutc(self.tzrule_after, timestamp, ((PyDateTimeDateTime)dt).Year, out fold);

                // Immediately after the last manual transition, the fold/gap is
                // between self->trans_ttinfos[num_transitions - 1] and whatever
                // ttinfo applies immediately after the last transition, not between
                // the STD and DST rules in the tzrule_after, so we may need to
                // adjust the fold value.
                if (num_trans != 0)
                {
                    TTInfo tti_prev;
                    if (num_trans == 1)
                        tti_prev = self.ttinfo_before;
                    else
                        tti_prev = self.trans_ttinfos[num_trans - 2];
                    // C long subtraction, 32 bits on Windows
                    long diff = unchecked(tti_prev.utcoff_seconds - tti.utcoff_seconds);
                    if (diff > 0 && timestamp < (self.trans_list_utc[num_trans - 1] + diff))
                        fold = true;
                }
            }
            else
            {
                int idx = _bisect(timestamp, self.trans_list_utc, num_trans);
                TTInfo tti_prev;

                if (idx >= 2)
                {
                    tti_prev = self.trans_ttinfos[idx - 2];
                    tti = self.trans_ttinfos[idx - 1];
                }
                else
                {
                    tti_prev = self.ttinfo_before;
                    tti = self.trans_ttinfos[0];
                }

                // Detect fold
                long shift = unchecked(tti_prev.utcoff_seconds - tti.utcoff_seconds);
                if (shift > (timestamp - self.trans_list_utc[idx - 1]))
                    fold = true;
            }

            var tmp = PyVM.Instance.ExecuteBinaryOpType(dt, tti.utcoff, BinaryOpType.ADD);

            if (fold)
            {
                if (PyDateTime_CheckExact(tmp))
                {
                    ((PyDateTimeDateTime)tmp).Fold = 1;
                    dt = tmp;
                }
                else
                {
                    var replace = tmp.GetAttribute("replace");
                    var kwargs = new PyDict();
                    kwargs.SetItem(PyString.From("fold"), PyInt.From(1));
                    dt = replace.Call(Array.Empty<PyObject>(), kwargs);
                }
            }
            else
            {
                dt = tmp;
            }
            return dt;
        }

        // zoneinfo_repr
        private static PyObject zoneinfo_repr(PyZoneInfo self)
        {
            string type_name = self.GetPyType().FullName;
            if (!(self.key is PyNone))
                return PyString.From($"{type_name}(key={self.key.ToRepr().Value})");
            return PyString.From($"{type_name}.from_file({((PyString)self.file_repr).Value})");
        }

        // zoneinfo_str
        private static PyObject zoneinfo_str(PyZoneInfo self)
        {
            if (!(self.key is PyNone))
                return self.key;
            return zoneinfo_repr(self);
        }

        /* Pickles the ZoneInfo object by key and source.
         *
         * ZoneInfo objects are pickled by reference to the TZif file that they came
         * from, which means that the exact transitions may be different or the file
         * may not un-pickle if the data has changed on disk in the interim.
         *
         * It is necessary to include a bit indicating whether or not the object
         * was constructed from the cache, because from-cache objects will hit the
         * unpickling process's cache, whereas no-cache objects will bypass it.
         *
         * Objects constructed from ZoneInfo.from_file cannot be pickled.
         */
        // zoneinfo_reduce
        private static PyObject zoneinfo_reduce(PyObject obj_self)
        {
            var self = (PyZoneInfo)obj_self;
            if (self.source == SOURCE_FILE)
            {
                // Objects constructed from files cannot be pickled.
                var pickle_error = PyImportSystem.Import("pickle").GetAttribute("PicklingError");
                throw new PythonException((PyBaseException)pickle_error.Call(
                    new PyObject[] { PyString.From("Cannot pickle a ZoneInfo file from a file stream.") }, null));
            }

            int from_cache = self.source == SOURCE_CACHE ? 1 : 0;
            var constructor = obj_self.GetAttribute("_unpickle");

            // Py_BuildValue("O(OB)", constructor, self->key, from_cache)
            return PyTuple.From(constructor, PyTuple.From(self.key, PyInt.From(from_cache)));
        }

        // zoneinfo_ZoneInfo__unpickle_impl
        private static PyObject zoneinfo_ZoneInfo__unpickle_impl(ZoneinfoState state, PyType type, PyObject key, byte from_cache)
        {
            if (from_cache != 0)
                return zoneinfo_new(state, type, new[] { key }, null);
            return zoneinfo_new_instance(state, type, key);
        }

        /* It is relatively expensive to construct new timedelta objects, and in most
         * cases we're looking at a relatively small number of timedeltas, such as
         * integer number of hours, etc. We will keep a cache so that we construct
         * a minimal number of these.
         */
        // load_timedelta
        private static PyObject load_timedelta(ZoneinfoState state, int seconds)
        {
            var pyoffset = PyInt.From(seconds);
            var rv = state.TIMEDELTA_CACHE.ContainsKey(pyoffset) ? state.TIMEDELTA_CACHE.Get(pyoffset) : null;
            if (rv == null)
            {
                var tmp = DatetimeModule.NewDeltaEx(0, seconds, 0, true, DatetimeModule.DeltaType);
                rv = state.TIMEDELTA_CACHE.SetDefault(pyoffset, tmp);
            }
            return rv;
        }

        /* Constructor for _ttinfo object - this starts by initializing the _ttinfo
         * to { NULL, NULL, NULL }, so that Py_XDECREF will work on partially
         * initialized _ttinfo objects.
         */
        // build_ttinfo
        private static void build_ttinfo(ZoneinfoState state, int utcoffset, int dstoffset, PyObject tzname, TTInfo @out)
        {
            @out.utcoff = null;
            @out.dstoff = null;
            @out.tzname = null;

            @out.utcoff_seconds = utcoffset;
            @out.utcoff = load_timedelta(state, utcoffset);
            @out.dstoff = load_timedelta(state, dstoffset);
            @out.tzname = tzname;
        }

        /* Equality function for _ttinfo. */
        // ttinfo_eq
        private static bool ttinfo_eq(TTInfo tti0, TTInfo tti1)
        {
            if (!PyObject.CompareBool(tti0.utcoff, tti1.utcoff, PyObject.CompareOp.EQ))
                return false;
            if (!PyObject.CompareBool(tti0.dstoff, tti1.dstoff, PyObject.CompareOp.EQ))
                return false;
            if (!PyObject.CompareBool(tti0.tzname, tti1.tzname, PyObject.CompareOp.EQ))
                return false;
            return true;
        }

        /* Given a file-like object, this populates a ZoneInfo object
         *
         * The current version calls into a Python function to read the data from
         * file into Python objects, and this translates those Python objects into
         * C values and calculates derived values (e.g. dstoff) in C.
         *
         * The function will never return while `self` is partially initialized —
         * the object only needs to be freed / deallocated if this succeeds.
         */
        // load_data
        private static void load_data(ZoneinfoState state, PyZoneInfo self, PyObject file_obj)
        {
            self.trans_list_utc = null;
            self.trans_list_wall[0] = null;
            self.trans_list_wall[1] = null;
            self.trans_ttinfos = null;
            self._ttinfos = null;
            self.file_repr = null;

            try
            {
                var data_tuple = CallMethod(state._common_mod, "load_data", file_obj);

                if (data_tuple is not PyTuple dataTuple || !dataTuple.IsExact)
                    throw PyTypeError.Create($"Invalid data result type: {data_tuple.ToRepr().Value}");

                // Unpack the data tuple
                var trans_idx_list = PyTuple_GetItem(data_tuple, 0);
                var trans_utc = PyTuple_GetItem(data_tuple, 1);
                var utcoff_list = PyTuple_GetItem(data_tuple, 2);
                var isdst_list = PyTuple_GetItem(data_tuple, 3);
                var abbr = PyTuple_GetItem(data_tuple, 4);
                var tz_str = PyTuple_GetItem(data_tuple, 5);

                // Load the relevant sizes
                int num_transitions = PyTuple_Size(trans_utc);
                int num_ttinfos = PyTuple_Size(utcoff_list);

                self.num_transitions = num_transitions;
                self.num_ttinfos = num_ttinfos;

                // Load the transition indices and list
                self.trans_list_utc = new long[self.num_transitions];
                var trans_idx = new int[self.num_transitions];

                for (int i = 0; i < self.num_transitions; ++i)
                {
                    var num = PyTuple_GetItem(trans_utc, i);
                    self.trans_list_utc[i] = PyLong_AsLongLong(num);

                    num = PyTuple_GetItem(trans_idx_list, i);

                    long cur_trans_idx = PyLong_AsSsize_t(num);
                    if (cur_trans_idx == -1)
                        throw NullWithoutError();

                    // The C test is `> num_ttinfos` and an index equal to num_ttinfos then reads
                    // past the end of the ttinfo arrays; here that index is refused as well.
                    if ((ulong)cur_trans_idx >= (ulong)self.num_ttinfos)
                        throw PyValueError.Create($"Invalid transition index found while reading TZif: {cur_trans_idx}");
                    trans_idx[i] = (int)cur_trans_idx;
                }

                // Load UTC offsets and isdst (size num_ttinfos)
                var utcoff = new int[self.num_ttinfos];
                var isdst = new bool[self.num_ttinfos];

                for (int i = 0; i < self.num_ttinfos; ++i)
                {
                    var num = PyTuple_GetItem(utcoff_list, i);
                    utcoff[i] = PyLong_AsLong(num);

                    num = PyTuple_GetItem(isdst_list, i);
                    isdst[i] = num.IsTrue();
                }

                var dstoff = new int[self.num_ttinfos];

                // Derive dstoff and trans_list_wall from the information we've loaded
                utcoff_to_dstoff(trans_idx, utcoff, dstoff, isdst, self.num_transitions, self.num_ttinfos);

                ts_to_local(trans_idx, self.trans_list_utc, utcoff, self.trans_list_wall, self.num_ttinfos, self.num_transitions);

                // Build _ttinfo objects from utcoff, dstoff and abbr
                self._ttinfos = new TTInfo[self.num_ttinfos];
                for (int i = 0; i < self.num_ttinfos; ++i)
                {
                    var tzname = PyTuple_GetItem(abbr, i);
                    self._ttinfos[i] = new TTInfo();
                    build_ttinfo(state, utcoff[i], dstoff[i], tzname, self._ttinfos[i]);
                }

                // Build our mapping from transition to the ttinfo that applies
                self.trans_ttinfos = new TTInfo[self.num_transitions];
                for (int i = 0; i < self.num_transitions; ++i)
                {
                    int ttinfo_idx = trans_idx[i];
                    self.trans_ttinfos[i] = self._ttinfos[ttinfo_idx];
                }

                // Set ttinfo_before to the first non-DST transition
                for (int i = 0; i < self.num_ttinfos; ++i)
                {
                    if (!isdst[i])
                    {
                        self.ttinfo_before = self._ttinfos[i];
                        break;
                    }
                }

                // If there are only DST ttinfos, pick the first one, if there are no
                // ttinfos at all, set ttinfo_before to NULL
                if (self.ttinfo_before == null && self.num_ttinfos > 0)
                    self.ttinfo_before = self._ttinfos[0];

                if (!(tz_str is PyNone) && tz_str.IsTrue())
                {
                    parse_tz_str(state, tz_str, self.tzrule_after);
                }
                else
                {
                    if (self.num_ttinfos == 0)
                        throw PyValueError.Create("No time zone information found.");

                    int idx;
                    if (self.num_transitions == 0)
                        idx = self.num_ttinfos - 1;
                    else
                        idx = trans_idx[self.num_transitions - 1];

                    var tti = self._ttinfos[idx];
                    build_tzrule(state, tti.tzname, null, tti.utcoff_seconds, 0, null, null, self.tzrule_after);

                    // We've abused the build_tzrule constructor to construct an STD-only
                    // rule mimicking whatever ttinfo we've picked up, but it's possible
                    // that the one we've picked up is a DST zone, so we need to make sure
                    // that the dstoff is set correctly in that case.
                    if (tti.dstoff.IsTrue())
                    {
                        var tti_after = self.tzrule_after.std;
                        tti_after.dstoff = tti.dstoff;
                    }
                }

                // Determine if this is a "fixed offset" zone, meaning that the output of
                // the utcoffset, dst and tzname functions does not depend on the specific
                // datetime passed.
                //
                // We make three simplifying assumptions here:
                //
                // 1. If tzrule_after is not std_only, it has transitions that might occur
                //    (it is possible to construct TZ strings that specify STD and DST but
                //    no transitions ever occur, such as AAA0BBB,0/0,J365/25).
                // 2. If self->_ttinfos contains more than one _ttinfo object, the objects
                //    represent different offsets.
                // 3. self->ttinfos contains no unused _ttinfos (in which case an otherwise
                //    fixed-offset zone with extra _ttinfos defined may appear to *not* be
                //    a fixed offset zone).
                if (self.num_ttinfos > 1 || !self.tzrule_after.std_only)
                    self.fixed_offset = false;
                else if (self.num_ttinfos == 0)
                    self.fixed_offset = true;
                else
                    self.fixed_offset = ttinfo_eq(self._ttinfos[0], self.tzrule_after.std);
            }
            catch (PythonException)
            {
                // These resources only need to be freed if we have failed.
                self.trans_list_utc = null;
                self.trans_list_wall[0] = null;
                self.trans_list_wall[1] = null;
                self._ttinfos = null;
                self.trans_ttinfos = null;
                throw;
            }
        }

        /* Function to calculate the local timestamp of a transition from the year. */
        // calendarrule_year_to_timestamp
        private static long calendarrule_year_to_timestamp(CalendarRule self, int year)
        {
            // We want (year, month, day of month); we have year and month, but we
            // need to turn (week, day-of-week) into day-of-month
            //
            // Week 1 is the first week in which day `day` (where 0 = Sunday) appears.
            // Week 5 represents the last occurrence of day `day`, so we need to know
            // the first weekday of the month and the number of days in the month.
            sbyte first_day = unchecked((sbyte)((ymd_to_ord(year, self.month, 1) + 6) % 7));
            byte days_in_month = (byte)DAYS_IN_MONTH[self.month];
            if (self.month == 2 && is_leap_year(year))
                days_in_month += 1;

            // This equation seems magical, so I'll break it down:
            // 1. calendar says 0 = Monday, POSIX says 0 = Sunday so we need first_day
            //    + 1 to get 1 = Monday -> 7 = Sunday, which is still equivalent
            //    because this math is mod 7
            // 2. Get first day - desired day mod 7 (adjusting by 7 for negative
            //    numbers so that -1 % 7 = 6).
            // 3. Add 1 because month days are a 1-based index.
            sbyte month_day = unchecked((sbyte)(((sbyte)self.day - (first_day + 1)) % 7));
            if (month_day < 0)
                month_day += 7;
            month_day += 1;

            // Now use a 0-based index version of `week` to calculate the w-th
            // occurrence of `day`
            month_day = unchecked((sbyte)(month_day + ((sbyte)self.week - 1) * 7));

            // month_day will only be > days_in_month if w was 5, and `w` means "last
            // occurrence of `d`", so now we just check if we over-shot the end of the
            // month and if so knock off 1 week.
            if (month_day > days_in_month)
                month_day -= 7;

            long ordinal = ymd_to_ord(year, self.month, month_day) - EPOCHORDINAL;
            return ordinal * 86400 + (long)self.hour * 3600 + (long)self.minute * 60 + self.second;
        }

        /* Constructor for CalendarRule. */
        // calendarrule_new: the errors it sets are replaced by parse_tz_str's "Malformed
        // transition rule", so they are kept as the messages of the failures only.
        private static int calendarrule_new(int month, int week, int day, int hour, int minute, int second, out CalendarRule @out)
        {
            @out = null;
            // These bounds come from the POSIX standard, which describes an Mm.n.d
            // rule as:
            //
            //   The d'th day (0 <= d <= 6) of week n of month m of the year (1 <= n <=
            //   5, 1 <= m <= 12, where week 5 means "the last d day in month m" which
            //   may occur in either the fourth or the fifth week). Week 1 is the first
            //   week in which the d'th day occurs. Day zero is Sunday.
            if (month < 1 || month > 12)
                return -1; // ValueError: "Month must be in [1, 12]"

            if (week < 1 || week > 5)
                return -1; // ValueError: "Week must be in [1, 5]"

            if (day < 0 || day > 6)
                return -1; // ValueError: "Day must be in [0, 6]"

            if (hour < -167 || hour > 167)
                return -1; // ValueError: "Hour must be in [0, 167]"

            @out = new CalendarRule
            {
                month = (byte)month,
                week = (byte)week,
                day = (byte)day,
                hour = (short)hour,
                minute = unchecked((sbyte)minute),
                second = unchecked((sbyte)second),
            };
            return 0;
        }

        /* Function to calculate the local timestamp of a transition from the year.
         *
         * This translates the day of the year into a local timestamp — either a
         * 1-based Julian day, not including leap days, or the 0-based year-day,
         * including leap days.
         * */
        // dayrule_year_to_timestamp
        private static long dayrule_year_to_timestamp(DayRule self, int year)
        {
            // ymd_to_ord calculates the number of days since 0001-01-01, but we want
            // to know the number of days since 1970-01-01, so we must subtract off
            // the equivalent of ymd_to_ord(1970, 1, 1).
            //
            // We subtract off an additional 1 day to account for January 1st (we want
            // the number of full days *before* the date of the transition - partial
            // days are accounted for in the hour, minute and second portions.
            long days_before_year = ymd_to_ord(year, 1, 1) - EPOCHORDINAL - 1;

            // The Julian day specification skips over February 29th in leap years,
            // from the POSIX standard:
            //
            //   Leap days shall not be counted. That is, in all years-including leap
            //   years-February 28 is day 59 and March 1 is day 60. It is impossible to
            //   refer explicitly to the occasional February 29.
            ushort day = self.day;
            if (self.julian != 0 && day >= 59 && is_leap_year(year))
                day += 1;

            return (days_before_year + day) * 86400 + (long)self.hour * 3600 + (long)self.minute * 60 + self.second;
        }

        /* Constructor for DayRule. */
        // dayrule_new: the errors it sets are replaced by parse_tz_str's, as calendarrule_new's.
        private static int dayrule_new(int julian, int day, int hour, int minute, int second, out DayRule @out)
        {
            @out = null;
            // The POSIX standard specifies that Julian days must be in the range (1 <=
            // n <= 365) and that non-Julian (they call it "0-based Julian") days must
            // be in the range (0 <= n <= 365).
            if (day < julian || day > 365)
                return -1; // ValueError: $"day must be in [{julian}, 365], not: {day}"

            if (hour < -167 || hour > 167)
                return -1; // ValueError: "Hour must be in [0, 167]"

            @out = new DayRule
            {
                julian = (byte)julian,
                day = unchecked((ushort)(short)day),
                hour = (short)hour,
                minute = unchecked((sbyte)minute),
                second = unchecked((sbyte)second),
            };
            return 0;
        }

        /* Calculate the start and end rules for a _tzrule in the given year. */
        // tzrule_transitions
        private static void tzrule_transitions(TzRule rule, int year, out long start, out long end)
        {
            start = rule.start.year_to_timestamp(year);
            end = rule.end.year_to_timestamp(year);
        }

        /* Calculate the _ttinfo that applies at a given local time from a _tzrule.
         *
         * This takes a local timestamp and fold for disambiguation purposes; the year
         * could technically be calculated from the timestamp, but given that the
         * callers of this function already have the year information accessible from
         * the datetime struct, it is taken as an additional parameter to reduce
         * unnecessary calculation.
         * */
        // find_tzrule_ttinfo
        private static TTInfo find_tzrule_ttinfo(TzRule rule, long ts, int fold, int year)
        {
            if (rule.std_only)
                return rule.std;

            bool isdst;
            tzrule_transitions(rule, year, out long start, out long end);

            // With fold = 0, the period (denominated in local time) with the smaller
            // offset starts at the end of the gap and ends at the end of the fold;
            // with fold = 1, it runs from the start of the gap to the beginning of the
            // fold.
            //
            // So in order to determine the DST boundaries we need to know both the
            // fold and whether DST is positive or negative (rare), and it turns out
            // that this boils down to fold XOR is_positive.
            if (fold == (rule.dst_diff >= 0 ? 1 : 0))
                end -= rule.dst_diff;
            else
                start += rule.dst_diff;

            if (start < end)
                isdst = (ts >= start) && (ts < end);
            else
                isdst = (ts < end) || (ts >= start);

            return isdst ? rule.dst : rule.std;
        }

        /* Calculate the ttinfo and fold that applies for a _tzrule at an epoch time.
         *
         * This function can determine the _ttinfo that applies at a given epoch time,
         * (analogous to trans_list_utc), and whether or not the datetime is in a fold.
         * This is to be used in the .fromutc() function.
         **/
        // find_tzrule_ttinfo_fromutc
        private static TTInfo find_tzrule_ttinfo_fromutc(TzRule rule, long ts, int year, out bool fold)
        {
            if (rule.std_only)
            {
                fold = false;
                return rule.std;
            }

            bool isdst;
            tzrule_transitions(rule, year, out long start, out long end);
            start -= rule.std.utcoff_seconds;
            end -= rule.dst.utcoff_seconds;

            if (start < end)
                isdst = (ts >= start) && (ts < end);
            else
                isdst = (ts < end) || (ts >= start);

            // For positive DST, the ambiguous period is one dst_diff after the end of
            // DST; for negative DST, the ambiguous period is one dst_diff before the
            // start of DST.
            long ambig_start, ambig_end;
            if (rule.dst_diff > 0)
            {
                ambig_start = end;
                ambig_end = end + rule.dst_diff;
            }
            else
            {
                ambig_start = start;
                ambig_end = start - rule.dst_diff;
            }

            fold = (ts >= ambig_start) && (ts < ambig_end);

            return isdst ? rule.dst : rule.std;
        }

        // The C string PyBytes_AsString hands out: its bytes up to the first NUL, and a NUL after.
        private sealed class CStr
        {
            private readonly byte[] _bytes;
            internal CStr(byte[] bytes)
            {
                int n = Array.IndexOf(bytes, (byte)0);
                _bytes = n < 0 ? bytes : bytes[..n];
            }
            internal byte this[int i] => i < _bytes.Length ? _bytes[i] : (byte)0;
            internal string Slice(int start, int end) => System.Text.Encoding.UTF8.GetString(_bytes, start, end - start);
        }

        // isalpha and isdigit of the C locale; Py_ISDIGIT
        private static bool isalpha(byte c) => (c >= (byte)'a' && c <= (byte)'z') || (c >= (byte)'A' && c <= (byte)'Z');
        private static bool isdigit(byte c) => c >= (byte)'0' && c <= (byte)'9';

        /* Parse a TZ string in the format specified by the POSIX standard:
         *
         *  std offset[dst[offset],start[/time],end[/time]]
         *
         *  std and dst must be 3 or more characters long and must not contain a
         *  leading colon, embedded digits, commas, nor a plus or minus signs; The
         *  spaces between "std" and "offset" are only for display and are not actually
         *  present in the string.
         *
         *  The format of the offset is ``[+|-]hh[:mm[:ss]]``
         *
         * See the POSIX.1 spec: IEE Std 1003.1-2018 §8.3:
         *
         * https://pubs.opengroup.org/onlinepubs/9699919799/basedefs/V1_chap08.html
         */
        // parse_tz_str
        private static void parse_tz_str(ZoneinfoState state, PyObject tz_str_obj, TzRule @out)
        {
            PyObject std_abbr = null;
            PyObject dst_abbr = null;
            TransitionRuleType start = null;
            TransitionRuleType end = null;
            // Initialize offsets to invalid value (> 24 hours)
            int std_offset = 1 << 20;
            int dst_offset = 1 << 20;

            // PyBytes_AsString
            if (tz_str_obj is not PyBytes bytes)
                throw PyTypeError.Create($"expected bytes, {tz_str_obj.GetPyType().FullName} found");
            var tz_str = new CStr(bytes.Value);
            int p = 0;
            string repr = null;
            string Repr() => repr ??= tz_str_obj.ToRepr().Value;

            // Read the `std` abbreviation, which must be at least 3 characters long.
            if (parse_abbr(tz_str, ref p, out std_abbr) != 0)
                throw PyValueError.Create($"Invalid STD format in {Repr()}");

            // Now read the STD offset, which is required
            if (parse_tz_delta(tz_str, ref p, out std_offset) != 0)
                throw PyValueError.Create($"Invalid STD offset in {Repr()}");

            // If the string ends here, there is no DST, otherwise we must parse the
            // DST abbreviation and start and end dates and times.
            if (tz_str[p] != 0)
            {
                if (parse_abbr(tz_str, ref p, out dst_abbr) != 0)
                    throw PyValueError.Create($"Invalid DST format in {Repr()}");

                if (tz_str[p] == ',')
                {
                    // From the POSIX standard:
                    //
                    // If no offset follows dst, the alternative time is assumed to be one
                    // hour ahead of standard time.
                    dst_offset = std_offset + 3600;
                }
                else
                {
                    if (parse_tz_delta(tz_str, ref p, out dst_offset) != 0)
                        throw PyValueError.Create($"Invalid DST offset in {Repr()}");
                }

                for (int i = 0; i < 2; ++i)
                {
                    if (tz_str[p] != ',')
                        throw PyValueError.Create($"Missing transition rules in TZ string: {Repr()}");
                    p++;

                    if (parse_transition_rule(tz_str, ref p, out var rule) != 0)
                        throw PyValueError.Create($"Malformed transition rule in TZ string: {Repr()}");
                    if (i == 0)
                        start = rule;
                    else
                        end = rule;
                }

                if (tz_str[p] != 0)
                    throw PyValueError.Create($"Extraneous characters at end of TZ string: {Repr()}");
            }

            // complete:
            build_tzrule(state, std_abbr, dst_abbr, std_offset, dst_offset, start, end, @out);
        }

        // parse_digits
        private static int parse_digits(CStr s, ref int p, int min, int max, out int value)
        {
            value = 0;
            for (int i = 0; i < max; i++, p++)
            {
                if (!isdigit(s[p]))
                    return (i < min) ? -1 : 0;
                value *= 10;
                value += s[p] - '0';
            }
            return 0;
        }

        /* Parse the STD and DST abbreviations from a TZ string. */
        // parse_abbr
        private static int parse_abbr(CStr s, ref int p, out PyObject abbr)
        {
            abbr = null;
            int ptr = p;
            int str_start;
            int str_end;

            if (s[ptr] == '<')
            {
                byte buff;
                ptr++;
                str_start = ptr;
                while ((buff = s[ptr]) != '>')
                {
                    // From the POSIX standard:
                    //
                    //   In the quoted form, the first character shall be the less-than
                    //   ( '<' ) character and the last character shall be the
                    //   greater-than ( '>' ) character. All characters between these
                    //   quoting characters shall be alphanumeric characters from the
                    //   portable character set in the current locale, the plus-sign (
                    //   '+' ) character, or the minus-sign ( '-' ) character. The std
                    //   and dst fields in this case shall not include the quoting
                    //   characters.
                    if (!isalpha(buff) && !isdigit(buff) && buff != '+' && buff != '-')
                        return -1;
                    ptr++;
                }
                str_end = ptr;
                ptr++;
            }
            else
            {
                str_start = ptr;
                // From the POSIX standard:
                //
                //   In the unquoted form, all characters in these fields shall be
                //   alphabetic characters from the portable character set in the
                //   current locale.
                while (isalpha(s[ptr]))
                    ptr++;
                str_end = ptr;
                if (str_end == str_start)
                    return -1;
            }

            // PyUnicode_FromStringAndSize
            abbr = PyString.From(s.Slice(str_start, str_end));

            p = ptr;
            return 0;
        }

        /* Parse a UTC offset from a TZ str. */
        // parse_tz_delta
        private static int parse_tz_delta(CStr s, ref int p, out int total_seconds)
        {
            // From the POSIX spec:
            //
            //   Indicates the value added to the local time to arrive at Coordinated
            //   Universal Time. The offset has the form:
            //
            //   hh[:mm[:ss]]
            //
            //   One or more digits may be used; the value is always interpreted as a
            //   decimal number.
            //
            // The POSIX spec says that the values for `hour` must be between 0 and 24
            // hours, but RFC 8536 §3.3.1 specifies that the hours part of the
            // transition times may be signed and range from -167 to 167.
            total_seconds = 0;
            int hours = 0;
            int minutes = 0;
            int seconds = 0;

            if (parse_transition_time(s, ref p, ref hours, ref minutes, ref seconds) != 0)
                return -1;

            if (hours > 24 || hours < -24)
                return -1;

            // Negative numbers correspond to *positive* offsets, from the spec:
            //
            //   If preceded by a '-', the timezone shall be east of the Prime
            //   Meridian; otherwise, it shall be west (which may be indicated by
            //   an optional preceding '+' ).
            total_seconds = -((hours * 3600) + (minutes * 60) + seconds);
            return 0;
        }

        /* Parse the date portion of a transition rule. */
        // parse_transition_rule
        private static int parse_transition_rule(CStr s, ref int p, out TransitionRuleType @out)
        {
            // The full transition rule indicates when to change back and forth between
            // STD and DST, and has the form:
            //
            //   date[/time],date[/time]
            //
            // This function parses an individual date[/time] section, and returns
            // the number of characters that contributed to the transition rule. This
            // does not include the ',' at the end of the first rule.
            //
            // The POSIX spec states that if *time* is not given, the default is 02:00.
            @out = null;
            int ptr = p;
            int hour = 2;
            int minute = 0;
            int second = 0;

            // Rules come in one of three flavors:
            //
            //   1. Jn: Julian day n, with no leap days.
            //   2. n: Day of year (0-based, with leap days)
            //   3. Mm.n.d: Specifying by month, week and day-of-week.

            if (s[ptr] == 'M')
            {
                ptr++;

                if (parse_digits(s, ref ptr, 1, 2, out int month) != 0)
                    return -1;
                if (s[ptr++] != '.')
                    return -1;
                if (parse_digits(s, ref ptr, 1, 1, out int week) != 0)
                    return -1;
                if (s[ptr++] != '.')
                    return -1;
                if (parse_digits(s, ref ptr, 1, 1, out int day) != 0)
                    return -1;

                if (s[ptr] == '/')
                {
                    ptr++;
                    if (parse_transition_time(s, ref ptr, ref hour, ref minute, ref second) != 0)
                        return -1;
                }

                if (calendarrule_new(month, week, day, hour, minute, second, out var rv) != 0)
                    return -1;

                @out = rv;
            }
            else
            {
                int julian = 0;
                if (s[ptr] == 'J')
                {
                    julian = 1;
                    ptr++;
                }

                if (parse_digits(s, ref ptr, 1, 3, out int day) != 0)
                    return -1;

                if (s[ptr] == '/')
                {
                    ptr++;
                    if (parse_transition_time(s, ref ptr, ref hour, ref minute, ref second) != 0)
                        return -1;
                }

                if (dayrule_new(julian, day, hour, minute, second, out var rv) != 0)
                    return -1;
                @out = rv;
            }

            p = ptr;
            return 0;
        }

        /* Parse the time portion of a transition rule (e.g. following an /) */
        // parse_transition_time
        private static int parse_transition_time(CStr s, ref int p, ref int hour, ref int minute, ref int second)
        {
            // From the spec:
            //
            //   The time has the same format as offset except that no leading sign
            //   ( '-' or '+' ) is allowed.
            //
            // The format for the offset is:
            //
            //   h[h][:mm[:ss]]
            //
            // RFC 8536 also allows transition times to be signed and to range from
            // -167 to +167.
            int ptr = p;
            int sign = 1;

            if (s[ptr] == '-' || s[ptr] == '+')
            {
                if (s[ptr] == '-')
                    sign = -1;
                ptr++;
            }

            // The hour can be 1 to 3 numeric characters
            if (parse_digits(s, ref ptr, 1, 3, out hour) != 0)
                return -1;
            hour *= sign;

            // Minutes and seconds always of the format ":dd"
            if (s[ptr] == ':')
            {
                ptr++;
                if (parse_digits(s, ref ptr, 2, 2, out minute) != 0)
                    return -1;
                minute *= sign;

                if (s[ptr] == ':')
                {
                    ptr++;
                    if (parse_digits(s, ref ptr, 2, 2, out second) != 0)
                        return -1;
                    second *= sign;
                }
            }

            p = ptr;
            return 0;
        }

        /* Constructor for a _tzrule.
         *
         * If `dst_abbr` is NULL, this will construct an "STD-only" _tzrule, in which
         * case `dst_offset` will be ignored and `start` and `end` are expected to be
         * NULL as well.
         */
        // build_tzrule
        private static void build_tzrule(ZoneinfoState state, PyObject std_abbr, PyObject dst_abbr,
            int std_offset, int dst_offset, TransitionRuleType start, TransitionRuleType end, TzRule @out)
        {
            var rv = new TzRule();

            rv.start = start;
            rv.end = end;

            build_ttinfo(state, std_offset, 0, std_abbr, rv.std);

            if (dst_abbr != null)
            {
                rv.dst_diff = unchecked(dst_offset - std_offset);
                build_ttinfo(state, dst_offset, rv.dst_diff, dst_abbr, rv.dst);
            }
            else
            {
                rv.std_only = true;
            }

            // *out = rv
            @out.std = rv.std;
            @out.dst = rv.dst;
            @out.dst_diff = rv.dst_diff;
            @out.start = rv.start;
            @out.end = rv.end;
            @out.std_only = rv.std_only;
        }

        /* Calculate DST offsets from transitions and UTC offsets
         *
         * This is necessary because each C `ttinfo` only contains the UTC offset,
         * time zone abbreviation and an isdst boolean - it does not include the
         * amount of the DST offset, but we need the amount for the dst() function.
         *
         * Thus function uses heuristics to infer what the offset should be, so it
         * is not guaranteed that this will work for all zones. If we cannot assign
         * a value for a given DST offset, we'll assume it's 1H rather than 0H, so
         * bool(dt.dst()) will always match ttinfo.isdst.
         */
        // utcoff_to_dstoff
        private static void utcoff_to_dstoff(int[] trans_idx, int[] utcoffs, int[] dstoffs, bool[] isdsts,
            int num_transitions, int num_ttinfos)
        {
            int dst_count = 0;
            int dst_found = 0;
            for (int i = 0; i < num_ttinfos; ++i)
                dst_count++;

            for (int i = 1; i < num_transitions; ++i)
            {
                if (dst_count == dst_found)
                    break;

                int idx = trans_idx[i];
                int comp_idx = trans_idx[i - 1];

                // Only look at DST offsets that have nto been assigned already
                if (!isdsts[idx] || dstoffs[idx] != 0)
                    continue;

                int dstoff = 0;
                int utcoff = utcoffs[idx];

                if (!isdsts[comp_idx])
                    dstoff = unchecked(utcoff - utcoffs[comp_idx]);

                // (size_t)(num_ttinfos - 1): num_ttinfos is at least 1 here.
                if (dstoff == 0 && idx < (num_ttinfos - 1))
                {
                    // The C reads trans_idx[num_transitions] at the last transition, one past
                    // the array; here that transition is left for later, as a DST one is.
                    if (i + 1 >= num_transitions)
                        continue;
                    comp_idx = trans_idx[i + 1];

                    // If the following transition is also DST and we couldn't find
                    // the DST offset by this point, we're going to have to skip it
                    // and hope this transition gets assigned later
                    if (isdsts[comp_idx])
                        continue;

                    dstoff = unchecked(utcoff - utcoffs[comp_idx]);
                }

                if (dstoff != 0)
                {
                    dst_found++;
                    dstoffs[idx] = dstoff;
                }
            }

            if (dst_found < dst_count)
            {
                // If there are time zones we didn't find a value for, we'll end up
                // with dstoff = 0 for something where isdst=1. This is obviously
                // wrong — one hour will be a much better guess than 0.
                for (int idx = 0; idx < num_ttinfos; ++idx)
                {
                    if (isdsts[idx] && dstoffs[idx] == 0)
                        dstoffs[idx] = 3600;
                }
            }
        }

        /* Calculate transitions in local time from UTC time and offsets.
         *
         * We want to know when each transition occurs, denominated in the number of
         * nominal wall-time seconds between 1970-01-01T00:00:00 and the transition in
         * *local time* (note: this is *not* equivalent to the output of
         * datetime.timestamp, which is the total number of seconds actual elapsed
         * since 1970-01-01T00:00:00Z in UTC).
         *
         * This is an ambiguous question because "local time" can be ambiguous — but it
         * is disambiguated by the `fold` parameter, so we allocate two arrays:
         *
         *  trans_local[0]: The wall-time transitions for fold=0
         *  trans_local[1]: The wall-time transitions for fold=1
         */
        // ts_to_local
        private static void ts_to_local(int[] trans_idx, long[] trans_utc, int[] utcoff, long[][] trans_local,
            int num_ttinfos, int num_transitions)
        {
            if (num_transitions == 0)
                return;

            // Copy the UTC transitions into each array to be modified in place later
            for (int i = 0; i < 2; ++i)
                trans_local[i] = (long[])trans_utc.Clone();

            long offset_0, offset_1, buff;
            if (num_ttinfos > 1)
            {
                offset_0 = utcoff[0];
                offset_1 = utcoff[trans_idx[0]];

                if (offset_1 > offset_0)
                {
                    // _swap(offset_0, offset_1, buff)
                    buff = offset_0;
                    offset_0 = offset_1;
                    offset_1 = buff;
                }
            }
            else
            {
                offset_0 = utcoff[0];
                offset_1 = utcoff[0];
            }

            trans_local[0][0] += offset_0;
            trans_local[1][0] += offset_1;

            for (int i = 1; i < num_transitions; ++i)
            {
                offset_0 = utcoff[trans_idx[i - 1]];
                offset_1 = utcoff[trans_idx[i]];

                if (offset_1 > offset_0)
                {
                    // _swap(offset_1, offset_0, buff)
                    buff = offset_1;
                    offset_1 = offset_0;
                    offset_0 = buff;
                }

                trans_local[0][i] += offset_0;
                trans_local[1][i] += offset_1;
            }
        }

        /* Simple bisect_right binary search implementation */
        // _bisect
        private static int _bisect(long value, long[] arr, int size)
        {
            int lo = 0;
            int hi = size;
            int m;

            while (lo < hi)
            {
                m = (lo + hi) / 2;
                if (arr[m] > value)
                    hi = m;
                else
                    lo = m + 1;
            }

            return hi;
        }

        /* Find the ttinfo rules that apply at a given local datetime. */
        // find_ttinfo
        private static TTInfo find_ttinfo(ZoneinfoState state, PyZoneInfo self, PyObject dt)
        {
            // datetime.time has a .tzinfo attribute that passes None as the dt
            // argument; it only really has meaning for fixed-offset zones.
            if (dt is PyNone)
            {
                if (self.fixed_offset)
                    return self.tzrule_after.std;
                return state.NO_TTINFO;
            }

            long ts = get_local_timestamp(dt);

            // PyDateTime_DATE_GET_FOLD and PyDateTime_GET_YEAR read the datetime struct; an
            // object of another layout has neither, and is taken as fold 0 in the year of its
            // ordinal (the C reads whatever bytes are there).
            int fold = dt is PyDateTimeDateTime d ? d.Fold : 0;
            int year = dt is PyDateTimeDate date ? date.Year : YearOfLocalTimestamp(ts);
            var local_transitions = self.trans_list_wall[fold];
            int num_trans = self.num_transitions;

            if (num_trans != 0 && ts < local_transitions[0])
            {
                return self.ttinfo_before;
            }
            else if (num_trans == 0 || ts > local_transitions[self.num_transitions - 1])
            {
                return find_tzrule_ttinfo(self.tzrule_after, ts, fold, year);
            }
            else
            {
                int idx = _bisect(ts, local_transitions, self.num_transitions) - 1;
                return self.trans_ttinfos[idx];
            }
        }

        // The year of a local timestamp, for an object that is not a datetime (see find_ttinfo).
        private static int YearOfLocalTimestamp(long ts)
        {
            long ord = Math.Clamp(Math.DivRem(ts, 86400L, out long rem) - (rem < 0 ? 1 : 0) + EPOCHORDINAL, 1, 3652059);
            DatetimeModule.OrdToYmd((int)ord, out int year, out _, out _);
            return year;
        }

        // is_leap_year
        private static bool is_leap_year(int year)
        {
            uint ayear = unchecked((uint)year);
            return ayear % 4 == 0 && (ayear % 100 != 0 || ayear % 400 == 0);
        }

        /* Calculates ordinal datetime from year, month and day. */
        // ymd_to_ord
        private static int ymd_to_ord(int y, int m, int d)
        {
            unchecked
            {
                y -= 1;
                int days_before_year = (y * 365) + (y / 4) - (y / 100) + (y / 400);
                int yearday = DAYS_BEFORE_MONTH[m];
                if (m > 2 && is_leap_year(y + 1))
                    yearday += 1;

                return days_before_year + yearday + d;
            }
        }

        /* Calculate the number of seconds since 1970-01-01 in local time.
         *
         * This gets a datetime in the same "units" as self->trans_list_wall so that we
         * can easily determine which transitions a datetime falls between. See the
         * comment above ts_to_local for more information.
         * */
        // get_local_timestamp
        private static long get_local_timestamp(PyObject dt)
        {
            int hour, minute, second;
            int ord;
            if (PyDateTime_CheckExact(dt))
            {
                var datetime = (PyDateTimeDateTime)dt;
                int y = datetime.Year;
                int m = datetime.Month;
                int d = datetime.Day;
                hour = datetime.Hour;
                minute = datetime.Minute;
                second = datetime.Second;

                ord = ymd_to_ord(y, m, d);
            }
            else
            {
                var num = CallMethod(dt, "toordinal");
                ord = PyLong_AsLong(num);

                num = dt.GetAttribute("hour");
                hour = PyLong_AsLong(num);
                if (hour == -1)
                    throw NullWithoutError();

                num = dt.GetAttribute("minute");
                minute = PyLong_AsLong(num);
                if (minute == -1)
                    throw NullWithoutError();

                num = dt.GetAttribute("second");
                second = PyLong_AsLong(num);
                if (second == -1)
                    throw NullWithoutError();
            }

            return unchecked((long)(ord - EPOCHORDINAL) * 86400L + (long)(hour * 3600 + minute * 60 + second));
        }

        // ---- Functions for cache handling ----------------------------------------------------

        /* Constructor for StrongCacheNode */
        // strong_cache_node_new
        private static StrongCacheNode strong_cache_node_new(PyObject key, PyObject zone) =>
            new StrongCacheNode { next = null, prev = null, key = key, zone = zone };

        /* Removes a node from the cache and update its neighbors.
         *
         * This is used both when ejecting a node from the cache and when moving it to
         * the front of the cache.
         */
        // remove_from_strong_cache
        private static void remove_from_strong_cache(ZoneinfoState state, StrongCacheNode node)
        {
            if (state.ZONEINFO_STRONG_CACHE == node)
                state.ZONEINFO_STRONG_CACHE = node.next;

            if (node.prev != null)
                node.prev.next = node.next;

            if (node.next != null)
                node.next.prev = node.prev;

            node.next = null;
            node.prev = null;
        }

        /* Retrieves the node associated with a key, if it exists.
         *
         * This traverses the strong cache until it finds a matching key and returns a
         * pointer to the relevant node if found. Returns NULL if no node is found.
         *
         * root may be NULL, indicating an empty cache.
         */
        // find_in_strong_cache
        private static StrongCacheNode find_in_strong_cache(StrongCacheNode root, PyObject key)
        {
            var node = root;
            while (node != null)
            {
                if (PyObject.CompareBool(key, node.key, PyObject.CompareOp.EQ))
                    return node;

                node = node.next;
            }

            return null;
        }

        /* Ejects a given key from the class's strong cache, if applicable.
         *
         * This function is used to enable the per-key functionality in clear_cache.
         */
        // eject_from_strong_cache
        private static void eject_from_strong_cache(ZoneinfoState state, PyType type, PyObject key)
        {
            if (!ReferenceEquals(type, state.ZoneInfoType))
                return;

            var cache = state.ZONEINFO_STRONG_CACHE;
            var node = find_in_strong_cache(cache, key);
            if (node != null)
                remove_from_strong_cache(state, node);
        }

        /* Moves a node to the front of the LRU cache.
         *
         * The strong cache is an LRU cache, so whenever a given node is accessed, if
         * it is not at the front of the cache, it needs to be moved there.
         */
        // move_strong_cache_node_to_front
        private static void move_strong_cache_node_to_front(ZoneinfoState state, StrongCacheNode node)
        {
            var root_p = state.ZONEINFO_STRONG_CACHE;
            if (root_p == node)
                return;

            remove_from_strong_cache(state, node);

            node.prev = null;
            node.next = root_p;

            if (root_p != null)
                root_p.prev = node;

            state.ZONEINFO_STRONG_CACHE = node;
        }

        /* Retrieves a ZoneInfo from the strong cache if it's present.
         *
         * This function finds the ZoneInfo by key and if found will move the node to
         * the front of the LRU cache and return a new reference to it. It returns NULL
         * if the key is not in the cache.
         *
         * The strong cache is currently only implemented for the base class, so this
         * always returns a cache miss for subclasses.
         */
        // zone_from_strong_cache
        private static PyObject zone_from_strong_cache(ZoneinfoState state, PyType type, PyObject key)
        {
            if (!ReferenceEquals(type, state.ZoneInfoType))
                return null; // Strong cache currently only implemented for base class

            var cache = state.ZONEINFO_STRONG_CACHE;
            var node = find_in_strong_cache(cache, key);

            if (node != null)
            {
                move_strong_cache_node_to_front(state, node);
                return node.zone;
            }

            return null; // Cache miss
        }

        /* Inserts a new key into the strong LRU cache.
         *
         * This function is only to be used after a cache miss — it creates a new node
         * at the front of the cache and ejects any stale entries (keeping the size of
         * the cache to at most ZONEINFO_STRONG_CACHE_MAX_SIZE).
         */
        // update_strong_cache
        private static void update_strong_cache(ZoneinfoState state, PyType type, PyObject key, PyObject zone)
        {
            if (!ReferenceEquals(type, state.ZoneInfoType))
                return;

            var new_node = strong_cache_node_new(key, zone);
            move_strong_cache_node_to_front(state, new_node);

            var node = new_node.next;
            for (int i = 1; i < ZONEINFO_STRONG_CACHE_MAX_SIZE; ++i)
            {
                if (node == null)
                    return;
                node = node.next;
            }

            // Everything beyond this point needs to be freed (strong_cache_free)
            if (node != null)
            {
                if (node.prev != null)
                    node.prev.next = null;
            }
        }

        /* Clears all entries into a type's strong cache.
         *
         * Because the strong cache is not implemented for subclasses, this is a no-op
         * for everything except the base class.
         */
        // clear_strong_cache
        private static void clear_strong_cache(ZoneinfoState state, PyType type)
        {
            if (!ReferenceEquals(type, state.ZoneInfoType))
                return;

            state.ZONEINFO_STRONG_CACHE = null;
        }

        // new_weak_cache
        private static PyObject new_weak_cache()
        {
            var WeakValueDictionary = PyImportSystem.Import("weakref").GetAttribute("WeakValueDictionary");
            return WeakValueDictionary.Call(Array.Empty<PyObject>(), null);
        }

        // initialize_caches: this function is not idempotent and must be called on a new module object.
        private static void initialize_caches(ZoneinfoState state)
        {
            state.TIMEDELTA_CACHE = new PyDict();
            state.ZONEINFO_WEAK_CACHE = new_weak_cache();
        }

        // zoneinfo_init_subclass
        private static PyObject zoneinfo_init_subclass(PyObject cls, PyObject[] args, PyDict kwargs)
        {
            var weak_cache = new_weak_cache();
            cls.SetAttribute("_weak_cache", weak_cache);
            return PyNone.Instance;
        }

        // ---- Specify the ZoneInfo type -------------------------------------------------------

        // A clinic classmethod with defining_class: the class it is called on, then the arguments.
        private static void ClassMethod(PyType type, string name, Func<PyType, PyObject[], PyDict, PyObject> body) =>
            type.TypeDict[name] = new PyClassMethodDescriptor(name, type,
                new PyBuiltinFunction(name, (args, kwargs) => NullChecked(
                    () => ((IDescriptor)type.TypeDict[name]).Get(null, (PyType)args[0]),
                    () => body((PyType)args[0], args[1..], kwargs))));

        // A clinic instance method with defining_class and (dt, /).
        private static void DtMethod(PyType type, string name, Func<PyObject, PyObject, PyObject> body) =>
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack(name, args, kwargs, new[] { "" }, 1, 1, 1);
                return NullChecked(() => BoundMethod(type, name, self), () => body(self, a[0]));
            }, 0, int.MaxValue, acceptsKwargs: true);

        // zoneinfo_spec: "zoneinfo.ZoneInfo", Py_TPFLAGS_DEFAULT | Py_TPFLAGS_BASETYPE |
        // Py_TPFLAGS_HAVE_GC | Py_TPFLAGS_IMMUTABLETYPE, made by PyType_FromModuleAndSpec over tzinfo.
        private static PyType MakeZoneInfoType(ZoneinfoState state)
        {
            var type = new PyType("ZoneInfo", new[] { DatetimeModule.TZInfoType }, "zoneinfo");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__module__"] = PyString.From("zoneinfo");
            type.TypeDict["__doc__"] = PyNone.Instance;
            state.ZoneInfoType = type;

            // zoneinfo_slots: tp_repr, tp_str, tp_getattro, tp_new
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type,
                (self, args, kwargs) => zoneinfo_repr((PyZoneInfo)self), minArgs: 0, maxArgs: 0) { CallsVirtual = true };
            type.TypeDict["__str__"] = new PyWrapperDescriptor("__str__", type,
                (self, args, kwargs) => zoneinfo_str((PyZoneInfo)self), minArgs: 0, maxArgs: 0) { CallsVirtual = true };
            TypeTables.GenericAttributes(type);
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => NullChecked(() => cls, () => zoneinfo_new(state, (PyType)cls, args, kwargs)),
                0, int.MaxValue, acceptsKwargs: true);

            // zoneinfo_methods
            ClassMethod(type, "clear_cache", (cls, args, kwargs) =>
            {
                var a = PyArgs.Unpack("clear_cache", args, kwargs, new[] { "only_keys" }, 0, 0, 0);
                return zoneinfo_ZoneInfo_clear_cache_impl(state, cls, a[0] ?? PyNone.Instance);
            });
            ClassMethod(type, "no_cache", (cls, args, kwargs) =>
            {
                var a = PyArgs.Unpack("no_cache", args, kwargs, new[] { "key" }, 0, 1, 1);
                return zoneinfo_ZoneInfo_no_cache_impl(state, cls, a[0]);
            });
            ClassMethod(type, "from_file", (cls, args, kwargs) =>
            {
                var a = PyArgs.Unpack("from_file", args, kwargs, from_file_keywords, 1, 1, 2);
                return zoneinfo_ZoneInfo_from_file_impl(state, cls, a[0], a[1] ?? PyNone.Instance);
            });
            DtMethod(type, "utcoffset", (self, dt) => zoneinfo_ZoneInfo_utcoffset_impl(state, self, dt));
            DtMethod(type, "dst", (self, dt) => zoneinfo_ZoneInfo_dst_impl(state, self, dt));
            DtMethod(type, "tzname", (self, dt) => zoneinfo_ZoneInfo_tzname_impl(state, self, dt));
            PyArgs.Define(type, "fromutc", PyCall.One, (self, args) =>
                NullChecked(() => BoundMethod(type, "fromutc", self), () => zoneinfo_fromutc(self, args[0])));
            PyArgs.Define(type, "__reduce__", PyCall.NoArgs, (self, args) => zoneinfo_reduce(self));
            ClassMethod(type, "_unpickle", (cls, args, kwargs) =>
            {
                var a = PyArgs.Unpack("_unpickle", args, kwargs, new[] { "", "" }, 2, 2, 2);
                // unsigned_char(bitwise=True): PyLong_AsUnsignedLongMask, the low byte.
                var ival = PyObject.IndexOrThrow(a[1]).Value;
                byte from_cache = (byte)(ival & 0xFF);
                return zoneinfo_ZoneInfo__unpickle_impl(state, cls, a[0], from_cache);
            });
            ClassMethod(type, "__init_subclass__", (cls, args, kwargs) => zoneinfo_init_subclass(cls, args, kwargs));

            // zoneinfo_members: key (T_OBJECT_EX, READONLY); __weaklistoffset__ is read by the spec.
            type.TypeDict["key"] = new PyMemberDescriptor("key", type, self =>
                ((PyZoneInfo)self).key ?? throw PyAttributeError.Create(
                    $"'{self.GetPyType().FullName}' object has no attribute 'key'"), readOnly: true);
            return type;
        }

        // ---- Specify the _zoneinfo module ----------------------------------------------------

        /// <summary>zoneinfomodule_exec over a new module and its state.</summary>
        internal static PyModule Create()
        {
            var m = new PyModule("_zoneinfo");

            // PyDateTime_IMPORT: the capsule of datetime; its types are DatetimeModule's.
            PyImportSystem.Import("datetime");

            var state = new ZoneinfoState();
            var type = MakeZoneInfoType(state);
            m.ModuleDict["ZoneInfo"] = type;

            /* Populate imports */
            state._tzpath_find_tzfile = PyImportSystem.Import("zoneinfo._tzpath").GetAttribute("find_tzfile");
            state.io_open = PyImportSystem.Import("io").GetAttribute("open");
            state._common_mod = PyImportSystem.Import("zoneinfo._common");

            if (state.NO_TTINFO.utcoff == null)
            {
                state.NO_TTINFO.utcoff = PyNone.Instance;
                state.NO_TTINFO.dstoff = PyNone.Instance;
                state.NO_TTINFO.tzname = PyNone.Instance;
            }

            initialize_caches(state);
            return m;
        }
    }
}
