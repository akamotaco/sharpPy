using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// 배열 기반 스택. CPython의 C 배열 스택 포인터 방식을 C#으로 구현.
    /// List{T} 대신 직접 배열 관리로 Push/Pop/Peek 오버헤드 최소화.
    /// </summary>
    public class PyStack : IEnumerable<PyObject>
    {
        private PyObject[] _items;
        private int _top;  // 다음 Push 위치 (= 현재 요소 수)
        private const int DefaultCapacity = 16;

        public PyStack()
        {
            _items = new PyObject[DefaultCapacity];
            _top = 0;
        }

        /// <summary>
        /// 스택 최상단에 값 추가 (TOS)
        /// </summary>
        public void Push(PyObject obj)
        {
            if (_top == _items.Length)
                Grow();
            _items[_top++] = obj;
        }

        /// <summary>
        /// 여러 요소를 한번에 Push (UNPACK_EX 등)
        /// </summary>
        public void PushRange(PyObject[] elements, int startIndex, int count)
        {
            int required = _top + count;
            if (required > _items.Length)
                Grow(required);
            Array.Copy(elements, startIndex, _items, _top, count);
            _top += count;
        }

        /// <summary>
        /// 스택 최상단 값 제거 및 반환
        /// </summary>
        public PyObject Pop()
        {
            if (_top == 0)
                throw new InvalidOperationException("Stack is empty");
            var item = _items[--_top];
            _items[_top] = null; // GC 참조 해제
            return item;
        }

        /// <summary>
        /// 스택 최상단 값 확인 (제거 없음)
        /// </summary>
        public PyObject Peek()
        {
            if (_top == 0)
                throw new InvalidOperationException("Stack is empty");
            return _items[_top - 1];
        }

        /// <summary>
        /// 스택 상단에서 depth만큼 떨어진 값 확인
        /// depth=0: TOS, depth=1: TOS-1, depth=2: TOS-2, ...
        /// </summary>
        public PyObject PeekAt(int depth)
        {
            if (depth < 0 || depth >= _top)
            {
                throw new ArgumentOutOfRangeException(nameof(depth),
                    $"Invalid stack depth {depth} (stack size: {_top})");
            }
            return _items[_top - 1 - depth];
        }

        /// <summary>
        /// TOS와 TOS-distance+1 위치의 값을 교환
        /// SWAP(1): TOS ↔ TOS (no-op)
        /// SWAP(2): TOS ↔ TOS-1
        /// SWAP(3): TOS ↔ TOS-2
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
            // GC 참조 해제: 사용된 슬롯만 null로 설정
            Array.Clear(_items, 0, _top);
            _top = 0;
        }

        /// <summary>
        /// 디버깅용: 스택을 배열로 변환 (TOS가 배열의 첫 번째 요소)
        /// </summary>
        public PyObject[] ToArray()
        {
            var array = new PyObject[_top];
            for (int i = 0; i < _top; i++)
            {
                array[_top - 1 - i] = _items[i]; // TOS가 [0]이 되도록 역순 복사
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
                result[i] = _items[_top - 1 - i]; // TOS부터
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
                yield return _items[i];
            }
        }

        /// <summary>
        /// 스택 전체를 복사 (O(n), 최소 오버헤드)
        /// Generator 상태 저장 등 성능이 중요한 경우 사용
        /// </summary>
        public PyStack Clone()
        {
            var clone = new PyStack();
            if (_top > clone._items.Length)
                clone._items = new PyObject[_top];
            Array.Copy(_items, 0, clone._items, 0, _top);
            clone._top = _top;
            return clone;
        }

        /// <summary>
        /// 다른 스택의 내용을 이 스택으로 복원 (Generator resume용)
        /// Clear() 후 source의 요소들을 복사하여 Push
        /// </summary>
        public void RestoreFrom(PyStack source)
        {
            Array.Clear(_items, 0, _top);
            _top = 0;
            if (source._top > _items.Length)
                _items = new PyObject[source._top];
            Array.Copy(source._items, 0, _items, 0, source._top);
            _top = source._top;
        }

        /// <summary>
        /// 내부 배열에 직접 접근 (읽기 전용 목적)
        /// count와 함께 사용: items[0..count-1]이 유효한 요소 (bottom-to-top)
        /// </summary>
        public PyObject[] GetInternalArray(out int count)
        {
            count = _top;
            return _items;
        }

        // Legacy compatibility for UNPACK_EX
        // Returns a temporary List view - ONLY for non-hot-path operations
        public List<PyObject> GetInternalList()
        {
            var list = new List<PyObject>(_top);
            for (int i = 0; i < _top; i++)
                list.Add(_items[i]);
            return list;
        }

        /// <summary>
        /// 내부 List를 bottom-to-top 순서로 열거 (IEnumerable 인터페이스)
        /// </summary>
        public IEnumerator<PyObject> GetEnumerator()
        {
            for (int i = 0; i < _top; i++)
                yield return _items[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void Grow()
        {
            Grow(_items.Length * 2);
        }

        private void Grow(int minCapacity)
        {
            int newCapacity = Math.Max(_items.Length * 2, minCapacity);
            var newItems = new PyObject[newCapacity];
            Array.Copy(_items, 0, newItems, 0, _top);
            _items = newItems;
        }
    }
}
