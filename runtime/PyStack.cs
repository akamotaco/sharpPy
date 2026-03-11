using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// PyValue 기반 배열 스택. CPython의 C 배열 스택 포인터 방식을 C#으로 구현.
    /// 내부적으로 PyValue[]를 사용하여 int/float/bool/none을 힙 할당 없이 저장.
    /// 기존 Push(PyObject)/Pop() API는 자동 변환으로 호환성 유지.
    /// </summary>
    public class PyStack : IEnumerable<PyObject>
    {
        private PyValue[] _items;
        private int _top;  // 다음 Push 위치 (= 현재 요소 수)
        private const int DefaultCapacity = 16;

        // ThreadStatic pool to avoid allocating new PyValue[16] per frame.
        // CPython reuses stack space via C call stack; we emulate with explicit pooling.
        [ThreadStatic] private static PyStack[] _pool;
        [ThreadStatic] private static int _poolCount;
        private const int PoolMaxSize = 32;

        public PyStack()
        {
            _items = new PyValue[DefaultCapacity];
            _top = 0;
        }

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
            if (_top == 0)
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
            if (_top == 0)
                throw new InvalidOperationException("Stack is empty");
            return _items[_top - 1].ToObject();
        }

        /// <summary>
        /// 스택 상단에서 depth만큼 떨어진 값 확인 (PyObject 변환)
        /// depth=0: TOS, depth=1: TOS-1, depth=2: TOS-2, ...
        /// </summary>
        public PyObject PeekAt(int depth)
        {
            if (depth < 0 || depth >= _top)
            {
                throw new ArgumentOutOfRangeException(nameof(depth),
                    $"Invalid stack depth {depth} (stack size: {_top})");
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
            if (_top == 0)
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
            if (_top == 0)
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
            if (_top < distance)
                throw new InvalidOperationException(
                    $"SWAP({distance}): Not enough items on stack (need {distance}, got {_top})");

            int topIdx = _top - 1;
            int otherIdx = topIdx - distance + 1;
            (_items[topIdx], _items[otherIdx]) = (_items[otherIdx], _items[topIdx]);
        }

        /// <summary>
        /// 스택의 현재 크기
        /// </summary>
        public int Count => _top;

        /// <summary>
        /// 스택의 모든 요소 제거
        /// </summary>
        public void Clear()
        {
            // Only null ObjRef fields for GC safety (8B per slot vs 24B full clear)
            for (int i = 0; i < _top; i++)
                _items[i].ObjRef = null;
            _top = 0;
        }

        #endregion

        #region Clone / Restore (Generator support)

        /// <summary>
        /// 스택 전체를 복사 (Generator 상태 저장용)
        /// </summary>
        public PyStack Clone()
        {
            var clone = new PyStack();
            if (_top > clone._items.Length)
                clone._items = new PyValue[_top];
            Array.Copy(_items, 0, clone._items, 0, _top);
            clone._top = _top;
            return clone;
        }

        /// <summary>
        /// 다른 스택의 내용을 이 스택으로 복원 (Generator resume용)
        /// </summary>
        public void RestoreFrom(PyStack source)
        {
            Array.Clear(_items, 0, _top);
            _top = 0;
            if (source._top > _items.Length)
                _items = new PyValue[source._top];
            Array.Copy(source._items, 0, _items, 0, source._top);
            _top = source._top;
        }

        #endregion

        #region Debug / Conversion helpers

        /// <summary>
        /// 디버깅용: 스택을 PyObject 배열로 변환 (TOS가 배열의 첫 번째 요소)
        /// </summary>
        public PyObject[] ToArray()
        {
            var array = new PyObject[_top];
            for (int i = 0; i < _top; i++)
            {
                array[_top - 1 - i] = _items[i].ToObject();
            }
            return array;
        }

        /// <summary>
        /// 디버깅용: 스택 상단 n개 요소 가져오기
        /// </summary>
        public IEnumerable<PyObject> Take(int count)
        {
            if (count <= 0) return Array.Empty<PyObject>();

            int actualCount = Math.Min(count, _top);
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
            for (int i = _top - 1; i >= 0; i--)
            {
                yield return _items[i].ToObject();
            }
        }

        /// <summary>
        /// 내부 PyValue 배열에 직접 접근 (읽기 전용 목적)
        /// </summary>
        public PyValue[] GetInternalValueArray(out int count)
        {
            count = _top;
            return _items;
        }

        /// <summary>
        /// Legacy: 내부 배열을 PyObject[]로 변환하여 반환
        /// </summary>
        public PyObject[] GetInternalArray(out int count)
        {
            count = _top;
            var result = new PyObject[_top];
            for (int i = 0; i < _top; i++)
                result[i] = _items[i].ToObject();
            return result;
        }

        /// <summary>
        /// Legacy compatibility for UNPACK_EX.
        /// Returns a temporary List view.
        /// </summary>
        public List<PyObject> GetInternalList()
        {
            var list = new List<PyObject>(_top);
            for (int i = 0; i < _top; i++)
                list.Add(_items[i].ToObject());
            return list;
        }

        #endregion

        #region IEnumerable

        /// <summary>
        /// bottom-to-top 순서로 열거
        /// </summary>
        public IEnumerator<PyObject> GetEnumerator()
        {
            for (int i = 0; i < _top; i++)
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
        }

        #endregion
    }
}
