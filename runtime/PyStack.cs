using System;
using System.Collections.Generic;
// Performance: Eliminated LINQ

namespace SharpPy
{
    /// <summary>
    /// List 기반 스택 구조체. CPython의 C 배열 스택 포인터 방식을 C#으로 구현.
    /// Stack<T>와 달리 O(1) 인덱스 접근을 지원하여 SWAP, COPY 등의 연산 최적화.
    /// </summary>
    public class PyStack : IEnumerable<PyObject>
    {
        private readonly List<PyObject> _items;

        public PyStack()
        {
            _items = new List<PyObject>();
        }

        /// <summary>
        /// 스택 최상단에 값 추가 (TOS)
        /// </summary>
        public void Push(PyObject obj)
        {
            _items.Add(obj);
        }

        /// <summary>
        /// 스택 최상단 값 제거 및 반환
        /// </summary>
        public PyObject Pop()
        {
            if (_items.Count == 0)
            {
                throw new InvalidOperationException("Stack is empty");
            }
            var lastIndex = _items.Count - 1;
            var item = _items[lastIndex];
            _items.RemoveAt(lastIndex);
            return item;
        }

        /// <summary>
        /// 스택 최상단 값 확인 (제거 없음)
        /// </summary>
        public PyObject Peek()
        {
            if (_items.Count == 0)
            {
                throw new InvalidOperationException("Stack is empty");
            }
            return _items[^1]; // C# 8.0+ index from end
        }

        /// <summary>
        /// 스택 상단에서 depth만큼 떨어진 값 확인
        /// depth=0: TOS, depth=1: TOS-1, depth=2: TOS-2, ...
        /// </summary>
        public PyObject PeekAt(int depth)
        {
            if (depth < 0 || depth >= _items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(depth),
                    $"Invalid stack depth {depth} (stack size: {_items.Count})");
            }
            return _items[_items.Count - 1 - depth];
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
            {
                throw new ArgumentException($"SWAP distance must be >= 1, got {distance}");
            }
            if (_items.Count < distance)
            {
                throw new InvalidOperationException(
                    $"SWAP({distance}): Not enough items on stack (need {distance}, got {_items.Count})");
            }

            int topIdx = _items.Count - 1;
            int otherIdx = topIdx - distance + 1;

            // C# tuple swap (O(1) 연산)
            (_items[topIdx], _items[otherIdx]) = (_items[otherIdx], _items[topIdx]);
        }

        /// <summary>
        /// 스택의 현재 크기
        /// </summary>
        public int Count => _items.Count;

        /// <summary>
        /// 스택의 모든 요소 제거
        /// </summary>
        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>
        /// 디버깅용: 스택을 배열로 변환 (TOS가 배열의 첫 번째 요소)
        /// </summary>
        public PyObject[] ToArray()
        {
            // Performance: Eliminated LINQ - ToArray() is not LINQ but List<T> method (keep as-is)
            var array = _items.ToArray();
            Array.Reverse(array); // TOS가 [0]이 되도록 역순
            return array;
        }

        /// <summary>
        /// 디버깅용: 스택 상단 n개 요소 가져오기
        /// </summary>
        public IEnumerable<PyObject> Take(int count)
        {
            // Performance: Eliminated LINQ - replaced Enumerable.Empty<T>() with empty array
            if (count <= 0) return new PyObject[0];

            int actualCount = Math.Min(count, _items.Count);
            var result = new PyObject[actualCount];

            for (int i = 0; i < actualCount; i++)
            {
                result[i] = _items[_items.Count - 1 - i]; // TOS부터
            }

            return result;
        }

        /// <summary>
        /// 디버깅용: 스택 전체를 역순(TOS부터)으로 열거
        /// </summary>
        public IEnumerable<PyObject> Reverse()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
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
            clone._items.AddRange(_items); // List.AddRange는 최적화된 bulk copy
            return clone;
        }

        /// <summary>
        /// 내부 List를 bottom-to-top 순서로 열거 (IEnumerable 인터페이스)
        /// 주의: TOS가 마지막에 나옴. 대부분의 경우 Reverse()나 ToArray() 사용 권장
        /// </summary>
        public IEnumerator<PyObject> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
