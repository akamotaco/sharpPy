using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// PyValue 기반 배열 스택. CPython 3.12의 localsplus 통합 배열 방식을 C#으로 구현.
    /// Option A: PyFrame의 FrameData (locals + stack) 단일 배열의 스택 영역을 관리.
    /// _base 오프셋부터 시작하여 locals 영역 뒤에 스택을 배치.
    /// 기존 Push(PyObject)/Pop() API는 자동 변환으로 호환성 유지.
    /// </summary>
    public class PyStack : IEnumerable<PyObject>
    {
        internal PyValue[] _items;
        internal int _top;   // 다음 Push 위치 (absolute index into _items)
        internal int _base;  // 스택 시작 오프셋 (= nlocals, locals 뒤부터)
        private const int DefaultCapacity = 16;

        // Back-reference to owning PyFrame for Grow() sync.
        // When _items grows, frame.LocalsPlus must be updated to the same new array.
        internal PyFrame _ownerFrame;

        public PyStack()
        {
            _items = new PyValue[DefaultCapacity];
            _top = 0;
            _base = 0;
        }

        /// <summary>
        /// Attach this stack to a shared FrameData buffer at the given base offset.
        /// CPython 3.12: localsplus array — stack starts after locals.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AttachTo(PyValue[] buffer, int baseOffset)
        {
            _items = buffer;
            _base = baseOffset;
            _top = baseOffset;
        }

        #region Legacy Pool (dead code — PyStack is permanently embedded in PyFrame)

        // ThreadStatic pool to avoid allocating new PyValue[16] per frame.
        // CPython reuses stack space via C call stack; we emulate with explicit pooling.
        [ThreadStatic] private static PyStack[] _pool;
        [ThreadStatic] private static int _poolCount;
        private const int PoolMaxSize = 32;

        /// <summary>
        /// Rent a PyStack from the thread-local pool (or create new).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PyStack Rent()
        {
            if (_poolCount > 0)
            {
                var stack = _pool[--_poolCount];
                _pool[_poolCount] = null;
                return stack;
            }
            return new PyStack();
        }

        /// <summary>
        /// Return a PyStack to the thread-local pool for reuse.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(PyStack stack)
        {
            stack.Clear();
            if (_pool == null) _pool = new PyStack[PoolMaxSize];
            if (_poolCount < PoolMaxSize)
            {
                _pool[_poolCount++] = stack;
            }
        }

        #endregion

        #region Compatibility API (PyObject — auto-converts via PyValue)

        /// <summary>
        /// 스택 최상단에 PyObject 추가 (자동 태깅)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Push(PyObject obj)
        {
            if (_top == _items.Length)
                Grow();
            _items[_top++] = PyValue.FromObject(obj);
        }

        /// <summary>
        /// 스택 최상단 값 제거 및 PyObject로 반환 (자동 박싱)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PyObject Pop()
        {
            if (_top == _base)
                throw new InvalidOperationException("Stack is empty");
            var val = _items[--_top];
            _items[_top].ObjRef = null; // GC safety: only clear reference (8B vs 24B)
            return val.ToObject();
        }

        /// <summary>
        /// 스택 최상단 값 확인 (PyObject 변환, 제거 없음)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PyObject Peek()
        {
            if (_top == _base)
                throw new InvalidOperationException("Stack is empty");
            return _items[_top - 1].ToObject();
        }

        /// <summary>
        /// 스택 상단에서 depth만큼 떨어진 값 확인 (PyObject 변환)
        /// depth=0: TOS, depth=1: TOS-1, depth=2: TOS-2, ...
        /// </summary>
        public PyObject PeekAt(int depth)
        {
            int stackCount = _top - _base;
            if (depth < 0 || depth >= stackCount)
            {
                throw new ArgumentOutOfRangeException(nameof(depth),
                    $"Invalid stack depth {depth} (stack size: {stackCount})");
            }
            return _items[_top - 1 - depth].ToObject();
        }

        /// <summary>
        /// 여러 PyObject를 한번에 Push (UNPACK_EX 등)
        /// </summary>
        public void PushRange(PyObject[] elements, int startIndex, int count)
        {
            int required = _top + count;
            if (required > _items.Length)
                Grow(required);
            for (int i = 0; i < count; i++)
                _items[_top + i] = PyValue.FromObject(elements[startIndex + i]);
            _top += count;
        }

        #endregion

        #region Fast API (PyValue — no conversion overhead)

        /// <summary>
        /// Push a PyValue directly (no conversion).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushValue(PyValue val)
        {
            if (_top == _items.Length)
                Grow();
            _items[_top++] = val;
        }

        /// <summary>
        /// Pop a PyValue directly (no conversion).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PyValue PopValue()
        {
            if (_top == _base)
                throw new InvalidOperationException("Stack is empty");
            var val = _items[--_top];
            _items[_top].ObjRef = null; // GC safety: only clear reference
            return val;
        }

        /// <summary>
        /// Bulk pop N values into a PyValue buffer (reverse order: TOS → buffer[count-1]).
        /// Used by CALL opcode to avoid per-arg ToObject/FromObject roundtrip.
        /// CPython 3.12: STACK_SHRINK(oparg) pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PopValues(PyValue[] buffer, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                buffer[i] = _items[--_top];
                _items[_top].ObjRef = null; // GC safety: only clear reference
            }
        }

        /// <summary>
        /// Peek at the top PyValue directly (no conversion).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PyValue PeekValue()
        {
            if (_top == _base)
                throw new InvalidOperationException("Stack is empty");
            return _items[_top - 1];
        }

        /// <summary>
        /// Peek at PyValue at given depth (no conversion).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PyValue PeekValueAt(int depth)
        {
            return _items[_top - 1 - depth];
        }

        /// <summary>
        /// Drop N items from the stack (GC-safe: clears ObjRef).
        /// CPython 3.12: STACK_SHRINK(n) pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DropN(int count)
        {
            for (int i = 0; i < count; i++)
                _items[--_top].ObjRef = null;
        }

        /// <summary>
        /// Push an int64 value directly (zero allocation).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushInt64(long value)
        {
            if (_top == _items.Length)
                Grow();
            _items[_top++] = PyValue.FromInt64(value);
        }

        /// <summary>
        /// Push a float64 value directly (zero allocation).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushFloat64(double value)
        {
            if (_top == _items.Length)
                Grow();
            _items[_top++] = PyValue.FromFloat64(value);
        }

        /// <summary>
        /// Push a bool value directly (zero allocation).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushBool(bool value)
        {
            if (_top == _items.Length)
                Grow();
            _items[_top++] = PyValue.FromBool(value);
        }

        /// <summary>
        /// Push PyValue.None directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushNone()
        {
            if (_top == _items.Length)
                Grow();
            _items[_top++] = PyValue.None;
        }

        #endregion

        #region Stack operations

        /// <summary>
        /// TOS와 TOS-distance+1 위치의 값을 교환
        /// </summary>
        public void Swap(int distance)
        {
            if (distance < 1)
                throw new ArgumentException($"SWAP distance must be >= 1, got {distance}");
            int stackCount = _top - _base;
            if (stackCount < distance)
                throw new InvalidOperationException(
                    $"SWAP({distance}): Not enough items on stack (need {distance}, got {stackCount})");

            int topIdx = _top - 1;
            int otherIdx = topIdx - distance + 1;
            (_items[topIdx], _items[otherIdx]) = (_items[otherIdx], _items[topIdx]);
        }

        /// <summary>
        /// 스택의 현재 크기 (locals 제외, 스택 요소만)
        /// </summary>
        public int Count => _top - _base;

        /// <summary>
        /// 스택의 모든 요소 제거 (locals는 유지)
        /// </summary>
        public void Clear()
        {
            // Conditional ObjRef clear — avoids GC write barrier on int/float/bool/null slots
            for (int i = _base; i < _top; i++)
            {
                if (_items[i].ObjRef != null) _items[i].ObjRef = null;
            }
            _top = _base;
        }

        #endregion

        #region Clone / Restore (Generator support)

        /// <summary>
        /// 스택 부분만 복사 (Generator 상태 저장용).
        /// Clone is standalone (not attached to any frame).
        /// </summary>
        public PyStack Clone()
        {
            int stackCount = _top - _base;
            var clone = new PyStack();
            if (stackCount > clone._items.Length)
                clone._items = new PyValue[stackCount];
            Array.Copy(_items, _base, clone._items, 0, stackCount);
            clone._top = stackCount;
            clone._base = 0;
            return clone;
        }

        /// <summary>
        /// 다른 스택의 내용을 이 스택으로 복원 (Generator resume용).
        /// Source is a standalone clone (base=0); restores into this attached stack.
        /// </summary>
        public void RestoreFrom(PyStack source)
        {
            // Clear current stack portion
            for (int i = _base; i < _top; i++)
            {
                if (_items[i].ObjRef != null) _items[i].ObjRef = null;
            }
            int sourceCount = source._top - source._base;
            int requiredTotal = _base + sourceCount;
            if (requiredTotal > _items.Length)
                Grow(requiredTotal);
            Array.Copy(source._items, source._base, _items, _base, sourceCount);
            _top = _base + sourceCount;
        }

        #endregion

        #region Debug / Conversion helpers

        /// <summary>
        /// 디버깅용: 스택을 PyObject 배열로 변환 (TOS가 배열의 첫 번째 요소)
        /// </summary>
        public PyObject[] ToArray()
        {
            int stackCount = _top - _base;
            var array = new PyObject[stackCount];
            for (int i = 0; i < stackCount; i++)
            {
                array[stackCount - 1 - i] = _items[_base + i].ToObject();
            }
            return array;
        }

        /// <summary>
        /// 디버깅용: 스택 상단 n개 요소 가져오기
        /// </summary>
        public IEnumerable<PyObject> Take(int count)
        {
            if (count <= 0) return Array.Empty<PyObject>();

            int stackCount = _top - _base;
            int actualCount = Math.Min(count, stackCount);
            var result = new PyObject[actualCount];

            for (int i = 0; i < actualCount; i++)
            {
                result[i] = _items[_top - 1 - i].ToObject();
            }

            return result;
        }

        /// <summary>
        /// 디버깅용: 스택 전체를 역순(TOS부터)으로 열거
        /// </summary>
        public IEnumerable<PyObject> Reverse()
        {
            for (int i = _top - 1; i >= _base; i--)
            {
                yield return _items[i].ToObject();
            }
        }

        /// <summary>
        /// 내부 PyValue 배열에 직접 접근 (읽기 전용 목적)
        /// Returns stack portion only: count = stack element count.
        /// Caller must add GetStackBase() to index into raw array.
        /// </summary>
        public PyValue[] GetInternalValueArray(out int count)
        {
            count = _top - _base;
            return _items;
        }

        /// <summary>
        /// Stack base offset for GetInternalValueArray callers.
        /// </summary>
        public int GetStackBase() => _base;

        /// <summary>
        /// Legacy: 내부 배열을 PyObject[]로 변환하여 반환 (스택 부분만)
        /// </summary>
        public PyObject[] GetInternalArray(out int count)
        {
            int stackCount = _top - _base;
            count = stackCount;
            var result = new PyObject[stackCount];
            for (int i = 0; i < stackCount; i++)
                result[i] = _items[_base + i].ToObject();
            return result;
        }

        /// <summary>
        /// Legacy compatibility for UNPACK_EX.
        /// Returns a temporary List view (스택 부분만).
        /// </summary>
        public List<PyObject> GetInternalList()
        {
            int stackCount = _top - _base;
            var list = new List<PyObject>(stackCount);
            for (int i = 0; i < stackCount; i++)
                list.Add(_items[_base + i].ToObject());
            return list;
        }

        #endregion

        #region IEnumerable

        /// <summary>
        /// bottom-to-top 순서로 열거 (스택 부분만)
        /// </summary>
        public IEnumerator<PyObject> GetEnumerator()
        {
            for (int i = _base; i < _top; i++)
                yield return _items[i].ToObject();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Internal

        private void Grow()
        {
            Grow(_items.Length * 2);
        }

        private void Grow(int minCapacity)
        {
            int newCapacity = Math.Max(_items.Length * 2, minCapacity);
            var newItems = new PyValue[newCapacity];
            Array.Copy(_items, 0, newItems, 0, _top);
            _items = newItems;
            // Sync frame reference — locals and stack share this array
            if (_ownerFrame != null)
                _ownerFrame.LocalsPlus = newItems;
        }

        #endregion
    }
}
