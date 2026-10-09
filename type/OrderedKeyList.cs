using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    // The ordinary key list stays compact. Active cursors get a stable history
    // only when deletion would move their positions. History is held weakly here.
    internal sealed class OrderedKeyList : List<PyObject>
    {
        private WeakReference<OrderedIterationState> _iteration;
        internal OrderedKeyList() { }
        internal OrderedKeyList(IEnumerable<PyObject> keys) : base(keys) { }

        internal OrderedIterationState GetIterationState()
        {
            if (_iteration != null && _iteration.TryGetTarget(out var current)) return current;
            var state = new OrderedIterationState(this);
            if (_iteration == null) _iteration = new WeakReference<OrderedIterationState>(state);
            else _iteration.SetTarget(state);
            return state;
        }

        internal void AddKey(PyObject key)
        {
            base.Add(key);
            if (_iteration != null && _iteration.TryGetTarget(out var state)) state.Add(key);
        }
        internal void RemoveKeyAt(int index, bool trimTail)
        {
            if (_iteration != null && _iteration.TryGetTarget(out var state)) state.Remove(this[index], trimTail);
            base.RemoveAt(index);
        }
        internal void ClearKeys()
        {
            if (_iteration != null && _iteration.TryGetTarget(out var state)) state.Clear();
            base.Clear();
        }
    }

    internal sealed class OrderedIterationState
    {
        private readonly OrderedKeyList _keys;
        private List<PyObject> _history;
        internal OrderedIterationState(OrderedKeyList keys) => _keys = keys;
        internal void Add(PyObject key)
        {
            if (_history == null) return;
            _history.Add(key);
            // Bound history during prolonged same-size churn. Numeric cursor positions
            // survive compaction, but this threshold does not mirror CPython capacity.
            if (_history.Count > (long)_keys.Count * 2 + 16) _history = null;
        }
        internal void Clear() => _history?.Clear();
        internal void Remove(PyObject key, bool trimTail)
        {
            _history ??= new List<PyObject>(_keys);
            for (int i = 0; i < _history.Count; i++)
            {
                if (ReferenceEquals(_history[i], key))
                {
                    _history[i] = null;
                    if (trimTail)
                        while (_history.Count > 0 && _history[_history.Count - 1] == null)
                            _history.RemoveAt(_history.Count - 1);
                    return;
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryNext(ref int position, out PyObject key)
        {
            if (_history != null) return TryNextHistory(ref position, out key);
            // Compact storage has no tombstones, so each step reads exactly one key.
            if ((uint)position < (uint)_keys.Count)
            {
                key = _keys[position++];
                return true;
            }
            key = null;
            return false;
        }

        private bool TryNextHistory(ref int position, out PyObject key)
        {
            var entries = _history;
            while (position < entries.Count)
            {
                key = entries[position++];
                if (key != null) return true;
            }
            key = null;
            return false;
        }
    }
}
